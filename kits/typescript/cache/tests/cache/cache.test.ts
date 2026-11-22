import { Registry } from 'prom-client';
import { describe, expect, it, vi } from 'vitest';

import { createCache, readCacheNodes, type CacheEntry } from '../../src/cache/cache.js';
import { parseNodes, type CacheNode } from '../../src/cache/ring.js';
import type { CacheClient } from '../../src/cache/valkey.js';

vi.mock('../../src/cache/log.js', () => ({
  logger: { debug: vi.fn(), info: vi.fn(), warn: vi.fn(), error: vi.fn() },
}));

type Mode = 'ok' | 'fail' | 'hang';

class FakeClient implements CacheClient {
  readonly data = new Map<string, { value: string; ttl: number | null }>();
  mode: Mode = 'ok';
  closed = false;

  private async apply<T>(work: () => T): Promise<T> {
    if (this.mode === 'fail') {
      throw new Error('connection refused');
    }
    if (this.mode === 'hang') {
      return new Promise<T>(() => undefined);
    }
    return work();
  }

  get(key: string) {
    return this.apply(() => this.data.get(key)?.value ?? null);
  }

  set(key: string, value: string, ttl: number | null) {
    return this.apply(() => {
      this.data.set(key, { value, ttl });
    });
  }

  del(keys: readonly string[]) {
    return this.apply(() => {
      for (const key of keys) {
        this.data.delete(key);
      }
    });
  }

  async quit() {
    this.closed = true;
  }
}

const NODES = parseNodes('cache-0=cache-0:6379,cache-1=cache-1:6379,cache-2=cache-2:6379');

function setup(nodes: readonly CacheNode[] = NODES) {
  const clients = new Map<string, FakeClient>();
  const clock = { now: 1_000_000 };
  const registry = new Registry();
  const cache = createCache({
    service: 'tamagotchi',
    nodes,
    registry,
    timeoutMs: 20,
    now: () => clock.now,
    connect: (node) => {
      const client = new FakeClient();
      clients.set(node.name, client);
      return client;
    },
  });
  const clientFor = (key: string) => clients.get(cache.nodeFor(key) as string) as FakeClient;
  const metric = async (node: string, result: string) => {
    const values = (await registry.getSingleMetric('cache_requests_total')?.get())?.values ?? [];
    return values.find((v) => v.labels.node === node && v.labels.result === result)?.value ?? 0;
  };
  return { cache, clients, clock, clientFor, metric };
}

const entry = (body: unknown, etag: string | null = '"v1"'): CacheEntry<unknown> => ({ body, etag });

describe('read', () => {
  it('misses, stores with the TTL, then hits', async () => {
    const { cache, clientFor, metric } = setup();
    const key = 'tamagotchi:v1:creature:c-1';
    const load = vi.fn(async () => entry({ id: 'c-1' }));

    const first = await cache.read(key, 60, load);
    const second = await cache.read(key, 60, load);

    expect(first).toEqual({ entry: entry({ id: 'c-1' }), cache: 'MISS' });
    expect(second).toEqual({ entry: entry({ id: 'c-1' }), cache: 'HIT' });
    expect(load).toHaveBeenCalledTimes(1);
    const stored = clientFor(key).data.get(key);
    expect(stored?.ttl).toBe(60);
    expect(JSON.parse(stored?.value as string)).toEqual({
      body: { id: 'c-1' },
      etag: '"v1"',
      cached_at: new Date(1_000_000).toISOString(),
    });
    const node = cache.nodeFor(key) as string;
    expect(await metric(node, 'miss')).toBe(1);
    expect(await metric(node, 'hit')).toBe(1);
  });

  it('stores immutable entries without a TTL', async () => {
    const { cache, clientFor } = setup();
    const key = 'registry:v1:stat-definitions:p-1:3';

    await cache.read(key, null, async () => entry([1, 2]));

    expect(clientFor(key).data.get(key)?.ttl).toBeNull();
  });

  it('does not store a missing resource', async () => {
    const { cache, clientFor } = setup();
    const key = 'tamagotchi:v1:creature:none';

    expect(await cache.read(key, 60, async () => null)).toEqual({ entry: null, cache: 'MISS' });
    expect(clientFor(key).data.size).toBe(0);
  });

  it('refuses a TTL above one hour', async () => {
    const { cache } = setup();

    await expect(cache.read('k', 3601, async () => null)).rejects.toThrow(RangeError);
    await expect(cache.read('k', 0, async () => null)).rejects.toThrow(RangeError);
  });

  it('treats an unreadable stored value as a miss and overwrites it', async () => {
    const { cache, clientFor } = setup();
    const key = 'tamagotchi:v1:creature:c-2';
    clientFor(key).data.set(key, { value: 'not json', ttl: 60 });

    const result = await cache.read(key, 60, async () => entry({ id: 'c-2' }));

    expect(result.cache).toBe('MISS');
    expect(JSON.parse(clientFor(key).data.get(key)?.value as string).body).toEqual({ id: 'c-2' });
  });

  it('reads from the database when caching is off', async () => {
    const { cache } = setup([]);

    expect(cache.enabled).toBe(false);
    expect(await cache.read('k', 60, async () => entry(1))).toEqual({ entry: entry(1), cache: 'MISS' });
    expect(cache.nodeFor('k')).toBeUndefined();
  });
});

