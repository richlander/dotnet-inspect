# Decompiler Pipeline Design

This document describes the target architecture for `DotnetInspector.Decompiler` and the migration plan for getting there. The companion [decompiler-taste.md](decompiler-taste.md) governs *what* the decompiler renders; this document governs *how the pipeline decides it*.

## Design goal: recognizability

This tool is expected to transition to the dotnet org and be maintained by engineers who work on Roslyn, RyuJIT, and related compilers. The overriding architectural goal is that those engineers open this codebase and find the shape they already know — and want to improve it rather than rewrite it.

That shape is the standard compiler pipeline, which Roslyn, RyuJIT, and ILSpy arrived at independently:

1. A **typed IR** with parent/child structure.
2. An **ordered list of named passes**, each doing one job.
3. **Invariant validation after every pass** (debug builds).
4. **All decisions made in the tree before output** — printing is a dumb final stage.

Decompilation is this pipeline run in reverse. Where Roslyn *lowers* C# constructs to IL through named rewriters, we *raise* IL back through the inverse transforms. This duality is load-bearing: Roslyn's `Lowering/` directory is our completeness checklist, and each of our raising passes should be named and documented as the inverse of the Roslyn rewriter whose output it recognizes.

## Rosetta stone

| Concept | This codebase | Roslyn | RyuJIT | ILSpy |
| --- | --- | --- | --- | --- |
| Typed IR | `ILAstNode` tree (+ planned statement tree) | `BoundNode` tree | `GenTree` | `ILInstruction` |
| Pass list | `TransformPipeline` | `LocalRewriter` + dedicated rewriters | phase table | `CSharpDecompiler.GetILTransforms()` (27 passes) |
| Sugar handling | raising passes (inverse of lowering) | lowering rewriters (`Lowering/`) | — | `LockTransform`, `UsingTransform`, `SwitchOnStringTransform`, … |
| State machines | planned raising passes (PDB-first) | `AsyncRewriter` / `IteratorRewriter` | — | `AsyncAwaitDecompiler` / `YieldReturnDecompiler` (passes 7–8) |
| Per-pass validation | planned `CheckInvariant` | `Debug.Assert` culture | asserts between phases | `ILInstruction.CheckInvariant(ILPhase)` |
| Diff-driven verification | h2h corpus diff + dual-config suites | — | jitutils `asmdiffs` / SuperPMI | — |
| Pipeline visibility | planned `--dump-stages` | — | `JitDump` | DebugSteps UI |
| Output stage | `CSharpEmitter` (today: decides while printing) | emit phase | codegen | `StatementBuilder` → `CSharpOutputVisitor` |
| Parenthesization | string inspection (today) | precedence in syntax factory | — | `InsertParenthesesVisitor` over finished AST |
| Naming | emitter-resident (today) | — | — | `AssignVariableNames` (final pass, scope-aware) |

## Current state, honestly

The front of the pipeline already has the standard shape: `ControlFlowGraph` → `StackSimulator` → `ILAstBuilder` → `TransformPipeline` → `StructuredControlFlow`. The condition/branch layer is typed (IL opcode duals for negation, documented polarity contracts), and the structuring layer is dominator-driven. These layers have been essentially bug-free since they were typed.

The back of the pipeline is not standard. `CSharpEmitter` is ~9,500 lines and makes sugar decisions *during* printing, coordinating through ~29 side-channel collections (`_consumedBlocks`, `_skipNodes`, `_mergedLocals`, …). Every entry in that list is a decision that should have been a tree edit made earlier. The recurring bug pattern — ordering constraints between fixups, state not threaded to temporary contexts, string-keyed substitutions — is the cost of that design, and it is the part a compiler engineer would *not* recognize.

## Target architecture

