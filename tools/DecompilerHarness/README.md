# Decompiler Harness

The diagnostic harness from [docs/decompiler.md](../../docs/decompiler.md) — the asmdiffs analog for the decompiler. It inventories the pipeline's health, scores the real-gap completeness, validates output two ways, and dumps a single method through every pipeline stage. This is the invocation reference for the modes; the strategy they serve — which check proves what, what gates CI, the corpus-sweep plan — is [docs/decompiler-quality.md](../../docs/decompiler-quality.md).

## Output discipline

**stdout = data, stderr = diagnostics.** A sensor's data — reports, cards, and `--json`/`--jsonl`/`--tsv` payloads — goes to stdout; status, progress, gate, and emit-confirmation messages (e.g. "Wrote …: `<path>`") go to stderr. This keeps structured stdout parseable (a `--jsonl` stream stays valid; a teed quality card stays free of stray status lines). Route status through `HarnessLog.Status(...)` rather than `Console.WriteLine` so new sensors follow the convention by default.

## Modes

**Inverse ledger regeneration** (`--emit-inverse-ledger <path>`): evaluates `[InverseOf]` and `[NotInverted]` attributes on the decompiler's node schema and renders the Markdown representation to the specified path. Use this command to update the single-source-of-truth document at `docs/design/inverse-ledger.generated.md` after adding or changing inverse annotations in the IR types. A drift-gate test enforces that the committed file matches this command's output.

**Inventory** (default): sweeps every method body in the given assemblies through the pipeline and reports the fidelity histogram plus stop-reason buckets — the prioritized slice roadmap. Exits nonzero if any importer bug (DEC0001) appears. `--max-examples N` sets how many example methods each bucket lists (default 5) — raise it to widen the candidate pool when picking the next target.

**Gaps** (`--gaps`): the completeness view — see below.

**Assertion scan** (`--assertion-scan`): runs the executable
inverse-architecture `assumes:` predicates across input methods and reports the
measurement view that `--dump --assertions` cannot provide alone: methods with at
least one violation, violation histograms by sink type, first failing pass, node,
and predicate, plus the distinct `[InverseOf]` nodes exercised by the scanned
population. Unexercised nodes are split into importer-emitted nodes (rare opcode
or corpus breadth gap) and pass-raised nodes (raise-pass or fixture gap;
investigate). Use `--sample N` to run a deterministic hash-ranked sample per
assembly, and combine it with `--package` / `--package-version` /
`--package-tfm` / `--package-assembly` the same way other harness scans do.

The scan splits every violation into a **discharged obligation** (flagged at an
intermediate stage, then wrapped by a later pass — e.g. coercion insertion) and a
**final-stage survivor** — an assertion still failing after the last pass, the
corpus-scale analog of `--dump --assertions`' `UNSOUND` marker (see below and
[docs/design/assertion-lane-effects.md](../../docs/design/assertion-lane-effects.md)).
The `final-stage survivors (UNSOUND)` line is the real soundness number:
`first violation sites` counts everything (mostly discharged obligations, which
are the pipeline working as designed), while a non-zero survivor count is the
load-bearing signal. "Final stage is zero survivors" for the wrappable population
is now measured corpus-wide rather than eyeballed from `--dump --assertions`
exemplars (known `PrinterOwned` residuals excluded). `--emit-assertion-violations`
persists the survivor flag (snapshot schema v2), and `--diff-assertion-violations`
reports a dedicated **survivor delta** so a newly surviving assertion is
distinguished from a gained-but-discharged obligation. A pass-bug method (one that
crashed before reaching a final stage) is reported as a third `unknown (pass bug,
no final stage)` state — its violations are neither survivors nor discharged.

Regenerate baselines after upgrading to snapshot schema v2: a v1 baseline records
the survivor flag as false everywhere, so `--diff-assertion-violations` cannot
compare survivor sets against it. The differ skips the survivor-delta section for a
pre-v2 baseline (a v1-baseline survivor delta is a migration artifact, not a
regression), and the regular violation delta is unaffected.

For discharged obligations the scan also reports **obligation lifetime** — the
number of pipeline stages from accrual (first appearance) to discharge (the stage
a later pass cleared it). A short lifetime means a pass decided the type early; a
long one means it retrofitted the claim late, so the `longest-lived (retrofit
hotspots)` list surfaces the obligations a pass could discharge sooner. Lifetime
is a construction-quality trend, not a gate; see
[docs/design/assertion-lane-effects.md](../../docs/design/assertion-lane-effects.md).

The scan is a triage and localized-signoff aid, not a CI gate on a raw violation
count. It automates the value-typed-emission leak-surface census, but the
population-scale correctness gates remain compile-back, render A/B, and the
quality-diff card; see
[docs/decompiler-raise-discipline.md](../../docs/decompiler-raise-discipline.md)
for the assertion-dump discipline.

