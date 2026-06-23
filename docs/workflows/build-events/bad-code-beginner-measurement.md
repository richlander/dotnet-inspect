---
id: build-events-bad-code-beginner-measurement
description: Lightweight A/B measurement for bad-code raw build vs build-event workflows
commands: [build]
areas: [build-events, diagnostics, measurement, agent-eval]
---

# Bad-Code Beginner Measurement

> Beginner measurement plan for broad signal before investing in a full harness.
> The goal is to compare raw build output with build-event views while keeping
> the matrix small, repeatable, and honest about what the result can claim.

## Preconditions

Use isolated worktrees. Do not edit or reset `~/git/bad-code` directly.

```bash
export BUILD_EVENT_REPO_ROOT="${BUILD_EVENT_REPO_ROOT:-/home/rich/git/dotnet-inspect-build-event-query}"
export BUILD_EVENT_VMR_DOTNET="${BUILD_EVENT_VMR_DOTNET:-/home/rich/git/dotnet-build-events-vmr-pure/artifacts/pure-sdk-test/dotnet}"
export BAD_CODE_SOURCE_ROOT="${BAD_CODE_SOURCE_ROOT:-/home/rich/git/bad-code}"
export BAD_CODE_EVAL_ROOT="${BAD_CODE_EVAL_ROOT:-/home/rich/git/bad-code-build-event-evals}"
test -x "$BUILD_EVENT_VMR_DOTNET"
test -d "$BAD_CODE_SOURCE_ROOT/.git"
mkdir -p "$BAD_CODE_EVAL_ROOT"
```

Build the local `dotnet-inspect` prototype once before running treatment arms.

```bash
dotnet build "$BUILD_EVENT_REPO_ROOT/src/dotnet-inspect" -c Release
```

## 1. Beginner A/B matrix

> Goal: Get broad directional signal with a small matrix before building a full
> skill-validator-style harness.

### 1a. Projects

Run the same two channels for these three projects:

| Project | Path | Baseline shape |
| --- | --- | --- |
| ZeroDaySearch | `ZeroDaySearch/ZeroDaySearch.csproj` | Repeated missing-member/undefined-symbol clusters. |
| BadCodeApp | `BadCodeApp/BadCodeApp.csproj` | Mixed conversion/argument errors plus warning debt. |
| DarkChannel | `DarkChannel/DarkChannel.csproj` | Unsupported language constructs in expression-tree/lambda contexts. |

These are the current dirty-baseline first-pass projects. The static sample logs
for BadWolf and EnterTheRing remain useful for offline view development, but the
current `~/git/bad-code` dirty worktree no longer exposes those older failing
shapes.

### 1b. Channels

| Channel | Agent sees | Purpose |
| --- | --- | --- |
| `raw-build` | Normal `dotnet build` output. | Control: status quo build-log workflow. |
| `build-events` | `dotnet build --view types --event-log-stderr`, then `dotnet-inspect build` views. | Treatment: durable event-log workflow. |

Minimum first pass:

```text
3 projects × 2 channels × 1 model × 1 run = 6 agent runs
```

Promote to `runs=3` and add a weaker model only after the first pass shows a
plausible behavior or cost delta.

## 2. Isolated run setup

> Goal: Every agent run starts from the same source commit and writes to its own
> worktree.

### 2a. Use the thin runner

The lightweight runner handles the setup mistakes found during manual probing:
it captures the dirty `~/git/bad-code` baseline, creates an isolated worktree,
restores the project, preflights `Types`, compares against expected baseline
counts, and writes the agent prompt.

```bash
scripts/build-event-eval.sh tasks
scripts/build-event-eval.sh prepare badcodeapp build-events-explicit
```

The prepare command prints:

```text
run_root=...
prompt=...
preflight_event_log=...
preflight_types=...
```

Hand the generated `prompt.md` to the agent. After the agent finishes, verify the
final event log:

```bash
scripts/build-event-eval.sh verify badcodeapp build-events-explicit <run-root> <final-event-log>
```

The verifier writes summary, types, projects, JSON, and TSV scorecards under
`<run-root>/.build-event-eval/final/` and appends one row to
`$BAD_CODE_EVAL_ROOT/scorecard.tsv`.

### 2b. Manual fallback: capture the bad-code baseline

