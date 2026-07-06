---
name: dotnet-inspect-deep-inspect
version: 0.1.0
description: Run and interpret opt-in expensive validation lanes: full slow tests, IL round-trip sweep, corpus sensors, validity scans, and analysis census.
---

# dotnet-inspect: Deep Inspect

Use this skill when a change needs expensive evidence outside normal PR CI.
Deep Inspect is opt-in: run it manually when preparing risky PRs, and the test
lane also runs during publish before packages are built.

## Lanes

| Lane | Use for | Runs |
| ---- | ------- | ---- |
| `test` | Blocking proof before publish or risky merges | Full decompiler tests, full analysis tests, vendored ILAssembler restore, full IL round-trip sweep. |
| `census` | Observational broad signal and triage | Real-world corpus sensor, validity predicate scan, uncapped validity sweep, assertion scan, analysis corpus sensor, paydirt recall. |
| `all` | Release-candidate deep read | Both lanes. |

Run manually:

```bash
gh workflow run deep-inspect.yml -f lane=test
gh workflow run deep-inspect.yml -f lane=census
gh workflow run deep-inspect.yml -f lane=all
```

Inspect recent runs and artifacts:

```bash
gh run list --workflow deep-inspect.yml --limit 10
gh run view <run-id> --log-failed
gh run download <run-id> -D /tmp/deep-inspect-<run-id>
```

## Local equivalents

For the test lane:

```bash
dotnet run --project src/ILInspector.Decompiler.Tests -c Release
dotnet run --project src/ILInspector.Analysis.Tests -c Release
bash eng/restore-ilassembler.sh
dotnet run --project tests/DotnetInspector.ILRoundtrip.Tests -c Release
```

For the fast PR subsets:

```bash
dotnet run --project src/ILInspector.Decompiler.Tests -c Release -- -trait- "Speed=Slow"
dotnet run --project src/ILInspector.Analysis.Tests -c Release -- -trait- "Speed=Slow"
dotnet run --project tests/DotnetInspector.ILRoundtrip.Tests -c Release -- -trait- "Speed=Slow"
```

For the census lane, prefer the workflow so artifacts are retained. If running
locally, use the same scripts/baselines as `deep-inspect.yml` and preserve the
generated snapshots/cards under `/tmp` or `artifacts/` for review.

## Reading results

- Treat `test` failures as blockers: reproduce locally, identify the first
  failing proof, and fix or explicitly defer before publish.
- Treat `census` output as triage signal unless a command exits nonzero by
  design. Compare snapshots against committed baselines and route meaningful
  drift to issues or follow-up PRs.
- Do not add broad/corpus-style tests to PR CI. Mark them
  `[Trait("Speed", "Slow")]` and keep them in Deep Inspect / publish / full
  local runs.