`--dump --assertions` marks undischarged typing assertions by *where* they are
observed. At a non-final stage it prints `OBLIGATION (informational)` — the
rewrite accrued a typing claim a downstream pass is contracted to discharge (e.g.
coercion insertion wrapping the sink), so a mid-pipeline marker is bookkeeping,
not a defect. When the dump can match the obligation to the scan's observed
lifetime, the marker names the discharging pass, e.g.
`OBLIGATION (informational; discharged by coercion-insertion)`. Only an assertion
that survives to the **final** stage prints `❌ UNSOUND (error)` (the first one
flagged `FIRST UNSOUND SURVIVOR`): nothing downstream remains to discharge it,
so the rendered output leans on an unproven claim. "Final stage is zero UNSOUND"
is the real soundness statement; the intermediate obligations are the pipeline
working as designed.

Add `--assertion-fixture-guarantee` to materialize the generated inverse-node
fixture assembly and include it in annotation coverage. The report prints a
per-node guarantee summary and any node the fixture no longer produces as a
**fixture regression alarm**. That alarm is report-only: it identifies a
raise-pass or fixture regression to investigate, but it does not turn coverage
counts into a CI gate. The daily assertion scan uses this mode so the
unexercised list can reach a true zero when the audit corpus plus deterministic
fixtures cover every `[InverseOf]` node.

For before/after work, mirror the validity-defects loop with
`--emit-assertion-violations <file>` and
`--diff-assertion-violations <file>`. The emitted JSON includes every scanned
method, including clean methods, so a method losing its last violation appears as
an `IMPROVED` row rather than disappearing behind a cap-boundary artifact.

```bash
dotnet run --project tools/DecompilerHarness -c Release -- \
  artifacts/bin/ILInspector.Decompiler/release/ILInspector.Decompiler.dll \
  --assertion-scan --sample 125 --max-examples 5

dotnet run --project tools/DecompilerHarness -c Release -- MyLib.dll \
  --assertion-scan --sample 250 --emit-assertion-violations /tmp/assertions.base.json

dotnet run --project tools/DecompilerHarness -c Release -- MyLib.dll \
  --assertion-scan --sample 250 --diff-assertion-violations /tmp/assertions.base.json

dotnet run --project tools/DecompilerHarness -c Release -- MyLib.dll \
  --assertion-scan --assertion-fixture-guarantee --max-examples 10
```

For annotation-batch audits, use the assertion audit corpus instead of one local
assembly. It combines the fixed real-world decompiler corpus with the current
managed product assemblies, so rare annotated nodes are more likely to be
exercised over real IR while the input set stays reproducible:

```bash
dotnet build src/dotnet-inspect -c Release -p:PublishAot=false
bash eng/prepare-decompiler-assertion-corpus.sh /tmp/assertion-corpus.txt
mapfile -t assemblies < /tmp/assertion-corpus.txt
dotnet run --project tools/DecompilerHarness -c Release -- "${assemblies[@]}" \
  --assertion-scan \
  --assertion-fixture-guarantee \
  --max-examples 10
```

