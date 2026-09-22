# Package dependency edge realization

## Status and authority

This document is the focused owner of target-aware destination Package Root
realization for one resolved package dependency edge. Design and adoption are
tracked by [#6424](https://github.com/richlander/dotnet-inspect/issues/6424).

The owner defines one host-neutral handoff from a root-relative resolved edge
in a completed Package Dependency Traversal result to an exact candidate-bound
PackageHouse compile realization. It retains the traversal edge occurrence,
the traversal target, optional Platform pruning, the exact House request, and
the completed House settlement.

Adjacent owners retain their authority:

- [Package Dependency Traversal](package-dependency-traversal.md) owns graph
  identity, source and target projection indexes, candidate correspondence,
  root-relative edge admission, and traversal target policy.
- [PackageHouse](package-house.md) owns package settlement, acquisition,
  compile selection, requested-versus-selected target evidence, Package Root
  contribution, and Platform delegation.
- [Platform/package pruning](platform-package-pruning.md) owns exact-target
  inventory and the package-version subsumption comparison.
- [Realized Package Dependency Context](realized-package-dependency-context.md)
  owns the source participant's exact package-selection and declaration-evidence
  association.
- [Package dependency Workspace routes](package-dependency-workspace-routes.md)
  owns destination batch admission, supplied-root reuse, and typed Package,
  Platform, or unavailable destination outcomes. Later call-graph composition
  owns which edges to execute, active Platform selection, and presentation.

## Claim

> A root-relative resolved package dependency edge reaches destination package
> realization only through one prepared execution that retains the edge's
> exact candidate correspondence and uses the traversal target as the
> PackageHouse selection request, optionally carrying the matching exact
> Platform pruning receipt.

The prepared execution is the join currency. Package identity, version text,
selected asset paths, or semantic graph-node identity cannot reconstruct it.

## Input

One request names:

- one immutable `PackageDependencyTraversalOutcome`;
- one root occurrence index;
- one edge index admitted by that root's reachability;
- one `PackageHouseOperation` whose profile is `Realize`;
- one exact `PackageHouseTargetContext`; and
- either no Platform inventory or one inventory corresponding to the target's
  exact `PlatformFamilyTarget`.

The target context's requested framework must equal
`TraversalTargetFrameworkPolicy.TargetFramework`. The source projection's
selected dependency group remains unchanged and may describe another
framework.

The current query accepts only `ResolvedCandidate` edges whose target
projection retains the exact `PackageAcquisitionCandidate`. Direct boundaries,
failed resolutions, work-budget boundaries, and root-supplied recurrence do
not authorize a new destination PackageHouse execution.

Root-supplied recurrence already identifies an admitted participant supplied
by the traversal root owner. Reusing or locating that participant is a
Workspace composition concern rather than another candidate-bound package
realization.

## Prepared execution

`PackageDependencyEdgeRealizationQuery` validates the root-relative edge
occurrence and prepares:

```text
PackageDependencyEdgeRealizationExecution
  Subject
    Traversal
    RootOccurrenceIndex
    EdgeIndex
    Root-relative Distance
    SourceProjection
    TargetProjection
    Candidate
  Input
    PackageHouseDependencyInput
    PackageHouseRequest
  Optional Pruning
    PackageHousePruningReceipt
```

The PackageHouse request:

- uses the traversal-issued exact candidate;
- retains the selected source declaration and evidence root;
- requests compile realization;
- uses the exact traversal target and optional runtime identifier;
- retains one opaque PackageHouse request association; and
- remains package-only until a later Workspace composition chooses Library
  materialization.

The query does not resolve the package version again, reacquire the manifest,
reselect the source dependency group, or infer the target from the destination
projection's selected manifest framework.

## Platform pruning

When no Platform target and inventory are supplied, the execution follows the
package route.

When both are supplied, the query constructs
`PackageHousePruningReceipt` directly from the exact candidate-bound House
request and inventory. This is a distinct adoption path from
`PackageHouseDependencyPruningApplicabilityQuery`, whose raw-declaration
contract requires the source dependency-group request to equal the
PackageHouse target.

That equality is intentionally not required here:

```text
source package selection: netstandard2.0
traversal target:         net12.0
destination House target: net12.0
```

The traversal edge already proves source declaration selection and candidate
correspondence. Requiring the source selection request to equal the destination
target would collapse the two contracts restored by Package Dependency
Traversal.

Only an exact `Subsumed` receipt delegates to Platform. `NotSubsumed` and
`NotComparable` remain package realizations with their pruning evidence
retained. A Platform target without its matching inventory is invalid
preparation; callers must retain their existing typed inventory-unavailable
failure rather than silently treating absence as `NotSubsumed`.

## Completion evidence

Executing the prepared request through PackageHouse produces
`PackageDependencyEdgeRealizationEvidence`. It accepts only a settlement whose
result retains the exact prepared PackageHouse request and retains:

- the exact prepared execution and root-relative edge occurrence;
- the optional prepared Platform pruning receipt, including when PackageHouse
  rejects the candidate before reaching pruning;
- the complete `PackageHouseSettlement`;
- the `PackageHouseResult` and optional `PlatformDelegation`; and
- the typed `PackageHouseRootContributionOutcome`.

When PackageHouse reaches pruning, its decision retains the same receipt. A
package result therefore preserves requested-versus-selected compile evidence
and may issue a Package Root binding from the exact acquired generation. A
delegated result retains the exact Platform target and proves that payload
acquisition did not run.

House failures remain House failures. No-match, rejection, unavailability,
incomplete work, or acquisition failure does not become an empty destination.

`PackageDependencyEdgeRealizationExecution.ExecuteAsync` preserves the ordinary
single-request ownership contract and consumes its Package Source lease.
`ExecuteStepAsync` performs the same exact prepared House request without
consuming the lease, allowing the
[package dependency call-graph operation](package-dependency-call-graph-operation.md)
to execute several fully validated edges serially through one caller-owned
source operation.

## Motivating and pathological cases

`Polly.Core@8.8.0` is the motivating real package:

- the explicit root selects `netstandard2.0` and retains four declarations;
- traversal uses `ProductDefault(net12.0)`;
- a resolved destination execution requests package assets for `net12.0`;
- a compatible lower destination folder remains selected evidence rather than
  replacing the request; and
- an exact Platform inventory may instead delegate a subsumed destination
  before package payload acquisition.

The required boundary cases are:

- a target projection with the same coordinate but another candidate
  correspondence is not interchangeable;
- a source selected for `netstandard2.0` can realize a destination under
  `net12.0` without changing source evidence;
- a compatible `net11.0` destination selected for a `net12.0` request retains
  both values;
- exact Platform subsumption produces delegation and zero package payload
  requests; and
- non-candidate edges are rejected before PackageHouse work.

## Conventional basis and divergence

NuGet restore carries one target through dependency resolution and target
library selection. This owner follows that conventional target-aware handoff
while retaining inspection-specific evidence for the source edge, exact
candidate, request, selected assets, and optional Platform decision.

The deliberate divergence remains the traversal graph's manifest-level
semantics. It may retain multiple exact versions and root-relative edge
occurrences rather than producing one reconciled project restore graph. Each
edge realization is therefore independent and never rewrites another edge or
semantic node.

No external implementation or source code is transferred.

## Required gates

| Property | Release gate |
| --- | --- |
| A real Polly.Core `netstandard2.0` root prepares a destination under `ProductDefault(net12.0)` without changing source selection. | `EdgeRealization_UsesTraversalTargetWithoutReselectingPollyRoot` |
| Exact Platform inventory is evaluated against the traversal target and may produce a retained delegating receipt. | `EdgeRealization_ComposesPlatformPruningAgainstTraversalTarget` |
| Same-coordinate target projections with distinct candidate correspondences prepare distinct candidate-bound House requests. | `EdgeRealization_PreservesSameCoordinateCandidateCorrespondence` |
| Direct, failed, budget, or otherwise non-candidate edges cannot prepare PackageHouse realization; a mismatched destination target is rejected. | `EdgeRealization_RejectsNonCandidateAndMismatchedTargetRequests` |
| Package execution selects a compatible destination folder while retaining the distinct traversal request and source selection. | `DependencyEdgeRealizationSelectsCompatibleDestinationWithoutChangingSource` |
| A subsumed destination delegates before package payload acquisition and retains no Package Root contribution. | `DependencyEdgeRealizationDelegatesSubsumedDestinationBeforeAcquisition` |
| A PackageHouse rejection before pruning remains a typed rejection associated with the prepared execution rather than becoming an edge-realization failure. | `DependencyEdgeRealizationPreservesHouseRejectionBeforePruning` |

## Production adoption

The end-to-end dependency-aware call-graph adoption path has four slices:

1. This owner prepares and completes one target-aware resolved-edge
   PackageHouse execution, including optional exact Platform pruning.
2. [Package dependency Workspace routes](package-dependency-workspace-routes.md)
   batches completed admitted-edge evidence, retains destination Package Root
   lifetimes through Scope, reuses already-realized root occurrences, and
   lowers package or Platform decisions into typed dependency destinations.
3. The shared
   [package dependency call-graph operation](package-dependency-call-graph-operation.md)
   executes all admitted resolved edges, invokes the Workspace route
   operation, and exposes detached route and graph evidence.
4. CLI and Browser/Wasm consume that same service. Inspect Web adds an
   independent traversal-TFM selector defaulted to `net12.0`; package TFM
   remains the root/member selection contract.

This first slice is independently coherent: it either produces exact
PackageHouse evidence or fails before execution. It does not present a
Workspace route or website behavior as implemented.

## Non-goals

- Changing dependency-group selection, candidate resolution, or traversal.
- Reproducing project restore reconciliation.
- Selecting the active Workspace Platform family or loading its inventory.
- Batching edges or owning destination Package Root lifetime.
- Reusing root-supplied recurrence as a new package realization.
- Defining final package, Platform, or mixed call-graph route shapes.
- Rendering or adding a CLI or Inspect Web gesture.