`~/git/bad-code` may carry the intentionally broken state as uncommitted tracked
changes. Capture that patch once and apply the same baseline to both channels.

```bash
set -euo pipefail
baseline_patch="$BAD_CODE_EVAL_ROOT/bad-code-baseline.patch"
git -C "$BAD_CODE_SOURCE_ROOT" diff --binary > "$baseline_patch"
sha256sum "$baseline_patch"
```

If the intended baseline includes untracked files, copy them deliberately into
each run worktree and record them in the scorecard.

### 2c. Manual fallback: create a run worktree

Replace `<project>` with `badwolf`, `enter-the-ring`, or `zerodaysearch`; replace
`<channel>` with `raw-build` or `build-events`.

```bash
set -euo pipefail
run_id="$(date -u +%Y%m%dT%H%M%SZ)-<project>-<channel>"
run_root="$BAD_CODE_EVAL_ROOT/$run_id"
git -C "$BAD_CODE_SOURCE_ROOT" worktree add --detach "$run_root" HEAD
if [ -s "$BAD_CODE_EVAL_ROOT/bad-code-baseline.patch" ]; then
  git -C "$run_root" apply "$BAD_CODE_EVAL_ROOT/bad-code-baseline.patch"
fi
printf 'RUN_ROOT=%s\n' "$run_root"
```

Record `RUN_ROOT`, commit SHA, channel, model, and start time in the scorecard.

### 2d. Manual fallback: pre-restore and validate the source-diagnostic shape

Restore before handing the worktree to an agent so `--no-restore` measures source
diagnostics, not missing `project.assets.json`.

```bash
set -euo pipefail
cd "$run_root"
"$BUILD_EVENT_VMR_DOTNET" restore "<project-path>"
```

Before launching the agent, run one evaluator-owned preflight build. Do not use
this as the agent's result; use it only to reject bad setup.

```bash
set +e
"$BUILD_EVENT_VMR_DOTNET" build "<project-path>" --no-restore --no-incremental --view types --event-log-stderr
exit_code=$?
set -e
test "$exit_code" -ne 0
```

If preflight passes, or if the only error is `NETSDK1004`, the run setup is
invalid. Fix the baseline setup before launching agents.

## 3. Normative metrics and informative signals

> Goal: Make only defensible claims. Tool behavior explains outcomes; it is not
> itself the value claim.

### 3a. Normative metrics

Claims may rest on these:

| Metric | How to record |
| --- | --- |
| Functional pass | Final VMR build exits `0`, or diagnostics are reduced without new errors when full pass is not reachable. |
| Quality | Dominant diagnostic fixed correctly; no unrelated rewrites; behavior-preservation checks pass when the project has runnable behavior. |
| Cost/time | Wall-clock elapsed time; token/cost fields if the agent host reports them. |

### 3b. Informative signals

Use these to explain a result, not as the result:

| Signal | Why it helps interpretation |
| --- | --- |
| Build count | Shows retry/flailing shape. |
| Turn count | Shows interaction shape, but not a value claim by itself. |
| Tool-call shape | Distinguishes raw-log parsing, source browsing, `dotnet-inspect build`, and repeated broad searches. |
| Diagnostic ordering | Shows whether the agent fixed the dominant cluster first. |
| Before/after counts | Checks whether the agent can account from durable event logs instead of raw text. |

## 4. Control prompt: raw build channel

> Goal: Establish the status quo without instructing the agent to use build-event
> views.

### 4a. Raw build agent prompt

```prompt
You are in an isolated bad-code worktree at $RUN_ROOT. Fix the build for
<project-path>. Use the source-built VMR dotnet at $BUILD_EVENT_VMR_DOTNET for
every build command. Start with:

`$BUILD_EVENT_VMR_DOTNET build <project-path> --no-restore --no-incremental`

Use the normal build output and whatever local source inspection is needed. Do
not modify unrelated projects. Do not commit changes.

Report:
- final build result
- files changed and rationale
- diagnostic codes fixed or remaining
- elapsed time if available
- build count
- any raw-log parsing or retry loops that affected the work
```

## 5. Treatment prompt: build-event channel

> Goal: Test whether event-log views improve the agent's outcome or cost.

### 5a. Build-event agent prompt

