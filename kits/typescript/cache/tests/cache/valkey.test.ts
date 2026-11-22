import { beforeEach, describe, expect, it, vi } from 'vitest';

const instances: FakeRedis[] = [];

class FakeRedis {
  readonly handlers = new Map<string, (error: Error) => void>();
  readonly get = vi.fn(async () => 'value');
  readonly set = vi.fn(async () => 'OK');
  readonly del = vi.fn(async () => 1);
  readonly quit = vi.fn(async () => 'OK');
  readonly disconnect = vi.fn();

  constructor(readonly options: Record<string, unknown>) {
    instances.push(this);
  }

  on(event: string, handler: (error: Error) => void) {
    this.handlers.set(event, handler);
    return this;
  }
}

vi.mock('ioredis', () => ({ Redis: FakeRedis }));
vi.mock('../../src/cache/log.js', () => ({
  logger: { debug: vi.fn(), info: vi.fn(), warn: vi.fn(), error: vi.fn() },
}));

const { connectValkey } = await import('../../src/cache/valkey.js');

const node = { name: 'cache-0', host: 'cache-0', port: 6379 };

describe('connectValkey', () => {
  beforeEach(() => {
    instances.length = 0;
  });

  it('fails fast instead of queueing while a node is unreachable', () => {
    connectValkey(node, 50);

    expect(instances[0]?.options).toMatchObject({
      host: 'cache-0',
      port: 6379,
      commandTimeout: 50,
      enableOfflineQueue: false,
      maxRetriesPerRequest: 0,
    });
    const retry = instances[0]?.options.retryStrategy as (attempt: number) => number;
    expect(retry(1)).toBe(200);
    expect(retry(50)).toBe(2000);
    expect(() => instances[0]?.handlers.get('error')?.(new Error('refused'))).not.toThrow();
  });

  it('maps get, set with and without TTL, and del', async () => {
    const client = connectValkey(node, 50);
    const redis = instances[0] as FakeRedis;

    expect(await client.get('k')).toBe('value');
    await client.set('k', 'v', 60);
    await client.set('k', 'v', null);
    await client.del(['a', 'b']);
    await client.del([]);

    expect(redis.set).toHaveBeenNthCalledWith(1, 'k', 'v', 'EX', 60);
    expect(redis.set).toHaveBeenNthCalledWith(2, 'k', 'v');
    expect(redis.del).toHaveBeenCalledTimes(1);
    expect(redis.del).toHaveBeenCalledWith('a', 'b');
  });

  it('disconnects when quit fails', async () => {
    const client = connectValkey(node, 50);
    const redis = instances[0] as FakeRedis;
    redis.quit.mockRejectedValueOnce(new Error('closed'));

    await client.quit();

    expect(redis.disconnect).toHaveBeenCalled();
  });
});
