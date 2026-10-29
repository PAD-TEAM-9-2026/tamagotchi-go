import express from 'express';
import request from 'supertest';
import { describe, expect, it } from 'vitest';

import { instance } from '../../src/discovery/instance.js';
import { createLoad } from '../../src/discovery/load.js';
import { createStatusRouter, type StatusSettings } from '../../src/discovery/status.js';

function appWith(settings: Partial<StatusSettings> = {}) {
  const app = express();
  app.use(createStatusRouter({ serviceName: 'example', load: createLoad(), ...settings }));
  return app;
}

describe('createStatusRouter', () => {
  it('answers UP with the status contract shape when no checks are given', async () => {
    const response = await request(appWith()).get('/status');

    expect(response.status).toBe(200);
    expect(response.body).toEqual({
      service: 'example',
      instance_id: instance.id,
      status: 'UP',
      version: instance.version,
      uptime_seconds: expect.any(Number),
      load: { in_flight_requests: 0, requests_last_minute: 0, avg_response_ms_last_minute: 0 },
      time: expect.stringMatching(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$/),
    });
  });

  it('is DOWN with 503 when isDown holds', async () => {
    const response = await request(appWith({ isDown: async () => true })).get('/status');

    expect(response.status).toBe(503);
    expect(response.body.status).toBe('DOWN');
  });

  it('is DEGRADED with 200 when only isDegraded holds', async () => {
    const response = await request(
      appWith({ isDown: () => false, isDegraded: () => true }),
    ).get('/status');

    expect(response.status).toBe(200);
    expect(response.body.status).toBe('DEGRADED');
  });

  it('prefers DOWN over DEGRADED', async () => {
    const response = await request(appWith({ isDown: () => true, isDegraded: () => true })).get(
      '/status',
    );

    expect(response.body.status).toBe('DOWN');
  });

  it('treats a check that throws as holding', async () => {
    const response = await request(
      appWith({
        isDegraded: () => {
          throw new Error('broker gone');
        },
      }),
    ).get('/status');

    expect(response.body.status).toBe('DEGRADED');
  });

  it('reports the load it is given', async () => {
    const load = createLoad();
    load.started();

    const response = await request(appWith({ load })).get('/status');

    expect(response.body.load.in_flight_requests).toBe(1);
  });
});
