import { createHash } from 'node:crypto';

export const VIRTUAL_NODES = 128;

export type CacheNode = { name: string; host: string; port: number };

export type RingPoint = { point: bigint; node: string };

export function hash64(text: string): bigint {
  return createHash('md5').update(text, 'utf8').digest().readBigUInt64BE(0);
}

export function parseNodes(value: string | undefined): CacheNode[] {
  const nodes: CacheNode[] = [];

  for (const raw of (value ?? '').split(',')) {
    const entry = raw.trim();
    if (entry === '') {
      continue;
    }

    const separator = entry.indexOf('=');
    const name = separator === -1 ? '' : entry.slice(0, separator);
    const address = separator === -1 ? '' : entry.slice(separator + 1);
    const colon = address.lastIndexOf(':');
    const host = address.slice(0, colon);
    const port = Number(address.slice(colon + 1));

    if (name === '' || colon <= 0 || !Number.isInteger(port) || port < 1 || port > 65535) {
      throw new Error(`CACHE_NODES entry must be name=host:port: ${entry}`);
    }
    if (nodes.some((node) => node.name === name)) {
      throw new Error(`duplicate cache node name: ${name}`);
    }

    nodes.push({ name, host, port });
  }

  return nodes;
}

function compare(a: RingPoint, b: RingPoint): number {
  if (a.point !== b.point) {
    return a.point < b.point ? -1 : 1;
  }
  if (a.node === b.node) {
    return 0;
  }
  return a.node < b.node ? -1 : 1;
}

export function buildRing(names: readonly string[]): RingPoint[] {
  const ring: RingPoint[] = [];

  for (const node of names) {
    for (let index = 0; index < VIRTUAL_NODES; index += 1) {
      ring.push({ point: hash64(`${node}#${index}`), node });
    }
  }

  return ring.sort(compare);
}

export function owner(ring: readonly RingPoint[], value: bigint): string {
  let low = 0;
  let high = ring.length;

  while (low < high) {
    const middle = (low + high) >>> 1;
    if ((ring[middle] as RingPoint).point < value) {
      low = middle + 1;
    } else {
      high = middle;
    }
  }

  return (ring[low % ring.length] as RingPoint).node;
}

export function locate(ring: readonly RingPoint[], key: string): string {
  return owner(ring, hash64(key));
}
