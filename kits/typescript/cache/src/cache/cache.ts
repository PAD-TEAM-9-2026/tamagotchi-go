import { Counter, type Registry } from 'prom-client';

import { logger } from './log.js';
import { buildRing, locate, parseNodes, type CacheNode } from './ring.js';
import { connectValkey, type CacheClient } from './valkey.js';

export const X_CACHE = 'x-cache';
export const MAX_TTL_SECONDS = 3600;
export const COMMAND_TIMEOUT_MS = 50;
export const NODE_DOWN_MS = 10_000;

export type CacheStatus = 'HIT' | 'MISS';
export type CacheEntry<T> = { body: T; etag: string | null };
export type CacheRead<T> = { entry: CacheEntry<T> | null; cache: CacheStatus };
export type CacheResult = 'hit' | 'miss' | 'error';

export type CacheSettings = {
  service: string;
  nodes: readonly CacheNode[];
  registry: Registry;
  timeoutMs?: number;
  downMs?: number;
  now?: () => number;
  connect?: (node: CacheNode, timeoutMs: number) => CacheClient;
};

export type Cache = {
  readonly enabled: boolean;
  read<T>(
    key: string,
    ttlSeconds: number | null,
    load: () => Promise<CacheEntry<T> | null>,
  ): Promise<CacheRead<T>>;
  invalidate(keys: readonly string[]): Promise<void>;
  nodeFor(key: string): string | undefined;
  close(): Promise<void>;
};

type Shard = { node: CacheNode; client: CacheClient; downUntil: number };
type Outcome<T> = { ok: true; value: T } | { ok: false };

export function readCacheNodes(env: NodeJS.ProcessEnv = process.env): CacheNode[] {
  return parseNodes(env.CACHE_NODES);
}

function requestCounter(registry: Registry): Counter<'service' | 'node' | 'result'> {
  const existing = registry.getSingleMetric('cache_requests_total');
  if (existing !== undefined) {
    return existing as Counter<'service' | 'node' | 'result'>;
  }
  return new Counter({
    name: 'cache_requests_total',
    help: 'Cache reads and failed cache commands by node and result.',
    labelNames: ['service', 'node', 'result'],
    registers: [registry],
  });
}

function withTimeout<T>(work: Promise<T>, ms: number): Promise<T> {
  let timer: NodeJS.Timeout | undefined;
  const timeout = new Promise<never>((_, reject) => {
    timer = setTimeout(() => reject(new Error(`cache command exceeded ${ms} ms`)), ms);
    timer.unref();
  });
  return Promise.race([work, timeout]).finally(() => clearTimeout(timer));
}

function checkTtl(ttlSeconds: number | null): void {
  if (ttlSeconds === null) {
    return;
  }
  if (!Number.isInteger(ttlSeconds) || ttlSeconds < 1 || ttlSeconds > MAX_TTL_SECONDS) {
    throw new RangeError(`cache TTL must be null or 1..${MAX_TTL_SECONDS} seconds`);
  }
}

function decode<T>(raw: string): CacheEntry<T> | undefined {
  try {
    const value = JSON.parse(raw) as { body?: T; etag?: unknown };
    if (typeof value !== 'object' || value === null || !('body' in value)) {
      return undefined;
    }
    const etag = typeof value.etag === 'string' ? value.etag : null;
    return { body: value.body as T, etag };
  } catch {
    return undefined;
  }
}

export function createCache(settings: CacheSettings): Cache {
  const timeoutMs = settings.timeoutMs ?? COMMAND_TIMEOUT_MS;
  const downMs = settings.downMs ?? NODE_DOWN_MS;
  const now = settings.now ?? Date.now;
  const connect = settings.connect ?? connectValkey;
  const counter = requestCounter(settings.registry);
  const ring = buildRing(settings.nodes.map((node) => node.name));
  const shards = new Map<string, Shard>(
    settings.nodes.map((node) => [node.name, { node, client: connect(node, timeoutMs), downUntil: 0 }]),
  );

  const count = (shard: Shard, result: CacheResult): void => {
    counter.inc({ service: settings.service, node: shard.node.name, result });
  };

  const available = (shard: Shard): boolean => shard.downUntil <= now();

  const shardFor = (key: string): Shard | undefined =>
    ring.length === 0 ? undefined : shards.get(locate(ring, key));

  const markDown = (shard: Shard, error: unknown): void => {
    const wasUp = available(shard);
    shard.downUntil = now() + downMs;
    count(shard, 'error');
    if (wasUp) {
      logger.warn('cache node marked down', {
        node: shard.node.name,
        down_ms: downMs,
        cause: error instanceof Error ? error.message : String(error),
      });
    }
  };

  const run = async <T>(shard: Shard, command: (client: CacheClient) => Promise<T>): Promise<Outcome<T>> => {
    try {
      return { ok: true, value: await withTimeout(command(shard.client), timeoutMs) };
    } catch (error) {
      markDown(shard, error);
      return { ok: false };
    }
  };

  return {
    enabled: ring.length > 0,

    async read<T>(key: string, ttlSeconds: number | null, load: () => Promise<CacheEntry<T> | null>) {
      checkTtl(ttlSeconds);
      const shard = shardFor(key);

      if (shard === undefined || !available(shard)) {
        if (shard !== undefined) {
          count(shard, 'error');
        }
        return { entry: await load(), cache: 'MISS' as const };
      }

      const cached = await run(shard, (client) => client.get(key));
      if (cached.ok) {
        const entry = cached.value === null ? undefined : decode<T>(cached.value);
        if (entry !== undefined) {
          count(shard, 'hit');
          return { entry, cache: 'HIT' as const };
        }
        count(shard, 'miss');
      }

      const entry = await load();
      if (entry !== null && cached.ok && available(shard)) {
        const value = JSON.stringify({ body: entry.body, etag: entry.etag, cached_at: new Date(now()).toISOString() });
        await run(shard, (client) => client.set(key, value, ttlSeconds));
      }

      return { entry, cache: 'MISS' as const };
    },

    async invalidate(keys: readonly string[]) {
      const groups = new Map<Shard, string[]>();
      for (const key of keys) {
        const shard = shardFor(key);
        if (shard !== undefined) {
          groups.set(shard, [...(groups.get(shard) ?? []), key]);
        }
      }

      await Promise.all(
        [...groups].map(async ([shard, group]) => {
          if (!available(shard)) {
            count(shard, 'error');
            return;
          }
          await run(shard, (client) => client.del(group));
        }),
      );
    },

    nodeFor(key: string) {
      return shardFor(key)?.node.name;
    },

    async close() {
      await Promise.all([...shards.values()].map((shard) => shard.client.quit()));
    },
  };
}
