import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

import { describe, expect, it } from 'vitest';

import { buildRing, hash64, locate, owner, parseNodes } from '../../src/cache/ring.js';

type Vectors = {
  virtual_nodes: number;
  keys: { key: string; hash: string }[];
  cases: {
    name: string;
    nodes: string[];
    first_points: { point: string; node: string }[];
    last_point: { point: string; node: string };
    expected: Record<string, string>;
  }[];
};

const path = process.env.CACHE_RING_VECTORS ?? fileURLToPath(new URL('./cache-ring-vectors.json', import.meta.url));
const vectors = JSON.parse(readFileSync(path, 'utf8')) as Vectors;

describe('cache ring vectors', () => {
  it('hashes every key like the reference', () => {
    for (const { key, hash } of vectors.keys) {
      expect(hash64(key).toString(), key).toBe(hash);
    }
  });

  for (const vector of vectors.cases) {
    it(`places every key for ${vector.name}`, () => {
      const ring = buildRing(vector.nodes);

      expect(ring).toHaveLength(vectors.virtual_nodes * vector.nodes.length);
      expect(ring.slice(0, vector.first_points.length).map((p) => ({ point: p.point.toString(), node: p.node }))).toEqual(
        vector.first_points,
      );
      const last = ring[ring.length - 1];
      expect({ point: last?.point.toString(), node: last?.node }).toEqual(vector.last_point);
      for (const [key, node] of Object.entries(vector.expected)) {
        expect(locate(ring, key), key).toBe(node);
      }
    });
  }
});

describe('ring rules', () => {
  it('gives equal points to the lower name and wraps past the last point', () => {
    const ring = [
      { point: 5n, node: 'cache-a' },
      { point: 5n, node: 'cache-b' },
      { point: 9n, node: 'cache-c' },
    ];

    expect(owner(ring, 3n)).toBe('cache-a');
    expect(owner(ring, 5n)).toBe('cache-a');
    expect(owner(ring, 6n)).toBe('cache-c');
    expect(owner(ring, 10n)).toBe('cache-a');
  });

  it('does not depend on the order the nodes are listed in', () => {
    expect(buildRing(['cache-1', 'cache-0'])).toEqual(buildRing(['cache-0', 'cache-1']));
  });
});

describe('parseNodes', () => {
  it('reads names and addresses', () => {
    expect(parseNodes(' cache-0=cache-0:6379, cache-1=10.0.0.2:6380 ')).toEqual([
      { name: 'cache-0', host: 'cache-0', port: 6379 },
      { name: 'cache-1', host: '10.0.0.2', port: 6380 },
    ]);
  });

  it('treats unset or empty as no nodes', () => {
    expect(parseNodes(undefined)).toEqual([]);
    expect(parseNodes('')).toEqual([]);
  });

  it('refuses malformed entries and duplicate names', () => {
    expect(() => parseNodes('cache-0')).toThrow('name=host:port');
    expect(() => parseNodes('cache-0=cache-0')).toThrow('name=host:port');
    expect(() => parseNodes('cache-0=cache-0:0')).toThrow('name=host:port');
    expect(() => parseNodes('=cache-0:6379')).toThrow('name=host:port');
    expect(() => parseNodes('cache-0=a:1,cache-0=b:2')).toThrow('duplicate');
  });
});
