---
name: deep-inspect-analysis
description: Use for opt-in Deep Inspect run triage: test lane failures, census artifacts, pinned-subset drift, and routing findings into issues or tracker updates.
---

# Deep Inspect analysis

Use this skill when the user asks for Deep Inspect analysis or wants an opt-in
validation/census run reviewed. Produce a short status report: whether the test
lane passed, whether census health moved, and whether any result needs a durable
issue, tracker row, rebaseline note, or no-action note.

Run from the repository root and prefer `gh` for GitHub Actions and issue data.

## Read the latest runs

Start with recent `deep-inspect.yml` runs:

```bash
gh run list --workflow deep-inspect.yml --limit 10 \
  --json databaseId,status,conclusion,createdAt,updatedAt,event,headBranch,headSha,url
```

Pick the relevant completed run. Check whether it ran `lane=test`, `lane=census`,
or `lane=all`, then classify failures by lane:

```bash
gh run view <run-id> --json jobs,conclusion,status,createdAt,updatedAt
gh run view <run-id> --log-failed
```

Test-lane failure classes: setup/build, full decompiler tests, full analysis
tests, ILAssembler restore, IL round-trip sweep, cancelled/environmental.
Census-lane failure classes: corpus prep, corpus sensor, validity scan, validity
sweep, assertion scan, analysis corpus sensor, paydirt recall, artifact upload,
cancelled/environmental.

## Read census artifacts

If the run produced `deep-inspect-census`, download it to `/tmp`:

```bash
rm -rf /tmp/deep-inspect-<run-id>
mkdir -p /tmp/deep-inspect-<run-id>
gh run download <run-id> -n deep-inspect-census \
  -D /tmp/deep-inspect-<run-id>
```

Summarize the corpus snapshot first:

```bash
jq '{generatedUtc, validityCompileCap, fidelityCompileCap, methodCap, metrics}' \
  /tmp/deep-inspect-<run-id>/deep-inspect/corpus-snapshot.json
```

Then list actionable rows:

```bash
snapshot=/tmp/deep-inspect-<run-id>/deep-inspect/corpus-snapshot.json

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

Aggregate counts include dotnet-inspect self assemblies, so repo growth can move
totals without a decompiler regression. Treat the pinned NuGet subset as the
stable regression signal and report aggregate drift separately.

```bash
baseline=tools/DecompilerHarness/corpus/real-world-baseline.json
snapshot=/tmp/deep-inspect-<run-id>/deep-inspect/corpus-snapshot.json

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

Before creating new work, check #1584 for triage comments and #1568 for active
burndowns. The current pattern is to cluster repeated rows into focused
burndowns like #1687 (invalid Full printer) or #1688 (changed-method fidelity
skeleton), not to file one issue per assembly.

## Report shape

Keep the report short:

```md
Deep Inspect analysis: <green | failed | moved>

- Latest run: <id>, <sha>, <lane>, <conclusion>, <duration/link>.
- Failure class: <none | test | census | environment>, with root cause if known.
- Corpus health: fully raised, conditional residual, forward-merge, Full malformed,
  semantic defects, pass bugs, fidelity exact/opcode-diff/recompile/context counts.
- Pinned-subset signal: <unchanged | improved | regressed>; separate aggregate drift.
- Action: <none | watch | rebaseline | update #1584/#1568 | file focused issue>.
```

Do not hand-construct PR quality tables. For PR evidence, use the
DecompilerHarness-generated `--quality-diff-card`.
