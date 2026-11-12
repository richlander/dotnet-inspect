#!/usr/bin/env python3
"""Sum one participant's model usage from its Copilot session events.

Counts only the task phase: usage records before the session is resumed for the
post-task question. Usage: tokens.py <copilot-home> <participant-name>
"""
import json, sys
from pathlib import Path

home, name = Path(sys.argv[1]), sys.argv[2]
events = next(home.glob("session-state/*/events.jsonl"))
task = []
for line in events.open():
    row = json.loads(line)
    if row.get("type") == "session.resume":
        break
    if row.get("type") == "session.usage_record":
        task.append(row["data"]["usage"])
total = lambda key: sum(u.get(key, 0) for u in task)
print(json.dumps({
    "participant": name, "model": sorted({u["model"] for u in task}), "model_calls": len(task),
    "input_tokens": total("inputTokens"), "cache_read_tokens": total("cacheReadTokens"),
    "uncached_input_tokens": total("inputTokens") - total("cacheReadTokens"),
    "output_tokens": total("outputTokens"), "reasoning_tokens": total("reasoningTokens"),
    "peak_input_tokens": max((u["inputTokens"] for u in task), default=0),
    "billed_requests": total("cost"),
}, indent=2))
