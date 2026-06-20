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

As the scorecard saturates, adversarial research becomes more valuable, not
less. A high recovered ratio means the curated positives are mostly climbing; it
does **not** mean the raises are sound around their edges. Do not add easy
scorecard rows just to keep the number moving. Once the obvious positive climbs
are recovered, shift more target selection toward adversarial passes over recent
or broad raises and toward hardening `Partial` ledger rows.

### Choosing high-value targets

Pick targets for value, not just availability. Use this vocabulary in planning
and PRs so the work stays tied to visible progress:

| Target kind | What it means | Good signal |
| --- | --- | --- |
| **Scorecard climb** | Raise a visible source idiom to a higher C# altitude, or shrink an owed ledger row. | A scorecard/ledger row changes, a floor improves, or the output diff is source users recognize. |
| **Bug hunt** | Fix demonstrated wrong, invalid, or fidelity-breaking output. | A negative fixture or corpus method fails before the change and passes after. |
| **Adversarial pass** | Try to falsify a recent or fragile raise with near-miss negative fixtures. | A positive/negative pair differs by one compiler discriminator. |
| **Predicate hardening** | Move exact rewrite-gate evidence into `MemberIdentity`, `GeneratedCodeIdentity`, or `PlaceIdentity`. | A concrete pass customer exists, a duplicated check is drifting, or fuzzy matching already caused a bug. |

Terminology matters. Use **decompiler substrate** for the broad family of shared
pass-evidence layers. Use **identity predicates** for the exact rewrite gates the
passes call (`MemberIdentity`, `GeneratedCodeIdentity`, `PlaceIdentity`). Avoid
**fact substrate** in quality docs: "facts" already names hidden-fact
annotations and sidecar ledger metadata, while these predicates are private
rewrite gates.

A high-value target has at least one of these signals:

- It is a scorecard climb users recognize in source (`await`, `foreach`,
  tuple/deconstruction, interpolation, using/lock, switch/range).
- It fixes a demonstrated false-positive or fidelity/validity failure, ideally
  with a negative fixture that fails before the change.
- It strengthens an adversarial pass for an active or recently changed raise.
- It consolidates identity predicates with a concrete consumer, especially after
  an adversarial review found the same category of bug.
- It improves a CI gate or measured corpus floor, not just formatting of an
  already-correct shape.

Prefer work in this order: correctness bug > validity failure > scorecard climb
> predicate hardening with a concrete consumer > adversarial pass for an active
raise > cosmetic readability. After a burst of obvious predicate wins, bias back
toward scorecard climbs and adversarial passes. Avoid speculative helpers,
one-off cleanup, or broad rewrites that do not move a measured signal. When
choosing among similar targets, pick the one with the clearest discriminator and
the smallest adversarial fixture pair.

Read progress as **leverage**, not elapsed time. The ledger gives breadth: `None`
rows are owed mechanisms, `Partial` rows are the live frontier, and `Full` rows
are solved enough for the current lowering category. The scorecard gives
altitude on curated fixtures: a climb means a recognizable source idiom is back,
not that soundness has been proven for every nearby lowering. A strong progress
review asks whether a row moved from `None` to `Partial`/`Full`, a `Partial` note
got narrower, a scorecard fixture climbed, an adversarial pass added a useful
near-miss, predicate hardening removed a fuzzy rewrite gate, or a corpus floor
improved without losing fidelity.

Use the ledger and scorecard together, but do not optimize either one in
isolation. The ledger selects terrain; the scorecard pins a visible altitude
target; adversarial passes, fidelity/validity checks, and corpus floors prove
the climb did not over-match. Do not add easy scorecard cases just to inflate the
ratio, and do not mark a ledger row `Full` until fixtures and adversarial shapes
cover the meaningful variants of that lowering. A `Partial` row with a sharper
note, stronger fixtures, and safer predicates is real progress even if the raw
bucket count does not change.

Treat a near-full scorecard as a mode switch. The default question changes from
"can we recover this idiom at all?" to "where could this recovered idiom
over-match?" Good follow-up targets are recent broad raises, pass families with
many `Partial` notes, and places where a single missing discriminator could turn
source-like output into different IL.

The intended pass-improvement loop is:

1. Pick one high-value target type: a **scorecard climb** (new or improved raise
   for an owed/recoverable idiom), a **bug hunt** (demonstrated wrong output), an
   **adversarial pass** (try to falsify an existing raise), or an
   **predicate hardening** task (shared exact evidence with a concrete pass
   customer). Apply the high-value filter above before starting; do not spend a
   PR on a target that lacks a measurable signal or a concrete consumer.
2. Create a dedicated feature worktree for the raise task. Raise work touches
   common hotspots, so do not work directly in the main checkout or share one
   worktree across unrelated raise/adversarial/doc tasks.
3. Add or identify fixture methods that represent the source idiom.
4. For new/improved raises, add scorecard entries as unrecovered when the
   current output is lower altitude.
5. Add adversarial fixtures for nearby shapes that must not raise.
6. Run the scorecard to capture the baseline.
7. Implement or improve the raise pass. For a pure adversarial-discovery task,
   do not broaden the pass; narrow it only if the negative fixture exposes an
   over-match.
8. Run the scorecard again and flip newly recovered entries in the same PR.
9. Before pushing or opening the PR, synchronize with current upstream
   `origin/main` (or recreate the work from it), resolve conflicts locally, and
   re-check files that frequently collide in raise work: `LoweringCoverage`,
   `LoweringCoverageTests`, `IrPasses`, `CfgSampleClass`, and the scorecard.
10. Run fidelity/validity checks after that upstream sync to prove the final
   branch state, not just the pre-merge local state, stayed
   semantic, valid C#.

After a raise looks good, run an **adversarial pass** before merge. This is a
review pass, not an IR pipeline pass: give an agent the raise pass, its intended
discriminator, the positive fixtures, and the soundness checklist below; ask it
to add or update fixtures that try to falsify the match without changing the
implementation first. The useful output is concrete: a source-shaped negative
fixture, the exact discriminator it toggles, and the test that proves the pass
does not raise it. When the pass is too broad, add the negative fixture first so
it fails for the current implementation, then narrow the matcher.

Good adversarial fixtures are usually positive/negative pairs that differ by
one compiler discriminator. Examples: the real `^1` lowering spills the receiver
once while hand-written `a[a.Length - 1]` reloads it; an interpolated string
uses a hidden handler temp while hand-written `DefaultInterpolatedStringHandler`
uses a source local; a signed guard-return comparison can fold to `?:` while an
unsigned bounds-check guard must stay in statement form because the ternary
recompiles differently; a compiler lowering calls one exact overload while
nearby source uses an overload with extra provider/format/alignment arguments.
Common false-positive traps are name-only method matches, ignored constructor or
call operands, accepting any same-shaped local instead of a compiler temp, slot
aliasing/reuse, unsigned/null comparisons that depend on branch codegen, and
formatted/provider overloads that carry semantics the candidate syntax cannot
represent. Pin positives in the idiom scorecard; pin near-miss negatives in
pass-level tests or the fidelity gate.

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
