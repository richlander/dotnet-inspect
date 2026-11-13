#!/usr/bin/env python3
"""Run reading probes; emit actual answers and byte/cardinality evidence."""
import argparse
import json
import re
from pathlib import Path
import subprocess

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--cli", required=True, help="compact-projection CLI executable")
parser.add_argument("--browser-json", required=True, type=Path,
                    help="unmodified CatalogExports.InspectVocabulary() JSON")
parser.add_argument("--output", required=True, type=Path)
args = parser.parse_args()
queries = Path(__file__).resolve().parent
args.output.mkdir(parents=True, exist_ok=True)


def invoke(argv):
    return subprocess.run(argv, check=True, capture_output=True).stdout


def query(name, data, **variables):
    argv = ["jq", "-c", "-L", str(queries)]
    for key, value in variables.items():
        argv.extend(["--arg", key, value])
    argv.extend(["-f", str(queries / (name + ".jq"))])
    answer = subprocess.run(argv, input=data, check=True, capture_output=True).stdout
    return answer, json.loads(answer)


inputs = {"browser": args.browser_json.read_bytes()}
for name, path, depth in [
    ("style", "vocabularies/csharp.style-choices", 1),
    ("facet", "package-query/query/facets/library-literal", 0),
    ("body", "vocabularies/csharp.body-kinds", 0),
]:
    inputs[name] = invoke([args.cli, "explain", path, "--depth", str(depth), "--json"])
    (args.output / (name + ".json")).write_bytes(inputs[name])
browser = json.loads(inputs["browser"])
if any(d["severity"] == "Error" for d in browser["diagnostics"]):
    raise RuntimeError("browser vocabulary inspection failed")
style_data = json.loads(inputs["style"])
relation = style_data["relationships"]["vocabulary-value"]
if relation["state"] != "Available" or relation["completeness"] != "Complete":
    raise RuntimeError("style choice target population is not complete")
choice_ids = [target["identity"] for target in relation["targets"]]
expanded_ids = [resource["identity"] for resource in style_data["_embedded"]["resources"]]
if any(expanded_ids.count(identity) != 1 for identity in choice_ids):
    raise RuntimeError("a style choice is missing or repeated in the expansion")

report = {"version": invoke([args.cli, "--version"]).decode().strip(),
          "input_bytes": {key: len(value) for key, value in inputs.items()},
          "scenarios": []}
for scenario, extra in [("style-menu", {}), ("style-safe", {}),
                        ("style-conflicts", {}), ("style-tier", {"tier": "Synthesis"})]:
    browser_bytes, browser = query(scenario, inputs["browser"], format="browser", **extra)
    compact_bytes, compact = query(scenario, inputs["style"], format="compact", **extra)
    if browser != compact:
        raise RuntimeError("browser/compact disagreement in " + scenario)
    (args.output / (scenario + ".json")).write_bytes(compact_bytes)
    report["scenarios"].append({"task": scenario, "answer_bytes": len(compact_bytes),
                                "rows": len(compact), "cross_representation_equal": True,
                                "requests": 1})
if len(json.loads((args.output / "style-menu.json").read_bytes())) != 17:
    raise RuntimeError("expected the pinned 17-choice production witness")
for scenario, source in [("facet-usage", "facet"), ("body-navigate", "body")]:
    answer_bytes, answer = query(scenario, inputs[source])
    (args.output / (scenario + ".json")).write_bytes(answer_bytes)
    report["scenarios"].append({"task": scenario, "answer_bytes": len(answer_bytes),
                                "requests": 1})

# Follow a returned link for one actual listed value; never decode opaque keys.
body = json.loads(inputs["body"])
links = body["_links"]["vocabulary-value"]
href = next(link["href"] for link in links if link["href"].endswith("/awaitexpression"))
value_payload = invoke([args.cli, "explain", href, "--json"])
answer_bytes, answer = query("value-usage", value_payload)
if answer["value"] != "AwaitExpression":
    raise RuntimeError("navigation did not resolve the expected owner-issued value")
(args.output / "value-usage.json").write_bytes(answer_bytes)
report["scenarios"].append({"task": "value-usage", "answer_bytes": len(answer_bytes),
                            "requests": 2,
                            "retrieved_bytes": len(inputs["body"]) + len(value_payload)})

# Count the original UTF-8 fact object spans without changing escaping or wrappers.
# This diagnostic is not a contract; identity/navigation/outcomes remain necessary.
style = json.loads(inputs["style"])
style_text = inputs["style"].decode()
decoder = json.JSONDecoder()
fact_bytes = []
for match in re.finditer(r'"facts"\s*:\s*', style_text):
    _, end = decoder.raw_decode(style_text, match.end())
    fact_bytes.append(len(style_text[match.end():end].encode()))
if len(fact_bytes) != 1 + len(style["_embedded"]["resources"]):
    raise RuntimeError("unexpected fact object count in the production witness")
report["style_fact_content_bytes"] = sum(fact_bytes)
report["style_other_bytes"] = len(inputs["style"]) - sum(fact_bytes)
report["style_expanded_resources"] = len(style["_embedded"]["resources"])
report["style_traversal"] = style["traversal"]
report["placement_probes"] = []
for projection in [[], [".tips"], [".reference"]]:
    command = [args.cli, "type", "System.Text.Json.JsonSerializer", "--explain", *projection]
    result = subprocess.run(command, capture_output=True)
    report["placement_probes"].append({"command": command[1:], "exit_code": result.returncode,
                                        "stdout_bytes": len(result.stdout),
                                        "stderr": result.stderr.decode().strip()})
# A failure here is an admission observation, never a fabricated jq success fixture.
report_path = args.output / "report.json"
report_path.write_text(json.dumps(report, indent=2) + "\n")
print(report_path)
