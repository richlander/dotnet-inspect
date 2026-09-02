# Round orchestration

`AGENTS.md` owns the binding rules for adversarial review: what a candidate is,
when a round may start, what makes a review review-clean, and when to stop. This
document owns the operational side — how to find out where the round stands, how
to dispatch and reconcile it, how approved workflow adjustments apply, and what
to do when the base moves under a clean result.

Read [Adversarial review](../AGENTS.md#adversarial-review) first. This document
owns operational transitions and reporting, not the rules that decide
eligibility, recovery, completion, or carry-forward. Where it applies one of
those rules, it cites the owner rather than restating it.

## User-directed workflow adjustments

[AGENTS.md](../AGENTS.md#user-directed-workflow-adjustments) states the binding
boundary: a user adjustment changes only its named sequencing gate and cannot
make failed evidence successful or transfer fixed-head evidence. The following
standing adjustments define their exact scope and effect.

### Standing adjustments

- **Review ordinary non-Markdown changes in parallel with CI:** requires user
  approval; conflict recovery is the explicit exception. A CI failure requiring
  an author change still supersedes the attempt, and all findings carry forward.
- **Pre-authorize merge for the final head:** after clean reviews or a waiver,
  the user may authorize its exact head and base ref. Keep auto-merge unarmed
  while gates are pending; after green preflight, use the [exact-head
  precondition](github-api-operations.md#bind-merge-mutations-to-the-head).
  Head/base-ref change or invalidated evidence expires authorization;
  no-interaction tip movement within the same base ref preserves it.
- **"CI is ready":** the user's statement that CI has no failures and the PR is
  mergeable. Trust it without re-checking and move to the next task, such as
  dispatching the next round's reviewers.
- **Authorizing the next round before CI completes:** the agent does not need
  to check CI status first; proceed with the authorized round.
- **Skip re-review after a trivial base interaction:** requires the user's
  approval for one exact integration head and its mechanically resolved
  interaction at one exact analyzed base tip, offered only for a
  `main`-targeting PR or bottom open stack slice whose waiver lineage starts at
  one immutable review-clean head and recorded base (a renewal may only
  integrate a further moved base from that same lineage).
  Every overlap must resolve mechanically — analyzed base side verbatim, or
  drop the PR's change to that file — and the cumulative diff against the
  newest base must stay a subset of the original reviewed diff with no
  surviving reviewed claim, contract, or behavior changed. `review-clean` stays
  absent on the integration head. Later no-interaction base movement extends
  the waiver and recorded merge authorization to the analyzed tip without
  moving the head or asking again; head movement or any other interaction
  expires both. Semantic conflict resolution or new authored change requires
  ordinary re-review. Evidence to publish:
  [Trivial-interaction re-review waiver](#trivial-interaction-re-review-waiver).

## Candidate lifecycle

[Canonical round flow](../AGENTS.md#canonical-round-flow) and
[Forming a candidate](../AGENTS.md#forming-a-candidate) state the binding
summary. This section owns the full round cycle, eligibility table,
review-clean definition, and recovery transitions.

### The round cycle

Steps 1-5 run unlocked; the push at step 6 locks the head until step 10 closes
it or a recovery transition supersedes it. Both integrations (steps 1 and 4)
happen before the push — base movement after the push does not reopen the
locked candidate.

Before step 1 for new work, visibly state `Design basis:`. Name exactly one
normative owner with its exact document section and owned claim, then identify
supporting models, adjacent contracts, consumed constraints, and consumer
boundaries by role rather than presenting them as co-owners. Apply the
[design-scope rules](design-scope.md) before starting if ownership is unclear
or the work appears to need multiple normative owners.

1. Integrate the effective base.
2. Make the initial or review-driven change.
3. Run the focused gate.
4. Integrate the effective base again.
5. Re-run focused gates for anything the integrated range can affect.
6. Push and record the candidate head and effective base; the lock begins.
7. Satisfy the applicable eligibility row below.
8. Dispatch every required reviewer at the exact candidate head.
9. Reconcile all feedback publicly.
10. Close only when reconciliation, the applicable local gates, and the status
    acquisition cadence are satisfied. The lock ends, the round number is
    spent, and the visible [round report](#the-round-report) is required.

### Eligibility table

| Attempt | Required before reviewer dispatch | May remain pending |
| --- | --- | --- |
| First attempt at round 1 | Pushed settled head, recorded effective base, focused gate, applicable CI rule below | Mergeability; eligible pending CI |
| Ordinary subsequent round | First-attempt requirements, one status attempt, and no observed conflict | Mergeability; eligible pending CI subject to [Bounded status waiting](#bounded-status-waiting) |
| Conflict-recovery attempt | Resolution head pushed, round number authorized | Post-push local gates, CI, mergeability |
| Failed-gate restart | Required fix pushed, one status attempt, applicable CI rule below, and no observed conflict | Mergeability; eligible pending CI subject to [Bounded status waiting](#bounded-status-waiting) |
| Six-round boundary approval | Fresh green current-head `ci-required` and definite positive mergeability | Nothing |

A non-Markdown candidate requires green current-head `ci-required` before
reviewer dispatch unless the user authorized parallel review or conflict
recovery applies. A Markdown-only candidate (every changed file is `*.md`)
substitutes pre-commit `markdownlint` at non-boundary rounds. Only these
exceptions make pending CI eligible; `ci-required` remains mandatory at
six-round boundaries and final merge.

### Review-clean, and recovery

A review is **review-clean** when public reconciliation leaves no finding
unresolved (a justified dismissal counts if recorded publicly) and the head did
not move in response. Only a replacement head can earn `review-clean` after a
fix-producing round. The report classification `clean` requires every required
reviewer to return no findings against an unchanged locked head; use
`converging`, `neutral`, or `diverging` when at least one finding was returned.

Recovery transitions, applied without waiting for CI:

- **Conflict:** supersede, integrate, resolve, push immediately, and restart
  the same round. A conflict after clean review may instead take the
  [exact-head trivial-interaction waiver](#trivial-interaction-re-review-waiver)
  path when its resolution satisfies every stated condition; don't dispatch
  replacement reviewers while that decision is pending.
- **Scope violation:** keep the locked head unchanged while the user chooses
  split, abandonment, or an approved broad exception (see
  [Recovering from an over-broad design](design-scope.md#recovering-from-an-over-broad-design)).
  A resulting head change follows the author-change transition at the same
  round.
- **Failure requiring an author change:** supersede, push the fix, satisfy the
  failed-gate row, and restart the same round.
- **Cancelled or evidenced transient failure:** keep the lock and retry the
  unchanged head; repeat only with concrete transient evidence, otherwise treat
  it as an author change.

A final-gate `ci-required` failure observed during or after a non-boundary
Markdown-only round does not interrupt or reopen that round. Finish its review
path; afterward, retry the unchanged head only with concrete transient evidence,
otherwise remove `review-clean` and form a candidate at the next round number.
Never close a round or goal while one of its applicable required checks is red. A
superseded attempt spends no round and gets no completion report; let its
reviewers finish or acknowledge cancellation, and carry every returned finding
forward.

### Merge preflight

Before an agent-driven merge, re-read GitHub state and confirm the expected
head and base-ref name, valid review-clean or approved-waiver evidence,
non-draft status, positive mergeability, and successful current-head
`ci-required`. A mismatch, invalid evidence, true draft flag, REST
`mergeable: null`, GraphQL `mergeable: UNKNOWN`, missing gate, or gate from
another head is not ready. Use a GraphQL snapshot so documented
`mergeStateStatus: BLOCKED` can also block the action; do not infer that enum
from undocumented REST `mergeable_state` values. Follow
[GitHub status queries](github-status-queries.md).

GitHub merge and auto-merge bind an expected head, not an expected base. This
preflight and carry-forward analysis are point-in-time observations, not an
exact-base lock; do not chase `main` with branch updates to approximate one.
Exact-base integration revalidation requires a merge queue or equivalent
ruleset. Keep GitHub auto-merge unarmed while gates are pending. After a green
preflight, exercise a recorded authorization through a direct merge using the
[exact-head precondition](github-api-operations.md#bind-merge-mutations-to-the-head).
If an auto-merge request exists, disable it before any recovery mutation or
head-moving push.

For stacks, every open layer must meet its applicable eligibility row above. A
known-red or conflicted parent blocks upper slices; a pending parent does not
block a first or conflict-recovery attempt.

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

Handle lifecycle, head/base mismatch, and conflict outcomes in the order
defined by [GitHub status queries](github-status-queries.md). Clear status
predicates, `schedule`, `status-deadline`, and `goal` when the workflow leaves
that wait. Preserve unrelated members such as `review`.

Evaluate `waiting` as a set, not an exact string. Normalize new CI waits to
`check:ci-required`, remove only predicates the result resolves, and preserve
unrelated members such as `review`. In the table, **status members** means
`checks`, every `check:<name>`, and `merge`.

| Status snapshot says | Round transition |
| --- | --- |
| PR is merged | Leave the status wait, relinquish ownership, and end. |
| PR is closed or draft | Leave the status wait, publish the human action or stopped state, and end. |
| Base ref changed | Leave the status wait; expire merge authorization and route the unchanged head through candidate formation without inheriting fixed-head evidence. |
| Head changed | Leave the status wait; disable auto-merge first, handle an already-merged result as terminal, then route the returned head through candidate formation without inheriting fixed-head evidence. |
| REST `mergeable: false` or GraphQL `mergeable: CONFLICTING` | Leave the status wait; apply conflict recovery before considering CI. |
| `ci-required` completed without `success` while required for the current round or goal | Leave the status wait; classify the result and apply the applicable recovery transition. |
| `ci-required` completed without `success` while not required for the current round or goal | Record the final-readiness failure and continue the current review path. |
| GraphQL `mergeStateStatus: BLOCKED`, `goal=merge` | Leave the status wait, publish `blocked=<pr-number> rec=wait`, and end. |
| Green `ci-required` and positive mergeability at the expected head | Leave the status wait and continue when no other predicate remains. |
| CI or mergeability is pending or missing | Preserve the unresolved status members and apply the round cadence below. |
| Rate-limited or transient query failure | Record the concrete failure and retry-not-before time, preserve the unresolved status members, and apply the round cadence below. |
| Terminal query failure | Leave the status wait with `rec=stop`, surface the failure, and end. |

Read the table top-down. Conflict recovery outranks CI, terminal non-green
required CI outranks the remaining merge states, and a documented GraphQL block
prevents a merge goal. Carry-forward remains a separate pre-merge obligation
driven by the fetched base tip, not by undocumented REST `mergeable_state`
values.

### Bounded status waiting

*This section defines repository policy, not GitHub timing guarantees.*

Every round attempts one current-head snapshot. When CI is a reviewer-dispatch
prerequisite, pending, missing, rate-limited, or transient status enters the
60-minute budget below; expiry publishes the status report and stops without
dispatch. When CI may remain pending, record that status and continue the
current review path. A known conflict, required CI completed without success,
or terminal query failure still takes its transition.

A reviewer-dispatch CI prerequisite spends up to a 60-minute status budget
before dispatch. Every third round, and any merge or readiness goal, may use the
same bound. Every sixth round uses that budget, but fresh green current-head
`ci-required` and positive mergeability remain prerequisites for the next-block
approval prompt. Measure the budget from the first scheduled wait and publish
`status-deadline=<UTC>`.

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

### Reviewer roster

[How many reviewers, and from which models](../AGENTS.md#how-many-reviewers-and-from-which-models)
states the binding tier table and roster names. Pick the second seat from the
prior round's clean count:

- **No prior round, or 0/2 clean:** prefer GPT-5.6 Sol again for the second
  seat.
- **1/2 clean:** keep GPT-5.6 Sol fixed, rule out last round's second seat,
  then prefer a different family than the author (the author's family only as
  a fallback) at that model's highest available quality.

Record the choice and its reasoning on the PR. If a roster model is
unavailable, substitute another model for that seat, report the substitution
on the PR, and proceed without approval — a substituted seat still counts as
filled. One round evaluates one settled head with all required reviewers.

### Dispatch

Start every reviewer prompt with the complete contents of
[`docs/adversarial-review-prompt.md`](adversarial-review-prompt.md).
Read it directly before composing the prompt:

```bash
cat docs/adversarial-review-prompt.md
```

Do not summarize, paraphrase, reorder, or put candidate-specific instructions
before the fixed prompt. Append the candidate's exact base and head, design
intent, relevant diff, concrete properties under test, prior findings, and
required real-run evidence. The appended material may narrow the review but
must not weaken or broaden the prompt's trust model and finding-admission rules.
It also records the user purpose, convention or best-practice baseline,
intentional divergence, analogous implementation evidence, pathological or
boundary case and gate, complexity basis, consumer and host plan, rendering
strategy, current slice and residual work, and the demo with a neighboring
case. Use `Not applicable — <reason>` for a field that genuinely does not
apply.
Agents that prefer a structured composition aid may instead fill the optional
[`docs/templates/adversarial-review-prompt.md`](templates/adversarial-review-prompt.md),
which includes the same fixed prompt followed by candidate placeholders.

Do not dispatch with a generic or incoherent frame. The prompt must name one
normative owner and exact claim, the supported actor or caller, the controlled
or variable input, the boundary through which it reaches the claim, trusted
parties and excluded scenarios, the user purpose, baseline and any divergence,
relevant analogous evidence, pathological case and gate, current slice,
residual work, demo and neighboring case, the observable consequence, and the
evidence that would falsify the claim. For an applicable capability, substrate,
host, or broad rendering change, candidate formation must also supply the
complexity basis, named consumer, focused issue, overall end-to-end tracker,
host-enablement plan, any recorded single-consumer or single-host approval and
its exact scope, and the rendering strategy. Reviewers judge the visible
design's consistency with those supplied facts; they do not grant approvals or
invent roadmap decisions. State the facts directly in the self-contained
prompt; links may support them but do not replace them. For a correctness
review without an untrusted actor, name the ordinary supported caller and input
instead. If required fields cannot be filled or explained as not applicable,
return to design or scope clarification before spending a review round.

Give every seat the same completed prompt except for its worktree path. State
candidate facts rather than rewarding findings; the canonical prompt already
makes reporting CLEAN an explicit successful outcome.

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

The canonical prompt requires a property description rather than an attack
brief. An exploit-tutorial-style prompt can trip a model's content filter, and
**that failure is silent**: the reviewer returns an empty or near-empty
response with a clean worktree, which is indistinguishable from a broken model
or a stalled harness.

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
- Design basis: normative owner <path#section> — <owned claim>; supporting
  <path and role for each model, adjacent contract, constraint, or consumer>.
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
  unchanged, use `clean`. The report's `Design basis` must restate the normative
  owner and supporting-role map, confirming that review did not reveal ownership
  drift. Do not use `converging` as a generic positive label.
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
- When a completed Markdown-only round is review-clean and no further
  author or review round is needed, but `ci-required` remains pending or
  missing, use `Waiting: check:ci-required` and `Recommendation: wait`. Use
  `Waiting: check:ci-required,merge` when live mergeability is also unresolved.
  A completed failure finishes the current round, then follows the final-gate
  transition above; use `Recommendation: continue` only when the next round is
  inside the authorized block.
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
trivial-interaction waiver. A carry-forward lineage is one immutable candidate
head plus the ordered base tips analyzed against it. This is the procedure once
the path applies.

1. **Detect movement without API spend.** Fetch the effective base
   non-mutating, resolve its remote-tracking ref to an exact SHA, and compare
   that SHA with the latest base tip recorded for this carry-forward lineage.
   Do not spend GraphQL solely to read the live base tip. If a graph-shaped
   query is already justified, the documented `baseRef.target.oid` identifies
   the object currently pointed to by the base ref. Do not rely on undocumented
   assumptions about `baseRefOid` freshness.
2. **Inspect without integrating.** Read the exact landed range between the
   recorded and fetched tips.
3. **Classify and report.** As normal session output, report which commits
   touch files this change touches, which relied-on behavior they alter, and
   any conflict a textual merge would resolve silently but wrongly. State the
   classification plainly: no interaction, trivial interaction, significant
   interaction, or conflict requiring semantic resolution.
4. **Act on the classification.**
   - *No interaction:* do not integrate or push. Keep the candidate head
     unchanged and record the analyzed tip as the lineage's new base tip. From
     a review-clean head, keep `review-clean`; from a pending or approved waiver
     head, keep it absent and carry the waiver forward. Preserve recorded merge
     authorization and keep GitHub auto-merge unarmed. Start no new validation,
     CI, review, or waiver decision; final preflight still observes the existing
     current-head gate.
   - *Trivial interaction:* if the PR remains open, expire merge authorization,
     disable any armed auto-merge first, and handle an already-merged result as
     terminal. Then remove `review-clean`, integrate the exact analyzed tip,
     resolve every overlap mechanically as classified, run affected focused
     gates, and push. Follow the waiver procedure below before dispatching
     replacement reviewers.
   - *Significant interaction, no conflict:* if the PR remains open, expire
     merge authorization, disable any armed auto-merge first, and handle an
     already-merged result as terminal. Then remove `review-clean`, integrate
     the tip, re-run the claimed validation, push, obtain current-head CI, and
     re-dispatch the required reviewers at the new head as a normal round.
   - *Conflict requiring semantic resolution:* expire merge authorization,
     disable any armed auto-merge first, and handle an already-merged result as
     terminal. Then remove `review-clean` and resolve it as an author change under
     [conflict recovery](../AGENTS.md#recovery-transitions), and re-dispatch
     the required reviewers at the new head.

For a no-interaction carry-forward, record the unchanged candidate head, the
old and newly analyzed tips, the non-interaction analysis, and the preserved
review or waiver state on the PR. For every other outcome, record the
classification and the action taken. An ordinary replacement review produces
the resulting round's normal
[round report](#the-round-report); an approved trivial-interaction waiver does
not start or spend a replacement round.

### Trivial-interaction re-review waiver

The binding criteria and evidentiary limits live in
[Standing adjustments](#standing-adjustments). Approval covers one
exact integration head and its mechanically resolved interaction at the named
base tip; later no-interaction tips extend that lineage without changing the
head. After the integration head is pushed, publish this evidence before
asking:

- the immutable reviewed head and its recorded base, the prior integration
  head/base when renewing, and the new integration head/base;
- every overlapping file and the mechanical resolution applied;
- a comparison proving the cumulative resulting PR diff is a subset of the
  original reviewed diff;
- why removed or base-side changes do not alter the surviving reviewed claims,
  contracts, or behavior; and
- the affected focused-gate results and current status observation.

Do not dispatch replacement reviewers while the waiver decision is pending. If
the user has not already approved the adjustment, open a separate prompt only
after the evidence appears in normal session output. Ask whether to skip
re-review for the exact integration head; keep the prompt itself concise.

On approval, record the immutable reviewed head/base, the approved exact
integration head/base, and the waiver's evidentiary consequence on the PR.
Keep `review-clean` absent because the new head was not reviewed, and continue
to current-head CI, live mergeability, and merge authorization.
Without approval, do not waive review; resume the ordinary replacement
workflow when work continues. A resolution that no longer satisfies the
criteria requires ordinary re-review. Later base movement requires
carry-forward classification: no interaction extends the pending or approved
waiver and recorded merge authorization to the newly analyzed tip without
another integration or decision; if observed while the PR remains open, any
other interaction invalidates both. Any head movement also invalidates both.

## Block boundaries and splitting

[Stop after six rounds](../AGENTS.md#stop-after-six-rounds) states the binding
rules: rounds 1-6 run without approval, approval is required only before
rounds 7, 13, 19, and so on, and round 12 (and every six-round boundary after
it) carries a presumption to split remaining work into focused successors.
This section owns the checkpoint procedure and the split mechanics.

### The block-approval checkpoint

Before requesting another block, answer:

1. **What changed?** Summarize product, architecture, and test improvements,
   findings retired, and confidence gained. Separate durable progress from
   churn.
2. **Are reviews converging?** Cite clean counts and repeated versus new finding
   categories. State why dual-clean is or is not likely in the next block.
3. **Are the foundations sound?** Classify remaining findings as architectural,
   coverage gaps, contract expansion, or harness-only concerns.
4. **Should implementation pause for a docs-only design PR?** Recommend it when
   contracts, ownership, or architecture need direct repository-owner
   engagement.
5. **If design work was skipped last block, why skip it again?** The prior
   decision is not standing authorization; identify the new evidence that makes
   implementation rounds the better investment.

Publish the complete checkpoint as normal session output before opening the
approval prompt. The prompt asks only which recommended action to authorize; it
must not contain the checkpoint itself.

### Round 12 and later six-round boundaries

At round 12 and every 6-round boundary after (18, 24, and so on), also answer:

1. **Would a design doc better define the design space?** Foundational APIs
   weigh heavily toward yes.
2. **Can hardening move to followups?** State whether deferring remaining
   hardening to followup work would unlock this PR's value for other agent
   work sooner.

Split the remaining work into focused successors unless the checkpoint
establishes a strong reason to keep the PR intact and the user explicitly
approves that exception. The strong reason must explain why the remaining
claims cannot become independently reviewable successors, why the reviews are
still converging, and why continuing the same PR is safer than splitting it.
Reviewer familiarity, sunk cost, or the inconvenience of restacking are not
strong reasons. Sprawling changes accumulated across review comments are
themselves a sign that the remaining work should be split.

State the proposed remedy and end with one recommendation: split into focused
successors, approve the next implementation block under the strong-reason
exception, switch to a docs-only design PR, or stop. If consecutive rounds only
strengthen the harness while the product goes unchallenged, report that count
and recommend splitting or stopping rather than continuing by reflex.

### Executing a split

After round 12 or a later six-round boundary closes, the split recommendation
puts the completed head in an immutable decision hold while the user decides.
This is not a round lock; do not mutate the head or dispatch another round
during the hold, including for conflict recovery.

If approved, publicly assign every current change, claim, and finding —
including resolved or dismissed findings and their resulting changes or
rationale — to a focused successor, or explicitly record why an item is being
dropped. Close the current PR as superseded without merging it, and open the
successors from their effective base. Each successor starts at round 1;
reviews, round counts, and authorization blocks do not carry forward.
