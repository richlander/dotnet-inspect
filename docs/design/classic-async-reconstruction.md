# Classic async inverse core

> **Owner:** the classic async inverse in `ILInspector.Decompiler`.
>
> **Owning document:** `docs/design/classic-async-reconstruction.md`.
>
> **Map:** [Decompiler design](../decompiler.md) owns pipeline composition.
> [Raise-work discipline](../decompiler-raise-discipline.md) owns the evidence
> required for an implemented raise.

## Status and decision

Design. Tracking:
[#5276](https://github.com/richlander/dotnet-inspect/issues/5276), advancing
[#4472](https://github.com/richlander/dotnet-inspect/issues/4472).

The classic async inverse core accepts one authenticated classic request with
unmodified import snapshots of the kickoff and execution IL present in the
inspected artifact. It produces either a healthy decline, a visible planning
failure, or one immutable reconstruction plan. A successful plan proves three
independent obligations:

1. every physical region in scope has one explicit disposition;
2. every input semantic effect has one output realization; and
3. every consumed semantic node has a complete modeled path through its
   structured ancestors.

This is a reconstruction proof, not a general control-flow proof. Container
CFG facts may support a recipe, but they do not establish arbitrary CLR
control-flow or exception-flow soundness.

PR #4473 introduced this owning document and the initial durable-inverse
direction. This corrective successor retains its Decompiler ownership,
owner-issued relationship boundary, and requirement for explicit physical and
semantic accounting. Review of implementation PR #5002 then showed that #4473
had also swept request attachment, stage application, nested-function
embedding, and result projection into one lifecycle. PR #5002 was superseded,
and this revision replaces those parts of #4473's contract with one re-derived
inverse-core boundary. The adjacent work is tracked independently:

- [#5277](https://github.com/richlander/dotnet-inspect/issues/5277) owns how
  Metadata relationship evidence reaches the Decompiler request boundary.
- [#5278](https://github.com/richlander/dotnet-inspect/issues/5278) owns
  foreign-body embedding decisions for local functions and lambdas.
- [#5279](https://github.com/richlander/dotnet-inspect/issues/5279) owns direct
  member and whole-type projection of typed body results.
- [#5292](https://github.com/richlander/dotnet-inspect/issues/5292) owns stage
  application and no-edit treatment of exact execution and support bodies.
- [#5293](https://github.com/richlander/dotnet-inspect/issues/5293) owns the
  declaration disposition produced from classic decisions and pre-decision
  outcomes.

All six efforts are unstacked and target `main`.

## Claim and non-claims

The core claims one thing: a `Reconstruct` result is licensed only by a closed
recipe proof over the exact request bodies. Shape resemblance, descendant
discovery, a clean-looking output tree, or agreement between two incomplete
control-flow models cannot license reconstruction.

The core does **not** own or claim:

- Metadata classification or state-machine relationship construction;
- request attachment, body acquisition, or importer policy;
- mutation of Raised or Lowered stage snapshots;
- declaration-modifier policy or honest-decline presentation (#5293);
- local-function or lambda embedding;
- direct member, whole-type, Research, CLI, or harness projection;
- general CFG, CLR exception-flow, or behavioral-equivalence soundness;
- runtime-async or iterator reconstruction; or
- a new accepted classic recipe family.

## Demo

Consider a supported recipe whose result store is nested under a condition:

```text
if (Probe())
{
    result = await WorkAsync();
}
SetResult(result);
```

A descendant search can find `result = await WorkAsync()` and synthesize:

```csharp
return await WorkAsync();
```

That output erases `Probe()` and changes conditional execution into
unconditional execution. A physical-region ledger over importer output alone
can miss the defect: the store's IL region can be marked consumed while the
control dependency that a derived view structures as `if` remains
unclassified.

The inverse core instead requires this decision:

```text
Decline(
  UnmodeledStructuredAncestor,
  consumed: result-store,
  ancestor: if (Probe()))
```

For the neighboring lowered shape obtained by compiling the positive test
fixture, with no semantic guard:

```text
try
{
    result = await WorkAsync();
}
catch (Exception error)
{
    builder.SetException(error);
    return;
}
builder.SetResult(result);
```

the recipe may reconstruct only after it proves that the enclosing completion
shell is compiler protocol, that the await/result path is realized exactly
once, and that every other effect is either protocol-owned or represented.

## Immediate boundary

The request adapter supplies an authenticated value with these roles:

```text
ClassicInverseRequest
  DeclaredMethod       guarded owner-issued identity
  ExecutionMethod      guarded owner-issued identity
  Relationship         resolved owner-issued classic relationship certificate
  KickoffBody          unmodified import snapshot bound to DeclaredMethod
  ExecutionBody        unmodified import snapshot bound to ExecutionMethod
```

The names describe roles, not a required implementation shape. The core treats
the relationship, its role dispositions, and the guards as opaque owner
evidence. It neither recreates them from names or IR nor selects replacement
identities. A missing, rejected, or filtered owner result never becomes a core
request; the
[request adapter](classic-async-request-adapter.md) preserves those result
arms. A resolved
relationship may record an absent classic support role under the
Metadata-owned
[relationship contract](state-machine-relationship-index.md#evidence-carrying-certificate);
the inverse requires the certified kickoff and execution identities, not a
support MethodDef.

### Body availability and post-build artifacts

`Unmodified` describes pipeline state, not provenance. The snapshots contain
the IL found in the inspected artifact before a Decompiler raising pass edits
it. They are not promised to be the original compiler bodies. A linker,
instrumenter, obfuscator, or other post-build tool may already have rewritten
them.

Trimmed assemblies are not categorically excluded. The adapter can form a core
request when the selected kickoff and execution MethodDefs both retain
importable IL and Metadata still supplies a successful authenticated
relationship. The core then proves a recipe against that post-trim IL. If a
post-build transform changes the lowering shell, the current recipe declines;
a later recipe may support a stable transformed shell when the retained IL
still carries enough evidence to satisfy every proof obligation.

A request cannot be formed from the inspected artifact when:

- trimming removes the kickoff, state-machine type, or execution MethodDef;
- a required MethodDef fails Metadata's managed-IL-body predicate because it
  has RVA zero, is P/Invoke, uses a non-IL code type, is unmanaged, is
  runtime-implemented, or is an internal call; or
- Metadata rejects the relationship because identity evidence is missing,
  ambiguous, malformed, or contradictory.

Artifact category alone never decides availability. Abstract members and
bodyless interface declarations ordinarily have RVA zero, but a default
interface method may carry ordinary managed IL and may be a valid classic
kickoff. SDK-produced reference assemblies may retain authenticated classic
relationships and replace every related body with synthesized `ldnull; throw`
IL. Those body-replacing assemblies reach the core and decline because the
replacement bodies do not satisfy a classic recipe. A stripped targeting-pack,
metadata-only, or native artifact forms no request only when its actual
MethodDefs or relationship evidence fail the boundary above.

The inverse core cannot repair these unavailable inputs. When a method or its
body is absent, the current artifact no longer contains the executable
operations, evaluation order, and exception structure that a reconstruction
proof must account for. An absent `SetStateMachine` support MethodDef does not
remove the retained kickoff or execution operations and therefore does not
block a request when Metadata certifies the relationship with
`AbsentFromArtifact`. The inverse cannot infer that identity or disposition
locally. Supplying an authenticated pre-trim assembly or another body source
would require a separate acquisition and request-adapter contract.

### Recipe demonstration matrix

The existing compiler-produced `ClassicAsync` fixture family provides the
recipe-level inputs that the proof-carrying core will consume:

| Case | Fixture method | Current observable outcome |
| --- | --- | --- |
| Neighboring accepted recipe | `TwoSequentialAwaits` | `Full`; both awaits and their ordering are reconstructed. |
| Effect nested in a conditional result | `AwaitConditionalWithWrappedResult` | `Partial` with visible `DEC0004`; the kickoff remains. |
| Effectful await operand inside a loop | `AwaitInLoopWithWrappedOperand` | `Partial` with visible `DEC0004`; the kickoff remains. |
| Store nested in loop control | `LoopWithAccumulatorWrite` | `Partial` with visible `DEC0004`; the kickoff remains. |
| Guarded effect in `finally` | `AwaitInTryFinallyWithGuardedCall` | `Partial` with visible `DEC0004`; the kickoff remains. |

`ClassicAsyncReconstructionHonestyTests` gates those current outcomes. A
single-method demonstration uses the same built assembly and product pipeline:

```bash
dotnet build fixtures/decompiler/ILInspector.Decompiler.Fixtures.ClassicAsync -c Release
dotnet run --project tools/DecompilerHarness -c Release -- \
  artifacts/bin/ILInspector.Decompiler.Fixtures.ClassicAsync/release/ILInspector.Decompiler.Fixtures.ClassicAsync.dll \
  --dump \
  'ILInspector.Decompiler.Fixtures.ClassicAsync.AsyncFixtures::LoopWithAccumulatorWrite' \
  --lowered --remarks
```

The broader `ClassicStateMachines` fixture and
`--corpus-profile classic-state-machines` process retain the neighboring
builder, exception, iterator, and async-iterator population. These fixtures
make the cases repeatable; their current honesty outcomes do not by themselves
prove the new physical, semantic, or structured-ancestor ledgers.

### Artifact demonstration matrix

`ILInspector.Decompiler.Fixtures.ClassicAsyncArtifacts` compiles one classic
async source population into implementation and SDK reference assemblies.
`ClassicAsyncArtifactMatrixTests` additionally publishes that project twice
with full trimming: once under ordinary reachability and once with the fixture
assembly rooted so every classic role remains available.

The resulting matrix separates artifact availability from reconstruction:

| Artifact | Observed method evidence | Relationship/core boundary |
| --- | --- | --- |
| Implementation | Kickoff, `MoveNext`, and `SetStateMachine` retain compiler IL. | Authenticated request; the neighboring accepted recipe reconstructs. |
| SDK reference | The same MethodDefs retain synthesized `ldnull; throw` bodies. | Authenticated request; `ClassicInverseBodyReplacingReferenceAssembliesDecline` remains the core gate. |
| Ordinary trim, reachable method | Kickoff and `MoveNext` remain, but ILLink removes `SetStateMachine`. | Metadata authenticates the relationship with `SetStateMachine: AbsentFromArtifact`; #5277 must form the request so the inverse can prove or decline the retained bodies. |
| Ordinary trim, unused method | The kickoff and generated state machine are removed. | No core request forms. |
| Role-preserved trim | All required MethodDefs retain post-trim IL. | Authenticated request; the accepted recipe reconstructs from the trimmed artifact. |
| Default-interface implementation | The kickoff and execution MethodDefs carry managed IL. | Authenticated request without a declaring-type category exclusion. |

Run the slow Release fixture gate to build and prove all artifacts:

```bash
dotnet run --project src/ILInspector.Decompiler.Tests -c Release -- \
  -class '*ClassicAsyncArtifactMatrixTests*'
```

The cross-platform publish recipe is owned by
`eng/classic-async-artifact-matrix.proj`; the test invokes that project with
the current host RID rather than assembling a test-local compiler command.
The publish outputs remain under
`artifacts/classic-async-artifact-matrix/<host-rid>/`. They can be passed
directly to `DecompilerHarness --dump` for the PR demo. These fixture gates
prove the stated artifact premises; they do not substitute for the unverified
inverse-core decision gates below.

Before #5277 supplies the authenticated request boundary, the legacy
`ClassicAsyncReconstructionPass` still reports `Full` for both trimmed
variants: it discovers the generated execution sibling directly and therefore
bypasses Metadata's certificate in the ordinary-trim case. That measured
behavior is not yet a valid core request. #5277 must attach the resolved
relationship and explicit support-role disposition rather than preserve that
sibling inference. The eventual end-to-end demo must reconstruct both
retained-body trim variants through authenticated requests, while the unused
and body-replacing cases continue to decline:

```csharp
int left = await first;
int right = await second;
GC.KeepAlive((left, right));
```

The terminal result is:

```text
ClassicInverseDecision
  Reconstruct(Plan)
  Decline(Reason)
  Failed(Failure)
```

`Decline` means the request is healthy but outside the proven recipe domain.
`Failed` means planning could not produce a trustworthy decision, including
invalid request/body correlation, core-owned resource exhaustion, or an
internal planning failure. A failure never becomes a decline or an empty plan.
Unexpected programmer errors remain errors rather than being translated into
a success-shaped result.

The decision and plan are immutable values detached from mutable input trees.
They may refer to owner-issued identities and stable input receipts, but never
retain an `IrNode`, local, parent link, or stage-owned collection.

### Derived planning views

The boundary snapshots are importer output, before raising passes create
conditions, loops, exception structure, and other recipe-level constructs. The
core may derive structured planning views from detached clones using
Decompiler-owned prerequisite passes. These are proof views, not caller-owned
Raised or Lowered stage snapshots.

Every receipt issued from a derived view maps unambiguously back to the
physical region in the unmodified import snapshot that it classifies. Derived
structure cannot replace the physical partition or manufacture semantic
identity. If a candidate's import correspondence is missing or ambiguous, the
recipe declines.

## Proof-carrying plan

A reconstruction plan contains the proposed user-body value and the proof
receipts that license it:

```text
ClassicInversePlan
  Recipe
  ReconstructedBody
  PhysicalPartition
  SemanticRealizations
  StructuredAncestorReceipts
```

These are three separate checks. Passing one never implies another.

### Physical partition

The physical scope is fixed by the complete unmodified import snapshots of the
kickoff and execution bodies. A nested function body is an opaque physical node
in this scope; its descendants belong to a separate body request and cannot be
searched or claimed by the outer recipe. The physical regions form a disjoint,
complete partition:

- **protocol** regions are authenticated lowering scaffolding;
- **semantic** regions contribute to reconstructed user behavior; and
- **preserved** regions remain uninterpreted and cannot contribute to the
  reconstructed body.

Every in-scope region has exactly one disposition. Regions do not overlap, and
no child is independently claimed after an ancestor receipt already owns its
subtree. An accepted plan has no unexplained region, external entry, external
use, or cross-region storage alias that can change a claimed region's meaning.

`Preserved` is a proof disposition, not permission to ignore evidence. A
preserved region must be positively proven semantically inert for the declared
method. If it contributes behavior represented by the output, it is semantic,
not preserved.

### Semantic realizations

The recipe inventories every operation or value whose execution, omission,
duplication, order, or value can affect observable behavior. Calls count
regardless of whether they appear as statements, conditions, operands,
initializers, or filters. Stores are not assumed to be the only effects. An
operation is inert only when the recipe positively proves it; absence from an
effect list is not a purity proof.

Every input semantic effect has exactly one primary output realization. Context
nodes may carry ordering or structure but may not duplicate the effect.
Conversely, every output effect cites the input effect or authenticated
protocol fact that licenses it. No output effect is synthesized from display
text, field names, block order, or an unrelated preserved region.

The realization relation preserves:

- evaluation order and multiplicity;
- value and storage identity;
- conditions that control whether the effect executes;
- loops that control how often the effect executes;
- exception and finally context; and
- return, throw, break, continue, and leave ownership.

### Structured-ancestor receipts

For each consumed semantic node, the plan records the uninterrupted parent path
from that node to its recipe root. Every ancestor on the path is classified as:

- **reproduced**, with the corresponding output context;
- **protocol**, under an exact lowering-shell rule whose semantics are
  accounted for elsewhere in the recipe; or
- **transparent**, under a recipe rule proving that removing the wrapper
  changes neither execution, ordering, value, nor exception behavior.

An ancestor with a condition, loop, catch, filter, finally, short-circuit,
structured transfer, or exception boundary cannot be transparent. If any
ancestor is unknown or has no receipt, the recipe declines.

Descendant traversal may discover a candidate. It cannot authorize consuming
that candidate. Authorization comes only from the complete ancestor path and
the physical and semantic receipts above.

## Recipe contract

Each accepted recipe is a closed inverse of one named compiler lowering shell.
It declares:

- source construct, compiler, configuration, and supported machine form;
- exact kickoff, await, completion, and exception-protocol roles;
- recipe roots and their allowed structured paths;
- protocol regions and positive inertness rules;
- semantic-effect inventory and realization relation;
- control-flow, storage, and identity obligations; and
- close negative shapes that must decline.

The recipe proves the whole request or produces no plan. Independently matching
an await, completion call, store, loop, or catch is insufficient. A matcher may
share facts with another recipe, but acceptance cannot be assembled from
partial successes whose combined ownership was never checked.

The current accepted recipe population is the compatibility floor. This design
does not add a recipe family. Migrating an existing accepted recipe may reduce
the accepted set only where the old result cannot satisfy this contract; that
change is an honesty correction and must be measured explicitly. Adding a new
recipe requires a separate raise contract and evidence under
[Raise-work discipline](../decompiler-raise-discipline.md).

## Bounded role of CFG evidence

Container-local CFG evidence can prove bounded facts such as:

- whether a claimed region has an external predecessor;
- exact successor identity and multiplicity within the modeled container;
- whether a protocol exit is the sole modeled exit; and
- whether two recipe-local views describe the same modeled edge.

It cannot by itself prove:

- arbitrary CLR exception dispatch or filter behavior;
- semantics of calls, volatile access, type initialization, or memory effects;
- equivalence between a structured ancestor and its proposed output;
- ownership across nested function bodies; or
- whole-program behavioral equivalence.

The recipe remains responsible for every semantic and structured obligation.
An unavailable or insufficient CFG fact causes decline; it never widens the
accepted domain.

## Domain and failure semantics

The domain is Roslyn classic (`runtime-async=off`) async lowering admitted by
the authenticated request boundary. Runtime async, async iterators, custom
relationships, and name-inferred state machines are outside the core.

Planning is deterministic and side-effect free over the request. Repeating it
over value-equal requests produces value-equal decisions. Request order,
rendering, stage selection, or mutation of a caller-owned clone cannot change
the decision.

Resource limits are part of the result algebra. Exhausting a core-owned
traversal, node, structured-view, or receipt budget is `Failed`; it cannot
yield a partial proof. Metadata relationship-budget exhaustion is an
owner-issued rejection and never enters this core. Unsupported but healthy
lowering shapes are `Decline`. The distinction is preserved so callers can
route failure without pretending the inverse made a semantic judgment.

## Validation status and implementation gates

This docs-only design changes no product behavior. Its reconstruction
properties are **unverified** until #5276's implementation runs the following
Release gates:

| Required gate | Must fail when |
| --- | --- |
| `ClassicInversePlanPartitionsPhysicalRegions` | A region is missing, overlaps another region, or has an unexplained entry, use, or alias. |
| `ClassicInversePlanRealizesEverySemanticEffectExactlyOnce` | An input effect is omitted or duplicated, an output effect lacks an input receipt, or preserved material supplies reconstructed semantics. |
| `ClassicInversePlanRequiresCompleteStructuredAncestorPaths` | A consumed node has an unknown ancestor or loses a condition, loop, exception context, or structured transfer. |
| `ClassicInverseSideEffectsInExpressionsDeclineWithoutRealization` | A call or other effect in a condition, operand, initializer, or filter is omitted because it is not an expression statement. |
| `ClassicInverseNestedStoresDoNotEscapeTheirControlContext` | A sequential or loop store nested under structured control is emitted unconditionally. |
| `ClassicInverseStructuredViewsRetainImportCorrespondence` | A derived planning node issues a receipt without unambiguous correspondence to its unmodified imported physical region. |
| `ClassicInverseBodyReplacingReferenceAssembliesDecline` | A direct core request built from an authenticated SDK reference-assembly relationship with synthesized bodies does not decline. |
| `ClassicInverseDefaultInterfaceBodiesUseMethodEvidence` | A direct core request built from authenticated managed-IL default-interface evidence is rejected solely because its declaring type is an interface. |
| `ClassicInverseDecisionIsDetachedAndDeterministic` | A plan retains mutable IR, aliases a request body, or changes with request order or caller mutation. |
| `ClassicInversePlanningFailuresRemainFailures` | Invalid correlation or core-owned budget exhaustion becomes decline, reconstruction, or empty success. |
| `ClassicInverseAcceptedPopulationIsMeasured` | The implementation changes the accepted compiler-fixture population without an explicit expected delta and per-method review. |

The first five gates need compiler-produced positives plus synthetic close
negatives. The
[exact-head PR #5002 reproduction reconciliation](https://github.com/richlander/dotnet-inspect/pull/5002#issuecomment-5469908350)
records the effectful-condition and nested sequential/loop-store evidence. It
demonstrates the defect class; it is not itself an implementation gate.

An implementation PR also owes the decompiler entry gate, IR invariants,
changed-method Render A/B, structural review, validity, compile-back fidelity
where supported, and the accepted-population delta required by
[Decompiler correctness pipeline](../decompiler-correctness-pipeline.md).

## Implementation sequence

1. Introduce the detached decision, plan, and receipt values at the core
   boundary without changing the accepted population.
2. Make one existing compiler-produced recipe issue all three proof ledgers.
3. Add the effectful-expression and structured-ancestor negatives.
4. Migrate each remaining accepted recipe through the same boundary, measuring
   every acceptance loss as an honesty correction.
5. Only after every accepted recipe uses the proof boundary may downstream
   owners consume `Reconstruct`. The independent #5277 adapter forms requests;
   #5278, #5279, #5292, and #5293 consume typed decisions or results without
   redefining this contract.
