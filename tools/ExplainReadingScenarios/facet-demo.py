#!/usr/bin/env python3
"""Capture actual facets and demonstrate a proposed self-contained reading view."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--cli", required=True)
parser.add_argument("--output", required=True, type=Path)
args = parser.parse_args()
args.output.mkdir(parents=True, exist_ok=True)


def capture(path, depth):
    data = subprocess.run([args.cli, "explain", path, "--depth", str(depth), "--json"],
                          check=True, capture_output=True).stdout
    result = json.loads(data)
    if result["fact_states"]:
        raise ValueError("experiment requires available root facts")
    return data, result


def key_of(identity):
    return json.dumps(identity, sort_keys=True, separators=(",", ":"))


def complete_relation(resource, name):
    relationship = resource["relationships"][name]
    if relationship["state"] != "Available" or relationship["completeness"] != "Complete":
        raise ValueError("selected relationship population is incomplete: " + name)
    return relationship["targets"]


catalog_bytes, catalog = capture("package-query/query", 1)
binding_bytes, binding = capture("package-query/bindings/cli", 0)
(args.output / "current-query.json").write_bytes(catalog_bytes)
(args.output / "current-binding.json").write_bytes(binding_bytes)
resources = {key_of(r["identity"]): r for r in catalog["_embedded"]["resources"]}
if len(resources) != len(catalog["_embedded"]["resources"]):
    raise ValueError("duplicate expanded identity")
ordered_resources = [resources[key_of(t["identity"])] for t in complete_relation(catalog, "query-facet")]
if len(ordered_resources) != 19 or len(resources) != 19:
    raise ValueError("unexpected real Query Space facet population")
identities_to_keys = {key_of(r["identity"]): r["facts"]["key"] for r in ordered_resources}
if len(set(identities_to_keys.values())) != 19:
    raise ValueError("facet keys collide inside this query space")
first = ordered_resources[0]["identity"]
scope = {key: first[key] for key in ("owner", "schema", "type")}
facets = {}
for resource in ordered_resources:
    identity = resource["identity"]
    if any(identity[key] != scope[key] for key in scope) or resource["fact_states"]:
        raise ValueError("this experiment requires one scoped facet family and available facts")
    facts = resource["facts"]
    # Retain every observed facet fact; change only field presentation.
    renames = {"identity": "fact_identity", "value-kind": "value_kind", "values": "listed_values"}
    record = {renames.get(key, key): value for key, value in facts.items()}
    record["identity"] = identity["value"]
    record["href"] = resource["_links"]["self"]["href"]
    if "required-context" in resource["relationships"]:
        record["requires"] = [identities_to_keys[key_of(target["identity"])]
                              for target in complete_relation(resource, "required-context")]
    recovered = {key: record[renames.get(key, key)] for key in facts}
    if recovered != facts:
        raise ValueError("facet fact content changed during lowering")
    facets[facts["key"]] = record
exposed = [identities_to_keys[key_of(t["identity"])] for t in complete_relation(binding, "exposes")]
if len(exposed) != 14 or "library-literal" not in exposed or "library-target" in exposed:
    raise ValueError("CLI exposure does not match the actual owner binding")
if facets["library-literal"]["requires"] != ["library-target"]:
    raise ValueError("literal context differs from the owner relationship")
document = {"format_version": 1,
            "selection": {"populations": ["query-facets", "cli-exposure"], "completeness": "Complete"},
            "query_space": {"identity": catalog["identity"], "fact_identity": catalog["facts"]["identity"],
                            **{key: value for key, value in catalog["facts"].items() if key != "identity"}},
            "facet_scope": scope,
            "facet_order": [r["facts"]["key"] for r in ordered_resources],
            "binding": {"identity": binding["identity"], "kind": binding["facts"]["consumer-kind"],
                        "gesture": binding["facts"]["gesture"], "exposed_facets": exposed},
            "facets": facets}
data = json.dumps(document, separators=(",", ":"), ensure_ascii=False).encode() + b"\n"
(args.output / "facet-document.json").write_bytes(data)
report = {"version": subprocess.run([args.cli, "--version"], check=True, capture_output=True).stdout.decode().strip(),
          "current_retrieved_bytes": len(catalog_bytes) + len(binding_bytes), "current_requests": 2,
          "prototype_bytes": len(data), "proposed_requests": 1,
          "facets": len(facets), "exposed_query_terms": len(exposed),
          "source_sha256": {"catalog": hashlib.sha256(catalog_bytes).hexdigest(),
                            "binding": hashlib.sha256(binding_bytes).hexdigest()}, "tasks": {}}
query = Path(__file__).resolve().parent / "facets" / "explore.jq"
for task in ["discover", "find-literal", "inspect", "context", "closed-values"]:
    answer = subprocess.run(["jq", "-c", "--arg", "task", task, "--arg", "key", "library-literal",
                             "-f", str(query)], input=data, check=True, capture_output=True).stdout
    (args.output / (task + ".json")).write_bytes(answer)
    report["tasks"][task] = {"answer_bytes": len(answer), "answer": json.loads(answer)}
if report["tasks"]["find-literal"]["answer"][0]["key"] != "library-literal":
    raise ValueError("discovery did not find the expected literal facet")
if report["tasks"]["context"]["answer"] != [{"key": "library-target", "value_kind": "NuGet target framework",
                                               "examples": ["net10.0"], "exposed_as_query_term": False}]:
    raise ValueError("context query confused required context with exposed authoring")
# Verify these two planning refusals without issuing an acquisition-capable query.
report["planning_refusals"] = []
for where, tfm in [("library-literal=https://", []), ("library-target=net8.0", ["--tfm", "net8.0"])]:
    command = [args.cli, "package", "query", "Microsoft.Azure.SignalR", "--where", where, *tfm, "--json"]
    result = subprocess.run(command, capture_output=True)
    if result.returncode == 0 or result.stdout:
        raise ValueError("expected a pre-acquisition planning refusal")
    report["planning_refusals"].append({"command": command[1:], "exit_code": result.returncode,
                                        "stderr": result.stderr.decode().strip()})
(args.output / "report.json").write_text(json.dumps(report, indent=2) + "\n")
print(json.dumps({key: report[key] for key in ["version", "current_retrieved_bytes", "prototype_bytes", "facets", "exposed_query_terms", "planning_refusals"]}, indent=2))
