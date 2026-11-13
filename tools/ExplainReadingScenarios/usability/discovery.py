#!/usr/bin/env python3
"""Log local CLI discovery retrieval separately from agent-displayed jq output."""
import argparse
import json
from pathlib import Path
import subprocess
import sys

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--cli', type=Path, required=True)
parser.add_argument('--output', type=Path, required=True)
parser.add_argument('arguments', nargs=argparse.REMAINDER)
args = parser.parse_args()
argv = args.arguments
if argv[:1] == ['--']:
    argv = argv[1:]
allowed = argv and (argv[0] == 'explain' or '--help' in argv
                   or (argv[:2] == ['package', 'query'] and '-Q' in argv))
if not allowed:
    parser.error('Only local explanation, help, and package-query discovery are permitted.')
args.output.mkdir(parents=True, exist_ok=True)
result = subprocess.run([str(args.cli.resolve()), *argv], capture_output=True)
with (args.output / 'calls.jsonl').open('a') as log:
    log.write(json.dumps({'argv': argv, 'stdout_bytes': len(result.stdout),
                          'stderr_bytes': len(result.stderr), 'exit': result.returncode}) + '\n')
sys.stdout.buffer.write(result.stdout)
sys.stderr.buffer.write(result.stderr)
sys.exit(result.returncode)
