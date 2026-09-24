# ArrayPool ownership retirement report

**Verdict:** the generic lifecycle and Research ownership paths are ready to replace the ArrayPool-specific implementations for the measured scope.

## Baselines

- Fully legacy product: `0.25.0+473d56a` (`473d56a68e26338fc27aca9808c1e98dbf30b259`).
- Shipped continuity product: `0.26.0+d236a7a` (`d236a7ad79bf4d76c63f0530cedfab0ede3cf3dd`).
- Generic comparison head: `a00d14fbbbe2bd7727bdab070169cffa612a2209`.
- The in-tree `LeakTriage`, `ArrayPoolOwnershipFlow`, and `ArrayPoolOwnershipPathFindings` oracle files are byte-for-byte unchanged from the v0.26.0 source tag; the producer refuses to run if that condition is false.

## Method

- Public product continuity compares sorted `Resource Triage` JSONL from v0.25.0, v0.26.0, and the current CLI.
- Lifecycle compares typed whole-library result states first, then complete `ResourceTriageAssessment` values when both engines complete.
- Research compares acquisition roots, physical forwarding coordinates, terminal outcome and sink identity, operation limits, Finding identity, and path-local completeness over one shared call-graph projection.
- The JSONL ledger beside this report is the complete machine-readable classification. No display text is used as a typed comparison authority.

## Observed differences

- Lifecycle preserves every comparable legacy assessment and adds 62 owner-specified positive assessments. Finding ordinals are excluded because they are stream metadata; identity, payload, coordinates, boundaries, policy fields, descriptor, and detail remain exact.
- Research adds 126 focus roots, 142 acquisition coordinates, and 29 typed terminal paths that the narrower legacy producer omitted.
- Research suppresses 2 legacy release paths after the same obligation already reached a proven field-store terminal; the generic contract does not traverse the stored alias.
- v0.25.0 to v0.26.0 records 9 already-shipped public JSONL, diagnostic, or exit-status changes and 3 corresponding typed fail-closed transitions. Every v0.26.0-to-current comparison is exact.

## Population

| Input | Lifecycle findings | Research roots | Research paths | Intentional improvements | Defects |
| --- | ---: | ---: | ---: | ---: | ---: |
| `fixture:arraypool-lookalikes` | 0 | 1 | 1 | 2 | 0 |
| `fixture:ownership-flow` | 25 | 46 | 40 | 85 | 0 |
| `nuget:MessagePack@2.5.192` | 4 | 9 | 8 | 30 | 0 |
| `nuget:MimeKit@4.8.0` | 4 | 19 | 11 | 58 | 0 |
| `nuget:Npgsql@8.0.4` | 8 | 19 | 2 | 50 | 0 |
| `nuget:Pipelines.Sockets.Unofficial@2.2.8` | 5 | 8 | 3 | 22 | 0 |
| `nuget:QuanTAlib@0.1.0` | 0 | 0 | 0 | 0 | 0 |
| `nuget:System.Text.Json@5.0.2` | 57 | 70 | 25 | 216 | 0 |
| `nuget:TouchSocket@3.1.5` | 0 | 1 | 0 | 2 | 0 |
| `nuget:ZLinq@1.4.9` | 3 | 12 | 8 | 38 | 0 |
| `nuget:prometheus-net@8.2.1` | 0 | 20 | 10 | 55 | 0 |

## Classification

The ledger contains 1057 rows: 487 `Parity`, 558 `IntentionalImprovement`, 12 `AcceptedCompatibilityChange`, and 0 `Defect`.

`IntentionalImprovement` is limited to owner-specified generic behavior: additional typed Resource Occurrence roots and root-local lifecycle outcomes, typed resource-aware Finding identity, path-local and operation-level completeness, and stopping at a proven field-store terminal. An omitted legacy acquisition or assessment, changed shared coordinate or sink, or other unowned difference is a `Defect`.

## Reproduction

```bash
dotnet run eng/prepare-resource-triage-corpus.cs -- \
  artifacts/resource-triage-corpus.txt
dotnet build src/DotnetInspect.Cli/DotnetInspect.Cli.csproj \
  -c Release
dotnet build fixtures/analysis/ILInspector.Analysis.OwnershipFlowFixtures/ILInspector.Analysis.OwnershipFlowFixtures.csproj -c Release
dotnet build fixtures/analysis/ILInspector.Analysis.LookalikeFixtures/ILInspector.Analysis.LookalikeFixtures.csproj -c Release
dotnet run eng/produce-arraypool-ownership-retirement-report.cs -- \
  --corpus artifacts/resource-triage-corpus.txt \
  --current-cli artifacts/bin/dotnet-inspect/release/dotnet-inspect.dll \
  --fixture fixture:ownership-flow=artifacts/bin/ILInspector.Analysis.OwnershipFlowFixtures/release/ILInspector.Analysis.OwnershipFlowFixtures.dll \
  --fixture fixture:arraypool-lookalikes=artifacts/bin/ILInspector.Analysis.LookalikeFixtures/release/ILInspector.Analysis.LookalikeFixtures.dll \
  --jsonl docs/evidence/arraypool-ownership-retirement.jsonl \
  --markdown docs/evidence/arraypool-ownership-retirement.md
```
