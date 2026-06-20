# Decompiler Quality

How `ILInspector.Decompiler` stays correct, and how it stays correct as the
raising passes evolve. The companion docs split the concern: [decompiler.md](decompiler.md)
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

## Checks, floors, and views

Correctness is anchored by construction plus weight of evidence — "pounds of
IL" — rather than per-expression semantic re-resolution. The verification surface
has a few distinct roles, and keeping them distinct is what makes "what proves
what" legible:

- **check** — renders a per-run verdict on each method: does *this* body compile,
  round-trip, or carry agreeing annotations? Each has a `--<property>-check` flag.
- **floor** — an aggregate threshold over the whole corpus, with no per-method
  verdict and no flag.
- **view** — shows you something with no verdict at all: `--gaps`, `--dump`,
  `--diff`, `--cfg`, `--facts`, `--remarks`, `--pass-impact`.
- **gate** — a check or floor wired into CI to hold a line. **oracle** — the
  reference of truth a check compares against (the original IL opcode stream).

### The three checks

Each is named for what it proves; a method can pass one and fail another, so they
are not redundant:

| Check | Question | Proves | Blind to |
| --- | --- | --- | --- |
| `--fidelity-check` | *Does it still mean the same thing?* | Semantic **fidelity**: decompile → recompile → compare the canonical opcode stream | Methods it cannot recompile (reported separately, not as diffs) |
| `--validity-check` | *Does it even compile?* | **Validity**: the rendered C# parses, is statement-legal, and binds | Whether valid C# is *faithful* (fidelity's job) |
| `--annotation-check` | *Do the IL annotations match the opcodes?* | **Annotation fidelity**: each allocation/unsafety/lifetime annotation agrees with the raw IL opcode at its offset (precision), and every unambiguous opcode produces its annotation (recall) | Whether the C# itself is right — only the annotations |

The deepest is **fidelity**: a body that compiles and reads plausibly but
recompiles to a different opcode stream changed the program — the worst failure
class, invisible to every check that never runs the output back through a
compiler. The supporting evidence:

- **The IL round-trip oracle.** Our disassembly reassembles (vendored managed
  ILAssembler, native `ilasm`) to byte-identical IL — the ground truth fidelity
  grades against.
- **Fixtures in both configurations.** Purpose-built methods whose *compilation*
  produces the IL shape under test, run in Debug *and* Release (the compiler
  emits structurally different IL per configuration; CI runs both).
- **Corpus sweeps.** Emit-all stress over each platform's CoreLib (three OSes in
  CI = three corpora), measured by the floors below.

### Fixture idiom-shape scorecard

`IdiomShapeScorecardTests` is the fixture-backed C# altitude check. It renders
raised fixture bodies, parses the output with Roslyn, and asserts that the
expected syntax nodes appear (`TupleExpressionSyntax`, `UsingStatementSyntax`,
`LockStatementSyntax`, etc.) while lower-altitude substitutes stay absent. This
is deliberately not a compile/fidelity check: valid C# can still be the wrong
idiom, such as a switch expression recovered as a switch statement.

Run it after changing a raising pass or printer path that can affect C# shape.
The current ratchet records the fixture idioms that are recovered today; when an
owed fixture starts passing, flip its scorecard entry to recovered in the same PR
so the aggregate score moves forward and future regressions fail.

The scorecard is a positive altitude signal, not a soundness proof. A raise that
recovers the target idiom can still be too broad, so each new or expanded raise
should also add at least one adversarial fixture when a nearby non-idiom shape is
plausible. Good negative fixtures include hand-written spellings that resemble
the lowering, older-C# equivalents, unsigned/null/pattern variants, and source
that differs only in an important compiler discriminator such as a receiver spill.
Pin those in pass-level tests or the fidelity gate; the scorecard keeps the
positive ratchet.

The intended pass-improvement loop is:

1. Pick a ledger-owned raise pass or owed idiom.
2. Add or identify fixture methods that represent the source idiom.
3. Add adversarial fixtures for nearby shapes that must not raise.
4. Add scorecard entries as unrecovered when the current output is lower altitude.
5. Run the scorecard to capture the baseline.
6. Implement or improve the raise pass.
7. Run the scorecard again and flip newly recovered entries in the same PR.
8. Run fidelity/validity checks to prove the higher-altitude shape stayed
   semantic, valid C#.

After a raise looks good, use an adversarial fixture pass before merge. Give an
agent the pass, its intended discriminator, the positive fixtures, and the
soundness checklist below; ask it to add or update fixtures that try to falsify
the match without changing the pass first. A useful run either finds a false
positive that needs a narrower discriminator, or adds negative fixtures proving
the pass rejects the nearest lookalikes.

### The two floor-only properties

