# Decompiler Harness

The diagnostic harness from [docs/decompiler.md](../../docs/decompiler.md) — the asmdiffs analog for the decompiler. It inventories the pipeline's health, scores the real-gap completeness, validates output two ways, and dumps a single method through every pipeline stage. This is the invocation reference for the modes; the strategy they serve — which check proves what, what gates CI, the corpus-sweep plan — is [docs/decompiler-quality.md](../../docs/decompiler-quality.md).

## Modes

**Inventory** (default): sweeps every method body in the given assemblies through the pipeline and reports the fidelity histogram plus stop-reason buckets — the prioritized slice roadmap. Exits nonzero if any importer bug (DEC0001) appears.

**Gaps** (`--gaps`): the completeness view — see below.

**Validity check** (`--validity-check`): the *validity* check — `--gaps` is *completeness*, `--fidelity-check` is *fidelity*, this is *does it even compile*. The pipeline guarantees by construction only that it never crashes and never silently fabricates (unrepresentable IL becomes a visible `/* … */` comment and drops fidelity to `Partial`) — **not** that the rendered text is valid C#. This mode measures the gap: each body is wrapped in a method shell carrying its real signature (return type, generic parameters, parameters, so locals/params/type-params and `this` all bind), then (1) parsed — a parse error is unambiguously a decompiler defect; (2) checked for statement legality (the CS0201 rule — a bare cast/expression statement parses but isn't valid); (3) bound against the runtime references. Diagnostics are bucketed by code with the member/type-**visibility** codes (the shell can't see the real declaring type's fields/methods) filtered as noise, so genuine defects stand out — `CS0193` (`*`-deref of a managed ref), `CS0175` (`base(...)` rendered as a statement), `CS1620` (an `out` argument not marked `out`), `CS0165` (a local used before the decompiler assigned it). Reported split by fidelity: a `Partial` method is *expected* to carry invalid fragments; a **`Full` method that fails to compile is the real "claimed good but isn't" signal** and the prioritized fix docket. Compiler-generated members are excluded (their metadata names aren't valid identifiers). `--compile-cap N` bounds the (slow) semantic-binding pass.

**Fidelity check** (`--fidelity-check`): the *semantic-fidelity* check — `--gaps` is *completeness*, the validity check is *validity*, and this is *does it still mean the same thing*. It closes the round trip named in [docs/decompiler.md](../../docs/decompiler.md): decompile → recompile → compare IL. A body that parses, binds, and reads plausibly but recompiles to a **different opcode stream changed the program** — the worst failure class ([docs/decompiler-taste.md](../../docs/decompiler-taste.md)), invisible to every other check because they never run the output back through a compiler. Each member is recompiled inside a reconstructed **whole-module skeleton** — every top-level type stubbed (fields present, sibling and nested members as throwing stubs) with the one target carrying its real decompiled body, the C# analog of the IL round-trip suite's `IlasmScaffold`. With fields and sibling types in scope, a dropped or mis-bound field access surfaces as a true opcode diff rather than a bind error. The recompiled method is disassembled and its canonical opcode stream (short forms folded, `ldarg`/`ldloc`/`ldc.i4` families normalized) compared against the original; `Full`-fidelity diffs are the docket. References are the running runtime plus the target's sibling DLLs, minus the target itself (it is reconstructed, not referenced). Recompile failures here overlap `--validity-check` (an un-bindable body cannot be opcode-compared) and are reported separately, not as diffs. `CB_TYPE=<substr>` filters to a type; `CB_DUMP=1` prints the first failing compilation units. `--compile-cap N` bounds the (slow) recompile pass.

*When to use it.* Reach for fidelity check when the question is **"is this decompilation faithful,"** not "does it compile." Run it after any change to the importer, a raising pass, or the printer that could alter emitted semantics — branch sense, checked/unchecked, conversions, field/local ordering, shift masking — and read the `Full` opcode-diff bucket as a regression docket. It is the tool that catches a fix in one method silently degrading another. Prefer the small, fast, purpose-built fixture corpus (`CfgSampleClass` in `ILInspector.Decompiler.Tests`) for a tight loop; sweep a real assembly (BCL) for breadth once the fixtures are clean. Use `--validity-check` first when you only need to know the output is valid C#, and `--gaps` to track the structuring completeness.

