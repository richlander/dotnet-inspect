# Decompiler correctness pipeline

This document designs the decompiler test and harness stack as an intentionally
staged correctness gauntlet. It is **not** just a catalog of today's harness
flags. The current tools are the raw material; this document names the
first-class correctness system we want agents and maintainers to use.

[Decompiler architecture](decompiler-architecture.md) maps the implementation,
host consumers, and test infrastructure. [decompiler.md](decompiler.md) explains
how the pipeline produces output.
[decompiler-quality.md](decompiler-quality.md) explains the quality
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

## Harness artifact ownership

The harness may parse source and compiler diagnostics as an independent
observation layer. It must not use that parse to construct or rewrite the C#
artifact it later compiles as evidence. C# spelling, declaration shape, body
layout, and artifact replacement remain product responsibilities.

Syntax and semantic validity bind the same first product projection. The
harness may sample that immutable rendered artifact for the semantic lane, but
must not invoke a mutating raising pipeline again and accidentally compile a
different second projection.
`CompilerFeatureOptionsTests.RuntimeAsyncUnsafeSpillBeforeAwait_ClosesUnsafeRunAndBindsFirstProjection`
gates this ownership boundary with compiler-produced runtime-async IL.

Semantic compilation replays the normalized memory-safety model with a compiler
configuration that actually enforces it. Updated modules use Preview plus the
`updated-memory-safety-rules` feature. Legacy, malformed, unsupported, and
unmarked modules use the stable latest language version without that feature;
Roslyn 5.9 Preview itself enables the updated behavior and therefore cannot
serve as a legacy oracle. Runtime-async is added independently from method
implementation metadata.
`CompilerFeatureOptionsTests.HarnessReplayEnforcesDistinctLegacyAndUpdatedUnsafeRules`
gates the observable distinction. Render A/B semantic comparisons select the
same per-assembly options for both projections, so a legacy validity regression
cannot be hidden by Preview's updated behavior.

