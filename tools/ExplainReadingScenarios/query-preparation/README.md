# Query-preparation reading proposal

Status: **experimental JSON and executable reading scenarios** for
[#9975](https://github.com/richlander/dotnet-inspect/issues/9975), under
[#9762](https://github.com/richlander/dotnet-inspect/issues/9762).
No example changes an installed command, website descriptor, or shipped skill.
Resource Explanation owns the
[proposed selection contract](../../../docs/design/resource-explanation.md#query-preparation-reading-selection).

The question comes before the format: what must an agent read to prepare a
valid query? For Package Query, the selected core is facet keys and meaning,
operators, operand kinds, examples, listed values, acquisition effects,
registered input rules, required-context records, and the applicable host's
syntax and exposed terms. Every selected facet retains those semantic fields.
Display names, receipt IDs, member counts, and repeated navigation are omitted.
The catalog remains the complete knowledge source; an individual read need not
return it all. Empty registered rules do not establish an unrestricted domain.

## Provenance and reproduction

[source-hal.json](source-hal.json) is the retained, unedited NativeAOT output of:

```bash
dotnet-inspect package query --explain --json
```

Source commit: `88500f2d8298e3ede5640c9284ad00ddbdb86c15` (#9966).
NativeAOT binary SHA-256:
`87967997dfc3643ed273927525bd2404b60a55288c50e1778e6ef96de36377d2`.
Response SHA-256:
`291faaf34918bb9150312009e70a4ecdb372118e80a4938f64bfeb8eda8927df`.
This is a pinned historical reading asset, rather than a claim about the latest
binary. The motivating result asset is the published package
`Microsoft.Extensions.Http.Polly` 10.0.12, whose direct dependency evidence
appears in the [landed trials](../usability/package-query-adoption/README.md).
This experiment prepares commands; it performs no package searches or acquisition.

From the repository root, with Python 3 and jq:

```bash
python3 tools/ExplainReadingScenarios/query-preparation/project.py \
  --output /tmp/explain-query-preparation
diff -ru tools/ExplainReadingScenarios/query-preparation/examples \
  /tmp/explain-query-preparation
```

The runner hashes the source, closes declared required-context relationships,
recovers every selected semantic field against the original response, and runs
five jq questions against each proposed representation. The preparation and
context answers also agree with the relevant original records. A missing
required target is rejected. Generated files must match the checked-in examples.
These are local design gates, not product regression tests or agent trials.

## Equivalent knowledge, different vocabulary

[Direct catalog JSON](examples/catalog-direct.json) uses keyed `facets` and
`bindings` tables. [HAL catalog JSON](examples/catalog-hal.json) places the same
records under `_embedded.facets` and `_embedded.bindings`; each resource has a
local `key`. Root `_links` appears first. All examples are minified, as a
machine would receive them; `jq .` makes them easier to browse.

`scope` identifies `package-query/query`, selected-facet completeness, and the
original catalog size. Keys inherit that scope. Binding `exposed_facets` is the
one small membership index needed to distinguish authorable inspection terms
from selector or context records. Its list is intersected with the selection;
it does not assert that unselected terms are unsupported. Facet order follows
the source, and required-context order is preserved.

The fixed experimental reading profile omits known empty array properties.
Inside a complete selected record only, missing `examples`, `effects`,
`input_rules`, `values`, or `requires` means the registered array is empty.
The projector never applies that convention to an unselected record or a
non-available observation. Operators remain present. It refuses incomplete or
non-available source data rather than manufacturing these examples from it.
Production adoption must preserve explicit outcomes for those cases.

Shared prerequisite records and binding rules appear once. Short acquisition
strings stay inline because that makes the main preparation query simpler;
this proposal does not normalize every repeated scalar into a lookup table.
It also retains all six binding rules, including library rules in a dependency
selection. Filtering unstructured rule prose is outside this proposal.

The HAL examples advertise just one `collection` link, to the existing full
HAL dataset. This destination is implemented, but returns the current larger
shape. These generated selections have no implemented self address; no tiny
response endpoint or new CLI spelling is implied. There are no per-facet self
links, backlinks to an embedded binding, repeated exposure links, or automatic
schema fetch. Root state qualifies the population; core records are embedded.
HAL adds navigation vocabulary rather than additional domain facts.

## Worked agent exploration

Task: **Find Microsoft.Extensions packages with direct dependencies whose IDs
start with Polly, using dependency target net10.0 and at most 40 candidates.**
The agent has read the shallow skill and knows how to use explain, HAL, and jq.
These steps operate on the proposed reading files, not a new installed terminal.

### Discover the relevant facets

On receiving the full HAL catalog, the agent reads exposed terms and their
summaries locally:

```bash
jq '._embedded as $data | $data.bindings[0].exposed_facets as $keys
  | [$data.facets[] | select(.key as $key | $keys | index($key))
     | {key, summary}]' \
  tools/ExplainReadingScenarios/query-preparation/examples/catalog-hal.json
```

That inventory includes `depends`, `dependency-target`, and `dependency-depth`.
The agent selects `depends` for the direct package-ID relationship and inspects
its supported operator before deciding whether depth is necessary.

### Read exactly the preparation data

[direct-hal.json](examples/direct-hal.json) contains `depends`,
`dependency-target`, and the CLI binding. A host-side narrowing operation could
return this selection before jq runs. The current offline experiment produces
it from the full source; it does not save retrieval work today.

```bash
jq '._embedded | {facets: [.facets[] | {key, summary, operators,
      value_kind, input_rules, effects}],
    binding: (.bindings[0] | {gesture, input_rules, exposed_facets})}' \
  tools/ExplainReadingScenarios/query-preparation/examples/direct-hal.json
```

The issued rules answer the important choices: `starts-with` matches direct
dependency package-ID prefixes, `dependency-target` accepts the requested TFM,
and a starts-with predicate alone cannot admit depth. The binding supplies the
actual `--where "key starts-with value"` syntax. It also distinguishes the
candidate work bound `--take` from the final row selection `-n`.

The resulting command is shown without executing a package search:

```bash
dotnet-inspect package query 'Microsoft.Extensions*' \
  --where 'depends starts-with Polly' \
  --where 'dependency-target=net10.0' --take 40 --json
```

The direct JSON file answers the same questions with `.facets.depends`,
`.facets["dependency-target"]`, and `.bindings.cli`. No link request, contract
request, or confirmation-only query is needed for this preparation.

### Resolve context and distinguish missing knowledge

For a different task, [depth-hal.json](examples/depth-hal.json) includes exact
`depends`, `dependency-depth`, and its declared `dependency-target` context.
Read required context with the reusable adapter:

```bash
jq --arg form hal --arg task context \
  -f tools/ExplainReadingScenarios/query-preparation/read.jq \
  tools/ExplainReadingScenarios/query-preparation/examples/depth-hal.json
```

The rules retain the exact-match prerequisite, target restriction, allowed
maximum depths 2/3/4, and five-candidate bound. They distinguish this depth from
both explanation graph depth and result row count. The selection is sufficient
for that stated preparation question; it is not a generic semantic validator.
Conditional dependencies in prose are not invented as unconditional graph edges.
The [literal selection](examples/literal-hal.json) similarly embeds
`library-target` and retains the CLI `--tfm` gesture, exact operand bounds, and
five-candidate bound.

Suppose the task changes to require a license predicate after the agent has
received only the direct-dependency selection:

```bash
jq --arg form hal --arg task missing \
  -f tools/ExplainReadingScenarios/query-preparation/read.jq \
  tools/ExplainReadingScenarios/query-preparation/examples/direct-hal.json
```

The answer is `{"key":"license","selected":false,"status":"unselected"}`.
The facet is unknown in this selection; it is not unsupported or an empty
observation. That new question justifies following `collection`:

```bash
href=$(jq -r '._links.collection.href' \
  tools/ExplainReadingScenarios/query-preparation/examples/direct-hal.json)
dotnet-inspect explain "$href" --json
```

That implemented destination currently returns the full existing HAL dataset.
The agent would then read the license facet locally. A future keyed selection
could reduce that transfer, but no such admission is assumed here. Reading the
same link merely to verify the dependency answer has no data benefit.

## Measurements and adoption

Sizes are uncompressed UTF-8, minified with one trailing newline. The original
terminal response is 18,770 B; minifying that same response yields 18,509 B.
These are serialized-content measurements, not latency or performance claims.

| Selected population | Direct JSON | HAL | HAL overhead |
| --- | ---: | ---: | ---: |
| All 19 facets and CLI binding | 8,198 B | 8,450 B | 252 B |
| Direct dependency plus target | 2,170 B | 2,320 B | 150 B |
| Exact dependency plus depth and target | 3,028 B | 3,184 B | 156 B |
| Literal plus required target | 1,972 B | 2,122 B | 150 B |

The full proposed HAL is 54% smaller than the minified current HAL without
narrowing the facet population. The focused direct-dependency selection is
87% smaller, with a different population. HAL/direct differences are modest
when both carry equivalent task knowledge. This establishes that the prior
4x gap is not inherent to HAL; it does not establish better agent behavior.
The selection remains larger than an individual jq answer because it includes
all registered constraints, effects, and host rules needed for local decisions.

The proposed next phase has four counted slices: this owner proposal and
examples; shared selection implementation and cross-host gates; CLI adoption
and retirement of its superseded ordinary reading projection; Browser/Wasm
adoption and retirement of its superseded reading projection. Rich website
authoring descriptors and explicit contract inspection remain available.
Any CLI `-Q` change is a separate owner adoption of this selection, not a new
fact inventory. Agent testing follows production adoption and measures total
retrieval, valid preparation, and necessary navigation. The examples establish
neither an optimal minimum nor coverage of unregistered validation rules.

## Shared production projection

The shared implementation under
[#9982](https://github.com/richlander/dotnet-inspect/issues/9982) now lowers
existing query `.data` and `.hal` selections through the reading profile.
The examples above remain the pinned proposal experiment; their exact sizes
are not claims about the current installed output.

Production scope includes `profile: query-preparation/v1`, the owner-issued
query-space identity, the requested routing root, selected completeness, and
catalog count. Direct JSON additionally retains `facet_order`; binding tables
use owner-issued binding identities and retain consumer kind. HAL has one
root self link for implemented exact selections and a collection link for
facet selections. All selected facets, including a facet root, appear under
`_embedded.facets` once. No member links or automatic contract links are emitted.

For example, the installed reading commands are:

```bash
dotnet-inspect package query --explain --json
dotnet-inspect explain package-query/query .data --json
dotnet-inspect explain package-query/query/facets/library-literal .hal --json
```

The reusable `read.jq` adapter accepts both these records and the historical
examples. Shared key narrowing is an API capability; no CLI narrowing syntax
or Browser query-explanation entrance is implied by this implementation.