describe('node down', () => {
  it('answers from the database when a node fails and marks it down for 10 s', async () => {
    const { cache, clientFor, clock, metric } = setup();
    const key = 'tamagotchi:v1:creature:c-3';
    const client = clientFor(key);
    const node = cache.nodeFor(key) as string;
    client.mode = 'fail';
    const load = vi.fn(async () => entry({ id: 'c-3' }));

    expect(await cache.read(key, 60, load)).toEqual({ entry: entry({ id: 'c-3' }), cache: 'MISS' });
    expect(await metric(node, 'error')).toBe(1);

    client.mode = 'ok';
    await cache.read(key, 60, load);
    expect(client.data.size).toBe(0);
    expect(await metric(node, 'miss')).toBe(1);
    expect(cache.nodeFor(key)).toBe(node);

    clock.now += 10_000;
    await cache.read(key, 60, load);
    expect(client.data.has(key)).toBe(true);
    expect(load).toHaveBeenCalledTimes(3);
  });

  it('times out a silent node within the budget', async () => {
    const { cache, clientFor, metric } = setup();
    const key = 'tamagotchi:v1:collection:u-1';
    clientFor(key).mode = 'hang';

    const started = Date.now();
    const result = await cache.read(key, 60, async () => entry([]));

    expect(result.cache).toBe('MISS');
    expect(Date.now() - started).toBeLessThan(500);
    expect(await metric(cache.nodeFor(key) as string, 'error')).toBe(1);
  });

  it('keeps the other nodes serving hits', async () => {
    const { cache, clients } = setup();
    const keys = Array.from({ length: 30 }, (_, i) => `tamagotchi:v1:creature:k-${i}`);
    for (const key of keys) {
      await cache.read(key, 60, async () => entry(key));
    }
    (clients.get('cache-1') as FakeClient).mode = 'fail';

    const results = await Promise.all(keys.map((key) => cache.read(key, 60, async () => entry(key))));

    keys.forEach((key, i) => {
      expect(results[i]?.cache).toBe(cache.nodeFor(key) === 'cache-1' ? 'MISS' : 'HIT');
      expect(results[i]?.entry?.body).toBe(key);
    });
  });
});

describe('invalidate', () => {
  it('deletes keys on their own nodes', async () => {
    const { cache, clientFor } = setup();
    const keys = ['tamagotchi:v1:creature:a', 'tamagotchi:v1:creature:b', 'tamagotchi:v1:collection:u'];
    for (const key of keys) {
      await cache.read(key, 60, async () => entry(key));
    }

    await cache.invalidate(keys);

    for (const key of keys) {
      expect(clientFor(key).data.has(key)).toBe(false);
    }
  });

  it('never throws when a node fails and counts the error', async () => {
    const { cache, clientFor, metric } = setup();
    const key = 'tamagotchi:v1:creature:d';
    clientFor(key).mode = 'fail';

    await expect(cache.invalidate([key])).resolves.toBeUndefined();
    await expect(cache.invalidate([key])).resolves.toBeUndefined();
    expect(await metric(cache.nodeFor(key) as string, 'error')).toBe(2);
  });
});

describe('setup', () => {
  it('reads CACHE_NODES and closes every client', async () => {
    expect(readCacheNodes({ CACHE_NODES: 'cache-0=localhost:6379' })).toEqual([
      { name: 'cache-0', host: 'localhost', port: 6379 },
    ]);
    const { cache, clients } = setup();

    await cache.close();

    expect([...clients.values()].every((client) => client.closed)).toBe(true);
  });

  it('shares one counter between caches on the same registry', () => {
    const registry = new Registry();
    const connect = () => new FakeClient();
    createCache({ service: 'a', nodes: NODES, registry, connect });
    expect(() => createCache({ service: 'b', nodes: NODES, registry, connect })).not.toThrow();
  });
});
