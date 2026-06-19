# Decompiler Quality

How `ILInspector.Decompiler` stays correct, and how it stays correct as the
raising passes evolve. The companion docs split the concern: [decompiler-pipeline.md](decompiler-pipeline.md)
is the architecture (*how* output is produced), [decompiler-taste.md](decompiler-taste.md)
is *what* to render. This doc is the goal — *how we know the output is right*.
Tool invocation lives in the harness reference ([tools/DecompilerHarness/README.md](../tools/DecompilerHarness/README.md));
here we describe the strategy those tools serve.

## Correct by construction

The pipeline's floor is set in its shape, before any test runs:

- **No crash, no silent wrong output.** `IrImporter.Import` and
  `CSharpPrinter.PrintRaised` are exception-safe by construction. A one-method
  bug surfaces as a diagnostic comment and a lowered fidelity level, never a
  process crash or plausible-but-wrong text.
- **Honest degradation.** IL with no C# spelling becomes an explicit
  `UnsupportedNode` rendered as a visible `/* … */` comment, and the result's
  fidelity level drops accordingly (`Full` → `Partial` → `StructuredOnly` →
  `IlOnly` → `Failed`). Output never pretends to be more faithful than it is.
- **Machine-readable diagnostics.** Every degradation carries a stable
  Roslyn-style `DEC####` code with prose alongside, so CI triage and fallback
  routing read identifiers, not strings.

Everything below verifies that the output *above* the floor — the `Full`-fidelity
C# we claim is faithful — actually is.

## The three rails

Correctness is anchored by construction plus weight of evidence — "pounds of
IL" — rather than per-expression semantic re-resolution. Three independent rails
measure three different questions; a method can pass one and fail another, so
they are not redundant:

| Rail | Question | Proves | Blind to |
| --- | --- | --- | --- |
| `--compile-back` | *Does it still mean the same thing?* | Semantic **fidelity**: decompile → recompile → compare the canonical opcode stream | Methods it cannot recompile (reported separately, not as diffs) |
| `--gaps` | *Is the tree fully raised?* | **Completeness**: a method is a gap iff its raised tree still holds unstructured control flow or an `UnsupportedNode` | Whether a fully-raised method is *correct* — only that it is structured |
| `--compile-check` | *Does it even compile?* | **Validity**: the rendered C# parses, is statement-legal, and binds | Whether valid C# is *faithful* (compile-back's job) |

The deepest is **compile-back**: a body that compiles and reads plausibly but
recompiles to a different opcode stream changed the program — the worst failure
class, invisible to every rail that never runs the output back through a
compiler. The supporting evidence:

- **The IL round-trip oracle.** Our disassembly reassembles (vendored managed
  ILAssembler, native `ilasm`) to byte-identical IL — the ground truth
  compile-back grades against.
- **Fixtures in both configurations.** Purpose-built methods whose *compilation*
  produces the IL shape under test, run in Debug *and* Release (the compiler
  emits structurally different IL per configuration; CI runs both).
- **Corpus sweeps.** Emit-all stress over each platform's CoreLib (three OSes in
  CI = three corpora). `--gaps` and `--compile-check` measure the sweep two
  ways; any unexpected delta on a decompiler change is a finding.

## What gates CI

The durable, blocking guard is the **fixture compile-back gate**:
`CompileBackGateTests` (and its lowered twin `LoweredCompileBackGateTests`)
decompile every method of `CfgSampleClass`, recompile each inside a reconstructed
type skeleton, and fail CI when a method newly recompiles to a different opcode
stream — a regression beyond the documented `KnownDiffs` docket — or when a
`PinnedExact` method (a previously-fixed one) regresses. Shrinking `KnownDiffs`
and growing `PinnedExact` is how fidelity progress ratchets forward and cannot
slip back. The decompiler unit suite (`ILInspector.Decompiler.Tests`) and the IL
round-trip sweep (`DotnetInspector.ILRoundtrip.Tests`) gate the importer, the
passes, and the disassembler.

The corpus rails (`--gaps`, `--compile-check`, `--compile-back` over a real
assembly, `--pass-impact`) are **developer-driven**, not gates: run them while
working, read them in review. They are the breadth signal; the fixture gate is
the depth signal.

