# Classic async request adapter

The classic async request adapter is the Decompiler-owned boundary between
Metadata's state-machine relationship certificate and the classic inverse. It
preserves owner identity and failure while allowing only one authenticated
classic kickoff to seed a reconstruction request.

## Status and decision

Implemented by
[#5277](https://github.com/richlander/dotnet-inspect/issues/5277).
This document owns import adaptation. Metadata continues to own relationship
construction and rejection; the
[classic async inverse](classic-async-reconstruction.md) owns body semantics
and reconstruction proof.

## Input

For each async-classified imported MethodDef, the adapter receives:

- its durable `MetadataMethodAddress`;
- Metadata's exact `MethodClassification?`;
- the exact `StateMachineRelationshipResult` selected by kickoff first and
  implementation role second; and
- an acquisition guard for the live `MetadataSource`.

The adapter does not recreate a relationship from generated names, kickoff IL,
or imported IR. `Resolved`, `Absent`, and `Rejected` remain the same immutable
owner values after import.

## Closed result

The adapter emits one `ClassicAsyncRequestAdapterResult`:

| Result | Meaning |
| --- | --- |
| `RequestAvailable` | The imported method is the exact declared kickoff of a resolved classic relationship. The request seed carries the exact kickoff, certified `MoveNext`, relationship, and acquisition guard. |
| `OwnerUnavailable` | Owner evidence needed for a classic request is absent or rejected. A `BudgetExceeded` rejection always takes this arm before classification filtering. |
| `Filtered` | The owner result is available, but the method is runtime async, an async iterator, a support/execution method, or otherwise outside the classic inverse. |
| `AcquisitionFailed` | The MethodDef cannot receive a durable module-scoped address. The raw token, classification, relationship result, acquisition guard, and failure detail remain visible, but no request identity is fabricated. |

Every arm that has a durable address carries `ClassicAsyncOwnerEvidence`:
requested identity, host role, classification, relationship result, and
acquisition guard. Filtering is a typed result, not missing evidence.
`AcquisitionFailed` is separate because malformed module identity makes that
address impossible.

The precedence is:

1. preserve `Rejected(BudgetExceeded)` as `OwnerUnavailable`;
2. filter classifications other than `StateMachineAsync`;
3. filter non-kickoff roles;
4. admit only `Resolved(ClassicAsync)` with
   `MoveNext: Present(Method)`; and
5. preserve a remaining absent or rejected owner result as
   `OwnerUnavailable`.

This ordering is required because runtime-async classification has precedence
inside `MethodClassificationScanner`. A malformed input may therefore be both
classified `RuntimeAsync` and carry an exact classic relationship claim whose
bounded owner scan rejects with `BudgetExceeded`. Filtering classification
first would erase that owner failure.

## Import and consumption

`MethodImporter` materializes the adapter result for methods classified
`RuntimeAsync` or `StateMachineAsync` while the source is live, and
`IrImporter` carries it unchanged onto `IrFunction`. Synchronous methods do not
force the module-wide relationship index and remain outside this boundary. The
immutable Metadata addresses and relationship result remain inspectable after
reader disposal; the acquisition guard is an opaque provenance token, not a
reader reference.

For `RequestAvailable`, `ClassicAsyncReconstructionPass` imports the certified
execution MethodDef by its exact address and matching acquisition guard. The
generated name remains only a display label. The pass also requires the kickoff
body's decoded state-machine local to carry the same module and TypeDef token
as the certificate. A name-equal sibling cannot replace either identity.

Synthetic IR continues to exercise recipe recognition without claiming
Metadata identity. Product imports carry an explicit `IsMetadataBacked` fact
and therefore cannot enter the classic pass without `RequestAvailable`.

## Evidence

The following gates enforce the boundary:

- `Import_CarriesAuthenticatedClassicRequestSeed` proves exact identity,
  classification, relationship, and post-disposal materialization;
- `ClassicPass_ImportsCertifiedExecutionMethod` proves the cross-method import
  uses the certified `MoveNext` address;
- `RuntimeAsyncBudgetFailureSurvivesProductionImport` proves budget failure
  precedes runtime filtering on the production import path;
- `InvalidModuleIdentityRemainsVisibleAcquisitionFailure` proves nil,
  out-of-range, and overflow-wrapping module identities cannot become absence
  or crash the ordinary import path;
- `IdenticalSource_ProducesClassicAndRuntimeAsyncPhysicalShapes` proves classic
  lowering forms a request while runtime lowering is filtered; and
- `TrimmedArtifactWithoutRolePreservation_AuthenticatesAbsentSupport` proves
  ordinary trimming still forms a request with
  `SetStateMachine: AbsentFromArtifact`.

## Non-claims

This boundary does not prove kickoff or execution body semantics, recognize a
classic recipe, account for physical or semantic regions, embed foreign bodies,
project member results, mutate generated support bodies, or decide declaration
disposition. Those remain with #5276, #5278, #5279, #5292, and #5293.
