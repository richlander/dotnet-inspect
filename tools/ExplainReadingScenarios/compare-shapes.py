#!/usr/bin/env python3
"""Compare self-contained style shapes derived from actual owner data."""
import argparse
import copy
import hashlib
import json
from pathlib import Path
import subprocess

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--browser-json", required=True, type=Path)
parser.add_argument("--output", required=True, type=Path)
parser.add_argument("--cli", help="optionally capture current compact CLI whole-task cost")
args = parser.parse_args()
args.output.mkdir(parents=True, exist_ok=True)
source_bytes = args.browser_json.read_bytes()
source = json.loads(source_bytes)
if any(d["severity"] == "Error" for d in source["diagnostics"]):
    raise ValueError("owner inspection failed")
vs = {v["identity"]["value"]: v for v in source["content"]["vocabularies"]}
if len(vs) != len(source["content"]["vocabularies"]):
    raise ValueError("duplicate vocabulary identity")
choices_source = vs["csharp.style-choices"]
tiers_source = vs["csharp.style-tiers"]


def term_record(term, vocabulary):
    """Lower only this complete declared domain; reject unsupported observations."""
    record = {"id": term["identity"]["value"], "name": term["displayLabel"],
              "summary": term["summary"]}
    entries = {e["map"]["value"]: e["values"] for e in term["mapEntries"]}
    if len(entries) != len(term["mapEntries"]):
        raise ValueError("duplicate property observation")
    for declaration in vocabulary["maps"]:
        key = declaration["identity"]["value"]
        if key in record or declaration["coverage"] != "Complete":
            raise ValueError("property collision or incomplete owner coverage")
        values = entries.pop(key)
        cardinality = declaration["cardinality"]
        if cardinality not in ("ExactlyOne", "OptionalOne") or len(values) > 1:
            raise ValueError("this experiment requires scalar-valued style properties")
        if not values:
            if cardinality != "OptionalOne":
                raise ValueError("missing required property")
            record[key] = None  # Declared complete OptionalOne: absent, not unavailable.
            continue
        value = values[0]
        target = declaration["target"]
        if value["kind"] == "term":
            identity = value["identity"]
            if (target["kind"] != "terms" or target["reference"]["kind"] != "local"
                    or identity["vocabulary"]["catalog"] != source["content"]["catalog"]
                    or identity["vocabulary"]["value"] != target["reference"]["vocabulary"]["value"]):
                raise ValueError("term value crosses its declared local vocabulary")
            record[key] = identity["value"]
        elif target["kind"] == "scalar":
            expected = {"Text": "text", "Boolean": "boolean", "Integer": "integer"}[target["scalarKind"]]
            if value["kind"] != expected:
                raise ValueError("value kind disagrees with its declaration")
            record[key] = value["value"]
        else:
            raise ValueError("unsupported value shape")
    if entries:
        raise ValueError("undeclared property observations")
    return record


choices = [term_record(term, choices_source) for term in choices_source["terms"]]
tiers = {term["identity"]["value"]: term_record(term, tiers_source) for term in tiers_source["terms"]}
if (len(choices) != 17 or len(tiers) != 4 or len(tiers) != len(tiers_source["terms"])
        or len({c["id"] for c in choices}) != len(choices)):
    raise ValueError("unexpected production witness population")
if any(choice["tier"] not in tiers for choice in choices):
    raise ValueError("missing local tier record")

# Identity scope, population completeness, and interpretation are supplied once.
# No HAL choice is required to run any of the six critical queries.
common = {"format_version": 1, "catalog": source["content"]["catalog"]["value"],
          "source_snapshot": source["content"]["identity"]["value"],
          "vocabularies": {}, "selection": {"populations": ["choices", "tiers"], "completeness": "Complete"}}
for name, vocabulary in [("choices", choices_source), ("tiers", tiers_source)]:
    common["vocabularies"][name] = {
        "id": vocabulary["identity"]["value"], "name": vocabulary["displayLabel"],
        "summary": vocabulary["summary"],
        "properties": vocabulary["maps"]}

shared = {**common, "choices": choices, "tiers": tiers}
flat_choices = copy.deepcopy(choices)
for choice in flat_choices:
    choice["tier"] = tiers[choice["tier"]]
# Retain otherwise unrepresented tiers; this also preserves empty-tier meaning.
flat = {**common, "choices": flat_choices, "tiers": tiers}
indexed = {**common, "choices": {c["id"]: c for c in choices},
           "choice_order": [c["id"] for c in choices], "tiers": tiers,
           "tier_choices": {tier: [c["id"] for c in choices if c["tier"] == tier] for tier in tiers},
           "conflict_groups": {group: [c["id"] for c in choices if c["conflict_group"] == group]
                               for group in sorted({c["conflict_group"] for c in choices if c["conflict_group"] is not None})}}