Deep Inspect's census lane runs the same corpus plus the generated fixture
guarantee as a report-only artifact named `assertion-scan-report`. The artifact's
`assertion-scan.txt` lists exercised and unexercised `[InverseOf]` nodes by
cause; a non-empty importer-emitted list is follow-up work for corpus/fixture
breadth, while a non-empty pass-raised list or fixture regression alarm is a
raise-pass or fixture-gap investigation signal, not a failed PR gate.

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
`minimal.conditional-expression-shape-frontier` records a source-shape frontier:
the conditional-expression source is compile-back exact, but the accepted current
output is return-statement shaped rather than `ConditionalExpression` shaped.
`minimal.switch-two-case-lowers-if` records the current SDK's two-case
source-switch lowering observation (lowered as if/else, not the dense switch
shape) and is opt-in by ID because it is a non-exact compiler-lowering frontier.
`assertion.inverse-node-coverage` is the deterministic inverse-ledger coverage
fixture used by `--assertion-fixture-guarantee`; it is addressable by ID but is
not part of the default stable generated-fixture ladder. The `record.*` fixtures
are addressable ReturnToSender catalog coverage for record property accessors,
equality operators, virtual helpers, field-read helpers, record structs, and
generic and nested-generic typed `Equals(T)`; run
`--return-to-sender-catalog record` for that slice. With no selector, stable
generated fixtures run; use `list` to list fixture IDs, `--json` for
machine-readable list/results, and
`--keep-generated-fixtures` to preserve the generated project for drill-down.
The `rts.*` fixtures are focused ReturnToSender parity-burndown probes; for
example `rts.attribute-shell` protects attribute type shells that must preserve a
spellable `System.Attribute` base before attribute usages can compile.
Use `--return-to-sender-fixtures rts.candidates` with `--return-to-sender`,
`--return-to-sender-ab`, `--return-to-sender-source-probe`, or
`--authored-rebuild-fidelity` to add built
fixture assemblies from `FixtureCatalog` as inputs without generating source.
The source probe reuses RTS target discovery/import/render/compile-back plumbing
and, for catalog fixtures, compares the decompiled target body against the
checked-in fixture source slice. It reports coarse source-fidelity outcomes:
`valid_match`, `valid_different`, `invalid`, `source_unavailable`, and
`unsupported_target`. Valid-but-different rows may be split by product-owned
decompiler decision evidence, such as `valid_different.known_taste` for a
documented taste rule versus `valid_different.unclassified` for source deltas
that still need analysis. Roslyn-assisted source comparison stays in the harness;
the product decompiler owns effective options and typed decisions that explain
intentional output choices.
The source probe also supports `--json` for machine-readable census rows. The
`reason` field classifies valid-but-different rows by the source-fidelity
frontier and compile-back status, for example
`valid_different.compiler_lowering.iterator.opcode_diff`,
`valid_different.compiler_lowering.dynamic_callsite.opcode_diff`,
`valid_different.known_compiler_option.checked_context`, or
`valid_different.semantic_opcode_diff.unsafe_residual`.
For rows whose compile-back status is `OpcodeDiff`, text examples and JSON rows
also include the original/recompiled opcode streams plus unified IL diff lines,
so semantic-difference buckets can be triaged without rerunning a member-specific
probe.
Rows may carry two independent expectations: a Roslyn `SyntaxKind` shape verdict
for the intended C# idiom, and a compile-back opcode verdict for semantic
fidelity. A row can therefore be opcode-exact while still exposing a shape
frontier. Shape frontiers record both the accepted current shape and the desired
frontier shape. ReturnToSender catalog rows can also carry body-scoped fragment
expectations; those match only the decompiled target body, not the reconstructed
type shell, so metadata scaffolding cannot satisfy a target-body assertion.
`--source-correspondence-census` is an alias for the source probe when the task
is source-fidelity triage rather than RTS compile-back triage. Its `--json`
payload includes `source_correspondence_findings`: stable Finding-style rows
keyed by member stable selector when available. Each row carries a descriptor ID
such as `source.correspondence.valid_different.known_taste`, a coarse category
(`ignorable`, `not-yet-raised-sugar`, `structuring-residue`,
`semantic-opcode-diff`, `invalid`, or `unclassified`), the source file name, and
whether opcode-diff evidence is attached. The finding projection intentionally
uses source file names rather than absolute source paths so the census can be
shared without leaking local checkout paths.

`--authored-rebuild-fidelity` is the SourceLink-backed second oracle. It
checksum-verifies the authored body, substitutes it into the same final RTS
artifact request, compiles it, and compares emitted IL through product
`ImplementationDiff`. The report keeps authored A→IL beside decompiled B→IL and
reports deterministic-build and portable-PDB option/reference context
separately. `SourceAbsent` is missing evidence; `SourceFailed` is an acquisition
or integrity failure.

The generated fixture ladder is intentionally staged:

| Stage | Harness responsibility |
| --- | --- |
| Specimen | Catalogue row with source, fixture ID, tags, targets, expected shape, and expected compile-back outcome. |
| Materialization | Build the selected source snippets into one temporary class library; `--keep-generated-fixtures` preserves it. |
| Projection | Decompile each target through the shipped raised product path and record the decompiler fidelity grade. |
| Shape verdict | Parse the rendered body with Roslyn and compare the optional expected `SyntaxKind`. |
| Compile-back verdict | Recompile the rendered body and compare canonical opcode streams with `FidelityCheck`. |
| Frontier ledger | Keep non-exact compiler-lowering observations opt-in; keep source-shape frontiers explicit even when they are compile-back exact. |

**Library report** (`--library-report`): a portfolio view. It combines the IR
residual buckets from `--gaps` with the Roslyn-backed validity oracle from
`--validity-check`, then prints a global top-pattern section and one section per
library. Use this for the direct "which patterns are unsupported in which
library?" loop. `--top-patterns N` limits the global/per-library pattern lists,
`--top-libraries N` limits the detailed library sections to the noisiest
libraries, and `--json` emits the same data as structured JSON.

