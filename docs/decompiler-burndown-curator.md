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
- cluster orphan decompiler issues into a new burndown when they share a clear
  measured theme and each row has an issue-level done signal;
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

Every tracker format must optimize for two failure modes:

- **Claim races.** Avoid making the issue body the first write for claims. Claim
  with an append-only comment on the row issue or tracker, then immediately
  re-read recent comments and open PRs for that row. If another agent claimed or
  opened a PR first, back off and explicitly release or pivot your claim. Curator
  sweeps should reconcile duplicate claims before assigning more work.
- **Low-context next work.** Do not require agents to read a huge tracker just to
  find a row. Keep a short stats/open-rows block near the top, cluster rows by
  bug family, and split new waves when the open list becomes hard to scan. The
  tracker should answer "what can I take next?" before the full table.

## Curator rollup

The **Decompiler Burndown Curator** owns the long-lived rollup tracker for
active burndown lists. The rollup is the single place a maintainer can check to
see which burndown lists exist, how many open row issues remain in each, and what
should happen next.

The curator maintains one tracking issue with:

- active burndown lists and their open row counts;
- rows in review and stale PRs needing takeover;
- recently closed or retired burndowns;
- newly discovered orphan-issue clusters that may need their own list;
- the next recommended action for agents and maintainers.

Current curator rollup tracker: #1568.

Update the rollup whenever a burndown opens, closes, splits, goes cold,
or materially changes open-row count. During active periods, refresh it on the
same `30m` cadence as PR sweeps; during idle periods, use a daily backup sweep or
refresh before assigning new work.

Maintainers should use the curator rollup as the dashboard: merge clean PRs,
assign the next open row, ask for rebaseline when a list closes, and stop agents
from creating motion when the next measured lane is not clear.

## Ladder tester

