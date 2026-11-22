import { createHash } from 'node:crypto';

export type QueryParams = Record<string, string | number | boolean | readonly string[] | undefined>;

export function queryHash(params: QueryParams): string | undefined {
  const pairs: [string, string][] = [];

  for (const [name, value] of Object.entries(params)) {
    if (value === undefined) {
      continue;
    }
    const values = Array.isArray(value) ? (value as readonly string[]) : [String(value)];
    for (const item of values) {
      pairs.push([encodeURIComponent(name), encodeURIComponent(item)]);
    }
  }

  if (pairs.length === 0) {
    return undefined;
  }

  pairs.sort(([an, av], [bn, bv]) => (an === bn ? (av < bv ? -1 : av > bv ? 1 : 0) : an < bn ? -1 : 1));
  const normalized = pairs.map(([name, value]) => `${name}=${value}`).join('&');
  return createHash('sha256').update(normalized, 'utf8').digest('hex').slice(0, 16);
}

export function cacheKey(service: string, resource: string, id: string, query?: QueryParams): string {
  const base = `${service}:v1:${resource}:${id}`;
  const suffix = query === undefined ? undefined : queryHash(query);
  return suffix === undefined ? base : `${base}:${suffix}`;
}
