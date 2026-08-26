# Durable classic-async reconstruction

> **Map:** [Decompiler design](../decompiler.md) is the pipeline entry.
> [Raise-work discipline](../decompiler-raise-discipline.md) is the proof
> contract for every raise slice. This document owns the classic
> (`runtime-async=off`) state-machine inverse: the structured machine value,
> honesty rules for declined kickoffs, and the slice plan. Runtime-async
> (`AsyncHelpers.Await`) stays with `AwaitRecoveryPass`.

## Status

Design. Tracking: [#4472](https://github.com/richlander/dotnet-inspect/issues/4472).
This is a standalone design that targets `main`.
[#4684](https://github.com/richlander/dotnet-inspect/issues/4684)
decouples classic-async reconstruction from the structured Implementation Diff
design in [#4560](https://github.com/richlander/dotnet-inspect/pull/4560) and
owns the resulting ownership and parallelization plan. This work is related to
but no longer stacked on #4560.
Implementing slice 0 depends on the shared Metadata state-machine relationship
substrate tracked by
[#4669](https://github.com/richlander/dotnet-inspect/issues/4669), not on
Implementation Diff. This document owns only classic-async classification,
reconstruction, projection, and honesty. Implementation Diff is an independent
downstream integration; a separately owned correctness measurement may
optionally integrate with it. Its participant, correspondence, population,
work-item, budget, query-lifetime, completion, and result currencies never
enter ordinary reconstruction.

Reconstruction and Implementation Diff share lower physical primitives rather
than a dependency edge: `MetadataMethodAddress` and physical MethodDef import
facts. Declared-source reconstruction alone enters `MetadataBodyProjector`,
consumes the Metadata state-machine relationship index, and produces Decompiler
stage results. Implementation Diff independently selects exact addresses and
enters the seam-free physical `CSharpBodyDiff` path; it does not traverse the
projector or consume reconstructed stage results.

Not implemented. `ClassicAsyncReconstructionPass` remains the current
fixture-shaped raise.

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
opt in). Restoring physical support bodies is therefore a
library-and-corpus change, not a default CLI `member` change.

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
MetadataBodyProjector.Prepare(MetadataSource, MetadataBodyRequest)

MetadataBodyRequest
  Exact(MetadataMethodAddress)
  Carried(MetadataBodyTarget)
  Selector(TypeFullName, MetadataBodySelector, Visibility)

MetadataBodyTarget
  Version                target schema version
  StrictKey              MetadataBodyKey
  RelationshipRole       Method | Getter | Setter | Adder | Remover
  PreferredAddress?      same-source MetadataMethodAddress hint
  PresentationAnchor?    label only; never resolution evidence

MetadataBodyAddressResult
  Resolved(MetadataMethodAddress)
  AddressFailed(MetadataBodyAddressFailure)

MetadataBodyAddressFailure
  Unavailable(UnsupportedTargetVersion)
  Rejected(CrossModuleHint | RelationshipRoleMismatch)
  Ambiguous(CandidateAddresses)
  Failed(Diagnostics)

MetadataBodySelector
  Name                   metadata MethodDef name
  ZeroBasedOrdinal       metadata-order position after name/visibility filtering

MetadataBodyProjectionResult
  AddressFailed(MetadataBodyAddressFailure)
  Resolved(MetadataBodyResolvedProjection)

MetadataBodyResolvedProjection
  Address                module-scoped MethodDef identity
  ClassificationFailed(Diagnostics)
  Classified(AsyncClassification, BodyProjection)

AsyncClassification      RuntimeAsync | ClassicAsync | AsyncIterator | Other

AsyncClassificationEvidence
  RuntimeAsyncFlag
  AsyncStateMachineAttribute
  AsyncIteratorStateMachineAttribute

BodyProjection
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
  Prepared(Stage, IrFunctionSnapshot, ClassicAsyncStageState)
  Failed(Diagnostics, ClassicAsyncStageState)

PreparationStage         Raised | Lowered

ClassicAsyncStageState
  Unavailable(ImportInternalError)
  NotReached              stage failed before the classic pass
  DecisionFailed          pass ran but produced no valid decision
  Decided(Decision, Outcome)

PreparedStageBody.Render(PrinterOptions)
  Stage                  retained from preparation; caller cannot override
  StylePolicy            catalog-derived Raised | Lowered
  RenderedFunction       private clone after print analysis
  DecompilerResult       includes ClassicAsyncOutcome when Decided
  PrintedRanges

PassContext.ClassicAsyncDecision
  None                   pass recognizes and records on the host function
  Supplied(Decision)     pass validates host identity and applies only
  Unavailable(ImportInternalError)
                         pass deliberately produces no classic outcome

ForeignFunctionPipelineResult
  Prepared(IrFunction)
  ClassificationFailed(MetadataMethodAddress, Diagnostics)
  Bodyless(MetadataMethodAddress)
  ImportFailed(MetadataMethodAddress, Diagnostics)
  RecursionDeclined(MetadataMethodAddress)

NestedFunctionEmbeddingDisposition
  Embeddable
  RetainLowered(AsyncDeclarationCarrierUnavailable | UnsupportedBody)

ClassicAsyncDecision
  HostIdentity           module-scoped MethodDef identity
  Outcome
  Machine?               detached recognition/consumption value
  Application            exhaustive detached host-mutation value

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
  NotClassic(SupportMethodAcknowledgment?)
  Reconstructed
  Declined(Reason, BodyDisposition)

ClassicAsyncSupportIdentity
  HostAddress            module-scoped MethodDef identity
  MethodKind             MoveNext | SetStateMachine
  BuilderKind            known builder identity
  InterfaceMapping       complete signature + MethodImpl/runtime mapping

SupportMethodAcknowledgment
  MethodKind             MoveNext | SetStateMachine
  BuilderKind            known builder identity
  BodyDisposition        PreservedPhysical

BodyDisposition
  ReplacedNarrowHandoff
  PreservedOriginal
```

Every exact metadata import carries the complete disjoint
`AsyncClassification` on `MethodBody` / `IrFunction`, including imports
performed for lambdas, local functions, iterator bodies, and classic
companions. `IsRuntimeAsync` and the new `IsClassicAsync` remain derived
compatibility facts; neither is the transport for async-iterator identity.
All direct and foreign import entries resolve an exact
`MetadataMethodAddress` and use the same guarded Metadata classifier before
body import, not the existing collapsed `StateMachineAsync` inventory plus a
second attribute query. The projector reads runtime-async,
`AsyncStateMachineAttribute`, and
`AsyncIteratorStateMachineAttribute` evidence independently in one
guarded metadata scan. Exactly one positive category maps to
`RuntimeAsync`, `ClassicAsync`, or `AsyncIterator`; no positive category
maps to `Other`. Multiple positive categories are contradictory metadata
and produce terminal `ClassificationFailed(DEC0015)` before body status
or import. This includes classic plus iterator attributes and a runtime
async flag combined with either state-machine attribute. The existing
collapsed `StateMachineAsync` value may remain for compatibility
inventories, but it is not projector classification authority.

Top-level projector classification failure remains
`Resolved(Address, ClassificationFailed)`. A foreign import cannot manufacture
an `IrFunction` with missing or guessed classification: it returns the typed
`ForeignFunctionPipelineResult.ClassificationFailed`, and its caller preserves
the compiler-shaped outer relationship. `Bodyless`, null import, and recursion
decline are distinct results too. Only deliberately constructed synthetic or
passless functions with no metadata provenance may carry explicitly
unavailable classification; a metadata-backed corpus sweep classifies while it
still owns each handle. Only the controlled no-provenance paths may reach the
`UnsupportedBody` fallback without a metadata classification.
`RunForeignFunctionPipeline` is the sole pass-running entry and propagates
these typed acquisition results as well as resetting the parent classic
directive.

Exact, carried, and selector resolution are prerequisites to async projection.
`MetadataBodyProjector` resolves its `MetadataBodyRequest` once through the
canonical Metadata owner and consumes only a successfully validated
`MetadataMethodAddress`; [member target resolution](member-target-resolution.md)
owns the user-facing selector boundary.

`MetadataBodyTarget` is the single-source carried-target currency owned by
Metadata. `MetadataBodyKey` retains declaring type, method kind/name, calling
convention, generic arity and constraints, parameter and return shapes, by-ref
shape, function pointers, custom modifiers, and exact named type assembly
scope. `RelationshipRole` exists only on the target envelope. Carried
resolution revalidates a preferred address against a reopened reader only when
its MVID, MethodDef row, strict key, and relationship role all agree; a
same-MVID reopened reader is supported. Otherwise it resolves by
current-version strict key plus the envelope role in that one
`MetadataSource`. A role is valid only when the resolved MethodDef occupies
that exact method/getter/setter/adder/remover relationship. An unknown key
version is `Unavailable`; a cross-MVID hint or role mismatch is `Rejected`;
duplicate keys are `Ambiguous` with every candidate address; malformed or
budget-exhausted metadata is `Failed` with diagnostics. No outcome falls back
to name, ordinal, presentation anchor, or raw token equality.
`MetadataBodyProjector` preserves the complete non-`Resolved` value in
`AddressFailed` and stops before async classification.

The existing Metadata `BodyTarget` and `ResolvedMemberTarget.Body` remain
selection-only compatibility values during migration; they are not accepted by
`MetadataBodyProjector`. Before a selected API member leaves its live Metadata
session, a Metadata factory validates its exact MethodDef and constructs
`MetadataBodyTarget`; `ResolvedMemberTarget` gains that value alongside the
legacy property until all non-body consumers migrate. Every declared-source
body consumer requires the new target or begins with an explicit fresh
`Selector` request. In particular, `MemberCodeProvider` and whole-member/type
composition may no longer turn `BodyTarget.MetadataToken` or
`DeclaringOverloadIndex` into a body request. This contract supersedes the
normalized-signature cross-reader fallback in
[member-body-substrate.md](member-body-substrate.md#address-identity-not-an-ordinal)
for declared-source projection; that document continues to describe current
behavior until slice 0 performs the migration.

Ordinary async projection does not realize comparison endpoints, mint
participants or work items, perform cross-version correspondence, or consume
an Implementation Diff operation or result. A downstream comparison may
independently select exact per-side addresses and pass each physical MethodDef
to `CSharpBodyDiff`; that integration does not become an input to
`MetadataBodyProjector` or `ClassicAsyncReconstructionPass`.

State-machine relationship discovery is a second, same-reader prerequisite.
[#4669](https://github.com/richlander/dotnet-inspect/issues/4669) owns a
bounded, immutable `StateMachineRelationshipIndex` in `ILInspector.Metadata`.
For one `MetadataReader`, it answers exact kickoff-to-execution and
execution-to-kickoff queries with module-scoped method/type identities, claim
kind, exact interface implementation roles, and typed absent, unresolved,
malformed, duplicate, cross-kind, and ambiguous outcomes. The owning
`MetadataSource` supplies the reader in which the index runs; hosts do not own
state-machine semantics. The async layer consumes the index result only after
exact body addressing and does not rescan attributes, interfaces, signatures,
or `MethodImpl` rows to reconstruct the relationship.

After resolution, the projector performs metadata async classification
once, before inspecting or importing the body. Contradictory positive
runtime/classic/iterator evidence, or the expected
`BadImageFormatException` from malformed custom-attribute
constructor/type metadata, becomes
`Resolved(Address, ClassificationFailed(Diagnostics))`; the projector
does not broadly catch unexpected failures. The failure carries the new
stable `DEC0015` (`MetadataClassificationFailed`) diagnostic with the
resolved address and conflict/decode detail. This terminal state retains the
resolved address but has no `AsyncClassification`, body projection,
import, stage, decision, modifier, or render. Its diagnostics are
detached immutable values. Exact, carried-key, and selector requests that
resolve to the same MethodDef produce the same classification result
and diagnostic.

After successful classification, the projector reads the MethodDef body
status once.
This precedence is absolute: address resolution, async classification, then
body status. An RVA-zero or abstract method with malformed or contradictory
async metadata is `ClassificationFailed(DEC0015)`, never `Bodyless`.
An abstract, extern, interface, or other RVA-zero method is
`Resolved(..., Classified(..., Bodyless))`: address and metadata
classification remain available, but there is no import, stage, outcome,
marker, or render failure. A carrier may preserve its existing typed
absence diagnostic; that does not turn the projector state into
`ImportFailed` or a failed stage. Selector ordinals continue to count
bodyless MethodDefs exactly as the existing physical importer does. A declared-source
consumer does not use RVA, `HasBody`, a classification exception, or a
null import to choose among classification failure, bodyless, and
body-bearing projection or to map that choice to its carrier before
calling the projector.

`ApiMember.IsAbstract` remains a declaration fact, not body-status
authority. Any selected method or accessor is resolved and classified
through the projector before a composer or section filter chooses
declaration-only presentation. This includes abstract methods and
explicit-interface methods, property/indexer getters and setters, and
event adders and removers. `ApiOutputFormatter.ResolveBodyMethods` does
not filter abstract candidates before projection. `ComposeMembers`,
`ComposeProperty`, and `ComposeEvent` do not use `IsAbstract`, RVA, or a
null import to skip projection.

For CLI member sections, this intentionally replaces the current
abstract-method prefilter: a requested body-backed section receives the
section's typed `Bodyless` mapping instead of becoming N/A before the
projector runs. Non-body metadata sections may still use `IsAbstract`
as a declaration/filter fact because they make no source-body claim.

Whole-type composition collects one typed projection for each existing
method/accessor role before choosing syntax. A classified `Bodyless`
method becomes a semicolon declaration. A property or event maps each
role independently: `Bodyless` becomes that accessor's semicolon,
`Imported` supplies its body, and any address, classification, import,
or stage failure remains visible and blocks a plausible aggregate
declaration. A mixed bodyless/body-bearing accessor set must pass the
existing C# aggregate-validity rules; otherwise composition fails rather
than guessing syntax. Field-like event and trivial-accessor
presentation tests run only after these typed states exist; they may
choose compact syntax but cannot hide a failed accessor.
`ClassificationFailed` on an abstract method, property/indexer accessor,
or event accessor therefore follows the same terminal failure mapping as
a concrete method.

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
decision on the host function, and applies it. The identity is the exact
host MethodDef, not only a source kickoff: an independently projected
`MoveNext` or `SetStateMachine` is its own decision host. Preparation
captures that decision as shared replay authority and supplies it through
`PassContext` when building any other stage snapshot; the pass validates
the host identity and applies without re-recognizing. The snapshots are
owned mutable IR; consumers print detached root clones, not the stored
instances.

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
`PreparedStageBody.Render` is the sole source-body emission seam. It
clones the stored snapshot and retains its preparation stage as render
policy; a caller cannot relabel a Lowered snapshot as Raised. Both
stages apply byte-preserving `PrinterOptions` spelling/layout and print
analysis without rerunning a structural pipeline.

`StyleOptionCatalog.Options.Where(option => option.ByteDivergent)` is
the single set that defines render altitude. Lowered clears or otherwise
neutralizes every option in that set before any pass or printer-local
rewrite can observe it by folding
`descriptor.WithValue(options, descriptor.DefaultValue)` over the set;
the policy does not hand-maintain `PrinterOptions` property names.
Raised allows the requested set, and every cataloged byte-divergent
option that actually changes output records one typed `StyleLens`
decision keyed by the catalog option/value, whether the rewrite is an
IR pass or occurs inside spelling. This includes
`prefer-long-literal-suffix`, not only the conditional-return and
branchless-boolean passes. A new catalog entry without Lowered
suppression, Raised decision reporting, and an outcome specimen fails
set equality. Lowered therefore represents the
shape below all raised sugar, records no `StyleLens` decision, and
retains statement-to-opcode correspondence and interleaved IL. The seam
returns the rendered clone, result, and printed ranges as one value.

The pass remains in `IrPasses.Default` and `IrPasses.Lowered`. A
standalone seam-enabled pipeline with no supplied decision recognizes
through that same implementation, records an available outcome on its
function, and produces the same body as prepared output. This keeps
stage dumps, corpus sensors, validity/fidelity harnesses, and render A/B
on the shipped policy without requiring a MethodDef handle. A null-seam
physical pipeline cannot reconstruct or decline a kickoff because it
cannot import a companion machine. Support-method acknowledgment is the
deliberate local exception. Import stamps the exact current MethodDef
with immutable `ClassicAsyncSupportIdentity` metadata before pass
execution: exact module-scoped host address, `MoveNext` or
`SetStateMachine` role, complete interface signature, and the
MethodImpl/runtime interface-mapping evidence that this MethodDef
implements the corresponding `IAsyncStateMachine` member, plus the recognized
builder type from the declaring state-machine type's exact unique
`<>t__builder` FieldDef. This classification reads no sibling body or support
body and remains present under
`PassContext.None`; absence, malformed relationships, ambiguity, a
wrong signature, or a same-named overload is `None`/unavailable support
identity and preserves the body. The local pass then records/applies its
complete `NotClassic(SupportMethodAcknowledgment)` decision even with no
foreign import seam. A diagnosed DEC0001 crash function still keeps no
classic outcome under the independent path; parity includes preserving
its importer marker rather than manufacturing a classic decision.

The cached decision borrows no `IrNode`, block, local, edge, mutable
diagnostic collection, or other function sidecar from the first stage
host. `ClassicAsyncMachine.UserRegions` records stable
IL-origin/structured identities. `ClassicAsyncApplication` owns the
body/marker fragments and the complete deterministic mutation the pass
applies to a host: body edit, local-table reset, companion type-fact
contribution, pass-authored diagnostics, and every function fact the
pass changes. The application is present even when its body edit is
`None`. Recognition and replay both call that one application method. A
new pass mutation outside the application is a contract failure.
Generate observes the pristine host and constructs the complete decision
before mutation; only `ClassicAsyncApplication.Apply` edits the function,
and a failed decision leaves the host unchanged.

Support-method acknowledgment is part of that same decision path, not a
pre-decision normalization. Import constructs
`ClassicAsyncSupportIdentity` only after it resolves exactly one
`<>t__builder` FieldDef on the declaring state-machine type and classifies the
field's type as a recognized classic or async-iterator builder. Builder
identity never comes from the support method body: compiler-generated
`SetStateMachine` may be a bare `ret`, and hand-written mapped support bodies
need not read the builder field. Missing, duplicate, malformed, or unrecognized
builder fields produce no acknowledgment and preserve the method physically. A
generated MethodDef with exact `ClassicAsyncSupportIdentity` produces
`NotClassic(SupportMethodAcknowledgment)` plus a complete application.
Every recognized builder and both support roles record
`PreservedPhysical`; the application has `BodyEdit.None` and performs no
body or local-table mutation. Exact interface identity establishes a
support role, not that its implementation is disposable. Safe support
erasure would require separate immutable correlation to one unique
compiler kickoff/state-machine relationship and is outside slices 0 and
1. An unsupported custom builder receives ordinary `NotClassic(null)`
and likewise preserves both support bodies. This
typed support result does not change `IsClassicAsync`,
`IsAsyncMethodBuilder`, or the slice-0 accepted kickoff raise set.
Raised and Lowered preparation of the same support MethodDef capture and
replay the same application; a supplied kickoff decision can never apply
to its imported `MoveNext`. A decoy `MoveNext` overload,
`SetStateMachine` with the wrong parameter, same-name helper, or
unmapped explicit/implicit implementation never receives a support
application, even when it accesses `<>t__builder`; an exact mapped
hand-written or post-processed implementation may receive the local
acknowledgment but its statements and locals remain untouched.

`IrFunctionSnapshot.CloneDetached` is a root-level operation, not a
cast of the existing subtree-only `IrNode.Clone`. It recursively clones
the tree and independently copies every mutable root sidecar consumed
by later passes or the printer, including diagnostics, while immutable
metadata values may be shared. Mutating a render clone cannot change a
prepared snapshot, another stage, or a later render. Applying a
decision to a different module-scoped kickoff remains a typed stage
failure.

A supplied or unavailable classic directive is scoped to only the
prepared top-level host identity. `PassContext.RunForeignFunctionPipeline` is the
sole entry for running passes over any separately imported function. It
always derives a nested context that preserves the sibling-import seam,
type oracle, and shared recursion guard while resetting
`ClassicAsyncDecision` to `None`; it never returns the parent context as
a non-stepping optimization. It returns
`ForeignFunctionPipelineResult`, so expected classification decode/conflict
failure, body absence, null import, and recursion decline cannot collapse to
one null body. `CrossMethodPipelineScope.Run`, lambda and local-function
raising, classic companion inspection, and the reducible and foreach iterator
reconstruction helpers all use that entry instead of calling `IrPasses.Run`
with the parent's context.

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
`NestedFunctionEmbeddingPolicy.Classify(IrFunction)` reads the complete
classification stamped by the shared importer and returns either
`Embeddable` or `RetainLowered(Reason)`. Both `LambdaRaisingPass` and
`LocalFunctionRaisingPass` consume it after the foreign pipeline:

- `AsyncClassification.RuntimeAsync`, `ClassicAsync`, or `AsyncIterator`, or
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
Compiler-produced async-iterator local functions are the third
classified carrier witness. Await-bearing and await-free bodies both
retain the compiler-shaped call/delegate before unsupported and shape
checks; the existing synchronous iterator recognizer's
`IEnumerable`/`IEnumerator` scope cannot be used as evidence that
`IAsyncEnumerable` is safe to embed.
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

Once classification succeeds, it is metadata-only and exists even when
the method is bodyless or body preparation fails. The unions separate
address failure, resolved classification failure, body absence, import
failure, post-import stage failure, and a decided body:

- exact validation or carried/selector resolution failure is
  `AddressFailed`, not a handle-less rendering mode and not a classified
  body
- `Resolved(..., ClassificationFailed)` retains a resolved MethodDef but
  is a terminal visible failure. It has no classification, body state,
  import, stage, decision, modifier, or plausible source output
- `Resolved(..., Classified(..., Bodyless))` is a successful metadata
  selection with no
  `ClassicAsyncOutcome`; it is never `NotClassic`, `Declined`, or a
  diagnosed body failure. The projector creates no `DecompilerResult`;
  `ProduceBody` may materialize its existing typed absence diagnostic
  while mapping this state to `Absent`
- `Resolved(..., Classified(..., ImportFailed))` means a resolved,
  classified body-bearing method returned no function; it has no stage,
  decision, or
  `ClassicAsyncOutcome`
- `Resolved(..., Classified(..., Imported))` proves import succeeded
  even if its requested `StageBodyProjection` is `Failed`. It includes
  nonfatal diagnostics and DEC0001 crash functions;
  `ImportObservation` preserves their import-time classification without
  deleting the renderable function
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
- an async iterator is `AsyncClassification.AsyncIterator` and
  `IsClassicAsync = No`, so `NotClassic` is its required source-kickoff
  outcome; its generated support methods may carry the typed
  acknowledgment above

The canonical function and outcome feed every declared source-body
projection:

- `MemberCodeProvider` calls `MetadataBodyProjector` once whenever any
  member C# artifact is requested. Decompiled Source calls the prepared
  stage's render seam; Research receives the same prepared value. Its
  exact, carried-member, and physical-selector paths differ only in
  `MetadataBodyRequest`; all canonicalize to an exact address before
  classification and import. A carried member uses its persisted
  structural body key even when a same-named stale token looks valid in
  the current reader.
  `ClassificationFailed` produces the standard visible failed
  source result with its metadata diagnostic, null output, and
  `StyledProjectionProduced = false`; Fidelity Causes is `Failed`.
  `Bodyless` produces no C# body output, marker, or body modifier. It
  preserves the existing non-null failed `DecompilerResult` with null
  `Output` and the visible DEC0001 "has no IL body" diagnostic when
  Decompiled Source is requested; `StyledProjectionProduced` remains
  false. Fidelity Causes maps the same state to its existing typed
  `Absent`, not `Failed`.
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
  reconstruction directly. `ClassificationFailed` maps to each
  request's typed visible failure with no document, overlay, or rows; it
  never falls back to an unclassified declaration. `Bodyless` preserves
  each request's current
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
  or raw unmarked kickoff. Both stages honor byte-preserving
  `PrinterOptions`; only Raised applies byte-divergent style lenses.
  Lowered records no style-lens decision and keeps interleaved IL.
- `MemberBodyProducer.ProduceBody`, whole-member composition, and
  whole-type composition use the same front door. The public
  `MemberBodyProductionResult` and internal whole-type
  `DecompiledBodyProjection` carry classification, body text/shape
  facts, and `ClassicAsyncOutcome`. API members and accessors use
  `Carried(MetadataBodyTarget)` with a persisted structural key, typed
  role, and optional MVID-scoped hint; stale-token and cross-MVID paths
  never fall back to name/ordinal. Every existing
  accessor is projected before compact property/event syntax is chosen.
  `ClassificationFailed` maps to `MemberBodyProductionStatus.Failed`;
  whole-member fails visibly, and whole-type composition fails visibly
  rather than emitting that member, continuing with an unclassified
  declaration, or restamping a modifier. `Bodyless` maps to
  `MemberBodyProductionStatus.Absent` and preserves that API's current
  typed absence diagnostic. Whole-member production remains `Complete`
  with declaration-only text, and whole-type composition includes the
  same declaration, both with no diagnostic or marker. An imported
  DEC0001 crash function remains body-bearing: `ProduceBody` is
  `Complete` with its marker body, whole-member keeps its existing
  diagnostic-sensitive failure, and whole-type keeps the marker body
  under its existing `failOnDiagnostic: false` policy.
- Metadata-addressed `BodyShapeSearch` uses the same projector and does not
  create a second classic decision. `Bodyless` keeps its current silent skip.
  Address, classification, and import failures do not increment
  `MethodsInspected`; an imported DEC0001 crash function records failure
  without incrementing; every other imported projection increments once
  before stage/render disposition.
- `CSharpBodyDiff` remains outside source-body preparation. It renders the
  request-selected physical MethodDef, stamps no declaration, imports no
  companion, and gives a kickoff no reconstructed/declined outcome. An exact
  support host may retain only its local no-edit acknowledgment/application.
- `PipelineStages` is an orchestration exception to the projector front
  door, not an exception to classic policy. It runs the same
  seam-enabled pass pipeline; its terminal C# must stay byte-identical
  to prepared Raised output. Corpus and harness sweeps follow the same
  rule. Tests may intentionally exercise passless/raw APIs.
- A `DecompilerResult` rendered from a successful
  `StageBodyProjection` or seam-enabled canonical pipeline carries the
  same outcome. `AddressFailed`, `ClassificationFailed`, `Bodyless`,
  `ImportFailed`, `Unavailable`, `NotReached`, `DecisionFailed`,
  a null-seam kickoff render, and intentionally passless raw-IR
  rendering have no classic outcome. A seam-free host with exact
  `ClassicAsyncSupportIdentity` records its local
  `NotClassic(SupportMethodAcknowledgment)` outcome; a
  projector-prepared exact support stage is `Decided` with that same
  value.
  `DecompilerResult` takes outcome only from its own stage state, never
  from `MetadataBodyProjection.CapturedDecision`. Its hand-written
  `Equals` and `GetHashCode` include outcome presence, decline reason,
  decline body disposition, and support-method acknowledgment fields.
  `with` copies preserve them.

`CSharpBodyDiff` is intentionally not another source-body projection.
Its currency is C# lines anchored to IL origins in one physical
MethodDef. Its null `importMethodBody` seam keeps lambda,
local-function, iterator, and classic companion bodies out of that
coordinate plane; `StatementStartOffset` relies on that property.
Routing it through `MetadataBodyProjector` would admit foreign offsets,
change implementation-diff lines/LCS, and violate the physical evidence
contract. Slice 0 does not do that.

Implementation Diff is an independent downstream consumer. After its own
selection and correspondence logic chooses exact per-side physical MethodDefs,
each `CSharpBodyDiff` remains a seam-free projection: it does not import
companion bodies, reconstruct a kickoff, or introduce foreign offsets. A
kickoff therefore receives no classic outcome or marker. An exact support
MethodDef may carry only its local no-edit `SupportMethodAcknowledgment` and
replayable application. Physical line and IL offsets remain local to the
selected MethodDef.

This document gates only that async-specific integration behavior.
[Implementation Diff](implementation-diff.md) independently owns target
selection, participant correspondence, work-item totality, authored-source
eligibility, query lifetime, budgets, completed results, and CLI failure
semantics. Those contracts are neither prerequisites nor currencies of
ordinary async reconstruction.

The declaration rule uses the exact facts:

| Metadata fact | Resolved/body stage state | Declaration `async` |
| --- | --- | --- |
| Unavailable | `ClassificationFailed` | No declaration/body is emitted; failure is visible |
| Successful classification | `Bodyless` | `false`; declaration/skeleton only |
| `RuntimeAsync` | Any body-bearing state or failure | Preserve metadata `true` |
| `IsClassicAsync = Yes` | Body-bearing import failure with no stage | Preserve metadata `true`; failure is visible |
| `IsClassicAsync = Yes` | `Unavailable(ImportInternalError)` | Preserve metadata `true`; DEC0001/importer marker is visible |
| `IsClassicAsync = Yes` | failed `NotReached` or `DecisionFailed` | Preserve metadata `true`; failure is visible |
| `IsClassicAsync = Yes` | prepared or failed `Decided(Reconstructed)` | `true` |
| `IsClassicAsync = Yes` | prepared or failed `Decided(Declined)` | `false`; a successful render carries the classic marker |
| `IsClassicAsync = Yes` | `Decided(NotClassic)` | Invalid; fail the gate |
| Async iterator (`AsyncIterator`, `IsClassicAsync = No`) | Any body-bearing state or failure | Preserve current `false` |
| Other | Any body-bearing state or failure | `false` |

This preserves runtime-async methods whose awaiter recovery declined.
It also keeps async iterators out of the classic contract. Stage-local
state takes precedence over the carrier's success/failure status:
post-classic failure cannot move `Decided(Declined)` back into the
generic preparation-failure row.

The legacy `TypeShellProducer.RequiresAsyncBodyModifier` combination of
collapsed `StateMachineAsync` plus a second attribute query is replaced
on declared-source paths by the projector classification and stage-local
decision above. `ClassicAsync` includes async void; contradictory
evidence is `ClassificationFailed`, not a modifier-bearing declaration.

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
annotation and spelling over the `PreparedStageBody.Render` result;
the prepared stage owns whether byte-divergent lenses are legal.

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

### Separate structural Metadata facts from consumer policy

Trusted SM uniqueness in `LibraryBodyAsyncSourceResolver` uses
Analysis types and attribution filters (source-gen, GeneratedCode,
Blazor). Slice 0 does **not** lift that walk.

The shared index from #4669 is structural only: it decodes the attribute
claim, resolves its exact same-module TypeDef, authenticates the required
interface methods through signature and `MethodImpl` evidence, and reports
typed relationship failure. Analysis applies its attribution, evidence-scope,
lifted-owner, fallback, caller-projection, and recommendation policy after
that result. Decompiler correlates the exact structural result with the
state-machine type identified by kickoff IR, then owns reconstruction
eligibility and honest decline. The consumers may accept different
populations, but they cannot produce different physical relationship answers
for the same reader and address.

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
  MoveNextAddress      exact MetadataMethodAddress
  MoveNext             MethodRef decoded from that exact MethodDef
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

The sibling-import seam consumes the shared
`StateMachineRelationshipIndex`. Metadata resolves the kickoff's exact
same-module state-machine TypeDef, proves the applicable state-machine
interface, and returns the exact MethodDefs implementing required roles such
as `void MoveNext()` and `SetStateMachine`. An explicit `MethodImpl`
relationship wins when present; otherwise Metadata applies the runtime
interface-mapping rules over complete instance, name, generic-arity, return,
parameter, calling-convention, and custom-modifier signature evidence.

`ClassicAsyncCompanionResolver`, if retained, is a thin Decompiler adapter. It
accepts the exact kickoff address and kickoff-IR state-machine identity,
queries the shared index, requires those identities to agree, and lowers the
typed structural outcome into reconstruction or an honest decline. It does
not scan attributes, enumerate candidate types or methods, interpret
interface mappings, or build a reverse index. The pass imports the exact
`MetadataMethodAddress` returned by Metadata and never synthesizes a
name/ordinal `MethodRef`.
`PassContext.TryImportAndRunExactMethodBody` is the companion entry: it
imports the validated address, then delegates pass execution to
`RunForeignFunctionPipeline` so import/type/recursion seams survive and
the parent directive is reset exactly as for every other foreign host.

An overload such as `MoveNext(int)`, a same-named wrong-signature method,
metadata order, or a method that is not the interface implementation is never
a candidate. Metadata preserves missing, duplicate, ambiguous, malformed,
cross-kind, unresolved, and cross-module relationship outcomes. Decompiler
maps those typed outcomes to `NoMoveNext`, `AmbiguousMoveNext`, or
`MalformedMoveNextRelationship` while preserving the kickoff. Release struct,
Debug class, explicit `MethodImpl`, implicit implementation, decoy overload
before/after, and metadata-order-swap fixtures gate exact selection first in
Metadata and then through each consumer. The independently imported function
retains the exact address as its host identity, so decision/application replay
and foreign-pipeline scope validate the same MethodDef the index selected.

Support `BuilderKind` is observed from the exact unique
`<>t__builder` FieldDef on the support host's declaring state-machine type
while `ClassicAsyncSupportIdentity` is minted. The classifier never depends on
a `FieldRef` occurring in `MoveNext` or `SetStateMachine`; the latter is
commonly a bare `ret`. Slice 0 does **not** change
`IsAsyncMethodBuilder`: its only consumer is `FinalSetResult`, so widening it
would enable new non-generic ValueTask / void raises through today's
`TryBuild*`. Legacy raise eligibility stays unchanged.

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

`Declined` reasons include `NoMoveNext`, `AmbiguousMoveNext`,
`MalformedMoveNextRelationship`,
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
and every other exact support MethodDef remain physical. A custom
classic builder is outside the acknowledgment and raise domains but
inside `IsClassicAsync`; it visibly declines without deleting its
kickoff.

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
   without `async`, but exists only after successful classification.
   Malformed or contradictory async metadata is `ClassificationFailed` even
   on an RVA-zero/abstract method; it emits no plausible declaration or
   modifier and remains a typed visible failure.
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
4. **Every support MethodDef keeps its physical body** in the
   decompiler library and corpus. The pass may still produce a host-addressed
   `NotClassic(SupportMethodAcknowledgment)` decision; its application
   records `PreservedPhysical` and performs no body/local edit. Do
   **not** change `IsAsyncMethodBuilder`, the legacy raise gate. This
   removes destructive legacy hollowing for classic `SetStateMachine`
   and async-iterator `MoveNext` as well as preserving in-domain classic
   `MoveNext`. Unsupported custom builders also preserve both support
   bodies. This is a printer/corpus change:
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
   classification carrier and no-new-raise behavior; await-bearing and
   await-free async-iterator local functions prove the third
   classification arm; importer-crash seams separately prove
   unsupported disposition.
7. **Every declared source-body path consumes the canonical projector.**
   `MemberCodeProvider`, direct Research, Research queries,
   `MemberBodyProducer.ProduceBody`, whole-member/whole-type composition, and
   Body Shape call `MetadataBodyProjector` after the canonical Metadata
   address owner resolves the request. Classification precedes body status, which
   precedes import and stage preparation. Address failure,
   `ClassificationFailed`, `Bodyless`, `ImportFailed`, stage failure, and a
   decided body remain distinct. Abstract methods and accessors are not
   filtered before classification. `PreparedStageBody.Render` is the sole
   declared-source emission seam. `CSharpBodyDiff` is the named seam-free
   physical evidence exception, and `PipelineStages` is the named canonical-
   pipeline diagnostic exception.
8. **Evidence runs the same classic policy.** A seam-enabled
   `CSharpPrinter.PrintRaised`, `PipelineStages`, corpus profile, or
   validity/fidelity harness executes
   `ClassicAsyncReconstructionPass` with no supplied decision. The pass
   recognizes once and records the same typed decision/outcome that
   `MetadataBodyProjector` captures. `IrImporter.ImportAssembly` may
   remain a handle-free function sweep after acquisition, but it stamps the
   complete classification while each MethodDef handle and reader are still
   available; the pass then uses its function plus sibling-import context.

A concrete observation falsifies slice 0 if any healthy, successfully
prepared classic body is neither reconstructed nor visibly marked; a
non-narrow statement disappears; a declined classic declaration retains
`async`; a declined runtime-async declaration loses `async`; exact source
views disagree on outcome; classification runs after body status; a typed
address, classification, bodyless, import, stage, or render failure becomes
plausible output; replay loses application state or aliases mutable
snapshots; an outer decision reaches a nested function; a foreign body that
needs an async declaration carrier is embedded; Lowered applies a
byte-divergent style lens or loses interleaved IL; a physical C# diff imports
a companion body or gives a kickoff a reconstructed outcome; or any exact
support MethodDef loses distinctive physical logic.

## Fidelity subject

Kickoff IL Exact is the wrong subject: raised source recompiles to a
new kickoff + `MoveNext`. Current declined kickoffs are already
Partial (DEC0009). Slice 0 does not claim a fidelity-level change;
it claims a marker.

**No new accepted raise ships until a named measurement exists.**
Intended contract: compile the raised method with Roslyn, Release,
`runtime-async=off`, and compare the regenerated `MoveNext` (or
behavioral execution covering result, exception, suspension, and
side effects). That separately owned measurement should use the lowest
suitable typed IL comparison API directly, so its base contract is independently
satisfiable. A separate optional adapter may instead execute that comparison
through Implementation Diff's direct operation when available; no accepted
raise and no ordinary reconstruction path may require the adapter or the
higher-level comparison lifecycle. Until that harness exists, slices after 0
are blocked. Slice 0 owes A/B for honesty markers and physical preservation of
every exact support MethodDef (library + corpus).

## Slices

Slice 1 is the named product failure that opened #4472. Further
raise slices are not designed here. After slice 1, take a
classic-async shape census on the pinned corpus before **defining**
another raise. Do not invent more `TryBuild*` methods.

The #4669 Metadata index and its exactness, boundedness, and typed-failure
gates are implementation prerequisites to slice 0. They may land as an
independent prerequisite PR; slice 0 does not carry a temporary
Decompiler-owned relationship resolver while waiting for them.

| Slice | Claim | Residual after it |
| --- | --- | --- |
| 0. Honesty | Add the disjoint guarded runtime/classic/iterator classifier and carry complete `AsyncClassification` through every top-level and foreign import. Resolve body requests through `MetadataBodyProjector` and consume #4669 structural relationships through the thin companion adapter; keep `ClassificationFailed`, relationship failure, `Bodyless`, import, stage, and render states distinct. Capture and replay one exact-host `ClassicAsyncDecision`; keep support acknowledgment local, exact, replayable, and no-edit. Reset the directive for every foreign pipeline and use one nested embedding policy. Keep physical C# evidence seam-free. Mark every healthy classic decline, preserve non-narrow statements, correlate Debug class allocation and async-void return, leave legacy raise eligibility unchanged, and stop hollowing exact support MethodDefs. | #4472 remains declined but honest. Debug class and custom-builder methods remain unraised. Support MethodDefs remain physical. Bodyless, classification-failure, and relationship-failure behavior stays explicit. Lowered Research retains interleaved IL and suppresses cataloged byte-divergent lenses. Unsafe async local/lambda/iterator embedding stays lowered. Runtime-async recovery and independent Implementation Diff infrastructure are unchanged; no comparison operation/result enters reconstruction. |
| 1. Void-await then statements then return | Accept `await Task.Yield(); return ReadValue(value);` as the first inverse raise from `AwaitPoints` + `UserRegions`, not as a new `TryBuild*` and not as a `HasUnexpectedStore` allow-list tweak. Must consume void `GetResult` as a statement, following statements, a non-await `SetResult` operand, the Yield operand temp, and an explicit `LoadLocalAddress` decline-then-remap. Hoisted parameter binding is already present. The smaller `await Task.Yield();` (no later statements) is the accepted boundary of the same slice. Blocked until the Correct measurement exists. | General multi-state dispatch, class SM, custom awaiters, broader state-dispatch descriptor, census-defined raises. |

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
- Compiler-produced Release async-iterator local functions with
  await-bearing and await-free bodies at both stages.
  Their foreign imports carry `AsyncClassification.AsyncIterator` from the
  shared exact-address classifier, which produces
  `RetainLowered(AsyncDeclarationCarrierUnavailable)` before
  unsupported or shape checks; neither `IAsyncEnumerable` body becomes
  a synchronous-looking nested declaration.
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
- Iterator / async-iterator reconstruction. Support bodies remain
  physical; no async iterator is newly reconstructed.
- Depending on #4461's `DirectCall.Caller` rewrite.
- Chaining an async local-function `MoveNext` to the owning method.
- Moving Analysis `MemberRef` / `MemberResolver` / `FrameworkIdentity`
  / attribution filters into Metadata.
- A product-surface listing filter for nested SM types.
- An async-capable `LocalFunctionStatement` / `Lambda` declaration
  carrier or accepted nested classic-async raise.
- Teaching `TypeShellProducer` about reconstruction outcomes.
- Changing `CSharpBodyDiff` rendering from physical MethodDef evidence into a
  reconstructed source-body comparison. Independent comparison owners retain
  typed per-side selection and correspondence.
- Making ordinary async reconstruction depend on Implementation Diff
  participants, work items, mechanisms, budgets, correspondence, query
  completion, or results.
- Designing a state-dispatch raise before a corpus census.
- Another `TryBuild*` matcher.

## Layer ownership

| Fact | Owner |
| --- | --- |
| Exact/carried/selector source-body addressing | Metadata `MetadataMethodAddress`, `MetadataBodyTarget`, strict-key builder, and resolver; [member target resolution](member-target-resolution.md) owns user-facing selection |
| Disjoint runtime/classic/iterator evidence scan | Metadata, with collapsed `StateMachineAsync` retained only as compatibility inventory |
| Kickoff/state-machine/support-method relationships and reverse lookup | Metadata `StateMachineRelationshipIndex`, tracked by #4669 |
| Complete async classification transport | Guarded Metadata classifier to every exact top-level and foreign import |
| Canonical address/classification/body/import union | Decompiler `MetadataBodyProjector`, over shared-substrate Metadata address resolution |
| `ClassicAsyncMachine`, decision, and application | Decompiler `ClassicAsyncReconstructionPass` |
| Exact classic companion resolution | Metadata index; Decompiler sibling-import adapter correlates kickoff IR and consumes exact addresses |
| Slice-0 accepted-raise boundary | Decompiler `LegacyRaiseEligibility`, separate from broader recognition |
| Frozen import observation and root snapshot clone | Decompiler `MetadataBodyProjector` and `IrFunctionSnapshot` |
| Shared cross-stage replay and stage-local outcome | Decompiler `MetadataBodyProjection`, `PassContext`, and `StageBodyProjection` |
| Raised/Lowered render altitude | Decompiler `PreparedStageBody` over `StyleOptionCatalog.ByteDivergent` |
| Kickoff and support-method mutation | Decompiler `ClassicAsyncApplication` |
| Foreign-function execution and directive reset | Decompiler `PassContext.RunForeignFunctionPipeline` |
| Nested lambda/local-function embedding disposition | Decompiler `NestedFunctionEmbeddingPolicy` |
| Public typed-body and whole-type carriers | Decompiler `MemberBodyProductionResult` and internal `DecompiledBodyProjection` |
| Research source presentation | Research over Decompiler-prepared clones |
| Physical C# async behavior | Decompiler `CSharpBodyDiff` over independently selected exact MethodDefs |
| Optional cross-version endpoint, participant, correspondence, work-item, mechanism, population, budget, completion, and result lifecycle | Independent [Implementation Diff](implementation-diff.md) consumer |
| CLI presentation | CLI |

## Gates

Honesty is unverified until these exist. They exercise the render or named
library/corpus surface, not only the metadata predicate. Independent
Implementation Diff lifecycle gates are not repeated here because they do not
gate ordinary reconstruction.

| Gate | Surface | Fails if |
| --- | --- | --- |
| Ordinary-path independence | Decompiler + Research + Queries source-architecture tests | Async projection mints or consumes an Implementation Diff participant, correspondence receipt, work item, mechanism, budget, query lifetime, completion, or result; or body projection bypasses exact address resolution |
| Carried target resolution | Metadata + Decompiler projector | A carried target omits key version or its sole relationship role; strict keys omit signature/modifier/scope evidence or duplicate the role; a same-MVID reopened-reader hint that passes row/key/role validation is rejected; a cross-MVID hint or role mismatch is not `Rejected`; unavailable/rejected/ambiguous/failed outcomes collapse or lose candidates/reasons; or resolution uses name, ordinal, presentation anchor, or raw token equality |
| Legacy body-target migration | Exact declared-source caller/sink manifest plus one non-vacuity removal test | `ResolvedMemberTarget.Body`, `BodyTarget.MetadataToken`, `DeclaringOverloadIndex`, or name/ordinal `ResolveMethod` still addresses a declared-source body; a live-reader selection omits `MetadataBodyTarget`; or a non-body compatibility consumer is forced through the projector |
| Exact async population matrix | Metadata + Decompiler top-level and foreign imports | Runtime, classic, and iterator evidence collapse; contradictory positives do not fail before body/import; or custom classic builders escape visible decline |
| Exact state-machine relationship index | `StateMachineRelationshipIndex_ResolvesExactInterfaceImplementations` over Metadata fixtures | Explicit/implicit interface implementation, signature, custom modifiers, `MethodImpl`, claim kind, named decoys, or metadata order select the wrong MethodDef |
| State-machine relationship totality | `StateMachineRelationshipIndex_PropagatesTypedFailures` over Metadata fixtures | Missing, duplicate, cross-kind, unresolved, malformed, foreign-module, budget, or ambiguous evidence becomes empty success, throws an expected decode failure, or loses its candidates and reason |
| Consumer policy separation | `StateMachineRelationshipConsumersRetainDistinctPolicy` over Analysis and Decompiler fixtures | Either consumer reimplements structural discovery, Analysis filters enter Metadata, Decompiler eligibility enters Metadata, or adopting the common fact forces equal accepted populations |
| Canonical front-door architecture | `MetadataAddressedBodyProjectionUsesCanonicalFrontDoor` | A declared-source consumer emits a body or runs a top-level pass outside `MetadataBodyProjector` / `PreparedStageBody`, except the named physical and diagnostic seams |
| Classification and body-status ownership | `DeclaredSourceAsyncClassificationUsesProjector` + `DeclaredSourceBodyStatusUsesProjector` | A consumer classifies, catches decode failure, checks RVA/`HasBody`, filters abstract members, or derives a body modifier outside the projector |
| Resolved-address projection parity | Metadata resolver + Decompiler projector | Requests resolving to the same MethodDef produce different classification, lifecycle state, outcome, render, or diagnostic |
| Resolved bodyless lifecycle | Decompiler + CLI + Research + typed/whole-member/type + Body Shape | `Bodyless` becomes import/stage failure, emits a body/marker/modifier, loses its existing absence signal, or counts as inspected |
| Resolved classification-failure lifecycle | Decompiler + CLI + Research + typed/whole-member/type + Body Shape | Failure loses `DEC0015`, runs body/import work, emits plausible output, or differs for concrete, abstract, method, or accessor cases |
| Import observation totality | Decompiler projector | A non-null function is not `Imported`, frozen import diagnostics change, DEC0001 is inferred late, or null import has a stage |
| Importer-crash preservation | Existing importer-crash surfaces + projector | The marker/DEC0001 disappears, the stage gains a classic outcome, or metadata `async` is lost |
| Complete classic application replay | Decompiler pass/projector | Another stage recognizes again; replay differs in tree, locals, type facts, diagnostics, provenance, modifier state, or outcome; or a decision applies to another host |
| Exact classic companion identity | Metadata index + Decompiler thin adapter/pass | Decompiler scans structural relationships, kickoff IR disagrees with the returned type without decline, name/order selects `MoveNext`, or a typed relationship failure reconstructs |
| Support-method identity and preservation | Decompiler importer/pass/projector + seam-free physical C# | Exact support mapping or builder identity is guessed; acknowledgment edits body/locals; replay differs; or classic/iterator/custom-builder support logic is lost |
| Stage-local classic state | Decompiler projector failure matrix | A healthy stage lacks `Decided`; DEC0001 gains a decision; pre-pass failure borrows another stage's outcome; or post-pass failure loses its own outcome |
| Snapshot clone isolation | Decompiler snapshot/render | Mutating one render changes another stage/render or any frozen sidecar |
| Foreign-function decision scope | Decompiler local/lambda/iterator fixtures | A parent directive reaches a foreign pipeline, nested identity resolves as the outer host, or a nested function fails to decide independently |
| Foreign-function pipeline architecture | Product pass-run inventory | A separately imported function runs passes outside `PassContext.RunForeignFunctionPipeline` |
| Foreign classification transport | Decompiler importer/pipeline matrix | A metadata-backed foreign import lacks complete classification or collapses classification failure, bodyless, null import, and recursion decline |
| Nested embedding honesty | Runtime, classic, iterator, modifier-fallback, and importer-crash local/lambda fixtures + `NestedFunctionEmbeddingUsesSharedPolicy` | A foreign body needing async syntax or carrying unsupported output is embedded; classic nesting widens the accepted raise set; or lambda/local-function rules diverge |
| Prepared/canonical pipeline parity | `PipelineStageTests.DumpMethod_FinalCSharp_IsTheShippedProductOutput` | Prepared Raised output/outcome differs from the terminal seam-enabled stage |
| Raised/Lowered Research contract | Catalog-derived byte-divergent style specimens | Lowered observes a divergent option or loses IL; Raised changes output without a typed decision; or altitude is caller-relabelled |
| Honesty marker and fidelity cause | Five CLI code views + public typed body + whole-type + Fidelity Causes | A declined classic body lacks its unsupported marker or DEC0004 |
| Non-narrow preservation | CLI + typed/whole-type over extra call/store fixtures | Any original statement disappears |
| Declaration modifier by stage-local state | CLI + typed/whole-member/type | Declined classic retains `async`, reconstructed classic omits it, runtime async loses it, or post-classic failure changes the retained decision's modifier |
| Address/classification/body/import/stage/render union | Decompiler + CLI + Research + whole-type + Body Shape | Lifecycle states collapse, failures acquire success-shaped outcomes, or inspection accounting changes |
| Exact legacy raise population | Existing accepted fixtures + close negatives | Slice 0 widens accepted reconstruction or a new eligibility path escapes set equality |
| Optional Implementation Diff boundary | `CSharpBodyDiff` + Findings + Implementation Diff async fixtures | The physical projection imports companions, gives kickoff a classic outcome/marker, mutates support bodies, loses local acknowledgment, changes unrelated offsets, or makes ordinary reconstruction depend on the comparison operation/result |
| `DecompilerResult` value semantics | Decompiler tests | Outcome, decline, disposition, or support acknowledgment is omitted from equality/hash/`with` behavior |
| Corpus A/B | `CorpusSensor` / `IrImporter.ImportAssembly` | Product and corpus policy differ, support methods are hollowed, or fidelity/coverage changes are unrecorded |

Deleting marker insertion must fail the render gate; deleting fidelity-cause
enumeration must fail the DEC0004 gate. Widening
`IsAsyncMethodBuilder` must fail the legacy-raise gate. Removing decision
capture, application-owned state, the shared relationship-index lookup, exact
companion/support identity, foreign context reset, shared nested embedding
policy, or catalog-derived render altitude must each fail its independent
gate.
