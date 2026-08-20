# Durable classic-async reconstruction

> **Map:** [Decompiler design](../decompiler.md) is the pipeline entry.
> [Raise-work discipline](../decompiler-raise-discipline.md) is the proof
> contract for every raise slice. This document owns the classic
> (`runtime-async=off`) state-machine inverse: the structured machine value,
> honesty rules for declined kickoffs, and the slice plan. Runtime-async
> (`AsyncHelpers.Await`) stays with `AwaitRecoveryPass`.

## Status

Design. Tracking: [#4472](https://github.com/richlander/dotnet-inspect/issues/4472).
Not implemented. r1 at `61dda3681` and r2 at `b7ccb8476` were BLOCKED;
this revision is the replacement after integrating `origin/main`
`98067bbb2`.

`ClassicAsyncReconstructionPass` remains the current fixture-shaped raise.

## The problem

A declared classic-async method has two physical bodies:

```text
kickoff MethodDef     — Create builder, copy args, Start<TStateMachine>, return Task
<M>d__N.MoveNext      — user logic, awaiter protocol, SetResult / SetException
```

The source the user wrote lives in `MoveNext`. The MethodDef they ask
`member` about is the kickoff. On `main` at `239ef9e48` (still true after
`98067bbb2`), the Analysis fixture

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

[#4466](https://github.com/richlander/dotnet-inspect/pull/4466) exposed
`LibraryBodyIndex.ResolveDeclaredMethod`; it did not raise the body.
[#4461](https://github.com/richlander/dotnet-inspect/pull/4461) later
attributed async call sites in Analysis. Reconstruction does not
depend on that Caller rewrite.

## Why the current pass cannot grow

`ClassicAsyncReconstructionPass` is a family of `TryBuild*` matchers for
the ClassicAsync overlay. `CallsSyncSiblingFromAsync` is none of those.

The #4472 fixture has **one** reconstruction decline: `HasUnexpectedStore`.

1. `Task.Yield()` returns a `YieldAwaitable` struct temp.
2. The compiler emits `stloc` / `ldloca` before `GetAwaiter`.
3. That `stloc` trips `HasUnexpectedStore` (allow list: state stores,
   `GetAwaiter` stores, `<>u__` loads).
4. `AwaitForGetResult` already matches `GetAwaiter` by name. The
   decline is not a `GetResult` return-type check.

`HasHoistedUserState` is **not** a second decline on this fixture. It
fires only on `StoreField` whose name is `<…>5__` or `<>7__wrap`. The
fixture's hoisted parameter is the plain field `value`, stored in the
kickoff; `MoveNext` only loads it. `RemapInPlace` already maps
`LoadField` of that name through `TryGetParameter`.

`RemapInPlace` has no `LoadLocalAddress` case. Unmatched nodes fall
through to walking children and leave `ok == true`, so a
`LoadLocalAddress` carrying a `MoveNext` local index would splice
into the kickoff as silent corruption. That path is currently
unreachable only because `HasUnexpectedStore` fires first. Relaxing
the store allow list without an explicit `LoadLocalAddress` decline
(or proven remap) is wrong output, not a raise.

Adding `TryBuildYieldThenReturn` would be another fixture, not an
inverse.

Recognition currently trusts compiler-reserved names (`<>t__builder`,
`<...>d__N`) plus `DeclaringTypeCompilerGenerated`.
`LooksLikeClassicAsyncStateMachine` matches any `<>t__builder` field,
including async-iterator `AsyncIteratorMethodBuilder`. That is a
known deficiency (`AwaitRecoveryFacts`: Start and `.Task` are
name-matched, not builder-correlated). Recognition and reconstruction
are one function.

## Where `async` is actually stamped

CSharp cannot own a reconstruction-driven `async` keyword.
`ILInspector.Decompiler` references `ILInspector.CSharp`; the reverse
edge does not exist. `TypeShellProducer` is contractually SRM-only and
must stay that way (API skeletons omit `async` because it is not part
of the callable surface).

`MemberCodeProvider` currently computes **one** metadata flag
(`TypeShellProducer.RequiresAsyncBodyModifier(selection)`) and
`ApiOutputFormatter` feeds that same bit to every member C# view:

| View | Consumer |
| --- | --- |
| Decompiled Source | `FormatCSharpResult(..., requiresAsyncBodyModifier: code.RequiresAsyncBodyModifier)` |
| Annotated Source | same |
| Cost Overlay | same |
| Semantics Overlay | same |
| Whole-type listing | `MemberBodyProducer.FormatMemberWithBody` restamps from metadata |

Each of those projections already has a `DecompilerResult`. Slice 0
makes every `FormatCSharpResult` call (and the whole-type listing)
read **that projection's** `DecompilerResult.RequiresAsyncBodyModifier`.
It does not restamp from the shared metadata bit.

The decompiler flag is already set when a `TryBuild*` succeeds, and
is already consumed when composing a `CSharpBlockBody` from a
projection and by `BodyShapeSearch`. Declaration formatting ignores
it. That is why a declined kickoff still says `async`.

`TypeShellProducer.RequiresAsyncBodyModifier` withholds `async` for
`AsyncIteratorStateMachineAttribute` by **attribute-name
discrimination**, unconditionally, reconstruction-blind. It is not a
"withhold until reconstructed" mechanism to copy.

The in-repo honesty precedent for an unreconstructed kickoff is
`IteratorAcknowledgmentPass`: replace the plausible handoff with
`UnsupportedNode`, drop fidelity to `Partial` (DEC0004), and **only
when the body is exactly the compiler handoff**
(`IteratorShapes.IsNarrowKickoffHandoff`). Extra observable
statements stay visible. A declined classic-async kickoff today stays
Full because the kickoff IR contains none of the node kinds
`FidelityRemarks` keys on. `TryGetKickoff` also ignores unmatched
statements; honesty must not inherit that hole.

## Design lessons

Same structured-system moves as
[#3636](https://github.com/richlander/dotnet-inspect/pull/3636) and
[type-forwarding resolution](type-forwarding-resolution.md).

### Put the property on the value that already crosses the boundary

"`async` on this full body" is `DecompilerResult.RequiresAsyncBodyModifier`.
Slice 0 makes every member C# view and the whole-type listing read
that flag. No new CSharp→Decompiler edge. Skeletons stay metadata-only
and stay without `async`.

### Do not pretend an Analysis walk is a Metadata fact

`MethodClassificationScanner.ClassifyAsyncMethod` detects the
attribute **name**. The unique trusted state-machine type argument in
`LibraryBodyAsyncSourceResolver` is not liftable as-is: it is written
against Analysis `MemberRef` / `MemberResolver` / `FrameworkIdentity`,
and uniqueness is computed **after** skipping source-generated types,
`[GeneratedCode]` / `[CompilerGenerated]` methods, and Blazor render
methods. A Metadata-owned descriptor that absorbed those filters would
depend upward. A Metadata-owned descriptor that omitted them would
disagree with Analysis on `AmbiguousStateMachineType`.

Slice 0 does **not** lift that walk. Kickoff acknowledgment identifies
the state machine from kickoff IR (`TryGetKickoff`'s local type), the
same way iterators identify theirs from the handoff constructor.

A later Metadata fact, if needed for a listing filter, is **structural
only**: decode the attribute type argument, require a same-assembly
TypeDef, and report uniqueness among methods that carry the attribute.
Analysis keeps attribution filters on top. The two populations are
allowed to differ; consumers name which one they use. Decompiler
pipeline types stay Decompiler `TypeRef`; Metadata does not grow a
third `TypeRef`.

### Degradation is data

A declined machine is still a machine. The outcome names why. The
kickoff body does not remain a Full, plausible method. `MoveNext` is
not hollowed as a substitute for a missing reverse index.

### Do not depend upward

Analysis maps `MoveNext` → source for call-site attribution
(`ResolveDeclaredMethod`, now also used by #4461). That stays
Analysis. Reconstruction does not assume rewritten `DirectCall.Caller`.

## The value

Working name: `ClassicAsyncMachine`. Types inside the pipeline stay
Decompiler `TypeRef`, not raw TypeDef tokens.

```text
ClassicAsyncMachine
  Kickoff              MethodRef
  StateMachineType     TypeRef (from kickoff IR / structural attribute arg)
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
kickoff argument (plain `value` is already remappable). Source names
come from reserved spelling only when present; otherwise the bound
argument name or unnamed.

`AwaitPoints` describe the awaiter protocol plus the operand that was
awaited and where it was stored. That is the Yield hole: the operand
is a by-ref temp, not a `Task` field.

`UserRegions` is a consumption ledger. A raise that cannot name every
remaining `MoveNext` block, edge, unmatched resume, unowned
state/awaiter use, external entry, continuation multiplicity, or
user/shell exception-region overlap declines. The current
`HasUnexpectedStore` `<>`-prefix allow list is not that ledger.

`Outcome = Reconstructed` in slice 0 means **a current `TryBuild*`
succeeded**. Adapters are not rewritten onto a fully populated machine
until a later slice that has been designed from a census.

`Declined` reasons include `NoMoveNext`, `NotNarrowKickoffHandoff`,
`UnrecognizedAwaiterProtocol`, `UnconsumedMoveNextRegion`,
`LoadLocalAddressUnmapped`, `ClassStateMachine` (Debug / reference SM;
out of the Release-struct domain until a later slice takes it).

Building the machine may walk `MoveNext` IR. After
`Outcome = Reconstructed`, printers consume the materialized value.

## Inverse

Roslyn's forward construct is `AsyncRewriter` /
`AsyncMethodToStateMachineRewriter` under `runtime-async=off`. The
declared domain is compiler-produced classic **struct** state machines
with a single `MoveNext`. Debug class state machines decline until a
slice names them.

Runtime-async (`MethodImplAttributes.Async`, `AsyncHelpers.Await`) is
a different lowering and a different pass.

Changing this inverse invalidates the two
`state-machine.classic-async-*` fact primitives in
`AwaitRecoveryFacts`. The changing PR updates that ledger.

## Honesty contract (slice 0)

Slice 0 ships **no new accepted raise**. It changes how a declined
kickoff is presented, and it stops erasing classic-async `MoveNext`.

1. **Declaration `async` follows each projection's decompiler flag.**
   For a method classified `StateMachineAsync` with
   `AsyncStateMachineAttribute`, Decompiled Source, Annotated Source,
   Cost Overlay, Semantics Overlay, and whole-type listings take that
   projection's `DecompilerResult.RequiresAsyncBodyModifier` (already
   set by today's successful `TryBuild*`). They do not restamp from
   `TypeShellProducer` / the shared `MemberCodeProvider` metadata bit.
   API skeletons are unchanged (no `async`).
2. **A declined kickoff is Partial only when the body is the narrow
   compiler handoff.** Follow `IteratorAcknowledgmentPass`: replace
   with `UnsupportedNode` + DEC0004-class diagnostic naming the state
   machine and that `MoveNext` was not reconstructed, **and only if**
   every statement is kickoff plumbing (builder `Create`, argument
   copies onto the SM local, `Start`, `return …Task`). Extra
   observable calls or stores: leave the lowered body visible at
   Partial; do not delete work. Removing `async` while leaving Full
   `Start<TStateMachine>` plumbing would make the lie more believable.
3. **By-token classic-async `MoveNext` is the physical body.** Stop
   hollowing `MoveNext` when the builder type is
   `AsyncTaskMethodBuilder`, `AsyncTaskMethodBuilder<TResult>`,
   `AsyncValueTaskMethodBuilder`, or `AsyncValueTaskMethodBuilder<TResult>`.
   Do **not** un-hollow async-iterator`MoveNext`
   (`AsyncIteratorMethodBuilder` still matches `<>t__builder` by
   name today). `SetStateMachine` may still be acknowledged as empty
   support. This is a printer change: raise-discipline A/B render
   evidence applies even though no new raise is accepted.
4. **Whole-type duplicate suppression is residual, not slice 0.**
   After stop-hollowing, a whole-type listing may show both a
   reconstructed kickoff and the physical `MoveNext` until a later
   listing filter exists. That filter needs a structural
   kickoff↔`MoveNext` pair; it is not this slice. The filter does
   not rewrite the body a by-token request would print.
5. **One hop.** An async local-function `MoveNext` maps to that local
   function's stub, not the owning method. Same non-goal as #4466.

A concrete observation that would falsify slice 0: any of the four
member C# views of `CallsSyncSiblingFromAsync` or `NoAwait` still
prints `async` over `Start<TStateMachine>` at Full; or by-token
classic-async `MoveNext` still lacks `ReadValue` / `Yield`; or
async-iterator `MoveNext` is no longer hollow.

`MemberBodyProducerAsyncTests.ClassicAsyncWithoutAwait_UsesResolvedMethodBodyModifier`
currently asserts `async` on `NoAwait()`. Slice 0 flips that
assertion. Deleting the formatter change must fail the member-view /
whole-type renders, not a `TypeShellProducer` predicate test.

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
accept a new raise; it **does** change printer output (honesty
markers and un-hollowed classic-async `MoveNext`) and owes
raise-discipline A/B evidence for those renders.

Validity of declined output is the Partial/`UnsupportedNode` path,
already gated by existing fidelity-level machinery.

## Slices

This issue was opened from a named product failure. Raise discipline
requires measurement before **defining** further raise slices. Slice 2
from r1/r2 is not a slice; it is residual. After slice 1, take a
classic-async shape census on the pinned corpus before writing another
raise slice. Do not invent more `TryBuild*` methods from this document.

| Slice | Claim | Residual after it |
| --- | --- | --- |
| 0. Honesty | Per-projection `async` flag. Narrow-handoff `UnsupportedNode` + Partial. Stop hollowing classic-async `MoveNext` only. `Reconstructed` means current `TryBuild*` succeeded. Printer A/B. | #4472 fixture still declined, but honest. Async-iterator `MoveNext` still hollow. Whole-type may duplicate `MoveNext`. No Metadata lift. |
| 1. Yield operand + post-await statements | Accept `await Task.Yield(); return ReadValue(value);`. Requires: `HasUnexpectedStore` allow-list for the Yield temp store; explicit `LoadLocalAddress` decline in `RemapInPlace` before any remap; then a proven remap; statements after the await. Hoisted parameter binding is already present. The smaller `await Task.Yield();` (no later statements) is the accepted boundary of the same slice. Blocked until the Correct measurement in [Fidelity subject](#fidelity-subject) exists. | General multi-state dispatch, class SM, custom awaiters beyond Yield/`ValueTask`/`Task`, listing filter, structural Metadata descriptor, census-defined raises. |

Slice 0 is independently shippable: it wraps today's success set and
fixes presentation of the failure set. It does not include a Metadata
substrate migration.

## Proof obligations (every raise slice)

1. **Lowering shell.** C# async method, Roslyn, `runtime-async=off`,
   Release. Compiler-produced fixture required. #4472
   `CallsSyncSiblingFromAsync` is the first real witness for slice 1;
   ClassicAsync overlay remains the accepted-raise pins.
2. **Consumed ownership.** Kickoff stores into the state-machine
   local, `Start`, and `get_Task` are consumed when replacing the
   kickoff body. Each await point consumes its awaiter field, operand
   storage, state transition, and `GetResult`. Unconsumed `MoveNext`
   regions are a decline, recorded on `UserRegions`. Extra kickoff
   statements are `NotNarrowKickoffHandoff`, not silent deletion.
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
  `ClassicAsyncReconstructionPassTests` lookalikes; extra kickoff
  statements; unmatched `LoadLocalAddress` before remap exists;
  class state machine.
- Nested-function negative: async local function (one hop only).
- Pinned real witness: the #4472 `member` render, Before/After from
  `dotnet-inspect`, structural review per the decompiler PR template.

`NoAwait` is a slice-0 honesty witness, not a slice-1 raise unless a
later slice explicitly takes empty async methods.

## Non-goals

- Runtime-async reconstruction (`AwaitRecoveryPass`).
- Iterator / async-iterator reconstruction. Async-iterator `MoveNext`
  stays hollow in slice 0.
- Depending on #4461's `DirectCall.Caller` rewrite. #4466 already
  exposes declared-method lookup for Analysis attribution.
- Chaining an async local-function `MoveNext` to the owning method.
- Sharing a live `MetadataReader` or Analysis index across the
  decompiler boundary.
- Moving Analysis `MemberRef` / `MemberResolver` / `FrameworkIdentity`
  / attribution filters into Metadata.
- A second state-machine detector in Analysis, CLI, or Research.
- Teaching `TypeShellProducer` about reconstruction outcomes.
- Designing a state-dispatch raise before a corpus census.

## Layer ownership

| Fact | Owner |
| --- | --- |
| Attribute name classification (`StateMachineAsync`) | Metadata (already) |
| Structural attribute type-arg decode + same-assembly TypeDef uniqueness | Metadata residual, not slice 0 |
| Attribution filters (source-gen, GeneratedCode, Blazor) | Analysis, stays Analysis |
| `ClassicAsyncMachine` and the raise | Decompiler |
| `async` on a full-body render | Each projection's decompiler result flag, consumed by CLI member views and `MemberBodyProducer` listings |
| `async` on an API skeleton | Omitted. `TypeShellProducer` stays SRM-only |
| `MoveNext` → declared source for call-site attribution | Analysis (`ResolveDeclaredMethod`) |
| Presentation of Calls / Decompiled Source / overlays | CLI |

## Gates

Honesty is unverified until these exist. They must exercise the
**render**, not the metadata predicate.

| Gate | Fails if |
| --- | --- |
| Decompiled Source, Annotated Source, Cost Overlay, and Semantics Overlay of `NoAwait` / `CallsSyncSiblingFromAsync` | Still contain `async` together with `Start<` |
| Same renders | Fidelity is Full (narrow handoff must be Partial + `UnsupportedNode`) |
| Kickoff with an extra observable statement | Body replaced (must remain visible at Partial) |
| By-token `MoveNext` of a declined classic-async SM | Distinctive user logic (`ReadValue` / `Yield`) is absent |
| By-token `MoveNext` of an async-iterator SM | No longer hollow (`return;`) |
| Whole-type listing of `AsyncFixtures` | `NoAwait` still spelled `async` over the stub |
| `ClassicAsyncWithoutAwait_UsesResolvedMethodBodyModifier` | Still expects `async Task NoAwait()` (must flip) |
| Raise-discipline A/B for un-hollowed classic-async `MoveNext` | Unrecorded printer delta |

Deleting the formatter change must fail the four member views and the
whole-type listing. A green `TypeShellProducer` test is not this gate.
