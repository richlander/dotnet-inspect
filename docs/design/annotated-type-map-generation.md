# Annotated Type Map generation

## Status and owner

This document owns typed contribution settlement and document generation for
the [Annotated Type Map](annotated-type-map.md). It owns:

- the settled content of `AnnotatedTypeMapContribution` for each family kind;
- exact correspondence among an accepted plan, its adapter queries, their
  contributions, the planned families, the focus, the Member keys, and the
  version sides;
- settlement of a map request's contributions into the one-version map or the
  map diff, at each result kind; and
- validation and return of a family contribution request's contributions.

Its exact claim is:

> Given one accepted `AnnotatedTypeMapPlan` and the completed results of its
> typed adapter-query demand, one result set per version side, generation
> verifies exact contribution correspondence, settles each result kind from
> exactly the families that kind reads, compares shape facts and fact streams
> only through the equalities the map's consumed owners define, and emits the
> result without executing, reacquiring, or replanning producer work.

This is adoption step 3 of
[Annotated Type Map planning](annotated-type-map-planning.md#adoption), which
it completes; it is tracked by
[#10215](https://github.com/richlander/dotnet-inspect/issues/10215) and
[#10217](https://github.com/richlander/dotnet-inspect/issues/10217). It mirrors
[Annotated Type Relations generation](annotated-type-relations-generation.md).

## Consumed contracts

| Owner | Contract consumed |
| --- | --- |
| [Annotated Type Map](annotated-type-map.md) | Map entries, shape facts, fact streams, admitted findings, keys, listing, outcomes, terminals, the map diff rule, and its gates |
| [Annotated Type Map planning](annotated-type-map-planning.md) | Families, result kinds, the accepted plan and its trail, and the request forms |
| [Finding Diff Any](finding-diff-any.md) | Member keys, the API and Body axes and their equality, presence, and `Indeterminate` |
| `MetadataFindings.CompareApi` | The complete API comparison, the API facts' equality |
| [Annotated Source diff document](annotated-source-diff-document.md#fact-comparison) | The fact comparison over two fact streams |
| [Finding nomenclature](finding-nomenclature.md) | `IFinding`, `IPairFinding`, descriptors, and outcomes |
| Typed inspection query catalog | Typed result access through `InspectionQueryResults.Get` |

Generation consumes these contracts without redefining a fact, finding,
equality, key, listing rule, or terminal.

## Follow the T

`AnnotatedTypeMapContribution` is a concrete product type. The map's
registration, planning, and generation layers construct, validate, and
interpret it; general query infrastructure only carries it as typed result
storage. Generation reads each adapter result with
`InspectionQueryResults.Get` on the plan's exact query. Recovering a
contribution through reflection, casts, string ids, or a universal result
interface is invalid.

Every contribution names the exact families it holds, so a settled entry
traces back to the plan trail line that bound it.

## Inputs

Generation consumes:

- one accepted `AnnotatedTypeMapPlan`;
- for a one-version request, one completed `InspectionQueryResults` from
  executing the plan's demand against that version; and
- for a pair, an ordered Before and After pair of completed results, each from
  executing the same demand against its version side.

It does not accept producer-native context, acquisition services, execution
delegates, planners, or an inspection query catalog. A missing result fails
through typed result access.

## Contribution content

A contribution is one adapter query's result for one version side: a content
set keyed by family, holding exactly the families the plan's trail binds to
that query, each once. A query bound to one family holds one family's
content; a query several families share holds each of theirs, with its own
entries and outcomes. Each family kind has one content shape:

| Family | Per Member key | Type's own entries |
| --- | --- | --- |
| API shape facts | When the API surface of its version admits the Member, its projection in that surface, the input the complete API comparison reads, including its status facts: accessibility, hidden, and obsolete status | The Type's admission on that side, and its row's projection when admitted |
| Body shape facts | Each owned method's body identity under Finding Diff Any's Body equality, and every declaration fact of the Member and of the generated code it owns, each with that side's admission | The residual's body identities and declaration facts, each with its admission |
| Fact stream | For each owned method, the keys of every fact in its fact census in stream order | The residual's methods' streams |
| Finding family | The Member's admitted findings of the family's descriptors | The residual's admitted findings |

Declaration facts carry their admission, not a fixed axis, because the axis a
fact compares on is settled across the pair, as the map states; generation
places them during the shape comparison.

Every contribution records the Type's **presence** on its side. A Type absent
from a side contributes no Member keys and no Type entries on that side.

Every Member entry and the Type's own entry carry an outcome. The outcomes
divide as the map divides them:

- **settled:** `Complete`, or no applicable input on that side, such as a
  method with no body for a Body fact or a body analysis; a settled outcome
  with no element is an empty answer; and
- **unsettled:** `Failed`, `Unavailable`, or `Indeterminate`.

An unsettled entry carries no partial content.

An API projection is detached and partial: a surface at the `ApiAxis`
extraction scope, which keeps hidden Members and marks them, holding the Type
row and the projected Members, with the Type context the complete API comparison's
classifier reads, and no reader or image. Two retained maps therefore compare
without reopening an image, as the map's Retained maps gate requires. The
status facts are the ones an ordinary consumer filters on, such as the diff
ladder's ordinary population, so they are part of the content, not a later
family.

A Member-scope plan's contributions hold the requested keys present in that
side's census. A requested key absent from a side is absent there, exactly as
a Member missing from that side at Type focus; generation never invents an
entry for it.

Contributions contain settled domain results. They expose no execution
callback, source choice, decoded body, or retained producer intermediate.

## Correspondence

A side's contributions are accepted only when:

- there is exactly one contribution for each adapter query in the plan's
  demand, read with that exact query;
- each contribution holds exactly the families the trail binds to its query,
  each once, so every family the trail demands is held by exactly one
  contribution;
- its focus is the plan's exact focus, and its keys are among the plan's
  Member keys when the plan has them; and
- a pair's two sides carry the same families.

A family the trail records as selected but not demanded has no contribution.
Correspondence failure is a contract exception, never an empty result.

## Settlement

Each result kind settles from exactly what it reads, the planning owner's
result-kind table.

### One-version map

- **Rows:** the map is the Type's outcome, its shape (both shape-fact
  families' entries), its annotations (the selected finding families'
  entries), and each method's fact stream. A key, or the Type, is listed
  exactly when it has an admitted finding or an unsettled analysis outcome, the
  map's one-version listing rule. Shape facts are carried for later
  comparison and never list a key. In a Member-scope request, a requested key
  absent from the version is its question's `Unavailable` outcome, an
  unresolved row.
- **Count and Exists:** from the selected finding families' entries only, by
  the same listing rule: Count reports changed rows and, separately,
  unresolved rows; Exists is `true` with a changed row, undecided with only
  unresolved rows, and `false` only with none and a complete Type outcome.

### Map diff

- **Join:** keys join by Finding Diff Any's Member key across the two sides,
  on both sides, After only, or Before only. The Type row joins by the Type's
  presence: on both sides, or `Added` or `Removed`, which makes the Type row
  and every Member of its present side one-sided. In a Member-scope request, a
  key present on one side is one-sided like any other; a key present on
  neither side is its question's `Unavailable` outcome, an unresolved row.
- **Placement:** before comparing, declaration facts are placed on an axis by
  Finding Diff Any's [Axes](finding-diff-any.md#axes) partition. A Type public
  on either side partitions its facts as public on both, and within that each
  Member's declaration facts are API or Body by the Member's own accessibility
  on its side, as if the Type were public there. A Type admitted on one side
  only makes its residual and every API-placed Member API `Changed`, Finding
  Diff Any's rule, without a surface comparison.
- **Shape comparison:** for a key on both sides whose Type is admitted on both
  sides, the API transition is the complete API comparison over the two
  detached projections. The Body transition is Finding Diff Any's Body
  equality over the two sides' body identities and Body-placed declaration
  facts. A one-sided key is `Added` or `Removed` on each axis where it owns
  shape facts. Only an unsettled entry makes that key's shape comparison
  unsettled; a settled entry with no applicable input, such as the Body entry
  of an abstract Member the surface admits, compares as an empty answer.
- **Count and Exists:** from the shape comparison only. Exists stops at the
  first shape transition. No fact stream or finding is read.
- **Rows:** a key, or the Type, is listed exactly when it has a shape
  transition or an unsettled shape comparison. For each listed key, and only
  for listed keys, each owned method's fact streams are compared by the fact
  comparison: a method on both sides compares its two streams, and a one-sided
  key or method compares its one present stream, so each of its facts is
  `Added` or `Removed`. The transitions are then filtered to the selected
  finding families' descriptors. Each listed row also carries its annotation
  outcome, read from the selected finding families' entries on each side; that
  outcome never lists or unlists a row. With no finding family selected, no
  fact stream or finding was demanded and a listed row carries only its shape
  transitions.

Generation adds no equality of its own. It compares API facts only through the
complete API comparison, Body facts only through Finding Diff Any's Body
equality, and fact streams only through the fact comparison.

### Family contribution request

The result is the validated contributions, keyed by family and, for a pair,
by side. It carries no listing, terminal, or map diff. A consumer that
compares two sides, such as Finding Diff Any's API census settlement, owns
that comparison.

## Output

A map result retains the plan's focus and trail, the result kind, the Type's
outcome, and for Rows the keyed collection and its Rows projection, as the map
owner defines them; for Count and Exists, only the terminal value and its
outcome. A family contribution result retains the plan's trail and the
contributions. The result is session-bound while it holds any process-local
identity; portable projection belongs to a later owner.

## Failure contract

Generation throws a contract exception for a missing result, a foreign focus,
a missing or extra contribution, a contribution holding a family the trail
does not bind to its query or omitting one it does, keys outside the plan's
Member keys, mismatched sides, or content whose shape does not match its
family kind.

Producer unavailability, failures, and `Indeterminate` comparisons are data in
entry outcomes, and a key absent from a side is a one-sided key. They do not throw
merely because they are not successful.

## Gates

- **Follow the T:** generation reads only
  `InspectionQuery<AnnotatedTypeMapContribution>` results through typed access;
  general query infrastructure is unchanged.
- **Correspondence:** a missing, extra, foreign-focus, foreign-key, or
  mismatched-side contribution fails as a contract exception, and two
  families sharing one adapter settle from that adapter's one contribution.
- **Settled kinds:** each result kind reads only its row of the result-kind
  table; a map-diff Count or Exists settles with no fact-stream or finding
  contribution present, and map-diff Rows reads finding entries only for
  listed rows' annotation outcomes.
- **Settled outcomes:** an entry with no applicable input is settled; an
  unchanged abstract or interface Member the surface admits is not listed in
  either form.
- **No new equality:** the API transition equals the complete API comparison
  over the two projections, and the Body transition equals Finding Diff Any's
  Body state, for every Member of the Finding Diff Any fixture pairs.
- **Status facts:** an API shape entry carries accessibility, hidden, and
  obsolete status, and a change to any of them alone is an API transition.
- **Visibility change:** for a Type going from `internal` to `public`, and
  back, with unchanged Members, each Member's API and Body transitions equal
  Finding Diff Any's states: API `Changed` and Body `Unchanged` for a public
  method whose IL and declaration are unchanged.
- **Listing:** the map owner's Listing, Exactness, Outcomes, and Symmetry gates,
  over settled contributions.
- **Retained comparison:** comparing two settled one-version Rows maps gives
  the same map diff as settling the pair, element for element.
- **Absent keys:** in a Member-scope pair request, a key present on one side
  only is `Added` or `Removed`, the same row as at Type focus, and a key
  present on neither side is an unresolved `Unavailable` row.
- **One-sided Type:** a Type present on one side settles its Type row and
  every Member of its present side as one-sided.

## Adoption

1. Lock this generation owner. This documentation slice.
2. Implement contribution content, correspondence, and settlement for every
   result kind and the family contribution request, over test adapters,
   widening `AnnotatedTypeMapContribution` from one family to the
   family-keyed content set.
3. Planning step 4 registers the API shape-fact family over
   `ApiSurfaceExtractor` and routes Finding Diff Any's API census settlement
   through a family contribution request, settled here.

## Non-claims

- This owner does not define any shape fact, finding, equality, key, listing
  rule, terminal, or outcome; the map and its consumed owners do.
- It does not execute producers, plan requests, or choose sources.
- It does not decide whether a map diff is computed from retained maps or in
  lockstep; the map admits both and its adoption steps measure both.
- It does not define portable serialization, CLI or Browser rendering, or a
  host's retention.
- It does not pair Members across keys; correspondence across keys belongs to
  [API qualified anchor](api-qualified-anchor.md#version-pair-correspondence).