**Real-world corpus sensor** (`--diff-corpus-baseline`): the Deep Inspect
baseline for #1166. It measures the fixed #1150 corpus — pinned NuGet assemblies
plus dotnet-inspect's own assemblies — and compares the run against
`tools/DecompilerHarness/corpus/real-world-baseline.json`. Deep Inspect's census
lane tracks fully-raised rate,
`structuring: conditional-branch`, forward-merge structuring stops
(`cond-target-past-region` + `forward-branch-not-region-exit`), Full malformed
output, semantic validity defects, compile-back fidelity defects, and pass bugs.
The validity and fidelity caps are per assembly so the sensor samples every
corpus member at bounded cost without adding that cost to every PR. When you
want to compare a baseline cap with a larger exploratory cap, repeat
`--corpus-fidelity-cap` (or use a comma-separated list) and the harness prints a
fidelity coverage series with the same per-bucket failure breakdown for each cap. The fidelity sample records useful compile-back outcomes (`Exact`/`OpcodeDiff`) while surfacing recompile- and context-failure buckets for triage. Each Deep Inspect census run
uploads the current JSON snapshot as an artifact so
trends can be compared without scraping logs.

Use `--corpus-fidelity-oracle rts-parity` (`return-to-sender` and `rts` remain
aliases) to run the fidelity sample through RTS instead of the default
compile-back oracle. The transition mode first selects the same bounded target
population as compile-back, including getters, setters, constructors, and
ordinary methods, then records native RTS outcomes under the existing method
identity and fidelity-status contract. Snapshots name this mode `rts-parity`;
diffing snapshots from different modes is rejected rather than presenting
incomparable fidelity movement.

The RTS cap is therefore a parity population: methods the default oracle checked
as `Exact` or `OpcodeDiff`, re-evaluated through RTS. The report classifies each
target as rescued, same, or worse and records the compile-back reference status
beside the native RTS result. Corpus parity deliberately disables RTS's
compile-back floor so compile-back evidence cannot rewrite the RTS verdict.
Because compile-back selects this population before RTS runs, `NotFullMethods`
is structurally zero and failure buckets describe only the selected parity
population; this mode measures parity, not independent RTS coverage.

Standalone `--fidelity-check` reports also print bounded examples for every
non-success bucket: opcode diffs include canonical opcode streams, while
recompile and context failures include the method and diagnostic detail. Use
`--max-examples` to widen the triage sample when exploring a new assembly set;
example headings say whether they show every row or only the first N of the
bucket.

```bash
dotnet build src/dotnet-inspect -c Release -p:PublishAot=false
bash eng/prepare-decompiler-corpus.sh /tmp/corpus-assemblies.txt
mapfile -t assemblies < /tmp/corpus-assemblies.txt
dotnet run --project tools/DecompilerHarness -c Release -- "${assemblies[@]}" \
  --emit-corpus-snapshot /tmp/corpus-snapshot.json \
  --diff-corpus-baseline tools/DecompilerHarness/corpus/real-world-baseline.json \
  --quality-diff-card \
  --compile-cap 4000 \
  --corpus-fidelity-cap 3 \
  --max-examples 3
```

To capture an RTS snapshot without comparing it to the compile-back baseline:

```bash
dotnet run --project tools/DecompilerHarness -c Release -- "${assemblies[@]}" \
  --emit-corpus-snapshot /tmp/rts-corpus-snapshot.json \
  --compile-cap 0 \
  --corpus-fidelity-cap 3 \
  --corpus-fidelity-oracle rts-parity \
  --max-examples 3
```

Add `--quality-diff-card` to emit a Markdown PR block generated directly from
the baseline/current snapshots. The card includes a correctness-coverage line
and per-row sampled denominators for semantic validity and compile-back fidelity
so reviewers can tell when evidence is strong or thin. Semantic validity and
compile-back fidelity samples are hash-stable, so zero-tolerance rows such as
RTS parity compare the same target population when the corpus and caps match.
Paste that block as the PR's aggregate corpus evidence; do not re-key the table
by hand.

**Render A/B** (`--emit-render-ab` / `--render-ab`): the before/after text
oracle for raise and printer changes. The first run writes a method-keyed JSON
baseline of rendered bodies; the second run compares the current render against
that baseline and reports changed, added, and removed methods. Changed methods
are classified on two axes:

- spelling: `structural`, `paren-equivalent`, or `unparsed`;
- semantic validity over the changed set only: `valid->valid`,
  `invalid->valid`, `valid->invalid`, or `invalid->invalid`.

