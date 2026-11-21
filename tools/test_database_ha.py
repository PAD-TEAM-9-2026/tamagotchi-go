"""Guard fail-closed Patroni settings and prepared-work recovery configuration."""
import importlib.util
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('ha_config', ROOT / 'deploy/ha/render-config.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

class DatabaseHaTests(unittest.TestCase):
    def test_missing_or_unknown_node_configuration_is_refused(self):
        for env in ({}, {'PATRONI_NAME':'unexpected'}):
            with self.assertRaises(ValueError):
                module.configuration(env)

    def test_missing_credentials_are_refused(self):
        with self.assertRaises(ValueError):
            module.configuration({'PATRONI_NAME':'pg-1'})

    def test_node_identity_replication_and_prepared_settings(self):
        env = {'PATRONI_NAME':'pg-2', 'HA_API_PASSWORD':'test-api',
               'POSTGRES_PASSWORD':'test-root', 'HA_REPLICATION_PASSWORD':'test-replication'}
        config = module.configuration(env)
        dcs = config['bootstrap']['dcs']
        self.assertTrue(dcs['synchronous_mode'])
        self.assertTrue(dcs['synchronous_mode_strict'])
        self.assertEqual(1, dcs['synchronous_node_count'])
        self.assertEqual(64, dcs['postgresql']['parameters']['max_prepared_transactions'])
        self.assertEqual('pg-2:5432', config['postgresql']['connect_address'])
        self.assertEqual({'checkpoint':'fast'}, config['postgresql']['basebackup'])

    def test_failover_closes_existing_writer_sessions(self):
        text = (ROOT / 'deploy/ha/haproxy.cfg').read_text()
        writer = text.split('listen writer', 1)[1].split('listen reader', 1)[0]
        self.assertIn('on-marked-down shutdown-sessions', writer)
        self.assertIn('GET /primary', writer)
