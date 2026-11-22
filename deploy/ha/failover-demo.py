#!/usr/bin/env python3
"""Bounded, isolated database failover proof; never targets the acceptance project."""
import argparse
import json
from pathlib import Path
import select
import subprocess
import time
import uuid

SQL = '''import json,os,sys,psycopg2
r=json.load(sys.stdin)
c=psycopg2.connect(host="pg-haproxy",port=5000,dbname="postgres",user="postgres",password=os.environ["POSTGRES_PASSWORD"],connect_timeout=2,options="-c statement_timeout=5000 -c lock_timeout=2000")
c.autocommit=True
q=c.cursor(); q.execute(r["sql"],r.get("parameters")); print(json.dumps(q.fetchall() if q.description else [],default=str))
c.close()
'''
SESSION = '''import os,sys,time,psycopg2,json
c=psycopg2.connect(host="pg-haproxy",port=5000,dbname="postgres",user="postgres",password=os.environ["POSTGRES_PASSWORD"],connect_timeout=2)
c.autocommit=True
print("READY",flush=True)
sys.stdin.readline(); started=time.monotonic()
try:
 c.cursor().execute("SELECT 1")
 print(json.dumps({"closed":False,"seconds":time.monotonic()-started}),flush=True)
except psycopg2.Error:
 print(json.dumps({"closed":True,"seconds":time.monotonic()-started}),flush=True)
finally:c.close()
'''


def wait_for(check, seconds=120):
    end = time.monotonic() + seconds
    last = None
    while time.monotonic() < end:
        try:
            value = check()
            if value:
                return value
        except (subprocess.SubprocessError, ValueError, KeyError) as error:
            last = type(error).__name__
        time.sleep(2)
    raise RuntimeError('Recovery deadline exceeded; last failure: ' + str(last))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--env-file', required=True, type=Path)
    parser.add_argument('--project', required=True)
    parser.add_argument('--fault', choices=('stop', 'pause'), default='stop')
    parser.add_argument('--report', type=Path)
    args = parser.parse_args()
    if not args.project.startswith('tamagotchi-go-ha-'):
        parser.error('Project must use the isolated tamagotchi-go-ha- prefix')
    deploy = Path(__file__).resolve().parents[1]
    dc = ['docker', 'compose', '--env-file', str(args.env_file.resolve()), '-p', args.project,
          '-f', str(deploy / 'compose.yaml'), '-f', str(deploy / 'compose.ha.yaml')]

    def run(*command, data=None, timeout=20):
        return subprocess.run(dc + list(command), input=data, text=True, capture_output=True,
                              check=True, timeout=timeout).stdout

    def members():
        for node in ('pg-1', 'pg-2', 'pg-3'):
            try:
                text = run('exec', '-T', node, 'python', '-c',
                           "import urllib.request; print(urllib.request.urlopen('http://localhost:8008/cluster',timeout=2).read().decode())", timeout=5)
                return json.loads(text)['members']
            except (subprocess.SubprocessError, ValueError):
                continue
        raise RuntimeError('No Patroni node answered')

    def leader():
        return next((m['name'] for m in members() if m['role'] == 'leader'), None)

    old = wait_for(leader)
    executor = next(n for n in ('pg-1', 'pg-2', 'pg-3') if n != old)

    def sql(statement, parameters=None):
        return json.loads(run('exec', '-T', executor, 'python', '-c', SQL,
                              data=json.dumps({'sql': statement, 'parameters': parameters}), timeout=15))

    wait_for(lambda: sql('SELECT 1') == [[1]])
    table = 'ha_probe_' + uuid.uuid4().hex
    gid = 'ha-proof-' + uuid.uuid4().hex
    session = None
    restored = False
    fault_attempted = False
    result = {'project': args.project, 'fault': args.fault, 'old_primary': old}
    try:
        sql('CREATE TABLE ' + table + ' (id integer PRIMARY KEY)')
        sql('INSERT INTO ' + table + ' VALUES (1)')
        sql('BEGIN; INSERT INTO ' + table + ' VALUES (2); PREPARE TRANSACTION %s', [gid])
        wait_for(lambda: sql('SELECT count(*) FROM pg_prepared_xacts WHERE gid=%s', [gid]) == [[1]])
        session = subprocess.Popen(dc + ['exec', '-T', executor, 'python', '-c', SESSION],
                                   stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                   text=True)
        if not select.select([session.stdout], [], [], 8)[0] or session.stdout.readline().strip() != 'READY':
            raise RuntimeError('Existing session did not initialize')
        started = time.monotonic()
        fault_attempted = True
        if args.fault == 'pause':
            run('pause', old)
        else:
            run('stop', '--timeout', '10', old)
        output, _ = session.communicate('\n', timeout=15)
        result['existing_session'] = json.loads(output.strip())
        if args.fault == 'pause':
            assert result['existing_session']['closed'], 'Silent primary retained an existing session'
        new = wait_for(lambda: (n if (n := leader()) and n != old else None))
        wait_for(lambda: sql('SELECT count(*) FROM ' + table) == [[1]])
        result['new_primary'] = new
        result['recovery_seconds'] = round(time.monotonic() - started, 3)
        wait_for(lambda: sql('SELECT count(*) FROM pg_prepared_xacts WHERE gid=%s', [gid]) == [[1]])
        sql('COMMIT PREPARED %s', [gid])
        wait_for(lambda: sql('SELECT count(*) FROM ' + table) == [[2]])
        result.update(acknowledged_row_preserved=True, prepared_transaction_preserved=True,
                      prepared_transaction_committed=True)
    finally:
        # Only this script's named fault and its own fixture are restored/removed.
        if session is not None and session.poll() is None:
            session.kill(); session.communicate(timeout=5)
        if fault_attempted:
            run('unpause', old) if args.fault == 'pause' else run('start', old)
            wait_for(lambda: any(m['name'] == old and m['role'] != 'leader' and m['state'] in ('running', 'streaming') for m in members()))
            restored = True
        try:
            if sql('SELECT count(*) FROM pg_prepared_xacts WHERE gid=%s', [gid]) == [[1]]:
                sql('ROLLBACK PREPARED %s', [gid])
        finally:
            sql('DROP TABLE IF EXISTS ' + table)
    result['old_primary_rejoined'] = restored
    text = json.dumps(result, indent=2) + '\n'
    if args.report:
        args.report.write_text(text)
    print(text, end='')


if __name__ == '__main__':
    main()
