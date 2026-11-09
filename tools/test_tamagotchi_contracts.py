import unittest

from google.protobuf import descriptor_pool, message_factory
from google.protobuf.descriptor_pb2 import FileDescriptorSet
from pathlib import Path
import subprocess
import sys
import tempfile
import grpc_tools

from check_broker_contracts import ROOT, load_json

PACKAGE = 'tamagotchi.tamagotchi.v1'
ENVELOPE_LIMIT = 1048576
INTERNAL = {
    'tamagotchi.award_xp': {'battle': 'tamagotchi:award-xp', 'monster-raid': 'tamagotchi:award-xp'},
    'tamagotchi.grant_access': {'battle': 'tamagotchi:grant-access'},
    'tamagotchi.reserve_engagement': {'battle': 'tamagotchi:reserve-engagement', 'monster-raid': 'tamagotchi:reserve-engagement'},
    'tamagotchi.get_engagement': {'battle': 'tamagotchi:read-engagement', 'monster-raid': 'tamagotchi:read-engagement'},
    'tamagotchi.release_engagement': {'battle': 'tamagotchi:release-engagement', 'monster-raid': 'tamagotchi:release-engagement'},
    'tamagotchi.list_holders': {'battle': 'tamagotchi:read-holders'},
    'tamagotchi.get_collection': {'monster-raid': 'tamagotchi:read-collection'},
}
ISSUER_ALLOWLIST = {
    'battle': {'tamagotchi:reserve-engagement', 'tamagotchi:read-engagement', 'tamagotchi:release-engagement', 'tamagotchi:award-xp', 'tamagotchi:grant-access', 'tamagotchi:read-holders'},
    'monster-raid': {'tamagotchi:read-collection', 'tamagotchi:reserve-engagement', 'tamagotchi:read-engagement', 'tamagotchi:release-engagement', 'tamagotchi:award-xp'},
}
MEASURED_JSON_PAGE = {
    'tamagotchi.list_tamagotchis': 544940,
    'tamagotchi.get_collection': 544940,
    'notification.list_devices': 26340,
    'notification.list_notifications': 55940,
}


def descriptors():
    base = ROOT / 'contracts/proto'
    with tempfile.TemporaryDirectory() as directory:
        output = Path(directory) / 'schema.pb'
        subprocess.run([sys.executable, '-m', 'grpc_tools.protoc', f'-I{base}', f"-I{Path(grpc_tools.__file__).parent / '_proto'}", '--include_imports', f'--descriptor_set_out={output}', *map(str, sorted(base.rglob('*.proto')))], check=True)
        content = FileDescriptorSet.FromString(output.read_bytes())
    pool = descriptor_pool.DescriptorPool()
    for file in content.file:
        pool.Add(file)
    return pool


class TamagotchiContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.pool = descriptors()
        cls.catalogue = load_json(ROOT / 'contracts/operations.v1.json')
        cls.operations = {o['name']: o for o in cls.catalogue['operations']}

    def message(self, name):
        return self.pool.FindMessageTypeByName(f'{PACKAGE}.{name}')

    def test_internal_callers_match_the_issuer_allowlist_exactly(self):
        for name, callers in INTERNAL.items():
            actual = {c['service']: c['scopes'] for c in self.operations[name]['internal']['callers']}
            self.assertEqual({service: [scope] for service, scope in callers.items()}, actual, name)
        granted = {}
        for name in INTERNAL:
            for caller in self.operations[name]['internal']['callers']:
                granted.setdefault(caller['service'], set()).update(caller['scopes'])
        self.assertEqual(ISSUER_ALLOWLIST, granted)

    def test_creature_mint_and_read_creature_stay_unmapped_internally(self):
        self.assertNotIn('internal', self.operations['tamagotchi.get_tamagotchi'])
        self.assertFalse(any('mint' in name for name in self.operations))
        internal_only = [n for n, o in self.operations.items() if o['destination'] == 'tamagotchi' and 'public' not in o]
        self.assertEqual(5, len(internal_only))
        self.assertTrue(all(o['internal']['request_type'].startswith(f'type.googleapis.com/{PACKAGE}.') for o in self.operations.values() if o['destination'] == 'tamagotchi' and 'internal' in o))

    def test_mutations_need_a_command_key_and_reads_do_not(self):
        for name in ['tamagotchi.award_xp', 'tamagotchi.grant_access', 'tamagotchi.reserve_engagement', 'tamagotchi.release_engagement']:
            self.assertEqual(('IDEMPOTENT', 'COMMAND_KEY'), (self.operations[name]['failover'], self.operations[name]['idempotency']), name)
        for name in ['tamagotchi.get_engagement', 'tamagotchi.list_holders', 'tamagotchi.get_collection']:
            self.assertEqual(('SAFE_READ', 'NONE'), (self.operations[name]['failover'], self.operations[name]['idempotency']), name)
        self.assertTrue(all(not o['contains_credentials'] and o['storage'] == 'DURABLE' for o in self.operations.values() if o['destination'] == 'tamagotchi'))

    def test_public_and_internal_mappings_coexist_only_on_the_user_or_service_reads(self):
        both = sorted(n for n, o in self.operations.items() if 'public' in o and 'internal' in o)
        self.assertEqual(['tamagotchi.get_collection', 'tamagotchi.list_holders'], both)

    def test_reply_bounds_cover_the_measured_worst_case_pages_inside_the_envelope(self):
        for name, measured in MEASURED_JSON_PAGE.items():
            bound = self.operations[name]['reply_max_bytes']
            self.assertGreaterEqual(bound, measured, name)
            self.assertLess(bound, ENVELOPE_LIMIT, name)

    def test_worst_case_collection_reply_fits_its_bound(self):
        reply_type = message_factory.GetMessageClass(self.message('GetCollectionReply'))
        creature = message_factory.GetMessageClass(self.message('Creature'))
        reply = reply_type(user_id='0192f3e4-6b71-7d92-a3c5-8e1f4b2d7a90')
        reply.next_cursor = 'c' * 2048
        reply.retrieved_at.GetCurrentTime()
        for position in range(101):
            record = creature(id='0192f3e4-6b71-7d92-a3c5-8e1f4b2d7a90', name='n' * 64, origin_package_id='0192f3e4-6b71-7d92-a3c5-8e1f4b2d7a90',
                              config_version=2147483647, origin_owner_id='0192f3e4-6b71-7d92-a3c5-8e1f4b2d7a90',
                              holder_user_ids=['0192f3e4-6b71-7d92-a3c5-8e1f4b2d7a90'] * 5, role=creature.SECONDARY,
                              combat_type=creature.ELECTRIC, level=100, xp=2 ** 53 - 1, sprite_ref='s' * 512, version=2147483647)
            for index in range(32):
                record.package_stats[f'k{index:02d}'.ljust(64, 'k')].text = 'v' * 64
            record.acquired_at.GetCurrentTime()
            if position == 0:
                reply.primary.CopyFrom(record)
            else:
                reply.secondary.append(record)
        self.assertLessEqual(len(reply.SerializeToString()), self.operations['tamagotchi.get_collection']['reply_max_bytes'])

    def test_wire_numbers_types_and_presence(self):
        def fields(name):
            return {f.name: (f.number, f.type, f.is_repeated) for f in self.message(name).fields}
        self.assertEqual({'tamagotchi_id': (1, 9, False), 'amount': (2, 13, False), 'source': (3, 14, False), 'source_id': (4, 9, False)}, fields('AwardXpRequest'))
        self.assertEqual({'id': (1, 9, False), 'xp': (2, 4, False), 'level': (3, 13, False), 'levels_gained': (4, 13, False), 'version': (5, 13, False)}, fields('AwardXpReply'))
        self.assertEqual({'tamagotchi_id': (1, 9, False), 'to_user_id': (2, 9, False), 'reason': (3, 14, False), 'reference_id': (4, 9, False), 'engagement_id': (5, 9, False)}, fields('GrantAccessRequest'))
        self.assertEqual({'source': (1, 14, False), 'reference_id': (2, 9, False), 'lineups': (3, 11, True), 'ttl_seconds': (4, 13, False)}, fields('ReserveEngagementRequest'))
        self.assertEqual(14, len(self.message('Creature').fields))
        request = message_factory.GetMessageClass(self.message('GetCollectionRequest'))()
        self.assertFalse(request.HasField('cursor'))
        request.cursor = ''
        self.assertTrue(type(request).FromString(request.SerializeToString()).HasField('cursor'))
        reply = message_factory.GetMessageClass(self.message('GetCollectionReply'))()
        self.assertFalse(reply.HasField('primary'))
        self.assertFalse(reply.HasField('next_cursor'))
        lineup = message_factory.GetMessageClass(self.message('Lineup'))()
        self.assertFalse(lineup.HasField('secondary_id'))

    def test_every_enum_starts_with_an_unspecified_zero_and_has_the_documented_values(self):
        expected = {
            'Source': ['SOURCE_UNSPECIFIED', 'SOURCE_BATTLE', 'SOURCE_RAID'],
            'Creature.CombatType': ['COMBAT_TYPE_UNSPECIFIED', 'FLAME', 'NATURE', 'EARTH', 'ELECTRIC', 'WATER', 'SHADOW'],
            'Creature.Role': ['ROLE_UNSPECIFIED', 'PRIMARY', 'SECONDARY'],
            'GrantAccessRequest.Reason': ['REASON_UNSPECIFIED', 'BATTLE_LOSS'],
            'GrantAccessReply.Outcome': ['OUTCOME_UNSPECIFIED', 'GRANTED', 'ALREADY_HOLDER', 'CAP_REACHED'],
            'Engagement.Status': ['STATUS_UNSPECIFIED', 'ACTIVE', 'RELEASED', 'EXPIRED'],
        }
        for name, values in expected.items():
            enum = self.pool.FindEnumTypeByName(f'{PACKAGE}.{name}')
            self.assertEqual(values, [v.name for v in enum.values], name)
            self.assertEqual([0, *range(1, len(values))], [v.number for v in enum.values], name)
            self.assertTrue(enum.values[0].name.endswith('UNSPECIFIED'), name)


if __name__ == '__main__':
    unittest.main()
