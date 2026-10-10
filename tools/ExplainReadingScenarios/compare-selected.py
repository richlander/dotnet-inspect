#!/usr/bin/env python3
"""Exercise actual selected CLI output against the independently checked specimens."""
import argparse
import json
from pathlib import Path
import subprocess

HERE = Path(__file__).resolve().parent
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--cli", required=True, type=Path, help="Built CLI apphost; set DOTNET_ROOT for managed builds")
parser.add_argument("--output", required=True, type=Path)
args = parser.parse_args()
args.output.mkdir(parents=True, exist_ok=True)


def jq(document, query, **variables):
    command = ["jq", "-c"]
    for key, value in variables.items():
        command += ["--arg", key, value]
    command += ["-f", str(query)]
    return json.loads(subprocess.run(command, input=json.dumps(document), text=True,
                                    capture_output=True, check=True).stdout)


def hrefs(value):
    if isinstance(value, dict):
        for relation, links in value.get("_links", {}).items():
            if relation not in ("self", "describedby", "vocabularies", "required-context", "bindings", "exposed-facets"):
                raise AssertionError("Unexpected reading relation: " + relation)
            for link in links if isinstance(links, list) else [links]:
                yield link["href"]
        for key, item in value.items():
            if key != "_links":
                yield from hrefs(item)
    elif isinstance(value, list):
        for item in value:
            yield from hrefs(item)


report = {}
all_hrefs = set()
for name, path, tasks, variables, prototype, reference_query in [
    ("style", "vocabularies/csharp.style-choices",
     ["menu", "safe", "conflicts", "tier", "known-choice", "grouped-menu", "endorsed"],
     {"tier": "Lens", "id": "slot-local-names"}, "style-sparse.json", HERE / "shapes/sparse.jq"),
    ("facets", "package-query/query",
     ["discover", "find-literal", "inspect", "context", "closed-values"],
     {"key": "library-literal"}, "package-query-facets.json", HERE / "facets/explore.jq"),
    ("facet", "package-query/query/facets/library-literal", ["inspect", "context"],
     {"key": "library-literal"}, "package-query-facets.json", HERE / "facets/explore.jq"),
]:
    specimens = {}
    sizes = {}
    for mode in ["data", "hal"]:
        result = subprocess.run([str(args.cli.resolve()), "explain", path, "." + mode, "--json"],
                                text=True, capture_output=True, check=True)
        specimens[mode] = json.loads(result.stdout)
        sizes[mode] = len(result.stdout.encode())
        (args.output / f"selected-{name}-{mode}.json").write_text(
            json.dumps(specimens[mode], indent=2, ensure_ascii=False) + "\n")
    assert "_links" in specimens["hal"] and "_embedded" in specimens["hal"], name
    assert "facts" not in specimens["hal"] and "identity" not in specimens["hal"], name
    assert list(specimens["hal"]).index("_links") < list(specimens["hal"]).index("_embedded"), name
    reference = json.loads((HERE / "examples" / prototype).read_text())
    query = HERE / ("selected-style.jq" if name == "style" else "selected-facets.jq")
    answers = {}
    for task in tasks:
        expected = jq(reference, reference_query, task=task, **variables)
        for mode in specimens:
            answer = jq(specimens[mode], query, task=task, **variables)
            assert answer == expected, (name, mode, task)
        answers[task] = len(json.dumps(expected, separators=(",", ":"), ensure_ascii=False).encode())
    all_hrefs.update(hrefs(specimens["hal"]))
    report[name] = {"bytes_including_newline": sizes, "answer_bytes": answers,
                    "tasks_equal_to_independent_specimen": len(tasks)}

# Newly registered rules must survive both projections independently of the older probes.
direct = json.loads((args.output / "selected-facet-data.json").read_text())
hal = json.loads((args.output / "selected-facet-hal.json").read_text())
operand = direct["facets"]["library-literal"]["facts"]["input-rules"]
host = direct["bindings"]["dotnet-inspect.cli/package-query"]["facts"]["input-rules"]
assert operand == hal["input_rules"]
assert host == hal["_embedded"]["bindings"][0]["input_rules"]
assert any("not an allow list" in rule for rule in operand)
assert any("1..1024 UTF-16" in rule for rule in operand)
assert any("ordinal substring" in rule for rule in operand)
assert any("--tfm TFM, not --where" in rule for rule in host)
assert any("--json" in rule for rule in host)
assert any("--take N" in rule for rule in host)
report["registered_input_rules"] = {"operand_rules": len(operand), "host_rules": len(host),
                                    "equal_across_projections": True}
prepared_direct = jq(direct, HERE / "query-preparation.jq", key="library-literal")
prepared_hal = jq(hal, HERE / "query-preparation.jq", key="library-literal")
assert prepared_direct == prepared_hal
assert prepared_hal["data_scope"]["completeness"] == "Complete"
(args.output / "query-preparation.json").write_text(json.dumps(prepared_hal, indent=2) + "\n")
report["query_preparation"] = {"equal_across_projections": True, "scope_retained": True,
    "answer_bytes_including_newline": len(json.dumps(prepared_hal, separators=(",", ":"), ensure_ascii=False).encode()) + 1}