ReturnToSender authored-body controls therefore create a separate
comparison-only `RoundTripRequest` with the independently acquired body as its
typed replacement. After
[#4931](https://github.com/richlander/dotnet-inspect/issues/4931) lands, it
consumes the CSharp owner's derived replacement artifact, which binds the frozen
source template digest, typed body range, preserved rendering policy and
non-target bytes, and distinct result digest. The control artifact cannot
inherit the template artifact's closure, participant coverage, admission, or
receipt evidence. It preserves the source artifact and module identity, target
set, scope and body policy, compiler policy, and frozen reference selection,
but it cannot issue a compile-context receipt or `Exact`.

The harness must not rediscover the target by member name, overload count,
syntax-tree search, or textual heuristics, and it must not recompose the shell
from mutable planning state. A mismatch in the preserved request identity or
policy, any changed non-target byte, or any reused artifact receipt makes the
control unavailable rather than comparable. This target wiring is **unverified**
until
`AuthoredBodyControlPreservesProductRenderedShell` runs in the Release harness
suite. The existing string-returning `CSharpSourceArtifact.ReplaceBody`
primitive and its
`SourceArtifactReplacesTheSelectedNestedMethodBlockOnly` and
`SourceArtifactReplacementPreservesConstructorInitializer` tests prove byte
replacement but not owner-issued derivation, so they cannot enable causal
control attribution by themselves.

ReturnToSender's earlier source-corpus lookup is a different, non-authoritative
operation. It may use
[`CSharpText.MemberSignatureShape`](design/member-signature-shape.md) to discriminate
same-named candidates, but correspondence remains typed as unique, ambiguous,
or unavailable and may fall back to the recorded ordinal. It cannot turn a
shape match into fault-attribution identity; attribution still requires the
exact MVID and MethodDef token. Only canonical `mss1:` shapes may select a
candidate. A persisted legacy signature may validate an already selected exact
MethodDef, but never participates in candidate selection. The close gates are
`ReturnToSenderSourceProbe_MatchesSourceBySignatureWhenDeclarationOrderDiffers`,
`SourceSignatureCorrespondence_ReportsAmbiguousCandidates`, and
`SourceSignatureCorrespondence_ReportsUnavailableCandidate`; legacy isolation is
gated by `CompileBackTargets_LegacySignatureCannotOverrideOrdinal` and
`SourceSignatureCorrespondence_RejectsLegacyCandidateSelection`.

An assembly-bound Portable PDB does not supply the missing attribution identity.
When present and recognized, its checksum can authenticate a mapped document's
content, but a `#line` directive in another, potentially unsupplied compilation
input can route that input's MethodDef sequence points into the authenticated
document. The PDB does not record the physical syntax tree behind those points.
Compiling a proposed body and comparing normalized IL can reject some false
candidates, but equivalent IL is not source provenance and divergence may
instead reflect compiler, reference, shell, local-layout, or generated-code
differences.

Local PDB spans may therefore inform non-authoritative correspondence but cannot
authorize fault attribution. `TryIsolateRecompileFailure_DeclinesRawSourceIndex`
gates raw-index ineligibility. No dedicated gate asserts the broader absence of
a PDB-authoritative path; that remains an architectural non-action boundary.
Issue #3835 is blocked on a trusted complete-source manifest plus an
assembly-wide line-mapping exclusion, or a stronger per-method provenance
contract.

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
| 8 | Fidelity boss | `--fidelity-check`, fixture fidelity gates, lowered fidelity gates | Decompiled body recompiles to an exact contract body. | Methods the check cannot recompile or compare. |
| 9 | Corpus boss | `--diff-corpus-baseline`, `--quality-diff-card`, Deep Inspect corpus, PR quick corpus | Aggregate movement across real assemblies, including regressions and coverage. | That the changed methods were fidelity-checked. |
| 10 | Changed-method boss | `--emit-corpus-delta`, `--fidelity-method-delta` | The methods a behavior PR changed are identified and attempted by compile-back fidelity. | That uncheckable changed methods are safe. |
| 11 | Final boss | changed-method fidelity over the risky target population, improved examples, still-flat near misses, adversarial review | A risky raise/structuring PR has evidence over the methods it actually changed and its nearest false positives. | Whole-program semantic equivalence. |

The real-world Corpus boss uses independently selected native ReturnToSender
results as its baseline-gated fidelity evidence. Legacy compile-back remains
per-row reference evidence for that sensor and cannot replace an unavailable or
failed native result. Routine corpus runs default to `rts-native`, which uses
the same independent selection and native RTS evaluation without executing the
legacy reference pass. The daily real-world Deep Inspect census explicitly
selects `rts-cutover` to retain the paired comparison ledger. Baselines that
have not yet migrated must explicitly select `compile-back`; the PR quick,
classic state-machine, and net11 opt-in consumers do so until their own #6199
adoption slices land. Standalone fidelity and changed-method consumers retain
their existing contracts.

The goal is not to make every PR fight every boss. The goal is to make the
highest relevant boss explicit. A docs-only PR may stop at markdown lint. A
small pass refactor may need the entry gate plus a no-movement quality card. A
new raise or structuring change must go much higher.

### Shared EH evidence adoption

Decompiler physical import consumes Metadata's closed method-body result and
complete exception-clause catalog. For a body that declares EH, it decodes that
same body observation once through Instructions and preserves exact clause and
region associations while raising flat EH into structured IR. Production
membership and supported explicit normal edges come from Instructions
`LocationAt` and `NormalTransferAt`; Decompiler continues to own C#
raisability, IR construction, correspondence, fidelity, and diagnostics.

A metadata-backed method never falls back to independent range reconstruction.
Missing, unavailable, ambiguous, or rejected correlated Instructions/catch
evidence retains the flat representation and reports `DEC0017`; an unavailable
Metadata body reports the closed import failure as `ContextUnavailable`.
Synthetic Layer 0 test inputs without Metadata identity retain the explicit
raw-range compatibility path. Body-replacement transforms clear stale root
correlation rather than attaching evidence from one body observation to
another.

`DecompilerExceptionFactAdoptionTests` is the Release gate for exact body and
clause association, Metadata catch order, runtime cleanup identity, closed
failure handling, visible refusal, and raw compatibility.
`ProtectedRegionControlFlowTests` gates the next consumer: production
try/catch `Leave` transfers use regions actually left by `NormalTransferAt`;
the current projection requires exact structured associations, with the
bounded predicate limited to associations below the candidate boundary.
Missing correlation and same-range foreign-body identity decline visibly. Its
synthetic cases preserve the explicit Layer 0 compatibility path, while its
detached-clone case proves that production structuring candidates retain their
function evidence owner.
`ProtectedContinueRecoveryTests` gates the production `ForLoopPass` adoption
outcome.
`ClassicInverseCoreExceptionTests` gates the classic-async consumer: production
raw membership comes from `LocationAt`, structured catch/finally projections
retain exact clause and region associations, raw and planning views share one
fact observation, missing correlation declines visibly, and same-range foreign
identity cannot license reconstruction. The same gate requires the
relationship-selected `MoveNext` MethodDef to equal the available Instructions
exception-flow body's MethodDef, rejects a foreign method observation, and
retains the neighboring user-`finally` reconstruction. Decompiler consumers
resolve owner-issued clause and region identities through Instructions lookup
rather than collection scans or object-reference correspondence. The full
`ClassicInverseCoreTests` population gates unchanged recipe and accounting
behavior. These gates do not verify the later return-timing consumer migration.

### EH normal-continuation return timing

This section owns one semantic-fidelity rule for exception-handling
structuring. When original control flow exits a protected region and evaluates
a return expression at its normal continuation, a rewrite may evaluate that
expression inside the protected region only when evaluation is observationally
equivalent relative to every exited `finally`. Preserving the same handler
count is necessary but insufficient: if the expression reads a place, the
rewrite must prove that no exited handler can change that place directly or
through a managed-reference alias.

The implementation uses a behavior-safe decline boundary rather than claiming
complete managed-reference alias analysis:

- a direct write to the returned local or argument in an exited cleanup retains
  evaluation at the normal continuation;
- if the current function takes the address of the returned local or argument,
  every transfer that exits a cleanup retains evaluation at the normal
  continuation because an indirect mutation has not been disproved; and
- missing, stale, ambiguous, duplicate, or mismatched cleanup facts retain
  evaluation at the normal continuation.

The address observation is intraprocedural and scoped to the current function
body. It deliberately does not distinguish local, conditional, field,
constructor, call, copied-carrier, or indirect-destination transfer shapes;
those are evidence that uncertainty must decline, not syntax-specific proof
rules. Constants, places whose address is not taken and whose exited cleanups
do not write them directly, and dedicated return blocks retain the existing
inlining path.

`FinallyReturnTimingTests` is this rule's Release gate. The
compiler-produced family covers direct and nested writes plus local, argument,
stack-join, field, ref-return, ref-parameter, constructor, helper-bound,
copied-carrier, indirect-destination, and conditional indirect-destination
transfer through stack slots and ref locals, including a ref-return field
receiver, field extraction through nested field addresses, and an interior
field of returned value-type storage; supported methods must also compile back
`Exact`. The
dedicated-return control and the existing
`IrImporterTests.TryFinallyTwoReturns_SinksBothReturnsIntoTry` plus
`FidelityGateTests` gate the neighboring safe-inlining boundary. Corpus cards
remain population evidence; they do not replace these method-level semantic
and fidelity gates.

Issue [#4178](https://github.com/richlander/dotnet-inspect/issues/4178)
supplies the compiler-produced motivating witness. No qualifying package or
repository witness is known. At the PR #6907 Round 6 boundary, the operator
[chose a docs-only design
slice](https://github.com/richlander/dotnet-inspect/pull/6907#issuecomment-5668866428)
instead of abandoning the synthetic-only defect. The operator separately
authorized Round 7 after shared-EH adoption steps 5 through 7 completed.
The existing decompiler path already serves CLI and browser/Wasm consumers
under tracker [#5876](https://github.com/richlander/dotnet-inspect/issues/5876);
this rule adds no architecture, host path, rendering strategy, or adoption
step.

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
   dotnet run --project tests/ILInspector.Decompiler.Tests -c Release
   dotnet run --project tests/DecompilerHarness.Tests -c Release
   dotnet run --project tests/ILInspector.Analysis.Tests -c Release
   dotnet run --project tests/ILInspector.Metadata.Tests -c Release
   ```

   These executables use MTP's `--filter-class`, `--filter-method`,
   `--filter-query`, and trait-filter options while iterating.
   [The repository xUnit test host](design/xunit-test-host.md) selects
   Microsoft Testing Platform (MTP) as the owner of aggregate non-vacuity.
   The decompiler host owns `--gate` preset expansion and the stronger
   `--gate pre-merge` receipt: the preset names independent correctness claims,
   so the CI checker requires report evidence for every expected class and
   compares independent pre-enumerated discovery identities with execution
   identities to prove every selected case executes exactly once. MTP's
   aggregate minimum cannot replace either property.

   The decompiler executable uses MTP for ordinary execution and has no
   repository-owned selector preflight. Its discovery receipt uses MTP's
   JSON-RPC server protocol because MTP 1.9 disables user data consumers during
   discovery; execution uses a suite-owned MTP `IDataConsumer`. Both carry
   MTP's stable `TestNodeUid`, preserving the per-class and
   discovery-to-execution completeness contracts without duplicating selector
   semantics.

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

- The full `tests/ILInspector.Decompiler.Tests` suite runs compile-back fidelity
  checks and can be slow, especially under a contended shared machine. Daily
  Deep Inspect owns the full non-corpus selection; while iterating, run the
  focused slow class or area affected by the change rather than treating the
  entire slow suite as a per-PR entry gate.
- **PR CI runs only the fast unit subset.** The `test` matrix runs
  `dotnet run --project tests/DotnetInspect.Cli.Tests -c Release --
  --filter-not-trait "Speed=Slow"` and the matching fast Analysis/IL round-trip
  filters. In parallel, the path-gated `decompiler-gates` job runs
  `dotnet run --project tests/ILInspector.Decompiler.Tests -c Release
  --no-build -- --gate fast` and
  `dotnet run --project tests/DecompilerHarness.Tests -c Release --no-build`
  plus the bounded receipt below. These gate command surface, pass logic,
  printer, importer facts, identity, and classification regressions without the
  broad integration and sweep costs.
  The slow CLI integration, compile-back/recompile, corpus-sweep, bind,
  scorecard, fidelity, and broad differential tests are tagged
  `[Trait("Speed", "Slow")]` and run only in Deep Inspect / full local runs.
  **Mark any new Roslyn-heavy / recompile / corpus-sweeping or broad integration
  test `[Trait("Speed", "Slow")]`** — at the class level for a wholly-slow
  class, or the method level for one slow case in an otherwise fast class — so
  it stays out of the PR gate. A green PR CI run therefore does *not* prove the
  slow suite is green; do not claim broad fidelity, validity, or corpus health
  without the corresponding focused or daily evidence.
- The IL round-trip oracle follows the same shape: PR CI runs
  `dotnet run --project tests/DotnetInspector.ILRoundtrip.Tests -c Release --
  --filter-not-trait "Speed=Slow"` when IL round-trip inputs change, while the unfiltered
  `DotnetInspector.ILRoundtrip.Tests` command keeps the assembly-wide sweep in
  Deep Inspect / full local coverage. Mark new broad/corpus-style
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

The shipped CLI deliberately opts out
(`IrInvariants.DisableForShippedTool()` in `src/DotnetInspect.Cli/Program.cs`), so
the tool pays nothing on the decompile hot path. Declining validation has
one public API — `Enabled`'s setter is private, so callers cannot mutate either
level independently. `IrInvariantsHostContractTests` pins that API shape,
default-on behavior, environment precedence, and the fact that a disarmed test
run fails loudly. It does not inventory trusted host call sites. An explicit
`DOTNET_INSPECT_IR_INVARIANTS` value (trimmed, case-insensitive) outranks the
host choice in both directions.

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
without hand-enumerating `--filter-class` names. The two dimensions compose:
`--filter-trait "Area=X"` selects area X fast and slow; adding
`--filter-not-trait "Speed=Slow"` narrows to X's fast tests.

```bash
# every Fidelity test, fast and slow:
dotnet run --project tests/ILInspector.Decompiler.Tests -c Release -- \
  --filter-trait "Area=Fidelity"
# fast Fidelity tests only:
dotnet run --project tests/ILInspector.Decompiler.Tests -c Release -- \
  --filter-trait "Area=Fidelity" --filter-not-trait "Speed=Slow"
```

Areas and their member classes:

| Area | Member test classes |
| --- | --- |
| `RoundTrip` | the compile-back / MemberBodyProducer seam: `ReturnToSender*`, `MemberBodyProducer*`, `CompileBackTypeIdentityTests`, `TypeBindGateTests`, `GeneratedFixtureCatalogTests`, `CompilerFeatureOptionsTests` |
| `Fidelity` | the changed-method fidelity gates: `FidelityGateTests`, `LoweredFidelityGateTests`, `ByteNeutralityGateTests`, `DiffFixtureFidelityTests`, `AuthoredRebuildFidelityTests`, `AnnotatedCompileBackFailureTests`, `SkeletonEmitTests`, `ClusterCaptureTests`, `NestedTargetLookupTests`, plus the compile-back gate method in `PrinterPrecedenceTests` |
| `Corpus` | corpus-wide sweeps: `CorpusSweepGateTests`, `CorpusSensorComparisonTests`, `SubstrateLeaderDifferentialTests`, plus `ControlFlowModelDifferentialTests.ControlFlowViews_AgreeOverCoreLib` |
| `Validity` | validity / ladder gates: `ValidityCoverageReportingTests`, `LadderIteratorGateTests`, `LadderRung*GateTests` |
| `Pass` | the per-pass unit tests (`*PassTests`), plus `ControlFlowModelDifferentialTests.ControlFlowViews_AgreeOnSyntheticBoundaryTerminators` |

`Area` is a targeting aid, not a completeness contract: unclassified unit tests
carry no `Area`, so `--filter-trait "Area=X"` selects only tagged members. When
you add a class that belongs to an area (especially a new slow gate), tag it with the
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
Deep Inspect also runs). `Area` does not change what CI runs — PR CI keys on
`Speed` (`--filter-not-trait "Speed=Slow"`) and Deep Inspect runs the whole slow
set — so every area's slow gates already run before release certification
without any per-area CI wiring.

### `--gate` preset flag: discoverable trait bundles

Memorizing the `Speed`/`Area` trait spellings above is friction, and an
*unfiltered* `ILInspector.Decompiler.Tests` run includes the multi-hour
`Corpus` sweep. The executable therefore accepts a first-class
`--gate <preset>` flag that expands to the corresponding MTP trait-filter
arguments before delegating to the runner. Run `--gate list` for the table:

```bash
dotnet run --project tests/ILInspector.Decompiler.Tests -c Release -- --gate list
dotnet run --project tests/ILInspector.Decompiler.Tests -c Release -- --gate no-corpus
```

| Preset | Expands to | Use |
| --- | --- | --- |
| `all` | *(no filter)* | the full slow suite (same as no flag) |
| `fast` | `--filter-not-trait "Speed=Slow"` | the fast lane the PR CI test job runs |
| `slow` | `--filter-trait "Speed=Slow"` | only the slow gates |
| `no-corpus` | `--filter-not-trait "Area=Corpus"` | everything except the multi-hour corpus sweep |
| `pre-merge` | explicit `--filter-class` options | the bounded compile-back receipt the PR CI `decompiler-gates` job runs |
| `corpus` | `--filter-trait "Area=Corpus"` | only the corpus sweep |
| `roundtrip` | `--filter-trait "Area=RoundTrip"` | the compile-back / ReturnToSender seam |
| `fidelity` | `--filter-trait "Area=Fidelity"` | the changed-method fidelity gates |
| `validity` | `--filter-trait "Area=Validity"` | the validity / ladder gates |

The flag is a naming convenience over the traits, not a new selection axis:
presets compose with any additional MTP xUnit arguments (e.g.
`--gate fast --filter-class …`), and omitting `--gate` leaves invocation
behavior unchanged. The preset table lives in the test executable's entry
point; keep it in sync with the areas above when an area is added or renamed.

`pre-merge` is the one preset that names classes rather than a trait, because
the set it selects is a *cost* decision rather than a functional slice — see
below.

### Pre-merge gate and the known-red pin

The PR contract is intentionally bounded. `decompiler-gates` proves the
genuinely fast unit subset is green and that a direct compile-back receipt is
complete: every expected class executes, independent discovery and execution
identities agree, every selected case starts exactly once, and known-red pins
ratchet in both directions. It does **not** claim that the broad docket,
lowered-fidelity, cluster, or printer-precedence sweeps are green.

Daily Deep Inspect owns those broad sweeps through `--gate no-corpus`, which
selects every non-corpus test regardless of `Speed`. Its decompiler step carries
an explicit post-build condition, so an earlier Test lane suite failure does
not skip that evidence. The historical weekly arrangement was not sufficient:
the slow docket and byte-neutrality gates exceeded the job timeout, a
*cancelled* job did not satisfy the workflow's `failure()` notifier, and five
regressions (#3489–#3493) accumulated with unbounded detection latency (#3432).
The dedicated pre-merge job originally closed that entire hole; #6889 narrows
the PR claim to restore the repository's approximately 10-minute entry gate
while preserving daily ownership and a bounded direct receipt.

Source, test, and tool projects run `decompiler-gates` by default, except for
measured false positives in
`eng/decompiler-gate-skip-projects.txt`. That manifest is generated, not
hand-maintained: `dotnet run eng/test-ci-change-detection.cs -- \
--refresh-decompiler-skip-projects` recomputes it from every project directory
under `fixtures/`, `src/`, `tests/`, and `tools/` that falls outside MSBuild's
evaluated Release project-reference closure rooted at
`ILInspector.Decompiler.Tests`, so it stays comprehensive as the repository
grows instead of drifting back toward a small hand-picked list. Re-run it and
commit the result whenever a project is added, removed, or re-wired.
`DecompilerProjectGraphPolicy` in the `eng/CiChangeDetection` gate still
asserts that every exemption names a project root and that no exempted project
tree overlaps that same closure — the generator and the assertion share the
one evaluated graph, so they cannot disagree. New project trees therefore run
the gate until the manifest is regenerated; neither a nested project nor a
nested exemption can silently hide sources compiled by a graph project. An
unreadable, invalid, or vacuous graph or skip list exempts nothing. Global
build inputs and the gate's own scripts and pins remain
explicit triggers. The job runs separately so it never serializes with the hot
`test` lane, and executes `--gate pre-merge`.

```bash
dotnet run --project tests/ILInspector.Decompiler.Tests -c Release -- \
  --gate-discovery-receipt /tmp/discovery.jsonl \
  --gate pre-merge --pre-enumerate-theories on --no-ansi
DOTNET_INSPECT_DECOMPILER_TEST_RECEIPT=/tmp/execution.jsonl \
  dotnet run --project tests/ILInspector.Decompiler.Tests -c Release -- \
  --gate pre-merge --pre-enumerate-theories on --no-ansi \
  --auto-reporters off --report-xunit-xml \
  --report-xunit-xml-filename gates.xml --results-directory /tmp
dotnet run eng/check-decompiler-gate.cs -- \
  /tmp/gates.xml \
  /tmp/execution.jsonl \
  eng/decompiler-gate-known-red.txt \
  eng/decompiler-gate-expected-classes.txt \
  /tmp/discovery.jsonl
```

The gate was turned on **red**. That was not made conditional on the open
failures being fixed first: a gate's job is to make *new* breakage attributable,
and waiting for green is what let the current backlog accumulate. Open failures
are pinned in `eng/decompiler-gate-known-red.txt`, one
`Namespace.Class.Method [TestNodeUid]` per line, each preceded by its issue
and the date it was pinned. Pins are case-granular, so one red theory row never
exempts its siblings.

As of #3528 the list is **empty** and the gate runs green — #3489 through #3493
are fixed and their pins retired. That is the intended end state of a pin, not a
reason to remove the mechanism: the next regression gets pinned with an issue and
a date, and the checker keeps failing the job while an unpinned case is red.

That list is a record of *open, filed* failures, not an escape hatch. Do not add
an entry to make your own change go green, and do not skip a gate test to green
it — the checker treats a test that neither passed nor failed as a coverage hole
and fails the job. A new failure means either a regression to fix or a diff to
docket in the owning gate with a rationale. Adding a pin requires an issue and a
date, and the checker fails the job when a pinned case starts passing, so retire
pins as fixes land.

`eng/check-decompiler-gate.cs` decides the job's pass/fail from the run report,
and treats drift in **both** directions as an error:

| Condition | Meaning |
| --- | --- |
| a failure that is not pinned | new breakage — the gate did its job |
| a pinned case that passed | the fix landed; retire the pin |
| a pinned case that never ran | dead pin — the case was renamed or deleted |
| a gate test that neither passed nor failed | coverage silently disappeared |
| an expected class with nothing executed | the preset stopped selecting it |
| a discovered `TestNodeUid` that never starts | the report is incomplete |
| an execution `TestNodeUid` discovery never listed | receipt and discovery describe different runs |
| one discovered `TestNodeUid` starts more than once | theory enumeration was delayed, or the test retried |
| discovery and execution attach one `TestNodeUid` to different methods | structured identity is inconsistent |
| MTP receipt and XML method counts disagree | the two result artifacts describe different runs |
| a `<test>` with no usable name | the report is malformed |
| the report contradicts its own declared totals | truncated or rewritten |
| the report declares skipped, not-run, or errored tests | coverage did not run |
| no report, or a report with zero tests | a crashed or empty run is not a pass |
| no discovery receipt, or one containing zero tests | there is no reference to judge completeness against |

Only `Pass` counts as passing. A skipped gate test is neither passing nor
failing, and treating it as either is how a gate becomes vacuous: an unpinned
skip would report an exact match, and a pinned skip would look like a landed
fix, prompting removal of the pin that was the last thing naming the case.
Skipping is not an approved way to green this job.

`eng/decompiler-gate-expected-classes.txt` records the classes `--gate pre-merge`
must select. It proves the *preset* has not quietly shrunk: a class renamed,
deleted, or dropped from the preset yields a report whose failing set still
matches the pin list exactly, and the inventory is what rejects it.

That inventory is only worth its accuracy, so it is not maintained by hand
against the preset. `GateExpectedClassesTests` asserts set equality between the
file and the `pre-merge` preset's `--filter-class` arguments, in both
directions, so a class added to the preset without being added to the file
fails and so does a stale entry. That test is itself in the `pre-merge` preset,
so it runs in the gate job and is covered by the same completeness check as the
correctness gates.

Completeness is a separate property, and it needs a reference the report cannot
forge. The report's own summary counters are not one — they are written by the
same run, so a report containing four of fifteen tests and honestly declaring
`total="4"` is entirely self-consistent. The checker therefore compares the
results against a **case discovery receipt** produced through MTP's JSON-RPC
`testing/discoverTests` request over the same preset. MTP 1.9 intentionally
disables user data consumers in discovery mode, so the custom host acts as the
protocol client and records the structured test-node notifications rather than
parsing console or diagnostic text. Direct execution registers a suite-owned
MTP `IDataConsumer`. Both artifacts carry MTP's stable `TestNodeUid`, class,
method, signature, and lifecycle state, so every discovered case must start and
every started case must have been discovered.

The equality is not merely a set comparison. `--pre-enumerate-theories on` expands
serializable theory data, but xUnit falls back to delayed enumeration when data
is not serializable. In that shape discovery emits one case ID for a method,
then execution starts several tests under the same ID. The checker therefore
requires **exactly one** `started` receipt row per discovered `TestNodeUid`.
Zero means a case vanished; more than one means discovery did not independently
enumerate every execution (or the runner retried a test). Both fail.

The MTP receipts own case identity and case-level known-red matching; the MTP
xUnit report owns diagnostics and class coverage. The checker cross-checks
total, per-method, and per-outcome execution counts between them. Discovery and
execution class/method signatures must agree by `TestNodeUid`; receipt
class/method names and XML `type`/`method` must also agree structurally. Display
names remain presentation strings: they carry theory arguments and are never
parsed to manufacture identity.

This observational contract replaces the removed semantic selector preflight.
It needs no knowledge of xUnit filter parsing, attribute types, inherited
methods, interface declarations, custom discoverers, or data serialization
rules; it measures what MTP discovery and execution actually did.
The four theory-bearing classes added in #3837 use serializable primitive
`[InlineData]`, so all 42 cases receive distinct IDs and satisfy the same
completeness contract as facts.

The declared totals are still cross-checked, including `passed` and `failed`
against the actual rows, but only as an internal-consistency check on a
possibly-corrupt report. They are not evidence of completeness and are not
relied on as such.

The stale-pin check is what keeps the pin list a ratchet rather than a growing
exemption set: a pin that outlives its failure silently un-gates the case it
names. Pass `--partial` to suppress the dead-pin, expected-class, and
completeness checks when deliberately running a subset locally. CI runs the full
preset and never passes it.

The path filter is deliberately broad — roughly `src/`, `tests/`, `tools/`, and
build files including `global.json`, minus `*.md`. A fidelity result is a
whole-pipeline observation, so its real input set is the test project's
transitive closure, which an enumerated project list cannot track without
rotting. Under-triggering silently disables the gate on exactly the changes it
exists to catch; over-triggering costs a parallel job that never blocks the hot
lane. Only `*.md` is excluded by extension: a `.txt` or `.jsonl` under those
trees can be a corpus or baseline fixture.

A job-level timeout would cancel the job, and a cancelled job runs no further
steps and satisfies no `failure()` condition — the same silent-cancellation
failure mode this gate exists to fix. The gate step therefore carries its own
`timeout-minutes` well under the job's, so a hang becomes a failed step that
the job survives, letting the checker run and fail loudly on the missing or
truncated report.

> [!NOTE]
> This job is intended to reach the merge gate through the aggregate
> `ci-required` job in `ci.yml`, the single context the `main` ruleset is meant
> to require. It cannot
> be required directly: it is path-gated, and a required check that does not run
> on a given PR is reported as "Expected" forever and blocks the merge
> permanently. `ci-required` passes a `skipped` dependency and fails a
> `cancelled` one, so this gate skipping on a docs-only PR is fine while this
> gate hitting its timeout is not (#3523).

`pre-merge` deliberately selects the workload classes named by its fail-closed
inventory rather than the whole `Fidelity` area. The bounded receipt covers
byte-neutral and byte-divergent behavior, whole-module skeleton hazards around
selected bodies, product-artifact RTS over typed diff fixtures, nested target
identity, authored rebuild and typed failure paths, plus
`GateExpectedClassesTests`, the plumbing guard that rides along in the preset it
guards.

`FidelityGateTests`, `LoweredFidelityGateTests`, `ClusterCaptureTests`, and
`PrinterPrecedenceTests` are daily-only whole-pipeline evidence. On #6835 they
accounted for about 2,004 of the former preset's 2,066 test seconds; no one of
them could share a four-minute solution build and still fit the 10-minute PR
target. They remain selected by daily `--gate no-corpus`; removing them from
`pre-merge` changes evidence timing, not the asserted product behavior.

Issue #6889 reclassified 41 wholly-slow classes and 72 individually-slow
methods.
The measured local `--gate fast` path fell from 6,940 tests in 2,300 seconds to
5,445 tests in 159 seconds, with no remaining case at or above the repository's
two-second threshold. The bounded receipt runs 110 cases across eight expected
classes in 123 seconds, and `DecompilerHarness.Tests` adds 11 seconds. The
combined local execution core is therefore 4m54s; the prior GitHub job's
build/setup/checker overhead was 4m28s, leaving a healthy decompiler-selected
path within the approximately 10-minute PR budget.

Those workload classes share `FidelityGateCollection` and therefore run
serially even though this test assembly allows two parallel collections. That
boundary is intentional: run 30885078644 overlapped the newly gated Printer
compile-back with Cluster capture for 7m01s and the lowered gate for another
33s, then Roslyn threw from
`CommonReferenceManager.ResolveReferencedAssembly`; the failed-job rerun
passed. `GateExpectedClassesTests` is excluded because it is a fast reflection
guard with no compile-back work.
`PreMergeWorkloadClasses_ShareFidelityGateCollection` derives the workload set
from the preset and fails if a future gate class escapes the collection.

`DiffFixtureFidelityTests`, `NestedTargetLookupTests`,
`AuthoredRebuildFidelityTests`, and `AnnotatedCompileBackFailureTests` carry
theories but are nearly free: 42 cases in 2.4 seconds combined. Before #3837
they remained outside the preset because the checker compared
method-granular discovery (28 methods) with case-granular execution (42
cases), so fourteen cases could disappear undetected.

The `TestNodeUid` contract above makes them safe to gate. With pre-enumeration, their
primitive `[InlineData]` produces 42 distinct MTP `TestNodeUid` values; the
execution receipt records each ID starting exactly once. The
delayed-enumeration negative canary remains
`CSharpPrecedenceTests`: its non-serializable
`TheoryData<IrExpression, Precedence>` discovers two case IDs but executes
nineteen tests, with one ID starting eighteen times. The checker rejects that
shape as `NON-ENUMERATED OR REPEATED CASES`.

`DiffFixtureFidelityTests` requests the seven named raised-view methods from
each paired fixture through product-artifact RTS with the legacy compile-back
floor disabled. The gate requires exactly one native result per requested
target and accepts the same checkable status set as before: `Exact`,
`OpcodeDiff`, or `OperandDiff`. It therefore proves native product-artifact
compile-back for this bounded fixture surface without allowing the retiring
whole-module path to rescue missing or failed evidence.

`SkeletonEmitTests` now contributes its eight cases to `pre-merge` (#3872).
Its focused `FidelityCheck.Evaluate` calls select a typed
`(Type, Method, Overload)` identity before method import, rendering,
disassembly, and compile-back, reducing the class from 374.25 seconds to 13.31
seconds locally while retaining the whole-module declaration hazard. A supplied
method filter that produces no processable row throws rather than returning a
vacuous green result; selecting by name admits all overloads, while the overload
ordinal can select one.

Method selection does **not** narrow reconstruction. Each selected body still
compiles against the whole-module skeleton, preserving the class's declaration
hazard contract. A gated `SkeletonEmitTests` case mutates two unrelated metadata
type names to collide and requires that collision to fail the selected
compile-back; a pruned skeleton would incorrectly turn that canary green.

> [!TIP]
> When measuring a class by name, check the namespace. Several classes in this
> assembly live in `ILInspector.DecompilerHarness`, not
> `ILInspector.Decompiler.Tests`. MTP returns exit code 8 when an execution
> filter matches nothing; `eng/decompiler-gate-expected-classes.txt` and the CI
> checker additionally keep the preset and its expected class registry
> synchronized.

## Vocabulary

Use these names in issues and PRs when selecting evidence:

| Name | Meaning |
| --- | --- |
| **Entry gate** | Build and focused tests. This should be 100% green before any broader claim. |
| **Shape proof** | The pass-specific `shape + proof + decline` story: positive fixture plus near-miss negative. |
| **Validity** | Parse/statement/binding proof. This catches invalid C# and many skeleton defects. |
| **Correct** | Authored-source correspondence after the harness's established source normalization. |
| **Printer exact** | Opt-in authored-source correspondence before normalization, after versioned mechanical envelope handling only. |
| **Annotation fidelity** | Allocation/unsafety/lifetime facts agree with independent IL witnesses. |
| **Type artifact correctness** | Whole-type/source output has the right type/file/member shape. |
| **Type binding** | Whole-type/source output binds in a Roslyn harness. |
| **Fidelity** | Compile-back contract body proof. This is the semantic body oracle. |
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

The shipping compile-back fidelity contract currently defines `Exact` as a full
product-owned IL body comparison match. The body comparison covers opcode
families, immediate values, symbolic member/type/string identities, and branch
topology while tolerating local/argument macro and slot-layout changes.
`OpcodeDiff` means opcode names differ; `OperandDiff` means the opcode names
match but the body comparison differs; `FidelityUnavailable` means the
comparison could not return a verdict. The contract is explicitly EH-blind and
is not a semantic-equivalence claim. Its version is independent from the corpus
snapshot schema version.

Issue #4810's target contract strengthens `Exact` to require a complete
compile-context receipt for the exact artifact and member. Under that contract,
missing, mismatched, or incomplete reference-closure, artifact-coverage, or
rebuilt-binding evidence produces `FidelityUnavailable` even when the body
comparison is exact. This safety property is **unverified** until the planned
`ExactRequiresNonVacuousCompileContextReceipts` and
`EveryExactProducerRequiresMatchingContextReceipt` gates run in Release.

When reporting deltas, spell out `currentValidity`, `currentDecompilerFidelity`,
and `currentFidelityCheck` rather than mixing axes.

For authored-source evidence, use the nested source judgments
`Printer exact ⊆ Correct ⊆ Valid`. The source-oracle manifest names a complete
expected eligible-member set for each immutable whole-file identity. Every
registered file must clear Valid and Correct; only files explicitly opted into
Printer exact must clear the pre-normalized comparison. Missing or stale
members fail the gate. `AuthoredSourceOracleManifestTests` enforces the set and
nesting contract. Compile-back fidelity remains independent: Printer exact does
not imply opcode fidelity, and opcode fidelity does not imply Printer exact.

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
2. `tests/ILInspector.Decompiler.Tests`;
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
6. adversarial review summary with resolution commit links, staffed according
   to the AGENTS.md
   [Adversarial Review](../AGENTS.md#adversarial-review) tier table.

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

1. **Fixture gate** — the focused `tests/ILInspector.Decompiler.Tests` fixture that
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
- tests/ILInspector.Decompiler.Tests <ClassTests>: <N> positive, <M> negative, all green
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
3. for any behavior change, the contract / changed-method fidelity evidence the
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

The raised rail resolves every current source-spellable row against the live
module, validates its persisted signature, and passes the resulting typed
method address to floor-disabled ReturnToSender. Product-artifact RTS must
return one aligned row per requested target. Missing native output, stale
identity, and assembly-context failure remain explicit failures, and the
product decompiler fidelity grade controls whether a changed body can form an
opcode or operand verdict. The lowered rail remains the labelled legacy
whole-module evaluator because no product-owned lowered artifact request exists.

Report changed-method runs in three bands:

1. **Attempted population** — total changed methods attempted, plus exact,
   opcode-diff, operand-diff, fidelity-unavailable, `NotFull`, recompile-fail,
   and context-fail counts.
2. **Checkable population** — `Exact` rows that pin a green set and
   `OpcodeDiff` / `OperandDiff` rows that become the semantic docket. These are
   the rows a PR may cite as compile-back evidence. Raised runs identify this as
   product-artifact RTS evidence. On the retained lowered legacy rail, cluster
   mode (`CB_CLUSTER=1`) continues to report **capture provenance** —
   *checkable whole-module* and *checkable cluster-rescued*.
3. **Uncheckable population** — rows classified by reason, such as
   generated/synthesized member, stale delta target, missing reference, or
   `not-safely-capturable` (failed the whole-module attempt *and* the closure
   escalation — typically a Roslyn-class internal cross-assembly graph). Do not
   count them as passing.

The lowered legacy operational order remains **escalate, do not
cluster-first**: run the cheap whole-module grouped compile, then escalate only
rows it could not check to the per-method iterative closure path. Raised RTS
does not use that legacy capture engine. Existing `Exact` labels record the
current comparison contract; they are not compile-context receipts.

Under issue #4810's target contract, a whole-module body comparison is reusable
as `Exact` only when its artifact and member compile-context receipt is complete;
comparison equality alone is not trustworthy. Rows without that receipt
escalate to the closure path. A closure bail is `not-safely-capturable`, and a
post-attempt stalled/root-budget/iteration-budget result remains a typed failure
rather than borrowing whole-module success. Once receipt production is
implemented, this ordering can still avoid unnecessary per-method attempts
because complete whole-module receipts need no escalation.

When repeated skeleton/context fixes only trade compiler diagnostics without
growing the checkable population, stop the incremental repair work and say the
plateau plainly. The next action is either a bounded safety case over the
checkable rows, or a measurement issue before redesign. The #1318 plateau was
measured under #1412: the failures are not predominantly unrelated-sibling
poison but types genuinely inside the target's (often large) reconstruction
closure. The harness ships an opt-in **reconstruction-closure (cluster) emitter**
(`CB_CLUSTER=1`) that reconstructs only the target's transitive closure instead
of the whole module. The current harness falls back to the whole-module skeleton
on bail; under issue #4810's target compile-context receipt contract, that
fallback is a separately labelled control and cannot replace the failed cluster
attempt or inherit its fidelity claim. The gain is library-shaped, not universal — see
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
follow-up work so the PR state remains unambiguous. Keep the `ready-to-merge`
and `carry-forward` PR labels synchronized with
[repository guidance](../AGENTS.md#keep-the-review-clean-label-current).

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

When the defect queue is empty, do not invent rows. Ask which boss is failing:

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
