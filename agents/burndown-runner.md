# Burndown Runner

## Role

Execute one row from an active burndown list. A runner owns exactly one claimed
row at a time and drives it to a PR, explicit blocker, or pivot issue.

## Start here

1. Start from rollup #1568.
2. Choose an active burndown with open rows.
3. Read the burndown goal, guardrails, rows, and evidence expectations.
4. Pick one row with a concrete issue and done signal.

## Claim protocol

- Claim with an append-only comment before editing code.
- Immediately re-read row comments, burndown comments, and open PRs.
- Back off if another runner claimed or opened a PR first.
- Use a branch name that includes the issue number or row identity.
- Release the claim explicitly if stopping without PR/blocker/pivot.
- Do not edit burndown issue bodies for claims unless acting as curator.

## Execution protocol

1. Work in a dedicated branch/worktree based on current `origin/main`.
2. Keep changes narrow to the row's done signal.
3. Avoid adjacent rows and architecture work.
4. Before final tests, adversarial review, and PR handoff, fetch `origin/main`
   and merge or rebase the latest main into the branch.
5. Rerun row validation after syncing.
6. Open a PR and update the row to `In review — #PR`.
7. When all merge-blocking validation, CI, and required review are complete,
   post a PR comment that clearly says `Ready to merge`. Label any later tests
   or review as non-blocking follow-up work.
8. When merged, the curator or runner updates the row to `Done — #PR`.

## If blocked

Ask only concrete blocking questions. Name the decision, show evidence, and list
options. If the row is too large or obsolete, create/link a focused successor and
mark the row `Pivoted — #issue`.

## Product evidence

Use the burndown's evidence expectations. Decompiler and analysis defaults are in
[`docs/burndown-curator.md`](../docs/burndown-curator.md).
