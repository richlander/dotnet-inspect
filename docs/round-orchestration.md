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
[GitHub status queries](github-status-queries.md). Clear status predicates,
`schedule`, `status-deadline`, and `goal` when the workflow leaves that wait.
Preserve unrelated members such as `review`.

Evaluate `waiting` as a set, not an exact string. Normalize new CI waits to
`check:ci-required`, remove only predicates the result resolves, and preserve
unrelated members such as `review`. In the table, **status members** means
`checks`, every `check:<name>`, and `merge`.

| Status snapshot says | Round transition |
| --- | --- |
| PR is merged | Leave the status wait, relinquish ownership, and end. |
| PR is closed or draft | Leave the status wait, publish the human action or stopped state, and end. |
| Head changed | Leave the status wait; route the returned head through candidate formation without inheriting fixed-head evidence. |
| REST `mergeable: false` or GraphQL `mergeable: CONFLICTING` | Leave the status wait; apply conflict recovery before considering CI. |
| `ci-required` completed without `success` | Leave the status wait; classify the result and apply the applicable recovery transition. |
| GraphQL `mergeStateStatus: BLOCKED`, `goal=merge` | Leave the status wait, publish `blocked=<pr-number> rec=wait`, and end. |
| Green `ci-required` and positive mergeability at the expected head | Leave the status wait and continue when no other predicate remains. |
| CI or mergeability is pending or missing | Preserve the unresolved status members and apply the round cadence below. |
| Rate-limited or transient query failure | Record the concrete failure and retry-not-before time, preserve the unresolved status members, and apply the round cadence below. |
| Terminal query failure | Leave the status wait with `rec=stop`, surface the failure, and end. |

Read the table top-down. Conflict recovery outranks CI, terminal non-green CI
outranks the remaining merge states, and a documented GraphQL block prevents a
merge goal. Carry-forward remains a separate pre-merge obligation driven by the
fetched base tip, not by undocumented REST `mergeable_state` values.

### Bounded status waiting

*This section defines repository policy, not GitHub timing guarantees.*

Every round attempts one current-head snapshot. At an ordinary round, a
pending, rate-limited, or transient result is recorded and the next round
continues. A known conflict, non-green `ci-required`, or terminal query failure
still takes its transition.

Every third round spends up to a 60-minute status budget before it may advance;
a merge or readiness goal may use the same bound. Every sixth round uses that
budget, but fresh green current-head `ci-required` and positive mergeability
remain prerequisites for the next-block approval prompt. Measure the budget
from the first scheduled wait and publish `status-deadline=<UTC>`.

Arm at most one schedule at a time. Key it to its own ID plus the expected
`head`, complete `waiting` set, `goal`, and deadline. A stale run stops itself
and exits before querying GitHub. A current run stops itself, clears the
retained ID, obtains one snapshot, and may arm one successor only when the
deadline still permits it.

For rate limits, never schedule before the query classification's
retry-not-before time. GitHub documents `Retry-After` as authoritative,
`x-ratelimit-reset` when the primary remaining count is zero, and at least a
one-minute exponentially increasing delay for a secondary limit without
either header. For pending or transient status without an authoritative time,
choose a conservative delay and never schedule beyond the deadline. Do not use
`gh run watch`, `gh pr checks --watch`, fixed-rate schedules, synchronous
sleeps, or concurrent status requests.

When the budget expires with status unresolved, clear `schedule`, keep the
unresolved predicates, publish the report below, set `rec=stop`, and end. This
is an informational stop: it ends observation only and neither closes nor
abandons the PR.

### Status budget report

Emit this report as visible session output, never inside an approval prompt:

```text
Status not observed for PR <number> at round <n> after <mm> minutes.
- Head: <40-character SHA>
- Unresolved: <waiting predicates>
- Last observation: ci-required=<state|not-observed>,
  mergeable=<true|false|null|not-observed> at <datetime>.
- Cause: <rate-limit evidence, transient failure, or still running/queued>.
- Snapshots: <count>, last at <datetime>.
- This is not a CI result. No failing check was observed. GitHub documents
  hosted-job execution limits up to 6 hours and self-hosted queue limits up to
  24 hours, so this repository's 60-minute budget can expire first.
- Effect: <next round not started | boundary approval withheld>.
- Next: <what a later user or workflow turn should re-check>.
Recommendation: stop (status budget exhausted); nothing is closed or abandoned.
```

