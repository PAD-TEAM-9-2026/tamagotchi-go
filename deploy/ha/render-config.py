"""Render private Patroni YAML-compatible JSON; never print credentials."""
import json
import os
from pathlib import Path


def configuration(env):
    def required(name):
        value = env.get(name, '')
        if not value.strip():
            raise ValueError('Missing setting: ' + name)
        return value
    name = required('PATRONI_NAME')
    if name not in {'pg-1', 'pg-2', 'pg-3'}:
        raise ValueError('Invalid Patroni node name')
    return {
        'scope': 'tamagotchi-ha', 'namespace': '/service/', 'name': name,
        'restapi': {'listen': '0.0.0.0:8008', 'connect_address': name + ':8008',
                    'authentication': {'username': 'patroni', 'password': required('HA_API_PASSWORD')}},
        'etcd3': {'hosts': 'etcd:2379'},
        'bootstrap': {
            'dcs': {'ttl': 20, 'loop_wait': 5, 'retry_timeout': 5,
                    'synchronous_mode': True, 'synchronous_mode_strict': True,
                    'synchronous_node_count': 1,
                    'postgresql': {'use_pg_rewind': True, 'use_slots': True,
                                   'parameters': {'wal_level': 'replica', 'hot_standby': 'on',
                                                  'max_prepared_transactions': 64,
                                                  'max_connections': 100,
                                                  'max_wal_senders': 10, 'max_replication_slots': 10,
                                                  'shared_buffers': '128MB',
                                                  'synchronous_commit': 'on',
                                                  'password_encryption': 'scram-sha-256'}}},
            'initdb': [{'encoding': 'UTF8'}, 'data-checksums'],
            'post_bootstrap': '/opt/ha/post-bootstrap.sh',
            'pg_hba': ['local all all trust',
                       'host replication replicator 0.0.0.0/0 scram-sha-256',
                       'host all all 0.0.0.0/0 scram-sha-256'],
        },
        'postgresql': {'listen': '0.0.0.0:5432', 'connect_address': name + ':5432',
                       'data_dir': '/var/lib/postgresql/data/pgdata',
                       'bin_dir': '/usr/lib/postgresql/17/bin',
                       'basebackup': {'checkpoint': 'fast'},
                       'authentication': {
                           'superuser': {'username': 'postgres', 'password': required('POSTGRES_PASSWORD')},
                           'replication': {'username': 'replicator', 'password': required('HA_REPLICATION_PASSWORD')}},
                       'parameters': {'unix_socket_directories': '/var/run/postgresql'}},
    }


if __name__ == '__main__':
    path = Path('/run/patroni/config.json')
    path.write_text(json.dumps(configuration(os.environ)))
    path.chmod(0o600)
