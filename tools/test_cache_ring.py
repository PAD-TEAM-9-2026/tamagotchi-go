import json
import unittest

import cache_ring


class CacheRingTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.data = json.loads(cache_ring.VECTORS.read_text(encoding='utf-8'))
        cls.cases = {case['name']: case for case in cls.data['cases']}

    def test_file_matches_reference(self):
        self.assertEqual(cache_ring.VECTORS.read_text(encoding='utf-8'), cache_ring.render())

    def test_about_two_hundred_distinct_keys(self):
        keys = [entry['key'] for entry in self.data['keys']]
        self.assertEqual(len(keys), 200)
        self.assertEqual(len(set(keys)), 200)

    def test_ring_has_every_virtual_node(self):
        for case in self.data['cases']:
            ring = cache_ring.build_ring(case['nodes'])
            self.assertEqual(len(ring), 128 * len(case['nodes']))

    def test_every_node_owns_keys(self):
        for case in self.data['cases']:
            self.assertEqual(set(case['expected'].values()), set(case['nodes']))

    def test_removing_a_node_moves_only_its_keys(self):
        before = self.cases['three_nodes']['expected']
        after = self.cases['cache_1_removed']['expected']
        for key, node in before.items():
            if node != 'cache-1':
                self.assertEqual(after[key], node, key)
            else:
                self.assertNotEqual(after[key], 'cache-1', key)

    def test_adding_a_node_moves_keys_only_to_it(self):
        before = self.cases['three_nodes']['expected']
        after = self.cases['cache_3_added']['expected']
        moved = [key for key in before if after[key] != before[key]]
        self.assertTrue(moved)
        for key in moved:
            self.assertEqual(after[key], 'cache-3', key)

    def test_key_equal_to_a_point_maps_to_that_node(self):
        expected = self.cases['three_nodes']['expected']
        self.assertEqual(expected['cache-0#0'], 'cache-0')
        self.assertEqual(expected['cache-1#64'], 'cache-1')
        self.assertEqual(expected['cache-2#127'], 'cache-2')

    def test_keys_past_the_last_point_wrap_to_the_first(self):
        ring = cache_ring.build_ring(cache_ring.THREE_NODES)
        wrapped = [entry['key'] for entry in self.data['keys'] if int(entry['hash']) > ring[-1][0]]
        self.assertTrue(wrapped)
        for key in wrapped:
            self.assertEqual(self.cases['three_nodes']['expected'][key], ring[0][1])

    def test_ties_go_to_the_lower_name(self):
        ring = sorted([(5, 'cache-b'), (5, 'cache-a'), (9, 'cache-c')])
        self.assertEqual(cache_ring.owner(ring, 3), 'cache-a')
        self.assertEqual(cache_ring.owner(ring, 5), 'cache-a')
        self.assertEqual(cache_ring.owner(ring, 6), 'cache-c')
        self.assertEqual(cache_ring.owner(ring, 9), 'cache-c')
        self.assertEqual(cache_ring.owner(ring, 10), 'cache-a')

    def test_parse_nodes_uses_names_only(self):
        self.assertEqual(cache_ring.parse_nodes('cache-0=cache-0:6379, cache-1=10.0.0.2:6380'),
                         ['cache-0', 'cache-1'])
        self.assertEqual(cache_ring.parse_nodes(''), [])
        with self.assertRaises(ValueError):
            cache_ring.parse_nodes('cache-0=a:1,cache-0=b:1')
        with self.assertRaises(ValueError):
            cache_ring.parse_nodes('cache-0')

    def test_address_does_not_change_placement(self):
        first = cache_ring.build_ring(cache_ring.parse_nodes('cache-0=a:1,cache-1=b:1'))
        second = cache_ring.build_ring(cache_ring.parse_nodes('cache-1=z:9,cache-0=y:9'))
        self.assertEqual(first, second)


if __name__ == '__main__':
    unittest.main()
