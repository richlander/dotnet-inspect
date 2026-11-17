#!/usr/bin/env python3
"""Freeze or check every file in a participant directory before the retrospective.
Usage: freeze.py <participant-dir> [--check]"""
import hashlib, json, sys
from datetime import datetime, timezone
from pathlib import Path

trial = Path(sys.argv[1]).resolve()
check = "--check" in sys.argv[2:]
skip = {"frozen.json", "post-task.md", "post-task.err"}
files = {str(p.relative_to(trial)): hashlib.sha256(p.read_bytes()).hexdigest()
         for p in sorted(trial.rglob("*")) if p.is_file() and p.name not in skip}
freeze = trial / "frozen.json"
if check:
    assert files == json.loads(freeze.read_text())["files"], trial
    print("Unchanged:", trial.name)
else:
    assert (trial / "answer.json").exists() and not freeze.exists(), trial
    freeze.write_text(json.dumps({"frozen_at": datetime.now(timezone.utc).isoformat(), "files": files}, indent=2) + "\n")
    print("Frozen:", trial.name, len(files), "files")
