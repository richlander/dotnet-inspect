#!/usr/bin/env python3
"""Replay retained minimal-instruction trial receipts; not an answer scorer."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess

HERE = Path(__file__).resolve().parent
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--root', type=Path, default=HERE)
root = parser.parse_args().root.resolve()
manifest = json.loads((root / 'manifest.json').read_text())


def digest(data):
    return hashlib.sha256(data).hexdigest()


def records(path):
    return [json.loads(line) for line in path.read_text().splitlines()]


def response(trial, name):
    path = trial / name
    return path.read_bytes() if path.exists() else b''


for name, expected in manifest['inputs'].items():
    assert digest((root / name).read_bytes()) == expected, name
for fmt, entry in manifest['guides'].items():
    data = (root / entry['file']).read_bytes()
    assert len(data) == entry['bytes'] and digest(data) == entry['sha256'], fmt
    word = 'JSON' if fmt == 'data' else 'HAL'
    assert data == (root / 'guide-common.txt').read_bytes() + (
        f'\nAssigned selector: .{fmt}. The representation is {word}.\n').encode(), fmt

report = {}
for label in manifest['trials']:
    task, fmt, replicate = label.split('-')
    assert task in ('query', 'style') and replicate in ('1', '2'), label
    trial = root / label
    for name, expected in manifest['artifacts'][label].items():
        assert digest((trial / name).read_bytes()) == expected, (label, name)
    calls, views = records(trial / 'calls.jsonl'), records(trial / 'views.jsonl')
    # Parallel requests allocate IDs before the CLI call and may log out of order.
    assert sorted(c['id'] for c in calls) == list(range(1, len(calls) + 1)), label
    assert len(calls) <= 40 and len(views) <= 81, label
    assert len({v['file'] for v in views}) == len(views), label
    other = 'hal' if fmt == 'data' else 'data'
    successful_hashes = []
    for call in calls:
        assert call['argv'][0] == 'explain', (label, call)
        assert not any(a == f'.{other}' or f'projection={other}' in a
                       for a in call['argv']), (label, call)
        for stream in ('stdout', 'stderr'):
            name = call[stream + '_file']
            data = response(trial, name)
            assert len(data) == call[stream + '_bytes'], (label, name)
            assert digest(data) == manifest['responses'][label][name], (label, name)
        if call['exit'] == 0:
            successful_hashes.append(manifest['responses'][label][call['stdout_file']])
    for view in views:
        op = view['operation']
        if op == 'guide':
            data = (root / manifest['guides'][fmt]['file']).read_bytes()
        elif op == 'text':
            data = response(trial, view['source'])
        elif op.startswith('jq: '):
            data = subprocess.run(['jq', '-c', op[4:]],
                                  input=response(trial, view['source']),
                                  capture_output=True, check=True).stdout
        else:
            # Error text contains original absolute paths; retain its exact bytes.
            assert op.startswith('jq error: '), op
            data = (trial / view['file']).read_bytes()
        assert len(data) == view['bytes'] and digest(data) == view['sha256'], (
            label, view['file'])
    json.loads((trial / 'answer.json').read_text())
    selected = min(c['id'] for c in calls if c['exit'] == 0 and any(
        a == f'.{fmt}' or f'projection={fmt}' in a for a in c['argv']))
    before = [c for c in calls if c['id'] < selected]
    report[label] = {
        'requests': len(calls),
        'failed_requests': sum(c['exit'] != 0 for c in calls),
        'retrieved_bytes': sum(c['stdout_bytes'] + c['stderr_bytes'] for c in calls),
        'product_rendered_bytes': sum(v['bytes'] for v in views if v['operation'] != 'guide'),
        'product_views': sum(v['operation'] != 'guide' for v in views),
        'jq_errors': sum(v['operation'].startswith('jq error: ') for v in views),
        'guide_emitted_bytes': sum(v['bytes'] for v in views if v['operation'] == 'guide'),
        'contract_requests': sum('.contract' in c['argv'] or any(
            'projection=contract' in a for a in c['argv']) for c in calls),
        'href_requests': sum(any(a.startswith('inspect-resource:') for a in c['argv'])
                             for c in calls),
        'repeated_successful_payloads': len(successful_hashes) - len(set(successful_hashes)),
        'first_selected_request': selected,
        'requests_before_first_selected': len(before),
        'retrieved_bytes_before_first_selected': sum(
            c['stdout_bytes'] + c['stderr_bytes'] for c in before),
    }
assert report == json.loads((root / 'measurements.json').read_text())
print(json.dumps(report, indent=2))
