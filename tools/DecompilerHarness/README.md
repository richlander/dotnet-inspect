# Decompiler Harness

The differential harness from [docs/decompiler-pipeline.md](../../docs/decompiler-pipeline.md) — the asmdiffs analog for the pipeline replacement. The parity gate between the current decompiler and its greenfield successor is measured here.

## Modes

**Inventory** (default, until the `next` pipeline exists): sweeps every method body in the given assemblies through one pipeline and reports render rate plus exceptions bucketed by type and topmost decompiler frame, so one bug is one bucket.

**Diff** (`--candidate <name>`): runs two pipelines over every method and reports agreement, categorized first-line diffs, and one-sided failures.

## Usage

```bash
# CoreLib of the running runtime (default input)
dotnet run --project tools/DecompilerHarness -c Release

# Whole shared framework, with reports
dotnet run --project tools/DecompilerHarness -c Release -- \
  /path/to/shared/Microsoft.NETCore.App/11.0.0 \
  --report harness.md --json harness.json

# Diff mode, once the replacement pipeline registers under "next"
dotnet run --project tools/DecompilerHarness -c Release -- --candidate next
```

Inputs are assembly paths or directories (non-managed files are skipped). Methods slower than 2s are listed in the report; true hangs stall the sweep visibly rather than being misreported by a nested-task timeout (CI applies job-level timeouts).

## Baseline

First full sweep (June 2026, .NET 11 preview 5 shared framework): 133,461/133,461 method bodies rendered, zero exceptions, ~6s wall clock.
