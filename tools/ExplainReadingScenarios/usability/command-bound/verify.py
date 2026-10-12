#!/usr/bin/env python3
"""Replay frozen command-bound trial evidence; not an answer scorer."""
import argparse
import hashlib
import json
import re
from pathlib import Path
import subprocess


def digest(data):
    return hashlib.sha256(data).hexdigest()


def records(path):
    return [json.loads(line) for line in path.read_text().splitlines()]


def measure(calls, views):
    result = {}
    for category in ("guidance", "inspection"):
        selected = [c for c in calls if c["category"] == category]
        result[category + "_requests"] = len(selected)
        result[category + "_retrieved_bytes"] = sum(c["stdout_bytes"] + c["stderr_bytes"] for c in selected)
        response_names = {c[k] for c in selected for k in ("stdout_file", "stderr_file")}
        result[category + "_rendered_bytes"] = sum(v["bytes"] for v in views if v["source"] in response_names)
    result.update({
        "requests": len(calls),
        "retrieved_bytes": sum(c["stdout_bytes"] + c["stderr_bytes"] for c in calls),
        "rendered_bytes": sum(v["bytes"] for v in views if v["operation"] != "guide"),
        "product_views": sum(v["operation"] != "guide" for v in views),
        "guide_bytes": sum(v["bytes"] for v in views if v["operation"] == "guide"),
        "failed_requests": sum(c["exit"] != 0 for c in calls),
        "jq_errors": sum(v["operation"].startswith("jq error:") for v in views),
        "selected_hal_requests": sum(c["argv"][0] == "explain" and (".hal" in c["argv"] or any("projection=hal" in a for a in c["argv"])) for c in calls),
        "selected_data_requests": sum(c["argv"][0] == "explain" and (".data" in c["argv"] or any("projection=data" in a for a in c["argv"])) for c in calls),
        "contract_requests": sum(".contract" in c["argv"] or any("projection=contract" in a for a in c["argv"]) for c in calls),
        "help_requests": sum("--help" in c["argv"] for c in calls),
    })
    result.update({
        "root_explain_requests": sum(c["argv"][0] == "explain" for c in calls),
        "query_help_requests": sum("-Q" in c["argv"] or "--query-help" in c["argv"] for c in calls),
        "primary_explanation_requests": sum(c["primary_explanation_requested"] for c in calls),
        "companion_requests": sum(c["companion_requested"] is not None for c in calls),
        "companion_stderr_bytes": sum(c["stderr_bytes"] for c in calls if c["companion_requested"] is not None),
        "inspection_stderr_bytes": sum(c["stderr_bytes"] for c in calls if c["category"] == "inspection"),
    })
    return result


def main(root):
    dispatch = json.loads((root / "dispatch.json").read_text())
    manifest = json.loads((root / "manifest.json").read_text())
    assert digest((root / "dispatch.json").read_bytes()) == manifest["dispatch_sha256"]
    for inventory in (dispatch["inputs"], manifest["inputs"]):
        for name, expected in inventory.items():
            assert digest((root / name).read_bytes()) == expected, name
    for receipt in json.loads((root / "preflight/receipts.json").read_text()):
        for channel in ("stdout", "stderr"):
            file = root / "preflight" / (receipt["name"] + (".out" if channel == "stdout" else ".err"))
            assert len(file.read_bytes()) == receipt[channel + "_bytes"]
            assert digest(file.read_bytes()) == receipt[channel + "_sha256"]
    pairs = [("extensions-ordinary", "extensions-tips"), ("member-ordinary", "member-companion"), ("member-ordinary", "member-tips-companion")]
    for ordinary, companion in pairs:
        assert (root / "preflight" / (ordinary + ".out")).read_bytes() == (root / "preflight" / (companion + ".out")).read_bytes()
    measurements = {}
    for label, task in dispatch["trials"].items():
        trial = root / label
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
            assert ".data" not in argv and not any("projection=data" in a for a in argv)
            assert call["primary_explanation_requested"] == ("--explain" in argv)
            expected_companion = (argv[argv.index("-E") + 1] if "-E" in argv and argv.index("-E") + 1 < len(argv) and argv[argv.index("-E") + 1].startswith(".") else "complete") if "-E" in argv else None
            assert call["companion_requested"] == expected_companion
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
        post = json.loads((trial / "post-task.json").read_text())
        assert post["question"] == (root / "post-task-question.txt").read_text().strip()
        measurements[label] = measure(calls, views)
    assert measurements == json.loads((root / "measurements.json").read_text())
    print("Four trial inventories, raw identity, jq, event order, input pins, and measurements passed.")
    print(json.dumps(measurements, indent=2))


if __name__ == "__main__":
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("root", nargs="?", type=Path, default=Path(__file__).parent)
    main(p.parse_args().root)