The semantic lane wraps each changed body in the same validity-check method shell
and binds it with the validity diagnostic filters. It catches regressions that
still parse, such as `1++`, without paying a corpus-wide compile cost. A
`valid->invalid` transition is a semantic regression; expression-moving PRs
should report the semantic line explicitly, e.g. `A/B: 55 changed (40
paren-equivalent, 15 structural; semantic: 0 valid->invalid)`.

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
`--quality-card-risky`, and non-card Deep Inspect runs still use the aggregate rates.
Pass-bug crashes always gate on the full aggregate. The pinned gate is computed
from the per-method snapshots both baseline and current carry, so no baseline
regen is required; it falls back to aggregate counts/rates when a snapshot lacks
per-method detail.

Expand the fixed corpus only after that targeting step shows a shape gap. Prefer
deterministic, pinned assemblies that add many examples of the missing lowering
family (for example forward-merge or retained-label control flow), then refresh
the baseline and prove a no-op `--emit-corpus-delta` stays empty. Keep the PR
quick corpus small; broad shape additions belong in the Deep Inspect corpus
unless they are cheap and stable enough for every PR.

**PR quick corpus** (`tools/DecompilerHarness/corpus/pr-quick-baseline.json`):
CI also runs a small artifact-producing corpus sensor after the managed tool
build. It takes a deterministic hash-ranked sample of 100 methods per assembly
across a mixed 15-assembly set: System.Private.CoreLib, the pinned package
libraries used by the Deep Inspect corpus, and dotnet-inspect's managed product
assemblies. The hash-ranked sample avoids the order churn of "first N" metadata
rows while still staying small. The run skips the expensive semantic validity
and compile-back fidelity oracles so it stays small; the Deep Inspect census lane remains
the authoritative full-corpus signal.

Keep the corpus-prep script paired with its matching baseline. The PR quick card
uses `eng/prepare-decompiler-pr-corpus.sh` with
`tools/DecompilerHarness/corpus/pr-quick-baseline.json`; the Deep Inspect
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

**Validity check** (`--validity-check`): the *validity* check — `--gaps` is *completeness*, `--fidelity-check` is *fidelity*, this is *does it even compile*. The pipeline guarantees by construction only that it never crashes and never silently fabricates (unrepresentable IL becomes a visible `/* … */` comment and drops fidelity to `Partial`) — **not** that the rendered text is valid C#. This mode measures the gap: each body is wrapped in a method shell carrying its real signature (return type, generic parameters with their `where` constraints reconstructed from metadata, parameters, so locals/params/type-params and `this` all bind — without the constraints a constrained generic-math call like `byte.TryConvertFromTruncating<TOther>` spuriously fails CS0314), then (1) parsed — a parse error is unambiguously a decompiler defect; (2) checked for statement legality (the CS0201 rule — a bare cast/expression statement parses but isn't valid); (3) bound against the runtime references. Diagnostics are bucketed by code with the member/type-**visibility** codes (the shell can't see the real declaring type's fields/methods) filtered as noise, so genuine defects stand out — `CS0193` (`*`-deref of a managed ref), `CS0175` (`base(...)` rendered as a statement), `CS1620` (an `out` argument not marked `out`), `CS0165` (a local used before the decompiler assigned it). Reported split by fidelity: a `Partial` method is *expected* to carry invalid fragments; a **`Full` method that fails to compile is the real "claimed good but isn't" signal** and the prioritized fix docket. Compiler-generated members are excluded (their metadata names aren't valid identifiers). `--compile-cap N` bounds the (slow) semantic-binding pass; `--compile-cap all` runs an exhaustive binding sweep. Capped reports print how many eligible `Full` methods were actually compiled and label semantic findings as per-sample, not corpus-wide.

Shell-noise classification uses diagnostic IDs, source spans, syntax, and
semantic symbols rather than localized diagnostic prose. If a supported
diagnostic lacks the source evidence needed to classify it, the original error
stays reported and the method also receives `VLD0001`; evidence gaps therefore
remain visible instead of silently joining an ordinary validity bucket.
Diagnostics outside the enumerated shell-noise set, such as `CS0039`, stay
reported as ordinary defects without `VLD0001` until they are explicitly
modeled; surfacing an unknown code is preferred to blanket suppression.
This intentionally stops suppressing method-level `CS0161` merely because its
message names `__Shell.__M`; the current Release decompiler assembly consequently
exposes ten previously hidden missing-return defects, while Debug IL exposes one.
Both configuration-specific populations are pinned by
`ValidityCoverageReportingTests.DecompilerAssembly_MissingReturnPopulationIsPinned`.
The separate type-binding report still extracts a `CS0104` ambiguous simple
name from invariant-culture message text for display; that reporting-only value
does not control filtering or classification.