**Ladder testers** are the agents who advance the product quality ladder
(currently #1599). The ladder is the user-visible bring-up sequence; it is not a
parallel bug queue. Use one Ladder tester at a time unless a maintainer
explicitly splits independent preparation work.

The Ladder tester should:

1. start from the curator rollup (#1568) and confirm the active ladder issue;
2. choose the current ladder leg, not an arbitrary later one;
3. define or refresh that leg's fixture/corpus, current score, scoped success
   bar, and regression guard;
4. run the current decompiler against the leg's fixture/corpus;
5. either **claim success** on the ladder issue when the leg already meets its
   bar and has a guard, or create focused issues for the failures that block it;
6. prefer creating a leg-specific burndown list when a non-success leg exposes
   multiple independent failures;
7. link any leg-specific burndown from the primary curator rollup (#1568), so
   maintainers and runners can find it without reading the whole ladder issue;
8. require cross-model adversarial review before merging ladder PRs that add or
   change fixture/corpus coverage, guards, success bars, or rung-completion
   claims.

The Ladder tester should not implement broad fixes directly by default. Its job
is to keep the ladder objective: define the target, prove current state, file or
cluster the blocking work, and verify that landed fixes move the leg to its
scoped 100% bar. If a single small fix is obviously enough to complete the leg,
ask before switching from tester to implementer.

Ladder PRs follow the same adversarial-review expectation as other decompiler
PRs: request review from another model family before merge, then post a PR
comment summarizing the finding and any follow-up change or explicit non-action.
For fixture/guard PRs, the review should try to falsify the rung bar and guard:
missing constructs, too-weak success criteria, unguarded regression paths, and
cases where a green fixture would let an invalid or misleading rung claim pass.
If review finds the rung claim is broader than the fixture evidence, either
widen the fixture/guard or narrow the rung-completion claim before marking the
rung done.

When a ladder leg is not successful, the preferred flow is:

1. Post the measured failure summary on the ladder issue.
2. File focused issues for each independent failure pattern.
3. If there is more than one issue, create a small burndown list for that leg.
4. Add that burndown to #1568 with open row count and next action.
5. When the burndown closes, re-run the ladder leg and either claim success or
   file the next blocking pattern.

## Burndown runner

**Burndown runners** are the agents who act on burndown lists. A runner owns one
claimed row at a time and drives that row to a PR, explicit blocker, or pivot
issue. Runners do not own the rollup tracker and should not create competing
dashboard issues.

Burndown runners should use burndown lists as hot-start work queues:

1. Start from the curator rollup (#1568) and choose an active burndown with open
   rows.
2. Open that burndown and read its goal, guardrails, rows, and evidence
   expectations before claiming anything.
3. Pick one row with a concrete issue and done signal. Claim with an append-only
   comment, then immediately re-check for duplicate claims or open PRs.
4. Work the row in one sitting toward a PR, explicit blocker, or pivot issue.
5. Keep the PR narrow to the row's done signal; do not broaden into adjacent rows
   or architecture work.
6. Before final tests, adversarial review, and PR handoff, fetch `origin/main`
   and merge or rebase the latest main into the row branch.
7. When the PR opens, update the row to `In review — #PR`; when it merges, update
   to `Done — #PR`.
8. If the row is obsolete, duplicated, or too broad, mark it `Pivoted — #issue`
   with the focused successor.

### When the queue drops to zero

A zero-open-row queue is a curator decision point, not permission to invent work.
When a burndown list reaches zero open rows:

1. Reconcile every row against live issue and PR state.
2. Mark merged rows `Done — #PR` and superseded rows `Pivoted — #issue`.
3. Close the burndown if all rows are `Done` or `Pivoted`.
4. Move the list to the retired section of the curator rollup.
5. Run or request a rebaseline when the closure may shift the next measured lane.
6. Look for orphan issues that form a clear themed queue.
7. If no clear queue exists, say "no new burndown yet" and recommend waiting,
   measuring, or assigning a non-burndown tracker such as #1396.

Do not keep a zero-row burndown alive as a placeholder. Do not create a one-row
burndown just to keep runners busy.

### Avoiding contention and double claims

Claim races waste more time than a short re-check. Runners must:

- claim with an append-only comment on the row issue or burndown before editing
  code;
- immediately re-read the row issue, burndown comments, and open PRs after
  claiming;
- own only one row at a time unless the curator explicitly splits disjoint work;
- use a branch name that includes the issue number or row identity;
- back off if another runner already claimed the row or opened a PR first;
- release the claim explicitly when stopping without a PR, pivot issue, or
  blocker;
- avoid editing the burndown issue body for claims unless acting as curator.

If two runners claim the same row, prefer the first open PR, then the earliest
clear claim. The other runner should comment that they are releasing or pivoting
and move to a different row.

### Avoiding merge conflicts

Runners should reduce conflicts before asking for review:

1. Start each row from current `origin/main` in a dedicated worktree or branch.
2. Keep changes narrow and row-scoped; avoid shared hotspots unless the row
   requires them.
3. Before final tests, adversarial review, and maintainer handoff, fetch
   `origin/main` and merge or rebase the latest main into the branch.
4. Rerun the row's validation commands after the sync.
5. Request adversarial review only after the synced branch has passed final local
   validation.

Use rebase only for branches you own or when the PR owner requested it. For
shared PR branches, prefer merging `origin/main` so other agents are not
surprised by rewritten history.

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
6. Look for orphan decompiler issues that are not already represented by an
   active burndown and cluster them only when they form a reasonable thematic
   queue.
7. Update the curator rollup when a list opens, closes, or changes open row
   count.
8. If several rows in one family merged, rebaseline before new claims.
9. Prefer backlog compression over backlog expansion.

The issue body is the source of truth. Use `gh issue edit --body-file` so the
state change is explicit and reproducible.

```bash
gh issue view 1081 --json body -q .body > /tmp/issue-1081.md
# edit only stale Status cells
gh issue edit 1081 --body-file /tmp/issue-1081.md
```

## Orphan issue clustering

The curator may create a new burndown from existing orphan issues when doing so
compresses scheduler work. An orphan issue is a concrete decompiler issue that is
not already owned by an active burndown row, open PR, or focused successor lane.

Create a new burndown only when:

- the issues share a clear theme such as the same pass layer, lowering shape,
  diagnostic family, validity bucket, or harness measurement bucket;
- each row has a linked issue that defines the observed defect and a done signal;
- the cluster has enough independent rows to justify a tracker instead of one
  focused issue;
- the theme is narrow enough that agents can claim rows without redoing taxonomy
  work.

Do not create a catch-all burndown for unrelated leftovers. If orphan issues do
not cluster cleanly, leave them as individual issues, recommend the next measured
lane, or run a rebaseline before opening a broad queue.

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

### PR service-level rules

The curator owns PR hygiene as well as issue grooming. Apply these timers from
the first visible actionable state unless a human explicitly asks for a different
handoff:

- **Merge conflicts, 30-minute rule**: if a PR sits with merge conflicts for
  `30m`, take it over.
- **CI failure, 60-minute rule**: if a PR sits with a CI failure for `60m`, take
  it over.
- **Adversarial review, 30-minute rule**: if a decompiler PR lacks an
  adversarial review after `30m`, request one.
- **Final resolution, 60-minute rule**: if adversarial feedback is present and
  unaddressed for `60m`, take over the PR or open the follow-up needed to resolve
  it.
- **Maintainer notification, 30-minute rule**: sweep open PRs every `30m` while
  active, with a longer idle-period backup cadence, and present the maintainers
  with the PRs that are mergeable: clean, green, and with adversarial review
  passed or explicitly resolved.

Taking over means posting a concise comment that names the timer and intended
action, then moving the PR to a terminal state: push a mechanical fix to the PR
branch when permitted, open a replacement/follow-up PR when not, or pivot/close
the associated row when the PR is superseded. Do not wait for another human to
notice a timed-out stale state.

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
6. If the conflict remains unresolved for `30m`, take over under the PR
   service-level rules.

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
6. If the failure remains unresolved for `60m`, take over under the PR
   service-level rules.

Do not mark a row done until CI is green or the failure is explicitly identified
as unrelated infrastructure.

### Adversarial review

Decompiler PRs need adversarial review evidence before they are considered ready
to merge. The curator should:

1. check whether the PR already has an adversarial review request, result comment,
   or documented resolution;
2. request a review after `30m` if none is present;
3. route concrete findings to a PR fix, linked issue, tracker row, or explicit
   non-action comment;
4. take over after `60m` if actionable adversarial feedback is present but
   unaddressed.

Adversarial review is resolved when the PR either incorporates the fix, links a
durable follow-up for accepted deferred work, or records why the finding is not
actionable.

### Maintainer merge list

Every active PR sweep should produce a maintainer-facing merge list. Include only
PRs that are clean, green, and have adversarial review passed or resolved. Keep
the list short and actionable:

- mergeable now;
- blocked only by maintainer approval/merge button;
- newly stale by the `30m`/`60m` rules and already taken over or queued for
  takeover.

For every open PR mentioned in a curator report or touched during a sweep, state
one of:

- `Ready to merge` with the evidence: clean branch, green checks, and adversarial
  review passed/resolved;
- `Blocked by ...` with the concrete blocker: checks, merge conflict, missing
  adversarial review, unresolved adversarial feedback, maintainer decision, or
  explicit runner action.

This PR sweep is in addition to burndown issue grooming: create new clustered
burndowns from orphan issues, close completed burndowns, and update stale rows as
part of the same curator loop.

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

### Periodic issue grooming

Every active burndown needs periodic grooming even when no PR check wakes the
curator. The grooming pass is the outer-loop maintenance that keeps row status,
claim state, and the top "next work" view honest.

Each grooming pass should:

1. refresh the stats/open-rows block near the top of the tracker;
2. reconcile every live row against its linked issue and PR state;
3. detect duplicate or stale claims and ask one owner to release, pivot, or open
   a PR;
4. route review/agent findings to a durable home: a linked issue, PR fix,
   tracker row, or docs update. Do not leave concrete findings only in PR
   comments, tracker comments, or memory;
5. add newly found rows only when they have a concrete issue and done signal;
6. split or close the wave when the open list becomes hard to scan.

Use a backoff cadence so hot queues stay current without wasting cycles during
cold periods:

- **Hot** — recent claims, comments, merges, CI failures, or multiple open PRs:
  groom every `10-20m`, and reset the timer whenever new tracker activity
  appears.
- **Cooling** — two consecutive grooming passes find no row/status changes:
  back off to `30-60m`.
- **Cold** — no open PRs, no recent claims, and only stable open rows remain:
  groom daily, before assigning new work, or before declaring the queue current.
- **Terminal** — all rows are `Done`/`Pivoted` or superseded: close the tracker
  or post the successor lane, then stop grooming that issue.

The cadence is a minimum hygiene rule, not a scheduler that blocks urgent work.
If a human asks for a fresh scan or a merge burst lands, groom immediately and
then resume the appropriate backoff band.

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
- new orphan-issue burndowns created or explicitly declined;
- stale PRs and their needed action;
- mergeable PRs ready for maintainer action;
- rows that require human escalation;
- whether a rebaseline is needed before new work;
- the single next recommended action.

If the best result is "do not start new work yet," say that. A good curator
compresses backlog; it does not create motion for motion's sake.
