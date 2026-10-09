# Explain reading scenarios

These executable jq probes ask what an agent needs to read before choosing an
operation or refining a query. They compare the website's existing vocabulary
inspection with PR #9774's compact explanation. They measure retrieved content
separately from filtered answers; running jq locally does not reduce retrieval.

Browse the [checked-in JSON examples](examples/README.md) to compare the proposed
style shapes and read the complete facet document without running the tools.

Normative owner: [Resource Explanation](../../docs/design/resource-explanation.md#query-meaning-and-evidence).
Claim: representative reading tasks must expose traversal complexity, answer
size, total retrieved bytes, and extra requests before a lowering earns its
agent-readability claim. The original probes retain their comparison gestures; the selected-data
comparison below exercises explicit `.data` and `.hal` selections.

## Run

Requires the repository SDK, Python 3, jq, and a CLI built from the compact
projection candidate. From the worktree root:

```bash
dotnet run --project tools/ExplainReadingScenarios/CaptureBrowser.csproj \
  -c Release -- /tmp/explain-reading-browser.json
python3 tools/ExplainReadingScenarios/run.py \
  --cli /path/to/compact/dotnet-inspect \
  --browser-json /tmp/explain-reading-browser.json \
  --output /tmp/explain-reading-results
cat /tmp/explain-reading-results/report.json
```

The capture tool invokes the browser export's pure managed serialization, as
its existing tests do. It does not execute Wasm. The runner captures fresh CLI
JSON, executes every jq filter, compares style answers across representations,
and checks the complete root-declared choice target population against the
expansion. All answers and a report are written outside the repository.

To see the cost of one query directly:

```bash
jq -L tools/ExplainReadingScenarios --arg format compact \
  -f tools/ExplainReadingScenarios/style-safe.jq \
  /tmp/explain-reading-results/style.json
jq -L tools/ExplainReadingScenarios --arg format browser \
  -f tools/ExplainReadingScenarios/style-safe.jq \
  /tmp/explain-reading-browser.json
```

## Questions and measured answers

Measured against product `0.27.0+5ce2e00`, NativeAOT CLI head
`5ce2e00e92c3fb9f1a7b22631cd6457011990b8b`. The browser export is the same
unchanged production serializer consumed by the settings UI. Sizes below are
UTF-8 JSON including the jq output newline, before compression; these are
functional reading measurements, not latency results.

| Question | Query | Answer | Rows | Retrieved / requests |
| --- | --- | ---: | ---: | --- |
| What style choices exist, what do they mean, and which tier owns each? | `style-menu.jq` | 4,889 B | 17 | Compact: 43,136 B / 1; browser: 32,060 B / 1 |
| Which choices preserve IL bytes, and are they oracle-endorsed? | `style-safe.jq` | 1,537 B | 15 | Compact: 43,136 B / 1; browser: 32,060 B / 1 |
| Which choices are mutually exclusive? | `style-conflicts.jq` | 317 B | 2 groups | Compact: 43,136 B / 1; browser: 32,060 B / 1 |
| Which selections belong to Synthesis, and which option/value do I supply? | `style-tier.jq` | 239 B | 2 | Compact: 43,136 B / 1; browser: 32,060 B / 1 |
| How do I use the literal facet, and what context does it require? | `facet-usage.jq` | 303 B | 1 facet | 2,661 B / 1 |
| Where can I navigate to the listed body kinds, and is that list complete? | `body-navigate.jq` | 6,505 B | 72 links | 29,357 B / 1 |
| What does the listed AwaitExpression value mean? | `value-usage.jq` | 166 B | 1 value | 30,467 B / 2 |

Every style answer agrees across representations. The root choice relationship
has complete targets, with all 17 choices embedded exactly once. The overall
traversal remains depth-truncated; it has not expanded all relationships.
Neither observation cancels the other. Empty facet `values` does not prove an
open domain; the probe reports listed values without inventing acceptance.

The value-detail task starts with a vocabulary resource, follows an emitted
link unchanged, and reads the owner-issued identity and description. A caller
already holding that exact link avoids the vocabulary fetch; the measured
30,467 B deliberately includes discovery rather than concealing its cost.

## Traversal and unused content

The proposed style shape requires joining root relationship target identities
to a heterogeneous `_embedded.resources` array, then turning `map-entries`
arrays of tagged values into lookup objects. The current browser shape instead
selects two vocabularies by identity, unwraps term identities, and performs
similar map-entry lookups. Neither supplies directly selectable choice
properties such as `tier` or `byte_divergent`. `reading.jq` keeps that work
visible rather than presenting the normalized helper output as the wire shape.

The compact style response contains 14,336 B of root and expanded fact content;
28,800 B of the 43,136 B response is outside those original fact objects. These
queries do not consume most qualified identity repetitions, public-address
records, repeated target/link descriptions, or traversal bookkeeping. The
fact objects are **not a valid replacement response**: identity, navigation, outcomes,
and completeness have independent semantic obligations. Their presence or
location should be justified per selection rather than discarded globally.

Likewise, the menu does not use option/value, conflict, or endorsement data,
while other scenarios do. Map declaration descriptions and cardinality/coverage
are not displayed in the answers, but interpretation and completeness checks
depend on their contract. No field is declared globally obsolete just because
one query ignores it. Full declarations remain available through `.contract`.

The [self-contained shape experiment](shapes/README.md) now starts with core
style data and six critical queries, compares repetition, shared records, and
indexes, and checks whole-task bytes and requests. It keeps the complete style
reading task separate from a concise summary, tips, or reusable reference.
The 239 B tier answer is useful selection evidence, not a promised product
payload budget.

The [worked facet demo](facets/README.md) follows an agent from discovering
exposed query terms through operand inspection, required-context lookup, and
host invocation. Its executable prototype also identifies constraint and host
binding facts that the current catalog does not yet expose.

## Contextual gaps

The runner also probes the three requested Type gestures against the actual
candidate: bare `--explain`, `.tips`, and `.reference`. All currently fail with
`Unrecognized option '--explain'` and empty stdout. There is no Type JSON to
query yet. Do not replace this gap with invented successful examples or use it
as evidence that tips or references are small. Admission and bounded gestures
remain owned by [Contextual Resource Explanation](../../docs/design/contextual-resource-explanation.md).

## Actual selected-data comparison

The shared assembler now emits the selected data directly from the validated
catalog. These [actual CLI specimens](examples/selected/) are separate from the
smaller design prototypes. Typed identities, source addresses, observation
states, and full property declarations explain the remaining size difference.
The assembler uses declared maps; it does not hardcode style flags or tiers.

```bash
python3 tools/ExplainReadingScenarios/compare-selected.py \
  --cli artifacts/bin/dotnet-inspect/release/dotnet-inspect \
  --output /tmp/explain-selected-comparison
```

For managed apphosts, set `DOTNET_ROOT` to the repository SDK directory. The
comparison captures fresh production-shaped output, checks all 14 reading tasks
in both formats against the independent specimens, strips HAL links to prove
state equality, and follows every unique HAL link through the CLI. It does not
execute a package query or acquire package content.

| Selected reading dataset | Direct JSON | HAL | Reading tasks |
| --- | ---: | ---: | ---: |
| All 17 style choices and 4 tier descriptions | 17,578 B | 27,360 B | 7 |
| All 19 package-query facets and the CLI binding | 14,559 B | 20,730 B | 5 |
| Literal facet, required target context, and exposing binding | 2,897 B | 3,962 B | 2 |

Sizes are UTF-8 bytes of minified managed Release CLI output, including its final
newline. Pretty checked-in examples are larger. These are content measurements,
not NativeAOT timing evidence. The prototype's 9,918 B style model omits typed
resource metadata that this first implementation preserves. Further reduction
should justify sharing that metadata rather than quietly dropping its meaning.

All reading answers are identical. HAL adds 9,782 B to the style dataset and
6,171 B to the complete facet dataset. Its demonstrated benefit is navigation:
a HAL-aware client recognizes `self` and relation links without knowing where
our vocabulary or facet records live. All 44 unique links resolve unchanged.
That benefit does not simplify the data-reading jq queries or make the document
smaller. This evidence supports direct JSON independently; it does not establish
HAL as the preferred default.

### Worked facet exploration

An agent can discover the 14 terms the CLI actually exposes from a single local
dataset. Five other catalog facets are not automatically CLI query terms.

```bash
dotnet-inspect explain package-query/query .data --json > facets.json
jq --arg task discover --arg key library-literal \
  -f tools/ExplainReadingScenarios/selected-facets.jq facets.json
jq --arg task find-literal --arg key library-literal \
  -f tools/ExplainReadingScenarios/selected-facets.jq facets.json
```

After identifying `library-literal`, an agent can retrieve just that facet,
its required context, and the binding that exposes it:

```bash
dotnet-inspect explain package-query/query/facets/library-literal \
  .data --json > literal.json
jq --arg task inspect --arg key library-literal \
  -f tools/ExplainReadingScenarios/selected-facets.jq literal.json
jq --arg task context --arg key library-literal \
  -f tools/ExplainReadingScenarios/selected-facets.jq literal.json
```

The context answer names `library-target` and reports
`exposed_as_query_term: false`. It therefore does not suggest placing
`library-target` in `--where`. The current catalog still does not issue the
literal length/cardinality constraints or the `--tfm` mapping. An agent must
consult the [literal term contract](../../docs/design/package-query-library-literal.md#term-contract)
before constructing that invocation; neither representation fills that gap.

To use HAL, substitute `.hal`. The same jq filters work. A HAL client can also
read links without knowing the document's application layout:

```bash
jq '[.. | objects | ._links? // empty | to_entries[]
  | {relation: .key, links: .value}]' literal-hal.json
```

Custom relation names are absolute URNs carrying schema owner, schema, resource
type, and relationship identity. Links point to the existing resource
explanation address; following one requests resource details. They do not imply
that query execution, validation constraints, or a selected dataset is present.
