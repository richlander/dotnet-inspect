# Durable classic-async reconstruction

> **Map:** [Decompiler design](../decompiler.md) is the pipeline entry.
> [Raise-work discipline](../decompiler-raise-discipline.md) is the proof
> contract for every raise slice. This document owns the classic
> (`runtime-async=off`) state-machine inverse: the structured machine value,
> honesty rules for declined kickoffs, and the slice plan. Runtime-async
> (`AsyncHelpers.Await`) stays with `AwaitRecoveryPass`.

## Status

Design. Tracking: [#4472](https://github.com/richlander/dotnet-inspect/issues/4472).
Not implemented. r1–r16 were BLOCKED; this revision is the replacement
after integrating `origin/main` `d073009ed`.

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
  ImportObservation       frozen diagnostics at importer return
  Raised                  StageBodyProjection
  Lowered                 StageBodyProjection, materialized on request
  CapturedDecision?       shared replay authority, never a stage outcome

ImportObservation
  DiagnosticsAtImport      detached immutable copy
  HasInternalError        DEC0001 was present when import returned

StageBodyProjection
  Prepared(IrFunctionSnapshot, ClassicAsyncStageState)
  Failed(Diagnostics, ClassicAsyncStageState)

ClassicAsyncStageState
  Unavailable(ImportInternalError)
  NotReached              stage failed before the classic pass
  DecisionFailed          pass ran but produced no valid decision
  Decided(Decision, Outcome)

PreparedStageBody.Render(PrinterOptions)
  RenderedFunction       private clone after print analysis
  DecompilerResult       includes ClassicAsyncOutcome when Decided
  PrintedRanges

PassContext.ClassicAsyncDecision
  None                   pass recognizes and records on the host function
  Supplied(Decision)     pass validates host identity and applies only
  Unavailable(ImportInternalError)
                         pass deliberately produces no classic outcome

NestedFunctionEmbeddingDisposition
  Embeddable
  RetainLowered(AsyncDeclarationCarrierUnavailable | UnsupportedBody)

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
  ClassicAsyncDecision?

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

Preparation imports once. Only a null import for a resolved body-bearing
MethodDef is `ImportFailed`; no `MetadataBodyProjection` exists. Every
non-null `IrFunction` is `Imported`, including `IrImporter.CrashFunction`
and functions with DEC0004, DEC0005, or other diagnostics. The projector
freezes the function's diagnostics and `HasInternalError` at importer
return in `ImportObservation`, before any pass can add diagnostics. The
pristine function retains those diagnostics for consumers that render
it. Stage projections are then materialized lazily through the canonical
pass pipeline with the sibling-import seam. The
`ClassicAsyncReconstructionPass` recognizes once, records its typed
decision on the host function, and applies it. Preparation captures
that decision as shared replay authority and supplies it through
`PassContext` when building any other stage snapshot; the pass validates
the kickoff identity and applies without re-recognizing. The snapshots
are owned mutable IR; consumers print detached root clones, not the
stored instances.

Stage materialization is serialized. While `CapturedDecision` is absent,
the next healthy-import stage runs with `None`. If the classic pass
records a decision, the projector captures it even when a later pass
fails; once captured, every later healthy-import stage receives
`Supplied`. When the frozen import observation has DEC0001, every stage
instead receives `Unavailable(ImportInternalError)`: the classic pass
deliberately leaves the diagnosed crash function unchanged and records
no `NotClassic`, `Reconstructed`, or `Declined`.
In an independent pipeline with directive `None`, DEC0001 already
present when the classic pass begins is the same terminal no-decision
case. The projector supplies the typed directive so stage state uses the
frozen import observation rather than rereading diagnostics after other
passes.

Each stage freezes its own `ClassicAsyncStageState`: `Unavailable` for
that import-health exemption, `NotReached` if it failed before the pass,
`DecisionFailed` if the pass ran but recognition/application or supplied
identity validation did not produce a valid decision, and `Decided` once
the pass produced/applied one. A prepared stage is `Decided`, except
that a stage over a frozen DEC0001 import is `Unavailable`. Every
`Decided` value must equal `CapturedDecision`; an unavailable projection
never has a captured decision. Neither consumers nor `DecompilerResult`
infer an earlier failed stage's outcome from a decision captured by a
different stage.
`PreparedStageBody.Render` is the sole
source-body emission seam: it clones the stored snapshot, performs
the selected style lenses plus print analysis without rerunning the
default/lowered structural pipeline, and returns the rendered clone,
result, and printed ranges as one value.

The pass remains in `IrPasses.Default` and `IrPasses.Lowered`. A
standalone seam-enabled pipeline with no supplied decision recognizes
through that same implementation, records an available outcome on its
function, and produces the same body as prepared output. This keeps
stage dumps, corpus sensors, validity/fidelity harnesses, and render A/B
on the shipped policy without requiring a MethodDef handle. A null-seam
physical pipeline cannot recognize a companion machine and keeps no
classic outcome. A diagnosed DEC0001 crash function also keeps no
classic outcome under the independent path; parity includes preserving
its importer marker rather than manufacturing a classic decision.

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

A supplied or unavailable classic directive is scoped to only the
prepared top-level host. `PassContext.RunForeignFunctionPipeline` is the
sole entry for running passes over any separately imported function. It
always derives a nested context that preserves the sibling-import seam,
type oracle, and shared recursion guard while resetting
`ClassicAsyncDecision` to `None`; it never returns the parent context as
a non-stepping optimization. `CrossMethodPipelineScope.Run`, lambda and
local-function raising, classic companion inspection, and the reducible
and foreach iterator reconstruction helpers all use that entry instead
of calling `IrPasses.Run` with the parent's context.

Imported lambdas, local functions, iterator `MoveNext` bodies, and other
reconstruction companions therefore recognize/decline under their own
identity. Their decision cannot overwrite the outer prepared outcome,
and body embedding is a separate typed disposition below. A source-
architecture gate inventories every product call that runs a pass list
over a foreign function and permits only
`PassContext.RunForeignFunctionPipeline`; direct parent-context
`IrPasses.Run` is zero.

Foreign execution and foreign declaration embedding are separate
decisions. `Lambda` and `LocalFunctionStatement` do not carry an async
declaration modifier or classic stage state. Slices 0 and 1 do not add
that carrier and therefore must not embed a foreign function that needs
it. One Decompiler-owned
`NestedFunctionEmbeddingPolicy.Classify(IrFunction)` returns either
`Embeddable` or `RetainLowered(Reason)`. Both `LambdaRaisingPass` and
`LocalFunctionRaisingPass` consume it after the foreign pipeline:

- `IsRuntimeAsync = Yes`, `IsClassicAsync = Yes`, or
  `RequiresAsyncBodyModifier` is
  `RetainLowered(AsyncDeclarationCarrierUnavailable)`
- an `UnsupportedNode` not already classified async, including an
  importer crash without usable async classification, is
  `RetainLowered(UnsupportedBody)`
- only a body that passes those shared checks plus the existing
  signature, local, and shape checks is `Embeddable`

Classification is the primary carrier signal; the pass-authored flag is
an additional safety signal, not a substitute. A compiler-produced
runtime-async valued local function currently reaches
`LocalFunctionRaisingPass`, which embeds its body without `async` and
reports Full. With an await, the equivalent lambda is already rejected
by its local `RequiresAsyncBodyModifier` check; without an await, both
local function and lambda are embedded invalidly because neither body
sets that flag. Slice 0 routes all four through the shared policy,
making their output honest retained-lowered compiler structure with its
generated-name fidelity signal rather than invalid Full source. This is
an embedding safety correction at the shared seam, not a change to
`AwaitRecoveryPass` or runtime-async reconstruction.
`RuntimeAsyncNestedEmbeddingHonestyTests` owns that observed
Full-invalid to retained-lowered transition.

Classic async local functions/lambdas also witness
`AsyncDeclarationCarrierUnavailable`: classification wins before their
decline marker's `UnsupportedBody` reason, while
`LegacyRaiseEligibility` independently proves they do not reconstruct.
A controlled importer-error seam whose crash function lacks a usable
async classification supplies the `UnsupportedBody` witness; it is not
described as compiler-produced.

`RetainLowered` means the raising pass leaves the compiler-shaped
invocation/delegate and generated method relationship intact; it does
not manufacture a nested declaration or borrow the outer modifier.
The independently imported function may still own its local classic
decision for evidence and direct rendering. A future slice may add a
typed async local/lambda declaration carrier, but it must then define
modifier, outcome, diagnostics, and validity together before changing
this disposition.

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
- `Resolved(..., ImportFailed)` means a resolved body-bearing method
  returned no function; it has no stage, decision, or
  `ClassicAsyncOutcome`
- `Resolved(..., Imported)` proves import succeeded even if its requested
  `StageBodyProjection` is `Failed`. It includes nonfatal diagnostics and
  DEC0001 crash functions; `ImportObservation` preserves their
  import-time classification without deleting the renderable function
- every successfully prepared stage with `IsClassicAsync = Yes` and no
  import-time DEC0001 is `Reconstructed` or `Declined`; `NotClassic` is
  invalid there
- a DEC0001 crash function is
  `Unavailable(ImportInternalError)`, has no classic outcome, and keeps
  its visible importer marker/diagnostic and metadata declaration
  modifier; this diagnosed import-health exemption is not `NotClassic`
- `Unavailable`, `NotReached`, and `DecisionFailed` stages have no
  outcome, regardless of whether another stage later captures one; a
  stage failure after `Decided` retains its own outcome. Render failure
  likewise retains the prepared stage's outcome. Every failure remains
  visible and does not need a marker in nonexistent output
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
  An imported DEC0001 crash function follows the body-bearing path
  instead: Decompiled Source retains its rendered `(importer crash)`
  marker body, diagnostics, successful result, and style-consumption
  latch, while Fidelity Causes retains its existing typed `Failed`. Its
  classic stage state is `Unavailable`, it carries no classic outcome or
  added classic marker, and an async metadata declaration keeps `async`.
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
  same declaration, both with no diagnostic or marker. An imported
  DEC0001 crash function remains body-bearing: `ProduceBody` is
  `Complete` with its marker body, whole-member keeps its existing
  diagnostic-sensitive failure, and whole-type keeps the marker body
  under its existing `failOnDiagnostic: false` policy.
- Metadata-addressed `BodyShapeSearch` uses the front door too. Its
  fidelity/search policy remains separate, but it does not create a
  second classic-async decision.
  `BodyShapeSearch.IncompleteBodyReason` replaces its current
  classic-or-async-iterator attribute union with the exact prepared
  outcome. `Bodyless` preserves the current silent skip: it is not
  inspected, incomplete, matched, or recorded as a search failure.
  `AddressFailed` and `ImportFailed` record failure without incrementing
  `MethodsInspected`. An `Imported` projection whose frozen
  `ImportObservation.HasInternalError` is true also records its DEC0001
  failure without incrementing or materializing a stage; that is Body
  Shape's existing import-health policy, not a global claim that the
  crash function is unrenderable. Every other `Imported` projection,
  including one with nonfatal diagnostics, increments exactly once
  before stage or render disposition, so a later failure records both
  one inspected method and one search failure as it does today.
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
  same outcome. `AddressFailed`, `Bodyless`, `ImportFailed`,
  `Unavailable`, `NotReached`, `DecisionFailed`, null-seam physical
  rendering, and intentionally passless raw-IR rendering have no
  classic outcome.
  `DecompilerResult` takes outcome only from its own stage state, never
  from `MetadataBodyProjection.CapturedDecision`. Its hand-written
  `Equals` and `GetHashCode` include outcome presence, decline reason,
  and body disposition. `with` copies preserve them.

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

| Metadata fact | Resolved/body stage state | Declaration `async` |
| --- | --- | --- |
| Any classification | `Bodyless` | `false`; declaration/skeleton only |
| `RuntimeAsync` | Any body-bearing state or failure | Preserve metadata `true` |
| `IsClassicAsync = Yes` | Body-bearing address/import failure with no stage | Preserve metadata `true`; failure is visible |
| `IsClassicAsync = Yes` | `Unavailable(ImportInternalError)` | Preserve metadata `true`; DEC0001/importer marker is visible |
| `IsClassicAsync = Yes` | failed `NotReached` or `DecisionFailed` | Preserve metadata `true`; failure is visible |
| `IsClassicAsync = Yes` | prepared or failed `Decided(Reconstructed)` | `true` |
| `IsClassicAsync = Yes` | prepared or failed `Decided(Declined)` | `false`; a successful render carries the classic marker |
| `IsClassicAsync = Yes` | `Decided(NotClassic)` | Invalid; fail the gate |
| Async iterator (`StateMachineAsync`, `IsClassicAsync = No`) | Any body-bearing state or failure | Preserve current `false` |
| Other | Any body-bearing state or failure | `false` |

This preserves runtime-async methods whose awaiter recovery declined.
It also keeps async iterators out of the classic contract. Stage-local
state takes precedence over the carrier's success/failure status:
post-classic failure cannot move `Decided(Declined)` back into the
generic preparation-failure row.

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
preparation owns one decision session, shared replay authority, and
stage snapshots with stage-local outcome state. Views own only
annotation and spelling over the
`PreparedStageBody.Render` result.

This applies to direct Research callers and structured source-body
artifacts, not only the four familiar text overlays. Fact Row C#
anchors must refer to the same function whose lines the sibling code
artifact prints. It does not apply to physical-body evidence whose
identity contract forbids companion-body import.

Preparation does not obtain invariance by running `PrintRaised` and
`PrintLowered` independently and comparing their answers. The first
stage that reaches the classic pass recognizes one
`ClassicAsyncMachine` / decline decision; later stage pipelines receive
that decision but own an outcome only after they reach the same pass and
apply it. `Reconstructed` installs owned body/local state and merges the
captured companion type-fact contribution; `Declined` applies the
decided replacement/preservation edit and diagnostic. `Decided` stage
pipelines may still differ in cosmetic sugar, but cannot differ on
classic identity, outcome, consumed regions, or pass-owned state. A
stage that failed earlier retains `NotReached`; it does not borrow that
invariance claim.

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

`Outcome = Reconstructed` in slice 0 means **the current kickoff
eligibility, builder gate, and a current `TryBuild*` all succeeded**.
Structured classification may widen the population that receives an
honest decline, but it does not make async local/lambda state-machine
names, Debug classes, async void, or another previously ineligible
kickoff newly reconstructable. `LegacyRaiseEligibility` records that
boundary separately from recognition. Every method with
`IsClassicAsync = Yes` that prepared successfully and did not satisfy
it is `Declined`.

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

1. **Declaration `async` follows classification plus stage-local
   classic state.** MemberCodeProvider declarations, public
   typed-body production, whole-member composition, and whole-type
   listings use the table in
   [Where `async` is actually stamped](#where-async-is-actually-stamped).
   Runtime-async keeps its metadata `async`, including when recovery
   declines. A classic `Decided(Declined)` body loses `async` even if a
   later pass failed; its outcome is already stage-local. An unavailable
   DEC0001 import has no classic outcome and preserves metadata `async`
   with its existing importer marker. `NotClassic` is impossible only
   when `IsClassicAsync = Yes`; it is required for async iterators.
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
6. **One hop and fresh foreign-function context.** An async
   local-function `MoveNext` maps to that
   local function's stub, not the owning method. A prepared outer
   decision is never inherited by any foreign-function pipeline: the
   central execution entry always derives a context and resets the
   directive to `None`, including when stepping is disabled. Local
   functions, lambdas, and reducible/foreach iterator `MoveNext`
   pipelines each get their own identity and decision. Slices 0 and 1
   retain compiler-shaped calls/delegates instead of embedding a body
   whose foreign result requires `async` or contains an unsupported
   marker. `NestedFunctionEmbeddingPolicy` is one shared gate for lambda
   and local-function raising; an async declaration carrier is a future
   design prerequisite, not an inferred printer bit. Await-bearing and
   await-free runtime-async valued local/lambda fixtures are
   non-vacuous carrier witnesses. Classic nested fixtures prove the
   classification carrier and no-new-raise behavior; importer-crash
   seams separately prove unsupported disposition.
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

A concrete observation that would falsify slice 0: any healthy-import
successfully prepared `IsClassicAsync` body is neither reconstructed
nor visibly marked; a non-narrow body's original statement disappears; a
declined classic declaration still says `async`; a declined
runtime-async method loses its metadata `async`; any declared
source-body artifact disagrees on outcome; a Fact Row anchor refers to
an independently raised body; a failed preparation becomes a plausible
body; a resolvable name-addressed member bypasses preparation; replay
loses companion type facts or aliases mutable snapshot state; an outer
decision reaches a nested function; a bodyless member loses its existing
visible absence signal, gains C# body output, a marker or modifier, or
becomes an inspected Body Shape; a null import counts as inspected; an
imported DEC0001 crash function is erased from a render surface,
counted as inspected by Body Shape, assigned a classic outcome/marker,
or loses metadata `async`; a nonfatal import diagnostic falls outside
the union; a stage inherits an outcome captured only by another stage;
a post-classic failure changes the modifier selected by its retained
outcome; a post-import non-DEC0001 stage failure does not count as
inspected; a parent directive reaches a reducible or foreach iterator
foreign-function pipeline; lambda and local-function raising use
different embedding rules; a foreign body that requires `async` is
embedded in a declaration with no async carrier; an unsupported foreign
body is embedded instead of retained lowered; the runtime-async valued
await-bearing local remains invalid or an await-free local/lambda loses
`async` and remains Full-invalid; the physical C# diff imports a
companion body; a stage,
corpus, or harness render differs from prepared Raised output for the
same classic fixture; an in-domain library `MoveNext` still lacks
distinctive user logic; or an async-iterator `MoveNext` is no longer
hollow.

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
| 0. Honesty | Add SRM `IsClassicAsync`, structured exact-or-selector addressing with resolved `Bodyless`, Decompiler-owned `MetadataBodyProjectionResult`, detached function snapshots, complete classic application values, and typed body carriers. `ClassicAsyncReconstructionPass` remains the single decision implementation: the projector captures/replays one decision across top-level stages, import-internal-error crash functions remain outcome-unavailable, and one foreign-function pipeline entry always resets the parent directive. A shared nested-function embedding policy retains any async-classified body, body requiring an async declaration modifier, or unsupported body as lowered compiler structure; this also corrects the current Full-invalid await-bearing local and await-free local/lambda runtime-async embeddings at that shared seam. Seam-enabled stage/corpus/harness runs use the same pass. Every declared source-body path canonicalizes through the front door; seam-free physical evidence remains separate. Every healthy classic decline gets a marker: replace exact narrow handoff; prepend while preserving a non-narrow body. Correlate Debug class allocation and void `Return(null)`. Leave legacy raise eligibility unchanged. Stop hollowing in-domain `MoveNext`; library + corpus A/B. | #4472 fixture still declined, but honest. Debug class SMs are honest but not raised. Async-iterator `MoveNext` still hollow. Custom builders visibly decline with preserved bodies. Bodyless members remain absent/declaration-only. Importer crash markers and metadata modifiers remain unchanged. Async local/lambda declarations are not reconstructed until they have a typed async carrier. Runtime-async recovery is unchanged; only unsafe nested embedding is retained lowered. Physical C# diff remains MethodDef-scoped and seam-free. No trusted Metadata/Analysis lift. |
| 1. Void-await then statements then return | Accept `await Task.Yield(); return ReadValue(value);` as the first inverse raise from `AwaitPoints` + `UserRegions`, not as a new `TryBuild*` and not as a `HasUnexpectedStore` allow-list tweak. Must consume void `GetResult` as a statement, following statements, a non-await `SetResult` operand, the Yield operand temp, and an explicit `LoadLocalAddress` decline-then-remap. Hoisted parameter binding is already present. The smaller `await Task.Yield();` (no later statements) is the accepted boundary of the same slice. Blocked until the Correct measurement exists. | General multi-state dispatch, class SM, custom awaiters, structural Metadata descriptor, census-defined raises. |

### Nested embedding fixture family (slice 0)

- Compiler-produced Release runtime-async valued local function and
  lambda, each with await-bearing and await-free bodies, at Raised and
  Lowered. All carry `IsRuntimeAsync = Yes` and must return
  `RetainLowered(AsyncDeclarationCarrierUnavailable)`. The
  await-bearing pair separates today's invalid local from its safe
  lambda sibling; both await-free forms are current Full-invalid
  witnesses and prove classification does not depend on recovered
  await.
- Controlled modifier-only foreign-function seams for local function
  and lambda, at both stages. With neither async classification at Yes,
  no UnsupportedNode, and `RequiresAsyncBodyModifier = true`, they
  return `RetainLowered(AsyncDeclarationCarrierUnavailable)`. These
  prove the defensive pass-authored fallback and are not
  compiler-produced claims.
- Compiler-produced Release classic async local function and lambda,
  at both stages. `LegacyRaiseEligibility` keeps them out of
  Reconstructed. `IsClassicAsync = Yes` produces
  `RetainLowered(AsyncDeclarationCarrierUnavailable)` before their
  marker could produce UnsupportedBody.
- Controlled non-null DEC0001 importer-error seams for local function
  and lambda, with neither async classification at Yes. The importer
  crash marker produces
  `RetainLowered(UnsupportedBody)`; these are deliberate seam fixtures,
  not compiler-produced claims.
- Compiler-produced synchronous local-function and lambda positives
  carry no async classification or modifier requirement and remain
  `Embeddable`, so the safety policy cannot become a deny list over all
  nested functions.

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
- Keep the complete slice-0 nested embedding fixture family unchanged.
- Slice-0 honesty witnesses (not slice-1 raises): `NoAwait`,
  `Async_VoidBuilder`.
- Pinned real witness: the #4472 `member` render, Before/After from
  `dotnet-inspect`, structural review per the decompiler PR template.

## Non-goals

- Runtime-async reconstruction (`AwaitRecoveryPass`); slice 0 changes
  only the shared nested embedding disposition after that pass.
- Iterator / async-iterator reconstruction. Async-iterator `MoveNext`
  stays hollow.
- Depending on #4461's `DirectCall.Caller` rewrite.
- Chaining an async local-function `MoveNext` to the owning method.
- Moving Analysis `MemberRef` / `MemberResolver` / `FrameworkIdentity`
  / attribution filters into Metadata.
- A product-surface listing filter for nested SM types.
- An async-capable `LocalFunctionStatement` / `Lambda` declaration
  carrier or accepted nested classic-async raise.
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
| Slice-0 accepted-raise boundary | Decompiler `LegacyRaiseEligibility`, separate from broader recognition |
| Classic-async metadata fact | Metadata import → `MethodBody` / `IrFunction` |
| Exact-or-selector address canonicalization and bodyless/imported/failure union | Decompiler `MetadataBodyProjector` over existing `MetadataMethodAddress` / resolver |
| Frozen import diagnostics / DEC0001 observation | Decompiler `MetadataBodyProjector` at importer return |
| Complete root snapshot clone | Decompiler `IrFunctionSnapshot` |
| Shared cross-stage replay authority | Decompiler `MetadataBodyProjection` + `PassContext`, scoped to one top-level prepared host |
| Stage-local decision/outcome/unavailable state | Decompiler `StageBodyProjection` |
| Foreign-function execution and classic-directive reset | Decompiler `PassContext.RunForeignFunctionPipeline` |
| Nested lambda/local-function embedding disposition | Decompiler `NestedFunctionEmbeddingPolicy`, consumed by both raising passes |
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
| Exact async population matrix | Metadata + Decompiler tests | Async iterator is rejected as invalid `NotClassic`; custom classic builder escapes visible `Declined`; or a DEC0001 crash function is forced into `NotClassic`, `Reconstructed`, or `Declined` instead of `Unavailable` |
| Canonical front-door architecture | Source-architecture test `MetadataAddressedBodyProjectionUsesCanonicalFrontDoor` | Product-consumer references outside `CSharpPrinter` / pass definitions to any body-emitting printer API or direct top-level `IrPasses.Run*` differ from the complete set: `MetadataBodyProjector` / `PreparedStageBody`, seam-free `CSharpBodyDiff`, and canonical-stage `PipelineStages` |
| Body-status policy ownership | Source-architecture test `DeclaredSourceBodyStatusUsesProjector` | A declared-source consumer uses RVA/`HasBody`/null import to choose `Bodyless` versus import/preparation/render or to map that state to a carrier; the exact retained body-reference manifest differs, or any named migration site remains |
| Exact/selector address parity | Decompiler projector + existing `ClassicAsyncWithoutAwait_UsesResolvedMethodBodyModifier` valid/stale-token rows + bodyless overload ordinal | A resolvable selector bypasses the projector, resolves a different MethodDef, drops a bodyless method from ordinal counting, or differs from exact-address classification/state/outcome/render; unresolved selection becomes plausible output |
| Resolved bodyless lifecycle | Decompiler + `RenderStyleConfigTests.NoBodyMethod_ProducesResultWithoutOutput_SoNoStyledSource` + `FidelityCauseSectionTests.BuildInspection_DistinguishesNoBodyFromImporterFailure` + `AnnotatedSourceDocumentProjectionTests.BodylessMemberDocumentFailureKeepsSiblingProjection` + whole-member/type + Body Shape | Exact/selector bodyless results differ; typed body is not `Absent` with its existing absence diagnostic; CLI/Research loses its existing visible absence failure, emits C# body output, marks style consumed, or changes Fidelity Causes from `Absent`; a marker or modifier appears; whole-member is not `Complete` declaration-only text; whole-type is not diagnostic-free declaration-only; Body Shape counts it as inspected/incomplete/failure |
| Import observation totality | Decompiler projector tests with clean, nonfatal DEC0004/DEC0005, DEC0001 crash-function, and null-import seams | A non-null function is not `Imported`; import-time diagnostics change after a pass; `HasInternalError` is inferred from later diagnostics; or a null import has a stage |
| Importer-crash cross-surface preservation | Existing `CommandExecutionTests.Member_SelectedOverload_SelectFidelityCauses_ImporterCrashIsFailed` plus CLI Decompiled Source/style latch, public typed body, whole-member/type, and Body Shape rows over one malformed classic-classified fixture | The crash marker/DEC0001 disappears; CLI output ceases to be a successful body render or style consumption changes; Fidelity Causes ceases to be `Failed`; `ProduceBody` ceases to be `Complete`; whole-member ceases to fail; whole-type loses the marker body; Body Shape increments `MethodsInspected`; the stage is not `Unavailable`; a classic outcome/marker is added; or metadata `async` is omitted |
| Complete classic application replay | Decompiler pass/projector tests with accepted interface-fact + declined diagnostic fixtures | A second prepared stage recognizes again; supplied replay differs in body, local state, type facts, diagnostics, provenance/fidelity, modifier state, or outcome; a supplied decision applies to a different kickoff |
| Stage-local classic state | Decompiler projector tests with a frozen DEC0001 import and injected pre-classic, in-classic, and post-classic failures in both Raised-first and Lowered-first order | A healthy `Prepared` stage lacks `Decided`; a DEC0001 `Prepared` stage is not `Unavailable` or acquires a captured decision; a pre-pass failure acquires another stage's later outcome; recognition/application or supplied-identity failure is not `DecisionFailed`; a post-pass failure loses its own decision/outcome; or two `Decided` stages disagree with shared replay authority |
| Snapshot clone isolation | Decompiler snapshot/render tests with diagnostic-producing fixture | Mutating one render clone changes a prepared snapshot, another stage/render, diagnostics, local state, type facts, or outcome |
| Foreign-function decision scope | Decompiler pass/projector tests with compiler-produced classic async local function/lambda plus reducible and foreach iterator paths, each at Raised and Lowered stages | An outer supplied/unavailable directive reaches a foreign-function pipeline; non-stepping execution returns the parent context; nested identity validation fails against the outer kickoff; a nested function fails to make its own decision; or nested state is attributed to the outer decision |
| Foreign-function pipeline architecture | Source-architecture test over product pass-run sites | Any pass list runs over a separately imported function outside `PassContext.RunForeignFunctionPipeline`, or a direct foreign-function `IrPasses.Run` site appears |
| Runtime-async nested embedding honesty | `RuntimeAsyncNestedEmbeddingHonestyTests` with compiler-produced Release await-bearing and await-free valued local-function/lambda fixtures at Raised and Lowered, plus existing `LambdaRaisingPassTests.AsyncVoidLambda_StaysLoweredWithoutAsyncLambdaSupport` | Any `IsRuntimeAsync = Yes` body becomes a non-async nested declaration; an await-free row is treated as a synchronous positive because it lacks `RequiresAsyncBodyModifier`; retained output remains Full instead of carrying its generated-name fidelity signal; either pass fails to return `RetainLowered(AsyncDeclarationCarrierUnavailable)`; or compiler-shaped invocation/delegate identity is lost |
| Classic nested embedding and raise boundary | Decompiler product tests with compiler-produced Release classic async local-function/lambda fixtures at Raised and Lowered | A nested classic fixture newly reconstructs; `IsClassicAsync = Yes` does not return `RetainLowered(AsyncDeclarationCarrierUnavailable)`; the marker reason wins before async classification; or exact legacy-raise set equality changes |
| Nested modifier-fallback boundary | Controlled local-function/lambda foreign-function seams at Raised and Lowered with both async classifications not Yes, `RequiresAsyncBodyModifier = true`, and no unsupported node | The modifier-only seam becomes Embeddable; it does not return `RetainLowered(AsyncDeclarationCarrierUnavailable)`; either raising pass bypasses the fallback; or the seam is mislabeled compiler-produced |
| Nested importer-crash embedding boundary | Controlled non-null DEC0001 local-function/lambda import seams at Raised and Lowered | A crash function becomes a nested declaration; it does not return `RetainLowered(UnsupportedBody)` when async classification is unavailable; or the seam is mislabeled compiler-produced |
| Nested embedding architecture | Source-architecture test `NestedFunctionEmbeddingUsesSharedPolicy` | A product `Lambda` or `LocalFunctionStatement` construction from an imported function bypasses `NestedFunctionEmbeddingPolicy`, or the two raising passes own separate async/unsupported checks |
| Prepared/canonical pipeline parity | `PipelineStageTests.DumpMethod_FinalCSharp_IsTheShippedProductOutput` with compiler-produced reconstructed + declined fixtures and a malformed importer-crash fixture | Terminal seam-enabled stage C# or outcome presence/value differs from prepared Raised output |
| Corpus/harness classic policy | Decompiler harness contract tests + `CorpusSensor` classic profile | Handle-free sweep or fidelity/validity/render A/B bypasses the pass, misses a decline marker, or loses an accepted reconstruction |
| Declined classic body, narrow and non-narrow | Five CLI code views + public typed body + whole-type | Render lacks the unsupported marker comment |
| Same declined classic body | CLI Fidelity Causes | Lacks DEC0004 |
| Non-narrow classic body with extra call/store | Five CLI code views + public typed body + whole-type | Any original statement disappears |
| Declaration modifier by stage-local classic state | CLI declarations + public typed body + whole-member/whole-type with prepared and post-classic-failed `Reconstructed`/`Declined`, pre-classic failure, and unavailable importer-crash rows | `Decided(Declined)` still says `async`; `Decided(Reconstructed)` omits it; post-classic carrier failure changes either decision's modifier; `Unavailable` or pre-classic failure loses metadata `async`; `IsClassicAsync` yields `NotClassic`; or an async iterator is treated as classic |
| Canonical outcome across member artifacts | CLI + direct `ResearchViews.ProjectMember` + Research queries | Views disagree, Source Document reimports, or an overlay without an import seam recomputes the machine |
| Raised/Lowered Research contract | Annotated Source + Annotated Source Document | Stage snapshots recompute the outcome or unsupported stage preparation falls back to raw unmarked kickoff |
| Fact Row source-line identity | Direct Research + CLI Facts | C# lines are mapped against a separately raised function |
| Address/bodyless/import/stage/render union | Decompiler + CLI + Research + whole-type + Body Shape accounting | `AddressFailed`, `Bodyless`, null `ImportFailed`, imported diagnostics, `Unavailable`, and stage-local failure states collapse; null import counts as inspected; DEC0001 import counts as inspected; nonfatal import does not; an unavailable or pre-decision failure has a success-shaped outcome; or post-decision stage/render failure drops its own captured outcome |
| Declined runtime-async fixture | CLI declarations + public typed body + whole-member/whole-type | Loses metadata `async` |
| Debug class narrow handoff | Decompiler library | Correlated `StoreLocal(NewObject(SM::.ctor))` prevents recognition |
| Async-void narrow handoff | Decompiler library | Terminal `Return(null)` prevents recognition |
| Non-generic ValueTask / async-void legacy raise negative | Decompiler library | Slice 0 newly reconstructs it |
| Exact legacy raise population | Decompiler pass tests derived from the current accepted fixture set, with close Debug-class, async-void, async local/lambda, custom-builder, and lookalike negatives | Structured recognition changes which fixtures reconstruct in slice 0, or a new `TryBuild*`/eligibility path escapes set equality |
| Typed body carriers | `MemberBodyProducer` tests | Outcome/classification is lost in `ProduceBody` or between `DecompileBody` and declaration formatting, or `Bodyless` becomes `ImportFailed` or a failed stage |
| Body Shape source projection | Decompiler product tests with bodyless, null import, imported DEC0001, imported nonfatal diagnostic, and post-import stage/render-failure fixtures | Search creates a second classic decision, retains the broad classic-or-async-iterator heuristic, records `Bodyless` as inspected/incomplete/failure, counts null import or imported DEC0001 as inspected, skips an imported nonfatal diagnostic, or fails to count a later stage/render failure as inspected |
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

Deleting the shared nested embedding check from either raising pass
must fail both the compiled validity fixture and
`NestedFunctionEmbeddingUsesSharedPolicy`. A synthetic
`LocalFunctionStatement` with an await expression is not sufficient:
`RuntimeAsyncNestedEmbeddingHonestyTests` must prove the real
compiler-produced await-bearing and await-free runtime-async
local/lambda dispositions at both stages. Deleting the classic
classification branch must independently fail the classic no-new-raise
rows; deleting the modifier fallback must fail its controlled seams;
deleting the unsupported branch must fail the controlled importer-crash
rows.

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
top-level directive in any foreign-function pipeline must fail the
local/lambda and iterator fixtures at both stages. Bypassing
`RunForeignFunctionPipeline` must independently fail the architecture
inventory. Bypassing `NestedFunctionEmbeddingPolicy` or splitting its
async/unsupported checks between lambda and local-function raising must
fail the separate embedding architecture inventory. Removing selector
canonicalization must fail the stale-token row, not silently retain a
direct printer branch.
Collapsing `Bodyless` into `ImportFailed`, a failed stage, `NotClassic`,
or a consumer-side RVA precheck must fail exact/selector bodyless parity
and all three surface dispositions. Adding a consumer-side body-status
branch must also fail `DeclaredSourceBodyStatusUsesProjector`, even when
its current render happens to match.

`DeclaredSourceBodyStatusUsesProjector` is an exact reviewed source
manifest over `ILInspector.Decompiler`, `ILInspector.Research`, and
`dotnet-inspect`, following the existing
`DynamicCompilationSiteInventoryTests` file/count/reason pattern. It
counts every access to MethodDef RVA, modeled `HasBody`, and the named
body-status helpers; it excludes data-member declarations and field RVA.
The implementation pins each occurrence by file and containing member,
not only a project-wide total.

These current policy sites must disappear:

| Migration site | Current occurrences | Replacement |
| --- | ---: | --- |
| `MemberCodeProvider.Collect` | 1 | Projector state and import observation |
| `MemberBodyProducer.ProduceBody` | 1 | Projector state |
| `MemberBodyProducer.ComposeEvent` | 2 | Per-accessor projector state |
| `BodyShapeSearch.SearchCore` | 1 | Projector state and import observation |

`ResearchViews` null-import handling also moves to the projector, but it
has no direct body-status token for this manifest; the canonical
front-door architecture gate covers that direct-import bypass.

The post-migration retained manifest is exhaustive:

| Retained component | Occurrences | Reason |
| --- | ---: | --- |
| `MetadataBodyProjector` | 1 new | Sole declared-source `Bodyless` classification |
| `IrImporter` | 6 | Low-level selection, import, and metadata inventory |
| `MethodImporter` | 2 | Low-level selector and body acquisition |
| `IlProjection` | 2 | Explicit physical IL projection |
| `ConstructorConfinementFacts` | 2 | Raw constructor-body proof |
| `CSharpBodyDiff` | 6 | Explicit seam-free physical body inventory/decode |
| `ResearchDiff.MethodBodyLookup.TryDecode` | 2 | Physical IL diff decode |
| `ImplementationDiff` | 3 | Two-sided physical-body comparability |
| `AppContextSwitchProjectionProducer` | 1 | Analysis over `MethodBodySource` bodies |
| `MemberBodyProducer.AccessorReferencesBackingField` | 2 | Raw-IL backing-field proof |
| `ResearchViews.BuildAnnotatedSourceDocument` | 2 | Post-projection provenance offsets |
| `ApiCommand.ResolveMethodSourceAsync` | 1 | Original Source no-body explanation |

The expected post-migration total is 30 pinned occurrences. Any new,
missing, or moved occurrence fails set equality and requires an explicit
ownership decision and reason. The gate therefore catches a new
consumer-side `Bodyless` branch anywhere in those product projects
without rejecting retained physical IL, analysis, provenance, or
Original Source evidence.
