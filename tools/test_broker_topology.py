import json
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CATALOGUE = json.loads((ROOT / "contracts/operations.v1.json").read_text())
PRODUCERS = {
    "user-management": "User Management",
    "tamagotchi": "Tamagotchi",
    "battle": "Battle",
    "guild": "Guild",
    "package-registry": "Package Registry",
    "map": "Map",
    "monster-raid": "Monster Raid",
}
SUBSCRIBERS = {
    "tamagotchi": "Tamagotchi",
    "package-registry": "Package Registry",
    "monster-raid": "Monster Raid",
    "notification": "Notification",
    "audit": "audit",
}


class EventTableTests(unittest.TestCase):
    """The README event table says the same as the operation catalogue the broker routes by."""

    def test_every_logical_subscription_is_in_the_readme_event_table(self):
        rows = re.findall(
            r"\| `((?:user|tamagotchi|battle|guild|registry|map|raid)\.[^`]+\.v1)` \| ([^|]+) \| ([^|]+) \|",
            (ROOT / "README.md").read_text(),
        )
        documented = {
            (topic, owner.strip(), consumer.strip())
            for topic, owner, consumers in rows
            for consumer in consumers.split(",")
        }
        producers = {event["topic"]: event["producer"] for event in CATALOGUE["events"]}
        routed = {
            (topic, PRODUCERS[producers[topic]], SUBSCRIBERS[subscription["subscriber"]])
            for subscription in CATALOGUE["subscriptions"]
            if subscription["kind"] == "LOGICAL"
            for topic in subscription["topics"]
        }
        self.assertEqual(15, len(routed))
        self.assertEqual(routed, documented)

    def test_every_event_has_a_subscriber_so_publish_is_never_unroutable(self):
        subscribed = {topic for subscription in CATALOGUE["subscriptions"] for topic in subscription["topics"]}
        self.assertEqual({event["topic"] for event in CATALOGUE["events"]}, subscribed)


if __name__ == "__main__":
    unittest.main()
