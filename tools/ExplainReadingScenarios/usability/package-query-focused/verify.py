#!/usr/bin/env python3
"""Replay focused-reading trial evidence; domain/task assessment stays manual."""
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import subprocess


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def load_measurements(root):
    spec = importlib.util.spec_from_file_location(
        "adoption_replay", root.parent / "package-query-adoption/verify.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module, module.load_replay(root)


def measure(root, trial):
    adoption, replay = load_measurements(root)
    result = adoption.measure(root, trial, replay)
    result["guide_inclusive_rendered_bytes"] = result["rendered_bytes"] + result["guide_bytes"]
    return result


def main(root):
    dispatch = json.loads((root / "dispatch.json").read_text())
    manifest = json.loads((root / "manifest.json").read_text())
    assert digest(root / "dispatch.json") == manifest["dispatch_sha256"]
    assert dispatch["source_head"] == "df4c7de7a7963b3b2e44c8146d0d341ccae3b73e"
    assert dispatch["sdk"] == "11.0.100-rc.1.26425.128"
    assert dispatch["model"] == "gpt-6-sol" and dispatch["reasoning"] == "high"
    assert dispatch["launch_waves"] == [["reader-1", "reader-2"], ["reader-3", "reader-4"]]
    assert dispatch["guides"] == {"reader-1": "prior", "reader-2": "focused", "reader-3": "focused", "reader-4": "prior"}
    for inventory in (dispatch["inputs"], manifest["inputs"]):
        for name, expected in inventory.items():
            assert digest(root / name) == expected, name
    prior = (root / "guide-prior.txt").read_bytes()
    assert prior == (root.parent / "package-query-adoption/guide.txt").read_bytes()
    assert (root / "guide-focused.txt").read_bytes() == prior + (root / "reading-hint.txt").read_bytes()
    assert (root / "post-task-question.txt").read_bytes() == (root.parent / "package-query-adoption/post-task-question.txt").read_bytes()
    receipts = json.loads((root / "preflight/receipts.json").read_text())
    assert len(receipts) == 10
    for receipt in receipts:
        assert receipt["exit"] == (1 if receipt["name"] in ("tips", "seed", "root-output") else 0)
        for channel, suffix in (("stdout", ".out"), ("stderr", ".err")):
            path = root / "preflight" / (receipt["name"] + suffix)
            assert path.stat().st_size == receipt[channel + "_bytes"]
            assert digest(path) == receipt[channel + "_sha256"]
    command = (root / "preflight/command.out").read_bytes()
    for name in ("command-json", "root-hal", "offline"):
        assert command == (root / "preflight" / (name + ".out")).read_bytes()
    binding = json.loads(command)["_embedded"]["bindings"][0]
    assert any('--where "key starts-with value" for starts-with' in rule for rule in binding["input_rules"])
    _, replay = load_measurements(root)
    measurements = {}
    frozen_times = []
    retrospective_times = []
    for label, guide in dispatch["guides"].items():
        trial = root / label
        assert digest(trial / "config.json") == dispatch["trial_configs"][label]
        assert (trial / "guide.txt").read_bytes() == (root / ("guide-" + guide + ".txt")).read_bytes()
        assert (trial / "guide.txt").stat().st_size == dispatch["guide_bytes"][guide]
        assert {p.name for p in trial.iterdir() if p.is_file()} == set(manifest["artifacts"][label])
        for name, expected in manifest["artifacts"][label].items():
            assert digest(trial / name) == expected, (label, name)
        original = [p for p in trial.iterdir() if p.name in ("answer.json", "calls.jsonl", "views.jsonl", "events.jsonl") or p.name.startswith(("response-", "view-"))]
        frozen = json.loads((trial / "frozen.json").read_text())
        assert {p.name: digest(p) for p in original} == frozen["files"]
        frozen_times.append(frozen["frozen_at"])
        calls, views, events = (replay.records(trial / name) for name in ("calls.jsonl", "views.jsonl", "events.jsonl"))
        assert len(calls) <= 30 and sum(v["operation"] != "guide" for v in views) <= 60
        assert [c["id"] for c in calls] == list(range(1, len(calls) + 1))
        for call in calls:
            discovery = call["argv"][0] == "explain" or any(a in call["argv"] for a in ("--help", "-Q", "--query-help", "--explain"))
            assert call["category"] == ("guidance" if discovery else "inspection")
            for prefix in ("stdout", "stderr", "source_stdout"):
                assert (trial / call[prefix + "_file"]).stat().st_size == call[prefix + "_bytes"]
            assert (trial / call["stdout_file"]).read_bytes() == (trial / call["source_stdout_file"]).read_bytes()
            assert call["removed_links"] == []
        for view in views:
            op = view["operation"]
            if op == "guide":
                data = (trial / "guide.txt").read_bytes()
            elif op == "text":
                data = (trial / view["source"]).read_bytes()
            else:
                error = op.startswith("jq error: ")
                assert error or op.startswith("jq: ")
                query = op[len("jq error: ") if error else len("jq: "):]
                run = subprocess.run(["jq", "-c", query, str(trial / view["source"])], capture_output=True)
                assert bool(run.returncode) == error
                data = (trial / view["file"]).read_bytes() if error else run.stdout
                if error:
                    normalize = lambda value: re.sub(rb"\(at .*?:([0-9]+)\)", rb"(at RESPONSE:\1)", value)
                    assert normalize(run.stderr) == normalize(data)
            assert data == (trial / view["file"]).read_bytes()
            assert len(data) == view["bytes"] and hashlib.sha256(data).hexdigest() == view["sha256"]
        assert len(events) == 2 * len(calls) + len(views)
        starts, ends = set(), set()
        by_file = {c[k]: c["id"] for c in calls for k in ("stdout_file", "stderr_file")}
        for event in events:
            if event["kind"] == "request-start":
                assert event["id"] not in starts
                starts.add(event["id"])
            elif event["kind"] == "request":
                assert event["id"] in starts and event["id"] not in ends
                ends.add(event["id"])
            else:
                assert event["kind"] == "view"
                if event["source"] in by_file:
                    assert by_file[event["source"]] in ends
        assert starts == ends == {c["id"] for c in calls}
        assert {e["file"] for e in events if e["kind"] == "view"} == {v["file"] for v in views}
        post = json.loads((trial / "post-task.json").read_text())
        assert post["question"] == (root / "post-task-question.txt").read_text().strip()
        retrospective_times.append(post["asked_at"])
        measurements[label] = measure(root, trial)
    assert max(frozen_times) < min(retrospective_times)
    assert measurements == json.loads((root / "measurements.json").read_text())
    print("Four trial pins/inventories, raw identity, local jq, chronology, frozen originals and byte arithmetic passed.")
    print(json.dumps(measurements, indent=2))


if __name__ == "__main__":
    main(Path(__file__).resolve().parent)