# A transitive query needs both prerequisites and their operand rules locally.
transitive = {}
transitive_sizes = {}
for mode in ["data", "hal"]:
    result = subprocess.run([str(args.cli.resolve()), "explain",
                             "package-query/query/facets/depends-transitive", "." + mode, "--json"],
                            text=True, capture_output=True, check=True)
    transitive[mode] = json.loads(result.stdout)
    transitive_sizes[mode] = len(result.stdout.encode())
    (args.output / f"selected-transitive-{mode}.json").write_text(
        json.dumps(transitive[mode], indent=2, ensure_ascii=False) + "\n")
prepared_transitive = {mode: jq(value, HERE / "query-preparation.jq", key="depends-transitive")
                       for mode, value in transitive.items()}
assert prepared_transitive["data"] == prepared_transitive["hal"]
answer = prepared_transitive["hal"]
assert answer["data_scope"]["completeness"] == "Complete"
assert answer["facet"]["requires"] == ["dependency-target", "dependency-depth"]
context = {facet["key"]: facet for facet in answer["context"]}
assert context["dependency-depth"]["values"] == ["2", "3", "4"]
assert context["dependency-depth"]["requires"] == ["dependency-target"]
assert not context["dependency-target"]["requires"]
assert any("all is not accepted" in rule for rule in answer["facet"]["input_rules"])
assert any("depends-ecosystem, or dependencies" in rule
           for rule in context["dependency-target"]["input_rules"])
assert all(set(binding["exposed_facets"]) == set(context) | {"depends-transitive"}
           for binding in answer["bindings"])
(args.output / "transitive-preparation.json").write_text(json.dumps(answer, indent=2) + "\n")
report["transitive_preparation"] = {"bytes_including_newline": transitive_sizes,
    "answer_bytes_including_newline": len(json.dumps(answer, separators=(",", ":"), ensure_ascii=False).encode()) + 1,
    "equal_across_projections": True, "locally_resolved_prerequisites": len(context)}
all_hrefs.update(hrefs(transitive["hal"]))

# Depth supports exact depends or the legacy spelling, so it cannot require both.
depth = {}
depth_sizes = {}
for mode in ["data", "hal"]:
    result = subprocess.run([str(args.cli.resolve()), "explain",
                             "package-query/query/facets/dependency-depth", "." + mode, "--json"],
                            text=True, capture_output=True, check=True)
    depth[mode] = json.loads(result.stdout)
    depth_sizes[mode] = len(result.stdout.encode())
    (args.output / f"selected-depth-{mode}.json").write_text(
        json.dumps(depth[mode], indent=2, ensure_ascii=False) + "\n")
prepared_depth = {mode: jq(value, HERE / "query-preparation.jq", key="dependency-depth")
                  for mode, value in depth.items()}
assert prepared_depth["data"] == prepared_depth["hal"]
answer = prepared_depth["hal"]
assert answer["facet"]["requires"] == ["dependency-target"]
assert answer["facet"]["values"] == ["2", "3", "4"]
assert any("exact depends (eq) or legacy depends-transitive" in rule
           for rule in answer["facet"]["input_rules"])
assert [facet["key"] for facet in answer["context"]] == ["dependency-target"]
(args.output / "depth-preparation.json").write_text(json.dumps(answer, indent=2) + "\n")
report["depth_preparation"] = {"bytes_including_newline": depth_sizes,
    "answer_bytes_including_newline": len(json.dumps(answer, separators=(",", ":"), ensure_ascii=False).encode()) + 1,
    "equal_across_projections": True, "locally_resolved_prerequisites": 1}
all_hrefs.update(hrefs(depth["hal"]))


# A HAL-aware client can follow these without understanding our facet/vocabulary layout.
# This verifies resolution, not a performance improvement or automatic query planning.
for href in sorted(all_hrefs):
    subprocess.run([str(args.cli.resolve()), "explain", href, "--json"],
                   text=True, capture_output=True, check=True)
# Start at an entry resource and navigate only advertised links. No destination paths are authored here.
entry = json.loads((args.output / "selected-facets-hal.json").read_text())
literal = next(resource for resources in entry["_embedded"].values() for resource in resources
               if resource["kind"] == "query-facet" and "string-literal" in resource.get("summary", ""))
def follow_hal(link):
    assert link["type"] == "application/hal+json"
    response = subprocess.run([str(args.cli.resolve()), "explain", link["href"], "--json"],
                              text=True, capture_output=True, check=True)
    document = json.loads(response.stdout)
    assert "_links" in document and "_embedded" in document
    return document
focused = follow_hal(literal["_links"]["self"])
context = follow_hal(focused["_links"]["required-context"][0])
assert context["key"] == "library-target"
assert all("library-target" not in binding["exposed_facets"]
           for binding in focused["_embedded"]["bindings"])
report["agent_navigation"] = {"authored_destination_paths": 0, "hal_mode_preserved": True,
                               "facet": focused["key"], "required_context": context["key"]}
report["navigation"] = {"unique_hal_links_followed": len(all_hrefs)}
(args.output / "comparison.json").write_text(json.dumps(report, indent=2) + "\n")
print(json.dumps(report, indent=2))