*The CI gate.* The console mode above is for exploration; the durable regression guard is `FidelityGateTests` in `ILInspector.Decompiler.Tests`, which calls the same machinery through `FidelityCheck.Evaluate` (the non-printing, structured-result entry point) over `CfgSampleClass`. It fails CI when a method newly recompiles to a different opcode stream (a regression beyond the documented `KnownDiffs` docket) and when a previously-fixed method (`PinnedExact`) regresses. Shrink `KnownDiffs` as you fix docket entries; add the fixed method to `PinnedExact` to pin the fix. `LoweredFidelityGateTests` is the twin gate for the lowered view (`--lowered`), with its own docket — the lowered C# is recompiled and opcode-compared the same way, so both official C# views earn a per-view E2E roundtrip.

**Stage dump** (`--dump 'Type::Method'`): JitDump for the decompiler — runs one method through the pipeline and prints the IR tree at every stage boundary (the importer output, then after each raising pass), ending in the shipped product C# (`PrintRaised`). So the output is exactly what each pass left behind. When a name resolves to several overloads, `--dump` selects index `0` but prints the overload menu (index, signature, body/no-body) to stderr so you can see what was chosen and pick another with `--index N` (stdout stays pipe-clean); `--list-overloads` prints that menu and stops. Add a sub-mode to narrow what `--dump` shows: `--steps`/`--step-limit` (per-pass fine-grained rewrites), `--facts` (the printer's definite-assignment `gen`/`in`/`out` sets that decide which locals keep `= default`), `--cfg` (per-block predecessor/successor edges; add `--mermaid` for a GitHub-renderable flowchart), `--diff` (each pass's effect as a unified `+`/`-` hunk over the previous stage), or `--remarks` (every IR site that caps the method below `Full` fidelity, with its `DEC####` code, block offset, and reason).

**Lowered view** (`--lowered`): a render selector, orthogonal to the dump sub-modes above, that lowers the *altitude* of the emitted C# rather than projecting a different analysis. It runs `IrPasses.Lowered` — the shipped pipeline minus the cosmetic statement-sugar passes (`for`/`foreach`, `lock`, `++`/`--`) — so the output is the decompiler's SharpLab "lowered C#": valid, recompilable C# at a lower level (`while` loops, explicit temps, explicit `Monitor.Enter`/`Exit`). It applies to `--dump` (with facts comments), `--validity-check --lowered` (its compile rate), and `--fidelity-check --lowered` (its opcode roundtrip).

**Simulate new rules** (`--simulate-new-rules`, with `--dump`): the optimistic memory-safety render selector — another render dial orthogonal to the dump sub-modes, but it changes *which unsafe contexts are emitted* rather than the C# altitude. By default the printer is conservative: it emits explicit `unsafe { }` blocks only for a module that opted into the `updated-memory-safety-rules` feature (a module-level `MemorySafetyRulesAttribute`), so legacy output is byte-identical. With this flag it forces new-rules rendering on for *any* input, wrapping the operations the new rules would require even in a legacy module. It only recovers contexts the binary still records — IL-visible ops (`*p`, `calli`, `stackalloc`+`SkipLocalsInit`), pointer-in-signature calls, and a cross-assembly `[RequiresUnsafe]` callee (the attribute lives in the opted-in callee's assembly, read through the shared `MetadataContext`). A legacy same-assembly pointerless `unsafe` method leaves no trace, so simulate honestly emits no block for it. The conservative vs. optimistic contract and its recoverability limits are [docs/design/memory-safety-modes.md](../../docs/design/memory-safety-modes.md).

**Pass impact** (`--pass-impact [pass]`): the corpus-wide *inverse* of `--dump --diff`. `--diff` answers "for this method, what did each pass do"; `--pass-impact` answers "for this pass, which methods does it change" — its blast radius across an assembly. With no pass named it prints a histogram (each pass and the count of methods it altered, the "which passes carry the load" roadmap); with a pass name it lists every method that pass changed. Add `--show-diff` to print each changed method's per-pass hunk beneath it. `--cap N` stops the sweep after `N` methods — a full-CoreLib stage sweep is not free, so cap it for a quick read. A pass that runs more than once in the pipeline (`typed-constants`, `expression-inlining`) counts a method once if any occurrence changed it.

