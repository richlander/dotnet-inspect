# Decompiler Pipeline Design

This document describes the target architecture for `ILInspector.Decompiler` and the replacement and shipping plan for getting there. The companion [decompiler-taste.md](decompiler-taste.md) governs *what* the decompiler renders; this document governs *how the pipeline decides it*.

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
| Pipeline visibility | `--dump-stages` (per-pass IR projection) | — | `JitDump` | DebugSteps UI |
| IL views | `AnnotatedILEmitter` depths as stage projections | — | dump of imported IR | — |
| Output stage | `CSharpEmitter` (today: decides while printing) | emit phase | codegen | `StatementBuilder` → `CSharpOutputVisitor` |
| Parenthesization | string inspection (today) | precedence in syntax factory | — | `InsertParenthesesVisitor` over finished AST |
| Naming | emitter-resident (today) | — | — | `AssignVariableNames` (final pass, scope-aware) |

## The legacy emitter, and why it is being retired

The front of the original pipeline already had the standard shape: `ControlFlowGraph` → `StackSimulator` → `ILAstBuilder` → `TransformPipeline` → `StructuredControlFlow`. The condition/branch layer is typed (IL opcode duals for negation, documented polarity contracts), and the structuring layer is dominator-driven. These layers stopped being the recurring bug source once they were typed.

The back was not standard. `CSharpEmitter` is ~9,500 lines and makes sugar decisions *during* printing, coordinating through ~29 side-channel collections (`_consumedBlocks`, `_skipNodes`, `_mergedLocals`, …). Every entry in that list is a decision that should have been a tree edit made earlier. The recurring bug pattern — ordering constraints between fixups, state not threaded to temporary contexts, string-keyed substitutions — is the cost of that design, and it is the part a compiler engineer would *not* recognize. That cost is what motivated the replacement pipeline below. The cutover is done: the product renders through `IrImporter.Import` → `CSharpPrinter` (via `MemberCodeProvider` and `TypeSourceComposer`), and `CSharpEmitter` survives only as the harness compile-back oracle and behind the annotated-IL view, slated for decommission once the new tooling is deployed.

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

