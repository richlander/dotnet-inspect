# Decompiler correctness pipeline

This document designs the decompiler test and harness stack as an intentionally
staged correctness gauntlet. It is **not** just a catalog of today's harness
flags. The current tools are the raw material; this document names the
first-class correctness system we want agents and maintainers to use.

[decompiler.md](decompiler.md) explains how the decompiler pipeline produces
output. [decompiler-quality.md](decompiler-quality.md) explains the quality
strategy and target selection. This page answers a more operational design
question: **which boss did this change beat, and which boss is still ahead?**

For raising, typing, structuring, fidelity, or printer changes, continue to
[raise-work discipline](decompiler-raise-discipline.md) and use the
[decompiler PR template](templates/decompiler-pr.md). The harness command
reference lives in [tools/DecompilerHarness/README.md](../tools/DecompilerHarness/README.md).

The core idea is to stop treating the harness modes as a bag of independent
tools. They should behave like a staged pipeline. Early stages are cheap, local,
and should be green all the time. Later stages are broader, slower, and answer
harder questions. A PR should run the highest stage its change can affect, then
report that result in reviewer-sized form.

## Design principles

The correctness system should have these properties:

1. **Named proof levels.** Every check has a role: entry, shape, validity,
   annotation, artifact, structure, fidelity, corpus, changed-method, final.
2. **One claim per level.** A check should say exactly what it proves and what it
   is blind to. No stage gets to imply more than it measured.
3. **Machine-readable artifacts.** Corpus and changed-method stages produce JSON
   artifacts that reviewers can drill into; PR bodies get compact generated
   cards.
4. **Population alignment.** Risky PRs must measure the methods they changed, not
   only an unrelated global sample.
5. **Honest exits.** If a method cannot be checked, the result is not success; it
   is a named blocker bucket.
6. **Work generation by failed boss.** New work comes from the lowest failing
   stage, not from taste or a stale backlog.

## The gauntlet

| Stage | Boss | Current implementation | What it proves | Does not prove |
| --- | --- | --- | --- | --- |
| 0 | Entry gate | build, focused xUnit tests, IR invariant checks | The code compiles and the pass preserves tree shape. | That output is valid or faithful. |
| 1 | Shape proof | pass fixtures, adversarial negatives, sidecar facts | The pass recognizes a specific lowering and declines near misses. | That the same logic is safe on the corpus. |
| 2 | Method validity boss | `--validity-check`, `Full malformed`, semantic validity diagnostics | Claimed-Full method C# parses, is statement-legal, and binds outside known shell noise. | That valid C# means the same thing. |
| 3 | Annotation boss | `--annotation-check`, annotation gates | Allocation/unsafety/lifetime annotations agree with raw IL witnesses. | Whether the C# body itself is right. |
| 4 | Type artifact boss | `--type-check`, whole-type/source checks | Type/file-level artifacts are coherent: type kind, modifiers, members, usings, surface. | Method-body semantic fidelity or binding. |
| 5 | Type binding boss | `--bind-check`, type-bind gates | Whole-type/source artifacts bind without ambiguous/missing-reference errors outside known noise. | Method-body compile-back fidelity. |
| 6 | Altitude boss | idiom scorecard, `LoweringCoverage`, sidecar rows | The output reached the intended C# idiom. | Soundness around near misses. |
| 7 | Structure boss | `--gaps`, `--structuring-stops`, `--by-shape` | Which control-flow or fidelity shapes remain unraised. | That raised shapes are semantically faithful. |
| 8 | Fidelity boss | `--fidelity-check`, fixture fidelity gates, lowered fidelity gates | Decompiled body recompiles to an exact contract V1 body. | Methods the check cannot recompile or compare. |
| 9 | Corpus boss | `--diff-corpus-baseline`, `--quality-diff-card`, Deep Inspect corpus, PR quick corpus | Aggregate movement across real assemblies, including regressions and coverage. | That the changed methods were fidelity-checked. |
| 10 | Changed-method boss | `--emit-corpus-delta`, `--fidelity-method-delta` | The methods a behavior PR changed are identified and attempted by compile-back fidelity. | That uncheckable changed methods are safe. |
| 11 | Final boss | changed-method fidelity over the risky target population, improved examples, still-flat near misses, adversarial review | A risky raise/structuring PR has evidence over the methods it actually changed and its nearest false positives. | Whole-program semantic equivalence. |

The goal is not to make every PR fight every boss. The goal is to make the
highest relevant boss explicit. A docs-only PR may stop at markdown lint. A
small pass refactor may need the entry gate plus a no-movement quality card. A
new raise or structuring change must go much higher.

## Entry gate checklist (stage 0)

The entry gate is the one stage that must be green for **every** decompiler PR
before any higher boss is claimed. It proves only that the code builds and the
pass preserves IR tree shape — not that output is valid or faithful — but a red
entry gate invalidates every later result, so run it first and report it.

"100% green" means all of the following pass on the changed revision:

1. **Build** the product/test/fixture graph:

   ```bash
   dotnet build dotnet-inspect.slnx -c Release
   ```

2. **Focused tests** for the area you touched, run with `dotnet run --project`,
   **not** `dotnet test`. These are xUnit v3 `OutputType Exe` runners; `dotnet
   test` exits 0 while silently producing **no test output**, so a real failure
   looks green. Decompiler-relevant projects:

   ```bash
   dotnet run --project src/ILInspector.Decompiler.Tests -c Release
   dotnet run --project src/ILInspector.Analysis.Tests -c Release
   dotnet run --project tests/ILInspector.Metadata.Tests -c Release
   ```

   Filter to a class while iterating, e.g.
   `… -c Release -- -filter "/*/*/IteratorAcknowledgmentPassTests/*"`.

