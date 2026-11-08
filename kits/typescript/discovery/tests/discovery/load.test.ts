import express from 'express';
import request from 'supertest';
import { describe, expect, it } from 'vitest';

import { countLoad, createLoad, LOAD_WINDOW_MS } from '../../src/discovery/load.js';

function clock(): { now: () => number; advance: (ms: number) => void } {
  let current = 1_000_000;
  return {
    now: () => current,
    advance: (ms) => {
      current += ms;
    },
  };
}

describe('load', () => {
  it('starts empty', () => {
    expect(createLoad(clock().now).snapshot()).toEqual({
      in_flight_requests: 0,
      requests_last_minute: 0,
      avg_response_ms_last_minute: 0,
    });
  });

  it('counts requests in flight until they finish', () => {
    const load = createLoad(clock().now);

    load.started();
    load.started();
    expect(load.snapshot().in_flight_requests).toBe(2);

    load.finished(10);
    expect(load.snapshot()).toEqual({
      in_flight_requests: 1,
      requests_last_minute: 1,
      avg_response_ms_last_minute: 10,
    });
  });

  it('averages the last minute to one decimal', () => {
    const load = createLoad(clock().now);

    for (const duration of [10, 20, 15.37]) {
      load.started();
      load.finished(duration);
    }

    expect(load.snapshot().avg_response_ms_last_minute).toBe(15.1);
  });

  it('forgets requests older than one minute', () => {
    const time = clock();
    const load = createLoad(time.now);

    load.started();
    load.finished(100);
    time.advance(30_000);
    load.started();
    load.finished(20);

    expect(load.snapshot()).toMatchObject({ requests_last_minute: 2, avg_response_ms_last_minute: 60 });

    time.advance(LOAD_WINDOW_MS - 30_000);
    expect(load.snapshot()).toMatchObject({ requests_last_minute: 1, avg_response_ms_last_minute: 20 });

    time.advance(30_000);
    expect(load.snapshot()).toMatchObject({ requests_last_minute: 0, avg_response_ms_last_minute: 0 });
  });

  it('never reports a negative number in flight', () => {
    const load = createLoad(clock().now);

    load.finished(5);

    expect(load.snapshot().in_flight_requests).toBe(0);
  });
});

describe('countLoad', () => {
  it('counts every request except the three probes', async () => {
    const load = createLoad();
    const app = express();
    app.use(countLoad(load));
    app.get(['/health', '/ready', '/status', '/v1/things'], (_request, response) => {
      response.json({});
    });

    for (const path of ['/health', '/ready', '/status', '/v1/things', '/v1/things?page=2']) {
      await request(app).get(path);
    }

    expect(load.snapshot()).toMatchObject({ in_flight_requests: 0, requests_last_minute: 2 });
  });

  it('holds a request in flight until its answer is sent', async () => {
    const load = createLoad();
    const app = express();
    let seen: number | undefined;
    app.use(countLoad(load));
    app.get('/v1/slow', (_request, response) => {
      seen = load.snapshot().in_flight_requests;
      response.json({});
    });

    await request(app).get('/v1/slow');

    expect(seen).toBe(1);
    expect(load.snapshot().in_flight_requests).toBe(0);
  });
});
