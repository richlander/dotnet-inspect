# Inspect Web Type and Member Source state

## Owned claim

Type Source and Member Source each have one feature-owned result state. Their
variants distinguish unattempted, pending, available, and failed results while
carrying only the request identity and result evidence valid in each state.

This document owns those two result states, their state-dependent presentation,
and their shared cancellation and publication contract. Source acquisition,
PDB/decompiler fallback, source document rendering, navigation, the managed
Type Source operation boundary, and Graph Source state remain unchanged.

## State contract

Each result state has four variants:

- `idle` carries no request or result;
- `loading` carries the exact request signature;
- `ready` carries that signature and the source result; and
- `failed` carries that signature and failure text, including an empty string.

Type and Member Source retain independent values so a settled result remains
cached while the user visits the other surface. A request needs loading only
when its state is idle or its signature differs. Ready and failed states for
the same signature are settled; explicit cache invalidation or a different
signature admits new work.

The source coordinator continues to own mutual exclusion across Type, Member,
and Graph Source loading. Starting or canceling competing work settles
interrupted Type or Member Source loading to idle without discarding settled
results. A canceled Graph Source load retains its existing cancelled state.

Member Source's current loading object is its publication receipt. Success or
failure may publish only while that exact object remains current and the
selected member still matches. Type Source instead publishes through the
existing operation-authority feature events, which associate each transition
with the current managed operation.

A workspace snapshot has no pending source operation. Snapshot normalization
therefore settles Type or Member Source loading to idle while preserving ready
and failed states. A failed state with empty text remains failed and renders
the visible surface fallback instead of becoming eligible for render-tail
automatic loading.

## Composition and boundaries

`source-inspection.ts` owns the states, loading eligibility, cancellation,
publication, and snapshot settlement. `dotnet-inspect.ts` retains active
selection validation, exact signature construction, snapshot composition,
engine ports, and host rendering. `type-panel.ts` consumes a narrowed state for
Type Source presentation.

The operation authority remains the logical owner of Type Source execution and
cancellation. This state contract consumes its feature events without changing
operation IDs, result DTOs, diagnostics, cancellation reasons, or the managed
bridge. Member Source remains on its current engine export.

This slice does not change Graph Source, Annotated Source, Facts, metadata,
package-lens, runtime-pack, top-level-view, or workspace-selection state. It
introduces no generic application-wide async-resource abstraction, reducer,
wire union, engine export, or source-code inventory gate.

## Adoption and evidence

[Issue #6679](https://github.com/richlander/dotnet-inspect/issues/6679)
tracks this focused production adoption under the state-union program in
[issue #4570](https://github.com/richlander/dotnet-inspect/issues/4570).

The production path has one step: use the two result states in the existing
inspect-web source coordinator, workspace snapshot normalization, composition
root, and Type and Member Source rendering. No second host or shared substrate
is introduced.

`test/source-inspection.test.ts` gates Member Source loading-object ownership,
cross-surface cancellation, current-selection publication, empty failure, cache
reuse, and focus restoration. `test/type-source-managed-operation.test.ts`
gates Type Source operation-event publication, replacement, cancellation,
boundary diagnostics, and empty failure. Type panel and workspace snapshot
tests gate exhaustive rendering and interrupted-loading settlement. Frontend
type checking rejects consumers that use signature, source, or failure evidence
without narrowing the result state.
