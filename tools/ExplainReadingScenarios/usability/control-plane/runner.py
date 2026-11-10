#!/usr/bin/env python3
"""Instrument cooperative, local CLI reading trials; not a security sandbox."""
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
input_parser = sub.add_parser('input')
input_parser.add_argument('name', choices=('component.json', 'parent.packet'))
request = sub.add_parser('request')
request.add_argument('argv', nargs=argparse.REMAINDER)
view = sub.add_parser('view')
view.add_argument('response')
view.add_argument('--jq')
view.add_argument('--text', action='store_true')
a = p.parse_args()
trial = a.trial.resolve()
config = json.loads((trial / 'config.json').read_text())

def log(name, value):
    with (trial / name).open('a') as f:
        fcntl.flock(f, fcntl.LOCK_EX)
        f.write(json.dumps(value, ensure_ascii=False) + '\n')

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
    existing = len((trial / 'views.jsonl').read_text().splitlines()) if (trial / 'views.jsonl').exists() else 0
    if existing >= 60:
        p.error('Stop at 60 views, including guide and supplied inputs.')
    n = allocate('view')
    path = trial / f'view-{n:02}.txt'
    path.write_bytes(data)
    log('views.jsonl', {'source': source, 'operation': operation,
                       'file': path.name, 'bytes': len(data),
                       'sha256': hashlib.sha256(data).hexdigest()})
    log('events.jsonl', {'kind': 'view', 'file': path.name, 'source': source, 'time_ns': time.monotonic_ns()})
    sys.stdout.buffer.write(data)

if a.action == 'guide':
    display((trial / 'guide.md').read_bytes(), config.get('guide_label', 'assigned guide'), 'guide')
elif a.action == 'input':
    if a.name not in config['inputs']:
        p.error('This input is not supplied for this task.')
    display(Path(config['inputs'][a.name]).read_bytes(), a.name, 'input')
elif a.action == 'request':
    argv = a.argv[1:] if a.argv[:1] == ['--'] else a.argv
    discovery = argv and (argv[0] == 'explain' or '--help' in argv
                          or '-Q' in argv or '--query-help' in argv or '--explain' in argv)
    inspection = argv and argv[0] in ('package', 'extensions', 'find', 'type', 'member', 'library', 'depends', 'workspace', 'skill')
    if not (discovery or inspection):
        p.error('Use product discovery, scoped inspections, Workspace commands or product skills.')
    # Cooperative scope restriction, not a security sandbox. All product HTTP is blocked.
    if config['task'] == 'relation' and argv and argv[0] == 'workspace':
        p.error('No Workspace was supplied for this relation task.')
    n_existing = len((trial / 'calls.jsonl').read_text().splitlines()) if (trial / 'calls.jsonl').exists() else 0
    if n_existing >= 30:
        p.error('Stop at 30 product requests.')
    n = allocate('response')
    env = {**os.environ, 'DOTNET_ROOT': config['dotnet_root'], 'DOTNET_INSPECT_OFFLINE': '1'}
    log('events.jsonl', {'kind': 'request-start', 'id': n, 'time_ns': time.monotonic_ns()})
    started = time.monotonic_ns()
    try:
        r = subprocess.run([config['cli'], *argv], env=env, capture_output=True, timeout=180, cwd=trial)
    except subprocess.TimeoutExpired as error:
        r = subprocess.CompletedProcess(argv, 124, error.stdout or b'', (error.stderr or b'') + b'\nRecorder: product request timed out after 180 seconds.\n')
    duration_ns = time.monotonic_ns() - started
    out = trial / f'response-{n:02}.out'
    err = trial / f'response-{n:02}.err'
    source = trial / f'response-{n:02}.source.out'
    source.write_bytes(r.stdout)
    projected, removed = r.stdout, []
    out.write_bytes(projected)
    err.write_bytes(r.stderr)
    receipt = {'id': n, 'argv': argv, 'exit': r.returncode, 'category': 'guidance' if discovery else 'inspection',
               'primary_explanation_requested': '--explain' in argv,
               'companion_requested': (argv[argv.index('-E') + 1] if '-E' in argv and argv.index('-E') + 1 < len(argv) and argv[argv.index('-E') + 1].startswith('.') else 'complete') if '-E' in argv else None,
               'duration_ns': duration_ns, 'stdout_bytes': len(projected), 'stderr_bytes': len(r.stderr),
               'source_stdout_file': source.name, 'source_stdout_bytes': len(r.stdout),
               'removed_links': removed,
               'stdout_file': out.name, 'stderr_file': err.name}
    log('calls.jsonl', receipt)
    log('events.jsonl', {'kind': 'request', 'id': n, 'time_ns': time.monotonic_ns()})
    # Participants see the same receipt shape as earlier trials.
    print(json.dumps({key: value for key, value in receipt.items()
                      if key not in ('source_stdout_file', 'source_stdout_bytes', 'removed_links')}))
elif a.action == 'view':
    source = (trial / a.response).resolve()
    if source.parent != trial or not re.fullmatch(r'response-\d+\.(out|err)', source.name):
        p.error('View only this trial\'s saved CLI response files.')
    if a.text:
        display(source.read_bytes(), source.name, 'text')
    elif a.jq is not None:
        r = subprocess.run(['jq', '-c', a.jq, str(source)], capture_output=True)
        if r.returncode:
            display(r.stderr, source.name, 'jq error: ' + a.jq)
            sys.exit(r.returncode)
        display(r.stdout, source.name, 'jq: ' + a.jq)
    else:
        p.error('Supply --jq FILTER or --text.')