## The corpus gap, and the plan

The fixture gate is strong but narrow (~80 curated methods). The breadth signals
are manual. That leaves one gap: **nothing in CI runs the whole CoreLib corpus
through the pipeline.** The old stack carried a `PlatformAssembly_AllMethods_NoCrashes`
sweep; it was deleted with that stack and has no replacement yet.

The plan of record is a **corpus-sweep ratchet test** — the new-pipeline analog
of the deleted sweep, made objective:

- Run every method of the running runtime's CoreLib through `IrImporter →
  IrPasses → CSharpPrinter` (the SDK pins the version, so the corpus is stable).
- Assert **floors, not exact baselines**: zero pass-bugs / exceptions (pins the
  by-construction safety), and `Full`-fidelity % and fully-raised % above a
  ratchet a couple points below today's numbers. Floors tolerate minor version
  drift and need no per-method baseline file to maintain — which is why this
  beats both a fuzzy text-agreement oracle and a brittle exact-baseline net.
- Cheap: import + passes + the `--gaps` / fidelity classification, no recompile.

It restores the broad regression net (a crash or a broad fidelity/raising drop
fails CI) using only the self-contained signals we already have. Beyond it,
deepening *fidelity* coverage means growing the compile-back fixture corpus, not
widening the sweep.

## The quality loop: detect, then diagnose

The harness modes pair into a loop, both ends anchored on the **same final C#**
the product emits:

- **Detect at scale.** `--compile-back` finds *which* methods regressed (opcode
  diffs across an assembly); `--gaps` finds which lost completeness;
  `--pass-impact` shows a pass's blast radius before and after a change.
- **Diagnose one.** `--dump` (with `--diff`, `--facts`, `--cfg`, `--remarks`)
  drills into the per-pass IR of a single method to find which pass introduced
  the divergence; `--steps` / `--step-limit` narrows to a single rewrite.

The connection is load-bearing: the final stage `--dump` shows is byte-identical
to what `--compile-back` grades, so there is no drift between what you inspect
and what is measured.

Two gotchas that save head-scratching when diagnosing:

- **Call-site defects surface at callers, not at the definition.** Dumping a
  method's own definition (e.g. `System.AppContext::TryGetSwitch`) can report
  `fidelity: Full` because its callees are MethodDefs in the same assembly; a
  `DEC0007` ref-kind loss only appears when you dump a cross-assembly *caller*.
  Dump the call site, not the target, to reproduce a call-shape defect.
- **`--skip-pdb` does not affect fidelity.** Local names are cosmetic — they
  never change emitted IL — so `--compile-back` is unaffected by whether names
  were recovered. `--skip-pdb` only changes the *spelling* in a dump (`V_n` vs
  `i`/`j`), for deterministic, symbol-independent reading.

## Soundness checklist for IR-mutating passes

Reviews of the raising passes converge on three recurring questions; an author
who answers them before requesting review collapses the serial round-trips. Any
pass that detaches, replaces, or rewrites IR nodes states its answers in the PR
(a short "Soundness" note), and the review verifies them rather than
rediscovering them:

1. **Prove preconditions whole-function, not locally.** A rewrite that removes a
   node's defining store (or any binding) must prove the affected locals, slots,
   and stack positions are referenced *only* by the nodes it consumes — scanned
   across the whole function, not just the matched neighbourhood. Hand-written or
   obfuscated IL can wear the shape without being the pattern.
2. **Preserve semantics exactly.** A conversion, comparison, or identity match
   keeps checked/unsigned/overflow behaviour and produces valid C# (no CS-error
   spellings). Match metadata members on assembly identity and exact signature,
   not just namespace/name and call-site argument shapes.
3. **Isolate per-item failure.** A sweep over many methods (or assemblies) guards
   each item so one malformed input yields a diagnostic, not a lost batch;
   results that escape their source hold no live readers.

A proposed change should arrive with: the IL shape it targets, the argument for
its class under the taste doc's three-class rule, a fixture covering both
configurations, and a `--pass-impact` blast-radius read showing exactly the
intended changes across the corpus. The default first reviewer is the author —
run the high-effort self-review over the branch before opening the PR.
