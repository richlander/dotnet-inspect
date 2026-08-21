# Adversarial review mechanics

`AGENTS.md` owns the binding rules for adversarial review: what a candidate is,
when a round may start, what makes a review review-clean, and when to stop. This
document owns the mechanics those rules depend on — how to query PR status, how
to dispatch and reconcile a round, and the carry-forward procedure after clean
reviews.

Read [Adversarial review](../AGENTS.md#adversarial-review) first. This document
states no rules of its own: where it needs a condition, it cites the rule rather
than restating it, so the two cannot drift apart.

## Status discovery

Two questions — is the PR mergeable, and is it green. What varies is which API
you spend answering them. Which attempts must wait for the answer is the
eligibility table in [Canonical round flow](../AGENTS.md#canonical-round-flow).

### Two budgets, not one

REST and GraphQL have separate hourly limits, so spending one does not touch the
other. Check before choosing:

```bash
gh api rate_limit --jq '.resources|{graphql,core}'
```

They are not interchangeable in practice. Measured while writing this document:
GraphQL sat at 4,077 of 5,000 consumed and fell a further 55 points across 45
seconds in which this session issued no query at all, while REST core held
steady at 6 of 5,000. Concurrent agents share these buckets, and GraphQL is
routinely the contended one.

Spend the bucket that has budget. Neither transport is the default, and a
per-call cost comparison is the wrong measure — what matters is which bucket is
near exhaustion when you need an answer.

### The REST pair

Two calls, the second pinned to the sha the first returned:

```bash
gh api repos/{owner}/{repo}/pulls/{n} \
  --jq '{head:.head.sha,draft,mergeable,mergeable_state}'
gh api "repos/{owner}/{repo}/commits/{sha}/check-runs?per_page=100" \
  --jq '[.check_runs[]|select(.name=="ci-required")|{status,conclusion}]'
```

The pin is an advantage, not merely a second call: check state is read for an
explicit commit rather than for whatever GitHub considers the latest one. The PR
endpoint also triggers mergeability computation, which is why it resolves
`UNKNOWN` when GraphQL does not.

### The GraphQL query

One request, one point, and the only cheap way to read the live base tip that
the carry-forward decision needs. Prefer it when REST is the contended bucket,
when a single atomic answer matters, or when you need that base tip.

Return `headRefOid`, `baseRefOid`, `baseRef { target { oid } }`, `isDraft`,
`mergeable`, `mergeStateStatus`, `statusCheckRollup` state and contexts with
`pageInfo`, and the query's own `rateLimit` cost, remaining quota, and reset
time. Request enough contexts for the normal check matrix; if
`pageInfo.hasNextPage` is true and `ci-required` is absent, page before
concluding that it is missing.

### Reading either result

Confirm that the returned head is the pushed head, the PR is not a draft,
mergeability is positive, and the current head's `ci-required` completed with a
`SUCCESS` conclusion. Treat `mergeStateStatus` `BLOCKED` and `DRAFT` — or
`mergeable_state` `blocked` and `draft` — as independent readiness blockers:
clear the blocker before posting `Ready to merge`.

Every status check re-reads the head and compares it. A run or check identifier
is pinned to one commit and cannot detect a later push, so retain the expected
head SHA locally. Do not scatter discovery beyond the pair or the single query;
additional calls are for pagination and one-off details, after the head is
confirmed.

### Four traps in the result

- **Green CI does not imply mergeable.** The two are independent; a PR can
  report every check successful while GitHub reports `CONFLICTING`/`DIRTY`.
  Read mergeability from the mergeability fields, never inferred from checks.
- **`mergeStateStatus` is not check state.** It is a composite, and it reports
  `CLEAN` for a PR with no checks at all — so `CLEAN` alone never establishes
  that anything ran.
- **A missing `ci-required` is inconclusive**, not green: the aggregate may not
  have registered yet. No PR is green until its current-head `ci-required` has
  completed with a `SUCCESS` conclusion.
- **A skipped job is not evidence.** `COMPLETED`/`SKIPPED` does not block, but
  never cite it as validation. If a change should have triggered a job that
  skipped, the path filter is the bug.

### Resolving `UNKNOWN`

`UNKNOWN` means GitHub has not finished computing the merge, and it does not
satisfy the zero-conflict gate. It is a GraphQL answer; the REST PR endpoint
triggers the computation, so reaching for the REST pair often returns a definite
result while GraphQL still says `UNKNOWN`.

Accept it only when `head.sha` is the expected head. `mergeable: true` satisfies
the mergeability half of the gate; `mergeable: false` blocks. A null result is
still computing: yield five minutes with small random jitter, then ask again.
Continue that self-recovery until GitHub returns a definite result. Do not ask
the user to report CI or mergeability.

### Cadence

Status discovery must conserve the shared GitHub API budget. After every push,
schedule one status check for five minutes later; do not hold a synchronous shell
or agent turn open with `sleep`. That first check verifies the expected head and
detects conflicts early.

| First check says | Do this |
| --- | --- |
| `ci-required` failed or was cancelled | Stop polling and apply the matching [recovery transition](../AGENTS.md#recovery-transitions): a failure needing an author change supersedes the attempt, while a cancelled or evidenced-transient one keeps the head and re-runs the check. A settled red result is an answer, not something to wait out. |
| `CONFLICTING` | Apply the conflict transition in [Canonical round flow](../AGENTS.md#canonical-round-flow), then schedule a new five-minute check. |
| `UNKNOWN`, CI green | Ask REST, which triggers the computation; see [resolving `UNKNOWN`](#resolving-unknown). |
| `UNKNOWN`, CI pending or missing | Follow up at 10 minutes plus jitter for documentation-only, or at the 35-minute mark otherwise. |
| `MERGEABLE`, documentation-only | Treat it as the expected CI completion check. If CI is unexpectedly pending, wait 10 minutes plus jitter. |
| `MERGEABLE`, not documentation-only | Expect CI at about 35 minutes from the push; schedule the next check about 30 minutes out. |

Read the table top-down: the first matching row wins. A failed or cancelled
check outranks every mergeability value, because `MERGEABLE` describes the merge
path and never means green.

If both mergeability and CI remain unresolved, keep at least 10 minutes plus
small random jitter between status checks. Switch to the five-minute cadence
once CI is green and mergeability is the only unknown.

If the bucket you are spending is near exhaustion, switch to the other one; if
both are low, yield until the earlier reset rather than sleeping or continuing
to query. These intervals are minimums, not targets: wait longer when no
decision depends on an immediate result. Yield the session or schedule a delayed
wake-up between checks. Do not use `gh run watch`, `gh pr checks --watch`, or a
polling loop.

## Running a round

### Dispatch

Give each reviewer the same self-contained prompt: exact base and head, design
intent, relevant diff, concrete attack points, and required real-run evidence.

State plainly that reporting CLEAN is an acceptable outcome. A prompt that only
rewards findings will produce findings.

Isolate every reviewer in a separate linked review worktree under the primary
checkout's `.worktrees/` directory or an operating-system temporary directory;
never place it at the root of the user's home directory, and never detach the
primary checkout for review. Require scratch work under `/tmp/` and prohibit
`git reset`, `git add`, and commits in review trees.

Before acting on a blocking finding, reproduce it on a clean exact-head review
worktree. A reviewer's own probe can be vacuous — an added rule clause that
changes nothing for an already-entitled input looks green for the wrong reason —
so reproduce the finding and measure it before accepting its severity.

### Reconciliation

Reconcile the reviews publicly on the PR: attribute findings, state what was
verified or dismissed, and link resolution commits or explain explicit
non-actions. Address actionable findings only after the locked-head reviews
finish.

When a replacement candidate is required, say so on the PR and name the base tip
and merge commit, so the next review reads as a confirmation rather than an
unexplained second full pass.

### The round report

After every completed round and before starting the next one, emit this report as
the assistant's visible user-facing response in the terminal, filling every field
and choosing exactly one feedback classification. Do not emit it through a shell
command such as `printf`, leave it only in tool output, collapse it behind a
tool-call summary, or replace it with a shorter completion summary:

```text
Round <n> is complete for PR <number>.
- Review models <model-a> and <model-b> were used for adversarial review.
- Review feedback is: [converging, diverging, neutral, clean].
- Round start: <datetime>.
- Round end: <datetime>.
- Round duration: <hours:minutes>

Fix description: <prose description of changes made in response to the round>.
```

Use `Fix description` to state the concrete review-driven changes. For a `clean`
classification, say that no findings or fixes were produced and that the locked
head remained unchanged. For a no-fix round with dismissed findings, use
`converging`, `diverging`, or `neutral` and explain the dismissals in the public
reconciliation.

The same report may also be posted on the PR; the public reconciliation may
include more detail when the findings or fixes warrant it.

## Carry-forward after clean reviews

[Clean reviews are not spent by main
moving](../AGENTS.md#clean-reviews-are-not-spent-by-main-moving) states when this
path applies and when it does not. This is the procedure once it does.

1. **Detect movement.** Compare the candidate's recorded base tip with the live
   tip in `baseRef.target.oid`. `baseRefOid` is the base commit recorded for the
   PR, not the live branch tip.
2. **Inspect without integrating.** A non-mutating fetch is permitted solely to
   read the exact landed range.
3. **Analyze and ask.** Report which commits touch files this change touches,
   which behavior this change relies on that they alter, and any conflict a
   textual merge would resolve silently but wrongly. Say plainly when nothing in
   the range interacts — that is the common case and the most useful thing you
   can report.
4. **If non-interacting and the user approves**, integrate that exact analyzed
   tip by SHA, not a moving branch ref. Re-run the claimed validation and
   current-head CI, and carry the clean reviews forward without another round.
   If the live tip moves before integration, analyze the additional range and
   obtain renewed approval.
5. **If interacting**, report that carry-forward is unavailable and keep the
   reviewed head. Ask whether to make a workflow adjustment that integrates,
   re-validates, and re-reviews the replacement head, or to leave the PR
   blocked.

Record the reviewed head, the old and approved new tips, the non-interaction
analysis, and the user's decision on the PR.
