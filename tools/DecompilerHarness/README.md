# Decompiler Harness

The differential harness from [docs/decompiler-pipeline.md](../../docs/decompiler-pipeline.md) — the asmdiffs analog for the pipeline replacement. The parity gate between the current decompiler and its greenfield successor is measured here.

## Modes

**Inventory** (default): sweeps every method body in the given assemblies through the current pipeline and reports render rate plus exceptions bucketed by type and topmost decompiler frame, so one bug is one bucket.

**Replacement-pipeline inventory** (`--pipeline next`): sweeps through the new importer and reports the fidelity histogram plus stop-reason buckets — the prioritized slice roadmap. Exits nonzero if any importer bug (DEC0001) appears.

**Diff** (`--candidate <name>`): runs two pipelines over every method and reports agreement, categorized first-line diffs, and one-sided failures. `--candidate next` is the parity scoreboard: every comparable method through both pipelines as trimmed C# text, with difference buckets that double as the raising-pass docket.

The raw agreement percentage conflates three unlike things, so the scoreboard also splits the disagreements by signal — because "agrees with the frozen emitter" is not the same as "good," and the gate is exact-or-*better*:

- **candidate-worse** — the new pipeline left a `goto`, an unraised `Monitor.Enter`, or an unrepresentable `/* … */` where the baseline is cleaner. These plus the **Partial** count are the real burndown.
- **baseline-worse** — the old emitter emitted an IL artifact (`Type::member`, `S_in_` leak) the new pipeline already renders correctly. The candidate wins; the gate does not require matching these.
- **likely cosmetic** — equal once `Namespace.` qualifier runs are stripped symmetrically (the baseline's semi-qualified names vs the new pipeline's simple ones). Not a gap.
- **uncertain** — both clean but genuinely different; only the source corpus can grade these.

The classifier is deliberately conservative: structural tells win first, so a real gap is never hidden behind a cosmetic verdict. The printed `REAL-GAP burndown` is `Partial + candidate-worse` (known) up to `+ uncertain` (worst case) — the lower bound is what to drive to zero before retiring the old emitter.

**Source grade** (`--grade-source`): the *quality* anchor, where the diff above is the *agreement* anchor. It grades both pipelines against the **original source** — fetched via the PDB's SourceLink (PDB pulled from the symbol server by the PE's CodeView GUID, source from the resolved URL, both cached) — because agreement with a frozen, imperfect emitter is not the same as being good. For each method where the two pipelines disagree, it scores candidate-vs-source and baseline-vs-source and reports which is closer, plus average similarity.

The similarity is a coarse token-bag measure and is deliberately understood to be **correctness-blind**: it can score a semantically-wrong rendering high on incidental token overlap (a spot-check found the baseline "winning" with an invalid `enum != null` because its `.Info()` call token-matched the source). So the mode prints a **per-bucket sample** — real source/candidate/baseline triples on each side of the verdict — alongside the aggregate. Read the number as a trend; read the examples as the audit. Methods with conditional-compilation directives in their source span (single-config IL ≠ multi-branch source) and compiler-generated methods are excluded and counted; the graded subset skews toward portable code. `--grade-cap N` bounds the sample; needs network for the symbol server and source host.

**Compile check** (`--compile-check`): the *validity* anchor — the diff is *agreement*, the source-grade is *quality*, this is *does it even compile*. The replacement pipeline guarantees by construction only that it never crashes and never silently fabricates (unrepresentable IL becomes a visible `/* … */` comment and drops fidelity to `Partial`) — **not** that the rendered text is valid C#. This mode measures the gap: each body is wrapped in a method shell carrying its real signature (return type, generic parameters, parameters, so locals/params/type-params and `this` all bind), then (1) parsed — a parse error is unambiguously a decompiler defect; (2) checked for statement legality (the CS0201 rule — a bare cast/expression statement parses but isn't valid); (3) bound against the runtime references. Diagnostics are bucketed by code with the member/type-**visibility** codes (the shell can't see the real declaring type's fields/methods) filtered as noise, so genuine defects stand out — `CS0193` (`*`-deref of a managed ref), `CS0175` (`base(...)` rendered as a statement), `CS1620` (an `out` argument not marked `out`), `CS0165` (a local used before the decompiler assigned it). Reported split by fidelity: a `Partial` method is *expected* to carry invalid fragments; a **`Full` method that fails to compile is the real "claimed good but isn't" signal** and the prioritized fix docket. Compiler-generated members are excluded (their metadata names aren't valid identifiers). `--compile-cap N` bounds the (slow) semantic-binding pass.

**Compile-back** (`--compile-back`): the *semantic-fidelity* anchor — the diff is *agreement*, the source-grade is *quality*, the compile-check is *validity*, and this is *does it still mean the same thing*. It closes the round trip named in [docs/decompiler-pipeline.md](../../docs/decompiler-pipeline.md): decompile → recompile → compare IL. A body that parses, binds, and reads plausibly but recompiles to a **different opcode stream changed the program** — the worst failure class ([docs/decompiler-taste.md](../../docs/decompiler-taste.md)), invisible to every other rail because they never run the output back through a compiler. Each member is recompiled inside a reconstructed **whole-module skeleton** — every top-level type stubbed (fields present, sibling and nested members as throwing stubs) with the one target carrying its real decompiled body, the C# analog of the IL round-trip suite's `IlasmScaffold`. With fields and sibling types in scope, a dropped or mis-bound field access surfaces as a true opcode diff rather than a bind error. The recompiled method is disassembled and its canonical opcode stream (short forms folded, `ldarg`/`ldloc`/`ldc.i4` families normalized) compared against the original; `Full`-fidelity diffs are the docket. References are the running runtime plus the target's sibling DLLs, minus the target itself (it is reconstructed, not referenced). Recompile failures here overlap `--compile-check` (an un-bindable body cannot be opcode-compared) and are reported separately, not as diffs. `CB_TYPE=<substr>` filters to a type; `CB_DUMP=1` prints the first failing compilation units. `--compile-cap N` bounds the (slow) recompile pass.

