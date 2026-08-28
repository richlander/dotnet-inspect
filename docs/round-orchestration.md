# Round orchestration

`AGENTS.md` owns the binding rules for adversarial review: what a candidate is,
when a round may start, what makes a review review-clean, and when to stop. This
document owns the operational side — how to find out where the round stands, how
to dispatch and reconcile it, and what to do when the base moves under a clean
result.

Read [Adversarial review](../AGENTS.md#adversarial-review) first. This document
owns operational transitions and reporting, not the rules that decide
eligibility, recovery, completion, or carry-forward. Where it applies one of
those rules, it cites the owner rather than restating it.

## Status discovery

Two questions matter: is the PR mergeable, and is it green. The eligibility
table in [Canonical round flow](../AGENTS.md#canonical-round-flow) decides
which attempts must wait for those answers. This section owns when a status
snapshot runs and how its result changes round state.

### Obtain one snapshot

Follow [GitHub status queries](github-status-queries.md) for API selection,
request ordering, fixed-head checks, and response classification. This document
does not restate those mechanics. It consumes one classified snapshot and
applies the round transition below.

### Apply the result

Handle lifecycle, head mismatch, and conflict outcomes in the order defined by
[GitHub status queries](github-status-queries.md). Clear status predicates and
the retained schedule when the workflow leaves that wait. Preserve unrelated
members such as `review`.

A pending snapshot is not permission to poll. Keep its unresolved predicates
visible in `waiting` and end the run. This is a passive wait unless a schedule
is present: GitHub does not wake the agent when state changes. A later user or
workflow turn may query again or arm one new keyed run when status once again
gates an action.

### Schedule at most one delayed run

Do not arm a status check while independent work can continue. When status
actually gates the next action, publish `rec=wait`, the unresolved predicates
in `waiting`, and `goal=advance` for round progress or `goal=merge` for a
readiness statement or merge attempt.

Run immediately when a decision is already due. Otherwise schedule one check at
the expected completion time: normally 10 minutes after a documentation push,
35 minutes after another push, or five minutes later when current-head CI is
green and mergeability alone remains unresolved. These are timing defaults, not
a retry sequence. A rate-limited result instead uses the query classification's
retry-not-before time; a transient result may use its conservative retry
recommendation.

Key the schedule to its own ID plus the expected `head`, complete `waiting` set,
and `goal`. Cancel it when any key changes or the workflow leaves that wait. A
stale run stops itself, removes its retained ID only when that ID still points
to the stale run, and exits before querying GitHub. A current run stops itself
and clears its retained ID before obtaining one snapshot.

An immediate snapshot may arm the one delayed run when the result is pending,
rate-limited, or transient. A scheduled run never schedules another run. If its
result remains unresolved, it preserves the predicates and becomes a passive
wait. A later user or workflow turn may explicitly re-enter status discovery.
This structural bound replaces retry counters and prevents one-shot scheduling
from becoming polling under another name.

Leaving a status wait clears `schedule` and `goal`, removes the resolved status
predicates, and replaces `rec=wait` with the transition's current
recommendation. A pending result is the exception: it preserves `goal`,
`rec=wait`, and the unresolved predicates, while `schedule` remains empty.

Evaluate `waiting` as a set, not an exact string. Normalize new CI waits to
`check:ci-required`, remove only predicates the result resolves, and preserve
unrelated members such as `review`. In the table, **status members** means
`checks`, every `check:<name>`, and `merge`.

| Status snapshot says | Round transition |
| --- | --- |
| PR is merged | Leave the status wait, relinquish ownership, and end. |
| PR is closed or draft | Leave the status wait, publish the human action or stopped state, and end. |
| Head changed | Leave the status wait; route the returned head through candidate formation without inheriting fixed-head evidence. |
| `mergeable: false` | Leave the status wait; apply conflict recovery before considering CI. |
| `ci-required` completed without `success` | Leave the status wait; classify the result and apply the applicable recovery transition. |
| Green, conflict-free, `mergeable_state: "behind"`, `goal=merge` | Leave the status wait; apply carry-forward before reporting readiness or merging. |
| Green, conflict-free, `mergeable_state: "blocked"` | For `goal=advance`, leave the status wait and continue when no other predicate remains. For `goal=merge`, leave the status wait, publish `blocked=<pr-number> rec=wait`, and end. |
| Green and conflict-free at the expected head | Leave the status wait and continue when no other predicate remains. |
| CI or mergeability is pending or missing | Preserve the unresolved status members. An immediate snapshot may arm the one keyed delayed run; a scheduled snapshot clears `schedule` and becomes a passive wait. |
| Rate-limited or transient query failure | Preserve the unresolved status members and surface the failure. An immediate snapshot may arm the one keyed delayed run using the query classification's timing; a scheduled snapshot clears `schedule` and becomes a passive wait. |
| Terminal query failure | Leave the status wait with `rec=stop`, surface the failure, and end. |

Read the table top-down. Conflict recovery outranks CI, terminal non-green CI
outranks the remaining merge states, and carry-forward outranks a generic green
exit for a merge goal. Do not use `gh run watch`, `gh pr checks --watch`,
fixed-rate schedules, synchronous sleeps, or repeated ad hoc status turns.

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
- `wait` requires a non-empty `Blocked` or `Waiting` field. A retained
  `schedule` means the agent will check automatically; without one, the wait is
  passive and resumes only when a later user or workflow turn re-enters it.
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
