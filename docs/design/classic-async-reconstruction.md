# Durable classic-async reconstruction

> **Map:** [Decompiler design](../decompiler.md) is the pipeline entry.
> [Raise-work discipline](../decompiler-raise-discipline.md) is the proof
> contract for every raise slice. This document owns the classic
> (`runtime-async=off`) state-machine inverse: the structured machine value,
> honesty rules for declined kickoffs, and the slice plan. Runtime-async
> (`AsyncHelpers.Await`) stays with `AwaitRecoveryPass`.

## Status

Design. Tracking: [#4472](https://github.com/richlander/dotnet-inspect/issues/4472).
Not implemented. r1–r5 were BLOCKED; this revision is the replacement
after integrating `origin/main` `f2457b9bb`.

`ClassicAsyncReconstructionPass` remains the current fixture-shaped raise.

## The problem

A declared classic-async method has two physical bodies:

```text
kickoff MethodDef     — Create builder, copy args, Start<TStateMachine>, return Task (void: terminal ret, no Task value)
<M>d__N.MoveNext      — user logic, awaiter protocol, SetResult / SetException
```

The source the user wrote lives in `MoveNext`. The MethodDef they ask
`member` about is the kickoff. The Analysis fixture

```csharp
public static async Task<int> CallsSyncSiblingFromAsync(int value)
{
    await Task.Yield();
    return ReadValue(value);
}
```

decompiles to the kickoff printed under an `async` signature:

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

That render is **already Partial**: `FidelityRemarks` keys on
`CSharpSpellability.InspectUnrepresentableMetadataName`, and every
`<…>d__N` spelling emits DEC0009. It is not Full. The lie is an
`async` method that still shows compiler handoff plumbing, with no
`UnsupportedNode` marker that the yield body was not reconstructed;
DEC0004 appears in the Fidelity Causes projection. Official
`NoAwait()` is the same shape. Async void
(`Async_VoidBuilder`) is the same shape without `return …Task`.

Decompiling `MoveNext` through the decompiler library / corpus
importer is worse: `TryAcknowledgeSupportMethod` replaces a
recognized state-machine `MoveNext` with `return;`. The shipped CLI
`member` / `type` surface does **not** show that type
(`ApiSurfaceExtractor` skips compiler-generated names unless tests
opt in). Un-hollowing is therefore a library-and-corpus change, not
a default CLI `member` change.

[#4466](https://github.com/richlander/dotnet-inspect/pull/4466) exposed
`LibraryBodyIndex.ResolveDeclaredMethod`; it did not raise the body.
[#4461](https://github.com/richlander/dotnet-inspect/pull/4461) later
attributed async call sites in Analysis. Reconstruction does not
depend on that Caller rewrite.

## Why the current pass cannot grow

`ClassicAsyncReconstructionPass` is a family of `TryBuild*` matchers
for the ClassicAsync overlay. `CallsSyncSiblingFromAsync` is none of
those. It is **void await, then statements, then return a non-await
expression**. No current matcher models that.

`TryBuildSingleAwaitReturn` is the only candidate with a single
`GetResult` and a valued `SetResult`. It declines **before**
`HasUnexpectedStore`:

1. `HasUnexpectedExpressionStatement(moveNext)` with an empty allow
   list. Void `await Task.Yield()` leaves a standalone
   `awaiter.GetResult();` statement. The allow list is only
   `AwaitUnsafeOnCompleted` / `SetException` / `SetResult`.
2. The `SetResult` operand must be a local whose store **contains**
   that `GetResult`. The fixture stores `ReadValue(this.value)`,
   which contains no await.
3. Only then would `HasUnexpectedStore` see the Yield temp
   (`stloc` / `ldloca` before `GetAwaiter`).

`HasHoistedUserState` does not fire: it requires `<…>5__` or
`<>7__wrap` stores. The fixture's parameter field is plain `value`,
stored in the kickoff; `RemapInPlace` already maps it through
`TryGetParameter`.

`RemapInPlace` has no `LoadLocalAddress` case. Unmatched nodes walk
children and leave `ok == true`, so a `MoveNext` local address would
splice into the kickoff as silent corruption. That path is currently
unreachable on this fixture because the earlier declines fire first.
Any raise of Yield must add an explicit `LoadLocalAddress` decline
before a proven remap.

Adding `TryBuildYieldThenReturn` would be another fixture, not an
inverse.

`TryGetKickoff` requires `Return { Value: LoadProperty { PropertyName:
"Task" } }`. Async-void kickoffs have no `.Task` and are never
recognized as kickoffs, while `LooksLikeClassicAsyncStateMachine`
still matches any `<>t__builder` field and hollows their `MoveNext`.

`LooksLikeClassicAsyncStateMachine` is name-only `<>t__builder`, so
async-iterator `AsyncIteratorMethodBuilder` matches too.

## Where `async` is actually stamped

CSharp cannot own a reconstruction-driven `async` keyword.
`ILInspector.Decompiler` references `ILInspector.CSharp`; the reverse
edge does not exist. `TypeShellProducer` is contractually SRM-only
(API skeletons omit `async`).

`MemberCodeProvider` currently computes **one** metadata flag
(`TypeShellProducer.RequiresAsyncBodyModifier(selection)`) and
`ApiOutputFormatter` feeds that same bit to Decompiled Source,
Annotated Source, Cost Overlay, and Semantics Overlay. Whole-type
listings restamp from metadata in `MemberBodyProducer`.

The outcome is **projection-invariant**. It is computed once by the
body-producing decompilation that has the sibling-import seam, before
view-specific printing. Slice 0 adds:

```text
ClassicAsyncProjection
  Function              canonical raised IrFunction
  Outcome               ClassicAsyncOutcome

ClassicAsyncOutcome
  NotClassic
  Reconstructed
  Declined(Reason, BodyDisposition)

BodyDisposition
  ReplacedNarrowHandoff
  PreservedOriginal
```

Metadata import adds `IsClassicAsync` to `MethodBody` / `IrFunction`
from the existing SRM classification (`StateMachineAsync` plus
`AsyncStateMachineAttribute`). Every function with that fact `Yes`
that imports successfully must finish `Reconstructed` or `Declined`;
`NotClassic` is invalid for that population. Import failure stays the
existing visible failed projection, not a success-shaped body.

The canonical function and outcome feed every view:

- `MemberCodeProvider` produces the canonical projection once whenever
  any member C# view is requested.
  Decompiled Source prints it directly. Research receives clones of
  that canonical function for Annotated Source, Cost Overlay, and
  Semantics Overlay; `RenderRaisedOverlay(importMethodBody: null)` does
  not recompute classic reconstruction.
- Whole-type output replaces `string? DecompileBody(...)` plus its four
  loose `out` parameters with an internal typed
  `DecompiledBodyProjection` carrying body text, constructor chain,
  unsafe/body-shape/destructor facts, and `ClassicAsyncOutcome`.
- `DecompilerResult` carries the same outcome. Its hand-written
  `Equals` and `GetHashCode` include outcome, decline reason, and body
  disposition. `with` copies preserve them.

The declaration rule is classification-aware:

| Metadata classification | Projection outcome | Declaration `async` |
| --- | --- | --- |
| `RuntimeAsync` | any | Preserve metadata `true` |
| `StateMachineAsync` | `Reconstructed` | `true` |
| `StateMachineAsync` | `Declined` | `false` (body carries the marker) |
| `StateMachineAsync` | `NotClassic` | Invalid; fail the gate |
| Other | any | `false` |

This preserves runtime-async methods whose awaiter recovery declined.

`TypeShellProducer.RequiresAsyncBodyModifier` is true for
`StateMachineAsync` plus `HasAsyncStateMachineAttribute`, including
async void.

The honesty precedent is `IteratorAcknowledgmentPass`: replace the
plausible handoff with `UnsupportedNode` **only when the body is
exactly the compiler handoff**. The node prints a marker and appears
as DEC0004 through Fidelity Causes. Extra observable statements stay
visible. Fidelity is already Partial on these kickoffs; the new signal
is the marker, not a Full→Partial transition.

## Design lessons

Same structured-system moves as
[#3636](https://github.com/richlander/dotnet-inspect/pull/3636) and
[type-forwarding resolution](type-forwarding-resolution.md).

### Put the property on the value that already crosses the boundary

"`async` on this full body" is the classification plus the canonical
projection outcome. `RequiresAsyncBodyModifier` remains the positive
raise flag. No new CSharp→Decompiler edge.

### Compute once, project many times

The machine is not a printer-local observation. Cost and Semantics
overlays intentionally print with `importMethodBody: null`; asking each
view to rediscover a sibling state machine makes `Reconstructed`
unreachable in those views. Canonical preparation owns sibling import,
recognition, reconstruction, decline marking, and the outcome. Views own
only annotation and spelling over clones of that prepared IR.

### Do not pretend an Analysis walk is a Metadata fact

Trusted SM uniqueness in `LibraryBodyAsyncSourceResolver` uses
Analysis types and attribution filters (source-gen, GeneratedCode,
Blazor). Slice 0 does **not** lift that walk. Kickoff acknowledgment
identifies the state machine from kickoff IR.

A later Metadata fact, if needed, is structural only (attribute
type-arg decode, same-assembly TypeDef, uniqueness among methods that
carry the attribute). Analysis keeps attribution filters. The two
populations may differ.

### Degradation is data

A declined kickoff is not a plausible `async` method. `MoveNext` is
not hollowed as a substitute for a missing reverse index.

### Do not depend upward

Analysis maps `MoveNext` → source for call-site attribution. That
stays Analysis. Reconstruction does not assume rewritten
`DirectCall.Caller`.

## The value

Working name: `ClassicAsyncMachine`. Pipeline types stay Decompiler
`TypeRef`.

```text
ClassicAsyncMachine
  Kickoff              MethodRef
  StateMachineType     TypeRef (from kickoff IR)
  MoveNext             MethodRef
  Kind                 Struct | Class
  BuilderField         FieldRef
  BuilderKind          Task | TaskOfT | ValueTask | ValueTaskOfT | Void
  StateField           FieldRef
  HoistedFields        { FieldRef, Kind, BoundKickoffArgument?, SourceName? }*
  AwaitPoints          { State, AwaiterField, AwaitedOperand, OperandStorage,
                         GetAwaiter, IsCompleted, GetResult,
                         Continuation (OnCompleted | UnsafeOnCompleted),
                         ResumeState }*
  Terminals            { SetResult?, SetException? }
  UserRegions          consumed MoveNext blocks/edges the raise claims
  Outcome              Reconstructed | Declined(Reason)
```

`BuilderKind` is observed from `FieldRef.Type` at acknowledgment
time by a new acknowledgment-only classifier. Slice 0 does **not**
change `IsAsyncMethodBuilder`: its only consumer is `FinalSetResult`,
so widening it would enable new non-generic ValueTask / void raises
through today's `TryBuild*`. Legacy raise eligibility stays unchanged.

`AwaitPoints` carry the awaited operand and its storage. Void
`GetResult` is a statement, not a value inside `SetResult`.

`UserRegions` is a consumption ledger. The current
`HasUnexpectedStore` / empty `HasUnexpectedExpressionStatement`
allow lists are not that ledger.

`Outcome = Reconstructed` in slice 0 means **a current `TryBuild*`
succeeded under the unchanged legacy builder gate**. Every
metadata-classified classic method that did not reconstruct is
`Declined`.

`Declined` reasons include `NoMoveNext`,
`UnrecognizedAwaiterProtocol`, `UnconsumedMoveNextRegion`,
`LoadLocalAddressUnmapped`, `ClassStateMachine`, and
`NonNarrowKickoffHandoff`. Body disposition records whether the narrow
handoff was replaced or the original body was preserved.

## Inverse

Roslyn's forward construct is `AsyncRewriter` /
`AsyncMethodToStateMachineRewriter` under `runtime-async=off`. The
honesty domain is compiler-produced classic **struct or class** state
machines whose builder is one of:

- `AsyncTaskMethodBuilder`
- `AsyncTaskMethodBuilder<TResult>`
- `AsyncValueTaskMethodBuilder`
- `AsyncValueTaskMethodBuilder<TResult>`
- `AsyncVoidMethodBuilder`

The raise domain remains Release-style structs. Debug class state
machines are recognized as narrow kickoffs, marked `Declined`, and keep
their physical `MoveNext`; slice 1 does not raise them. Runtime-async is
a different lowering. Async iterators
(`AsyncIteratorMethodBuilder`) are out of domain; their `MoveNext`
stays hollow.

Changing this inverse invalidates the two
`state-machine.classic-async-*` fact primitives in
`AwaitRecoveryFacts`. The changing PR updates that ledger.

## Honesty contract (slice 0)

Slice 0 ships **no new accepted raise**. It changes how a declined
kickoff is presented, and it stops erasing in-domain `MoveNext` in
the decompiler library and corpus.

1. **Declaration `async` follows classification plus the canonical
   `ClassicAsyncOutcome`.** Decompiled Source, Annotated Source, Cost
   Overlay, Semantics Overlay, and whole-type listings use the table in
   [Where `async` is actually stamped](#where-async-is-actually-stamped).
   Runtime-async keeps its metadata `async`, including when recovery
   declines. A classic `Declined` body loses `async` only together with
   the marker. `NotClassic` is impossible for a metadata-classified
   classic method. Skeletons stay without `async`.
2. **Every declined classic body gets an `UnsupportedNode` marker.**
   A narrow compiler handoff is replaced by the marker
   (`ReplacedNarrowHandoff`). A non-narrow body gets the marker inserted
   before the original statements (`PreservedOriginal`): no call,
   store, or return is deleted. `UnsupportedNode` prints the visible
   unsupported comment in code views. DEC0004 is observed separately
   through `DecompilerFindings.InspectFidelityCauses`; successful code
   rendering does not put it in `DecompilerResult.Diagnostics`.
3. **Narrow handoff ownership is exact and correlated.** Every
   statement must belong to one machine/local:
   - for a class SM, exactly one
     `StoreLocal(NewObject(StateMachineType::.ctor))`, bound to the
     local used by every later store and `Start`
   - no allocation statement for a struct SM
   - builder `Create` store
   - initial `<>1__state` store (typically `-1`)
   - copies of `this` / arguments onto SM fields (absent on
     `NoAwait`)
   - `Start`
   - terminal `Return(builder.Task)` / equivalent — **Task and
     ValueTask builders only**
   - terminal `Return(null)` — **async void** (the printer hides this,
     but every IL `ret` imports as a return node)
   Struct handoffs use the state-machine local/address form; Debug class
   handoffs use the reference-local form. Required statements occur
   once; optional argument copies are source-parameter-correlated.
   Anything else makes the body non-narrow: preserve it and prepend the
   marker.
4. **In-domain `MoveNext` is the physical body** in the decompiler
   library and corpus. Stop hollowing `MoveNext` when
   the acknowledgment-only `BuilderKind` is one of the five builders
   above. Do **not** change `IsAsyncMethodBuilder`, the legacy raise
   gate. Do **not**
   un-hollow `AsyncIteratorMethodBuilder`. `SetStateMachine` may
   still be empty support. This is a printer/corpus change:
   raise-discipline A/B and corpus-sensor evidence apply. The
   shipped CLI `member`/`type` surface omits compiler-generated
   types, so these gates are decompiler-library tests plus corpus
   A/B, not `dotnet-inspect member '…+<M>d__N'`.
5. **No product-surface listing filter.** Nested SM types are not
   on the default API surface, so whole-type listings do not print
   a second copy of `MoveNext`. Do not reserve a listing-filter
   slice for a non-problem.
6. **One hop.** An async local-function `MoveNext` maps to that
   local function's stub, not the owning method.

A concrete observation that would falsify slice 0: any
metadata-classified classic body is neither reconstructed nor visibly
marked; a non-narrow body's original statement disappears; a declined
classic declaration still says `async`; a declined runtime-async
method loses its metadata `async`; the four member views disagree on
outcome; an in-domain library `MoveNext` still lacks distinctive user
logic; or an async-iterator `MoveNext` is no longer hollow.

`MemberBodyProducerAsyncTests.ClassicAsyncWithoutAwait_UsesResolvedMethodBodyModifier`
currently asserts `async` on `NoAwait()`. Slice 0 flips that
assertion.

## Fidelity subject

Kickoff IL Exact is the wrong subject: raised source recompiles to a
new kickoff + `MoveNext`. Current declined kickoffs are already
Partial (DEC0009). Slice 0 does not claim a fidelity-level change;
it claims a marker.

**No new accepted raise ships until a named measurement exists.**
Intended contract: compile the raised method with Roslyn, Release,
`runtime-async=off`, and compare the regenerated `MoveNext` (or
behavioral execution covering result, exception, suspension, and
side effects). Until that harness exists, slices after 0 are
blocked. Slice 0 owes A/B for honesty markers and un-hollowed
in-domain `MoveNext` (library + corpus).

## Slices

Slice 1 is the named product failure that opened #4472. Further
raise slices are not designed here. After slice 1, take a
classic-async shape census on the pinned corpus before **defining**
another raise. Do not invent more `TryBuild*` methods.

| Slice | Claim | Residual after it |
| --- | --- | --- |
| 0. Honesty | Add SRM `IsClassicAsync`, canonical `ClassicAsyncProjection`, and typed whole-body carrier. All views clone one prepared IR/outcome. Every classic decline gets a marker: replace exact narrow handoff; prepend while preserving a non-narrow body. Correlate Debug class allocation and void `Return(null)`. Separate acknowledgment builder classification; leave legacy raise eligibility unchanged. Stop hollowing in-domain `MoveNext`; library + corpus A/B. | #4472 fixture still declined, but honest. Debug class SMs are honest but not raised. Async-iterator `MoveNext` still hollow. No trusted Metadata/Analysis lift. |
| 1. Void-await then statements then return | Accept `await Task.Yield(); return ReadValue(value);` as the first inverse raise from `AwaitPoints` + `UserRegions`, not as a new `TryBuild*` and not as a `HasUnexpectedStore` allow-list tweak. Must consume void `GetResult` as a statement, following statements, a non-await `SetResult` operand, the Yield operand temp, and an explicit `LoadLocalAddress` decline-then-remap. Hoisted parameter binding is already present. The smaller `await Task.Yield();` (no later statements) is the accepted boundary of the same slice. Blocked until the Correct measurement exists. | General multi-state dispatch, class SM, custom awaiters, structural Metadata descriptor, census-defined raises. |

## Proof obligations (every raise slice)

1. **Lowering shell.** C# async method, Roslyn, `runtime-async=off`,
   Release. Compiler-produced fixture required.
2. **Consumed ownership.** Narrow-handoff statements are consumed
   when replacing the kickoff. Each await point consumes awaiter
   field, operand storage, state transition, and `GetResult`.
   Unconsumed `MoveNext` regions decline. Extra kickoff statements
   are `NotNarrowKickoffHandoff`.
3. **Control-flow contract.** Await points are sequencing barriers.
   Successor identity across an await is the resume state.
4. **Replacement contract.** Valid on the accepted fixture family.
   Correctness uses the kickoff-aware measurement. IL Exact against
   the original kickoff MethodDef is not claimed.

### Fixture family (slice 1)

- Compiler-produced positive: `CallsSyncSiblingFromAsync`.
- Smallest accepted boundary: `await Task.Yield();` with no later
  statements.
- Keep: every ClassicAsync overlay method that already reconstructs.
- Still-flat negatives: existing lookalikes; extra kickoff
  statements; unmatched `LoadLocalAddress` before remap exists;
  class state machine for raising (slice 0 must still recognize and mark it).
- Nested-function negative: async local function (one hop only).
- Slice-0 honesty witnesses (not slice-1 raises): `NoAwait`,
  `Async_VoidBuilder`.
- Pinned real witness: the #4472 `member` render, Before/After from
  `dotnet-inspect`, structural review per the decompiler PR template.

## Non-goals

- Runtime-async reconstruction (`AwaitRecoveryPass`).
- Iterator / async-iterator reconstruction. Async-iterator `MoveNext`
  stays hollow.
- Depending on #4461's `DirectCall.Caller` rewrite.
- Chaining an async local-function `MoveNext` to the owning method.
- Moving Analysis `MemberRef` / `MemberResolver` / `FrameworkIdentity`
  / attribution filters into Metadata.
- A product-surface listing filter for nested SM types.
- Teaching `TypeShellProducer` about reconstruction outcomes.
- Designing a state-dispatch raise before a corpus census.
- Another `TryBuild*` matcher.

## Layer ownership

| Fact | Owner |
| --- | --- |
| Attribute name classification (`StateMachineAsync`) | Metadata (already) |
| Structural attribute type-arg decode | Metadata residual, not slice 0 |
| Attribution filters | Analysis |
| `ClassicAsyncMachine` and the raise | Decompiler |
| Classic-async metadata fact | Metadata import → `MethodBody` / `IrFunction` |
| Canonical classic projection | Decompiler (`IrFunction` + `NotClassic` / `Reconstructed` / `Declined`) |
| Whole-type body/outcome carrier | Decompiler internal `DecompiledBodyProjection` |
| Runtime-async declaration context | Metadata classification OR existing runtime-async IR fact |
| `async` on an API skeleton | Omitted |
| `MoveNext` → declared source | Analysis (`ResolveDeclaredMethod`) |
| CLI member / overlay presentation | CLI |
| Corpus / library `MoveNext` rendering | Decompiler |

## Gates

Honesty is unverified until these exist. They must exercise the
**render** or the named library/corpus surface, not the metadata
predicate. They must not assume current kickoffs are Full.

| Gate | Surface | Fails if |
| --- | --- | --- |
| Declined classic body, narrow and non-narrow | Four CLI member views + whole-type | Render lacks the unsupported marker comment |
| Same declined classic body | CLI Fidelity Causes | Lacks DEC0004 |
| Non-narrow classic body with extra call/store | Four CLI member views + whole-type | Any original statement disappears |
| Declaration modifier by classic outcome | Four CLI member views + whole-type | `Declined` still says `async`, `Reconstructed` omits it, or classic metadata yields `NotClassic` |
| Canonical outcome across four member views | CLI member projection | Views disagree, or an overlay without an import seam recomputes the machine |
| Declined runtime-async fixture | Four CLI member views + whole-type | Loses metadata `async` |
| Debug class narrow handoff | Decompiler library | Correlated `StoreLocal(NewObject(SM::.ctor))` prevents recognition |
| Async-void narrow handoff | Decompiler library | Terminal `Return(null)` prevents recognition |
| Non-generic ValueTask / async-void legacy raise negative | Decompiler library | Slice 0 newly reconstructs it |
| Whole-type typed carrier | `MemberBodyProducer` tests | Outcome is lost between `DecompileBody` and declaration formatting |
| `DecompilerResult` value semantics | Decompiler tests | Results differing only by outcome/reason/disposition compare equal, hash inconsistently, or lose outcome through `with` |
| In-domain `MoveNext` of a declined classic-async SM | Decompiler library (CLI type surface omits `d__` types) | Distinctive user logic absent |
| Async-iterator `MoveNext` | Decompiler library | No longer hollow |
| Whole-type listing of `AsyncFixtures` | `MemberBodyProducer` | `NoAwait` still spelled `async` over the stub without the marker |
| `ClassicAsyncWithoutAwait_UsesResolvedMethodBodyModifier` | Existing test | Still expects `async Task NoAwait()` |
| Corpus A/B for un-hollowed in-domain `MoveNext` | `CorpusSensor` / `IrImporter.ImportAssembly` | Unrecorded fidelity or coverage delta |

Deleting marker insertion must fail the render gate; deleting fidelity
cause enumeration must fail the DEC0004 gate. Deleting outcome-aware
modifier formatting must fail its independent gate. Widening
`IsAsyncMethodBuilder` must fail the legacy-raise negative. A green
`TypeShellProducer` test is not this gate. A green "fidelity is not
Full" check is not this gate.
