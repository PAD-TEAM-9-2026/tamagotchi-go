import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import {
  CHECK_INTERVAL_MS,
  createRegistration,
  MAX_RETRY_MS,
  retryDelay,
  type DiscoverySettings,
} from '../../src/discovery/registration.js';
import { instance } from '../../src/discovery/instance.js';

vi.hoisted(() => {
  process.env.LOG_LEVEL = 'info';
});

const SETTINGS: DiscoverySettings = {
  serviceName: 'example',
  url: 'http://service-discovery:3009/',
  host: 'example-1',
  port: 3000,
  checkIntervalMs: 15_000,
};
const SELF = `http://service-discovery:3009/v1/instances/${instance.id}`;

type Call = { method: string; url: string; body?: Record<string, unknown> };
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
    if (answer instanceof Error) {
      throw answer;
    }
    return new Response(answer === 204 ? null : '{}', { status: answer });
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

const methods = (calls: Call[]) => calls.map(({ method }) => method);

beforeEach(() => {
  vi.useFakeTimers();
});

afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
});

describe('retry delay', () => {
  it('doubles from one second up to thirty', () => {
    expect([1, 2, 3, 4, 5, 6, 10].map(retryDelay)).toEqual([
      1000, 2000, 4000, 8000, 16_000, MAX_RETRY_MS, MAX_RETRY_MS,
    ]);
  });
});

