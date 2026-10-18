#!/usr/bin/env python3
"""Replay a guide-only follow-up; reuse baseline measurement/format readers."""
import argparse
import difflib
import importlib.util
import json
from pathlib import Path
import re
import subprocess

prior_path = Path(__file__).resolve().parent.parent / "narrowed" / "verify.py"
spec = importlib.util.spec_from_file_location("narrowed_replay", prior_path)
baseline_replay = importlib.util.module_from_spec(spec)
spec.loader.exec_module(baseline_replay)
digest = baseline_replay.digest
records = baseline_replay.records
measure = baseline_replay.measure
emitted_readings = baseline_replay.emitted_readings


def comparison(current, previous):
    keys = ("requests", "guidance_requests", "guidance_retrieved_bytes", "guidance_rendered_bytes", "retrieved_with_guide_bytes", "rendered_with_guide_bytes", "failed_requests", "jq_errors", "format_violations")
    return {label: {key: {"before": previous[label][key], "after": current[label][key], "delta": current[label][key] - previous[label][key]} for key in keys} for label in current}


def main(root):
    dispatch = json.loads((root / "dispatch.json").read_text())
    manifest = json.loads((root / "manifest.json").read_text())
    assert digest((root / "dispatch.json").read_bytes()) == manifest["dispatch_sha256"]
    assert dispatch["source_head"] == "e701f56d4e3964ca47ed50c424b166ae7b4c9670"
    assert dispatch["sdk"] == "11.0.100-rc.1.26425.128"
    assert dispatch["model"] == "gpt-6-sol" and dispatch["reasoning"] == "high"
    assert len(dispatch["trials"]) == 6
    assert sorted(map(tuple, dispatch["trials"].values())) == sorted((task, mode) for task in ("package", "api", "vocabulary") for mode in ("hal", "data"))
    for name in ("package-task.txt", "api-task.txt", "post-task-question.txt"):
        assert (root / name).read_bytes() == (root.parent / "command-bound" / name).read_bytes()
    for receipt in json.loads((root / "preflight/receipts.json").read_text()):
        assert receipt["exit"] == receipt["expected_exit"]
        for channel in ("stdout", "stderr"):
            data = (root / "preflight" / (receipt["name"] + (".out" if channel == "stdout" else ".err"))).read_bytes()
            assert len(data) == receipt[channel + "_bytes"] and digest(data) == receipt[channel + "_sha256"]
    for inventory in (dispatch["inputs"], manifest["inputs"]):
        for name, expected in inventory.items():
            assert digest((root / name).read_bytes()) == expected, name
    measurements = {}
    for label, (task, mode) in dispatch["trials"].items():
        trial = root / label
        assert {p.name for p in trial.iterdir() if p.is_file()} == set(manifest["artifacts"][label])
        assert digest((trial / "config.json").read_bytes()) == dispatch["trial_configs"][label]
        assert (trial / "guide.txt").read_bytes() == (root / ("guide-" + mode + ".txt")).read_bytes()
        freeze = json.loads((trial / "frozen.json").read_text())
        files = sorted(p for p in trial.iterdir() if p.name in ("answer.json", "calls.jsonl", "views.jsonl", "events.jsonl") or p.name.startswith(("response-", "view-")))
        assert {p.name: digest(p.read_bytes()) for p in files} == freeze["files"], label
        for name, expected in manifest["artifacts"][label].items():
            assert digest((trial / name).read_bytes()) == expected, (label, name)
        calls, views, events = (records(trial / name) for name in ("calls.jsonl", "views.jsonl", "events.jsonl"))
        assert sorted(c["id"] for c in calls) == list(range(1, len(calls) + 1))
        assert len(calls) <= 30 and sum(v["operation"] != "guide" for v in views) <= 60
        for call in calls:
            argv = call["argv"]
            assert call["category"] in ("guidance", "inspection")
            for prefix in ("stdout", "stderr", "source_stdout"):
                data = (trial / call[prefix + "_file"]).read_bytes()
                assert len(data) == call[prefix + "_bytes"]
            assert (trial / call["stdout_file"]).read_bytes() == (trial / call["source_stdout_file"]).read_bytes()
            assert call["removed_links"] == []
            discovery = argv[0] == "explain" or "--help" in argv or "-Q" in argv or "--query-help" in argv or "--explain" in argv
            assert call["category"] == ("guidance" if discovery else "inspection")
            if not discovery:
                if task == "api":
                    assert "--platform" in argv
                elif argv[:2] == ["package", "query"]:
                    assert argv[2].startswith("Microsoft.Extensions")
                    assert "--take" in argv and 1 <= int(argv[argv.index("--take") + 1]) <= 40
        for view in views:
            op = view["operation"]
            if op == "guide":
                data = (root / ("guide-" + mode + ".txt")).read_bytes()
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
                    # jq includes the response's absolute path; preserve captured
                    # bytes and compare the reproduced diagnostic apart from it.
                    def portable_error(value):
                        return re.sub(rb"\(at .*?:([0-9]+)\)", rb"(at RESPONSE:\1)", value)
                    assert portable_error(run.stderr) == portable_error(data)
                else:
                    data = run.stdout
            assert len(data) == view["bytes"] and digest(data) == view["sha256"]
            assert data == (trial / view["file"]).read_bytes()
        assert len(events) == len(calls) * 2 + len(views)
        starts, ends = {}, {}
        active = None
        by_file = {c[k]: c["id"] for c in calls for k in ("stdout_file", "stderr_file")}
        for position, event in enumerate(events):
            if event["kind"] == "request-start":
                assert active is None, (label, "overlapping requests")
                active = event["id"]
                assert event["id"] not in starts
                starts[event["id"]] = position
            elif event["kind"] == "request":
                assert active == event["id"], (label, "request ordering")
                active = None
                assert event["id"] in starts and event["id"] not in ends
                ends[event["id"]] = position
            else:
                assert event["kind"] == "view"
                assert active is None, (label, "view during request")
                if event["source"] in by_file:
                    assert by_file[event["source"]] in ends
        assert set(starts) == set(ends) == {c["id"] for c in calls}
        assert {e["file"] for e in events if e["kind"] == "view"} == {v["file"] for v in views}
        post = json.loads((trial / "post-task.json").read_text())
        assert post["question"] == (root / "post-task-question.txt").read_text().strip()
        measurements[label] = measure(calls, views)
        readings = emitted_readings(trial, calls)
        measurements[label]["emitted_readings"] = readings
        measurements[label]["format_violations"] = sum(r["format"] != mode for r in readings)
        for call in calls:
            assert call["primary_explanation_requested"] == ("--explain" in call["argv"])
        assert views[0]["operation"] == "guide"
        assert sum(v["operation"] == "guide" for v in views) == 1
    assert measurements == json.loads((root / "measurements.json").read_text())
    prior = root.parent / "narrowed"
    baseline = json.loads((prior / "dispatch.json").read_text())
    assert dispatch["cli_sha256"] == baseline["cli_sha256"]
    assert (root / "platform.json").read_bytes() == (prior / "platform.json").read_bytes()
    for name in ("participant.txt", "package-task.txt", "api-task.txt", "vocabulary-task.txt", "post-task-question.txt", "runner.py"):
        assert (root / name).read_bytes() == (prior / name).read_bytes()
    expected_changes = []
    for mode in ("hal", "data"):
        old = (prior / ("guide-" + mode + ".txt")).read_text()
        new = (root / ("guide-" + mode + ".txt")).read_text()
        expected_changes.extend(difflib.unified_diff(old.splitlines(True), new.splitlines(True), fromfile="narrowed/guide-" + mode + ".txt", tofile="bootstrap-reuse/guide-" + mode + ".txt"))
        assert len(new.encode()) == dispatch["guide_bytes"][mode]
        assert len(new.encode()) - len(old.encode()) == 466
    assert "".join(expected_changes) == (root / "guide-change.patch").read_text()
    assert comparison(measurements, json.loads((prior / "measurements.json").read_text())) == json.loads((root / "comparison.json").read_text())
    for mode, paths in (("hal", ["vocabulary-hal/response-02.out", "vocabulary-hal/response-03.out"]), ("data", ["vocabulary-data/response-03.out", "preflight/vocabulary-data-selected.out"])):
        for path in paths:
            run = subprocess.run(["jq", "-c", "-f", str(root / ("worked-cache-" + mode + ".jq")), str(root / path)], capture_output=True, check=True)
            assert run.stdout == (root / ("worked-cache-" + mode + ".json")).read_bytes()
    observations = json.loads((root / "reading-observations.json").read_text())
    assert observations["vocabulary-data"]["off_profile_detail_requests"] == [4, 5, 6]
    for number in observations["vocabulary-data"]["off_profile_detail_requests"]:
        data = json.loads((root / "vocabulary-data" / ("response-%02d.out" % number)).read_text())
        assert data["scope"] == "Installed" and data["identity"]["type"] in ("value-vocabulary", "vocabulary-value")
    assert observations["api-data"]["warning"] == (root / "api-data/response-08.err").read_text().strip()
    assert records(root / "api-data/calls.jsonl")[-1]["exit"] == 1
    print("Six trial inventories, raw identity, jq, event order, input pins, and measurements passed.")
    print(json.dumps(measurements, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", nargs="?", type=Path, default=Path(__file__).parent)
    root = parser.parse_args().root
    # Historical evidence is replayed offline, never rerun against live NuGet.
    baseline_replay.main(root.resolve().parent / "narrowed")
    main(root)
