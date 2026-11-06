#!/usr/bin/env python3
"""Measure one participant from its Copilot transcript (session.md).

Reports tool calls by kind, output bytes returned to the participant, failed
calls, and any use of dotnet-inspect or path outside the scratch directory.
Usage: measure.py <participant-dir>
"""
import json, re, sys
from pathlib import Path

trial = Path(sys.argv[1]).resolve()
text = (trial / "session.md").read_text(errors="replace")
sections = re.split(r"\n### ", text)[1:]
calls = []
for section in sections:
    head = section.split("\n", 1)[0].strip()
    m = re.fullmatch(r"`([^`]+)`", head)
    if not m:
        continue
    tool = m.group(1)
    command = (re.search(r"^`\$ (.*)`$", section, re.M) or re.search(r"```json\n(.*?)\n```", section, re.S))
    command = command.group(1) if command else ""
    blocks = re.findall(r"```(?:[a-z]*)\n(.*?)\n```", section, re.S)
    output = blocks[-1] if blocks else ""
    exit_code = re.search(r"completed with exit code (\d+)", output)
    if tool == "bash":
        kind = ("document" if "symbols.json" in command else
                "download" if re.search(r"\b(curl|wget|unzip|Invoke-WebRequest)\b", command) else
                "sdk-or-compiled" if re.search(r"(^|[\s;|&(])dotnet\s", command) else
                "script" if re.search(r"\bpython3?\b|\bnode\b", command) else "shell")
    else:
        kind = tool
    calls.append({"tool": tool, "kind": kind, "bytes": len(output.encode()),
                  "failed": bool(exit_code and exit_code.group(1) != "0"), "command": command[:300]})

# An invocation, not a path fragment: the command word dotnet-inspect, or dnx/dotnet tool forms of it.
invocation = re.compile(r"(^|[\s;|&(])((dnx|dotnet\s+tool\s+(run|install|exec))\s+(-\S+\s+)*)?dotnet-inspect(\s|$)")
uses_tool = [c["command"] for c in calls if invocation.search(c["command"])]
outside = sorted({p for c in calls for p in re.findall(r"(/(?:Users|private|tmp|var)[^\s'\"`)]*)", c["command"])
                  if not p.startswith(str(trial)) and "/dotnet" not in p})
kinds = {}
for c in calls:
    k = kinds.setdefault(c["kind"], {"calls": 0, "bytes": 0, "failed": 0})
    k["calls"] += 1; k["bytes"] += c["bytes"]; k["failed"] += c["failed"]
print(json.dumps({"participant": trial.name, "tool_calls": len(calls), "output_bytes": sum(c["bytes"] for c in calls),
                  "failed": sum(c["failed"] for c in calls), "by_kind": kinds,
                  "dotnet_inspect_use": uses_tool, "paths_outside_scratch": outside}, indent=2))
