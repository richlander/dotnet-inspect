# Decompiler Harness

The diagnostic harness from [docs/decompiler.md](../../docs/decompiler.md) — the asmdiffs analog for the decompiler. It inventories the pipeline's health, scores the real-gap completeness, validates output two ways, and dumps a single method through every pipeline stage. This is the invocation reference for the modes; the strategy they serve — which check proves what, what gates CI, the corpus-sweep plan — is [docs/decompiler-quality.md](../../docs/decompiler-quality.md).

## Modes

**Inventory** (default): sweeps every method body in the given assemblies through the pipeline and reports the fidelity histogram plus stop-reason buckets — the prioritized slice roadmap. Exits nonzero if any importer bug (DEC0001) appears. `--max-examples N` sets how many example methods each bucket lists (default 5) — raise it to widen the candidate pool when picking the next target.

**Gaps** (`--gaps`): the completeness view — see below.

**Generated fixtures** (`--generated-fixtures [id|prefix|list]`): builds selected
generated-fixture catalogue entries into a temporary class library, runs the
Roslyn shape oracle and compile-back oracle, and prints results by stable fixture
ID plus target method. This is the first step toward an addressable progressive
fixture ladder:
`minimal.property.literal`, `minimal.ctor-field.getter`,
`minimal.auto-property.getter`, `minimal.method-call.same-type`, and
`minimal.primary-ctor.field-init` are expected `Exact`; the static-call, if/else,
integer-addition, array-index, array-length, string-length, null-coalescing,
try/finally, using/dispose, foreach-array, for-loop, while-loop, and do-while
rungs extend that minimal exact set;
`minimal.switch-int` adds the first switch statement rung.
`minimal.switch-two-case-lowers-if` records the current SDK's two-case
source-switch lowering observation (lowered as if/else, not the dense switch
shape) and is opt-in by ID. `minimal.conditional-expression-shape-frontier`
records a source-shape frontier: the conditional-expression source is
compile-back exact, but the accepted current output is return-statement shaped
rather than `ConditionalExpression` shaped. With no selector, stable generated
fixtures run; use `list` to list fixture IDs, `--json` for machine-readable
list/results, and `--keep-generated-fixtures` to preserve the generated project
for drill-down.
Rows may carry two independent expectations: a Roslyn `SyntaxKind` shape verdict
for the intended C# idiom, and a compile-back opcode verdict for semantic
fidelity. A row can therefore be opcode-exact while still exposing a shape
frontier. Shape frontiers record both the accepted current shape and the desired
frontier shape.

The generated fixture ladder is intentionally staged:

| Stage | Harness responsibility |
| --- | --- |
| Specimen | Catalogue row with source, fixture ID, tags, targets, expected shape, and expected compile-back outcome. |
| Materialization | Build the selected source snippets into one temporary class library; `--keep-generated-fixtures` preserves it. |
| Projection | Decompile each target through the shipped raised product path and record the decompiler fidelity grade. |
| Shape verdict | Parse the rendered body with Roslyn and compare the optional expected `SyntaxKind`. |
| Compile-back verdict | Recompile the rendered body and compare canonical opcode streams with `FidelityCheck`. |
| Frontier ledger | Keep expected non-exact or compiler-lowering observations explicit and opt-in. |

**Library report** (`--library-report`): a portfolio view. It combines the IR
residual buckets from `--gaps` with the Roslyn-backed validity oracle from
`--validity-check`, then prints a global top-pattern section and one section per
library. Use this for the direct "which patterns are unsupported in which
library?" loop. `--top-patterns N` limits the global/per-library pattern lists,
`--top-libraries N` limits the detailed library sections to the noisiest
libraries, and `--json` emits the same data as structured JSON.

**Real-world corpus sensor** (`--diff-corpus-baseline`): the daily/manual
baseline for #1166. It measures the fixed #1150 corpus — pinned NuGet assemblies
plus dotnet-inspect's own assemblies — and compares the run against
`tools/DecompilerHarness/corpus/real-world-baseline.json`. The scheduled
Decompiler Daily workflow tracks fully-raised rate,
`structuring: conditional-branch`, forward-merge structuring stops
(`cond-target-past-region` + `forward-branch-not-region-exit`), Full malformed
output, semantic validity defects, compile-back fidelity defects, and pass bugs.
The validity and fidelity caps are per assembly so the sensor samples every
corpus member at bounded cost without adding that cost to every PR. When you
want to compare a baseline cap with a larger exploratory cap, repeat
`--corpus-fidelity-cap` (or use a comma-separated list) and the harness prints a
fidelity coverage series with the same per-bucket failure breakdown for each cap. The fidelity sample records useful compile-back outcomes (`Exact`/`OpcodeDiff`) while surfacing recompile- and context-failure buckets for triage. Each daily run
uploads the current JSON snapshot as the `decompiler-corpus-snapshot` artifact so
trends can be compared without scraping logs.

```bash
dotnet build src/dotnet-inspect -c Release -p:PublishAot=false
bash eng/prepare-decompiler-corpus.sh /tmp/corpus-assemblies.txt
mapfile -t assemblies < /tmp/corpus-assemblies.txt
dotnet run --project tools/DecompilerHarness -c Release -- "${assemblies[@]}" \
  --emit-corpus-snapshot /tmp/corpus-snapshot.json \
  --diff-corpus-baseline tools/DecompilerHarness/corpus/real-world-baseline.json \
  --quality-diff-card \
  --compile-cap 25 \
  --corpus-fidelity-cap 3 \
  --max-examples 3
```

Add `--quality-diff-card` to emit a Markdown PR block generated directly from
the baseline/current snapshots. The card includes a correctness-coverage line
and per-row sampled denominators for semantic validity and compile-back fidelity
so reviewers can tell when evidence is strong or thin. Paste that block as the
PR's aggregate corpus evidence; do not re-key the table by hand.

