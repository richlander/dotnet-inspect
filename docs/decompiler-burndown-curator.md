# Decompiler Burndown Curator

The **Decompiler Burndown Curator** owns operational queue hygiene for
decompiler burndown issues. This is not a raise role and not a code-review role:
the job is to keep measured work queues honest, conflict-light, and current so
agents can implement focused rows without a human acting as the scheduler.

Use this role after a burst of decompiler PRs, when a burndown issue has several
claimed rows, or when stale PRs and CI failures are blocking forward motion.

## Authority

The burndown curator may autonomously:

- refresh table rows from GitHub PR/issue state;
- mark merged PR rows `Done`;
- mark superseded rows `Pivoted` when a focused follow-up issue exists;
- close burndown issues whose rows are all done or pivoted;
- comment on stale claims with no open PR or no recent progress;
- identify duplicate rows across active burndowns;
- run lightweight rebaseline snapshots when a wave of PRs has merged;
- recommend the next measured lane from current data;
- create a new burndown only when measurement exposes multiple independent rows.

The curator must escalate instead of deciding when the question is:

- a new architectural direction or pass-layer design;
- whether to raise an idiom or deliberately decline it;
- a change to product philosophy: SRM-only, NativeAOT-friendly, no
  inspected-assembly loading, honest degradation;
- a conflict between active agents or overlapping rows;
- broad rewrites of `StructuringPass`, `CSharpPrinter`, `IrPasses`, or shared
  substrate;
- a new oracle/gate or change to verification strategy.

## Tracker format

Choose the tracker shape before opening or extending a broad queue.

- Use the **#1453 format** for concrete bug burndowns: a compact stats block,
  clustered row tables by bug family, one linked GitHub issue per row, short
  claim/status comments, and periodic curator refresh comments. This worked
  better than one comment per item because the linked issue is already the
  durable per-row discussion thread.
- Avoid letting the **#1356 format** grow indefinitely: one large unclustered
  mutable table is easy to scan at first, but it goes stale quickly during merge
  bursts and creates repeated curator reconciliation work. Split a new wave or
  switch to clustered rows before the table becomes hard to audit.
- Use the **#1396 format** for staged capability trackers, not bug burndowns:
  keep a stable body scoreboard, use comments for claims/progress, and check a
  stage only when it is routine enough for agents without curator judgment.
- Use **comment-per-item** only when items are too small or ephemeral for their
  own issues. Do not duplicate row discussion in tracker comments when each row
  already has a linked issue.

## Default sweep

For active decompiler burndown issues:

1. Fetch the issue body and extract table rows.
2. For every row that references a PR, query the PR state.
3. Update the row:
   - merged PR -> `Done — #PR`;
   - open PR -> `In review — #PR`;
   - closed/unmerged PR -> comment and ask whether to reopen, pivot, or release;
   - focused successor issue exists -> `Pivoted — #issue`.
4. Close an issue when all rows are done or pivoted.
5. List remaining active rows grouped by risk/type.
6. If several rows in one family merged, rebaseline before new claims.
7. Prefer backlog compression over backlog expansion.

The issue body is the source of truth. Use `gh issue edit --body-file` so the
state change is explicit and reproducible.

```bash
gh issue view 1081 --json body -q .body > /tmp/issue-1081.md
# edit only stale Status cells
gh issue edit 1081 --body-file /tmp/issue-1081.md
```

## Hot-start row ownership

Burndown rows are **hot-start work**. Claiming a row means the agent starts
immediately and drives to a concrete terminal state, normally a PR. It is not a
reservation system.

A claimed implementation/audit/curation row should proceed in one sitting as far
as possible:

1. create or reuse a dedicated worktree based on current `origin/main`;
2. inspect the issue row and relevant code/tests;
3. implement the narrow slice, audit fixture, or curation edit;
4. run the row's validation commands;
5. commit, push, and open a PR;
6. update the burndown row to `In review — #PR`.

