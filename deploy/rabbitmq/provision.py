"""Merge the shared broker topology without deleting existing broker data."""
import base64
import json
import os
import re
import time
import urllib.error
import urllib.request

PUBLISHERS = {
    "user-management": "user-management.events", "tamagotchi": "tamagotchi.events",
    "battle": "battle.events", "guild": "guild.events", "registry": "package-registry.events",
    "map": "map.events", "monster-raid": "monster-raid.events",
}
CONSUMERS = {
    "tamagotchi": [("user-management.events", "user.package_joined.v1")],
    "package-registry": [("user-management.events", "user.package_joined.v1")],
    "monster-raid": [("package-registry.events", "registry.occurrence_changed.v1")],
    "notification": [
        ("user-management.events", "user.friend_request_created.v1"),
        ("tamagotchi.events", "tamagotchi.access_granted.v1"),
        ("battle.events", "battle.request_created.v1"),
        ("guild.events", "guild.invitation_created.v1"),
        ("map.events", "map.proximity_detected.v1"),
        ("monster-raid.events", "raid.started.v1"),
    ],
    "audit": [
        ("tamagotchi.events", "tamagotchi.created.v1"),
        ("tamagotchi.events", "tamagotchi.leveled_up.v1"),
        ("battle.events", "battle.completed.v1"),
        ("guild.events", "guild.member_joined.v1"),
        ("guild.events", "guild.member_left.v1"),
        ("monster-raid.events", "raid.completed.v1"),
    ],
}
PASSWORDS = {name: name.upper().replace("-", "_") + "_RABBITMQ_PASSWORD" for name in (*PUBLISHERS, "notification")}


def regex(names):
    return "^(" + "|".join(re.escape(name) for name in sorted(set(names))) + ")$" if names else "^$"


def definitions(passwords):
    queues, bindings, users, permissions = [], [], [], []
    for consumer, subscriptions in CONSUMERS.items():
        work = consumer + ".work"
        suffixes = ["work"] if consumer == "audit" else ["work", "retry.5s", "retry.30s", "dlq"]
        for suffix in suffixes:
            args = {"x-queue-type": "quorum"}
            if suffix == "work":
                args["x-delivery-limit"] = -1
            if suffix.startswith("retry"):
                args.update({"x-message-ttl": 5000 if suffix == "retry.5s" else 30000,
                             "x-dead-letter-exchange": "", "x-dead-letter-routing-key": work,
                             "x-overflow": "reject-publish"})
            queues.append({"name": consumer + "." + suffix, "vhost": "/", "durable": True,
                           "auto_delete": False, "arguments": args})
        bindings.extend({"source": exchange, "vhost": "/", "destination": work,
                         "destination_type": "queue", "routing_key": key, "arguments": {}}
                        for exchange, key in subscriptions)
    for name, password in passwords.items():
        consumer = "package-registry" if name == "registry" else name
        own_queues = [q["name"] for q in queues if q["name"].startswith(consumer + ".")]
        own_exchange = [PUBLISHERS[name]] if name in PUBLISHERS else []
        sources = [exchange for exchange, _ in CONSUMERS.get(consumer, [])]
        users.append({"name": name, "password": password, "tags": []})
        permissions.append({"user": name, "vhost": "/", "configure": regex(own_exchange + own_queues),
                            "write": regex(own_exchange + own_queues + (["amq.default"] if own_queues else [])),
                            "read": regex(own_queues + sources)})
    return {"users": users, "permissions": permissions,
            "exchanges": [{"name": exchange, "vhost": "/", "type": "topic", "durable": True,
                           "auto_delete": False, "internal": False, "arguments": {}} for exchange in PUBLISHERS.values()],
            "queues": queues, "bindings": bindings,
            "policies": [{"name": "shared-retry-delivery", "vhost": "/", "pattern": r"^(tamagotchi|package-registry|monster-raid|notification)\.retry\.(5s|30s)$",
                          "apply-to": "quorum_queues", "priority": 10,
                          "definition": {"dead-letter-strategy": "at-least-once", "overflow": "reject-publish"}}]}


def main():
    required = ["RABBITMQ_USER", "RABBITMQ_PASSWORD", *PASSWORDS.values()]
    missing = [name for name in required if not os.environ.get(name)]
    if missing:
        raise SystemExit("Missing broker settings: " + ", ".join(missing))
    credentials = (os.environ["RABBITMQ_USER"] + ":" + os.environ["RABBITMQ_PASSWORD"]).encode()
    header = "Basic " + base64.b64encode(credentials).decode()
    for attempt in range(30):
        try:
            request = urllib.request.Request("http://rabbitmq:15672/api/overview", headers={"Authorization": header})
            with urllib.request.urlopen(request, timeout=3):
                break
        except (urllib.error.URLError, TimeoutError):
            if attempt == 29:
                raise SystemExit("Broker management is unavailable.") from None
            time.sleep(2)
    body = json.dumps(definitions({name: os.environ[variable] for name, variable in PASSWORDS.items()})).encode()
    request = urllib.request.Request("http://rabbitmq:15672/api/definitions", data=body,
                                     headers={"Authorization": header, "Content-Type": "application/json"}, method="POST")
    try:
        with urllib.request.urlopen(request, timeout=30):
            pass
    except (urllib.error.URLError, TimeoutError):
        raise SystemExit("Broker topology import failed; existing data was retained.") from None
    print("Broker accounts and topology ready.")


if __name__ == "__main__":
    main()
