# Database connection and failover (proposal)

Status: proposed. Validate each owner's candidate before replacing its release
image. The HA overlay changes database routing, not database ownership or grants.

## Topology

PostgreSQL17/PostGIS, one Patroni primary and two standbys, one etcd and HAProxy.
All eight domain databases and Broker's database are replicated in the same
cluster with separate owner roles. It demonstrates database failover, not full
infrastructure HA: one proxy, one DCS and one laptop remain failure domains.

Internal writer endpoint is pg-haproxy:5000. Reader is pg-haproxy:5001; only ETL
and explicitly stale-tolerant reads use it. Normal operations use the writer.
Existing connection formats and credentials remain supported. The overlay uses
fresh, project-scoped volumes; it never attaches the existing acceptance volumes.

Patroni uses ttl20/loop_wait5/retry_timeout5, checksums/pg_rewind, synchronous_mode,
synchronous_mode_strict, one synchronous standby and synchronous_commit=on.
max_prepared_transactions=64 is consistent on all nodes. With no eligible sync
standby, writes block and must be bounded by clients; availability is deliberately
traded for replicated acknowledgements. No unconditional zero-loss claim.

HAProxy checks /primary and /replica. Its writer backend uses inter3s/fall3/rise2
and on-marked-down shutdown-sessions. Test an old primary that goes silent, not
only a clean shutdown. There is no automatic replay of uncertain mutations.

## Service behavior

Connection refusal/reset/timeout, SQLSTATE08xxx, 57P01, 57P03 and 25006 become
503 database_unavailable, Retry-After:1. Work and probe waits obey their budgets;
/status reports DOWN promptly. No crash loop or silent loss of an uncertain
committed effect. Pool recovery must work after leadership changes.

Npgsql starts with Timeout=2;Command Timeout=3 plus cancellation. Node and JDBC
owners configure equivalent bounded connection/query/acquisition waits. A driver
timeout is not permission to extend the inherited request deadline.

## Bootstrap and prepared work

Patroni's post_bootstrap runs service-role/schema initialization and creates the
Broker DB. Only first-start DDL uses synchronous_commit=local to avoid waiting on
standbys that cannot clone until initialization finishes. App startup is gated on
two streaming standbys and one synchronous standby. No domain writes use this
bootstrap exception. Existing-volume schema upgrades remain owner-reviewed.

A prepared transaction survives the session. Only its owner participant resolves
its GIDs. An unavailable coordinator is not ABORT. Observe and alert on prepared
work older than60s; do not erase it. Keep credentials and SQL out of routine logs.

## Acceptance

Build the image natively, validate Compose dependency replacement, initialize
all owner DBs and PostGIS, test writer/reader routing, stop/pause the leader,
verify acknowledged data and prepared state, resolve it, restart the old leader
and verify standby rejoin. Record measured outage, temporary503s and recovery.
Owner service failover acceptance and final published-image acceptance are
separate from cluster/component checks.
