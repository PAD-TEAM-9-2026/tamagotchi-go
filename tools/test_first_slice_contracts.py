import importlib.util
import unittest

from google.protobuf import descriptor_pool, message_factory
from google.protobuf.descriptor_pb2 import FileDescriptorSet
from pathlib import Path
import subprocess
import sys
import tempfile
import grpc_tools

from check_broker_contracts import ROOT, load_json


class FirstSliceTests(unittest.TestCase):
    def setUp(self):
        self.catalogue = load_json(ROOT / 'contracts/operations.v1.json')

    def test_only_explicit_map_relationship_grant(self):
        entry = next(o for o in self.catalogue['operations'] if o['name'] == 'users.list_relationships')
        self.assertEqual('user-management', entry['destination'])
        self.assertEqual([{'service': 'map', 'scopes': ['users:read-relationships']}], entry['internal']['callers'])
        self.assertNotIn('public', entry)
        mapped = [o for o in self.catalogue['operations'] if o['destination'] == 'map']
        self.assertEqual(4, len(mapped))
        self.assertTrue(all(o['public']['access'] == 'user' for o in mapped))
        self.assertFalse(any('internal' in o for o in mapped))

    def test_ticket_is_typed_gateway_only_volatile_and_unsafe(self):
        ticket = next(o for o in self.catalogue['operations'] if o['name'] == 'guild.issue_chat_ticket')
        self.assertNotIn('public', ticket)
        self.assertEqual(('VOLATILE', 'UNSAFE', 'NONE', True),
                         (ticket['storage'], ticket['failover'], ticket['idempotency'], ticket['contains_credentials']))
        self.assertEqual([{'service': 'gateway', 'scopes': ['guild:issue-chat-ticket']}], ticket['internal']['callers'])
        self.assertEqual('type.googleapis.com/tamagotchi.guild.v1.IssueChatTicketRequest', ticket['internal']['request_type'])
        self.assertEqual('type.googleapis.com/tamagotchi.guild.v1.IssueChatTicketResponse', ticket['internal']['response_type'])

    def test_victoria_public_mappings_keep_credential_and_retry_policy(self):
        operations = self.catalogue['operations']
        self.assertEqual(11, sum(o['destination'] == 'tamagotchi' and 'public' in o for o in operations))
        self.assertEqual(8, sum(o['destination'] == 'notification' for o in operations))
        device = next(o for o in operations if o['name'] == 'notification.register_device')
        self.assertEqual('VOLATILE', device['storage'])
        self.assertTrue(device['contains_credentials'])
        mark = next(o for o in operations if o['name'] == 'notification.mark_read')
        self.assertEqual(('IDEMPOTENT', 'NATURAL'), (mark['failover'], mark['idempotency']))
        self.assertEqual([mark['name']], [o['name'] for o in operations if o['idempotency'] == 'NATURAL'])

    def test_conditional_writes_and_repeat_sensitive_deletes_stay_unsafe_to_retry(self):
        operations = self.catalogue['operations']
        conditional = [o for o in operations if 'if-match' in o.get('public', {}).get('request_headers', [])]
        self.assertEqual(6, len(conditional))
        for entry in conditional:
            self.assertEqual('UNSAFE', entry['failover'], entry['name'])
        delete = next(o for o in operations if o['name'] == 'notification.delete_device')
        self.assertEqual('UNSAFE', delete['failover'])

    def test_all_existing_durable_subscriptions_and_publishers_preserved(self):
        spec = importlib.util.spec_from_file_location('topology', ROOT / 'deploy/rabbitmq/provision.py')
        topology = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(topology)
        actual = {(s['subscriber'], topic) for s in self.catalogue['subscriptions'] for topic in s['topics']}
        expected = {(subscriber, topic) for subscriber, bindings in topology.CONSUMERS.items() for _, topic in bindings}
        self.assertEqual(expected, actual)
        producers = {exchange: ('package-registry' if account == 'registry' else account) for account, exchange in topology.PUBLISHERS.items()}
        self.assertEqual({topic: producers[exchange] for bindings in topology.CONSUMERS.values() for exchange, topic in bindings}, {e['topic']: e['producer'] for e in self.catalogue['events']})
        notifications = next(s for s in self.catalogue['subscriptions'] if s['subscriber'] == 'notification')
        self.assertEqual(6, len(notifications['topics']))
        self.assertNotIn('raid.completed.v1', notifications['topics'])

    def test_compiled_presence_and_transport_numbers(self):
        base = ROOT / 'contracts/proto'
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / 'schema.pb'
            subprocess.run([sys.executable, '-m', 'grpc_tools.protoc', f'-I{base}', f"-I{Path(grpc_tools.__file__).parent / '_proto'}", '--include_imports', f'--descriptor_set_out={output}', *map(str, sorted(base.rglob('*.proto')))], check=True)
            descriptors = FileDescriptorSet.FromString(output.read_bytes())
        pool = descriptor_pool.DescriptorPool()
        for file in descriptors.file:
            pool.Add(file)
        reply_type = message_factory.GetMessageClass(pool.FindMessageTypeByName('tamagotchi.user_management.v1.ListRelationshipsReply'))
        reply = reply_type()
        self.assertFalse(reply.HasField('next_cursor'))
        reply.next_cursor = ''
        self.assertTrue(reply_type.FromString(reply.SerializeToString()).HasField('next_cursor'))
        self.assertEqual(9, pool.FindMessageTypeByName('tamagotchi.user_management.v1.ListRelationshipsRequest').fields_by_name['user_id'].type)
        outcome = pool.FindEnumTypeByName('tamagotchi.messaging.v1.DeliveryReply.Outcome')
        self.assertEqual({'OUTCOME_UNSPECIFIED': 0, 'APPLIED': 1, 'DUPLICATE': 2, 'RETRYABLE_FAILURE': 3, 'PERMANENT_FAILURE': 4, 'DEFERRED': 5}, {v.name: v.number for v in outcome.values})
        transport = pool.FindMessageTypeByName('tamagotchi.messaging.v1.CallRequest')
        self.assertEqual({'message_id': 1, 'destination': 2, 'operation': 3, 'context': 4, 'idempotency_key': 5, 'public_http': 6, 'internal_request': 7}, {f.name: f.number for f in transport.fields})
