"""Disposable identity-generation regressions; never reads deployment secrets."""
import hashlib
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]


class IdentityMaterialTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.seed = tempfile.TemporaryDirectory()
        cls.seed_path = Path(cls.seed.name)
        (cls.seed_path / 'message-broker').mkdir()
        shutil.copyfile(ROOT / 'deploy/message-broker/generate-identities.sh',
                        cls.seed_path / 'message-broker/generate-identities.sh')
        cls.run_generator(cls.seed_path, True)

    @classmethod
    def tearDownClass(cls):
        cls.seed.cleanup()

    @staticmethod
    def run_generator(root, success):
        env = os.environ.copy()
        env.pop('KEY_ID', None)
        env.pop('BROKER_CONTEXT_KEY_ID', None)
        result = subprocess.run(['sh', str(root / 'message-broker/generate-identities.sh')],
                                env=env, capture_output=True)
        if (result.returncode == 0) != success:
            raise AssertionError('unexpected identity-generator exit: ' + str(result.returncode))

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name) / 'deploy'
        shutil.copytree(self.seed_path, self.root)

    def tearDown(self):
        self.temp.cleanup()

    def fingerprint(self):
        return {str(p.relative_to(self.root)): hashlib.sha256(p.read_bytes()).hexdigest()
                for p in self.root.rglob('*') if p.is_file()}

    def refusal_preserves_files(self):
        before = self.fingerprint()
        self.run_generator(self.root, False)
        self.assertEqual(before, self.fingerprint())

    def test_repeated_run_preserves_every_byte(self):
        before = self.fingerprint()
        self.run_generator(self.root, True)
        self.assertEqual(before, self.fingerprint())

    def test_incomplete_workload_pair_refuses(self):
        (self.root / 'secrets/tls/map.crt').unlink()
        self.refusal_preserves_files()

    def test_incomplete_ca_refuses(self):
        (self.root / 'secrets/tls/ca.crt').unlink()
        self.refusal_preserves_files()

    def test_wrong_workload_identity_refuses(self):
        for suffix in ('key', 'crt'):
            shutil.copyfile(self.root / f'secrets/tls/guild.{suffix}',
                            self.root / f'secrets/tls/map.{suffix}')
        self.refusal_preserves_files()

    def test_mismatched_key_refuses(self):
        shutil.copyfile(self.root / 'secrets/tls/guild.key', self.root / 'secrets/tls/map.key')
        self.refusal_preserves_files()

    def test_missing_jwks_refuses(self):
        (self.root / 'secrets/broker-context-jwks.json').unlink()
        self.refusal_preserves_files()

    def test_changed_key_id_refuses(self):
        (self.root / '.env').write_text('BROKER_CONTEXT_KEY_ID=different\n')
        self.refusal_preserves_files()

    def test_mismatched_jwks_refuses(self):
        path = self.root / 'secrets/broker-context-jwks.json'
        path.write_text(path.read_text().replace('"n": "', '"n": "A'))
        self.refusal_preserves_files()
