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
across catalogue entries.

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
recall gate. The remaining non-mechanical #1818 work is the precision-labeling convention: who
labels `--precision-sample` worksheets, where labels live, and what false-positive-rate bar turns
the worksheet into a maintained quality signal.

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