Add `--emit-corpus-delta <file>` with `--diff-corpus-baseline` to write the
changed per-method rows as JSON. The quality card stays compact and names the
artifact path; reviewers and follow-up scripts can use the JSON to pick changed
methods for targeted dump/fidelity checks.

For risky raise/structuring PRs, add `--quality-card-risky`. It keeps the same
card shape but warns when semantic validity coverage is below 1.00% or
compile-back fidelity coverage is below 0.10%, and reminds authors to add
targeted improved examples plus still-flat near misses.

For risky PRs whose changed-method population is known, use that population as
the fidelity target. The general corpus card is still the aggregate health view,
but it can be green while the methods a broad structuring change actually
rewrote remain unchecked. Generate a per-method delta, inspect the changed rows,
and run targeted dump/fidelity checks over that set before relying on the global
sample. If the changed population is mostly not recompilable, classify those
failures first; simply raising `--corpus-fidelity-cap` grows an easier general
sample, not necessarily the risky shape.

If a changed-method run plateaus — compiler diagnostics trade buckets while the
`Exact` + `OpcodeDiff` population stays flat — stop treating each new diagnostic
as another incremental skeleton task. Report the checkable population separately
from named uncheckable buckets, and measure whether failures are in the target's
reconstruction closure before redesigning compile-back context. The #1412
closure-measurement pass is the model: size unrelated-sibling poison vs.
in-closure failures before building a scoped emitter.

To deliberately rebaseline after reviewed corpus movement, run the same command
with `--emit-corpus-baseline tools/DecompilerHarness/corpus/real-world-baseline.json`.
For a quick before/after coverage sweep, repeat `--corpus-fidelity-cap` (for
example `--corpus-fidelity-cap 3 --corpus-fidelity-cap 10`).

The corpus includes the repo's own assemblies, which grow as unrelated code
lands, so a baseline captured earlier can disagree with the current run on
method population even when a PR changed no decompiler behavior. When that
happens the card prints a **`Baseline staleness:`** block (which assemblies
drifted and the net method-count delta).

To keep that drift from producing false verdicts, the PR quick card gates
rate/count regressions on the **pinned-NuGet subset** when per-method snapshots
are available: a fixed, fixed-version method set whose counts move only when
decompiler behavior does. The card prints a **`Pinned-subset gate`** line showing
those stable rates and counts alongside the drifting aggregate rows, and any
`(pinned)` regression in the `Regressions:` list is a real decompiler delta
rather than repo growth. Aggregate fully-raised, conditional-residual, and
forward-merge rate movement still appears in the table and in an advisory block
when it crosses tolerance, but it is not a normal PR quick hard gate. Risky
decompiler changes can opt back into aggregate rate hard-fails with
`--quality-card-risky`, and non-card/daily runs still use the aggregate rates.
Pass-bug crashes always gate on the full aggregate. The pinned gate is computed
from the per-method snapshots both baseline and current carry, so no baseline
regen is required; it falls back to aggregate counts/rates when a snapshot lacks
per-method detail.

Expand the fixed corpus only after that targeting step shows a shape gap. Prefer
deterministic, pinned assemblies that add many examples of the missing lowering
family (for example forward-merge or retained-label control flow), then refresh
the baseline and prove a no-op `--emit-corpus-delta` stays empty. Keep the PR
quick corpus small; broad shape additions belong in the daily/manual corpus
unless they are cheap and stable enough for every PR.

**PR quick corpus** (`tools/DecompilerHarness/corpus/pr-quick-baseline.json`):
CI also runs a small artifact-producing corpus sensor after the managed tool
build. It takes a deterministic hash-ranked sample of 100 methods per assembly
across a mixed 15-assembly set: System.Private.CoreLib, the pinned package
libraries used by the daily corpus, and dotnet-inspect's managed product
assemblies. The hash-ranked sample avoids the order churn of "first N" metadata
rows while still staying small. The run skips the expensive semantic validity
and compile-back fidelity oracles so it stays small; the daily workflow remains
the authoritative full-corpus signal.

Keep the corpus-prep script paired with its matching baseline. The PR quick card
uses `eng/prepare-decompiler-pr-corpus.sh` with
`tools/DecompilerHarness/corpus/pr-quick-baseline.json`; the daily/manual
real-world card uses `eng/prepare-decompiler-corpus.sh` with
`tools/DecompilerHarness/corpus/real-world-baseline.json`. Do not mix the full
corpus script with the PR quick baseline, or the card will report artificial
assembly additions/removals such as System.Private.CoreLib appearing to drop out
of the sample.

When a card shows capped changed rows, use
[Reproducing decompiler corpus deltas](../../docs/decompiler-corpus-delta-repro.md)
to select the matching PR commit and regenerate the full local delta.

```bash
dotnet build src/dotnet-inspect -c Release -p:PublishAot=false
bash eng/prepare-decompiler-pr-corpus.sh /tmp/pr-corpus-assemblies.txt
mapfile -t assemblies < /tmp/pr-corpus-assemblies.txt
dotnet run --project tools/DecompilerHarness -c Release -- "${assemblies[@]}" \
  --diff-corpus-baseline tools/DecompilerHarness/corpus/pr-quick-baseline.json \
  --quality-diff-card \
  --corpus-method-cap 100 \
  --compile-cap 0 \
  --corpus-fidelity-cap 0 \
  --max-examples 3
```

The CI artifact (`decompiler-pr-corpus`) contains the assembly list, generated
snapshot JSON, generated Markdown quality card, and exit code. It is useful as a
fast smoke/regression slice over real assemblies and for reviewer download, but
it is intentionally too small to prove broad corpus quality by itself.

