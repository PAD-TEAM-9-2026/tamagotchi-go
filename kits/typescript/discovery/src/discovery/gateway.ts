import { SlidingWindowBreaker } from './breaker.js';
import type { BreakerSettings } from './breaker.js';
import { logger } from './log.js';

export type GatewayConfig = {
  url: string;
  timeoutMs: number;
  breaker: BreakerSettings;
};

export type GatewayClient = {
  request(method: string, path: string, body?: unknown): Promise<Response>;
};

export class BreakerOpenError extends Error {
  constructor() {
    super('circuit breaker is open');
    this.name = 'BreakerOpenError';
  }
}

const GATEWAY_URL_DEFAULT = 'http://gateway:3000';
const TASK_TIMEOUT_MS_DEFAULT = 5000;
const BREAKER_THRESHOLD_DEFAULT = 3;
const BREAKER_WINDOW_MULTIPLIER_DEFAULT = 3.5;
const BREAKER_RESET_SECONDS_DEFAULT = 30;

export function readGatewayConfig(
  env: Record<string, string | undefined> = process.env,
): GatewayConfig {
  const timeoutMs = env['TASK_TIMEOUT_MS']
    ? Number(env['TASK_TIMEOUT_MS'])
    : TASK_TIMEOUT_MS_DEFAULT;
  const threshold = env['BREAKER_THRESHOLD']
    ? Number(env['BREAKER_THRESHOLD'])
    : BREAKER_THRESHOLD_DEFAULT;
  const multiplier = env['BREAKER_WINDOW_MULTIPLIER']
    ? Number(env['BREAKER_WINDOW_MULTIPLIER'])
    : BREAKER_WINDOW_MULTIPLIER_DEFAULT;
  const resetSeconds = env['BREAKER_RESET_SECONDS']
    ? Number(env['BREAKER_RESET_SECONDS'])
    : BREAKER_RESET_SECONDS_DEFAULT;

  return {
    url: env['GATEWAY_URL'] ?? GATEWAY_URL_DEFAULT,
    timeoutMs,
    breaker: {
      threshold,
      windowMs: timeoutMs * multiplier,
      resetMs: resetSeconds * 1000,
    },
  };
}

export function createGatewayClient(
  config: GatewayConfig,
  send: typeof fetch = fetch,
  now: () => number = Date.now,
): GatewayClient {
  const base = config.url.replace(/\/+$/, '');

  const breaker = new SlidingWindowBreaker(
    config.breaker,
    {
      onTrip(count, reason) {
        logger.error('circuit breaker tripped', {
          target: base,
          failures_in_window: count,
          threshold: config.breaker.threshold,
          window_seconds: config.breaker.windowMs / 1000,
          last_reason: reason,
        });
      },
      onClose() {
        logger.info('circuit breaker closed', { target: base });
      },
    },
    now,
  );

  return {
    async request(method: string, path: string, body?: unknown): Promise<Response> {
      if (breaker.isOpen()) {
        throw new BreakerOpenError();
      }

      const url = `${base}${path}`;
      const init: RequestInit = {
        method,
        signal: AbortSignal.timeout(config.timeoutMs),
        ...(body !== undefined
          ? { headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) }
          : {}),
      };

      try {
        const response = await send(url, init);
        breaker.recordSuccess();
        return response;
      } catch (error) {
        // Only timeout and connection errors trip the breaker; 5xx responses do not.
        if (
          error instanceof DOMException ||
          (error instanceof TypeError && !(error instanceof RangeError))
        ) {
          breaker.recordFailure(error.name);
        }
        throw error;
      }
    },
  };
}