**Gaps** (`--gaps`): the *self-contained* real-gap view. It inspects only the raised tree: a method is a gap iff it still holds **unstructured control flow** — a `Branch`/`ConditionalBranch`/`SwitchBranch` the structuring passes could not consume, or an EH `Leave` (a surviving `goto`) — or an `UnsupportedNode`. A fully-raised tree holds only structured nodes (`IfStatement`, loops, `Switch`, `TryCatch`), so the residual is exact: reading the tree alone tells you the gap, no recompile or comparison needed. It reports "fully raised" (the metric to drive up) and a residual-kind docket (the prioritized work). It measures completeness, not correctness, so pair it with `--fidelity-check` for fidelity.

*When to use it.* Track the structuring completeness with `--gaps`. Over CoreLib it currently reads ~96% fully raised, the residual dominated by `structuring: conditional-branch` (the forward-branch-to-common-exit work). Add `--by-shape` to sub-classify the `switch-branch` bucket by the structural shape of its residual switch (e.g. `switch-not-block-terminator`, `case-branches-to-shared-terminator-case`) — a bucket count becomes a per-shape slice docket that scopes the next `SwitchRaisingPass` relaxation.

**Annotation check** (`--annotation-check`): the hidden-fact annotation check — the analyzer analog of `--fidelity-check`. Where fidelity check grades the decompiler's *C#* against a recompiled opcode stream, this grades each *annotation* (the allocation/unsafety/lifetime facts from [docs/design/hidden-fact-annotations.md](../../docs/design/hidden-fact-annotations.md)) against the raw IL opcode it claims to describe. The witness is read with the runtime-ported `ILReader` directly over the method's IL bytes — **not** via the IR importer that produced the annotations — so the two paths share only that externally byte-match-validated reader, never the semantic classification logic under test. It measures two directions: **precision** (every annotation's offset carries a consistent opcode — an `alloc.box` sits on a `box`; a violation is an importer-typing or classifier bug) and **recall** (every *unambiguous* witness opcode produced its annotation — a `box`/`newarr`/`localloc`/`calli`, plus every confirmed reference-type `newobj`, always yields its fact). A `newobj`'s constructed type is resolved from metadata (operand token → constructor → declaring-type base chain, or a TypeSpec signature) independently of the importer; the ambiguous remainder stays precision-only and out of the recall gate: a value-type `newobj` (a struct constructor) allocates nothing, a bare cross-assembly `TypeRef` can't be resolved from a single-assembly walk (the documented value-type gap), and a `ldind`/`stind` may be a safe managed-`ref` access. A confirmed value-type `newobj` is additionally held to the *opposite* precision rule — it must **not** carry an allocation fact — catching a false-allocation claim the opcode-precision check is blind to. Recall also excludes partial-import methods, where a stop legitimately leaves later opcodes with no IR node. Exits nonzero on any precision violation (a wrong fact) or import crash.

*When to use it.* Run after any change to the classifiers (`AllocationClassifier`/`UnsafetyClassifier`/`LifetimeClassifier`), or to the importer's typing/metadata layer the classifiers read (value-type hints, signature decoding). A precision drop on a category points straight at the bug. Over .NET 11 preview CoreLib it currently reads **100% precision** (all descriptors plus the value-type-newobj no-allocation checks) and **100% recall** (the gated witnesses, including ~9k confirmed reference-type newobjs).

*The CI gate.* The console mode above is for exploration; the durable regression guard is `AnnotationGateTests` in `ILInspector.Decompiler.Tests`, which calls the same machinery through `AnnotationCheck.Evaluate` (the non-printing, structured-result entry point) over the running runtime's CoreLib. It is the breadth gate (analog of `FidelityGateTests`, the fixture depth gate): it fails CI on any precision violation (a wrong fact, always a bug — never runtime drift, so gated absolutely) or import crash, holds recall above a floor, and asserts a large checked population so a refactor that silently stops producing annotations cannot pass vacuously.

## Usage

```bash
# CoreLib of the running runtime (default input)
dotnet run --project tools/DecompilerHarness -c Release

# Whole shared framework: fidelity histogram + stop-reason roadmap
dotnet run --project tools/DecompilerHarness -c Release -- \
  /path/to/shared/Microsoft.NETCore.App/11.0.0

# IR dump for one method
dotnet run --project tools/DecompilerHarness -c Release -- \
  --dump 'System.String::IsNullOrEmpty'

# Compile-back (semantic fidelity): decompile -> recompile -> compare IL.
# Tight loop over the purpose-built fixture corpus:
dotnet build src/ILInspector.Decompiler.Tests -c Release
dotnet run --project tools/DecompilerHarness -c Release -- --fidelity-check \
  artifacts/bin/ILInspector.Decompiler.Tests/release/ILInspector.Decompiler.Tests.dll
# Focus one type, dump the units that fail to recompile:
CB_TYPE=CfgSampleClass CB_DUMP=1 dotnet run --project tools/DecompilerHarness -c Release -- \
  --fidelity-check artifacts/bin/ILInspector.Decompiler.Tests/release/ILInspector.Decompiler.Tests.dll

# Stage-by-stage dump of one method (metadata type name)
dotnet run --project tools/DecompilerHarness -c Release -- \
  --dump 'System.Collections.Generic.Stack`1::Push'

# Introspect one method: definite-assignment facts, the CFG, or per-pass deltas
dotnet run --project tools/DecompilerHarness -c Release -- \
  /path/to/System.Private.CoreLib.dll --dump 'DecCalc::Div128By96' --facts
dotnet run --project tools/DecompilerHarness -c Release -- \
  /path/to/System.Private.CoreLib.dll --dump 'DecCalc::Div128By96' --cfg
dotnet run --project tools/DecompilerHarness -c Release -- \
  /path/to/System.Private.CoreLib.dll --dump 'DecCalc::Div128By96' --cfg --mermaid
dotnet run --project tools/DecompilerHarness -c Release -- \
  /path/to/System.Private.CoreLib.dll --dump 'DecCalc::Div128By96' --diff
dotnet run --project tools/DecompilerHarness -c Release -- \
  /path/to/System.Private.CoreLib.dll --dump 'System.TypedReference::GetTargetType' --remarks

# Lowered C# view (de-sugared but valid): dump, validity check, or fidelity check roundtrip
dotnet run --project tools/DecompilerHarness -c Release -- \
  /path/to/System.Private.CoreLib.dll --dump 'DecCalc::Div128By96' --lowered
dotnet run --project tools/DecompilerHarness -c Release -- --fidelity-check --lowered \
  artifacts/bin/ILInspector.Decompiler.Tests/release/ILInspector.Decompiler.Tests.dll

# Optimistic "simulate" render: force new memory-safety rules on a legacy module,
# so unsafe { } blocks appear where the new rules would require them. Referenced
# DLLs must sit beside the opened assembly (the default locator probes siblings),
# so a cross-assembly [RequiresUnsafe] callee can be resolved.
dotnet run --project tools/DecompilerHarness -c Release -- \
  artifacts/bin/ILInspector.Decompiler.Fixtures.UnsafeChainC/release/ILInspector.Decompiler.Fixtures.UnsafeChainC.dll \
  --dump 'ILInspector.Decompiler.Fixtures.UnsafeChainC.Program::CallChain' --lowered --simulate-new-rules

# Pass impact (blast radius — inverse of --dump --diff)
# Histogram: how many methods each pass changes (cap the sweep for a quick read)
dotnet run --project tools/DecompilerHarness -c Release -- \
  /path/to/System.Private.CoreLib.dll --pass-impact --cap 3000
# One pass: list every method it changed, with the per-method hunk
dotnet run --project tools/DecompilerHarness -c Release -- \
  /path/to/System.Private.CoreLib.dll --pass-impact return-merge --show-diff --cap 3000

# Self-contained completeness view (the completeness signal)
dotnet run --project tools/DecompilerHarness -c Release -- \
  /path/to/System.Private.CoreLib.dll --gaps

# Hidden-fact annotation check: precision + recall of the annotations vs raw IL
dotnet run --project tools/DecompilerHarness -c Release -- \
  /path/to/System.Private.CoreLib.dll --annotation-check
```

Inputs are assembly paths or directories (non-managed files are skipped).

## Baseline

Over .NET 11 preview 5 `System.Private.CoreLib` (~41k methods): the inventory imports at high `Full` fidelity, `--gaps` reads ~96% fully raised — the residual is the structuring completeness docket — and `--annotation-check` reads 100% precision and 100% recall (over ~19.4k graded annotations and ~14.5k recall witnesses).
