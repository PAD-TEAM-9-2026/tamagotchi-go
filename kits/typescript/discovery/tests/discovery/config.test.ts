import { hostname } from 'node:os';

import { describe, expect, it } from 'vitest';

import { DEFAULT_SERVICE_DISCOVERY_URL, readDiscoveryConfig } from '../../src/discovery/config.js';

describe('discovery configuration', () => {
  it('is on by default, with the Compose address and the container hostname', () => {
    expect(readDiscoveryConfig({})).toEqual({ url: DEFAULT_SERVICE_DISCOVERY_URL, host: hostname() });
    expect(DEFAULT_SERVICE_DISCOVERY_URL).toBe('http://service-discovery:3009');
  });

  it('treats blank values as unset', () => {
    expect(
      readDiscoveryConfig({ SERVICE_DISCOVERY_ENABLED: ' ', SERVICE_DISCOVERY_URL: '', INSTANCE_HOST: '' }),
    ).toEqual({ url: DEFAULT_SERVICE_DISCOVERY_URL, host: hostname() });
  });

  it('stays off when SERVICE_DISCOVERY_ENABLED is false', () => {
    expect(readDiscoveryConfig({ SERVICE_DISCOVERY_ENABLED: 'false' })).toBeUndefined();
  });

  it('takes the URL and host from the environment', () => {
    expect(
      readDiscoveryConfig({
        SERVICE_DISCOVERY_ENABLED: 'true',
        SERVICE_DISCOVERY_URL: 'http://localhost:3009',
        INSTANCE_HOST: 'example-2',
      }),
    ).toEqual({ url: 'http://localhost:3009', host: 'example-2' });
  });

  it.each(['yes', '1', 'TRUE'])('refuses SERVICE_DISCOVERY_ENABLED=%s', (value) => {
    expect(() => readDiscoveryConfig({ SERVICE_DISCOVERY_ENABLED: value })).toThrow(
      "SERVICE_DISCOVERY_ENABLED must be 'true' or 'false'.",
    );
  });

  it.each(['service-discovery:3009', 'ftp://service-discovery'])('refuses the URL %s', (url) => {
    expect(() => readDiscoveryConfig({ SERVICE_DISCOVERY_URL: url })).toThrow(
      'SERVICE_DISCOVERY_URL must be an absolute http or https URL.',
    );
  });
});
