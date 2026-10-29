import type { RequestHandler } from 'express';

export const LOAD_WINDOW_MS = 60_000;
export const PROBE_PATHS = new Set(['/health', '/ready', '/status']);

export type LoadSnapshot = {
  in_flight_requests: number;
  requests_last_minute: number;
  avg_response_ms_last_minute: number;
};

export type Load = {
  started(): void;
  finished(durationMs: number): void;
  snapshot(): LoadSnapshot;
};

export function createLoad(now: () => number = () => performance.now()): Load {
  let inFlight = 0;
  const finished: { at: number; durationMs: number }[] = [];

  function prune(): void {
    const oldest = now() - LOAD_WINDOW_MS;

    while (finished.length > 0 && finished[0]!.at <= oldest) {
      finished.shift();
    }
  }

  return {
    started() {
      inFlight += 1;
    },
    finished(durationMs) {
      inFlight = Math.max(0, inFlight - 1);
      finished.push({ at: now(), durationMs });
      prune();
    },
    snapshot() {
      prune();
      const total = finished.reduce((sum, { durationMs }) => sum + durationMs, 0);

      return {
        in_flight_requests: inFlight,
        requests_last_minute: finished.length,
        avg_response_ms_last_minute:
          finished.length === 0 ? 0 : Math.round((total / finished.length) * 10) / 10,
      };
    },
  };
}

export const load = createLoad();

export function countLoad(target: Load = load): RequestHandler {
  return (request, response, next) => {
    const path = request.originalUrl.split('?')[0] ?? request.originalUrl;

    if (PROBE_PATHS.has(path)) {
      next();
      return;
    }

    const startedAt = performance.now();
    target.started();
    response.once('close', () => target.finished(performance.now() - startedAt));
    next();
  };
}