Two properties have no per-method check at all — they exist only as corpus
floors, enforced by `CorpusSweepGateTests`:

- **completeness** — the fully-raised %, surfaced by the `--gaps` *view*. A method
  is incomplete iff its raised tree still holds unstructured control flow (a
  surviving `goto`) or an `UnsupportedNode`. `--gaps` measures the residual; it
  renders no per-run verdict, which is why it is a view, not a check.
- **pass-safety** — zero pass-bugs / exceptions over the whole corpus, pinning the
  by-construction safety. No flag; observed only in aggregate.

(Fidelity, being a check, *also* contributes an aggregate floor to the same
sweep — its corpus `Full`-fidelity % — but unlike these two it has a per-method
form. So `CorpusSweepGateTests` enforces three thresholds: pass-safety, the
`Full`-fidelity floor, and the completeness floor.)

## What gates CI

The durable, blocking guard is the **fixture fidelity gate**:
`FidelityGateTests` (and its lowered twin `LoweredFidelityGateTests`)
decompile every method of `CfgSampleClass`, recompile each inside a reconstructed
type skeleton, and fail CI when a method newly recompiles to a different opcode
stream — a regression beyond the documented `KnownDiffs` docket — or when a
`PinnedExact` method (a previously-fixed one) regresses. Shrinking `KnownDiffs`
and growing `PinnedExact` is how fidelity progress ratchets forward and cannot
slip back. The **annotation gate** (`AnnotationGateTests`) holds annotation
fidelity over the whole CoreLib corpus the same way — precision is absolute (a
wrong fact always fails; it is never runtime drift), recall is held above a
floor. The decompiler unit suite (`ILInspector.Decompiler.Tests`) and the IL
round-trip sweep (`DotnetInspector.ILRoundtrip.Tests`) gate the importer, the
passes, and the disassembler.

Breadth is gated separately by `CorpusSweepGateTests` (next section), which
enforces health **floors** — pass-safety, the aggregate `Full`-fidelity %, and
completeness — over the whole CoreLib corpus. The fixture and annotation gates
are the depth signals; the
corpus sweep is the breadth signal. The exploratory corpus checks and views
(`--fidelity-check`/`--validity-check` over a real assembly, the `--gaps` view,
`--pass-impact`) stay **developer-driven** — run them while working and read
them in review, but only the gates and the sweep's floors block CI.

## The corpus breadth gate

The fixture gate is strong but narrow (~80 curated methods), and the corpus
checks and views above are manual. The breadth net is **`CorpusSweepGateTests`** — the
new-pipeline analog of the old stack's no-crash sweep, made objective:

- It runs every method of the running runtime's CoreLib through `IrImporter →
  IrPasses` and the fidelity/gap classification (the SDK pins the version, so the
  corpus is stable). Cheap — no recompile.
- It asserts **floors, not exact baselines**: zero pass-bugs / exceptions
  (pass-safety), the `Full`-fidelity % (the fidelity check's aggregate floor), and
  the fully-raised % (completeness) above ratchets a couple points below the
  measured numbers. Floors tolerate minor
  runtime-version drift and need no per-method baseline file — which is why this
  beats both fuzzy text agreement and a brittle exact-baseline net.

So a crash, or a broad fidelity/raising drop, fails CI, using only the
self-contained signals. When the structuring work raises the true numbers, the
floors ratchet up to lock the gain in. Beyond breadth, deepening *fidelity*
coverage means growing the fidelity fixture corpus, not widening the sweep.

## The quality loop: detect, then diagnose

The harness modes pair into a loop, both ends anchored on the **same final C#**
the product emits:

- **Detect at scale.** `--fidelity-check` finds *which* methods regressed (opcode
  diffs across an assembly); `--gaps` finds which lost completeness;
  `--pass-impact` shows a pass's blast radius before and after a change.
- **Diagnose one.** `--dump` (with `--diff`, `--facts`, `--cfg`, `--remarks`)
  drills into the per-pass IR of a single method to find which pass introduced
  the divergence; `--steps` / `--step-limit` narrows to a single rewrite.

The connection is load-bearing: the final stage `--dump` shows is byte-identical
to what `--fidelity-check` grades, so there is no drift between what you inspect
and what is measured.

Two gotchas that save head-scratching when diagnosing:

- **Call-site defects surface at callers, not at the definition.** Dumping a
  method's own definition (e.g. `System.AppContext::TryGetSwitch`) can report
  `fidelity: Full` because its callees are MethodDefs in the same assembly; a
  `DEC0007` ref-kind loss only appears when you dump a cross-assembly *caller*.
  Dump the call site, not the target, to reproduce a call-shape defect.
- **`--skip-pdb` does not affect fidelity.** Local names are cosmetic — they
  never change emitted IL — so `--fidelity-check` is unaffected by whether names
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
