# Package Dependency Workspace Routes

## Status and owner

This design owns one claim: a completed package dependency traversal, its exact
realized root bindings, and completed destination realization evidence can be
joined to one captured Workspace Scope publication base to atomically admit
Package destinations and issue one typed destination outcome per root-relative
admitted edge.

Issue [#8029](https://github.com/richlander/dotnet-inspect/issues/8029)
tracks this owner. The motivating production scenario is the member call graph
for `Microsoft.Extensions.Http.Polly`: the explicitly selected
`netstandard2.0` root remains selected while dependency destinations use the
traversal target, which defaults to `net12.0`.

The owner composes, but does not redefine:

- traversal reachability, completion, candidate correspondence, or target
  policy;
- PackageHouse acquisition, Platform pruning, or compile realization;
- Workspace Artifact Root preparation, publication, retirement, or query
  leases;
- member call-graph algorithms, external-focus policy, rendering, or host
  gestures.

## Basis

`package-dependency-traversal.md` owns the immutable root-relative graph and
distinguishes `ResolvedCandidate` from `SuppliedRoot` authority.
`package-dependency-edge-realization.md` owns one exact PackageHouse execution
authorized by one resolved root-relative edge occurrence.
`package-house.md` owns Package and Platform realization outcomes.
`inspection-space.md` owns Workspace Scope mutation and Package Root lifetime.

NuGet restore is analogous in resolving a target graph before consumers use
its libraries, and in reusing one resolved library for repeated references.
This design follows that target-aware composition model but deliberately
retains root-relative edge occurrences and owner-issued Package/Platform
evidence rather than lowering immediately to a coordinate-only lock graph.

## Input join

One route-composition request names:

- one `InspectionWorkspace`;
- one exact current `WorkspaceScopeSnapshot`, including its Workspace identity,
  Scope revision, and publication-base identity;
- one immutable `PackageDependencyTraversalOutcome`;
- one live `PackageRootBinding` for every traversal root occurrence;
- one `PackageDependencyEdgeRealizationEvidence` for every root-relative
  admitted `ResolvedCandidate` edge occurrence;
- one Workspace operation deadline.

Every root must be a realized-package traversal source. Its binding must carry
the exact content-generation and selection identities recorded by the realized
context, and the captured Scope must already contain the binding's exact
Ready Package Root correspondence. A projected-evidence root cannot be
inferred to be a Workspace Package.

Every realization must retain the request's exact traversal object, root
occurrence index, and edge index. Realizations are occurrence-specific:
evidence for one root-relative occurrence does not authorize another
occurrence, even when both reach the same graph edge.

Malformed joins are rejected before Workspace mutation. In particular, the
operation does not guess roots by coordinate, accept unrelated House
settlements, or silently omit missing resolved-edge evidence.

## Destination classification

The operation visits every edge admitted by every root occurrence and preserves
its root occurrence, graph edge, and root-relative distance.

### Resolved candidate

A resolved-candidate occurrence consumes its exact edge realization evidence.

- A Package Root contribution becomes a pending Package destination.
- An exact Platform delegation becomes a Platform destination.
- A visible House settlement that cannot contribute a Package Root becomes an
  unavailable destination retaining that evidence.

The operation never reacquires or reselects a completed contribution.

### Supplied root

A `SuppliedRoot` edge targets a traversal projection carrying an exact supplied
root occurrence index. The destination is the already-current Workspace
occurrence matched from that root's live binding. No PackageHouse operation and
no new Package Root admission are authorized.

### Traversal boundary

`DirectBoundary`, `FailedResolution`, and `WorkBudgetBoundary` edges become
typed unavailable destinations. Their existing traversal authority remains the
diagnostic owner; route composition does not reinterpret it as a Package or
Platform destination.

## Atomic Workspace admission

All Package Root contributions are submitted in one
`InspectionWorkspace.AddPackagesAsync` call against the captured Scope revision
and publication-base identity. Workspace owns correspondence deduplication,
Root preparation, publication, query quiescence, and retirement.

The final committed or no-effect Scope snapshot is the only authority for a
Package route. Each contributed binding is resolved back to its exact
`WorkspacePackageOccurrenceDescriptor` in that snapshot. Multiple edge
occurrences may therefore retain distinct realization evidence while
legitimately naming one Workspace occurrence when their logical Package Root
requests are equal.

A rejected, failed, unavailable, or cancelled Scope operation yields no
success-shaped destination set. Platform and supplied-root classifications do
not justify returning a partial route set when Package publication failed.

## Result algebra

A completed composition retains the terminal Workspace Scope operation and one
destination per root-relative admitted edge:

- **Package** — the exact final Workspace Package occurrence, its source
  authority (`ResolvedCandidate` or `SuppliedRoot`), and optional exact edge
  realization evidence;
- **Platform** — the exact PackageHouse Platform delegation and realization
  evidence;
- **Unavailable** — either the traversal boundary authority or the exact
  realization evidence that issued no Package or Platform destination.

A non-committed composition retains the terminal Workspace Scope operation and
issues no destinations.

These results are operation/composition evidence, not a completed host-facing
projection. They do not use `InspectionEnvelope<T>` until a later host-neutral
call-graph API detaches and lowers the selected route content.

## Lifetime

Route composition does not own a second Package lifetime system. House
settlements and bindings are caller-provided evidence; Workspace adopts and
owns the Artifact Roots it publishes through its existing Scope machinery.
The completed route result may retain the caller-provided exact edge
realization evidence so candidate, settlement, and no-contribution association
remain inspectable; it does not acquire separate ownership of that evidence.
Workspace occurrence descriptors and Platform delegation evidence remain the
route authorities consumed by later composition.

## Pathological cases and gates

Release gates prove:

- `EdgeRealization_UsesTraversalTargetWithoutReselectingPollyRoot` preserves the
  real `netstandard2.0` Polly root while destination requests use traversal
  target `net12.0`;
- `DependencyWorkspaceRoutesBatchPackagesAndRetainPlatformDelegation`
  publishes several destination contributions in one Scope addition and adds
  no Package Root for the Platform delegation;
- `WorkspaceRoutesReuseExactSuppliedRootOccurrence` reuses the exact current
  Workspace occurrence;
- `EdgeRealization_PreservesSameCoordinateCandidateCorrespondence` and
  `AddPreservesExistingOrderAndAppendsOneDistinctBatch` jointly preserve
  occurrence-specific candidate evidence while Workspace deduplicates exact
  logical Package Root requests;
- `DependencyWorkspaceRoutesDoNotPublishPartialBatchWhenPreparationFails`
  returns no route set after a failed Scope preparation;
- `WorkspaceRoutesRequireEveryResolvedEdgeRealizationBeforeMutation` rejects
  missing resolved-edge evidence before mutation; and
- `GuardedAddRequiresExactPublicationBase` and
  `WorkspaceRoutesReturnCancellationWithoutBoundaryDestinations` keep stale
  publication-base and cancellation outcomes visible.

## Production adoption

This is the second destination-composition slice after single-edge
realization. The next slice provides an owner-issued source-operation context
that executes all admitted resolved edges, invokes this composition operation,
and exposes detached dependency destinations to one shared call-graph service.
The CLI and Browser/Wasm hosts then consume that same service. External-focus
policy and website controls remain presentation and call-graph concerns, not
Workspace route concerns.