Never describe an unobserved result as failure, red, or blocked. Cite the
observed HTTP status and rate-limit headers rather than guessing the cause. At
a six-round boundary, withhold the approval prompt until a later current-head
snapshot satisfies the prerequisite. The duration context comes from
[GitHub Actions limits](https://docs.github.com/en/actions/reference/limits).

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
  CI only when the next round remains inside the current authorized block and
  the status cadence permits it. At a six-round boundary, fresh green
  current-head `ci-required` and positive mergeability are prerequisites for
  an `approve next rounds` recommendation. If the status budget expires first,
  publish the status budget report and withhold that approval prompt; the
  checkpoint may still recommend split or judgment-stop on its own evidence.
- `split into focused successors` is valid at round 12 and later six-round
  boundaries after the required checkpoint. It requests the user's split
  decision and follows the transition in
  [Stop after six rounds](../AGENTS.md#stop-after-six-rounds).
- `approve next rounds` is valid only after rounds 6, 12, 18, and so on, after
  the required architectural checkpoint. Never use it for an earlier round in
  the current block.
- `merge`, `split into focused successors`, `approve next rounds`, and a
  judgment `stop` request a user decision; judgment `stop` does not close
  anything until approved. `stop (status budget exhausted)` is informational:
  it requests no decision and leaves the PR and round state unchanged.

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
this path applies and how each landed-range classification resolves. It applies
both to a review-clean head and to a head with a pending or approved
trivial-interaction waiver. A base tip beyond the one recorded for a waiver
expires that waiver before classification. This is the procedure once the path
applies.

1. **Detect movement without API spend.** Fetch the effective base
   non-mutating, resolve its remote-tracking ref to an exact SHA, and compare
   that SHA with the candidate's recorded base tip. Do not spend GraphQL solely
   to read the live base tip. If a graph-shaped query is already justified,
   the documented `baseRef.target.oid` identifies the object currently pointed
   to by the base ref. Do not rely on undocumented assumptions about
   `baseRefOid` freshness.
2. **Inspect without integrating.** Read the exact landed range between the
   recorded and fetched tips.
3. **Classify and report.** As normal session output, report which commits
   touch files this change touches, which relied-on behavior they alter, and
   any conflict a textual merge would resolve silently but wrongly. State the
   classification plainly: no interaction, trivial interaction, significant
   interaction, or conflict requiring semantic resolution.
4. **Act on the classification.**
   - *No interaction:* keep `review-clean`, integrate the exact analyzed tip by
     SHA (not a moving branch ref), and update the recorded head SHA when
     entering from a review-clean head; only that path skips re-running
     validation, CI, and review. When entering from a pending or approved
     waiver head, leave `review-clean` absent, integrate the exact tip, and
     follow the waiver procedure below for the new head and base, including its
     current-head gates, before dispatching review. Merging itself still needs
     a live readiness check and explicit user authorization; base movement
     alone does not grant either.
   - *Trivial interaction:* remove `review-clean`, integrate the exact analyzed
     tip, resolve every overlap mechanically as classified, run affected
     focused gates, and push. Follow the waiver procedure below before
     dispatching replacement reviewers.
   - *Significant interaction, no conflict:* remove `review-clean`, integrate
     the tip, re-run the claimed validation and current-head CI, and
     re-dispatch the required reviewers at the new head as a normal round.
   - *Conflict requiring semantic resolution:* remove `review-clean`, resolve
     it as an author change under
     [conflict recovery](../AGENTS.md#recovery-transitions), and re-dispatch
     the required reviewers at the new head.

For a no-interaction carry-forward, record the reviewed head, the old and
integrated tips, and the non-interaction analysis on the PR. For every other
outcome, record the classification and the action taken. An ordinary
replacement review produces the resulting round's normal
[round report](#the-round-report); an approved trivial-interaction waiver does
not start or spend a replacement round.

### Trivial-interaction re-review waiver

The binding criteria and evidentiary limits live in
[Standing adjustments](../AGENTS.md#standing-adjustments). After the exact
integration head is pushed, publish this evidence before asking:

- the reviewed head, its recorded base, the new base, and the integration head;
- every overlapping file and the mechanical resolution applied;
- a comparison proving the resulting PR diff is a subset of the reviewed diff;
- why removed or base-side changes do not alter the surviving reviewed claims,
  contracts, or behavior; and
- the affected focused-gate results and current status observation.

Do not dispatch replacement reviewers while the waiver decision is pending. If
the user has not already approved the adjustment, open a separate prompt only
after the evidence appears in normal session output. Ask whether to skip
re-review for the exact integration head; keep the prompt itself concise.

On approval, record the exact-head, exact-base waiver and its evidentiary
consequence on the PR. Keep `review-clean` absent because the new head was not
reviewed, and continue to current-head CI, live mergeability, and merge
authorization.
Without approval, do not waive review; resume the ordinary replacement
workflow when work continues. A resolution that no longer satisfies the
criteria requires ordinary re-review. Any later head or base movement
invalidates a pending or approved waiver and requires fresh carry-forward
classification.