```prompt
You are in an isolated bad-code worktree at $RUN_ROOT. Fix the build for
<project-path>. Use the source-built VMR dotnet at $BUILD_EVENT_VMR_DOTNET for
every build command. Start with:

`$BUILD_EVENT_VMR_DOTNET build <project-path> --no-restore --no-incremental --view types --event-log-stderr`

Use the emitted event log with this worktree's `dotnet-inspect build`
implementation. If `dotnet-inspect-dev` is not on PATH, run:

`dotnet run --project $BUILD_EVENT_REPO_ROOT/src/dotnet-inspect -c Release --no-build -- build <log> -S Types --tsv`

Use `Types` first, then filtered `Errors`/`Diagnostics`, `Projects`, or `Details`
only as needed. Do not parse raw build logs as the primary diagnostic source. Fix
the dominant diagnostic cluster first when it is safe to do so. Do not modify
unrelated projects. Do not commit changes.

Report:
- before diagnostic counts by code from build-event views
- final build result
- files changed and rationale
- after diagnostic counts by code from build-event views
- final event-log path or ID
- elapsed time if available
- build count
- whether build-event views changed the path you took
```

## 6. Scorecard

> Goal: Store enough data to compare runs without building a full harness yet.

### 6a. One row per run

| Field | Example |
| --- | --- |
| Project | `ZeroDaySearch` |
| Channel | `raw-build` / `build-events` |
| Model | Agent model ID |
| Run root | `/home/rich/git/bad-code-build-event-evals/...` |
| Source commit | `git rev-parse HEAD` |
| Functional result | pass / partial / fail |
| Quality notes | dominant cluster fixed, no unrelated rewrites, behavior checks |
| Wall time | minutes |
| Token/cost | if reported by host |
| Build count | informative |
| Tool behavior | informative |
| Before counts | treatment only, from event views |
| After counts | treatment preferred, from event views |
| Final log | treatment final event-log path or ID |
| Notes | view gaps, confusion, missing `Explain` coverage |

### 6b. Beginner readout

After the six-run first pass, make only directional claims:

- Same-or-better functional result at lower wall time/token cost means the
  build-event workflow is promising.
- Same result but clearer accounting still counts as workflow evidence, not a
  cost claim.
- Worse result, more cost, or repeated view confusion means the product/skill
  needs another iteration before a larger eval.
- Tool-call/build-count changes explain the result; they do not prove benefit
  unless the normative metrics move.

## 7. Best-case explicit directive task

> Goal: Test the build-event workflow under favorable conditions before backing
> off to less direct instructions.

### 7a. Task contract

Start with `BadCodeApp`. The agent is explicitly directed to use build-event
views and must not stop at build success if warnings remain.

Success criterion:

```text
Final build-event Types view is empty: 0 errors and 0 warnings.
```

This removes the ambiguity from the first pass, where one arm treated "fix the
build" as errors-only and another fixed warnings too.

### 7b. Explicit build-event prompt

```prompt
You are in an isolated bad-code worktree at $RUN_ROOT. Fix all diagnostics for
BadCodeApp/BadCodeApp.csproj: errors and warnings. Use the source-built VMR
dotnet at $BUILD_EVENT_VMR_DOTNET for every build command.

You must use the build-event workflow. Start with:

`$BUILD_EVENT_VMR_DOTNET build BadCodeApp/BadCodeApp.csproj --no-restore --no-incremental --view types --event-log-stderr`

Use the emitted event log with this worktree's `dotnet-inspect build`
implementation. If `dotnet-inspect-dev` is not on PATH, run:

`dotnet run --project $BUILD_EVENT_REPO_ROOT/src/dotnet-inspect -c Release --no-build -- build <log> -S Types --tsv`

Then use `Diagnostics`, `Errors`, `Warnings`, `Projects`, or `Details` as needed.
Do not parse raw build logs as the primary diagnostic source. Fix the dominant
diagnostic cluster first when safe, rebuild, and repeat until the final `Types`
view is empty. Do not modify unrelated projects. Do not commit changes.

Report:
- before diagnostic counts by code from build-event views
- files changed and rationale
- after diagnostic counts by code from the final event log
- final event-log path or ID
- final functional result: pass / partial / fail
- elapsed time if available
- build count
- whether any diagnostics were skipped and why
```
