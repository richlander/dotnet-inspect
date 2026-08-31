# Classic async inverse core

> **Owner:** the classic async inverse in `ILInspector.Decompiler`.
>
> **Owning document:** this document.
>
> **Map:** [Decompiler design](../decompiler.md) owns pipeline composition.
> [Raise-work discipline](../decompiler-raise-discipline.md) owns the evidence
> required for an implemented raise.

## Status and decision

Design. Tracking:
[#5276](https://github.com/richlander/dotnet-inspect/issues/5276), advancing
[#4472](https://github.com/richlander/dotnet-inspect/issues/4472).

The classic async inverse core accepts one authenticated classic request with
pristine kickoff and execution bodies. It produces either a healthy decline, a
visible planning failure, or one immutable reconstruction plan. A successful
plan proves three independent obligations:

1. every physical region in scope has one explicit disposition;
2. every input semantic effect has one output realization; and
3. every consumed semantic node has a complete modeled path through its
   structured ancestors.

This is a reconstruction proof, not a general control-flow proof. Container
CFG facts may support a recipe, but they do not establish arbitrary CLR
control-flow or exception-flow soundness.

The previous design swept request attachment, planning, stage application,
nested-function embedding, and result projection into one lifecycle. Review of
PR #5002 showed that those owners change independently, so that PR was
superseded. This document re-derives only the inverse core. The adjacent work is
tracked independently:

- [#5277](https://github.com/richlander/dotnet-inspect/issues/5277) owns how
  Metadata relationship evidence reaches the Decompiler request boundary.
- [#5278](https://github.com/richlander/dotnet-inspect/issues/5278) owns
  foreign-body embedding decisions for local functions and lambdas.
- [#5279](https://github.com/richlander/dotnet-inspect/issues/5279) owns direct
  member and whole-type projection of typed body results.

All four efforts are unstacked and target `main`.

## Claim and non-claims

The core claims one thing: a `Reconstruct` result is licensed only by a closed
recipe proof over the exact request bodies. Shape resemblance, descendant
discovery, a clean-looking output tree, or agreement between two incomplete
control-flow models cannot license reconstruction.

The core does **not** own or claim:

- Metadata classification or state-machine relationship construction;
- request attachment, body acquisition, or importer policy;
- mutation of Raised or Lowered stage snapshots;
- declaration-modifier policy or honest-decline presentation;
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
unconditional execution. A physical-region ledger alone can miss the defect:
the store can be marked consumed while its `if` ancestor remains preserved.

The inverse core instead requires this decision:

```text
Decline(
  UnmodeledStructuredAncestor,
  consumed: result-store,
  ancestor: if (Probe()))
```

For the neighboring compiler-produced shape with no semantic guard:

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
  Relationship         successful owner-issued classic relationship
  KickoffBody          pristine imported body bound to DeclaredMethod
  ExecutionBody        pristine imported body bound to ExecutionMethod
```

The names describe roles, not a required implementation shape. The core treats
the relationship and guards as opaque owner evidence. It neither recreates
them from names or IR nor selects replacement identities. A missing, rejected,
or filtered owner result never becomes a core request; preserving those result
arms is #5277's responsibility.

The terminal result is:

```text
ClassicInverseDecision
  Reconstruct(Plan)
  Decline(Reason)
  Failed(Failure)
```

`Decline` means the request is healthy but outside the proven recipe domain.
`Failed` means planning could not produce a trustworthy decision, including
invalid request/body correlation, resource exhaustion, or an internal
planning failure. A failure never becomes a decline or an empty plan.
Unexpected programmer errors remain errors rather than being translated into a
success-shaped result.

The decision and plan are immutable values detached from mutable input trees.
They may refer to owner-issued identities and stable input receipts, but never
retain an `IrNode`, local, parent link, or stage-owned collection.

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

The physical scope is fixed by the complete pristine kickoff and execution
bodies. A nested function body is an opaque physical node in this scope; its
descendants belong to a separate body request and cannot be searched or claimed
by the outer recipe. The physical regions form a disjoint, complete partition:

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

Resource limits are part of the result algebra. Exhausting a traversal,
relationship, node, or receipt budget is `Failed`; it cannot yield a partial
proof. Unsupported but healthy lowering shapes are `Decline`. The distinction
is preserved so callers can route failure without pretending the inverse made
a semantic judgment.

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
| `ClassicInverseDecisionIsDetachedAndDeterministic` | A plan retains mutable IR, aliases a request body, or changes with request order or caller mutation. |
| `ClassicInversePlanningFailuresRemainFailures` | Invalid correlation or budget exhaustion becomes decline, reconstruction, or empty success. |
| `ClassicInverseAcceptedPopulationIsMeasured` | The implementation changes the accepted compiler-fixture population without an explicit expected delta and per-method review. |

The first five gates need compiler-produced positives plus synthetic close
negatives. The exact-head PR #5002 reproduction for an effectful condition and
nested sequential/loop stores is retained as adversarial input evidence:
`pr5002-round4c-reproductions.patch`, SHA-256
`554b7f8b9b4057c9e73439dc1f5d8df14d548da0faddfe69cc22d0cf06c01c59`.
It demonstrates the defect class; it is not itself an implementation gate.

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
5. Only after every accepted recipe uses the proof boundary may callers consume
   `Reconstruct`; the independent #5277, #5278, and #5279 owners then adopt the
   typed result without redefining this contract.
