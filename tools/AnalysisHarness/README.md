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
the sweep. The sampled-judgment precision/recall loop (Layer 3) is tracked on #1818.
