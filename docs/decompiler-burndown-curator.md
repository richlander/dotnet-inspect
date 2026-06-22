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
