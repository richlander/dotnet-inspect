# Resource-owner type map

Status: **non-normative current-state index**. [#6654](https://github.com/richlander/dotnet-inspect/issues/6654)
established the index; [#7063](https://github.com/richlander/dotnet-inspect/issues/7063)
defines its compact maintenance boundary.

This document answers one routing question: for each resource boundary, what
is its current maturity, live authority, detached evidence, normative owner,
and open adoption task?

The focused owner linked by each row remains normative. This index does not
define or independently evidence construction, acquisition, transfer,
borrowing, release, settlement, identity, correspondence, caching, House,
Workspace, or Analysis behavior.

## Maturity

| State | Meaning |
| --- | --- |
| **Implemented** | The focused owner identifies a shipping type or operation and its Release evidence. |
| **Design-only** | A focused owner has approved the type or role, but production implementation and Release evidence have not landed. |
| **Compatibility bridge** | Shipping behavior remains temporarily useful but is not the target ownership shape. |
| **Planned** | The focused owner or adopter has not yet selected or implemented the concrete type shape. |

A row may combine states when its resource-free floor has landed but its live
ownership target has not. The maturity cell is routing metadata, not a
substitute for the focused owner's exact status, gates, or residual gaps.

## Owner index

| Resource boundary | Maturity | Live authority or owner | Detached or resource-free evidence | Normative owner and open task |
| --- | --- | --- | --- | --- |
| `AssemblyInspectionSession` | **Implemented** floor | Session owner, lender-bound borrowed session, and scoped snapshot view | Detached snapshot callback result | [Assembly image lifetime](assembly-image-lifetime.md) |
| Artifact | **Implemented** core with **compatibility bridges** | `ArtifactSetSession`; acquisition, admission, query, and content leases; contribution scope | `ArtifactContentReference`, identity, registration, roles, provenance, digests, status, and receipts | [Artifact ownership and borrowing](artifact-ownership-and-borrowing.md), #6647 |
| Package Source | **Implemented** | `PackageSourceSettlementLease` and `PackageSourceOperationLease`; active awaited work remains an ownership effect | Producer identities and portable correspondence tokens; candidates and completed non-payload evidence; authorization remains separate non-resource authority | [Package source model](package-source-model.md), #5400 |
| PackageHouse / Package Source | **Implemented** consumption and configured package Root contribution | Consumed `PackageSourceOperationLease`; acquired payload remains separately caller-owned | `PackageHouseResult`, decisions, failures, receipts, and `PackageHouseRootContribution` | [PackageHouse](package-house.md), #6426, #6946, and #6967 |
| Library | **Implemented** owner and reference floor | `LibraryContentOwner`, `LibraryOperationLease`, and accepted Artifact content-child obligations | `LibraryReference`, `LibraryContentReference`, closed roles, Artifact/Metadata correspondence, and typed operation-issuance outcomes | [Library ownership and borrowing](library-ownership-and-borrowing.md), #6621 and #7033 |
| Workspace / Library | **Compatibility bridge** plus **planned** target | Current `InspectionWorkspace`; target active realization accepts and owns realized Library owners | Workspace definition, realization identity, immutable definition snapshots, and detached results | [Stateless core services](stateless-core-services.md), #6751 and #6621 |
| PackageHouse / Library | **Implemented** compile-handoff adoption | `PackageHouseLibraryMaterializationOutcome.Completed` transfers separate `LibraryContentOwner` and `ArtifactSetSession` authorities; consumers read through `LibraryOperationLease` | `PackageHouseLibraryHandoff`, materialization receipt, per-content provenance, and terminal failure evidence | [PackageHouse](package-house.md), #7324 and #6621 slice 4; Workspace and host adoption remain under #6426 |
| PlatformHouse / Library | Adopter design approved; production adoption **planned** | Target constructs and transfers `LibraryContentOwner`; internal reads use fresh `LibraryOperationLease` values | Demands, contributions, operation snapshots, outcomes, and receipts | [PlatformHouse](platform-house-reference-processing.md), #6301 and #6621 slice 5 |
| SourceHouse / Library | House **design-only**; Library adoption **planned** | Target consumes one `LibraryOperationLease` and borrows content synchronously | Source attempts, provenance, detached source text, failures, and receipts | [SourceHouse](source-house.md), #6512 and #6621 slice 7 |
| DocumentationHouse / Library | Compiled-XML core, PackageHouse and direct-Library contribution adapters, and the shared Queries result **implemented**; source/provider and host adoption **planned** | The adapters retain no authority; `DocumentationHouse.ExecuteAsync` consumes one separately issued `LibraryOperationLease`, borrows exact XML content synchronously, and settles before return. Queries copies the settlement into a portable JSON snapshot that retains no Library or Artifact authority. | Package- or direct-Library-bound source-neutral contributions, materialized compiled documentation, typed attempts, failures, work evidence, resource-free receipts, and a Queries-owned serialization snapshot | [DocumentationHouse](documentation-house.md), #6579 slices 3-6 and #6621 slice 8 |
| Queries API coordinate correspondence | **Implemented** detached boundary | `InspectionWorkspace`, structural subjects, package observations, and the live `ApiCoordinateCorrespondenceResult` | `ApiCoordinateCorrespondenceEvidence`, detached declaration and Library-pairing evidence, and detached Type-resolution route | [Forwarded API coordinate correspondence](forwarded-api-coordinate-correspondence.md), #7337 and #7248 |
| Analysis `ArrayPool<T>` comparison | **Implemented** API-specific baseline | Rented array and its return obligation | Resource-effect receipts, lifecycle occurrences, and Findings | [Resource ownership and borrowing](resource-ownership-and-borrowing.md), #6729-#6732 |

## Boundaries this index does not duplicate

[Resource ownership and borrowing](resource-ownership-and-borrowing.md) owns
the shared meanings of protected categories, owners, transfer, borrows,
leases, receipts, and current or possible future enforcement. Each focused
owner above owns its lifecycle protocol, correspondence, evidence, and
residual gaps.

Retained state is not a resource boundary merely because it caches memory,
files, tasks, or observations. [Stateless core
services](stateless-core-services.md) owns the cross-owner classification;
[Analysis Index Cache Ownership](analysis-index-cache.md),
[Research Assembly-Context
Ownership](research-assembly-context-ownership.md), and
[Platform Type Catalog
Retention](platform-type-catalog-retention.md) own their focused behavior.
Those services do not receive duplicate rows here unless an owner later
introduces a live authority or release obligation.

## Maintenance

When a focused owner changes:

1. Update the owner document and its evidence first.
2. Update this index only when the boundary, coarse maturity, live authority,
   detached evidence, normative owner, or active task changes.
3. Do not copy owner algorithms, gate names, transition rules, or residual-gap
   inventories into this index.
4. File a focused owner task for a contradiction instead of resolving it here.
5. Do not add an owner row solely because a service retains state.

The index may close while downstream adopters remain planned. It records
current routing facts; it is not an umbrella implementation tracker.

## Validation

This document changes no product behavior and adds no independent evidence
claim. Follow the focused owner for the exact contract and Release gates behind
each maturity entry.

## Non-goals

- A normative cross-owner lifecycle or stateless-service design.
- A generic owner, lease, borrow, result, settlement, or cache framework.
- Concrete Workspace CLR names before #6750-#6752 choose them.
- Treating caches, memoizers, catalogs, receipts, outcomes, or Findings as
  ownership-protected merely because they retain data.
- Compiler implementation or repository substitutes for proposed C#
  ownership syntax.
- A repository-wide ownership migration or absence claim.
