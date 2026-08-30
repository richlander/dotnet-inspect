# Durable classic-async reconstruction

> **Owner:** `ILInspector.Decompiler`.
>
> **Owning document:** this document.
>
> **Map:** [Decompiler design](../decompiler.md) is the pipeline entry.
> [Raise-work discipline](../decompiler-raise-discipline.md) defines the
> evidence required for an implemented raise.

## Status and decision

Implementation in progress. Tracking:
[#4472](https://github.com/richlander/dotnet-inspect/issues/4472).

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
  Metadata state-machine relationship facts. This component consumes those
  owner-issued facts and does not claim their construction.
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

The focused component is being implemented incrementally in
`ILInspector.Decompiler`. Each behavior change is gated by compiler-produced
positives, close declines, exact region accounting, and the evidence described
below.

## Claim and non-claims

The component claims:

- one Decompiler-owned classic planning operation over exact owner-issued
  identities and imported bodies;
- one stage-neutral `ClassicAsyncDecision` applied independently to Raised and
  Lowered snapshots without re-recognition or mutable aliasing;
- exact preservation of the existing pass ordering and bounded accepted raise
  population;
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

The focused component does not pretend to reconstruct that method. It
produces an honest body result and a typed declaration disposition:

```text
Outcome                 Declined(UnrecognizedAwaiterProtocol,
                                 ReplacedNarrowHandoff)
DeclarationDisposition  OmitAsync
Body                     /* Unsupported classic async state machine:
                             unrecognized await protocol. */
```

For a non-narrow or hand-written kickoff, the component prepends the marker and
keeps every original statement. For an accepted shape, reconstruction proceeds
only when every modeled physical and semantic region is realized. A
declaration consumer may compose a declined body with `OmitAsync`, but
declaration rendering belongs to its own owner. A neighboring async-iterator
or runtime-async method never enters this inverse.

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
  cross-kind, or ambiguous becomes a named healthy decline only when its
  owner-issued rejected claims contain the exact requested kickoff address
  paired with `ClassicAsync`; another kickoff's classic claim in the same
  rejection component cannot classify this kickoff;
- `BudgetExceeded` is always preserved as `InputUnavailable` with its original
  typed reason, even when the failure also carries an exact classic claim;
- owner acquisition or decode failure without a complete claim is likewise
  preserved as `InputUnavailable`;
- failure to import an exact owner-selected kickoff, execution, or support
  MethodDef is `ImportFailed` with the role and diagnostics;
- an internal planning failure is `PlanningFailed`, never a plausible decline;
- every healthy classic input produces exactly one decision.

The `Owner-result preservation` gate enforces the per-kickoff association and
budget precedence.

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
selects another candidate. The kickoff local must carry the exact owner-selected
MVID and TypeDef row, not only the same metadata name. Every state, builder,
parameter, and receiver field used by the handoff must name that same exact
definition. The symbolic declaring type used for exact `MoveNext` import is the
validated owner-selected machine type; same-name provenance cannot substitute
for it.

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
  ParameterBindings[]
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

The physical census is input-derived from the canonical planning snapshots.
One slot is recorded for each direct statement child of every root-function
`Block`, including blocks nested directly under structured statements rather
than only blocks owned by a `BlockContainer`. Nested-function bodies remain
separate scopes. Each immutable slot identity contains the owner-issued method
address, the kickoff or execution host role, and a canonical child-index path.
Recipe matching retains exact node references only long enough to issue
ownership receipts; the persisted plan contains slot identities and flow facts,
never borrowed nodes.

`Cfg.Build` supplies bounded block-entry, successor, external-target, and
region-leave evidence for container-owned blocks. Directly structured blocks
receive the equivalent single-block census. A cross-container target is also
recorded as an external entry on its destination. A consumed slot with an
external entry or target, a region leave, no entry, or more than two entries or
successors invalidates the plan; this is a conservative description of the
currently modeled straight-line and conditional recipes, not a claim of
general CLR control-flow soundness. The `Plan-region partition` gate enforces
the census, receipt, and bounded-flow rules.

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

The first await-point realization gate covers the awaited operand itself.
Direct state-machine parameter fields are normalized to their declared kickoff
parameters, ordinary call operands retain the exact callee signature and
normalized arguments, and the loop recipe correlates the compiler's
array-element spill with the reconstructed `foreach` iteration variable. Each
input operand has exactly one same-position `AwaitExpression` operand
realization. Call identity includes the exact definition when available,
closed generic arguments, receiver and virtual-call shape, constraints,
resolution identity, custom modifiers, and normalized argument slot/dynamic
identity. An unrecognized input or output operand shape declines rather than
receiving a role-only receipt. Other operand expressions use a recursive typed
IR key over the node kind, shape-specific description, exact result/direct
types, and ordered children; this preserves already accepted constants,
conversions, element access, and other cloned operand forms instead of
narrowing reconstruction to calls over parameters. Await order is global
within the root function, and nested-function awaits cannot be silently counted
as part of that root realization. For a reused awaiter local, each `GetResult`
is paired with exactly one non-resume `GetAwaiter` store reaching the use
through the owning root-function `BlockContainer`. The bounded dataflow resets
at each same-local `GetResult`, joins predecessor definitions, and admits a
compiler resume load only when one same-field suspension spill is reached from
the same unique source definition. Exactly one non-resume source and at most
one authenticated resume definition may reach a `GetResult`; a diamond that
joins two resume stores declines even when both load the same spill field.
Missing, alternate, backedge-introduced, cross-container, unmodeled, or
nested-function definitions also decline. This is container-local structural
evidence for the supported compiler protocol, not a claim of generally sound
CLR reaching-definition or exception-flow semantics. Tree traversal order is
never reaching-definition evidence.

The source store, completion test, suspension callback, and result call form
one authenticated await protocol. `GetAwaiter` must be an instance member of
the awaited operand's exact type and return the exact awaiter local type.
`IsCompleted` must be the exact parameterless Boolean property getter over that
local, and its conditional branch must have CFG successors to both the
correlated suspension and result-use blocks. `GetResult` must be the exact
parameterless instance member over the same local and awaiter type. All three
members use supported external `MemberRef` provenance and exact signatures
without custom modifiers. A compatible foreign helper, disconnected completion
test, or name-only member declines. The `Await source uniqueness` gate enforces
the dataflow and complete protocol bounds.

The completion protocol is execution ownership, not preserved scaffolding.
Every exact-machine `SetResult`, `SetException`, `AwaitOnCompleted`, and
`AwaitUnsafeOnCompleted` statement in an accepted recipe is authenticated and
claimed. The selected final result callback, one exception callback, and one
suspension callback per accepted await point are the complete callback
inventory. An extra callback declines; it cannot enter the preserved physical
set because replacing the kickoff makes that physical execution unreachable.
The execution-side `<>t__builder` field must normalize to the same canonical
builder storage authenticated in the kickoff; an independently self-consistent
Task/ValueTask builder protocol cannot substitute.
Completion members obey the same external-core-library provenance and exact
custom-modifier boundary as kickoff members. Each suspension callback's closed
type arguments must equal its instantiated by-ref parameter element types; its
first pair must name the accepted await point's exact awaiter local and type,
and its second pair must name the exact closed state-machine type and actual
`this` argument. The generic definition placeholders must be exact as well.
The `Completion callback ownership` gate independently inventories the
compiler positives and exercises each correlation decline.

The first predicate realization gate covers the accepted conditional recipe.
Only a condition carrying the field-to-kickoff-parameter mapping required by
the accepted recipe enters the input inventory; compiler state and awaiter
branches do not. The matched branch sense is normalized to the source
predicate, then paired with the reconstructed conditional by global predicate
position and the same recursive typed expression identity. Compound and
guarded predicates remain declines until recipes model their complete control
and effect regions.

The first guarded-effect realization gate covers the accepted `try`/`finally`
recipe. The input effect must be one direct call under the exact compiler
finally-state guard; the guard itself remains protocol structure, not a user
predicate. That guard has no else arm, uses the unique local seeded from the
exact machine's `<>1__state` field, and has no user-derived reaching
assignment. A later state-local transition is accepted only when the next
statement writes the same constant or stack-slot value to the exact machine's
`<>1__state` field; stack-slot definitions must themselves be recognized and
cycle-free. The accepted recipe has exactly the initial field seed, suspension
state `0`, and resumption state `-1`, in that order. Extra or uncorrelated state
assignments do not authenticate the guard. The call is remapped to kickoff
parameters and paired with one call in the reconstructed `finally` by global
guarded-effect position and exact typed call identity. A nested user guard or
non-call effect remains a decline.

## Planning and stage application

`ClassicAsyncReconstructionPass` remains the single classic recognizer and
application owner. It separates its work into two phases:

1. **Plan once.** Run the existing complete
   registered prefix before `ClassicAsyncReconstructionPass` over a detached
   kickoff snapshot, and
   `ForReconstruction<ClassicAsyncReconstructionPass>()` over the detached
   execution snapshot. The distinct sequences are derived from
   `IrPasses.Default`: replaying passes registered after classic reconstruction
   over the kickoff would change the recognizer's historical input.
   Recognition produces one immutable `ClassicAsyncDecision`.
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

Preparation is cached by exact requested-host address on the live
`MetadataSource` acquisition that owns the reader and import lifetime. It is
not a static or cross-reader cache. The cache computes outside publication
locks, so nested or concurrent preparation cannot deadlock; duplicate
concurrent computations publish one value-equal decision. Canonical planning
uses a source-owned import context rather than inheriting the requesting
stage's optional capabilities or active nested-pipeline stack.

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

The accepted raise population remains the bounded Release-style struct
population covered by the current recipes. Debug class state machines, custom
classic builders, and recognized-but-unsupported await protocols receive
honest declines. Runtime async and async iterators are not classic inputs.

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
to source parameters, not accepted merely because their values are valid source
arguments. Planning issues one immutable field-to-argument map from the
validated stores. Each entry records the exact-machine field name and type plus
the source argument slot, name, type, and dynamic facts. Target fields and
source arguments are each unique, and a user-parameter field must receive its
corresponding declared parameter; swapped, duplicate, or foreign copies make
the kickoff non-narrow. A missing copy makes reconstruction decline when the
execution body requires that binding. Awaited operands, predicates, guarded
effects, and reconstructed calls all use this map rather than independently
matching fields by name. A local, address-of local, or field use that cannot be
mapped to the same machine makes the kickoff non-narrow. The map records
`<>4__this` for kickoff ownership, but the current recipes do not realize
instance receivers and therefore still decline when execution requires it.

The handoff order is part of ownership: canonical builder creation precedes
state initialization and every parameter/receiver copy, `Start` follows all of
them, and the Task/ValueTask or async-void return follows `Start`. This matters
because `Start` may invoke `MoveNext` synchronously. A statement set with valid
counts but a copy or state write after `Start` is non-narrow. Field, source
argument, and declared parameter types are compared after state-machine generic
normalization with exact definition provenance and recursive custom modifiers;
display-equivalent types cannot establish a binding. State initialization and
parameter copies are direct field stores and may retain either relative order;
their required boundary is before `Start`.

`Create`, `Start<TStateMachine>`, and the Task/ValueTask accessor are protocol
members, not name-shaped calls. Their static/instance and virtual shape,
declaring builder storage identity, return and parameter types, generic
definition shape, closed type arguments, and receiver/argument instantiation
must match the supported core-library builder contract exactly, including the
ordered required/optional custom modifiers and exact modifier-type identity at
every nested type position. The supported compiler handoff reaches those
members as external core-library `MemberRef`s, for which both the exact
definition address and acquisition guard are absent. A member carrying either
value is declined: this component has no owner-issued expected member identity
against which it could authenticate that definition, and same-module MVID/row
resemblance is not a substitute. A foreign same-signature member likewise
makes the handoff non-narrow; Decompiler neither resolves a replacement
relationship nor broadens the supported builder set. The storage type itself
must be one canonical unmodified core-library builder shape before it can
become the baseline for member comparisons; consistent custom modification of
the storage and all members does not authenticate a new protocol. The `Narrow
ownership non-vacuity` gate enforces this conservative member boundary.

When the decline is about the handoff itself and the kickoff is proven narrow,
application may replace it with one `UnsupportedNode` carrying
`ReplacedNarrowHandoff`. An execution-recipe decline preserves the original
kickoff even when the handoff is narrow: removing the call into an
unrecognized or incompletely realized `MoveNext` would remove the behavior the
decline is meant to keep visible. Non-narrow declines likewise insert the
marker before an unchanged copy of the original statements and carry
`PreservedOriginal`.

`UnsupportedResolvedClassic_PreservesKickoffAndNamesDecline`,
`NaturalUnmatchedShapePreservesKickoff`, and the compiled
`UnrealizedControlFlowRegionDeclinesAtPartialFidelity` cases gate this
reason-specific disposition.

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

`NestedFunctionEmbeddingPolicy` consumes the foreign Decompiler result. It
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

## Implementation state

### Model and honesty foundation

Implemented:

- consume exact owner-issued classification and relationships;
- introduce the immutable machine, decision, plan, application, outcome, and
  declaration-disposition values;
- plan once and apply independently to Raised and Lowered;
- preserve the existing accepted raise population;
- mark every healthy declined kickoff honestly;
- stop editing exact support bodies;
- isolate foreign-function decisions; and
- record corpus A/B evidence for the support-body and marker changes.

### Current semantic realization

Implemented:

- complete physical statement-slot partitioning;
- checked and unsigned arithmetic plus abrupt-control declines;
- exact awaited-operand realization; and
- exact predicate and guarded-effect realization for the accepted conditional
  and `try`/`finally` recipes.

Result receivers, returns and leaves, and general user calls and writes remain
unverified. Until each family has input-derived regions, exact output
realizations, compiler-produced positives, and close declines, its presence
prevents reconstruction.

For the current result recipes, the exact `GetResult` protocol call may be
surrounded only by already verified structural conversions and operators.
Any unverified invocation/effect node anywhere in the cloned result expression
declines, including ordinary calls, constructors, indirect calls,
local-function invocations, delegate creation, dynamic member access, and
raised construction/initializer forms. This includes sibling effects that do
not enclose `GetResult`. A field/property receiver rooted in the post-await
result also declines.
Calls in the separately inventoried awaited operand remain eligible; they
supply the await source and are not post-await result use. The `Result-use
boundary` gate covers compiler-produced enclosing and sibling construction,
synthetic enclosing and sibling invocation forms, and the accepted
direct/conversion/operator neighbors.

### New raises

Each new raise names one compiler lowering family and its close negatives.
General state dispatch, exception/finally interaction, multiple awaits, custom
awaiters, Debug class reconstruction, and additional builders are separate
raises. A raise does not broaden this design's owner or absorb its measurement
consumer.

## Gates

Every asserted property below names its enforcing gate. Tests run in Release.

| Gate | Evidence | Fails when |
| --- | --- | --- |
| Focused owner boundary | `ClassicAsyncArchitectureTests` source inventory | Classic code scans Metadata relationships, imports by name/ordinal, defines physical diff/Research/CLI/harness policy, or references #4716–#4719 implementation types |
| Owner-result preservation | Metadata fixture adapters over every #4669 result arm plus `StateMachineRelationshipIndex_PreservesClaimKindPerKickoff`, `StateMachineRelationshipIndex_BudgetFailureRetainsExactClaim`, `MixedRejectedClaimsClassifyEachKickoffExactly`, and `BudgetFailureWithClassicClaimRemainsInputFailure` | A merged rejection loses the exact kickoff/kind association; another kickoff's claim produces `RejectedRelationship`/`OmitAsync`; `BudgetExceeded` becomes decline despite exact claim evidence; or failure becomes empty success or a guessed candidate |
| Exact host identity | Kickoff/execution/support fixtures with same-name, same-token, and byte-distinct same-MVID/same-row decoys, plus `KickoffLocalWithoutExactDefinitionIdentityDeclines` | Planning or application drops the owner guard, changes the requested host role, accepts a same-name foreign machine local, or uses another acquired module or MethodDef |
| Planning totality | Healthy classic, non-classic, owner-failure, import-failure, and injected planner-failure fixtures | A request has no terminal result; failure becomes decline; or classic health is inferred from rendered text |
| Stage-neutral plan | Raised/Lowered fixtures in both request orders | Recognition runs twice; decisions differ by request order; a plan retains stage-owned nodes/locals; or stage snapshots alias |
| Registered pipeline preservation | Independent exact pass-list baselines plus accepted classic fixtures | The registered `IrPasses.Default` or `IrPasses.Lowered` sequence or required relative order changes without an intentional baseline update |
| Planning-sequence derivation | Set/order equality against the registered prefix before `ClassicAsyncReconstructionPass` for kickoff planning and `ForReconstruction<ClassicAsyncReconstructionPass>()` for execution planning | Planning uses a copied list, omits a registered prerequisite, includes the requesting/application pass, replays a post-classic pass over the kickoff, or changes order |
| Bounded population equality | `CompilerProducedAcceptedPopulationIsExact` over the complete healthy classic fixture kickoff inventory, plus close negatives | A semantic gate unintentionally adds or removes one of the 20 accepted compiler-produced reconstructions |
| Plan-region partition | Accepted compiler fixtures plus injected extra-region, external-entry, duplicate-consumption, overlap, and unconsumed-use negatives | A physical kickoff/execution region is neither consumed nor preserved, appears in both sets, is consumed twice, has an unmodeled entry/use, or is rewritten while preserved |
| User-region realization | `CheckedRegionHasOnePrimaryRealization`, `AwaitedOperandsHaveOnePrimaryRealization`, `PredicateRegionHasOnePrimaryRealization`, `GuardedEffectRegionHasOnePrimaryRealization`, and the `RegionLedgerRejects*` negatives | A modeled user region has no realization or more than one, its typed semantics or position changes, or preserved physical material supplies reconstructed semantics |
| Decline honesty | Narrow and non-narrow classic kickoff fixtures for every decline category | A healthy decline lacks a marker/reason; a narrow handoff survives; a non-narrow statement changes or disappears; or a failure fabricates a marker-only success |
| Narrow ownership non-vacuity | One-machine and `GenericContainingTypeAndMethodMapFieldTypeParameters` positives plus mixed-local, extra-call/store/return, duplicate-step, unmapped-address, `SwappedKickoffParameterCopiesDecline`, `NarrowKickoffRequiresProtocolOrder`, `ParameterBindingRequiresExactFieldType`, the foreign Create/Start/Task-accessor negatives, `BuilderStorageMustBeCanonical`, `SameExactTypeIncludesOrderedRecursiveCustomModifiers`, `CustomModifiedBuilderMemberMakesKickoffNonNarrow`, `ExactAddressedBuilderMemberMakesKickoffNonNarrow`, and `InconsistentBuilderMemberProvenanceMakesKickoffNonNarrow` | Shape resemblance, member names, reordered synchronous handoff, custom-modifier or provenance loss, unauthenticated definition coordinates, inconsistent provenance, or a set of valid source arguments establishes ownership without exact builder protocol and field-to-argument identity |
| Await source uniqueness | `AuthenticatedAwaitProtocolIsAccepted`, `AwaitProtocolRequiresExactCorrelatedMembers`, `CompletionBranchMustDefineAwaitCfgEdges`, `SequentialAwaiterLocalReuseHasUniqueReachingSources`, `CompetingAwaiterDefinitionsDecline`, `AwaitSourceReachingDefinitionsRejectBackedgeAfterUse`, and `AwaitSourceRejectsDiamondWithTwoResumeDefinitions` | A `GetResult` selects a source by tree order, accepts a missing/alternate/backedge/cross-container definition or multiple resume definitions, fails to authenticate the exact correlated `GetAwaiter`/terminating `IsCompleted`/suspension/`GetResult` protocol and one resume spill, or rejects ordinary sequential reuse |
| Result-use boundary | `PostAwaitResultReceiverCallDeclinesAtPartialFidelity`, `PostAwaitInvocationNodesAreUnverified`, `CompilerConstructorAfterAwaitDeclines`, and the direct/conversion cases in `FaithfulLegacyRecipeRemainsFullyReconstructed` | A post-await receiver or invocation/effect node is cloned without a region and realization, a call in the separately inventoried awaited operand is rejected, or a verified direct/conversion/operator result is rejected |
| Completion callback ownership | `AcceptedRecipesOwnEveryCompletionCallback`, `ExtraExactMachineSetResultIsRejected`, `CallbackBuilderMustMatchKickoffBuilder`, `AwaitCallbackRequiresExactAwaitPointCorrelation`, and `CompletionCallbackRequiresExactExternalMemberIdentity` | An accepted recipe leaves a completion/suspension callback preserved, accepts an extra callback, loses custom-modifier/member provenance, substitutes an execution builder not authenticated by the kickoff, mismatches an awaiter or machine argument/type, or derives the compiler-positive inventory from the same permissive matcher |
| Finally-state transition correlation | Compiler-produced `AwaitInTryFinally` positives plus `FinallyStateGuardRequiresExactMachineStateAndNoElse` | A guard accepts a foreign state field, else arm, user assignment, direct constant assignment, or uncoupled stack-slot transition |
| Support preservation | Classic, runtime, iterator, custom-builder, and unrelated support-like methods | An Execution/Support host is not `NotApplicable`, broad builder-name recognition edits a method, or any support body/local changes |
| Declaration disposition | Reconstructed, declined, not-applicable, owner/import/planner failure matrix | Reconstructed omits `IncludeAsync`, declined does not return `OmitAsync`, or a non-decision invents classic modifier policy |
| Declaration disposition wiring | `ClassicAsyncDeclarationDispositionFlowsThroughDecompilerBodyResults`, `WholeTypeUsesDecidedClassicAsyncDeclarationDisposition`, and `RejectedClassicClaimRendersAsNonAsyncTaskMethod` over direct member, whole-type, and CLI production | `DecompilerResult` or `MemberBodyProductionResult` drops the value; either body path rederives a decided classic modifier from Metadata; a rejected classic body renders `async`; or the final Decompiler body fact disagrees |
| Foreign-context isolation | Nested local/lambda/iterator/classic fixtures with pass stepping on and off | Parent classic state reaches a foreign pipeline or a foreign host borrows parent identity |
| Embedding honesty | Await-bearing/await-free local and lambda fixtures plus failure/marker negatives | A foreign result needing an async carrier, carrying unsupported output, or failing is embedded as plausible synchronous source |
| Value semantics | Equality/hash/clone tests for every decision and result arm | A behavior-affecting field is omitted or a mutable plan is shared |
| Support and marker rendering | Exact `ClassicAsyncSupportBodiesRemainPhysical` and `DeclinedClassicKickoffsRenderExpectedMarkers` fixtures | A support body remains hollowed, a marker/disposition is absent, narrow replacement preserves handoff statements, or non-narrow marking changes an original statement |
| Classic corpus population A/B | `classic-state-machines` exact-base/head snapshots plus Render A/B; automated delta enforcement is unverified | The source-kickoff population moves, the accepted set changes unexpectedly, or any rendered method remains unclassified |

The source-inventory gate derives its expected product files and allowed
owner-issued boundary types from the component declaration, so missing and
stale entries both fail. Registered pipeline preservation uses an independent
exact baseline; planning-sequence derivation separately proves that the
reconstruction sequence is a filtered projection of the registered list.

The bounded-population gate is the compatibility property. A new accepted
raise changes its expected set only in that raise's separately reviewed change
and only after the named correctness measurement is available.

The classic corpus gate uses the existing Release fixture and profile. In an
isolated worktree at the exact effective base, build and emit both the
population snapshot and body-sensitive render baseline:

```bash
dotnet build src/ILInspector.Decompiler.Fixtures.ClassicStateMachines -c Release
dotnet run --project tools/DecompilerHarness -c Release -- \
  artifacts/bin/ILInspector.Decompiler.Fixtures.ClassicStateMachines/release/ILInspector.Decompiler.Fixtures.ClassicStateMachines.dll \
  --corpus-profile classic-state-machines \
  --emit-corpus-baseline /tmp/classic-base.json \
  --max-examples 10

dotnet run --project tools/DecompilerHarness -c Release -- \
  artifacts/bin/ILInspector.Decompiler.Fixtures.ClassicStateMachines/release/ILInspector.Decompiler.Fixtures.ClassicStateMachines.dll \
  --emit-render-ab /tmp/classic-render-base.json \
  --sequential
```

Then run the corresponding commands from an isolated worktree at the exact
candidate head:

```bash
dotnet build src/ILInspector.Decompiler.Fixtures.ClassicStateMachines -c Release
dotnet run --project tools/DecompilerHarness -c Release -- \
  artifacts/bin/ILInspector.Decompiler.Fixtures.ClassicStateMachines/release/ILInspector.Decompiler.Fixtures.ClassicStateMachines.dll \
  --corpus-profile classic-state-machines \
  --diff-corpus-baseline /tmp/classic-base.json \
  --emit-corpus-delta /tmp/classic-delta.json \
  --max-examples 10

dotnet run --project tools/DecompilerHarness -c Release -- \
  artifacts/bin/ILInspector.Decompiler.Fixtures.ClassicStateMachines/release/ILInspector.Decompiler.Fixtures.ClassicStateMachines.dll \
  --render-ab /tmp/classic-render-base.json \
  --sequential \
  --max-examples 100
```

The emitted method rows contain no body text and are never cited as
support-body proof. Automated delta enforcement remains unverified. The Render
A/B report is the complete body-sensitive changed-method evidence. Record its
exact base/head SHAs and classify every changed classic method, not only
selected examples. The targeted support/marker render tests enforce exact
expected bodies; any additional Render A/B method is unexplained movement and
blocks the change.

## Implementation order

1. Consume #4669's owner-issued relationship result without a temporary
   Decompiler scanner.
2. Add the focused value model and total planning boundary.
3. Split planning from stage application while preserving pass order.
4. Implement honest decline and no-edit support-host handling.
5. Isolate foreign-function state and gate embedding.
6. Establish complete physical ownership.
7. Add semantic families incrementally, with exact realization and declines.
8. Run the focused Release gates and pinned corpus A/B before review.

Physical C# diff, Research, CLI, and harness work may proceed independently in
issues #4716–#4719. None is a prerequisite for this design to converge, and
none enters the ordinary classic reconstruction path.
