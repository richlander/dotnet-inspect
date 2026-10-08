# Inspect Web Member Body Diff

## Status and ownership

This document owns the merged **API + Member Body** Browser Compare content:
the hierarchy that discovers changed declarations and implementations and the
inline Member reader that shows one exact implementation comparison. It is
part of the Compare experience
adopted through
[#9338](https://github.com/richlander/dotnet-inspect/issues/9338), under the
end-to-end Compare tracker
[#7213](https://github.com/richlander/dotnet-inspect/issues/7213).

Its normative claim is:

> For one retained Gallery Package Library and its effective Diff target,
> API + Member Body projects the settled Implementation Diff document and
> portable Library API member relations into changed Type rows and current-Member
> destinations. Opening one issued Member destination presents that exact
> added, signature-changed, or body-changed Member's Annotated Source diff
> document in the shared diff viewer on the Member page. A deleted Member of a
> surviving Type remains a non-activatable Type-level finding. Managed code
> issues every relation, identity, and destination; the Browser performs no
> correspondence, text comparison, or identity inference.

Member is the detailed-result boundary. Explore may expand the same retained
Member document, but it neither owns nor replaces the inline result.

This document defines:

- the default `member-body` Compare content choice and its
  Library-to-Type-to-Member hierarchy;
- the Browser operation association and bounded projection of body changes;
- the exact handoff from an inventory row to one Member comparison;
- the inline Member Body reader, its media, states, and retention; and
- the relationship between that reader and optional Explore expansion.

It consumes and does not redefine:

| Owner | Contract consumed |
| --- | --- |
| [Compare experience](inspect-web-compare-experience.md) | The retained Package model, effective Diff target, sticky Compare state, subject drill-down, Member detail boundary, and return behavior |
| [Library API diff presentation](library-api-diff-presentation.md) | Producer-corresponded `LibraryApiMemberRelation` values: exact occupied-side Type identities and `MemberAnchor`s, changed/added/removed topology, and removed-Type Member summaries |
| [Implementation Diff](implementation-diff.md) and its [explicit request adoption](https://github.com/richlander/dotnet-inspect/issues/9339) | Exact-Library-pair endpoints, selected population and mechanisms, changed body-backed Members, `ResearchSubjectKey` identity, producer evidence, and per-mechanism coverage |
| [Research designated-pair admission](implementation-diff.md#research-designated-pair-admission) | Physical association of two already-corresponded exact side-local Research attempts without asserting semantic identity |
| [Annotated Source diff document](annotated-source-diff-document.md) and its [API-relation and added-Member extension](https://github.com/richlander/dotnet-inspect/issues/9369) | Exact-Member side outcomes, admitted designated pairs, added-Member mapped comparisons, C# and optional IL text comparisons, line maps, and fact comparison |
| [Diff viewer interaction](inspect-web-diff-viewer-interaction.md) | Embedded and full-bleed rendering of one mapped diff |
| [Source-diff transport](inspect-web-source-diff-transport.md) | Bounded mapped rows and typed admission outcomes |
| [Operation Authority](inspect-web-operation-authority.md) | Per-execution identity, authorization, cancellation, supersession, and publication |
| Browser Navigation | Canonical Type and Member locations and atomic subject transitions |

## Why

Public API answers which declarations changed. It cannot answer which
implementations changed while their declarations stayed stable. The existing
Member reader is reached only through a changed API relation, so it hides the
most interesting implementation changes and makes the viewer appear absent for
ordinary unchanged declarations.

The motivating Gallery pair is
`System.Text.Json@11.0.0-preview.6.26359.118..11.0.0-preview.7.26381.103`.
Production `dotnet-inspect --implementation` reports 27 changed Member subjects
on `System.Text.Json.JsonSerializerOptions` before Browser public-population
scoping; the public API comparison exposes the new
`InferClosedTypePolymorphism` declaration but does not expose the added
assignment in
`JsonSerializerOptions(JsonSerializerOptions)`. Member Body makes the changed
constructor discoverable and opens its C# body comparison inline. This
observation motivates the design; the pinned package gate below is the
repeatable evidence.

The user-visible precedent is the content-first Member Compare composition in
[#9042](https://github.com/richlander/dotnet-inspect/pull/9042): summary first,
then the diff reader directly on the Member page. The full-bleed viewer in
[#9112](https://github.com/richlander/dotnet-inspect/pull/9112) is an expansion
of that result, not its primary home.

Member Body is the site's signature comparison experience: a current Member
opens directly into one polished document that makes a whole addition, a
signature change, a body change, or a combined signature-and-body change
immediately legible. Reachability alone is insufficient; each supported case
uses the same complete inline viewer.

## Content and hierarchy

The Compare content picker offers these closed choices in this order:

1. **API + Member Body**
2. **String literals**

API + Member Body is the default. It presents one exact union of declaration
and implementation changes, with one row when both evidence kinds describe the
same Member. Entering Compare may therefore perform local decompilation across
the selected Library pair; it does not request network source content. The
choice is retained in the Package model and stays active through Compare-owned
Library, Type, and Member navigation, including Up/Down Member navigation. It
is not portable Workspace state. Legacy shared Public API and Member Body links
restore into this merged content choice.

API + Member Body has one hierarchy:

| Subject | Presentation |
| --- | --- |
| Library | Changed Types with API, C#, and IL category chips |
| Type | Changed API and body-backed Members declared by that Type |
| Member | API change detail and, when present, one inline exact-Member body comparison |

Library and Type are inventories, not detail panes. A Type row carries a
managed-issued Type destination. A Member row carries a managed-issued exact
Member destination when its identity can be represented by Browser Navigation.
Activating a destination performs one atomic subject transition while retaining
Compare and Member Body.

Rows whose evidence cannot issue an exact destination remain visible with their
typed identity or analysis reason and are inert. The Browser never converts
`Display`, `TypeName`, or `MemberName` text into a selector and never resolves
an ordinal independently on each endpoint. A Member deleted from a surviving
current Type is intentionally one such Type-level result: because no current
Member exists, the row carries no Member destination and is not clickable.
When the whole Type was removed, its inert Library row retains the portable API
diff's occupied-side Member count and names; there is no Type page on which to
repeat those individual rows.

## Inventory operation

Member Body starts no work until the user selects it or returns to a retained
Member Body result. Its stable inventory cache key contains:

- the retained Package model generation;
- current and target package versions in their Compare order;
- framework and exact compile asset for each endpoint;
- exact Library assembly identity for each endpoint;
- the selected Implementation Diff mechanisms;
- and the public body-backed Member selections issued from both endpoint
  surfaces.

Operation Authority separately issues a fresh, never-reused identity for each
execution under that key. The identity authorizes pending work and publication;
it is not part of the cache key and is never reused to authorize a later
operation.

The first production slice consumes the explicit mechanism request from
[#9339](https://github.com/richlander/dotnet-inspect/issues/9339) and requests
C# and IL/body. Complexity may be added later as a separately visible summary;
it is not computed, reported, or used to decide whether a row is changed.

Managed code resolves exactly one Library assembly at each endpoint through the
retained Gallery scopes. The current compile asset remains exact. The baseline
selects its compatible compile slice independently and consumes the package
owner's [comparison counterpart](package-asset-selection-correspondence.md#comparison-counterparts).
Both inventory and retained Member execution use that same issued Library
pair. An API or body absent on Before remains a normal addition, not an
endpoint-selection failure. For that same pair it consumes the portable Library
API diff's complete changed-member relations. A `Changed` relation establishes
the exact public Before/After API pair, including a signature change; an
`Added` or `Removed` relation establishes the occupied side and absent side.
These Metadata-corresponded relations are the only semantic source for
signature and public addition/deletion topology.

Managed code also obtains the union of public body-backed Member selections
from the endpoint surface and correspondence owners and passes an explicit
selected-population request to `ImplementationDiffDocumentQuery`, carrying
API-owned exact paired selections for Research designation when strict body
correspondence is unavailable. Body-backed accessors associate through their
exact API anchor and producer-issued Getter, Setter, Adder, or Remover role;
roles are never guessed or paired by ordinal. That request
remains selected when the union contains zero Members, producing a complete
empty C#/IL result rather than whole-assembly work. The query therefore
compares only the requested public population; the host does not compare every
implementation and filter private rows afterward. The selected population and
mechanism semantics are owned and gated by #9339, not by this Browser
composition. A rejected or failed endpoint does not manufacture an empty
document. The result keeps its
`InspectionEnvelope<ImplementationDiffDocument>` baseline and ordered
diagnostics.

The Browser projection is a bounded, lossless composition of those two results
for this host. Managed code groups changed implementation subjects and API
relations by their exact owner-issued Type identities, retains every complete
`ResearchSubjectKey`, `LibraryApiMemberRelation`, mechanism result, coverage,
and typed failure, and joins canonical Navigation destinations where
available. A current Member appears once when API and body evidence overlap;
the managed projection associates them only through the API relation's exact
After `MemberAnchor` and its resolved Research attempt, never through display
text. This overlap applies to a method body and its logical API Member.
Property and Event API rows remain logical Members, while an owner-issued
Getter, Setter, Adder, or Remover body remains a distinct body-backed row; the
Browser does not duplicate the logical API relation onto each accessor. It
does not recompute correspondence. If complete Content fits the ordinary
Worker limits, the projection retains it; otherwise it returns the typed
transport rejection and no successful-looking inventory.

The projection admits at most 10,000 changed Member subjects and the ordinary
Worker's 83,886,080-character and 2,621,440-collection-entry limits. Admission
measures the complete serialized result, including Content, Share, diagnostics,
inventory rows, destinations, and coverage. The operation does not truncate
Members, evidence, or diagnostics to fit.

The real `System.Text.Json@9.0.20..10.0.12/netstandard2.0` pair motivates
transport regression coverage. Its complete Library inventory crosses the
former Worker limits; rejecting it under those stale limits prevented opening
the added `JsonDocumentOptions.AllowDuplicateProperties` getter. Managed
admission uses the shared ordinary-Worker JSON budget, measures the JSON tree
as JavaScript serializes it, counts every collection container and entry, and
reserves the one-element result tuple. The managed production test and
published Firefox journey retain this exact pair and open the added getter
in both C# and IL without discarding owning Content, Share, or diagnostics.

## Library and Type presentation

The common Compare frame remains quiet and stable while content changes:

```text
Compare System.Text.Json                              Diff
preview.6 -> preview.7                       Change target
API + Member Body | String literals

CHANGED  System.Text.Json.JsonSerializerOptions
         API  C#  IL

CHANGED  System.Text.Json.Serialization.Metadata.JsonTypeInfo
         C#  IL
...
```

The default inventory renders no aggregate or per-row counts. Each row keeps
state separate from the fully qualified subject and category chips. Complete
coverage is quiet. Incomplete coverage remains visible as concise C# or IL
unavailable, incomplete, or failed notices without the evaluated/exact/changed
census.

Type presentation narrows the settled Library result:

```text
Compare JsonSerializerOptions                          Diff
API + Member Body | String literals

CHANGED  System.Text.Json.JsonSerializerOptions.#ctor(...)
         C#  IL
CHANGED  System.Text.Json.Serialization.Metadata.JsonTypeInfo...
         API  C#  IL
ADDED    System.Text.Json.JsonSerializerOptions.get_InferClosedTypePolymorphism()
         API  C#
```

Mechanism, outcome, and subject are separate dimensions. Added, removed,
changed, unavailable, and failed are never collapsed into one visual
"changed" state. A Member with evidence from several mechanisms appears once;
its row summarizes those mechanisms without discarding their individual
outcomes.

Library-to-Type and Type-to-Member navigation does not rerun the assembly-wide
comparison. Back, Forward, and returning from the Member restore the same
settled inventory, selected row, list position, and useful focus.

A removed Member whose declaring Type survives has no current Navigation
subject. Its row and Before-side evidence remain visible in that Type
inventory, but it is inert. A removed Type instead keeps its occupied-side
Member count and names on its inert Library row; it has neither a Type
inventory nor descendant destinations. Added and paired Members with issued
current locations may open the Member result.

## Exact Member handoff

An active Member row supplies:

- the ordered comparison endpoints from the inventory request;
- its complete `ResearchSubjectKey` for a strict body correspondence, complete
  `LibraryApiMemberRelation` for an API addition or signature change, or both;
- the declaring Type identity;
- either one stable Member selector, the API relation's exact Before/After
  `MemberAnchor`s, or its exact After anchor with typed Before absence;
- nullable canonical Before and After Member locations;
- the inventory cache key; and
- one stable Annotated Source diff cache key derived in managed code.

The exact-Member request executes the Annotated Source diff query extended by
[#9369](https://github.com/richlander/dotnet-inspect/issues/9369). Its
input path follows the row's owner-issued evidence:

- A body-only row uses its strict Research correspondence and one stable
  selector.
- A signature-changed `LibraryApiMemberRelation` already establishes the
  semantic pair. Managed code resolves its exact Before and After anchors as
  side-local Research attempts under the same admitted question, then requests
  `ResearchDesignatedPairAdmission`. The admitted pair associates those
  physical endpoints with producer work; it does not assert correspondence.
- An added `LibraryApiMemberRelation` supplies exact After identity and typed
  Before absence. Managed code resolves only its occupied After anchor.

The Browser does not resolve one selector twice, turn `SelectionDrift` into
correspondence, infer that same-named methods correspond, or manufacture an
empty endpoint for an addition.

`BeforeOnly` remains valid inventory evidence but cannot be an active Member
handoff because it has no current Member destination.

Each execution for that cache key receives its own fresh Operation Authority
identity. Publication requires both the current semantic cache key and the
current operation identity; a retained settled result is read by cache key
without reusing its execution identity.

The handoff is authorized only while the retained Package model, target,
Library pair, content choice, subject, cache key, and fresh operation identity
still match. A late inventory or Member result changes no visible state after
any of those values changes.

## Member presentation

The Member page is the primary body-diff experience:

| Type inventory outcome | Member-page evidence |
| --- | --- |
| Added current Member | API relation's typed Before absence and exact After anchor produce an exact document whose empty Before sequence and complete After sequence lower to mapped C#/IL rows that are all additions |
| Signature changed | API relation's producer-corresponded anchors admit one Research designated pair, even when their stable selectors differ; the declaration and body share one mapped document |
| Body changed | Existing `Paired` C#/IL text comparison |
| Signature and body changed | The same designated pair and document show both changes together |

A deleted Member is not a Member-page case. When its declaring Type survives,
its Before-side identity and evidence remain visible in the Type inventory,
with no destination or click affordance. When its declaring Type was removed,
the inert Library row retains only that Type's occupied-side Member summary.

```text
Subject path: JsonSerializerOptions > .ctor
Content: Member Body    C# | IL   Explore
Diff baseline: preview.6 -> preview.7    Change target    Diff | Clone

───────────────────────────────────────────────────────────────
  184 184      _unknownTypeHandling = options._unknownTypeHandling;
      185 +    _inferClosedTypePolymorphism =
      186 +        options._inferClosedTypePolymorphism;
  185 187      _unmappedMemberHandling = options._unmappedMemberHandling;
```

The reader loads automatically from already acquired package evidence. It does
not require the authored-Source network action. C# is the default medium; IL is
available when the document carries it. Switching media runs no comparison.
The embedded reader uses the same mapped rows, whitespace and move controls,
keyboard navigation, narrow layout, and accessibility behavior as the shared
diff viewer.

The content selector and C#/IL/Explore actions occupy the existing page toolbar,
following the Source inspector. A separate row below those actions holds the
effective baseline, Change target, and Diff/Clone controls, following the
Analysis layout. The shell subject path identifies the Member. The diff
fills the content area without another subject heading or Member Body heading.
Added lines and the empty Before gutter explain an addition without a separate
Before-absence banner. Typed absence remains in the owning document.

The content selector and comparison controls remain available through loading,
identical, one-sided, unavailable, not-applicable, failed, and too-complex
results. Media and Explore actions use the retained document when available.
The body area shows:

| Document outcome | Inline presentation |
| --- | --- |
| Both sides Present, text differs | Mapped diff |
| Both sides Present, selected medium identical | **Identical** |
| AfterOnly added Member | Mapped diff with every present line added and an empty Before gutter |
| Medium Too complex | Typed limit and the other medium when available |
| Unavailable or NotApplicable side | The side's typed reason and any present other side |
| Projection or query Failed | Failure with retry when the context remains current |
| Canceled | **Canceled**, never an empty or identical diff |

An added or removed property relation is not itself a body Member. Its accessor
appears only when Implementation Diff issues that accessor as a body-backed
Member with an exact destination. The Browser does not invent accessor
selection from a Property row.

## Retention and Explore

One settled inventory is retained by stable inventory cache key for the
retained Package model. One settled Annotated Source diff document is retained
per exact-Member cache key. Available, identical, added-Member, unavailable,
not-applicable, and too-complex outcomes may be retained; failed and canceled
operations are not.

Returning to a Member with the same identity presents its retained reader
without running again. A new target, Library pair, Package generation, or
destination identity discards the old association. A pending superseded
operation is canceled, and its late completion cannot publish.

**Explore** is optional expansion of the settled Member document. It opens the
full-bleed diff viewer over the same mapped rows and current medium, preserving
the Member, Compare, Member Body, target, and history. Closing it restores the
embedded reader's medium, scroll position, and useful focus. Explore never
starts an independent comparison and is not required to see the body diff.

## States

The common Compare frame remains mounted through every state:

| State | Presentation |
| --- | --- |
| Not requested | Compare has not yet entered the default API + Member Body content |
| Loading inventory | Progress associated with the exact Library-pair request |
| Available, non-empty | Coverage and complete Type or Member inventory |
| Available, no changes | **No implementation changes found** with complete coverage |
| Available, incomplete | Rows plus explicit mechanism coverage and reasons |
| Inventory unavailable or failed | Typed endpoint, comparison, or transport reason |
| Loading Member | Stable Member heading and inline-reader progress |
| Settled Member | The outcome table above |

No-changes is valid only when every requested mechanism's coverage is complete.
Zero changed rows with unavailable, incomplete, or failed coverage is not an
empty success.

## Progressive disclosure

API + Member Body is the default Compare content and may perform local
decompilation across the selected Library pair. It does not request PDB
Source, SourceLink, repository access, or any other network source-content
operation. Authored Source remains separately explicit under its existing
owner.

The initial Member reader shows body text. The Annotated Source document's fact
comparison remains available for a later evidence layer, but the Browser does
not need code lenses to deliver the primary Member Body experience.

## Preserved follow-on

[PR #8550](https://github.com/richlander/dotnet-inspect/pull/8550) is
superseded as a placement design: Decompiler is not an Explore-only mode.
Its useful evidence-presentation requirements remain candidate input for a
later focused code-lens owner:

- added, removed, changed, conditional, and optionally present facts;
- exact line placement through document targets and line maps;
- a bounded summary for facts without a visible row;
- caller-supplied viewer row decorations; and
- fact visibility when text is identical.

That enrichment must present the same retained Annotated Source diff document.
It is not a prerequisite for Member Body inventory, navigation, or the inline
text reader.

Shared Member Body links use the inert exact Diff query attachment owned by
[Workspace definitions](workspace-definitions.md#exact-diff-query-attachment).
They retain the effective exact baseline, compile asset, Member anchor, body
selector, and C#/IL medium. Reopening restores Package settings before the
normal authorized Compare operation starts. It validates the acquired Library
and body identities and displays the inline diff automatically; an unavailable
body or medium remains a visible failure rather than a substituted selection.

## Non-claims

This design does not claim:

- selected-population or mechanism request semantics, which #9339 owns;
- API Member correspondence, which the portable Library API diff owns;
- designated-pair admission or added-Member comparison semantics, which
  existing Research and #9369 respectively own;
- new implementation correspondence, C#, IL, complexity, or fact semantics;
- semantic equivalence when body text is identical;
- authored Source acquisition or presentation;
- Property, Event, or Field comparison without an issued accessor Member;
- a Member page or destination for a deleted Member;
- private, internal, or protected implementation inventory;
- a generalized Browser analysis picker;
- local assembly, platform, or multi-Library comparison;
- a second live Workspace or comparison session; or
- code-lens content or interaction.

## Adoption

| Step | Delivers | Production host |
| --- | --- | --- |
| Inventory prerequisite | #9339 distinguishes whole from explicitly selected population, including zero selections, and executes exactly the requested Implementation Diff mechanisms | Shared Implementation Diff query; existing CLI defaults remain unchanged |
| Exact-Member prerequisite | #9369 consumes producer-corresponded Library API relations, admits their physical signature-change pair, and supplies added-Member mapped comparisons | Annotated Source diff document and Browser projection |
| MB1 | `member-body` choice, exact-pair operation, bounded managed inventory projection, Library and Type presentation, sticky drill-down | Inspect Web Compare |
| MB2 | Exact Member destination and Annotated Source diff Browser export, automatic inline C#/IL reader, retained result, optional Explore expansion | Inspect Web Member Compare |

MB1 follows the inventory prerequisite and consumes its request without
redefining it. MB2 follows the exact-Member prerequisite. MB1 and MB2 may land
as a stack, but MB2 is the production adoption target and the stack is not
complete until the viewer is visible on the Member page for additions,
signature changes, and body changes. Any temporary MB1-only deployment must
keep Member rows inert rather than route them to a placeholder or the API
Member detail.

The implementation retires any Explore-only Decompiler route it supersedes. It
reuses the shared diff viewer and one Annotated Source document per stable
cache key; it does not keep a parallel Member-body dialog or comparison path.

## Demo and gates

The production demo compares
`System.Text.Json@11.0.0-preview.6.26359.118..11.0.0-preview.7.26381.103`,
selects `System.Text.Json`, opens the default **API + Member Body** Diff, drills through
`JsonSerializerOptions`, and opens
`JsonSerializerOptions(JsonSerializerOptions)`. The Member page shows the
inline C# diff containing the new `_inferClosedTypePolymorphism` assignment;
switching to IL changes the retained medium without another comparison.

| Gate | Evidence |
| --- | --- |
| Release Implementation Diff request tests from #9339 | Whole population remains distinct from an explicitly selected empty population; C#+IL does not execute or report Complexity; document mechanisms, coverage, and completeness describe only requested work |
| Release Annotated Source tests from #9369 | Added API relation lowers to all-added C#/IL mapped rows; changed API relation resolves both exact anchors and admits one Research designated pair; signature and signature-and-body documents retain the API relation; malformed associations are rejected |
| Release managed Browser operation tests | Pinned System.Text.Json pair, exact endpoint order and assets, complete envelope, public selections including zero Members, current Member destinations, inert deleted rows in surviving Types, removed-Type Member summaries, identity-failure rows, mechanism coverage, transport bounds, cancellation, and stale publication rejection |
| Release Annotated Source Browser projection tests | Strict exact Member handoff, API relation handoff, admitted designated pairs, added-Member mapped comparison, Present/Absent/Unavailable/NotApplicable/Failed sides, changed and identical C#/IL media, Too complex admission, and no host-side correspondence |
| Node Compare composition tests | Closed content choices, explicit activation, sticky Library/Type/Member and Up/Down navigation, inert deleted and failure rows, restoration, and stale completion suppression |
| Node diff viewer tests | Embedded changed, identical, all-added, signature, failure, narrow, keyboard, whitespace, move, and medium-switch behavior |
| Published Firefox gate | Real Gallery package acquisition and inline Member reader through the generated Wasm facade |

Synthetic fixtures define deterministic failure, one-sided, collision, limit,
and stale-operation boundaries. The pinned System.Text.Json gate preserves the
real product value and must exercise product-owned document construction; the
harness does not manufacture or repair C# or IL.

## Acceptance scenarios

1. Compare the pinned System.Text.Json pair in the default API + Member Body
   content and confirm
   Library shows changed Types that Public API alone does not reveal.
2. Open `JsonSerializerOptions`, activate its copy constructor, and confirm the
   Member page immediately shows the C# diff with the added
   `_inferClosedTypePolymorphism` assignment.
3. Switch C# to IL and back and confirm no operation runs and the viewer keeps
   the same exact Member relation.
4. Move to the next or previous Member with Up/Down and confirm Compare and
   Member Body remain active while the exact destination and viewer change.
5. Open an added method and confirm the inline mapped diff renders every C#
   and IL line as added with an empty Before gutter and no absence banner.
6. Open a signature-changed method whose endpoint selectors differ and confirm
   its producer-issued API relation resolves both exact anchors, admits one
   Research designated pair, and shows the declaration and body together.
   Repeat with both signature and body changed and confirm ordinary Research
   correspondence still reports `SelectionDrift` for the divergent keys.
7. Confirm a removed method of a surviving Type remains visible in the Type
   inventory with Before-side evidence but has no navigation or click
   affordance and no Member page. Remove the entire declaring Type and confirm
   its inert Library row retains the occupied-side Member count and names but
   offers no Type or Member destination.
8. Open a Member with identical C# but changed IL and confirm each medium states
   its own outcome.
9. Exercise unavailable, incomplete, failed, canceled, and too-complex results
   and confirm none appears as no changes, identical, or empty source.
10. Exercise an identity-failure row and confirm its evidence remains visible,
   it has no navigation affordance, and the Browser does not derive one.
11. Open Explore and close it and confirm it uses the same result and restores
   the inline medium, scroll position, and focus.
12. Change the target while inventory and Member requests are pending and
    confirm neither stale completion can publish.
13. Return to the same Member and semantic cache key and confirm the settled
    reader appears without another comparison.
14. At a narrow viewport, confirm the inventory and embedded reader use one
    vertical scroll owner each and create no page-level horizontal overflow.
