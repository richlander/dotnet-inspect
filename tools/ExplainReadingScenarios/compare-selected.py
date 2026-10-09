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
            if relation == "curies":
                continue
            if relation not in ("self", "describedby") and not relation.startswith("inspect:"):
                raise AssertionError("Custom HAL relations must be qualified")
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
context = follow_hal(focused["_links"]["inspect:required-context"][0])
assert context["key"] == "library-target"
assert all("library-target" not in binding["exposed_facets"]
           for binding in focused["_embedded"]["inspect:bindings"])
report["agent_navigation"] = {"authored_destination_paths": 0, "hal_mode_preserved": True,
                               "facet": focused["key"], "required_context": context["key"]}
report["navigation"] = {"unique_hal_links_followed": len(all_hrefs)}
(args.output / "comparison.json").write_text(json.dumps(report, indent=2) + "\n")
print(json.dumps(report, indent=2))
