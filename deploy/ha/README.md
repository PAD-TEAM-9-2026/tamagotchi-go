# Database failover development

This optional overlay uses a new project and fresh volumes. It does not migrate
an existing database. Keep the released stack and its volumes as a reference.
All eight service databases and Broker's database keep separate owner roles.

Copy deploy/.env.example to a private environment file and fill its required
settings. Additionally set HA_API_PASSWORD and HA_REPLICATION_PASSWORD. Never
commit that file or print resolved Compose configuration. Compose must support
`!override` (2.24.4 or newer).

From deploy/:

```sh
docker compose --env-file /private/path/ha.env -p tamagotchi-go-ha-dev \
  -f compose.yaml -f compose.ha.yaml config --quiet
docker compose --env-file /private/path/ha.env -p tamagotchi-go-ha-dev \
  -f compose.yaml -f compose.ha.yaml build pg-1
docker compose --env-file /private/path/ha.env -p tamagotchi-go-ha-dev \
  -f compose.yaml -f compose.ha.yaml up -d --no-deps \
  etcd pg-1 pg-2 pg-3 pg-haproxy ha-ready
```

The postgres-ha image is a local candidate, not a published release. It installs
PostGIS on a multi-platform PostgreSQL17 base and uses hash-locked Patroni
packages. Native builds and final published architectures are separate checks.

The completed ha-ready job proves two streaming standbys and one synchronous
standby, not owner service acceptance. Before starting application services,
prepare their normal signing/mTLS material, select compatible owner candidates
and follow the existing setup. Do not claim the published previous-generation
images implement new participant or failover behavior.

Internal connections: pg-haproxy:5000 for writes and normal reads; port5001 only
for stale-tolerant reads/ETL. Host ports default to loopback15435/15436 and can be
changed using HA_RW_PORT/HA_RO_PORT. Patroni and etcd are internal only.

Demonstrate clean loss and silent primary loss separately:

```sh
python3 ha/failover-demo.py --env-file /private/path/ha.env \
  --project tamagotchi-go-ha-dev --fault stop
python3 ha/failover-demo.py --env-file /private/path/ha.env \
  --project tamagotchi-go-ha-dev --fault pause
```

The bounded script creates its own named infrastructure fixture, commits one
row, prepares a second, breaks the real primary and verifies both survive. It
then commits prepared work, restores the old node and removes only that fixture.
Pause also verifies HAProxy closes an already connected session. Run it only on
a disposable development project with no concurrent test suites. Failure paths
attempt restoration and never delete volumes; inspect the report and cluster
before rerunning an interrupted script.

One etcd, HAProxy and host remain failure domains. Strict synchronous replication
blocks writes when no eligible sync standby exists; clients must enforce their
budgets. Prepared transactions retain locks until resolved. See
[the HA contract](../../contracts/database-ha.md).
