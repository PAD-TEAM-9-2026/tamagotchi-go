import { hostname } from 'node:os';

export const DEFAULT_SERVICE_DISCOVERY_URL = 'http://service-discovery:3009';

export type DiscoveryConfig = {
  url: string;
  host: string;
};

function read(env: NodeJS.ProcessEnv, name: string): string | undefined {
  const value = env[name]?.trim();
  return value === undefined || value === '' ? undefined : value;
}

function readEnabled(env: NodeJS.ProcessEnv): boolean {
  const value = read(env, 'SERVICE_DISCOVERY_ENABLED');

  if (value === undefined) {
    return true;
  }

  if (value !== 'true' && value !== 'false') {
    throw new Error("SERVICE_DISCOVERY_ENABLED must be 'true' or 'false'.");
  }

  return value === 'true';
}

function readUrl(env: NodeJS.ProcessEnv): string {
  const value = read(env, 'SERVICE_DISCOVERY_URL');

  if (value === undefined) {
    return DEFAULT_SERVICE_DISCOVERY_URL;
  }

  let url: URL;

  try {
    url = new URL(value);
  } catch {
    throw new Error('SERVICE_DISCOVERY_URL must be an absolute http or https URL.');
  }

  if (url.protocol !== 'http:' && url.protocol !== 'https:') {
    throw new Error('SERVICE_DISCOVERY_URL must be an absolute http or https URL.');
  }

  return value;
}

export function readDiscoveryConfig(env: NodeJS.ProcessEnv = process.env): DiscoveryConfig | undefined {
  if (!readEnabled(env)) {
    return undefined;
  }

  return { url: readUrl(env), host: read(env, 'INSTANCE_HOST') ?? hostname() };
}
