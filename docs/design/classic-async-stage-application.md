# Classic async stage application

Classic async stage application is the Decompiler-owned boundary that decides
whether `ClassicAsyncReconstructionPass` may edit the imported host method. It
admits only an authenticated declared kickoff to the existing proof-carrying
inverse and preserves every other imported body by default.

## Status and owner

This document owns stage application for
[#5292](https://github.com/richlander/dotnet-inspect/issues/5292). It consumes:

- the authenticated declared-kickoff request, when available, from the
  [classic async request adapter](classic-async-request-adapter.md); and
- the terminal decision from the
  [classic async inverse core](classic-async-reconstruction.md).

The named consumer is `ClassicAsyncReconstructionPass` in the shared
`ILInspector.Decompiler` pipeline. That pipeline is already consumed by the CLI
and browser/Wasm body-production paths tracked by
[#4472](https://github.com/richlander/dotnet-inspect/issues/4472); this owner
requires no host-specific adapter or rendering policy.

## Claim

Only `RequestAvailable` for an authenticated declared kickoff grants the
classic pass mutation authority. Every other imported method keeps its body
and local table unchanged at this stage. That default preserves exact classic
execution and support methods without requiring the Decompiler to rediscover
or attach their Metadata roles merely to avoid an edit.

Method names, generated-type spelling, builder-field names, and body shape do
not grant stage-application authority.

## Input boundary

For one imported `IrFunction`, stage application receives:

- the imported body and local table at classic-pass entry;
- `ClassicAsyncRequestAdapterResult?`, including the authenticated declared
  kickoff request and acquisition guard when available; and
- the stage invocation context used by the existing cross-method pipeline.

The adapter result is consumed unchanged. Stage application does not rebuild a
relationship, scan `MethodImpl` rows, or infer a role from names or IR.

## Closed application result

The policy emits one `ClassicAsyncStageApplicationKind`:

| Result | Input | Stage behavior |
| --- | --- | --- |
| `PreserveImportedBody` | Missing, unavailable, failed, filtered, or otherwise inapplicable declared-kickoff request | Return without editing the body or local table. |
| `EvaluateDeclaredKickoff` | `RequestAvailable` for exact role `DeclaredKickoff` | Continue through the existing kickoff correlation and inverse decision path. |

Directly imported synchronous methods, including classic `MoveNext` and
`SetStateMachine`, do not force the module-wide relationship index merely to
select the no-edit result. Their physical role remains Metadata-owned and can
be reached through the relationship certificate when a consumer needs it.

## Preservation invariant

For `PreserveImportedBody`, this pass:

- keeps the same `BlockContainer`, blocks, and descendant nodes;
- keeps `Locals`, recovered and synthesized local names, nested-scope evidence,
  and eliminated-slot evidence; and
- does not set method modifiers or add, remove, or replace diagnostics.

The stage performs no compensating rewrite and emits no success-shaped empty
body. Later ordinary passes may process the preserved IR under their own
contracts.

An authenticated support role may be absent from the artifact. In that case
there is no host MethodDef to import or preserve; absence does not authorize a
fabricated support body.

## Declared kickoff application

`EvaluateDeclaredKickoff` does not itself license reconstruction. The pass
still correlates the decoded kickoff state-machine type with the exact Metadata
relationship, imports the certified execution MethodDef, and asks
`ClassicInverseCore` for its terminal decision.

- `Reconstruct` installs the detached plan through the existing application
  path.
- `Decline` preserves the kickoff or marks a visible unsupported boundary
  according to the inverse contract.
- `Failed` remains a visible planning failure.

Stage application neither broadens the recipe domain nor translates a decline
or failure into reconstruction.

## Evidence

The following Release gates enforce this owner:

- `ExactClassicExecutionRole_PreservesImportedStageSnapshot` proves a
  Metadata-certified `MoveNext` imported directly keeps its body tree, local
  table, names, eliminated slots, diagnostics, and modifier state;
- `ExactClassicSupportRole_PreservesImportedStageSnapshot` proves the same
  invariant for a Metadata-certified, artifact-present `SetStateMachine`;
- `ExplicitMethodImplRole_PreservesImportedStageSnapshot` proves exact
  execution/support methods with noncanonical explicit-implementation names
  receive the same default preservation;
- `SharedPipeline_ExactClassicExecutionRoleRetainsPhysicalBody` proves the
  preserved execution host still renders its physical completion and failure
  paths through the shared decompiler pipeline;
- `BuilderShapedMoveNextWithoutOwnerRequest_IsPreserved` and
  `BuilderShapedSetStateMachineWithoutOwnerRequest_IsPreserved` prove names,
  generated-type metadata, and builder-field shape cannot bypass the request
  gate;
- `OrdinarySameNamedMethod_HasNoClassicMutationAuthority` proves direct
  same-named import receives the default no-edit result;
- `AsyncIteratorExecutionRole_HasNoClassicMutationAuthority` proves the classic
  pass performs no edit for another state-machine family's exact role;
- `KickoffShapeWithoutOwnerRequest_DoesNotEnterSiblingImport` proves generated
  kickoff shape cannot enter cross-method acquisition;
- `PreserveArm_ReturnsBeforeKickoffAcquisition` proves preservation returns
  before an otherwise kickoff-shaped host can enter cross-method acquisition;
  and
- `FaithfulLegacyRecipeRemainsFullyReconstructed` keeps authenticated declared
  kickoffs on the terminal inverse-decision path.

These run in `tests/ILInspector.Decompiler.Tests` with Release configuration.

## Non-claims

This owner does not define Metadata relationship construction or
implementation-role discovery, request adaptation, classic recipe recognition,
proof ledgers, nested local-function or lambda embedding, member/type result
projection, declaration modifier/disposition policy, C# rendering, or host
presentation. Those remain with #5277, #5276, #5278, #5279, #5293, and their
owning documents.
