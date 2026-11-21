"""Compatibility guards for the proposed coordinator boundary."""
import unittest
from check_broker_contracts import ROOT, compile_types, load_json

class TransactionContractsTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.types = compile_types()
        cls.operations = {o['name']: o for o in load_json(ROOT / 'contracts/operations.v1.json')['operations']}

    def test_transaction_ids_and_battle_ids_are_separate(self):
        text = (ROOT / 'contracts/proto/tamagotchi/transactions/v1/coordinator.proto').read_text()
        self.assertIn('string transaction_id = 1;', text)
        self.assertIn('string battle_id = 1;', text)
        self.assertIn('rpc GetDecision(GetDecisionRequest) returns (Decision);', text)

    def test_participant_operations_are_internal_and_battle_only(self):
        for domain in ('users', 'tamagotchi', 'battle'):
            for phase in ('prepare', 'commit', 'abort'):
                op = self.operations[f'{domain}.tx_{phase}']
                self.assertNotIn('public', op)
                self.assertEqual('DURABLE', op['storage'])
                self.assertEqual(('IDEMPOTENT', 'COMMAND_KEY'), (op['failover'], op['idempotency']))
                self.assertEqual([{'service':'battle', 'scopes':[f'{domain}:transactions']}], op['internal']['callers'])
                self.assertIn(op['internal']['request_type'], self.types)
                self.assertIn(op['internal']['response_type'], self.types)

    def test_compensation_keeps_unsigned_balances(self):
        text = (ROOT / 'contracts/proto/tamagotchi/user_management/v1/battle_settlements.proto').read_text()
        self.assertIn('uint32 unrecovered = 4;', text)
        self.assertIn('uint64 winner_balance = 6;', text)
        self.assertIn('uint64 loser_balance = 7;', text)
        self.assertNotIn('int64 ', text.replace('uint64 ', ''))

    def test_no_new_public_coordinator_routes(self):
        for name, op in self.operations.items():
            if name.endswith(('.mark_settled', '.reverse_settlement', '.revoke_xp')):
                self.assertNotIn('public', op)
                self.assertFalse(op['contains_credentials'])
