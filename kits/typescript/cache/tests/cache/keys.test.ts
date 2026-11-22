import { createHash } from 'node:crypto';

import { describe, expect, it } from 'vitest';

import { cacheKey, queryHash } from '../../src/cache/keys.js';

const sha16 = (text: string): string => createHash('sha256').update(text).digest('hex').slice(0, 16);

describe('cacheKey', () => {
  it('builds the contract shape without a query', () => {
    expect(cacheKey('tamagotchi', 'creature', 'c-1')).toBe('tamagotchi:v1:creature:c-1');
    expect(cacheKey('tamagotchi', 'creature', 'c-1', {})).toBe('tamagotchi:v1:creature:c-1');
  });

  it('appends the hash of the normalized query', () => {
    expect(cacheKey('notification', 'history', 'u-1', { page: 1, limit: 20 })).toBe(
      `notification:v1:history:u-1:${sha16('limit=20&page=1')}`,
    );
  });
});

describe('queryHash', () => {
  it('ignores parameter order and undefined values', () => {
    expect(queryHash({ b: '2', a: '1', c: undefined })).toBe(queryHash({ a: '1', b: '2' }));
  });

  it('sorts repeated values and percent-encodes', () => {
    expect(queryHash({ tag: ['z', 'a b'] })).toBe(sha16('tag=a%20b&tag=z'));
  });

  it('returns undefined for an empty query', () => {
    expect(queryHash({ a: undefined })).toBeUndefined();
  });
});
