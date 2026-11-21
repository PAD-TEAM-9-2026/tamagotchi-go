#!/bin/sh
set -eu

# This container sees only log volumes, never service databases or Docker's socket.
reader_gid=${LOG_READER_GID:-20000}
case "$reader_gid" in ''|*[!0-9]*) exit 2;; esac
[ "$reader_gid" -gt 0 ] || exit 2

for spec in gateway:GATEWAY_LOG_UID service-discovery:DISCOVERY_LOG_UID map:MAP_LOG_UID message-broker:BROKER_LOG_UID user-management:USER_MANAGEMENT_LOG_UID battle:BATTLE_LOG_UID guild:GUILD_LOG_UID package-registry:REGISTRY_LOG_UID tamagotchi:TAMAGOTCHI_LOG_UID notification:NOTIFICATION_LOG_UID monster-raid:MONSTER_RAID_LOG_UID; do
    service=${spec%%:*}
    variable=${spec#*:}
    uid=$(printenv "$variable")
    case "$uid" in ''|*[!0-9]*) exit 2;; esac
    [ "$uid" -gt 0 ] || exit 2
    directory=/owned/$service
    [ -d "$directory" ] && [ ! -L "$directory" ] || exit 2
    chown "$uid:$reader_gid" "$directory"
    chmod 2750 "$directory"
done
