import { Router } from 'express';

import { instance } from './instance.js';
import { load as sharedLoad, type Load } from './load.js';

export type StatusSettings = {
  serviceName: string;
  isDown?: () => Promise<boolean> | boolean;
  isDegraded?: () => Promise<boolean> | boolean;
  load?: Load;
};

async function holds(check: (() => Promise<boolean> | boolean) | undefined): Promise<boolean> {
  if (check === undefined) {
    return false;
  }

  try {
    return (await check()) === true;
  } catch {
    return true;
  }
}

export function createStatusRouter(settings: StatusSettings): Router {
  const router = Router();
  const load = settings.load ?? sharedLoad;

  router.get('/status', async (_request, response) => {
    const down = await holds(settings.isDown);
    const degraded = !down && (await holds(settings.isDegraded));

    response.status(down ? 503 : 200).json({
      service: settings.serviceName,
      instance_id: instance.id,
      status: down ? 'DOWN' : degraded ? 'DEGRADED' : 'UP',
      version: instance.version,
      uptime_seconds: Math.floor((Date.now() - instance.startedAt) / 1000),
      load: load.snapshot(),
      time: new Date().toISOString(),
    });
  });

  return router;
}
