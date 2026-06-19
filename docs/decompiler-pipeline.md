# Decompiler Pipeline Design

This document describes the architecture of `ILInspector.Decompiler`. The companion [decompiler-taste.md](decompiler-taste.md) governs *what* the decompiler renders; this document governs *how the pipeline decides it*.

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
| Typed IR | `IrNode` tree | `BoundNode` tree | `GenTree` | `ILInstruction` |
| Pass list | `IrPasses.Default` | `LocalRewriter` + dedicated rewriters | phase table | `CSharpDecompiler.GetILTransforms()` (27 passes) |
| Sugar handling | raising passes (inverse of lowering) | lowering rewriters (`Lowering/`) | — | `LockTransform`, `UsingTransform`, `SwitchOnStringTransform`, … |
| State machines | raising passes (PDB-first) | `AsyncRewriter` / `IteratorRewriter` | — | `AsyncAwaitDecompiler` / `YieldReturnDecompiler` (passes 7–8) |
| Per-pass validation | `IrFunction.CheckInvariant` (debug) | `Debug.Assert` culture | asserts between phases | `ILInstruction.CheckInvariant(ILPhase)` |
| Verification | `--gaps` / `--compile-back` / `--compile-check` | — | jitutils `asmdiffs` / SuperPMI | — |
| Pipeline visibility | `--dump` (per-pass IR projection) | — | `JitDump` | DebugSteps UI |
| Output stage | `CSharpPrinter` (decides in the tree, prints last) | emit phase | codegen | `StatementBuilder` → `CSharpOutputVisitor` |
| Naming | PDB-scope-aware local names | — | — | `AssignVariableNames` (final pass, scope-aware) |

## Architecture

```text
PE/metadata
  → MetadataSource              (PE + metadata + optional PDB lifetime; SRM-only)
  → IrImporter.Import           (raw IL → typed IR tree, with symbolic type identity)
  → IrPasses.Default            (ordered raising passes — one named class, one job;
                                 CheckInvariant after each in debug builds)
  → CSharpPrinter.PrintRaised   (runs the passes, then prints the raised tree;
                                 taste-doc spelling policy lives here)
```

The product reaches it through `MemberCodeProvider` (member level) and `TypeSourceComposer` (whole-type). `IrImporter.Import` and `CSharpPrinter.PrintRaised` are both exception-safe by construction: a one-method bug surfaces as a diagnostic comment and a lowered fidelity level, never a crash or silently-wrong output.

Key properties:

