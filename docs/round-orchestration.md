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
without advancing the PR. The routine commands include response headers. Treat
HTTP 429 as rate-limited. Treat HTTP 403 as rate-limited only when
`Retry-After` is present, `x-ratelimit-remaining` is zero, or the response body
explicitly identifies a secondary rate limit; an unclassified 403 is terminal.
Publish the gating predicate as `waiting`, then schedule one successor after
`Retry-After` when present. Use `x-ratelimit-reset` only when
`x-ratelimit-remaining` is zero. An identified secondary limit with remaining
quota and no `Retry-After` uses increasing delays of 1, 5, and 15 minutes
instead of the routine reset header.

For a transport failure or GitHub 5xx response, schedule successors after 10
minutes plus jitter, then 30 minutes, then one hour. Every remaining non-success
HTTP response or malformed response is terminal, including 401,
non-rate-limit 403, 404, and 422: remove every status predicate from `waiting`,
clear `schedule`, `attempt`, and `attempt-for`, preserve unrelated wait
members, publish an explicit error with `rec=stop`, surface the concrete
response, and end. Do not leave a wait that no scheduled run can satisfy or
schedule a success-shaped retry.

Every retryable result participates in the same bound. Set `attempt=1` and
`attempt-for=<predicate>` when arming the first automated successor for an
unresolved status component. Prefer the CI component while CI and mergeability
are both unresolved; use `merge` once CI is green. Increment `attempt` while
that component, head, and goal remain unchanged. If the run with `attempt=3`
is still rate-limited, transient, pending, missing, or reports null
mergeability, remove every status predicate from `waiting`, clear `schedule`,
`attempt`, and `attempt-for`, publish `blocked=<pr-number> rec=wait` with a
`HELP` reason describing the unresolved status, and stop without a successor.

Reset the counter when `head` or `goal` changes, or when `attempt-for` clears or
is replaced. Do not reset it merely because an unrelated member of a composite
`waiting` value clears. A finite retry budget is mandatory; one-shot mechanics
must not recreate an unbounded polling loop.

Use GraphQL only when the task genuinely requires graph-shaped data that the
REST pair cannot provide economically, such as review threads with their
comments or a single consistent snapshot of several related objects. Never use
GraphQL for ordinary CI or mergeability monitoring.

### The REST pair

The default for a routine status check. Run the two requests as separate agent
tool calls, not one compound shell command. First set the PR number explicitly:

```bash
pr_number=4822
gh api "repos/{owner}/{repo}/pulls/$pr_number" \
  --include \
  --jq '{head:.head.sha,state,merged,draft,mergeable,mergeable_state}'
```

If that tool call fails, apply the rate-limit, transient, or terminal rule
above before doing anything else. If it succeeds, apply the lifecycle, head,
and `mergeable: false` transitions below. Treat `waiting` as a set. When it
contains `checks` or `check:ci-required`, copy the validated 40-character head
SHA into this separate tool call. A run whose only status predicate is `merge`
skips the second request while mergeability remains null, but uses it once
mergeability becomes definite and the resulting transition depends on green
CI:

```bash
head_sha="replace-with-validated-head-sha"
gh api "repos/{owner}/{repo}/commits/$head_sha/check-runs?per_page=100" \
  --include \
  --jq '[.check_runs[]|select(.name=="ci-required")|{status,conclusion}]'
```

Apply the same failure rules if the second tool call fails. Because the PR
request succeeded but check state remains unknown, ensure
`check:ci-required` remains in `waiting` before arming any transient or
rate-limit successor; preserve other unresolved members and retain the existing
`goal`.

A standalone `waiting=merge` records that `ci-required` was already confirmed
green for the expected head, whether by the preceding snapshot or a trusted
user statement. After the PR request validates that same head, retain that
evidence and do not repeat the check-runs request while mergeability remains
null. Once mergeability becomes definite, re-read `ci-required` before round
progress, readiness, or merge because a workflow rerun can change check state
without changing the head. A returned-head mismatch invalidates the evidence.

`--include` exposes the HTTP status and the `Retry-After`,
`x-ratelimit-remaining`, and `x-ratelimit-reset` headers without another API
request; `--jq` still selects the response body. Classify a failed call from
that status, those headers, and the surfaced body before applying a retry or
terminal transition.

