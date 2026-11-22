import bisect
import statistics
import sys

from cache_ring import RESOURCES, hash64, stable_id

KEY_COUNT = 100_000
SLOTS = 16384
THREE = ['cache-0', 'cache-1', 'cache-2']
FOUR = THREE + ['cache-3']


def experiment_keys(count=KEY_COUNT):
    return [f'{service}:v1:{resource}:{stable_id("experiment", str(n))}'
            for n in range(count)
            for service, resource in [RESOURCES[n % len(RESOURCES)]]]


def ring_of(nodes, virtual_nodes):
    return sorted((hash64(f'{name}#{index}'), name)
                  for name in nodes for index in range(virtual_nodes))


def place_ring(ring, hashes):
    points = [point for point, _ in ring]
    names = [name for _, name in ring]
    return [names[bisect.bisect_left(points, value) % len(names)] for value in hashes]


def place_modulo(nodes, hashes):
    return [nodes[value % len(nodes)] for value in hashes]


def crc16(data):
    crc = 0
    for byte in data:
        crc ^= byte << 8
        for _ in range(8):
            crc = ((crc << 1) ^ 0x1021) if crc & 0x8000 else crc << 1
            crc &= 0xFFFF
    return crc


def hash_slot(key):
    start = key.find('{')
    if start != -1:
        end = key.find('}', start + 1)
        if end > start + 1:
            key = key[start + 1:end]
    return crc16(key.encode('utf-8')) % SLOTS


def even_slot_ranges(count):
    bounds = [round(i * SLOTS / count) for i in range(count + 1)]
    return [(bounds[i], bounds[i + 1]) for i in range(count)]


def cluster_three():
    return {slot: f'master-{i}' for i, (low, high) in enumerate(even_slot_ranges(3))
            for slot in range(low, high)}


def cluster_rebalanced(owners):
    target = SLOTS // 4
    moved = dict(owners)
    taken = 0
    for master in ['master-0', 'master-1', 'master-2']:
        own = sorted(slot for slot, owner in owners.items() if owner == master)
        give = len(own) - target if master != 'master-2' else target - taken
        for slot in own[:give]:
            moved[slot] = 'master-3'
        taken += give
    return moved


def shares(placement, nodes):
    counts = {node: 0 for node in nodes}
    for node in placement:
        counts[node] += 1
    return counts


def spread_row(label, placement, nodes):
    counts = shares(placement, nodes)
    total = len(placement)
    values = list(counts.values())
    mean = total / len(nodes)
    cells = ' | '.join(f'{100 * counts[node] / total:.2f}%' for node in nodes)
    return (f'| {label} | {cells} | {max(values) / mean:.3f} | '
            f'{100 * statistics.pstdev(values) / mean:.2f}% |')


def moved(before, after):
    return sum(1 for a, b in zip(before, after) if a != b)


def main():
    keys = experiment_keys()
    hashes = [hash64(key) for key in keys]
    total = len(keys)
    out = []

    out.append(f'Key spread over 3 nodes, {total:,} keys')
    out.append('')
    out.append('| Placement | cache-0 | cache-1 | cache-2 | max / mean | std dev / mean |')
    out.append('|---|---|---|---|---|---|')
    for virtual_nodes in (1, 16, 128, 1024):
        placement = place_ring(ring_of(THREE, virtual_nodes), hashes)
        out.append(spread_row(f'ring, {virtual_nodes} virtual node{"s" if virtual_nodes > 1 else ""}',
                              placement, THREE))
    out.append(spread_row('hash % 3', place_modulo(THREE, hashes), THREE))
    slots = [hash_slot(key) for key in keys]
    three_masters = cluster_three()
    out.append(spread_row('Redis Cluster slots', [three_masters[s].replace('master', 'cache') for s in slots],
                          THREE))
    out.append('')

    out.append(f'Keys that move when a 4th node joins, {total:,} keys')
    out.append('')
    out.append('| Placement | moved | of keys | ideal | moved to the new node |')
    out.append('|---|---|---|---|---|')
    for virtual_nodes in (1, 16, 128, 1024):
        before = place_ring(ring_of(THREE, virtual_nodes), hashes)
        after = place_ring(ring_of(FOUR, virtual_nodes), hashes)
        count = moved(before, after)
        to_new = sum(1 for a, b in zip(before, after) if a != b and b == 'cache-3')
        out.append(f'| ring, {virtual_nodes} virtual node{"s" if virtual_nodes > 1 else ""} | {count:,} | '
                   f'{100 * count / total:.2f}% | 25% | {100 * to_new / max(count, 1):.0f}% |')
    before = place_modulo(THREE, hashes)
    after = place_modulo(FOUR, hashes)
    count = moved(before, after)
    to_new = sum(1 for a, b in zip(before, after) if a != b and b == 'cache-3')
    out.append(f'| hash % N, 3 to 4 | {count:,} | {100 * count / total:.2f}% | 25% | '
               f'{100 * to_new / max(count, 1):.0f}% |')
    rebalanced = cluster_rebalanced(three_masters)
    count = sum(1 for s in slots if three_masters[s] != rebalanced[s])
    out.append(f'| Redis Cluster, 4,096 slots resharded | {count:,} | {100 * count / total:.2f}% | 25% | 100% |')
    out.append('')

    before = place_ring(ring_of(THREE, 128), hashes)
    after = place_ring(ring_of(['cache-0', 'cache-2'], 128), hashes)
    count = moved(before, after)
    owned = sum(1 for node in before if node == 'cache-1')
    out.append(f'Removing cache-1 from the 128 virtual node ring moves {count:,} keys '
               f'({100 * count / total:.2f}%), exactly the {owned:,} it held.')
    print('\n'.join(out))
    return 0


if __name__ == '__main__':
    sys.exit(main())
