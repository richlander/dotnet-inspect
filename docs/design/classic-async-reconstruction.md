# Durable classic-async reconstruction

> **Map:** [Decompiler design](../decompiler.md) is the pipeline entry.
> [Raise-work discipline](../decompiler-raise-discipline.md) is the proof
> contract for every raise slice. This document owns the classic
> (`runtime-async=off`) state-machine inverse: the structured machine value,
> honesty rules for declined kickoffs, and the slice plan. Runtime-async
> (`AsyncHelpers.Await`) stays with `AwaitRecoveryPass`.

## Status

Design. Tracking: [#4472](https://github.com/richlander/dotnet-inspect/issues/4472).
Not implemented. `ClassicAsyncReconstructionPass` remains the current
fixture-shaped raise.

## The problem

A declared classic-async method has two physical bodies:

```text
kickoff MethodDef     — Create builder, copy args, Start<TStateMachine>, return Task
<M>d__N.MoveNext      — user logic, awaiter protocol, SetResult / SetException
```

The source the user wrote lives in `MoveNext`. The MethodDef they ask `member`
about is the kickoff. On `main` at `239ef9e48`, the Analysis fixture

```csharp
public static async Task<int> CallsSyncSiblingFromAsync(int value)
{
    await Task.Yield();
    return ReadValue(value);
}
```

decompiles to

```csharp
public static async System.Threading.Tasks.Task<int> CallsSyncSiblingFromAsync(int value)
{
    __CallsSyncSiblingFromAsync_d__2 V_0 = default;

    V_0.___t__builder = AsyncTaskMethodBuilder<int>.Create();
    V_0.value = value;
    V_0.___1__state = -1;
    V_0.___t__builder.Start<__CallsSyncSiblingFromAsync_d__2>(ref V_0);
    return V_0.___t__builder.Task;
}
```

That render is not a decompiler crash and not an Analysis regression. It is
the kickoff printed under an `async` signature. The same hole exists on the
official ClassicAsync overlay: `NoAwait()` stays a stub. Decompiling
`MoveNext` is worse — `TryAcknowledgeSupportMethod` replaces a recognized
state-machine `MoveNext` with `return;`, so the user logic is nowhere.

[#4461](https://github.com/richlander/dotnet-inspect/pull/4461) made this
visible by putting the render in a PR demo. [#4466](https://github.com/richlander/dotnet-inspect/pull/4466)
exposed `LibraryBodyIndex.ResolveDeclaredMethod`; it did not raise the body.

## Why the current pass cannot grow

`ClassicAsyncReconstructionPass` is a family of `TryBuild*` matchers for the
ClassicAsync overlay:

| Builder | Source shape it accepts |
| --- | --- |
| `TryBuildSingleAwaitReturn` | `return await a + b;` |
| `TryBuildSingleAwaitVoid` | `await a;` |
| `TryBuildSequentialVoid` | two Task awaits + `GC.KeepAlive` |
| `TryBuildConditional` | `flag ? await a : 0` |
| `TryBuildLoop` | `foreach` over `Task<int>[]` with three `<>7__wrap*` fields |
| `TryBuildTryFinally` | `try { return await a; } finally { KeepAlive }` |

`CallsSyncSiblingFromAsync` is none of those. It is `await Task.Yield()`
(a `YieldAwaitable`, not a `Task`) plus a later sync call that reads a
hoisted parameter. Several builders hard-decline on
`HasHoistedUserState`. Adding `TryBuildYieldThenReturn` would be another
fixture, not an inverse.

The pass also trusts compiler-reserved names (`<>t__builder`, `<...>d__N`)
plus `DeclaringTypeCompilerGenerated`. That is the right *recognition*
trust line (user C# cannot forge those names). It is not a *reconstruction*
model. Recognition and reconstruction are currently one function.

C# already knows the honesty rule for a sibling lowering.
`TypeShellProducer.RequiresAsyncBodyModifier` withholds `async` from an
unreconstructed *iterator* kickoff because stamping it on the raw
state-machine return makes otherwise-compilable output invalid. Classic
async does not get that withhold: `AsyncStateMachineAttribute` alone
stamps `async` onto the physical kickoff body.

## Design lessons

The same structured-system moves as
[#3636](https://github.com/richlander/dotnet-inspect/pull/3636) and
[type-forwarding resolution](type-forwarding-resolution.md):

### Put the property on the value

"This kickoff reconstructed to a source async body" is a property of a
machine value, not a call-site obligation in the printer. Once
reconstruction succeeds, `RequiresAsyncBodyModifier` is true because the
value says so. Once it declines, the printer cannot invent `async`.

### Materialize what crosses the owner boundary

While reconstructing, the decompiler may hold the imported kickoff IR, the
imported `MoveNext` IR, and live metadata. After reconstructing, the raise
consumes a materialized `ClassicAsyncMachine`: tokens, hoisted-field
kinds, await points, and a typed outcome. Consumers do not re-walk
`MoveNext` looking for `SetResult`.

### Degradation is data

A declined machine is still a machine. The outcome names why
(`NoMoveNext`, `AmbiguousStateMachineType`, `UnrecognizedAwaiterProtocol`,
`UnconsumedMoveNextRegion`, …). The kickoff body stays the kickoff body.
`MoveNext` stays `MoveNext`. Neither is hollowed.

### Do not depend upward

Analysis already maps `MoveNext` → source
(`LibraryBodyAsyncSourceResolver`, public as `ResolveDeclaredMethod`).
The decompiler must not take an Analysis dependency
([inspection layers](inspection-layers.md)). Metadata already classifies
kickoffs (`MethodClassification.StateMachineAsync` +
`AsyncStateMachineAttribute`). Classification stays in Metadata. The
machine value and the raise stay in the decompiler. Analysis may later
read the same Metadata facts; it must not become the decompiler's
classifier.

## The value

Working name: `ClassicAsyncMachine`.

```text
ClassicAsyncMachine
  Kickoff            MethodDef token
  StateMachineType   TypeDef token (from AsyncStateMachineAttribute)
  MoveNext           MethodDef token
  BuilderField       FieldDef (<>t__builder)
  StateField         FieldDef (<>1__state)
  HoistedFields      { FieldDef, Kind, SourceName? }*
  AwaitPoints        { State, AwaiterField, GetAwaiter, GetResult, ResumeState }*
  Outcome            Reconstructed | Declined(Reason)
```

`Kind` is a closed set: `Builder`, `State`, `This`, `Parameter`, `Local`,
`Awaiter`, `Wrap`. Source names come from the compiler-reserved spelling
(`<value>5__1` → `value`) only when that spelling is present; otherwise
the field stays unnamed and the raise declines rather than inventing one.

`AwaitPoints` describe the awaiter *protocol*, not a Task type. Yield,
`ValueTask`, and custom awaiters are the same four members
(`GetAwaiter` / `IsCompleted` / `GetResult` / `AwaitUnsafeOnCompleted`
or `AwaitOnCompleted`). A builder that requires `GetResult` to return
`Task` is how the current pass misses `Task.Yield()`.

The existing `TryBuild*` methods are temporary adapters over a fully
populated machine. New accepted shapes extend the machine or the raise
that consumes it. They do not add another top-level builder.

## Inverse

Roslyn's forward construct is `AsyncRewriter` /
`AsyncMethodToStateMachineRewriter` under `runtime-async=off`. The
declared domain is compiler-produced classic state machines with a
single `MoveNext` and a unique `AsyncStateMachineAttribute` type
argument.

Runtime-async (`MethodImplAttributes.Async`, `AsyncHelpers.Await`) is a
different lowering and a different pass. This design does not absorb it.

## Honesty contract

These rules are slice 0. They ship with no new raise.

1. **`async` is a reconstruction claim.** A classic kickoff prints
   `async` only when `ClassicAsyncMachine.Outcome` is `Reconstructed`.
   Metadata classification alone is not enough. Match the iterator
   withhold already documented on `TypeShellProducer`.
2. **Do not hollow `MoveNext` unless the kickoff reconstructed.**
   `TryAcknowledgeSupportMethod` may still collapse `SetStateMachine`.
   It must not erase user logic that the kickoff raise failed to import.
3. **Declined output stays physical.** The kickoff render is the kickoff
   IL, without `async`. A later diagnostic comment (`DEC####`) may name
   the decline reason. It must not look like a source async body.
4. **One hop.** An async local-function `MoveNext` maps to that local
   function's stub, not the owning method. Same non-goal as #4466.

A concrete observation that would falsify slice 0: `NoAwait()` or
`CallsSyncSiblingFromAsync` still prints `async` over `Start<TStateMachine>`.

## Fidelity subject

Raised source recompiles to a *new* kickoff + `MoveNext`, not the
original kickoff IL (`Create` / `Start` / `get_Task`). Opcode fidelity
against the kickoff MethodDef is the wrong subject. That gap is
**unverified** until a harness names a kickoff-aware fidelity contract
(semantic compile-back of the raised method, or a MoveNext-shaped
comparison). Do not report Exact against the stub.

Validity and observable-behavior correctness remain required for every
accepted raise, per [raise-work discipline](../decompiler-raise-discipline.md).

## Slices

Each slice lands alone, with its own fixture family and decline
boundary. If a slice is only defensible once the next one lands, fold
it.

| Slice | Claim | Residual after it |
| --- | --- | --- |
| 0. Honesty + model | Build `ClassicAsyncMachine` from Metadata + kickoff + `MoveNext`. Stamp `async` only on `Reconstructed`. Stop hollowing `MoveNext` when the kickoff declined. Existing overlay raises keep working. | Common Yield + statements shape still declined, but honest. |
| 1. Await then statements | Accept hoisted parameters and user statements between / after await points, including the #4472 fixture. | Custom / Yield awaiters still declined if the protocol matcher is still Task-shaped. |
| 2. Awaiter protocol | Recognize awaiters by the four-member protocol, not by `Task` / `ValueTask` type names. Pins `Task.Yield()` and `ValueTask`. | General multi-state dispatch may still be overlay-shaped. |
| 3. State dispatch | Raise from `AwaitPoints` + remaining user regions. Retire per-shape `TryBuild*`. | Runtime-async, iterators, async-local chaining. |

Slice 0 is the InertString move: the value exists before consumers
depend on a better raise.

## Proof obligations (every raise slice)

1. **Lowering shell.** C# async method, Roslyn, `runtime-async=off`,
   Release. Compiler-produced fixture required. The #4472 fixture is
   the first real witness for slice 1; ClassicAsync overlay remains
   the accepted-raise pins.
2. **Consumed ownership.** Kickoff stores into the state-machine local,
   `Start`, and `get_Task` are consumed when replacing the kickoff
   body. Each await point consumes its awaiter field, state transition,
   and `GetResult`. Unconsumed `MoveNext` regions are a decline, not a
   partial raise.
3. **Control-flow contract.** Await points are sequencing barriers.
   Successor identity across an await is the resume state, not
   fallthrough. Exception paths through `SetException` stay on the
   builder; do not flatten them into user `throw` unless a later slice
   proves ownership.
4. **Replacement contract.** Valid + correct on the accepted fixture
   family. IL fidelity against the kickoff MethodDef is explicitly not
   claimed (see [Fidelity subject](#fidelity-subject)).

### Fixture family

- Compiler-produced positive: `CallsSyncSiblingFromAsync` (slice 1),
  each ClassicAsync overlay method that already reconstructs (keep).
- Smallest accepted boundary: `await Task.Yield();` with no later
  statements (slice 2); empty `async Task` (`NoAwait`) as honesty
  witness (slice 0) and as a raise only if a slice explicitly takes it.
- Still-flat negatives: existing
  `ClassicAsyncReconstructionPassTests` lookalikes; a kickoff whose
  `AsyncStateMachineAttribute` type argument is missing or ambiguous;
  a `MoveNext` with two `SetResult` calls.
- Nested-function negative: async local function (one hop only).
- Pinned real witness: the #4472 `member` render, Before/After from
  `dotnet-inspect`, structural review per the decompiler PR template.

## Non-goals

- Runtime-async reconstruction (`AwaitRecoveryPass`).
- Iterator / async-iterator reconstruction.
- Rewriting `DirectCall.Caller`. #4466 already exposes declared-method
  lookup. #4461 is a competing larger consumer; this design does not
  land it.
- Chaining an async local-function `MoveNext` to the owning method.
- Sharing a live `MetadataReader` or Analysis index across the
  decompiler boundary.
- A second state-machine detector in Analysis, CLI, or Research.

## Layer ownership

| Fact | Owner |
| --- | --- |
| `AsyncStateMachineAttribute`, `MethodClassification.StateMachineAsync` | Metadata |
| `ClassicAsyncMachine` and the raise | Decompiler |
| `async` keyword on a reconstructed body | CSharp, driven by the machine outcome |
| `MoveNext` → declared source for call-site attribution | Analysis (`ResolveDeclaredMethod`) |
| Presentation of Calls / Decompiled Source | CLI |

## Gates (to be named when slice 0 lands)

Slice 0 must land with a named non-vacuity test that fails if
`async` returns to a declined kickoff, and a second that fails if
`MoveNext` is hollowed while the paired kickoff is declined. Those
tests do not yet exist. Until they do, the honesty contract is
unverified.

Existing `ClassicAsyncReconstructionPassTests` remain the recognition
negatives. Existing ClassicAsync overlay decompiles remain the
accepted-raise pins. Do not weaken either to make a new shape pass.