3. **IR invariant checks.** Every pass must leave a structurally valid tree.
   `IrPasses.Run` calls `function.CheckInvariant()` after each pass — armed by
   default in every host except the shipped CLI (`IrInvariants`, #3267) — and
   pass tests assert it explicitly; a thrown invariant is an entry-gate failure,
   not a fidelity question. New pass tests should call `CheckInvariant()` on the
   result. [IR invariant checks: hosts, levels, and
   fixtures](#ir-invariant-checks-hosts-levels-and-fixtures) below is the full
   contract.

4. **Markdownlint** for any changed Markdown (docs-only PRs stop here):

   ```bash
   npx markdownlint-cli --fix <file> && npx markdownlint-cli <file>
   ```

Notes:

- The full `src/ILInspector.Decompiler.Tests` suite runs compile-back fidelity
  checks and can be slow, especially under a contended shared machine; it is part
  of the entry gate for behavior changes, but iterate against a class filter and
  run the full suite before requesting review.
- **PR CI runs only the fast unit subset.** The `test` job in `ci.yml` runs
  `dotnet run --project src/dotnet-inspect.Tests -c Release -- -trait-
  "Speed=Slow"`, `dotnet run --project src/ILInspector.Decompiler.Tests -c
  Release -- -trait- "Speed=Slow"`, and the matching fast Analysis/IL
  round-trip filters. These gate command surface, pass logic, printer, importer
  facts, identity, and classification regressions without the broad integration
  and sweep costs. The slow CLI integration, compile-back/recompile,
  corpus-sweep, bind, scorecard, fidelity, and broad differential tests are
  tagged `[Trait("Speed", "Slow")]` and run only in Deep Inspect / publish /
  full local runs. **Mark any new Roslyn-heavy / recompile / corpus-sweeping or
  broad integration test `[Trait("Speed", "Slow")]`** — at the class level for a
  wholly-slow class, or the method level for one slow case in an otherwise fast
  class — so it stays out of the PR gate. A green PR CI run therefore does *not*
  prove the slow suite is green; run the full suite locally before review.
- The IL round-trip oracle follows the same shape: PR CI runs
  `dotnet run --project tests/DotnetInspector.ILRoundtrip.Tests -c Release --
  -trait- "Speed=Slow"` when IL round-trip inputs change, while the unfiltered
  `DotnetInspector.ILRoundtrip.Tests` command keeps the assembly-wide sweep in
  Deep Inspect / publish / full local coverage. Mark new broad/corpus-style
  round-trip checks `[Trait("Speed", "Slow")]`.
- A green entry gate is necessary, never sufficient: it says nothing about
  validity, fidelity, or corpus health. Do not report it as if it did.

### IR invariant checks: hosts, levels, and fixtures

`AGENTS.md` requires that a correctness check not hide behind
`[Conditional("DEBUG")]`, because the suite runs Release for fixture fidelity
and such a call is stripped from the Release test assembly. The IR invariant
check is the worked example of the alternative: `IrNode.CheckInvariant` is
reached through a runtime flag (`IrInvariants.Enabled`, env var
`DOTNET_INSPECT_IR_INVARIANTS`) that is **on by default**, so any host that runs
the pipeline — test suite, harness, sweep, benchmark — validates after every
pass in the same build users run.

The shipped CLI is the one sanctioned opt-out
(`IrInvariants.DisableForShippedTool()` in `src/dotnet-inspect/Program.cs`), so
the tool pays nothing on the decompile hot path. Declining validation has
exactly one form — `Enabled`'s setter is private, so the compiler rejects any
other spelling — and `IrInvariantsHostContractTests` pins that one call site, so
a new host cannot quietly decline. An explicit `DOTNET_INSPECT_IR_INVARIANTS`
value (trimmed, case-insensitive) outranks the opt-out in both directions.

The check is **leveled**, but both levels are armed together, so the leveling
names what is checked rather than offering a way to check less:

- **Structural** invariants (parent/child back-pointer consistency, tree shape)
  hold on *any* well-formed `IrNode` graph, including the deliberately minimal
  `IrFunction`s that hand-built pass-unit fixtures construct
  (`IrInvariants.Enabled`).
- **Semantic** invariants (e.g. local-slot indices within the enclosing
  function/lambda's `Locals`) require a function that declares the slots it
  references. These were opt-in until #3302 on the stated grounds that arming
  them suite-wide would false-positive on ~120 minimal fixtures; measured, the
  number was five. Those five now declare their locals, and the level is on by
  default (`IrInvariants.CheckSemantics`), as a computed projection of `Enabled`
  so the two cannot drift apart and the shipped tool's opt-out lowers both.
  `CheckInvariant(includeSemantics: true)` still threads the level explicitly
  for hermetic per-test coverage.

A hand-built fixture that trips the semantic level is referencing locals it does
not declare; give the `IrFunction` its local table rather than lowering the
level. Do not derive the local table from the body — that makes every fixture
pass by construction and retires the invariant while appearing to keep it.

Per-pass validation fires inside `IrPasses.Run`/`PipelineRunner`, so a test that
calls `pass.Run(...)` directly never reaches it. Roughly a dozen test files
still build an `IrFunction` with an empty local table and reference slots in it.
They are unaffected today, but **converting one to `IrPasses.Run` will fail
it** — correctly, because the fixture is malformed. Declare the locals; do not
route around the check.

### Area trait: targeting a functional slice

`Speed` is a cost split of *everything*; it cannot target a functional area.
`ILInspector.Decompiler.Tests` therefore carries an orthogonal
`[Trait("Area", "…")]` dimension (applied at the class level, or at the method
level for a lone slow gate in an otherwise unrelated class) so a change author
can run one area's tests —
including that area's slow gates — without every other area's slow gates, and
without hand-enumerating `-class` names. The two dimensions compose:
`-trait "Area=X"` selects area X fast and slow; adding `-trait- "Speed=Slow"`
narrows to X's fast tests.

```bash
# every Fidelity test, fast and slow:
dotnet run --project src/ILInspector.Decompiler.Tests -c Release -- -trait "Area=Fidelity"
# fast Fidelity tests only:
dotnet run --project src/ILInspector.Decompiler.Tests -c Release -- -trait "Area=Fidelity" -trait- "Speed=Slow"
```

Areas and their member classes:

| Area | Member test classes |
| --- | --- |
| `RoundTrip` | the compile-back / MemberBodyProducer seam: `ReturnToSender*`, `MemberBodyProducer*`, `CompileBackTypeIdentityTests`, `TypeBindGateTests`, `GeneratedFixtureCatalogTests`, `CompilerFeatureOptionsTests` |
| `Fidelity` | the changed-method fidelity gates: `FidelityGateTests`, `LoweredFidelityGateTests`, `DiffFixtureFidelityTests`, `AuthoredRebuildFidelityTests`, `SkeletonEmitTests`, `ClusterCaptureTests`, `NestedTargetLookupTests`, plus the compile-back gate method in `PrinterPrecedenceTests` |
| `Corpus` | corpus-wide sweeps: `CorpusSweepGateTests`, `CorpusSensorComparisonTests`, `SubstrateLeaderDifferentialTests` |
| `Validity` | validity / ladder gates: `ValidityCoverageReportingTests`, `LadderIteratorGateTests`, `LadderRung*GateTests` |
| `Pass` | the per-pass unit tests (`*PassTests`) |

`Area` is a targeting aid, not a completeness contract: unclassified unit tests
carry no `Area`, so `-trait "Area=X"` selects only tagged members. When you add
a class that belongs to an area (especially a new slow gate), tag it with the
matching `[Trait("Area", "…")]` so the area's group filter keeps finding it; add
a new area value only when an expensive slice has no existing home.

Which area to run while iterating on a change:

| Change surface | Iterate against |
| --- | --- |
| `MemberBodyProducer`, changed-method emit, skeleton, type binding, the compile-back oracle | `Area=RoundTrip` (add `Area=Fidelity` — skeleton/compile-back overlap both) |
| The changed-method fidelity path, cluster capture, nested-target lookup, a printer/typing change that can alter recompiled output | `Area=Fidelity` |
| Validity ladder or iterator-reconstruction behavior | `Area=Validity` |
| A single raising / structuring / lowering / printer pass | `Area=Pass` for the pass's own `*PassTests`, then the fidelity/validity/corpus gates below |
| Corpus-sweep or sensor tooling | `Area=Corpus` |

`Area` narrows the *iteration* loop, not the pre-review gate. A raising,
structuring, typing, or printer change can shift any corpus row, so it is not
covered by its `Area=Pass` unit tests alone: before requesting review still run
the full slow suite locally (unfiltered `ILInspector.Decompiler.Tests`, which
Deep Inspect and release also run). `Area` does not change what CI runs — PR CI
keys on `Speed` (`-trait- "Speed=Slow"`) and Deep Inspect/release run the whole
slow set — so every area's slow gates already run before merge without any
per-area CI wiring.

### `--gate` preset flag: discoverable trait bundles

Memorizing the `Speed`/`Area` trait spellings above is friction, and an
*unfiltered* `ILInspector.Decompiler.Tests` run includes the multi-hour
`Corpus` sweep. The executable therefore accepts a first-class
`--gate <preset>` flag that expands to the corresponding `-trait`/`-trait-`
arguments before delegating to the runner. Run `--gate list` for the table:

```bash
dotnet run --project src/ILInspector.Decompiler.Tests -c Release -- --gate list
dotnet run --project src/ILInspector.Decompiler.Tests -c Release -- --gate no-corpus
```

| Preset | Expands to | Use |
| --- | --- | --- |
| `all` | *(no filter)* | the full slow suite (same as no flag) |
| `fast` | `-trait- "Speed=Slow"` | the fast lane the PR CI test job runs |
| `slow` | `-trait "Speed=Slow"` | only the slow gates |
| `no-corpus` | `-trait- "Area=Corpus"` | everything except the multi-hour corpus sweep |
| `pre-merge` | three `-class` filters | the docket + byte-neutrality gates the PR CI `decompiler-gates` job runs |
| `corpus` | `-trait "Area=Corpus"` | only the corpus sweep |
| `roundtrip` | `-trait "Area=RoundTrip"` | the compile-back / ReturnToSender seam |
| `fidelity` | `-trait "Area=Fidelity"` | the changed-method fidelity gates |
| `validity` | `-trait "Area=Validity"` | the validity / ladder gates |

The flag is a naming convenience over the traits, not a new selection axis:
presets compose with any additional xUnit arguments (e.g.
`--gate fast -class …`), and omitting `--gate` leaves invocation behavior
unchanged. The preset table lives in the test executable's entry point; keep it
in sync with the areas above when an area is added or renamed.

`pre-merge` is the one preset that names classes rather than a trait, because
the set it selects is a *cost* decision rather than a functional slice — see
below.

### Pre-merge gate and the known-red pin

The docket and byte-neutrality gates carry doc comments asserting they fail CI,
but they are all `Speed=Slow` and every pre-merge lane ran `-trait-
"Speed=Slow"`. They therefore ran only in `release.yml` and the weekly Deep
Inspect lane — where they were exceeding the job timeout, and a *cancelled* job
does not satisfy Deep Inspect's `if: ... && failure()` notifier, so no
notification was ever sent. Detection latency was unbounded, not weekly
(#3432), and five regressions (#3489–#3493) accumulated unseen.

The `decompiler-gates` CI job closes that hole. It is path-gated on the
decompiler and its substrate, runs as its own job so it never serializes with
the hot `test` lane, and runs `--gate pre-merge`:

```bash
dotnet run --project src/ILInspector.Decompiler.Tests -c Release -- \
  --gate pre-merge -noColor -list methods/json > /tmp/expected.json
dotnet run --project src/ILInspector.Decompiler.Tests -c Release -- \
  --gate pre-merge -xml /tmp/gates.xml
dotnet run eng/check-decompiler-gate.cs -- \
  /tmp/gates.xml \
  eng/decompiler-gate-known-red.txt \
  eng/decompiler-gate-expected-classes.txt \
  /tmp/expected.json
```

The gate was turned on **red**. That was not made conditional on the open
failures being fixed first: a gate's job is to make *new* breakage attributable,
and waiting for green is what let the current backlog accumulate. Open failures
are pinned in `eng/decompiler-gate-known-red.txt`, one fully qualified test name
per line, each preceded by its issue and the date it was pinned.

As of #3528 the list is **empty** and the gate runs green — #3489 through #3493
are fixed and their pins retired. That is the intended end state of a pin, not a
reason to remove the mechanism: the next regression gets pinned with an issue and
a date, and the checker keeps failing the job while an unpinned test is red.

That list is a record of *open, filed* failures, not an escape hatch. Do not add
an entry to make your own change go green, and do not skip a gate test to green
it — the checker treats a test that neither passed nor failed as a coverage hole
and fails the job. A new failure means either a regression to fix or a diff to
docket in the owning gate with a rationale. Adding a pin requires an issue and a
date, and the checker fails the job when a pinned test starts passing, so retire
pins as fixes land.

`eng/check-decompiler-gate.cs` decides the job's pass/fail from the run report,
and treats drift in **both** directions as an error:

| Condition | Meaning |
| --- | --- |
| a failure that is not pinned | new breakage — the gate did its job |
| a pinned test that passed | the fix landed; retire the pin |
| a pinned test that never ran | dead pin — the test was renamed or deleted |
| a gate test that neither passed nor failed | coverage silently disappeared |
| an expected class with nothing executed | the preset stopped selecting it |
| a discovered test with no row in the report | the report is incomplete |
| a row for a test discovery never listed | report and listing describe different runs |
| one test with more than one result row | the method expanded into several cases |
| a `<test>` with no usable name | the report is malformed |
| the report contradicts its own declared totals | truncated or rewritten |
| the report declares skipped, not-run, or errored tests | coverage did not run |
| no report, or a report with zero tests | a crashed or empty run is not a pass |
| no discovery listing, or one listing zero tests | there is no reference to judge completeness against |

Only `Pass` counts as passing. A skipped gate test is neither passing nor
failing, and treating it as either is how a gate becomes vacuous: an unpinned
skip would report an exact match, and a pinned skip would look like a landed
fix, prompting removal of the pin that was the last thing naming the test.
Skipping is not an approved way to green this job.

`eng/decompiler-gate-expected-classes.txt` records the classes `--gate pre-merge`
must select. It proves the *preset* has not quietly shrunk: a class renamed,
deleted, or dropped from the preset yields a report whose failing set still
matches the pin list exactly, and the inventory is what rejects it.

That inventory is only worth its accuracy, so it is not maintained by hand
against the preset. `GateExpectedClassesTests` asserts set equality between the
file and the `pre-merge` preset's `-class` arguments, in both directions, so a
class added to the preset without being added to the file fails and so does a
stale entry. That test is itself in the `pre-merge` preset, so it runs in the
gate job and is covered by the same completeness check as the correctness
gates. Running it as a *separate* CI step was worse than useless: a `-class`
filter naming a renamed or deleted class discovers nothing and exits 0, so the
step would have gone green while enforcing nothing.

Completeness is a separate property, and it needs a reference the report cannot
forge. The report's own summary counters are not one — they are written by the
same run, so a report containing four of fifteen tests and honestly declaring
`total="4"` is entirely self-consistent. The checker therefore compares the
results against a **discovery listing** produced by `-list methods/json` over
the same preset, which enumerates what should run without running it. Every
discovered test must appear in the results, and every result must correspond to
a discovered test. Identity for every decision — pins, class coverage, and
completeness alike — comes from the report's `type` and `method` attributes.
The display name is a presentation string: it carries theory arguments, honors
`-methodDisplayOptions`, and can disagree with the structured attributes
outright, so a row whose name claims to be a pinned test cannot pass a new
failure off as a known one.

That comparison is **method-granular**, and deliberately so: no `-list` mode
enumerates individual cases. A method that expands to five cases is listed
once, so a run that lost four of them would still satisfy the check. Method
granularity is sufficient only while every gate test is exactly one case, and
that is enforced rather than assumed —
`GateExpectedClassesTests.PreMergeGateClasses_ContainOnlyPlainFacts` requires
every test in a gate class to carry exactly `FactAttribute`.

It is an **allow list**, not a deny list, because rejecting only `[Theory]`
would miss `[CulturedFact]` — which derives from `FactAttribute`, not
`TheoryAttribute`, and still yields one case per culture — and would miss any
future multi-case attribute. It is anchored on `IFactAttribute` rather than on
`FactAttribute`, because that is the abstraction xUnit itself keys on
(`ExtensibilityPointFactory.GetMethodFactAttributes` returns
`IReadOnlyCollection<IFactAttribute>`): an attribute can implement that
interface directly, skip `FactAttribute` entirely, and supply a discoverer that
emits several cases. It scans the same method surface xUnit discovers,
including inherited and non-public methods and interface declarations, because
a theory inherited from a base class runs exactly like a declared one, and
because a `[Fact]` on a *default interface method* runs on the implementing
class. Multiplicity is counted per method signature, not judged per attribute:
a plain `[Fact]` on an interface method declaration and a plain `[Fact]` on its
implementation produce two cases from two perfectly ordinary `FactAttribute`s,
so only their number is wrong. Resolving each preset class by name in the same
test also catches an arm naming a renamed or deleted class, which would
otherwise select zero tests and exit 0.

That guard is prevention, and it has been wrong three times, so it is not the
only line of defence. The checker independently fails any report in which one
`(type, method)` produced more than one row. That check is purely
observational: it needs to know nothing about xUnit's attribute model, and it
catches every multi-case shape above — plus any future one — by counting what
the run actually produced. The two are complementary. The guard fails fast, in
the fast lane, before a multi-case test can ever reach the gate; the row check
is the backstop for the case where the guard's reflection is wrong again.

The declared totals are still cross-checked, including `passed` and `failed`
against the actual rows, but only as an internal-consistency check on a
possibly-corrupt report. They are not evidence of completeness and are not
relied on as such.

The stale-pin check is what keeps the pin list a ratchet rather than a growing
exemption set: a pin that outlives its failure silently un-gates the test it
names. Pass `--partial` to suppress the dead-pin, expected-class, and
completeness checks when deliberately running a subset locally. CI runs the full
preset and never passes it.

The path filter is deliberately broad — roughly `src/`, `tests/`, `tools/`, and
build files including `global.json` and `nuget.config`, minus `*.md`. A fidelity
result is a whole-pipeline observation, so its real input set is the test
project's transitive closure, which an enumerated project list cannot track
without rotting. Under-triggering silently disables the gate on exactly the
changes it exists to catch; over-triggering costs a parallel job that never
blocks the hot lane. Only `*.md` is excluded by extension: a `.txt` or `.jsonl`
under those trees can be a corpus or baseline fixture.

A job-level timeout would cancel the job, and a cancelled job runs no further
steps and satisfies no `failure()` condition — the same silent-cancellation
failure mode this gate exists to fix. The gate step therefore carries its own
`timeout-minutes` well under the job's, so a hang becomes a failed step that
the job survives, letting the checker run and fail loudly on the missing or
truncated report.

> [!NOTE]
> This job does not block merges today. The `main` ruleset declares no required
> status checks at all, so no job in `ci.yml` blocks a merge; this one is
> exactly as enforcing as the existing `test` job. Closing that gap is a
> repository-wide change tracked separately.

`pre-merge` deliberately selects three classes rather than the whole `Fidelity`
area. The area is ~31 minutes; these three are ~8. The exclusions are cost, not
principle — `ClusterCaptureTests` and `PrinterPrecedenceTests` alone are ~21
minutes for *two* tests and want the #3495 type-filter treatment before they can
be gated. Widen the preset as classes get cheap enough.

## Vocabulary

Use these names in issues and PRs when selecting evidence:

| Name | Meaning |
| --- | --- |
| **Entry gate** | Build and focused tests. This should be 100% green before any broader claim. |
| **Shape proof** | The pass-specific `shape + proof + decline` story: positive fixture plus near-miss negative. |
| **Validity** | Parse/statement/binding proof. This catches invalid C# and many skeleton defects. |
| **Annotation fidelity** | Allocation/unsafety/lifetime facts agree with independent IL witnesses. |
| **Type artifact correctness** | Whole-type/source output has the right type/file/member shape. |
| **Type binding** | Whole-type/source output binds in a Roslyn harness. |
| **Fidelity** | Compile-back contract V1 body proof. This is the semantic body oracle. |
| **Completeness** | Raised-vs-residual coverage: `--gaps`, `--structuring-stops`, scorecard/ledger movement. |
| **Corpus health** | Aggregate real-world signal from the fixed corpus. |
| **Changed-method evidence** | Per-method delta plus compile-back over methods the PR actually changed. |

Avoid saying "fidelity" when you mean two different things. The pipeline has
both:

- **Decompiler fidelity grade**: `Full`, `Partial`, `StructuredOnly`, `IlOnly`,
  `Failed`.
- **Compile-back fidelity result**: `Exact`, `OpcodeDiff`, `OperandDiff`,
  `FidelityUnavailable`, `RecompileFail`, `ContextFail`, `NotFull`,
  `not-sampled`.

Compile-back fidelity contract V1 defines `Exact` as a full product-owned IL
body comparison match. It compares opcode families, immediate values, symbolic
member/type/string identities, and branch topology while tolerating
local/argument macro and slot-layout changes. `OpcodeDiff` means opcode names
differ; `OperandDiff` means the opcode names match but the body comparison
differs; `FidelityUnavailable` means the comparison could not return a verdict.
The contract is explicitly EH-blind and is not a semantic-equivalence claim.
Its version is independent from the corpus snapshot schema version.

When reporting deltas, spell out `currentValidity`, `currentDecompilerFidelity`,
and `currentFidelityCheck` rather than mixing axes.

## What each PR should report

### Documentation-only

Run the relevant markdown lint. No corpus card is needed unless the doc claims a
new measured number.

### Harness-only measurement changes

Report the entry gate plus a small smoke run that proves the new mode works. If
the mode changes corpus cards, include a same-revision card or a before/after
example.

### Behavior-preserving decompiler refactors

Report:

1. focused tests;
2. `src/ILInspector.Decompiler.Tests`;
3. generated quality card showing no unexpected corpus movement;
4. adversarial review summary with resolution commit links.

### Bug fixes

Report:

1. the failing fixture or corpus example before the fix;
2. the same example after the fix;
3. generated quality card if corpus behavior can move;
4. any changed-method fidelity result if the fix came from a corpus-delta issue.

Invalid `Full` becoming `Partial` is an honesty improvement, not a regression,
but say that explicitly.

### New raises, printer semantics, and structuring changes

Report:

1. shape proof: positive fixture plus near-miss negative;
2. improved examples and still-flat near misses;
3. generated quality card, preferably with `--quality-card-risky`;
4. per-method delta artifact;
5. changed-method fidelity result, or a clear statement that changed methods are
   not currently checkable;
6. cross-model adversarial review summary with resolution commit links (two
   reviewers from the AGENTS.md
   [Adversarial Review](../AGENTS.md#adversarial-review) roster, never your own
   model).

For #1175-class retained-label work, the changed-method population must include
the forward-merge / structuring-residual methods the PR changes. A green global
fidelity sample that does not intersect those methods is not enough.

### Structure target-population refresh

The **structure boss** answers whether a structuring target population exists and
whether it is growing, shrinking, or stable. Use it when a tracker such as #1175
depends on a residual shape rather than on one specimen.

Run the fixed corpus and report the residual counts, not dump walls:

```bash
bash eng/prepare-decompiler-corpus.sh /tmp/corpus-assemblies.txt
mapfile -t assemblies < /tmp/corpus-assemblies.txt
dotnet run --project tools/DecompilerHarness -c Release -- "${assemblies[@]}" \
  --gaps \
  --by-shape \
  --structuring-stops \
  --max-examples 3
```

Post a short snapshot on the owning issue:

```text
### Structure boss — <target>

Corpus revision: <git sha / baseline>
Target bucket: <for example, structuring: conditional-branch>
Current count: <methods / containers, as reported>
Comparison: <previous count + source>
Examples: <up to 3 method names or artifact link>
Result: target population stable / shrinking / growing
Next: go / blocker / follow-up issue
```

For #1175-class retained-label work, compare the
`structuring: conditional-branch` method bucket and the forward-merge container
count against the previous #1175/#1212 snapshot. A stable target population says
"specimens still exist"; it does not prove that a proposed rewrite is safe.

### Compile-back fidelity changes

Behavior changes that can alter emitted method-body semantics fight the
**fidelity boss**. Use this band when a PR changes the importer, a raising pass, a
structuring pass, or printer semantics such as branch sense, checked/unchecked
context, conversions, field/local ordering, or shift masking.

Report compile-back evidence in two layers:

1. **Fixture gate** — the focused `src/ILInspector.Decompiler.Tests` fixture that
   covers the changed shape. Name whether the sugared gate (`FidelityGateTests`),
   lowered gate (`LoweredFidelityGateTests`), or a pass-specific test is the
   relevant guard. If a fidelity-diff docket row is fixed, shrink `KnownDiffs` and
   add the method to `PinnedExact` in the same PR. `DocketRowsStayCheckedDiffs`
   (both rails) enforces this: a `KnownDiffs` row that recompiles `Exact` fails the
   gate and names the row to promote. Before #3584 the rule was documented but
   unenforced, and 46 of 143 rows had silently gone stale — a stale row gates
   nothing, because the diff it allows no longer happens.
2. **Changed-method / corpus layer** — for risky or broad changes, identify the
   methods the PR actually changed and run `--fidelity-method-delta` over that
   population when available. Treat `Exact` as checked green and `OpcodeDiff` /
   `OperandDiff` as the semantic docket. Report `FidelityUnavailable`,
   `RecompileFail`, `ContextFail`, `NotFull`, and uncheckable buckets
   separately; they are not passing evidence.

Keep the axes separate:

- A green validity check proves the C# parses and binds, not that it is faithful.
- A green corpus card is aggregate health, not proof over the changed methods.
- A lowered-view result belongs to the lowered gate; it does not automatically
  prove the shipped sugared view, or vice versa.

### Annotation classifier changes

Hidden-fact annotation changes fight the **annotation boss**, not the method-body
validity or fidelity bosses. Use this band when a PR changes annotation import,
classification, hidden-fact emission, `AnnotationCheck`, or the annotation gate:

1. name the annotation family affected (`alloc.box`, `alloc.newarr`, `unsafe`,
   lifetime, function pointer, etc.) and whether the PR is intended to improve
   precision, recall, or both;
2. run the focused annotation tests plus the gate path that covers the changed
   witness population (`AnnotationGateTests` or a targeted
   `--annotation-check` harness run);
3. report precision failures and recall movement separately. A wrong annotation
   at an offset is a precision bug; a missing annotation for an unambiguous raw-IL
   witness is a recall bug. Do not summarize both as "fidelity";
4. if recall changes, include the checked population and floor/denominator so a
   smaller sample cannot look like an improvement;
5. if the C# body also changes, report the relevant validity/fidelity stage
   separately. Annotation fidelity proves the comments/facts match IL witnesses,
   not that the rendered C# parses or round-trips.

`tools/DecompilerHarness/README.md` is the command reference for
`--annotation-check` and explains the CI gate. Keep PR evidence at this proof
level: precision/recall counts, the affected witness family, and any remaining
ambiguous-opcode exclusions.

### Shape + proof + decline template

Over-raise correctness PRs (the [#1356](https://github.com/richlander/dotnet-inspect/issues/1356)-style
rows) all share one shape-proof story: name the discriminator the pass keys on,
show it still raises a real positive, and show the narrowest near miss now
declines. Copy this snippet into the PR body and fill it in:

```text
### <Pass> over-raise: <one-line claim being narrowed>

Discriminator (shape): <the exact IR/IL the pass recognizes, and why it is too broad>
Narrowest gate added: <the proof now required before raising>

Positive fixture (still raises): <real lowering that legitimately raises post-fix>
Decline (adversarial near miss): <synthetic/near-miss shape that must NOT raise;
  stays lowered/Partial after the fix>

Proof level: shape proof (pass fixtures + adversarial negative)
  [+ validity if output legality changes]
Evidence:
- src/ILInspector.Decompiler.Tests <ClassTests>: <N> positive, <M> negative, all green
- <generated quality card, only if corpus behavior can move>
Honesty note: invalid Full -> Partial is an honesty improvement, not a regression.
```

Guidance:

- Keep the gate the **narrowest** proof that makes the over-raise impossible; do
  not widen the pass to "fix" it.
- The decline fixture must be a true near miss — one property away from the
  positive — so it proves the discriminator, not an unrelated guard.
- A purely synthetic decline fixture is fine when stock `csc` cannot emit the
  shape (hand-written/obfuscated IL); say so, matching the #1356 realism note.

### Altitude and scorecard climbs

A scorecard, ledger, or `LoweringCoverage` row moving is an **altitude** signal —
the output reached the intended C# idiom — not a soundness proof. Report:

1. the scorecard/ledger/sidecar row that moved (a positive climb or a shrunk
   `Partial` row);
2. shape proof for the raise: positive fixture plus near-miss decline (altitude
   without a decline is just an unproven positive);
3. for any behavior change, the contract V1 / changed-method fidelity evidence the
   raise needs — altitude says nothing about near-miss soundness.

Do not inflate the scorecard with positive-only rows just to move a number. Keep
scorecard entries positive-by-construction, but back each one with adversarial
negatives in pass tests (the #1356 shape-proof bar) rather than letting a rising
count stand in for correctness. See
[decompiler-quality.md](decompiler-quality.md) for the scorecard/ledger strategy
and saturation guidance.

### Type and composer changes

Changes to `MemberBodyProducer`, type-declaration rendering, member-surface
projection, `using` emission, or name qualification affect whole-type
**artifacts**, not method bodies. Method-body fidelity checks are blind to them,
so run the two type bosses — both stub method bodies before checking, so a body
codegen defect can neither mask nor manufacture a type/binding artifact defect:

- **Type artifact boss** (`--type-check`) — syntactic: the namespace, type kind
  and modifiers, and the full member surface match the metadata inventory. Run it
  after any composer, type-declaration, or signature/`ApiSurfaceExtractor`
  change. Deltas bucket by kind (`namespace`, `type-kind`, `modifier-dropped`,
  `member-missing`, …); report the count outside the visibility-code noise.
  Current CoreLib frontier: `--type-check --cap 2000` is clean over the .NET 11
  preview sample (0 deltas over 1,098 composed types), so a new bucket is a
  type-artifact regression to route to composer/signature/surface work, not to
  method-body validity or compile-back fidelity.
- **Type binding boss** (`--bind-check`) — binds each composed type and reports
  the `CS0104` ambiguous-reference collisions a binder sees but the SRM-only
  product path cannot (the competing type lives outside the composed assembly, so
  the composer cannot detect it). Run it when a change can alter which namespaces
  are imported, whether a name is emitted qualified, or which references the
  composed type binds against: `using` hoisting, namespace qualification, type
  name shortening, explicit-interface/type-source rendering, or new reference-set
  logic. The current frontier is intentionally small: the running-runtime
  `TypeBindGateTests` binds CoreLib and allows only the documented
  `System.AppDomain` / `AssemblyHashAlgorithm` ambiguity. A new `CS0104` is a
  type-source binding regression; an allowlist change needs a comment explaining
  why the collision is unknowable from the SRM-only product path.

See [tools/DecompilerHarness/README.md](../tools/DecompilerHarness/README.md) for
the flags, buckets, and current baselines.

### Changed-method plateau decisions

Changed-method evidence fights the **changed-method boss**. Its first job is to
align the population: the methods a risky PR actually changed, not a friendlier
global sample. Its second job is to separate rows that are checkable today from
rows that need a named uncheckability reason.

Report changed-method runs in three bands:

1. **Attempted population** — total changed methods attempted, plus exact,
   opcode-diff, operand-diff, fidelity-unavailable, `NotFull`, recompile-fail,
   and context-fail counts.
2. **Checkable population** — `Exact` rows that pin a green set and
   `OpcodeDiff` / `OperandDiff` rows that become the semantic docket. These are
   the rows a PR may cite as
   compile-back evidence. Under cluster mode (`CB_CLUSTER=1`) this band is
   reported by **capture provenance** — *checkable whole-module* (bound under the
   whole-module skeleton) and *checkable cluster-rescued* (bound only after the
   target's transitive closure was reconstructed in isolation, i.e. a row a single
   unrelated sibling gap had been poisoning). Both are equally citable; the split
   only shows how much of the checkable population depended on closure isolation.
3. **Uncheckable population** — rows classified by reason, such as
   generated/synthesized member, stale delta target, missing reference, or
   `not-safely-capturable` (failed the whole-module attempt *and* the closure
   escalation — typically a Roslyn-class internal cross-assembly graph). Do not
   count them as passing.

The operational order is **escalate, do not cluster-first**: run the cheap
whole-module grouped compile, then escalate only the rows it could not check to
the (per-method, iterative) closure path, and treat a closure bail as
`not-safely-capturable`. A whole-module `Exact` is already trustworthy and a
closure cannot make it worse (it falls back), so escalation reaches the same
checkable population as attempting the closure on every row, far more cheaply,
while the three bands fall out of the capture provenance for free.

When repeated skeleton/context fixes only trade compiler diagnostics without
growing the checkable population, stop the incremental burndown and say the
plateau plainly. The next action is either a bounded safety case over the
checkable rows, or a measurement issue before redesign. The #1318 plateau was
measured under #1412: the failures are not predominantly unrelated-sibling
poison but types genuinely inside the target's (often large) reconstruction
closure. The harness ships an opt-in **reconstruction-closure (cluster) emitter**
(`CB_CLUSTER=1`) that reconstructs only the target's transitive closure instead
of the whole module and falls back to the whole-module skeleton on bail, so it
never regresses, emitting the safely-capturable bands above. The gain is
library-shaped, not universal — see
[decompiler-quality.md](decompiler-quality.md#reconstruction-closures-and-the-safely-capturable-population)
for the framing and the extension/inherited-member follow-ups.

For #1175-class retained-label work, a go/no-go comment should name the
checkable changed-method rows, the fidelity-diff docket, and the remaining
uncheckable buckets. A green global corpus card is still not a substitute.

### Final boss go/no-go

The final boss is the reviewer-sized decision packet for risky raise or
structuring work. It does not introduce a new oracle; it composes the relevant
proof levels above and makes the decision explicit. Use it before starting or
merging broad work such as #1175-class retained labels.

Post a short go/no-go comment on the owning issue or PR:

```text
### Final boss — <target>

Decision: Go / Blocked / Pivot
Scope: <methods, corpus slice, pass family, or PR>

Changed-method evidence:
- Attempted: <N>; Exact: <N>; OpcodeDiff: <N>; OperandDiff: <N>;
  FidelityUnavailable: <N>; NotFull: <N>; RecompileFail: <N>;
  ContextFail: <N>
- Checkable green set: <examples or artifact link>
- Semantic docket: <OpcodeDiff / OperandDiff examples or artifact link>
- Uncheckable buckets: <named reasons + counts>

Shape/altitude evidence:
- Improved examples: <positive raises / scorecard or ledger rows>
- Still-flat near misses: <adversarial declines that remain lowered/Partial>

Corpus/structure evidence:
- Quality card: <artifact/PR link>
- Structure target population: <gaps/structuring-stops counts if relevant>

Review:
- Cross-model adversarial review: <summary/link>
- Resolution commits: <links for addressed guidance, or "none" with rationale>
- Follow-ups: <issues for remaining buckets>

Merge readiness:
Ready to merge / Blocked by <concrete blocker>

Why this is enough:
<one paragraph tying the evidence to the decision>
```

Choose **Go** only when the changed-method checkable population covers the risky
shape well enough and the remaining uncheckable buckets are named, bounded, and
not the source of the safety claim. Choose **Blocked** when the lowest failing
boss prevents a meaningful safety claim (for example, changed-method rows are
mostly uncheckable for unknown reasons). Choose **Pivot** when the evidence says
the next useful work is a different boss or a measurement issue rather than more
raise code.

When the decision is **Go** and all merge-blocking validation, CI, and required
review are complete, post a PR comment that clearly says `Ready to merge`. If
extra tests or review continue after that point, mark them as non-blocking
follow-up work so the PR state remains unambiguous.

## Naming the harnesses by role

The command names are historical and intentionally stable, but PRs and issues
should refer to the role they serve:

| Role | Command / artifact |
| --- | --- |
| Method validity boss | `--validity-check`, `Full malformed`, semantic defects |
| Annotation boss | `--annotation-check` |
| Opcode boss | `--fidelity-check` |
| Type artifact boss | `--type-check` |
| Type binding boss | `--bind-check` |
| Structure boss | `--gaps`, `--structuring-stops`, `--by-shape` |
| Corpus boss | `--diff-corpus-baseline`, `--quality-diff-card` |
| Changed-method boss | `--emit-corpus-delta`, `--fidelity-method-delta` |
| Drill-down view | `--dump --steps --diff --cfg --facts --remarks` |

Do not paste drill-down walls into PR bodies. Link artifacts or gists when a
reviewer needs to inspect the fight.

## Using the gauntlet to generate work

When the burndown queue is empty, do not invent rows. Ask which boss is failing:

- Entry gate failures become build/test fixes.
- Shape proof failures become adversarial fixtures or predicate hardening.
- Validity failures become `Full malformed` or semantic-defect root-cause issues.
- Annotation failures become classifier/importer precision or recall issues.
- Type artifact failures become composer/signature/display issues.
- Type binding failures become qualification, using-hoist, or reference issues.
- Structure failures become `--gaps` / `--structuring-stops` pattern issues.
- Opcode failures become fidelity docket issues.
- Corpus aggregate movement becomes quality-card regression work.
- Changed-method uncheckability becomes classification work first; only build
  more harness context/skeleton machinery when measurement shows it will grow the
  checkable population.

This keeps work generation tied to evidence rather than taste.

## Current boss for risky work

As of the changed-method fidelity work, the current blocker for risky
structuring PRs is not target selection. We can identify changed methods. The
blocker is either making enough of those changed methods compile-back checkable
to be a useful semantic safety net, or honestly bounding the rows that are not
checkable today.

Until that improves, a risky PR must either:

- provide changed-method fidelity over its actual changed population;
- explain why the changed methods are not checkable and bound the safety case to
  fixtures, validity, readability, near-miss negatives, and named
  uncheckability buckets; or
- first measure and then fix the harness context/skeleton bucket that blocks
  those methods.