**Unsupported nodes** (`--unsupported-nodes`): a focused view of the
`fidelity: unsupported-node` bucket. It runs the normal raising pipeline, walks
the finished tree for every `UnsupportedNode`, and groups sites by opcode and
normalized reason while keeping concrete method examples. Use it after
`--gaps`/`--library-report` show a small unsupported-node bucket and you need to
classify it into intentional unrepresentable IL, a missing printer/importer
slice, or a larger raise such as iterator reconstruction. `--json` emits the
same report as structured data.

**DEC0009 classifier** (`--classify-dec0009`, alias `--dec0009-shapes`): a discovery view for the
unrepresentable metadata-name bucket. It runs the normal decompiler pipeline,
collects `DEC0009` fidelity remarks, and groups affected methods by generated
name family such as anonymous types, display classes, state machines,
lambda/cache holders, method-group caches, regex/source-generator types, and
read-only-array helpers. The report prints both every method with a `DEC0009`
remark and the subset whose primary `--library-report` bucket is
`fidelity: DEC0009`; `--json` emits the same data for issue triage. Categories
also carry a disposition. The read-only-array helper family is classified as
`generated-internal/non-actionable`, so it remains visible in total DEC0009
counts while being excluded from the actionable DEC0009 counters that drive
follow-up work.

### Multi-mode fixture matrix (on-demand)

**Why it exists.** The CoreLib corpus and the CI gates measure **one compiler
mode** — whatever the framework shipped (today: `runtime-async=on`, updated
memory-safety rules, Release). But the decompiler's job is to read *every*
assembly anyone feeds it, and the same C# lowers to different IL under different
compiler flags. The biggest such split is **async**: `runtime-async=on` emits an
`AsyncHelpers.Await` call (raised by `AwaitRecoveryPass`), `off` emits a classic
`AsyncTaskMethodBuilder` state machine (a `<M>d__N` struct + `MoveNext`) — two
unrelated lowerings. A construct that only the *off* mode produces is invisible
to a single-mode sweep, so the corpus reports "0" not because the decompiler
handles it but because the corpus never contains it. That is a measurement blind
spot, not a statement of value: the classic state machine is the dominant
real-world async form.

**How it works.** The matrix is the `[Theory]`/`[Params]` idea made physical:
mode-sensitive fixture source is compiled with one flag flipped. When the same
source is legal in both modes, reuse it; when a mode changes source legality or
the required spelling, use a paired representative source that produces the same
IL and differs only in the mode metadata. The mode axis is a compiler flag, which
is per-assembly, so "vary a fixture over a mode" means "compile it into another
assembly." Most fixtures are mode-agnostic (identical IL either way) and stay in
the default assembly; only the mode-*sensitive* ones go into thin **overlay**
projects that flip a single flag. Cost: one big default assembly plus a few
progressively-smaller single-flag overlays — never the corpus times N. Axis
switches live in `Directory.Build.targets`: `<RuntimeAsync>off</RuntimeAsync>`
opts out of the global `runtime-async=on`; `<MemorySafetyRules>updated</MemorySafetyRules>`
opts a fixture into `/features:updated-memory-safety-rules`.

**On-demand, not a CI gate.** These overlays are a discovery and bring-down
instrument, not a regression wall — build one and point `--library-report` at it.
The first axis is `src/ILInspector.Decompiler.Fixtures.ClassicAsync` (the async
fixtures at `runtime-async=off`):

```bash
dotnet build src/ILInspector.Decompiler.Fixtures.ClassicAsync -c Release
dotnet run --project tools/DecompilerHarness -c Release -- --library-report \
  artifacts/bin/ILInspector.Decompiler.Fixtures.ClassicAsync/release/ILInspector.Decompiler.Fixtures.ClassicAsync.dll
```

