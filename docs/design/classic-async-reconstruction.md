# Durable classic-async reconstruction

> **Owner:** `ILInspector.Decompiler`.
>
> **Owning document:** this document.
>
> **Map:** [Decompiler design](../decompiler.md) is the pipeline entry.
> [Raise-work discipline](../decompiler-raise-discipline.md) defines the
> evidence required for an implemented raise.

## Status and decision

Design. Tracking: [#4472](https://github.com/richlander/dotnet-inspect/issues/4472).

This document owns one focused component: the Decompiler inverse for classic
(`runtime-async=off`) async state machines. It consumes owner-issued method
addresses, async classification, and state-machine relationships; it owns the
classic machine model, planning, stage application, nested-function isolation,
and honest decline.

[#4684](https://github.com/richlander/dotnet-inspect/issues/4684) made this
design independent of Implementation Diff. The focused-design recovery in
[#4705](https://github.com/richlander/dotnet-inspect/issues/4705) removed
cross-owner contracts that accumulated during review. Those contracts are now
tracked by their owners:

- [#4669](https://github.com/richlander/dotnet-inspect/issues/4669) owns
  Metadata state-machine relationship facts and remains the implementation
  prerequisite for slice 0. This design does not claim that issue.
- [#4716](https://github.com/richlander/dotnet-inspect/issues/4716) owns
  physical C# body projection, render equivalence, and correspondence proof.
- [#4717](https://github.com/richlander/dotnet-inspect/issues/4717) owns the
  Research member-projection lifecycle.
- [#4718](https://github.com/richlander/dotnet-inspect/issues/4718) owns CLI
  adoption of typed member-projection outcomes.
- [#4719](https://github.com/richlander/dotnet-inspect/issues/4719) owns
  DecompilerHarness structural-review replay.
- Reconstruction correctness measurement remains separately owned as required
  by #4684.

No product behavior changes in this PR. The current
`ClassicAsyncReconstructionPass` remains fixture-shaped.

## Claim and non-claims

The component claims:

- one Decompiler-owned classic planning operation over exact owner-issued
  identities and imported bodies;
- one stage-neutral `ClassicAsyncDecision` applied independently to Raised and
  Lowered snapshots without re-recognition or mutable aliasing;
- exact preservation of the existing pass ordering and accepted raise
  population in honesty slice 0;
- visible decline for every healthy classic kickoff that is not reconstructed:
  exact handoff replacement when the kickoff is proven narrow, and
  statement-preserving marking otherwise;
- no-edit treatment of exact support methods;
- fresh classic state for every foreign-function pipeline; and
- typed outcome and declaration disposition for immediate Decompiler
  consumers.

The component does **not** claim:

- Metadata classification, relationship construction, selector replay, or
  body-address resolution;
- physical C# diffing, render-equivalence keys, source correspondence, or
  serialized trust;
- Research facts, Fact Rows, evidence coordinates, or render-stage requests;
- CLI sections, wording, verbosity, or structured output;
- harness orchestration, compile-back, behavioral validation, or correctness
  measurement;
- runtime-async or iterator reconstruction; or
- a new accepted classic raise before its separately owned correctness
  measurement exists.

## Demo

The motivating method is:

```csharp
public static async Task<int> CallsSyncSiblingFromAsync(int value)
{
    await Task.Yield();
    return ReadValue(value);
}
```

Today the declared method is printed as an `async` method whose body is still
the compiler kickoff:

```csharp
public static async Task<int> CallsSyncSiblingFromAsync(int value)
{
    __CallsSyncSiblingFromAsync_d__2 V_0 = default;
    V_0.___t__builder = AsyncTaskMethodBuilder<int>.Create();
    V_0.value = value;
    V_0.___1__state = -1;
    V_0.___t__builder.Start<__CallsSyncSiblingFromAsync_d__2>(ref V_0);
    return V_0.___t__builder.Task;
}
```

Slice 0 does not pretend to reconstruct that method. The focused component
produces an honest body result and a typed declaration disposition:

```text
Outcome                 Declined(UnrecognizedAwaiterProtocol,
                                 ReplacedNarrowHandoff)
DeclarationDisposition  OmitAsync
Body                     /* Unsupported classic async state machine:
                             unrecognized await protocol. */
```

For a non-narrow or hand-written kickoff, slice 0 prepends the marker and keeps
every original statement. For an already accepted legacy shape, the existing
raised body remains unchanged. A declaration consumer may compose that body
with `OmitAsync`, but declaration rendering belongs to its own owner. A
neighboring async-iterator or runtime-async method never enters this inverse.

## Why a structured inverse is required

A classic async method has two physical bodies:

```text
kickoff MethodDef     Create builder, copy state, Start<T>, return Task
MoveNext MethodDef    user control flow, await protocol, SetResult/SetException
```

The declaration names the kickoff, but the user body lives in `MoveNext`.
Printing the kickoff under an `async` modifier is plausible-looking but false.
The current pass also recognizes support methods with broad shape heuristics and
can replace a `MoveNext` body with `return;`, removing physical evidence when
reconstruction did not supply an equivalent user body.

Adding another `TryBuild*` matcher does not solve the architecture. The current
single-await matcher, for example, cannot represent a void `GetResult`
statement followed by unrelated statements and a valued `SetResult`. It also
cannot prove that every local-address use maps safely into the declaration
host. Durable reconstruction needs a structured machine value, an explicit
consumption ledger, and a detached plan.

## Immediate typed boundary

This component consumes existing owner-issued types; the names below describe
roles at the boundary and do not redefine their construction or validation.

```text
ClassicAsyncRequest
  RequestedHost           owner-issued guarded method identity (#4669)
  HostRole                DeclaredKickoff | Execution | Support | Ordinary
  Classification         owner-issued async classification result
  Relationship           owner-issued state-machine relationship result (#4669)
  HostBody                pristine Decompiler import snapshot

ClassicAsyncPreparationResult
  NotApplicable(ClassificationKind)
  InputUnavailable(OwnerFailure)
  ImportFailed(MethodRole, Diagnostics)
  PlanningFailed(Diagnostics)
  Decided(ClassicAsyncDecision)

ClassicAsyncDecision
  Reconstruct(ClassicAsyncPlan, IncludeAsync)
  Decline(ClassicAsyncDeclineReason, KickoffDisposition, OmitAsync)

KickoffDisposition
  ReplacedNarrowHandoff
  PreservedOriginal

ClassicAsyncStageResult
  Applied(Stage, Snapshot, ClassicAsyncOutcome,
          ClassicAsyncDeclarationDisposition)
  NotApplicable(Stage, Snapshot, NoOpinion)
  Failed(Stage, Diagnostics, ClassicAsyncDecision?, NoOpinion)

ClassicAsyncOutcome
  Reconstructed
  Declined(ClassicAsyncDeclineReason, KickoffDisposition)

ClassicAsyncDeclarationDisposition
  IncludeAsync
  OmitAsync
  NoOpinion
```

`Classification` and `Relationship` are opaque owner results. The classic
component never scans custom attributes, interface implementations,
`MethodImpl`, names, overload order, or metadata tables to recreate them.

The boundary is total:

- ordinary, runtime-async, and iterator classifications are `NotApplicable`;
- execution and support hosts are `NotApplicable` and retain their pristine
  snapshots;
- a complete negative owner relationship such as missing, malformed,
  cross-kind, or ambiguous becomes a named healthy decline only for a
  classic declared kickoff;
- owner acquisition, budget, or decode failure is preserved as
  `InputUnavailable` with its original typed reason;
- failure to import an exact owner-selected kickoff, execution, or support
  MethodDef is `ImportFailed` with the role and diagnostics;
- an internal planning failure is `PlanningFailed`, never a plausible decline;
- every healthy classic input produces exactly one decision.

The adapter that invokes this component decides how its own larger lifecycle
represents `NotApplicable`, owner failure, or import failure. This design does
not define that caller's result union.

## Exact identity and import

Every request carries the guarded identity of the method named by `HostRole`.
For a declared kickoff, a successful #4669 relationship result supplies guarded
execution and support identities. For an execution or support request, the
related declared kickoff remains part of that opaque owner result rather than
replacing `RequestedHost`. Decompiler imports only owner-selected identities and
stamps each imported function with its host identity.

`MetadataMethodAddress` remains the durable MVID-plus-MethodDef projection. It
is not cryptographic identity and is insufficient by itself when byte-distinct
modules share an MVID. The classic component consumes #4669's reader- or
acquisition-bound guard in addition to that projection; it does not strengthen
Metadata address semantics locally.

The component must not:

- search by generated type or method name;
- select `MoveNext`, `SetStateMachine`, or another support method by overload
  position;
- treat token, MVID-plus-row, or name equality without the owner guard as
  identity;
- infer a relationship from kickoff IR when the owner result is absent; or
- fall back from a present invalid exact address to a name or selector lookup.

Kickoff IR may confirm that the handoff uses the state-machine identity supplied
by Metadata. Disagreement produces a typed decline or input failure; it never
selects another candidate.

## `ClassicAsyncMachine`

Planning produces a detached semantic value:

```text
ClassicAsyncMachine
  DeclaredMethod
  ExecutionMethod
  StateMachineType
  BuilderKind
  StateStorage
  BuilderStorage
  CurrentState
  ExitStates
  AwaitPoints[]
  Completion
  ExceptionCompletion
  HoistedValues[]
  UserRegions[]
  SupportMethods[]

ClassicAsyncPlan
  Machine
  ReconstructedBody
  ConsumedRegions
  PreservedRegions
  UserRegionRealizations[]

UserRegionRealization
  UserRegion
  PrimaryOutputNode
  ContextOutputNodes[]
```

Guarded host identities, their durable address projections, and relationship
roles remain typed. `ReconstructedBody` is a stage-neutral Decompiler plan, not
an `IrNode` graph borrowed from a planning snapshot. `ConsumedRegions` and
`PreservedRegions` form a disjoint, complete partition of the physical kickoff
and execution regions considered by an accepted plan. No consumed region has
an unmodeled external entry or use. The partition is the proof that every
physical region represented by the reconstructed body was intentionally
handled and that no preserved region was silently rewritten.

Physical ownership and semantic realization are separate proofs. Every
`UserRegion` in an accepted machine has exactly one
`UserRegionRealization`. Its `PrimaryOutputNode` emits that region's semantic
effect exactly once; optional context nodes may carry non-effectful structure
but cannot duplicate it. Every primary output node identifies its contributing
user region. A region cannot be marked consumed merely to satisfy the physical
partition: omission, duplicate realization, or realization from preserved
physical material invalidates the plan.

An await point records:

- state before suspension and state after resumption;
- awaiter storage;
- awaited operand;
- `IsCompleted`, suspension, and `GetResult` roles;
- whether `GetResult` is valued or void; and
- the exact user region that receives the result.

Void `GetResult` is a statement. It is never forced into a value merely because
the current matcher expects a valued `SetResult`.

## Planning and stage application

`ClassicAsyncReconstructionPass` remains the single classic recognizer and
application owner. Slice 0 separates its work into two phases:

1. **Plan once.** Run the existing complete
   `ForReconstruction<ClassicAsyncReconstructionPass>()` prerequisite sequence
   over detached kickoff and execution snapshots. Recognition produces one
   immutable `ClassicAsyncDecision`.
2. **Apply per stage.** Raised and Lowered each clone their own stage snapshot
   and materialize the same decision. Application does not rescan metadata,
   reimport bodies, rerun recognition, or retain nodes/locals from another
   stage.

`IrPasses.Default`, `IrPasses.Lowered`, and the order of every pass before and
after the classic pass remain unchanged. The planning sequence is derived from
the existing reconstruction sequence rather than copied into a second manual
list.

Request order is irrelevant. Preparing Lowered before Raised or Raised before
Lowered yields value-equal decisions and stage-appropriate independent
snapshots. Mutating or rendering one snapshot cannot alter the decision,
another stage, or a later render.

Planning failures do not become declines. A decline means the classic input was
healthy and the component intentionally refused reconstruction for a named
reason.

## Inverse domain

The forward transform is Roslyn's classic
`AsyncMethodToStateMachineRewriter`. The honesty domain includes
compiler-produced struct and class state machines using:

- `AsyncTaskMethodBuilder`
- `AsyncTaskMethodBuilder<TResult>`
- `AsyncValueTaskMethodBuilder`
- `AsyncValueTaskMethodBuilder<TResult>`
- `AsyncVoidMethodBuilder`

The accepted raise population in slice 0 remains the current Release-style
struct population. Debug class state machines, custom classic builders, and
recognized-but-unsupported await protocols receive honest declines. Runtime
async and async iterators are not classic inputs.

The component does not widen `IsAsyncMethodBuilder` or use that helper as a
relationship classifier. Doing so would silently expand legacy raise
eligibility.

## Honest decline

Every healthy classic decision is either `Reconstruct` or `Decline`.

Complete negative relationship outcomes and Decompiler recognition outcomes
produce decline reasons such as:

- `NoExecutionMethod`
- `AmbiguousExecutionMethod`
- `MalformedRelationship`
- `KickoffMachineMismatch`
- `UnsupportedBuilder`
- `UnrecognizedAwaiterProtocol`
- `UnconsumedExecutionRegion`
- `UnmappedLocalAddress`
- `ClassStateMachine`
- `NonNarrowKickoffHandoff`

The exact names may evolve with implementation, but every reason is a stable
typed value and every healthy non-reconstruction names one.

### Narrow kickoff ownership

A kickoff is narrow only when every statement is correlated to one exact
state-machine instance and belongs to the compiler handoff:

- optional class-state-machine allocation;
- builder creation;
- initial state assignment;
- copies of `this` and user parameters into corresponding fields;
- `Start<TStateMachine>` over the same instance;
- the matching Task/ValueTask return, or terminal return for async void; and
- no unexplained call, store, branch, or return.

Required statements occur exactly once. Optional parameter copies are matched
to source parameters, not accepted by field-name resemblance. A local,
address-of local, or field use that cannot be mapped to the same machine makes
the kickoff non-narrow.

For a declined narrow kickoff, application replaces the handoff with one
`UnsupportedNode` carrying `ReplacedNarrowHandoff`. For a non-narrow kickoff,
application inserts the marker before an unchanged copy of the original
statements and carries `PreservedOriginal`.

The marker is not a success diagnostic. It is visible source evidence that the
component declined. The existing fidelity-cause projection may associate a
stable Decompiler cause such as DEC0004, but this component does not define how
CLI or Research displays it.

### Declaration disposition

A reconstructed decision and its successful stage result carry `IncludeAsync`.
A declined decision and its successful stage result carry `OmitAsync`, because
the visible body is not an async user body. `NotApplicable`, owner failure,
import failure, planning failure, and failed stage application carry
`NoOpinion`; callers retain their own typed lifecycle and must not invent a
classic outcome.

The disposition is an immediate Decompiler output. It does not prescribe
section visibility, formatting, or structured-output fields.

The value cannot stop at internal stage application. It flows through the
existing Decompiler-owned public body boundary:

```text
ClassicAsyncStageResult
  -> DecompilerResult.ClassicAsyncDeclarationDisposition
  -> MemberBodyProductionResult.ClassicAsyncDeclarationDisposition
  -> Decompiler-owned RequiresAsyncBodyModifier body fact
```

`IncludeAsync` resolves the body fact to true, `OmitAsync` resolves it to false,
and `NoOpinion` preserves the existing non-classic metadata/pipeline result.
Both the direct per-member and whole-type `MemberBodyProducer` paths consume
that same result. Neither may rederive a decided classic modifier from
`TypeShellProducer.RequiresAsyncBodyModifier`.

`ILInspector.CSharp` remains the declaration-spelling owner. It consumes the
resolved Decompiler body fact exactly as it does today; this design neither
changes its modifier grammar nor defines presentation behavior.

## Support methods are no-edit

An owner-issued `Execution` or `Support` host role is `NotApplicable`. The
classic component returns the pristine stage snapshot and performs no body or
local edit.

This removes the destructive legacy behavior that can hollow recognized
`MoveNext` or `SetStateMachine` methods. It also prevents broad builder-name
heuristics from editing iterator, custom-builder, runtime-async, or unrelated
methods. The classic component does not discover support relationships, emit a
support-specific product outcome, or infer a role from a field named
`<>t__builder`.

## Foreign functions and nesting

Every separately imported local function, lambda, iterator body, or state
machine runs through `PassContext.RunForeignFunctionPipeline` with a fresh
classic context. The parent decision, plan, application, outcome, and
declaration disposition are cleared even when pass stepping is disabled.

A foreign function may independently receive an owner-issued classification
and relationship and make its own decision. Parent addresses or outcomes never
stand in for that input.

`NestedFunctionEmbeddingPolicy` consumes the foreign Decompiler result. Slice 0
does not embed a local function or lambda when:

- its body needs an async declaration carrier the current node model cannot
  represent;
- it carries an unsupported classic marker;
- its import or stage result failed; or
- embedding would discard its typed classic outcome.

Lambda and local-function raising share this policy. The classic component does
not define Research facts or source coordinates for nested evidence.

## Failure and value semantics

All decisions, plans, applications, outcomes, decline reasons, body
dispositions, declaration dispositions, and support identities are immutable
values. Equality, hashing, cloning, and `with`-style copies include every field
that changes behavior.

Expected owner or import failures are result values. Unexpected programmer
errors remain failures; they are not caught and translated into `Decline`.

The existing importer-crash marker remains owned by the general Decompiler
pipeline. A DEC0001 import failure cannot acquire a classic decision, marker,
or reconstructed declaration disposition.

## Slice plan

### Slice 0: model and honesty

Blocked on #4669 implementation.

- consume exact owner-issued classification and relationships;
- introduce the immutable machine, decision, plan, application, outcome, and
  declaration-disposition values;
- plan once and apply independently to Raised and Lowered;
- preserve the existing accepted raise population;
- mark every healthy declined kickoff honestly;
- stop editing exact support bodies;
- isolate foreign-function decisions; and
- record corpus A/B evidence for the support-body and marker changes.

### Slice 1: void await, then statements

Blocked on the separately owned correctness measurement required by #4684.

- model a void `GetResult` statement;
- preserve subsequent statements and valued completion;
- map hoisted user parameters through exact machine storage;
- reject any unproven local-address mapping; and
- reconstruct the motivating `Task.Yield()` witness plus close negatives.

### Later slices

Each later slice names one compiler lowering family and its close negatives.
General state dispatch, exception/finally interaction, multiple awaits, custom
awaiters, Debug class reconstruction, and additional builders are separate
raises. A slice does not broaden this design's owner or absorb its measurement
consumer.

## Gates

Every asserted property below names its enforcing gate. Tests run in Release.

| Gate | Evidence | Fails when |
| --- | --- | --- |
| Focused owner boundary | `ClassicAsyncArchitectureTests` source inventory | Classic code scans Metadata relationships, imports by name/ordinal, defines physical diff/Research/CLI/harness policy, or references #4716–#4719 implementation types |
| Owner-result preservation | Metadata fixture adapters over every #4669 result arm | A complete negative relationship loses its typed reason before decline; an acquisition/budget/decode failure becomes decline; or either becomes empty success or a guessed candidate |
| Exact host identity | Kickoff/execution/support fixtures with same-name, same-token, and byte-distinct same-MVID/same-row decoys | Planning or application drops the owner guard, changes the requested host role, or uses another acquired module or MethodDef |
| Planning totality | Healthy classic, non-classic, owner-failure, import-failure, and injected planner-failure fixtures | A request has no terminal result; failure becomes decline; or classic health is inferred from rendered text |
| Stage-neutral plan | Raised/Lowered fixtures in both request orders | Recognition runs twice; decisions differ by request order; a plan retains stage-owned nodes/locals; or stage snapshots alias |
| Registered pipeline preservation | Independent exact pass-list baselines plus accepted classic fixtures | The registered `IrPasses.Default` or `IrPasses.Lowered` sequence or required relative order changes without an intentional baseline update |
| Planning-sequence derivation | Set/order equality against `ForReconstruction<ClassicAsyncReconstructionPass>()` | Planning uses a copied list, omits a registered prerequisite, includes the requesting/application pass, or changes order |
| Legacy population equality | Existing accepted classic fixture set plus close negatives | Slice 0 adds or removes an accepted reconstruction |
| Plan-region partition | Accepted compiler fixtures plus injected extra-region, external-entry, duplicate-consumption, overlap, and unconsumed-use negatives | A physical kickoff/execution region is neither consumed nor preserved, appears in both sets, is consumed twice, has an unmodeled entry/use, or is rewritten while preserved |
| User-region realization | Accepted side-effect/call/store/return fixtures plus omitted-region, duplicate-primary, effectful-context, and preserved-material negatives | A user region has no realization or more than one; a primary output effect appears twice; a context node emits the effect; or preserved physical material supplies reconstructed semantics |
| Decline honesty | Narrow and non-narrow classic kickoff fixtures for every decline category | A healthy decline lacks a marker/reason; a narrow handoff survives; a non-narrow statement changes or disappears; or a failure fabricates a marker-only success |
| Narrow ownership non-vacuity | One-machine positives plus mixed-local, extra-call/store/return, duplicate-step, and unmapped-address negatives | Shape resemblance establishes ownership without complete correlation |
| Support preservation | Classic, runtime, iterator, custom-builder, and unrelated support-like methods | An Execution/Support host is not `NotApplicable`, broad builder-name recognition edits a method, or any support body/local changes |
| Declaration disposition | Reconstructed, declined, not-applicable, owner/import/planner failure matrix | Reconstructed omits `IncludeAsync`, declined does not return `OmitAsync`, or a non-decision invents classic modifier policy |
| Declaration disposition wiring | `ClassicAsyncDeclarationDispositionFlowsThroughDecompilerBodyResults` over direct member and whole-type production | `DecompilerResult` or `MemberBodyProductionResult` drops the value; either body path rederives a decided classic modifier from Metadata; or the final Decompiler body fact disagrees |
| Foreign-context isolation | Nested local/lambda/iterator/classic fixtures with pass stepping on and off | Parent classic state reaches a foreign pipeline or a foreign host borrows parent identity |
| Embedding honesty | Await-bearing/await-free local and lambda fixtures plus failure/marker negatives | A foreign result needing an async carrier, carrying unsupported output, or failing is embedded as plausible synchronous source |
| Value semantics | Equality/hash/clone tests for every decision and result arm | A behavior-affecting field is omitted or a mutable plan is shared |
| Support and marker rendering | Exact `ClassicAsyncSupportBodiesRemainPhysical` and `DeclinedClassicKickoffsRenderExpectedMarkers` fixtures | A support body remains hollowed, a marker/disposition is absent, narrow replacement preserves handoff statements, or non-narrow marking changes an original statement |
| Classic corpus population A/B | `classic-state-machines` base/head snapshots plus `ClassicAsyncCorpusDeltaTests` over the emitted delta | The source-kickoff population moves, the accepted set changes, or expected fidelity/residual movement is absent |

The source-inventory gate derives its expected product files and allowed
owner-issued boundary types from the component declaration, so missing and
stale entries both fail. Registered pipeline preservation uses an independent
exact baseline; planning-sequence derivation separately proves that the
reconstruction sequence is a filtered projection of the registered list.

The legacy-population gate is the slice-0 safety property. A later accepted
raise changes its expected set only in that raise's separately reviewed slice
and only after the named correctness measurement is available.

The classic corpus gate uses the existing Release fixture and profile. In an
isolated worktree at the exact effective base, build and emit both the
population snapshot and body-sensitive render baseline:

```bash
dotnet build src/ILInspector.Decompiler.Fixtures.ClassicStateMachines -c Release
dotnet run --project tools/DecompilerHarness -c Release -- \
  artifacts/bin/ILInspector.Decompiler.Fixtures.ClassicStateMachines/release/ILInspector.Decompiler.Fixtures.ClassicStateMachines.dll \
  --corpus-profile classic-state-machines \
  --emit-corpus-baseline /tmp/pr4473-classic-base.json \
  --max-examples 10

dotnet run --project tools/DecompilerHarness -c Release -- \
  artifacts/bin/ILInspector.Decompiler.Fixtures.ClassicStateMachines/release/ILInspector.Decompiler.Fixtures.ClassicStateMachines.dll \
  --emit-render-ab /tmp/pr4473-classic-render-base.json \
  --sequential
```

Then run the corresponding commands from an isolated worktree at the exact
candidate head:

```bash
dotnet build src/ILInspector.Decompiler.Fixtures.ClassicStateMachines -c Release
dotnet run --project tools/DecompilerHarness -c Release -- \
  artifacts/bin/ILInspector.Decompiler.Fixtures.ClassicStateMachines/release/ILInspector.Decompiler.Fixtures.ClassicStateMachines.dll \
  --corpus-profile classic-state-machines \
  --diff-corpus-baseline /tmp/pr4473-classic-base.json \
  --emit-corpus-delta /tmp/pr4473-classic-delta.json \
  --max-examples 10

dotnet run --project tools/DecompilerHarness -c Release -- \
  artifacts/bin/ILInspector.Decompiler.Fixtures.ClassicStateMachines/release/ILInspector.Decompiler.Fixtures.ClassicStateMachines.dll \
  --render-ab /tmp/pr4473-classic-render-base.json \
  --sequential \
  --max-examples 100
```

`ClassicAsyncCorpusDeltaTests` reads the emitted method rows and enforces the
population/fidelity/residual categories; those rows contain no body text and
are never cited as support-body proof. The Render A/B report is the complete
body-sensitive changed-method evidence. Record its exact base/head SHAs and
paste every changed classic method, not only selected examples. The targeted
support/marker render tests enforce exact expected bodies; any additional
Render A/B method is unexplained movement and blocks the slice.

## Implementation order

1. Land #4669 and consume its owner-issued relationship result without a
   temporary Decompiler scanner.
2. Add the focused value model and total planning boundary.
3. Split planning from stage application while preserving pass order.
4. Implement honest decline and no-edit support-host handling.
5. Isolate foreign-function state and gate embedding.
6. Run the focused Release gates and pinned corpus A/B.
7. Only then begin slice 1 under its correctness prerequisite.

Physical C# diff, Research, CLI, and harness work may proceed independently in
issues #4716–#4719. None is a prerequisite for this design to converge, and
none enters the ordinary classic reconstruction path.
