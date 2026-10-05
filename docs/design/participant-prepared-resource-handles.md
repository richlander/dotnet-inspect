# Participant prepared-resource handles

> InspectionSpace design for the typed construction boundary tracked by
> [#9320](https://github.com/richlander/dotnet-inspect/issues/9320). The
> [InspectionSpace lifetime contract](../inspection-space.md#participant-scoped-prepared-resource-leases)
> owns lease admission, retirement, accounting, and cleanup ordering. This
> document owns the producer-facing capability that applies that contract.

## Status

This document defines the target boundary. The raw producer-facing lifetime
surfaces remain until the staged adoptions in
[#9321](https://github.com/richlander/dotnet-inspect/issues/9321) and
[#9322](https://github.com/richlander/dotnet-inspect/issues/9322) complete.

## Question

How does a prepared producer derive state from one assembly participant and
retain that state for repeated execution without separately composing resource
registration, participant validation, snapshot access, lease accounting, and
retirement?

The two existing adopters demonstrate why the construction boundary matters.
Hierarchy-index preparation admits a participant-resource borrow before using
its lease-bound snapshot operation. Declared-Method preparation instead uses
ordinary snapshot admission and first borrows the resource for terminal
execution. A concurrent participant release may therefore begin resource
retirement while that preparation is still deriving or publishing state. Both
adopters intend the same lifetime, but the raw API permits different safety
properties.

## Claim

InspectionSpace issues the sole typed capability by which participant-derived
resource state can access a participant snapshot or remain live across
execution. Possession of a live borrow from that capability means the exact
participant-resource pair is admitted by its `AssemblyContextGroup`; snapshot
access through the borrow and the borrow's retained lifetime are therefore
accounted by the same authority.

The capability is a participant prepared-resource handle. It binds exactly one
group-owned producer state object to its group. Producers receive the handle,
not the raw registration, borrowing, or retirement surfaces from which the
lifetime protocol could be reconstructed.

## Contract

### Construction

An `AssemblyContextGroup` creates or returns one typed handle for one registered
producer state object. The handle and state are group-owned and share the
group's lifetime. Construction either registers the complete pair or publishes
nothing; a failed factory cannot leave a partially registered resource.

The handle is the only producer-facing type that can admit a borrow for its
state. It never exposes that state except inside an admitted preparation
callback or through a live typed borrow. The untyped participant-resource
interface and the group's raw participant-resource lease operations are
InspectionSpace implementation details. Private raw operations plus the
handle's state-access boundary make the type system, rather than a repository
scan or caller convention, the enforcement gate.

### Preparation

A preparation operation supplies an exact `AssemblyContextParticipant`,
cancellation, producer input, and a settlement callback. The handle:

1. validates and admits the exact participant-resource pair;
2. attempts snapshot access through that admitted borrow;
3. for a settled access, invokes the callback exactly once with its state and
   either the immutable snapshot or the typed acquisition rejection;
4. keeps the borrow live through outcome mapping and publication; and
5. releases the temporary borrow on every return or failure path.

Participant validation and resource admission are one InspectionSpace
operation, not two producer calls. Release requested before admission rejects
the operation. Release requested after admission cannot revoke snapshot access
or retire the producer state until the callback and its borrow finish.

The producer callback owns preparation keys, single-flight or retry policy,
typed ready and failure outcomes, diagnostics, and publication of physical
state. It maps both available and rejected snapshot access to the producer's
settled outcome before the borrow closes. Publication while the callback holds
the borrow is within the admitted lifetime. InspectionSpace does not interpret
or cache the callback result.

Cancellation before settlement and callback failures remain visible; the
handle does not turn them into settled success. Snapshot acquisition rejection
remains typed but cannot be returned for later publication outside the
admitted lifetime.

### Reusable execution

A ready outcome may request a reusable borrow by the exact participant
registration captured during preparation. The returned typed borrow:

- keeps the participant-resource pair and participant snapshot retained;
- exposes the bound producer state;
- provides lease-bound snapshot access if the execution needs it; and
- closes exactly once through `Dispose`.

The producer validates that the ready outcome still denotes live state while
the borrow is held. If validation or execution construction fails, the
producer closes the borrow before propagating the failure. A successfully
constructed execution owns the borrow until that execution is disposed.

InspectionSpace does not define ready-state identity or staleness. It guarantees
only that state consulted while a borrow is live cannot be retired
concurrently.

### Retirement

The group-owned handle is the sole participant-retirement target. After the
last borrow for a requested participant-resource pair closes, the handle
dispatches synchronous cleanup to the producer state. The producer may remove
settled outcomes and dispose physical sessions or indexes for that participant,
but it cannot admit another borrow, maintain an authoritative parallel lease
count, or authorize snapshot release.

The handle disposes the complete producer state when its group is disposed.
Participant cleanup failures continue through the InspectionSpace release
failure path; the handle does not replace them with empty or successful
outcomes.

## Minimum API shape

The names are illustrative; the ownership and capability boundaries are
normative.

```csharp
AssemblyContextParticipantResource<TState>
    AssemblyContextGroup.GetOrCreateParticipantResource<TState, TInput>(
        TInput input,
        Func<TInput, TState> create);

TResult AssemblyContextParticipantResource<TState>.Prepare<TInput, TResult>(
        AssemblyContextParticipant participant,
        CancellationToken cancellationToken,
        TInput input,
        Func<
            TState,
            AssemblyContextParticipantPreparationAccess,
            TInput,
            TResult> settle);

abstract record AssemblyContextParticipantPreparationAccess
{
    sealed record Available(AssemblyImageSnapshot Snapshot);
    sealed record Rejected(CandidateOpenFailure Failure);
}

AssemblyContextParticipantResourceBorrow<TState>
    AssemblyContextParticipantResource<TState>.Borrow(
        AssemblyAcquisitionRegistration registration);
```

The state supplies synchronous participant cleanup and whole-state disposal,
either through a narrow callback contract or an InspectionSpace-owned state
interface. Only the handle implements the group-internal retirement protocol.
The borrow exposes the state and lease-bound snapshot use; it does not expose
the group or permit rebinding to another participant or resource.

## Boundaries

This pattern does not define:

- producer preparation keys, caches, gates, or retry policy;
- Metadata hierarchy construction or declared-Method population semantics;
- QuerySpace capability grouping, provision selection, terminal folding, or
  limits;
- cross-assembly discovery or resource sharing between context groups; or
- asynchronous cleanup.

The pattern is host-neutral, SRM-only, NativeAOT-friendly, and compatible with
single-threaded Browser/Wasm. It adds no threading or blocking requirement
beyond the synchronous lease and cleanup contract already owned by
InspectionSpace.

## Evidence and adoption

The existing
[participant-resource lifecycle model](../models/assembly-context-participant-resource-lifecycle/README.md)
already checks admission closure, admitted-work survival, final-pair
retirement, resource-before-snapshot ordering, retained accounting, sibling
independence, and eventual release. This API does not change those semantics,
so it does not claim a new model result. Runtime gates must continue to exercise
the same properties through the typed handle. A direct gate additionally races
an unreadable-image rejection with participant release while a sibling keeps
the group alive, proving that rejection publication finishes before retirement
and that no settled outcome can publish after participant cleanup.

The pattern rollout is three independently mergeable slices: this design, then
the two implementation stages:

1. [#9321](https://github.com/richlander/dotnet-inspect/issues/9321)
   adds the typed handle and direct gates while migrating the declared-Method
   producer as the bounded first adopter;
2. [#9322](https://github.com/richlander/dotnet-inspect/issues/9322)
   migrates the hierarchy-index producer and makes the raw
   participant-resource registration, borrowing, and retirement surfaces
   inaccessible to producers.

`MemberGroupScorecard` through the host-neutral declared-Method section
operation is the production-host witness for the first adoption. The hierarchy
scorecard over pinned framework assemblies is the second, materially different
witness. Each adoption preserves its own typed outcomes and exact NativeAOT
terminal evidence.

The first planned CLI and Browser/Wasm enablement follows the existing
19-step [Subject Relations adoption
plan](subject-relations-workflows.md#adoption-and-retirement). Its Metadata
hierarchy adapters are step 9, shared section and Markout projection is step
14, CLI adoption is step 16, and Inspect Web/Browser-Wasm adoption is step 17.
When that request set justifies the prepared hierarchy capability, it consumes
the same typed handle rather than introducing host-local lifetime coordination.
The direct targeted hierarchy producer remains the cold reference and
QuerySpace provision selection remains economics-driven. This pattern does not
redefine Subject Relations composition, host gestures, rendering, or
presentation.
