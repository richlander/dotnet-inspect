#!/usr/bin/env python3
"""Replay preserved Package Query adoption evidence; not a domain answer scorer."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import subprocess


def digest(data):
    return hashlib.sha256(data).hexdigest()


def load_replay(root):
    spec = importlib.util.spec_from_file_location(
        "command_bound_replay", root.parent / "command-bound" / "verify.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def measure(root, trial, replay):
    calls, views = (replay.records(trial / name) for name in ("calls.jsonl", "views.jsonl"))
    result = replay.measure(calls, views)
    admitted = []
    for call in calls:
        if call["argv"][:2] == ["package", "query"] and call["primary_explanation_requested"] and call["exit"] == 0:
            data = json.loads((trial / call["stdout_file"]).read_text())
            assert data["kind"] == "query-space"
            assert data["_links"]["self"]["type"] == "application/hal+json"
            assert data["data_scope"]["completeness"] == "Complete"
            admitted.append(call)
    files = {call[key] for call in admitted for key in ("stdout_file", "stderr_file")}
    result["admitted_command_hal_requests"] = len(admitted)
    result["command_hal_rendered_bytes"] = sum(v["bytes"] for v in views if v["source"] in files)
    return result


def main(root):
    root = root.resolve()
    dispatch = json.loads((root / "dispatch.json").read_text())
    manifest = json.loads((root / "manifest.json").read_text())
    assert digest((root / "dispatch.json").read_bytes()) == manifest["dispatch_sha256"]
    assert dispatch["source_head"] == "813406e7fa0c023fd2845c64c00c56d17a958b4d"
    assert dispatch["sdk"] == "11.0.100-rc.1.26425.128"
    assert dispatch["model"] == "gpt-6-sol" and dispatch["reasoning"] == "high"
    assert len(dispatch["trials"]) == 4 and set(dispatch["trials"].values()) == {"package"}
    assert (root / "guide.txt").stat().st_size == dispatch["guide_bytes"]
    for inventory in (dispatch["inputs"], manifest["inputs"]):
        for name, expected in inventory.items():
            assert digest((root / name).read_bytes()) == expected, name
    assert (root / "package-task.txt").read_bytes() == (root.parent / "command-bound" / "package-task.txt").read_bytes()
    assert (root / "post-task-question.txt").read_bytes() == (root.parent / "command-bound" / "post-task-question.txt").read_bytes()
    preflight = json.loads((root / "preflight/receipts.json").read_text())
    assert len(preflight) == 10
    for receipt in preflight:
        for channel in ("stdout", "stderr"):
            path = root / "preflight" / (receipt["name"] + (".out" if channel == "stdout" else ".err"))
            data = path.read_bytes()
            assert len(data) == receipt[channel + "_bytes"]
            assert digest(data) == receipt[channel + "_sha256"]
        assert receipt["exit"] == (1 if receipt["name"] in ("tips", "seed", "root-output") else 0)
    for name in ("root-hal", "command-json", "offline"):
        assert (root / "preflight/command.out").read_bytes() == (root / "preflight" / (name + ".out")).read_bytes()
    replay = load_replay(root)
    measurements = {}
    for label in dispatch["trials"]:
        trial = root / label
        assert digest((trial / "config.json").read_bytes()) == dispatch["trial_configs"][label]
        assert (trial / "guide.md").read_bytes() == (root / "guide.txt").read_bytes()
        assert {p.name for p in trial.iterdir() if p.is_file()} == set(manifest["artifacts"][label])
        for name, expected in manifest["artifacts"][label].items():
            assert digest((trial / name).read_bytes()) == expected, (label, name)
        original = sorted(p for p in trial.iterdir() if p.name in ("answer.json", "calls.jsonl", "views.jsonl", "events.jsonl") or p.name.startswith(("response-", "view-")))
        assert {p.name: digest(p.read_bytes()) for p in original} == json.loads((trial / "frozen.json").read_text())["files"]
        calls, views, events = (replay.records(trial / name) for name in ("calls.jsonl", "views.jsonl", "events.jsonl"))
        assert len(calls) <= 30 and sum(v["operation"] != "guide" for v in views) <= 60
        assert [c["id"] for c in calls] == list(range(1, len(calls) + 1))
        for call in calls:
            argv = call["argv"]
            discovery = argv[0] == "explain" or any(token in argv for token in ("--help", "-Q", "--query-help", "--explain"))
            assert call["category"] == ("guidance" if discovery else "inspection")
            assert call["primary_explanation_requested"] == ("--explain" in argv)
            assert ".data" not in argv and not any("projection=data" in value for value in argv)
            expected_companion = ((argv[argv.index("-E") + 1] if argv.index("-E") + 1 < len(argv) and argv[argv.index("-E") + 1].startswith(".") else "complete") if "-E" in argv else None)
            assert call["companion_requested"] == expected_companion
            if not discovery and argv[:2] == ["package", "query"]:
                assert argv[2].startswith("Microsoft.Extensions")
                assert "--take" in argv and 1 <= int(argv[argv.index("--take") + 1]) <= 40
                assert "dependency-target=net10.0" in argv
            for prefix in ("stdout", "stderr", "source_stdout"):
                assert (trial / call[prefix + "_file"]).stat().st_size == call[prefix + "_bytes"]
            assert (trial / call["stdout_file"]).read_bytes() == (trial / call["source_stdout_file"]).read_bytes()
            assert call["removed_links"] == []
        for view in views:
            op = view["operation"]
            if op == "guide":
                data = (root / "guide.txt").read_bytes()
            elif op == "text":
                data = (trial / view["source"]).read_bytes()
            else:
                error = op.startswith("jq error: ")
                assert error or op.startswith("jq: ")
                query = op[len("jq error: ") if error else len("jq: "):]
                run = subprocess.run(["jq", "-c", query, str(trial / view["source"])], capture_output=True)
                assert bool(run.returncode) == error
                if error:
                    data = (trial / view["file"]).read_bytes()
                    normalize = lambda value: re.sub(rb"\(at .*?:([0-9]+)\)", rb"(at RESPONSE:\1)", value)
                    assert normalize(run.stderr) == normalize(data)
                else:
                    data = run.stdout
            assert data == (trial / view["file"]).read_bytes()
            assert len(data) == view["bytes"] and digest(data) == view["sha256"]
        assert len(events) == len(calls) * 2 + len(views)
        starts, ends = {}, {}
        by_file = {c[k]: c["id"] for c in calls for k in ("stdout_file", "stderr_file")}
        for position, event in enumerate(events):
            if event["kind"] == "request-start":
                assert event["id"] not in starts
                starts[event["id"]] = position
            elif event["kind"] == "request":
                assert event["id"] in starts and event["id"] not in ends
                ends[event["id"]] = position
            else:
                assert event["kind"] == "view"
                if event["source"] in by_file:
                    assert by_file[event["source"]] in ends
        assert set(starts) == set(ends) == {c["id"] for c in calls}
        assert {e["file"] for e in events if e["kind"] == "view"} == {v["file"] for v in views}
        assert json.loads((trial / "post-task.json").read_text())["question"] == (root / "post-task-question.txt").read_text().strip()
        measurements[label] = measure(root, trial, replay)
    assert measurements == json.loads((root / "measurements.json").read_text())
    worked = subprocess.run(["jq", "-c", "-f", str(root / "worked-selection.jq"), str(root / "preflight/command.out")], capture_output=True, check=True).stdout
    assert worked == (root / "worked-selection.json").read_bytes()
    assert sorted(facet["key"] for facet in json.loads(worked)["facets"]) == ["dependency-depth", "dependency-target", "depends"]
    print("Four frozen trial inventories, inputs, preflight, raw identity, jq, event order and measurements passed.")
    print(json.dumps(measurements, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", nargs="?", type=Path, default=Path(__file__).parent)
    main(parser.parse_args().root)
