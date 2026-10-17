# Compact query reading

These are production output examples of Resource Explanation's
[`query-preparation/v2` reading profile](../../../docs/design/resource-explanation.md#query-preparation-reading-selection).
The full catalog is still available, but website picker metadata is outside
this profile. Execution class, option-source identity and value descriptions
remain useful reading knowledge. The website explicitly consumes the richer
presentation profile from the same selected owner declarations.

## Worked agent preparation

Question: find Microsoft.Extensions packages with direct Polly dependencies
for net10.0, inspecting at most 40 candidates. An agent that knows the two
relevant keys starts with the selected reading:

```bash
dotnet-inspect package query --explain -Q depends,dependency-target \
  > query.json
jq '._embedded | {
  facets: [.facets[] | {key, summary, operators, value_kind,
    input_rules, execution_class, requires,
    target: .value_descriptions["net10.0"]}],
  binding: (.bindings[0] | {gesture, input_rules, exposed_facets})
}' query.json
```

The response in [dependency-hal.json](examples/dependency-hal.json) is 3,631
bytes, versus 4,469 for the previous profile. The jq view retains the full
registered rules and requested value meaning. The agent can author:

```bash
dotnet-inspect package query 'Microsoft.Extensions*' \
  --where 'depends starts-with Polly' \
  --where 'dependency-target=net10.0' --take 40 --json
```

This page does not execute that network query or claim an exhaustive result.
The issued rules explain direct versus traversed dependencies, the restrictions
on combining prefix matching with depth, and the distinction between candidate
and final row limits. An agent saves the response and reads additional fields
with jq only if needed; a collection link adds no knowledge about these already
selected facets.

When the keys are unknown, save the full catalog once and read its summaries:

```bash
dotnet-inspect package query --explain > catalog.json
jq '._embedded.facets | map({key, summary})' catalog.json
```

Then use jq on that saved complete document for preparation instead of issuing
a narrower copy. `-Q` narrows the first request when the keys are already known.
Neither this example nor smaller output proves that agents will consistently
choose this workflow; that requires another fresh-agent evaluation.

## Direct and HAL examples

| Selection | Direct JSON | HAL | Selected knowledge |
| --- | --- | --- | --- |
| Full catalog | [catalog-data.json](examples/catalog-data.json) | [catalog-hal.json](examples/catalog-hal.json) | All 19 registered facets and the CLI binding |
| Exact depends resource | [depends-data.json](examples/depends-data.json) | [depends-hal.json](examples/depends-hal.json) | `depends`; it does not silently select dependency-target |
| Exact literal resource | [literal-data.json](examples/literal-data.json) | [literal-hal.json](examples/literal-hal.json) | `library-literal` and its required library-target context |

The paired files use `explain <resource> .data/.hal --json` and carry equivalent
selected facts. The additional dependency HAL file uses command-local `-Q` to
select both depends and dependency-target explicitly. A complete selected
population does not imply that unselected facets or all validation rules are
known. Missing optional reading fields have the fixed-profile meaning described
by the owner; missing records remain unknown.

`value_descriptions` is a native-value lookup, for example:

```bash
jq '.facets["dependency-target"].value_descriptions["net10.0"]' \
  examples/catalog-data.json
```

HAL places that same lookup inside the corresponding embedded facet. This
preserves whole registered summaries without repeated picker labels, control
objects or null grouping fields. The array of listed `values` remains separate;
neither table is an exhaustive allow list unless the owner declares that rule.

## Reproduce

Run from the repository root with a source or NativeAOT executable:

```bash
python3 tools/ExplainReadingScenarios/query-reading/verify.py \
  /path/to/dotnet-inspect --baseline /path/to/base/dotnet-inspect
```

`--write` refreshes examples deliberately. The verifier invokes seven installed
explanation reads without package acquisition, compares exact example bytes,
checks equivalent data and HAL tables, and compares v2 knowledge with v1 after
applying only the declared presentation boundary. It preserves every core
field, prerequisite, binding, related operation, execution class, external
option source and complete value summary. It does not trim rule prose.

These payload sizes measure emitted UTF-8 bytes, not model tokens or latency.
Only bytes actually displayed from a saved response contribute to the separate
presented-content proxy used by the agent trials. Product gates are
`QueryPreparationSelectionTests`, `ResourceExplanationCommandTests`,
`PackageQueryExplanationTests`, `BrowserPackageQueryExplanationTests` and
`BrowserPackageQueryOperationsTests`; this local verifier supplements them.
