# Round orchestration

`AGENTS.md` owns the binding rules for adversarial review: what a candidate is,
when a round may start, what makes a review review-clean, and when to stop. This
document owns the operational side — how to find out where the round stands, how
to dispatch and reconcile it, and what to do when the base moves under a clean
result.

Read [Adversarial review](../AGENTS.md#adversarial-review) first. This document
owns procedure, never round state: it decides no question of eligibility,
recovery, completion, or carry-forward, and where it needs one of those
conditions it cites the rule rather than restating it, so the two cannot drift
apart. Its own imperatives — which API to spend, how to set up a reviewer, what
the report looks like — are the mechanics it exists to hold.

## Status discovery

Two questions — is the PR mergeable, and is it green. What varies is which API
you spend answering them. Which attempts must wait for the answer is the
eligibility table in [Canonical round flow](../AGENTS.md#canonical-round-flow).

### Which API to spend

Treat the shared GitHub API budget as scarce and normally contended. A status
read is justified only when its answer unlocks an eligibility, recovery,
completion, or merge decision. Do not check merely because a push happened or
because time passed; run independent local gates and eligible review work
first.

Use REST for every routine status snapshot. Do not call `rate_limit` before a
snapshot, use GraphQL as a fallback for exhausted REST, or spend one bucket to
discover whether the other has room. Those calls consume shared capacity
without advancing the PR. If REST is rate-limited, publish the gating predicate
as `waiting`, schedule one retry after `Retry-After` or the reported reset, and
yield. This includes primary and secondary limits and HTTP 429. If no retry time
is available, wait at least one hour.

For a transport failure or GitHub 5xx response, retain `attempt=<n>` and
schedule one successor after 10 minutes plus jitter, then 30 minutes, then one
hour for later consecutive failures. Remove `attempt` after a successful
snapshot. Every remaining non-success HTTP response or malformed response is
terminal, including 401, non-rate-limit 403, 404, and 422: clear `waiting`,
`schedule`, and `attempt`, publish an explicit error with `rec=stop`, surface
the concrete response, and end. Do not leave a wait that no scheduled run can
satisfy or schedule a success-shaped retry.

Use GraphQL only when the task genuinely requires graph-shaped data that the
REST pair cannot provide economically, such as review threads with their
comments or a single consistent snapshot of several related objects. Never use
GraphQL for ordinary CI or mergeability monitoring.

### The REST pair

The default for a routine status check. Set the PR number explicitly, capture
the first response, and pin the second call to the returned SHA:

```bash
pr_number=4822
pr_state=$(gh api "repos/{owner}/{repo}/pulls/$pr_number" \
  --jq '[.head.sha,.state,.merged,.draft,.mergeable,.mergeable_state]
    |map(if . == null then "null" else tostring end)|join("|")') \
  || exit
IFS='|' read -r head_sha state merged draft mergeable mergeable_state \
  <<< "$pr_state"

# Apply lifecycle, head, and mergeable:false transitions before this call.
gh api "repos/{owner}/{repo}/commits/$head_sha/check-runs?per_page=100" \
  --jq '[.check_runs[]|select(.name=="ci-required")|{status,conclusion}]'
```

`gh api` expands `{owner}` and `{repo}`; it does not expand arbitrary `{n}` or
`{sha}` placeholders. The explicit pin ensures check state is read for the
validated commit rather than for whatever GitHub considers latest. The PR
endpoint also triggers mergeability computation, which is why it resolves
`UNKNOWN` when GraphQL does not.

### The GraphQL query

One request can provide one consistent, graph-shaped snapshot. Reserve it for
work that needs that shape — the live base tip that carry-forward reads, review
threads, or the whole PR at a single instant — never as the routine status path
or a substitute when REST is rate-limited.

Return `headRefOid`, `baseRefOid`, `baseRef { target { oid } }`, `isDraft`,
`mergeable`, `mergeStateStatus`, `statusCheckRollup` state and contexts with
`pageInfo`, and the query's own `rateLimit` cost, remaining quota, and reset
time. Request enough contexts for the normal check matrix; if
`pageInfo.hasNextPage` is true and `ci-required` is absent, page before
concluding that it is missing.

### Reading either result

Confirm the readiness conditions in [before merge, the PR is mergeable and
green](../AGENTS.md#forming-a-candidate) against this result.

Every status check re-reads the head and compares it. A run or check identifier
is pinned to one commit and cannot detect a later push, so retain the expected
head SHA locally. Do not scatter discovery beyond the pair or the single query;
additional calls are for pagination and one-off details, after the head is
confirmed.

The PR request comes first. Handle lifecycle before mergeability or checks:

- **Merged:** clear the window's wait and schedule state, relinquish ownership
  of the PR, and end without another API call.
- **Closed but not merged:** clear `waiting`, `schedule`, and `attempt`, publish
  the closed state with `rec=stop`, and end without another API call.
- **Draft:** clear automated wait and schedule state. Publish
  `blocked=<pr-number> rec=wait` when human action is required, and end without
  another API call; do not monitor a draft on a cadence.

For an open, non-draft PR, compare its returned head with the expected head. If
they differ, do not make the check-runs request. Clear the old wait and route
the returned head through candidate formation or the applicable recovery
transition; it never inherits the old head's schedule.

After the head matches, short-circuit `mergeable: false` into conflict recovery
without making the check-runs request. CI cannot change that transition, so the
second call would spend shared capacity without advancing the PR.

### Four traps in the result

- **Green CI does not imply mergeable.** The two are independent; a PR can
  report every check successful while GitHub reports `CONFLICTING`/`DIRTY`.
  Read mergeability from the mergeability fields, never inferred from checks.
- **`mergeStateStatus` is not check state.** It is a composite, and it reports
  `CLEAN` for a PR with no checks at all — so `CLEAN` alone never establishes
  that anything ran.
- **A missing `ci-required` is inconclusive**, not green: the aggregate may not
  have registered yet. No PR is green until its current-head `ci-required` has
  completed with REST conclusion `success` (GraphQL `SUCCESS`).
- **A skipped leaf job is not evidence.** `COMPLETED`/`SKIPPED` does not block,
  but never cite it as validation. The aggregate `ci-required` still must
  conclude `success`. If a change should have triggered a job that skipped, the
  path filter is the bug.

### Resolving unknown mergeability

GraphQL `UNKNOWN` and REST `mergeable: null` mean GitHub has not finished
computing the merge; neither satisfies the zero-conflict gate. The REST PR
endpoint triggers the computation, so a later REST snapshot often returns a
definite result.

Accept a snapshot only when its returned head is the expected head.
`mergeable: true` satisfies the mergeability half of the gate;
`mergeable: false` blocks. A null result is still computing: schedule one run
for five minutes later with small random jitter, then end the current run.
Continue that one-shot self-recovery until GitHub returns a definite result. Do
not ask the user to report CI or mergeability.

### Cadence

Status discovery must conserve the shared GitHub API budget. Do not arm a check
while independent work can continue. Once CI or mergeability actually gates the
next action, publish `waiting=checks` or `waiting=merge` with `rec=wait`, then
schedule exactly one status run for the next useful time: 10 minutes after the
push for documentation-only changes, 35 minutes after the push otherwise, or
five minutes later when CI is already green and mergeability alone is
unresolved. Run immediately if that target time has already passed. The future
turn is intentional; do not reject scheduling because it consumes one, and do
not wait for the user to volunteer status.

Retain the active schedule ID beside its expected head and waiting predicate.
Cancel it immediately when the predicate clears, the head is superseded, or
the workflow otherwise leaves that wait — including when the user supplies a
trusted "CI is ready" result. A replacement head never inherits the old
schedule.

The run must be one-shot. If the scheduler only creates recurring schedules,
its prompt first compares its own ID, expected head, and waiting predicate with
the retained state. A stale run stops its schedule and exits without an API
call. A current run stops its schedule and clears the retained ID before its
first API call. It then performs one REST snapshot, acts on a terminal result,
or creates exactly one replacement schedule at the next cadence below. Never
leave a fixed-rate schedule active, hold a synchronous shell or agent turn open
with `sleep`, or make extra status calls in the same run.

| Status run says | Do this |
| --- | --- |
| `mergeable: false` | Apply the conflict transition in [Canonical round flow](../AGENTS.md#canonical-round-flow). After pushing the resolution head, use the standard initial 10- or 35-minute cadence when status gates progress again. |
| `ci-required` completed with a conclusion other than `success` | Classify it and apply the applicable [recovery transition](../AGENTS.md#recovery-transitions). A terminal non-green result is an answer, not something to wait out. |
| `mergeable: true`, `ci-required` green at this head | **Done.** Clear the waiting state and proceed to whatever waited on the answer. |
| `mergeable: null`, CI green | Schedule one REST snapshot for five minutes later; see [resolving unknown mergeability](#resolving-unknown-mergeability). |
| `mergeable: null`, CI pending or missing | Schedule one successor 10 minutes plus jitter after this snapshot for documentation-only changes, or 30 minutes after this snapshot otherwise. |
| `mergeable: true`, documentation-only | Treat it as the expected CI completion check. If CI is unexpectedly pending, schedule one successor 10 minutes plus jitter after this snapshot. |
| `mergeable: true`, not documentation-only | Schedule one successor about 30 minutes after this snapshot. |

Read the table top-down: the first matching row wins. Conflict recovery has
first priority, including when CI is also terminal non-green. A terminal
non-green `ci-required` outranks the remaining mergeability values because
`mergeable: true` describes the merge path and never means green. The green row
is the exit; every pending row creates one later run, not a polling loop.

Every successor delay is relative to the snapshot that just completed, never
to the original push. If both mergeability and CI remain unresolved, keep at
least 10 minutes plus small random jitter between status checks. Switch to the
five-minute cadence once CI is green and mergeability is the only unknown.

These intervals are minimums, not targets: wait longer when no decision depends
on an immediate result. A pending result always ends the current run after its
single replacement has been scheduled. A REST request failure follows the
backoff or explicit-error rule above; it never silently leaves `waiting` set
without a successor. Do not use `gh run watch`, `gh pr checks --watch`,
repeated ad hoc turns, or any polling loop.

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
non-actions.

When a replacement candidate is required, say so on the PR and name the base tip
and merge commit, so the next review reads as a confirmation rather than an
unexplained second full pass.

### The round report

After every [completed round](../AGENTS.md#canonical-round-flow) and before
starting the next one, emit this report as the assistant's visible user-facing
response in the terminal, filling every field and choosing exactly one feedback
classification. Do not emit it through a shell command such as `printf`, leave
it only in tool output, collapse it behind a tool-call summary, or replace it
with a shorter completion summary. Do not put it in an interactive approval
prompt:

```text
Round <n> is complete for PR <number>.
- Review models <model-a> and <model-b> were used for adversarial review.
- Review feedback is: [converging, diverging, neutral, clean].
- Round start: <datetime>.
- Round end: <datetime>.
- Round duration: <hours:minutes>

Reviews: <clean>/<required> clean — <status by reviewer>
Blocked: <PR or issue numbers not yours to fix; omit when empty>
Recommendation: [continue, wait, merge, approve next rounds, stop (reason)]

Fix description: <prose description of changes made in response to the round>.
```

Use `Fix description` to state the concrete review-driven changes.
Classification must match the reviewer outcomes:

- If every required reviewer returned no findings and the locked head remained
  unchanged, use `clean`. Do not use `converging` as a generic positive label.
- If any reviewer returned a finding, use `converging`, `diverging`, or
  `neutral`, even when every finding was dismissed and the head stayed
  unchanged. Explain dismissals in the public reconciliation.

`Reviews` records the dual-clean count that GitHub cannot observe. Every
`Blocked` entry must be an existing PR or issue; file one before citing a new
shared failure.

- `continue` means the next round is inside the current authorized six-round
  block. Emit the report, then immediately begin the next candidate cycle. Do
  not ask, set `HELP`, or wait for user input.
- `wait` requires a non-empty blocker list and means the agent will resume when
  it clears.
- `approve next rounds` is valid only after rounds 6, 12, 18, and so on, after
  the required architectural checkpoint. Never use it for an earlier round in
  the current block.
- `merge`, `approve next rounds`, and `stop` request a user decision; `stop`
  does not close anything until approved.

When the recommendation needs approval, render the complete report first as
normal session output. Then open a separate prompt containing only the concise
decision question and answer labels. Do not repeat the report or its evidence
inside the prompt.

Before emitting the report or opening its approval prompt, synchronize the PR's
`review-clean` label with
[Keep the review-clean label current](../AGENTS.md#keep-the-review-clean-label-current).
The label describes the state the report records; it must not lag behind it.

The same report may also be posted on the PR; the public reconciliation may
include more detail when the findings or fixes warrant it.

## Carry-forward after clean reviews

[Clean reviews are not spent by main
moving](../AGENTS.md#clean-reviews-are-not-spent-by-main-moving) states when
this path applies and how each landed-range classification resolves. This is
the procedure once it does.

1. **Detect movement.** Compare the candidate's recorded base tip with the live
   tip in `baseRef.target.oid`. `baseRefOid` is the base commit recorded for the
   PR, not the live branch tip.
2. **Inspect without integrating.** A non-mutating fetch is permitted solely to
   read the exact landed range.
3. **Classify and report.** As normal session output, report which commits
   touch files this change touches, which relied-on behavior they alter, and
   any conflict a textual merge would resolve silently but wrongly. State the
   classification plainly: no interaction, significant interaction, or
   conflict.
4. **Act on the classification — no approval prompt.**
   - *No interaction:* keep `review-clean`, integrate the exact analyzed tip by
     SHA (not a moving branch ref), and update the recorded head SHA. Skip
     re-running validation, CI, and review. Merging itself still needs a live
     readiness check and explicit user authorization; base movement alone does
     not grant either.
   - *Significant interaction, no conflict:* remove `review-clean`, integrate
     the tip, re-run the claimed validation and current-head CI, and
     re-dispatch the required reviewers at the new head as a normal round.
   - *Conflict:* remove `review-clean`, resolve it as an author change under
     [conflict recovery](../AGENTS.md#recovery-transitions), and re-dispatch the
     required reviewers at the new head.

For a no-interaction carry-forward, record the reviewed head, the old and
integrated tips, and the non-interaction analysis on the PR. For the other two
outcomes, record the classification and the action taken, and produce the
resulting round's normal [round report](#the-round-report).
