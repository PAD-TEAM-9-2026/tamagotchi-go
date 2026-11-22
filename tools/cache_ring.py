import argparse
import bisect
import hashlib
import json
from pathlib import Path
import sys
import uuid

ROOT = Path(__file__).resolve().parents[1]
VECTORS = ROOT / 'contracts' / 'cache-ring-vectors.json'
VIRTUAL_NODES = 128
THREE_NODES = ['cache-0', 'cache-1', 'cache-2']
RESOURCES = [
    ('tamagotchi', 'creature'),
    ('tamagotchi', 'collection'),
    ('tamagotchi', 'type-matrix'),
    ('notification', 'history'),
    ('notification', 'preferences'),
    ('registry', 'package'),
    ('registry', 'stat-definitions'),
    ('guild', 'guild'),
    ('guild', 'members'),
    ('users', 'profile'),
    ('users', 'wallet'),
    ('battle', 'battle'),
    ('map', 'region'),
    ('raid', 'raid'),
]


def hash64(text):
    return int.from_bytes(hashlib.md5(text.encode('utf-8')).digest()[:8], 'big')


def parse_nodes(value):
    names = []
    for entry in value.split(','):
        entry = entry.strip()
        if not entry:
            continue
        name, separator, address = entry.partition('=')
        if not separator or not name or not address:
            raise ValueError('CACHE_NODES entry must be name=host:port: ' + entry)
        if name in names:
            raise ValueError('duplicate cache node name: ' + name)
        names.append(name)
    return names


def build_ring(nodes):
    return sorted((hash64(f'{name}#{index}'), name)
                  for name in nodes for index in range(VIRTUAL_NODES))


def owner(ring, value):
    position = bisect.bisect_left(ring, (value, ''))
    return ring[position % len(ring)][1]


def locate(ring, key):
    return owner(ring, hash64(key))


def stable_id(*parts):
    return str(uuid.UUID(bytes=hashlib.md5(':'.join(parts).encode('utf-8')).digest()))


def wrap_keys(ring, count):
    top = ring[-1][0]
    found = []
    index = 0
    while len(found) < count:
        key = f'tamagotchi:v1:creature:{stable_id("wrap", str(index))}'
        if hash64(key) > top:
            found.append(key)
        index += 1
    return found


def vector_keys():
    keys = [f'{service}:v1:{resource}:{stable_id(service, resource, str(n))}'
            for service, resource in RESOURCES for n in range(13)]
    for n in range(10):
        query = f'limit=20&page={n + 1}'
        suffix = hashlib.sha256(query.encode('utf-8')).hexdigest()[:16]
        keys.append(f'notification:v1:history:{stable_id("user", str(n))}:{suffix}')
    keys += ['cache-0#0', 'cache-1#64', 'cache-2#127']
    keys += ['notification:v1:preferences:ünïcødé-ключ', 'x', 'registry:v1:package:' + 'p' * 300]
    keys += wrap_keys(build_ring(THREE_NODES), 2)
    return keys


def case(name, nodes, keys):
    ring = build_ring(nodes)
    return {
        'name': name,
        'nodes': nodes,
        'first_points': [{'point': str(point), 'node': node} for point, node in ring[:5]],
        'last_point': {'point': str(ring[-1][0]), 'node': ring[-1][1]},
        'expected': {key: locate(ring, key) for key in keys},
    }


def vectors():
    keys = vector_keys()
    return {
        'spec': 'contracts/cache.md',
        'hash': 'md5, first 8 bytes, big-endian unsigned 64-bit',
        'virtual_nodes': VIRTUAL_NODES,
        'keys': [{'key': key, 'hash': str(hash64(key))} for key in keys],
        'cases': [
            case('three_nodes', THREE_NODES, keys),
            case('cache_1_removed', ['cache-0', 'cache-2'], keys),
            case('cache_3_added', THREE_NODES + ['cache-3'], keys),
        ],
    }


def render():
    return json.dumps(vectors(), ensure_ascii=False, indent=2) + '\n'


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    text = render()
    if args.check:
        if VECTORS.read_text(encoding='utf-8') != text:
            print('contracts/cache-ring-vectors.json is stale; run python tools/cache_ring.py')
            return 1
        print('contracts/cache-ring-vectors.json matches the reference ring')
        return 0
    VECTORS.write_text(text, encoding='utf-8')
    print(f'wrote {VECTORS.relative_to(ROOT)}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