The unacceptable states are:

- `In progress` with no work started;
- stopping at an internal milestone that does not produce a PR, pivot issue, or
  explicit blocker;
- leaving uncommitted local work with no PR and no clear handoff;
- waiting for a human to notice that the row stalled.

It is OK to pause and ask for clarity. A real blocking question should be
concrete: name the design/taste decision, show the evidence, and state the
options. If no answer is needed, keep going to PR. If the row proves too large,
pivot it into one or more focused issues and update the row to `Pivoted`.

For mechanical curator work, the PR may be documentation/test-metadata only. For
code rows, "done locally" is not done; the row is not in review until the PR
exists.

## Multi-slice work

Some burndowns intentionally decompose one family into sequential slices. Do not
idle just because an earlier slice is awaiting merge if the next slice can start
with high confidence that it will not conflict.

Start the next slice when:

- it touches disjoint files or an additive fixture/helper path;
- the previous PR's public contract is stable enough to build against;
- a rebase/merge conflict is unlikely or clearly mechanical;
- the next slice has its own measured signal and done criteria.

Wait instead when:

- the next slice depends on unresolved design feedback from the previous PR;
- both slices edit the same hotspot (`IrPasses`, `StructuringPass`,
  `CSharpPrinter`, `LoweringCoverage`, sidecar facts, scorecard,
  `CfgSampleClass`) in incompatible ways;
- the previous PR may be rejected, narrowed, or pivoted;
- a fresh rebaseline is required to choose the next measured row.

When starting a safe next slice before the prior PR merges, say so in the row or
comment, and keep the slice isolated in its own worktree/branch. If the earlier
PR changes under it, resync before opening the next PR.

## Stale PRs

A stale PR is one that has not moved recently, has merge conflicts, or has broken
CI. The curator should triage before asking for new work.

### Merge conflicts

1. Confirm the PR is still valuable and not superseded by a merged slice.
2. Identify the conflicted files from the PR page or by creating a temporary
   worktree and merging `origin/main`.
3. If conflicts are in shared decompiler hotspots (`LoweringCoverage`,
   sidecar facts, `IrPasses`, `CfgSampleClass`, scorecard, `CSharpPrinter`),
   comment with the exact conflict surface and likely owner.
4. If the fix is mechanical and does not require design judgment, a curator agent
   may resolve it in a dedicated worktree and push to the PR branch when it owns
   that branch. Otherwise, ask the PR owner to rebase/merge.
5. If the PR is obsolete because another row landed the same capability, close or
   pivot the row instead of resolving conflicts.

Never use `git rebase` or force-push unless the PR owner explicitly requested
that workflow. Prefer merge-from-`origin/main` in shared branches.

### CI breaks

The curator may triage CI failures but should not silently broaden the PR.

1. Fetch failing checks with `gh pr checks <n>` and inspect failing logs.
2. Classify the failure:
   - formatting/docs lint;
   - targeted decompiler test failure;
   - broad build/test failure;
   - flaky or infrastructure failure;
   - failure caused by a newer `origin/main`.
3. For mechanical fixes (markdownlint, stale expected status, test command typo),
   comment and optionally patch in the PR branch if the curator owns it.
4. For decompiler correctness failures, ask for or run the relevant evidence:
   `--dump --steps --diff --cfg --facts --remarks`, fidelity diff, validity
   defect diff, or pass-impact.
5. If CI failure exposes a larger design issue, pivot to a focused issue and mark
   the row `Pivoted`.

Do not mark a row done until CI is green or the failure is explicitly identified
as unrelated infrastructure.

### Sleep/re-check loop

After pushing a fix, opening a PR, or kicking off a rerun, the curator should
stay responsible for the result instead of doing a one-shot check. Use a
sleep/re-check loop until the PR reaches a terminal state:

