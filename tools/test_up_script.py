from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]


class UpScriptTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.deploy = Path(self.temp.name)
        shutil.copy(ROOT / 'deploy/up.sh', self.deploy / 'up.sh')
        for name in ('compose.yaml', 'compose.cache.yaml', 'compose.tracing.yaml', 'compose.demo.yaml',
                     'compose.ha.yaml'):
            (self.deploy / name).write_text('services: {}\n')

    def tearDown(self):
        self.temp.cleanup()

    def run_up(self, *args):
        return subprocess.run(['sh', str(self.deploy / 'up.sh'), *args], capture_output=True, text=True)

    def test_uses_every_overlay_present_in_a_fixed_order(self):
        result = self.run_up('lab5', 'files')

        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(result.stdout.split(),
                         ['compose.yaml', 'compose.ha.yaml', 'compose.cache.yaml', 'compose.tracing.yaml',
                          'compose.demo.yaml'])

    def test_only_keeps_the_fixed_order_and_drops_repeats(self):
        result = self.run_up('lab5', '--only', 'demo,cache,demo', 'files')

        self.assertEqual(result.stdout.split(), ['compose.yaml', 'compose.cache.yaml', 'compose.demo.yaml'])

    def test_refuses_an_unknown_or_missing_overlay(self):
        unknown = self.run_up('lab5', '--only', 'redis', 'files')
        missing = self.run_up('lab5', '--only', 'warehouse', 'files')

        self.assertEqual(unknown.returncode, 2)
        self.assertIn('unknown overlay: redis', unknown.stderr)
        self.assertEqual(missing.returncode, 2)
        self.assertIn('compose.warehouse.yaml is not in this checkout yet', missing.stderr)

    def test_needs_a_target_and_a_value_for_only(self):
        self.assertEqual(self.run_up().returncode, 2)
        self.assertEqual(self.run_up('lab4').returncode, 2)
        self.assertEqual(self.run_up('lab5', '--only').returncode, 2)

    def test_stops_without_an_env_file(self):
        result = self.run_up('lab5', '--only', 'cache', 'ps')

        self.assertEqual(result.returncode, 2)
        self.assertIn('deploy/.env is missing', result.stderr)

    def test_demo_overlay_turns_on_both_fault_switches(self):
        demo = (ROOT / 'deploy/compose.demo.yaml').read_text()

        self.assertIn('  tamagotchi:\n    environment:\n      FAULT_INJECTION_ENABLED: "true"', demo)
        self.assertIn('USER_MANAGEMENT_JWKS_URL: http://user-management:3001/v1/jwks', demo)
        self.assertIn('  user-management:\n    environment:\n      FAULT_INJECTION_ENABLED: "true"', demo)
        self.assertNotIn('FAULT_INJECTION_ENABLED', (ROOT / 'deploy/compose.yaml').read_text())


if __name__ == '__main__':
    unittest.main()