**Validity predicate scan** (`--validity-predicate-scan`): the cheap exhaustive
coverage lane for known validity-risk classes. It does **not** compile or replace
`--validity-check`; instead it runs the decompiler pipeline over every input
method and counts IR predicates whose defect class is already known. The first
predicates cover the conditional numeric-cast blind spots from #2302/#2306:
conditional arms that need a numeric cast to their merged join type, and
conditionals whose result type needs a numeric cast at a typed sink. Use this as
the "discovered class -> predicate -> corpus-wide census" loop; use
`--validity-check` or `--diff-validity-defects` as the binding oracle for a fix.
Deep Inspect's census lane runs both this scan and an uncapped validity sweep,
while PR quality cards keep capped validity for cost.

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
emits the target's type, compiles, and adds every same-assembly type identified
by structured Roslyn diagnostic evidence (`CS0246`/`CS0234`/`CS0103`/`CS0122`
for types, `CS1061`/`CS0117` for members), recompiling until the unit binds or
the closure stops growing. Membership uses diagnostic IDs, source locations,
syntax, semantic receiver types, and inaccessible candidate symbols rather than
localized message text. The compiler computes the exact closure, so unrelated
sibling types are simply omitted. A `CS0234` identifies a missing namespace
*segment* rather than a leaf type, so the closure reconstructs the full
namespace from the qualified-name syntax and pulls in the roots declared
directly in it — without this, any body that reaches into a sub-namespace
stalls the closure (it was the dominant bail on real libraries: ~81% on
Newtonsoft.Json, driving exact-match from 7.9% to 10.3%).

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
diagnostics. Cluster-bailed result rows also retain a durable `CaptureDetail`;
when a supported diagnostic cannot be mapped from structured evidence, it uses
`closure-stalled-unextracted[CS####,...]` rather than folding the miss into an
ordinary closure stall. Return-to-sender failures use the same reason shape in
their `Detail`, making extractor coverage gaps visible in JSON and summary
output without enabling debug logging.

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

**Slot residual census** (`--slot-residual-census`): the post-F2 measurement
lane for #2386/#2209. It runs each method to the late slots-only
`ExpressionInliningPass` immediately before `SlotMaterializationPass`, captures
`StoreStackSlot`/`LoadStackSlot` counts before and after that pass, and
classifies the remaining stack-slot webs by deferral class (`multi-use`,
`multi-def/merged`, `cross-block`, `effect/order-interleaved`, `nested-scope`,
and store/load-only residuals). This is C2 entry evidence, not a correctness
gate; use `--corpus-method-cap N` for a quick bounded read.

