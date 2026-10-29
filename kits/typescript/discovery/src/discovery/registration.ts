import { instance } from './instance.js';
import { logger } from './log.js';

export const REQUEST_TIMEOUT_MS = 2000;
export const CHECK_INTERVAL_MS = 30_000;
export const FIRST_RETRY_MS = 1000;
export const MAX_RETRY_MS = 30_000;

export type DiscoverySettings = {
  serviceName: string;
  url: string;
  host: string;
  port: number;
  checkIntervalMs?: number;
};

export type Registration = {
  start(): void;
  stop(): Promise<void>;
  registered(): boolean;
};

type Fetch = typeof fetch;

function causeOf(error: unknown): string {
  return error instanceof Error ? error.name : 'unknown';
}

export function retryDelay(failures: number): number {
  return Math.min(FIRST_RETRY_MS * 2 ** Math.max(0, failures - 1), MAX_RETRY_MS);
}

export function createRegistration(settings: DiscoverySettings, send: Fetch = fetch): Registration {
  const base = settings.url.replace(/\/+$/, '');
  const self = `${base}/v1/instances/${instance.id}`;
  const fields = { instance_id: instance.id, host: settings.host, port: settings.port };
  const checkIntervalMs = settings.checkIntervalMs ?? CHECK_INTERVAL_MS;
  let stopped = false;
  let isRegistered = false;
  let failures = 0;
  let lost = false;
  let timer: NodeJS.Timeout | undefined;
  let current: Promise<void> = Promise.resolve();

  function call(url: string, init: RequestInit = {}): Promise<Response> {
    return send(url, { ...init, signal: AbortSignal.timeout(REQUEST_TIMEOUT_MS) });
  }

  function later(step: () => Promise<void>, ms: number): void {
    if (stopped) {
      return;
    }

    timer = setTimeout(() => {
      current = step();
    }, ms);
    timer.unref?.();
  }

  async function register(): Promise<void> {
    let status: number | undefined;

    try {
      const response = await call(`${base}/v1/instances`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          instance_id: instance.id,
          service_name: settings.serviceName,
          host: settings.host,
          port: settings.port,
          status_path: '/status',
          version: instance.version,
        }),
      });
      status = response.status;

      if (status === 200 || status === 201) {
        isRegistered = true;
        failures = 0;
        const logFields = { ...fields, status: status === 201 ? 'created' : 'refreshed' };
        if (lost) {
          logger.debug('service re-registered', logFields);
        } else {
          logger.info('service registered', logFields);
        }
        lost = false;
        later(check, checkIntervalMs);
        return;
      }

      failures += 1;
      logger.warn('registration failed', {
        ...fields,
        status,
        retry_in_ms: retryDelay(failures),
      });
    } catch (error) {
      failures += 1;
      logger.warn('registration failed', {
        ...fields,
        cause: causeOf(error),
        retry_in_ms: retryDelay(failures),
      });
    }

    later(register, retryDelay(failures));
  }

  async function check(): Promise<void> {
    try {
      const response = await call(self);

      if (response.status === 404) {
        isRegistered = false;
        lost = true;
        await register();
        return;
      }

      if (response.status === 200) {
        logger.debug('registration check passed', fields);
      } else {
        logger.warn('registration check failed', { ...fields, status: response.status });
      }
    } catch (error) {
      logger.warn('registration check failed', { ...fields, cause: causeOf(error) });
    }

    later(check, checkIntervalMs);
  }

  return {
    start() {
      stopped = false;
      current = register();
    },

    async stop() {
      stopped = true;
      clearTimeout(timer);
      await current;

      if (!isRegistered) {
        return;
      }

      isRegistered = false;

      try {
        const response = await call(self, { method: 'DELETE' });

        if (response.status === 204 || response.status === 404) {
          logger.info('service deregistered', fields);
          return;
        }

        logger.warn('deregistration failed', {
          ...fields,
          status: response.status,
        });
      } catch (error) {
        logger.warn('deregistration failed', { ...fields, cause: causeOf(error) });
      }
    },

    registered() {
      return isRegistered;
    },
  };
}
