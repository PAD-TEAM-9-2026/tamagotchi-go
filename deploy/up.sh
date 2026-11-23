#!/bin/sh
set -eu

cd "$(dirname "$0")"

OVERLAYS="ha cache warehouse monitoring tracing demo"

usage() {
    cat >&2 <<'TEXT'
Usage: ./up.sh lab5 [--only NAME[,NAME...]] [COMMAND...]

Runs compose.yaml with the Lab 5 overlays, in this order: ha, cache, warehouse,
monitoring, tracing, demo. Without --only, every overlay present in this checkout
is used. COMMAND goes to docker compose and defaults to "up -d --wait", for
example: ps, logs -f tamagotchi, down. "files" prints the compose files instead.
TEXT
    exit 2
}

[ "${1:-}" = lab5 ] || usage
shift

only=
if [ "${1:-}" = --only ]; then
    [ -n "${2:-}" ] || usage
    only=$2
    shift 2
fi

files=compose.yaml
if [ -z "$only" ]; then
    for name in $OVERLAYS; do
        if [ -f "compose.$name.yaml" ]; then
            files="$files compose.$name.yaml"
        fi
    done
else
    selected=" $(printf '%s' "$only" | tr ',' ' ') "
    for name in $selected; do
        case " $OVERLAYS " in
            *" $name "*) ;;
            *) echo "unknown overlay: $name (choose from: $OVERLAYS)" >&2; exit 2 ;;
        esac
        if [ ! -f "compose.$name.yaml" ]; then
            echo "compose.$name.yaml is not in this checkout yet" >&2
            exit 2
        fi
    done
    for name in $OVERLAYS; do
        case "$selected" in
            *" $name "*) files="$files compose.$name.yaml" ;;
        esac
    done
fi

if [ $# -eq 0 ]; then
    set -- up -d --wait
fi

if [ "$1" = files ]; then
    echo "$files"
    exit 0
fi

case " $files " in
    *" compose.ha.yaml "*)
        version=$(docker compose version --short)
        version=${version#v}
        major=${version%%.*}
        rest=${version#*.}
        minor=${rest%%.*}
        if [ "$major" -lt 2 ] || { [ "$major" -eq 2 ] && [ "$minor" -lt 24 ]; }; then
            echo "compose.ha.yaml needs Docker Compose 2.24 or newer, found $version" >&2
            exit 2
        fi
        ;;
esac

if [ ! -f .env ]; then
    echo "deploy/.env is missing: cp .env.example .env and fill in every empty value" >&2
    exit 2
fi

# The HA overlay turns the single `database` off, so the ETL service reads the service databases
# from the HA read-only port instead (contracts/database-ha.md), unless .env chooses a source.
uses() {
    case " $files " in *" compose.$1.yaml "*) return 0 ;; esac
    return 1
}
if uses ha && uses warehouse && ! grep -Eq '^ETL_SOURCE_HOST=.+' .env; then
    export ETL_SOURCE_HOST="${ETL_SOURCE_HOST:-pg-haproxy}"
    export ETL_SOURCE_PORT="${ETL_SOURCE_PORT:-5001}"
fi

set -- $(for file in $files; do printf -- '-f %s ' "$file"; done) "$@"
exec docker compose "$@"