**Slot unifier census** (`--slot-unifier-census`): the C2/#2209 burn-down view
from the printer's own stack-slot unifier path. It runs the full product
pipeline, then asks `CSharpPrinter` to collect its stack-slot naming/type
telemetry without emitting C#. The key lines are `Multi-candidate slots unified
by printer` and `Un-unified split slots`; C2 slices should drive both down until
no `LoadStackSlot`/`StoreStackSlot` reaches the printer.

**Gaps** (`--gaps`): the *self-contained* real-gap view. It inspects only the raised tree: a method is a gap iff it still holds **unstructured control flow** — a `Branch`/`ConditionalBranch`/`SwitchBranch` the structuring passes could not consume, or an EH `Leave` (a surviving `goto`) — or an `UnsupportedNode`. A fully-raised tree holds only structured nodes (`IfStatement`, loops, `Switch`, `TryCatch`), so the residual is exact: reading the tree alone tells you the gap, no recompile or comparison needed. It reports "fully raised" (the metric to drive up) and a residual-kind docket (the prioritized work). It measures completeness, not correctness, so pair it with `--fidelity-check` for fidelity.

*When to use it.* Track the structuring completeness with `--gaps`. Over CoreLib it currently reads ~97% fully raised, the residual dominated by `structuring: conditional-branch` (the forward-branch-to-common-exit work). Add `--by-shape` to sub-classify the `switch-branch` bucket by the structural shape of its residual switch, classifying the imported (pre-pass) tree where the switch is still a block terminator. The buckets, in priority order, are `loop-back-edge` (a section block branches backward — a loop/iterator), `nested-switch` (a case body is itself a switch, or the method has more than one), `default-routes-into-cases` (the default is a case target or branches into one), `external-entry-into-cases` (a block before the switch jumps into a case body), `multi-block-case-section` (a case body carries its own `if`/`?:` — raised by the section-as-region relaxation), and `single-block-clean` (none of the above — a clean switch that nonetheless did not raise, i.e. a pass bug to investigate). A bucket count becomes a per-shape slice docket that scopes the next `SwitchRaisingPass` relaxation.

**Structuring stops** (`--structuring-stops`): the *why-not* companion to `--gaps`. Where `--gaps` reports *that* a method's tree stayed flat (the residual-kind docket), this tallies *why* `StructuringPass` left a container flat — a per-reason histogram of its stop diagnostics across the corpus, each with an example method, plus the `containers structured` vs `left flat across N methods` totals. The dominant reason is the next structuring slice to attack (e.g. the common-exit merge work behind the `conditional-branch` gap). Honors `--cap N` to bound a full-CoreLib sweep, and exits nonzero on any pass bug (a structuring crash). Pair it with `--dump --cfg` on one of its example methods to see the concrete block graph that defeated the pass.

**Annotation check** (`--annotation-check`): the hidden-fact annotation check — the analyzer analog of `--fidelity-check`. Where fidelity check grades the decompiler's *C#* against a recompiled opcode stream, this grades each *annotation* (the allocation/unsafety/lifetime facts from [docs/design/hidden-fact-annotations.md](../../docs/design/hidden-fact-annotations.md)) against the raw IL opcode it claims to describe. The witness is read with the runtime-ported `ILReader` directly over the method's IL bytes — **not** via the IR importer that produced the annotations — so the two paths share only that externally byte-match-validated reader, never the semantic classification logic under test. It measures two directions: **precision** (every annotation's offset carries a consistent opcode — an `alloc.box` sits on a `box`; a violation is an importer-typing or classifier bug) and **recall** (every *unambiguous* witness opcode produced its annotation — a `box`/`newarr`/`localloc`/`calli`, plus every confirmed reference-type `newobj`, always yields its fact). A `newobj`'s constructed type is resolved from metadata (operand token → constructor → declaring-type base chain, or a TypeSpec signature) independently of the importer; the ambiguous remainder stays precision-only and out of the recall gate: a value-type `newobj` (a struct constructor) allocates nothing, a bare cross-assembly `TypeRef` can't be resolved from a single-assembly walk (the documented value-type gap), and a `ldind`/`stind` may be a safe managed-`ref` access. A confirmed value-type `newobj` is additionally held to the *opposite* precision rule — it must **not** carry an allocation fact — catching a false-allocation claim the opcode-precision check is blind to. Recall also excludes partial-import methods, where a stop legitimately leaves later opcodes with no IR node. Exits nonzero on any precision violation (a wrong fact) or import crash.

*When to use it.* Run after any change to allocation/unsafety occurrence production, lifetime classification, or to the importer's typing/metadata layer those producers read (value-type hints, signature decoding). A precision drop on a category points straight at the bug. Over .NET 11 preview CoreLib it currently reads **100% precision** (all descriptors plus the value-type-newobj no-allocation checks) and **100% recall** (the gated witnesses, including ~9k confirmed reference-type newobjs).

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

### Return Address equivalence census (`--return-address`)

`--return-address` runs the Return Address (RA) equivalence census: the
signature-identity sibling of Return-to-Sender. For every method-like member in
the input assemblies it compares the two product member-identity producers,
matched by metadata token:

- **A** = `ApiMemberIdentity.GetMemberAnchor` (the ApiSurface path used by the
  member index / resolver);
- **B** = `ApiMemberIdentity.CreateMethodAnchor` (the SRM-direct path used by
  `CSharpBodyDiff`).

It reports the **agreement rate** (how many members get a byte-identical canonical
signature from both producers) plus capped example divergences. It is a thin
observer: it only compares product-produced canonical strings and embeds no
type/name knowledge, so the divergence *axis* breakdown (keyword vs full-name,
generic arity, byref, nullability) is a separate analysis (see issue #2440). As
the member-identity consolidation lands, the agreement rate should climb toward
100%; the sensor is the leading-signal guard for that work.

```bash
dotnet run --project tools/DecompilerHarness -c Release -- <assemblies> --return-address
```

Output is a Markout card (Markdown by default; `--tsv` and `--jsonl` select the
tabular and JSONL renderings). `--max-examples N` caps the divergence rows. The
summary also reports `unmatched` — method-defs (accessors, compiler-generated,
or API-filtered) that have no ApiSurface member, so they are counted for coverage
but never miscompared. The sweep never crashes on unreadable file inputs — a bad
path is reported as an unopened assembly.

#### Return Address baseline drift gate (Deep Inspect)

The census runs in the Deep Inspect **census lane** as a committed-baseline drift
gate, mirroring the real-world corpus sensor:

```bash
# Refresh the committed baseline (run when the agree rate legitimately improves):
dotnet run --project tools/DecompilerHarness -c Release -- "${assemblies[@]}" \
  --emit-return-address-snapshot tools/DecompilerHarness/corpus/return-address-baseline.json

# Gate against the committed baseline (fails with exit 1 on regression):
dotnet run --project tools/DecompilerHarness -c Release -- "${assemblies[@]}" \
  --diff-return-address-baseline tools/DecompilerHarness/corpus/return-address-baseline.json \
  --max-examples 10
