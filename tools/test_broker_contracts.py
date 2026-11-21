import copy
import json
from pathlib import Path
import tempfile
import unittest

from jsonschema.exceptions import ValidationError
from check_broker_contracts import ROOT, check, compile_types, load_json, validate


class BrokerContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.schema = load_json(ROOT / 'contracts/operations.schema.json')
        cls.types = compile_types()

    def manifest(self):
        return {'schema_version': '1.0.0', 'events': [], 'subscriptions': [], 'operations': [{
            'name': 'map.get_location', 'destination': 'map', 'storage': 'DURABLE',
            'failover': 'SAFE_READ', 'idempotency': 'NONE', 'contains_credentials': False,
            'request_max_bytes': 16384, 'reply_max_bytes': 65536,
            'public': {'method': 'GET', 'path': '/v1/location/{userId}', 'access': 'user',
                       'request_headers': ['accept'], 'query_parameters': [],
                       'response_headers': ['content-type', 'etag']},
        }]}

    def verify(self, manifest):
        validate(manifest, self.schema, self.types | {
            'type.googleapis.com/tamagotchi.map.v1.GetLocationRequest',
            'type.googleapis.com/tamagotchi.map.v1.GetLocationReply'})

    def test_actual_catalogue_and_all_schemas_compile(self):
        check()
        self.assertIn('type.googleapis.com/tamagotchi.messaging.v1.CallRequest', self.types)

    def test_public_entry_and_dual_mapping(self):
        manifest = self.manifest()
        self.verify(manifest)
        manifest['operations'][0]['internal'] = {
            'request_type': 'type.googleapis.com/tamagotchi.map.v1.GetLocationRequest',
            'response_type': 'type.googleapis.com/tamagotchi.map.v1.GetLocationReply',
            'callers': [{'service': 'monster-raid', 'scopes': ['raid:read']}]}
        self.verify(manifest)
        manifest['operations'][0]['internal']['request_type'] += 'Missing'
        with self.assertRaisesRegex(ValueError, 'unknown protobuf'):
            self.verify(manifest)

    def test_duplicate_operations_and_normalized_routes_refused(self):
        manifest = self.manifest()
        manifest['operations'].append(copy.deepcopy(manifest['operations'][0]))
        manifest['operations'][1]['reply_max_bytes'] = 100
        with self.assertRaisesRegex(ValueError, 'duplicate operation'):
            self.verify(manifest)
        manifest['operations'][1]['name'] = 'map.other'
        manifest['operations'][1]['public']['path'] = '/v1/location/{otherId}'
        with self.assertRaisesRegex(ValueError, 'duplicate public'):
            self.verify(manifest)

    def test_unknown_fields_services_and_unbounded_envelopes_refused(self):
        for field, value in [('destination', 'invented'), ('reply_max_bytes', 1048577), ('surprise', True)]:
            with self.subTest(field=field):
                manifest = self.manifest()
                manifest['operations'][0][field] = value
                with self.assertRaises(ValidationError):
                    self.verify(manifest)

    def test_credentials_and_secret_routes_cannot_be_durable(self):
        for path, flagged in [('/v1/location/{id}', True), ('/v1/users/login', False), ('/v1/devices', False)]:
            manifest = self.manifest()
            entry = manifest['operations'][0]
            entry.update(contains_credentials=flagged, failover='UNSAFE')
            entry['public'].update(method='POST', path=path)
            with self.subTest(path=path), self.assertRaises(ValueError):
                self.verify(manifest)
        entry.update(storage='VOLATILE', contains_credentials=True)
        self.verify(manifest)

    def test_retry_safety_and_natural_idempotency(self):
        manifest = self.manifest()
        entry = manifest['operations'][0]
        entry['failover'] = 'IDEMPOTENT'
        with self.assertRaisesRegex(ValueError, 'enforcement basis'):
            self.verify(manifest)
        entry['idempotency'] = 'NATURAL'
        self.verify(manifest)
        entry['failover'] = 'SAFE_READ'
        with self.assertRaises(ValueError):
            self.verify(manifest)
        entry['idempotency'] = 'NONE'
        entry['public']['method'] = 'POST'
        with self.assertRaises(ValueError):
            self.verify(manifest)

    def test_header_and_path_ambiguities_refused(self):
        for header in ['authorization', 'connection', 'x-user-id', 'idempotency-key', 'x-extra']:
            manifest = self.manifest()
            manifest['operations'][0]['public']['request_headers'] = [header]
            with self.subTest(header=header), self.assertRaises(ValueError):
                self.verify(manifest)
        for path in ['/v1//location', '/v1/../location', '/v1/%2flocation', '/v1/{id}/{id}']:
            manifest = self.manifest()
            manifest['operations'][0]['public']['path'] = path
            with self.subTest(path=path), self.assertRaises(ValueError):
                self.verify(manifest)

    def test_generic_transport_types_cannot_be_domain_operations(self):
        manifest = self.manifest()
        manifest['operations'][0]['internal'] = {
            'request_type': 'type.googleapis.com/tamagotchi.messaging.v1.CallRequest',
            'response_type': 'type.googleapis.com/tamagotchi.messaging.v1.CallReply',
            'callers': [{'service': 'monster-raid', 'scopes': ['raid:read']}]}
        with self.assertRaisesRegex(ValueError, 'destination domain'):
            self.verify(manifest)

    def test_unknown_callers_refused(self):
        manifest = self.manifest()
        manifest['operations'][0]['internal'] = {
            'request_type': 'type.googleapis.com/tamagotchi.map.v1.GetLocationRequest',
            'response_type': 'type.googleapis.com/tamagotchi.map.v1.GetLocationReply',
            'callers': [{'service': 'fake', 'scopes': []}]}
        with self.assertRaises(ValidationError):
            self.verify(manifest)

    def test_events_and_subscriptions_are_explicit(self):
        manifest = self.manifest()
        manifest['events'] = [{'topic': 'map.proximity_detected.v1', 'producer': 'map'}]
        manifest['subscriptions'] = [{'subscriber': 'notification', 'kind': 'LOGICAL', 'topics': ['map.proximity_detected.v1']}]
        self.verify(manifest)
        manifest['subscriptions'][0]['topics'] = ['map.unknown.v1']
        with self.assertRaises(ValueError):
            self.verify(manifest)
        manifest['subscriptions'][0]['topics'] = ['map.proximity_detected.v1']
        manifest['subscriptions'][0]['kind'] = 'INSTANCE'
        with self.assertRaises(ValueError):
            self.verify(manifest)

    def test_duplicate_json_keys_refused(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'bad.json'
            path.write_text('{"schema_version": "1.0.0", "schema_version": "other"}')
            with self.assertRaisesRegex(ValueError, 'duplicate JSON'):
                load_json(path)
