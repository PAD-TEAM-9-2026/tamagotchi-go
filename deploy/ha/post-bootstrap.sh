#!/bin/sh
set -eu
# Bootstrap precedes standby creation. Only initial DDL uses local commit;
# application traffic is gated until both standbys stream and one is synchronous.
export PGHOST=/var/run/postgresql POSTGRES_USER=postgres POSTGRES_DB=postgres
export PGOPTIONS='-c synchronous_commit=local'
/opt/ha/init-service-databases.sh
psql -U postgres -d postgres -v ON_ERROR_STOP=1 \
    -v role="$BROKER_DB_USER" -v password="$BROKER_DB_PASSWORD" -v db="$BROKER_DB_NAME" <<'SQL'
SELECT format('CREATE ROLE %I LOGIN PASSWORD %L', :'role', :'password') \gexec
SELECT format('CREATE DATABASE %I OWNER %I', :'db', :'role') \gexec
SELECT format('REVOKE CONNECT ON DATABASE %I FROM PUBLIC', :'db') \gexec
SELECT format('GRANT CONNECT ON DATABASE %I TO %I', :'db', :'role') \gexec
SQL
