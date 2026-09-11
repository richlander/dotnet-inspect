# Inspection subject navigation

Inspection Subject Navigation is the product owner for choosing and retaining
the structural subject inside one exact inspection Workspace. It supplies a
host-neutral contract for Workspace, Package, Library, Type, and Member
navigation so that browser, CLI, and future hosts do not invent different
defaults or recovery rules.

## Status

This is the target architecture for issue #4794, corrected by #5582 after the
approved split of #5434 and PR #5524 to de-conflate Workspace,
retained-coordinate selection, and Package inspection. Issue #5013 completes
its focused lens-recommendation semantics. The
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
subject ranking over available Library candidates and their retained Type
inventory is implemented by `NavigationInitialSubjectRecommendation`
and gated at its claim below for one already selected coordinate occurrence.
Generation-free classification of bounded Type and Member inventory evidence
is implemented by
`NavigationSubjectInventoryClassification` and gated at its claim below. Pure
standalone exact-lens activation is implemented by
`NavigationLensActivation` and gated by
`StandaloneLensActivation_RejectsDifferentExactSubjectBeforeRegistryResolution`,
`ExplicitLensResolution_MapsEveryRegistryOutcomeWithoutFallback`, and
`ExplicitLensResolution_RetainsExactRegistryEvidence`. Workspace and Package
identity as a Navigation subject, snapshot installation, reconciliation,
revision behavior, retained sessions, synchronization, and restoration remain
unverified until their implementation gates in
[Verification](#verification) land. The workspace-owned identity prerequisite
is implemented by `InspectionWorkspaceIdentity`; the first package descriptor
composition and exact view-scoped activation slice is implemented by
`InspectionWorkspacePackageOccurrenceView` for the Inspect Web and CLI
consumers. This slice does not install or mutate Navigation state. Registry
adoption is tracked by #5509, and portable
Workspace/Package subject projection by #5525.

The concurrency claims are specified separately as executable TLA+ models under
[`models/inspection-subject-navigation/`](models/inspection-subject-navigation/).
Those models check the design state machines; they do not prove that a future
C# or TypeScript implementation conforms to them.

PR #5433 demonstrates the intended browser distinction: Workspace manages
retained coordinates, Package is inspectable, and package tabs are absent.
Those browser identities and transitions remain host-local migration facts,
not authority for this product contract. PR #5501 refines only their responsive
presentation. The browser still seeds a Type cursor, widens accessibility
to admit it, reconstructs coordinate activation from package keys, and
reconciles subject levels locally; #5510 and #5511 track removal of those
migration paths.

## Consumer and complexity record

The end-to-end tracker is #5512. The concrete consumers are:

- the Browser/Wasm Workspace and structural-subject experience demonstrated by
  #5433, with product descriptor adoption tracked by #5510, presentation
  composed by [Inspect Web Navigation
  Presentation](inspect-web-navigation-presentation.md), and result-authority
  adoption by #5511; and
- the agent-inspectable CLI Workspace navigation surface tracked by #5513.

The first host-neutral implementation slice is #5518. Workspace Definitions
adoption for portable Workspace and Package subjects is #5525.

This is shared product substrate; no single-consumer or single-host exception
applies. The simplest sufficient boundary is one Navigation session bound to
one exact Workspace, with one Workspace inventory subject and one Package
subject for each retained package occurrence. It adds no generic Root, global
Package, `All packages` subject, cross-Workspace correspondence, or second
concurrency protocol.

Non-package inputs remain outside this structural grammar. Platform, project,
file, embedded, or another future input does not become a generic Root merely
because it can contain Libraries. A focused design may add a concrete subject
kind when a named consumer demonstrates its own identity, hierarchy, facets,
and behavior. Artifact Acquisition continues to use Root for its broader
physical realization contract. Scope's pre-issuance `WorkspaceRoot*` names are
replaced in place by Package-specific `WorkspacePackage*` names under #6293;
neither vocabulary creates a Navigation subject.
The pre-adoption decision to keep this grammar package-specific is recorded on
[PR #6184](https://github.com/richlander/dotnet-inspect/pull/6184#issuecomment-5574458614).

The atomic descendant-subject plus exact-lens capability added by #6490 follows
that same two-host adoption:

- #6111's stateless Navigation producer evaluates one exact descendant pair,
  and #5513 exposes that result through the agent-oriented CLI surface when a
  CLI request supplies an exact subject and facet. The CLI receives the same
  exact Registry mapping and complete snapshot result, but no action ID,
  retained effect authority, Browser history, or Compare mode.
- #6113's retained Navigation session binds the same request to current intent
  and synchronization authority. #5510 and #5511 adopt the resulting opaque
  interactive action and complete result in Browser/Wasm; Compare uses it in
  stages 4 and 5 of its nine-stage adoption path.

The pure exact-pair evaluator and retained wrapper are one Navigation
capability, not separate host policies. The CLI does not acquire a retained
terminal session, and Browser/Wasm does not reconstruct the exact pair from
display state.

The exact Workspace and retained-occurrence ancestry is necessary for
correctness: without it, distinct logical occurrences inside one Workspace can
alias, and display keys can target the wrong retained occurrence. A
corresponding physical-generation replacement intentionally preserves that
occurrence; its refreshed `ArtifactRootScopeProjection` and
generation-scoped actions distinguish current physical authority and reject
stale work. The existing opaque-snapshot TLA+ state machines remain sufficient
for Navigation-local intent, maintenance, and authority ordering. Workspace
scope-operation results are owned by
[Workspace Scope and Expansion](workspace-scope-and-expansion.md). Their
protected Navigation consumption remains the separate focused contract in
[#5584](https://github.com/richlander/dotnet-inspect/issues/5584).
Structural containment remains implementation-gated rather than model-checked.

Navigation returns typed descriptors, identities, evidence, and outcomes. The
CLI consumer lowers those types through Markout. Browser/Wasm uses the
host-specific interactive rendering owned by
[Inspect Web Navigation Presentation](inspect-web-navigation-presentation.md)
because focus, accessibility, and responsive SlideStrip behavior are browser
concerns; it does not reconstruct product semantics from rendered text.

## Design demo

The production Browser/Wasm scenario in #5433 is behavioral evidence:
Workspace and Package are distinct navigation identities, the Workspace surface
lists retained coordinates without package tabs, and Package has its own
icon-backed inspection surface. Navigation Presentation composes the
product-issued Workspace subject in the application-scope strip and Package in
the structural-subject strip. #5501 preserves those subject identities while
changing only responsive strip allocation.

The contract behind the Workspace-selected case is:

```text
Application: [Query] [Workspace*]
Subjects:    [Package] [Library] [Type] [Member]
Inspectors:  [Overview]

Workspace
  System.Text.Json 10.0.0 / net10.0    current
  Newtonsoft.Json 13.0.4 / net8.0
```

Selecting Workspace changes the active subject but retains the exact
`System.Text.Json` occurrence and descendant context. Activating
`Newtonsoft.Json` submits its opaque occurrence action and receives a new
Workspace-bound snapshot; no label or tab key identifies it. If the current
occurrence is replaced by an exact owner-supplied occurrence, retained
descendants reconcile only through typed correspondence.

## Problem

Workspace lifetime, retained-package selection, and structural subjects are
different concepts. One Workspace owns an isolated set of retained package
occurrences. Package identifies what is inspected at one such occurrence;
Library, Type, and Member narrow within it.

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
- Workspace, Package, Library, Type, Member, and lens navigation
  descriptors;
- exact subject and lens activation outcomes;
- same-occurrence and coordinate-variation reconciliation within one exact
  Workspace;
- retained navigation-session authority; and
- subject-and-lens initialization in a fresh restored Workspace.

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
- zero or one active retained-package occurrence and its realized Package
  facts;
- zero or one scope-result requested active/replacement occurrence plus typed
  effect and correspondence outcomes;
- owner-issued retained-coordinate activation operations;
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
- ordered retained-package and Package descriptors;
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
composition](artifact-acquisition-and-workspaces.md) owns
Workspace identity, including the #5508 construction and close contract,
isolation,
admitted artifacts and contexts, query authorization, and lifetime. [Workspace
Scope and
Expansion](workspace-scope-and-expansion.md) owns retained-coordinate
membership and order, Workspace-bound occurrence construction, retention, and
retirement, selective dependency expansion, revisions, and scope-operation
results. Navigation consumes the Workspace identity, complete ordered occurrence
descriptors, the scope result's requested active/replacement occurrence, and
typed correspondence without defining identity construction, equality, scope
policy, replacement policy, or closure. Scope-operation production is the
focused successor to #5583; protected Navigation consumption remains #5584.

Artifact acquisition and package realization own package coordinate,
`PackageRootBinding`, content-generation, selection, and acquired-descendant
identity currencies. The current #5656 substrate composes a Workspace-local
occurrence with `PackageRootBinding`; Workspace Scope and Expansion replaces
that resource-bearing association with a complete
`WorkspacePackageOccurrence` containing its own identity, typed
`WorkspacePackageDescriptor`, and the adjacent
owner's `ArtifactRootCorrespondence`, plus a point-in-time
`ArtifactRootScopeProjection` in the occurrence descriptor. Navigation
consumes those owner-issued exact values. A portable package coordinate alone
cannot identify one retained occurrence.

The `ArtifactRoot*` names in the adjacent Artifact owner describe one physical
realization and publication unit and remain correct. Issue #6293 replaces
Scope's pre-issuance `WorkspaceRoot*` names in place; its implementation
exposes only Package occurrences.
Navigation consumes that Package-specific contract and never exposes a Root
subject.

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
- structural navigation for platform, project, file, embedded, or other
  non-package inputs;
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
| Library | All admitted libraries for one Package when aggregate inspection is supported, or one exact Library |
| Type | One exact type definition in one admitted Library |
| Member | One exact API member in one Type |

Its shape is:

```text
Workspace -> Package -> Library -> Type -> Member
```

Workspace is the container and inventory for retained package occurrences.
Package is one exact occurrence, not an aggregate over all packages. `All
libraries` is the only structural aggregate below Workspace. The hierarchy is
a grammar, not a required navigation path; a Package, Library, Type, or Member
may be activated directly when its complete ancestry is supplied.

Workspace is always applicable while its owner-issued lifetime remains open.
Each Package occurrence admitted to this grammar is owner-issued. Lower levels
remain applicable when that occurrence supports them even if their inventories
are validly empty. Structurally unsupported levels are omitted; applicable but
empty levels remain visible as unavailable. Package classification follows the
owner-issued typed descriptor, never coordinate text, an icon, a
package-shaped display label, or host flags.

Workspace inventory preserves every owner-issued Package occurrence. Current
Browser platform rows remain host-local behavior outside shared Scope and
Navigation; this design neither suppresses nor relabels them. A later concrete
subject such as Platform extends Scope, this grammar, and its inventory
behavior together through its own named consumer.

### Identity

The conceptual subject identity family is:

| Kind | Identity components |
| --- | --- |
| Workspace | Artifact-owner `InspectionWorkspaceIdentity` established by #5508 |
| Package | Complete scope-owner `WorkspacePackageOccurrence` and its separate `WorkspacePackageDescriptor` |
| All Libraries | Exact Package plus explicit aggregate Library identity |
| One Library | Exact Package plus acquired Library identity |
| Type | Exact Library binding plus exact metadata definition |
| Member | Type identity plus product-owned member anchor |

Identity equality never uses display text, filename, list position, metadata
token alone, portable package coordinate alone, browser cache key, or backend
arrival order. Workspace and retained-coordinate
occurrence identities are process-local and never serialized. Artifact
Acquisition issues the Workspace identity under #5508; Workspace Scope and
Expansion constructs and retires the occurrence identity under that live
Workspace authority.

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
| Active package occurrence | Names the exact Package ancestry whenever one occurrence is active, including while Workspace is the active subject |
| Active subject | The one committed Workspace, Package, Library, Type, or Member |
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
Workspace, Package, Library, Type, and Member descriptors. Action IDs
are scoped to one exact Workspace and generation and are distinct from
structured identities.

Stale, foreign-Workspace, unknown, or duplicated action IDs produce typed
rejection without state change. Canonical product peers may submit structured
identities through typed seams; browser display text never becomes a command
currency.

Retained-coordinate descriptors separately carry an owner-issued exact
`WorkspacePackageOccurrence`, including its typed Package descriptor, and owner order
from
[Workspace Scope and Expansion](workspace-scope-and-expansion.md), current
realization status from
[Artifact acquisition and workspace
composition](artifact-acquisition-and-workspaces.md), and an optional
Navigation-issued activation action. Navigation resolves the action to the
exact occurrence; the host never submits a package key or display label. An
artifact-owner loading status maps to `Pending` activation, retains the
owner's typed status and evidence, and carries no activation action. A failed
status maps to `Failed` activation and likewise carries no activation action.
Current `Ready` occurrences omit activation.

Activation status is independent of exact occurrence presence in the complete
owner-issued inventory. When the current retained occurrence is being
re-realized without a membership or identity change, `Pending` or `Failed`
keeps its exact logical occurrence, installed Package subject, descendant
subject context, and typed owner evidence but carries no current artifact
realization reference or activation action. Neither status runs
occurrence-first correspondence, fallback, or truncation, and neither pretends
that a retired artifact generation remains consumable. Only absence of that
exact occurrence from the complete inventory enters the
replacement-or-Workspace branch; #5584 owns protected result consumption for
the non-`Ready` occurrence.

Admission and Close remain Artifact Acquisition concerns. Root removal,
replacement, invalidation, and effect disposition are Workspace Scope and
Expansion results. Navigation applies the reconciliation rule below and #5584
owns protected result consumption. This design consumes only an installed
complete inventory and any exact requested occurrence supplied through those
contracts; it does not acquire a separate successor-selection policy.

## Product policy

### Initial subject

When no subject is committed and one exact retained-coordinate occurrence is
already active, recommendation order is:

1. Library, using the recommendation below.
2. The occurrence's exact Package.

Type and Member are never implicit subjects. A retained Type cursor does not
make Type the active subject.

When no retained-coordinate occurrence is active, Workspace is selected,
whether the inventory contains zero, one, or several entries. Navigation never
chooses an inventory entry from cardinality or order. Restoration or an
activation action supplies the exact active occurrence; only that explicit
input establishes it. When the operation supplies no exact subject request,
Navigation applies the recommendation above only inside that occurrence. The
CLI consumer in #5513 and canonical restoration must supply an exact occurrence
before expecting a lower initial subject.

Library choice is independent of Type count, accessibility, UI filters, search
text, display labels, and arrival order. Type inventory retains its producer
order for explicit navigation; it does not choose the initial subject.

Initial recommendation does not rank Types. When retained-context derivation or
level-local Type fallback below requires the highest-ranked trustworthy Type,
it uses these tiers:

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

### Initial Library and Package

Library recommendation selects:

1. The available primary Library.
2. The first available one-Library descriptor in declaration order.
3. `All libraries`, only when no one-Library descriptor is available.

Unavailable or failed aggregate evidence remains visible when a one-Library
subject is selected.

When no Library is available, the exact Package is selected. This allows
Package-only occurrences, including the tools-v2 pointer-package case
implemented by #4829.

For package entry, "best Library" means this product-owned preference, not the
largest public surface or the first displayed row. Package compile selection
supplies the primary asset: a file name matching the package ID, ignoring case,
then the selector's case-insensitive asset-path order with an ordinal
tie-breaker. Surface projection retains that exact default when available and
otherwise supplies its available fallback. Consumers use the returned identity,
not a second ranking implementation.

The browser adoption in #6098 uses the existing `defaultAssemblyId` projection
for fresh package entry, including opening retained packages through Search.
Explicit Package, Library, Type, Member and inspector destinations, and
restored workspace/history state, take precedence. Package remains explicitly
reachable and retains its full Library inventory. The existing Library Overview
is the browser's entry inspector; this slice does not adopt the separate
Registry-backed lens-recommendation protocol below.
Root-only `NoCompileAssets` and `EmptyCompileGroup` outcomes open Package with
their explanation visible. Failed selection is not treated as an empty package.
Browser entry and restoration are gated by
`inspect-web/browser/library-hierarchy.spec.ts`; root-only and failed
selection modeling is gated by `test/package-acquisition.test.ts` in that host.
This is a default-entry adoption, not completion of #5510/#5511's broader
snapshot and result-authority migration.

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
| No exact Type plus participant rejection, participant failure, inspection failure, missing exact Type or unresolved projected-Member declaration identity, or projection omission | `Failed` with the original typed evidence |
| Exact Types plus any of those failures | `Available` and partial; retain the exact rows and original typed evidence |

Projection truncation never proves that an omitted Library is empty. A returned
Type without exact `MetadataTypeDefinitionName` is retained as identity-failure
evidence and is not reconstructed from display text, metadata token, or list
position. A Member projected onto another Type resolves its
`DeclaringTypeDefinitionName` against exact Type rows in the same Library. One
unique match retains the producer row beneath its containing Type while its
`MemberSubject` binds the declaration Type and the declaration-scoped
`MemberAnchor`, exact-joining the declaration Member by its producer-issued
metadata token. Missing identity, no returned exact declaration, or multiple
matching Type or Member rows retains the complete producer row as typed failure
evidence and emits no Member subject. Classification never parses display or
canonical declaring text as lookup identity. Returned exact rows remain
trustworthy when another row or participant fails; failure does not erase
positive evidence.

Every admitted Library remains an available Library candidate for initial
subject recommendation. Only exact returned Type rows become Type candidates.
Classification does not commit the recommendation, choose an active subject,
compose `Current` or `Selection required`, mint generation-scoped actions, or
produce a navigation snapshot.

This classification is gated by
`NavigationSubjectInventoryTests.EveryBoundedInventoryRow_PreservesProducerOrderAndIdentity`,
`ProjectedMemberFromRealProducer_BindsDeclarationTypeAndAnchor`,
`ProjectedMemberWithoutTypedDeclaringIdentity_FailsClosed`,
`ProjectedMemberWithUnreturnedDeclaringType_FailsClosed`,
`ProjectedMemberWithAmbiguousDeclaringType_FailsClosed`,
`ProjectedMemberWithoutDeclarationMemberIdentity_FailsClosed`,
`ProjectedMemberWithUnreturnedDeclarationMember_FailsClosed`,
`ProjectedMemberWithAmbiguousDeclarationMember_FailsClosed`,
`ProjectedMemberLookup_DoesNotUseDeclaringText`,
`SuccessfulProducerRows_AreTrustworthyDespitePeerFailure`,
`CompleteSuccessfulEmptyInventory_IsUnavailable`,
`NoCandidateWithIndeterminateProducer_IsFailed`,
`ProjectionTruncation_NeverProvesUnavailability`,
`ProducerEvidence_IsRetainedWithoutTranslation`,
`InitialCandidates_ContainOnlyTrustworthyExactRows`, and
`InventoryJoin_RequiresExactParticipantRegistration` for the implemented
coordinate-rooted subset. Workspace-occurrence binding remains unverified.

The pure ranking over available Library candidates is gated by
`NavigationInitialSubjectRecommendationTests.InitialRecommendation_PrefersOneLibraryThenAggregateThenRoot`,
`LibraryRecommendation_UsesPrimaryThenProducerOrderRegardlessOfTypes`, and
`InitialRecommendation_NeverChoosesTypeOrMember`. Candidate coordinate, Library,
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
| Workspace or Package | Defining Library of the deepest retained Type or Member; otherwise the deepest retained Library; otherwise available aggregate, then the highest-ranked trustworthy Type's Library, then primary or first available Library; none for Workspace without retained occurrence context |

If no context can be established, the context is unavailable or failed. The
context does not activate Library or promote Package or Workspace.
Ancestor context is derived from the retained path and realized occurrence
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

`Rejected` is an admitted Navigation result with ordinary result authority. A
future protected-membership refusal belongs to #5584 rather than this ordinary
result algebra.

Standalone lens activation first requires the request's exact subject to equal
the snapshot's active subject. A mismatch is `Rejected` with the complete
request identity retained, before Registry resolution or fallback. It cannot
change the active subject. Canonical restoration's separately validated atomic
subject+lens pair remains governed by the fresh Workspace initialization
contract.

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
recommended subject. If the already committed subject became invalid
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

#### Atomic descendant subject and lens activation

Issue [#6490](https://github.com/richlander/dotnet-inspect/issues/6490)
adds one product-owned request for a current subject that must activate an
exact descendant with an exact destination lens. Its first retained consumer
is Library-to-Type and Type-to-Member drill-down in
[Inspect Web Compare Experience](inspect-web-compare-experience.md), under the
end-to-end tracker
[#5083](https://github.com/richlander/dotnet-inspect/issues/5083). The
stateless CLI consumer is tracked by #5513 under #5512.

The host-neutral request binds:

```text
DescendantSubjectLensRequest
  Source       exact current structural subject
  Destination  NavigationLensIdentity
```

The destination lens already binds its exact descendant subject and exact
Registry facet. A retained interactive session issues an opaque
generation-scoped action ID bound to the complete request for an available
owner-issued descendant row. A canonical stateless product peer may submit the
structured pair through the typed evaluation seam. Browser display state never
becomes request identity.

The destination must be in the same Workspace and retained Package occurrence
as its source and must be an eligible descendant admitted by that exact row.
The first retained consumer uses Library-to-Type and Type-to-Member edges;
callers do not construct or broaden the relationship from metadata, display
text, or hierarchy position.

For one-Library sources, an eligible Type retains that exact Library as its
defining Library. For `All libraries`, each eligible Type row names one exact
constituent Library from the aggregate's complete admitted Library set; the
Type keeps that concrete defining-Library identity rather than acquiring an
aggregate parent. Applying the action installs the destination Type's defining
Library as hierarchy and Type-inventory context. A Type-to-Member action
requires the Member's exact declaring Type to equal the source Type.

Submitting the opaque action begins one explicit Navigation intent. Navigation
validates the action's session, generation, source subject, destination
ancestry, and exact subject-bound lens before Registry resolution. A stale,
foreign-Workspace, foreign-occurrence, duplicated, source-mismatched, or
non-descendant action is `Rejected` without Registry evaluation,
recommendation, correspondence, or fallback.

Stateless evaluation validates the same exact source, destination, Workspace,
occurrence, and descendant relationship without issuing retained action or
effect authority. It returns the same semantic mapping and complete evaluated
snapshot as data, while retained execution alone may install that snapshot.

After validation, Navigation resolves the destination facet against the exact
destination subject. It never activates the subject first and never runs lens
recommendation for that destination:

| Destination Registry or preparation result | Navigation result and state |
| --- | --- |
| `Available`, with successful Navigation preparation | `Applied`; install one complete replacement snapshot whose active subject and effective lens equal the exact destination pair |
| `Unavailable` | `Unavailable`; retain the installed pair and return the exact request and Registry evidence |
| `Failed` | `Failed`; retain the installed pair and return the exact request and Registry diagnostic |
| `Inapplicable` or `Unknown` | `Rejected`; retain the installed pair and return the exact Registry evidence |
| Navigation preparation failure | `Failed`; retain the installed pair and identify Navigation as the failure source |
| Superseded by a newer explicit intent | `Superseded`; publish no visible effect |

Here, "retain the installed pair" means that this action does not install
either requested half. The ordinary retained-session result may still carry a
newer complete snapshot with `Synchronization required` when product state was
committed by another operation; the consumer installs that current snapshot
without presenting this descendant request as applied.

An applied action advances the state revision once and produces one canonical
subject-and-lens transition. The Navigation Consumer applies its existing
explicit-action history rule to that one result, so the Browser pushes one
entry and never records an intermediate recommended lens. The action carries
no host presentation mode, query input, or renderer state.

This action is general Navigation capability, not a Compare-specific command.
Any future consumer must supply one owner-issued descendant row and exact
destination lens under the same rules. Ordinary subject activation without an
exact lens remains recommendation-driven, and standalone lens activation
continues to require the requested subject to be current.

Selecting Workspace changes only the committed active subject. It preserves
the active retained-coordinate occurrence and its descendant context when one
exists, allowing the Workspace surface to identify that current entry and the
subject strip to retain Package, Library, Type, and Member context.
It never changes the occurrence implicitly.

Activating an exact retained occurrence is a coordinate request, not
display-label or tab selection. It restores an explicitly supplied exact
subject when valid; otherwise it runs initial recommendation only within that
occurrence.

Selecting Package directly keeps the same exact occurrence and installs that
Package subject. Selecting a Library does not also select a Type. Selecting a
Type or Member directly returns its complete Workspace, Package, and
structural ancestor context.

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
| Package | Retain while the same exact occurrence remains present in the complete owner-issued inventory; otherwise reconcile within an explicitly supplied exact replacement occurrence, or select Workspace |
| All Libraries | Retain when aggregate remains available; otherwise the exact Package |
| One Library | Retain when available; otherwise aggregate, then the exact Package |
| Type | Retain when available; otherwise highest-ranked trustworthy Type in its defining Library, then that Library, aggregate, then the exact Package |
| Member | Retain when available; otherwise containing Type; if that Type is unavailable, apply the Type rule |

Navigation reconciles one retained context with one occurrence-first
algorithm:

1. **Establish the retained Package.** If the current exact occurrence remains
   present in the complete owner-issued inventory, keep its Package independent
   of `Pending` or `Failed` activation status. Otherwise, if the evaluation
   input supplies an exact replacement occurrence, establish that occurrence's
   Package. If neither applies, clear retained context and select Workspace.
   Navigation never infers a replacement from inventory order.
2. **Resolve the retained path.** Starting at the established Package, resolve each
   retained Library, Type, and Member in ancestry order. Same-occurrence refresh
   uses exact availability; replacement movement uses typed correspondence. Each
   resolved node must be an exact descendant of the preceding result.
3. **Apply one fallback.** At the first unresolved path node, apply the table's
   fallback for that level inside the established Package and truncate every lower
   node. Missing, ambiguous, refused, or failed correspondence follows the same
   rule with its diagnostic. No fallback crosses the established Package or
   Workspace.
4. **Derive the active subject.** Workspace remains active independently. A
   non-Workspace active subject uses its resolved path node when present;
   otherwise it becomes the single fallback result. Retained nodes below an
   unchanged or exactly resolved active ancestor remain context without becoming
   active.
5. **Complete the snapshot.** Rebuild contiguous hierarchy descriptors, derive
   Type-inventory Library context from the resulting path and current realized
   facts, then reconcile the active subject's lens basis.

For example, `Package -> Library -> Type -> Member` with Package active retains
a correspondable complete path across an exact replacement occurrence. A missing
Member truncates the path to Type while Package remains active. The identical
path with Workspace active produces the same retained result while Workspace
remains active. The active subject no longer controls whether the path receives
same-occurrence or replacement reconciliation.

No arbitrary Member replaces a missing Member. Inventory refresh never promotes
an explicitly selected Workspace, Package, or Library to Type. Navigation
never chooses a sibling occurrence when the current occurrence is absent. It
consumes only an exact replacement occurrence supplied by the evaluation input.
Otherwise Workspace remains active with no active occurrence. This removes the
browser's package-key-based replacement choice tracked by #5510 and #5511.

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

Step 2 of the occurrence-first algorithm uses typed owner-issued correspondence
when the retained Package moves between exact occurrences inside one Workspace:

| Resolution | Result |
| --- | --- |
| Exact subject resolves and is available | Resolved subject |
| Member missing, Type resolves | Resolved Type |
| Type missing, defining Library resolves | Highest-ranked trustworthy Type in that Library, then the Library |
| Library missing | Available aggregate, then the new occurrence's exact Package |
| Correspondence missing, ambiguous, refused, or failed | Apply the unresolved node's level fallback inside the already resolved ancestor, truncate lower nodes, and retain the diagnostic |

Display text, package ID alone, portable coordinate equality, assembly name,
token, and ordinal are not correspondence.

For an unchanged occurrence, failure to evaluate reconciliation retains the
installed snapshot and surfaces failure. For a newly activated occurrence with
no prior retained path, Navigation runs independent initial recommendation;
correspondence is not invented. Failed lower levels remain failed.

Correspondence never crosses a Workspace boundary. A different exact Workspace
uses a different retained navigation session and independently selected or
restored state.

Membership-changing effects are outside this structural claim and are owned by
Workspace Scope and Expansion. #5584 owns their stale-work sequencing and
protected Navigation consumption. This design accepts only the exact installed
inventory and active-occurrence inputs. Non-invalidating realization-status
refresh remains ordinary maintenance.

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

`NavigationSession.tla` does not model external Workspace membership effects.
Its opaque `coordinate` intent covers Navigation-local coordinate activation
and variation under ordinary latest-admitted-intent supersession. Workspace
scope-operation results are owned by Workspace Scope and Expansion; their
protected Navigation consumption is #5584.

## Fresh Workspace navigation initialization

After Definitions constructs a fresh unpublished Workspace and publishes its
complete explicit Package membership, it supplies Navigation with that exact
Workspace, zero or one exact retained occurrence context, and the optional
exact active subject and lens requested inside it. Every supplied identity was
issued within the new Workspace, and Navigation validates only their internal
consistency there.

The retained occurrence context is independent from the active subject. It
contains:

- the exact retained occurrence and its Package;
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
contain only the exact occurrence and Package; a lower retained Library, Type,
or Member path is rejected as ambiguous before recommendation. Package-only
context permits initial recommendation inside that exact occurrence; no
retained context selects Workspace. The lens identity's exact subject must
equal the requested subject. A path/subject mismatch, subject-less lower path,
internally inconsistent context, or subject/lens mismatch fails before Registry
resolution and aborts initialization. Navigation then resolves its subject and
lens halves and publishes one complete snapshot inside the new
Workspace only when both halves succeed. Any half-failure closes the
new Workspace through the Definitions coordinator, and supersession prevents
an older attempt's Workspace from becoming active. The focused local state
machine is
[`AtomicRestoration.tla`](models/inspection-subject-navigation/AtomicRestoration.tla).

This owner does not install the new Workspace or coordinate its
lifetime. Complete Workspace construction and result classification belong to
[Workspace Definitions](workspace-definitions.md); the retained host owns the
current-authority collection publication and active-identity selection.
Navigation owns only the new Workspace's
internally complete current snapshot.

Selecting a loaded coordinate, Library, Type, or Member in Spotlight uses
ordinary Navigation inside the active Workspace and never enters this
construction path. Selecting an external package creates a fresh one-package
Workspace; the full Workspace editor may create a Workspace with multiple
explicit package Roots. In either case, subject focus remains independent from
membership, and traversal-derived libraries do not become explicit Roots.

The current version-2 shape cannot yet represent an explicitly selected
Workspace or carry an optional retained occurrence and descendant context
independently from the active subject. #5525 owns that focused adoption.
Section, body, source-target, and other portable state remain outside this
owner.

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
| `AtomicRestoration.tla` | One exact requested subject+lens pair initializes atomically; failed or superseded initialization is not published |
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
- `AncestorTypeInventoryContext_DerivesFromDeepestRetainedNode`
- `WorkspaceSubject_ExposesCoordinatesWithoutNavigationAggregation`
- `PackageSubject_RequiresPackageOccurrence`
- `RetainedCoordinateActivation_UsesExactOccurrenceAction`
- `ForeignWorkspaceSubjectActionAndRestoration_AreRejected`
- `SnapshotComposition_RejectsForeignWorkspaceEvidence`
- `SnapshotComposition_RejectsForeignOccurrenceEvidence`
- `RetainedCoordinateDescriptor_FailureHasEvidenceAndNoActivation`
- `RetainedCoordinateDescriptor_PendingHasEvidenceAndNoActivation`
- `RetainedCoordinatePending_PreservesInstalledContextUntilSettled`
- `RetainedCoordinateFailure_PreservesInstalledContextWithEvidence`
- `RetainedCoordinateCorrespondingGenerationRefresh_PreservesPackageSubject`
- `ZeroOneOrManyOccurrences_DoNotInventActiveOccurrence`
- `RetainedContextReconciliation_ResolvesOccurrenceThenPathThenActiveSubject`
- `CoordinateVariation_NeverCrossesWorkspaceBoundary`
- `MemberIdentity_BindsExactDeclaringTypeAndAnchor`
- `InitialRecommendation_PrefersLibraryThenPackage`
- `TypeRecommendation_UsesPrimaryLibraryAccessibilityAndProducerOrder`
- `InitialRecommendation_NeverChoosesTypeOrMember`
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
- `DescendantLensAction_BindsExactSourceDestinationAndFacet`
- `DescendantLensAction_RejectsStaleForeignAndNonDescendantBeforeRegistryResolution`
- `DescendantLensResolution_MapsEveryRegistryOutcomeWithoutRecommendation`
- `StatelessAndRetainedDescendantLens_UseSameExactMapping`
- `AppliedDescendantLens_InstallsExactPairInOneSnapshot`
- `NonAppliedDescendantLens_InstallsNeitherRequestedHalf`
- `SupersededDescendantLens_PublishesNoEffect`
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

The descendant-action binding gate independently retains the source subject,
destination subject, facet, Workspace, occurrence, defining Library, and
issuing generation. Its rejection gate varies each currency and the eligible
descendant relation before a throwing Registry sentinel. It includes two
identically named Types in different Libraries under `All libraries` and
requires the selected row's exact defining Library to become hierarchy and
Type-inventory context. The exact-pair gate compares the installed subject and
effective lens with that independent request after one applied result. The
non-applied gate covers unavailable, failed, inapplicable, unknown, and
Navigation-preparation failure and requires that neither requested half enters
the installed snapshot. Existing retained-session authority and consumer
synchronization gates cover supersession, complete-snapshot installation, and
acknowledgement; this action introduces no second operation or partial
publication protocol. The exact pair and descendant relationship remain
**unverified** until these named Release gates land.

## Acceptance cases

| Case | Expected result |
| --- | --- |
| Workspace selected with an active occurrence | Exact Workspace subject and ordered retained-coordinate descriptors; the active occurrence and its Package, Library, Type, and Member context remain available |
| Workspace selected without an active occurrence, with zero, one, or many retained entries | Exact Workspace subject with no invented coordinate or lower context |
| Package coordinate selected | Exact Workspace-bound Package ancestry; no tab or display identity participates |
| Package subject activated | Exact Package with Package Overview recommendation after #5509 |
| Active coordinate is absent without a supplied replacement | Workspace with no active occurrence |
| Active coordinate is absent with an exact supplied replacement | Occurrence-first correspondence and level-local fallback only inside that occurrence |
| Current retained coordinate is Pending during non-invalidating re-realization | Exact logical occurrence, installed Package subject, descendant subject context, and typed owner evidence remain without fallback or truncation; no current artifact realization reference or Navigation activation action is exposed |
| Current retained coordinate is Failed while its exact occurrence remains present | Exact logical occurrence, installed Package subject, descendant subject context, and typed owner evidence remain without fallback or truncation; no current artifact realization reference or Navigation activation action is fabricated |
| Foreign-Workspace subject, action, or restoration payload | Rejected before Registry resolution, correspondence, or fallback |
| Restoration occurrence and subject ancestry disagree inside one Workspace | Preparation aborts before Registry resolution |
| Restoration active Type and retained path name different Types in one occurrence | Preparation aborts before Registry resolution |
| Restoration omits active subject but supplies retained Library/Type/Member context | Preparation aborts before initial recommendation |
| Workspace restoration retains Type in Library L2 | Type-inventory context is derived as L2; no independent Library context is decoded |
| Package remains active with retained Type in Library L2 | Type-inventory context is derived as L2 before any Package-only fallback |
| Two Workspace-selected restorations retain different Type contexts | Distinct initialized snapshots preserve the exact independently supplied descendant context |
| Retained Member disappears while Workspace is active | Retained context falls back to the containing Type while Workspace and its lens remain active |
| Retained Member disappears while Package is active | Retained context falls back to the containing Type while Package and its lens remain active |
| Package O1 with retained Type/Member context resolves exactly to replacement Package O2 | Correspondable retained descendants resolve under O2 before invalid descendants are discarded |
| Package content or binding-context generation is replaced with equal logical correspondence | Same exact Package subject and occurrence; projection and actions refresh, retained descendants reconcile, and stale generation-scoped actions are rejected |
| Package coordinate or selection target changes so logical correspondence differs | Membership-changing replacement supplies a new occurrence and Package subject; correspondence and level-local fallback govern retained descendants |
| Coordinate variation within one Workspace | Typed correspondence or independent recommendation confined to the requested occurrence |
| Coordinate variation across Workspaces | No correspondence; separate retained session and independently restored state |
| Ordinary package | Best available one-Library subject with Library Overview; aggregate only when no one-Library subject is available, then Package |
| Preferred role is not first | Preferred available role, not the earlier available descriptor |
| Preferred lens unavailable | First available registry-ordered fallback with preferred evidence retained |
| No lens available and one evaluation failed | Failed lens outcome with all non-success evidence retained |
| Preferred role missing or options empty | Typed Navigation policy failure, never implicit first-option selection |
| Direct Member activation without a lens | Exact Member with Member Overview recommendation |
| Same facet on two exact Types | Two distinct subject-bound navigation lens identities |
| Lens request bound to another exact Type | Rejected before Registry resolution with active subject unchanged |
| Explicit inapplicable or unknown lens | Rejected with exact Registry evidence and no fallback |
| Library Type row with exact Type Compare lens | One applied snapshot contains that Type and `type.compare`; no Library-to-Type intermediate recommendation |
| `All libraries` has identically named Types in L1 and L2, and the L2 row is activated | Exact L2-bound Type, L2 hierarchy, L2 Type-inventory context, and `type.compare`; aggregate identity does not become Type ancestry |
| Type Member row with exact Member Compare lens | One applied snapshot contains that Member and `member.compare`; no Type-to-Member intermediate recommendation |
| Descendant lens unavailable, failed, inapplicable, unknown, or preparation-failed | Prior installed subject and lens remain; the exact destination evidence is returned and neither requested half is installed |
| Stale, foreign-occurrence, or non-descendant subject+lens action | Rejected before Registry resolution or recommendation |
| Descendant subject+lens action superseded by a newer intent | No visible effect from the superseded action |
| Stateless CLI evaluates the same exact descendant pair | Same exact Registry mapping and complete snapshot data as retained evaluation; no action ID, effect authority, history, or Compare mode |
| Failed recommendation becomes available on refresh | Recommendation reruns and installs the newly effective exact lens |
| Recommended fallback then preferred role becomes available | Recommendation replaces the fallback with the preferred exact lens |
| Explicit unavailable lens becomes available on refresh | Exact identity is re-resolved without considering a sibling fallback |
| Exact non-success while its subject disappears | Result retains the exact request evidence; installed snapshot uses the replacement subject's recommendation basis |
| Navigation preparation fails after Registry availability | Failed result identifies Navigation; snapshot and revision remain unchanged |
| Multi-library package | Primary one-Library subject, then first declaration-order one-Library subject; aggregate only when no one-Library subject is available |
| Libraries with no Types | Library with References; Type is validly unavailable |
| Tools-v2 pointer package | Package with Package Overview; lower subjects unavailable |
| Primary Library has no default-accessibility Type | Library remains the recommendation |
| Partial Type inventory | Deterministic successful candidate plus retained failures |
| Member disappears | Containing Type, never another Member |
| Type disappears with Library retained | Recommended Type in that Library, then Library |
| Coordinate correspondence is ambiguous | Level-local fallback inside the resolved ancestor, lower-path truncation, and retained diagnostic |
| Two lens requests complete out of order | Latest issued lens is final |
| Refresh and reconciliation complete out of order | Maintenance request order determines final snapshot |
| Coordinate acquisition fails | Prior snapshot retained; abort effect visible; maintenance eventually resumes |
| Canonical subject plus non-default lens | One complete initialized snapshot returns the exact requested pair with no partial result |
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
