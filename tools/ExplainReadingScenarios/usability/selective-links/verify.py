#!/usr/bin/env python3
"""Replay selective-link receipts and projection; not an answer scorer."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess

from project import project


def digest(data):
    return hashlib.sha256(data).hexdigest()


def records(path):
    return [json.loads(line) for line in path.read_text().splitlines()]


def measure(calls, views):
    return {
        'requests': len(calls),
        'failed_requests': sum(c['exit'] != 0 for c in calls),
        'visible_retrieved_bytes': sum(c['stdout_bytes'] + c['stderr_bytes'] for c in calls),
        'source_retrieved_bytes': sum(c['source_stdout_bytes'] + c['stderr_bytes'] for c in calls),
        'product_rendered_bytes': sum(v['bytes'] for v in views if v['operation'] != 'guide'),
        'product_views': sum(v['operation'] != 'guide' for v in views),
        'jq_errors': sum(v['operation'].startswith('jq error: ') for v in views),
        'guide_emitted_bytes': sum(v['bytes'] for v in views if v['operation'] == 'guide'),
        'contract_requests': sum('.contract' in c['argv'] or any('projection=contract' in a for a in c['argv']) for c in calls),
        'href_requests': sum(any(a.startswith('inspect-resource:') for a in c['argv']) for c in calls),
        'href_hal_overrides': sum('.hal' in c['argv'] and any(a.startswith('inspect-resource:') for a in c['argv']) for c in calls),
        'removed_links': sum(len(c['removed_links']) for c in calls),
    }


def without_links(value):
    if isinstance(value, dict):
        return {k: without_links(v) for k, v in value.items() if k != '_links'}
    if isinstance(value, list):
        return [without_links(v) for v in value]
    return value


def mask_links(data):
    """Independent check for pinned generated JSON: mask only _links values."""
    text = data.decode('utf-8')
    spans = []
    for match in re.finditer(r'(?<!\\)"_links"\s*:\s*', text):
        _, end = json.JSONDecoder().raw_decode(text, match.end())
        spans.append((match.end(), end))
    for start, end in reversed(spans):
        text = text[:start] + '{}' + text[end:]
    return text


def hrefs(value):
    if isinstance(value, dict):
        for key, item in value.items():
            if key == 'href' and isinstance(item, str):
                yield item
            else:
                yield from hrefs(item)
    elif isinstance(value, list):
        for item in value:
            yield from hrefs(item)


def main(root):
    manifest = json.loads((root / 'manifest.json').read_text())
    dispatch = json.loads((root / 'dispatch.json').read_text())
    assert digest((root / 'dispatch.json').read_bytes()) == manifest['dispatch_sha256']
    parent = root.parent / 'hal-stop'
    assert digest((parent / 'dispatch-strong.json').read_bytes()) == dispatch['historical_dispatch_sha256']
    assert (root / 'guide.txt').read_bytes() == (parent / 'guide-strong.txt').read_bytes()
    for name in ('participant.txt', 'query-task.txt', 'style-task.txt', 'navigation-task.txt',
                 'assessment-criteria.txt', 'navigation-criteria.txt', 'post-task-question.txt'):
        assert (root / name).read_bytes() == (parent / name).read_bytes(), name
    for inputs in (dispatch['inputs'], manifest['inputs']):
        for name, expected in inputs.items():
            assert digest((root / name).read_bytes()) == expected, name
    assert digest((root / 'source-equivalence.json').read_bytes()) == manifest['source_equivalence_sha256']
    historical = {}
    for label in json.loads((parent / 'dispatch-strong.json').read_text())['trials']:
        for call in records(parent / label / 'calls.jsonl'):
            historical[tuple(call['argv'])] = (parent / label / call['stdout_file']).read_bytes()
    equivalence = json.loads((root / 'source-equivalence.json').read_text())
    assert len(equivalence) == dispatch['source_equivalence_requests'] == len(historical)
    for entry in equivalence:
        source = historical[tuple(entry['argv'])]
        visible, removed = project(source)
        assert digest(source) == entry['source_sha256'] and len(source) == entry['source_bytes']
        assert len(visible) == entry['projected_bytes'] and removed == entry['removed_links']
        assert mask_links(source) == mask_links(visible)
    question = (root / 'post-task-question.txt').read_text().strip()
    report = {}
    for label in dispatch['trials']:
        trial = root / label
        def response(name):
            path = trial / name
            return path.read_bytes() if path.exists() else b''
        for name, expected in manifest['artifacts'][label].items():
            assert digest(response(name)) == expected, (label, name)
        calls, views, events = (records(trial / name) for name in
                               ('calls.jsonl', 'views.jsonl', 'events.jsonl'))
        assert sorted(c['id'] for c in calls) == list(range(1, len(calls) + 1))
        assert len(calls) <= 40 and sum(v['operation'] != 'guide' for v in views) <= 80
        for call in calls:
            assert call['argv'][0] == 'explain'
            assert not any(a == '.data' or 'projection=data' in a for a in call['argv'])
            for prefix, count in (('stdout', 'stdout_bytes'), ('stderr', 'stderr_bytes'),
                                  ('source_stdout', 'source_stdout_bytes')):
                name = call[prefix + '_file']
                assert len(response(name)) == call[count]
                assert digest(response(name)) == manifest['responses'][label][name], (label, name)
            source = response(call['source_stdout_file'])
            visible, removed = project(source) if call['exit'] == 0 else (source, [])
            assert visible == response(call['stdout_file']) and removed == call['removed_links']
            if call['exit'] == 0:
                assert without_links(json.loads(source)) == without_links(json.loads(visible))
                assert mask_links(source) == mask_links(visible)
            if tuple(call['argv']) in historical:
                assert source == historical[tuple(call['argv'])]
        for view in views:
            op = view['operation']
            if op == 'guide':
                data = (root / 'guide.txt').read_bytes()
            elif op == 'text':
                data = response(view['source'])
            elif op.startswith('jq: '):
                data = subprocess.run(['jq', '-c', op[4:]], input=response(view['source']),
                                      capture_output=True, check=True).stdout
            else:
                assert op.startswith('jq error: ')
                data = response(view['file'])
            assert len(data) == view['bytes'] and digest(data) == view['sha256']
        # Preserve the captured clock values, but they are not comparable across
        # processes in this environment. The locked append ledger supplies cross-process order.
        starts = {e['id']: index for index, e in enumerate(events) if e['kind'] == 'request-start'}
        ends = {e['id']: index for index, e in enumerate(events) if e['kind'] == 'request'}
        view_events = {e['file']: index for index, e in enumerate(events) if e['kind'] == 'view'}
        assert len(events) == 2 * len(calls) + len(views)
        assert starts.keys() == ends.keys() == {c['id'] for c in calls}
        assert view_events.keys() == {v['file'] for v in views}
        assert all(starts[key] < ends[key] for key in starts)
        for view in views:
            if view['operation'] != 'guide':
                request_id = int(view['source'].split('-')[1].split('.')[0])
                assert ends[request_id] < view_events[view['file']]
        frozen = json.loads(response('frozen.json'))
        originals = set(manifest['responses'][label]) | {v['file'] for v in views} | {
            'answer.json', 'calls.jsonl', 'views.jsonl', 'events.jsonl'}
        assert set(frozen['files']) == originals
        for name, expected in frozen['files'].items():
            matches = [v for v in views if v['file'] == name]
            actual = matches[0]['sha256'] if matches else digest(response(name))
            assert actual == expected
        assert json.loads(response('post-task.json'))['question'] == question
        json.loads(response('answer.json'))
        if label.startswith('navigation'):
            ordered = sorted(calls, key=lambda c: starts[c['id']])
            assert ordered[0]['argv'] == ['explain', 'package-query/query/facets/library-literal', '.hal', '--json']
            for call in ordered[1:]:
                available = set()
                for prior in calls:
                    if prior['exit'] == 0 and ends[prior['id']] < starts[call['id']]:
                        available.update(hrefs(json.loads(response(prior['stdout_file']))))
                assert call['argv'][1] in available
                assert call['argv'][2:] in (['--json'], ['.hal', '--json'])
        report[label] = measure(calls, views)
    assert report == json.loads((root / 'measurements.json').read_text())
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=Path(__file__).resolve().parent)
    main(parser.parse_args().root.resolve())
