#!/bin/sh
set -eu

create_service_database() {
    psql --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
        -v ON_ERROR_STOP=1 -v service_role="$1" -v service_password="$2" -v service_db="$3" <<'SQL'
SELECT format('CREATE ROLE %I LOGIN PASSWORD %L', :'service_role', :'service_password') \gexec
SELECT format('CREATE DATABASE %I OWNER %I', :'service_db', :'service_role') \gexec
SELECT format('REVOKE CONNECT ON DATABASE %I FROM PUBLIC', :'service_db') \gexec
SELECT format('GRANT CONNECT ON DATABASE %I TO %I', :'service_db', :'service_role') \gexec
SQL
}

create_service_database "$USER_MANAGEMENT_DB_USER" "$USER_MANAGEMENT_DB_PASSWORD" "$USER_MANAGEMENT_DB_NAME"
create_service_database "$TAMAGOTCHI_DB_USER" "$TAMAGOTCHI_DB_PASSWORD" "$TAMAGOTCHI_DB_NAME"
create_service_database "$BATTLE_DB_USER" "$BATTLE_DB_PASSWORD" "$BATTLE_DB_NAME"
create_service_database "$GUILD_DB_USER" "$GUILD_DB_PASSWORD" "$GUILD_DB_NAME"
create_service_database "$REGISTRY_DB_USER" "$REGISTRY_DB_PASSWORD" "$REGISTRY_DB_NAME"
create_service_database "$MAP_DB_USER" "$MAP_DB_PASSWORD" "$MAP_DB_NAME"
create_service_database "$MONSTER_RAID_DB_USER" "$MONSTER_RAID_DB_PASSWORD" "$MONSTER_RAID_DB_NAME"
create_service_database "$NOTIFICATION_DB_USER" "$NOTIFICATION_DB_PASSWORD" "$NOTIFICATION_DB_NAME"

psql --username "$USER_MANAGEMENT_DB_USER" --dbname "$USER_MANAGEMENT_DB_NAME" -v ON_ERROR_STOP=1 \
    -f /service-schema/user-management.sql
psql --username "$BATTLE_DB_USER" --dbname "$BATTLE_DB_NAME" -v ON_ERROR_STOP=1 \
    -f /service-schema/battle.sql

psql --username "$POSTGRES_USER" --dbname "$MAP_DB_NAME" -v ON_ERROR_STOP=1 \
    -c 'CREATE EXTENSION IF NOT EXISTS postgis'