```text
PE/metadata
  → ControlFlowGraph → StackSimulator → ILAstBuilder      (existing)
  → TransformPipeline: ordered raising passes              (grows: lock, using,
      each pass = one named class, one job,                 interpolation, string-switch,
      CheckInvariant after each in debug builds             spill folds, state machines)
  → StructuredControlFlow                                   (existing, dominator-driven)
  → Statement tree (typed; the public product)              (new)
  → Finishing visitors: parenthesization, naming            (new; naming is PDB-scope-aware)
  → Printer                                                 (small; taste-doc policy lives here)
```

Key properties:

- **The statement tree is the library's product, not the string.** Alternate front ends (IDE hovers, web viewers, diff tools) consume the tree and apply their own formatting and spans; our printer is merely the first front end. The taste document becomes printer policy, not library behavior.
- **Whole-type composition lives in the library.** `TypeSourceComposer` (today in the CLI) moves into `DotnetInspector.Decompiler` so any front end gets per-type listings, using-hoisting, and forwarder-following without rebuilding them.
- **Naming is a final pass over fully-determined scopes**, as in ILSpy's `AssignVariableNames`. PDB local scopes are its natural input. The two remaining corpus gaps (synthesized names for `S_N`/`V_N`, multi-scope declaration placement) are this pass, not emitter features.
- **State machines wait for this architecture.** Both Roslyn (dedicated rewriters) and ILSpy (dedicated early transforms) treat async/iterators as pass-layer work. Attempting them against the current emitter would be building on the part of the codebase scheduled for demolition.

## What we deliberately do differently

Two divergences from ILSpy are intentional and argued in [decompiler-taste.md](decompiler-taste.md):

- **Honest output over aggressive canonicalization.** Where Debug and Release builds produce different IL, we preserve the difference rather than normalizing to one rendering. The canonicalization dial is set weaker than ILSpy's on purpose: this is an inspection tool, and the IL is the ground truth.
- **Zero runtime dependencies.** The library depends only on `System.Reflection.Metadata` (via `DotnetInspector.Metadata`). We borrow the architecture of our neighbors, not their packages — no Roslyn syntax trees, no NRefactory-derived AST. The statement tree is small (roughly a dozen node kinds) and hand-written; ILSpy's generated 60 KB instruction set solves a scale problem we do not have.

## Migration plan

Each step is a normal PR validated the same way as a rendering change: full h2h corpus diff (only intended sites move), decompiler suite green in both Debug and Release, CLI suite green.

- **Step 0 — verification rails.** `CheckInvariant` on the ILAst, run after every pass in debug builds; pass-list discipline (every new detection is a pass, never a new emitter field); a `--dump-stages` diagnostic in the JitDump genre.
- **Step 1 — code motion (independent, anytime).** Move `TypeSourceComposer` from the CLI into the library.
- **Step 2 — drain the emitter into passes.** Migrate detections one PR at a time: lock, using, string interpolation, string-switch, spill folds. Each PR deletes at least one emitter side-channel field. Each pass tells us what the statement tree must eventually represent — this is how the tree's schema gets dictated rather than guessed.
- **Step 3 — the statement tree.** Introduce the typed statement tree between structuring and printing; remaining mid-print decisions become tree edits; the emitter shrinks to a printer. The tree is designed as public API for external front ends.
- **Step 4 — finishing visitors.** Parenthesization over the finished tree (retiring string inspection); the naming pass (PDB-scope-aware), which closes the last two corpus gaps.
- **Then: state machines**, as dedicated raising passes mirroring `AsyncRewriter`/`IteratorRewriter`, designed PDB-first against state-machine debug info.

## Conventions for new pipeline code

- Name passes after what they raise, in the neighbors' vocabulary: `LockTransform`, `UsingTransform`, `StringInterpolationTransform` — an engineer who knows the Roslyn lowering or ILSpy transform of the same name should find the inverse here.
- One pass, one job, one file under `Transforms/`, registered in the ordered list. The list *is* the architecture document.
- Passes communicate through the tree (typed nodes, annotations). A pass-local dictionary is acceptable; a field on the emitter is not.
- Every pass change ships with the corpus diff in the PR description, per the taste-doc checklist.
