import { Redis } from 'ioredis';

import { logger } from './log.js';
import type { CacheNode } from './ring.js';

export type CacheClient = {
  get(key: string): Promise<string | null>;
  set(key: string, value: string, ttlSeconds: number | null): Promise<void>;
  del(keys: readonly string[]): Promise<void>;
  quit(): Promise<void>;
};

export function connectValkey(node: CacheNode, timeoutMs: number): CacheClient {
  const client = new Redis({
    host: node.host,
    port: node.port,
    connectTimeout: 1000,
    commandTimeout: timeoutMs,
    enableOfflineQueue: false,
    maxRetriesPerRequest: 0,
    retryStrategy: (attempt: number) => Math.min(attempt * 200, 2000),
  });

  client.on('error', (error: Error) => {
    logger.debug('cache node connection error', { node: node.name, cause: error.message });
  });

  return {
    get: (key) => client.get(key),
    set: async (key, value, ttlSeconds) => {
      if (ttlSeconds === null) {
        await client.set(key, value);
      } else {
        await client.set(key, value, 'EX', ttlSeconds);
      }
    },
    del: async (keys) => {
      if (keys.length > 0) {
        await client.del(...keys);
      }
    },
    quit: async () => {
      await client.quit().catch(() => client.disconnect());
    },
  };
}