Baseline (classic async unraised): 21 methods, 0 raised, with 0 pass bugs and 0
`Full`-malformed — the state machines degrade honestly, never mis-raise. The
7 `MoveNext`s bucket as `structuring: conditional-branch` (the goto state
dispatch the structurer can't raise); the 14 kickoffs and state-machine helpers
bucket as `fidelity: DEC0009` (`UnrepresentableMetadataName` — their residual
`<>`-prefixed members, `<…>d__N`/`<>t__builder`/`<>1__state`, have no legal C#
spelling until the shape is raised). A future raise's proof obligations are the
queue's falsification list: kickoff/`MoveNext` correlation, state dispatch,
builder identity, await ordering, and exception/finally paths.

The second axis is the old/new memory-safety pair:

```bash
dotnet build src/ILInspector.Decompiler.Fixtures.LegacyUnsafe -c Release
dotnet build src/ILInspector.Decompiler.Fixtures.NewUnsafe -c Release
dotnet run --project tools/DecompilerHarness -c Release -- --library-report \
  artifacts/bin/ILInspector.Decompiler.Fixtures.LegacyUnsafe/release/ILInspector.Decompiler.Fixtures.LegacyUnsafe.dll \
  artifacts/bin/ILInspector.Decompiler.Fixtures.NewUnsafe/release/ILInspector.Decompiler.Fixtures.NewUnsafe.dll
```

`Fixtures.LegacyUnsafe` sets `<MemorySafetyRules>legacy</MemorySafetyRules>` and
carries no module `MemorySafetyRulesAttribute`; `Fixtures.NewUnsafe` sets
`<MemorySafetyRules>updated</MemorySafetyRules>` and carries the attribute. The
source spellings differ where the language rules differ, but the IL is the same;
the mode metadata is what drives conservative old-vs-new rendering. Baseline:
both assemblies are 8/8 full and fully raised, with no unsupported patterns.
`Fixtures.UnsafeChainA/B/C` extends the same axis to cross-assembly
`RequiresUnsafeAttribute` resolution and optimistic `--simulate-new-rules`
diagnostics.

The checked-arithmetic axis is `src/ILInspector.Decompiler.Fixtures.CheckedArithmetic`:

```bash
dotnet build src/ILInspector.Decompiler.Fixtures.CheckedArithmetic -c Release
dotnet run --project tools/DecompilerHarness -c Release -- --library-report \
  artifacts/bin/ILInspector.Decompiler.Fixtures.CheckedArithmetic/release/ILInspector.Decompiler.Fixtures.CheckedArithmetic.dll
```

It sets `<CheckForOverflowUnderflow>true</CheckForOverflowUnderflow>` so plain
arithmetic/conversions lower to `*.ovf` opcodes. Baseline: 10/10 full and fully
raised, with no unsupported patterns or pass bugs. The axis is still useful as a
discovery guard: any future checked-context fixture that does not remain full
should become a focused issue from the report.

**The quality loop it drives — discovery, then bring-down.** This is how the
matrix feeds the quality program (see
[docs/decompiler-quality.md](../../docs/decompiler-quality.md), "Multi-mode
coverage"):

1. **Discover.** Run `--library-report` over an overlay. The unsupported-pattern
   buckets are gaps the single-mode corpus could never surface — each a real,
   named decompiler gap (the classic-async `MoveNext` residual above). File them
   as **pattern-pivoted issues** — one issue per pattern, its hits in the body —
   so a single agent owns each raise end to end (see the quality doc, "From report
   to ownable issues").
2. **Bring down.** Pick a bucket, raise the idiom (a pass), and re-run the
   report: the bucket shrinks, method by method. The count is the tracked signal
   — "21 → 14 → 0 raised" is real progress against a real mode.
3. **Prove no regression.** The default-mode corpus and the CI gates still guard
   that you did not break the *shipped* mode while teaching a new one. Pair the
   overlay report with the usual `--diff-validity-defects` / `--fidelity-check`
   on the default corpus.

So the corpus catches "did we regress the default mode broadly," and the matrix
catches "do we handle this lowering mode *at all*" — complementary, not
redundant.

**Adding an axis.** When you find a mode the decompiler must handle but the
corpus omits: (1) put the mode-sensitive source in its own file (or reuse shared
fixtures when one source is legal in both modes); (2) add a thin overlay csproj
that sets the flag — preferably through a property in `Directory.Build.targets`
for a reusable axis; (3) build it and baseline with `--library-report`. Keep it
out of CI references so it stays a discovery tool. Candidate next axes:
**checked arithmetic** (`CheckForOverflowUnderflow`) and **downlevel framework /
LangVersion** (an older TFM, which cascades classic async + old switch/iterator
lowerings at once).

**Validity check** (`--validity-check`): the *validity* check — `--gaps` is *completeness*, `--fidelity-check` is *fidelity*, this is *does it even compile*. The pipeline guarantees by construction only that it never crashes and never silently fabricates (unrepresentable IL becomes a visible `/* … */` comment and drops fidelity to `Partial`) — **not** that the rendered text is valid C#. This mode measures the gap: each body is wrapped in a method shell carrying its real signature (return type, generic parameters with their `where` constraints reconstructed from metadata, parameters, so locals/params/type-params and `this` all bind — without the constraints a constrained generic-math call like `byte.TryConvertFromTruncating<TOther>` spuriously fails CS0314), then (1) parsed — a parse error is unambiguously a decompiler defect; (2) checked for statement legality (the CS0201 rule — a bare cast/expression statement parses but isn't valid); (3) bound against the runtime references. Diagnostics are bucketed by code with the member/type-**visibility** codes (the shell can't see the real declaring type's fields/methods) filtered as noise, so genuine defects stand out — `CS0193` (`*`-deref of a managed ref), `CS0175` (`base(...)` rendered as a statement), `CS1620` (an `out` argument not marked `out`), `CS0165` (a local used before the decompiler assigned it). Reported split by fidelity: a `Partial` method is *expected* to carry invalid fragments; a **`Full` method that fails to compile is the real "claimed good but isn't" signal** and the prioritized fix docket. Compiler-generated members are excluded (their metadata names aren't valid identifiers). `--compile-cap N` bounds the (slow) semantic-binding pass.

*Defect tracking — prove a fix regressed nothing.* A raw count (e.g. "CS0266: 263") tells you a bucket shrank but not *which* methods changed, so it cannot distinguish a real fix from a fix that also broke something else. `--emit-validity-defects <file>` writes the per-method defect map (one `Type::Method<TAB>CODE,CODE` row per method) before your change; after the change, `--diff-validity-defects <file>` re-runs the check and prints the differential against that baseline — **REGRESSED** (methods that gained a code) and **IMPROVED** (methods that lost one), per code. A clean fix shows entries only under IMPROVED with an empty REGRESSED; any REGRESSED row is a method your change broke. Only methods checked in *both* runs are compared (cap-boundary methods are excluded), so keep `--compile-cap` identical across the baseline and diff runs. This is the regression-proof loop behind a "N→M occurrences, 0 regressions" claim.

**Fidelity check** (`--fidelity-check`): the *semantic-fidelity* check — `--gaps` is *completeness*, the validity check is *validity*, and this is *does it still mean the same thing*. It closes the round trip named in [docs/decompiler.md](../../docs/decompiler.md): decompile → recompile → compare IL. A body that parses, binds, and reads plausibly but recompiles to a **different opcode stream changed the program** — the worst failure class ([docs/decompiler-taste.md](../../docs/decompiler-taste.md)), invisible to every other check because they never run the output back through a compiler. Each member is recompiled inside a reconstructed **whole-module skeleton** — every top-level type stubbed (fields present, sibling and nested members as throwing stubs) with the one target carrying its real decompiled body, the C# analog of the IL round-trip suite's `IlasmScaffold`. With fields and sibling types in scope, a dropped or mis-bound field access surfaces as a true opcode diff rather than a bind error. The recompiled method is disassembled and its canonical opcode stream (short forms folded, `ldarg`/`ldloc`/`ldc.i4` families normalized) compared against the original; `Full`-fidelity diffs are the docket. References are the running runtime plus the target's sibling DLLs, minus the target itself (it is reconstructed, not referenced). Recompile failures here overlap `--validity-check` (an un-bindable body cannot be opcode-compared) and are reported separately, not as diffs. Compiler/source-generated implementation details — generated-code attributes, compiler-synthesized names, and `JsonSerializerContext` helper types — are skipped because their emitted members are not actionable source-shape fixes; source-spellable auto-property accessors still remain in scope. `CB_TYPE=<substr>` filters to a type; `CB_DUMP=1` prints the first failing compilation units. `--compile-cap N` bounds the slow recompile pass before collecting and compiling a type, so cap-boundary types do not compile more target bodies than the remaining budget. Add `--fidelity-timings` to print phase timings for collect/render, skeleton emit, parse, compilation creation, emit, and opcode comparison. Add `--fidelity-zero-signal-guard N` for large exploratory runs: it probes the first `N` methods and stops early when the probe has no `Exact`/`OpcodeDiff` rows and one failure bucket dominates, reporting the population as zero-signal/uncheckable instead of scaling the same failure to the full cap.

Add `--fidelity-method-delta <delta.json>` when the question is "did the
methods this PR changed still compile back faithfully?" The input is the
per-method artifact from `--emit-corpus-delta`; removed methods are skipped and
current changed methods are attempted exactly, with `Exact`, `OpcodeDiff`,
`RecompileFail`, `ContextFail`, and `NotFull` buckets. Changed methods on
**nested types** are matched through their declaring type (`Outer.Inner`), so a
risky PR's nested-type changes are measured rather than silently dropped.
Compiler-synthesized rows the skeleton can never recompile — regex
source-generator output, lambda display classes, iterator/async frames, the
`<Module>` pseudo-type, any `<…>` name — are classified up front into a
`generated/synthesized member (unsupported)` bucket instead of masquerading as a
lookup miss. A `target method not found` row therefore means a genuine identity
miss: most often a **stale delta** whose method signature has drifted from the
current corpus build (e.g. a return type changed since the snapshot), so the
exact method no longer exists to attempt.

*Reconstruction-closure (cluster) capture* (`CB_CLUSTER=1`, opt-in). The
whole-module skeleton is all-or-nothing: because the target assembly cannot be
referenced (the reconstructed type would collide with it), **every** top-level
type must be stubbed, so a single un-reconstructable sibling type — an unrelated
printer gap in a type the target never touches — poisons the compile and the
target is scored `RecompileFail` for reasons that have nothing to do with its
own fidelity. Cluster mode reconstructs only the target's transitive closure: it
emits the target's type, compiles, and adds every same-assembly type the
compiler names as missing (`CS0246`/`CS0234`/`CS0103` for types, `CS1061` for
static/extension members), recompiling until the unit binds or the closure stops
growing. The compiler computes the exact closure, so unrelated sibling types are
simply omitted. A `CS0234` names a missing namespace *segment*
(`'Serialization' does not exist in the namespace 'Newtonsoft.Json'`) rather than
a leaf type, so the closure reconstructs the full namespace from the diagnostic's
two quoted spans and pulls in the roots declared directly in it — without this,
any body that reaches into a sub-namespace stalls the closure (it was the dominant
bail on real libraries: ~81% on Newtonsoft.Json, driving exact-match from 7.9% to
10.3%).

`CB_CLUSTER=1` runs the **escalation** order: the cheap whole-module grouped
compile first, and only the rows it could not check (`RecompileFail`/`ContextFail`)
are escalated to the per-method closure path. A whole-module `Exact` never needs
re-checking — only its failures can improve — so escalation reaches the same
checkable population as attempting the closure on every row, at a fraction of the
cost. When the closure cannot be closed within budget (default 200 roots / 80
iterations) the row **falls back to its whole-module result**, so cluster results
are always ≥ the baseline: it never regresses, only rescues targets the
all-or-nothing skeleton failed for unrelated reasons. A persistent bail is itself
a useful signal — the target is *not safely capturable* in isolation (typically a
Roslyn-class internal cross-assembly graph), the population for which
changed-method fidelity is least meaningful. `CB_CLUSTER_DUMP=1` prints bail
diagnostics.

Each row carries its capture provenance (whole-module, cluster-rescued, or
cluster-bailed), and the changed-method report prints the segmented
**safely-capturable bands** — checkable whole-module, checkable cluster-rescued,
and not-safely-capturable — so a go/no-go comment can separate the rows it may
cite as compile-back evidence from the rows it must not count as passing. The
gain is modest and library-shaped, not universal: on a Roslyn-heavy stress delta
it rescues a handful of non-pathological rows. The closure improves lever by
lever as it learns to satisfy what the compiler names — namespace-segment
inclusion (the dominant `CS0234` bail), a synthetic parameterless constructor
stub for reconstructed classes whose base lacks one (so a derived stub's implicit
`base()` binds instead of failing `CS1729`/`CS7036`), and reconstructing sibling
properties as property syntax (so a body's `obj.X` binds instead of failing
`CS1061`, preserving accessor virtualness so a `?.X` call kind is unchanged)
together took Newtonsoft.Json exact-match from 7.9% to 43.4%. The dominant
remaining bail is then inherited and extension members (`CS1061` on members a
base type declares); resolving those is the next tracked follow-up.

```bash
dotnet run --project tools/DecompilerHarness -c Release -- "${assemblies[@]}" \
  --fidelity-check \
  --fidelity-method-delta /tmp/pr-corpus-delta.json
```

*When to use it.* Reach for fidelity check when the question is **"is this decompilation faithful,"** not "does it compile." Run it after any change to the importer, a raising pass, or the printer that could alter emitted semantics — branch sense, checked/unchecked, conversions, field/local ordering, shift masking — and read the `Full` opcode-diff bucket as a regression docket. It is the tool that catches a fix in one method silently degrading another. Prefer the small, fast, purpose-built fixture corpus (`CfgSampleClass` in `ILInspector.Decompiler.Tests`) for a tight loop; sweep a real assembly (BCL) for breadth once the fixtures are clean. Use `--validity-check` first when you only need to know the output is valid C#, and `--gaps` to track the structuring completeness.

*The CI gate.* The console mode above is for exploration; the durable regression guard is `FidelityGateTests` in `ILInspector.Decompiler.Tests`, which calls the same machinery through `FidelityCheck.Evaluate` (the non-printing, structured-result entry point) over `CfgSampleClass`. It fails CI when a method newly recompiles to a different opcode stream (a regression beyond the documented `KnownDiffs` docket) and when a previously-fixed method (`PinnedExact`) regresses. Shrink `KnownDiffs` as you fix docket entries; add the fixed method to `PinnedExact` to pin the fix. `LoweredFidelityGateTests` is the twin gate for the lowered view (`--lowered`), with its own docket — the lowered C# is recompiled and opcode-compared the same way, so both official C# views earn a per-view E2E roundtrip.

**Stage dump** (`--dump 'Type::Method'`): JitDump for the decompiler — runs one method through the pipeline and prints the IR tree at every stage boundary (the importer output, then after each raising pass), ending in the shipped product C# (`PrintRaised`). So the output is exactly what each pass left behind. When a name resolves to several overloads, `--dump` selects index `0` but prints the overload menu (index, signature, body/no-body) to stderr so you can see what was chosen and pick another with `--index N` (stdout stays pipe-clean); `--list-overloads` prints that menu and stops. Add a sub-mode to narrow what `--dump` shows: `--steps`/`--step-limit` (per-pass fine-grained rewrites), `--facts` (the printer's definite-assignment `gen`/`in`/`out` sets that decide which locals keep `= default`), `--cfg` (per-block predecessor/successor edges; add `--mermaid` for a GitHub-renderable flowchart), `--diff` (each pass's effect as a unified `+`/`-` hunk over the previous stage), or `--remarks` (every IR site that caps the method below `Full` fidelity, with its `DEC####` code, block offset, and reason). Two orthogonal reading dials apply to any of these: `--il` prepends the annotated-IL import views (raw/typed/structured) above the stage dump, and `--skip-pdb` ignores any portable PDB so locals render as `V_index` — deterministic, symbol-independent output regardless of nearby symbols (cosmetic only; it never changes emitted IL, so it cannot affect a fidelity result).

**Lowered view** (`--lowered`): a render selector, orthogonal to the dump sub-modes above, that lowers the *altitude* of the emitted C# rather than projecting a different analysis. It runs `IrPasses.Lowered` — the shipped pipeline minus the cosmetic statement-sugar passes (`for`/`foreach`, `lock`, `++`/`--`) — so the output is the decompiler's SharpLab "lowered C#": valid, recompilable C# at a lower level (`while` loops, explicit temps, explicit `Monitor.Enter`/`Exit`). It applies to `--dump` (with facts comments), `--validity-check --lowered` (its compile rate), and `--fidelity-check --lowered` (its opcode roundtrip).

**Simulate new rules** (`--simulate-new-rules`, with `--dump`): the optimistic memory-safety render selector — another render dial orthogonal to the dump sub-modes, but it changes *which unsafe contexts are emitted* rather than the C# altitude. By default the printer is conservative: it emits explicit `unsafe { }` blocks only for a module that opted into the `updated-memory-safety-rules` feature (a module-level `MemorySafetyRulesAttribute`), so legacy output is byte-identical. With this flag it forces new-rules rendering on for *any* input, wrapping the operations the new rules would require even in a legacy module. It only recovers contexts the binary still records — IL-visible ops (`*p`, `calli`, `stackalloc`+`SkipLocalsInit`), pointer-in-signature calls, and a cross-assembly `[RequiresUnsafe]` callee (the attribute lives in the opted-in callee's assembly, read through the shared `MetadataContext`). A legacy same-assembly pointerless `unsafe` method leaves no trace, so simulate honestly emits no block for it. The conservative vs. optimistic contract and its recoverability limits are [docs/design/memory-safety-modes.md](../../docs/design/memory-safety-modes.md).

**Pass impact** (`--pass-impact [pass]`): the corpus-wide *inverse* of `--dump --diff`. `--diff` answers "for this method, what did each pass do"; `--pass-impact` answers "for this pass, which methods does it change" — its blast radius across an assembly. With no pass named it prints a histogram (each pass and the count of methods it altered, the "which passes carry the load" roadmap); with a pass name it lists every method that pass changed. Add `--show-diff` to print each changed method's per-pass hunk beneath it. `--cap N` stops the sweep after `N` methods — a full-CoreLib stage sweep is not free, so cap it for a quick read. A pass that runs more than once in the pipeline (`typed-constants`, `expression-inlining`) counts a method once if any occurrence changed it.

**Gaps** (`--gaps`): the *self-contained* real-gap view. It inspects only the raised tree: a method is a gap iff it still holds **unstructured control flow** — a `Branch`/`ConditionalBranch`/`SwitchBranch` the structuring passes could not consume, or an EH `Leave` (a surviving `goto`) — or an `UnsupportedNode`. A fully-raised tree holds only structured nodes (`IfStatement`, loops, `Switch`, `TryCatch`), so the residual is exact: reading the tree alone tells you the gap, no recompile or comparison needed. It reports "fully raised" (the metric to drive up) and a residual-kind docket (the prioritized work). It measures completeness, not correctness, so pair it with `--fidelity-check` for fidelity.

*When to use it.* Track the structuring completeness with `--gaps`. Over CoreLib it currently reads ~97% fully raised, the residual dominated by `structuring: conditional-branch` (the forward-branch-to-common-exit work). Add `--by-shape` to sub-classify the `switch-branch` bucket by the structural shape of its residual switch, classifying the imported (pre-pass) tree where the switch is still a block terminator. The buckets, in priority order, are `loop-back-edge` (a section block branches backward — a loop/iterator), `nested-switch` (a case body is itself a switch, or the method has more than one), `default-routes-into-cases` (the default is a case target or branches into one), `external-entry-into-cases` (a block before the switch jumps into a case body), `multi-block-case-section` (a case body carries its own `if`/`?:` — raised by the section-as-region relaxation), and `single-block-clean` (none of the above — a clean switch that nonetheless did not raise, i.e. a pass bug to investigate). A bucket count becomes a per-shape slice docket that scopes the next `SwitchRaisingPass` relaxation.

**Structuring stops** (`--structuring-stops`): the *why-not* companion to `--gaps`. Where `--gaps` reports *that* a method's tree stayed flat (the residual-kind docket), this tallies *why* `StructuringPass` left a container flat — a per-reason histogram of its stop diagnostics across the corpus, each with an example method, plus the `containers structured` vs `left flat across N methods` totals. The dominant reason is the next structuring slice to attack (e.g. the common-exit merge work behind the `conditional-branch` gap). Honors `--cap N` to bound a full-CoreLib sweep, and exits nonzero on any pass bug (a structuring crash). Pair it with `--dump --cfg` on one of its example methods to see the concrete block graph that defeated the pass.

**Annotation check** (`--annotation-check`): the hidden-fact annotation check — the analyzer analog of `--fidelity-check`. Where fidelity check grades the decompiler's *C#* against a recompiled opcode stream, this grades each *annotation* (the allocation/unsafety/lifetime facts from [docs/design/hidden-fact-annotations.md](../../docs/design/hidden-fact-annotations.md)) against the raw IL opcode it claims to describe. The witness is read with the runtime-ported `ILReader` directly over the method's IL bytes — **not** via the IR importer that produced the annotations — so the two paths share only that externally byte-match-validated reader, never the semantic classification logic under test. It measures two directions: **precision** (every annotation's offset carries a consistent opcode — an `alloc.box` sits on a `box`; a violation is an importer-typing or classifier bug) and **recall** (every *unambiguous* witness opcode produced its annotation — a `box`/`newarr`/`localloc`/`calli`, plus every confirmed reference-type `newobj`, always yields its fact). A `newobj`'s constructed type is resolved from metadata (operand token → constructor → declaring-type base chain, or a TypeSpec signature) independently of the importer; the ambiguous remainder stays precision-only and out of the recall gate: a value-type `newobj` (a struct constructor) allocates nothing, a bare cross-assembly `TypeRef` can't be resolved from a single-assembly walk (the documented value-type gap), and a `ldind`/`stind` may be a safe managed-`ref` access. A confirmed value-type `newobj` is additionally held to the *opposite* precision rule — it must **not** carry an allocation fact — catching a false-allocation claim the opcode-precision check is blind to. Recall also excludes partial-import methods, where a stop legitimately leaves later opcodes with no IR node. Exits nonzero on any precision violation (a wrong fact) or import crash.

*When to use it.* Run after any change to allocation, unsafety, or lifetime occurrence production, or to the importer's typing/metadata layer those producers read (value-type hints, signature decoding). A precision drop on a category points straight at the bug. Over .NET 11 preview CoreLib it currently reads **100% precision** (all descriptors plus the value-type-newobj no-allocation checks) and **100% recall** (the gated witnesses, including ~9k confirmed reference-type newobjs).

*The CI gate.* The console mode above is for exploration; the durable regression guard is `AnnotationGateTests` in `ILInspector.Decompiler.Tests`, which calls the same machinery through `AnnotationCheck.Evaluate` (the non-printing, structured-result entry point) over the running runtime's CoreLib. It is the breadth gate (analog of `FidelityGateTests`, the fixture depth gate): it fails CI on any precision violation (a wrong fact, always a bug — never runtime drift, so gated absolutely) or import crash, holds recall above a floor, and asserts a large checked population so a refactor that silently stops producing annotations cannot pass vacuously.

**Type-source check** (`--type-check`): the *whole-type source* oracle ([issue #1112](https://github.com/richlander/dotnet-inspect/issues/1112)) — where `--fidelity-check` closes the loop on one method body's opcodes, this validates the file- and type-level artifacts the product `TypeSourceComposer` emits: the namespace, the type kind and modifiers, and the complete member surface. The metadata inventory (`ApiSurfaceExtractor`) is ground truth; the composed whole-type listing is the output under test. Roslyn parses the listing with **member bodies stubbed** — a resilient lexer pass blanks every block and expression body before parsing — so a method body the decompiler cannot render (a synthesized `<Clone>$d__2` name, an unbindable cast) can neither derail recovery of a *sibling* declaration nor be reported as a phantom artifact defect. The comparison is purely syntactic and never binds, so it is orthogonal to (and not masked by) method-body codegen. Member matching folds the projections the composer applies: operators render under their raw `op_*` name, an indexer renders as `this[...]`, an explicit interface property implementation renders by its short name, and an enum's synthetic `value__` is not source. The product path stays SRM-only, NativeAOT-friendly, and Roslyn-free; Roslyn lives only here in the oracle.

*When to use it.* Reach for type-check when the question is **"is the whole-type file right,"** not "does a body mean the same thing" — after any change to `TypeSourceComposer`, the type-declaration rendering, or `ApiSurfaceExtractor`. Deltas are bucketed by kind (`namespace`, `type-kind`, `modifier-dropped`/`-extra`, `member-missing`, `type-decl-missing`) with examples. Over the current .NET 11 preview CoreLib sample, `--type-check --cap 2000` is clean (0 type-level artifact deltas over 1,098 composed types); a new bucket is therefore a concrete type-artifact regression to route to the composer, signature display, or surface extractor rather than to method-body fidelity. The pure comparison (`TypeSourceCheck.CompareType`) and the assembly driver (`TypeSourceCheck.Evaluate`) are covered by `TypeSourceCheckTests` in `ILInspector.Decompiler.Tests`, including a fixture proving an unparseable method body does not mask a sibling artifact.

**Type-bind check** (`--bind-check`): the *whole-type binding* oracle ([issue #1137](https://github.com/richlander/dotnet-inspect/issues/1137)) — the binding companion to `--type-check`. Where type-check is purely syntactic, this one **compiles** each composed type against the platform reference set and reports the `CS0104` ambiguous-reference collisions that only a binder can see. A collision happens when the listing imports two namespaces that both define a simple name the source uses unqualified. The composer already keeps a name qualified when its *own* metadata shows two owners (the detectable case, #1017); the residue this oracle catches is the **undetectable** case — the competing type is not a TypeDef/TypeRef of the composed assembly, so the SRM-only product path, which must not enumerate external namespaces, cannot know it exists. Method bodies are stubbed (the same `StubMemberBodies` pass type-check uses) before binding, so a body-codegen defect can neither manufacture nor mask a type/signature-level collision. Roslyn lives only here in the oracle; the product path is unchanged.

*When to use it.* Run after any change that can alter a composed type's binding
environment: `TypeSourceComposer` using-hoisting, namespace qualification, type
name shortening, explicit-interface/type-source rendering, or the reference set
the harness binds against. Do **not** use it as a method-body proof; bodies are
stubbed before binding so the signal stays at the type/signature level. The
canonical artifact is `System.AppDomain.ExecuteAssembly`, whose
`AssemblyHashAlgorithm` parameter (from `System.Configuration.Assemblies`)
collides with `System.Reflection.AssemblyHashAlgorithm` once `System.Reflection`
is imported for other members — its namespace is seeded by the *parameter type
itself*, not by signature-text shortening, so it cannot be suppressed by a
conservative-hoist policy without qualifying signature types wholesale.
Known-unfixable artifacts are allowlisted in `TypeBindCheck.KnownArtifacts`; a
*new* collision exits nonzero (the listing would not compile), and a new
allowlist row should explain why the ambiguity is unknowable from the SRM-only
product path. The breadth gate is `TypeBindGateTests` in
`ILInspector.Decompiler.Tests` (analog of `TypeSourceCheckTests`), which binds
the whole running-runtime CoreLib and fails on any collision outside the
allowlist.

## Usage

```bash
# CoreLib of the running runtime (default input)
dotnet run --project tools/DecompilerHarness -c Release

# Per-assembly unsupported-pattern report for a product or shared framework folder
dotnet run --project tools/DecompilerHarness -c Release -- \
  artifacts/bin/ILInspector.Decompiler/release/ILInspector.Decompiler.dll --library-report \
  --top-patterns 5
dotnet run --project tools/DecompilerHarness -c Release -- \
  /path/to/shared/Microsoft.NETCore.App/11.0.0 --library-report \
  --top-patterns 10 --top-libraries 12 --json

# Split DEC0009 by generated-name family
dotnet run --project tools/DecompilerHarness -c Release -- \
  /path/to/shared/Microsoft.NETCore.App/11.0.0 --classify-dec0009 --max-examples 10

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

# Generated progressive fixture catalogue: build source snippets, then compile-back.
dotnet run --project tools/DecompilerHarness -c Release -- --generated-fixtures
dotnet run --project tools/DecompilerHarness -c Release -- --generated-fixtures list
dotnet run --project tools/DecompilerHarness -c Release -- \
  --generated-fixtures minimal.property.literal --json
dotnet run --project tools/DecompilerHarness -c Release -- \
  --generated-fixtures --keep-generated-fixtures

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

# Classify unsupported-node residue by opcode/reason
dotnet run --project tools/DecompilerHarness -c Release -- \
  /path/to/System.Private.CoreLib.dll --unsupported-nodes --max-examples 30

# Why structuring left containers flat (the why-not companion to --gaps)
dotnet run --project tools/DecompilerHarness -c Release -- \
  /path/to/System.Private.CoreLib.dll --structuring-stops --cap 5000

# Prove a printer/pass fix regressed nothing: baseline -> change -> diff.
# Keep --compile-cap identical across both runs (only methods in both are compared).
dotnet run --project tools/DecompilerHarness -c Release -- \
  /path/to/System.Private.CoreLib.dll --validity-check --emit-validity-defects /tmp/defects.txt
# ... make the change, rebuild, then: REGRESSED must be empty for a clean fix
dotnet run --project tools/DecompilerHarness -c Release -- \
  /path/to/System.Private.CoreLib.dll --validity-check --diff-validity-defects /tmp/defects.txt

# Hidden-fact annotation check: precision + recall of the annotations vs raw IL
dotnet run --project tools/DecompilerHarness -c Release -- \
  /path/to/System.Private.CoreLib.dll --annotation-check

# Whole-type source oracle: namespace/kind/modifier/member deltas vs metadata
dotnet run --project tools/DecompilerHarness -c Release -- \
  /path/to/System.Private.CoreLib.dll --type-check --cap 2000 --max-examples 20

# Whole-type binding oracle: new CS0104 ambiguous-reference collisions
dotnet run --project tools/DecompilerHarness -c Release -- \
  /path/to/System.Private.CoreLib.dll --bind-check --max-examples 20
```

Inputs are assembly paths or directories (non-managed files are skipped).

## Baseline

Over .NET 11 preview 5 `System.Private.CoreLib` (~41k methods): the inventory imports at high `Full` fidelity, `--gaps` reads ~96% fully raised — the residual is the structuring completeness docket — and `--annotation-check` reads 100% precision and 100% recall (over ~19.4k graded annotations and ~14.5k recall witnesses).
