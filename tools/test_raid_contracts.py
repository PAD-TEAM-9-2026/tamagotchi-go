import subprocess
import json
import sys
import tempfile
import unittest
from pathlib import Path

import grpc_tools
from google.protobuf import descriptor_pool
from google.protobuf.descriptor_pb2 import FileDescriptorSet

from check_broker_contracts import ROOT, load_json

STANDARD_RESPONSE = ['content-type', 'retry-after', 'www-authenticate', 'cache-control']
# Monster Raid's public routes, from its endpoint code: (failover, idempotency, method, path, query, reply bound).
PUBLIC = {
    'raid.create_raid': ('IDEMPOTENT', 'COMMAND_KEY', 'POST', '/v1/raids', [], 65536),
    'raid.list_raids': ('SAFE_READ', 'NONE', 'GET', '/v1/raids', ['guild_id', 'limit', 'cursor'], 262144),
    'raid.get_raid': ('SAFE_READ', 'NONE', 'GET', '/v1/raids/{raidId}', [], 65536),
    'raid.attack': ('IDEMPOTENT', 'COMMAND_KEY', 'POST', '/v1/raids/{raidId}/attack', [], 65536),
    'raid.get_leaderboard': ('SAFE_READ', 'NONE', 'GET', '/v1/raids/{raidId}/leaderboard', ['limit', 'cursor'], 65536),
    'raid.cancel_raid': ('UNSAFE', 'NONE', 'DELETE', '/v1/raids/{raidId}', [], 65536),
}
# Raid's typed dependency reads: operation -> (package, request, response, scope).
READS = {
    'guild.get_guild': ('tamagotchi.guild.v1', 'GetGuildRequest', 'GetGuildResponse', 'guild:read'),
    'guild.list_members': ('tamagotchi.guild.v1', 'ListMembersRequest', 'ListMembersResponse', 'guild:read'),
    'registry.get_occurrence': ('tamagotchi.package_registry.v1', 'GetOccurrenceRequest', 'GetOccurrenceResponse',
                                'registry:read-occurrences'),
    'registry.get_boss': ('tamagotchi.package_registry.v1', 'GetBossRequest', 'GetBossResponse', 'registry:read-bosses'),
}
# The messages mirror the public JSON shapes in docs/field-types.md, field for field.
FIELDS = {
    'tamagotchi.guild.v1.GetGuildRequest': ['guild_id'],
    'tamagotchi.guild.v1.GetGuildResponse': ['guild_id', 'name', 'description', 'leader_id', 'member_count', 'version'],
    'tamagotchi.guild.v1.ListMembersRequest': ['guild_id'],
    'tamagotchi.guild.v1.ListMembersResponse': ['guild_id', 'members', 'version', 'retrieved_at'],
    'tamagotchi.guild.v1.Member': ['user_id', 'role'],
    'tamagotchi.package_registry.v1.GetOccurrenceRequest': ['occurrence_id'],
    'tamagotchi.package_registry.v1.GetOccurrenceResponse': [
        'occurrence_id', 'boss_id', 'boss_version', 'available_from', 'available_until', 'status', 'version'],
    'tamagotchi.package_registry.v1.GetBossRequest': ['boss_id', 'config_version'],
    'tamagotchi.package_registry.v1.GetBossResponse': ['boss_id', 'config_version', 'definition'],
    'tamagotchi.package_registry.v1.BossInput': [
        'name', 'description', 'sprite_ref', 'combat_type', 'max_hp', 'defense', 'weaknesses', 'resistances',
        'special_properties', 'duration_seconds', 'max_participants', 'rewards', 'defeat_rewards'],
    'tamagotchi.package_registry.v1.RewardAmounts': ['global_currency', 'xp'],
}


def pool():
    base = ROOT / 'contracts/proto'
    with tempfile.TemporaryDirectory() as directory:
        output = Path(directory) / 'schema.pb'
        subprocess.run([sys.executable, '-m', 'grpc_tools.protoc', f'-I{base}',
                        f"-I{Path(grpc_tools.__file__).parent / '_proto'}", '--include_imports',
                        f'--descriptor_set_out={output}', *map(str, sorted(base.rglob('*.proto')))], check=True)
        files = FileDescriptorSet.FromString(output.read_bytes())
    result = descriptor_pool.DescriptorPool()
    for file in files.file:
        result.Add(file)
    return result


class MonsterRaidContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        manifest = load_json(ROOT / 'contracts/operations.v1.json')
        cls.operations = {o['name']: o for o in manifest['operations']}
        cls.pool = pool()

    def test_raid_exposes_exactly_its_six_user_routes(self):
        raid = sorted(n for n, o in self.operations.items() if o['destination'] == 'monster-raid')
        self.assertEqual(sorted(PUBLIC), raid)
        for name, (failover, idempotency, method, path, query, reply) in PUBLIC.items():
            o = self.operations[name]
            self.assertNotIn('internal', o, name)
            self.assertEqual(('DURABLE', False, 16384), (o['storage'], o['contains_credentials'], o['request_max_bytes']), name)
            self.assertEqual((failover, idempotency, reply), (o['failover'], o['idempotency'], o['reply_max_bytes']), name)
            public = o['public']
            self.assertEqual(('user', method, path, query), (public['access'], public['method'], public['path'],
                                                              public['query_parameters']), name)

    def test_only_mutations_take_a_body_and_only_creation_answers_with_a_location(self):
        for name in PUBLIC:
            public = self.operations[name]['public']
            body = ['accept', 'content-type'] if public['method'] == 'POST' else ['accept']
            self.assertEqual(body, public['request_headers'], name)
            expected = list(STANDARD_RESPONSE)
            if name == 'raid.create_raid':
                expected.insert(1, 'location')
            self.assertEqual(expected, public['response_headers'], name)

    def test_dependency_reads_keep_their_public_route_and_add_one_typed_caller(self):
        for name, (package, request, response, scope) in READS.items():
            o = self.operations[name]
            self.assertEqual(('user', 'GET'), (o['public']['access'], o['public']['method']), name)
            self.assertEqual(('SAFE_READ', 'NONE'), (o['failover'], o['idempotency']), name)
            internal = o['internal']
            self.assertEqual(f'type.googleapis.com/{package}.{request}', internal['request_type'], name)
            self.assertEqual(f'type.googleapis.com/{package}.{response}', internal['response_type'], name)
            self.assertEqual([{'service': 'monster-raid', 'scopes': [scope]}], internal['callers'], name)

    def test_raid_reads_stat_bonuses_with_the_existing_config_scope(self):
        callers = {c['service']: c['scopes'] for c in self.operations['registry.get_stat_bonuses']['internal']['callers']}
        self.assertEqual({'battle': ['registry:read-config'], 'monster-raid': ['registry:read-config'],
                          'tamagotchi': ['registry:read-config']}, callers)

    def test_messages_mirror_the_public_shapes(self):
        for message, fields in FIELDS.items():
            descriptor = self.pool.FindMessageTypeByName(message)
            self.assertEqual(fields, [f.name for f in descriptor.fields], message)

    def test_member_roles_start_with_unspecified_so_adapters_can_refuse_it(self):
        role = self.pool.FindEnumTypeByName('tamagotchi.guild.v1.Member.Role')
        self.assertEqual(['ROLE_UNSPECIFIED', 'LEADER', 'OFFICER', 'MEMBER'], [v.name for v in role.values])
        self.assertEqual(0, role.values[0].number)

    def test_timestamps_use_the_well_known_type(self):
        for message, field in [('tamagotchi.guild.v1.ListMembersResponse', 'retrieved_at'),
                               ('tamagotchi.package_registry.v1.GetOccurrenceResponse', 'available_from'),
                               ('tamagotchi.package_registry.v1.GetOccurrenceResponse', 'available_until')]:
            descriptor = self.pool.FindMessageTypeByName(message).fields_by_name[field]
            self.assertEqual('google.protobuf.Timestamp', descriptor.message_type.full_name, f'{message}.{field}')



class ConditionalRegistryWriteTests(unittest.TestCase):
    def test_conditional_updates_keep_required_keys_without_enabling_failover(self):
        catalogue = json.loads((ROOT / "contracts/operations.v1.json").read_text())
        operations = {item["name"]: item for item in catalogue["operations"]}
        for name in ("registry.update_package", "registry.update_boss"):
            with self.subTest(operation=name):
                self.assertEqual("COMMAND_KEY", operations[name]["idempotency"])
                self.assertEqual("UNSAFE", operations[name]["failover"])
                self.assertIn("if-match", operations[name]["public"]["request_headers"])

if __name__ == '__main__':
    unittest.main()
