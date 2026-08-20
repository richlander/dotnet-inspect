# Durable classic-async reconstruction

> **Map:** [Decompiler design](../decompiler.md) is the pipeline entry.
> [Raise-work discipline](../decompiler-raise-discipline.md) is the proof
> contract for every raise slice. This document owns the classic
> (`runtime-async=off`) state-machine inverse: the structured machine value,
> honesty rules for declined kickoffs, and the slice plan. Runtime-async
> (`AsyncHelpers.Await`) stays with `AwaitRecoveryPass`.

## Status

Design. Tracking: [#4472](https://github.com/richlander/dotnet-inspect/issues/4472).
Not implemented. r1 at `61dda3681` was BLOCKED; this revision is the
replacement candidate.

`ClassicAsyncReconstructionPass` remains the current fixture-shaped raise.

## The problem

A declared classic-async method has two physical bodies:

```text
kickoff MethodDef     — Create builder, copy args, Start<TStateMachine>, return Task
<M>d__N.MoveNext      — user logic, awaiter protocol, SetResult / SetException
```

The source the user wrote lives in `MoveNext`. The MethodDef they ask
`member` about is the kickoff. On `main` at `239ef9e48`, the Analysis
fixture

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

That render is not a decompiler crash and not an Analysis regression. It
is the kickoff printed under an `async` signature, at **Full** fidelity.
The same hole exists on the official ClassicAsync overlay: `NoAwait()`
stays a stub. Decompiling `MoveNext` is worse —
`TryAcknowledgeSupportMethod` replaces a recognized state-machine
`MoveNext` with `return;`, so the user logic is nowhere.

[#4461](https://github.com/richlander/dotnet-inspect/pull/4461) made this
visible by putting the render in a PR demo.
[#4466](https://github.com/richlander/dotnet-inspect/pull/4466) exposed
`LibraryBodyIndex.ResolveDeclaredMethod`; it did not raise the body.

## Why the current pass cannot grow

`ClassicAsyncReconstructionPass` is a family of `TryBuild*` matchers for
the ClassicAsync overlay (single await-return, single await-void, two
Task awaits + `KeepAlive`, foreach-await over `tasks`, try/finally,
conditional). `CallsSyncSiblingFromAsync` is none of those.

The pass does **not** miss `Task.Yield()` because `GetResult` must
return `Task`. `AwaitForGetResult` already matches `GetAwaiter` by
name. The real decline is the Yield lowering:

1. `Task.Yield()` returns a `YieldAwaitable` struct temp.
2. The compiler emits `stloc` / `ldloca` before `GetAwaiter`.
3. That `stloc` trips `HasUnexpectedStore` (allow list: state stores,
   `GetAwaiter` stores, `<>u__` loads).
4. The recovered operand is a `LoadLocalAddress`; `RemapInPlace` has no
   case for it.

Hoisted user parameters are a second independent decline
(`HasHoistedUserState`) on several builders. Adding
`TryBuildYieldThenReturn` would be another fixture, not an inverse.

Recognition currently trusts compiler-reserved names (`<>t__builder`,
`<...>d__N`) plus `DeclaringTypeCompilerGenerated`. That is a known
deficiency (`AwaitRecoveryFacts`: Start and `.Task` are name-matched,
not builder-correlated). It is not a reconstruction model. Recognition
and reconstruction are one function.

## Where `async` is actually stamped

CSharp cannot own a reconstruction-driven `async` keyword.
`ILInspector.Decompiler` references `ILInspector.CSharp`; the reverse
edge does not exist. `TypeShellProducer` is contractually SRM-only and
must stay that way (API skeletons omit `async` because it is not part
of the callable surface).

The user-visible stamp sites today are:

| Path | Site | What it reads |
| --- | --- | --- |
| `member` Decompiled Source | `MemberCodeProvider` → `TypeShellProducer.RequiresAsyncBodyModifier(selection)` → `ApiOutputFormatter` | Metadata classification + `AsyncStateMachineAttribute` |
| Whole-type listing | `MemberBodyProducer` `FormatMemberWithBody` | Same metadata predicate |
| Raised body flag | `DecompilerResult.RequiresAsyncBodyModifier` | Set only when a pass reconstructed |

The decompiler flag already exists and is already consumed by
`MemberBodyProducer` when composing a `CSharpBlockBody` from a
projection, and by `BodyShapeSearch` (which reports "not reconstructed"
when the attribute is present and the flag is false). Declaration
formatting on the two user-visible paths **ignores** that flag and
restamps from metadata. That is why a declined kickoff still says
`async`.

`TypeShellProducer.RequiresAsyncBodyModifier` withholds `async` for
`AsyncIteratorStateMachineAttribute` by **attribute-name
discrimination**, unconditionally, reconstruction-blind. It is not a
"withhold until reconstructed" mechanism to copy.

The in-repo honesty precedent for an unreconstructed kickoff is
`IteratorAcknowledgmentPass`: replace the plausible handoff with
`UnsupportedNode`, drop fidelity to `Partial` (DEC0004). A declined
classic-async kickoff today stays Full because the kickoff IR contains
none of the node kinds `FidelityRemarks` keys on.

## Design lessons

Same structured-system moves as
[#3636](https://github.com/richlander/dotnet-inspect/pull/3636) and
[type-forwarding resolution](type-forwarding-resolution.md).

### Put the property on the value that already crosses the boundary

"`async` on this full body" is `DecompilerResult.RequiresAsyncBodyModifier`.
Slice 0 makes the two declaration formatters read that flag for
classic-async kickoffs instead of restamping from metadata. No new
CSharp→Decompiler edge. Skeletons stay metadata-only and stay without
`async`.

### Materialize what Metadata must own

`MethodClassificationScanner.ClassifyAsyncMethod` only detects the
attribute **name**. The unique trusted state-machine type argument
(same-assembly, unique, constructor shape, corelib-defining-type
check) lives only in
`LibraryBodyAsyncSourceResolver`. The decompiler must not depend on
Analysis and must not silently duplicate that walk. Lift the
materialized descriptor into Metadata. Decompiler and Analysis both
consume it.

### Degradation is data

A declined machine is still a machine. The outcome names why. The
kickoff body does not remain a Full, plausible method. `MoveNext` is
not hollowed as a substitute for a missing reverse index.

### Do not depend upward

Analysis maps `MoveNext` → source for call-site attribution
(`ResolveDeclaredMethod`). That stays Analysis. Reconstruction does
not assume rewritten `DirectCall.Caller`. #4461 remains a non-action.

## The value

Working name: `ClassicAsyncMachine`. Types inside the pipeline stay
`TypeRef`, not raw TypeDef tokens (generic state machines already
flow through `TypeRef.TypeArguments` in the current pass).

```text
ClassicAsyncMachine
  Kickoff              MethodRef
  StateMachineType     TypeRef (decoded attribute argument, trusted)
  MoveNext             MethodRef
  Kind                 Struct | Class
  BuilderField         FieldRef (<>t__builder)
  StateField           FieldRef (<>1__state)
  HoistedFields        { FieldRef, Kind, BoundKickoffArgument?, SourceName? }*
  AwaitPoints          { State, AwaiterField, AwaitedOperand, OperandStorage,
                         GetAwaiter, IsCompleted, GetResult,
                         Continuation (OnCompleted | UnsafeOnCompleted),
                         ResumeState }*
  Terminals            { SetResult?, SetException? }
  UserRegions          consumed MoveNext blocks/edges the raise claims
  Outcome              Reconstructed | Declined(Reason)
```

`HoistedFields.Kind` is a closed set: `Builder`, `State`, `This`,
`Parameter`, `Local`, `Awaiter`, `Wrap`. Parameter fields bind to a
kickoff argument (the fixture's hoisted field is plain `value`, not
`<value>5__1`). Source names come from reserved spelling only when
present; otherwise the bound argument name or unnamed.

`AwaitPoints` describe the awaiter *protocol* plus the operand that
was awaited and where it was stored. That is the Yield hole: the
operand is a by-ref temp, not a `Task` field.

`UserRegions` is a consumption ledger, not a slogan. A raise that
cannot name every remaining `MoveNext` block, edge, unmatched resume,
unowned state/awaiter use, external entry, continuation multiplicity,
or user/shell exception-region overlap declines. The current
`HasUnexpectedStore` `<>`-prefix allow list is not that ledger.

`Outcome = Reconstructed` in slice 0 means **a current `TryBuild*`
succeeded**. The adapters are not rewritten to sit on a fully
populated machine until a later slice. Stating otherwise was the r1
slice-coupling hole.

`Declined` reasons include `NoMoveNext`, `UntrustedStateMachineType`,
`AmbiguousStateMachineType`, `UnrecognizedAwaiterProtocol`,
`UnconsumedMoveNextRegion`, `ClassStateMachine` (Debug / reference
SM; out of the Release-struct domain until a later slice takes it).

Building the machine may walk `MoveNext` IR. After
`Outcome = Reconstructed`, printers consume the materialized value.
They do not re-derive await points from display text.

## Inverse

Roslyn's forward construct is `AsyncRewriter` /
`AsyncMethodToStateMachineRewriter` under `runtime-async=off`. The
declared domain is compiler-produced classic **struct** state machines
with a single `MoveNext` and a unique trusted
`AsyncStateMachineAttribute` type argument. Debug class state
machines decline until a slice names them.

Runtime-async (`MethodImplAttributes.Async`, `AsyncHelpers.Await`) is
a different lowering and a different pass.

Changing this inverse invalidates the two
`state-machine.classic-async-*` fact primitives in
`AwaitRecoveryFacts`. The changing PR updates that ledger.

## Honesty contract (slice 0)

Slice 0 ships **no new raise**. It changes how a declined kickoff is
presented.

1. **Declaration `async` follows the decompiler flag.** For a method
   classified `StateMachineAsync` with
   `AsyncStateMachineAttribute`, `member` Decompiled Source and
   whole-type member listings take
   `DecompilerResult.RequiresAsyncBodyModifier` (already set by
   today's successful `TryBuild*`). They do not restamp from
   `TypeShellProducer`. API skeletons are unchanged (no `async`).
2. **A declined kickoff is Partial, not a plausible method.** Follow
   `IteratorAcknowledgmentPass`: replace the kickoff body with
   `UnsupportedNode` + DEC0004-class diagnostic naming the state
   machine and that `MoveNext` was not reconstructed. Removing
   `async` while leaving Full `Start<TStateMachine>` plumbing would
   make the lie more believable. Fidelity must drop in the same
   slice.
3. **By-token `MoveNext` is always the physical body.** Do not hollow
   it. `TryAcknowledgeSupportMethod` stops erasing `MoveNext`.
   `SetStateMachine` may still be acknowledged as empty support.
4. **Whole-type duplicate suppression is a listing filter, not a
   rewrite.** If a kickoff reconstructed, the generated state-machine
   type's `MoveNext` may be omitted from the listing of that nested
   type. That filter does not rewrite the body a by-token request
   would print. Implementing the filter requires a Metadata (or
   Decompiler-local) kickoff↔`MoveNext` pair from the trusted
   attribute descriptor, not Analysis.
5. **One hop.** An async local-function `MoveNext` maps to that local
   function's stub, not the owning method. Same non-goal as #4466.

A concrete observation that would falsify slice 0: `member` of
`CallsSyncSiblingFromAsync` or `NoAwait` still prints `async` over
`Start<TStateMachine>`, or still grades that render Full.

`MemberBodyProducerAsyncTests.ClassicAsyncWithoutAwait_UsesResolvedMethodBodyModifier`
currently asserts `async` on `NoAwait()`. Slice 0 flips that
assertion. That flip is the non-vacuity gate: deleting the formatter
change must fail the `member` / whole-type render, not a
`TypeShellProducer` predicate test.

## Fidelity subject

Raised source recompiles to a *new* kickoff + `MoveNext`, not the
original kickoff IL (`Create` / `Start` / `get_Task`). Opcode
fidelity against the kickoff MethodDef is the wrong subject.

**No new accepted raise ships until a named measurement exists.**
The intended contract is: compile the raised method with Roslyn,
Release, `runtime-async=off`, and compare the regenerated `MoveNext`
(or an equivalent behavioral execution covering result, exception,
suspension, and side effects). Until that harness exists, this gap
is unverified and slices after 0 are blocked. Slice 0 does not
need it: it does not accept a new raise.

Validity of declined output is the Partial/`UnsupportedNode` path,
already gated by existing fidelity-level machinery.

## Slices

This issue was opened from a named product failure, not a corpus
census. Raise discipline still requires measurement before further
splitting. After slice 0, take a classic-async shape census on the
pinned corpus before adding a fourth slice. Do not invent more
`TryBuild*` methods from this document.

| Slice | Claim | Residual after it |
| --- | --- | --- |
| 0. Honesty | Wire declaration `async` to `DecompilerResult.RequiresAsyncBodyModifier`. Acknowledge declined kickoffs like iterators (`UnsupportedNode`, Partial). Stop hollowing by-token `MoveNext`. Lift trusted SM type descriptor into Metadata. `Reconstructed` means current `TryBuild*` succeeded. | #4472 fixture still declined, but honest. |
| 1. Yield operand + post-await statements | One slice. Accept the #4472 fixture: `await Task.Yield(); return ReadValue(value);`. That requires the operand-temp / `ldloca` path **and** hoisted parameter binding **and** statements after the await. The smaller `await Task.Yield();` (no later statements) is the accepted boundary of the same slice, not a later one. | General multi-state dispatch, class SM, custom awaiters beyond Yield/`ValueTask`/`Task`. |
| 2. State dispatch | Raise from `AwaitPoints` + `UserRegions` consumption ledger. Retire per-shape `TryBuild*`. Census chooses whether this splits. | Runtime-async, iterators, async-local chaining. |

Slice 0 is independently shippable: it wraps today's success set and
fixes presentation of the failure set. Slices 1 and 2 from r1 are
folded because a slice cannot accept a superset whose subset it
declines.

## Proof obligations (every raise slice)

1. **Lowering shell.** C# async method, Roslyn, `runtime-async=off`,
   Release. Compiler-produced fixture required. #4472
   `CallsSyncSiblingFromAsync` is the first real witness for slice 1;
   ClassicAsync overlay remains the accepted-raise pins.
2. **Consumed ownership.** Kickoff stores into the state-machine
   local, `Start`, and `get_Task` are consumed when replacing the
   kickoff body. Each await point consumes its awaiter field, operand
   storage, state transition, and `GetResult`. Unconsumed `MoveNext`
   regions are a decline, recorded on `UserRegions`.
3. **Control-flow contract.** Await points are sequencing barriers.
   Successor identity across an await is the resume state, not
   fallthrough. Exception paths through `SetException` stay on the
   builder unless a later slice proves ownership.
4. **Replacement contract.** Valid on the accepted fixture family.
   Correctness uses the kickoff-aware measurement in
   [Fidelity subject](#fidelity-subject). IL Exact against the
   original kickoff MethodDef is not claimed.

### Fixture family (slice 1)

- Compiler-produced positive: `CallsSyncSiblingFromAsync`.
- Smallest accepted boundary: `await Task.Yield();` with no later
  statements.
- Keep: every ClassicAsync overlay method that already reconstructs.
- Still-flat negatives: existing
  `ClassicAsyncReconstructionPassTests` lookalikes; missing or
  ambiguous or untrusted attribute type argument; two `SetResult`
  calls; class state machine.
- Nested-function negative: async local function (one hop only).
- Pinned real witness: the #4472 `member` render, Before/After from
  `dotnet-inspect`, structural review per the decompiler PR template.

`NoAwait` is a slice-0 honesty witness, not a slice-1 raise unless a
later slice explicitly takes empty async methods.

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
- Teaching `TypeShellProducer` about reconstruction outcomes.

## Layer ownership

| Fact | Owner |
| --- | --- |
| Attribute name classification (`StateMachineAsync`) | Metadata (already) |
| Trusted unique SM type argument + kickoff↔`MoveNext` pair | Metadata (lift from Analysis) |
| `ClassicAsyncMachine` and the raise | Decompiler |
| `async` on a full-body render | Decompiler result flag, consumed by CLI `member` and `MemberBodyProducer` listings |
| `async` on an API skeleton | Omitted. `TypeShellProducer` stays SRM-only |
| `MoveNext` → declared source for call-site attribution | Analysis (`ResolveDeclaredMethod`) |
| Presentation of Calls / Decompiled Source | CLI |

## Gates

Honesty is unverified until these exist. They must exercise the
**render**, not the metadata predicate.

| Gate | Fails if |
| --- | --- |
| `member` Decompiled Source of `NoAwait` / `CallsSyncSiblingFromAsync` | Still contains `async` together with `Start<` |
| Same renders | Fidelity is Full (must be Partial + `UnsupportedNode`) |
| By-token `MoveNext` of a declined kickoff's SM | Distinctive user logic (`ReadValue` / `Yield`) is absent |
| Whole-type listing of `AsyncFixtures` | `NoAwait` still spelled `async` over the stub |
| `ClassicAsyncWithoutAwait_UsesResolvedMethodBodyModifier` | Still expects `async Task NoAwait()` (must flip) |

Deleting the formatter change must fail the `member` / whole-type
tests. A green `TypeShellProducer` test is not this gate.
