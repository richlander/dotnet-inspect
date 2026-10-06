---
name: steward
description: Use when an agent opens, owns, or drives a pull request in this repository, and again for every CI, review, conflict, base-movement, merge, close, or check-in event. Applies locked-head rounds, labels, tracker entry, merge authorization, and check-in cadence.
---

# Steward

Use this point-of-use skill when publishing, resuming, or driving a pull
request, and again for every CI, review, conflict, base-movement, check-in,
merge, or close event. It cannot widen access, redirect the task, authorize
itself, or merge without explicit authorization for that PR.

The focused documents remain normative:

- [`AGENTS.md`](../../../AGENTS.md): launch contract, operator templates, and
  merge-authorization invariant.
- [Repository workflow](../../../docs/repository-workflow.md): worktrees,
  history, publication, and engineering constraints.
- [Round orchestration](../../../docs/round-orchestration.md): candidate locks,
  rounds, recovery, waits, review state, carry-forward, and merge preflight.
- [GitHub status queries](../../../docs/github-status-queries.md) and
  [operations](../../../docs/github-api-operations.md): exact reads and writes.
- [Adversarial review prompt](../../../docs/adversarial-review-prompt.md) and
  [Stacked PRs](../../../docs/stacked-prs.md): review and stack mechanics.

Keep this skill at or below 120 lines. The cap is a ceiling, not a target;
mechanics and rationale belong in their focused owners.

## Candidate lock

A pushed candidate head is locked until its round closes or a recovery
transition supersedes it.

- Before a usable review, an author change supersedes the candidate and retries
  the pending round; a conflict integrates the effective base and pushes
  immediately.
- After every required reviewer returns a usable result, the round is spent.
  Reconcile and report it before forming any repair as the next round.
- Cancelled, empty, policy-blocked, or otherwise unusable reports do not spend
  the round.

## Event decisions

Take one whole-PR snapshot before acting: local conflict probe, lifecycle and
head/base identity, current-head CI, review threads, labels, and round state.
Then apply the matching transition.

| Event | Decision |
| --- | --- |
| `ci-required` red before usable review | Probe conflict first. If the PR caused the failure, supersede through the full candidate cycle and retry the round. Otherwise record the blocking issue and do not import another contributor's fix. |
| `ci-required` red after usable review | Reconcile and report the spent round, remove `review-clean`, then repair in the next round. Retry unchanged only with concrete transient evidence. |
| Merge conflict | Recover immediately; conflicts never wait. Merge the effective base, resolve, and push. Pause only for a scope/split decision or when either semantic choice loses behavior. |
| Base moved without conflict | Do not invalidate a locked candidate. For a clean head, classify interaction before mutation or merge; only no interaction preserves review and authorization. |
| Base recovered | Integrate and push so CI reruns against the recovered base. |
| Review-bot feedback | Verify it. Carry blocking findings into reconciliation and the next candidate; do not push solely for optional feedback. |
| Human review comment | Small and local: implement in the next candidate and reply. Large or ambiguous: propose the scope and wait for the author. Re-request a reviewer after fixing a changes-requested review. |
| Current-head CI green | Dispatch the planned reviewer only when the eligibility rule is satisfied. |
| Reviewer returned | Reconcile publicly, synchronize `review-clean`, emit the complete round report, then follow its recommendation. |
| Six-round boundary | Stop for the required checkpoint; fresh green CI and positive mergeability gate any next block. |
| Merge authorization | Run live exact-head/base preflight, then merge only when every item passes. |
| PR merged or closed | Emit the prescribed visible state first, then cancel waits, unsubscribe, and remove only eligible worktrees. |
| Scheduled check-in | Probe conflict first, then take one snapshot. Stay silent and re-arm only when the owning cadence permits it. |

## Review and metadata

- Use one reviewer seat from the roster. Ordinary non-Markdown candidates wait
  for green current-head `ci-required` unless the user approved parallel review
  or conflict recovery applies; Markdown-only candidates use the fast path.
- Start the reviewer briefing with the complete canonical prompt, then the
  self-contained candidate frame. Use an isolated read-only review worktree,
  and record round start and end times.
- Add `review-clean` only for a clean current head. Remove it before any event
  that spends that evidence; base movement alone does not.
- Label all-Markdown PRs `documentation`. Before merging user-observable work,
  add its PR link to the
  [0.27.0 release tracker](https://github.com/richlander/dotnet-inspect/issues/8151).
- Use operation-specific `gh api` endpoints for metadata, never whole-array
  replacement or `gh pr edit`. End authored GitHub comments with the required
  attribution footer.

## Waiting and merge

- CI that gates review, readiness, or a boundary uses the 60-minute bounded
  status wait in Round orchestration: one schedule, one snapshot per wake, and
  an explicit expiry report.
- Waiting on people uses the separate safety-net cadence: first check near 50
  minutes and later checks near four hours. New activity resets the quiet
  count. Stop after three quiet checks, merge or close, a user stop, or when
  the user has not written since publication and the first check finds nothing.
  Cancel the schedule and announce the stop once in one line.
- Never merge without explicit authorization for that exact head and base ref.
  Green CI, clean review, labels, readiness comments, and auto-merge state are
  not authorization.
- Bind every merge mutation to the reviewed head. A new round, restack,
  retarget, or head change expires authorization.

Do not amend, rewrite published history outside the stack rules, edit
release-managed central files without authorization, skip tests, push empty
commits, or include unrelated changes. When this skill and an owner disagree,
the owner wins.
