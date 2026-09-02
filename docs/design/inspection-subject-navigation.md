# Inspection subject navigation

Inspection Subject Navigation is the product owner for choosing and retaining
the structural subject inside one exact inspection Workspace. It supplies a
host-neutral contract for Workspace, Package or non-package Root, Library,
Type, and Member navigation so that browser, CLI, and future hosts do not
invent different defaults or recovery rules.

## Status

This is the target architecture for issue #4794, corrected by #5434 to
de-conflate Workspace, retained-coordinate selection, and Package inspection.
Issue #5013 completes its focused lens-recommendation semantics. The
coordinate-rooted structural kind and exact subject identity subset is
implemented by
`StructuralSubjectIdentity` and gated by
`StructuralSubjectIdentityTests.KindVocabulary_IsClosedAndStructurallyOrdered`,
`Identities_BindExactOwnerIssuedComponents`,
`MemberIdentity_BindsExactDeclaringTypeAndAnchor`, and
`Construction_RejectsAbsentOwnerIssuedComponents`. That implementation does
not yet include Workspace or Package subjects or bind descendants to an exact
Workspace occurrence. Exact lens identity, retained evaluation bases, and pure
lens recommendation are implemented by `NavigationLensRecommendation` and
gated at their claims below for the implemented subject subset. Pure initial
subject ranking over already trustworthy Type candidates and already available
Library candidates is implemented by `NavigationInitialSubjectRecommendation`
and gated at its claim below for one already selected coordinate occurrence.
Generation-free classification of bounded Type and Member inventory evidence
is implemented by
`NavigationSubjectInventoryClassification` and gated at its claim below. Pure
standalone exact-lens activation is implemented by
`NavigationLensActivation` and gated by
`StandaloneLensActivation_RejectsDifferentExactSubjectBeforeRegistryResolution`,
`ExplicitLensResolution_MapsEveryRegistryOutcomeWithoutFallback`, and
`ExplicitLensResolution_RetainsExactRegistryEvidence`. Workspace and Package
identity, descriptor composition, subject activation, snapshot installation,
reconciliation, revision behavior, retained sessions, synchronization, and
restoration remain unverified until their implementation gates in
[Verification](#verification) land. The workspace-owned identity prerequisite
is tracked by #5508, Registry adoption by #5509, and portable
Workspace/Package subject projection by #5525.

The concurrency claims are specified separately as executable TLA+ models under
[`models/inspection-subject-navigation/`](models/inspection-subject-navigation/).
Those models check the design state machines; they do not prove that a future
C# or TypeScript implementation conforms to them.

PR #5433 demonstrates the intended browser distinction: Workspace manages
retained coordinates, Package is inspectable, and package tabs are absent.
Those browser identities and transitions remain host-local migration facts,
not authority for this product contract. PR #5501 refines only their responsive
presentation. The browser still chooses a default Type, widens accessibility
to admit it, reconstructs coordinate activation from package keys, and
reconciles subject levels locally; #5510 and #5511 track removal of those
migration paths.

## Consumer and complexity record

The end-to-end tracker is #5512. The concrete consumers are:

- the Browser/Wasm Workspace and subject-strip experience demonstrated by
  #5433, with product descriptor adoption tracked by #5510 and result-authority
  adoption by #5511; and
- the agent-inspectable CLI Workspace navigation surface tracked by #5513.

The first host-neutral implementation slice is #5518. Workspace Definitions
adoption for portable Workspace and Package subjects is #5525.

This is shared product substrate; no single-consumer or single-host exception
applies. The simplest sufficient boundary is one Navigation session bound to
one exact Workspace, with one Workspace inventory subject and mutually
exclusive Package or non-package Root subjects for its retained coordinate
occurrences. It adds no global Root, `All packages` subject, cross-Workspace
correspondence, or second concurrency protocol.

The exact Workspace and retained-occurrence ancestry is necessary for
correctness: without it, a replacement content or selection generation inside
one Workspace can alias its predecessor, and display keys or stale actions can
target the wrong retained occurrence. The existing opaque-snapshot TLA+ state
machines remain sufficient for Navigation-local intent, maintenance, and
authority ordering. A narrow protected-membership barrier additionally keeps
an external Workspace effect from being superseded before its correlated result
reconciles Navigation. That barrier, structural containment, and exact
owner-result correlation are implementation-gated rather than model-checked.

Navigation returns typed descriptors, identities, evidence, and outcomes. The
CLI consumer lowers those types through Markout. Browser/Wasm uses the
host-specific interactive rendering owned by
[Inspect Web Navigation Presentation](inspect-web-navigation-presentation.md)
because focus, accessibility, and responsive SlideStrip behavior are browser
concerns; it does not reconstruct product semantics from rendered text.

## Design demo

The production Browser/Wasm screenshots in #5433 are the visual oracle:
Workspace and Package are distinct subject-strip entries, the Workspace surface
lists retained coordinates without package tabs, and Package has its own
icon-backed inspection surface. #5501 preserves that subject identity while
changing only responsive strip allocation.

The contract behind the Workspace-selected case is:

```text
Subjects:   [Workspace*] [Package] [Library] [Type] [Member]
Inspectors: [Overview]

Workspace
  System.Text.Json 10.0.0 / net10.0    current
  Newtonsoft.Json 13.0.4 / net8.0
```

Selecting Workspace changes the active subject but retains the exact
`System.Text.Json` occurrence and descendant context. Activating
`Newtonsoft.Json` submits its opaque occurrence action and receives a new
Workspace-bound snapshot; no label or tab key identifies it. If the current
occurrence closes while old Type or lens work is in flight, that stale result
cannot install. Navigation consumes only the Workspace owner's returned
inventory and exact successor, when supplied.

## Problem

Workspace lifetime, retained-coordinate selection, and structural subjects are
different concepts. One Workspace owns an isolated set of retained coordinate
occurrences. Package or a non-package Root identifies what is inspected at one
such occurrence; Library, Type, and Member narrow within it.

Today the host owns too much of that distinction:

- it treats retained coordinates as package tabs and reconstructs their
  selection from browser state;
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
- Workspace, coordinate-root, Library, Type, Member, and lens navigation
  descriptors;
- exact subject and lens activation outcomes;
- same-occurrence and coordinate-variation reconciliation within one exact
  Workspace;
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

- one exact open Workspace identity and its ordered retained-coordinate
  occurrence descriptors;
- zero or one active retained-coordinate occurrence and its realized
  coordinate-root facts;
- owner-issued ordered membership, activation, admission, Close, replacement,
  and invalidation operations with their exact correlated outcomes;
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

- one Workspace-bound active structural subject;
- ordered retained-coordinate and coordinate-root descriptors;
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
composition](artifact-acquisition-and-workspaces.md) owns Workspace identity,
isolation, retained-coordinate membership and order, coordinates, admitted
artifacts, lifetime, and the operation contract for any membership effect that
can invalidate Navigation state. That contract begins the protected
Navigation transition before the owner effect and returns the complete ordered
inventory, effect disposition, typed evidence, optional exact admitted
occurrence, and optional exact successor in one correlated result. Navigation
consumes those identities and results without
defining identity construction, equality, membership policy, replacement
policy, or successor choice. The missing runtime identity and ordered
membership-transition projection is tracked by #5508.

