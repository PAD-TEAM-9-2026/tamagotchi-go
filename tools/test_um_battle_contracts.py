import unittest

from check_broker_contracts import ROOT, load_json

CREDENTIAL_ROUTES = {'users.login', 'users.refresh', 'users.logout', 'users.register'}
NEVER_RESENT = {'users.login', 'users.refresh', 'users.logout', 'users.mark_enemy', 'users.remove_enemy', 'users.remove_friend'}
COMMANDS = {
    'users.register', 'users.join_package', 'users.create_friend_request', 'users.accept_friend_request',
    'users.reject_friend_request', 'battle.create_battle', 'battle.accept_battle', 'battle.reject_battle',
    'battle.attack', 'battle.forfeit_battle',
}
READS = {
    'users.get_me', 'users.get_profile', 'users.list_my_boosts', 'users.list_friend_requests',
    'users.get_global_wallet', 'users.get_local_wallet', 'battle.list_battles', 'battle.get_battle',
}
PAGES = {'users.list_my_boosts', 'users.list_friend_requests', 'battle.list_battles'}
CREATED = {'users.register', 'users.create_friend_request', 'battle.create_battle'}


class UserManagementAndBattlePublicRoutesTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        manifest = load_json(ROOT / 'contracts/operations.v1.json')
        cls.ops = {o['name']: o for o in manifest['operations']}
        cls.slice = {
            name: o for name, o in cls.ops.items()
            if name in CREDENTIAL_ROUTES | NEVER_RESENT | COMMANDS | READS
        }

    def test_every_route_of_the_slice_is_public_only(self):
        self.assertEqual(len(CREDENTIAL_ROUTES | NEVER_RESENT | COMMANDS | READS), len(self.slice))
        for name, o in self.slice.items():
            self.assertIn('public', o, name)
            self.assertNotIn('internal', o, name)
            self.assertEqual(name.split('.')[0], {'user-management': 'users', 'battle': 'battle'}[o['destination']], name)

    def test_credential_routes_are_anonymous_volatile_and_never_stored(self):
        for name in CREDENTIAL_ROUTES:
            o = self.slice[name]
            self.assertEqual('anonymous', o['public']['access'], name)
            self.assertEqual('VOLATILE', o['storage'], name)
            self.assertTrue(o['contains_credentials'], name)
            self.assertEqual(16384, o['reply_max_bytes'], name)
        others = set(self.slice) - CREDENTIAL_ROUTES
        self.assertTrue(all(self.slice[n]['public']['access'] == 'user' for n in others))
        self.assertTrue(all(self.slice[n]['storage'] == 'DURABLE' and not self.slice[n]['contains_credentials'] for n in others))

    def test_a_removal_that_answers_404_when_repeated_is_never_resent(self):
        for name in NEVER_RESENT:
            self.assertEqual(('UNSAFE', 'NONE'), (self.slice[name]['failover'], self.slice[name]['idempotency']), name)

    def test_commands_with_an_idempotency_key_may_fail_over(self):
        for name in COMMANDS:
            o = self.slice[name]
            self.assertEqual(('IDEMPOTENT', 'COMMAND_KEY'), (o['failover'], o['idempotency']), name)
            self.assertEqual('POST', o['public']['method'], name)
            self.assertEqual(['accept', 'content-type'], o['public']['request_headers'], name)

    def test_reads_are_safe_and_pages_take_only_limit_and_cursor(self):
        for name in READS:
            o = self.slice[name]
            self.assertEqual(('SAFE_READ', 'NONE', 'GET'), (o['failover'], o['idempotency'], o['public']['method']), name)
            self.assertEqual(['accept'], o['public']['request_headers'], name)
            self.assertEqual(['limit', 'cursor'] if name in PAGES else [], o['public']['query_parameters'], name)

    def test_a_full_page_fits_its_reply_bound(self):
        # 100 items at most; a battle with both sides is under 1 KiB of JSON, a friend request or boost under 400 bytes.
        self.assertGreaterEqual(self.slice['battle.list_battles']['reply_max_bytes'], 100 * 1024 * 2)
        for name in ('users.list_my_boosts', 'users.list_friend_requests'):
            self.assertGreaterEqual(self.slice[name]['reply_max_bytes'], 100 * 400 * 2)

    def test_only_created_resources_keep_the_location_header(self):
        for name, o in self.slice.items():
            self.assertEqual(name in CREATED, 'location' in o['public']['response_headers'], name)

    def test_relationship_listing_stays_internal_and_service_tokens_keep_their_mappings(self):
        self.assertNotIn('public', self.ops['users.list_relationships'])
        self.assertEqual('admin', self.ops['users.issue_service_token_as_admin']['public']['access'])
        self.assertNotIn('public', self.ops['users.issue_service_token'])


if __name__ == '__main__':
    unittest.main()
