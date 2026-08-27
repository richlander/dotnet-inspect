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

Default to REST. Reach for GraphQL when its capability is worth a point.

The two draw on separate hourly limits, so spending one does not touch the
other. Checking is cheap: `rate_limit` does not consume the `core` or `graphql`
quota it reports, verified by three consecutive calls leaving both counters
unchanged. It is not unlimited — GitHub's secondary rate limits still apply — so
read it when you need it, not in a loop.

```bash
gh api rate_limit --jq '.resources|to_entries[]
  |select(.key=="core" or .key=="graphql")
  |"\(.key)\tused=\(.value.used)/\(.value.limit)\treset=\(.value.reset|todate)"'
```

```text
core     used=10/5000     reset=2026-08-21T22:55:53Z
graphql  used=335/5000    reset=2026-08-21T23:12:28Z
```

Read the reset, not just the remaining count. That sample looks like GraphQL has
plenty left, but it was taken three minutes into a fresh window. Concurrent
agents were burning roughly 77 points per minute, which projects to about 4,600
of the 5,000 before it resets — consistent with two earlier readings that caught
the same window late, at 4,077 and 4,287 consumed. REST core stayed at single
digits throughout.

So GraphQL is reliably contended and REST reliably is not, but a spot check
early in a window will tell you the opposite.

The cost models differ in the way that decides the rule. A REST call costs one
request whatever it returns, so a wide question costs a call per object. A
GraphQL query is priced by node count, but the floor dominates in practice: the
routine status query at 101 nodes and a deliberately wide one at 701 nodes — PR
fields, live base tip, 50 review threads with their comments, 50 reviews, 100
check contexts — both cost **1 point**.

GraphQL's value per point therefore rises with breadth, while REST's cost rises
with it. Spend a point when you are buying breadth:

- **Quick checks — REST.** Is this head mergeable, did `ci-required` pass. Two
  calls, from a bucket with thousands to spare.
- **Wide or graph-shaped reads — GraphQL.** The whole PR at one instant, review
  threads with their comments, anything needing the live base tip beside other
  fields. One point buys what would be five or ten REST calls.
- **Either bucket near exhaustion — use the other**, whatever the question.

### The REST pair

The default for a routine status check. Two calls, the second pinned to the sha
the first returned:

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

One request, one point, and one consistent snapshot. Prefer it when you need
breadth — the live base tip that carry-forward reads, review threads, or the
whole PR at a single instant — or when REST is the contended bucket.

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
| `ci-required` failed or was cancelled | Stop polling. Classify it and apply the applicable [recovery transition](../AGENTS.md#recovery-transitions). A settled red result is an answer, not something to wait out. |
| `CONFLICTING` | Apply the conflict transition in [Canonical round flow](../AGENTS.md#canonical-round-flow), then schedule a new five-minute check. |
| `MERGEABLE`, `ci-required` green at this head | **Done. Stop polling** and proceed to whatever waited on the answer. |
| `UNKNOWN`, CI green | Ask REST, which triggers the computation; see [resolving `UNKNOWN`](#resolving-unknown). |
| `UNKNOWN`, CI pending or missing | Follow up at 10 minutes plus jitter for documentation-only, or at the 35-minute mark otherwise. |
| `MERGEABLE`, documentation-only | Treat it as the expected CI completion check. If CI is unexpectedly pending, wait 10 minutes plus jitter. |
| `MERGEABLE`, not documentation-only | Expect CI at about 35 minutes from the push; schedule the next check about 30 minutes out. |

Read the table top-down: the first matching row wins. A failed or cancelled
check outranks every mergeability value, because `MERGEABLE` describes the merge
path and never means green. The green row is the exit: every other row schedules
another check, so polling stops only by reaching it or by leaving for a recovery
transition.

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
