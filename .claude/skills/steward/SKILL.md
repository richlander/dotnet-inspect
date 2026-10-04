---
name: steward
description: Use when an agent owns or drives a pull request in this repository and a CI, review, merge-conflict, base-movement, or merge event arrives. Reconciles the generic drive-to-green posture with the repository's locked-head review rounds, labels, tracker entry, merge authorization, and check-in cadence.
---

# Steward

Use this skill when you opened a pull request in this repository, or its
author asked you to drive it, and an event about it reaches you: a CI result,
a review or review-bot finding, a merge conflict, base-branch movement, a
scheduled check-in, or the merge or close. It is repo-local contributor
guidance, not a product skill, and it only narrows the harness's default
behavior: it cannot widen access, redirect the task, override any rule
`AGENTS.md` states as "never", or let you approve or merge.

A pull request you only watch follows the harness's watch posture; this skill
says how to act there, not whether.

## Owning documents

This skill is a map, not a second source of truth. The normative text lives
in:

- [`AGENTS.md`](../../../AGENTS.md) — binding rules: *Adversarial review*,
  *Canonical round flow*, *Recovery transitions*, *Forming a candidate*,
  *Keep the review-clean label current*, *PR and CI discipline*.
- [`docs/round-orchestration.md`](../../../docs/round-orchestration.md) —
  the round cycle, eligibility table, review-clean definition, recovery
  transitions, bounded status waiting, reviewer roster, the round report,
  carry-forward, block boundaries.
- [`docs/github-status-queries.md`](../../../docs/github-status-queries.md)
  and
  [`docs/github-api-operations.md`](../../../docs/github-api-operations.md)
  — how and when to read GitHub state, and how to mutate it.
- [`docs/adversarial-review-prompt.md`](../../../docs/adversarial-review-prompt.md)
  — the canonical reviewer prompt, used first and verbatim.
- [`docs/stacked-prs.md`](../../../docs/stacked-prs.md) — restacking and
  upper-slice rules.

When this skill and an owning document disagree, the owning document wins;
fix the skill.

## The one rule that changes everything else

