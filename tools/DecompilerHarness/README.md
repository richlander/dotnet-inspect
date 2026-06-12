# Decompiler Harness

The differential harness from [docs/decompiler-pipeline.md](../../docs/decompiler-pipeline.md) — the asmdiffs analog for the pipeline replacement. The parity gate between the current decompiler and its greenfield successor is measured here.

## Modes

**Inventory** (default, until the `next` pipeline exists): sweeps every method body in the given assemblies through one pipeline and reports render rate plus exceptions bucketed by type and topmost decompiler frame, so one bug is one bucket.

**Diff** (`--candidate <name>`): runs two pipelines over every method and reports agreement, categorized first-line diffs, and one-sided failures.

**Stage dump** (`--dump 'Type::Method'`): JitDump for the decompiler — prints every stage of one method's analysis as projections of the shared `MethodAnalysis`: raw IL, typed IL (per-instruction stack states), structured IL (blocks and exception regions), then C#. The IL projections come from pre-transform stages, so the output is exactly what each pipeline layer saw.

## Usage

```bash
# CoreLib of the running runtime (default input)
dotnet run --project tools/DecompilerHarness -c Release

# Whole shared framework, with reports
dotnet run --project tools/DecompilerHarness -c Release -- \
  /path/to/shared/Microsoft.NETCore.App/11.0.0 \
  --report harness.md --json harness.json

# Replacement-pipeline inventory: fidelity histogram + stop-reason roadmap
dotnet run --project tools/DecompilerHarness -c Release -- --pipeline next

# Replacement-pipeline IR dump for one method
dotnet run --project tools/DecompilerHarness -c Release -- \
  --pipeline next --dump 'System.String::IsNullOrEmpty'

# Diff mode (--candidate next) activates when the replacement pipeline
# grows its C# printer

# Stage-by-stage dump of one method (metadata type name)
dotnet run --project tools/DecompilerHarness -c Release -- \
  --dump 'System.Collections.Generic.Stack`1::Push'
```

Inputs are assembly paths or directories (non-managed files are skipped). Methods slower than 2s are listed in the report; true hangs stall the sweep visibly rather than being misreported by a nested-task timeout (CI applies job-level timeouts).

## Baseline

First full sweep (June 2026, .NET 11 preview 5 shared framework): 133,461/133,461 method bodies rendered, zero exceptions, ~6s wall clock.