- **The statement tree is the library's product, not the string.** Alternate front ends (IDE hovers, web viewers, diff tools) consume the tree and apply their own formatting and spans; our printer is merely the first front end. Taste splits across two homes: **raising policy** — which patterns the passes recover (`lock`, `using`, switch expressions vs. goto; the taste doc's three-class rule) — lives in the pipeline and shapes the tree itself, while **spelling policy** (qualification, parenthesization, formatting) lives in the printer and is the part alternate front ends may replace.
- **Whole-type composition lives in the library.** `TypeSourceComposer` (today in the CLI) moves into `ILInspector.Decompiler` so any front end gets per-type listings, using-hoisting, and forwarder-following without rebuilding them.
- **Naming is a final pass over fully-determined scopes**, as in ILSpy's `AssignVariableNames`. PDB local scopes are its natural input. The two remaining corpus gaps (synthesized names for `S_N`/`V_N`, multi-scope declaration placement) are this pass, not emitter features.
- **One analysis, many projections.** A single analysis facade computes CFG, stack simulation, ILAst, and structure once per method; the C# printer, the annotated IL emitter, stage dumps, and any future front end consume the same result. Today `CSharpEmitter` and `AnnotatedILEmitter` each rebuild these pieces.
- **Every stage boundary is a projectable IR, and the IL views are early-stage projections.** This is already latent in the code: `ILAnnotationDepth.Raw/Typed/Structured` renders the same method at three analysis depths. Formalized: raw IL projects the imported instruction stream (pre-transform — the IL views are ground truth, so they must project the tree *before* raising passes rewrite it), annotated IL projects the ILAst enriched with stack/CFG/structure facts, and C# projects the statement tree. One projection function parameterized by stage kills the IL-vs-annotated-IL divergence bug class structurally (it took a dedup PR to fix it once already). `--dump-stages` is the realization of this principle: a shared `IrPasses.RunWithStages` runner captures `(PassName, Projection, Fidelity)` at import and after every pass, and one `StageDump` formatter frames them — exactly JitDump's relationship to GenTree. The default projection is the typed IR tree (`IrPrinter`); the annotated-IL import views (`AnnotatedILEmitter` Raw/Typed/Structured) prepend as an opt-in (`--il` in the harness, the `Full` view in the library). It is surfaced through both the harness (`--dump`) and the CLI (`--dump-stages` / `-S "IR (Stages)"`).
- **Results carry diagnostics, with concrete fidelity levels.** The library returns a result with output, diagnostics, and a fidelity level — never a silent `catch { }` in the library or its hosts. The levels are ordered and concrete, because the shipping plan routes on them: `Full` (every construct raised; representable C#), `Partial` (C# containing explicit unrepresentable nodes), `StructuredOnly` (structured control flow over low-level expressions), `IlOnly` (no C# rendering; IL projections still available), `Failed`. IL that has no C# spelling is modeled explicitly in the tree, not forced into plausible text — output degrades honestly, with the reason attached.
- **Diagnostics get stable IDs from the first PR.** They drive fallback routing and CI triage, so they are machine-readable Roslyn-style identifiers (`DEC0001`-form) with the prose message alongside — never bare strings.
- **Type identity is symbolic inside the pipeline.** Today type names become strings early; in the target architecture, handles and signatures (byref, pinned, generic, function pointer, token identity) stay typed through the tree, and strings appear only at the printer.
- **State machines wait for this architecture.** Both Roslyn (dedicated rewriters) and ILSpy (dedicated early transforms) treat async/iterators as pass-layer work. Attempting them against the current emitter would be building on the part of the codebase scheduled for demolition.

## What we deliberately do differently

Two divergences from ILSpy are intentional and argued in [decompiler-taste.md](decompiler-taste.md):

- **Honest output over aggressive canonicalization.** Where Debug and Release builds produce different IL, we preserve the difference rather than normalizing to one rendering. The canonicalization dial is set weaker than ILSpy's on purpose: this is an inspection tool, and the IL is the ground truth.
- **Zero runtime dependencies.** The library depends only on `System.Reflection.Metadata` (via `ILInspector.Metadata`). We borrow the architecture of our neighbors, not their packages — no Roslyn syntax trees, no NRefactory-derived AST. The statement tree is small (roughly a dozen node kinds) and hand-written; ILSpy's generated 60 KB instruction set solves a scale problem we do not have.
- **Dataflow facts proportionate to the rewrites we do.** Cross-block transforms get dominance and use-def facts from the pipeline context (today `ExpressionInliner` documents that it has no dominance check and restricts itself to position-independent constants). We deliberately stop short of SSA and value numbering: ILSpy ships a complete decompiler without them, and a JIT-grade dataflow stack would be infrastructure without a customer here.

## Replacement plan: greenfield behind a baseline

The back half of the pipeline is replaced, not refactored in place. This is the move the neighbor teams made themselves: RyuJIT was written new alongside the legacy JIT and driven to parity through asmdiffs; Roslyn was greenfield against years of differential testing with the native compilers. Neither team has ever drained a 9,500-line emitter in place — so the replacement strategy is itself part of the recognizability goal.

The existing decompiler is the baseline: corpus at grade A, 260 fixture tests in both configurations, and the taste document amount to an executable specification at the strongest it has ever been. It keeps shipping, untouched in behavior, until the new pipeline passes it.

**What carries over:** the front half (`ControlFlowGraph`, `StackSimulator`, the structuring algorithms — standard-shaped, and not where the bugs have been), the test fixtures, the verification harness, and the taste rules. **What is built new:** the typed IR (symbolic type identity from the importer on), the raising passes, the statement tree, the finishing visitors, and the printer — with invariants, diagnostics, dataflow facts, and stage projection designed in rather than retrofitted.

Order of work:

1. **The differential harness is the first artifact** (`tools/DecompilerHarness`). A runner that decompiles thousands of BCL methods through both pipelines and reports agreement plus categorized diffs — our SuperPMI/asmdiffs. Until the candidate pipeline exists it runs in inventory mode: render rate and bucketed exceptions across whole assemblies. The parity gate is defined before the new pipeline exists: exact-or-better on the graded h2h corpus, no untriaged regressions on the BCL sweep, both-config suites and the IL round-trip suite green through the new path.
2. **Honest failure lands now, in the old path too.** The result type carrying output, diagnostics, and a fidelity level replaces the silent `catch { }` blocks in the CLI formatter and composer immediately — it is also the routing mechanism the shipping plan below depends on.
3. **CLI seams move regardless of pipeline.** `TypeSourceComposer` into the library; a code-section provider extracted so `ApiOutputFormatter` only renders. These are correct under either architecture. The `MethodBodyContext` split, by contrast, is deferred to the new pipeline's input contract: splitting the old pipeline's input type would be investment in code scheduled for replacement, and its real lesson (the context's hidden lifetime coupling to the live `PEReader`) is a design input for the new importer, not a retrofit.
4. **The new pipeline is built in dependency order** ([decompiler-ir.md](decompiler-ir.md)) — IR, then passes, then statement tree, then finishing visitors, then printer — in normal PR-sized increments, each validated by the differential harness from the first method it can render.
5. **Overlapping feature work in the old emitter stops** once the new pipeline has a meaningful vertical slice — IR through passes, tree, and printer rendering a corpus subset end-to-end — so nothing substantial lands twice. Critical correctness fixes continue in the old path until retirement.
6. **State machines are built only on the new pipeline**, as dedicated raising passes mirroring `AsyncRewriter`/`IteratorRewriter`, designed PDB-first against state-machine debug info — with shape-based recovery as an explicit lower-priority fallback, since many assemblies ship without useful symbols. Missing symbols lower the fidelity level; they do not remove the feature. State machines are the first feature the old emitter never gets.

Two verification rails grow alongside:

- **Compile-back testing.** Where output is representable C#, decompile → compile → compare IL shape. This is the semantic analog of asmdiffs, and the natural complement to the text-level differential harness and the IL round-trip suite. Built as the harness's `--compile-back` mode (see [tools/DecompilerHarness/README.md](../tools/DecompilerHarness/README.md)): each member recompiles inside a reconstructed whole-module skeleton and its canonical opcode stream is compared against the original, so a body that compiles but means something different surfaces as a `Full`-fidelity opcode diff.
- **A stress corpus.** EH filters and fault handlers, `constrained.`/`tail.`/`volatile.`/`readonly.` prefixes, `calli`, and malformed or obfuscated IL as first-class test cases — the inputs a JIT treats as table stakes and a text-first decompiler tends to meet in the field first.

## Shipping plan: coexistence on main, no long-lived branch

The new pipeline is developed on `main` from day one, as ordinary PRs — a long-lived feature branch would rot against a tool that ships continuously. Both pipelines coexist inside the library through cutover: the product source path now runs the new contract (`MetadataSource` → `IrImporter.Import` → `CSharpPrinter.PrintRaised`), while the old `MethodBodyContext` → `Emit` contract survives only as the harness oracle and behind the annotated-IL view. The tool never stops shipping.

Rollout is staged on the fidelity level:

1. **Present but not wired.** New-pipeline code merges to `main` fully tested but unreachable from product paths. The differential harness runs in CI and the agreement number is the progress metric.
2. **Opt-in.** A flag (or environment variable) selects the new pipeline for dogfooding; the old path remains the default.
3. **Cutover, no fallback — honest degradation instead.** The new pipeline becomes the *sole* product source path, at both the member level (`MemberCodeProvider`) and the whole-type level (`TypeSourceComposer`). There is no per-method fallback to the old emitter. A per-method fallback was considered and rejected: the old emitter was never a real safety net (its output is parity-or-worse, with no fidelity signal of its own), and routing around the new pipeline would mask exactly the gaps the burndown must close. The new pipeline degrades *honestly* instead — worst case is valid-but-ugly C# (a stack-slot spill, a `goto`, a diagnosed `/* unsupported */` marker), never a crash or silently-wrong output. `IrImporter.Import` and `CSharpPrinter.PrintRaised` are both exception-safe by construction, so a one-method bug surfaces as a diagnostic comment, not a thrown exception. This is the forcing function: the output users read *is* the new pipeline's output, and it is always valid.
4. **Retirement.** The old emitter is demoted at cutover to the differential oracle the harness diffs against (its only remaining callers are the harness and its own tests — see the `CSharpEmitter` remarks). When the parity gate has held for a sustained period, it is deleted — the corpus history and fixture tests remain as its record.

## Inspection and verification: `--dump-stages` and `--compile-back`

These two harness modes are the two ends of the same pipeline, and they are designed to meet at a single artifact: the shipped product C#.

Both run the identical decompile front end — `IrImporter.Import` → the canonical `IrPasses` list → `CSharpPrinter`. The only difference is what each does with the result:

- **`--dump-stages` is white-box observability.** It answers *how* a method became this C#: `IrPasses.RunWithStages` captures the typed IR after import and after every pass, and the dump frames them in order, terminating on `CSharpPrinter.Print` of the fully-raised function. That final stage is byte-identical to `CSharpPrinter.PrintRaised` — i.e. it is exactly the C# the product emits, not an intermediate view (`PipelineStages.cs`). It is JitDump for the decompiler.
- **`--compile-back` is a black-box oracle.** It answers *whether* that same C# is semantically faithful: it takes `PrintRaised`'s output, recompiles it inside a reconstructed whole-type skeleton, and compares the canonical IL opcode stream against the original. A body that compiles and reads plausibly but recompiles to a different opcode stream changed the program — the failure class invisible to compile-check and source-grade.

The connection is load-bearing: **the final stage `--dump-stages` shows is exactly the artifact `--compile-back` grades.** Terminating the stage dump on the product renderer (rather than the legacy `CSharpEmitter` staged view, a known fidelity trap) is what guarantees no drift between what you inspect and what is measured.

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

## Conventions for new pipeline code

- Name passes after what they raise, in the neighbors' vocabulary: `LockTransform`, `UsingTransform`, `StringInterpolationTransform` — an engineer who knows the Roslyn lowering or ILSpy transform of the same name should find the inverse here.
- One pass, one job, one file under `Transforms/`, registered in the ordered list. The list *is* the architecture document.
- Passes communicate through the tree (typed nodes, annotations). A pass-local dictionary is acceptable; a field on the emitter is not.
- Every pass change ships with the corpus diff in the PR description, per the taste-doc checklist.
