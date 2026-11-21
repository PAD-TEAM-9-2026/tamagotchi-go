# Structured log volumes

`compose.yaml` gives every service its own log volume and mounts all of them
read-only into the Message Broker, which serves the system log export. The
`log-init` job prepares them before any service starts.

Each `*_LOG_UID` defaults to the runtime user of the pinned image: Gateway and
Discovery 1001, User Management, Battle, Map and Raid 1654, Guild, Registry,
Tamagotchi and Notification 1000, Broker 10001. Override one only when an image
changes its runtime user. `LOG_READER_GID` defaults to
20000 and must be a nonzero numeric group ID.

The one-shot `log-init` job has no network and sees only these log volumes. It
sets each volume root's owner and restricted group traversal/read permissions
(2750, setgid). The initializer retains `FSETID` so the kernel preserves that
bit when the directory group differs from its own groups. New directories/files
inherit the reader group; writers must keep group traversal/read permission and
restrict access for others. It does not recursively change existing files. Each
application mounts only its own log volume writable. Broker receives the other volumes
read-only and its own volume writable under a separate root. No Docker socket or
service database is mounted into the initializer or exporter.

Writers must use the agreed service/instance/date layout, redact before writing
and enforce their own storage/rotation limits. Compose cannot provide file
logging for an image that does not implement it. The Broker's export temporary
filesystem is limited to 64 MiB; exceeding its storage bound must refuse the
export, never truncate it. Existing exporter limits and admin checks still apply.
