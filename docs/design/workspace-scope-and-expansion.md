# Workspace scope and expansion

## Status

Focused component design proposal for
[#5697](https://github.com/richlander/dotnet-inspect/issues/5697), the
end-to-end tracker for one live Inspect Web Workspace viewer/editor.

This design establishes **Workspace Scope and Expansion** as one architectural
owner through the bounded one-donor transfer allowed by
[Design scope and composition](../design-scope.md#one-owner-per-focused-design).
The donor is
[Artifact acquisition and workspace composition](artifact-acquisition-and-workspaces.md).
That owner retains runtime Workspace identity, artifact realization, admission
and query authorization, assembly-context construction, physical publication,
budgets, and resource lifetime. Its existing concepts table assigns logical
Workspace composition to that owner. This effort transfers that one cohesive
responsibility and defines it concretely as committed Root membership and
order, Workspace-bound occurrence issuance, selective dependency-expansion
eligibility, closure completeness, revision authority, and scope-operation
results.

This owner consumes the resource-free Root projection defined by parent slice
[#5715](https://github.com/richlander/dotnet-inspect/pull/5715) and the Root
preparation/publication handoff defined by parent slice
[#5729](https://github.com/richlander/dotnet-inspect/pull/5729). It does not
redefine Artifact Acquisition correspondence, generation, status, currentness,
receipt lifetime, physical publication, or resource-erasure semantics.

### Initial shared implementation

Issue [#5821](https://github.com/richlander/dotnet-inspect/issues/5821) implements
the initial host-neutral, Package-only closed Scope: complete immutable
snapshots, exact ordered replacement, Clear, and cancellation of one exact
preparing operation. It consumes the physical producer landed in
[#6068](https://github.com/richlander/dotnet-inspect/pull/6068). The fixed initial
logical profile permits at most 64 distinct Roots; Artifact Acquisition
continues to enforce physical admission and lifetime limits.

The public entry points on `InspectionWorkspace` are `GetScopeSnapshotAsync`,
`ReplaceScopeAsync`, `ClearScopeAsync`, and `CancelScopePreparationAsync`.
Replace consumes already-acquired `PackageRootBinding` inputs, the expected
`WorkspaceScopeRevision`, and a finite deadline; Clear likewise requires the
expected revision and deadline. Snapshots and operation results retain
resource-free Root facts rather than those bindings. Package descriptors
preserve display `PackageId`, exact `PackageVersion`, and effective
`TargetFramework` separately from canonical coordinate and selection facts.
Root-only and explicit-empty selections remain reportable Roots.

Issue [#6151](https://github.com/richlander/dotnet-inspect/issues/6151) extends
this exact-package, closed profile with `AddRootsAsync` and
`RemoveRootOccurrenceAsync`. Add consumes already-acquired bindings and
appends the complete distinct new batch in first-request order, retaining
existing order and occurrence identities. An empty or all-present batch is
`NoEffect` and does not prepare or repair physical Roots. Remove consumes one
`WorkspaceRootOccurrenceIdentity`; it neither interprets a package name or row
index nor selects a successor. Both require the expected revision and a finite
deadline. Ordinary Add/Remove return `Rejected(Busy)` rather than superseding
preparation; validation precedes that admission decision. Replace/Clear retain
their existing supersession authority and first-observed stop ordering.

Effective incremental publication requires a current Ready generation for
every surviving Root, as required by Artifact Acquisition's existing Retain
arm. If Add or Remove would retain a Pending/Failed Root, this initial profile
returns `Failed(ArtifactGenerationMismatch)` before preparing new material and
preserves the complete current Scope. It does not drop that Root, invent a
generation, or reacquire through a retained snapshot. Removing the non-Ready
occurrence itself can succeed when every survivor is Ready or the resulting
set is empty. Supporting publication that retains non-Ready Roots requires
separate Artifact-owner work; observation of those Roots remains supported.

Membership changes use the sealed Artifact publication participant. Current
snapshot reads observe every Ready/Pending/Failed projection under the existing
composition read lease and perform only the clarified Scope-only refresh;
they preserve logical revision, occurrences, and Preparing while issuing fresh
Scope publication and closure identities. The Artifact epoch is unchanged by
that observation.

The user-authorized first host adoption is the CLI
([#5513](https://github.com/richlander/dotnet-inspect/issues/5513)) in the same
issue #5821 slice. The CLI now populates its fresh Scope with one complete
`AddRootsAsync` batch, providing a production consumer for the incremental
path without per-package publication or changed output. Browser Add/remove is
the named incremental consumer under #5697, but its migration still requires
owner-backed complete restoration under #5525. Existing Browser behavior and
its legacy occurrence-view consumer remain unchanged. Expansion scopes and evidence,
non-package preparation, Navigation, Definitions, and persistence remain later
work. The broader target gates below remain **unverified** outside the
implemented subset. CLI inventory rendering uses the committed snapshot rather
than constructing a caller-owned occurrence view; it lowers package facts
through the existing Markout rows without serializing runtime identities.
The acquisition-only context adapter preserves the existing implementation-
universe compatibility rule when no exact compile selection exists; the CLI
continues to display the requested coordinate's framework, while the descriptor
also retains the selected framework.
[`WorkspaceCommandTests`](../../src/dotnet-inspect.Tests/WorkspaceCommandTests.cs)
gates duplicate coalescing before row windows/counts, root-only and explicit-empty
Roots, display/framework preservation, JSON/JSONL shape, and absence of a
successful prefix after a failed Add batch. Browser adoption remains unverified.

The Release implementation gate is
[`WorkspaceScopeTests`](../../src/DotnetInspector.Queries.Tests/WorkspaceScopeTests.cs).
Its boundary evidence includes:

| Implemented boundary | Release tests |
| --- | --- |
| Complete immutable state and default multi-package replacement | `InitialScopeIsCompleteEmptyClosedAndWorkspaceExact`, `DefaultReplacementPublishesTwoSmallPackagesAndResourceFreePresentation`, `ReplacementKeepsPriorRevisionCurrentDuringPreparation` |
| All-or-failure, exact duplicate coalescing, and occurrence retention | `OneFailedRootPublishesNoSuccessfulPrefix`, `ExactDuplicatesCoalesceBeforePreparationAndRetainedOccurrencesFollowRequestOrder`, `RemovedThenEqualReaddedRootGetsFreshOccurrence` |
| Validation before supersession, finite deadlines, and exact cancellation | `InvalidSubmissionsDoNotSupersedeAdmittedPreparation`, `DeadlineExpiryAfterAdmissionCancelsRatherThanRejects`, `ExactCancellationActionSettlesTheOriginalOperationAndCannotCancelAnother`, `CancellationBeforeCommitPreservesPriorRevision`, `CancellationAfterCommitCannotRetractPublication` |
| Clear and Replace supersession without stale publication | `ClearSupersedesBlockedPreparationWithoutWaitingForCurrentQuery`, `ValidReplaceSupersedesPreparationAndOldCompletionCannotOverwriteIt` |
| Cancellation/deadline versus supersession preserves the first observed outcome | `CancellationBeforeSupersessionRetainsFirstOutcome`, `SupersessionBeforeCancellationRetainsFirstOutcome` |
| Ordered incremental Add, exact reduction, and no successful prefix | `AddPreservesExistingOrderAndAppendsOneDistinctBatch`, `EmptyOrDuplicateOnlyAddHasNoPhysicalEffect`, `AddReducesExactCorrespondenceBeforeLogicalCapacityAndPreparation`, `LaterAddFailureDoesNotPublishSuccessfulPrefix` |
| Exact removal, surviving identity, and admitted-query drainage | `RemoveRetainsOtherOccurrencesAndDoesNotWaitForAnAdmittedQuery`, `RemovingTheLastOccurrenceCommitsAnEmptyClosedRevision` |
| Incremental validation before Busy and inherited stop ordering | `OrdinaryAddAndRemoveAreBusyWithoutSupersedingPreparation`, `IncrementalValidationPrecedesBusy`, `AddSupersessionPreservesFirstObservedStop`, `AddCancellationOrExpiryLeavesThePriorRevisionCurrent`, `RemoveHonorsCancellationBeforeAdmissionAndCannotBeRetractedAfterCommit` |
| Non-Ready incremental boundary and stale physical refusal | `IncrementalEditsRefuseNonReadySurvivorsBeforePreparingMaterial`, `PhysicalMovementDuringAddRefusesTheStaleCandidateAndRefreshesAllCurrentRoots`, `IncrementalOperationsRespectRuntimeUnavailabilityBeforeInvalidRequests` |
| Complete physical observation without another Artifact publication | `ObservationRefreshPreservesReadyPendingFailedAndDoesNotPublishArtifactComposition`, `RefreshDuringPreparationPreservesPreparingAndStalePhysicalCandidateCannotRebase` |
| Runtime unavailability and historical resource drainage | `ClosingWorkspaceReportsUnavailableWhileAnAdmittedQueryDrains`, `CloseDuringPreparationSettlesUnavailableAndReleasesOperationAuthority`, `HistoricalSnapshotsAndResultsDoNotRetainRetiredResources` |

The historical-state gate retains both a Preparing snapshot and a committed
result, awaits actual product retirement settlement, and then checks collection
of package bindings, content, sessions, and realization resources.

The focused implementation and adjacent-owner regression command is:

```bash
dotnet run --project src/DotnetInspector.Queries.Tests -c Release -- \
  -class '*WorkspaceScopeTests' \
  -class '*ArtifactRootPublicationTests' \
  -class '*ArtifactRootCorrespondenceTests' \
  -class '*PackageAssemblyContextRealizationTests' \
  -class '*WorkspacePackageRootAcquisitionTests'
```

The [revision/publication model](models/workspace-scope-revisions/README.md)
provides separate bounded design evidence: all 50 registered exact outcomes
passed for the clarification preserved at
[`4e360ae0b3d0a792cecd3a3fc66caceb464fc4b0`](https://github.com/richlander/dotnet-inspect/commit/4e360ae0b3d0a792cecd3a3fc66caceb464fc4b0).
Those model outcomes do not substitute for Release implementation or host
conformance gates.

[Approved lazy traversal](approved-lazy-traversal.md) records the approved
cross-owner target experience for Browser construction defaults, ecosystem
knowledge, and demand-driven operations. It does not change this owner's
empty-registration-set-is-closed invariant or claim the missing registration,
candidate-discovery, or query-population adoption is implemented.

## Authority and exact claim

Workspace Scope and Expansion is the product authority for the committed
logical inspection scope of one exact runtime Workspace.

It owns:

- one immutable current `WorkspaceScopeRevision` per exact open Workspace;
- ordered committed Root occurrences, typed Root descriptors, and their
  Workspace-bound identities;
- explicit Root addition, replacement, removal, and Clear operations;
- registered typed dependency-expansion scopes;
- the derived closed or selectively open boundary;
- finite logical-scope limits;
- exact closure-completeness and boundary-failure evidence;
- revision-bound mutation admission, supersession, and publication; and
- complete typed scope-operation results.

It does not own:

- runtime Workspace identity construction, close, or resource lifetime;
- package, platform, project, local, or embedded coordinate construction;
- source authorization, package resolution, acquisition, caching, or
  realization;
- artifact admission, byte, network, or execution-work budgets;
- assembly binding, context-group construction, or query authorization;
- dependency discovery or query-specific relationship semantics;
- Navigation subject selection, recommendation, reconciliation, or focus;
- browser history, rendering, interaction, URL state, or effect authority;
- portable Workspace schema, packet capacity, projection, or restoration
  coordination; or
- saved-Workspace accounts, synchronization, or storage.

The owner answers:

> Which exact Roots are committed in this Workspace revision, which external
> dependencies may be admitted next, and what complete snapshot resulted from
> this one scope operation?

## Consumers and proportionality

The named production consumers are:

- the Inspect Web Workspace viewer/editor tracked by
  [#5697](https://github.com/richlander/dotnet-inspect/issues/5697), with
  presentation and browser-result adoption remaining in
  [#5510](https://github.com/richlander/dotnet-inspect/issues/5510) and
  [#5511](https://github.com/richlander/dotnet-inspect/issues/5511); and
- the stateless agent-oriented CLI Workspace surface tracked by
  [#5513](https://github.com/richlander/dotnet-inspect/issues/5513).

The concrete Browser scenario is one current inspection scope containing one
or more exact package, platform, and later non-package Roots. A user can replace
that scope, add to it, remove from it, clear it, inspect its admitted
assemblies, and selectively permit dependency following. The CLI consumes the
same snapshot and results without adding retained terminal navigation.

This infrastructure is warranted only to support that scenario. It deliberately
does not add:

- a Workspace collection, switcher, tab model, or simultaneous live Workspace
  composition;
- a generalized transaction framework for unrelated product state;
- an extensible plugin vocabulary for expansion policy; or
- a universal source-realization protocol.

The first complex proof is the 44-package `Microsoft.Extensions` set from the
[Package Set Registry](package-set-registry.md). The scope owner therefore
needs atomic multi-Root edits, visible failures, and a capacity above the
current 12-package Browser limit. It does not need a multi-Workspace manager.

## Design demo

Inspect Web exposes one Workspace subject rather than a list containing one
named Workspace:

```text
Workspace

In scope
  Microsoft.Extensions.Logging       10.0.0  net10.0  4 assemblies  [Inspect] [Remove]
  Microsoft.Extensions.Options       10.0.0  net10.0  3 assemblies  [Inspect] [Remove]
  :Platform                          10.0.0  net10.0  168 assemblies [Inspect] [Remove]

Dependency expansion
  Package prefix  Microsoft.Extensions.                         [Remove]
  [Add expansion scope]

Boundary
  Complete for observed eligible dependencies
  2 external dependencies remain outside registered expansion scopes

[Add package] [Add package set] [Clear]
```

With no registered expansion scopes, the same section reads:

```text
Dependency expansion
  Closed
  Dependencies outside the admitted scope remain visible but are not acquired.
```

Global Search and Package inspection use **Open** to replace the current
Workspace. The Workspace editor uses **Add** for explicit accumulation. Opening
an exact Root that is already present returns that existing occurrence without
reacquisition.

The neighboring package-set case prepares all 44
`package-set.microsoft-extensions` coordinates and commits one revision. A
failure in any required package leaves the prior revision current and reports
the exact package failure; it never evicts older Roots or publishes a shortened
set.

## Problem

The product currently has several partially overlapping meanings of
Workspace:

- a physical owner of artifact sessions and assembly-context groups;
- a Browser-retained package array;
- a portable definition or share packet;
- a Navigation subject;
- a proposed collection of several simultaneously live Workspaces; and
- a possible dependency-discovery boundary.

Those meanings have begun to produce independent lifecycle, history,
membership, and identity protocols. Inspect Web instead needs one ordinary
current scope with explicit editing and persistence as a separate concern.

The current Browser package list also weakens the contract in important ways:

- ordinary package opens accumulate while canonical restoration replaces;
- exceeding 12 packages silently evicts older members;
- a demo can arrive through either canonical restoration or a special engine
  operation;
- dependency references do not have one explicit closed/open boundary; and
- the visible Workspace previously rendered as `WORKSPACES 1` and
  `Default Workspace`, implying a multi-Workspace manager.

The missing product concept is not a Workspace manager. It is one authoritative
logical scope over physical acquisition and binding resources that already have
owners.

## One live Workspace

Inspect Web holds exactly one live runtime Workspace. Activating a demo, share
packet, imported definition, saved definition, or ordinary **Open** request
means replacing the scope in that Workspace; none creates a second live
Workspace object. An ordinary scope-only **Open** can perform that replacement
now. An input that also restores canonical Navigation, view, query, or history
state remains blocked on the focused complete-restoration participant described
below.

This component exposes no Workspace collection, switcher, name, or
cross-Workspace operation. Each runtime Workspace has one current scope
revision. A host may retain portable definitions or browser-history entries as
data, but activating one prepares a replacement revision rather than reviving a
simultaneously live instance. A packet is serialization input and output, not a
user-visible packet Workspace.

The CLI normally creates one ephemeral runtime Workspace for one invocation.
Future service hosts may independently create runtime Workspaces for separate
requests, but this owner does not compose, compare, or present them together.

## Analogous designs

Two established products provide useful evidence:

- [Visual Studio Code workspaces](https://code.visualstudio.com/docs/editing/workspaces/workspaces)
  associate one window with one current workspace. A workspace may contain
  multiple folders, users add or remove folders explicitly, and
  [multi-root workspaces](https://code.visualstudio.com/docs/editing/workspaces/multi-root-workspaces)
  can be saved separately as a `.code-workspace` file. This supports separating
  one live editable scope from its persisted definition.
- [dotPeek assembly lists](https://www.jetbrains.com/help/decompiler/Managing_Assemblies.html)
  can be saved, opened, and cleared. References remain inspectable evidence,
  while a user explicitly promotes a referenced assembly to the root assembly
  list for inspection. This supports visible dependency boundaries without
  mandatory admission.

dotnet-inspect follows the conventional one-current-scope, explicit-edit, and
separate-persistence model. It deliberately diverges by allowing typed
selective expansion such as a package prefix. Assembly inspection crosses
package, platform, and source authorities; a single global
`Auto-load dependencies` Boolean would either be too permissive or too weak.

## Domain model

```text
InspectionWorkspaceIdentity              artifact owner
  |
  +-- WorkspaceScopeRevision             this owner
        RevisionIdentity
        Ordered RootOccurrences
        ExpansionScopes
        ScopeLimits
        |
        +-- WorkspaceScopeSnapshot
              Scope publication base
              Physical composition epoch
              Ordered current Root descriptors
              ClosureObservation
              Optional preparation
```

### Runtime Workspace identity

The artifact owner issues the exact process-local
`InspectionWorkspaceIdentity` and decides whether its runtime is accepting
operations, closing, or closed. This owner cannot construct, compare by value,
serialize, reopen, or prolong that identity.

Every scope revision carries that exact identity. Equal definitions, package
coordinates, context addresses, URLs, labels, or member sequences do not make
two runtime Workspaces equal.

User-facing **closed** and **selectively open** describe dependency-expansion
eligibility. They do not rename or replace the artifact owner's runtime
accepting/closing/closed lifetime states.

Every new scope operation and current snapshot refresh first consumes
Artifact Acquisition's gate-observing runtime and physical-composition status.
An absent runtime for a retained scope identity is an invariant or stale-
composition failure, not an empty Workspace. Closing or closed rejects new
scope operations. Snapshot refresh returns a typed
`Unavailable(RuntimeCompositionUnavailable)` result and may expose the last
retained resource-free snapshot only as historical diagnostic evidence; it
does not fabricate `Pending`, `Failed`, an empty Root sequence, or another
success-shaped current result.

### Scope revision

`WorkspaceScopeRevision` is an immutable complete logical snapshot:

```text
WorkspaceScopeRevision
  Workspace               InspectionWorkspaceIdentity
  Revision                WorkspaceScopeRevisionIdentity
  Roots                   ordered WorkspaceRootOccurrence sequence
  ExpansionScopes         ordered WorkspaceExpansionScope sequence
  Limits                  WorkspaceScopeLimits
```

`WorkspaceScopeRevisionIdentity` is opaque, process-local, and distinct for
every committed revision, including a later revision whose data equals an
earlier revision. The initial revision is empty, has no expansion scopes, and
its initial snapshot carries `ClosedBoundary` with empty evidence.

One revision is current. Retired revision values remain comparable and may be
held by Navigation, history preparation, or diagnostics, but cannot authorize a
new mutation.

`WorkspaceScopeSnapshot` is the complete observable view:

```text
WorkspaceScopeSnapshot
  Revision                WorkspaceScopeRevision
  PublicationBase         WorkspaceScopePublicationBaseIdentity
  PhysicalComposition     ArtifactRootCompositionGenerationIdentity
  Roots                   ordered WorkspaceRootOccurrenceDescriptor sequence
  Closure                 WorkspaceClosureObservation
  Preparing               optional WorkspaceScopePreparationDescriptor
```

The immutable revision carries logical membership and expansion policy. The
snapshot additionally projects current adjacent-owner realization status, one
exact Scope publication-base issuance, one exact parent-owned
physical-composition epoch, one closure observation, and one preparing
operation without pretending that uncommitted Roots are members. A closure-only
publication or physical re-realization can replace the snapshot's publication
base, physical epoch, Root projections, and closure observation without
changing the logical revision identity.

`WorkspaceScopePublicationBaseIdentity` is opaque, process-local, and
process-lifetime non-reused. The scope owner issues one fresh value for the
initial snapshot and every later current-snapshot pointer swap, including
preparation progress, membership, policy, closure-only, physical-refresh,
cancellation, supersession, and failure publication. Equality proves only one
exact Scope pointer issuance. It is not a revision, closure observation,
physical-composition identity, operation authority, portable value, or access
grant.

Scope-only preparation progress, cancellation, supersession, failure, and
observation of an already-published physical epoch observe the shared runtime
composition gate but need no Artifact publication plan when they leave physical
composition unchanged. Each issues a fresh Scope publication base. A physical
observation refresh preserves the logical revision and every occurrence,
including those currently Pending or Failed. After the final parent participant is constructed, no
ordinary progress publication may invalidate it; cancellation or supersession
that enters the same gate first replaces its expected base before
`PrepareCommit`. After `PrepareCommit`, independently signaled cancellation or
deadline expiry can still win at the parent's final recheck. A later
supersession waiting for the held gate, or cancellation after the final
non-yielding commit begins, loses to the committed result.

`WorkspaceScopePreparationDescriptor` carries only the operation identity and
kind, requested Root count, non-retaining adjacent-owner progress evidence,
deadline, and exact cancellation action. Provisional bindings, contexts,
leases, and receipts remain internal to Artifact Acquisition. The descriptor
carries no occurrence identity for a requested Root that has not committed.

### Root occurrences

A committed Root occurrence is one exact logical membership issuance:

```text
WorkspaceRootOccurrence
  Identity                Workspace-bound opaque occurrence identity
  Root                    WorkspaceRootDescriptor
  Correspondence          ArtifactRootCorrespondence

WorkspaceRootDescriptor
  = Package(owner-issued exact resolved package and selection descriptor)
  | NonPackage(owner-issued exact resource-free Root coordinate descriptor)
```

`ArtifactRootCorrespondence` is issued and defined by
[Artifact acquisition and workspace
composition](artifact-acquisition-and-workspaces.md#resource-free-root-scope-projection).
This owner retains that opaque logical relation without reconstructing its
typed coordinate or selection inputs. The adjacent owner defines equality,
exact request matching, process locality, and resource erasure. Display text,
paths, assembly names, definition addresses, and row indexes are neither
occurrence identity nor correspondence.

`WorkspaceRootDescriptor` is the scope-owned composition of one adjacent
coordinate owner's resource-free exact descriptor and one closed Package versus
non-package discriminator. The package arm exposes the exact resolved package
ID, version, target framework, and runtime selection facts needed by current
inventory consumers. A non-package arm retains its coordinate owner's exact
typed Root descriptor. This owner preserves those values; it does not parse,
construct, compare, or infer an inner coordinate. Presentation owners derive
labels from the typed descriptor, and Workspace Definitions separately decides
which fields have a portable representation.

The descriptor strongly owns no Root realization, package content, byte
buffer, binding, assembly context, artifact session, lease, provisional
receipt, delegate, or access authority. Retaining a descriptor therefore
cannot prolong physical generation lifetime.

An occurrence identity remains stable while that exact occurrence is retained
across revisions. Removing it retires the occurrence. Re-adding an equal Root
later creates a new occurrence unless the operation classified it as already
present before removal.

One snapshot descriptor combines that logical occurrence with current physical
status:

```text
WorkspaceRootOccurrenceDescriptor
  Occurrence               WorkspaceRootOccurrence
  Realization              ArtifactRootScopeProjection
```

Artifact Acquisition issues the immutable point-in-time projection and defines
its `Ready`, `Pending`, and `Failed` arms. Workspace Scope and Expansion
associates a projection with the exact occurrence and preserves owner order.
It does not infer currentness from a retained generation reference. A newly
published current scope snapshot refreshes each occurrence through
`GetCurrentRootScopeProjection` or consumes a projection returned atomically by
the adjacent operation.

A current snapshot read observes the shared runtime composition gate and
compares the snapshot's physical-composition identity with
`GetCurrentArtifactRootCompositionGeneration`. Equal identity permits the
already complete snapshot. Different identity requires one complete projection
refresh and closure invalidation before a current snapshot returns. Scope reads
the complete projection set from one Artifact composition read lease and swaps
only its preconstructed snapshot while that lease holds the shared gate. This
issues a fresh Scope publication base and closure observation, preserves the
logical revision, occurrence identities, and preparing descriptor, and leaves
the already-current Artifact composition identity unchanged. It does not submit
an Artifact publication plan: Pending and Failed projections have no Ready
generation to Retain. Individual per-Root reads are never exposed as a mixed-epoch
snapshot. Membership-changing publication still uses the parent transition.

An absent correspondence projection for a committed current occurrence is a
typed invariant or stale-composition failure. A closing or closed runtime
returns `Unavailable(RuntimeCompositionUnavailable)`. Neither case removes the
occurrence, substitutes an empty result, or changes the logical revision.

When Artifact Acquisition changes a retained occurrence from `Ready`, or
publishes a corresponding replacement generation, the next observable scope
snapshot atomically carries the refreshed projection while the logical revision
and occurrence identity remain unchanged. The scope owner also replaces the
current closure observation: an empty expansion-scope set becomes
`ClosedBoundary` with empty evaluated coverage and evidence, and any
selectively open scope becomes `NotEvaluated` with empty evaluated coverage,
the complete current occurrence sequence as its frontier, and empty evidence.
A replacement that does not prove correspondence to the retained logical Root
requires a membership-changing scope result before consumers can observe it as
current. A newly requested Root remains in the operation-level `Preparing`
descriptor until complete realization permits atomic admission.

The scope owner does not reinterpret a physical failure or mint a success-shaped
empty Root. Navigation may consume these exact statuses without becoming the
realization owner.

### Expansion scopes

The version-1 expansion vocabulary is a closed union:

```text
WorkspaceExpansionScope
  = ExactPackage(owner-issued exact package coordinate)
  | PackagePrefix(owner-issued validated package-name prefix value)
  | PlatformGroup(owner-issued well-known group identity)
```

The union is deliberately finite. A new expansion kind requires a focused
contract revision; consumers do not register plugins or untyped predicates.

Each arm retains its adjacent owner's exact typed value:

- package equality and prefix matching remain package-domain behavior;
- Workspace Definitions supplies well-known platform-group identity; and
- this owner only asks whether exact external dependency evidence matches the
  retained value.

The package-prefix arm retains only the validated package-name prefix value
carried by owner-issued source intent. It does not retain a source selector,
query bound, paging cursor, or completion policy. An ecosystem recorded-prefix
action may carry that same value inside a source-query request, but executing
or selecting the request is distinct from explicitly registering the value as
Workspace expansion eligibility.

An expansion scope is eligibility, not membership:

- registration acquires nothing;
- a package prefix does not enumerate matching packages;
- removing a scope does not remove Roots admitted while it was registered; and
- an eligible dependency still requires current source authorization,
  realization, admission, and budget.

Equal scopes are retained once, in first-registration order. Registering an
equal scope returns `NoEffect` with the retained exact scope value.

### Closure observation and state

Closure is one immutable observation over an exact logical revision and exact
evaluated physical generations:

```text
WorkspaceClosureObservation
  Identity                WorkspaceClosureObservationIdentity
  SourceRevision          WorkspaceScopeRevisionIdentity
  EvaluatedRoots          ordered WorkspaceEvaluatedRoot sequence
  State                   WorkspaceClosureState

WorkspaceEvaluatedRoot
  Occurrence              WorkspaceRootOccurrenceIdentity
  Realization             adjacent-owner resource-free generation reference
```

The observation identity is opaque, process-local, and fresh for every closure
publication or invalidation. `EvaluatedRoots` records the exact non-retaining
generation references covered by the dependency producer. It never contains a
`Pending` or `Failed` occurrence. Initial or invalidated `ClosedBoundary`
observations and reset-created `NotEvaluated` observations have empty
`EvaluatedRoots`. A `NotEvaluated` observation published by explicit expansion
may retain the exact prior Ready-generation coverage while naming a remaining
unevaluated frontier.

Closure state describes what is known for that exact coverage:

```text
WorkspaceClosureState
  = ClosedBoundary(observed outside-boundary evidence,
      producer-bound evidence)
  | CompleteForObservedEvidence(declined outside-boundary evidence)
  | Incomplete(declined evidence, unsupported, rejections, failures, limits,
      unevaluated Root occurrences)
  | NotEvaluated(unevaluated Root occurrences,
      current-observation outside-boundary evidence)
```

`ClosedBoundary` means no expansion scope is registered. It does not claim that
the admitted Roots have no external dependencies. An explicit dependency
evaluation may attach every observed dependency as intentionally outside that
closed boundary.

`CompleteForObservedEvidence` is always bounded by the exact dependency query,
evidence generation, expansion depth, and operation limits that produced it.
It is available only when membership did not change, no current Root remains
non-Ready, and every eligible candidate in that evidence was already admitted
or otherwise settled without adding a Root. Dependencies outside registered
eligibility remain visible declined-boundary evidence without making the
operation incomplete. It never means universal transitive closure for every
possible query.

`Incomplete` retains exact external dependency evidence and the typed reason
each eligible or possibly eligible candidate could not settle. An unsupported,
failed, rejected, depth-bounded, candidate-bounded, or capacity-declined
candidate cannot disappear from the result or become a complete closure.

Every logical membership or scope-policy change publishes a fresh revision and
closure observation. It produces `ClosedBoundary` with empty observed,
producer-bound, and evaluated evidence when no expansion scopes remain.
Otherwise it resets closure to `NotEvaluated` with empty evaluated coverage,
the complete current Root sequence as its unevaluated frontier, and empty
evidence. An
expansion whose evidence covered the complete prior Ready Root sequence and
then appends Roots may instead retain that exact coverage, identify every
current non-Ready or newly admitted occurrence as the frontier in owner order,
and retain outside-boundary evidence observed by that same operation. Evidence
from an earlier revision or physical generation is never carried across a
membership, eligibility, or realization change.

## Scope limits and capacity

Every Workspace has one immutable `WorkspaceScopeLimits` profile. Missing,
zero, or unbounded dimensions are invalid.

The profile contains at least:

- maximum committed Root occurrences;
- maximum registered expansion scopes;
- maximum external candidates in one expansion operation; and
- maximum expansion depth.

The v1 product profile permits **64 committed Root occurrences**. This is the
smallest power-of-two ceiling that admits the current audited 44-package
`Microsoft.Extensions` set while leaving capacity for neighboring explicit
Roots. It replaces silent least-recent eviction; an explicit Add or Replace
that would exceed the ceiling returns a typed rejection and preserves the
current revision.

Sixty-four is only the logical Root ceiling. Artifact Acquisition continues to
enforce retained bytes, acquisition bytes, participant counts, network work,
execution work, and concurrent-generation budgets. A 44-package edit succeeds
only when both owners admit the complete candidate.

The other dimensions are explicit finite inputs because their correct values
depend on the dependency producer and execution budget. A host may choose
stricter values but must disclose them and must not offer a package-set action
whose complete set exceeds its Root ceiling.

An operation's effective limit for each dimension is the component-wise
minimum of the immutable Workspace profile and the finite operation envelope.
An omitted, zero, or unbounded operation value is invalid; a value above the
profile is finite but does not enlarge authority. The envelope can narrow
authority but cannot enlarge it.

Structural limits and producer bounds are different evidence:

- a submitted batch whose distinct materialized acquisition candidates exceed
  its effective candidate envelope, whose relationship depth exceeds its
  effective depth, or whose bound metadata is inconsistent is malformed and
  returns a typed rejection before any adjacent-owner work; while
- a conforming producer that stopped at a finite candidate or depth limit
  submits a typed `CandidateBoundReached` or `DepthBoundReached` marker inside
  the valid envelope. The marker is retained as incomplete closure evidence and
  is not a malformed batch.

Root capacity selection still occurs after exact candidate coalescing. Present,
within-envelope candidates in a producer-bounded batch remain eligible for
ordinary classification and preparation; no omitted or out-of-envelope
candidate invokes an adjacent owner.

The current Workspace Definitions packet limit is 12 tuples. Aligning portable
capacity with the Browser's reachable scope remains a separately owned
[#5525](https://github.com/richlander/dotnet-inspect/issues/5525) residual.
This owner defines neither packet capacity nor projection failure semantics.

The 64-Root logical limit does not raise Artifact Acquisition's retained-byte
or participant budgets. Before the Browser offers the complete registered
`Microsoft.Extensions` set, the artifact-backed Browser adoption in
[#5576](https://github.com/richlander/dotnet-inspect/issues/5576) must prove
that its acquisition-owned budget admits that descriptor's complete current
membership or change that budget under its owning design.

## Scope operations

The initial operation vocabulary is limited to the viewer/editor scenario:

```text
WorkspaceScopeOperation
  = ReplaceScope
  | AddRoots
  | RemoveRootOccurrence
  | Clear
  | RegisterExpansionScope
  | RemoveExpansionScope
  | ExpandDependencies
```

Package **Open**, resolved package-set **Open**, and an explicitly scope-only
demo or definition action that resets rather than restores Navigation, view,
query, and history state lower to `ReplaceScope`. Workspace-editor **Add
package** and resolved **Add package set** lower to `AddRoots`.
Source-selection owners resolve their inputs before this owner receives exact
Root requests. Canonical restoration inputs do not lower to `ReplaceScope`;
they remain blocked on the #5525 participant described below.

```text
WorkspaceScopeReplacement
  Roots                   ordered exact Root request sequence
  ExpansionScopes         ordered typed expansion-scope sequence
```

An ordinary package Open supplies an empty expansion-scope sequence and is
therefore closed. An explicitly scope-only demo or definition action may
supply its own complete typed expansion policy. The previous Workspace's
expansion scopes are never inherited by omission. Canonical restoration will
supply the same complete sequences through its future uncommitted Scope
participant rather than this publishing operation.

Package-set Browser adoption is not enabled by this transfer alone.
[Static Ecosystem Packs](ecosystem-packs.md) may expose an **Add curated
packages** action, but selection returns only its referenced `PackageSetId`.
Issue #5720 preserves one Package Set Registry membership authority, while
issue #5602 owns typed source declaration and normalization, and package-source
owners resolve exact coordinates. Only then does the front end choose
`ReplaceScope` for Open or `AddRoots` for editor accumulation. Pack identity,
discovery metadata, prefix actions, and scanner bindings never enter scope
state or Artifact publication.

### Common operation envelope

Every operation carries:

- the exact runtime Workspace identity;
- the exact current base revision identity;
- one operation identity;
- one complete requested effect;
- finite operation limits and preparation deadline; and
- optional user-activation intent naming one exact requested Root.

The result is one closed union:

```text
WorkspaceScopeOperationResult
  = Committed(snapshot, effect, optional requested occurrence, evidence)
  | NoEffect(snapshot, optional requested occurrence, evidence)
  | Rejected(current snapshot, exact reason)
  | Failed(current snapshot, exact failure)
  | Cancelled(current snapshot, cancelled operation)
  | Superseded(current snapshot, superseding operation)
  | Unavailable(optional last retained snapshot, exact runtime outcome)
```

Every arm other than `Unavailable` carries the complete current scope snapshot
observed when the result settles. `Unavailable` is returned only when Artifact
Acquisition reports the exact runtime Workspace absent, closing, or closed. It
may carry the last retained resource-free snapshot as historical diagnostic
evidence, explicitly not as current authority. No result requires Navigation
or a host to reconstruct membership from an effect delta.

`Committed` means that the observable scope state changed. A membership or
expansion-policy effect carries a fresh logical revision and closure
observation. A closure-only effect retains the logical revision identity and
carries a fresh closure-observation identity.

Only an explicit user request can authorize an optional requested occurrence.
Owner-policy expansion never supplies activation authority. Navigation
consumes that field as its requested active/replacement occurrence input and
decides whether and how to focus it.

`Busy`, `RevisionMismatch`, and `EvidenceMismatch` are exact `Rejected`
reasons. Adjacent runtime availability has absolute precedence because no
current authoritative snapshot exists when that runtime is absent, closing, or
closed. Every submission first consumes that gate-observing status and returns
`Unavailable` without adjacent work when the runtime is unavailable.

For an accepting runtime, submission validation is complete and
side-effect-free before mutation-authority admission. In deterministic order
it validates:

1. the operation union arm, required envelope fields, finite non-expired
   deadline, operation identity, and finite limits;
2. the exact Workspace identity;
3. the current base revision;
4. the complete operation-specific request, typed values, structural limits,
   and evidence correspondence.

The first failure is returned. For an accepting runtime, a pre-admission
expired deadline returns typed `Rejected(DeadlineExpired)`. Deadline expiry or
caller cancellation after admission returns `Cancelled` only when observed
before the parent
publication's final recheck; it releases preparation and leaves the prior
revision current. Once the parent's non-yielding commit begins, publication
wins and the preconstructed Scope result is `Committed`. A stale, foreign,
malformed, over-limit, or otherwise invalid Replace or Clear cannot supersede
current preparation. A non-current base revision therefore returns
`Rejected(RevisionMismatch)` even while another mutation is preparing; only a
fully valid current submission can return `Rejected(Busy)` or exercise
Replace/Clear supersession. `Superseded` is reserved for an admitted
preparation later displaced by a valid Replace or Clear.

The singular optional occurrence is only the explicit activation-intent target.
The complete snapshot reports every other retained or added occurrence in a
batch.

### Mutation authority

One exact Workspace permits at most one preparing scope mutation.
The following admission rules apply only after the common validation order has
accepted the complete envelope and operation-specific request.

- An ordinary Add, Remove, or expansion-scope edit submitted while another
  mutation is preparing returns `Rejected(Busy)`; it is not silently queued.
- `ExpandDependencies` submitted while any mutation is preparing likewise
  returns `Rejected(Busy)`.
- A valid Replace Scope or Clear supersedes the preparing operation.
- Clear needs no source or Root preparation. It submits an empty parent
  publication plan and becomes current at that gate's atomic commit, retiring
  every occurrence and leaving no expansion scopes.
- A superseded preparation releases every provisional adjacent-owner resource
  and cannot publish.
- A completion displaced after admission returns `Superseded`; it cannot rebase
  itself onto the replacement.
- Every preparing operation exposes one exact cancellation action and carries a
  finite preparation deadline. Caller cancellation or deadline expiry observed
  before the parent's final recheck returns `Cancelled`, releases provisional
  resources, and leaves the prior revision current. After the final
  non-yielding commit starts, publication wins and returns the committed
  result.
- Failure leaves the prior revision current.

This is intentionally smaller than a general transaction scheduler. Browser
effect authority, Navigation intent, and complete-restoration coordination
remain adjacent contracts.

### Replace scope

`ReplaceScope` prepares one complete `WorkspaceScopeReplacement`.

- The sequence is all-or-failure.
- The complete ordered expansion-scope sequence is validated and deduplicated
  under the same operation before any publication.
- A fully pinned owner-issued exact request for which Artifact Acquisition's
  exact request-matching operation returns a current occurrence's
  `ArtifactRootCorrespondence` retains that occurrence without acquisition.
- Exact duplicate requests are reduced before realization by the source
  owner's equality rules, preserving the first request.
- After realization, requests with exact adjacent-owner Root correspondence
  are coalesced, preserving the first request and releasing every redundant
  parent preparation receipt.
- Every unmatched required Root must realize and pass both logical and
  physical limits.
- Required unmatched Roots may prepare through one or more parent receipts.
  The required set is still all-or-failure: any preparation failure releases
  every successful receipt and publishes nothing. When correspondence cannot
  be known before preparation, separate receipts permit duplicate
  correspondence to release before the publication plan is formed.
- Publication atomically installs the complete new occurrence sequence and
  complete expansion-scope sequence, retires every occurrence not retained by
  exact correspondence, and creates the initial closure observation for that
  replacement.
- The prior revision remains current until publication.
- A Root or expansion-scope failure publishes no shortened or policy-leaking
  scope.

If an existing occurrence has exact adjacent-owner correspondence with one
requested Root, the operation retains that exact occurrence at the order
position determined by the first corresponding replacement request. Equal
display coordinates without that proof do not retain identity. User-activation
intent naming any reduced, matched, or coalesced replacement request maps to
that first corresponding retained or newly admitted occurrence.

### Add Roots

`AddRoots` prepares one exact ordered Root request sequence and appends new
occurrences in request order.

- A fully pinned owner-issued exact request for which Artifact Acquisition's
  exact request-matching operation returns a current occurrence's
  `ArtifactRootCorrespondence` produces no acquisition for that Root.
- Exact duplicate requests inside the Add batch are reduced before capacity
  reservation by the source owner's equality rules, preserving the first
  request and its order.
- A floating, range-based, or otherwise unresolved request must realize first.
  Exact post-realization correspondence with a current Root classifies that
  request as already present and releases the redundant provisional
  realization.
- Exact post-realization correspondence with an earlier request in the same
  batch retains the first request, releases the later redundant provisional
  realization, and creates no second occurrence.
- User-activation intent naming a reduced or coalesced request resolves to that
  first corresponding existing or newly admitted occurrence.
- Before preparation, each unresolved request remaining after exact request
  reduction conservatively reserves one potential new Root slot. An
  over-capacity batch is rejected without adjacent work even when later
  realization might have proved a duplicate; callers may submit a smaller
  batch.
- If the operation carries user-activation intent for an already present Root,
  the result returns that exact existing occurrence.
- New Roots are one all-or-failure logical set. They may use one or more parent
  preparation receipts so post-realization duplicates can release before plan
  construction; a failure in any required new Root releases every successful
  receipt and leaves the whole prior revision current.
- Successful publication retains existing occurrence identities and appends
  every distinct new correspondence atomically in first-request order.
- The whole operation returns `NoEffect` only when every request corresponds to
  a current Root and no scope data changes. If any new Root is appended, the
  operation returns `Committed`, including when the same batch also contained
  already-present Roots.

This behavior makes **Add package set** predictable. It never leaves an
arbitrary successful prefix installed after a later required package fails.

### Remove Root occurrence

Removal names the opaque exact occurrence, not a coordinate or row index.

- A current occurrence is removed in one committed revision.
- A foreign-Workspace, retired, or absent occurrence returns a typed rejection.
- Removing one occurrence does not infer removal of dependents.
- The result identifies no successor and grants no focus authority.

Navigation applies its existing retained-coordinate reconciliation rule after
consuming the complete result. This result itself identifies no successor.

### Clear

Clear commits one empty revision:

- no Roots;
- no expansion scopes;
- `ClosedBoundary` with empty observed and producer-bound evidence; and
- the same exact runtime Workspace identity.

Clear supersedes pending preparation. Physical generations are retired and
drain under Artifact Acquisition's lifetime contract; Clear does not wait for
unrelated already admitted query work to finish before the empty logical
revision becomes current.

### Register and remove expansion scope

Registration validates only the exact typed scope and logical limit. It does no
source discovery or acquisition.

Removal supplies one typed `WorkspaceExpansionScope` value and uses the same
arm-specific equality as registration. It prevents future matches but
preserves already admitted Root occurrences. Registering an equal value or
removing an absent value returns `NoEffect` and preserves the current revision
and closure observation. Effective registration resets closure to
`NotEvaluated` with empty evaluated coverage, the complete current Root
sequence as its frontier, and empty observed evidence. Effective removal does
the same unless it removes the final scope, in which case closure becomes
`ClosedBoundary` with empty evaluated, observed, and producer-bound evidence.
No Workspace-bound scope descriptor or retired-registration identity exists.

### Expand dependencies

Expansion consumes one bounded, producer-issued external-dependency evidence
batch tied to the exact base revision, closure observation, and complete
current Ready Root coverage. Producer order is deterministic and part of the
evidence contract:

1. Reject stale, foreign-Workspace, structurally over-limit, or internally
   inconsistent evidence. The batch's source revision and closure observation
   must still be current, and its evaluated Roots must exactly equal every
   current `Ready` occurrence and generation reference in owner order. Any
   included `Pending` or `Failed` occurrence, omitted current `Ready` Root,
   extra Root, reordered Root, or replaced generation returns
   `Rejected(EvidenceMismatch)`. Recheck current projections and the same
   correspondence immediately before publication. A mismatch after preparation
   releases every provisional receipt and publishes no candidate membership or
   candidate-derived closure evidence. While the runtime remains accepting and
   the logical revision remains current, complete the required physical-refresh
   publication under the parent gate: retain membership, bind the current
   physical-composition epoch and projections, and invalidate closure to
   `NotEvaluated`. Then return `Failed(RealizationChanged)` carrying that
   refreshed current snapshot. Runtime loss returns `Unavailable`; logical
   state movement follows the ordinary stale-completion result.
2. When no expansion scopes are registered, classify every dependency as
   intentionally outside the boundary. Publish a fresh `ClosedBoundary`
   closure observation under the unchanged logical revision when that evidence
   changed, retaining any producer-bound markers without interpreting them as
   eligible candidates. Perform no source resolution, Root preparation,
   acquisition, or receipt work; changed closure evidence still uses the
   receipt-free parent publication plan and sealed Scope participant.
3. Otherwise classify each relationship row as already admitted, eligible
   under one or more registered scopes, outside the boundary, or unsupported
   because it lacks an owner-issued acquisition coordinate.
4. Coalesce eligible rows carrying the same exact owner-issued acquisition
   coordinate into one candidate before capacity selection. The first row's
   producer order locates the candidate, while every relationship row remains
   attached as exact evidence.
5. In producer order, select unique eligible candidates until the effective
   remaining Root capacity is exhausted. Classify every relationship attached
   to a later candidate as `CapacityDeclined`; no declined candidate enters
   source payload preparation or Artifact preparation.
6. Ask adjacent source and artifact owners to prepare every selected exact
   candidate under their current authorization and budgets.
7. Atomically append every successfully prepared distinct candidate.
8. Publish exact closure evidence for every outside, unsupported,
   capacity-declined, rejected, or failed relationship and every producer-issued
   candidate- or depth-bound marker.

Unlike explicit Add or Replace, expansion may commit a bounded subset because
its input is a sequence of independently discovered optional boundary
candidates.
The post-operation unevaluated frontier is every resulting occurrence that is
currently `Pending` or `Failed` or was newly admitted, in owner order. The
resulting closure observation uses `NotEvaluated` when all selected candidates
settled and that frontier is non-empty. It retains the exact evaluated
Ready-generation coverage and same-operation outside-boundary evidence.
Because the batch covered every prior Ready Root and explicitly retains every
non-Ready Root, no earlier frontier is silently discarded.

The observation uses `Incomplete` when any potentially eligible candidate was
unsupported, capacity-declined, producer-bounded, rejected, or failed. That
state retains the same non-Ready/newly-admitted frontier in addition to the
exact incomplete evidence.

`CompleteForObservedEvidence` is published only when membership does not change
and the exact complete current Ready Root coverage has no non-Ready frontier
after every eligible candidate was already admitted or otherwise settled and
the producer reported no candidate- or depth-bound marker. Deliberately
declined outside-boundary evidence remains visible in that complete state. When
membership does not change but closure evidence changes, the operation
publishes a fresh closure observation under the unchanged logical revision so
the evaluated boundary is durable. Only identical membership and identical
closure observation produce `NoEffect`.

One expansion batch never recursively follows dependencies discovered during
its own preparation. A later query against the committed revision may produce
the next depth's evidence. The exact depth carried by that evidence must remain
within the operation limit.

## Dependency-evidence boundary

Inspection queries cannot mutate Workspace scope. A query runs against one
exact revision and may return:

```text
ExternalDependencyEvidenceBatch
  SourceRevision
  SourceClosureObservation
  EvaluatedRoots          ordered WorkspaceEvaluatedRoot sequence
  ProducerEvidenceIdentity
  Relationships           ordered ExternalDependencyRelationship sequence
  BoundEvidence           ordered ExternalDependencyBoundEvidence sequence

ExternalDependencyRelationship
  ProducerOrder
  Exact dependency identity
  Optional owner-issued acquisition coordinate
  Boundary relationship
  Depth

ExternalDependencyBoundEvidence
  = CandidateBoundReached(resource-free producer evidence)
  | DepthBoundReached(resource-free producer frontier evidence)
```

The query owner defines the relationship and dependency identity. This owner
uses only the exact evaluated-Root coverage, acquisition coordinate, and typed
relationship evidence it is given. The coordinate owner must also define exact
candidate-correspondence equality before expansion capacity is assigned.
Evidence whose coordinate cannot support that comparison is unsupported for
owner-policy expansion; the scope owner does not prepare speculative
candidates and deduplicate them after capacity selection.

`ProducerEvidenceIdentity` is an opaque identity for one exact producer
evaluation. It binds that issuance's relationships and bound evidence against
accidental cross-batch mixing, but it is not consumed authority and cannot by
itself reject replay. Workspace, revision, closure, Root-generation, and
temporal currentness use the explicit source revision, source closure
observation, and exact evaluated-Root coverage. A retry after a committed
closure change is stale through those currencies. A retry after `NoEffect` is
permitted and returns the same state-based `NoEffect` when those currencies and
the complete result remain current.

`BoundEvidence` records valid producer truncation within the operation
envelope. It is empty only when the producer reports no candidate or depth
boundary. In a selectively open scope, a bound marker is durable incomplete
evidence. In a closed scope, it remains visible in `ClosedBoundary` without
overriding closed precedence, because no observed or omitted candidate is
eligible for acquisition. A bound marker is never permission to invent an
omitted relationship or acquisition coordinate. A batch whose materialized
rows or declared depths exceed the envelope is rejected as malformed instead
of being converted into a bound marker.

The host or query coordinator is the operation driver. After an explicit query
and any required adjacent-owner adapter produce one complete
`ExternalDependencyEvidenceBatch`, the Browser or CLI may submit
`ExpandDependencies` and then rerun the query against the resulting revision.
This owner never self-schedules expansion, repeats a query, or expands merely
because a scope is registered. The follow-up query covers the complete current
Ready Root sequence again; the unevaluated frontier is visible disclosure, not
authority to submit a frontier-only evidence batch.

A raw unresolved `AssemblyRef` name is not a package coordinate. This owner
must not search a package catalog by assembly name, concatenate a prefix, or
guess package ownership. Exact-package and package-prefix policy matching
requires package-owner-issued canonical package identity; admission additionally
requires the exact owner-issued acquisition coordinate used for candidate
correspondence and capacity.

The landed [Package Dependency Evidence
Query](package-dependency-evidence.md) supplies canonical package identity and
version-constraint declarations but deliberately does not claim a resolved
dependency version for package manifests. That result is not directly a
submit-ready expansion batch.
[Package Dependency Candidate Resolution](package-dependency-candidate-resolution.md),
tracked by [#5765](https://github.com/richlander/dotnet-inspect/issues/5765),
owns the focused host-neutral composition from eligible declaration evidence
through package-source authorization and version-constraint resolution to an
owner-issued exact acquisition candidate. Until that adapter lands, a
package-manifest relationship without such a coordinate is `unsupported` and
remains visible as incomplete evidence; a host must not reinterpret an omitted
version as latest or resolve the constraint through an ad hoc path.

Closed evaluation and expansion-scope registration invoke no #5765 work. For a
selectively open Workspace, the coordinator may ask the adapter only about
dependency identities that match an exact-package or package-prefix scope. The
adapter preserves producer order and returns exact candidate, failure, or
incomplete evidence; Scope still performs exact-coordinate coalescing and Root
capacity assignment before Artifact preparation. This finite, candidate-bounded
source-resolution work necessarily precedes exact-coordinate coalescing and may
therefore run for a candidate later classified `CapacityDeclined`; that
classification forbids later payload and Artifact preparation, not the
resolution evidence required to identify and coalesce the candidate.
Restored-project evidence that already names an exact resolved coordinate
passes through the same candidate-result contract without redundant range
resolution.

Package sets are explicit Root-request producers, not dependency-expansion
scopes. An ecosystem-pack package-set selection returns only `PackageSetId`;
issue #5720 and the Package Set Registry own membership, #5602 owns typed
declaration and normalization, and source owners resolve every member before
the front end lowers exact requests to `ReplaceScope` or `AddRoots`. This
design defines no pack-to-scope identity, floating-descriptor-to-exact-
dependency membership relation, or implicit expansion policy.

Package-prefix query remains a different operation:

- an ecosystem-pack prefix action selects only owner-issued source-query
  intent;
- a prefix query enumerates packages matching that source-owned intent;
- an expansion scope authorizes exact dependency candidates already carrying
  package correspondence; and
- opening or adding selected query results is an explicit `ReplaceScope` or
  `AddRoots` request.

Selecting or executing a recorded prefix action never registers a Workspace
expansion scope. The editor may separately register a package-prefix expansion
scope carrying validated prefix value, but that action neither runs the source
query nor adds its results.

## Artifact realization handoff

This owner prepares no bytes and constructs no binding context. It consumes the
[Artifact Root preparation and scope publication](artifact-acquisition-and-workspaces.md#artifact-root-preparation-and-scope-publication)
contract for every logical publication:

1. Validate the complete logical operation, current revision, expansion
   evidence, Root capacity, and resulting resource-free candidate before
   adjacent preparation.
2. Read the current owner-issued
   `ArtifactRootCompositionGenerationIdentity`. Refresh every retained Root
   projection and use only owner-validated current generation references.
3. Ask source and Artifact Acquisition owners to prepare exact unmatched Root
   candidates. Required Add or Replace candidates form one logical
   all-or-failure set that may use one or more preparation receipts so
   post-realization duplicate correspondence can release independently; any
   required failure releases all successful receipts and prevents publication.
   Expansion instead prepares independently optional candidates and retains
   only the successful receipts while recording exact typed failure evidence.
4. Construct one complete `ArtifactRootPublicationPlan`: retain every desired
   current physical Root by correspondence and generation, adopt every entry
   from every listed successful receipt exactly once, omit physical Roots to be
   retired, and carry the exact Workspace, composition generation, deadline,
   cancellation, and ordered desired physical set.
5. Supply one sealed `ArtifactRootScopePublicationParticipant` containing the
   exact current `WorkspaceScopePublicationBaseIdentity`, operation identity,
   prevalidated resource-free candidate revision and closure, result facts,
   optional requested occurrence, and one fresh non-reused next Scope
   publication base. Its side-effect-free `PrepareCommit` revalidates those
   Scope-owned facts under the shared runtime gate, consumes the parent's exact
   unpublished candidate physical-composition identity and projected Roots,
   constructs the complete immutable candidate snapshot and operation result,
   and returns only the parent's private no-fail current-state pointer-swap
   token. Refusal discards the candidate Scope base without reuse.
6. Invoke `PublishArtifactRootComposition`. Success returns the fresh physical-
   composition identity and exact point-in-time Root projections while the
   participant token publishes the logical state in the same non-yielding
   region. Refusal preserves both prior current states and releases every
   listed prepared batch according to the parent contract.

Remove, Clear, expansion-policy edits, and closure-only publication use the
same operation with no preparation receipts and a complete retained or empty
physical set. Corresponding physical re-realization remains Artifact-owned and
advances the same composition identity without changing logical occurrence
identity.

Artifact Acquisition defines correspondence construction, exact request
matching, projection status, generation-reference issuance and currentness,
preparation receipt lifetime, complete physical-plan validation, query
admission, retirement, and resource erasure. This owner never adopts a receipt
directly, swaps physical state, or redefines release and retry outcomes.

Artifact Acquisition remains authoritative for:

- whether a source is available or authorized;
- package version, framework, and runtime selection;
- all-or-failure assembly-context construction;
- admission and retained-resource charging;
- physical generation replacement and quiescence; and
- query access to admitted participants.

Logical revision publication never mutates a sealed artifact generation or
rebinds an existing assembly context in place.

## Navigation and restoration boundaries

The scope result's optional requested occurrence is Navigation's requested
active/replacement occurrence input to
[Inspection Subject Navigation](inspection-subject-navigation.md). It is not a
focus command. Navigation owns subject recommendation, reconciliation,
retained intent, and active-snapshot publication.

[Workspace Definitions](workspace-definitions.md) owns portable schema,
projection, and complete restoration. An ordinary `ReplaceScope` publishes
Scope state and therefore cannot act as the uncommitted Scope fragment required
by that owner's prepare-and-commit protocol. The focused contract below,
tracked by [#6190](https://github.com/richlander/dotnet-inspect/issues/6190),
defines Scope's contribution toward
[#5525](https://github.com/richlander/dotnet-inspect/issues/5525).
It is not implemented or model-checked. Canonical demo, share, import,
saved-definition, and history restoration through this Scope remains
unsupported; those inputs cannot be approximated by invoking `ReplaceScope`
before or after the other participants.

An ordinary **Open** that intentionally replaces only Scope and resets rather
than restores Navigation, view, query, and history state may use
`ReplaceScope`. Workspace Definitions and the Browser owners retain the
portable and presentation semantics; the future Scope participant must not
transfer those semantics here or become a generalized transaction framework.

After the complete-restoration participant lands, Browser Back/Forward may
restore prior committed data into a new current revision. It does not
reactivate the old runtime revision identity or make several Workspaces live
simultaneously.

### Uncommitted Scope restoration fragment

The claim is limited to this owner:

> Prepare one complete candidate Scope with exact occurrence identities for
> adjacent preparation, without making it current; install exactly that Scope
> contribution only within the complete restoration commit, or publish none of
> it.

The immediate consumer is the Definitions coordinator, which supplies candidate
occurrences to Navigation's
[canonical restoration participant](inspection-subject-navigation.md#canonical-restoration-participant).
Definitions owns the complete request and result. Navigation owns the attempt
token, intent ordering, prepared subject/lens snapshot, and effect authority.
Artifact Acquisition owns candidate physical facts, provisional inspection,
publication, and resource lifetime. This section neither creates a second
coordinator nor changes those contracts.

The first runtime profile remains exact-package, closed Scope with at most 64
distinct Roots. A restoration must supply the complete Root and expansion-policy
intent; unsupported non-package Roots or nonempty expansion registrations fail
visibly rather than being dropped. It does not reapply fresh-Workspace defaults.
Wider Scope profiles require their own implementation and evidence.

#### Complete candidate and exact association

Scope consumes one exact accepting runtime Workspace, the expected current
Scope revision, a finite deadline, cancellation, and the complete owner-resolved
replacement request. It carries the coordinator's opaque Navigation-issued
attempt token unchanged. That token correlates the participant with the complete
attempt; Scope neither issues another intent token nor interprets its ordering.

The resource-free fragment identifies one complete candidate revision and its
ordered Root occurrences, descriptors, closed policy, and logical limits. It
also preserves the association between each requested Root and its exact
candidate occurrence. Request reduction and retention use the existing
[Replace scope](#replace-scope) correspondence rules, not portable coordinate
text, display names, or list positions. Multiple reduced requests may map to
one occurrence; every request must still have its exact mapping.

An exactly corresponding current occurrence keeps its identity. Each unmatched
Root gets a fresh Scope-issued occurrence identity. The candidate revision is
new even when its logical contents equal an earlier revision; successful
restoration never reactivates a historical revision. Navigation can bind its
private prepared state to those candidate occurrences. The committed revision
must use those same identities, not freshly mint equal-looking replacements
after Navigation has prepared.

Candidate facts carry their exact attempt association and expected Scope and
Artifact publication bases. Equality proves correspondence within that
preparation, not current membership. The fragment is not a
`WorkspaceScopeSnapshot` returned by a current-state read, and its occurrence
identities grant neither ordinary query admission nor activation authority.
Any inspection needed before publication must use Artifact-owned provisional
access, not temporarily install a Root to make current queries work.

Provisional bindings, receipts, contexts, leases, and reservations remain in
private preparation authority under their existing owners. They are not
retained by the resource-free fragment, historical snapshots, Navigation state,
or portable projections. Holding an abandoned or terminal fragment cannot
prolong physical preparation; the existing finite deadline and release
contracts remain applicable.

#### Admission, invalidation, and publication

A restoration preparation is a complete replacement for Scope mutation
admission. Common validation and the full logical request validation precede
admission or supersession. A valid current restoration may supersede an earlier
preparation; an invalid, stale, or foreign request may not. Ordinary Add/Remove
remain Busy while it prepares, and valid Replace/Clear can supersede it.
This does not give Navigation-local activation authority to undo a committed
Scope effect; that separate consumption boundary remains #5584.

Preparation does not publish a new current Scope snapshot, including an
ordinary `Preparing` snapshot. Its progress and exact cancellation action
belong to the unpublished participant outcome. This differs from ordinary
Scope progress because Definitions requires preparation to leave the complete
installed state unchanged. Current membership and revision remain unchanged by
the attempt until complete commit. The shared admission slot is not a second
current Workspace or a second intent scheduler.

Before contributing to commit, Scope must still be preparing that exact
candidate against its unchanged expected Scope publication base and
Artifact-owned physical basis. A newer Scope publication, physical movement,
supersession, cancellation, expiry, or runtime unavailability prevents stale
publication under the applicable owner contract. A candidate cannot silently
refresh its bases, replace its occurrences, or reacquire material while keeping
the earlier ready fragment; that would invalidate the other participants'
association with it.

Scope readiness is necessary, not sufficient, for complete commit. Its final
contribution must pair its exact candidate revision and occurrence sequence
with the Artifact owner's exact candidate composition and projected Roots.
The complete snapshot, initial closure observation, and fresh publication base
must describe that same association. The contribution cannot independently
make the candidate current while Navigation, queries, or canonical projection
can still refuse the complete attempt.

A non-success or abandoned preparation releases its provisional authority and
publishes none of its candidate. If another valid operation has since changed
Scope, settlement leaves that newer state current; it must not restore the
attempt's cached old snapshot. Scope preserves exact owner failure evidence
for Definitions rather than manufacturing an empty successful fragment.
Definitions alone classifies the complete restoration result. After complete
publication becomes irrevocable, late cancellation cannot retract Scope's
committed contribution.

#### Physical prerequisite and evidence boundary

The existing Artifact publication protocol is a useful comparison, not a
complete-restoration implementation. It stages physical Roots privately and
accepts a sealed Scope-only no-fail pointer swap. Its current operation does
not return a privately inspectable candidate for arbitrary later participant
preparation, and its token cannot implicitly become a multi-owner commit hook.
[#6189](https://github.com/richlander/dotnet-inspect/issues/6189) owns the
required Artifact design and implementation. This section does not choose its
staging, query-access, locking, or complete-publication mechanism.

Likewise, the existing Scope revision model checks ordinary Scope/Artifact
publication, and Definitions'
[restoration model](models/workspace-definitions-restoration/README.md) checks
its abstract coordinator. Neither proves their composition with candidate
occurrences. Before implementing this participant, compose the resolved
owner-issued behaviors through named model instances, preserving the live
attempt/candidate/occurrence and publication-base associations. Recheck imported
properties in that composition; do not manufacture model-local equivalents of
owner-issued publication or Navigation authority.

The new interaction and implementation claims are **unverified**. Required
future gates, tracked by
[#6194](https://github.com/richlander/dotnet-inspect/issues/6194), are
deliberately limited to the participant's observable outcomes:

| Claim | Required evidence |
| --- | --- |
| Candidate occurrence identity survives complete installation | Composed model plus a Release case preparing Navigation under a new occurrence and observing that exact occurrence after commit |
| Preparation and later participant refusal publish no candidate Scope | Composed model plus a Release case failing after physical and Scope preparation, with prior membership retained and provisional resources released even while the fragment is retained |
| An obsolete candidate cannot replace newer current Scope | Composed model plus Release cases for valid Replace/Clear, physical movement, cancellation, deadline, and close during preparation |
| Complete empty replacement is not an early Clear | Release case retaining nonempty current Scope until the complete empty restoration commits |

These are not additional gates on today's ordinary Add/Remove/Replace/Clear.
Model-checking precedes runtime implementation; runtime delivery must include
the real coordinator/host adoption path, not an independently unused participant.

#### Mock restoration and delivery

```text
Current Scope: JSON occurrence A
Definition: JSON, NETStandard; inspect a descendant under NETStandard

Prepared Scope: A, new occurrence B       Current Scope: still A
Navigation prepares its exact view under B

Required participant refuses            Current Scope: still A
  or complete restoration commits       Current Scope: A,B; view still names B
```

An empty definition is the neighboring case: its candidate is empty, but the
current nonempty Scope is not cleared until complete restoration succeeds.
Neither path saves editor state, selects a successor, or writes browser history
through this participant.

The production hosts are Browser saved/share/history restoration
(#5511/#5697) and CLI canonical replay (#4647). The immediate coordinator is
Definitions #5525 and the Navigation fragment is #6112. The counted adoption plan in
[#6190](https://github.com/richlander/dotnet-inspect/issues/6190), linked from
overall tracker #5865, expands the previously grouped restoration milestone:
six landed milestones, then eight remaining milestones for this contract,
Artifact support, Scope model/runtime, Navigation support, Definitions
composition, Browser adoption, CLI replay, and migrated Browser retirement.
The total is fourteen delivery milestones, not fourteen mandatory PRs.
Independent owners may work in parallel; CLI replay is not a prerequisite for
Browser delivery. The shared runtime must stay within the existing near-term
consumer lead bound. No host is replaced until its corresponding adoption is
complete.

This fragment adds no rendering path. Hosts retain their existing typed
result-to-Markout or interactive Browser presentation boundaries. The separate
CLI packet/full-URL idea #6150 does not change this contract.

## Concurrency model

Before implementation, a focused TLA+ model under
`docs/design/models/workspace-scope-revisions/` must check:

- one current revision per accepting runtime Workspace;
- one current closure observation over that revision and its evaluated
  physical bindings;
- one fresh process-lifetime non-reused Scope publication base per current
  snapshot pointer swap;
- complete resource-free typed Root descriptors for every committed occurrence;
- at most one preparing mutation;
- Replace Scope and Clear supersession;
- stale and foreign completion rejection;
- physical re-realization invalidates closure without changing logical
  occurrence identity;
- stale realization-covered evidence cannot publish;
- retired revisions and snapshots retain no physical artifact resource;
- no provisional-resource leak after failure or supersession;
- cancellation and deadline settlement without publication;
- parent commit wins once its final non-yielding region begins;
- no partial explicit Add or Replace Scope publication;
- atomic bounded expansion publication with visible incomplete evidence;
- no complete closure while a current non-Ready or newly admitted Root remains
  unevaluated;
- occurrence retention only through exact correspondence;
- no operation after runtime close; and
- eventual settlement of every admitted operation under fair adjacent-owner
  completion or the finite preparation deadline.

The model instantiates the owner-issued `PublishArtifactRootComposition`
transition and its abstract preparation-set, physical-composition-generation,
participant-refusal, and no-fail commit outcomes. It does not copy Artifact
Acquisition's receipt, generation, budget, query-admission, retirement, or
lifetime state machines.

Model configurations and exact expected outcomes must enter
`eng/tla-expected-exit-codes.txt` before implementation claims these
properties.

The [revision/publication composition model](models/workspace-scope-revisions/README.md)
provides the focused #5796 bounded evidence and its exact-outcome gates. It
instantiates Artifact Acquisition's publication lifecycle over live shared
currencies rather than copying physical publication. This is design evidence,
not Release implementation conformance; the broader expansion, resource-erasure,
and host gates below remain unverified. The immediate implementation consumer
is [#5821](https://github.com/richlander/dotnet-inspect/issues/5821), initial
snapshot and exact Replace/Clear with CLI snapshot adoption. Browser adoption
follows its Add/Remove and complete-restoration prerequisites.

## Pathological cases

The implementation must demonstrate:

| Case | Required result |
| --- | --- |
| Add the resolved current Microsoft.Extensions package set to an empty Workspace | One complete revision containing every current set member under the 64-Root logical profile and one atomically published parent preparation set |
| Render the committed package and platform inventory after preparation resources release | Each occurrence retains a resource-free typed Root descriptor with its Package/non-package kind and exact owner-issued coordinate facts |
| One required package in that set fails realization | No new revision; the prior scope remains current with the exact package failure |
| Add a pinned exact package already present | No acquisition; `NoEffect` returns the existing exact occurrence |
| Add a floating request that resolves to a package already present | Realization runs, exact correspondence returns `NoEffect`, and redundant provisional resources release |
| Add two equal exact requests in one batch | The first request determines order; one occurrence is appended and no duplicate preparation runs |
| Add two unresolved requests that realize to equal correspondence | The first realized request determines order; one occurrence is appended and the redundant provisional resource releases |
| Slow Add completes after Clear | Clear remains current; the Add is superseded and releases its provisional resources |
| User cancels a slow Add | The Add returns `Cancelled`, the prior revision remains current, and provisional resources release |
| Cancellation arrives after the parent final commit starts | Publication wins; the complete committed logical and physical result returns rather than a false `Cancelled` result |
| Prefix scope matches text but evidence is only an `AssemblyRef` | No expansion; the exact unsupported boundary remains visible |
| Prefix scope matches a package-manifest declaration before #5765 supplies an exact candidate | No ad hoc range-to-latest conversion; the relationship remains visible as unsupported incomplete evidence |
| One successful expansion appends a Root while another dependency is outside the registered scopes | `NotEvaluated` retains the new-Root frontier and the same-operation outside-boundary evidence |
| Two dependency relationships name the same exact acquisition coordinate with one remaining Root slot | One candidate is prepared and admitted; both relationships settle against it and neither becomes `CapacityDeclined` |
| Four eligible dependencies realize and one fails | One atomic revision appends the three successful Roots and records the failure plus those new Roots as an unevaluated frontier |
| Eligible expansion candidates exceed remaining Root capacity | Producer order selects the candidates attempted; every later candidate is visible as `CapacityDeclined` |
| A retained Root is physically re-realized while expansion candidates prepare | Candidate receipts release; no candidate membership publishes; the required physical-refresh publication invalidates closure before `Failed(RealizationChanged)` returns its current snapshot |
| Open an unrelated package after a prefix scope was registered | One `ReplaceScope` atomically installs the new Root with an empty expansion-scope set and a closed initial observation; the old prefix cannot authorize acquisition |
| Scope-only Open supplies Roots and typed expansion scopes | One `ReplaceScope` publishes both sequences or neither; no prior policy leaks into the replacement revision |
| Open one fully pinned exact Root already present | `ReplaceScope` retains its exact occurrence without source or artifact preparation |
| All eligible dependencies are already admitted and two are outside the scopes | A closure-only observation is complete for the exact current Ready Root coverage and retains the two declined boundaries |
| A closure-only publication is retried with an equivalent new participant | The prior Scope publication base is stale even though membership revision is unchanged; no second logical or physical publication occurs |
| A delayed participant waits through several later Scope publications | Every pointer swap issues a fresh non-reused publication base, so the old participant cannot become current again through ABA |
| Closed Workspace evaluates external dependencies | A closure-only `ClosedBoundary` observation retains the observed outside-boundary evidence, performs no source or preparation work, and publishes through the receipt-free parent gate |
| Closed Workspace receives producer-bound dependency evidence | `ClosedBoundary` retains the typed bound marker, performs no source or preparation work, and publishes through the receipt-free parent gate |
| Dependency evidence covers only one of several current Ready Roots | `Rejected(EvidenceMismatch)`; the current unevaluated frontier remains visible |
| A Root is re-realized after complete closure and before old evidence is submitted | The logical occurrence remains, closure becomes not evaluated for the replacement realization reference, and the old batch is rejected without publication |
| One Root is Ready and another is Pending during expansion | Ready coverage is retained, the Pending occurrence remains in the unevaluated frontier, and closure cannot become complete |
| Remove a package-prefix scope after prior expansion | Future matching stops; admitted Roots remain |
| Register an expansion scope already present | `NoEffect` preserves the current closure observation |
| Remove an expansion-scope value that is absent | `NoEffect` preserves the current revision and closure observation |
| Reach 64 Roots and attempt one more Add | Typed capacity rejection; no eviction or membership change |
| Remove the active occurrence | Scope commits removal; Navigation independently selects Workspace unless it has exact authorized retained state |
| Artifact Acquisition retires a retained occurrence before replacement settles | `Pending` or `Failed` keeps logical identity but projects no current realization reference and invalidates prior closure evidence |
| Artifact runtime closes while Add is preparing | Parent publication refuses and releases preparation; Scope returns `Unavailable` with at most the last resource-free snapshot as historical evidence |
| A committed occurrence has no corresponding current Artifact projection | Typed invariant or stale-composition failure; the occurrence is neither removed nor replaced by an empty or fabricated status |
| History retains many removed revisions after repeated package Open and Clear | Retained revisions hold only resource-free correspondence values; retired package bytes and contexts drain under Artifact Acquisition |
| A stale-base Remove arrives while Add is preparing | `Rejected(RevisionMismatch)` wins before mutation admission can return `Busy` |
| A malformed Replace arrives while Add is preparing | The exact validation rejection wins; the valid Add is not superseded |
| A producer reaches its candidate or depth bound within the envelope | The valid marker is retained as `Incomplete`; omitted candidates invoke no adjacent owner |
| A batch exceeds its structural candidate or depth envelope | Typed rejection occurs before `Busy`, supersession, or adjacent-owner work |

## Required gates

The host-neutral Release suite is `WorkspaceScopeTests`. Tests may use
controlled adjacent-owner receipts but must not manufacture package-resolution
or artifact evidence later claimed by those owners.

| Gate | Property |
| --- | --- |
| `InitialSnapshot_IsEmptyClosedAndBoundToExactWorkspace` | One exact runtime Workspace starts with one empty revision and closed observation, and no portable or display identity aliases it. |
| `LogicalRevision_IsCompleteImmutableAndDistinct` | Every logical membership or expansion-policy publication returns one immutable complete revision with a fresh revision identity. |
| `ScopePublicationBase_IsFreshDistinctAndNonReusable` | Initial state and every current-snapshot pointer swap issue one fresh process-lifetime non-reused base; refused candidate bases never become current or reusable. |
| `ClosureObservation_IsExactAndDistinct` | Every closure publication or invalidation carries a fresh identity, exact source revision, and exact evaluated Artifact Root generation references. |
| `RetiredScopeState_RetainsNoArtifactResources` | Revisions, snapshots, closure observations, and operation results retain no Root realization, package content, binding, context, lease, or provisional receipt. |
| `OccurrenceDescriptor_PreservesTypedRootFactsWithoutResources` | Every occurrence retains its Package/non-package discriminator and exact adjacent-owner coordinate descriptor without retaining a physical artifact resource. |
| `OccurrenceIdentity_IsRetainedOnlyByExactCorrespondence` | Retained Roots keep exact occurrence identity; equal display coordinates or a later re-add do not recreate it. |
| `ReplaceScope_IsAllOrFailureAndCoalescesExactCorrespondence` | Complete success atomically replaces Roots and expansion scopes, exact post-realization duplicates retain one first-ordered occurrence, redundant receipts release, and any required input failure leaves the prior revision and policy current. |
| `ReplaceScope_ExactCurrentRootRequiresNoPreparation` | A fully pinned exact request matching current logical correspondence retains that occurrence without source or artifact work and maps activation intent to it. |
| `OpenWithNoExpansionPolicy_DoesNotInheritPriorScopes` | Ordinary package Open supplies an empty policy and cannot retain a prefix or set from the prior scope. |
| `AddRoots_IsAllOrFailureAndPreservesOrder` | New Roots append atomically in request order while existing occurrences retain identity. |
| `AddPinnedExistingRoot_ReturnsExactOccurrenceWithoutPreparation` | A fully pinned exact duplicate Add performs no adjacent-owner work and returns the existing occurrence when activation was requested. |
| `AddResolvedExistingRoot_ReleasesRedundantPreparation` | An unresolved request may realize to an existing exact Root, return its occurrence, and release the redundant provisional receipt. |
| `AddDuplicateRequests_CoalesceBeforePublication` | Equal exact requests reduce before preparation, and unresolved requests that realize to equal correspondence retain one first-ordered occurrence while redundant resources release. |
| `AddMixedExistingAndNewRoots_CommitsOnlyNewRoots` | Per-Root duplicate classification does not turn a mixed batch into operation-level `NoEffect`; every new Root commits atomically. |
| `RemoveRoot_RequiresExactCurrentOccurrence` | Foreign, absent, or retired occurrences cannot remove a Root. |
| `Clear_SupersedesPreparationAndCommitsEmptyClosedRevision` | Clear needs no Root preparation, becomes current through the parent receipt-free publication gate, and prevents stale preparation from publishing. |
| `OrdinaryMutation_WhilePreparingReturnsBusy` | A second non-superseding mutation is visibly refused rather than queued or raced. |
| `ValidationPrecedesBusyOrSupersession` | Structural, envelope, deadline, Workspace, revision, evidence, and operation-specific validation returns its exact rejection before `Busy` or Replace/Clear supersession. |
| `CancellationOrDeadline_RespectsParentCommitLinearization` | Cancellation or finite deadline expiry observed before the parent final recheck releases preparation and preserves the prior revision; after final commit begins, publication wins and returns Committed. |
| `StaleCompletion_ReleasesReceiptWithoutPublication` | Revision movement prevents parent publication and releases every provisional resource. |
| `ExpansionScopeVocabulary_IsClosedTypedAndDeduplicated` | Only the three version-1 typed arms register, and equal scopes retain one first-ordered value. |
| `DuplicateExpansionScopeRegistration_PreservesClosure` | Equal registration returns `NoEffect` without discarding evaluated closure evidence. |
| `RemoveExpansionScope_IsValueBasedAndIdempotent` | Removal uses typed scope-value equality; an absent value returns `NoEffect`, and no Workspace-bound registration identity exists. |
| `EmptyExpansionScopes_MeanClosedBoundary` | Closed is derived from the empty scope set and causes no acquisition. |
| `ExpansionScopeRegistration_PerformsNoDiscoveryOrAcquisition` | Registration only changes logical eligibility. |
| `ClosedExpansion_EvidenceRemainsClosedAndPerformsNoPreparation` | An empty scope set has precedence, records observed outside-boundary and producer-bound evidence, performs no source, acquisition, or receipt work, and uses the receipt-free parent gate only when closure changes. |
| `PackageExpansion_RequiresOwnerIssuedIdentityAndCandidate` | Package scopes never match assembly names, labels, or uncorrelated references; policy uses owner-issued package identity and admission requires an exact owner-issued candidate. |
| `PackageManifestConstraint_RequiresOwnerIssuedCandidate` | Declaration evidence without #5765's exact source-authorized candidate remains visible as unsupported; Scope and hosts never reinterpret a version range as latest. |
| `Expansion_CommitsSuccessfulCandidatesWithExactIncompleteEvidence` | Successfully prepared selected candidates publish atomically; every unsupported, capacity-declined, rejected, or failed relationship and every producer-bound marker remains typed and visible. |
| `Expansion_AddingRootsLeavesUnevaluatedFrontier` | A revision containing newly expanded Roots cannot claim those Roots were already evaluated. |
| `Expansion_AddingRootsRetainsCurrentBoundaryEvidence` | A Root-adding expansion retains exact prior Ready-generation coverage, its new-Root frontier, and same-operation outside-boundary evidence in one `NotEvaluated` state. |
| `Expansion_NonReadyRootsRemainUnevaluated` | Evaluating every Ready Root retains each current Pending/Failed occurrence as an unevaluated frontier and cannot publish complete closure. |
| `Expansion_AllEligibleCandidatesSettled_IsCompleteForObservedEvidence` | With unchanged membership, no non-Ready frontier, and exact complete current Ready Root coverage, outside-boundary declines remain visible while closure is complete when every eligible candidate settled. |
| `Expansion_RejectsIncompleteOrStaleRootCoverage` | Omitted, reordered, non-Ready, extra, or physically replaced realization coverage cannot erase a frontier or publish closure. |
| `Expansion_RealizationChangeBeforePublicationRefreshesCurrentSnapshot` | A physical-binding change after admission releases candidate receipts and publishes no candidate membership or evidence; the required physical refresh invalidates closure before `Failed(RealizationChanged)` returns a current snapshot. |
| `Expansion_CoalescesExactCandidatesBeforeCapacity` | Relationship rows naming one exact acquisition coordinate consume one Root slot and retain all relationship evidence. |
| `ExpansionCapacity_UsesProducerOrderAndPreparesNoDeclinedCandidate` | Remaining Root slots are assigned in deterministic producer order; finite pre-capacity candidate resolution may already have occurred, but capacity-declined candidates enter no source payload or Artifact preparation. |
| `EffectiveOperationLimits_CannotExceedWorkspaceProfile` | Each effective dimension is the stricter finite Workspace-profile or operation-envelope value. |
| `ExpansionStructuralLimits_RejectBeforePreparation` | Materialized relationships or declared depths outside effective limits are malformed and cannot invoke adjacent owners. |
| `ExpansionProducerBounds_RemainTypedEvidence` | Valid candidate- and depth-bound markers remain durable `Incomplete` evidence in selectively open scope, remain typed `ClosedBoundary` evidence in closed scope, and never authorize work for omitted candidates. |
| `ProducerEvidenceIdentity_BindsIssuanceButNotFreshness` | Producer identity prevents cross-batch mixing but is not consumed replay authority; revision, closure, and exact Root-generation coverage establish freshness. |
| `ExpansionRetry_IsStateIdempotent` | A retry after committed closure movement is stale, while an unchanged current batch after `NoEffect` may repeat only the same state-based `NoEffect`. |
| `ProductProfile_AdmitsRegisteredMicrosoftExtensionsWithoutEviction` | The resolved current package-set membership fits the 64-Root profile and no existing Root is evicted. |
| `RootCapacity_RejectionPreservesCurrentRevision` | A sixty-fifth distinct Root fails visibly without truncation or replacement. |
| `RuntimeClose_RejectsNewScopeOperations` | Scope authority cannot outlive the artifact owner's runtime Workspace lifetime. |
| `RuntimeUnavailable_DoesNotFabricateCurrentScope` | Absent, closing, or closed Artifact runtime state rejects current refresh or mutation and never becomes an empty or success-shaped Workspace result. |
| `ScopePublication_UsesArtifactRootPublicationPlan` | Membership, policy, and closure evaluation publication supplies one complete parent-owned physical plan and sealed Scope participant carrying exact current and fresh candidate Scope bases; the parent gate changes both current states or neither. Observation of an already-published physical epoch instead uses the complete Scope-only refresh under the Artifact read lease and does not submit a physical plan. |
| `EveryOperationResultCarriesCompleteCurrentSnapshot` | Committed, no-effect, rejected, failed, cancelled, and superseded results require no host reconstruction; Unavailable is explicitly historical and carries no current authority. |
| `CurrentSnapshot_BindsOnePhysicalCompositionEpoch` | One returned current snapshot carries the owner-issued composition identity and complete Root projections from that epoch; physical movement causes complete refresh or typed refusal, never a mixed-epoch view. |
| `OwnerPolicyExpansion_CarriesNoActivationAuthority` | Dependency following cannot move Navigation focus. |
| `OccurrenceSnapshot_ProjectsExactOwnerRealizationStatus` | One `ArtifactRootScopeProjection` carries the adjacent owner's point-in-time `Ready`, `Pending`, or `Failed` status without admitting an unprepared Root. |
| `OccurrenceSnapshot_RefreshesPhysicalGenerationWithoutLogicalMutation` | A corresponding re-realization refreshes the projection, invalidates closure when generation coverage changes, and preserves logical revision and occurrence identity. |
| `PreparationSnapshot_ExposesProgressWithoutUncommittedOccurrence` | A preparing operation is observable and cancellable without presenting any requested Root as a member before publication. |

The current package-only substrate remains evidence for this target:
`PackageOccurrence_IsExactPerIssuanceAndCarriesBinding`,
`PackageOccurrence_DistinguishesWorkspaceAndBindingGeneration`,
`NonPackageOccurrence_IsExactAndWorkspaceScoped`,
`PackageOccurrenceView_PreservesOrderAndBindingFacts`,
`PackageOccurrenceView_EmptyInputProducesTypedEmptyView`,
and `PackageOccurrenceView_RepeatedBindingIssuesDistinctOccurrences`. The
implementation slice replaces that ad hoc ordered membership view with the
complete revision snapshot. Artifact Acquisition retains the shipped
transitional package-view action and transport contract until #5584 replaces
it with Navigation-owned actions; that compatibility adapter is not a second
scope-owner action system.

Browser adoption additionally requires outcome-level gates for Open versus Add,
Remove, Clear, stale completion, visible closed/selectively open state, and the
complete current `Microsoft.Extensions` descriptor. CLI adoption must prove
Markout and structured lowering omit process-local revision, occurrence,
action, and receipt identities.

## Staged adoption

1. Land #5715's Artifact Acquisition-owned resource-free Root projection.
2. Land #5729's Artifact Acquisition-owned Root preparation/publication
   handoff.
3. Land this design and the one-donor logical-scope ownership correction.
4. Implement the host-neutral revision, Root occurrence inventory, logical
   limits, and exact Add, Replace, Remove, and Clear results. This focused slice
   replaces the generalized producer scope proposed by
   [#5583](https://github.com/richlander/dotnet-inspect/issues/5583).
5. Narrow
   [#5584](https://github.com/richlander/dotnet-inspect/issues/5584) to consume
   these concrete results in Navigation.
6. Adopt exact package Open/Add/Remove/Clear in Inspect Web and expose the
   stateless inventory through the CLI.
7. Complete #5720's Package Set Registry/ecosystem-pack composition and #5602's
   typed source-intent adoption, then lower selected package-set actions to
   exact `ReplaceScope` or `AddRoots` requests. Prove the current complete
   `Microsoft.Extensions` membership under the 64-Root logical profile. Browser
   adoption #5576 separately owns the physical budget needed to realize that
   complete set.
8. Land #5765's dependency-evidence-to-candidate adapter, then implement the
   three typed expansion scopes and bounded dependency-evidence loop. Package
   manifest dependencies remain unsupported until that adapter supplies exact
   source-authorized candidates; restored-project evidence may already satisfy
   that contract.
9. Adopt the full Browser Workspace viewer/editor through the Inspect Web
   presentation and consumer owners.
10. Have Workspace Definitions #5525 decide portable capacity and projection
   for the larger reachable scope under its own contract. Adopt browser history
   through its focused owner, then remove the packet inventory and
   multi-live-Workspace paths.

Each slice names its one adopting owner. This design does not authorize one PR
spanning core scope, acquisition, Navigation, Browser presentation, history,
and portable schema.

## Non-claims

This design does not define:

- simultaneous live Workspaces, Workspace switching, or cross-Workspace
  operations;
- Workspace names, tabs, recents, or saved-definition storage;
- a complete dependency graph or an automatic expansion recommendation;
- eager expansion merely from registering a package prefix;
- package ownership inferred from assembly metadata or display text;
- query mutation of scope;
- source credentials or authorization transport;
- byte-for-byte Workspace snapshots;
- browser focus, announcement, history, or URL policy;
- a packet format or compatibility promise;
- one universal limit for artifact bytes, assemblies, relationships, or query
  work; or
- support for Windows Metadata inputs.
