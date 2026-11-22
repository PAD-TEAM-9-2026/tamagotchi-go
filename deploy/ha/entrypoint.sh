#!/bin/sh
set -eu
umask 077
install -d -o postgres -g postgres /run/patroni /var/lib/postgresql/data /var/run/postgresql
/opt/patroni/bin/python /opt/ha/render-config.py
chown postgres:postgres /run/patroni/config.json
exec gosu postgres /opt/patroni/bin/patroni /run/patroni/config.json
