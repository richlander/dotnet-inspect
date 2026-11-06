#!/usr/bin/env python3
"""Instrument the cooperative symbol document trial; not a security sandbox.

Adapted from ../command-bound/runner.py: the same guide, request, and view
operations and receipts, plus a document operation for the document arm, a
single-package scope, and request and view limits read from config.json.
"""
import argparse
import fcntl
import hashlib
import json
import os
import re
from pathlib import Path
import subprocess
import sys
import time

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('trial', type=Path)
sub = p.add_subparsers(dest='action', required=True)
sub.add_parser('guide')
request = sub.add_parser('request')
request.add_argument('argv', nargs=argparse.REMAINDER)
view = sub.add_parser('view')
view.add_argument('response')
view.add_argument('--jq')
view.add_argument('--text', action='store_true')
document = sub.add_parser('document')
document.add_argument('--jq')
document.add_argument('--grep')
a = p.parse_args()
trial = a.trial.resolve()
config = json.loads((trial / 'config.json').read_text())


def log(name, value):
    with (trial / name).open('a') as f:
        fcntl.flock(f, fcntl.LOCK_EX)
        f.write(json.dumps(value, ensure_ascii=False) + '\n')


def count(name):
    path = trial / name
    return len(path.read_text().splitlines()) if path.exists() else 0


def allocate(name):
    with (trial / (name + '.counter')).open('a+') as f:
        fcntl.flock(f, fcntl.LOCK_EX)
        f.seek(0)
        n = int(f.read() or '0') + 1
        f.seek(0)
        f.truncate()
        f.write(str(n))
        return n


def display(data, source, operation):
    if count('views.jsonl') >= config['view_limit']:
        p.error(f"Stop at {config['view_limit']} views.")
    n = allocate('view')
    path = trial / f'view-{n:02}.txt'
    path.write_bytes(data)
    log('views.jsonl', {'source': source, 'operation': operation,
                        'file': path.name, 'bytes': len(data),
                        'sha256': hashlib.sha256(data).hexdigest()})
    log('events.jsonl', {'kind': 'view', 'file': path.name, 'source': source, 'time_ns': time.monotonic_ns()})
    sys.stdout.buffer.write(data)


def run_jq(filter_text, path):
    return subprocess.run(['jq', '-c', filter_text, str(path)], capture_output=True)


if a.action == 'guide':
    display((trial / 'guide.md').read_bytes(), config['arm'] + ' guide', 'guide')
elif a.action == 'request':
    argv = a.argv[1:] if a.argv[:1] == ['--'] else a.argv
    if not argv or argv[0] not in ('find', 'type', 'member', 'library', 'extensions'):
        p.error('Use find, type, member, library, or extensions.')
    if '--package' not in argv or argv[argv.index('--package') + 1] != config['package']:
        p.error(f"Every request must name --package {config['package']}.")
    if count('calls.jsonl') >= config['request_limit']:
        p.error(f"Stop at {config['request_limit']} product requests.")
    discovery = '--help' in argv or '-D' in argv or '-Q' in argv
    n = allocate('response')
    log('events.jsonl', {'kind': 'request-start', 'id': n, 'time_ns': time.monotonic_ns()})
    started = time.monotonic()
    r = subprocess.run([config['cli'], *argv], env=os.environ.copy(), capture_output=True, timeout=180)
    seconds = round(time.monotonic() - started, 3)
    out = trial / f'response-{n:02}.out'
    err = trial / f'response-{n:02}.err'
    out.write_bytes(r.stdout)
    err.write_bytes(r.stderr)
    receipt = {'id': n, 'argv': argv, 'exit': r.returncode, 'category': 'guidance' if discovery else 'inspection',
               'stdout_bytes': len(r.stdout), 'stderr_bytes': len(r.stderr), 'seconds': seconds,
               'stdout_file': out.name, 'stderr_file': err.name}
    log('calls.jsonl', receipt)
    log('events.jsonl', {'kind': 'request', 'id': n, 'time_ns': time.monotonic_ns()})
    print(json.dumps({key: value for key, value in receipt.items() if key != 'seconds'}))
elif a.action == 'view':
    source = (trial / a.response).resolve()
    if source.parent != trial or not re.fullmatch(r'response-\d+\.(out|err)', source.name):
        p.error('View only this trial\'s saved CLI response files.')
    if a.text:
        display(source.read_bytes(), source.name, 'text')
    elif a.jq is not None:
        r = run_jq(a.jq, source)
        display(r.stdout if r.returncode == 0 else r.stderr, source.name, ('jq: ' if r.returncode == 0 else 'jq error: ') + a.jq)
        sys.exit(r.returncode)
    else:
        p.error('Supply --jq FILTER or --text.')
elif a.action == 'document':
    if not config.get('document'):
        p.error('This arm has no document.')
    path = Path(config['document'])
    if a.jq is not None:
        r = run_jq(a.jq, path)
        display(r.stdout if r.returncode == 0 else r.stderr, 'document', ('jq: ' if r.returncode == 0 else 'jq error: ') + a.jq)
        sys.exit(r.returncode)
    elif a.grep is not None:
        r = subprocess.run(['grep', '-F', '--', a.grep, str(path)], capture_output=True)
        display(r.stdout, 'document', 'grep: ' + a.grep)
    else:
        p.error('Supply --jq FILTER or --grep TEXT.')
