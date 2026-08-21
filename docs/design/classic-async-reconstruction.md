# Durable classic-async reconstruction

> **Map:** [Decompiler design](../decompiler.md) is the pipeline entry.
> [Raise-work discipline](../decompiler-raise-discipline.md) is the proof
> contract for every raise slice. This document owns the classic
> (`runtime-async=off`) state-machine inverse: the structured machine value,
> honesty rules for declined kickoffs, and the slice plan. Runtime-async
> (`AsyncHelpers.Await`) stays with `AwaitRecoveryPass`.

## Status

Design. Tracking: [#4472](https://github.com/richlander/dotnet-inspect/issues/4472).
Not implemented. r1–r10 were BLOCKED; this revision is the replacement
after integrating `origin/main` `9557e31f3`.

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

The outcome is **projection-invariant within the declared source-body
contract**. Decompiler owns one metadata-addressed preparation front
door:

```text
MetadataBodyProjector.Prepare(MetadataSource, MetadataBodyAddress)

MetadataBodyAddress
  Exact(MetadataMethodAddress)
  Selector(TypeFullName, MethodName, OverloadIndex, PublicOnly)

MetadataBodyProjectionResult
  AddressFailed(Diagnostics)
  Resolved(MetadataBodyResolvedProjection)

MetadataBodyResolvedProjection
  Address                module-scoped MethodDef identity
  AsyncClassification    RuntimeAsync | ClassicAsync | AsyncIterator | Other
  Bodyless
  ImportFailed(Diagnostics)
  Imported(MetadataBodyProjection)

MetadataBodyProjection
  ImportedFunction        pristine annotation/IL anchor snapshot
  Raised                  StageBodyProjection
  Lowered                 StageBodyProjection, materialized on request
  ClassicAsyncDecision?   captured if the canonical pass reaches its decision
  ClassicAsyncOutcome?    present exactly when the decision is present

StageBodyProjection
  Prepared(IrFunctionSnapshot)
  Failed(Diagnostics)

PreparedStageBody.Render(PrinterOptions)
  RenderedFunction       private clone after print analysis
  DecompilerResult       includes ClassicAsyncOutcome
  PrintedRanges

PassContext.ClassicAsyncDecision
  None                   pass recognizes and records on the host function
  Supplied(Decision)     pass validates host identity and applies only

ClassicAsyncDecision
  KickoffIdentity        module-scoped MethodDef identity
  Outcome
  Machine?               detached recognition/consumption value
  Application?           exhaustive detached host-mutation value

ClassicAsyncApplication
  BodyEdit               Replace(owned body) | Prepend(owned marker) | None
  LocalTable?            locals, names, scopes, eliminated slots
  TypeFactContribution   complete companion fact maps/sets
  DiagnosticsToAdd
  FunctionFactChanges    flags and provenance/fidelity inputs written by pass

IrFunctionSnapshot
  FunctionTree
  LocalState
  TypeFacts
  Diagnostics
  FunctionFacts
  ClassicAsyncDecision

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
`AsyncStateMachineAttribute`). `StateMachineAsync` alone is not this
fact: it also includes `AsyncIteratorStateMachineAttribute`.

`Exact` validates the existing `MetadataMethodAddress` against the
live source. `Selector` uses the existing name/ordinal resolver, then
immediately becomes an `Exact` address; all later import,
classification, preparation, and rendering use that resolved
MethodDef. This preserves the intentional fallback for an absent or
stale carried token in MemberCodeProvider, Research, public/whole-type
body production, accessors, and Body Shape. Failure to resolve the
selector is a typed outer `AddressFailed(Diagnostics)`,
never a direct-printer bypass or a plausible unmarked body.

After resolution, the projector reads the MethodDef body status once.
An abstract, extern, interface, or other RVA-zero method is
`Resolved(..., Bodyless)`: address and metadata classification remain
available, but there is no import, stage, outcome, marker, or render
failure. A carrier may preserve its existing typed absence diagnostic;
that does not turn the projector state into `ImportFailed` or a failed
stage. Selector ordinals continue to count bodyless methods exactly as
the existing resolver does. A declared-source consumer does not use RVA,
`HasBody`, or a null import to choose between bodyless and body-bearing
projection or to map that choice to its carrier before calling the
projector.

Preparation imports once. A null import or importer-owned fatal
diagnostic before any canonical pass is `ImportFailed`; no
`MetadataBodyProjection` exists. A successful, diagnostic-free import
creates `Imported(MetadataBodyProjection)` before stage preparation, so
a later pass or render failure cannot erase the successful-import
boundary. The first requested stage then runs through the canonical
pass pipeline with the sibling-import seam. The
`ClassicAsyncReconstructionPass` recognizes once, records its typed
decision on the host function, and applies it. Preparation captures
that decision and supplies it through `PassContext` when building any
other stage snapshot; the pass validates the kickoff identity and
applies without re-recognizing. The snapshots are owned mutable IR;
consumers print detached root clones, not the stored instances.
Stage materialization is serialized. While no decision exists, the next
stage runs with `None`; reaching the classic pass captures its decision
even if a later pass fails. Once captured, every later stage receives
`Supplied`. A stage that fails before reaching the classic pass does not
invent an outcome or prevent another requested stage from becoming the
one capture run.
`PreparedStageBody.Render` is the sole
source-body emission seam: it clones the stored snapshot, performs
the selected style lenses plus print analysis without rerunning the
default/lowered structural pipeline, and returns the rendered clone,
result, and printed ranges as one value.

The pass remains in `IrPasses.Default` and `IrPasses.Lowered`. A
standalone seam-enabled pipeline with no supplied decision recognizes
through that same implementation, records the outcome on its function,
and produces the same body as prepared output. This keeps stage dumps,
corpus sensors, validity/fidelity harnesses, and render A/B on the
shipped policy without requiring a MethodDef handle. A null-seam
physical pipeline cannot recognize a companion machine and keeps no
classic outcome.

The cached decision borrows no `IrNode`, block, local, edge, mutable
diagnostic collection, or other function sidecar from the first stage
host. `ClassicAsyncMachine.UserRegions` records stable
IL-origin/structured identities. `ClassicAsyncApplication` owns the
body/marker fragments and the complete deterministic mutation the pass
applies to a host: body edit, local-table reset, companion type-fact
contribution, pass-authored diagnostics, and every function fact the
pass changes. Recognition and replay both call that one application
method. A new pass mutation outside the application is a contract
failure.

`IrFunctionSnapshot.CloneDetached` is a root-level operation, not a
cast of the existing subtree-only `IrNode.Clone`. It recursively clones
the tree and independently copies every mutable root sidecar consumed
by later passes or the printer, including diagnostics, while immutable
metadata values may be shared. Mutating a render clone cannot change a
prepared snapshot, another stage, or a later render. Applying a
decision to a different module-scoped kickoff remains a typed stage
failure.

A supplied decision is scoped to only the prepared top-level host.
`PassContext.NestedPipelineContext` preserves the sibling-import seam,
type oracle, and shared recursion guard but resets
`ClassicAsyncDecision` to `None`, including on non-stepping runs.
Imported lambdas, local functions, and reconstruction companions
therefore recognize/decline under their own identity. Any embedded
marker/body travels with their IR, but their decision cannot overwrite
the outer prepared outcome.

Once an address resolves, its classification is metadata-only and
exists even when the method is bodyless or body preparation fails. The
unions separate address failure, body absence, import failure,
post-import stage failure, and a decided body:

- selector-resolution failure is `AddressFailed`, not a handle-less
  rendering mode and not a classified body
- `Resolved(..., Bodyless)` is a successful metadata selection with no
  `ClassicAsyncOutcome`; it is never `NotClassic`, `Declined`, or a
  diagnosed body failure. The projector creates no `DecompilerResult`;
  `ProduceBody` may materialize its existing typed absence diagnostic
  while mapping this state to `Absent`
- `Resolved(..., ImportFailed)` means import returned no function or an
  importer-owned fatal diagnostic; it has no stage, decision, or
  `ClassicAsyncOutcome`
- `Resolved(..., Imported)` proves import succeeded even if its requested
  `StageBodyProjection` is `Failed`; it is the boundary consumers use
  when their existing accounting distinguishes import from later failure
- every successfully prepared stage with `IsClassicAsync = Yes` is
  `Reconstructed` or `Declined`; `NotClassic` is invalid there
- stage preparation failure before the classic pass has no decision or
  outcome; failure after that pass retains both. Render failure likewise
  retains an already prepared outcome. Every failure remains visible and
  does not need a marker in nonexistent output
- an unsupported custom builder is
  `Declined(UnsupportedBuilder, PreservedOriginal)`, not outside the
  classified population
- an async iterator is `StateMachineAsync` but
  `IsClassicAsync = No`, so `NotClassic` is its required outcome

The canonical function and outcome feed every declared source-body
projection:

- `MemberCodeProvider` calls `MetadataBodyProjector` once whenever any
  member C# artifact is requested. Decompiled Source calls the prepared
  stage's render seam; Research receives the same prepared value. Its
  exact-token and name/ordinal paths differ only in
  `MetadataBodyAddress`; both canonicalize before import. `Bodyless`
  produces no C# body output, marker, or body modifier. It preserves the
  existing non-null failed `DecompilerResult` with null `Output` and the
  visible DEC0001 "has no IL body" diagnostic when Decompiled Source is
  requested; `StyledProjectionProduced` remains false. Fidelity Causes
  maps the same state to its existing typed `Absent`, not `Failed`.
- `ResearchViews.ProjectMember` accepts that value. Direct Research and
  Research-query callers that do not come through `MemberCodeProvider`
  call the same Decompiler front door once. Annotated Source, Annotated
  Source Document, Cost Overlay, Semantics Overlay, and Fact Row C#
  line mapping all render clones through `PreparedStageBody`. No
  Research renderer invokes `CSharpPrinter`, pass execution, or classic
  reconstruction directly. `Bodyless` preserves each request's current
  visible absence path rather than returning success-shaped empty
  artifacts: requested Annotated Source, Cost Overlay, and Semantics
  Overlay receive their failed result, Source Document receives
  `SourceDocumentFailure` with no document, and a Fact Row request
  remains an explicit Research failure rather than an empty row set.
- `AnnotationStage.Raised` consumes `Raised`.
  `AnnotationStage.Lowered` consumes `Lowered`, prepared from the same
  classic decision. If a stage-compatible classic snapshot
  cannot be produced, Source Document / Annotated Source returns a
  typed visible failure; it never falls back to an independently raised
  or raw unmarked kickoff.
- `MemberBodyProducer.ProduceBody`, whole-member composition, and
  whole-type composition use the same front door. The public
  `MemberBodyProductionResult` and internal whole-type
  `DecompiledBodyProjection` carry classification, body text/shape
  facts, and `ClassicAsyncOutcome`. Stale-token and accessor fallbacks
  use `Selector`, not direct import/print. `Bodyless` maps to
  `MemberBodyProductionStatus.Absent` and preserves that API's current
  typed absence diagnostic. Whole-member production remains `Complete`
  with declaration-only text, and whole-type composition includes the
  same declaration, both with no diagnostic or marker.
- Metadata-addressed `BodyShapeSearch` uses the front door too. Its
  fidelity/search policy remains separate, but it does not create a
  second classic-async decision.
  `BodyShapeSearch.IncompleteBodyReason` replaces its current
  classic-or-async-iterator attribute union with the exact prepared
  outcome. `Bodyless` preserves the current silent skip: it is not
  inspected, incomplete, matched, or recorded as a search failure.
  `AddressFailed` and `ImportFailed` record failure without incrementing
  `MethodsInspected`; `Imported` increments it exactly once before stage
  or render disposition, so a later failure records both one inspected
  method and one search failure as it does today.
- `CSharpBodyDiff` remains outside this contract: it renders one
  supplied physical tree, does not stamp a declaration, and does not
  claim a `ClassicAsyncOutcome`.
- `PipelineStages` is an orchestration exception to the projector front
  door, not an exception to classic policy. It runs the same
  seam-enabled pass pipeline; its terminal C# must stay byte-identical
  to prepared Raised output. Corpus and harness sweeps follow the same
  rule. Tests may intentionally exercise passless/raw APIs.
- A `DecompilerResult` rendered from a successful
  `StageBodyProjection` or seam-enabled canonical pipeline carries the
  same outcome. `AddressFailed`, `Bodyless`, `ImportFailed`, stage
  failure before a decision, null-seam physical rendering, and
  intentionally passless raw-IR rendering have no classic outcome. Its
  hand-written `Equals` and `GetHashCode` include outcome presence,
  decline reason, and body disposition. `with` copies preserve them.

`CSharpBodyDiff` is intentionally not another source-body projection.
Its currency is C# lines anchored to IL origins in one physical
MethodDef. Its null `importMethodBody` seam keeps lambda,
local-function, iterator, and classic companion bodies out of that
coordinate plane; `StatementStartOffset` relies on that property.
Routing it through `MetadataBodyProjector` would admit foreign offsets,
change implementation-diff lines/LCS, and require a separate
correspondence design. Slice 0 does none of those things.

The diff therefore remains a named seam-free physical-evidence
projection. It can display the physical kickoff because it neither
prints a declaration nor claims reconstructed source. It carries no
classic outcome or marker. Its own gate pins the null seam, MethodDef
provenance, and unchanged non-classic line/offset output.

The declaration rule uses the exact facts:

| Metadata fact | Projection outcome | Declaration `async` |
| --- | --- | --- |
| Any classification | `Bodyless` | `false`; declaration/skeleton only |
| `RuntimeAsync` | prepared or failed | Preserve metadata `true` |
| `IsClassicAsync = Yes` | preparation failed | Preserve metadata `true`; body is visibly failed |
| `IsClassicAsync = Yes` | `Reconstructed` | `true` |
| `IsClassicAsync = Yes` | `Declined` | `false` (successful render carries marker; failed render is visible) |
| `IsClassicAsync = Yes` | `NotClassic` | Invalid; fail the gate |
| Async iterator (`StateMachineAsync`, `IsClassicAsync = No`) | `NotClassic` | Preserve current `false` |
| Other | `NotClassic` | `false` |

This preserves runtime-async methods whose awaiter recovery declined.
It also keeps async iterators out of the classic contract.

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

The machine is not a printer-local observation. Research renderers
currently import and raise independently; asking each view to
rediscover a sibling state machine makes the result depend on section
selection and import-seam availability.
`ClassicAsyncReconstructionPass` owns sibling recognition,
reconstruction, decline marking, and the typed decision. Canonical
preparation owns one decision session, stage snapshots, and outcome
propagation. Views own only annotation and spelling over the
`PreparedStageBody.Render` result.

This applies to direct Research callers and structured source-body
artifacts, not only the four familiar text overlays. Fact Row C#
anchors must refer to the same function whose lines the sibling code
artifact prints. It does not apply to physical-body evidence whose
identity contract forbids companion-body import.

Preparation does not obtain invariance by running `PrintRaised` and
`PrintLowered` independently and comparing their answers. Its first
stage lets the pass recognize one `ClassicAsyncMachine` / decline
decision; later stage pipelines reach the same pass position with that
decision supplied and apply the same exhaustive detached mutation
without recognition. `Reconstructed` installs owned body/local state
and merges the captured companion type-fact contribution; `Declined`
applies the decided replacement/preservation edit and diagnostic.
Stage pipelines may still differ in cosmetic sugar, but cannot differ
on classic identity, outcome, consumed regions, or pass-owned state.

An independent top-level pipeline is a separate product/evidence
projection, not a second view inside that prepared request. With the
sibling seam it invokes the same pass recognition once. Compiler-
produced parity fixtures gate that its final body/outcome equals the
prepared Raised projection for both `Reconstructed` and `Declined`.

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
  UserRegions          stable IL-origin/structured identities the raise claims
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
succeeded under the unchanged legacy builder gate**. Every method with
`IsClassicAsync = Yes` that prepared successfully and did not
reconstruct is `Declined`.

`Declined` reasons include `NoMoveNext`,
`UnrecognizedAwaiterProtocol`, `UnconsumedMoveNextRegion`,
`LoadLocalAddressUnmapped`, `ClassStateMachine`, and
`NonNarrowKickoffHandoff`. `UnsupportedBuilder` covers a classic
attribute with a builder outside the five acknowledged kinds and always
uses `PreservedOriginal`. Body disposition records whether the narrow
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
stays hollow. A custom classic builder is outside the acknowledgment
and raise domains but inside `IsClassicAsync`; it visibly declines
without deleting its kickoff.

Changing this inverse invalidates the two
`state-machine.classic-async-*` fact primitives in
`AwaitRecoveryFacts`. The changing PR updates that ledger.

## Honesty contract (slice 0)

Slice 0 ships **no new accepted raise**. It changes how a declined
kickoff is presented, and it stops erasing in-domain `MoveNext` in
the decompiler library and corpus.

1. **Declaration `async` follows classification plus the canonical
   `ClassicAsyncOutcome`.** MemberCodeProvider declarations, public
   typed-body production, whole-member composition, and whole-type
   listings use the table in
   [Where `async` is actually stamped](#where-async-is-actually-stamped).
   Runtime-async keeps its metadata `async`, including when recovery
   declines. A classic `Declined` body loses `async` only together with
   the marker. `NotClassic` is impossible only when
   `IsClassicAsync = Yes`; it is required for async iterators.
   `Bodyless` has no classic outcome and stays a declaration/skeleton
   without `async`, even if malformed metadata carries an async
   attribute.
2. **Every declined classic body gets an `UnsupportedNode` marker.**
   A narrow compiler handoff is replaced by the marker
   (`ReplacedNarrowHandoff`). A non-narrow body gets the marker inserted
   before the original statements (`PreservedOriginal`): no call,
   store, or return is deleted. `UnsupportedNode` prints the visible
   unsupported comment in code views. DEC0004 is observed separately
   through `DecompilerFindings.InspectFidelityCauses`; successful code
   rendering does not put it in `DecompilerResult.Diagnostics`.
   Prepared Raised and Lowered snapshots apply the same decided
   outcome. A typed import/preparation/render failure is already visible
   failure and does not fabricate a marker-only body.
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
   local function's stub, not the owning method. A prepared outer
   decision is never inherited by that nested pipeline: its derived
   context resets the decision to `None`, so the local function gets
   its own identity, decision, marker/body, and outcome.
7. **Every metadata-addressed declared source-body path uses one front
   door.**
   MemberCodeProvider, direct Research, Research queries,
   `MemberBodyProducer.ProduceBody`, whole-member/whole-type
   composition, and Body Shape search call `MetadataBodyProjector`.
   The front door accepts either an existing `MetadataMethodAddress` or
   a structured type/name/ordinal/visibility selector that resolves
   once to that exact address. Missing/stale-token and accessor
   fallbacks remain successful when their selector resolves; resolution
   failure is typed and visible. A resolved bodyless method is typed
   `Bodyless`, not `ImportFailed` or a failed stage: typed body
   production returns `Absent`,
   whole-member/type output remains declaration-only, C# body output is
   absent while existing CLI/Research absence diagnostics remain visible,
   and Body Shape silently skips it.
   Annotated Source Document and Fact Row line mapping are part of the
   Research set. `CSharpBodyDiff` is the explicit seam-free
   physical-evidence exception; `PipelineStages` is the explicit
   canonical-pipeline diagnostic exception.
8. **Evidence runs the same classic policy.** A seam-enabled
   `CSharpPrinter.PrintRaised`, `PipelineStages`, corpus profile, or
   validity/fidelity harness executes
   `ClassicAsyncReconstructionPass` with no supplied decision. The pass
   recognizes once and records the same typed decision/outcome that
   `MetadataBodyProjector` captures. `IrImporter.ImportAssembly` may
   remain a handle-free function sweep because the pass uses its
   function plus sibling-import context.

A concrete observation that would falsify slice 0: any
successfully prepared `IsClassicAsync` body is neither reconstructed nor
visibly marked; a non-narrow body's original statement disappears; a
declined classic declaration still says `async`; a declined
runtime-async method loses its metadata `async`; any declared
source-body artifact disagrees on outcome; a Fact Row anchor refers to
an independently raised body; a failed preparation becomes a plausible
body; a resolvable name-addressed member bypasses preparation; replay
loses companion type facts or aliases mutable snapshot state; an outer
decision reaches a nested function; a bodyless member loses its existing
visible absence signal, gains C# body output, a marker or modifier, or
becomes an inspected Body Shape; an import failure counts as inspected or
a post-import stage failure does not; the physical C# diff imports a
companion body; a stage, corpus, or harness render differs from prepared
Raised output for the same classic fixture; an in-domain library
`MoveNext` still lacks distinctive user logic; or an async-iterator
`MoveNext` is no longer hollow.

`MemberBodyProducerAsyncTests.ClassicAsyncWithoutAwait_UsesResolvedMethodBodyModifier`
currently asserts `async` on `NoAwait()` for both its valid-token and
intentionally stale-token rows. Slice 0 flips both assertions; the
stale-token row also gates selector canonicalization.

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
| 0. Honesty | Add SRM `IsClassicAsync`, structured exact-or-selector addressing with resolved `Bodyless`, Decompiler-owned `MetadataBodyProjectionResult`, detached function snapshots, complete classic application values, and typed body carriers. `ClassicAsyncReconstructionPass` remains the single decision implementation: the projector captures/replays one decision across top-level stages, nested contexts reset it, and seam-enabled stage/corpus/harness runs recognize through the same pass. Every declared source-body path canonicalizes through the front door; seam-free physical evidence remains separate. Every classic decline gets a marker: replace exact narrow handoff; prepend while preserving a non-narrow body. Correlate Debug class allocation and void `Return(null)`. Leave legacy raise eligibility unchanged. Stop hollowing in-domain `MoveNext`; library + corpus A/B. | #4472 fixture still declined, but honest. Debug class SMs are honest but not raised. Async-iterator `MoveNext` still hollow. Custom builders visibly decline with preserved bodies. Bodyless members remain absent/declaration-only. Physical C# diff remains MethodDef-scoped and seam-free. No trusted Metadata/Analysis lift. |
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
- Changing `CSharpBodyDiff` from physical MethodDef evidence into a
  reconstructed source-body comparison.
- Designing a state-dispatch raise before a corpus census.
- Another `TryBuild*` matcher.

## Layer ownership

| Fact | Owner |
| --- | --- |
| Attribute name classification (`StateMachineAsync`) | Metadata (already) |
| Structural attribute type-arg decode | Metadata residual, not slice 0 |
| Attribution filters | Analysis |
| `ClassicAsyncMachine`, decision, and application | Decompiler `ClassicAsyncReconstructionPass` |
| Classic-async metadata fact | Metadata import → `MethodBody` / `IrFunction` |
| Exact-or-selector address canonicalization and bodyless/imported/failure union | Decompiler `MetadataBodyProjector` over existing `MetadataMethodAddress` / resolver |
| Complete root snapshot clone | Decompiler `IrFunctionSnapshot` |
| Cross-stage decision capture/supply | Decompiler `PassContext` scoped to one top-level prepared host |
| Nested decision reset | Decompiler `PassContext.NestedPipelineContext` |
| Canonical classic projection | Decompiler stage snapshots + `NotClassic` / `Reconstructed` / `Declined` |
| Public typed-body carrier | Decompiler `MemberBodyProductionResult` |
| Whole-type body/outcome carrier | Decompiler internal `DecompiledBodyProjection` |
| Runtime-async declaration context | Metadata classification OR existing runtime-async IR fact |
| `async` on an API skeleton | Omitted |
| `MoveNext` → declared source | Analysis (`ResolveDeclaredMethod`) |
| Research annotation, document, overlay, and Fact Row presentation | Research over Decompiler-prepared clones |
| CLI member presentation | CLI |
| Seam-free physical C# body evidence | Decompiler `CSharpBodyDiff` (no classic outcome) |
| Corpus / library `MoveNext` rendering | Decompiler |

## Gates

Honesty is unverified until these exist. They must exercise the
**render** or the named library/corpus surface, not the metadata
predicate. They must not assume current kickoffs are Full.

| Gate | Surface | Fails if |
| --- | --- | --- |
| Exact async population matrix | Metadata + Decompiler tests | Async iterator is rejected as invalid `NotClassic`, or custom classic builder escapes visible `Declined` |
| Canonical front-door architecture | Source-architecture test `MetadataAddressedBodyProjectionUsesCanonicalFrontDoor` | Product-consumer references outside `CSharpPrinter` / pass definitions to any body-emitting printer API or direct top-level `IrPasses.Run*` differ from the complete set: `MetadataBodyProjector` / `PreparedStageBody`, seam-free `CSharpBodyDiff`, and canonical-stage `PipelineStages` |
| Body-status policy ownership | Source-architecture test `DeclaredSourceBodyStatusUsesProjector` | A declared-source consumer uses RVA/`HasBody`/null import to choose `Bodyless` versus import/preparation/render or to map that state to a carrier; a body-status reference falls outside the closed allow list, or any named migration site remains |
| Exact/selector address parity | Decompiler projector + existing `ClassicAsyncWithoutAwait_UsesResolvedMethodBodyModifier` valid/stale-token rows + bodyless overload ordinal | A resolvable selector bypasses the projector, resolves a different MethodDef, drops a bodyless method from ordinal counting, or differs from exact-address classification/state/outcome/render; unresolved selection becomes plausible output |
| Resolved bodyless lifecycle | Decompiler + `RenderStyleConfigTests.NoBodyMethod_ProducesResultWithoutOutput_SoNoStyledSource` + `FidelityCauseSectionTests.BuildInspection_DistinguishesNoBodyFromImporterFailure` + `AnnotatedSourceDocumentProjectionTests.BodylessMemberDocumentFailureKeepsSiblingProjection` + whole-member/type + Body Shape | Exact/selector bodyless results differ; typed body is not `Absent` with its existing absence diagnostic; CLI/Research loses its existing visible absence failure, emits C# body output, marks style consumed, or changes Fidelity Causes from `Absent`; a marker or modifier appears; whole-member is not `Complete` declaration-only text; whole-type is not diagnostic-free declaration-only; Body Shape counts it as inspected/incomplete/failure |
| Complete classic application replay | Decompiler pass/projector tests with accepted interface-fact + declined diagnostic fixtures | A second prepared stage recognizes again; supplied replay differs in body, local state, type facts, diagnostics, provenance/fidelity, modifier state, or outcome; a supplied decision applies to a different kickoff |
| Snapshot clone isolation | Decompiler snapshot/render tests with diagnostic-producing fixture | Mutating one render clone changes a prepared snapshot, another stage/render, diagnostics, local state, type facts, or outcome |
| Nested decision scope | Decompiler pass/projector test with compiler-produced classic async local function/lambda | Outer supplied decision reaches a nested pipeline, nested identity validation fails against the outer kickoff, or nested marker/body/outcome is attributed to the outer decision |
| Prepared/canonical pipeline parity | `PipelineStageTests.DumpMethod_FinalCSharp_IsTheShippedProductOutput` with compiler-produced reconstructed + declined fixtures | Terminal seam-enabled stage C# or outcome differs from prepared Raised output |
| Corpus/harness classic policy | Decompiler harness contract tests + `CorpusSensor` classic profile | Handle-free sweep or fidelity/validity/render A/B bypasses the pass, misses a decline marker, or loses an accepted reconstruction |
| Declined classic body, narrow and non-narrow | Five CLI code views + public typed body + whole-type | Render lacks the unsupported marker comment |
| Same declined classic body | CLI Fidelity Causes | Lacks DEC0004 |
| Non-narrow classic body with extra call/store | Five CLI code views + public typed body + whole-type | Any original statement disappears |
| Declaration modifier by exact classic outcome | CLI declarations + public typed body + whole-member/whole-type | `Declined` still says `async`, `Reconstructed` omits it, `IsClassicAsync` yields `NotClassic`, or an async iterator is treated as classic |
| Canonical outcome across member artifacts | CLI + direct `ResearchViews.ProjectMember` + Research queries | Views disagree, Source Document reimports, or an overlay without an import seam recomputes the machine |
| Raised/Lowered Research contract | Annotated Source + Annotated Source Document | Stage snapshots recompute the outcome or unsupported stage preparation falls back to raw unmarked kickoff |
| Fact Row source-line identity | Direct Research + CLI Facts | C# lines are mapped against a separately raised function |
| Address/bodyless/import/stage/render union | Decompiler + CLI + Research + whole-type + Body Shape accounting | `AddressFailed`, `Bodyless`, `ImportFailed`, and post-import stage failure collapse; importer-null/fatal-diagnostic failure counts as inspected; a failure after successful import does not count as inspected; a failure has a success-shaped outcome/body; or stage/render failure drops an already captured outcome |
| Declined runtime-async fixture | CLI declarations + public typed body + whole-member/whole-type | Loses metadata `async` |
| Debug class narrow handoff | Decompiler library | Correlated `StoreLocal(NewObject(SM::.ctor))` prevents recognition |
| Async-void narrow handoff | Decompiler library | Terminal `Return(null)` prevents recognition |
| Non-generic ValueTask / async-void legacy raise negative | Decompiler library | Slice 0 newly reconstructs it |
| Typed body carriers | `MemberBodyProducer` tests | Outcome/classification is lost in `ProduceBody` or between `DecompileBody` and declaration formatting, or `Bodyless` becomes `ImportFailed` or a failed stage |
| Body Shape source projection | Decompiler product tests with bodyless, importer-failure, and post-import render-failure fixtures | Search creates a second classic decision, retains the broad classic-or-async-iterator heuristic, records `Bodyless` as inspected/incomplete/failure, counts importer failure as inspected, or fails to count post-import failure as inspected |
| Physical C# body diff boundary | `CSharpBodyDiff` product tests | Diff wires `importMethodBody`, admits foreign MethodDef origins, carries a classic outcome/marker, or changes non-classic lines/offsets |
| `DecompilerResult` value semantics | Decompiler tests | Results differing only by outcome/reason/disposition compare equal, hash inconsistently, or lose outcome through `with` |
| In-domain `MoveNext` of a declined classic-async SM | Decompiler library (CLI type surface omits `d__` types) | Distinctive user logic absent |
| Async-iterator `MoveNext` | Decompiler library | No longer hollow |
| Whole-type listing of `AsyncFixtures` | `MemberBodyProducer` | `NoAwait` still spelled `async` over the stub without the marker |
| `ClassicAsyncWithoutAwait_UsesResolvedMethodBodyModifier` | Existing valid-token + stale-token selector rows | Either row still expects `async Task NoAwait()`, or selector and exact-address rows disagree |
| Corpus A/B for honesty + un-hollowed in-domain `MoveNext` | `CorpusSensor` / `IrImporter.ImportAssembly` | Marker/reconstruction differs from product policy, or an unrecorded fidelity/coverage delta appears |

Deleting marker insertion must fail the render gate; deleting fidelity
cause enumeration must fail the DEC0004 gate. Deleting outcome-aware
modifier formatting must fail its independent gate. Widening
`IsAsyncMethodBuilder` must fail the legacy-raise negative. A green
`TypeShellProducer` test is not this gate. A green "fidelity is not
Full" check is not this gate.

The architecture test discovers every product-project reference to
`CSharpPrinter` body emission and direct top-level pass execution; it
does not enumerate only today's method names. Set equality makes both a
new bypass and a stale exception fail. `IrImporter.Import` alone is
acquisition, not rendering, and is intentionally not denied; combining
it with any product C# emission hits the gate. Adding a declared
source-body entry point without routing it through
`MetadataBodyProjector` / `PreparedStageBody`, or adding a raw/physical
exception without naming its separate contract, must fail.

Removing decision capture must fail the second-stage replay gate.
Removing no-decision pass recognition must fail prepared/canonical
parity and the corpus/harness gate. The parity fixtures must include one
current accepted reconstruction and one visible decline; extending the
existing non-async-only stage test without those fixtures is vacuous.
Dropping a function-state field from application replay must fail the
complete-application or clone-isolation gate. Reusing a supplied
top-level decision in `NestedPipelineContext` must fail the nested
classic fixture. Removing selector canonicalization must fail the
stale-token row, not silently retain a direct printer branch.
Collapsing `Bodyless` into `ImportFailed`, a failed stage, `NotClassic`,
or a consumer-side RVA precheck must fail exact/selector bodyless parity
and all three surface dispositions. Adding a consumer-side body-status
branch must also fail `DeclaredSourceBodyStatusUsesProjector`, even when
its current render happens to match.

`DeclaredSourceBodyStatusUsesProjector` is symbol/reference set equality
over the declared-source consumer projects, not a ban on every metadata
body read. The body-status decision sites that move are
`MemberCodeProvider` selection/Fidelity Causes,
`MemberBodyProducer.ProduceBody` and accessor/body composition,
`ResearchViews` null-import absence handling, and `BodyShapeSearch`.
The test has a closed allow list for body reads that acquire independent
evidence without choosing a projection state:

- `IrImporter` and `MetadataBodyProjector` acquisition/classification;
- `MemberBodyProducer.AccessorReferencesBackingField` raw-IL proof;
- `ResearchViews.BuildAnnotatedSourceDocument` provenance-offset evidence
  after projection; and
- `ApiCommand.ResolveMethodSource`'s Original Source no-body explanation,
  which is outside the decompiled source-body contract.

Any new reference, or a change to that allow list, fails set equality and
requires an explicit ownership decision. This keeps the gate sensitive to
a new consumer-side `Bodyless` branch without rejecting legitimate raw-IL,
provenance, or Original Source evidence.
