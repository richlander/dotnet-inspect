#!/usr/bin/env python3
"""Dispatch the Member diff printer trial: fresh tool-less participants, one rendering each.

`run.py freeze` writes dispatch.json with input hashes; `run.py dispatch` runs every
participant that has no answer yet and verifies the inputs still match dispatch.json.
"""
import concurrent.futures
import hashlib
import json
import subprocess
import sys
import tempfile
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parent
MODEL = 'gpt-6-sol'
REASONING = 'high'
REPLICATES = 2
MAIN_ARMS = ['G', 'U', 'M', 'ML', 'MLH']
SEPARATOR_ARMS = ['sep-pipe', 'sep-bracket', 'sep-comment']
INPUTS = ['scenarios.json', 'generate.py', 'run.py', 'participant.txt', 'task-main.txt',
          'task-separator.txt', 'criteria.txt'] + [f'arms/{a}.md' for a in MAIN_ARMS + SEPARATOR_ARMS]


def sha(path):
    return hashlib.sha256((ROOT / path).read_bytes()).hexdigest()


def prompt(arm):
    task = 'task-separator.txt' if arm.startswith('sep-') else 'task-main.txt'
    return '\n\n'.join([(ROOT / 'participant.txt').read_text().strip(),
                        (ROOT / 'arms' / f'{arm}.md').read_text().strip(),
                        (ROOT / task).read_text().strip()]) + '\n'


def participants():
    for arm in MAIN_ARMS + SEPARATOR_ARMS:
        for replicate in range(1, REPLICATES + 1):
            yield arm, replicate


def run_one(arm, replicate):
    out = ROOT / 'results' / f'{arm}-{replicate}'
    if (out / 'answer.json').exists():
        return f'{arm}-{replicate}: kept'
    out.mkdir(parents=True, exist_ok=True)
    text = prompt(arm)
    (out / 'prompt.md').write_text(text)
    with tempfile.TemporaryDirectory() as empty:
        started = time.time()
        result = subprocess.run(
            ['copilot', '-p', text, '--model', MODEL, '--reasoning-effort', REASONING,
             '--no-custom-instructions', '--no-ask-user', '--available-tools', 'none',
             '-s', '--no-color'],
            cwd=empty, capture_output=True, text=True, timeout=600)
    (out / 'response.out').write_text(result.stdout)
    (out / 'response.err').write_text(result.stderr)
    receipt = {'arm': arm, 'replicate': replicate, 'exit': result.returncode,
               'seconds': round(time.time() - started, 1), 'prompt_sha256': hashlib.sha256(text.encode()).hexdigest()}
    body = result.stdout.strip()
    if body.startswith('```'):
        body = body.split('\n', 1)[1].rsplit('```', 1)[0]
    try:
        (out / 'answer.json').write_text(json.dumps(json.loads(body), indent=2, ensure_ascii=False) + '\n')
        receipt['parsed'] = True
    except json.JSONDecodeError:
        receipt['parsed'] = False
    (out / 'receipt.json').write_text(json.dumps(receipt, indent=2) + '\n')
    return f"{arm}-{replicate}: exit {result.returncode}, parsed {receipt['parsed']}"


def main():
    action = sys.argv[1] if len(sys.argv) > 1 else ''
    if action == 'freeze':
        head = subprocess.run(['git', 'rev-parse', 'HEAD'], cwd=ROOT, capture_output=True, text=True).stdout.strip()
        dispatch = {'source_head': head, 'model': MODEL, 'reasoning': REASONING, 'replicates': REPLICATES,
                    'arms': MAIN_ARMS + SEPARATOR_ARMS, 'isolation': 'empty cwd, --no-custom-instructions, --available-tools none',
                    'inputs': {name: sha(name) for name in INPUTS}, 'issue': 11185}
        (ROOT / 'dispatch.json').write_text(json.dumps(dispatch, indent=2) + '\n')
        print('frozen')
    elif action == 'dispatch':
        frozen = json.loads((ROOT / 'dispatch.json').read_text())['inputs']
        changed = [name for name, digest in frozen.items() if sha(name) != digest]
        if changed:
            sys.exit(f'inputs changed since freeze: {changed}')
        with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
            for line in pool.map(lambda p: run_one(*p), participants()):
                print(line, flush=True)
    else:
        sys.exit('usage: run.py freeze|dispatch')


if __name__ == '__main__':
    main()