*When to use it.* Reach for compile-back when the question is **"is this decompilation faithful,"** not "does it compile." Run it after any change to the importer, a raising pass, or the printer that could alter emitted semantics — branch sense, checked/unchecked, conversions, field/local ordering, shift masking — and read the `Full` opcode-diff bucket as a regression docket. It is the tool that catches a fix in one method silently degrading another. Prefer the small, fast, purpose-built fixture corpus (`CfgSampleClass` in `ILInspector.Decompiler.Tests`) for a tight loop; sweep a real assembly (BCL) for breadth once the fixtures are clean. Use `--compile-check` first when you only need to know the output is valid C#, and `--candidate next` when comparing the two pipelines' text rather than meaning.

*The CI gate.* The console mode above is for exploration; the durable regression guard is `CompileBackGateTests` in `ILInspector.Decompiler.Tests`, which calls the same machinery through `CompileBack.Evaluate` (the non-printing, structured-result entry point) over `CfgSampleClass`. It fails CI when a method newly recompiles to a different opcode stream (a regression beyond the documented `KnownDiffs` docket) and when a previously-fixed method (`PinnedExact`) regresses. Shrink `KnownDiffs` as you fix docket entries; add the fixed method to `PinnedExact` to pin the fix.

**Stage dump** (`--dump 'Type::Method'`): JitDump for the decompiler — prints every stage of one method's analysis as projections of the shared `MethodAnalysis`: raw IL, typed IL (per-instruction stack states), structured IL (blocks and exception regions), then C#. The IL projections come from pre-transform stages, so the output is exactly what each pipeline layer saw. Add a sub-mode to narrow what `--dump` shows: `--steps`/`--step-limit` (per-pass fine-grained rewrites), `--facts` (the printer's definite-assignment `gen`/`in`/`out` sets that decide which locals keep `= default`), `--cfg` (per-block predecessor/successor edges), or `--diff` (each pass's effect as a unified `+`/`-` hunk over the previous stage).

## Usage

```bash
# CoreLib of the running runtime (default input)
dotnet run --project tools/DecompilerHarness -c Release

# Whole shared framework, with reports
dotnet run --project tools/DecompilerHarness -c Release -- \
  /path/to/shared/Microsoft.NETCore.App/11.0.0 \
  --report harness.md --json harness.json

# Replacement-pipeline inventory: fidelity histogram + stop-reason roadmap
dotnet run --project tools/DecompilerHarness -c Release -- --pipeline next

# Replacement-pipeline IR dump for one method
dotnet run --project tools/DecompilerHarness -c Release -- \
  --pipeline next --dump 'System.String::IsNullOrEmpty'

# Parity scoreboard: every comparable method through both pipelines
dotnet run --project tools/DecompilerHarness -c Release -- --candidate next

# Compile-back (semantic fidelity): decompile -> recompile -> compare IL.
# Tight loop over the purpose-built fixture corpus:
dotnet build src/ILInspector.Decompiler.Tests -c Release
dotnet run --project tools/DecompilerHarness -c Release -- --compile-back \
  artifacts/bin/ILInspector.Decompiler.Tests/release/ILInspector.Decompiler.Tests.dll
# Focus one type, dump the units that fail to recompile:
CB_TYPE=CfgSampleClass CB_DUMP=1 dotnet run --project tools/DecompilerHarness -c Release -- \
  --compile-back artifacts/bin/ILInspector.Decompiler.Tests/release/ILInspector.Decompiler.Tests.dll

# Stage-by-stage dump of one method (metadata type name)
dotnet run --project tools/DecompilerHarness -c Release -- \
  --dump 'System.Collections.Generic.Stack`1::Push'

# Introspect one method: definite-assignment facts, the CFG, or per-pass deltas
dotnet run --project tools/DecompilerHarness -c Release -- \
  /path/to/System.Private.CoreLib.dll --dump 'DecCalc::Div128By96' --facts
dotnet run --project tools/DecompilerHarness -c Release -- \
  /path/to/System.Private.CoreLib.dll --dump 'DecCalc::Div128By96' --cfg
dotnet run --project tools/DecompilerHarness -c Release -- \
  /path/to/System.Private.CoreLib.dll --dump 'DecCalc::Div128By96' --diff
```

Inputs are assembly paths or directories (non-managed files are skipped). Methods slower than 2s are listed in the report; true hangs stall the sweep visibly rather than being misreported by a nested-task timeout (CI applies job-level timeouts).

## Baseline

First full sweep (June 2026, .NET 11 preview 5 shared framework): 133,461/133,461 method bodies rendered, zero exceptions, ~6s wall clock.