shapes = {"flat": flat, "shared": shared, "indexed": indexed}
report = {"source_sha256": hashlib.sha256(source_bytes).hexdigest(),
          "source_bytes": len(source_bytes), "choices": len(choices), "tiers": len(tiers),
          "shapes": {}, "tasks": {}}
if args.cli:
    retrieved = []
    for vocabulary in ["csharp.style-choices", "csharp.style-tiers"]:
        payload = subprocess.run([args.cli, "explain", "vocabularies/" + vocabulary,
                                  "--depth", "1", "--json"], check=True,
                                 capture_output=True).stdout
        (args.output / (vocabulary + "-compact.json")).write_bytes(payload)
        retrieved.append(len(payload))
    report["compact_baseline"] = {
        "version": subprocess.run([args.cli, "--version"], check=True, capture_output=True).stdout.decode().strip(),
        "grouped_menu_retrieved_bytes": sum(retrieved), "requests": 2,
        "per_request_bytes": retrieved}
queries = Path(__file__).resolve().parent / "shapes"
for name, shape in shapes.items():
    data = json.dumps(shape, separators=(",", ":"), ensure_ascii=False).encode() + b"\n"
    (args.output / (name + ".json")).write_bytes(data)
    report["shapes"][name] = {"bytes": len(data), "requests": 1}
    # Recover every original style/tier property, description, and sequence.
    if name == "flat":
        recovered = [{**c, "tier": c["tier"]["id"]} for c in shape["choices"]]
    elif name == "indexed":
        recovered = [shape["choices"][identity] for identity in shape["choice_order"]]
    else:
        recovered = shape["choices"]
    if recovered != choices or shape["tiers"] != tiers or shape["vocabularies"] != common["vocabularies"]:
        raise ValueError("core data was lost in " + name)
    for task in ["menu", "safe", "conflicts", "tier", "known-choice", "grouped-menu"]:
        argv = ["jq", "-c", "--arg", "task", task, "--arg", "tier", "Synthesis",
                "--arg", "id", "slot-local-names", "-f", str(queries / (name + ".jq"))]
        result = subprocess.run(argv, input=data, check=True, capture_output=True).stdout
        answer = json.loads(result)
        if task in report["tasks"] and answer != report["tasks"][task]["answer"]:
            raise ValueError("query disagreement: " + name + "/" + task)
        report["tasks"][task] = {"answer_bytes": len(result), "answer": answer}
        (args.output / (name + "-" + task + ".json")).write_bytes(result)
# A synthetic empty tier checks the meaning lost by grouping only observed choices.
empty = {"id": "Empty", "name": "Synthetic empty tier", "summary": "Boundary probe",
         "order": 5, "byte_divergent": False}
for name, shape in shapes.items():
    boundary = copy.deepcopy(shape)
    boundary["tiers"]["Empty"] = empty
    if name == "indexed":
        boundary["tier_choices"]["Empty"] = []
    result = subprocess.run(["jq", "-c", "--arg", "task", "grouped-menu", "--arg", "tier", "Empty",
                             "--arg", "id", "slot-local-names", "-f", str(queries / (name + ".jq"))],
                            input=json.dumps(boundary).encode(), check=True, capture_output=True).stdout
    if json.loads(result)[-1] != {"tier": empty, "choices": []}:
        raise ValueError("empty tier disappeared in " + name)
report["empty_tier_boundary"] = "all three candidates retain an empty tier in the grouped menu"
# Cross-check the original four tasks against the original browser jq adapter.
original_queries = {"menu": "style-menu", "safe": "style-safe", "conflicts": "style-conflicts", "tier": "style-tier"}
for task, filename in original_queries.items():
    result = subprocess.run(["jq", "-c", "-L", str(queries.parent), "--arg", "format", "browser",
                             "--arg", "tier", "Synthesis", "-f", str(queries.parent / (filename + ".jq"))],
                            input=source_bytes, check=True, capture_output=True).stdout
    if json.loads(result) != report["tasks"][task]["answer"]:
        raise ValueError("owner reading disagreement: " + task)
(args.output / "comparison.json").write_text(json.dumps(report, indent=2) + "\n")
print(json.dumps({"shapes": report["shapes"], "tasks": {k: v["answer_bytes"] for k, v in report["tasks"].items()}}, indent=2))
