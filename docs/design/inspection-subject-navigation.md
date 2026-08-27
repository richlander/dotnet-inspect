# Inspection subject navigation

Inspection Subject Navigation is the product owner for choosing and retaining
the structural subject of one realized inspection coordinate. It supplies a
host-neutral contract for Root, Library, Type, and Member navigation so that
browser, CLI, and future hosts do not invent different defaults or recovery
rules.

## Status

This is the target architecture for issue #4794. Product implementation remains
unverified until the implementation gates in
[Verification](#verification) land.

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
- initial subject and lens recommendation;
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
- product view-facet registry descriptors and availability facts;
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
- typed transition or reconciliation outcomes; and
- opaque retained-session authority.

### Adjacent owners

[Artifact acquisition and workspace
composition](artifact-acquisition-and-workspaces.md) owns coordinates, admitted
artifacts, and workspace lifetime. Root-capable package realization with no
compile Library is tracked by
[#4829](https://github.com/richlander/dotnet-inspect/issues/4829).

[Type, member, and API representation](type-member-api-representation.md) owns
the Type and Member identity currencies used here.

[Workspace definitions](workspace-definitions.md) owns portable view-facet
registry binding. The focused View Facet Registry work tracked by
[#4880](https://github.com/richlander/dotnet-inspect/issues/4880) owns runtime
lens membership, labels, and order.

[Inspect Web UI](inspect-web-ui.md) owns rendering, accessibility, focus, and
interaction. Issue #4787 owns portable projection and complete restoration
composition.

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
token alone, or backend arrival order.

A navigation lens identity combines a structural subject kind with one
view-facet registry identity. The registry owns the stable facet identity;
Inspection Subject Navigation owns the subject binding. Library Metadata and
Type Metadata can therefore share a label without sharing identity.

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
| Lens outcome | Effective, unavailable, or failed |
| Diagnostics | Partial evidence and scoped failures |

The snapshot is the retained session's only committed subject and lens state.
A host cannot supply a second retained-state value.

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

The initial semantic recommendations are:

| Subject | Preferred lens role |
| --- | --- |
| Type | API |
| Library | References |
| Package Root | Package Overview |
| Other Root | Root owner's recommendation |

The exact identities and membership come from the View Facet Registry. If the
preferred descriptor is unavailable or failed, navigation selects the first
available descriptor in registry order and retains the non-success evidence.
When none is available, the subject remains active and the lens outcome is
unavailable or failed according to the underlying results.

### Type-inventory Library context

Type navigation has an explicit Library context:

| Active subject | Type-inventory context |
| --- | --- |
| Library | The active Library |
| Type or Member | The defining Library |
| Root | Available aggregate, then the highest-ranked trustworthy Type's Library, then primary or first available Library |

If no context can be established, the context is unavailable or failed. The
context does not activate Library or promote Root.

## Activation and reconciliation

### Explicit activation

Subject and lens activation return one of these semantic outcomes:

| Outcome | State effect |
| --- | --- |
| Applied | Installs the exact requested subject or lens in a replacement snapshot |
| Unavailable | Returns current availability without substituting another requested target |
| Rejected | Retains state because the command is stale, foreign, or invalid |
| Failed | Retains state because navigation evaluation failed |
| Superseded | Produces no visible effect because a newer explicit intent owns the session |

An unavailable request never silently activates a sibling, ancestor, or
recommended Type. If the already committed subject became invalid
independently, automatic reconciliation may change it before the unavailable
outcome is returned.

Selecting a Library does not also select a Type. Selecting a Type or Member
directly returns its complete ancestor context.

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
  maintenance results, while queued maintenance rebuilds from the replacement
  snapshot;
- standalone maintenance is admitted in request order, not completion order;
- maintenance cannot install during unresolved explicit work or unconsumed
  visible effects;
- every admitted result receives exact session, state-revision, intent, and
  effect-epoch authority;
- stale or foreign authority cannot install state or move focus;
- prerequisite failure terminates the explicit operation without inventing a
  navigation result; and
- acknowledgement or abandonment releases queued maintenance.

The host treats tokens and authority as opaque. It validates authority through
the session before rendering a returned snapshot and again before deferred
focus or outcome work. Installation alone is not continuing authority.

Retained operations read the session's installed snapshot. The separate
stateless variant may consume an explicit prior snapshot and has no implicit
cross-command state.

## Canonical restoration

Canonical restoration prepares subject and lens together under one explicit
intent. The authoritative transaction state machine is
[`AtomicRestoration.tla`](models/inspection-subject-navigation/AtomicRestoration.tla).

After packet decoding, coordinate realization, and portable identity
resolution, the canonical-state owner supplies:

- the realized coordinate;
- an optional exact subject; and
- an optional exact navigation lens.

Inspection Subject Navigation prepares one complete snapshot without installing
it. Issue #4787's coordinator commits that snapshot only when every restoration
participant is ready. Participant failure aborts without partial navigation
state; supersession prevents an older preparation from committing.

Section, body, source-target, and other portable state remain outside this
owner.

## Consumer contract

### Inspect Web

Inspect Web:

- renders snapshot identity, order, labels, availability, reasons, and
  diagnostics verbatim;
- submits subject action IDs with their issuing generation;
- submits lens identities through Inspection Subject Navigation;
- obtains opaque intent and effect authority from the retained session;
- installs no snapshot and moves no focus under stale authority;
- renders the active subject consistently across command, hierarchy, Library
  selector, lens strip, and content; and
- performs no subject or lens fallback after a non-applied outcome.

The UI owns menu, listbox, tab, focus, responsive, history, and visible failure
behavior. It does not own the product state machine.

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
| `NavigationSession.tla` | Latest explicit intent wins; maintenance is request ordered; stale authority has no effect; abort and acknowledgement preserve liveness |
| `AtomicRestoration.tla` | Subject and lens commit together; failed or superseded preparation cannot partially install |
| `SnapshotAuthority.tla` | Retained state comes only from the installed snapshot; stateless prior state is explicit; stale or foreign authority is rejected |

The model README records the TLC commands and scope. Model checking validates
these finite specifications, not the implementation.

### Required implementation gates

The eventual subject-navigation implementation must include named gates for:

- `InitialRecommendation_PrefersTypeThenLibraryThenRoot`
- `TypeRecommendation_UsesPrimaryLibraryAccessibilityAndProducerOrder`
- `InitialRecommendation_NeverChoosesMember`
- `EveryBoundedInventoryRow_PreservesProducerOrderAndIdentity`
- `UnavailableDescriptor_HasNoTargetOrActionId`
- `ExplicitUnavailableTransition_DoesNotApplyFallback`
- `SameCoordinateReconciliation_FollowsSubjectTable`
- `CoordinateVariation_UsesTypedCorrespondence`
- `LensReconciliation_PreservesExactSubjectScopedIdentity`
- `RetainedSession_UsesInstalledSnapshotAsOnlyPriorState`
- `RetainedSession_RejectsCallerSuppliedPriorSnapshot`
- `Maintenance_SerializesInRequestOrderAcrossCompletionTiming`
- `Maintenance_CannotInstallDuringUnconsumedEffect`
- `EffectAuthority_RequiresExactCurrentSessionRevisionIntentAndEpoch`
- `ExternalIntentAbort_ReleasesMaintenanceAfterAcknowledgement`
- `CanonicalRestoration_PreparesAndCommitsSubjectLensAtomically`

Inspect Web needs non-vacuity gates that fail when it:

- chooses an initial subject or fallback locally;
- submits Type or Member identities instead of action IDs;
- supplies retained prior state outside the navigation session;
- derives Type-inventory Library context from visible rows;
- constructs or compares intent tokens;
- installs or focuses under stale effect authority; or
- splits canonical subject and lens restoration.

## Acceptance cases

| Case | Expected result |
| --- | --- |
| Ordinary package | Highest-ranked trustworthy Type with API lens |
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
| Canonical subject plus non-default lens | One prepared snapshot commits with no intermediate lens |

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
