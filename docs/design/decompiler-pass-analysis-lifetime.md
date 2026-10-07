# Decompiler pass analysis lifetime

Issues:

- [#9519](https://github.com/richlander/dotnet-inspect/issues/9519)
- [#9544](https://github.com/richlander/dotnet-inspect/issues/9544)

## Owned claim

For one `IrPasses` execution over one `IrFunction`, each manager-owned
read-only analysis result belongs to one analysis generation. A pass receives
only a result valid for the current generation. After the pass completes, the
manager retains that result only when the pass explicitly preserves the
analysis; otherwise a later consumer constructs a fresh result.

This owner defines:

- finite analysis identities declared by passes;
- construction and reuse inside one function's pipeline execution;
- conservative preservation and invalidation at completed pass boundaries;
- generation identity for freshness; and
- opt-in receipts for construction, reuse, preservation, and invalidation.

[Decompiler pass composition](decompiler-pass-composition.md) continues to own
which ordered pass lists are legal. [Decompiler pass execution
receipts](decompiler-pass-execution-receipts.md) continues to own observed IR
projection changes. Individual analyses own their facts, and individual passes
own their rewrites and decline behavior.

## Why this slice exists

The pass infrastructure in
[`decompiler-ir.md`](../decompiler-ir.md#pass-infrastructure) already assigns
`PassContext` the role of carrying read-only facts computed once and
invalidated by structural rewrites. The implementation has not yet established
that lifetime contract.

The first candidate is branch-target identity. The product has 14 static calls
to `ReferenceOwnership.CollectBranchTargets`. The adjacent late passes
`StoreElementReceiverInliningPass`, `PdbScopeEntryLocalPass`, and
`PdbLocalScopePass` repeatedly derive the same target-offset set. Their
rewrites change expression storage, exact PDB local projection, and lexical
wrapping, but never a `Branch`, `ConditionalBranch`, `Leave`, or
`SwitchBranch` target. The result can therefore be constructed once and reused
across the three passes.

The compiler-produced `PdbScopeFixtures` methods with gotos and retained entry
and internal labels are the production witness. This slice makes no elapsed
time claim. Its measurable result is fewer complete-tree constructions of the
same fact.

The first broader-reuse adoption carries that result through the final
expression, storage, and naming passes into `DefiniteAssignmentPass`. Those
passes may replace expressions, bind stack slots as locals, decide scalar
presentation, or allocate names, but they do not change control-transfer
target offsets. Reusing the existing generation therefore exercises the
complete preservation chain rather than introducing another analysis.

The candidate census rejected CFG as this adoption's analysis. Current CFG
inputs belong to individual `BlockContainer` block lists across unrelated
stages, including a mutating structuring pass; no adjacent pass pair currently
reconstructs one unchanged container graph. A manager-owned CFG would require
a separately designed keyed lifetime and mutation footprint without a
demonstrated production reuse.

## Contract

### Declarations do not schedule passes

A pass declares the analyses it requires and the analyses it preserves.
Declarations do not reorder the authored pass list, select a pass, or cause
another pass to run. The manager prepares declared requirements immediately
before their pass and retains the existing explicit pipeline.

An undeclared analysis request is a pipeline defect. A direct focused-pass test
that does not run through `IrPasses` receives an ephemeral result and creates
no cross-pass state.

### Analysis generations establish freshness

Each analysis identity has a generation within one function's pipeline
execution. Construction associates the result with the current generation.
Reuse returns only that current result.

After a completed pass:

- an explicitly preserved analysis keeps its result and generation; and
- every other constructed analysis is discarded and advances to a new
  generation.

Preservation is unconditional for that pass occurrence. A pass may declare it
only when every successful execution path leaves the analysis fact unchanged,
including paths that perform a rewrite. A pass that is merely often a no-op
does not preserve an analysis unless its possible rewrites preserve it too.

The rule composes conjunctively: a result survives several pass boundaries only
when every intervening pass preserves it. The final pass's declaration alone
cannot restore a result invalidated earlier.

### State is execution-local

Analysis state belongs to one `IrPasses` execution and one `IrFunction`. It is
not process-global and does not cross separate top-level runs. A nested
pipeline over an imported sibling body receives independent analysis state,
even when it shares the parent's `PassContext` capabilities.

Analysis values may contain IR nodes only when their analysis owner says so.
The first result contains immutable integer target offsets and retains no
nodes, metadata handles, readers, or host resources.

### Failure remains visible

Analysis construction failure prevents the pass from running. A receipt is
issued only after the pass and the ordinary post-pass invariant checks
complete. If analysis construction, the pass, or an invariant throws, the run
fails visibly and no success-shaped completed receipt is issued for that pass.
Execution-local analysis state cannot be observed by a later run.

## First analysis: branch-target offsets

The branch-target analysis is the immutable set of all target offsets named by
`Branch`, `ConditionalBranch`, `Leave`, and `SwitchBranch` nodes outside nested
function bodies. It is the existing fact produced by
`ReferenceOwnership.CollectBranchTargets`; this owner changes its lifetime, not
its meaning.

The first consumers are:

- `StoreElementReceiverInliningPass`;
- `PdbScopeEntryLocalPass`; and
- `PdbLocalScopePass`.

All three require and preserve the analysis. Trial functions constructed by
`PdbScopeEntryLocalPass` are distinct functions and use independent ephemeral
or nested-pipeline state rather than borrowing the original function's result.

### Late emission-tail adoption

The branch-target result remains current across:

- `CheckedIntegerOperandPass`;
- `ReferenceCoalesceBindingPass`;
- `ReferenceConditionalBindingPass`;
- `PrimitiveJoinBindingPass`;
- `CoercionInsertionPass`;
- `ResidualSlotBindingPass`;
- `ScalarSelfUpdatePass`; and
- `ParameterNameAllocationPass`.

Each pass explicitly preserves the analysis. `DefiniteAssignmentPass` requires
and preserves it, reusing the current outer-function result while independently
constructing target sets for nested lambda and local-function bodies.

`Newtonsoft.Json` 13.0.4 is the real production witness. The pinned PR-quick
corpus records switch, conditional, branch, and leave targets for
`JsonTextReader.<ParseCommentAsync>d__16.MoveNext`, produced from
[commit `4e13299d`](https://github.com/JamesNK/Newtonsoft.Json/blob/4e13299d4b0ec96bd4df9954ef646bd2d1b5bf2a/Src/Newtonsoft.Json/JsonTextReader.Async.cs).
The compiler-produced PDB-scope fixture remains the deterministic Release gate
for generation reuse and output parity.

This owner does not claim that earlier passes preserve branch targets; each
additional preservation declaration requires its own adoption evidence.

## Receipts

An opt-in analysis receipt identifies:

- the one-based pass ordinal and pass name;
- the analysis identity;
- the analysis generation used by the pass;
- whether the pass constructed, reused, or did not request the result; and
- whether the completed pass preserved or invalidated the result.

One pass may produce several analysis receipts when future slices register more
analysis identities. Repeated pass names retain distinct ordinal identity.
Ordinary `IrPasses.Run` retains the analysis values needed by production
consumers but does not retain receipt objects.

The receipt does not claim that reuse improved elapsed time, that a preserving
pass made no IR change, or that two different analyses share an implementation.

## Model evidence

The
[Decompiler pass analysis lifetime Lean model](../../prototypes/decompiler-pass-analysis-lifetime/)
proves:

- construction produces a current result;
- any sequence of preserving passes retains that result;
- one non-preserving pass advances the generation and clears it;
- the next consumer constructs before use; and
- checking only the final pass's preservation admits a stale result.

The QuerySpace
[producer-capability demand pilot](../../prototypes/producer-capability-demand/)
is supporting evidence for owner-defined demand joins and shared read-only
traversal. It does not own Decompiler analysis generations, pass preservation,
or invalidation, and the Decompiler product does not depend on QuerySpace
capability types.

## Boundaries

This slice does not add:

- automatic scheduling, reordering, or pass discovery;
- dynamic analysis registration;
- cross-function or cross-run reuse;
- CFG, dominance, use-def, definite-assignment, or declaration-plan caching;
- per-container analysis keys or CFG mutation tracking;
- structural hashes or general mutation counters;
- conditional preservation inferred from whether a pass changed the observed
  printer projection;
- QuerySpace or Producer Planning product dependencies; or
- an elapsed-time performance claim.

## Gates

Release tests establish:

- one construction followed by reuse across the three preserving production
  passes;
- preservation through the complete late emission tail and reuse by
  `DefiniteAssignmentPass`;
- a non-preserving intervening pass forces a new construction;
- no result can be requested under a stale generation;
- nested imported-body execution uses independent state;
- repeated pass names retain distinct receipt ordinals;
- a throwing pass produces no completed receipt; and
- the compiler-produced PDB-scope outputs and final IR remain unchanged.

The Decompiler fast gate covers the ordinary product pipeline and its CLI and
harness consumers.
