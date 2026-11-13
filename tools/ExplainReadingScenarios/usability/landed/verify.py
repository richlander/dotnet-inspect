#!/usr/bin/env python3
"""Check retained cooperative-trial receipts and replay successful jq views."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess

HERE = Path(__file__).resolve().parent
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--root', type=Path, default=HERE)
args = parser.parse_args()
root = args.root.resolve()
manifest = json.loads((root / 'manifest.json').read_text())


def digest(data):
    return hashlib.sha256(data).hexdigest()


def records(path):
    return [json.loads(line) for line in path.read_text().splitlines()]


def guide(label):
    if label.startswith('slim-'):
        return (root / 'reading-guide.txt').read_bytes()
    return b'\n\n'.join(subprocess.check_output(
        ['git', 'show', f'{manifest["source_commit"]}:{path}'], cwd=HERE)
        for path in manifest['skills'])


report = {}
for label in manifest['trials']:
    trial = root / label
    calls = records(trial / 'calls.jsonl')
    views = records(trial / 'views.jsonl')
    assert [c['id'] for c in calls] == list(range(1, len(calls) + 1)), label
    assert len({v['file'] for v in views}) == len(views), label
    for call in calls:
        assert call['argv'][0] == 'explain', (label, call)
        for stream in ('stdout', 'stderr'):
            path = trial / call[stream + '_file']
            data = path.read_bytes() if path.exists() else b''
            assert len(data) == call[stream + '_bytes'], (label, path)
            assert digest(data) == manifest['responses'][label][path.name], (label, path)
    for view in views:
        operation = view['operation']
        if operation == 'guide':
            data = guide(label)
        elif operation == 'text':
            data = (trial / view['source']).read_bytes()
        elif operation.startswith('jq: '):
            result = subprocess.run(['jq', '-c', operation[4:], str(trial / view['source'])],
                                    capture_output=True, check=True)
            data = result.stdout
        else:
            # jq diagnostics include the original absolute scratch path. Retain
            # those emitted diagnostics; replay only successful projections.
            assert operation.startswith('jq error: '), operation
            data = (trial / view['file']).read_bytes()
        assert len(data) == view['bytes'], (label, view['file'])
        assert digest(data) == view['sha256'], (label, view['file'])
    json.loads((trial / 'answer.json').read_text())
    report[label] = {
        'requests': len(calls),
        'failed_requests': sum(c['exit'] != 0 for c in calls),
        'retrieved_bytes': sum(c['stdout_bytes'] + c['stderr_bytes'] for c in calls),
        'product_rendered_bytes': sum(v['bytes'] for v in views if v['operation'] != 'guide'),
        'jq_errors': sum(v['operation'].startswith('jq error: ') for v in views),
        'guide_bytes_once': len(guide(label)),
        'guide_emitted_bytes': sum(v['bytes'] for v in views if v['operation'] == 'guide'),
        'guide_views': sum(v['operation'] == 'guide' for v in views),
        'contract_requests': sum('.contract' in c['argv'] or any(
            'projection=contract' in a for a in c['argv']) for c in calls),
    }

assert report == json.loads((root / 'measurements.json').read_text())
print(json.dumps(report, indent=2))
