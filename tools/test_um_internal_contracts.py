import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

import grpc_tools
from google.protobuf import descriptor_pool
from google.protobuf.descriptor_pb2 import FileDescriptorSet

from check_broker_contracts import ROOT, load_json

PACKAGE = 'tamagotchi.user_management.v1'
# User Management's issuer allowlist (service-token-policy.json), audience user-management, without the
# relationship listing and token issuance, which have their own slices.
INTERNAL = {
    'users.read_profile': {'battle': 'users:read-profile'},
    'users.settle_battle': {'battle': 'users:settle-battle'},
    'users.consume_boost': {'battle': 'users:consume-boost'},
    'users.credit_global': {'battle': 'users:credit-global', 'monster-raid': 'users:credit-global'},
    'users.credit_local': {'tamagotchi': 'users:credit-local'},
    'users.get_relationship': {'guild': 'users:check-relationship'},
    'users.get_membership': {'guild': 'users:read-membership'},
}
COMMANDS = {'users.settle_battle', 'users.consume_boost', 'users.credit_global', 'users.credit_local'}
READS = {'users.read_profile', 'users.get_relationship', 'users.get_membership'}
FIELDS = {
    'GetUserProfileRequest': ['user_id'],
    'UserProfile': ['user_id', 'username'],
    'SettleBattleRequest': ['battle_id', 'winner_id', 'loser_id'],
    'BattleSettlement': ['battle_id', 'winner_id', 'loser_id', 'winner_credit', 'loser_debit', 'winner_balance', 'loser_balance'],
    'ConsumeBoostRequest': ['battle_id', 'user_id', 'boost_id'],
    'BoostReceipt': ['battle_id', 'user_id', 'boost_id', 'attack_bps', 'remaining_charges'],
    'CreditGlobalRequest': ['user_id', 'amount', 'reason', 'reference_id'],
    'CreditLocalRequest': ['user_id', 'package_id', 'config_version', 'action', 'care_action_id'],
    'CreditReceipt': ['user_id', 'credited_amount', 'new_balance', 'reference_id'],
    'GetRelationshipRequest': ['user_id', 'other_user_id'],
    'GetRelationshipReply': ['relationship', 'reverse_relationship', 'version'],
    'GetMembershipRequest': ['user_id'],
    'MembershipSnapshot': ['user_id', 'package_ids', 'membership_version'],
}
ENUMS = {
    'BoostId': ['BOOST_ID_UNSPECIFIED', 'ATTACK_10'],
    'CreditReason': ['CREDIT_REASON_UNSPECIFIED', 'RAID_WIN', 'RAID_DEFEAT', 'BATTLE_ACCESS_CAP'],
    'GetRelationshipReply.Kind': ['KIND_UNSPECIFIED', 'FRIEND', 'ENEMY', 'STRANGER'],
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


class UserManagementInternalOperationsTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        manifest = load_json(ROOT / 'contracts/operations.v1.json')
        cls.operations = {o['name']: o for o in manifest['operations']}
        cls.pool = pool()

    def test_internal_callers_match_the_issuer_allowlist_exactly(self):
        for name, callers in INTERNAL.items():
            actual = {c['service']: c['scopes'] for c in self.operations[name]['internal']['callers']}
            self.assertEqual({service: [scope] for service, scope in callers.items()}, actual, name)

    def test_the_slice_is_internal_only_and_never_carries_credentials(self):
        for name in INTERNAL:
            o = self.operations[name]
            self.assertNotIn('public', o, name)
            self.assertEqual(('user-management', 'DURABLE', False), (o['destination'], o['storage'], o['contains_credentials']), name)
            self.assertEqual((16384, 16384), (o['request_max_bytes'], o['reply_max_bytes']), name)

    def test_commands_replay_by_command_key_and_reads_are_safe(self):
        for name in COMMANDS:
            self.assertEqual(('IDEMPOTENT', 'COMMAND_KEY'), (self.operations[name]['failover'], self.operations[name]['idempotency']), name)
        for name in READS:
            self.assertEqual(('SAFE_READ', 'NONE'), (self.operations[name]['failover'], self.operations[name]['idempotency']), name)
        self.assertEqual(set(INTERNAL), COMMANDS | READS)

    def test_messages_carry_exactly_the_documented_fields(self):
        for message, fields in FIELDS.items():
            descriptor = self.pool.FindMessageTypeByName(f'{PACKAGE}.{message}')
            self.assertEqual(fields, [f.name for f in descriptor.fields], message)

    def test_every_enum_starts_with_unspecified_so_adapters_can_refuse_it(self):
        for name, values in ENUMS.items():
            descriptor = self.pool.FindEnumTypeByName(f'{PACKAGE}.{name}')
            self.assertEqual(values, [v.name for v in descriptor.values], name)
            self.assertEqual(0, descriptor.values[0].number, name)

    def test_the_public_profile_read_stays_public_only(self):
        self.assertNotIn('internal', self.operations['users.get_profile'])
        self.assertEqual(
            'type.googleapis.com/tamagotchi.user_management.v1.UserProfile',
            self.operations['users.read_profile']['internal']['response_type'])


if __name__ == '__main__':
    unittest.main()
