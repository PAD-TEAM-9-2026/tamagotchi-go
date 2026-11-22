"""Guards for the battle-settlement saga definition in contracts/transactions.md."""

import re
import unittest

from check_broker_contracts import ROOT, load_json

PROTO = ROOT / "contracts/proto/tamagotchi/transactions/v1/coordinator.proto"
DOC = ROOT / "contracts/transactions.md"

STEPS = [
    ("settle_currency", "users.settle_battle", "COMPENSATABLE", "users.reverse_settlement"),
    ("award_xp_primary", "tamagotchi.award_xp", "COMPENSATABLE", "tamagotchi.revoke_xp"),
    ("award_xp_secondary", "tamagotchi.award_xp", "COMPENSATABLE", "tamagotchi.revoke_xp"),
    ("grant_access", "tamagotchi.grant_access", "PIVOT", None),
    ("credit_access_cap", "users.credit_global", "RETRIABLE", None),
    ("release_engagement", "tamagotchi.release_engagement", "FINALIZER", None),
    ("mark_settled", "battle.mark_settled", "FINALIZER", None),
]


class SagaContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.proto = PROTO.read_text()
        cls.doc = DOC.read_text()
        operations = load_json(ROOT / "contracts/operations.v1.json")["operations"]
        cls.operations = {o["name"]: o for o in operations}

    def test_saga_view_uses_typed_states(self):
        self.assertIn("SagaState state = 3;", self.proto)
        self.assertIn("SagaStepState state = 2;", self.proto)
        self.assertIn("SagaStepKind kind = 5;", self.proto)
        for value in ("SAGA_RUNNING", "SAGA_COMPENSATING", "SAGA_COMPLETED",
                      "SAGA_COMPENSATED", "SAGA_NEEDS_ATTENTION"):
            self.assertRegex(self.proto, rf"\b{value} = \d+;")

    def test_steps_are_listed_in_order_with_kind_and_compensation(self):
        rows = re.findall(r"^\| (\d) \| `([a-z_]+)` \| `([a-z_.]+)`[^|]*\| ([A-Z]+) \|", self.doc, re.M)
        self.assertEqual([(s, o, k) for s, o, k, _ in STEPS], [(s, o, k) for _, s, o, k in rows])
        for step, _, _, compensation in STEPS:
            if compensation:
                self.assertRegex(self.doc, rf"`{step}`.*`{re.escape(compensation)}`")

    def test_every_step_operation_is_internal_and_idempotent(self):
        names = {o for _, o, _, c in STEPS} | {c for *_, c in STEPS if c}
        for name in names:
            op = self.operations[name]
            self.assertNotIn("public", op, name)
            self.assertEqual(("IDEMPOTENT", "COMMAND_KEY"), (op["failover"], op["idempotency"]), name)
            self.assertIn("battle", [c["service"] for c in op["internal"]["callers"]], name)


if __name__ == "__main__":
    unittest.main()
