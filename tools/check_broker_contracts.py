#!/usr/bin/env python3
"""Compile broker schemas and check the executable, fail-closed operation catalogue."""
from __future__ import annotations

import json
from pathlib import Path
import re
import subprocess
import sys
import tempfile

from google.protobuf.descriptor_pb2 import FileDescriptorSet
import grpc_tools
from jsonschema import Draft202012Validator
from jsonschema.exceptions import ValidationError, SchemaError

ROOT = Path(__file__).resolve().parents[1]
FORBIDDEN_HEADERS = frozenset({
    'authorization', 'x-service-secret', 'x-gateway-assertion', 'x-gateway-context',
    'x-broker-assertion', 'x-user-id', 'x-user-roles', 'x-service-name', 'cookie',
    'set-cookie', 'host', 'content-length', 'connection', 'keep-alive',
    'proxy-authorization', 'proxy-authenticate', 'te', 'trailer',
    'transfer-encoding', 'upgrade', 'idempotency-key', 'x-request-deadline-ms',
    'x-correlation-id',
})
REQUEST_HEADERS = {'accept', 'content-type', 'if-match', 'if-none-match'}
RESPONSE_HEADERS = {'content-type', 'etag', 'retry-after', 'www-authenticate', 'location', 'cache-control', 'vary'}
PATH = re.compile(r'/v1(?:/(?:[a-zA-Z0-9_-]+|\{[a-zA-Z][a-zA-Z0-9_]*\}))+\Z')


def load_json(path):
    def unique(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise ValueError(f'duplicate JSON key: {key}')
            result[key] = value
        return result
    return json.loads(path.read_text(), object_pairs_hook=unique)


def compile_types(root=ROOT):
    base = root / 'contracts/proto'
    with tempfile.TemporaryDirectory() as temporary:
        output = Path(temporary) / 'contracts.pb'
        subprocess.run([
            sys.executable, '-m', 'grpc_tools.protoc', f'-I{base}',
            f'-I{Path(grpc_tools.__file__).parent / '_proto'}', '--include_imports',
            f'--descriptor_set_out={output}',
            *[str(p) for p in sorted(base.rglob('*.proto'))],
        ], check=True)
        descriptors = FileDescriptorSet.FromString(output.read_bytes())
    types = set()
    def visit(prefix, message):
        name = prefix + '.' + message.name
        types.add('type.googleapis.com/' + name)
        for nested in message.nested_type:
            visit(name, nested)
    for file in descriptors.file:
        for message in file.message_type:
            visit(file.package, message)
    return types


def validate(manifest, schema, types):
    Draft202012Validator.check_schema(schema)
    Draft202012Validator(schema).validate(manifest)
    names, routes = set(), set()
    for entry in manifest['operations']:
        name = entry['name']
        if name in names:
            raise ValueError(f'duplicate operation: {name}')
        names.add(name)
        if entry['contains_credentials'] and entry['storage'] != 'VOLATILE':
            raise ValueError(f'credentials require volatile storage: {name}')
        if entry['failover'] == 'IDEMPOTENT' and entry['idempotency'] == 'NONE':
            raise ValueError(f'idempotent failover needs an enforcement basis: {name}')
        if entry['failover'] == 'SAFE_READ' and entry['idempotency'] != 'NONE':
            raise ValueError(f'safe read cannot require a mutation key: {name}')
        public = entry.get('public')
        if public:
            path = public['path']
            if not PATH.fullmatch(path):
                raise ValueError(f'noncanonical path template: {name}')
            parameters = re.findall(r'\{([^}]+)\}', path)
            if len(parameters) != len(set(parameters)):
                raise ValueError(f'duplicate path parameter: {name}')
            route = (entry['destination'], public['method'], re.sub(r'\{[^}]+\}', '{}', path))
            if route in routes:
                raise ValueError(f'duplicate public mapping: {name}')
            routes.add(route)
            for field, allowed in [('request_headers', REQUEST_HEADERS), ('response_headers', RESPONSE_HEADERS)]:
                if set(public[field]) - allowed or set(public[field]) & FORBIDDEN_HEADERS:
                    raise ValueError(f'forbidden {field}: {name}')
            if entry['failover'] == 'SAFE_READ' and public['method'] not in {'GET', 'HEAD', 'OPTIONS'}:
                raise ValueError(f'mutation cannot be a safe read: {name}')
            sensitive = path.endswith(('/login', '/register', '/refresh', '/logout', '/service-tokens', '/chat-tickets')) or (public['method'] == 'POST' and path == '/v1/devices')
            if sensitive and (entry['storage'] != 'VOLATILE' or not entry['contains_credentials']):
                raise ValueError(f'credential route requires explicit volatile policy: {name}')
        internal = entry.get('internal')
        if internal:
            if not internal['callers']:
                raise ValueError(f'internal mapping needs a caller: {name}')
            if any(internal[field] not in types for field in ['request_type', 'response_type']):
                raise ValueError(f'unknown protobuf type: {name}')
            prefix = 'type.googleapis.com/tamagotchi.' + entry['destination'].replace('-', '_') + '.v'
            if any(not internal[field].startswith(prefix) for field in ['request_type', 'response_type']):
                raise ValueError(f'internal types must belong to the destination domain: {name}')
            callers = [c['service'] for c in internal['callers']]
            if len(callers) != len(set(callers)):
                raise ValueError(f'duplicate caller: {name}')
    topics = [event['topic'] for event in manifest['events']]
    if len(topics) != len(set(topics)):
        raise ValueError('duplicate event topic')
    subscriptions = set()
    for subscription in manifest['subscriptions']:
        key = (subscription['subscriber'], subscription['kind'])
        if key in subscriptions:
            raise ValueError('duplicate subscription')
        subscriptions.add(key)
        if not subscription['topics'] or set(subscription['topics']) - set(topics):
            raise ValueError('subscription references unknown/empty topics')
        if subscription['kind'] == 'INSTANCE' and subscription['subscriber'] != 'guild':
            raise ValueError('only Guild relay uses per-instance subscriptions')


def check(root=ROOT):
    manifest = load_json(root / 'contracts/operations.v1.json')
    schema = load_json(root / 'contracts/operations.schema.json')
    validate(manifest, schema, compile_types(root))
    return len(manifest['operations'])


if __name__ == '__main__':
    try:
        count = check()
    except (ValueError, OSError, subprocess.CalledProcessError, ValidationError, SchemaError) as error:
        sys.exit(f'Broker contract check failed: {error}')
    print(f'Broker contracts compiled; {count} operation mappings validated.')