`gh api` expands `{owner}` and `{repo}`; it does not expand arbitrary `{n}` or
`{sha}` placeholders. The explicit pin ensures check state is read for the
validated commit rather than for whatever GitHub considers latest. The PR
endpoint also triggers mergeability computation, which is why it resolves
`UNKNOWN` when GraphQL does not.

### The GraphQL query

One request can provide one consistent, graph-shaped snapshot. Reserve it for
work that needs that shape — review threads or the whole PR at a single
instant — never as the routine status path, a live-base lookup that `git fetch`
already provides, or a substitute when REST is rate-limited.

Return `state`, `merged`, `headRefOid`, `baseRefOid`,
`baseRef { target { oid } }`, `isDraft`, `mergeable`, `mergeStateStatus`,
`statusCheckRollup` state and contexts with `pageInfo`, and the query's own
`rateLimit` cost, remaining quota, and reset time. Request enough contexts for
the normal check matrix; if `pageInfo.hasNextPage` is true and `ci-required` is
absent, page before concluding that it is missing.

### Reading either result

Confirm the readiness conditions in [before merge, the PR is mergeable and
green](../AGENTS.md#forming-a-candidate) against this result.

Every status check re-reads the head and compares it. A run or check identifier
is pinned to one commit and cannot detect a later push, so retain the expected
head SHA locally. Do not scatter discovery beyond the pair or the single query;
additional calls are for pagination and one-off details, after the head is
confirmed.

The PR request comes first. Handle lifecycle before mergeability or checks:

- **Merged:** clear the window's wait, schedule, `attempt`, and `attempt-for`
  state, relinquish ownership of the PR, and end without another API call.
- **Closed but not merged:** clear `waiting`, `schedule`, `attempt`, and
  `attempt-for`, publish the closed state with `rec=stop`, and end without
  another API call.
- **Draft:** clear automated wait, schedule, `attempt`, and `attempt-for` state.
  Publish `blocked=<pr-number> rec=wait` when human action is required, and end
  without another API call; do not monitor a draft on a cadence.

For an open, non-draft PR, compare its returned head with the expected head. If
they differ, do not make the check-runs request. Clear the old wait and route
the returned head through candidate formation or the applicable recovery
transition; it never inherits the old head's schedule, attempt, or
`attempt-for`.

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
`mergeable: false` blocks. A null result is still computing: schedule
successors after 5, 15, and 30 minutes with small random jitter, subject to the
three-successor bound, and end each current run. Do not ask the user to report
CI or mergeability while automated resolution remains available.

### Cadence

Status discovery must conserve the shared GitHub API budget. Do not arm a check
while independent work can continue. Once CI or mergeability actually gates the
next action, publish the unresolved status members in `waiting`: use
`check:ci-required`, `merge`, or both. Preserve unrelated members such as
`review`. Publish `rec=wait` and `goal=advance` for round progress or
`goal=merge` for a readiness statement or merge attempt, even when the status
run will be immediate.

When a delay is useful, schedule exactly one status run: 10 minutes after the
push for documentation-only changes, 35 minutes after the push otherwise, or
five minutes later when CI is already green and mergeability alone is
unresolved. Run immediately if that target time has already passed. The future
turn is intentional; do not reject scheduling because it consumes one, and do
not wait for the user to volunteer status. Publish standalone `waiting=merge`
only when current-head CI is already green; its successor spends only the PR
request while mergeability remains null, then revalidates CI once before a
green-dependent exit.

Retain the active schedule ID beside its expected head, waiting set, goal,
attempt, and `attempt-for` when present. Cancel it immediately when its keyed
state changes or the workflow otherwise leaves that wait — including when the
user supplies a trusted "CI is ready" result. A replacement head never inherits
the old schedule.

The run must be one-shot. If the scheduler only creates recurring schedules,
its prompt first compares its own ID, expected head, waiting set, goal, attempt,
and `attempt-for` when present with the retained state. A stale run stops its
schedule and exits without an API call. If its ID still equals the retained
`schedule` value, it also removes that dead pointer; if a different schedule ID
is retained, it leaves the current schedule untouched. A current run stops its
schedule and clears the retained ID before its first API call. It then performs
one REST snapshot, acts on a terminal result, or publishes the resulting
predicate set and creates exactly one replacement schedule at the next cadence
below. Never leave a fixed-rate schedule active, hold a synchronous shell or
agent turn open with `sleep`, or make extra status calls in the same run.

Each status run evaluates membership in `waiting`, not exact string equality.
`checks` and `check:ci-required` both select the CI component; normalize new
routine status state to `check:ci-required`. Remove only components the result
resolves, and preserve unrelated members. In the table, **status members**
means `checks`, every `check:<name>`, and `merge`. The CI component determines
cadence while both CI and mergeability are unresolved.

| Status run says | Do this |
| --- | --- |
| `mergeable: false` | Remove all status members from `waiting`, clear `schedule`, `attempt`, and `attempt-for`, and apply the conflict transition in [Canonical round flow](../AGENTS.md#canonical-round-flow). Preserve unrelated wait members. After pushing the resolution head, use the standard initial 10- or 35-minute cadence when status gates progress again. |
| `ci-required` completed with a conclusion other than `success` | Remove all status members from `waiting`, clear `schedule`, `attempt`, and `attempt-for`, classify the result, and apply the applicable [recovery transition](../AGENTS.md#recovery-transitions). Preserve unrelated wait members. A terminal non-green result is an answer, not something to wait out. |
| `mergeable: true`, `ci-required` green, REST `mergeable_state: "behind"`, `goal=merge` | Remove the status members from `waiting`, clear `schedule`, `attempt`, and `attempt-for`, then apply [carry-forward after clean reviews](#carry-forward-after-clean-reviews). Preserve unrelated wait members. Do not report readiness or merge from this snapshot. |
| `mergeable: true`, `ci-required` green, REST `mergeable_state: "blocked"` | For `goal=advance`, remove the status members from `waiting`, clear `attempt` and `attempt-for`, then continue round completion or reviewer dispatch when no other predicate remains. For `goal=merge`, remove the status members, clear `schedule`, `attempt`, and `attempt-for`, publish `blocked=<pr-number> rec=wait`, and end without a successor. Preserve unrelated wait members. |
| `mergeable: true`, `ci-required` green at this head | **Done.** Remove the status members from `waiting`, clear `attempt` and `attempt-for`, then proceed when no other predicate remains. |
| `mergeable: null`, CI green | Remove the CI member, ensure `merge` remains in `waiting`, retain `goal`, set `attempt-for=merge`, and use the 5-, 15-, then 30-minute mergeability sequence within that component's successor bound; see [resolving unknown mergeability](#resolving-unknown-mergeability). |
| `mergeable: null`, CI pending or missing | Ensure `check:ci-required,merge` are in `waiting`, retain `goal`, set `attempt-for=check:ci-required`, and use the pending-CI sequence within that component's successor bound. |
| `mergeable: true`, documentation-only | Remove `merge` from `waiting`. If CI is pending or missing, ensure `check:ci-required` remains, retain `goal`, set `attempt-for=check:ci-required`, and use the documentation pending-CI sequence within that component's successor bound. |
| `mergeable: true`, not documentation-only | Remove `merge` from `waiting`. If CI is pending or missing, ensure `check:ci-required` remains, retain `goal`, set `attempt-for=check:ci-required`, and use the non-documentation pending-CI sequence within that component's successor bound. |

Read the table top-down: the first matching row wins. Conflict recovery has
first priority, including when CI is also terminal non-green. A terminal
non-green `ci-required` outranks the remaining mergeability values because
`mergeable: true` describes the merge path and never means green. A `behind`
merge goal enters carry-forward before the generic green exit because the
effective base is part of the candidate. The green row is the exit; every
pending row creates one later run only while its retry budget remains.

Every successor delay is relative to the snapshot that just completed, never
to the original push. Use 10, 20, and 30 minutes plus small random jitter for
documentation pending-CI successors, and 30, 45, and 60 minutes for
non-documentation pending-CI successors. If both mergeability and CI remain
unresolved, use the applicable CI sequence. Switch to the 5-, 15-, and
30-minute sequence once CI is green and mergeability is the only unknown.

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

### Wording the prompt

Write the prompt as a description of the property under test. A prompt written
as an attack brief can trip a model's content filter, and **that failure is
silent**: the reviewer returns an empty or near-empty response with a clean
worktree, which is indistinguishable from a broken model or a stalled harness.

This has happened here, and it cost most of a day. A seat returned empty
several times across two heads and was nearly reported to the user as a
non-functional model in need of repinning. A reviewer from a different family
then failed with an explicit message naming content filtering as the cause,
which is the only reason the real explanation surfaced at all. The prompt had
been written as a catalog of concrete strings to try against a gate that rejects
markup able to run unreviewed code. Rewording it -- same surfaces, same required
evidence, same rigor -- produced full reports from both seats on the first
attempt.

The reviewer was refusing the prompt, not the work. Keep prompts in terms of the
property:

- **Say what the property actually is.** If the gate enforces static-analysis
  coverage, say that, and say the concern is unreviewed code rather than
  attackers. Do not dress a correctness property as a security one for
  emphasis.
- **Name constructs structurally rather than quoting them.** Describing an
  attribute by what a parser does with its contents asks for the same probe as a
  literal string and reads as a specification. This is the load-bearing rule:
  quoted strings are what draws the refusal.
- **Use an inert marker in the required evidence.** Assigning a uniquely named
  global proves a construct reached the output exactly as well as anything
  active does, and it is also easier to grep for.
- **Describe already-closed cases by name, not by spelling**, when listing the
  floor a reviewer should push past.

Apply the same discipline to notes, issues and documentation, including this
page. A write-up that reproduces the strings in order to explain them becomes
the hazard it is describing, for every agent that later reads it. Say what the
construct was; do not reproduce it. For the same reason, describe an incident by
what it teaches rather than by pointing at the pull request where it happened,
so that following the reference is not itself the way to load the problem
material.

None of this softens the review. "Adversarial" describes the rigor, not a
simulated attacker, and a reviewer that understands the invariant will find more
than one handed a list of strings to retry.

When a reviewer returns empty or near-empty, suspect the prompt before the
model. Check the worktree for artifacts, then re-dispatch the same work to a
model from a different family: filters differ, and one may state the reason
where another fails silently. Do not propose repinning a roster seat on
empty-response evidence alone.

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
Waiting: <comma-separated tool-evaluable predicates; omit when empty>
Recommendation: [continue, wait, merge, split into focused successors,
approve next rounds, stop (reason)]

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
shared failure. `Waiting` records one or more comma-separated predicates tooling
can evaluate, such as `check:<name>`, `checks`, `merge`, or `review`; it is not
a blocker, and it clears only when every listed predicate clears.

- `continue` means the next round is inside the current authorized six-round
  block. Emit the report, then immediately begin the next candidate cycle. Do
  not ask, set `HELP`, or wait for user input.
- `wait` requires a non-empty `Blocked` or `Waiting` field and means the agent
  will resume when it clears.
- When a completed documentation-only round is review-clean and no further
  author or review round is needed, but `ci-required` remains pending or
  missing, use `Waiting: check:ci-required` and `Recommendation: wait`. Use
  `Waiting: check:ci-required,merge` when live mergeability is also unresolved.
  An intermediate or fix-producing round reports `continue` without waiting for
  CI only when the next round remains inside the current authorized block. At a
  six-round boundary, use the applicable approval, split, or stop recommendation
  without waiting for CI.
- `split into focused successors` is valid at round 12 and later six-round
  boundaries after the required checkpoint. It requests the user's split
  decision and follows the transition in
  [Stop after six rounds](../AGENTS.md#stop-after-six-rounds).
- `approve next rounds` is valid only after rounds 6, 12, 18, and so on, after
  the required architectural checkpoint. Never use it for an earlier round in
  the current block.
- `merge`, `split into focused successors`, `approve next rounds`, and `stop`
  request a user decision; `stop` does not close anything until approved.

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

1. **Detect movement without API spend.** Fetch the effective base
   non-mutating, resolve its remote-tracking ref to an exact SHA, and compare
   that SHA with the candidate's recorded base tip. Do not spend GraphQL solely
   to read the live base tip. If a graph-shaped query is already justified,
   `baseRef.target.oid` provides the same live value; `baseRefOid` is only the
   base commit recorded for the PR.
2. **Inspect without integrating.** Read the exact landed range between the
   recorded and fetched tips.
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
