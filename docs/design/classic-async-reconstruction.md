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
MetadataBodyProjector.Prepare(MetadataSource, MetadataBodyRequest)

MetadataBodyRequest
  Exact(MetadataMethodAddress)
  Carried(MemberBodyTarget)
  Selector(TypeFullName, MemberTargetSelector, Visibility)

MemberBodyTarget
  Key                    versioned MemberBodyKey
  DeclaringType          exact MetadataTypeDefinitionName
  Role                   Method | Getter | Setter | Adder | Remover
  PreferredAddress?      same-origin MetadataMethodAddress hint

MemberBodyKey
  Version                body-key-v1
  StructuralSignature    strict MethodStructuralSignature

MemberBodyTargetResolution
  Resolved(MetadataMethodAddress)
  Missing(Diagnostics)
  Ambiguous(Diagnostics)
  Unavailable(Diagnostics)
  Malformed(Diagnostics)

MetadataBodyProjectionResult
  AddressFailed(Diagnostics)
  Resolved(MetadataBodyResolvedProjection)

MetadataBodyResolvedProjection
  Address                module-scoped MethodDef identity
  ClassificationFailed(Diagnostics)
  Classified(AsyncClassification, BodyProjection)

AsyncClassification      RuntimeAsync | ClassicAsync | AsyncIterator | Other

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

SupportMethodAcknowledgment
  MethodKind             MoveNext | SetStateMachine
  BuilderKind            known builder identity
  BodyDisposition        PreservedPhysical | ReplacedWithEmptyReturn

BodyDisposition
  ReplacedNarrowHandoff
  PreservedOriginal
