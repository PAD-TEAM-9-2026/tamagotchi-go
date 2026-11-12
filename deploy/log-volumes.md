# Structured log volumes

This optional overlay adds isolated log volumes and bounded export temporary
storage to a base configuration that already defines Message Broker. It does
not enable broker dispatch, replace images or change database volumes.

Run from `deploy/` with the candidate's private environment file:

```sh
docker compose --env-file /path/to/private/candidate.env \
  -f compose.yaml -f compose.broker.yaml -f compose.logs.yaml config --quiet
docker compose --env-file /path/to/private/candidate.env \
  -f compose.yaml -f compose.broker.yaml -f compose.logs.yaml up -d
```

For owner candidates, explicitly set `USER_MANAGEMENT_LOG_UID`, `BATTLE_LOG_UID`,
`GUILD_LOG_UID`, `REGISTRY_LOG_UID`, `TAMAGOTCHI_LOG_UID` and
`NOTIFICATION_LOG_UID` to their actual non-root runtime UID. Inspect the candidate
Dockerfile/image, not an old image tag. Defaults for Gateway/Discovery (1001),
Map/Raid (1654) and Broker (10001) match their current Dockerfiles; override them
if a reviewed candidate changes its runtime UID. `LOG_READER_GID` defaults to
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
and enforce their own storage/rotation limits. This overlay cannot provide file
logging for an image that does not implement it. Broker's export temporary
filesystem is limited to 64 MiB; exceeding its storage bound must refuse the
export, never truncate it. Existing exporter limits and admin checks still apply.

The overlay is preparation until combined with the reviewed Broker/adapter
candidate and exercised. Do not treat `config --quiet` as live log/export evidence.
