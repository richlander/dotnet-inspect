#!/usr/bin/env python3
"""Freeze a completed trial before asking the separate retrospective question."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('trial', type=Path)
p.add_argument('--check', action='store_true')
a = p.parse_args()
files = sorted(path for path in a.trial.iterdir() if path.name in
               ('answer.json', 'calls.jsonl', 'views.jsonl') or
               path.name.startswith(('response-', 'view-')))
assert all((a.trial / name).exists() for name in ('answer.json', 'calls.jsonl', 'views.jsonl'))
identity = {path.name: hashlib.sha256(path.read_bytes()).hexdigest() for path in files}
freeze = a.trial / 'frozen.json'
if a.check:
    assert identity == json.loads(freeze.read_text())['files'], a.trial
    print('Original answer, receipts, responses and views unchanged:', a.trial.name)
else:
    assert not freeze.exists(), freeze
    freeze.write_text(json.dumps({'frozen_at': datetime.now(timezone.utc).isoformat(),
                                  'files': identity}, indent=2) + '\n')
    print('Frozen:', a.trial.name)
