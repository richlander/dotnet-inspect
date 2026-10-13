# Vocabulary reading proposal

This is the worked evidence for
[Vocabulary reading selection](../../../docs/design/resource-explanation.md#vocabulary-reading-selection),
owned by Resource Explanation. Product Vocabulary owns the declarations being
read. These are **offline proposal examples**, not installed narrowing syntax.
The examples remain pinned proposal evidence. Shared production reading is
[implemented below](#shared-production-reading); host value narrowing is pending.

The design starts with questions, as in the pinned
[CVE schema design](https://github.com/dotnet/designs/blob/b9bc7465feae40aa606b8e781fc290c20b690748/accepted/2025/cve-schema/cve_schema.md#design-philosophy):
use lookups where a key is known, preserve useful discovery arrays, and judge
normalization by actual jq reads. HAL supplies the embedding and navigation
vocabulary; it does not replace the underlying value/map semantics. Both formats
must answer the same questions without fetching already embedded knowledge.

## Critical questions and evidence

| Agent question | Required knowledge | Example |
| --- | --- | --- |
| What accessibility values exist and which apply by default? | All four native IDs, owner order, default meaning and membership | [accessibility-hal.json](examples/accessibility-hal.json) |
| What exact identity names an object creation for Body Shapes `Kind`? | One native body-kind identity; a path segment is not the operand | [body-one-hal.json](examples/body-one-hal.json) |
| What body kinds can I select? | Complete 72-value inventory and accepted input identities | [body-catalog-hal.json](examples/body-catalog-hal.json) |
| What does `slot-local-names` select; does it change bytes? | Option/value, Boolean properties, map meanings, exact `Synthesis` term | [slot-hal.json](examples/slot-hal.json) |
| Which selected guarded-return alternatives conflict, and what does their tier mean? | Both alternative IDs, group membership, values and exact `Lens` term | [conflicts-hal.json](examples/conflicts-hal.json) |
| Which choices have byte divergence or oracle endorsement? | Full style population and sparse Boolean indices | [style-catalog-hal.json](examples/style-catalog-hal.json) |
| Is an absent choice false or merely unselected? | Selected population, complete map coverage, membership check before Boolean lookup | Slot example and `read.jq` |

Every example has a corresponding `*-direct.json` and `*-answers.json` file.
The answers are demo/validation outputs, not a third product format.
[read.jq](read.jq) accepts `direct`, `hal`, and the unchanged `source` HAL. It
answers value meaning/properties/context, inventory, defaults, sets, and groups.
The projector compares every selected value answer with the unchanged source
and checks independent field recovery. It also compares the direct/HAL index
and inventory answers, rejects incomplete/unavailable/missing-context sources,
and checks a known catalog value that is absent from the selection.

## Reproduce

The checked-in sources were captured with the NativeAOT CLI at commit
`7c18afc97b170947835b732797572292dba2fff2`, binary SHA-256
`af0fb5b731ffeebca34b52dec7b9eb75602e35e1cae9f68b1854c8edf1e3fc87`.
[provenance.json](sources/provenance.json) records exact commands and source
hashes. This pin is historical, not a claim that examples track installed output.
The vocabulary catalog/dataset source files are unchanged between that commit
and the proposal base `17aace859f8082f2a62c8da8dcec58d6bf3f7ac8`.

Run from the repository root with Python 3 and jq:

```bash
python3 tools/ExplainReadingScenarios/vocabulary-reading/project.py \
  --output /tmp/vocabulary-reading-examples
diff -ru tools/ExplainReadingScenarios/vocabulary-reading/examples \
  /tmp/vocabulary-reading-examples
```

The generator is deliberately offline. It materializes pinned full responses
and then selects them to compare shapes. It proves neither production pushdown
nor runtime work reduction. Shared implementation must use the composed owner
snapshot, not parse these fixtures or add another declaration inventory.

## Worked agent exploration

An agent has read the skill's HAL instructions. Its task is to identify the
Body Shapes `Kind` operand for object creations. The current broad acquisition is:

```bash
dotnet-inspect explain vocabularies/csharp.body-kinds .hal --json
```

The proposed single-value result is [body-one-hal.json](examples/body-one-hal.json).
It starts with HAL `_links` and `_embedded`; the core values are embedded resources:

```bash
jq '._embedded.vocabularies[] |
    select(.id == "csharp.body-kinds") | ._embedded.values[].id' \
  tools/ExplainReadingScenarios/vocabulary-reading/examples/body-one-hal.json
```

The answer is `ObjectCreationExpression`. Preserve that case as the query
operand. The current canonical explanation path ends in
`values/objectcreationexpression`; the path spelling is not the native identity.
No schema or link request is needed for this answer. `accepted_by` supplies
owner-issued input identities; it does not supply a CLI gesture. Query discovery
remains responsible for operators and host syntax.

For an API inventory task, the full accessibility example is already small:

```bash
jq '._embedded.vocabularies[] | select(.id == "api.accessibility") |
    {values: [._embedded.values[].id], defaults}' \
  tools/ExplainReadingScenarios/vocabulary-reading/examples/accessibility-hal.json
```

It returns `public`, `protected`, `internal`, `private`, with `public` as the
default. The direct form is equally usable:

```bash
jq '.vocabularies["api.accessibility"] | {values: .value_order, defaults}' \
  tools/ExplainReadingScenarios/vocabulary-reading/examples/accessibility-direct.json
```

The style question needs more knowledge. Read one choice plus its local tier:

```bash
jq --arg form hal --arg vocabulary csharp.style-choices \
  --arg value slot-local-names --arg task value \
  -f tools/ExplainReadingScenarios/vocabulary-reading/read.jq \
  tools/ExplainReadingScenarios/vocabulary-reading/examples/slot-hal.json
```

The answer retains `option: slot-local-names`, `value: true` as an owner text
token, tier identity `Synthesis`, its local-naming meaning, all three Boolean
properties as false, and no conflict group. No tier request is needed.
Change `--arg form` to `direct` and use `slot-direct.json` for the same answer.

If the task changes to `qualify-field-access`, run the same read with that value
on the slot file. It returns `status: unselected`, even though the full catalog
knows the choice and its oracle endorsement. It must not return false. That
new question justifies following the one collection link:

```bash
href=$(jq -r '._links.collection.href' \
  tools/ExplainReadingScenarios/vocabulary-reading/examples/slot-hal.json)
dotnet-inspect explain "$href" --json
```

The implemented destination currently returns the broad existing vocabulary HAL.
Following it merely to confirm the completed slot answer provides no new task
knowledge and adds latency. A future host selection may narrow this acquisition;
this proposal advertises no fictional selection address.

For a conflict question, discover the full group's identities locally:

```bash
jq '.property_groups.conflict_group["guarded-boolean-return-style"]' \
  tools/ExplainReadingScenarios/vocabulary-reading/sources/style-hal.json
```

The conflict example selects those two alternatives and their one exact tier.
Its group table is sufficient for this question because both members were
explicitly selected. Group tables in a narrow document describe selected
membership, not all alternatives in the catalog. Term context closes
automatically; optional group peers do not.

## Shape decisions and size

Map meanings and declarations live once per included vocabulary. Values use
native identities; different display names and all summaries survive. A label
identical to its ID is reconstructed from that ID. Direct values are keyed
lookups with explicit owner order; HAL values are ordered embedded resources.
This means HAL can be smaller for an inventory because it need not repeat IDs
in an order index. These are two usable views of the same selected knowledge.

Boolean sets, optional text groups, and selected defaults remain sparse indices.
False and no-group defaults apply only to available complete selected map
observations. Unselected values remain unknown. Integer map tokens retain their
source representation. Map labels, summaries, coverage, cardinality and target
vocabulary remain available; this proposal does not discard a constraint or
meaning because none of these sample questions happen to use it.

The reading profile omits per-value kind tags, links and repeated titles, plus
the full unrelated term population. Explicit contract inspection and website
Settings descriptors have distinct consumers. Neither is removed by this slice.

All comparative sizes use the same minified UTF-8 serialization and one trailing
newline. Original .NET wire responses differ slightly due to escaping. Source
hashes pin those original bytes. These are artifact sizes, **not latency results**.

| Source / selected population | Source HAL | Direct proposal | HAL proposal |
| --- | ---: | ---: | ---: |
| All 4 accessibility values | 1,965 B | 978 B | 1,102 B |
| All 72 body kinds | 18,326 B | 3,879 B | 2,717 B |
| One body kind | 18,326 B | 495 B | 618 B |
| All 17 style choices and all 4 referenced tiers | 14,723 B | 10,088 B | 9,770 B |
| One style choice and one exact tier | 14,723 B | 2,959 B | 3,090 B |
| Two conflict alternatives and one exact tier | 14,723 B | 4,023 B | 4,087 B |

The full body-kind HAL reduction is 85%, with an identical population. Full
style HAL drops 34%; slot HAL drops 79% with a narrower population. They measure
different effects. Dropping links alone from the current body source saves
11,921 B; from style, 4,425 B. Existing style map declarations occupy 1,847 B
as a serialized array-of-arrays; the full referenced tier resources occupy
2,345 B. Those component measurements overlap with link costs and are not
additive. The remaining choice prose/map meanings explain why style is still
larger. The generated [report.json](examples/report.json) retains the counts.

This establishes a bounded saving without manufacturing absent false/null fields:
the current model already normalizes Boolean properties and optional groups.
It does not establish minimum size, faster execution, or better agent behavior.
In particular, source capture plus offline selection transfers the broad response;
only host adoption can make the smaller selection available before transfer.

## Counted adoption

Overall tracker [#9762](https://github.com/richlander/dotnet-inspect/issues/9762)
now plans four vocabulary slices after the ten completed explain/query slices:

1. [#10013](https://github.com/richlander/dotnet-inspect/issues/10013): this
   Resource Explanation proposal and worked evidence.
2. [#10014](https://github.com/richlander/dotnet-inspect/issues/10014): shared
   snapshot selection/lowering and meaningful source-equivalence gates.
3. [#10015](https://github.com/richlander/dotnet-inspect/issues/10015): focused
   CLI admission and retirement of its superseded ordinary reading projection.
4. [#10016](https://github.com/richlander/dotnet-inspect/issues/10016): focused
   Browser/Wasm admission and retirement of its superseded reading projection.

Host gestures and URI admission are decisions for those focused adoptions.
Production NativeAOT comparisons and Browser/Wasm entrance evidence belong with
implementation. Agent trials then measure answer validity, retrieved bytes,
necessary navigation, and whether the familiar HAL shape improves reading over
the equally queryable direct tables.

## Shared production reading

[#10014](https://github.com/richlander/dotnet-inspect/issues/10014) implements
`vocabulary-reading/v1` in the shared dataset. Existing full vocabulary commands
now use that reading profile:

```bash
dotnet-inspect explain vocabularies/csharp.body-kinds .hal --json
dotnet-inspect explain vocabularies/api.accessibility .data --json
```

The same `read.jq` adapter reads production output. A full HAL root has one
implemented `self` link; a shared API selection has one full-vocabulary
`collection` link. The narrow API is `VocabularyExplanation.SelectData(path,
valueIds)` or `ResourceExplanationDataset.Create(..., valueIds: ...)`; identities
are exact and deduplicated in owner order, with exact term closure. No CLI `-Q`,
value-root projection or Browser narrowing gesture is implied yet. Host admission
remains #10015/#10016. The historical example sizes above are not current wire
size claims.

Production conformance gates compare owner-declared values/map observations,
complete selected groups/defaults, direct/HAL records and order, exact recursive
term closure, and case-sensitive selection. A cycle terminates without expanding
unrelated values; a narrow request can fit while the whole population exceeds the
host bound. Existing partial-coverage and absent-Boolean refusal gates still
apply. Both host suites exercise full reading and shared content pins; ordinary
compact and explicit contract pins remain unchanged. Only selected facts are
lowered; the installed catalog is still composed before selection.