Artifact acquisition and package realization own package coordinate,
`PackageRootBinding`, content-generation, selection, and acquired-descendant
identity currencies. #5508 composes the Workspace identity with those existing
currencies rather than minting a parallel package-occurrence identity.
Navigation consumes only that owner-issued exact binding; a portable package
coordinate alone cannot identify one retained occurrence.

[Type, member, and API representation](type-member-api-representation.md) owns
the Type and Member identity currencies used here.

[Workspace definitions](workspace-definitions.md) owns portable view-facet
registry binding. The [View Facet Registry](view-facet-registry.md), established
by [#4880](https://github.com/richlander/dotnet-inspect/issues/4880), owns
runtime lens membership, labels, order, structural applicability, and
facet-availability outcomes. Registry adoption of Workspace and Package
subjects is tracked by #5509.

[Inspect Web Navigation Presentation](inspect-web-navigation-presentation.md)
owns descriptor rendering, accessibility, and widget interaction; [Inspect Web
Navigation Consumer](inspect-web-navigation-consumer.md) owns post-result
effect-authority validation, snapshot/history commitment, and
result-authorized focus/announcement ordering.
[Workspace Definitions](workspace-definitions.md) owns portable projection and
complete restoration composition. #4787 established the current version-2
shape; #5525 tracks adoption of explicit Workspace and Package subjects plus an
optional retained occurrence and descendant context independent from the active
subject.

### Non-claims

This owner does not define:

- Workspace identity construction, opening, closing, retention, ordering, or
  lifetime, or membership policy;
- coordinate acquisition, authorization, admission, occurrence identity, or
  successor selection;
- package, platform, project, file, or package-icon construction;
- metadata, Type, Member, API, or view-facet registry internals;
- Type and Member inventory extraction;
- lens contents, section execution, or rendering;
- browser history, URL encoding, or complete restoration atomicity;
- package-source selection, credentials, provenance, or caching; or
- cross-Workspace inspection, aggregation, correspondence, or Spotlight
  composition.

## Domain model

### Structural subjects

Subjects form one Workspace-rooted grammar:

| Level | Meaning |
| --- | --- |
| Workspace | One exact open Workspace and its retained-coordinate inventory; Navigation itself does not combine descendant inspection results across occurrences |
| Package | One exact retained package occurrence in that Workspace |
| Root | One exact retained non-package coordinate root in that Workspace |
| Library | All admitted libraries for one Package or Root when aggregate inspection is supported, or one exact Library |
| Type | One exact type definition in one admitted Library |
| Member | One exact API member in one Type |

Its shape is:

```text
Workspace -> (Package | Root) -> Library -> Type -> Member
```

Workspace is the container and inventory for retained coordinate occurrences. Package and
Root are mutually exclusive coordinate-root variants, not aggregate and
single-package forms. `All libraries` is the only structural aggregate below
Workspace. The hierarchy is a grammar, not a required navigation path; a
Package, Root, Library, Type, or Member may be activated directly when its
complete ancestry is supplied.

Workspace is always applicable while its owner-issued lifetime remains open.
Exactly one coordinate-root variant is applicable for each retained
occurrence. Lower levels remain applicable when that occurrence supports them
even if their inventories are validly empty. Structurally unsupported levels
are omitted; applicable but empty levels remain visible as unavailable.
The variant follows the owner-issued realized-coordinate kind, never coordinate
text, an icon, a package-shaped display label, or host flags.

### Identity

The conceptual subject identity family is:

| Kind | Identity components |
| --- | --- |
| Workspace | Owner-issued runtime Workspace occurrence identity |
| Package | Workspace identity plus exact retained `PackageRootBinding` occurrence |
| Root | Workspace identity plus exact retained non-package occurrence identity |
| All Libraries | Exact Package or Root plus explicit aggregate Library identity |
| One Library | Exact Package or Root plus acquired Library identity |
| Type | Exact Library binding plus exact metadata definition |
| Member | Type identity plus product-owned member anchor |

Identity equality never uses display text, filename, list position, metadata
token alone, portable package coordinate alone, browser cache key, or backend
arrival order. The Workspace and retained-coordinate identities are
process-local and never serialized. Their adjacent owner issues them; #5508
owns their construction and lifetime.

The current coordinate-rooted `StructuralSubjectIdentity` implementation is
replaced in place rather than retained as a parallel identity family. Its
closed-kind, component-binding, and construction gates must be updated to this
Workspace-rooted grammar while preserving their existing exact Type and Member
witnesses.

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
| Workspace | Binds the session, every subject, descriptor, action, lens, basis, and diagnostic to one exact isolation boundary |
| Active coordinate occurrence | Names the exact Package or non-package Root ancestry whenever one occurrence is active, including while Workspace is the active subject |
| Active subject | The one committed Workspace, Package, Root, Library, Type, or Member |
| Type-inventory Library context | Scopes Type navigation independently of the active subject |
| Retained-coordinate descriptors | Owner-ordered exact occurrences available from Workspace |
| Hierarchy descriptors | Ordered Workspace through Member context for the active occurrence |
| Library descriptors | Aggregate, primary, then declaration order |
| Type and Member rows | Producer rows plus product activation state |
| Lens descriptors | Registry order, subject-scoped identity, and availability |
| Lens outcome | Effective identity or non-effective outcome, evaluation basis, and exact Registry evidence |
| Diagnostics | Partial evidence and scoped failures |

The snapshot is the retained session's only committed subject and lens state.
One retained session is bound to one exact Workspace occurrence for its
lifetime. Workspace binding is carried transitively by every subject identity,
and therefore by every subject-bound lens and evaluation basis. The session
never installs or reconciles a subject or accepts an action or restoration
payload from another Workspace. A host cannot supply a second retained-state
value.

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
| Pending | Owner evaluation or realization has not settled |
| Unavailable | Successful settled evaluation proved that no target exists now |
| Failed | Availability could not be established |
| Selection required | Choices exist, but policy forbids an implicit default |

`Selection required` is used for Member context when choices exist but no
Member is committed. It is neither valid-empty nor failure.

Every bounded Type and Member inventory row is preserved in producer order and
wrapped with the same activation classification. Navigation does not create a
second inventory or omit rows because of host filters.

### Action IDs

Interactive consumers receive opaque action IDs for non-current available
Workspace, Package, Root, Library, Type, and Member descriptors. Action IDs
are scoped to one exact Workspace and generation and are distinct from
structured identities.

Stale, foreign-Workspace, unknown, or duplicated action IDs produce typed
rejection without state change. Canonical product peers may submit structured
identities through typed seams; browser display text never becomes a command
currency.

Retained-coordinate descriptors separately carry an owner-issued exact
occurrence identity, owner order, current status, and independently optional
Navigation-issued activation and Close actions. Navigation resolves either
action to the exact occurrence; the host never submits a package key or display
label. An owner-loading status maps to `Pending` activation, retains the
owner's typed status and evidence, and carries no activation action. A failed
status maps to `Failed` activation and likewise carries no activation action.
Either descriptor may still carry Close when the Workspace owner says that
exact occurrence is closable. Current available occurrences omit activation
but retain Close independently.

The Workspace owner defines and performs Close over the same exact occurrence
identity, but a retained Navigation session is the sequencing entry point for a
Close that can invalidate its snapshot. Navigation synchronously issues a new
protected coordinate intent before invoking the descriptor's owner-issued Close
operation. That intent supersedes older work, but no later explicit command is
admitted until the correlated membership result settles; an attempted command
is rejected as `membership transition in progress` without issuing a newer
intent. The host submits the opaque Close action through Navigation rather than
invoking the owner around it. Only the exact owner result correlated to that
intent and requested occurrence may settle it.

The owner result separates effect disposition from diagnostic outcome:

| Effect disposition | Required result |
| --- | --- |
| No effect | Exact transition identity, proof that membership is unchanged, complete ordered inventory, and typed success, rejection, or failure evidence |
| Effect applied | Exact transition identity, complete resulting ordered inventory, typed success or failure evidence, optional exact admitted occurrence, and optional exact successor |

An admission request may have no pre-existing occurrence. Its correlated
`No effect` rejection or failure therefore carries no invented occurrence. A
successful admission supplies the exact admitted occurrence. An
`Effect applied` result always reconciles Navigation from its complete
inventory even when the owner also reports failure evidence; Navigation never
retains a snapshot whose membership the owner changed. A `No effect` rejection
or failure retains the prior snapshot and typed evidence only after the owner
result establishes unchanged membership. A host never invokes Close around
the retained session and later reports the result as maintenance.

The same protected handoff applies to owner-initiated admission, removal,
replacement, or invalidation that can change retained membership or make an
installed occurrence unusable. The Workspace owner begins the Navigation
transition before publishing or performing the invalidating effect, then
returns one correlated complete result with the effect disposition above. A
begun protected transition must eventually settle with that exact result;
Navigation consumes it, reconciles or retains state according to its effect
disposition, and releases the explicit-admission barrier. Cancellation or
failure uses `No effect` only after proving unchanged membership. A status
refresh that cannot change membership or invalidate the installed occurrence
remains ordinary maintenance.

## Product policy

### Initial subject

When no subject is committed and one exact retained-coordinate occurrence is
already active, recommendation order is:

1. Type, when a trustworthy Type exists.
2. Library, when a Library subject is available.
3. The occurrence's exact Package or non-package Root.

Member is never implicit.

When no retained-coordinate occurrence is active, Workspace is selected,
whether the inventory contains zero, one, or several entries. Navigation never
chooses an inventory entry from cardinality or order. A successful admission,
restoration, or activation action supplies the exact active occurrence; only
that owner-returned applied result establishes it. When the operation supplies
no exact subject request, Navigation applies the recommendation above only
inside that occurrence. A `No effect` rejection or failure leaves Workspace
selected with the original typed evidence. An `Effect applied` failure
reconciles the returned inventory but establishes no active occurrence unless
the owner supplies one exactly. The CLI consumer in #5513 and canonical
restoration must supply an exact occurrence before expecting a lower initial
subject.

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

### Initial Library and coordinate root

Library recommendation selects:

1. `All libraries`, when its aggregate descriptor is available.
2. The available primary Library.
3. The first available one-Library descriptor in declaration order.

Unavailable or failed aggregate evidence remains visible when a one-Library
subject is selected.

When no Library is available, the exact coordinate root is selected: Package
for a package occurrence and Root for a non-package occurrence. This allows
root-only package occurrences, including the tools-v2 pointer-package case
implemented by #4829.

### Bounded subject inventory classification

Navigation classifies one bounded API-surface result over the admitted Library
participants of one exact retained-coordinate occurrence before
snapshot-relative descriptors are composed. Participant outcomes exact-join
the admitted Library prefix by owner-issued acquisition registration; a
foreign-Workspace, foreign-occurrence, reordered, duplicated, or unexplained
missing outcome is invalid input rather than evidence about subject
availability.

The generation-free classification follows this table:

| Producer evidence | Type inventory outcome |
| --- | --- |
| One or more returned Types with exact definition identity | `Available`; retain every exact Type and Member row in producer order plus all peer evidence |
| Complete successful production with zero Types and no inspection failures | `Unavailable` |
| No exact Type plus participant rejection, participant failure, inspection failure, missing exact Type or projected-Member declaring-Type identity, or projection omission | `Failed` with the original typed evidence |
| Exact Types plus any of those failures | `Available` and partial; retain the exact rows and original typed evidence |

Projection truncation never proves that an omitted Library is empty. A returned
Type without exact `MetadataTypeDefinitionName` is retained as identity-failure
evidence and is not reconstructed from display text, metadata token, or list
position. #5437 added the exact typed declaring-Type definition identity to a
Member projected onto another Type. The current Navigation classifier has not
yet adopted that field and still retains the complete producer row as typed
identity-failure evidence rather than rewriting it as a declaration on the
containing Type. The Workspace-rooted implementation consumes the typed
identity and never reconstructs it from canonical declaring text. Returned
exact rows remain trustworthy when another row or participant fails; failure
does not erase positive evidence.

Every admitted Library remains an available Library candidate for initial
subject recommendation. Only exact returned Type rows become Type candidates.
Classification does not commit the recommendation, choose an active subject,
compose `Current` or `Selection required`, mint generation-scoped actions, or
produce a navigation snapshot.

This classification is gated by
`NavigationSubjectInventoryTests.EveryBoundedInventoryRow_PreservesProducerOrderAndIdentity`,
`ProjectedMemberWithoutTypedDeclaringIdentity_FailsClosed`,
`SuccessfulProducerRows_AreTrustworthyDespitePeerFailure`,
`CompleteSuccessfulEmptyInventory_IsUnavailable`,
`NoCandidateWithIndeterminateProducer_IsFailed`,
`ProjectionTruncation_NeverProvesUnavailability`,
`ProducerEvidence_IsRetainedWithoutTranslation`,
`InitialCandidates_ContainOnlyTrustworthyExactRows`, and
`InventoryJoin_RequiresExactParticipantRegistration` for the implemented
coordinate-rooted subset. Workspace-occurrence binding remains unverified.

The pure ranking over already trustworthy Type candidates and already
available Library candidates is gated by
`NavigationInitialSubjectRecommendationTests.InitialRecommendation_PrefersTypeThenLibraryThenRoot`,
`TypeRecommendation_UsesPrimaryLibraryAccessibilityAndProducerOrder`, and
`InitialRecommendation_NeverChoosesMember`. Candidate coordinate, Library,
Type, primary-role, and accessibility consistency is gated by
`CandidateConstruction_RejectsInconsistentOwnerIssuedEvidence`. The bounded
classification above supplies the trustworthy Type candidates and retains
availability and failure evidence. These gates establish ranking only after one
coordinate occurrence is selected; they do not choose among Workspace
inventory entries.

### Lens recommendation

Lens recommendation is a pure policy over one exact structural subject and the
target-aware options returned for that subject by one View Facet Registry
snapshot. It runs when an initial snapshot needs a lens and when activation or
reconciliation changes the exact subject without an explicit lens request.
Reactivating the unchanged current subject does not reset an effective lens. A
directly activated Member therefore receives the same owner-issued
recommendation as an initially recommended subject.

After the Registry adoption tracked by #5509, the preferred semantic roles
are:

| Subject | Preferred lens role |
| --- | --- |
| Workspace | Workspace overview |
| Package | Package overview |
| Root | Root overview |
| Type | Type API |
| Member | Member overview |
| Library | Library references |

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
`MemberRecommendation_UsesMemberOverviewRole` for the implemented subject
subset. Workspace and Package recommendation remain unverified until #5509
lands and replacement gates exercise those exact subject kinds.

### Type-inventory Library context

Type navigation has an explicit Library context:

| Active subject | Type-inventory context |
| --- | --- |
| Library | The active Library |
| Type or Member | The defining Library |
| Package or Root | Available aggregate, then the highest-ranked trustworthy Type's Library, then primary or first available Library |
| Workspace | Defining Library of the deepest retained Type or Member; otherwise the deepest retained Library; otherwise apply the Package-or-Root rule to the retained root; none without retained occurrence context |

If no context can be established, the context is unavailable or failed. The
context does not activate Library or promote Package, Root, or Workspace.
Workspace context is derived from the retained path and realized occurrence
facts; it is not an independently selectable or caller-authored Library.

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
  Library set of the active retained occurrence.
- An individual Library requests the same lens for only that Library.
- The selected Library subject persists when switching among returned Library
  lenses.
- A package-version or TFM change supplies an exact replacement occurrence to
  reconciliation, which decides whether that exact Library subject survives.

Navigation's `All libraries` evaluation never combines Libraries from sibling
coordinate occurrences or another Workspace. No Navigation snapshot combines
inspection evidence from sibling occurrences. The Workspace subject may expose
owner-issued retained-coordinate descriptors, but a future lens that needs
cross-occurrence inspection evidence requires its own focused owner contract
and Navigation adoption rather than an implicit exception here.

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

The pure exact-request boundary is gated by
`StandaloneLensActivation_RejectsDifferentExactSubjectBeforeRegistryResolution`,
`ExplicitLensResolution_MapsEveryRegistryOutcomeWithoutFallback`, and
`ExplicitLensResolution_RetainsExactRegistryEvidence`. Snapshot replacement,
revision advancement, and installation of an exact-request basis remain
unverified until their separately named gates land.

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

Selecting Workspace changes only the committed active subject. It preserves
the active retained-coordinate occurrence and its descendant context when one
exists, allowing the Workspace surface to identify that current entry and the
subject strip to retain Package or Root, Library, Type, and Member context.
It never changes the occurrence implicitly.

Activating an exact retained occurrence is a coordinate request, not
display-label or tab selection. It restores an explicitly supplied exact
subject when valid; otherwise it runs initial recommendation only within that
occurrence.

Selecting Package or Root directly keeps the same exact coordinate occurrence
and installs that coordinate-root subject. Selecting a Library does not also
select a Type. Selecting a Type or Member directly returns its complete
Workspace, coordinate-root, and structural ancestor context.

Activating a different exact subject without an explicit lens runs lens
recommendation for that subject. A prior lens is never carried to a different
subject merely because its registry facet ID or structural kind matches.

Every structured subject request and restoration payload carries the session's
exact Workspace transitively through subject identity; every action is scoped
to it explicitly. A foreign-Workspace value is rejected before Registry
resolution, correspondence, or fallback.

### Reconciliation

| Current subject | Reconciled subject |
| --- | --- |
| Workspace | Workspace |
| Package | Package while its occurrence remains retained; otherwise reconcile within the exact successor supplied by the Workspace transition, or Workspace when none is supplied |
| Root | Root while its occurrence remains retained; otherwise reconcile within the exact successor supplied by the Workspace transition, or Workspace when none is supplied |
| All Libraries | Retain when aggregate remains available; otherwise the exact Package or Root |
| One Library | Retain when available; otherwise aggregate, then the exact Package or Root |
| Type | Retain when available; otherwise highest-ranked trustworthy Type in its defining Library, then that Library, aggregate, then the exact Package or Root |
| Member | Retain when available; otherwise containing Type; if that Type is unavailable, apply the Type rule |

Navigation reconciles one retained context with one root-first algorithm:

1. **Establish the retained root.** If the exact occurrence remains retained,
   keep its Package or Root. If the Workspace owner supplies an exact successor,
   establish that successor's Package or Root. If neither exists, clear retained
   context and select Workspace.
2. **Resolve the retained path.** Starting at the established root, resolve each
   retained Library, Type, and Member in ancestry order. Same-occurrence refresh
   uses exact availability; successor movement uses typed correspondence. Each
   resolved node must be an exact descendant of the preceding result.
3. **Apply one fallback.** At the first unresolved path node, apply the table's
   fallback for that level inside the established root and truncate every lower
   node. Missing or ambiguous correspondence follows the same rule with its
   diagnostic. No fallback crosses the established root or Workspace.
4. **Derive the active subject.** Workspace remains active independently. A
   non-Workspace active subject uses its resolved path node when present;
   otherwise it becomes the fallback result produced for that level. Retained
   nodes below an unchanged or exactly resolved active ancestor remain context
   without becoming active.
5. **Complete the snapshot.** Rebuild contiguous hierarchy descriptors, derive
   Type-inventory Library context from the resulting path and current realized
   facts, then reconcile the active subject's lens basis.

For example, `Package -> Library -> Type -> Member` with Package active retains
a correspondable complete path across an exact successor occurrence. A missing
Member truncates the path to Type while Package remains active. The identical
path with Workspace active produces the same retained result while Workspace
remains active. The active subject no longer controls whether the path receives
same-occurrence or successor reconciliation.

No arbitrary Member replaces a missing Member. Inventory refresh never promotes
an explicitly selected Workspace, Package, Root, or Library to Type. Navigation
never chooses a sibling occurrence after removal. It consumes the exact
successor selected by the Workspace owner's transition, when present; otherwise
Workspace remains active with no active occurrence. This deliberately removes
the browser's current package-key-based successor choice in #5510 and #5511.

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

### Retained-coordinate variation

Step 2 of the root-first algorithm uses typed owner-issued correspondence when
the retained root moves between exact occurrences inside one Workspace:

| Resolution | Result |
| --- | --- |
| Exact subject resolves and is available | Resolved subject |
| Member missing, Type resolves | Resolved Type |
| Type missing, defining Library resolves | Highest-ranked trustworthy Type in that Library, then the Library |
| Library missing | Available aggregate, then the new occurrence's exact Package or Root |
| Correspondence missing, ambiguous, refused, or failed | New occurrence's independent Type -> Library -> Package-or-Root recommendation with diagnostic |

Display text, package ID alone, portable coordinate equality, assembly name,
token, and ordinal are not correspondence.

For an unchanged occurrence, failure to evaluate reconciliation retains the
installed snapshot and surfaces failure. For a newly activated occurrence with
no prior retained path, Navigation runs independent initial recommendation;
correspondence is not invented. Failed lower levels remain failed.

Correspondence never crosses a Workspace boundary. A different exact Workspace
uses a different retained navigation session and independently selected or
restored state.

A retained-membership change begins as explicit coordinate intent over the
same exact Workspace before the Workspace owner performs or publishes its
effect, including owner-initiated removal or replacement. When it removes or
replaces an occurrence while subject or lens work is in flight, that protected
intent supersedes stale work. Later explicit commands are not admitted until
the exact correlated owner result settles, so the external effect cannot occur
while a newer Navigation intent owns the session. Reconciliation uses only
that result's complete inventory and exact successor, if any. Non-invalidating
status refresh remains ordinary maintenance.

## Retained navigation session

Retained hosts use a product-owned session rather than coordinating snapshots
with host-local counters. The authoritative state machine is
[`NavigationSession.tla`](models/inspection-subject-navigation/NavigationSession.tla).

The model establishes these design guarantees:

- every admitted explicit subject, lens, retained-coordinate, or restoration
  request receives a product-issued monotonic intent token;
- a newer admitted explicit intent supersedes older explicit results and in-flight
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
lens, retained-coordinate, or restoration command. The session returns the
latest complete installed snapshot with fresh current authority and no
semantic navigation change. If standalone maintenance is already queued, its
eventual current result may discharge the same debt without changing request
order; otherwise the dedicated synchronization result is admitted after the
queue drains. Repeated remounts may request fresh authority again after
abandonment; the product contract imposes no retry ceiling.

A newer current result is also a synchronization vehicle. Product-side discard
of older superseded work publishes no authority, but the current result's
disposition is computed from the unchanged consumer receipt. If the consumer
still lags, even a non-installing semantic outcome requires the current complete
snapshot to be installed before acknowledgement.

This owner does not decide how a host renders the synchronization, classifies
browser history, or focuses a remounted surface. It supplies the complete
snapshot, typed disposition, and current authority needed for that owner to act.

`NavigationSession.tla` does not model the external Workspace effect or the
protected-membership admission barrier. Its opaque `coordinate` intent covers
Navigation-local coordinate activation and variation under ordinary
latest-admitted-intent supersession. The barrier and mandatory consumption of
the correlated membership result are enforced by the named implementation
gates below.

## Canonical restoration participant

After packet decoding, Workspace and retained-coordinate realization, and
portable identity resolution, the canonical-state owner supplies one exact
Workspace, zero or one exact retained occurrence context, and the optional
exact active subject and navigation lens requested inside it.

The retained occurrence context is independent from the active subject. It
contains:

- the exact retained occurrence and its Package or non-package Root;
- one contiguous optional exact retained Library, Type, and Member path beneath
  that occurrence.

An explicitly selected Workspace may therefore retain one complete occurrence
context without making any descendant the active subject. Two Workspace-selected
snapshots with the same occurrence but different retained Type or Member
contexts remain distinct restoration inputs. No active occurrence means no
retained occurrence context.

Inspection Subject Navigation independently retains that requested payload,
requires every identity in the retained context to share one exact occurrence
ancestry and form one contiguous path. An active Workspace may retain that
independent path. Any requested non-Workspace subject must equal one exact node
of the retained path, not merely share its occurrence. Navigation derives the
Type-inventory Library context from that path and current realized occurrence
facts through [Type-inventory Library
context](#type-inventory-library-context); it is not supplied independently by
the canonical-state owner. When active subject is absent, retained context may
contain only the exact occurrence and coordinate root; a lower retained
Library, Type, or Member path is rejected as ambiguous before recommendation.
Root-only context permits initial recommendation inside that exact occurrence;
no retained context selects Workspace. The lens identity's exact subject must
equal the requested subject. A path/subject mismatch, subject-less lower path,
internally inconsistent context, or subject/lens mismatch fails before Registry
resolution and aborts preparation. Navigation then resolves its subject and
lens halves and publishes one complete prepared snapshot only when both halves
succeed. Any half-failure likewise aborts, and supersession prevents an older
preparation from being published. The focused participant state machine is
[`AtomicRestoration.tla`](models/inspection-subject-navigation/AtomicRestoration.tla).

This owner does not install the prepared snapshot or coordinate other
restoration participants. Complete Workspace restoration composition and
atomic commit belong to [Workspace Definitions](workspace-definitions.md),
whose current version-2 shape was established by #4787. That shape cannot yet
represent an explicitly selected Workspace, distinguish Package from
non-package Root, or carry an optional retained occurrence and descendant
context independently from the active subject. #5525 owns that focused
adoption. Section, body, source-target, and other portable state remain outside
this owner.

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
[Inspect Web Navigation Consumer](inspect-web-navigation-consumer.md), with
the migration historically tracked by
[#4917](https://github.com/richlander/dotnet-inspect/issues/4917).

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
| `NavigationSession.tla` | Latest admitted Navigation-local explicit intent wins; completed unavailable and failed revision behavior follows complete-snapshot change; Navigation preparation failure retains snapshot and revision with a distinct source and fresh retained authority; maintenance is request ordered; abort and acknowledgement preserve liveness; stale authority has no effect; consumer acknowledgement requires synchronization; abandoned lag can obtain the latest snapshot under fresh authority |
| `AtomicRestoration.tla` | One exact requested subject+lens pair is prepared atomically; failed or superseded preparation is not published |
| `SnapshotAuthority.tla` | Retained state comes only from the installed snapshot; applied lens results equal the independently retained request; stale or foreign authority is rejected |

The model README records the TLC commands and scope. Model checking validates
these finite specifications, not the implementation.

Workspace isolation, structural ancestry, lens ranking, Registry-result
classification, and the exact subject-plus-facet identity structure are
intentionally absent from the models: subjects, snapshots, and lenses remain
opaque values there. The pure recommendation, mapping, identity-binding, and
Workspace-containment rules above are enforced by the implementation gates
below rather than claimed as model-checked behavior.

### Required implementation gates

The eventual subject-navigation implementation must include named gates for:

- `WorkspaceSubject_BindsOneExactWorkspaceOccurrence`
- `KindVocabulary_IsClosedAndWorkspaceRooted`
- `Identities_BindExactOwnerIssuedComponents`
- `Construction_RejectsAbsentOwnerIssuedComponents`
- `PortableCoordinateAlone_CannotIdentifyRetainedPackageSubject`
- `WorkspaceSubject_PreservesActiveOccurrenceAndDescendantContext`
- `WorkspaceTypeInventoryContext_DerivesFromDeepestRetainedNode`
- `WorkspaceSubject_ExposesCoordinatesWithoutNavigationAggregation`
- `PackageAndNonPackageRoot_AreMutuallyExclusive`
- `RetainedCoordinateActivation_UsesExactOccurrenceAction`
- `ForeignWorkspaceSubjectActionAndRestoration_AreRejected`
- `SnapshotComposition_RejectsForeignWorkspaceEvidence`
- `SnapshotComposition_RejectsForeignOccurrenceEvidence`
- `RetainedCoordinateDescriptor_FailureHasEvidenceAndNoActivation`
- `RetainedCoordinateDescriptor_PendingHasEvidenceAndNoActivation`
- `RetainedCoordinateDescriptor_CloseActionIsIndependentOfActivation`
- `WorkspaceClose_IssuesCoordinateIntentBeforeOwnerEffect`
- `WorkspaceClose_AcceptsOnlyCorrelatedOwnerResult`
- `MembershipTransition_BlocksLaterExplicitAdmissionUntilCorrelatedResult`
- `MembershipTransition_CorrelatedResultReleasesAdmissionBarrier`
- `OwnerInitiatedMembershipChange_BeginsProtectedIntentBeforeEffect`
- `MembershipResult_NoEffectFailureProvesUnchangedMembership`
- `MembershipResult_EffectAppliedFailureReconcilesCompleteInventory`
- `FailedAdmission_DoesNotInventOccurrence`
- `RetainedMembershipChange_SupersedesStaleOccurrenceWork`
- `RemovedOccurrence_UsesOnlyWorkspaceSuppliedSuccessor`
- `AdmissionResult_ExactOccurrenceBecomesActiveBeforeRecommendation`
- `ZeroOneOrManyOccurrences_DoNotInventActiveOccurrence`
- `RetainedContextReconciliation_ResolvesRootThenPathThenActiveSubject`
- `CoordinateVariation_NeverCrossesWorkspaceBoundary`
- `MemberIdentity_BindsExactDeclaringTypeAndAnchor`
- `InitialRecommendation_PrefersTypeThenLibraryThenCoordinateRoot`
- `TypeRecommendation_UsesPrimaryLibraryAccessibilityAndProducerOrder`
- `InitialRecommendation_NeverChoosesMember`
- `EveryBoundedInventoryRow_PreservesProducerOrderAndIdentity`
- `ProjectedMemberWithoutTypedDeclaringIdentity_FailsClosed`
- `SuccessfulProducerRows_AreTrustworthyDespitePeerFailure`
- `CompleteSuccessfulEmptyInventory_IsUnavailable`
- `NoCandidateWithIndeterminateProducer_IsFailed`
- `ProjectionTruncation_NeverProvesUnavailability`
- `ProducerEvidence_IsRetainedWithoutTranslation`
- `InitialCandidates_ContainOnlyTrustworthyExactRows`
- `InventoryJoin_RequiresExactParticipantRegistration`
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
- `UnavailableDescriptor_HasNoTargetOrActionId`
- `ExplicitUnavailableTransition_DoesNotApplyFallback`
- `UnavailableReplacement_AdvancesStateRevision`
- `UnavailableUnchangedSnapshot_RetainsStateRevision`
- `UnavailableResult_InstalledRevisionMatchesRecordedResultRevision`
- `FailedReplacement_AdvancesStateRevision`
- `FailedUnchangedSnapshot_RetainsStateRevision`
- `FailedResult_InstalledRevisionMatchesRecordedResultRevision`
- `RetainedCoordinateVariation_UsesTypedCorrespondence`
- `LensReconciliation_PreservesExactSubjectScopedIdentity`
- `RetainedSession_UsesInstalledSnapshotAsOnlyPriorState`
- `RetainedSession_BindsOneExactWorkspaceOccurrence`
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
- `CanonicalRestoration_RejectsSubjectFromAnotherOccurrence`
- `CanonicalRestoration_RejectsInconsistentRetainedOccurrenceContext`
- `CanonicalRestoration_RejectsSameOccurrenceSubjectOutsideRetainedPath`
- `CanonicalRestoration_RejectsSubjectlessLowerRetainedPath`
- `CanonicalRestoration_DerivesTypeInventoryContextFromRetainedPathAndFacts`
- `CanonicalRestoration_WorkspaceSubjectPreservesDistinctDescendantContexts`
- `CanonicalRestoration_FailedPreparationSettlesAsAbort`

The closed-kind, component-binding, and construction gates are updated in
place. Initial recommendation and coordinate reconciliation receive the
replacement gate names above. The old four-kind, Package-as-Root, and
same-coordinate expectations are not retained as parallel currencies. Existing
Type, Member, inventory, lens, and typed-correspondence witnesses remain
regression cases inside the new Workspace-rooted gates.

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
| Workspace selected with an active occurrence | Exact Workspace subject and ordered retained-coordinate descriptors; the active occurrence and its Package-or-Root, Library, Type, and Member context remain available |
| Workspace selected without an active occurrence, with zero, one, or many retained entries | Exact Workspace subject with no invented coordinate or lower context |
| Successful coordinate admission | Its exact owner-returned admitted occurrence becomes active before lower-subject recommendation |
| Coordinate admission fails or is rejected with no effect | Workspace remains selected with no invented occurrence and the exact typed evidence |
| Membership effect is applied but the owner also reports failure | Navigation reconciles the complete resulting inventory and uses only an exact owner-supplied active occurrence or successor |
| Package coordinate selected | Exact Workspace-bound Package ancestry; no tab or display identity participates |
| Non-package coordinate selected | Exact Workspace-bound non-package Root ancestry; it is never labelled Package |
| Package subject activated | Exact Package with Package Overview recommendation after #5509 |
| Active coordinate disappears without a supplied successor | Workspace with no active occurrence |
| Active coordinate disappears with an exact Workspace-supplied successor | Reconciliation or independent recommendation only inside that occurrence |
| Pending or failed retained coordinate | Typed owner evidence and no Navigation activation action; Close remains available when the owner marks the occurrence closable |
| Foreign-Workspace subject, action, or restoration payload | Rejected before Registry resolution, correspondence, or fallback |
| Restoration occurrence and subject ancestry disagree inside one Workspace | Preparation aborts before Registry resolution |
| Restoration active Type and retained path name different Types in one occurrence | Preparation aborts before Registry resolution |
| Restoration omits active subject but supplies retained Library/Type/Member context | Preparation aborts before initial recommendation |
| Workspace restoration retains Type in Library L2 | Type-inventory context is derived as L2; no independent Library context is decoded |
| Two Workspace-selected restorations share an occurrence but retain different Type contexts | Distinct prepared snapshots preserve the exact independently supplied descendant context |
| Retained Member disappears while Workspace is active | Retained context falls back to the containing Type while Workspace and its lens remain active |
| Retained Member disappears while Package is active | Retained context falls back to the containing Type while Package and its lens remain active |
| Package O1 with retained Type/Member context resolves exactly to successor Package O2 | Correspondable retained descendants resolve under O2 before invalid descendants are discarded |
| Package content or selection generation is replaced at the same portable coordinate | New exact Package subject; stale subject and actions are rejected |
| Coordinate variation within one Workspace | Typed correspondence or independent recommendation confined to the requested occurrence |
| Coordinate variation across Workspaces | No correspondence; separate retained session and independently restored state |
| Active occurrence closes while a request is in flight | Close first issues a protected coordinate intent; stale work cannot install, later commands are rejected until settlement, and only the correlated owner result supplies inventory and successor |
| Workspace owner removes or replaces an occurrence | The owner begins the protected Navigation intent before the effect and supplies the correlated complete inventory and optional successor |
| Protected membership operation fails without changing membership | Correlated `No effect` result retains state and releases the explicit-admission barrier |
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
| Tools-v2 pointer package | Package with Package Overview; lower subjects unavailable |
| Only non-default-accessibility Type | Type remains the recommendation |
| Partial Type inventory | Deterministic successful candidate plus retained failures |
| Member disappears | Containing Type, never another Member |
| Type disappears with Library retained | Recommended Type in that Library, then Library |
| Coordinate correspondence is ambiguous | Independent new-occurrence recommendation plus diagnostic |
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

- define Workspace or retained-coordinate occurrence identity construction;
- define a universal portable identity for every coordinate or producer;
- make the Workspace subject or `All libraries` combine inspection results
  across retained coordinates or Workspaces;
- create an `All packages` structural subject;
- define the Workspace owner's close or successor-selection policy;
- require every structural level to be visited;
- make arbitrary Library subsets structural subjects;
- select a default Member;
- make UI filters part of subject identity;
- define view-facet registry membership;
- define portable packet fields or browser-history policy;
- define lens contents or section execution;
- authorize acquisition or expensive inspection work; or
- add implicit session state to stateless commands.
