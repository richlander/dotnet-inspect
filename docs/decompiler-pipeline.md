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
| Pipeline visibility | planned `--dump-stages` (= annotated IL per stage) | — | `JitDump` | DebugSteps UI |
| IL views | `AnnotatedILEmitter` depths as stage projections | — | dump of imported IR | — |
| Output stage | `CSharpEmitter` (today: decides while printing) | emit phase | codegen | `StatementBuilder` → `CSharpOutputVisitor` |
| Parenthesization | string inspection (today) | precedence in syntax factory | — | `InsertParenthesesVisitor` over finished AST |
| Naming | emitter-resident (today) | — | — | `AssignVariableNames` (final pass, scope-aware) |

## Current state, honestly

The front of the pipeline already has the standard shape: `ControlFlowGraph` → `StackSimulator` → `ILAstBuilder` → `TransformPipeline` → `StructuredControlFlow`. The condition/branch layer is typed (IL opcode duals for negation, documented polarity contracts), and the structuring layer is dominator-driven. These layers have not been the recurring bug source since they were typed.

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

- **The statement tree is the library's product, not the string.** Alternate front ends (IDE hovers, web viewers, diff tools) consume the tree and apply their own formatting and spans; our printer is merely the first front end. Taste splits across two homes: **raising policy** — which patterns the passes recover (`lock`, `using`, switch expressions vs. goto; the taste doc's three-class rule) — lives in the pipeline and shapes the tree itself, while **spelling policy** (qualification, parenthesization, formatting) lives in the printer and is the part alternate front ends may replace.
- **Whole-type composition lives in the library.** `TypeSourceComposer` (today in the CLI) moves into `ILInspector.Decompiler` so any front end gets per-type listings, using-hoisting, and forwarder-following without rebuilding them.
- **Naming is a final pass over fully-determined scopes**, as in ILSpy's `AssignVariableNames`. PDB local scopes are its natural input. The two remaining corpus gaps (synthesized names for `S_N`/`V_N`, multi-scope declaration placement) are this pass, not emitter features.
- **One analysis, many projections.** A single analysis facade computes CFG, stack simulation, ILAst, and structure once per method; the C# printer, the annotated IL emitter, stage dumps, and any future front end consume the same result. Today `CSharpEmitter` and `AnnotatedILEmitter` each rebuild these pieces.
- **Every stage boundary is a projectable IR, and the IL views are early-stage projections.** This is already latent in the code: `ILAnnotationDepth.Raw/Typed/Structured` renders the same method at three analysis depths. Formalized: raw IL projects the imported instruction stream (pre-transform — the IL views are ground truth, so they must project the tree *before* raising passes rewrite it), annotated IL projects the ILAst enriched with stack/CFG/structure facts, and C# projects the statement tree. One projection function parameterized by stage kills the IL-vs-annotated-IL divergence bug class structurally (it took a dedup PR to fix it once already), and `--dump-stages` stops being a new format: it is the annotated IL printer applied after each pass — exactly JitDump's relationship to GenTree.
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

- **Compile-back testing.** Where output is representable C#, decompile → compile → compare IL shape. This is the semantic analog of asmdiffs, and the natural complement to the text-level differential harness and the IL round-trip suite.
- **A stress corpus.** EH filters and fault handlers, `constrained.`/`tail.`/`volatile.`/`readonly.` prefixes, `calli`, and malformed or obfuscated IL as first-class test cases — the inputs a JIT treats as table stakes and a text-first decompiler tends to meet in the field first.

## Shipping plan: coexistence on main, no long-lived branch

The new pipeline is developed on `main` from day one, as ordinary PRs — a long-lived feature branch would rot against a tool that ships continuously. Both pipelines coexist inside the library; the public contract (`MethodBodyContext` → `Emit`) stays put and routes between them, so neither the CLI nor any future front end notices the transition. The tool never stops shipping.

Rollout is staged on the fidelity level, in the tiered-fallback pattern the JIT uses:

1. **Present but not wired.** New-pipeline code merges to `main` fully tested but unreachable from product paths. The differential harness runs in CI and the agreement number is the progress metric.
2. **Opt-in.** A flag (or environment variable) selects the new pipeline for dogfooding; the old path remains the default.
3. **Default with per-method fallback.** Methods the new pipeline renders at full fidelity use it; anything less falls back to the old path, with the diagnostic saying so. Users only ever see output from whichever path could render that method best.
4. **Retirement.** When the parity gate has held and fallback has been silent for a sustained period, the old emitter is deleted — the corpus history and fixture tests remain as its record.

## Conventions for new pipeline code

- Name passes after what they raise, in the neighbors' vocabulary: `LockTransform`, `UsingTransform`, `StringInterpolationTransform` — an engineer who knows the Roslyn lowering or ILSpy transform of the same name should find the inverse here.
- One pass, one job, one file under `Transforms/`, registered in the ordered list. The list *is* the architecture document.
- Passes communicate through the tree (typed nodes, annotations). A pass-local dictionary is acceptable; a field on the emitter is not.
- Every pass change ships with the corpus diff in the PR description, per the taste-doc checklist.
