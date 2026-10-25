#!/usr/bin/env python3
"""Replay frozen control-plane reading evidence offline; does not grade answers."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def records(path):
    return [json.loads(line) for line in path.read_text().splitlines()]


def measure(trial):
    calls = records(trial / 'calls.jsonl')
    views = records(trial / 'views.jsonl')
    supplied = sum(v['bytes'] for v in views if v['operation'] in ('guide', 'input'))
    retrieved = sum(c['stdout_bytes'] + c['stderr_bytes'] for c in calls)
    return {
        'requests': len(calls),
        'guidance_requests': sum(c['category'] == 'guidance' or c['argv'][0] == 'skill' for c in calls),
        'skill_requests': sum(c['argv'][0] == 'skill' for c in calls),
        'bare_root_requests': sum(c['argv'] == ['explain'] for c in calls),
        'primary_explanation_requests': sum('--explain' in c['argv'] for c in calls),
        'companion_requests': sum('-E' in c['argv'] for c in calls),
        'failed_requests': sum(c['exit'] != 0 for c in calls),
        'views': len(views),
        'jq_errors': sum(v['operation'].startswith('jq error:') for v in views),
        'retrieved_product_bytes': retrieved,
        'rendered_product_bytes': sum(v['bytes'] for v in views if v['operation'] not in ('guide', 'input')),
        'rendered_guide_input_bytes': supplied,
        'retrieved_with_guide_input_bytes': retrieved + supplied,
        'rendered_with_guide_input_bytes': sum(v['bytes'] for v in views),
        'product_elapsed_ns': sum(c['duration_ns'] for c in calls),
    }


def verify(root):
    dispatch = json.loads((root / 'dispatch.json').read_text())
    assert len(dispatch['trials']) == 6
    assert dispatch['model'] == 'gpt-6-sol' and dispatch['reasoning'] == 'high'
    assert dispatch['offline'] is True
    for name, expected in dispatch['inputs'].items():
        assert digest(root / name) == expected, name
    measurements = {}
    for label, assigned in dispatch['trials'].items():
        trial = root / label
        assert digest(trial / 'config.json') == assigned['config_sha256']
        assert digest(trial / 'briefing.txt') == assigned['briefing_sha256']
        freeze = json.loads((trial / 'frozen.json').read_text())
        frozen_files = sorted(p for p in trial.iterdir() if p.name in ('answer.json', 'calls.jsonl', 'views.jsonl', 'events.jsonl') or p.name.startswith(('response-', 'view-')))
        assert {p.name: digest(p) for p in frozen_files} == freeze['files'], label
        config = json.loads((trial / 'config.json').read_text())
        assert config['cli'] == dispatch['cli'] and config['task'] == assigned['task']
        calls, views, events = [records(trial / name) for name in ('calls.jsonl', 'views.jsonl', 'events.jsonl')]
        assert [c['id'] for c in calls] == list(range(1, len(calls) + 1))
        assert len(calls) <= 30 and len(views) <= 60
        assert views[0]['operation'] == 'guide'
        for call in calls:
            for prefix in ('stdout', 'source_stdout', 'stderr'):
                assert (trial / call[prefix + '_file']).stat().st_size == call[prefix + '_bytes']
            assert (trial / call['stdout_file']).read_bytes() == (trial / call['source_stdout_file']).read_bytes()
            assert call['removed_links'] == [] and call['duration_ns'] >= 0
            argv = call['argv']
            guidance = argv[0] == 'explain' or '--help' in argv or '-Q' in argv or '--query-help' in argv or '--explain' in argv
            assert call['category'] == ('guidance' if guidance else 'inspection')
        for view in views:
            op = view['operation']
            if op == 'guide':
                data = (trial / 'guide.md').read_bytes()
            elif op == 'input':
                assert view['source'] in config['inputs']
                data = (trial / view['source']).read_bytes()
            elif op == 'text':
                data = (trial / view['source']).read_bytes()
            else:
                error = op.startswith('jq error: ')
                assert error or op.startswith('jq: ')
                query = op[len('jq error: ') if error else len('jq: '):]
                replay = subprocess.run(['jq', '-c', query, str(trial / view['source'])], capture_output=True)
                assert bool(replay.returncode) == error, (label, op)
                if error:
                    data = (trial / view['file']).read_bytes()
                    def portable(value):
                        return re.sub(rb'\(at .*?:([0-9]+)\)', rb'(at RESPONSE:\1)', value)
                    assert portable(data) == portable(replay.stderr), (label, op)
                else:
                    data = replay.stdout
            assert len(data) == view['bytes'] and hashlib.sha256(data).hexdigest() == view['sha256']
            assert (trial / view['file']).read_bytes() == data
        assert len(events) == 2 * len(calls) + len(views)
        # This host's Python 3.9 monotonic epoch is process-relative. Validate
        # append order, not cross-process timestamp arithmetic.
        active, finished, seen_views = None, set(), []
        by_file = {c[k]: c['id'] for c in calls for k in ('stdout_file', 'stderr_file')}
        for event in events:
            if event['kind'] == 'request-start':
                assert active is None and event['id'] not in finished
                active = event['id']
            elif event['kind'] == 'request':
                assert active == event['id']
                finished.add(active)
                active = None
            else:
                assert event['kind'] == 'view' and active is None
                if event['source'] in by_file:
                    assert by_file[event['source']] in finished
                seen_views.append(event['file'])
        assert active is None and finished == {c['id'] for c in calls}
        assert seen_views == [v['file'] for v in views]
        post = json.loads((trial / 'post-task.json').read_text())
        assert post['question'] == (root / 'post-task-question.txt').read_text().strip()
        measurements[label] = measure(trial)
    launches = json.loads((root / 'launches.json').read_text())
    assert {entry['trial'] for entry in launches} == set(dispatch['trials']) and len(launches) == 6
    for entry in launches:
        assert entry['model'] == dispatch['model'] and entry['reasoning'] == dispatch['reasoning']
        assert entry['fork_turns'] == 'none'
    if (root / 'manifest.json').exists():
        manifest = json.loads((root / 'manifest.json').read_text())
        assert digest(root / 'dispatch.json') == manifest['dispatch_sha256']
        for name, expected in manifest['retained_files'].items():
            assert digest(root / name) == expected, name
        for label, files in manifest['artifacts'].items():
            assert {p.name for p in (root / label).iterdir() if p.is_file()} == set(files)
            for name, expected in files.items():
                assert digest(root / label / name) == expected, (label, name)
    return measurements


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('root', nargs='?', type=Path, default=Path(__file__).parent)
    parser.add_argument('--write-measurements', action='store_true')
    args = parser.parse_args()
    result = verify(args.root.resolve())
    output = args.root / 'measurements.json'
    if args.write_measurements:
        output.write_text(json.dumps(result, indent=2) + '\n')
    else:
        assert result == json.loads(output.read_text())
    print('Six frozen trials: input pins, unmodified output, jq replay, serial log order and byte arithmetic verified.')
    print(json.dumps(result, indent=2))
