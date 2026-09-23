# Incremental Library API diff

## Status and owner

Focused design for the incremental production and delivery of one Library
API comparison, recorded from the measurements and the QuerySpace-owner
reconciliation in
[#8198](https://github.com/richlander/dotnet-inspect/issues/8198), under the
Compare tracker
[#7213](https://github.com/richlander/dotnet-inspect/issues/7213).

This document establishes **Incremental Library API diff** as one
architectural owner. It sits where the comparison is produced today,
`DotnetInspector.Presentation` over `Inspector.Findings` and
`DotnetInspector.Queries`, and consumes the QuerySpace contracts for its
delivery shape. Its normative claim is:

> One Library API comparison over two retained images is producible as an
> ordered row source partitioned by exact Type key and materialized per member
> row. It yields the same relations, classifications, and counts as the
> whole-surface comparison, answers exact inventory Counts from text-free
> correspondence and structural-facet passes ahead of any row, exposes
> compatibility totals from a distinct typed change population, checkpoints
> continuation at an owner-issued
> `(TypeKey, MemberKey)`, and bounds retained text per delivery window rather
> than per assembly, so that the Compare inventories are delivered through
> QuerySpace Rows and exact Count over one immutable snapshot generation.

Nothing in this design changes what a Library API diff means. It changes when
and in what unit the comparison is evaluated and delivered.

## Why this is needed

The current production path runs one complete comparison and delivers one
complete document that the Browser then filters and counts locally. Measured
on the managed export
([#8198](https://github.com/richlander/dotnet-inspect/issues/8198)):

| Pair | Result | Wire | Changed Types / Members |
| --- | --- | ---: | --- |
| System.Text.Json 9.0.0 → 10.0.0 | Succeeded | 111 KB | 12 / 32 |
| Newtonsoft.Json 12.0.3 → 13.0.3 | Succeeded | 385 KB | 16 / 112 |
| Microsoft.CodeAnalysis.CSharp 4.8.0 → 4.12.0 | Unavailable | 4 KB | none: both surfaces truncated at the retained-text bound before any Type was projected |

Payload grows linearly with changed Members at roughly 1.2 KB each before the
envelope; the Browser re-walks the whole document on every render; and a
library the size of Roslyn cannot be compared at all because the whole API
surface must be projected with retained text before comparison begins. The
first two are delivery problems; the third is an evaluation problem. Delivery
changes alone cannot fix the third.

## Ownership and boundaries

This owner defines:

- the partition rule: correspondence by exact Type key, with the member-level
  soft correspondence that crosses Types routed by its receiver key;
- the materialization rule: rows are produced per member, never per whole
  Type;
- the three row sources, Type summaries, member rows, and compatibility
  changes, and their ordering;
- the text-free correspondence and structural-facet pass that answers exact
  Counts before rows;
- the snapshot generation that binds Count and Rows executions to one pair of
  retained images;
- per-window work bounds for retained text and what a bound-exceeding
  partition reports;
- the equivalence obligation against the whole-surface comparison; and
- the adoption order for the CLI and Browser hosts.

It does not own:

- Finding correspondence, identity-set matching, soft keys, or classification
  ([Analysis diff](analysis-diff.md), `FindingMatcher`, `ApiDiffAnalyzer`);
- the comparison document envelope
  ([Comparison document](comparison-document.md));
- the portable Type-entry and Member-relation projection
  ([Library API diff presentation](library-api-diff-presentation.md));
- QuerySpace row vocabularies, terminals, semantic selection, continuation
  receipts, or completion evidence
  ([Query space composition](query-space-composition.md),
  [Section-row shaping](section-row-shaping.md),
  [Row query and ordering](row-query-order.md));
- API surface extraction limits or the retained-image budget
  (`ApiSurfaceExtractor`, `BrowserApiSurfacePolicy`);
- the Browser wire, its envelope, or its transport mechanics
  ([Inspect Web Library API Diff](inspect-web-library-api-diff.md), #8210); or
- Compare presentation, drill-down, or Explore
  ([Inspect Web Compare Experience](inspect-web-compare-experience.md),
  [Inspect Web Compare Explore](inspect-web-compare-explore.md)).

## Inputs and prerequisites

| Owner | Consumed contract |
| --- | --- |
| Inspector.Findings | `FindingComparison.Compare` in identity-set mode over `api.type` and `api.member` findings; keys are exact Type full names and canonical member signatures scoped to the declaring type; the `api.member.extension-instance` soft key carries the receiver Type |
| ILInspector.Metadata | API surface projection from a retained image with bounded retained text, and per-Type member enumeration from that surface |
| Library API diff presentation | The Type-entry and Member-relation projection, classification placement, and the rejection vocabulary for contradictory topology |
| Query space composition | Rows and exact Count as peer terminals; Head, Tail, Window executed once after predicates; continuation as an opaque receipt over one immutable population; source completion evidence and source-only dispositions |
| Diff History | The precedent for population selection, evaluation selection, and result-row selection as three distinct decisions over one settled population |
| Retained images | Two `AssemblyContextGroup` participants held for the lifetime of one snapshot generation |

## Partition rule

Library API correspondence is identity-set matching: a Type on one side
corresponds to the Type with the same exact full name on the other, and a
member corresponds to the member with the same canonical signature within the
same declaring Type. There is no sequence alignment across the surface, so the
comparison of one Type's members depends only on that Type's members on both
sides.

Two nontrivial partition cases are already keyed:

- The extension-instance soft correspondence pairs a static extension method
  with the instance method it became. Its soft key carries the receiver Type,
  so the relation is evaluated in the receiver's partition; the declaring
  static class's partition sees the removal. After
  [#8178](https://github.com/richlander/dotnet-inspect/issues/8178) the
  extension enters the finding stream once, on its declaring type.
- Type-level compatibility changes (kind, base type, interfaces, generic
  parameters, attributes) belong to the Type partition itself.

Type forwarders remain outside the declaration population compared by the
whole-surface oracle. Partitioning does not resolve a forwarder into another
Library or change a removal into cross-Library correspondence.
[Forwarded API coordinate
correspondence](forwarded-api-coordinate-correspondence.md) is a separate
operation over an explicit destination population and is not a prerequisite
of this producer.

Unkeyed renames and moves are not attempted by the whole-surface comparison
today and are not attempted here. Partitioning loses nothing that exists.

Partitions are evaluated in the ordered union of Type keys from both sides.
That order is the semantic order of the Library inventory and of every
continuation receipt.

## Materialization rule

A Type is the correspondence partition, not the materialization unit. A single
Type may carry more members and more retained text than one delivery window
admits, so:

- member relations are produced one at a time, in the ordered union of member
  keys within their Type;
- a continuation may checkpoint inside a Type at the owner-issued
  `(TypeKey, MemberKey)` of the last delivered member row; and
- Type summary rows are produced from identity correspondence, structural
  facets, and signature models, never from retained member text.

Retained text (rendered signatures, display strings) is projected only for the
member rows inside the current window. Classification needs each changed
member's signature model, which is bounded and structural, and never needs
rendered text.

## Row sources

### Type summary rows

One row per changed Type in key order, carrying exactly what the Library
inventory shows today: exact Type identity on each present side, change
state, whether the definition changed, distinct changed-member count, and
the complete ordered typed compatibility changes attached to the Type, with
breaking, additive, and potentially-breaking counts derived from that
population. Producing a summary row requires the Type's key sets, classified
Type facets, and the signature models of its changed members; it does not
require the members' retained text or their relation rows.

### Member rows

One row per member relation in `(TypeKey, MemberKey)` order, carrying the
relation as the Type inventory shows it today: pair kind, role, exact
identities on each present side, the classified changes placed on it, and
match provenance. Member rows are the Type inventory's row set and the
continuation's checkpoint unit.

### Compatibility change rows

One row per existing typed `ApiChange` occurrence, in Type-key and
producer-issued change order. A row carries its classification, category,
change kind, inert message and old/new values, occupied Type identities, and
optional member identities. This bounded structured classification evidence is
distinct from retained member signature or display text. The population
preserves independent Type-facet and member changes: several compatibility
rows may belong to one Type summary or member relation.

### Predicates

All three sources accept the exact-Type-identity predicate. Compatibility
change rows also accept classification. The Type inventory is therefore its
own scoped query, not a narrowing of a parent result, which is the rule the
Compare experience already applies to Clone.

## Counts

Exact inventory Counts are answered before any row from a text-free structural
pass over the two surfaces:

- the ordered union of Type keys establishes every partition, including added,
  removed, and candidate matched Types;
- every partition compares Type facets and the complete member populations;
  member keys establish additions, removals, and candidate matched pairs, and
  typed member facets and signature models determine which matched Members
  changed;
- changed-Type membership is the union of added or removed Types, matched Type
  facet changes, changed-member relations, and compatibility-change subjects.

These are QuerySpace exact Counts over the selected Type-summary and member-row
populations. Breaking, additive, and potentially-breaking totals are separate
exact Counts over the compatibility-change population after its classification
predicate; they are not Type-row or member-row cardinalities. Classification
uses Type facets and member signature models without retained text.

The Count pass executes the same correspondence and structural comparisons
needed to determine row membership. It does not project retained member
signatures, display strings, or relation rows merely to count them.

A retained-text work bound never redefines any population: a window that could
not project its text still counts, and its members are reported as source-only
dispositions rather than dropped. Transport-population counts on a delivery
receipt are not these Counts.

## Snapshot generation

One comparison is one snapshot: two retained images, their surface key sets,
and one owner-issued generation. Every Count and Rows execution names the
generation it binds to; a Count and a Rows result from different generations
are never combined. Replacing either image, the baseline, or the Package model
retires the generation. A continuation receipt resumes only the generation
that issued it. This is the binding point the QuerySpace owner requires for
Count followed by Rows.

## Work bounds

Retained text is bounded per delivery window, not per assembly. When a window
cannot project a member's text within its bound, that member row is delivered
as a source-only disposition with its identity and classification, its text
absent with the bound named, and the window remains complete. The Roslyn case
becomes a comparable library whose large members disclose their bound, rather
than an unavailable comparison.

Type and member population limits, metadata-row limits, and the retained-image
budget remain owned by API surface extraction and the Browser surface policy.
This design does not raise them; it stops requiring the whole surface's text
to fit them at once.

## Equivalence obligation

The incremental producer is correct only if, on any pair the whole-surface
comparison can complete, it yields the same set of Type entries, Member
relations, classified changes, match provenance, and counts. The pairs measured
in #8198 are the first oracle; the Release gate runs both producers on real
package fixtures and compares the complete populations, not counts alone.

## Rendering strategy

The row sources preserve the existing typed Type summaries,
`LibraryApiMemberRelation` values, classifications, identities, and
dispositions through the rendering boundary. They do not emit display strings
or define a parallel format model.

[Library API diff presentation](library-api-diff-presentation.md) retains the
portable document and projection contracts consumed by CLI and Browser hosts.
Host-specific CLI formatting and Browser DOM lowering consume the typed rows;
neither host reparses rendered text. Markout remains the lowering for the
separately owned declaration or source comparison opened from a selected row.
This owner changes production and delivery units, not format ownership.

## Non-claims

This design does not claim:

- that the comparison algorithm, its soft keys, or its classification change;
- that unkeyed renames or moves are detected;
- that retained-image acquisition becomes incremental;
- that exact Count implies row seekability or random access;
- that a continuation is portable, shareable, or valid across generations;
- that the Browser wire, envelope, or transport mechanics are defined here;
- that text-diff values (declaration or authored Source) are produced by
  this row source; they remain whole values under their own owners; or
- that Type forwarding changes the selected-Library declaration population or
  establishes cross-Library correspondence; or
- that whole-ecosystem comparison is delivered here, although it partitions
  one level up by the same rule.

## Adoption

1. Key-only exact Count and Type summary rows over one snapshot generation,
   with the whole-surface equivalence gate on the measured pairs.
2. Member rows with `(TypeKey, MemberKey)` continuation and per-window
   retained-text bounds, with source-only dispositions gated.
3. The Browser Compare inventory transport as the first adopter of the
   general QuerySpace continuation contract, after
   [#8210](https://github.com/richlander/dotnet-inspect/issues/8210) removes
   the envelope duplication so the baseline is honest: Library and Type
   inventories issue Count then Rows against one generation and stop holding
   the document in the page.
4. The CLI Diff renderers over the same row sources, so both hosts share one
   producer.

Each stage lands only when its own result and failure states are complete and
the equivalence gate passes.

Stages 1 and 2 run the incremental producer beside the whole-surface producer
only in the Release equivalence gate. Stage 3 replaces the Browser inventory's
whole-document production and retention. Stage 4 replaces the CLI production
path. After both host adoptions, no product path invokes the whole-surface
producer; it remains test-only as the equivalence oracle rather than as a
second supported architecture.

## Required gates

| Gate | Required property |
| --- | --- |
| `IncrementalDiffMatchesWholeSurfaceComparison` | On every pair the whole-surface comparison completes, both producers yield identical Type entries, Member relations, classified changes, provenance, and counts. |
| `TypeSummaryRowsNeedNoRetainedMemberText` | Summary rows and exact Counts are produced with retained member signature and display-text projection disabled. |
| `TypeFacetChangesRemainInTypeSummaryRows` | A matched Type whose own facets change and whose member set does not still carries the complete typed compatibility-change population and derived totals. |
| `CompatibilityCountsUseChangePopulation` | Multiple compatibility changes attached to one Type or member produce separate compatibility-change rows and exact classification-filtered Counts without changing Type-summary or member-row Count. |
| `MemberOnlyChangesCreateTypeSummaryRows` | A matched Type with unchanged Type facets and only added, removed, or changed members still contributes one Type summary and its complete member relations. |
| `MemberRowContinuationResumesInsideAType` | A continuation issued mid-Type resumes at the next `(TypeKey, MemberKey)` with no duplicated or skipped row. |
| `CountAndRowsBindToOneGeneration` | A Rows execution against a retired generation is refused; Count and Rows from one generation describe the same population. |
| `TextBoundYieldsSourceOnlyDisposition` | A member whose text exceeds the window bound is delivered with identity and classification, its bound named, and the window complete. |
| `ExtensionInstanceCorrespondenceCrossesPartitions` | The extension-to-instance relation is produced exactly once, in the receiver's partition, with the declaring class's removal in its own. |

## Acceptance scenarios

1. Run Count then Rows for Newtonsoft.Json 12.0.3 → 13.0.3 and confirm the
   chips equal the whole-surface aggregate before any row arrives, and the
   delivered rows equal the whole-surface document.
2. Run the Type inventory for one exact Type with a window smaller than its
   member count; confirm continuation resumes inside the Type and the union
   of windows equals the whole-surface Type entry.
3. Run Microsoft.CodeAnalysis.CSharp 4.8.0 → 4.12.0; confirm the comparison
   completes, Counts are exact, and members beyond the text bound appear as
   source-only dispositions naming the bound.
4. Retire the generation while a continuation is outstanding; confirm the
   resume is refused and no rows from the old generation reach the consumer.
5. Run the `LibraryApiDiff` fixtures after #8178; confirm the moved
   `Transform` relation appears once, on `ProjectionReceiver`, and its removal
   on `ProjectionExtensions`.
6. Compare a matched Type whose base or sealedness changes without a member
   change; confirm its Type summary retains every typed compatibility change,
   Type Count remains one, Member Count remains zero, and compatibility Counts
   equal the classified change-row population.
7. Compare `MethodRemovalSample`; confirm unchanged Type facets do not suppress
   its Type summary, two removed overload relations, or their compatibility
   changes and Counts.