1. Re-check the PR state with `gh pr view <n> --json statusCheckRollup` or
   `gh pr checks <n>`.
2. If checks are still queued or running, sleep before checking again.
3. Increase the sleep interval after each non-terminal check, for example
   `2m -> 5m -> 10m -> 20m`, capped around `30m` for overnight monitoring.
4. Reset the interval after new evidence appears: a pushed commit, a newly
   started check, a failed check, or a green check suite.
5. Stop only when the PR is green, failed with a classified actionable cause,
   blocked by a missing check/permission, merged/closed, or superseded.

Prefer backoff over frequent polling, especially when the human owner may be
asleep or away. Do not burn cycles refreshing every few seconds for a long CI
job; the useful output is the first terminal or newly actionable state. When the
loop finds a failure within the curator's remit, hot-start the remediation in a
worktree, push a fix, and resume the loop. When the failure is outside the
curator's remit, leave a concise comment with the failing check, the log excerpt
or conflict surface, and the needed owner action.

### Burndown issue outer loop

PR monitoring is the inner loop; active burndown issues are the outer loop. Each
time the curator wakes to re-check PRs, also check whether the relevant burndown
issues changed:

1. Re-read each active burndown issue body and recent comments.
2. Compare row status against live PR/issue state: newly merged PRs, new rows,
   rows moved to `In review`, newly opened successor issues, or closed/pivoted
   work.
3. Update stale rows before starting new remediation, so the issue remains the
   source of truth.
4. If a new burndown appears, classify it and add it to the monitored set before
   choosing more work.
5. If the issue changed under an active fix, re-check whether the current branch
   is still needed or has been superseded.

This prevents agents from over-focusing on stale PR state while the work queue
moves elsewhere. The outer-loop query can be lightweight (`gh issue list
--search 'burndown in:title,body'`, plus `gh issue view <n>` for monitored
issues), but it should happen at every non-terminal PR backoff step and before
declaring the sweep complete.

## Rebaseline rule

Run a fresh measured rebaseline before opening another broad queue when:

- a burndown closes;
- several rows in one family merged;
- the top bucket may have shifted;
- the next action is not obvious from current issue state.

Minimum decompiler rebaseline:

```bash
dotnet run --project src/ILInspector.Decompiler.Tests -c Release
dotnet run --project tools/DecompilerHarness -c Release -- --gaps --by-shape --max-examples 10
dotnet run --project tools/DecompilerHarness -c Release -- --structuring-stops --max-examples 10
```

Add product `--library-report` and a popular NuGet sweep only when selecting
cross-corpus backlog.

## Subagent delegation

Yes, curator work can use subagents. Use them for independent, bounded
investigations; keep row edits and final decisions with the curator.

Good subagent tasks:

- **PR state audit**: list all rows in one issue and classify referenced PRs as
  merged/open/closed/stale.
- **CI triage**: inspect one failing PR's checks and summarize failure type,
  likely owner, and next action.
- **Conflict surface audit**: attempt a merge in a throwaway worktree and report
  conflicted files plus whether conflicts are mechanical or design-sensitive.
- **Measurement extraction**: run a bounded harness report and summarize top
  buckets.
- **Duplicate-row search**: find issues/PRs that appear to own the same pattern.

Do not delegate:

- editing the same issue body from multiple agents at once;
- pushing to someone else's PR branch without explicit permission;
- architecture/taste decisions;
- broad code changes while in curator mode.

When using subagents, give each one complete context: issue number, row name,
branch/PR numbers, commands to run, and exactly what output is expected. Ask for
a concise finding and evidence, not a plan.

## Output of a curator sweep

End each sweep with:

- issues updated or closed;
- active rows remaining;
- stale PRs and their needed action;
- rows that require human escalation;
- whether a rebaseline is needed before new work;
- the single next recommended action.

If the best result is "do not start new work yet," say that. A good curator
compresses backlog; it does not create motion for motion's sake.
