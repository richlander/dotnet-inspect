# Decompiler pass composition

## Status

Focused design for [#9397](https://github.com/richlander/dotnet-inspect/issues/9397).
It owns the validity of an assembled Decompiler IR rewrite pipeline. Individual
passes continue to own their algorithms, evidence, and decline behavior.

The first production adoption validates the existing default, lowered,
capturing-lambda, intermediate-body, reconstruction, and caller-supplied
pipelines. It changes no pass order or output.

## Motivation

The ordered pass list is intentionally readable as the Decompiler architecture,
but some positions are correctness boundaries rather than preferences. At
commit
[`217b68ddf2`](https://github.com/richlander/dotnet-inspect/commit/217b68ddf2),
the storage tail records three examples:

- the final slots-only inliner must run before slot materialization;
- materialized locals must exist before coercion insertion; and
- residual storage binding must immediately follow coercion insertion.

The comments in
[`IrPass.cs`](../../src/ILInspector.Decompiler/Pipeline/Passes/IrPass.cs)
record the concrete consequences: invalid reconstructed updates and bare,
uncoerced minted locals. The same commit's iterator reconstruction in
[`ForeachIteratorReconstruction.cs`](../../src/ILInspector.Decompiler/Pipeline/Passes/ForeachIteratorReconstruction.cs)
and
[`ReducibleIteratorReconstruction.cs`](../../src/ILInspector.Decompiler/Pipeline/Passes/ReducibleIteratorReconstruction.cs)
re-runs an intermediate pipeline whose residual slot webs must remain available
for the host tail. These are production consumers, not hypothetical invalid
lists.

Position comments explain the architecture but cannot reject a new specialized
pipeline that violates it. Composition validity makes the load-bearing subset
executable without replacing the ordered list.

## Owner and exact claim

**Decompiler Pass Composition** owns this exact claim:

> Every Decompiler IR rewrite pipeline is an explicit ordered list. Before its
> first pass runs, the Decompiler validates that list against its finite set of
> correctness-bearing predecessor, adjacency, required-membership, and
> exclusion constraints. An invalid composition is a pipeline-construction
> defect independent of inspected input.

This owner defines:

- the pipeline profiles whose composition differs intentionally;
- the finite composition constraints enforced for each profile;
- rejection before mutation; and
- the diagnostic identity of a failed pipeline and relationship.

It does not define:

- pass algorithms, evidence, or decline policy;
- automatic pass ordering or scheduling;
- dynamic registration, discovery, or plugins;
- pass parallelism;
- read-only analysis construction, caching, or invalidation; or
- Producer Planning adoption for rewriting.

## Contract

### Authored order remains authoritative

Validation never reorders a pipeline. `IrPasses.Default` and its derived lists
remain explicit authored values and the readable architecture document.
Validation answers only whether the supplied order is legal.

### Constraints are admitted narrowly

A relationship belongs in the composition contract only when violating it can
change correctness, destroy evidence needed by a later owner, or make a
production pipeline claim false. Proximity, naming, convention, or potential
future reuse is insufficient.

The first admitted relationships are:

1. A pipeline containing slot materialization contains the slots-only
   expression inliner before it.
2. When materialization and coercion insertion both occur, materialization
   precedes insertion.
3. Residual storage binding requires materialization and immediately follows
   coercion insertion.
4. Complete presentation pipelines contain the complete storage tail.
5. Capturing-lambda preparation ends before that tail.
6. Intermediate reconstructed bodies include materialization and coercion but
   defer residual binding to the host tail.
7. Reconstruction imports exclude their requesting pass and the emission-stage
   passes whose results would replace structural evidence needed by the
   reconstruction owner.

A partial caller-supplied pipeline that contains none of the governed passes is
valid. The contract does not turn the default pipeline into the only legal
pipeline.

### Failure precedes mutation

The complete list is validated before any pass runs. Rejection names the
pipeline and violated pass relationship. It is an
`InvalidOperationException`: composition is trusted product construction, not
an inspected-data failure or a partial Decompiler result.

### Validation is operation-independent

Composition depends only on the pass list and profile. It does not read an
assembly, method, IR node, or host setting. Built-in pipelines validate once
when constructed; caller-supplied pipelines validate at the execution
boundary.

## Adoption

`IrPasses` is the production adopter. The default and lowered product paths,
capturing-lambda split, iterator intermediate reruns, and async/iterator
reconstruction imports all consume the same validator. CLI and Browser/Wasm
inherit the result through their shared Decompiler pipeline.

This is one architecture and one adoption slice. No host-specific work or
alternative pipeline remains to migrate.

## Analogous implementations

LLVM's
[new pass manager](https://llvm.org/docs/NewPassManager.html)
keeps explicit pass-manager construction separate from its analysis managers
and invalidation. That separation is the relevant convention. This design
adopts explicit manager-owned composition checks but deliberately does not add
LLVM's analysis cache, IR hierarchy, extension callbacks, or plugin model.

[Producer Planning](producer-planning.md) independently validates declared
read-only producer work before execution. It is evidence for early validation,
not an adopter or implementation substrate: rewriting and analysis remain
separate, and any future shared Decompiler analysis needs its own invalidation
contract.

## Verification

`IrPassCompositionTests` runs in Release and gates:

- every built-in profile validates;
- governed predecessor and adjacency violations reject;
- missing required tail members reject;
- intermediate and reconstruction exclusions reject;
- requester self-inclusion rejects;
- rejection happens before the first pass mutates the function; and
- an unrelated custom pipeline remains valid and executes.

The ordinary Decompiler suite preserves output behavior. This change makes no
performance claim: built-in pipelines validate at construction, while custom
validation is a bounded scan of an explicitly supplied pass list.