```

The baseline is the pinned real-world corpus (`eng/prepare-decompiler-corpus.sh`),
the same corpus used by the real-world corpus sensor. The gate compares only the
**global agree rate** (stored in basis points): it fails if the rate drops below
the baseline beyond the tolerance embedded in the snapshot
(`agreeRateDropBasisPoints`, default 50 bps). This makes the baseline a **floor**
that ratchets toward 100% as identity consolidation (#2440) lands — improvements
never fail; re-emit the baseline to raise the floor. `matched`/`unmatched` counts
drift with corpus composition (framework/NuGet version bumps), so they are
reported in the card for triage but not gated.

### Not My Type equivalence census (`--not-my-type`)

`--not-my-type` runs the Not My Type (NMT) type-shape equivalence census. It
compares the product type-shape oracle (`MetadataSource.ClassifyType`) with the
legacy base-name classifier that the harness sites used to re-derive locally.
It reports two axes:

- **same-assembly agreement** over type definitions, where the oracle and legacy
  classifier read the same local metadata and therefore must agree; and
- **cross-assembly reference recovery**, where the corpus-aware product oracle
  can resolve shapes that a single-assembly legacy walk leaves `Unknown`.

```bash
dotnet run --project tools/DecompilerHarness -c Release -- <assemblies> --not-my-type
```

Output is a Markout card (Markdown by default; `--tsv` and `--jsonl` select the
tabular and JSONL renderings). `--max-examples N` caps same-assembly divergence
rows. The recovery rate is informational; it quantifies the cross-assembly gap
the product oracle closes.

#### Not My Type baseline drift gate (Deep Inspect)

The census runs in the Deep Inspect **census lane** as a committed-baseline drift
gate, matching the Return Address pattern:

```bash
# Refresh the committed baseline (run when the agree rate legitimately improves):
dotnet run --project tools/DecompilerHarness -c Release -- "${assemblies[@]}" \
  --emit-not-my-type-snapshot tools/DecompilerHarness/corpus/not-my-type-baseline.json

# Gate against the committed baseline (fails with exit 1 on regression):
dotnet run --project tools/DecompilerHarness -c Release -- "${assemblies[@]}" \
  --diff-not-my-type-baseline tools/DecompilerHarness/corpus/not-my-type-baseline.json \
  --max-examples 10
```

The baseline is the pinned real-world corpus (`eng/prepare-decompiler-corpus.sh`).
The gate compares only the **global same-assembly agree rate** (stored in basis
points). This floor is intentionally hard (`agreeRateDropBasisPoints` defaults to
0) because same-assembly agreement should be 100%; a drop is a real oracle or
legacy-classifier divergence, not corpus drift. Re-emit the baseline only when
the floor legitimately improves or the pinned corpus changes deliberately.

### Malformed-metadata signature fuzzer (`--fuzz-signatures`)

`--fuzz-signatures` runs a self-contained fuzzer (#2499) over the signature-decode
hardening. It generates adversarial signature blobs — deep wrapper chains, wide and
huge declared counts, adversarial array shapes, function pointers, truncation, and
random bytes — across all five `SignatureBlobGuard.Kind` shapes, and for each blob:

1. runs `SignatureBlobGuard.IsSafeToDecode`, then
2. when the guard reports the blob safe, runs a **real** SRM signature decode.

The guard's contract is that a *safe* verdict implies SRM can decode the blob without
an uncatchable `StackOverflowException` or an unbounded pre-allocation. A blob that
violates that contract aborts the process — which is the fuzzing signal — so
**finishing all iterations is the pass**. The decode uses a trivial provider because
the StackOverflow (native `DecodeType` recursion) and the count-driven pre-allocation
both live inside SRM's decoder, independent of the provider.

```bash
# Deterministic run; a crash reproduces with the same --fuzz-seed.
dotnet run --project tools/DecompilerHarness -c Release -- \
  --fuzz-signatures --fuzz-iterations 1000000 --fuzz-seed 1 --fuzz-log-every 50000

# Positive control: skip the guard so a deep/huge blob decodes directly. Expected to
# abort (StackOverflow/OOM) — this demonstrates the guard is necessary and the fuzzer
# generates genuinely crashing inputs.
dotnet run --project tools/DecompilerHarness -c Release -- --fuzz-unguarded
```

Each blob is logged to stderr (kind, iteration, hex) every `--fuzz-log-every` iterations
so a process abort leaves the culprit as the last line; the run is deterministic given
`--fuzz-seed`, so a crash is reproduced by re-running the same seed. A bounded run
(1,000,000 iterations) runs in the opt-in Deep Inspect **census lane**.

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