```

Metadata import adds `IsClassicAsync` to `MethodBody` / `IrFunction`
from the existing SRM classification (`StateMachineAsync` plus
`AsyncStateMachineAttribute`). `StateMachineAsync` alone is not this
fact: it also includes `AsyncIteratorStateMachineAttribute`.

`Exact` accepts only a complete `MetadataMethodAddress`: MVID plus
validated MethodDef handle. It revalidates MVID, token table, row
bounds, and MethodDef ownership against the live source. A raw
`ApiMember.MetadataToken`, getter/setter token, or adder/remover token
never becomes `Exact` by pairing it with the module that happens to be
open.

Carried API members use `Carried(MemberBodyTarget)`, not
`MemberAnchor`. Surface anchors and metadata anchors intentionally use
different spelling spaces and
`SurfaceAndMetadataAnchors_UseDistinctSpellings` continues to gate that
they are never compared for body correspondence. API extraction mints a
new versioned `MemberBodyKey` directly from the originating MethodDef's
existing strict `MethodStructuralSignature`. The key includes exact
declaring-type structure, metadata method name, generic parameters and
constraints, calling convention, return and parameter types, by-ref and
pointer shape, arrays, function pointers, and every `modreq`/`modopt`.
`body-key-v1` stores the bounded canonical structural transport, not a
hash-only fingerprint; equality therefore does not introduce a collision
acceptance path. Existing structural-signature work and materialization
budgets gate both minting and live projection.

The key is persisted on the API member and separately for each
property/indexer/event accessor; JSON round trips retain it even though
`SignatureModel` and physical addresses do not. This is an additive
versioned API-surface schema field, not display text. Legacy or
programmatically constructed carried data without a supported key
cannot establish cross-reader body identity and returns typed
`AddressFailed(BodyKeyUnavailable)` rather than parsing `Signature`,
using `MemberAnchor`, or falling back to name/ordinal.

`MemberBodyTarget.DeclaringType` is persisted beside the opaque key so
resolution never parses the structural-signature transport to find its
candidate set. An extension projected onto its receiver retains the
extension holder's exact definition name here; the selected receiver
remains selector context, not body declaring-type identity.

`MemberBodyKey` is body correspondence only. It does not enter
`MemberAnchor`, stable selectors, API diff identity, ordering, or
presentation. Older JSON payloads deserialize with no key and remain
usable for API inventory/display; only a cross-reader body request fails
visibly. New schema/round-trip tests pin the optional field, all accessor
slots, unknown-version refusal, and non-action on API identity.

Getter, setter, adder, and remover remain exact relationship roles, not
accessor ordinals. API extraction may also retain an ephemeral
body-facing `MetadataMethodAddress` hint minted from the originating
reader; existing raw tokens may remain for non-body contracts but never
act as body identity. The Metadata-owned
`MemberBodyTargetResolver` validates a same-MVID preferred address
against the structural key and role. A missing, foreign-MVID, stale, or
mismatched hint is ignored as authority and the resolver projects the
same versioned structural key and role from bounded live metadata. It
returns one exact address or typed missing, ambiguous, unavailable, or
malformed evidence; it never selects the first same-named MethodDef.

The resolver uses metadata names, signatures, constraints, and
relationship rows. It does not parse declaration/display text or inspect
method bodies. Carried targets and ordinary fresh selectors read no
custom attributes. A fresh selector's unqualified candidate domain is
declared methods/accessors of the requested type; attached extension
selection requires the explicit existing `extension:` kind. Only that
kind activates one narrowly scoped, guarded `ExtensionAttribute` scan
and the existing extension-receiver attachment rules. Expected decode
failure is typed address-resolution failure; a true extension and an
otherwise identical static helper are close rows. The resolver never
reads `AsyncStateMachineAttribute` or
`AsyncIteratorStateMachineAttribute`, so malformed async attribute
metadata remains a resolved classification failure rather than an
address failure.

Property/event roles follow their MethodSemantics relationships.
Duplicate or unavailable structural identity is `AddressFailed`, not
permission to use order. `MemberBodyTargetResolution.Malformed` means
the member's name/signature/constraint/relationship identity itself
could not be decoded and therefore maps to `AddressFailed`; malformed
async custom-attribute metadata remains the distinct resolved
`ClassificationFailed` state.

`Selector` is only a fresh user question against the current source.
`MemberBodyTargetResolver` accepts the existing typed
`MemberTargetSelector` syntax and applies its kind/digest/ordinal rules
within the body candidate domain above, without requiring full
API-surface extraction. Unqualified body selection is intentionally
declared-member-only; projected extensions require `extension:`. CLI/API
selection that already resolved an extension through the full API
surface passes its carried body target and does not reselect here, so
this narrows only direct name/ordinal body requests that lack an API
member identity. A new Metadata-owned `MemberBodyCandidateProjector` is
the bounded
per-target-type capability: it projects declared methods/accessors and
for an explicit `extension:` request reuses the existing
extension-attachment rules to project extension methods whose first
parameter corresponds to that target. It owns one bounded extension
scan/index per source, uses the same safety budgets and exact type-name
correspondence as API extraction, and returns typed incomplete evidence
rather than a partial candidate list. It does not materialize unrelated
API declarations, documentation, or display attributes. The resolver
selects over that projection and produces the role, current body key,
actual declaring type, and exact address.
Name/ordinal selection may therefore express a user's current-source
request, but it is never a fallback for carried data, a stale token, or
an accessor. The optional `extension:` kind invokes only the guarded
extension projection above. All later classification, body-status,
import, preparation, and rendering use the resolved exact MethodDef.
Any failed exact, carried, or selector resolution is a typed outer
`AddressFailed(Diagnostics)`, never a direct-printer bypass or a
plausible unmarked body.

After resolution, the projector performs metadata async classification
once, before inspecting or importing the body. The expected
`BadImageFormatException` from malformed custom-attribute
constructor/type metadata becomes
`Resolved(Address, ClassificationFailed(Diagnostics))`; the projector
does not broadly catch unexpected failures. The failure carries the new
stable `DEC0015` (`MetadataClassificationFailed`) diagnostic with the
resolved address and decode detail. This terminal state retains the
resolved address but has no `AsyncClassification`, body projection,
import, stage, decision, modifier, or render. Its diagnostics are
detached immutable values. Exact, carried-key, and selector requests that
resolve to the same MethodDef produce the same classification result
and diagnostic.

After successful classification, the projector reads the MethodDef body
status once.
An abstract, extern, interface, or other RVA-zero method is
`Resolved(..., Classified(..., Bodyless))`: address and metadata
classification remain available, but there is no import, stage, outcome,
marker, or render failure. A carrier may preserve its existing typed
absence diagnostic; that does not turn the projector state into
`ImportFailed` or a failed stage. Selector ordinals continue to count
bodyless methods exactly as the existing resolver does. A declared-source
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
deliberate local exception: it reads only the current host, remains
available under `PassContext.None`, and records/applies its complete
`NotClassic(SupportMethodAcknowledgment)` decision even with no import
seam. A diagnosed DEC0001 crash function still keeps no classic outcome
under the independent path; parity includes preserving its importer
marker rather than manufacturing a classic decision.

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
pre-decision normalization. A generated `MoveNext` or `SetStateMachine`
whose body carries a recognized builder produces
`NotClassic(SupportMethodAcknowledgment)` plus a complete application.
For each of the five in-domain classic builders, `MoveNext` records
`PreservedPhysical` with no body/local mutation, while
`SetStateMachine` may record `ReplacedWithEmptyReturn` with the owned
empty body and local-table reset. `AsyncIteratorMethodBuilder` keeps the
legacy `MoveNext` replacement. An unsupported custom builder receives
ordinary `NotClassic(null)` and preserves both support bodies. This
typed support result does not change `IsClassicAsync`,
`IsAsyncMethodBuilder`, or the slice-0 accepted kickoff raise set.
Raised and Lowered preparation of the same support MethodDef capture and
replay the same application; a supplied kickoff decision can never apply
to its imported `MoveNext`.

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
- an async iterator is `StateMachineAsync` but
  `IsClassicAsync = No`, so `NotClassic` is its required source-kickoff
  outcome; its generated support methods may carry the typed
  acknowledgment above

The canonical function and outcome feed every declared source-body
projection:

- `MemberCodeProvider` calls `MetadataBodyProjector` once whenever any
  member C# artifact is requested. Decompiled Source calls the prepared
  stage's render seam; Research receives the same prepared value. Its
  exact, carried-member, and fresh-selector paths differ only in
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
  `Carried(MemberBodyTarget)` with a persisted structural key, typed
  role, and optional MVID-scoped hint; stale-token and cross-reader paths
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
- Metadata-addressed `BodyShapeSearch` uses the front door too. Its
  fidelity/search policy remains separate, but it does not create a
  second classic-async decision.
  `BodyShapeSearch.IncompleteBodyReason` replaces its current
  classic-or-async-iterator attribute union with the exact prepared
  outcome. `Bodyless` preserves the current silent skip: it is not
  inspected, incomplete, matched, or recorded as a search failure.
  `AddressFailed`, `ClassificationFailed`, and `ImportFailed` record
  failure without incrementing `MethodsInspected`. An `Imported`
  projection whose frozen
  `ImportObservation.HasInternalError` is true also records its DEC0001
  failure without incrementing or materializing a stage; that is Body
  Shape's existing import-health policy, not a global claim that the
  crash function is unrenderable. Every other `Imported` projection,
  including one with nonfatal diagnostics, increments exactly once
  before stage or render disposition, so a later failure records both
  one inspected method and one search failure as it does today.
- `CSharpBodyDiff` remains outside this contract: it renders one
  supplied physical tree, does not stamp a declaration, and cannot claim
  a kickoff `Reconstructed`/`Declined` outcome. A support host may carry
  its seam-independent local acknowledgment/application outcome.
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
  rendering have no classic outcome. A seam-free support host records
  its local `NotClassic(SupportMethodAcknowledgment)` outcome; a
  projector-prepared support stage is `Decided` with that same value.
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
change implementation-diff lines/LCS, and require a separate
correspondence design. Slice 0 does none of those things.

The diff therefore remains a named seam-free physical-evidence
projection. It can display the physical kickoff because it neither
prints a declaration nor claims reconstructed source. A kickoff carries
no classic outcome or marker. A support MethodDef may carry its locally
decided acknowledgment and application; this preserves today's
seam-independent hollow/preserve policy without importing another
MethodDef. The diff gate pins the null seam, exact physical provenance,
support-only outcome boundary, and unchanged non-classic line/offset
output.

The declaration rule uses the exact facts:

| Metadata fact | Resolved/body stage state | Declaration `async` |
| --- | --- | --- |
| Unavailable | `ClassificationFailed` | No declaration/body is emitted; failure is visible |
| Any classification | `Bodyless` | `false`; declaration/skeleton only |
| `RuntimeAsync` | Any body-bearing state or failure | Preserve metadata `true` |
| `IsClassicAsync = Yes` | Body-bearing import failure with no stage | Preserve metadata `true`; failure is visible |
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

The sibling-import seam owns a new
`ClassicAsyncCompanionResolver`. It resolves the kickoff's exact
same-module state-machine TypeDef, proves that type implements
`System.Runtime.CompilerServices.IAsyncStateMachine`, and resolves the
one MethodDef that implements interface `void MoveNext()`. An explicit
MethodImpl relationship wins when present; otherwise the resolver uses
the runtime interface-mapping rules over complete instance, name,
generic-arity, return, parameter, calling-convention, and custom-
modifier signature evidence. It returns an exact
`MetadataMethodAddress`; the pass imports by that address and never
synthesizes a name/ordinal `MethodRef`.
`PassContext.TryImportAndRunExactMethodBody` is the companion entry: it
imports the validated address, then delegates pass execution to
`RunForeignFunctionPipeline` so import/type/recursion seams survive and
the parent directive is reset exactly as for every other foreign host.

An overload such as `MoveNext(int)`, a same-named wrong-signature
method, metadata order, or a method that is not the interface
implementation is never a candidate. Missing, duplicate, ambiguous,
malformed, or cross-module relationship evidence produces a typed
decline (`NoMoveNext`, `AmbiguousMoveNext`, or
`MalformedMoveNextRelationship`) while preserving the kickoff. Release
struct, Debug class, explicit MethodImpl, implicit implementation, decoy
overload before/after, and metadata-order-swap fixtures gate exact
selection. The independently imported function retains the exact address
as its host identity, so decision/application replay and foreign-pipeline
scope validate the same MethodDef the resolver selected.

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
   attribute. `ClassificationFailed` emits no plausible declaration or
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
4. **In-domain `MoveNext` is the physical body** in the decompiler
   library and corpus. Stop hollowing `MoveNext` when
   the acknowledgment-only `BuilderKind` is one of the five builders
   above. The pass still produces a host-addressed
   `NotClassic(SupportMethodAcknowledgment)` decision; its application
   records `PreservedPhysical` and performs no body/local edit. Do
   **not** change `IsAsyncMethodBuilder`, the legacy raise gate. Do **not**
   un-hollow `AsyncIteratorMethodBuilder`. `SetStateMachine` may
   still use an application-owned empty support body. Unsupported custom
   builders preserve both support bodies. This is a printer/corpus change:
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
   The front door accepts an exact `MetadataMethodAddress`, an API
   persisted modifier-complete body key plus typed method/accessor role
   and optional same-origin address hint, or a fresh current-source
   `MemberTargetSelector`; every path resolves once to an exact address.
   A stale or cross-reader hint resolves by structural body-key
   correspondence, never name/ordinal, and missing/ambiguous resolution
   is typed and visible.
   Abstract methods and accessors are not filtered before projection. A
   resolved bodyless method is typed `Bodyless`, not `ImportFailed` or a
   failed stage: typed body production returns `Absent`,
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
body; a carried member/accessor resolves through stale name/ordinal
identity, or a fresh selector bypasses preparation; an abstract member
or accessor is filtered before classification; replay loses companion
type facts or aliases mutable snapshot state; an outer decision reaches
a nested function; a Lowered render applies a byte-divergent style lens
or loses interleaved IL; a bodyless member loses its existing visible
absence signal, gains C# body output, a marker or modifier, or becomes
an inspected Body Shape; a null import counts as inspected; an imported
DEC0001 crash function is erased from a render surface,
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
intentionally stale-token rows. Slice 0 flips both assertions. The
second row becomes a stale-MVID carried-key resolution gate; separate
same-name overload and overloaded-indexer rows prove a carried member
cannot alias another valid token or ordinal-zero accessor.

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
| 0. Honesty | Add SRM `IsClassicAsync`, structured exact/carried-key/fresh-selector addressing with resolved `ClassificationFailed` and `Bodyless`, Decompiler-owned `MetadataBodyProjectionResult`, detached function snapshots, catalog-owned render altitude, complete exact-host application values, and typed body carriers. Carried API methods/accessors resolve by a persisted versioned `MethodStructuralSignature` body key plus relationship role and MVID-scoped hints, never `MemberAnchor` or stale name/ordinal fallback; abstract members project before presentation filtering. Classic companion `MoveNext` resolves by exact signature and `IAsyncStateMachine` implementation. `ClassicAsyncReconstructionPass` remains the single decision implementation: the projector captures/replays one decision across top-level stages, support-method acknowledgment is an application-owned seam-independent local disposition, import-internal-error crash functions remain outcome-unavailable, and one foreign-function pipeline entry always resets the parent directive. A shared nested-function embedding policy retains any async-classified body, body requiring an async declaration modifier, or unsupported body as lowered compiler structure; this also corrects the current Full-invalid await-bearing local and await-free local/lambda runtime-async embeddings at that shared seam. Seam-enabled stage/corpus/harness runs use the same pass. Every declared source-body path canonicalizes through the front door; seam-free physical evidence remains separate. Every healthy classic decline gets a marker: replace exact narrow handoff; prepend while preserving a non-narrow body. Correlate Debug class allocation and void `Return(null)`. Leave legacy raise eligibility unchanged. Stop hollowing in-domain `MoveNext`; library + corpus A/B. | #4472 fixture still declined, but honest. Debug class SMs are honest but not raised. Async-iterator `MoveNext` still hollow through replayable support application. Custom builders visibly decline with preserved bodies. Bodyless members remain absent/declaration-only. Resolved classification failures remain typed and visibly failed. Lowered Research suppresses every cataloged byte-divergent lens and retains interleaved IL. Importer crash markers and metadata modifiers remain unchanged. Async local/lambda declarations are not reconstructed until they have a typed async carrier. Runtime-async recovery is unchanged; only unsafe nested embedding is retained lowered. Physical C# diff remains MethodDef-scoped and seam-free, while locally decidable support hosts may carry support acknowledgment. No trusted Metadata/Analysis lift. |
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
| Exact classic companion resolution | Decompiler sibling-import seam + `ClassicAsyncCompanionResolver` over Metadata relationships |
| Slice-0 accepted-raise boundary | Decompiler `LegacyRaiseEligibility`, separate from broader recognition |
| Classic-async metadata fact | Metadata import → `MethodBody` / `IrFunction` |
| Persisted modifier-complete body identity | Metadata API extraction → versioned `MemberBodyKey` over `MethodStructuralSignature` |
| Fresh per-type body candidates and extension attachment | Metadata `MemberBodyCandidateProjector`, sharing API extraction's exact attachment rules and safety budgets |
| Exact address and carried member/accessor correspondence | Metadata `MemberBodyTargetResolver` over `MetadataMethodAddress`, versioned `MemberBodyKey`, and typed accessor role |
| Exact/carried-key/fresh-selector canonicalization and classification/body/import union | Decompiler `MetadataBodyProjector` over the Metadata resolver |
| Resolved metadata classification failure | Decompiler `MetadataBodyProjector`; catches only expected metadata decode failure |
| Frozen import diagnostics / DEC0001 observation | Decompiler `MetadataBodyProjector` at importer return |
| Complete root snapshot clone | Decompiler `IrFunctionSnapshot` |
| Shared cross-stage replay authority | Decompiler `MetadataBodyProjection` + `PassContext`, scoped to one exact top-level host |
| Stage-local decision/outcome/unavailable state | Decompiler `StageBodyProjection` |
| Raised/Lowered render altitude | Decompiler `PreparedStageBody` over `StyleOptionCatalog.ByteDivergent`; Research cannot override it |
| Kickoff and support-method host mutation | Decompiler `ClassicAsyncApplication` |
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
| Seam-free physical C# body evidence | Decompiler `CSharpBodyDiff` (no kickoff outcome; local support acknowledgment allowed) |
| Corpus / library `MoveNext` rendering | Decompiler |

## Gates

Honesty is unverified until these exist. They must exercise the
**render** or the named library/corpus surface, not the metadata
predicate. They must not assume current kickoffs are Full.

| Gate | Surface | Fails if |
| --- | --- | --- |
| Exact async population matrix | Metadata + Decompiler tests | Async iterator is rejected as invalid `NotClassic`; custom classic builder escapes visible `Declined`; or a DEC0001 crash function is forced into `NotClassic`, `Reconstructed`, or `Declined` instead of `Unavailable` |
| Canonical front-door architecture | Source-architecture test `MetadataAddressedBodyProjectionUsesCanonicalFrontDoor` | Product-consumer references outside `CSharpPrinter` / pass definitions to any body-emitting printer API or direct top-level `IrPasses.Run*` differ from the complete set: `MetadataBodyProjector` / `PreparedStageBody`, seam-free `CSharpBodyDiff`, and canonical-stage `PipelineStages` |
| Classification policy ownership | Source-architecture test `DeclaredSourceAsyncClassificationUsesProjector` | A declared-source consumer classifies the resolved MethodDef, catches its decode failure, or derives a body modifier outside `MetadataBodyProjector` and the typed projection; exact non-source metadata inventories remain named exceptions |
| Body-status policy ownership | Source-architecture test `DeclaredSourceBodyStatusUsesProjector` | A declared-source consumer uses RVA/`HasBody`/null import or semantic `ApiMember.IsAbstract` filtering to choose `Bodyless`, suppress a method/accessor, skip classification, or map state to a carrier; the exact retained body-reference/semantic-policy manifest differs, or any named migration site remains |
| Body-target identity architecture | Metadata + Decompiler source-architecture tests | A body-facing raw token is treated as exact without its originating MVID; a carried API member/accessor falls back to name/ordinal or either `MemberAnchor` spelling; an accessor is identified by ordinal rather than role; target resolution parses declaration/display text, reads bodies, or reads a custom attribute outside the guarded ExtensionAttribute exception; an unsupported/legacy body key guesses; or duplicate/unavailable structural correspondence selects a candidate |
| Per-type body candidate projection | Metadata product tests over declared methods/accessors, explicit `extension:` true extensions, generic receivers, static-helper near misses, malformed ExtensionAttribute, and budget exhaustion | Fresh selector resolution requires full API-surface extraction; unqualified selection scans/attaches extensions; explicit extension attachment logic is duplicated or disagrees with API extraction; the projected receiver replaces the actual body declaring type; unrelated API/docs/display work runs; malformed/budget exhaustion returns a partial candidate set; or candidate order changes identity |
| Modifier-complete body-key contract | Metadata key/schema tests deriving specimens from `MethodStructuralSignature`, including safety-limit and hash-collision near cases | API extraction and live candidate projection produce different keys for one MethodDef; version or exact declaring type is omitted; a hash-only fingerprint is authoritative; structural safety limits are bypassed or collapsed to missing; API JSON loses the target/key or accessor-specific targets; an attached extension stores the receiver instead of its holder; a legacy missing/unknown version is accepted cross-reader; `modreq`/`modopt`, calling convention, generic constraint, function-pointer, or by-ref drift compares equal; surface/metadata `MemberAnchor` spelling affects the body key; or the optional key changes API anchor/diff/order/presentation identity |
| Exact/carried/selector address parity | Metadata resolver + Decompiler projector with same-module exact, persisted-key JSON round trip, legacy missing-key failure, stale-MVID, valid-wrong-row token, hidden same-name overload drift, modreq/modopt drift, explicit true-extension/static-helper, overloaded indexer/event accessors, bodyless selector, and malformed-attribute rows | A same-source preferred address fails body-key/role validation; a carried target selects by order; stale or cross-reader identity resolves another MethodDef; custom-modifier drift is accepted; getter/setter or adder/remover roles swap; a persisted key does not round-trip or a legacy missing key guesses; a fresh selector bypasses carried-key canonicalization; explicit extension selection accepts a helper or loses its real declaring type; bodyless candidates disappear from current-source ordinal counting; exact/carried/selector classification/state/outcome/render/diagnostic differ after resolving the same MethodDef; or missing/ambiguous correspondence becomes plausible output |
| Resolved bodyless lifecycle | Decompiler + `RenderStyleConfigTests.NoBodyMethod_ProducesResultWithoutOutput_SoNoStyledSource` + `FidelityCauseSectionTests.BuildInspection_DistinguishesNoBodyFromImporterFailure` + `AnnotatedSourceDocumentProjectionTests.BodylessMemberDocumentFailureKeepsSiblingProjection` + whole-member/type + Body Shape, including abstract methods and property/indexer/event accessors | Exact/carried/selector bodyless results differ; an abstract body-backed request is filtered to N/A before projection; typed body is not `Absent` with its existing absence diagnostic; CLI/Research loses its standard visible absence failure, emits C# body output, marks style consumed, or changes Fidelity Causes from `Absent`; a marker or modifier appears; whole-member is not `Complete` declaration-only text; whole-type is not diagnostic-free declaration-only; a compact property/event form hides a typed accessor state; or Body Shape counts it as inspected/incomplete/failure |
| Resolved classification-failure lifecycle | Decompiler projector plus CLI, Research, public typed body, whole-member/type, and Body Shape over concrete and abstract ordinary methods and property/indexer/event accessors with a malformed async-attribute constructor/type reference | The resolved address or stable `DEC0015` diagnostic is lost; the expected metadata decode failure escapes or is called `AddressFailed`; exact/carried/ordinary-selector differ; an abstract or compact-syntax path filters before classification; import or a pass runs; a classification, modifier, outcome, declaration, or plausible body appears; diagnostics are mutable/shared; CLI/Research is success-shaped or marks style consumed; Fidelity Causes is not `Failed`; typed/whole-member production does not fail visibly; whole-type presents a decompiled member; or Body Shape increments `MethodsInspected` |
| Import observation totality | Decompiler projector tests with clean, nonfatal DEC0004/DEC0005, DEC0001 crash-function, and null-import seams | A non-null function is not `Imported`; import-time diagnostics change after a pass; `HasInternalError` is inferred from later diagnostics; or a null import has a stage |
| Importer-crash cross-surface preservation | Existing `CommandExecutionTests.Member_SelectedOverload_SelectFidelityCauses_ImporterCrashIsFailed` plus CLI Decompiled Source/style latch, public typed body, whole-member/type, and Body Shape rows over one malformed classic-classified fixture | The crash marker/DEC0001 disappears; CLI output ceases to be a successful body render or style consumption changes; Fidelity Causes ceases to be `Failed`; `ProduceBody` ceases to be `Complete`; whole-member ceases to fail; whole-type loses the marker body; Body Shape increments `MethodsInspected`; the stage is not `Unavailable`; a classic outcome/marker is added; or metadata `async` is omitted |
| Complete classic application replay | Decompiler pass/projector tests with accepted interface-fact + declined diagnostic fixtures | A second prepared stage recognizes again; supplied replay differs in body, local state, type facts, diagnostics, provenance/fidelity, modifier state, or outcome; an application is absent; or a supplied decision applies to a different host |
| Exact classic companion identity | Decompiler resolver/pass tests with implicit and explicit `IAsyncStateMachine.MoveNext` implementations, decoy `MoveNext` overloads before/after, wrong-signature/name lookalikes, duplicate/malformed relationships, and metadata-order swaps | The pass synthesizes/imports by name or ordinal; ignores complete signature or interface/MethodImpl evidence; selects the decoy; changes selection with metadata order; imports across modules; fails to retain the exact address as foreign host identity; or malformed/missing/ambiguous evidence reconstructs instead of visibly declining |
| Support-method application replay | Decompiler pass/projector tests over exact `MoveNext` and `SetStateMachine` MethodDefs for all five classic builders, `AsyncIteratorMethodBuilder`, and a custom builder at Raised and Lowered, plus `PassContext.None`/`CSharpBodyDiff` rows | Generate and supplied replay differ in body, local table, outcome, or acknowledgment; either stage recognizes again; an acknowledgment mutation occurs outside `ClassicAsyncApplication`; a supplied decision applies to another support host; a seam-free support host lacks its local decided outcome/application; in-domain classic `MoveNext` is hollowed; classic `SetStateMachine` cannot retain its typed empty-support application; async-iterator `MoveNext` is un-hollowed; a custom-builder support body is changed; or support handling changes the classic kickoff raise set |
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
| Raised/Lowered Research contract | Catalog-derived set-equality specimens for every `StyleOptionCatalog.Options.Where(ByteDivergent)` entry, including pass-based conditional/branchless rewrites and printer-local `prefer-long-literal-suffix`, through Annotated Source and Source Document | Stage snapshots recompute the outcome; unsupported stage preparation falls back to raw unmarked kickoff; caller options relabel the prepared altitude; the specimen set differs from the catalog; Lowered observes any byte-divergent option, records a `StyleLens` decision, changes pre-lens output, or loses interleaved IL; Raised changes output without one typed decision or fails to apply a matching request; or a new catalog entry lacks both altitude rows |
| Fact Row source-line identity | Direct Research + CLI Facts | C# lines are mapped against a separately raised function |
| Address/classification/body/import/stage/render union | Decompiler + CLI + Research + whole-type + Body Shape accounting | `AddressFailed`, `ClassificationFailed`, `Bodyless`, null `ImportFailed`, imported diagnostics, `Unavailable`, and stage-local failure states collapse; classification or null-import failure counts as inspected; DEC0001 import counts as inspected; nonfatal import does not; an unavailable or pre-decision failure has a success-shaped outcome; or post-decision stage/render failure drops its own captured outcome |
| Declined runtime-async fixture | CLI declarations + public typed body + whole-member/whole-type | Loses metadata `async` |
| Debug class narrow handoff | Decompiler library | Correlated `StoreLocal(NewObject(SM::.ctor))` prevents recognition |
| Async-void narrow handoff | Decompiler library | Terminal `Return(null)` prevents recognition |
| Non-generic ValueTask / async-void legacy raise negative | Decompiler library | Slice 0 newly reconstructs it |
| Exact legacy raise population | Decompiler pass tests derived from the current accepted fixture set, with close Debug-class, async-void, async local/lambda, custom-builder, and lookalike negatives | Structured recognition changes which fixtures reconstruct in slice 0, or a new `TryBuild*`/eligibility path escapes set equality |
| Typed body carriers | `MemberBodyProducer` tests | Outcome/classification is lost in `ProduceBody` or between `DecompileBody` and declaration formatting, `ClassificationFailed` becomes an unclassified declaration, or `Bodyless` becomes `ImportFailed` or a failed stage |
| Body Shape source projection | Decompiler product tests with classification failure, bodyless, null import, imported DEC0001, imported nonfatal diagnostic, and post-import stage/render-failure fixtures | Search creates a second classic decision, retains the broad classic-or-async-iterator heuristic, records `ClassificationFailed` or `Bodyless` as inspected, records `Bodyless` as incomplete/failure, counts null import or imported DEC0001 as inspected, skips an imported nonfatal diagnostic, or fails to count a later stage/render failure as inspected |
| Physical C# body diff boundary | `CSharpBodyDiff` product tests over kickoff, ordinary, classic support, and async-iterator support MethodDefs | Diff wires `importMethodBody`, admits foreign MethodDef origins, gives a kickoff any classic outcome/marker, drops or invents the local support acknowledgment/application, or changes unrelated lines/offsets |
| `DecompilerResult` value semantics | Decompiler tests | Results differing only by outcome/reason/decline disposition/support acknowledgment compare equal, hash inconsistently, or lose outcome through `with` |
| In-domain `MoveNext` of a declined classic-async SM | Decompiler library (CLI type surface omits `d__` types) | Distinctive user logic absent |
| Async-iterator `MoveNext` | Decompiler library | No longer hollow or lacks a replayable support-method application |
| Whole-type listing of `AsyncFixtures` | `MemberBodyProducer` | `NoAwait` still spelled `async` over the stub without the marker |
| `ClassicAsyncWithoutAwait_UsesResolvedMethodBodyModifier` | Existing valid-token row plus stale-MVID carried-key row and close same-name overload/accessor negatives | Either positive still expects `async Task NoAwait()`; exact and carried rows disagree after resolving the same MethodDef; or a stale target borrows another member's modifier/outcome |
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
fail the separate embedding architecture inventory. Treating a raw
token as an exact address, removing MVID or body-key/role validation,
restoring name/ordinal as carried-member fallback, or selecting one of
duplicate structural-key candidates must fail the body-target identity
gates.
Removing fresh-selector canonicalization must fail its current-source
row, not silently retain a direct printer branch. Allowing any cataloged
byte-divergent option to reach a Lowered prepared snapshot must fail set
equality plus output shape, decision absence, and retained interleaved
IL. A Raised printer-local rewrite without a typed decision must fail the
corresponding specimen and Research IL-suppression contract.
Moving support-method body or local mutation out of
`ClassicAsyncApplication` must fail Generated-versus-Supplied snapshot
equality for both stages. Replacing exact host identity with a kickoff-
only or name-only check must fail cross-host support replay. Removing
exact interface/MethodImpl companion resolution or restoring synthesized
name/ordinal `MoveNext` import must fail decoy-overload and metadata-order
rows. Removing
the resolved classification-failure arm, catching broadly, importing
after expected classification failure, or adding a consumer-side
classification catch must fail the malformed-metadata and ownership
gates.
Collapsing `Bodyless` into `ImportFailed`, a failed stage, `NotClassic`,
or a consumer-side RVA/`IsAbstract` precheck must fail
exact/carried/selector bodyless parity and all three surface
dispositions. Adding a consumer-side body-status or semantic abstract
filter must also fail `DeclaredSourceBodyStatusUsesProjector`, even when
its current render happens to match.

`DeclaredSourceBodyStatusUsesProjector` is an exact reviewed source
manifest over `ILInspector.Decompiler`, `ILInspector.Research`, and
`dotnet-inspect`, following the existing
`DynamicCompilationSiteInventoryTests` file/count/reason pattern. It
counts every access to MethodDef RVA, modeled `HasBody`, the named
body-status helpers, and every `ApiMember.IsAbstract` branch that can
suppress or choose presentation for a declared source method/accessor.
It excludes declaration-modifier rendering, aggregate counts,
data-member declarations, and field RVA. The implementation pins each
occurrence by file and containing member, not only a project-wide total.

These current policy sites must disappear:

| Migration site | Current occurrences | Replacement |
| --- | ---: | --- |
| `MemberCodeProvider.Collect` | 1 | Projector state and import observation |
| `MemberBodyProducer.ProduceBody` | 1 | Projector state |
| `MemberBodyProducer.ComposeMembers` method branch | 1 | Projector state before declaration syntax |
| `MemberBodyProducer.ComposeProperty` | 1 | Per-accessor projector state |
| `MemberBodyProducer.ComposeEvent` | 3 | Per-accessor projector state before compact syntax |
| `BodyShapeSearch.SearchCore` | 1 | Projector state and import observation |
| `ApiOutputFormatter.ResolveBodyMethods` | 2 | Project candidates before section-specific bodyless mapping |

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

The expanded current manifest has 39 occurrences: the prior 34 plus
five semantic abstract-policy branches. Ten policy occurrences migrate
to the projector and one projector body-status read is added, so the
expected post-migration total remains 30 pinned occurrences
(`39 - 10 + 1`). Any new, missing, or moved occurrence fails set
equality and requires an explicit ownership decision and reason. The
gate therefore catches a new consumer-side `Bodyless` or semantic
abstract filter anywhere in those product projects without rejecting
retained declaration facts, physical IL, analysis, provenance, or
Original Source evidence.
