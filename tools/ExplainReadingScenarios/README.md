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

The shared assembler emits reading data from the validated catalog. The
[actual CLI specimens](examples/selected/) include independently usable direct
JSON and a HAL projection with its own resource layout. The HAL projection
uses the checked-out `core` release-notes prototype's approach: meaningful root
state, early navigation, embedded resources, and deeper detail on demand.

```bash
python3 tools/ExplainReadingScenarios/compare-selected.py \
  --cli artifacts/bin/dotnet-inspect/release/dotnet-inspect \
  --output /tmp/explain-selected-comparison
```

For managed apphosts, set `DOTNET_ROOT` to the repository SDK directory. The
comparison checks all 14 reading tasks in both layouts against independent
specimens, follows every unique non-template HAL link, and explores the literal
facet and its required context using advertised links only. It does not
execute a package query or acquire package content.

| Selected reading dataset | Direct JSON | HAL | Reading tasks |
| --- | ---: | ---: | ---: |
| All 17 style choices and 4 tier descriptions | 17,578 B | 14,938 B | 7 |
| All 19 package-query facets and the CLI binding | 14,559 B | 15,173 B | 5 |
| Literal facet, required target context, and exposing binding | 2,897 B | 2,397 B | 2 |

Sizes are UTF-8 minified managed Release CLI output including its newline.
Pretty examples are larger. These are content measurements, not NativeAOT
performance evidence. The 9,918 B sparse style prototype retains less metadata
and navigation than either implementation; it is not the implemented size.

HAL now leads with `kind`, `name`, `summary`, and `_links`. Owner/schema identity
records, address receipts, generic fact wrappers, traversal budgets, and empty
observation receipts leave the ordinary reading surface. Domain identifiers,
property declarations, ordering, complete sparse sets, and meaningful outcomes
remain. Root `data_scope.completeness` makes membership negatives interpretable
for known selected values. Root `describedby` links disclose the full contract
separately. This is a deliberate projection, not metadata deletion from the
underlying model or a second copy of the dataset.

Root `_links.curies` defines the readable `inspect:` relation vocabulary.
Related resource arrays live under `_embedded["inspect:values"]`,
`_embedded["inspect:vocabularies"]`, `_embedded["inspect:facets"]`, and
`_embedded["inspect:bindings"]`. Each has its own state and self link. Complete
embedded member inventories are not repeated as root link inventories. Links
carry titles and media types; HAL dataset destinations retain HAL mode while
JSON resource-detail destinations are explicitly typed. All 47 distinct
non-template links resolve.

### Worked facet exploration

Start with the query-space resource. Read the embedded facet summaries and the
binding's `exposed_facets` to distinguish 14 authorable terms from 19 catalog
facets. Find the decoded string-literal facet, then follow its advertised self
link; no destination path needs to be learned or constructed:

```bash
dotnet-inspect explain package-query/query .hal --json > facets-hal.json
literal_href=$(jq -r '._embedded["inspect:facets"][]
  | select(.summary | contains("string-literal")) | ._links.self.href' facets-hal.json)
dotnet-inspect explain "$literal_href" --json > literal-hal.json
jq '{key, value_kind, operators, values, examples, effects, requires}' literal-hal.json
```

The response is still HAL. Its `inspect:required-context` link names the required
target framework and advertises another HAL resource:

```bash
jq '._links["inspect:required-context"]' literal-hal.json
context_href=$(jq -r '._links["inspect:required-context"][0].href' literal-hal.json)
dotnet-inspect explain "$context_href" --json
```

The required context is also embedded locally, so an agent can inspect it
without another request. Following the link demonstrates representation
continuity, not an optimal fetch requirement. The binding's `exposed_facets`
excludes `library-target`; it is not an authorable `--where` term. Full schema
and observation details are available separately:

```bash
contract_href=$(jq -r '._links.describedby.href' literal-hal.json)
dotnet-inspect explain "$contract_href" --json
```

The catalog still lacks literal length/cardinality constraints and the `--tfm`
mapping. Agents must consult the [literal term contract](../../docs/design/package-query-library-literal.md#term-contract)
before preparing execution. HAL cannot supply facts that have not been
registered. The demonstration covers navigation without path instructions;
it does not establish unfamiliar-agent recognition or eliminate interpretive
cautions. An agent usability evaluation remains necessary for that claim.

`selected-style.jq` and `selected-facets.jq` expose the adaptations needed by each
layout and verify equivalent reading answers. Merely removing `_links` is no
longer expected to produce identical documents.