describe('registration', () => {
  it('registers itself on start with the status contract details', async () => {
    const logs = capture();
    const { send, calls } = fakeFetch(() => 201);
    const registration = createRegistration(SETTINGS, send);

    registration.start();
    await vi.advanceTimersByTimeAsync(0);

    expect(calls).toEqual([
      {
        method: 'POST',
        url: 'http://service-discovery:3009/v1/instances',
        body: {
          instance_id: instance.id,
          service_name: 'example',
          host: 'example-1',
          port: 3000,
          status_path: '/status',
          version: instance.version,
        },
      },
    ]);
    expect(registration.registered()).toBe(true);
    expect(logs().find((line) => line.message === 'service registered')).toMatchObject({
      level: 'info',
      instance_id: instance.id,
      status: 'created',
    });
    await registration.stop();
  });

  it('retries with backoff until service discovery answers', async () => {
    const logs = capture();
    let attempts = 0;
    const { send, calls } = fakeFetch(({ method }) => {
      if (method !== 'POST') return 200;
      attempts += 1;
      return attempts === 1 ? new TypeError('fetch failed') : attempts === 2 ? 503 : 201;
    });
    const registration = createRegistration(SETTINGS, send);

    registration.start();
    await vi.advanceTimersByTimeAsync(0);
    expect(registration.registered()).toBe(false);

    await vi.advanceTimersByTimeAsync(999);
    expect(calls).toHaveLength(1);
    await vi.advanceTimersByTimeAsync(1);
    expect(calls).toHaveLength(2);

    await vi.advanceTimersByTimeAsync(2000);
    expect(methods(calls)).toEqual(['POST', 'POST', 'POST']);
    expect(registration.registered()).toBe(true);

    const failures = logs().filter((line) => line.message === 'registration failed');
    expect(failures.map((line) => [line.level, line.cause, line.status, line.retry_in_ms])).toEqual([
      ['warn', 'TypeError', undefined, 1000],
      ['warn', undefined, 503, 2000],
    ]);
    await registration.stop();
  });

  it('checks itself on every interval while registered', async () => {
    const { send, calls } = fakeFetch(({ method }) => (method === 'POST' ? 201 : 200));
    const registration = createRegistration(SETTINGS, send);

    registration.start();
    await vi.advanceTimersByTimeAsync(15_000 * 3);

    expect(methods(calls)).toEqual(['POST', 'GET', 'GET', 'GET']);
    expect(calls[1]?.url).toBe(SELF);
    await registration.stop();
  });

  it('registers again when service discovery has lost it', async () => {
    const logs = capture();
    let lost = true;
    const { send, calls } = fakeFetch(({ method }) => {
      if (method === 'POST') return calls.length === 1 ? 201 : 200;
      if (lost) {
        lost = false;
        return 404;
      }
      return 200;
    });
    const registration = createRegistration(SETTINGS, send);

    registration.start();
    await vi.advanceTimersByTimeAsync(15_000 * 2);

    expect(methods(calls)).toEqual(['POST', 'GET', 'POST', 'GET']);
    expect(registration.registered()).toBe(true);
    expect(
      logs()
        .filter((line) => line.message === 'service registered' || line.message === 'service re-registered')
        .map((line) => [line.message, line.status]),
    ).toEqual([
      ['service registered', 'created'],
      ['service re-registered', 'refreshed'],
    ]);
    await registration.stop();
  });

  it('checks every 30 seconds by default', async () => {
    const { send, calls } = fakeFetch(({ method }) => (method === 'POST' ? 201 : 200));
    const registration = createRegistration({ ...SETTINGS, checkIntervalMs: undefined }, send);

    registration.start();
    await vi.advanceTimersByTimeAsync(CHECK_INTERVAL_MS - 1);
    expect(methods(calls)).toEqual(['POST']);
    await vi.advanceTimersByTimeAsync(1);
    expect(methods(calls)).toEqual(['POST', 'GET']);
    await registration.stop();
  });

  it('keeps checking after a failed check', async () => {
    const logs = capture();
    let checks = 0;
    const { send, calls } = fakeFetch(({ method }) => {
      if (method === 'POST') return 201;
      checks += 1;
      return checks === 1 ? 500 : checks === 2 ? new TypeError('fetch failed') : 200;
    });
    const registration = createRegistration(SETTINGS, send);

    registration.start();
    await vi.advanceTimersByTimeAsync(15_000 * 3);

    expect(methods(calls)).toEqual(['POST', 'GET', 'GET', 'GET']);
    expect(registration.registered()).toBe(true);
    expect(
      logs()
        .filter((line) => line.message === 'registration check failed')
        .map((line) => [line.status, line.cause]),
    ).toEqual([
      [500, undefined],
      [undefined, 'TypeError'],
    ]);
    await registration.stop();
  });

  it.each([204, 404])('deregisters on stop when the answer is %i', async (answer) => {
    const logs = capture();
    const { send, calls } = fakeFetch(({ method }) => (method === 'DELETE' ? answer : 201));
    const registration = createRegistration(SETTINGS, send);

    registration.start();
    await vi.advanceTimersByTimeAsync(0);
    await registration.stop();

    expect(calls.at(-1)).toEqual({ method: 'DELETE', url: SELF });
    expect(registration.registered()).toBe(false);
    expect(logs().some((line) => line.message === 'service deregistered')).toBe(true);
  });

  it.each([500, new TypeError('fetch failed')])('logs a failed deregistration (%s)', async (answer) => {
    const logs = capture();
    const { send } = fakeFetch(({ method }) => (method === 'DELETE' ? answer : 201));
    const registration = createRegistration(SETTINGS, send);

    registration.start();
    await vi.advanceTimersByTimeAsync(0);
    await registration.stop();

    const [failed] = logs().filter((line) => line.message === 'deregistration failed');
    expect(failed?.level).toBe('warn');
  });

  it('stops retrying and sends nothing when stopped before it registered', async () => {
    capture();
    const { send, calls } = fakeFetch(() => new TypeError('fetch failed'));
    const registration = createRegistration(SETTINGS, send);

    registration.start();
    await vi.advanceTimersByTimeAsync(0);
    await registration.stop();
    await vi.advanceTimersByTimeAsync(MAX_RETRY_MS * 2);

    expect(methods(calls)).toEqual(['POST']);
  });

  it('stops checking after it stopped', async () => {
    capture();
    const { send, calls } = fakeFetch(({ method }) => (method === 'DELETE' ? 204 : method === 'POST' ? 201 : 200));
    const registration = createRegistration(SETTINGS, send);

    registration.start();
    await vi.advanceTimersByTimeAsync(0);
    await registration.stop();
    await vi.advanceTimersByTimeAsync(15_000 * 3);

    expect(methods(calls)).toEqual(['POST', 'DELETE']);
  });
});
