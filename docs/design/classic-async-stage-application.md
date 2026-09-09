# Classic async stage application

Classic async stage application is the Decompiler-owned boundary that decides
whether `ClassicAsyncReconstructionPass` may edit the imported host method. It
preserves physical execution and support bodies and admits only the declared
kickoff to the existing proof-carrying inverse.

## Status and owner

This document owns stage application for
[#5292](https://github.com/richlander/dotnet-inspect/issues/5292). It consumes:

- the exact requested-method identity and authenticated host role from the
  [classic async request adapter](classic-async-request-adapter.md); and
- the terminal decision from the
  [classic async inverse core](classic-async-reconstruction.md).

The named consumer is `ClassicAsyncReconstructionPass` in the shared
`ILInspector.Decompiler` pipeline. That pipeline is already consumed by the CLI
and browser/Wasm body-production paths tracked by
[#4472](https://github.com/richlander/dotnet-inspect/issues/4472); this owner
requires no host-specific adapter or rendering policy.

## Claim

An owner-issued exact classic `Execution` or `Support` role is physical
artifact evidence, not a declared-method reconstruction target. Running the
classic stage preserves that host's imported body and local table unchanged.
Only an exact `DeclaredKickoff` with `RequestAvailable` may consume a terminal
inverse decision and mutate into a reconstructed body.

Method names, generated-type spelling, builder-field names, and body shape do
not grant stage-application authority.

## Input boundary

For one imported `IrFunction`, stage application receives:

- the imported body and local table at classic-pass entry;
- `ClassicAsyncRequestAdapterResult?`, including the requested
  `MetadataMethodAddress`, authenticated `ClassicAsyncHostRole`, relationship
  result, and acquisition guard when available; and
- the stage invocation context used by the existing cross-method pipeline.

The adapter result is consumed unchanged. Stage application does not rebuild a
relationship or infer a role from IR.

## Closed application result

The policy emits one `ClassicAsyncStageApplicationKind`:

| Result | Input | Stage behavior |
| --- | --- | --- |
| `PreserveImportedBody` | A resolved classic relationship whose requested MethodDef has exact role `Execution` or `Support` | Return without editing the body or local table. |
| `EvaluateDeclaredKickoff` | `RequestAvailable` for exact role `DeclaredKickoff` | Continue through the existing kickoff correlation and inverse decision path. |
| `NoOpinion` | Missing, unavailable, failed, ordinary, non-classic, or otherwise inapplicable evidence | Return before cross-method acquisition and grant no classic mutation authority. |

`Filtered` is not synonymous with absence. When it retains a resolved classic
execution or support role, the role deliberately selects
`PreserveImportedBody`. A lookalike that has no such owner-issued role remains
`NoOpinion`, even when its name and fields match compiler conventions.

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
  compiler-produced exact `MoveNext` keeps its body tree, local table, names,
  eliminated slots, diagnostics, and modifier state;
- `ExactClassicSupportRole_PreservesImportedStageSnapshot` proves the same
  invariant for an artifact-present exact `SetStateMachine`;
- `ExplicitMethodImplRole_PreservesImportedStageSnapshot` proves exact
  execution/support roles survive noncanonical explicit-implementation names;
- `SharedPipeline_ExactClassicExecutionRoleRetainsPhysicalBody` proves the
  preserved execution host still renders its physical completion and failure
  paths through the shared decompiler pipeline;
- `BuilderShapedMoveNextWithoutOwnerRole_IsPreserved` and
  `BuilderShapedSetStateMachineWithoutOwnerRole_IsPreserved` prove names,
  generated-type metadata, and builder-field shape grant no mutation
  authority;
- `OrdinarySameNamedMethod_HasNoClassicMutationAuthority` proves direct
  same-named import remains an ordinary no-edit result; and
- `AsyncIteratorExecutionRole_HasNoClassicMutationAuthority` proves the classic
  pass performs no edit for another state-machine family's exact role;
- `KickoffShapeWithoutOwnerRole_DoesNotEnterSiblingImport` proves `NoOpinion`
  returns before cross-method acquisition;
- `PreserveArm_ReturnsBeforeKickoffAcquisition` proves preservation returns
  before an otherwise kickoff-shaped host can enter cross-method acquisition;
  and
- `FaithfulLegacyRecipeRemainsFullyReconstructed` keeps authenticated declared
  kickoffs on the terminal inverse-decision path.

These run in `tests/ILInspector.Decompiler.Tests` with Release configuration.

## Non-claims

This owner does not define Metadata relationship construction, request
adaptation, classic recipe recognition, proof ledgers, nested local-function or
lambda embedding, member/type result projection, declaration
modifier/disposition policy, C# rendering, or host presentation. Those remain
with #5277, #5276, #5278, #5279, #5293, and their owning documents.
