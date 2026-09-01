# Inspection subject navigation

Inspection Subject Navigation is the product owner for choosing and retaining
the structural subject of one realized inspection coordinate. It supplies a
host-neutral contract for Root, Library, Type, and Member navigation so that
browser, CLI, and future hosts do not invent different defaults or recovery
rules.

## Status

This is the target architecture for issue #4794. Issue #5013 completes its
focused lens-recommendation semantics. The structural kind and exact subject
identity foundation is implemented by
`StructuralSubjectIdentity` and gated by
`StructuralSubjectIdentityTests.KindVocabulary_IsClosedAndStructurallyOrdered`,
`Identities_BindExactOwnerIssuedComponents`,
`MemberIdentity_BindsExactDeclaringTypeAndAnchor`, and
`Construction_RejectsAbsentOwnerIssuedComponents`. Exact lens identity,
retained evaluation bases, and pure lens recommendation are implemented by
`NavigationLensRecommendation` and gated at their claims below. Initial
subject selection, activation, reconciliation, revision behavior, retained
sessions, synchronization, and restoration remain unverified until their
implementation gates in [Verification](#verification) land.

The concurrency claims are specified separately as executable TLA+ models under
[`models/inspection-subject-navigation/`](models/inspection-subject-navigation/).
Those models check the design state machines; they do not prove that a future
C# or TypeScript implementation conforms to them.

Current Inspect Web code still chooses a default Type, widens accessibility to
admit that choice, stores subject levels independently, and reconciles them in
the browser. Package realization also rejects packages with no compile
libraries. These are migration facts, not authority for the target design.

## Problem

Inspection coordinates and structural subjects are different concepts. A
package, project, platform, or file identifies the input. Root, Library, Type,
and Member identify what the user is inspecting inside that input.

Today the host owns too much of that distinction:

- it chooses initial Type and lens state;
- it reconstructs parent relationships from browser data;
- it decides what survives version, framework, or inventory changes;
- it coordinates subject and lens requests with local mutable state; and
- it can expose partial or completion-order-dependent navigation.

That makes the website's defaults and recovery behavior impossible to reuse,
hard to test, and vulnerable to races between acquisition, refresh, and user
navigation.

## Decision

Inspection Subject Navigation owns:

- structural subject identity and hierarchy composition;
- subject applicability, availability, and failure classification;
- initial subject recommendation and subject-scoped lens recommendation;
- hierarchy, Library, Type, Member, and lens navigation descriptors;
- exact subject and lens activation outcomes;
- same-coordinate and coordinate-variation reconciliation;
- retained navigation-session authority; and
- the subject-and-lens participant in canonical restoration.

The owner returns one internally consistent navigation snapshot. Interactive
consumers render its descriptors and submit opaque commands from it. They do
not select defaults, infer identity from display text, or apply fallback after
a failed request.

The expected implementation is host-neutral and normally belongs in
`DotnetInspector.Queries`. The architecture owner is the contract described
here, not a project boundary.

## Ownership and boundaries

### Inputs

The owner consumes:

- one realized coordinate and its root descriptor;
- admitted Library identities, declaration order, and primary preference;
- bounded Type and Member inventories in producer-issued navigation order;
- product accessibility descriptors;
- exact type-definition identities and member anchors;
- product View Facet Registry target-aware options and exact-resolution
  results;
- typed identity-resolution and correspondence outcomes; and
- either a retained-session operation or an explicit stateless evaluation.

A retained operation reads prior state only from its navigation session.
Stateless evaluation may receive an explicit prior snapshot as data.

### Outputs

The owner returns:

- one active structured subject;
- one Type-inventory Library context;
- hierarchy and Library descriptors;
- Type and Member inventory rows wrapped with activation state;
- subject-scoped lens descriptors and one lens outcome;
- scoped diagnostics and partial-result evidence;
- typed transition or reconciliation outcomes;
- opaque retained-session authority; and
- a typed consumer-synchronization disposition plus a fresh-authority
  synchronization result for retained consumers.

### Adjacent owners

[Artifact acquisition and workspace
composition](artifact-acquisition-and-workspaces.md) owns coordinates, admitted
artifacts, and workspace lifetime. Root-capable package realization with no
compile Library is tracked by
[#4829](https://github.com/richlander/dotnet-inspect/issues/4829).

[Type, member, and API representation](type-member-api-representation.md) owns
the Type and Member identity currencies used here.

[Workspace definitions](workspace-definitions.md) owns portable view-facet
registry binding. The [View Facet Registry](view-facet-registry.md), established
by [#4880](https://github.com/richlander/dotnet-inspect/issues/4880), owns
runtime lens membership, labels, order, structural applicability, and
facet-availability outcomes.

[Inspect Web Navigation Presentation](inspect-web-navigation-presentation.md)
owns descriptor rendering, accessibility, and widget interaction; [Inspect Web
Navigation Consumer](inspect-web-navigation-consumer.md) owns post-result
effect-authority validation, snapshot/history commitment, and
result-authorized focus/announcement ordering.
[Workspace Definitions](workspace-definitions.md) owns portable projection and
complete restoration composition, tracked by
[#4787](https://github.com/richlander/dotnet-inspect/issues/4787).

### Non-claims

This owner does not define:

- coordinate acquisition, authorization, or lifetime;
- package, platform, project, or file construction;
- metadata, Type, Member, API, or view-facet registry internals;
- Type and Member inventory extraction;
- lens contents, section execution, or rendering;
- browser history, URL encoding, or complete restoration atomicity; or
- package-source selection, credentials, provenance, or caching.

## Domain model

### Structural subjects

Subjects form one ordered structural hierarchy:

| Level | Meaning |
| --- | --- |
| Root | The realized coordinate's product-owned root, such as Package |
| Library | All admitted libraries when aggregate inspection is supported, or one Library |
| Type | One exact type definition in one admitted Library |
| Member | One exact API member in one Type |

The hierarchy is a grammar, not a required navigation path. A Type or Member
may be activated directly.

Root is always applicable after coordinate realization. Lower levels remain
applicable when the coordinate kind supports them even if their inventories are
validly empty. Structurally unsupported levels are omitted; applicable but
empty levels remain visible as unavailable.

### Identity

The conceptual subject identity family is:

| Kind | Identity components |
| --- | --- |
| Root | Realized coordinate root identity |
| All Libraries | Coordinate plus explicit aggregate Library identity |
| One Library | Coordinate plus acquired Library identity |
| Type | Coordinate, acquired Library binding, and exact metadata definition |
| Member | Type identity plus product-owned member anchor |

Identity equality never uses display text, filename, list position, metadata
token alone, or backend arrival order. This is gated for the implemented
identity foundation by
`StructuralSubjectIdentityTests.Identities_BindExactOwnerIssuedComponents`
and `MemberIdentity_BindsExactDeclaringTypeAndAnchor`.

A navigation lens identity combines one exact structural subject identity with
one view-facet registry identity:

```text
NavigationLensIdentity
  Subject  StructuralSubjectIdentity
  Facet    ViewFacetId
```

The registry owns the stable facet identity; Inspection Subject Navigation
owns the exact subject binding. `type.api` on two exact Types therefore names
two navigation lenses, while Library Metadata and Type Metadata can share a
label without sharing either facet or navigation identity. Consumers treat the
combined identity as opaque and never reconstruct it from kind, display text,
or active UI state. This is gated by
`NavigationLensRecommendationTests.LensIdentity_BindsExactStructuralSubjectAndFacet`.

### Snapshot

One navigation snapshot contains:

| Field | Purpose |
| --- | --- |
| Generation | Scopes action IDs and snapshot-relative commands |
| Coordinate | Binds every subject and descriptor to one realized input |
| Active subject | The one committed Root, Library, Type, or Member |
| Type-inventory Library context | Scopes Type navigation independently of the active subject |
| Hierarchy descriptors | Ordered Root through Member context |
| Library descriptors | Aggregate, primary, then declaration order |
| Type and Member rows | Producer rows plus product activation state |
| Lens descriptors | Registry order, subject-scoped identity, and availability |
| Lens outcome | Effective identity or non-effective outcome, evaluation basis, and exact Registry evidence |
| Diagnostics | Partial evidence and scoped failures |

The snapshot is the retained session's only committed subject and lens state.
A host cannot supply a second retained-state value.

A lens outcome retains one evaluation basis:

| Basis | Retained input |
| --- | --- |
| Recommendation | Exact subject, preferred role, and complete target-aware Registry options |
| Exact request | Exact subject-bound navigation lens identity and exact Registry result |

Descriptor-bearing `Available`, `Unavailable`, and `Failed` exact results match
the requested subject kind because the Registry produces them only after
structural applicability succeeds. `Inapplicable` may describe another kind;
retaining that cross-kind descriptor is the exact evidence for rejecting the
request rather than treating it as unknown.

An effective outcome carries the selected exact navigation lens. A
non-effective recommendation outcome carries no invented lens identity; a
non-effective exact-request outcome retains the requested identity without
making it effective. This basis is product state used by reconciliation, not a
host hint. Recommendation installs a recommendation basis whether it selects a
lens or not. An explicit lens command installs an exact-request basis even when
it selects the same lens, recording that subsequent refresh must preserve the
exact request rather than resume automatic fallback.

The implemented basis shapes are gated by
`NavigationLensRecommendationTests.LensOutcome_RetainsRecommendationOrExactRequestBasis`.

### Descriptor states

Available descriptors carry an exact target and either `Current` or an opaque
generation-scoped action ID. Unavailable and failed descriptors carry no
target.

| State | Meaning |
| --- | --- |
| Available | An exact target can be activated |
| Unavailable | Successful evaluation proved that no target exists now |
| Failed | Availability could not be established |
| Selection required | Choices exist, but policy forbids an implicit default |

`Selection required` is used for Member context when choices exist but no
Member is committed. It is neither valid-empty nor failure.

Every bounded Type and Member inventory row is preserved in producer order and
wrapped with the same activation classification. Navigation does not create a
second inventory or omit rows because of host filters.

### Action IDs

Interactive consumers receive opaque action IDs for non-current available
Root, Library, Type, and Member descriptors. Action IDs are scoped to one
coordinate and generation and are distinct from structured identities.

Stale, foreign, unknown, or duplicated action IDs produce typed rejection
without state change. Canonical product peers may submit structured identities
through typed seams; browser display text never becomes a command currency.

## Product policy

### Initial subject

When no subject is committed, recommendation order is:

1. Type, when a trustworthy Type exists.
2. Library, when a Library subject is available.
3. Root.

Member is never implicit.

Type candidates use these tiers:

1. Primary Library and default accessibility.
2. Other Library and default accessibility.
3. Primary Library and non-default accessibility.
4. Other Library and non-default accessibility.

Within a tier, Libraries use primary-then-declaration order and Types use the
inventory producer's deterministic navigation order. UI filters, search text,
display labels, and arrival order never participate.

A trustworthy candidate from a successful participant may be selected when
another participant failed; every participant failure remains visible. If no
producer can vouch for a candidate, Type availability is failed rather than
delegated to the consumer.

### Initial Library and Root

Library recommendation selects:

1. `All libraries`, when its aggregate descriptor is available.
2. The available primary Library.
3. The first available one-Library descriptor in declaration order.

Unavailable or failed aggregate evidence remains visible when a one-Library
subject is selected.

When no Library is available, Root is selected. This allows root-only package
coordinates, including the tools-v2 pointer-package case tracked by #4829.

### Lens recommendation

Lens recommendation is a pure policy over one exact structural subject and the
target-aware options returned for that subject by one View Facet Registry
snapshot. It runs when an initial snapshot needs a lens and when activation or
reconciliation changes the exact subject without an explicit lens request.
Reactivating the unchanged current subject does not reset an effective lens. A
directly activated Member therefore receives the same owner-issued
recommendation as an initially recommended subject.

The preferred semantic roles are:

| Subject | Preferred lens role |
| --- | --- |
| Type | Type API |
| Member | Member overview |
| Library | Library references |
| Package-capable Root | Package overview |
| Other Root | Root overview |

Recommendation applies these rules in order:

1. Find the one applicable option carrying the subject's preferred role.
   Missing preferred-role or empty option input is a typed Navigation policy
   failure; it does not silently turn registry order into policy.
2. If the preferred option is available, select its exact subject-bound
   navigation lens even when another available option appears first.
3. If the preferred option is unavailable or failed, select the first
   available option in registry order. Preserve the preferred option's
   non-success evidence and every returned descriptor.
4. If no option is available and any option is failed, return a failed lens
   outcome with every failed and unavailable result retained.
5. Otherwise return an unavailable lens outcome with every unavailable result
   retained.

Navigation consumes Registry order as returned and never re-sorts by role,
title, ID, or local host preference. Registry `Retired` is one unavailable
reason and follows the same fallback rule. Failure dominates unavailability
when no available fallback exists because Navigation cannot claim that no lens
is available while an applicable option could not be evaluated.

Recommendation never changes the active subject. An unavailable or failed
recommendation leaves that exact subject active and installs the corresponding
lens outcome with no effective lens.

The pure recommendation policy is gated by
`NavigationLensRecommendationTests.LensRecommendation_UsesPreferredRoleBeforeRegistryOrder`,
`LensRecommendation_FallsBackToFirstAvailableInRegistryOrder`,
`LensRecommendation_ConsumesRegistryOrderWithoutResorting`,
`LensRecommendation_RetainsAllRegistryOptionsAndEvidence`,
`LensRecommendation_MissingPreferredRoleFails`,
`LensRecommendation_EmptyOptionsFails`,
`LensRecommendation_FailedDominatesUnavailableWhenNoOptionIsAvailable`,
`LensRecommendation_AllUnavailableReturnsUnavailable`, and
`MemberRecommendation_UsesMemberOverviewRole`.

### Type-inventory Library context

Type navigation has an explicit Library context:

| Active subject | Type-inventory context |
| --- | --- |
| Library | The active Library |
| Type or Member | The defining Library |
| Root | Available aggregate, then the highest-ranked trustworthy Type's Library, then primary or first available Library |

If no context can be established, the context is unavailable or failed. The
context does not activate Library or promote Root.

### Aggregate and single-library capability

`All libraries` is a real aggregate inspection mode, not a client-side
concatenation of independently rendered library pages. Aggregate evaluation
returns one owner-provided result that defines ordering, identity,
deduplication, and partial-failure behavior across the admitted library set.

Each Library-scoped lens declares explicit aggregate and single-library
capability, together with a visible rejection reason when the current subject
arity is unsupported. This is symmetric: an aggregate-only lens does not
report one-library data, and a single-library-only lens does not report an
aggregate. A lens exposes only the arities it can genuinely support; capability
is never inferred from source family or transport method.

The active Library subject controls every Library-scoped lens:

- `All libraries` requests a coordinate-wide result over the complete admitted
  Library set.
- An individual Library requests the same lens for only that Library.
- The selected Library subject persists when switching among returned Library
  lenses.
- A package-version or TFM change supplies the realized coordinate result to
  reconciliation, which decides whether that exact Library subject survives.

Because standalone lens activation requires the request's exact subject to
equal the snapshot's active subject (see
[Explicit activation](#explicit-activation)), switching lenses never silently
changes the Library subject to obtain a supported arity. An unsupported arity
is reported as `Unavailable` for that lens while the current Library subject
remains active and selectable for a supported lens.

## Activation and reconciliation

### Explicit activation

Subject and lens activation return one of these semantic outcomes:

| Outcome | State effect |
| --- | --- |
| Applied | Installs the exact requested subject or lens in a replacement snapshot |
| Unavailable | Applies no target or fallback; a completed exact lens evaluation installs its non-effective exact-request basis and evidence when either differs, while other operations install a replacement only when evaluation or reconciliation changes the snapshot |
| Rejected | Retains state because the command is stale, foreign, or invalid |
| Failed | A completed Registry or Navigation-policy lens evaluation installs its non-effective basis and evidence when either differs; Navigation preparation failure retains the prior snapshot |
| Superseded | Produces no visible effect because a newer explicit intent owns the session |

Standalone lens activation first requires the request's exact subject to equal
the snapshot's active subject. A mismatch is `Rejected` with the complete
request identity retained, before Registry resolution or fallback. It cannot
change the active subject. Canonical restoration's separately validated atomic
subject+lens pair remains governed by the restoration participant contract.

After that precondition succeeds, exact lens activation maps the View Facet
Registry result without fallback:

| Registry result | Navigation outcome |
| --- | --- |
| Available | `Applied` with the exact requested subject-bound lens, unless later Navigation preparation fails or the operation is superseded |
| Unavailable | `Unavailable` with the exact registry reason and a non-effective exact-request basis |
| Failed | `Failed` with the registry diagnostic identified as the source and a non-effective exact-request basis |
| Inapplicable | `Rejected` as structurally invalid for the exact subject |
| Unknown | `Rejected` as an unknown facet ID |

Every outcome retains the exact registry result and request identity, including
the absent descriptor in `Unknown`. A Navigation-owned preparation failure
after an available registry result remains distinguishable from a
Registry-owned failed result. Neither failure is rewritten as unavailable.
A valid exact request that completes as Registry `Unavailable` or `Failed`
installs its exact-request basis and evidence whenever that replacement differs
from the prior snapshot and its bound subject remains active. It does not
retain an earlier recommendation basis.

An unavailable request never silently activates a sibling, ancestor, or
recommended Type. If the already committed subject became invalid
independently, automatic reconciliation may change it before the unavailable
outcome is returned. When that reconciliation changes the exact subject, its
structural consistency takes precedence: the replacement snapshot installs a
recommendation basis for the replacement subject, while the operation result
still returns the original exact request's non-success outcome and evidence.
It never installs an exact-request basis bound to the inactive subject.

Outcome labels do not determine revision behavior. Every semantically changed
snapshot advances the state revision, including an unavailable result with
refreshed descriptors, a reconciled active subject, or a changed lens basis or
evidence. The same rule applies to a completed Registry or policy `Failed`
outcome. A non-success result shares the unchanged-snapshot outcome class only
when the complete snapshot is unchanged.

Selecting a Library does not also select a Type. Selecting a Type or Member
directly returns its complete ancestor context.

Activating a different exact subject without an explicit lens runs lens
recommendation for that subject. A prior lens is never carried to a different
subject merely because its registry facet ID or structural kind matches.

### Same-coordinate reconciliation

| Current subject | Reconciled subject |
| --- | --- |
| Root | Root |
| All Libraries | Retain when aggregate remains available; otherwise Root |
| One Library | Retain when available; otherwise aggregate, then Root |
| Type | Retain when available; otherwise highest-ranked trustworthy Type in its defining Library, then that Library, aggregate, then Root |
| Member | Retain when available; otherwise containing Type; if that Type is unavailable, apply the Type rule |

No arbitrary Member replaces a missing Member. Inventory refresh never promotes
an explicitly selected Root or Library to Type.

Lens reconciliation follows the retained evaluation basis:

- a recommendation-basis outcome, effective or non-effective, reruns
  recommendation for its retained exact subject against the refreshed complete
  Registry options;
- an exact-request-basis outcome, effective or non-effective, re-resolves its
  exact subject-bound lens identity and never applies recommendation fallback;
  and
- when subject reconciliation changes the exact subject, the prior basis no
  longer matches and Navigation runs recommendation for the replacement
  subject unless canonical restoration supplied an atomic exact pair.

This lets a recommendation recover when refreshed facts make a facet available
or replace a fallback with the now-available preferred role, without turning an
explicit request into a different lens. Every replacement outcome retains its
new basis and complete evidence.

### Coordinate variation

Coordinate variation uses typed owner-issued correspondence:

| Resolution | Result |
| --- | --- |
| Exact subject resolves and is available | Resolved subject |
| Member missing, Type resolves | Resolved Type |
| Type missing, defining Library resolves | Highest-ranked trustworthy Type in that Library, then the Library |
| Library missing | Available aggregate, then Root |
| Correspondence missing, ambiguous, refused, or failed | New coordinate's independent Type -> Library -> Root recommendation with diagnostic |

Display text, package ID alone, assembly name, token, and ordinal are not
correspondence.

For an unchanged coordinate, reconciliation failure retains the installed
snapshot and surfaces failure. For a newly realized coordinate with no prior
snapshot, Root is the fallback only when no trustworthy lower recommendation
exists; failed lower levels remain failed.

## Retained navigation session

Retained hosts use a product-owned session rather than coordinating snapshots
with host-local counters. The authoritative state machine is
[`NavigationSession.tla`](models/inspection-subject-navigation/NavigationSession.tla).

The model establishes these design guarantees:

- every explicit subject, lens, coordinate, or restoration request receives a
  product-issued monotonic intent token;
- a newer explicit intent supersedes older explicit results and in-flight
  maintenance results, while each same queued maintenance request survives,
  rebuilds from the replacement revision, re-gathers its facts, and remains in
  its original admission order;
- standalone maintenance is admitted in request order, not completion order;
- every queued maintenance request is retained and its own exact identity is
  eventually admitted;
- maintenance cannot install during unresolved explicit work or unconsumed
  visible effects;
- every admitted result receives exact session, state-revision, intent, and
  effect-epoch authority;
- every semantically changed snapshot advances the state revision regardless
  of its outcome label;
- every current result carries the complete installed snapshot and identifies
  whether the retained consumer must synchronize it before acknowledgement;
- consumer installation and product acknowledgement are separate state
  transitions, and each authority must be installed under its exact effect
  epoch before acknowledgement;
- acknowledgement advances the product-owned receipt only after that current
  installation;
- abandonment never advances the receipt, including when the consumer
  installed the snapshot but lost authority before acknowledgement;
- every bounded model synchronization request is settled by dedicated fresh
  authority or by acknowledgement of an intervening current result, without a
  product-side retry ceiling;
- stale or foreign authority cannot authorize a consumer-visible effect;
- prerequisite failure terminates the explicit operation without inventing a
  navigation result; and
- acknowledgement or abandonment releases queued maintenance.

A retained consumer treats tokens and authority as opaque. It validates
authority through the session before applying a returned result and again
before each deferred consumer-visible effect. Earlier validation is not
continuing authority.

Retained operations read the session's installed snapshot. The separate
stateless variant may consume an explicit prior snapshot and has no implicit
cross-command state.

### Consumer synchronization

One retained navigation session records the revision of the complete snapshot
last acknowledged by its retained consumer. This is a product-owned receipt,
not a caller-supplied prior snapshot. The consumer neither orders revisions nor
uses them as command identity.

Consumer installation is separate from that receipt. Applying a result records
the complete snapshot and exact effect epoch installed by the consumer, but
does not advance the product-owned receipt. Acknowledgement requires that the
consumer installed the result under the current authority's exact epoch.

Every current explicit or maintenance result carries the session's complete
installed snapshot and one typed disposition:

| Disposition | Consumer obligation |
| --- | --- |
| Current | The product-owned acknowledged consumer receipt already names this result's complete snapshot revision |
| Synchronization required | Install the complete result snapshot before acknowledging its authority |

The disposition is independent of semantic outcome. A rejected, failed,
aborted, or unchanged-unavailable result is still `Synchronization required`
when an earlier applied or maintenance result advanced the session before the
consumer installed it. The consumer presents the current semantic outcome only
after synchronizing the complete snapshot, so descriptors, generation-scoped
actions, diagnostics, and lens state come from one revision.

Acknowledgement confirms consumption of the result snapshot named by the
current authority and advances the product-owned consumer receipt. The session
rejects acknowledgement while synchronization is required and incomplete.
Abandonment releases the current authority but does not advance the receipt;
the debt survives supersession, destination destruction, and remount, including
when destruction occurs after installation but before acknowledgement.

A retained consumer may request synchronization without submitting a subject,
lens, coordinate, or restoration command. The session returns the latest
complete installed snapshot with fresh current authority and no semantic
navigation change. If standalone maintenance is already queued, its eventual
current result may discharge the same debt without changing request order;
otherwise the dedicated synchronization result is admitted after the queue
drains. Repeated remounts may request fresh authority again after abandonment;
the product contract imposes no retry ceiling.

A newer current result is also a synchronization vehicle. Product-side discard
of older superseded work publishes no authority, but the current result's
disposition is computed from the unchanged consumer receipt. If the consumer
still lags, even a non-installing semantic outcome requires the current complete
snapshot to be installed before acknowledgement.

This owner does not decide how a host renders the synchronization, classifies
browser history, or focuses a remounted surface. It supplies the complete
snapshot, typed disposition, and current authority needed for that owner to act.

## Canonical restoration participant

After packet decoding, coordinate realization, and portable identity
resolution, the canonical-state owner supplies one realized coordinate and the
optional exact subject and navigation lens requested for it.

Inspection Subject Navigation independently retains that requested payload,
requires the lens identity's exact subject to equal the requested subject,
resolves its subject and lens halves, and publishes one complete prepared
snapshot only when both halves succeed. A mismatched pair fails before Registry
resolution and aborts preparation. Any half-failure likewise aborts, and
supersession prevents an older preparation from being published. The focused
participant state machine is
[`AtomicRestoration.tla`](models/inspection-subject-navigation/AtomicRestoration.tla).

This owner does not install the prepared snapshot or coordinate other
restoration participants. Complete restoration composition and atomic commit
belong to [Workspace Definitions](workspace-definitions.md), tracked by
[#4787](https://github.com/richlander/dotnet-inspect/issues/4787). Section,
body, source-target, and other portable state remain outside this owner.

## Consumer contract

### Retained consumers

A retained consumer submits subject action IDs with their issuing generation
and submits lens identities through Inspection Subject Navigation. It treats
intent tokens and effect authority as opaque, applies no effect without current
authority, consumes the result's typed synchronization disposition, and
performs no subject or lens fallback after a non-applied outcome. It installs
the complete result snapshot before acknowledging `Synchronization required`,
may request fresh synchronization authority while its receipt lags, and
abandons authority it can no longer consume so queued maintenance can proceed.

Inspect Web presentation and accessibility belong to
[Inspect Web Navigation Presentation](inspect-web-navigation-presentation.md).
Focus, acknowledgement timing, and surface-destruction behavior belong to
[Inspect Web Navigation Consumer](inspect-web-navigation-consumer.md) and
issue #4917.

### Canonical state

The canonical-state owner consumes structured subject and lens identities.
Action IDs and retained-session authority are never serialized.

### Other hosts

Another retained host may use the same session model without adopting browser
layout. A stateless CLI may use recommendation and reconciliation without
retaining a navigation session.

## Verification

### Executable design models

| Model | Checked design properties |
| --- | --- |
| `NavigationSession.tla` | Latest explicit intent wins; completed unavailable and failed revision behavior follows complete-snapshot change; Navigation preparation failure retains snapshot and revision with a distinct source and fresh retained authority; maintenance is request ordered; abort and acknowledgement preserve liveness; stale authority has no effect; consumer acknowledgement requires synchronization; abandoned lag can obtain the latest snapshot under fresh authority |
| `AtomicRestoration.tla` | One exact requested subject+lens pair is prepared atomically; failed or superseded preparation is not published |
| `SnapshotAuthority.tla` | Retained state comes only from the installed snapshot; applied lens results equal the independently retained request; stale or foreign authority is rejected |

The model README records the TLC commands and scope. Model checking validates
these finite specifications, not the implementation.

Lens ranking, Registry-result classification, and the exact subject-plus-facet
identity structure are intentionally absent from the models: lenses remain
opaque values there. The pure recommendation, mapping, and identity-binding
rules above are enforced by the implementation gates below rather than claimed
as model-checked behavior.

### Required implementation gates

The eventual subject-navigation implementation must include named gates for:

- `KindVocabulary_IsClosedAndStructurallyOrdered`
- `Identities_BindExactOwnerIssuedComponents`
- `MemberIdentity_BindsExactDeclaringTypeAndAnchor`
- `Construction_RejectsAbsentOwnerIssuedComponents`
- `InitialRecommendation_PrefersTypeThenLibraryThenRoot`
- `TypeRecommendation_UsesPrimaryLibraryAccessibilityAndProducerOrder`
- `InitialRecommendation_NeverChoosesMember`
- `LensIdentity_BindsExactStructuralSubjectAndFacet`
- `LensOutcome_RetainsRecommendationOrExactRequestBasis`
- `LensRecommendation_UsesPreferredRoleBeforeRegistryOrder`
- `LensRecommendation_FallsBackToFirstAvailableInRegistryOrder`
- `LensRecommendation_ConsumesRegistryOrderWithoutResorting`
- `LensRecommendation_RetainsAllRegistryOptionsAndEvidence`
- `LensRecommendation_MissingPreferredRoleFails`
- `LensRecommendation_EmptyOptionsFails`
- `LensRecommendation_FailedDominatesUnavailableWhenNoOptionIsAvailable`
- `LensRecommendation_AllUnavailableReturnsUnavailable`
- `MemberRecommendation_UsesMemberOverviewRole`
- `StandaloneLensActivation_RejectsDifferentExactSubjectBeforeRegistryResolution`
- `ExplicitLensResolution_MapsEveryRegistryOutcomeWithoutFallback`
- `ExplicitLensResolution_RetainsExactRegistryEvidence`
- `ExactNonSuccess_InstallsExactRequestBasis`
- `NavigationPreparationFailure_RemainsDistinctFromRegistryFailure`
- `NavigationPreparationFailure_RetainsSnapshotAndRevision`
- `RecommendationBasis_RefreshRerunsRecommendation`
- `ExactNonSuccessLens_RefreshReresolvesExactIdentityWithoutFallback`
- `ExactNonSuccessDuringSubjectReconciliation_InstallsReplacementSubjectRecommendationBasis`
- `EveryBoundedInventoryRow_PreservesProducerOrderAndIdentity`
- `UnavailableDescriptor_HasNoTargetOrActionId`
- `ExplicitUnavailableTransition_DoesNotApplyFallback`
- `UnavailableReplacement_AdvancesStateRevision`
- `UnavailableUnchangedSnapshot_RetainsStateRevision`
- `UnavailableResult_InstalledRevisionMatchesRecordedResultRevision`
- `FailedReplacement_AdvancesStateRevision`
- `FailedUnchangedSnapshot_RetainsStateRevision`
- `FailedResult_InstalledRevisionMatchesRecordedResultRevision`
- `SameCoordinateReconciliation_FollowsSubjectTable`
- `CoordinateVariation_UsesTypedCorrespondence`
- `LensReconciliation_PreservesExactSubjectScopedIdentity`
- `RetainedSession_UsesInstalledSnapshotAsOnlyPriorState`
- `RetainedSession_RejectsCallerSuppliedPriorSnapshot`
- `RetainedSession_RejectsSuppliedSameSessionSnapshotCustody`
- `SuppliedPriorRejection_CorrelatesExactOperation`
- `AppliedResult_EqualsExactRequestedSubjectAndLens`
- `Maintenance_SerializesInRequestOrderAcrossCompletionTiming`
- `Maintenance_EveryQueuedRequestIsAdmittedByExactIdentity`
- `Maintenance_CannotInstallDuringUnconsumedEffect`
- `StaleBasisMaintenance_SameRequestRebuildsRegathersAndIsAdmitted`
- `EffectAuthority_RequiresExactCurrentSessionRevisionIntentAndEpoch`
- `ConsumerSynchronization_DispositionComesFromAcknowledgedRevision`
- `ConsumerSynchronization_DispositionIsIndependentOfSemanticOutcome`
- `ConsumerSynchronization_NonInstallingSuccessorCarriesCurrentSnapshot`
- `ConsumerSynchronization_InstallationDoesNotAdvanceReceipt`
- `ConsumerSynchronization_AcknowledgementRequiresCurrentEffectInstallation`
- `ConsumerSynchronization_AcknowledgementRequiresInstalledResult`
- `ConsumerSynchronization_AbandonmentPreservesDebt`
- `ConsumerSynchronization_RequestReturnsLatestSnapshotWithFreshAuthority`
- `ConsumerSynchronization_RemountCanRequestAgainAfterAbandonment`
- `ConsumerSynchronization_EveryRequestSettlesByCurrentResult`
- `ConsumerSynchronization_MaintenanceOrderAndLivenessArePreserved`
- `ExternalIntentAbort_ReleasesMaintenanceAfterAcknowledgement`
- `CanonicalRestoration_PreparedPairEqualsExactRequest`
- `CanonicalRestoration_RejectsMismatchedSubjectBoundLens`
- `CanonicalRestoration_FailedPreparationSettlesAsAbort`

`LensRecommendation_UsesPreferredRoleBeforeRegistryOrder` is the role-policy
non-vacuity gate: its preferred available descriptor is deliberately not first
in Registry order, and replacing role selection with first-available selection
must fail it. `LensRecommendation_RetainsAllRegistryOptionsAndEvidence`
compares the complete result with independently retained input options,
including non-selected unavailable and failed peers that cannot affect the
chosen lens. The exact mapping gate covers all five Registry results, and its
evidence gate likewise compares each result with independently retained input
evidence rather than reconstructing expected evidence from Navigation output.
The cross-subject activation gate uses the same facet ID on two exact subjects
and requires rejection before a throwing Registry-resolution sentinel. It
compares the returned complete request identity with independently retained
input. The canonical mismatch gate independently retains the requested subject,
the differently bound lens, and the restoration operation identity; it requires
the correlated pair to abort before a throwing Registry-resolution sentinel.
The exact-non-success gate begins with a recommendation basis, submits an exact
request returning `Unavailable` and `Failed` in separate cases, and requires
the installed replacement basis and evidence to equal the independent request
and Registry result before the refresh gate re-resolves that identity. The
subject-reconciliation gate invalidates the bound subject during those same
non-success cases and instead requires the installed snapshot to carry the
replacement subject's independently computed recommendation basis while the
operation result retains the original exact-request evidence.
The preparation-failure retention gate starts with an installed snapshot,
forces Navigation preparation to fail after Registry availability, and
requires the complete snapshot and revision to remain unchanged while the
result identifies Navigation as the failure source.

## Acceptance cases

| Case | Expected result |
| --- | --- |
| Ordinary package | Highest-ranked trustworthy Type with API lens |
| Preferred role is not first | Preferred available role, not the earlier available descriptor |
| Preferred lens unavailable | First available registry-ordered fallback with preferred evidence retained |
| No lens available and one evaluation failed | Failed lens outcome with all non-success evidence retained |
| Preferred role missing or options empty | Typed Navigation policy failure, never implicit first-option selection |
| Direct Member activation without a lens | Exact Member with Member Overview recommendation |
| Same facet on two exact Types | Two distinct subject-bound navigation lens identities |
| Lens request bound to another exact Type | Rejected before Registry resolution with active subject unchanged |
| Explicit inapplicable or unknown lens | Rejected with exact Registry evidence and no fallback |
| Failed recommendation becomes available on refresh | Recommendation reruns and installs the newly effective exact lens |
| Recommended fallback then preferred role becomes available | Recommendation replaces the fallback with the preferred exact lens |
| Explicit unavailable lens becomes available on refresh | Exact identity is re-resolved without considering a sibling fallback |
| Exact non-success while its subject disappears | Result retains the exact request evidence; installed snapshot uses the replacement subject's recommendation basis |
| Navigation preparation fails after Registry availability | Failed result identifies Navigation; snapshot and revision remain unchanged |
| Multi-library package | Aggregate then primary then declaration-order Library descriptors |
| Libraries with no Types | Library with References; Type is validly unavailable |
| Tools-v2 pointer package | Root with Package Overview; lower subjects unavailable |
| Only non-default-accessibility Type | Type remains the recommendation |
| Partial Type inventory | Deterministic successful candidate plus retained failures |
| Member disappears | Containing Type, never another Member |
| Type disappears with Library retained | Recommended Type in that Library, then Library |
| Coordinate correspondence is ambiguous | Independent new-coordinate recommendation plus diagnostic |
| Two lens requests complete out of order | Latest issued lens is final |
| Refresh and reconciliation complete out of order | Maintenance request order determines final snapshot |
| Coordinate acquisition fails | Prior snapshot retained; abort effect visible; maintenance eventually resumes |
| Canonical subject plus non-default lens | One prepared snapshot returns the exact requested pair with no partial result |
| Canonical subject plus lens bound to another subject | Preparation aborts before Registry resolution |
| Applied result is abandoned before consumer install | Product retains the applied snapshot; consumer receipt remains behind |
| Applied result is installed then abandoned before acknowledgement | Consumer-installed state advances, but the product-owned receipt and synchronization debt do not |
| Non-installing successor follows an abandoned applied result | Successor carries the complete current snapshot with `Synchronization required` |
| Maintenance completes while the consumer lags | Current maintenance result carries the complete current snapshot and may discharge the lag without bypassing request order |
| Consumer requests synchronization after abandonment | Latest complete snapshot returns under fresh current authority with no semantic navigation change |
| Consumer abandons synchronization and remounts | Receipt remains behind and a later request can obtain fresh synchronization authority again |
| Consumer acknowledges while still lagging | Acknowledgement is rejected and the product-owned consumer receipt does not advance |
| Snapshot contents return to an earlier value at a newer revision | `Synchronization required`; equal contents do not make generation-scoped state current |

## Non-goals

This design does not:

- define a universal identity for every coordinate or producer;
- require every structural level to be visited;
- make arbitrary Library subsets structural subjects;
- select a default Member;
- make UI filters part of subject identity;
- define view-facet registry membership;
- define portable packet fields or browser-history policy;
- define lens contents or section execution;
- authorize acquisition or expensive inspection work; or
- add implicit session state to stateless commands.
