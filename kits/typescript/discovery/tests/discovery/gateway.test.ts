import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import {
  BreakerOpenError,
  createGatewayClient,
  readGatewayConfig,
} from '../../src/discovery/gateway.js';
import type { GatewayConfig } from '../../src/discovery/gateway.js';

vi.hoisted(() => {
  process.env.LOG_LEVEL = 'debug';
});

const BASE_CONFIG: GatewayConfig = {
  url: 'http://gateway:3000',
  timeoutMs: 5000,
  breaker: { threshold: 3, windowMs: 17_500, resetMs: 30_000 },
};

type Call = { method: string; url: string; body?: unknown };
type Answer = number | Error;

function fakeFetch(answers: (call: Call) => Answer) {
  const calls: Call[] = [];
  const send = vi.fn(async (input: string | URL | Request, init?: RequestInit) => {
    const call: Call = {
      method: init?.method ?? 'GET',
      url: String(input),
      ...(typeof init?.body === 'string' ? { body: JSON.parse(init.body) } : {}),
    };
    calls.push(call);
    const answer = answers(call);
    if (answer instanceof Error) throw answer;
    return new Response(answer === 204 ? null : JSON.stringify({ ok: true }), { status: answer });
  });
  return { send: send as unknown as typeof fetch, calls };
}

function capture() {
  const written: string[] = [];
  const write = (chunk: unknown) => {
    written.push(String(chunk));
    return true;
  };
  vi.spyOn(process.stdout, 'write').mockImplementation(write);
  vi.spyOn(process.stderr, 'write').mockImplementation(write);
  return () =>
    written
      .join('')
      .split('\n')
      .filter((line) => line.startsWith('{'))
      .map((line) => JSON.parse(line) as Record<string, unknown>);
}

let nowMs = 0;
const clock = () => nowMs;

beforeEach(() => {
  vi.useFakeTimers();
  nowMs = 0;
});

afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
});

describe('readGatewayConfig', () => {
  it('returns defaults when no env vars are set', () => {
    const config = readGatewayConfig({});
    expect(config).toEqual({
      url: 'http://gateway:3000',
      timeoutMs: 5000,
      breaker: { threshold: 3, windowMs: 17_500, resetMs: 30_000 },
    });
  });

  it('reads all values from env', () => {
    const config = readGatewayConfig({
      GATEWAY_URL: 'http://gw:8080',
      TASK_TIMEOUT_MS: '2000',
      BREAKER_THRESHOLD: '5',
      BREAKER_WINDOW_MULTIPLIER: '4',
      BREAKER_RESET_SECONDS: '60',
    });
    expect(config).toEqual({
      url: 'http://gw:8080',
      timeoutMs: 2000,
      breaker: { threshold: 5, windowMs: 8000, resetMs: 60_000 },
    });
  });
});

describe('createGatewayClient', () => {
  it('forwards method, path, and body to fetch', async () => {
    const { send, calls } = fakeFetch(() => 200);
    const client = createGatewayClient(BASE_CONFIG, send, clock);

    await client.request('POST', '/v1/items', { name: 'egg' });

    expect(calls).toEqual([
      { method: 'POST', url: 'http://gateway:3000/v1/items', body: { name: 'egg' } },
    ]);
  });

  it('returns the response for a successful call', async () => {
    const { send } = fakeFetch(() => 201);
    const client = createGatewayClient(BASE_CONFIG, send, clock);

    const response = await client.request('POST', '/v1/items');
    expect(response.status).toBe(201);
  });

  it('does not trip the breaker on 5xx responses', async () => {
    const { send } = fakeFetch(() => 500);
    const client = createGatewayClient(BASE_CONFIG, send, clock);

    for (let i = 0; i < 4; i++) {
      const response = await client.request('GET', '/v1/items');
      expect(response.status).toBe(500);
    }
  });

  it('trips the breaker after threshold TypeError failures', async () => {
    const logs = capture();
    const { send } = fakeFetch(() => new TypeError('fetch failed'));
    const client = createGatewayClient(BASE_CONFIG, send, clock);

    for (let i = 0; i < 3; i++) {
      await client.request('GET', '/').catch(() => null);
    }

    await expect(client.request('GET', '/')).rejects.toBeInstanceOf(BreakerOpenError);

    const [tripped] = logs().filter((line) => line.message === 'circuit breaker tripped');
    expect(tripped).toMatchObject({
      level: 'error',
      target: 'http://gateway:3000',
      failures_in_window: 3,
      threshold: 3,
      window_seconds: 17.5,
    });
  });

  it('trips the breaker after threshold DOMException (timeout) failures', async () => {
    const { send } = fakeFetch(() => new DOMException('timeout', 'TimeoutError'));
    const client = createGatewayClient(BASE_CONFIG, send, clock);

    for (let i = 0; i < 3; i++) {
      await client.request('GET', '/').catch(() => null);
    }

    await expect(client.request('GET', '/')).rejects.toBeInstanceOf(BreakerOpenError);
  });

  it('throws BreakerOpenError immediately when open without calling fetch', async () => {
    const { send, calls } = fakeFetch(() => new TypeError('fetch failed'));
    const client = createGatewayClient(BASE_CONFIG, send, clock);

    for (let i = 0; i < 3; i++) {
      await client.request('GET', '/').catch(() => null);
    }

    const callCountBeforeOpen = calls.length;
    await client.request('GET', '/').catch(() => null);

    expect(calls.length).toBe(callCountBeforeOpen); // no new fetch call
  });

  it('closes the breaker after a successful trial and logs it', async () => {
    const logs = capture();
    let callCount = 0;
    const { send } = fakeFetch(() => {
      callCount += 1;
      return callCount <= 3 ? new TypeError('fetch failed') : 200;
    });
    const client = createGatewayClient(BASE_CONFIG, send, clock);

    for (let i = 0; i < 3; i++) {
      await client.request('GET', '/').catch(() => null);
    }

    // advance past resetMs to enter half-open
    nowMs += BASE_CONFIG.breaker.resetMs;

    const response = await client.request('GET', '/');
    expect(response.status).toBe(200);

    // should be fully closed now — next call goes through normally
    const response2 = await client.request('GET', '/');
    expect(response2.status).toBe(200);

    const [closed] = logs().filter((line) => line.message === 'circuit breaker closed');
    expect(closed).toMatchObject({ level: 'info', target: 'http://gateway:3000' });
  });

  it('returns to open when the half-open trial fails', async () => {
    const { send } = fakeFetch(() => new TypeError('fetch failed'));
    const client = createGatewayClient(BASE_CONFIG, send, clock);

    for (let i = 0; i < 3; i++) {
      await client.request('GET', '/').catch(() => null);
    }

    nowMs += BASE_CONFIG.breaker.resetMs;
    await client.request('GET', '/').catch(() => null); // trial fails

    await expect(client.request('GET', '/')).rejects.toBeInstanceOf(BreakerOpenError);
  });

  it('does not trip the breaker on mixed successes and failures below threshold', async () => {
    let callCount = 0;
    const { send } = fakeFetch(() => {
      callCount += 1;
      return callCount % 2 === 0 ? new TypeError('fetch failed') : 200;
    });
    const client = createGatewayClient(BASE_CONFIG, send, clock);

    for (let i = 0; i < 6; i++) {
      await client.request('GET', '/').catch(() => null);
    }

    const response = await client.request('GET', '/');
    expect(response.status).toBe(200);
  });
});
