# Resource-owner type map

Status: **non-normative current-state index** for
[#6654](https://github.com/richlander/dotnet-inspect/issues/6654).

This document records how focused resource owners map their current or approved
contracts onto the shared
[resource ownership and borrowing](resource-ownership-and-borrowing.md)
vocabulary. It also records the relationship between current C# shapes and
possible future compiler ownership.

The focused owner linked by each row remains normative. This map does not
define construction, acquisition, mutation, transfer, borrowing, release,
settlement, identity, correspondence, cache, House, Workspace, or Analysis
behavior.

## Protection scope

The shared protocol begins with the behavior that must remain impossible, not
with a cleanup interface:

| Protected category | Failure avoided |
| --- | --- |
| Exclusive mutable value | Two operations write through aliases to one object, a retained result observes another operation's update, or a stale owner mutates after transfer. |
| Terminal resource | The owner leaks, double-releases, uses after release, releases through the wrong authority, or drops required asynchronous settlement. |
| Combined value | Mutation authority and terminal responsibility separate or move independently. |

The protocol also governs borrows and detached results. A borrow is non-owning
access and must not escape, conflict with another borrow, or outlive its owner.
A detached immutable value is a non-owned result; its safety depends on not
retaining mutable owner state, live authority, or a terminal obligation.

`IDisposable` and `IAsyncDisposable` are current encodings for some terminal
resources. They are not the definition of ownership. `ArrayPool<T>.Rent` and
`Return` form an ownership protocol without either interface, and an exclusive
mutable value may need ownership without any release operation.

## Claim

An ownership mapping is useful only when it identifies all of these separately:

1. the protected category and unfavorable behavior;
2. the current owner or consuming operation;
3. mutation and transfer authority;
4. any synchronous borrow;
5. any terminal release or settlement obligation;
6. resource-free identity, evidence, outcomes, and receipts;
7. the owner-issued correspondence that joins those values;
8. current enforcement and its explicit gaps;
9. possible future compiler enforcement; and
10. asynchronous admission, quiescence, failure, and settlement that remain
   explicit protocol.

A type name, `IDisposable`, `IAsyncDisposable`, or
`[ResourceOwnership]` declaration is not sufficient evidence by itself.

## Demo

### Exclusive mutable state without cleanup

Consider a package resolver implemented with one reusable mutable result
object:

```text
resolve System.Text.Json -> write coordinate A -> caller retains result
resolve Humanizer.Core   -> write coordinate B into the same object
first caller observes coordinate B through the result returned for A
```

No `Dispose` call is missing. The error is shared mutable identity across two
operations. A correct shape either keeps the mutable construction object under
one exclusive owner and never lets it escape, or publishes a fresh immutable
detached result.

Current `ResolvedPackageCoordinate` demonstrates the second shape. Its
properties are get-only, its source sequence is copied into a read-only
collection, and each resolution constructs one answer. The result is
resource-free after publication because later resolutions cannot mutate it.

### Terminal resource with cleanup

Now consider one PackageHouse acquisition of `System.Text.Json@10.0.0`.

```text
PackageSourceSettlementService
  issues PackageSourceSettlementLease
    borrows the live root to issue PackageSourceOperationLease
      transfers the operation lease into PackageHouse.ExecuteAsync
        performs awaited source work
        releases the operation lease on every terminal path
        returns resource-free evidence
        and, on acquisition, a separately caller-owned payload
```

The map classifies `PackageSourceSettlementLease` as the asynchronous root
resource, `PackageSourceOperationLease` as the consumed synchronous operation
resource, and the active awaited source step as an ownership effect rather than
a third lease. PackageHouse does not mint a `PackageHouseLease`.

The result and receipts retain the exact request, source, candidate, producer,
generation, and outcome correspondence without retaining either Package Source
lease. A successful acquisition may separately carry a caller-owned payload;
the presence of a resource-free receipt does not make that payload
resource-free.

The neighboring Library case is intentionally different in maturity.
`LibraryContentOwner`, `LibraryReference`, and `LibraryOperationLease` are
approved owner-issued names, but remain design-only until
[#6621](https://github.com/richlander/dotnet-inspect/issues/6621) implements
them. PackageHouse, PlatformHouse, Workspace, SourceHouse, and
DocumentationHouse therefore link to that owner rather than inventing
consumer-specific lifetime wrappers.

## How to read the map

### Maturity

| State | Meaning |
| --- | --- |
| **Implemented** | The named type or operation exists and the cited Release evidence exercises its stated current behavior. |
| **Design-only** | A focused owner has approved the type or role, but no production implementation or Release gate establishes it. |
| **Compatibility bridge** | Shipping behavior remains temporarily useful but is not the target ownership shape. |
| **Planned** | The focused owner or adopter has not yet selected or implemented the concrete type shape. |

An implemented row may still have explicit compiler-enforcement gaps or
unverified broader properties. A design-only row must not be presented as a
shipping API.

### Mapping fields

Each detailed row records:

| Field | Question answered |
| --- | --- |
| Normative owner | Which focused document defines the behavior? |
| Current or approved shape | Which exact type or operation carries the role? |
| Protected category and lifecycle role | Is it an exclusive mutable value, terminal resource, combined value, owner, authorization, operation lease, borrow, detached value, reference, receipt, outcome, or settlement observation? |
| State | Is the shape implemented, design-only, a compatibility bridge, or planned? |
| Transfer | Which operation constructs, acquires, consumes, accepts, detaches, returns, or releases ownership? |
| Lifetime and correspondence | Which issuer, generation, registration, definition snapshot, or lender bounds it? |
| Current enforcement and evidence | What does current C# or the owner reject, and which Release gate supports the claim? |
| Future compiler correspondence | Could the shape become a resource, consuming parameter or receiver, borrow, borrow-derived result, transitive child, or none? |
| Asynchronous residual | Which admission, active-work, completion, abandonment, quiescence, observation, or failure protocol remains explicit? |
| Resource-free evidence | Which values retain identity or outcomes without authority? |
| Simplification and owner task | Which wrapper, hidden capture, compatibility path, or runtime check remains, and who owns the change? |

## Owner index

| Owner boundary | Maturity | Ownership-bearing shapes | Resource-free shapes | Focused owner or task |
| --- | --- | --- | --- | --- |
| `AssemblyInspectionSession` | Implemented floor | Session owner; lender-bound borrowed session | Detached snapshot callback result | [Assembly image lifetime](assembly-image-lifetime.md), #6571 |
| Artifact | Implemented core with compatibility bridges | `ArtifactSetSession`, acquisition/admission/query/content leases, contribution scope, compatibility stream | `ArtifactContentReference`, identity, registration, roles, provenance, digests, detached status and receipts | [Artifact ownership and borrowing](artifact-ownership-and-borrowing.md), #6647 |
| Package Source | Implemented | `PackageSourceSettlementLease`, `PackageSourceOperationLease`; active awaited work is an effect | Candidates and completed non-payload evidence; authorization is separate non-resource authority | [Package source model](package-source-model.md), #6619 |
| PackageHouse / Package Source | Implemented operation consumption | Consumed `PackageSourceOperationLease`; acquired payload remains separately caller-owned | `PackageHouseResult`, evidence, decisions, failures, and receipts | [PackageHouse](package-house.md), #6622 and #6426 |
| Library | Design-only | `LibraryContentOwner`, `LibraryOperationLease`, accepted Artifact child obligations | `LibraryReference`, `LibraryContentReference`, correspondence and detached outcomes | [Library ownership and borrowing](library-ownership-and-borrowing.md), #6621 |
| Workspace / Library | Compatibility predecessor plus planned target | Current `InspectionWorkspace`; target realization aggregate and accepted Library owners | Resource-free definition, realization identity, immutable definition snapshots, detached results | [Stateless core services](stateless-core-services.md), #6750-#6752 and #6621 |
| PackageHouse / Library | Planned | Transferred `LibraryOperationLease` when content is read | Current `PackageHouseLibraryHandoff` and receipts | [PackageHouse](package-house.md), #6426 and #6621 |
| PlatformHouse / Library | Planned | Transferred `LibraryOperationLease` when content is read | Demands, contributions, operation snapshots, outcomes, and receipts | [PlatformHouse](platform-house-reference-processing.md), #6301 and #6621 |
| SourceHouse / Library | Design-only House; Library adoption planned | Target consumed `LibraryOperationLease` and synchronous content snapshots | Source attempts, provenance, detached source text, failures, and receipts | [SourceHouse](source-house.md), #6512 and #6621 |
| DocumentationHouse / Library | Design-only House; Library adoption planned | Target consumed `LibraryOperationLease` and synchronous content snapshots | Materialized documentation, provenance, conflicts, failures, and receipts | [DocumentationHouse](documentation-house.md), #6579 and #6621 |
| `ArrayPool<T>` comparison | Implemented API-specific Analysis baseline | Rented array return obligation | Analysis occurrences and Findings | [Resource ownership and borrowing](resource-ownership-and-borrowing.md), #6729-#6731 |

## Implemented mappings

### `AssemblyInspectionSession` and `Inspector.Resources`

| Field | Mapping |
| --- | --- |
| Normative owner | [Resource ownership and borrowing](resource-ownership-and-borrowing.md) owns the shared vocabulary; [assembly image lifetime](assembly-image-lifetime.md) owns session image lifetime and correspondence. |
| Current or approved shape | `ResourceOwnershipAttribute`, `ConsumesResourceReceiverAttribute`, `IResourceSnapshotSource<TResource>`, `ResourceSnapshotCallback<TResource, TState, TResult>`, `ReadOnlyResourceSnapshotView<TResource>`, and `AssemblyInspectionSession`. |
| Protected category and lifecycle role | `AssemblyInspectionSession` is a synchronous terminal resource. `Open` acquires an owner. `Borrow(PdbContext)` acquires a lender-bound session obligation. `Snapshot` supplies one synchronous scoped read-only borrow and returns the callback result. Runtime liveness state enforces that lifecycle; the focused owner has not separately classified the session as an exclusive mutable value. |
| State | **Implemented** contract floor. The complete assembly-image lifetime contract retains separately listed unverified gates. |
| Transfer | Ordinary session construction acquires ownership; `Dispose` releases it. Snapshot callbacks borrow rather than transfer. A borrowed session releases only its lender registration. |
| Lifetime and correspondence | One session retains one assembly image, and a borrowed session is bounded by its exact `PdbContext` lender; runtime access fails after either session or lender closes. The broader claim that every producer uses the same image remains **unverified** until the complete [assembly image lifetime](assembly-image-lifetime.md) gates land. |
| Current enforcement and evidence | Ref-like views, `scoped`, and `ReadOnlySpan<T>` enforce the stack-only borrow shape. Runtime liveness checks enforce session and lender state. Release gates include `ResourceOwnershipContract_IsDeclared`, `Snapshot_ReturnsDetachedDataFromTheLiveSession`, `Snapshot_UsesLiveSessionAndReturnsDetachedProjectionWithoutCopyingIt`, `Snapshot_RejectsDisposedSessionBeforeInvokingCallback`, and `BorrowedSessionSnapshot_UsesTheLenderLifetime`. |
| Future compiler correspondence | The session corresponds to a synchronous resource and `Drop`; ordinary receivers and `Snapshot` correspond to borrows. Lender identity and image correspondence remain owner protocol. |
| Asynchronous residual | None for session release. Current C# still cannot prove that every class-valued callback result is detached. |
| Resource-free evidence | A detached callback result. An independently owned result remains ownership-bearing under its own contract. The snapshot view itself is a borrow and cannot escape. |
| Simplification and owner task | Future compiler ownership can prevent copied owners and use after move. Bounded declaration admission is implemented by #6727 and resolver design is locked by #6728; resolver, generalized Analysis, and Research implementation remain #6729-#6732 work. Owner-specific liveness and correspondence remain. |

### Artifact

| Field | Mapping |
| --- | --- |
| Normative owner | [Artifact ownership and borrowing](artifact-ownership-and-borrowing.md), tracked by #6647. |
| Current or approved shape | `ArtifactSetSession`, `IArtifactAcquisitionLease`, `ArtifactAdmissionLease`, `ArtifactQueryLease`, `ArtifactContentLease`, `ArtifactContributionScope`, scoped content views, and explicit-authority compatibility streams. |
| Protected category and lifecycle role | The session is an asynchronous aggregate terminal-resource owner. Acquisition outcomes may carry source leases. Admission and query leases are operation resources. `ArtifactContentLease` is a transferable synchronous child resource. Content views are scoped borrows. Runtime liveness and generation checks enforce those terminal contracts; the focused owner has not separately classified them as exclusive mutable values. |
| State | **Implemented core with compatibility bridges.** `ArtifactContentReference` is resource-free; downstream content-child adoption is incomplete. |
| Transfer | Successful acquisition transfers a source lease into the session. Content issuance transfers one exact child obligation to the caller or downstream aggregate. `Dispose` releases the child; `DisposeAsync` settles the aggregate. |
| Lifetime and correspondence | Every content lease names one exact `ArtifactContentReference`, session, generation, registration, and immutable retained content. Query-policy replacement does not revoke an already-issued content child. |
| Current enforcement and evidence | Ref safety protects scoped views; runtime checks enforce exact reference, generation, authorization, revocation, active-borrow, and release state. Release gates include `ArtifactContentReference_IsResourceFreeExactEvidence`, `ArtifactContentLease_SurvivesQueryReplacementAndPinsRetirement`, `ArtifactContentLease_IssuanceRequiresCurrentQueryAndExactReference`, `ArtifactContentLease_ActiveBorrowPreventsReleaseAndPinsRetirement`, `ArtifactContentLease_NormalReturnWinsLaterCancellation`, and `Digest_RequiresExplicitQueryOrContentAuthority`. |
| Future compiler correspondence | Admission, query, content, and contribution values correspond to synchronous resources; content views correspond to read-only borrows; accepted content children correspond to transitive owned fields. `ArtifactSetSession` and `IArtifactAcquisitionLease` remain asynchronous resources with no synchronous `Drop` correspondence. |
| Asynchronous residual | Session retirement, admitted-access and child drainage, abandonment, acquisition cleanup order, failure observation, and Browser/Wasm-safe settlement remain explicit protocol. |
| Resource-free evidence | `ArtifactContentReference`, identity, descriptor, registration, roles, provenance, digests, detached status outcomes, and receipts. A successful acquisition outcome is not resource-free while it carries its source lease. |
| Simplification and owner task | #6740 removed hidden query-authority capture and parameterless reference open/digest operations. Explicit session-plus-query stream access remains a compatibility bridge until Metadata, Queries, Library, and host adopters consume content children under #6647. |

### Package Source

| Field | Mapping |
| --- | --- |
| Normative owner | [Package source model](package-source-model.md), with operation ownership completed by #6619. |
| Current or approved shape | `PackageSourceSettlementService`, `PackageSourceSettlementLease`, `PackageSourceOperationLease`, package-source authorization, candidates, typed outcomes, and the internal active-work registration. |
| Protected category and lifecycle role | The settlement lease is the asynchronous root terminal resource. The operation lease is the synchronous terminal resource for one top-level package operation. Active awaited work is an ownership effect, not a third resource. Authorization is package-ID policy, not a lease. The focused owner has not separately classified either lease as an exclusive mutable value. |
| State | **Implemented**, with direct root-operation adapters retained as compatibility bridges. |
| Transfer | A synchronous borrow of the live root issues an operation lease. The caller transfers that lease into PackageHouse, package-backed Platform, or another direct consumer. Operation release removes the root registration; observed root settlement completes only after every operation releases. |
| Lifetime and correspondence | The lease owns one root settlement generation and one `NuGetOperationContext`, including caller cancellation and operation deadlines. Package ID, authorization, and candidate are supplied to each operation; owner-issued results preserve their source associations. |
| Current enforcement and evidence | Runtime gates atomically order operation issuance and retirement, reject release during active work, and reject foreign-generation or foreign-authority evidence supplied to an operation. Release gates include `PackageSourceSettlementLeaseIssuanceCannotRacePastSettlement`, `PackageSourceSettlementLeaseSettlementWaitsForIssuedOperations`, `PackageSourceOperationLeaseOwnsOneContextAcrossSequentialSteps`, `PackageSourceOperationLeaseRejectsForeignGenerationEvidence`, `PackageSourceSettlementLeaseRejectsForeignCandidateAndClientAssociation`, `PackageSourceOperationAsyncStateOwnsAuthorityWithoutBorrowEscape`, `PackageSourceOperationLeaseRejectsReleaseWhileAsyncWorkIsActive`, `PackageSourceOperationLeaseCancellationSettlesWorkBeforeRelease`, and `PackageSourceOperationLeaseFailureSettlesWorkBeforeRelease`. |
| Future compiler correspondence | `PackageSourceOperationLease` corresponds to a synchronous resource and consuming parameter. Root settlement and active awaited work have no synchronous `Drop` correspondence. |
| Asynchronous residual | Operation admission, active-work ownership, cancellation, quiescence, root settlement observation and failure, and caller-owned client release ordering remain explicit. |
| Resource-free evidence | Candidates, discovery and manifest evidence, failures, and completed non-payload outcomes retain no root or operation lease. Package source authorization is separate non-resource authority. A payload-bearing result propagates its caller-owned retained payload and is not resource-free merely because its evidence is detached. |
| Simplification and owner task | Adopted consumers use transferred operation leases. Direct root operations remain a compatibility surface for unmigrated adapters; broader package acquisition adoption remains #5400. |

### PackageHouse consumption of Package Source

| Field | Mapping |
| --- | --- |
| Normative owner | [PackageHouse composition](package-house.md), with operation transfer completed by #6622 and broader adoption tracked by #6426. |
| Current or approved shape | `PackageHouse.ExecuteAsync(request, PackageSourceOperationLease)`, `PackageHouseSettlement.ResourceFree`, `PackageHouseSettlement.Acquired`, `PackageHouseResult`, evidence, decisions, failures, and receipts. |
| Protected category and lifecycle role | PackageHouse is a stateless service, not an ownership issuer. `ExecuteAsync` consumes one Package Source operation terminal resource across every awaited step. An acquired settlement separates a caller-owned payload from resource-free result evidence. |
| State | **Implemented** for `Settle` and `Acquire`; `Realize`, pruning, dependency realization, Workspace handoff, and host adoption remain planned. |
| Transfer | The caller transfers the operation lease into `ExecuteAsync`; the House releases it on every terminal path. House results never return or retain that lease. |
| Lifetime and correspondence | The request and PackageHouse's source authorization are separate inputs. The operation lease supplies one settlement generation and operation context; source results supply candidate and authority correspondence. Acquired payload coordinate, producer, origin, and retained-content generation must match the acquisition receipt. |
| Current enforcement and evidence | Current C# does not invalidate the caller's alias after transfer. The implementation uses consuming metadata, runtime association checks, and terminal `using` cleanup. Release gates include `ExactAcquireBindsLivePayloadToResourceFreeReceipt`, `NullRequestReleasesTransferredOperation`, `CallerCancellationRemainsCallerCancellation`, `OperationTimeoutBecomesTypedTerminalFailure`, `UnsupportedRealizeReleasesTransferredOperation`, `ThrownSourceExceptionReleasesTransferredOperation`, `ResultsAndReceiptsRetainNoSourceLeaseAuthority`, and `TerminalResultFamilyIsClosedAndResourceFree`. |
| Future compiler correspondence | The operation parameter corresponds to a consuming resource parameter; House calls borrow the owned value and terminal release corresponds to synchronous `Drop`. PackageHouse and its receipts have no resource correspondence. |
| Asynchronous residual | Awaited source work, cancellation, timeout, exception settlement, payload lifetime, and later root quiescence remain explicit protocol. |
| Resource-free evidence | `PackageHouseResult`, `PackageHouseEvidence`, decisions, candidates, failures, and receipts. `PackageHouseSettlement.Acquired.Payload` remains separately owned. |
| Simplification and owner task | Compiler consumption can prevent caller reuse but cannot replace request, issuer, source, or payload correspondence. Desktop routing, `Realize`, Workspace, CLI, and Browser/Wasm adoption remain #6426. |

## Designed and planned mappings

### Library

| Field | Mapping |
| --- | --- |
| Normative owner | [Library ownership and borrowing](library-ownership-and-borrowing.md), tracked by #6621. |
| Current or approved shape | Approved names are `LibraryReference`, `LibraryContentReference`, `LibraryContentOwner`, and `LibraryOperationLease`, plus owner-controlled synchronous multi-content snapshot callbacks. |
| Protected category and lifecycle role | `LibraryContentOwner` is the approved asynchronous aggregate terminal-resource owner. `LibraryOperationLease` is consuming terminal-resource authority for one async operation. Snapshot views are scoped borrows. References, House contributions, outcomes, and receipts carry no live authority. The focused owner has not separately classified either approved type as an exclusive mutable value. |
| State | **Design-only** and **unverified**. No `DotnetInspector.Libraries` implementation currently supplies these CLR types. |
| Transfer | Construction atomically accepts Artifact-issued content children. The owner issues a fresh operation lease, which transfers into exactly one async operation and settles on every terminal path. |
| Lifetime and correspondence | A reference identifies one exact realized Library registration. Equal source coordinates in different Workspaces produce distinct references and owners. Library preserves source-, Artifact-, and Metadata-issued correspondence without manufacturing it. |
| Current enforcement and evidence | The design requires owner-liveness, exact-reference, content-membership, terminal-path, borrow, detached-result, and asynchronous-drain gates. None establishes implementation yet. |
| Future compiler correspondence | Owner and operation lease correspond to resources; operation parameters consume ownership; snapshots are read-only borrows; accepted Artifact children are transitive owned resources. |
| Asynchronous residual | Lease admission, owner retirement, active-operation drainage, aggregate child release, abandonment, and settlement failure remain explicit. |
| Resource-free evidence | `LibraryReference`, `LibraryContentReference`, correspondence, House contributions, detached operation results, and receipts. |
| Simplification and owner task | One shared Library owner replaces Package-, Platform-, Workspace-, Source-, and Documentation-specific lifetime wrappers. #6621 owns implementation and five focused adopter slices. |

### Workspace and Library

| Field | Mapping |
| --- | --- |
| Normative owner | Workspace Definitions, Workspace Scope, and Artifact Acquisition own focused behavior. [Stateless core services](stateless-core-services.md) owns only the cross-owner definition/realization/operation association. |
| Current or approved shape | Current compatibility types include `InspectionWorkspace`, `InspectionWorkspaceIdentity`, `WorkspaceScopeSnapshot`, and mutation operations. The target roles are a resource-free definition, one active realization, immutable definition snapshots, append-only Add, and realization-and-snapshot-bound operation authority. Those roles are not final CLR names. |
| Protected category and lifecycle role | The active realization is the planned physical aggregate terminal-resource owner for realized Libraries, Artifact sessions, assembly groups, correctness indexes, admitted operations, and drainage. The definition, identity, and immutable snapshots are resource-free evidence. The Workspace owners have not separately classified the realization as an exclusive mutable value. |
| State | Current Workspace ownership is **implemented compatibility behavior**. Definition/realization separation, Add-only admission, Library adoption, and cutover are **planned** and **unverified**. |
| Transfer | The target realization accepts realized Library owners. Selecting another definition creates a fresh realization; no Artifact, Library, group, index, lease, or authority transfers from the predecessor. |
| Lifetime and correspondence | Every operation identifies the exact realization and immutable definition snapshot that admitted it. Selecting equal coordinates later still creates fresh realization identity. |
| Current enforcement and evidence | Existing Release gates such as `AddPreservesExistingOrderAndAppendsOneDistinctBatch`, `RemoveRetainsOtherOccurrencesAndDoesNotWaitForAnAdmittedQuery`, and `WorkspaceClose_RejectsAdmissionAndRoutesLateGroupToRelease` establish current Scope mutation and close behavior only. #6750-#6752 must provide the target model and gates. |
| Future compiler correspondence | The realization and accepted Library owners are aggregate resources; operation authority may become consuming; synchronous content access may become borrow-derived. Definitions, identities, snapshots, and detached results have no ownership role. |
| Asynchronous residual | Candidate construction, single-active cutover, predecessor admission closure, already-admitted work, drainage, failure, and visible settlement remain explicit protocol. |
| Resource-free evidence | Workspace definition, realization identity, definition snapshots and revisions, component coordinates, and detached operation results. |
| Simplification and owner task | #6750 separates definitions and realization identity; #6751 replaces update/removal policy with Add and `Added`/`AlreadyPresent`; #6752 owns cutover and drainage; #6621 owns Library adoption. |

### Library adopters

These services consume Library-issued authority. They do not issue a
House-, Workspace-, source-, or documentation-named substitute lease.

| Adopter | Current or approved shape | State and evidence | Ownership mapping | Owner task |
| --- | --- | --- | --- | --- |
| PackageHouse | Current `PackageHouseLibraryHandoff` and receipts are resource-free package/asset correspondence; `Realize` is not implemented. | **Planned** Library adoption. `ResultsAndReceiptsRetainNoSourceLeaseAuthority` and `TerminalResultFamilyIsClosedAndResourceFree` gate only the current handoff and Package Source operation floor. | Future content-reading operations consume `LibraryOperationLease`; results remain detached. | #6426 and #6621 slice 4 |
| PlatformHouse | Current demands, operation snapshots, contributions, outcomes, and receipts preserve target/source/view correspondence without carrying source handles. | **Planned** Library adoption. `ClosedOperations_SeparateLiveInputsFromReceiptIdentities` and `Contributions_RetainOnlyResourceFreeRequestAndEvidence` gate current PlatformHouse evidence, not Library ownership. | Future Library realization or content-reading operations use the shared Library owner/reference/lease rather than a bare-Library lifetime wrapper. | #6301 and #6621 slice 5 |
| SourceHouse | Current `PdbSourceHouse` and `AssemblyContextSourceQuery` are compatibility evidence; no `SourceHouse` CLR type exists. | The House design is **design-only**; shared Library adoption is **planned** and **unverified**. Its current source-ready/lifetime wording requires reconciliation. | Target: consume one `LibraryOperationLease` across the async operation, borrow assembly/PDB content synchronously, and return detached source evidence. | #6512 and #6621 slice 7 |
| DocumentationHouse | Current Platform documentation contracts, `SourceEnricher`, and `DocCommentParser` are migration evidence; no `DocumentationHouse` CLR type exists. | The House design is **design-only**; shared Library adoption is **planned** and **unverified**. Its documentation-ready terminology requires reconciliation. | Target: consume one `LibraryOperationLease`, borrow XML/assembly/PDB content synchronously, and return materialized documentation and resource-free receipts. | #6579 and #6621 slice 8 |

PackageHouse and PlatformHouse keep their package/platform correspondence.
SourceHouse and DocumentationHouse keep their producer, channel, provenance,
and failure policy. The shared Library contract replaces only duplicated
content-lifetime ownership.

## Analysis comparison

`ArrayPool<T>` is the implemented API-specific baseline for the Analysis side
of the protocol. The rented array carries a return obligation, and
`ArrayPool<T>.Return` is its release operation. Current
`ArrayPoolOwnershipFlow` and Resource Lifecycle Analysis can report supported
return, storage, caller-transfer, forwarding, and exception-path evidence.

The Analysis types that describe this behavior are not themselves resources:

- `ResourceEffectModelReceipt`, `ResourceEffectAdmissionReceipt`, and
  `AdmittedResourceEffectModel` are immutable declaration evidence;
- `ResourceBoundaryEvidence` and `ResourceLifecycleOccurrence` are detached
  observations; and
- Findings are product results, not leases.

Bounded declaration admission is implemented under #6727. Concrete metadata
resolution, generalized lifecycle flow, retirement of the ArrayPool-specific
semantic path, and Research migration remain #6729-#6732 work. Future compiler
ownership may provide stronger facts about the inspected resource operations;
it does not turn Analysis receipts or Findings into resources.

The current Resource Effect Language and generalized lifecycle plan cover
terminal obligations. They do not yet claim support for an exclusive mutable
value with no release effect. #6778 owns that focused declaration-language
extension, #6780 owns concrete effect resolution, and #6779 owns Analysis
adoption. Until all three land, the package resolution aliasing case is
architectural evidence and an explicit **unverified** Analysis category, not a
capability attributed to the current language.

Exact Release gates for the current baseline include
`Admission_ReceiptsAreDeterministicAndPreserveInputOrder`,
`Admission_ExactReceiptPreservesProvenanceAssociations`,
`ScopedOwnershipFlowRetainsTheRentBoundary`,
`FullOwnershipFlowClassifiesParameterEffects`,
`AddressTakenRentIsRetainedAsIncomplete`,
`ResourceLifecycleAnalysis_ReportsExactBoundaryEvidence`,
`ResourceLifecycleAnalysis_ReportsInspectionFailure`, and
`ResourceLifecycleAnalysis_ConsumesSharedBodyIndex`. Generalized
declaration-driven resource flow remains **unverified** until #6729-#6732 land.

## Retained state that is not a resource row

The stateless-core migration classifies behavior-bearing retained observations
without inventing release obligations for caches or memoizers.

| Current state | Classification | Future ownership correspondence | Focused task |
| --- | --- | --- | --- |
| `AnalysisIndexCache` | Current static derived-evidence cache. `AnalysisIndexCache_UsesMemberScopeAndReusesCompatibleFullIndex` and the `AnalysisIndexCache_ForPath_*` gates cover selected key/reopen behavior; exact-generation reuse, Workspace replacement isolation, and the open-duration mutation case remain **unverified**. Target lifetime is one operation or exact realization. | None. The supplying operation or realization may own the evidence, but the cache is not a lease. | #6754 |
| `ResearchAssemblyContextCache` | Current static exact-index memoizer. `MemberProjection_CollectsFactsOnceForRequestedViews` and `RequirementsNone_DoesNotResolveAnAssemblyContext` cover selected request reuse; release eligibility and cross-generation isolation remain **unverified**. | None. Exact-object memoization is not ownership transfer. | #6755 |
| `PlatformTypeCatalog` | Current static path-keyed filesystem observation. `PlatformTypeCatalog_UnavailableDirectory_IsRetried`, `PlatformTypeCatalog_EmptyDirectory_IsRejectedAndRetried`, `LookupType_ResolvesDefinitionDeterministically`, and `LookupType_UnqualifiedCollision_ReturnsOrderedAmbiguity` cover retry and selection; exact platform/generation identity and Workspace replacement remain **unverified**. | None. Catalog evidence is not a resource obligation. | #6756 |
| `PersistentCache` | Implemented filesystem mechanism for the optional host-supplied persistent-cache port. `GetFilePath_UsesStableSha256HashAndBucketsUnderCategory`, `SetAndTryGet_RoundTripAndReportStoreThenHit`, `TryGet_ExpiredEntryReturnsMiss`, `TryGet_ReadFailureIsBestEffortAndNeverReportsHit`, and `RegisteringVersionedCategory_CleansOnlyOlderContracts` gate the mechanism. Category semantic correctness remains with each owner. | None. Cache hits cannot issue Workspace identity, realization generation, correspondence, or live authority. | [Stateless core services](stateless-core-services.md) and category owners |

These rows matter to service orientation, but they do not belong in the
resource-owner index merely because they retain memory, files, tasks, or
observations.

## Current C# and future compiler ownership

| Contract concept | Current C# representation | Possible future compiler role | Required residual protocol |
| --- | --- | --- | --- |
| Exclusive mutable owner without terminal release | Explicit owner type, encapsulated mutation, and preferably a fresh immutable detached result at publication; no `IDisposable` requirement | Move-only value and owner-derived mutable or read-only borrows where supported; the pinned proposal does not directly cover this category | Identity, mutation policy, detachment boundary, stale-alias rejection, and #6778/#6780/#6779 declaration, resolution, and Analysis support |
| Synchronous terminal resource | `IDisposable` when its semantics fit, or an owner-specific acquisition/release pair such as `ArrayPool<T>.Rent`/`Return`; explicit fields, lexical cleanup where available, runtime validation, and declared effects | Move-only `IResource` and compiler-invoked `Drop` where the terminal contract fits | Issuer identity, generation, correspondence, rejection, cleanup failure, and any release-pair authority |
| Consuming transfer | Consuming metadata plus an ordinary parameter or static/extension receiver | Compiler-enforced consuming parameter or receiver where a future language covers the value; the pinned proposal directly covers its `IResource` subset | Fallible acceptance, result ownership, and owner-specific transfer outcome |
| Read-only or mutable borrow | `readonly ref struct`/`ref struct`, `scoped`, `ReadOnlySpan<T>`/`Span<T>`, synchronous callback | `ReadOnlyBorrow<T>`/`Borrow<T>` and owner-derived result lifetime where a future language covers the value | Owner liveness and exact borrowed-content correspondence |
| Aggregate child | Explicit field or collection plus acceptance and cleanup code | Transitive owned field where supported | Dynamic collection accounting, release order, partial failure, and child identity |
| Asynchronous owner | `IAsyncDisposable`, operation registration, explicit drain task and awaited observation | No approved asynchronous `Drop` correspondence | Admission closure, active work, abandonment, quiescence, fault/cancellation, and observed settlement |
| Reference or receipt | Immutable identity/evidence value with no opener, callback, service, or lease | None | Owner-issued correspondence and visible stale/foreign rejection |

The map never upgrades a design declaration into compiler enforcement.
Current C# can still copy mutable class references, retain aliases after
conceptual transfer, reuse one object across operations, and drop a
`DisposeAsync` awaitable. Focused runtime gates and Resource Lifecycle Analysis
cover only their declared supported subsets and must report unsupported proof
as incomplete.

## Maintenance

When a focused owner changes:

1. update the owner document and its evidence first;
2. update this map only after the owner-issued shape is established;
3. preserve the distinction among implemented, design-only, compatibility, and
   planned states;
4. link the exact Release gate or state `unverified`;
5. do not infer ownership from a type name, wrapper, cache, or
   `[ResourceOwnership]` declaration alone; and
6. file a focused owner task for any contradiction instead of resolving it in
   this map.

The map may close while downstream adopters remain planned. It records current
truth and owner links; it is not an umbrella implementation tracker.

## Validation

This document changes no product behavior. Its current-state claims are
supported by the focused owner documents and cited Release suites; planned
Library, Workspace, House, retained-state, and generalized Analysis claims
remain explicitly **unverified** until their owner issues land.

## Non-goals

- A normative cross-owner lifecycle or stateless-service design.
- A generic owner, lease, borrow, result, settlement, or cache framework.
- Concrete Workspace CLR names before #6750-#6752 choose them.
- A claim that Library or its House adopters are implemented.
- Treating caches, memoizers, catalogs, receipts, outcomes, or Findings as
  ownership-protected merely because they retain data, or as terminal resources
  without an owner-issued release obligation.
- Compiler implementation or repository substitutes for proposed C# ownership
  syntax.
- An asynchronous `Drop` assumption.
- A repository-wide ownership migration or absence claim.
