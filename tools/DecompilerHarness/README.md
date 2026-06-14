# Decompiler Harness

The differential harness from [docs/decompiler-pipeline.md](../../docs/decompiler-pipeline.md) — the asmdiffs analog for the pipeline replacement. The parity gate between the current decompiler and its greenfield successor is measured here.

## Modes

**Inventory** (default): sweeps every method body in the given assemblies through the current pipeline and reports render rate plus exceptions bucketed by type and topmost decompiler frame, so one bug is one bucket.

**Replacement-pipeline inventory** (`--pipeline next`): sweeps through the new importer and reports the fidelity histogram plus stop-reason buckets — the prioritized slice roadmap. Exits nonzero if any importer bug (DEC0001) appears.

**Diff** (`--candidate <name>`): runs two pipelines over every method and reports agreement, categorized first-line diffs, and one-sided failures. `--candidate next` is the parity scoreboard: every comparable method through both pipelines as trimmed C# text, with difference buckets that double as the raising-pass docket.

The raw agreement percentage conflates three unlike things, so the scoreboard also splits the disagreements by signal — because "agrees with the frozen emitter" is not the same as "good," and the gate is exact-or-*better*:

- **candidate-worse** — the new pipeline left a `goto`, an unraised `Monitor.Enter`, or an unrepresentable `/* … */` where the baseline is cleaner. These plus the **Partial** count are the real burndown.
- **baseline-worse** — the old emitter emitted an IL artifact (`Type::member`, `S_in_` leak) the new pipeline already renders correctly. The candidate wins; the gate does not require matching these.
- **likely cosmetic** — equal once `Namespace.` qualifier runs are stripped symmetrically (the baseline's semi-qualified names vs the new pipeline's simple ones). Not a gap.
- **uncertain** — both clean but genuinely different; only the source corpus can grade these.

The classifier is deliberately conservative: structural tells win first, so a real gap is never hidden behind a cosmetic verdict. The printed `REAL-GAP burndown` is `Partial + candidate-worse` (known) up to `+ uncertain` (worst case) — the lower bound is what to drive to zero before retiring the old emitter.

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

# Parity scoreboard: every comparable method through both pipelines
dotnet run --project tools/DecompilerHarness -c Release -- --candidate next

# Stage-by-stage dump of one method (metadata type name)
dotnet run --project tools/DecompilerHarness -c Release -- \
  --dump 'System.Collections.Generic.Stack`1::Push'
```

Inputs are assembly paths or directories (non-managed files are skipped). Methods slower than 2s are listed in the report; true hangs stall the sweep visibly rather than being misreported by a nested-task timeout (CI applies job-level timeouts).

## Baseline

First full sweep (June 2026, .NET 11 preview 5 shared framework): 133,461/133,461 method bodies rendered, zero exceptions, ~6s wall clock.
