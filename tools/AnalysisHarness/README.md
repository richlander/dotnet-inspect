# Analysis Harness

The fixture-catalogue side of the analysis harness (#1818, Layer 1) — the analysis analog of
the decompiler generated-fixture catalogue (#1742). It makes the `ILInspector.Analysis` rung
fixtures **addressable** and **generated**, and grades each target method's signals against an
**expected-signal ledger** using the real analyzer.

Unlike the decompiler harness there is no mechanical compile-back oracle: analysis output is
triage candidates a human/agent judges. The mechanical part the catalogue *can* check is "does
the analyzer emit the declared signals?", so each entry declares its expected `MethodSignals` /
`OptimizationOpportunity` outcome. The interesting entries are **owned boundaries** —
deliberately-accepted false positives/negatives at the SRM-only, no-referenced-assembly-loading
edge — recorded so a future improvement flips them on purpose rather than as a silent diff.

## Run

```bash
dotnet build tools/AnalysisHarness -c Release
DLL=tools/AnalysisHarness/bin/Release/net11.0/analysis-harness.dll

dotnet "$DLL" --generated-fixtures list          # list entries + expected outcomes
dotnet "$DLL" --generated-fixtures               # build and grade every fixture
dotnet "$DLL" --generated-fixtures alloc --json  # grade a subset (id/prefix/tag), JSON output
dotnet "$DLL" --generated-fixtures exception.unsuffixed.external --keep  # keep temp projects
```

Each fixture builds in isolation: a consumer assembly (the inspected one) plus, when the
fixture is cross-assembly, a referenced external assembly (with an extern alias for the
name-collision case). Isolation keeps same-fully-qualified-name and alias fixtures from clashing
across catalogue entries. Fixtures may opt into framework references when the signal depends on a
trusted platform identity, such as the real ASP.NET Core `RenderTreeBuilder`.

## Deferred catalogue seeds

The catalogue should pin only shapes with a clear discriminator. Deferred #1871 seeds stay out of
the generated ledger until their analyzer shape exists:

| Seed | Blocker |
| --- | --- |
| State-machine allocation | The useful form is cross-method: an iterator/async state machine allocated and consumed in an outer loop. The naive per-method constructor shape floods every iterator and is intentionally not pinned (#1805). |
| LINQ materialize/dataflow shapes | Need the #1807 dataflow/escape discriminator so transient, streamable object graphs can be separated from necessary model construction. |

## Test

`ILInspector.Analysis.Tests/AnalysisFixtureCatalogTests` runs the catalogue and asserts each
target. It is `[Trait("Speed", "Slow")]` (every fixture runs `dotnet build`), so it is excluded
from the fast PR lane — like the decompiler generated-fixture catalogue. Run it directly:

```bash
dotnet artifacts/bin/ILInspector.Analysis.Tests/release/ILInspector.Analysis.Tests.dll \
  -method "*SeedCatalogue*"
```

## Layers above this

This is Layer 1 of #1818. The corpus stability sensor (Layer 2) is `--corpus-list`:

```bash
dotnet "$DLL" --corpus-list assemblies.txt                                    # stability card
dotnet "$DLL" --corpus-list assemblies.txt --diff-corpus-baseline corpus/analysis-baseline.json
```

It sweeps the analyzer over a pinned corpus (one assembly path per line) and reports the
mechanically-checkable signals — did every assembly open, did the analyzer choke (recoverable
diagnostics) — diffed against a committed baseline. A REGRESSION (an assembly that stops opening,
times out, or whose diagnostics increase) exits nonzero; signal-count DRIFT is reported, not
failed. Each assembly is bounded by a per-assembly timeout so one pathological input cannot hang
the sweep.

Allocation metadata readout is a measurement-only companion for deciding whether
path/escape evidence is strong enough to inform future confidence or ranking work:

```bash
bash eng/prepare-decompiler-corpus.sh /tmp/corpus-assemblies.txt
dotnet run --project tools/AnalysisHarness -c Release -- \
  --allocation-readout /tmp/corpus-assemblies.txt --top 12
dotnet run --project tools/AnalysisHarness -c Release -- \
  --allocation-readout /tmp/corpus-assemblies.txt --json
```

The readout aggregates occurrence buckets (`kind`, `allocation`, `path`,
`path-confidence`, `post-dominance`, `escape`) and opportunity buckets (`shape`,
`allocation`, `path`, `path-confidence`, `post-dominance`, `confidence`), plus
cross-tabs. Text output caps each bucket with `--top`; JSON keeps all buckets.
Use it before changing confidence/ranking so the proposal names its measured
population.

A 2026-07-01 fixed-corpus run (`14/14` assemblies opened) showed 41,890
allocation occurrences and 6,587 optimization opportunities. `return-post-dominates`
was common (29,175 occurrences; 4,817 opportunities), but not selective by
itself: it appears heavily in `small-array`, `box-value-type`, and delegate
rows. `local-only` escape evidence was sparse (133 occurrences, ~0.3%), so
`LocalOnly + post-dominance` is not yet broad enough to justify global confidence
or ranking changes. Treat this as a query/example signal first; behavior changes
should remain shape-specific and cite the measured bucket they target.

Layer 3 (sampled precision/recall) splits by oracle: recall is mechanical once curated, precision
needs judgement.

```bash
dotnet "$DLL" --paydirt-recall ILInspector.Decompiler.dll       # committed reference sites must surface
dotnet "$DLL" --precision-sample ILInspector.Decompiler.dll --top 20   # worksheet for TP/FP labeling
```

`--paydirt-recall` exits nonzero if a committed reference paydirt site stops surfacing as a
loop+high triage candidate (a recall regression). `--precision-sample` emits the top-N ranked
candidates as a labeling worksheet — there is no automatic precision oracle, so an agent/human
labels true vs false positive.

The daily workflow runs the generated-fixture catalogue, corpus stability sensor, and paydirt
recall gate. Precision labeling, baseline refreshes, and recall-reference edits remain
maintainer-owned upkeep: they are documented conventions, not automatic CI gates.

## Leak triage corpus sensor (#1992)

`--leak-triage` sweeps the fail-closed ArrayPool leak-triage analyzer
(`LeakTriageAnalyzer`) over a corpus and reports where it fires, as a
[Markout](https://github.com/richlander/markout) card:

```bash
dotnet "$DLL" --leak-triage assemblies.txt --top 5          # Markdown card (default)
dotnet "$DLL" --leak-triage assemblies.txt --tsv            # section-tagged TSV
dotnet "$DLL" --leak-triage assemblies.txt --jsonl          # one heterogeneous JSON record per row
```

The card has three sections — a **Summary** (assemblies / opened / failed / timed out / total
findings), a **By shape** histogram (`arraypool-rent-not-returned`, `arraypool-use-after-return`,
`arraypool-double-return`), and **Findings** (assembly / shape / method, `--top` bounding examples
per assembly). One declarative Markout model renders the dense Markdown table and decomposes into
section-tagged TSV/JSONL. It is a single-run census with no baseline, so it uses plain sectioned
rows, not composite/delta cells; a `--diff-baseline` mode against a committed snapshot is the
natural home for those (`Change`/`[MarkoutDelta]`). Each assembly is bounded by a per-assembly
timeout, and any per-assembly input failure (a directory path, a truncated PE) becomes an
`Opened=false` row rather than crashing the sweep.

This is the evidence engine that must earn any user-facing `Leak Triage` section: the analyzer
fails closed on incomplete CFG/RD, non-`Shared` pools, aliases, field stores, cross-method
ownership, and ambiguous uses, so an **empty card on real code means recall — not a product
section — is the next lever**. A 2026-07-05 run over CoreLib, `Microsoft.CodeAnalysis`, and
`Microsoft.CodeAnalysis.CSharp` produced **0 findings** (all gates suppressed), while the fixture
assembly's three known-misuse methods surfaced exactly once each. Wire the section only once this
card shows non-zero, high-precision rows on real assemblies.

## Resource lifecycle census (#2439 Slice 1)

`--resource-lifecycle` sweeps the **measurement-only** ArrayPool resource-lifecycle census
(`ResourceLifecycleCensus`) over a corpus and reports the candidate/suppression bucket census,
as a [Markout](https://github.com/richlander/markout) card:

```bash
dotnet "$DLL" --resource-lifecycle assemblies.txt --top 5     # Markdown card (default)
dotnet "$DLL" --resource-lifecycle assemblies.txt --tsv       # section-tagged TSV
dotnet "$DLL" --resource-lifecycle assemblies.txt --jsonl     # one JSON record per row
```

Unlike `--leak-triage`, this changes **no** user-facing finding: it consumes the same CFG +
def-use substrate but, instead of accusing, partitions every recognized
`ArrayPool<T>.Shared.Rent` acquire into typed buckets so their size and shape can be measured
before any bucket graduates to a finding (Slice 4). The card has three sections — a **Summary**
(assemblies / opened / failed / timed out / acquires observed / candidate / suppressed / total
facts), a **By bucket** histogram, and **Examples** (bucket / assembly / method, `--top` bounding
examples per bucket).

Buckets: candidate buckets `normal-path-leak-candidate`, `exception-path-leak-candidate`,
`use-after-return-candidate`, `double-return-candidate` (a clean acquire may satisfy several at
once — candidate reachability is deliberately raw, so a correlated-branch multi-return shows up as
both a normal-path leak and a double return); suppression buckets `ownership-transfer-suppressed`,
`alias-or-field-suppressed`, `cross-method-suppressed`, `incomplete-cfg-or-rd-suppressed` (each
terminal — a suppressed acquire never counts toward a leak). The normal-path vs exception-path
split matters because exception-path candidates are only clearly actionable when the exception is
commonly caught one layer higher; measuring them separately is the point of the slice.

## Allocation convergence parity

The Rung 4 allocation-convergence build must prove that a candidate
occurrence-derived projection produces the same decompiler allocation annotations
as the legacy decompiler allocation annotation producer. Use the parity gate with two
`AnnotationStructuredView.Json(...)` outputs: the legacy allocation annotations
and the candidate projection serialized in the same structured shape.

```bash
dotnet "$DLL" --allocation-parity legacy-annotations.json candidate-annotations.json
```

The comparison ignores non-allocation annotations and is exact for allocation
annotation `id`, IL `offset`, `conditionality`, and `detail`. Duplicate rows are
counted, so Track A can use this as a mechanical exit gate for identical
projection rather than manually inspecting mixed-source output.

## Precision labeling convention

`--precision-sample` is an agent/human worksheet, not a gate. The analyzer has no automatic
precision oracle, so labels must be reviewable evidence rather than an implied pass/fail.

Use this convention for precision pilots and recurring reviews:

- **Owner:** one agent prepares labels from the worksheet; a maintainer or second reviewer resolves
  disputed rows before the result is treated as a quality signal.
- **Storage:** post the labeled worksheet as an issue or PR comment on the tracker that requested
  the sample. Do not commit labels unless they are promoted into curated recall references.
- **Cadence:** run on demand for behavior changes, ranking changes, new shape families, or a
  suspected false-positive cluster. Do not add it to PR CI or daily automation.
- **Sample size:** start with `--top 20` for a normal review. Use a smaller sample only for a pilot
  that validates the convention itself; state the sample size in the comment.
- **Bar:** a sample is healthy when at least 80% of labeled rows are true positives and no
  high-confidence false-positive cluster dominates the top rows. Below that bar, open or link a
  follow-up issue for each cluster before treating the signal as healthy.
- **Stale labels:** labels are tied to the assembly, command, commit, and tool version. Re-label
  after ranking, confidence, or shape changes; do not compare old labels to new output without
  re-running the worksheet.

Required fields per row:

| Field | Meaning |
| --- | --- |
| `Candidate` | Copy the worksheet row identity: type, method, signature, shape, confidence, loop, root reach. |
| `Label` | `TP`, `FP`, or `Unknown`. `Unknown` is allowed when the row needs runtime/profile evidence. |
| `Reason` | One sentence explaining why the row is real paydirt, a false positive, or undecidable. |
| `Evidence` | The command/output inspected, usually `dotnet-inspect member ... -S "Annotated Source,IL"` or equivalent. |
| `Action` | `none`, `open issue`, `promote to recall reference`, or `needs maintainer decision`. |

Comment template:

```text
### Analysis precision sample — <assembly>

Command: dotnet run --project tools/AnalysisHarness -c Release -- --precision-sample <assembly> --top <N>
Commit/tool: <git sha>
Summary: <TP>/<labeled> TP, <FP>/<labeled> FP, <Unknown> unknown. Healthy: yes/no.

| Candidate | Label | Reason | Evidence | Action |
| --- | --- | --- | --- | --- |
| <type>::<method>(<signature>) [shape, confidence, loop, root reach] | TP/FP/Unknown | <why> | <command/output> | <next> |
```

## Corpus baseline and recall-reference upkeep

The committed corpus files are review artifacts:

| File | Purpose |
| --- | --- |
| `corpus/analysis-baseline.json` | The last accepted corpus stability snapshot for the pinned corpus. |
| `corpus/paydirt-reference.json` | Curated recall anchors that must keep surfacing as loop+high candidates. |

Refresh `analysis-baseline.json` only when the corpus changed, the analyzer intentionally changed
signal counts, or a previous timeout/failure is now measured. Do not rebaseline a regression away:
first explain why each change is drift, an improvement, or an environment/corpus update.

Use this command shape for baseline updates:

```bash
bash eng/prepare-decompiler-corpus.sh /tmp/corpus-assemblies.txt
dotnet run --project tools/AnalysisHarness -c Release -- \
  --corpus-list /tmp/corpus-assemblies.txt \
  --diff-corpus-baseline tools/AnalysisHarness/corpus/analysis-baseline.json \
  --emit-corpus-snapshot /tmp/analysis-baseline.json
```

Then copy `/tmp/analysis-baseline.json` to `tools/AnalysisHarness/corpus/analysis-baseline.json`
only after reviewing the diff card. The PR or issue comment must include the command, the diff-card
summary, and the reason each movement is acceptable.

Edit `paydirt-reference.json` only after a labeled precision worksheet or targeted investigation
identifies a stable, high-value recall anchor. A reference should have:

- qualified assembly, type, method, signature, and shape identity;
- stable loop+high ranking on the current analyzer;
- evidence that it is real paydirt, not just a convenient row; and
- enough reach or representative value to make it worth guarding.

Remove or update a reference only when the underlying code moved, the shape was intentionally
renamed, or the candidate stopped being real paydirt. Missing references are recall regressions
until proven stale. Validate reference edits with:

```bash
dotnet run --project tools/AnalysisHarness -c Release -- \
  --paydirt-recall artifacts/bin/ILInspector.Decompiler/release/ILInspector.Decompiler.dll \
  --reference tools/AnalysisHarness/corpus/paydirt-reference.json
```

Daily failure triage:

| Symptom | First classification | Expected action |
| --- | --- | --- |
| Assembly stops opening or times out | Analyzer or environment regression | Reproduce locally; fix analyzer/runtime issue before rebaselining. |
| Analyzer diagnostics increase | Analyzer regression | Find the failing method family and fix or file a focused issue. |
| Signal counts move with no regressions | Drift | Review the card; rebaseline only with an explanation. |
| Paydirt recall misses a site | Recall regression or stale reference | Re-run precision evidence; fix the analyzer or update the reference with rationale. |
| Corpus assembly added/removed | Corpus update | Refresh the baseline after confirming the pinned corpus change is intentional. |
