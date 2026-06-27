# Burndown Roles

Burndown work is coordinated through a small set of agent roles. The operational
source of truth is the compact rollup issue #1568; role details live under
`agents/`.

| Role | Agent file | Purpose |
| --- | --- | --- |
| Burndown Curator | [`agents/burndown-curator.md`](../agents/burndown-curator.md) | Owns #1568, PR SLA hygiene, row reconciliation, orphan clustering, and queue compression. |
| Burndown Runner | [`agents/burndown-runner.md`](../agents/burndown-runner.md) | Claims one row from a burndown list and drives it to a PR, blocker, or pivot issue. |
| Ladder Tester | [`agents/ladder-tester.md`](../agents/ladder-tester.md) | Measures product quality ladder rungs and files focused implementation rows or rung burndowns. |
| Burndown Discovery | [`agents/burndown-discovery.md`](../agents/burndown-discovery.md) | Finds high-confidence defects and turns them into runner-ready issues or themed burndowns. |

## Current workflow anchors

- Rollup/dashboard: #1568.
- Decompiler product ladder: #1599.
- Analysis product ladder: #1623.
- Decompiler proof/gauntlet strategy: #1584.

## Product-specific evidence

Generic role rules apply across product areas. Product-specific evidence and
philosophy still come from the row's burndown and the relevant docs.

### Decompiler rows

Decompiler rows must preserve the product path constraints: SRM-only,
NativeAOT-friendly, Roslyn-free, no inspected-assembly loading, and honest
degradation instead of plausible wrong C#. Use
[`decompiler-correctness-pipeline.md`](decompiler-correctness-pipeline.md) to
choose the right proof boss.

Common evidence includes focused `ILInspector.Decompiler.Tests`, dump/stepper
output, fidelity/validity diffs, pass-impact, quality cards, and cross-model
adversarial review.

### Analysis rows

Analysis rows must preserve `ILInspector.Analysis` as a standalone SRM-direct
product with no inspected-assembly loading. Its type/identity model is separate
from the decompiler's model by design.

Default validation is
`dotnet run --project src/ILInspector.Analysis.Tests -c Release`. When rendering
changes, include a compact before/after from the affected surface: `Top Leverage`,
`Performance Triage`, `Call Graph`, `Caller Graph`, or `Analysis Diff`.
