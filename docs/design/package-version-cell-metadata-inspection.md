# Package version-cell Metadata inspection

## Status, owner, and claim

Status: **implemented**. This focused composition is tracked by
[#7146](https://github.com/richlander/dotnet-inspect/issues/7146), under
[Diff History #6940](https://github.com/richlander/dotnet-inspect/issues/6940),
[PackageHouse #6426](https://github.com/richlander/dotnet-inspect/issues/6426),
[command ownership #6638](https://github.com/richlander/dotnet-inspect/issues/6638),
and [command inventory #6639](https://github.com/richlander/dotnet-inspect/issues/6639).

**Package Version-Cell Metadata Inspection** in
`DotnetInspector.PackageQueries` owns:

> Given one PackageHouse-issued version-population cell, one Realize operation,
> one package target context, one host-supplied compile-realization capability,
> and one finite Workspace deadline, execute the cell once, preserve its exact
> House realization through one package Root and one ephemeral Workspace, and
> return the existing resource-free Metadata image result only after Workspace
> cleanup settles.

This owner defines only the cross-owner composition and its terminal result.
It does not redefine PackageHouse selection or realization, package Root
construction, Workspace admission and borrowing, Metadata decoding, or host
source configuration.

The real motivating population is `Markout@0.33.0..0.35.2`. Diff History can
select one address from that population and invoke this operation without
rediscovering versions or introducing command-local package extraction.

## Normative basis

This design consumes these owner-issued contracts:

- [PackageHouse](package-house.md#version-population-settlement) owns the
  population cell, candidate, source reauthorization, payload acquisition,
  compile selection, and House terminal evidence.
- [Artifact acquisition and Workspaces](artifact-acquisition-and-workspaces.md)
  owns `PackageRootBinding`, Root admission, retained resources, exact
  generation borrowing, and cleanup.
- [Inspection layers](inspection-layers.md) owns host-neutral L1 query
  placement and resource-free query results.
- Metadata and core Queries own `AssemblyContextMetadataImageQuery` and
  `AssemblyContextResult<MetadataImageOverview>`.
- [Diff History](diff-history.md) is a downstream consumer. It owns temporal
  evaluation and correlation, not this lower composition.

No behavior or code is transferred from an external implementation.
`PackageAssemblyEvaluator` is the in-repository lifecycle analogue: an
ephemeral Workspace makes results provisional until close, and cleanup
evidence remains visible. Its sparse one-assembly projection is not reused
because this operation inspects the complete House-selected compile surface.

## Public contract

`PackageVersionCellMetadataInspectionRequest` carries:

- the exact `PackageHouseVersionPopulationCell`;
- a `PackageHouseOperation` whose profile is `Realize`;
- an optional PackageHouse target context; and
- a finite absolute Workspace deadline.

Compile selection and package-only handoff are fixed by this operation. They
are not caller-selectable dimensions.

`IPackageVersionCellCompileExecutor` is the host-neutral capability boundary.
It accepts the cell, Realize operation, target context, and cancellation token.
Its result must retain:

- the cell's exact `PackageHouseRequestAssociation`;
- the cell's exact reporter-bound candidate demand;
- the exact supplied operation and target context;
- `PackageHouseAssetSelectionKind.Compile`; and
- `PackageHouseLibraryHandoffMode.PackageOnly`.

The PackageHouse-owned cell validates its association and candidate demand;
the operation validates the remaining execution dimensions before adapting the
settlement.
`DesktopPackageVersionCellCompileExecutor` binds that capability to
`DesktopPackageSourceComposition`. Browser/Wasm can implement the same
capability without depending on the desktop composition.

A House compile realization whose owner-default target retains a runtime
identifier but no acquisition framework is valid House evidence but cannot
form a package Root coordinate. The House-to-Root try-adapter returns
`CoordinateNotRepresentable`; this operation preserves that typed
`NoContribution` result.

## Exact correspondence

The composition preserves this join:

```text
population cell
  -> cell request association
  -> reporter-bound PackageHouse candidate
  -> House acquisition generation
  -> House compile-selection receipt
  -> PackageRootBinding content and selection identities
  -> Workspace package occurrence correspondence
  -> ready Artifact Root generation
  -> scoped package-Root query
  -> materialized Metadata result
```

Reference identity is required where the issuing owner supplies opaque
identity. Package identity, version, target, producer, or path display text is
never used to recreate correspondence.

`PackageHouseRootContributionAdapter` is the sole House-to-Root adapter. The
inspection operation does not rerun acquisition or either package asset
selector. `WorkspaceScopeSnapshot.FindPackageOccurrence` locates the exact
committed occurrence from the contributed binding. The query borrows only the
ready generation issued for that occurrence.

## Operation

One execution:

1. validates the prepared request and observes caller cancellation;
2. invokes the host-supplied compile executor exactly once;
3. validates the returned House request correspondence;
4. adapts the complete House settlement through
   `PackageHouseRootContributionAdapter`;
5. returns a typed no-contribution result when no package Root can be issued;
6. creates one fresh `InspectionWorkspace`;
7. reads its initial Scope revision;
8. replaces Scope with exactly the contributed `PackageRootBinding` under the
   request's finite deadline;
9. locates the exact committed occurrence and requires its ready generation;
10. executes `AssemblyContextMetadataImageQuery` under
    `ExecutePackageRootQueryAsync`;
11. materializes the complete Metadata result inside the callback; and
12. awaits Workspace close before publishing the final outcome.

The operation performs no version discovery. It does not retry another
population cell, candidate, source, framework, or package Root.

## Results and failure

The semantic result is one of:

- `Inspected`, carrying the existing
  `AssemblyContextResult<MetadataImageOverview>`;
- `NoAssemblyContext`, carrying the package selector's exact non-selected
  compile status;
- `NoContribution`, carrying the original House result and adapter reason;
- `WorkspaceNotCommitted`, carrying the exact Workspace Scope terminal result;
  or
- `RootQueryRejected`, carrying the Artifact Root admission failure.

Every semantic result retains the exact cell and `PackageHouseResult`.
It retains no payload, package Root binding, Workspace, group, participant,
session, lease, callback, or source composition.

The final outcome is either `Completed` or `CleanupFailed`. `CleanupFailed`
preserves the provisional semantic result and reports counts by Workspace
group release, aggregate artifact-Root release, and close-orchestration stage.
The aggregate artifact-Root stage covers the Workspace close report's Root
group, lease, and session release failures. Cleanup cannot turn an inspected
result into clean success.

Caller cancellation and unexpected executor, Workspace, or query exceptions
remain exceptions. Once a Workspace exists, cleanup still runs. When cleanup
also fails, resource-free cleanup evidence is attached to the primary
exception. Caller cancellation is re-observed after cleanup with the original
caller token, including when a lower layer throws with a linked token.

A root-only package or explicit empty compile group is not successful empty
Metadata. The exact Root query returns `NoAssemblyContext`. Per-participant
Metadata rejection remains inside the existing ordered Metadata result for
Roots admitted by Workspace. The current Scope owner rejects a package Root
whose selected assembly image fails admission; this operation preserves that
`WorkspaceScopeOperationResult.Failed` boundary rather than weakening Root
readiness.

## Lifetime

The PackageHouse settlement is local to the operation. A contributed
`PackageRootBinding` is a transient Workspace input. Once Scope commits, the
Workspace owns the retained Root resources; its scoped query callback only
borrows them.

The Metadata result is fully materialized before the callback returns.
Workspace close is unconditional after Workspace creation and has no
cancellation token. The final outcome is not observable until close settles.

## Non-goals

This owner does not define:

- Diff History population scheduling, sparse or dense selection, focus
  reacquisition, Findings, transition correlation, or temporal documents;
- package version Count;
- CLI or Browser gestures, sections, rendering, or output formats;
- package source registration, credentials, transport, cache, or offline
  policy;
- a generic asset-selection operation;
- a second package Root adapter or Metadata algorithm; or
- compatibility behavior for the legacy top-level `timeline` command.

## Pathological case

One selected package has a compile surface containing both a valid managed
assembly and a malformed image. Current Workspace Scope admission rejects the
whole Root as `PreparationFailed`; the operation must preserve that typed
failure, execute no Metadata callback or version rediscovery, and close every
provisional Workspace resource before the caller receives the result. If a
ready Root later contains a Metadata-level participant rejection, the operation
preserves that rejection beside healthy entries in the existing ordered
Metadata result.

## Required Release gates

The focused suite is `DotnetInspector.Queries.Tests`.

| Gate | Property |
| --- | --- |
| `SuccessfulCell_InspectsExactCompileRootAndClosesWorkspace` | One cell execution, exact House-to-Root-to-Workspace correspondence, materialized Metadata, and cleanup before observation. |
| `SettlementForAnotherCellDemand_IsRejected` | Reusing the cell association cannot substitute another House demand for the cell's reporter-bound candidate. |
| `MalformedCompileImage_RemainsWorkspaceFailure` | The current Workspace all-or-failure Root admission boundary remains visible and publishes no partial Metadata result. |
| `RootOnlyCell_ReturnsNoAssemblyContext` | No compile assets do not become successful empty Metadata or an exception. |
| `OwnerDefaultRuntimeIdentifier_ReturnsNoContribution` | A valid House realization whose RID lacks a representable acquisition framework remains typed `CoordinateNotRepresentable`. |
| `HouseFailure_RemainsTypedWithoutWorkspaceAdmission` | Source reauthorization and other House no-contribution results remain visible without constructing a Workspace result. |
| `ExpiredWorkspaceDeadline_RemainsTyped` | Scope deadline rejection retains the owner-issued Workspace terminal. |
| `Cancellation_PropagatesAfterCleanup` | Caller cancellation remains cancellation with the caller token and no escaped live resources. |
| `LinkedCancellation_IsNormalizedToCallerToken` | Linked-token cancellation is normalized to the original caller token. |
| `Outcome_WaitsForWorkspaceClose` | No final outcome is observable while Workspace close remains blocked. |
| `CleanupFailure_ReturnsTypedOutcome` | Workspace cleanup failure invalidates clean completion and returns typed stage evidence. |
| `Cancellation_PreservesCleanupEvidence` | Cleanup failure evidence remains attached when caller cancellation propagates. |
| `PublicResults_AreResourceFree` | Result closure rejects payload, stream, Root binding, Workspace, group, participant, session, lease, callback, and desktop source-composition fields. |
