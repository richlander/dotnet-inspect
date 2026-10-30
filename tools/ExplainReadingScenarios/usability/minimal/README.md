# Minimal-instruction explain reading trials

This study compares unfamiliar direct JSON with identified HAL without
teaching either representation's schema. It follows the
[landed reading trials](../landed/README.md), which taught table names, HAL
vocabulary and qualification semantics. This comparison asks where reusable
domain data helps and where familiar navigation actually saves reading work.

All eight participants produced correct core domain answers. Six satisfied the
whole task; both HAL Style participants incorrectly said another tier fetch
was necessary despite the tiers already being embedded. Direct JSON was usable
without schema teaching. These traces demonstrate no HAL efficiency advantage;
they identify local-data reuse and contract detours as optimization targets.

Normative owner: [Resource Explanation — Skill](../../../../docs/design/resource-explanation.md#skill).
Focused issue: [#9880](https://github.com/richlander/dotnet-inspect/issues/9880).
Theme tracker: [#9762](https://github.com/richlander/dotnet-inspect/issues/9762).

## Protocol

Eight fresh GPT-6 Sol/high participants, without conversation history, each
received one assigned task and guide. Query and Style each have two
participants per representation. Their tasks match the earlier study. The
[common guide](guide-common.txt) teaches invocation, installed search,
vocabulary discovery and local href dispatch. Its only appended difference is
the selector and representation name: [.data/JSON](guide-data.txt) is 687 B;
[.hal/HAL](guide-hal.txt) is 685 B. Neither teaches table names, `_links`,
`_embedded`, sparse properties, completeness states or task-specific jq.
Guide sizes exclude the shared recorder protocol, task and tool framing.

The fixed [participant prompt](participant.txt) and [assessment criteria](assessment-criteria.txt)
were written before dispatch. The [Query](query-task.txt) and
[Style](style-task.txt) tasks ask for qualifications and completeness, without
supplying their interpretation. No entry resource path or task answer was
given. Participants could inspect assigned selected data, ordinary discovery
and full contracts, using their own jq. They could not read shipped skills,
source, designs, other trials, the other selected format or network; they
could not execute inspections. They received no follow-up coaching. The
existing [recorder](../skill-informed/runner.py) logs requests and rendered
views. This is a cooperative protocol, not a security sandbox.

The CLI was built once in Release from
`536ce35bccbd6baa56d592fc6a8e944afd836d30` with SDK
`11.0.100-rc.1.26425.128`, reporting `0.27.0+536ce35`. The source, binary,
guide and response identities are pinned in [manifest.json](manifest.json).
No production behavior or shipped skill changes in this evidence slice.

## Results

| Task / representation / participant | Requests | Retrieved B | Product rendered B | jq errors | Contracts | Assessment |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Query / HAL / 1 | 26 | 90,158 | 56,332 | 0 | 0 | Satisfied |
| Query / direct / 1 | 20 | 78,156 | 63,945 | 0 | 0 | Satisfied |
| Query / HAL / 2 | 26 | 185,248 | 56,896 | 0 | 2 | Satisfied |
| Query / direct / 2 | 15 | 70,478 | 44,712 | 1 | 0 | Satisfied |
| Style / HAL / 1 | 8 | 121,069 | 42,905 | 2 | 1 | Partial |
| Style / direct / 1 | 8 | 37,987 | 41,154 | 1 | 0 | Satisfied |
| Style / HAL / 2 | 10 | 118,524 | 25,549 | 0 | 1 | Partial |
| Style / direct / 2 | 9 | 38,099 | 25,853 | 1 | 0 | Satisfied |

All CLI requests succeeded. Each participant viewed its assigned guide once.
[measurements.json](measurements.json) retains exact totals, view counts,
href requests, repeated-payload counts, and discovery before the first selected
request. Request IDs allocate before execution: parallel calls can finish and
log out of order. The discovery split uses allocated IDs, not a timing measure.

[assessment.json](assessment.json) records manual obligation-level assessment
with response/view provenance under the fixed criteria. Query answers prepare
the requested literal/framework and preferred dependency/depth/target commands,
distinguish preferred direct-inclusive from legacy depth-2-or-greater reachability,
reject the astral/depth-1/target-all neighbors, and distinguish candidate work
from final rows. Missing normalization and unexecuted matches remain uncertain.
All four Style answers return the exact five eligible choices, option/value and
tier descriptions, both conflict groups, and bounded sparse-negative and
arbitrary-key interpretations. The HAL fetch-necessity answers are incorrect;
core domain correctness is not the same as completing every task obligation.

Retained answers:
[Query HAL 1](query-hal-1/answer.json), [Query direct 1](query-data-1/answer.json),
[Query HAL 2](query-hal-2/answer.json), [Query direct 2](query-data-2/answer.json),
[Style HAL 1](style-hal-1/answer.json), [Style direct 1](style-data-1/answer.json),
[Style HAL 2](style-hal-2/answer.json), [Style direct 2](style-data-2/answer.json).

## Where the value appeared

**Reusable domain data:** all four minimally instructed Query participants
obtained the correct rules and command gestures. Direct Style participants
discovered the unfamiliar keyed tables and local tier joins without table-name
teaching. The installed data can carry substantial task knowledge independently
of HAL or a long prose skill.

**HAL navigation:** three HAL participants followed advertised hrefs, showing
the menu was actionable. They fetched four contracts totaling 315,685 B;
direct participants fetched no explicit contracts. Ordinary `explain --json`
reads are compact resource representations, not `.contract`, even when a
participant called them full contracts. Link following was useful mechanically
but did not establish more efficient task completion. HAL's standard terms do
not teach domain fields, sparse membership semantics or our nesting choices.

**Shared discovery and schema exploration:** broad task phrases often produced
no matches in either group. The first selected request was allocated after
4/6 prior requests for direct Query and 10/6 for HAL Query; Style required
4/3 for direct and 3/5 for HAL. Several participants initially guessed search
fields such as `path` instead of `resource_path`. HAL readers also initially
projected `facts` from selected resources before recognizing root state; one
direct reader tried an array-like projection of keyed facets. Some mistaken
filters emitted nulls rather than errors, so jq-error counts alone understate
schema-discovery work.

**Reuse:** all Query participants fetched a query-space closure and separate
facets/bindings already available within it. All Style participants fetched
tiers despite having them locally. Both direct Style answers eventually
recognized that extra fetch was unnecessary; both HAL answers still claimed
it was required. HAL Style participants inspected `_embedded` keys and its
`values` relation while overlooking the `vocabularies` relation and its nested
values. No response payload was retrieved twice byte-for-byte: overlapping
resources in distinct responses are the relevant repeated work here.

The next focused optimization should help agents index and reuse a selected
closure before navigating elsewhere, then test whether contract links lead to
necessary information or exploration detours. A bounded reuse instruction is
a guide experiment; changing discovery matching belongs to Capability Catalog
Search, and changing HAL relationship placement belongs to its lowering.
These traces do not justify choosing a format winner, removing HAL, or moving
domain inventories back into the skill.

## Worked local-data check

Both HAL Style answers say another tier fetch was necessary. The primary
choice response already contains every tier name and description. Inspect
those saved records directly:

```bash
jq -c '[._embedded.vocabularies[]
  | select(.id == "csharp.style-tiers") | ._embedded.values[]
  | {id, name, summary}]' \
  tools/ExplainReadingScenarios/usability/minimal/style-hal-1/response-04.out
```

The independently usable direct model contains the same four records:

```bash
jq -c '[.vocabularies["csharp.style-tiers"].values[]
  | {id, name, summary}]' \
  tools/ExplainReadingScenarios/usability/minimal/style-data-2/response-04.out
```

Both produce identical 746 B tier lists from the captured primary response. The
Formatting description begins `Layout only`; Spelling begins
`An equally faithful way to spell the same code`. These are local-presence
falsifiers, not general property-query filters: a client must retain applicable
scope and member states when interpreting sparse negatives. HAL Style 2 has
the same local records in `response-06.out`. No second tier or contract request
is needed to obtain the descriptions.

## Interpretation limits

Two samples per cell, nonrandomized assignment and shared initial discovery
do not establish statistical or causal format superiority. A HAL label tests
spontaneous use of familiar vocabulary, not measured prior knowledge of HAL.
The previous study used a different guide and source snapshot, so its results
are context rather than a controlled baseline. These tasks exercise
registered descriptions, not package inspection or the broader shipped skill.

Retrieved bytes count CLI stdout plus stderr before filtering. Product
rendered bytes count every recorder emission, including errors and repeated
views; guide emissions are separate. These are UTF-8 counts, not tokens,
delivered UI context, reasoning effort or latency. Receipt emission does not
prove an untruncated rendering. Schema-exploration traces are diagnostic
observations, not a complete measure of cognitive effort.

## Reproduce receipts

With Python 3 and jq, from the repository root:

```bash
python3 tools/ExplainReadingScenarios/usability/minimal/verify.py
```

The verifier checks fixed-input and response hashes, request/view receipts,
assigned-format use, successful jq replay and recomputed measurements. It
parses answers but does not score their semantics or prove future behavior.
Successful views are reconstructed; jq error text is retained because it
contains original scratch paths. Empty streams are reconstructed as empty.
Use the participant template with a new scratch directory, matching CLI and
SDK config, the assigned guide and one task for another agent run. New
participants need not choose the same requests or answers.

The Release build passed with zero warnings/errors. Independent
`compare-selected.py` checks passed all 14 reading shapes, registered rules and
preparation equivalences, plus 49 advertised HAL hrefs. These check mechanics
and data meaning, not a population success rate. A same-length response mutation
in a copied evidence directory failed hash verification; the checked-in
artifacts remain unchanged. Changed Markdown and whitespace checks passed for
this evidence-only slice.