- **The statement tree is the library's product, not the string.** Alternate front ends (IDE hovers, web viewers, diff tools) consume the tree and apply their own formatting and spans; our printer is merely the first front end. Taste splits across two homes: **raising policy** — which patterns the passes recover (`lock`, `using`, switch expressions vs. goto; the taste doc's three-class rule) — lives in the pipeline and shapes the tree itself, while **spelling policy** (qualification, parenthesization, formatting) lives in the printer and is the part alternate front ends may replace.
- **Whole-type composition lives in the library.** `TypeSourceComposer` (in `ILInspector.Decompiler`) gives any front end per-type listings, using-hoisting, and forwarder-following without rebuilding them.
- **Naming is a final pass over fully-determined scopes**, as in ILSpy's `AssignVariableNames`. PDB local scopes are its natural input. The two remaining corpus gaps (synthesized names for `S_N`/`V_N`, multi-scope declaration placement) are this pass, not emitter features.
- **Every stage boundary is a projectable IR.** One `IrPasses.RunWithStages` runner captures the typed IR tree at import and after every pass, and one `StageDump` formatter frames them — exactly JitDump's relationship to GenTree — so `--dump`, `--diff`, `--facts`, and `--pass-impact` all read one capture rather than each rebuilding it. The default projection is the typed IR tree (`IrPrinter`); the annotated-IL import views (raw/typed/structured, from `IlProjection`) prepend as an opt-in (`--il`).
- **Results carry diagnostics, with concrete fidelity levels.** The library returns a result with output, diagnostics, and a fidelity level — never a silent `catch { }` in the library or its hosts. The levels are ordered and concrete, because the product routes on them: `Full` (every construct raised; representable C#), `Partial` (C# containing explicit unrepresentable nodes), `StructuredOnly` (structured control flow over low-level expressions), `IlOnly` (no C# rendering; IL projections still available), `Failed`. IL that has no C# spelling is modeled explicitly in the tree, not forced into plausible text — output degrades honestly, with the reason attached.
- **Diagnostics get stable IDs from the first PR.** They drive fallback routing and CI triage, so they are machine-readable Roslyn-style identifiers (`DEC0001`-form) with the prose message alongside — never bare strings.
- **Type identity is symbolic inside the pipeline.** Handles and signatures (byref, pinned, generic, function pointer, token identity) stay typed (`TypeRef`) through the tree; strings appear only at the printer.
- **State machines are pass-layer work.** Both Roslyn (dedicated rewriters) and ILSpy (dedicated early transforms) treat async/iterators that way, and they land here as dedicated raising passes, PDB-first against state-machine debug info with shape-based recovery as a lower-priority fallback.

## What we deliberately do differently

Two divergences from ILSpy are intentional and argued in [decompiler-taste.md](decompiler-taste.md):

- **Honest output over aggressive canonicalization.** Where Debug and Release builds produce different IL, we preserve the difference rather than normalizing to one rendering. The canonicalization dial is set weaker than ILSpy's on purpose: this is an inspection tool, and the IL is the ground truth.
- **Zero runtime dependencies.** The library depends only on `System.Reflection.Metadata` (via `ILInspector.Metadata`). We borrow the architecture of our neighbors, not their packages — no Roslyn syntax trees, no NRefactory-derived AST. The statement tree is small (roughly a dozen node kinds) and hand-written; ILSpy's generated 60 KB instruction set solves a scale problem we do not have.
- **Dataflow facts proportionate to the rewrites we do.** Cross-block transforms get only the facts they need (the definite-assignment CFG dataflow behind `--facts`, for instance). We deliberately stop short of SSA and value numbering: ILSpy ships a complete decompiler without them, and a JIT-grade dataflow stack would be infrastructure without a customer here.

## Inspection and verification: `--dump-stages` and `--compile-back`

These two harness modes are the two ends of the same pipeline, and they are designed to meet at a single artifact: the shipped product C#.

Both run the identical decompile front end — `IrImporter.Import` → the canonical `IrPasses` list → `CSharpPrinter`. The only difference is what each does with the result:

- **`--dump-stages` is white-box observability.** It answers *how* a method became this C#: `IrPasses.RunWithStages` captures the typed IR after import and after every pass, and the dump frames them in order, terminating on `CSharpPrinter.Print` of the fully-raised function. That final stage is byte-identical to `CSharpPrinter.PrintRaised` — i.e. it is exactly the C# the product emits, not an intermediate view (`PipelineStages.cs`). It is JitDump for the decompiler.
- **`--compile-back` is a black-box oracle.** It answers *whether* that same C# is semantically faithful: it takes `PrintRaised`'s output, recompiles it inside a reconstructed whole-type skeleton, and compares the canonical IL opcode stream against the original. A body that compiles and reads plausibly but recompiles to a different opcode stream changed the program — the failure class invisible to compile-check and source-grade.

The connection is load-bearing: **the final stage `--dump-stages` shows is exactly the artifact `--compile-back` grades.** Terminating the stage dump on the product renderer is what guarantees no drift between what you inspect and what is measured.

In workflow terms they form the quality loop: **`--compile-back` detects at scale** *which* methods regressed (opcode diffs across whole assemblies), and **`--dump-stages` diagnoses one** of them — drilling into the per-pass IR to find which pass introduced the divergence (`--steps`/`--step-limit` narrows to a single rewrite). Detection → diagnosis, both anchored on the same final C#.

Four narrower inspection modes drill past the per-pass tree into the analyses and structure the tree alone does not show:

- **`--facts`** surfaces the printer's definite-assignment dataflow — the per-block `gen` and `in`/`out` sets, computed by the *same* `CSharpPrinter` walk that ships, that decide which locals keep `= default`. It answers "is this `= default` elision sound?" by reading the analysis instead of running a slow `--compile-back` A/B.
- **`--cfg`** prints the control-flow graph (predecessor/successor edges) of each block container, so a flat goto-residue body's structure is a glance instead of a reconstruction by eye from `Branch IL_xxxx` targets. The edges come from the shared `Cfg.Build` the definite-assignment dataflow also uses, so the view and the analysis cannot disagree. Add **`--mermaid`** to render the graph as a mermaid `flowchart` (GitHub renders it inline) instead of the textual edge listing.
- **`--diff`** renders each pass's effect as a unified `+`/`-` hunk over the previous stage (no-change passes collapse), turning "what did this pass do?" into a glance over the same `RunWithStages` capture.
- **`--remarks`** lists every IR site that caps the method below `Full` fidelity, each paired with its stable `DEC####` code, block offset, and reason — the optimization-remarks analog (LLVM `-Rpass` / opt-viewer). The same predicate that computes `IrFunction.Fidelity` produces the list, so a remark exists for exactly the nodes that lower the score; fidelity is computed from the final tree (never asserted by a pass), so a remark names the IR site and cause, not a pass.

Where `--diff` is per-method, **`--pass-impact`** is its corpus-wide inverse — blast radius. `--diff` answers "for this method, what did each pass do"; `--pass-impact` answers "for this pass, which methods does it change" across a whole assembly. With no pass named it prints a histogram (each pass and the count of methods it altered — the roadmap for which passes carry the load); with a pass name it lists every method that pass changed, optionally with each method's hunk (`--show-diff`). It runs over the same `RunWithStages` capture (a method counts for a pass when any stage of that pass differs from the stage before it, surfaced by the shared `StageDump.PassesThatChanged`/`FormatPassDiff` helpers), and `--cap N` bounds the sweep the way the compile rails do. Use it to scope a pass change before touching it ("how many methods will this pass edit affect, and which") and to validate one after ("the count moved exactly where expected").

Orthogonal to those projections, **`--lowered`** selects a different *render altitude* for the C# itself. The shipped (sugared) output runs the full `IrPasses.Default` pipeline; the lowered view runs `IrPasses.Lowered`, the same pipeline minus the three cosmetic statement-sugar passes (`ForLoopPass`, `IncrementDecrementPass`, `LockSugarPass`), so loops stay `while`, `++`/`--` stay explicit temps, and `lock` stays an explicit `Monitor.Enter`/`Exit` with a `try`/`finally`. It is the decompiler's SharpLab "lowered C#": a lower-level-but-still-valid spelling, never invalid code. The cut is deliberately the largest de-sugaring that stays recompilable — dropping `PropertySugarPass` would emit `get_X()`/`set_X()` (CS0571) and dropping `DelegateConstructionPass` would leave an unspellable `ldftn`, so both stay in. Because the view is still valid C#, it earns the same two quality rails as the sugared view: `--compile-check --lowered` measures its compile rate and `--compile-back --lowered` roundtrips it through the compiler and compares opcode streams (gated by `LoweredCompileBackGateTests`, the lowered twin of `CompileBackGateTests`).

Two notes that save head-scratching:

- **Ref-kind and other call-site defects surface at callers, not at the definition.** Dumping a method's own definition (e.g. `System.AppContext::TryGetSwitch`) can report `fidelity: Full` because its callees are MethodDefs in the same assembly; a `DEC0007` ref-kind loss only appears when you dump a cross-assembly *caller* of that method. Dump the call site, not the target, to reproduce a call-shape defect.
- **`--skip-pdb` does not affect fidelity.** Local names are cosmetic — they never change emitted IL — so `--compile-back` is unaffected by whether names were recovered. `--skip-pdb` only changes the *spelling* in a dump (`V_n` vs `i`/`j`), for deterministic, symbol-independent reading.

## Conventions for pipeline code

- Name passes after what they raise, in the neighbors' vocabulary (`LockSugarPass`, `SwitchRaisingPass`, `StructuringPass`) — an engineer who knows the Roslyn lowering or ILSpy transform of the same name should find the inverse here.
- One pass, one job, one file under `Pipeline/Passes/`, registered in `IrPasses.Default`. The ordered list *is* the architecture document.
- Passes communicate through the tree (typed nodes via `ReplaceWith`), never side-channel state. A pass-local dictionary is acceptable; a field that outlives the pass is not.
- Every pass change ships with the corpus diff in the PR description, per the taste-doc checklist.
