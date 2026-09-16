# Package version-cell Metadata inspection

## Status, owner, and claim

Status: **implemented** by the initial slice tracked in
[#7146](https://github.com/richlander/dotnet-inspect/issues/7146).

The **Package version-cell Metadata inspection** owner defines one
`DotnetInspector.PackageQueries` operation:

> Consume one PackageHouse-issued version-population cell through an exact
> host-executed compile-realization request, admit its package Root to one
> bounded ephemeral Workspace, execute the existing assembly-context Metadata
> image query and explicitly requested bounded API evidence under
> Workspace-scoped borrowing, and return only detached resource-free evidence
> after awaited Workspace close.

This is a focused package-aware L1 composition owner. It does not redefine
PackageHouse settlement, package Root construction, Workspace admission,
Metadata facts, or host source authorization.

The API-evidence extension in
[#7280](https://github.com/richlander/dotnet-inspect/issues/7280) is a producer
prerequisite for the shared History contract in
[#7229](https://github.com/richlander/dotnet-inspect/issues/7229).
It does not implement History or a second host terminal.

## Motivation and real asset

Subject-owned Diff History needs to inspect selected cells from one already
settled package-version population. Re-discovering a selected version,
reconstructing its source authority, or extracting its package directly in a
host would break the cell's reporter-bound correspondence and duplicate
PackageHouse and Workspace policy.

The motivating population is `Markout@0.33.0..0.35.2`. Production
`dotnet-inspect` observation of `Markout@0.35.2` records one `net10.0` package
surface containing `lib/net10.0/Markout.dll` at 233,472 bytes. The package
names repository commit
`e2302d97c166aad0ef73a00d79bc50e8f228d379` and uses the MIT license. The
initial Release evidence retains the real assembly through a pinned NuGet
package restore while synthetic package content supplies otherwise unreachable
failure and cleanup boundaries.

## Owner map

| Concern | Owner | Consumed contract |
| --- | --- | --- |
| Population cell, reporter-bound candidate, exact House request, settlement, and compile receipt | [PackageHouse](package-house.md) | One prepared cell execution and its exact terminal settlement |
| House-to-Root correspondence | [Package Root realization](artifact-acquisition-and-workspaces.md#package-root-realization) | `PackageHouseRootContributionAdapter` and its typed no-contribution outcome |
| Root preparation, atomic Scope admission, scoped query borrowing, bounds, and release | [Workspace Scope and Expansion](workspace-scope-and-expansion.md) and [Artifact acquisition and Workspaces](artifact-acquisition-and-workspaces.md) | One fresh Workspace, one package Root, one scoped Root query, and awaited close |
| Metadata facts and participant outcomes | [Assembly inspection query](assembly-inspection-query.md) | Existing image and bounded API-surface queries over the admitted surface group |
| API Type, Member, and Attribute censuses | [Finding adoption](finding-adoption.md) | Native `MetadataFindings` producers over the exact projected participant surface |
| Temporal selection and correlation | [Diff History](diff-history.md) | Later production consumer; not owned here |

The operation owns only the sequencing, exact cross-owner correspondence, and
detached terminal outcome.

## Prepared cell execution

PackageHouse issues one prepared execution from a
`PackageHouseVersionPopulationCell`. The preparation fixes:

- the exact reporter-bound candidate already retained by the cell;
- one caller-supplied `Realize` operation;
- the target context;
- compile asset selection;
- package-only handoff; and
- the cell's request association.

The prepared execution carries the exact `PackageHouseRequest` object that the
host must execute. A host capability accepts that owner-issued value; it does
not rebuild a demand from package ID, version, source display text, or request
association.

The returned settlement is accepted only when its House evidence retains that
exact prepared request object. This is an ordinary construction invariant over
a trusted host adapter, not a defense against a hostile in-process caller. It
detects accidental execution of a neighboring demand, including one that
reuses the cell's public association.

Desktop composition adapts its existing configured-source execution to the
prepared value. Browser/Wasm may supply another host adapter without changing
the PackageQueries operation.

## Request and bounds

One inspection request carries:

- the exact prepared cell execution;
- the package target context;
- a positive finite assembly-count limit;
- a positive finite selected-entry byte limit;
- a positive finite aggregate retained-image byte limit; and
- a finite absolute Workspace deadline.

Caller cancellation is supplied to execution rather than retained in the
request or result. PackageHouse's own request and operation timeouts remain
owned by its `Realize` operation.

The operation passes the three realization limits unchanged to the
Workspace-owned package Root preparation path. The Workspace remains the
enforcement owner. The PackageQueries operation does not infer a limit from a
deadline or reinterpret a Workspace rejection.

### Explicit API evidence

The optional API request supplies one exact Metadata Type full name, an
existing API-surface visibility scope, and explicit `ApiSurfaceProjectionLimits`.
Omission preserves image-only work. Requesting API evidence uses the same
prepared cell execution, Root, and Workspace borrow; it does not reacquire the
package or open another Workspace.

The bounded API query retains ordered participant outcomes, inspection
failures, and truncation under its existing contract. For each available
participant, this operation invokes the native Type, Member, and Attribute
Finding producers for the requested Type name. Each resulting set retains the
exact available participant and surface from which its censuses were produced.
This supplies reusable observation evidence; it does not choose a cross-Library
focus or correlate versions.

Unavailable or omitted participants do not become empty or absent censuses.
Their native outcomes and projection bounds remain in the API result.
Consumers must interpret that evidence rather than treat the shorter Finding
set array as complete coverage. Within an available participant, native
`Complete`, `Absent`, and `Failed` meanings are preserved without normalization.
An explicit empty compile selection retains its existing selection evidence
and empty participant population, not a fabricated Type evaluation.

## Composition

For one request, the operation:

1. invokes the host cell executor exactly once;
2. rejects a settlement that does not retain the exact prepared House request;
3. adapts the settlement through
   `PackageHouseRootContributionAdapter`;
4. returns typed no-contribution evidence without constructing a Workspace
   when the adapter cannot issue a Root;
5. creates one empty ephemeral Workspace;
6. atomically replaces its empty Scope with the exact contributed
   `PackageRootBinding` under the supplied realization limits and deadline;
7. locates the single committed Root occurrence and enters one
   Workspace-scoped Root query;
8. executes `AssemblyContextMetadataImageQuery` over the surface group, or
   returns its ordinary empty assembly-context result when the package Root has
   no selected assembly contexts, and produces bounded API evidence only when
   explicitly requested; and
9. awaits Workspace close before publishing any result.

The operation never performs version discovery, package acquisition, compile
selection, Metadata inspection, or cleanup through a second algorithm.

## Detached evidence and outcomes

Every outcome carries detached evidence for:

- canonical package ID and exact population address;
- the complete resource-free `PackageHouseResult`;
- the exact compile selection receipt when one exists; and
- the realized package Root coordinate when a contribution exists.

The terminal family distinguishes:

- **Available** — the existing ordered
  `AssemblyContextResult<MetadataImageOverview>`, including participant-local
  rejection or failure, plus optional detached API evidence when requested;
- **No contribution** — the exact
  `PackageHouseRootNoContributionReason`;
- **Workspace failure** — the operation stage plus the owner-issued
  `WorkspaceScopeRejection` or `ArtifactRootFailure`; and
- **Cleanup failure** — bounded cleanup-stage counts after a provisional
  success.

A valid House compile realization may retain an owner-default runtime
identifier without an acquisition framework. That evidence cannot form the
complete package Root coordinate, so the Root try-adapter returns false and
the operation preserves
`PackageHouseRootNoContributionReason.CoordinateNotRepresentable` rather than
throwing after acquisition. Malformed House or selection correspondence remains
exceptional.

The returned closure retains no cell, payload, package Root binding, package
content, Workspace, group, session, lease, stream, callback, source
composition, or close exception. House results and Metadata participant
results remain the owner-issued resource-free evidence they already define.

## Terminal precedence and cleanup

House no-contribution completes before a Workspace exists. Every
Workspace-bearing result remains provisional until close settles.

After Workspace creation:

1. an unexpected exception remains the exact primary exception;
2. caller cancellation remains cancellation with the caller's token;
3. a typed House, Workspace, or Metadata result remains primary;
4. awaited Workspace close always runs;
5. bounded cleanup evidence is attached to a propagated exception or
   cancellation;
6. cleanup failure replaces a provisional successful Metadata result; and
7. cleanup evidence remains secondary beside a typed Workspace failure.

The operation observes caller cancellation after cleanup and before publishing
a completed result. A Workspace deadline rejected before admission remains the
exact `WorkspaceScopeRejection.DeadlineExpired`. Deadline expiry during
preparation is reported as `ArtifactRootFailure.DeadlineExpired` when caller
cancellation did not win.

Cleanup evidence contains only stage and count:

- direct group release;
- artifact-session or Root release;
- close-report contract; and
- close orchestration.

It carries no exception instance, message, path, stream, or resource handle.

## Pathological case

A selected cell realizes a package whose compile set contains one valid
managed assembly and one invalid metadata image. Workspace preparation rejects
the complete Root as `ArtifactRootFailure.PreparationFailed`. The operation
returns no partial Metadata result, performs no second PackageHouse execution
or version discovery, awaits release of provisional Workspace resources, and
publishes any cleanup failure only as bounded secondary evidence.

Once a Root is successfully admitted, Metadata-level participant failures
remain ordered beside healthy entries through the existing
`AssemblyContextResult` contract.

## Production path

This operation is step 2 of the seven-step temporal ownership path:

1. PackageHouse version-population settlement — complete in #7133.
2. Package version-cell Metadata and explicit API Finding evidence — this owner.
3. Exact matched API Member to implementation Analysis — owned by
   [matched API Member Analysis](matched-api-member-analysis.md).
4. Diff History detached baseline receipt with stable Finding subject, exact
   pair-local source binding, and direct source-to-checkpoint correspondence —
   owned by
   [Diff History inspection](diff-history.md).
5. Bounded PackageHouse baseline-cell and source/destination cell-pair
   Analysis — #7248.
6. Shared Diff History and metadata-only version-count terminals, followed by
   subject CLI cutover with top-level `timeline` removal.
7. Browser Compare and version-count adoption.

The later hosts supply their source authorization and cell executor while
consuming the same request and outcome. This lower producer feeds the shared
`InspectionEnvelope<DiffHistoryOutcome>` terminal in step 6; it does not
introduce a separate CLI or Browser counting/correlation algorithm. The existing
standalone Timeline's host-local API acquisition/projection remains until the
subject-owned cutover retires it in step 6.

API evidence is structured producer content, not rendered output. The later
History terminal retains it in the shared envelope; CLI Markout lowering and
Browser presentation remain with their existing owners.

## Evidence

The Release gate is `PackageVersionCellMetadataInspectionTests`:

| Claim | Release evidence |
| --- | --- |
| Exact cell execution | `PreparedExecutionRequiresExactHouseRequest` and `InspectionRejectsSettlementForSubstitutedDemand` |
| One execution and no rediscovery | `InspectionExecutesOnePreparedCellAndReturnsDetachedMetadata` |
| Real package motivation | `InspectionExecutesPinnedMarkoutPackage` |
| Opt-in native API evidence | `ApiInspectionReturnsDetachedNativeMarkoutFindings` |
| Exact per-participant association | `ApiInspectionKeepsParticipantCensusesSeparate` |
| Native absent versus unavailable evidence | `ApiInspectionPreservesNativeSubjectAbsence`, `ApiInspectionPreservesTruncationWithoutInventingAbsence`, and `ApiInspectionPreservesEmptyCompileSelection` |
| Finite realization | `InspectionEnforcesAssemblyEntryAndAggregateBounds` |
| Atomic malformed-image refusal | `InspectionMalformedNeighborRejectsWholeRootWithoutMetadata` |
| Typed House, Workspace, and deadline failures | `InspectionPreservesNoContributionWithoutCreatingARoot`, `InspectionPreservesExpiredWorkspaceDeadline`, and the bounded-admission tests |
| Unrepresentable Root coordinate | `InspectionPreservesOwnerDefaultRuntimeIdentifierAsNoContribution` |
| Scoped borrowing and empty package shape | `InspectionPreservesExplicitEmptyCompileSelection` |
| Terminal cancellation | `InspectionCancellationAfterQueryWaitsForCloseAndPublishesNoOutcome` |
| Cleanup precedence | `CleanupFailureSupersedesSuccessAndRemainsSecondaryToFailure` |
| Resource-free closure | `OutcomeClosureIsResourceFree` |

The first implementation reuses the repository's sequential
`PackageAssemblyEvaluator` lifecycle convention: provisional result, cleanup
in `finally`, typed bounded cleanup evidence, and terminal cancellation
observation. It does not reuse that evaluator's sparse one-assembly selection
or semantic producer.

The API gates are targeted, bounded cases over the pinned 233 KB Markout
assembly, not a corpus or Analysis sweep. They run in the PR-fast query suite;
the focused Release run and xUnit timing report keep their cost below the
test-cost threshold.

## Non-claims

This owner does not:

- choose Diff History evaluation cells or correlate versions;
- produce Analysis Findings, select a unique Library/Member focus, or establish
  cross-version correspondence;
- define package version Count;
- bind CLI or Browser commands, rendering, sections, or transport;
- authorize package sources or offline acquisition;
- redefine PackageHouse, Workspace, or Metadata failure semantics;
- dispose caller-owned payload or host/store-owned package content;
- inspect implementation-role assemblies;
- introduce concurrent cell evaluation; or
- claim tuned throughput, allocation, or memory targets.
