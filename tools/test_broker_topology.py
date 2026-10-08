import importlib.util
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("broker_provision", ROOT / "deploy/rabbitmq/provision.py")
broker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(broker)


class BrokerTopologyTests(unittest.TestCase):
    def setUp(self):
        self.topology = broker.definitions({name: "test-value" for name in broker.PASSWORDS})

    def test_bindings_match_every_contract_subscription(self):
        events = re.findall(r"\| `((?:user|tamagotchi|battle|guild|registry|map|raid)\.[^`]+\.v1)` \| ([^|]+) \| ([^|]+) \|", (ROOT / "README.md").read_text())
        publishers = {"User Management": "user-management.events", "Tamagotchi": "tamagotchi.events", "Battle": "battle.events", "Guild": "guild.events", "Package Registry": "package-registry.events", "Map": "map.events", "Monster Raid": "monster-raid.events"}
        consumers = {"Tamagotchi": "tamagotchi", "Package Registry": "package-registry", "Monster Raid": "monster-raid", "Notification": "notification", "audit": "audit"}
        expected = {(publishers[owner.strip()], consumers[consumer.strip()] + ".work", event) for event, owner, destinations in events for consumer in destinations.split(",")}
        actual = {(item["source"], item["destination"], item["routing_key"]) for item in self.topology["bindings"]}
        self.assertEqual(15, len(expected))
        self.assertEqual(expected, actual)
        self.assertEqual(7, len(self.topology["exchanges"]))
        self.assertFalse(any(q["name"].startswith("map.") for q in self.topology["queues"]))

    def test_queues_retry_safely_and_accounts_have_only_required_permissions(self):
        for queue in self.topology["queues"]:
            self.assertTrue(queue["durable"])
            args = queue["arguments"]
            self.assertEqual("quorum", args["x-queue-type"])
            if ".retry." in queue["name"]:
                self.assertEqual(5000 if queue["name"].endswith("5s") else 30000, args["x-message-ttl"])
                self.assertEqual("", args["x-dead-letter-exchange"])
                self.assertEqual(queue["name"].split(".retry.")[0] + ".work", args["x-dead-letter-routing-key"])
                self.assertEqual("reject-publish", args["x-overflow"])
        self.assertEqual("at-least-once", self.topology["policies"][0]["definition"]["dead-letter-strategy"])
        permissions = {item["user"]: item for item in self.topology["permissions"]}
        self.assertIsNone(re.fullmatch(permissions["map"]["write"], "amq.default"))
        self.assertIsNotNone(re.fullmatch(permissions["map"]["write"], "map.events"))
        self.assertIsNone(re.fullmatch(permissions["map"]["write"], "guild.events"))
        self.assertIsNotNone(re.fullmatch(permissions["monster-raid"]["write"], "amq.default"))
        self.assertIsNotNone(re.fullmatch(permissions["monster-raid"]["read"], "package-registry.events"))
        self.assertIsNone(re.fullmatch(permissions["notification"]["write"], "map.events"))
        self.assertEqual({*broker.PUBLISHERS, "notification"}, {item["name"] for item in self.topology["users"]})


if __name__ == "__main__":
    unittest.main()
