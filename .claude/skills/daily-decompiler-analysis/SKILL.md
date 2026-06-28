---
name: daily-decompiler-analysis
description: Use for morning triage of decompiler-daily runs, corpus snapshots, pinned-subset drift, and routing failures into issues or tracker updates.
---

# Daily decompiler analysis

Use this skill when the user asks for "daily decompiler analysis" or wants the
morning decompiler signal reviewed. Produce a short status report: whether the
nightly decompiler signal is green, whether corpus health moved, and whether any
result needs a durable issue, tracker row, rebaseline note, or no-action note.

Run from the repository root and prefer `gh` for GitHub Actions and issue data.

## Read the latest runs

Start with recent `decompiler-daily.yml` runs:

```bash
gh run list --workflow decompiler-daily.yml --limit 10 \
  --json databaseId,status,conclusion,createdAt,updatedAt,event,headBranch,headSha,url
```

Pick the newest completed run, but scan the last few runs for repeated or newly
appearing failures. For failed runs, classify the failing step before diagnosing:

```bash
gh run view <run-id> --json jobs,conclusion,status,createdAt,updatedAt
gh run view <run-id> --log-failed
```

Use these failure classes: setup/build, `ILInspector.Decompiler.Tests`,
corpus-prep, corpus sensor, artifact upload, cancelled/environmental.

## Read the corpus snapshot

If the run produced a `decompiler-corpus-snapshot` artifact, download it to
`/tmp`:

```bash
rm -rf /tmp/decompiler-daily-<run-id>
mkdir -p /tmp/decompiler-daily-<run-id>
gh run download <run-id> -n decompiler-corpus-snapshot \
  -D /tmp/decompiler-daily-<run-id>
```

Summarize the snapshot first:

```bash
jq '{generatedUtc, validityCompileCap, fidelityCompileCap, methodCap, metrics}' \
  /tmp/decompiler-daily-<run-id>/corpus-snapshot.json
```

Then list actionable rows:

```bash
snapshot=/tmp/decompiler-daily-<run-id>/corpus-snapshot.json

printf '\nFULL_MALFORMED\n'
jq -r '.methods[] | select((.validity // "") | startswith("full-malformed:")) |
  [.assembly,.displayMethod,.validity,.fidelity,.residual,.fidelityCheck] | @tsv' "$snapshot"

printf '\nSEMANTIC_DEFECT\n'
jq -r '.methods[] | select((.validity // "") | startswith("semantic-defect:")) |
  [.assembly,.displayMethod,.validity,.fidelity,.residual,.fidelityCheck] | @tsv' "$snapshot"

printf '\nFIDELITY_OPCODE_DIFF\n'
jq -r '.methods[] | select(.fidelityCheck == "OpcodeDiff") |
  [.assembly,.displayMethod,.validity,.fidelity,.residual,.fidelityCheck] | @tsv' "$snapshot"

printf '\nPASS_BUG\n'
jq -r '.methods[] | select(.passBug != null) |
  [.assembly,.displayMethod,.passBug,.validity,.fidelity,.residual,.fidelityCheck] | @tsv' "$snapshot"
```

## Separate pinned signal from repo-growth drift

Daily aggregate counts include dotnet-inspect self assemblies, so repo growth can
move totals without a decompiler regression. Treat the pinned NuGet subset as the
stable regression signal and report aggregate drift separately.

```bash
baseline=tools/DecompilerHarness/corpus/real-world-baseline.json
snapshot=/tmp/decompiler-daily-<run-id>/corpus-snapshot.json

jq -n --slurpfile b "$baseline" --slurpfile c "$snapshot" '
  def pinned($s):
    [ $s.methods[] | select(.assemblyPath | startswith("nuget:")) ] as $m |
    {
      total: ($m | length),
      fullyRaised: ($m | map(select(.fullyRaised)) | length),
      conditional: ($m | map(select(.residual == "structuring: conditional-branch")) | length),
      fullMalformed: ($m | map(select((.validity // "") | startswith("full-malformed:"))) | length),
      semantic: ($m | map(select((.validity // "") | startswith("semantic-defect:"))) | length),
      fidelityChecked: ($m | map(select(.fidelityCheck != "not-sampled")) | length),
      opcode: ($m | map(select(.fidelityCheck == "OpcodeDiff")) | length),
      recompile: ($m | map(select(.fidelityCheck == "RecompileFail")) | length),
      context: ($m | map(select(.fidelityCheck == "ContextFail")) | length)
    };
  {baseline: pinned($b[0]), current: pinned($c[0])}'
```

If pinned counts moved, drill into per-method changes before calling it a
regression. If only aggregate counts moved, call it baseline staleness or
repo-growth drift unless pass bugs appeared.

## Route findings

Use `docs/decompiler-correctness-pipeline.md` vocabulary:

- Stage 0: build/test failures.
- Stage 2: Full malformed or semantic validity defects.
- Stage 8: compile-back fidelity opcode diffs.
- Stage 9: corpus-card movement or baseline staleness.
- Stage 10: changed-method fidelity/skeleton failures.

Before creating new work, check #1584 for nightly triage comments and #1568 for
active burndowns. The current pattern is to cluster repeated rows into focused
burndowns like #1687 (invalid Full printer) or #1688 (changed-method fidelity
skeleton), not to file one issue per assembly.

## Report shape

Keep the morning report short:

```md
Daily decompiler analysis: <green | failed | moved>

- Latest run: <id>, <sha>, <conclusion>, <duration/link>.
- Failure class: <none | test | corpus sensor | environment>, with root cause if known.
- Corpus health: fully raised, conditional residual, forward-merge, Full malformed,
  semantic defects, pass bugs, fidelity exact/opcode-diff/recompile/context counts.
- Pinned-subset signal: <unchanged | improved | regressed>; separate aggregate drift.
- Action: <none | watch | rebaseline | update #1584/#1568 | file focused issue>.
```

Do not hand-construct PR quality tables. For PR evidence, use the
DecompilerHarness-generated `--quality-diff-card`.