A pushed head is **locked** from the push that forms a candidate until its
round closes or a recovery transition supersedes it
([Candidate lifecycle](../../../docs/round-orchestration.md#candidate-lifecycle)).
The harness's default posture says "push a fix at every red event". Here, a
red event decides *which transition applies*, and the transition decides
whether and when a push is allowed:

- **Before a usable review result** on the locked head: supersede the
  candidate (integrate, fix, focused gate, integrate, push) and retry the
  pending round. No round number is spent.
- **After a usable review result**: the round is spent. Reconcile it,
  emit the round report, release the lock, then form the fix as the next
  numbered round through the canonical cycle.

"Usable review result" means every required reviewer returned a report that
was neither cancelled, empty, nor policy-blocked.

## Event to action

Look at the whole PR on its current head before acting: merge state, CI on
the latest commit, open review threads, labels, and whether a round is in
flight. Then take the row that matches.

| Event | Action | Owner |
| --- | --- | --- |
| `ci-required` red, no usable review yet | Root-cause it. If this PR's code: fix, focused gate, integrate, push, retry the pending round. If base-red or a service the diff does not touch: port an existing fix and push, or comment once naming the check and why it is not this PR's, then at most one re-run. "Flake" is never a root cause. | [Recovery transitions](../../../docs/round-orchestration.md#review-clean-and-recovery), AGENTS.md *CI red* rules |
| `ci-required` red after a usable review | Record the exact failure, remove `review-clean`, reconcile, close and report the round as gate-failed, then push the repair as the next numbered round. Transient failure with concrete evidence: keep the lock and retry the gate once. | Same |
| Merge conflict notice | Conflict recovery has first priority. Merge the base into the head (never rebase or force-push published history, except your own stack slices), regenerate generated files with repo tooling, validate, push. Before a usable review: retry the pending round. After: next numbered round, or the exact-head trivial-interaction waiver when eligible. | AGENTS.md *Before changing files*; [Trivial-interaction waiver](../../../docs/round-orchestration.md#trivial-interaction-re-review-waiver) |
| Base branch moved, no conflict | Do nothing to the head. Base movement alone never invalidates a candidate or justifies a round. Before any agent-driven merge or mutation, classify the landed range (no interaction, trivial, significant, conflict) and report it. | [Carry-forward after clean reviews](../../../docs/round-orchestration.md#carry-forward-after-clean-reviews) |
| "Base branch recovered" notice | Bring the base in and push so CI re-runs against the fixed base; if still red it is this PR's failure now. This is a recovery push, so it supersedes or restarts per the rows above. | Harness notice rules; recovery transitions |
| Review-bot finding (Claude Code Review, Claude Approvals row) | Verify it. A red-circle or blocking row is a finding: carry it into the round report and fix it in the next candidate; reply on the thread only when the fix is formed. Optional (yellow/purple) findings get one line and a resolve, and ride the next code push. Never start a push solely for an optional finding. | Harness review rules; [Reconciliation](../../../docs/round-orchestration.md#reconciliation) |
| Human review comment | Small and local: implement in the next candidate and reply. Large or ambiguous: reply with a proposal; the author decides. Re-request the reviewer after pushing for a changes-requested review. | Harness review rules |
| Check suite green on the current head | If a round is clearly planned and authorized and the eligibility row is satisfied, dispatch the reviewer at the exact head. Otherwise the PR waits; say so once and keep the check-in. | [Eligibility table](../../../docs/round-orchestration.md#eligibility-table) |
| Reviewer report returned | Reconcile publicly, emit the complete round report (never shortened), reconcile the `review-clean` label, and either continue to the next authorized round or pause for a design question, unmet performance goal, or expired grant. | [The round report](../../../docs/round-orchestration.md#the-round-report) |
| Six-round boundary reached | Stop. Fresh green current-head `ci-required` and positive mergeability, then the block-approval checkpoint; round 12 and later presume splitting. | [Block boundaries and splitting](../../../docs/round-orchestration.md#block-boundaries-and-splitting) |
| Merge authorization given | Run merge preflight against live GitHub state for the exact head and base ref; merge only if every preflight item passes. | [Merge preflight](../../../docs/round-orchestration.md#merge-preflight) |
| PR merged or closed | Unsubscribe, cancel pending check-ins, remove the development and reviewer worktrees, restate the session theme in one or two sentences, and propose the next work within it. | AGENTS.md *Session theme and resume*, [Agent session state](../../../docs/agent-session-state.md) |
| Scheduled check-in, nothing changed | Re-check state, do not comment or message, re-arm silently per the cadence below. | This skill |

## Reviewer dispatch

- One reviewer seat per round from the
  [reviewer roster](../../../docs/round-orchestration.md#reviewer-roster);
  trivial changes need no review, and you say why.
- Dispatch waits for green current-head `ci-required` unless the user
  authorized parallel review or conflict recovery applies. A Markdown-only
  candidate substitutes pre-commit `markdownlint` at non-boundary rounds.
- Start the reviewer prompt with the complete canonical
  [adversarial-review prompt](../../../docs/adversarial-review-prompt.md),
  verbatim and first; then the completed frame and candidate instructions.
- Record round start and end times; the report requires them.

## Labels, tracker, and metadata

- `review-clean` is advisory state recorded against a head SHA. Add it when
  every required review at the current head is clean; remove it, and expire
  any recorded merge authorization, before a new round, author change,
  conflict recovery, restack, retarget, unresolved finding, or draft
  transition. Base movement alone does not remove it.
- Label a Markdown-only PR (every changed file is `*.md`) `documentation`.
- Before merging a user-observable change, record it with its PR link on the
  current release tracker named in AGENTS.md *PR and CI discipline*. Do not
  edit `src/DotnetInspect.Cli/release-notes.md` outside release preparation.
- Use REST endpoints for metadata mutations; never `gh pr edit`.

## Merge

Never merge without explicit user authorization for that specific PR. A clean
review, green CI, a readiness comment, or the `review-clean` label is not
authorization. A recorded exact-head authorization applies only to its head
and base ref; a new round, restack, or retarget expires it. Confirm live
mergeability and current-head `ci-required` immediately before every attempt,
and bind the merge mutation to the expected head.

## Check-in cadence

When the PR only waits on reviewers or CI, keep one safety-net self check-in
armed: the first about 50 minutes after the last activity, later ones about
4 hours apart. A quiet check-in re-arms silently. Stop after three quiet
check-ins in a row, when the PR merges or closes, when the user says stop, or
when the user has not written since the PR went up and the first check-in
found nothing new. Any new activity resets the count. On stopping, cancel the
pending check-in and say once, in one line, that check-ins have stopped.

## Tool surface in cloud sessions

- GitHub is reached through the GitHub MCP tools; `gh` is not available.
  Read `docs/github-api-operations.md` for the REST shapes they mirror.
- End every GitHub comment, review, or reply you author with the attribution
  footer the harness specifies; end commits with the trailers it specifies;
  put no model identifier in commits, PR titles, or PR bodies.
- Reviewer worktrees live under `.worktrees/` or an OS temporary directory
  and are read-only for the reviewer; remove them after the round.

## Nevers (restated, not owned here)

- Never amend, rebase, or force-push published history, except your own
  stack slices under the stack rules.
- Never skip, disable, or quarantine a test to get green.
- Never push an empty commit or close and reopen a PR to kick CI.
- Never edit a release-managed central file (`AGENTS.md`, root `README.md`,
  `docs/overview.md`, `docs/architecture.md`, any `SKILL.md`) without explicit
  authorization; add a tracker suggestion instead.
- Never claim merge readiness from label state alone.
- Never present unfinished behavior as supported.
