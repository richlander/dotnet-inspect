# Inspection subject navigation design models

These are executable TLA+ models of the concurrency and authority mechanisms
described in [`../../inspection-subject-navigation.md`](../../inspection-subject-navigation.md).
They replace prose state-machine description with specifications a model
checker can exhaust.

There are three independent models. None imports another, and each is small
enough for TLC to explore its entire state space in a second or two.

| Model | Mechanism |
| --- | --- |
| `NavigationSession.tla` | Retained session: intent, supersession, maintenance order, effect authority |
| `AtomicRestoration.tla` | Canonical restoration: one prepared subject+lens pair committed as a transaction |
| `SnapshotAuthority.tla` | Retained versus stateless execution and the prior state each may read |

## What these models cover

Each model is a design specification. TLC checks that the design's own rules
are mutually consistent across every interleaving of a small finite instance:
every ordering of user intent, background maintenance, participant readiness,
fact completion, failure, and consumer acknowledgement that the rules permit.

## What these models do not cover

They are not evidence for any of the following, and no claim here should be
read that way:

- **Identity ranking.** Initial subject recommendation, Type candidate tiers,
  Library declaration order, and lens preference are not modelled. Subjects
  and lenses appear only as opaque values.
- **Availability semantics.** The distinction between available, unavailable,
  failed, and selection-required, and the reconciliation tables, are not
  modelled.
- **UI accessibility.** Focus, roving `tabindex`, menu and tablist semantics,
  history, and rendering belong to [Inspect Web UI](../../inspect-web-ui.md)
  and appear here only as an abstract "consumer holds authority and executes a
  visible effect" step.
- **Implementation correctness.** Nothing here proves that a future C# or
  TypeScript implementation conforms to these specifications. Conformance is
  the job of the named implementation gates in the owning document.
- **Acquisition, security, or performance.** Coordinate realization appears
  only as an external prerequisite that may abort.

The models are also finite by construction: a bounded number of intents,
maintenance requests, and operations. They establish that no permitted
interleaving within those bounds violates the invariants; they are not
inductive proofs for unbounded runs.

## Correlated claims

A claim that says "an operation like this eventually reaches an outcome like
that" can be discharged by some *other* operation's outcome. Every claim in
these models that could fall into that trap names the thing it is talking
about: a maintenance request number, an intent token, a restoration token and
its recorded settlement reason, or a navigation operation ID that the result
carries back. The three models therefore carry three correlation currencies:

| Model | Currency | Used by |
| --- | --- | --- |
| `NavigationSession.tla` | maintenance request number, intent token | per-request admission and per-token settlement |
| `AtomicRestoration.tla` | restoration token plus a settlement reason | per-attempt settlement and commit provenance |
| `SnapshotAuthority.tla` | operation ID assigned on submission | per-operation resolution and rejection |

## `NavigationSession.tla`

One retained navigation session holding zero or one installed snapshot. The
product issues monotonic explicit intent tokens for subject, lens, coordinate,
and canonical-restoration work. The owner issues maintenance request numbers
for standalone inventory refresh and reconciliation. Every admitted result
returns four-part effect authority: session identity, snapshot state revision,
intent token, and effect epoch. A consumer validates that authority, then
acknowledges or abandons it.

Modelled behaviour includes: a newer explicit intent superseding older explicit
and maintenance work; a superseded operation returning late; an external
prerequisite abort that ends an intent without a navigation result; standalone
maintenance queued in request order while its facts complete in any order; and
a consumer holding authority that has since stopped being current.

| Invariant | Claim |
| --- | --- |
| `TypeOK` | State stays within its declared shape |
| `LatestIntentSafety` | Unresolved explicit work carries the current token, every superseded operation carries a strictly older one, and unconsumed authority is the current intent's |
| `ExactCurrentAuthority` | Unconsumed authority matches the session identity, installed revision, current intent, and current epoch exactly |
| `MaintenanceAdmissionDiscipline` | No maintenance was admitted while explicit work was unresolved or an effect was unconsumed |
| `MaintenanceRequestOrder` | Maintenance was admitted in owner-issued request order, never fact-completion order, and the queue stays ordered and outstanding |
| `NoStaleVisibleEffect` | Every render, focus, or outcome effect executed under exactly the session's current unconsumed authority |

Progress is stated per request and per intent token rather than only for the
session as a whole. Each property below names a specific blocker or a specific
token, so another request's admission or a newer intent's resolution cannot
discharge it.

| Liveness property | Claim |
| --- | --- |
| `ExplicitWorkEventuallyResolves` | Explicit work always reaches a result, a retained outcome, or an abort |
| `EveryExplicitIntentSettles` | Each intent token's own operation stops being in flight and stops being an outstanding superseded result, so supersession settles rather than dangles |
| `EffectEventuallyConsumed` | Unconsumed authority is eventually acknowledged, abandoned, or superseded |
| `MaintenanceEventuallyDrains` | The whole queue eventually drains |
| `EveryQueuedRequestIsAdmitted` | Every queued request is eventually admitted, so the head advances and that request leaves the queue |
| `BlockedMaintenanceResumes` | A request blocked by unresolved explicit work or an unconsumed effect is still admitted once that work resolves and that effect is released |
| `MaintenanceResumesAfterAbort` | A request blocked behind an external prerequisite abort is admitted after that abort effect is acknowledged or abandoned |
| `StaleBasisMaintenanceResumes` | A request whose basis a newer snapshot invalidated rebuilds, re-gathers, and is admitted |

Liveness uses weak fairness on explicit resolution, discarding superseded
results, per-request fact gathering and rebuilding, maintenance admission, and
acknowledgement. Beginning a new explicit intent is deliberately unfair and
bounded, so TLC can show that the queue drains once intents stop arriving.

## `AtomicRestoration.tla`

Canonical restoration prepares one subject and one lens together under one
explicit intent, coordinates with at least one other restoration participant,
and commits or aborts as a transaction.

Navigation's own preparation has two explicitly tracked halves. The subject
half and the lens half are each working, ready, or failed, so navigation can
fail before either half resolves, after the subject half alone, or after the
lens half alone. Each of those failures has to settle through abort with
neither half becoming visible.

Every attempt records its own settlement reason: `committed`, `aborted`, or
`discarded`. A failed preparation settles as `aborted` even when a newer intent
superseded it as well, because the ordinary superseded-discard path explicitly
excludes failed preparations. Supersession does not relabel a failure.

The visible subject and the visible lens are modelled as two separate
variables, each tagged with the intent that installed it. That is the shape a
host reaches for when it stores subject levels independently, which makes
`NoPartialInstallation` a real check rather than a restatement of a single
assignment. `lastCommit` is written only by the commit action, so any other
action that disturbed the visible pair is detected.

| Invariant | Claim |
| --- | --- |
| `TypeOK` | State stays within its declared shape |
| `NoPartialInstallation` | The visible subject and visible lens always come from the same restoration |
| `VisiblePairIsLastCommit` | Failure, abort, and supersession retain the prior visible pair exactly |
| `CommitRequiresReadyParticipantsAndCurrentIntent` | A commit happened only with both navigation halves ready, every peer ready, and the preparation's token still current |
| `NoSupersededCommit` | A preparation a newer intent replaced never became visible |
| `FailedPreparationNeverVisible` | A preparation that failed in either navigation half or in a peer never became visible |
| `PreparationIsInvisible` | A live preparation never leaks its subject or lens before commit |
| `LiveAttemptHasNoSettlement` | A settlement reason is recorded once, by the step that ended the attempt |
| `FailedAttemptSettlesAsAborted` | A failed preparation settles as aborted, never as a commit and never as an ordinary superseded discard |
| `CommittedAttemptWasNeitherFailedNorSuperseded` | An attempt recorded as committed appears in neither the failure nor the supersession history |
| `VisiblePairComesFromACommittedAttempt` | The token that installed the visible pair is the one recorded as committed |

| Liveness property | Claim |
| --- | --- |
| `EveryAttemptSettles` | Every attempt settles with its own recorded reason; another attempt settling does not discharge it |
| `FailedAttemptsAbort` | An attempt with any failed participant settles with the aborted reason, not merely as no longer live |
| `HalfFailedAttemptsSettle` | A navigation half-failure reaches that same aborted settlement, including when the other half is already prepared |

## `SnapshotAuthority.tla`

Retained and stateless execution of the same navigation work. A retained
operation reads prior state only from its session's installed snapshot and
rejects explicitly supplied prior state with a typed outcome. A stateless
evaluation may consume an explicit prior snapshot as data and retains nothing.

Snapshots carry **custody** as well as origin. Custody says who holds the
value: `sessionInstalled` is the snapshot the session installed, and `supplied`
is any value handed in by a consumer. A stale copy of this session's own
earlier snapshot has session origin and a session lens, so origin and lens
alone would accept it; its custody keeps it detectably supplied. Each installed
snapshot also records the origin and the custody of the snapshot it was derived
from, so adopting a supplied value shows up in the installed record rather than
having to be inferred.

Operations and results are correlated by an **operation ID** that the session
assigns on submission and every result carries back. That is what lets a claim
name one operation's own outcome instead of settling for some outcome having
occurred: a later operation that is wrongly applied is not excused by an
earlier one having been rejected.

A retained typed rejection is still a retained result. It changes no installed
state, but it advances the effect epoch and returns current retained
authority, so a consumer renders and acknowledges a rejection exactly as it
does an applied outcome. Stateless evaluation is unaffected: it issues no
retained authority, whether it succeeds or is rejected.

| Invariant | Claim |
| --- | --- |
| `TypeOK` | State stays within its declared shape |
| `InstalledSnapshotIsSessionCustody` | The installed snapshot is in session custody, session-owned, carries a session lens, and was derived from a snapshot that was itself in session custody, so no supplied value — including a stale same-session copy — becomes retained state |
| `OnlyRetainedExecutionInstalls` | The installed revision advances only through retained execution |
| `RetainedPriorStateIsInstalledSnapshot` | Every retained operation used the session-custody installed snapshot as its only prior state |
| `OperationAndResultAreCorrelated` | Every result names a submitted operation, an in-flight operation is one past the outstanding result, and with nothing in flight the outstanding result belongs to the most recent operation |
| `NonApplyStepsPreserveInstalledSnapshot` | Stateless execution, stateless rejection, retained rejection, and the authority-only steps left the whole installed snapshot record unchanged, compared field by field rather than by revision counting |
| `RetainedRejectionHasExactAuthorityAndInstallsNothing` | Every retained rejection advanced the effect epoch, returned authority naming this session, the unchanged installed revision, the current operation, and the new epoch, and installed nothing |
| `RetainedCommittedLensEqualsInstalledLens` | The retained committed lens equals the installed snapshot's lens and is never a lens only supplied data could carry |
| `StatelessIssuesNoRetainedAuthority` | A stateless evaluation issues no retained effect authority |
| `ExactCurrentAuthority` | Unconsumed authority matches the session identity, installed revision, current operation, and epoch exactly |
| `StaleOrForeignAuthorityNeverExecutes` | Stale or foreign authority never executes deferred work |

| Liveness property | Claim |
| --- | --- |
| `EveryCommandResolves` | Each operation reaches the result carrying its own operation ID |
| `EffectEventuallyConsumed` | Unconsumed authority is eventually acknowledged, abandoned, or superseded |
| `SuppliedRetainedPriorStateIsAlwaysRejected` | Every retained operation carrying explicitly supplied prior state reaches its own typed rejection, identified by that operation's ID |

## Alignment with the owning document

The three models were scanned against the current owning document. The
remaining differences are deliberate abstractions rather than disagreements:

- **Outcome classes.** The document's five explicit-activation outcomes
  collapse to three classes in `NavigationSession.tla`. `applied` covers every
  outcome that installs a replacement snapshot, which includes an
  `Unavailable` outcome whenever refreshed availability or reconciliation
  returns a semantically changed, revision-advancing snapshot. `retained`
  covers only outcomes that leave the installed snapshot unchanged: `Rejected`,
  `Failed`, and an `Unavailable` outcome whose complete refreshed snapshot,
  including its descriptors and active subject, is unchanged. A superseded
  operation returns with no visible effect at all. Every non-superseded class
  still takes a new effect epoch, which is what keeps two results that share a
  revision distinguishable.
- **Superseded maintenance results.** A newer explicit intent invalidates
  already gathered maintenance facts. The queued request remains, rebuilds
  from the replacement snapshot, and re-gathers before admission.
- **Optional restoration inputs.** Canonical restoration's subject and lens are
  optional in the packet. The model always prepares both, because the claim
  under test is that a prepared pair installs together.
- **Unmodelled currencies.** Action IDs, generations, descriptor states,
  diagnostics, and correspondence are not modelled. Subjects, lenses, and
  snapshots are opaque values. The operation ID and settlement reason in the
  models are correlation currencies for the specifications, not proposed
  product fields.

## Guard witnesses

Some claims are about a step rather than a state: "no maintenance was admitted
while an effect was unconsumed" is not visible in any single state after the
fact. Those claims use latching boolean witness variables, named
`admissionWitness`, `orderWitness`, `visibleWitness`, `commitWitness`,
`basisWitness`, `snapshotStabilityWitness`, `rejectionAuthorityWitness`, and
`executeWitness`.

A witness re-derives, in the pre-state and independently of the action's own
guard, the exact condition the design requires for the step being taken, and
conjoins it into the witness. The paired invariant then asserts the witness was
never falsified. If a future edit weakens an action guard, the witness still
evaluates the real pre-state, so TLC reports a counterexample. Witnesses are
model bookkeeping, not product state.

`snapshotStabilityWitness` compares the whole installed snapshot record rather
than its revision, so a step that rewrote the snapshot's lens or provenance
while leaving the revision alone is still caught. Probe `SA5`/`SA6` below
demonstrates exactly that gap in revision arithmetic.

The settlement reason in `AtomicRestoration.tla` is not a witness. It is
ordinary modelled state that the settling step records, and the invariants
cross-check it against the independently maintained failure and supersession
histories.

## Running TLC

The models are plain TLA+ and need only a JVM and `tla2tools.jar`. TLC reads
`<Module>.cfg` from beside the module, so no extra arguments are required.

From this directory, with `tla2tools.jar` in `$TLA_TOOLS`:

```sh
java -XX:+UseParallelGC -cp "$TLA_TOOLS/tla2tools.jar" tlc2.TLC \
  -workers auto -cleanup NavigationSession.tla
java -XX:+UseParallelGC -cp "$TLA_TOOLS/tla2tools.jar" tlc2.TLC \
  -workers auto -cleanup AtomicRestoration.tla
java -XX:+UseParallelGC -cp "$TLA_TOOLS/tla2tools.jar" tlc2.TLC \
  -workers auto -cleanup SnapshotAuthority.tla
```

`tla2tools.jar` is the official release asset from
`https://github.com/tlaplus/tlaplus/releases`. Neither it nor a JVM is
vendored in this repository, and no repository build or test target depends on
them.

### Locally used tools

The results below came from:

- TLC `TLC2 Version 2026.08.21.155922 (rev: 9787e65)`, from the official
  `v1.8.0` `tla2tools.jar` asset downloaded on 2026-08-27.
- Eclipse Temurin JRE `21.0.12.1+1`, macOS `arm64`.
- Run on 2026-08-27.

## Evidence

Three different things are recorded below, and they are not interchangeable.
Exhaustive checking says the shipped configuration has no reachable
counterexample. Action coverage says each modelled step actually occurs in that
exploration. Mutation probes say a named claim would catch a specific broken
rule.

### Exhaustive model checking

Each run is an exhaustive breadth-first exploration of the shipped `.cfg`, so
the counts are stable across repeated runs on the same tools version. All three
report `Model checking completed. No error has been found.`

| Model | States generated | Distinct states | Search depth |
| --- | --- | --- | --- |
| `NavigationSession.tla` | 9,834 | 1,867 | 15 |
| `AtomicRestoration.tla` | 78,733 | 13,097 | 11 |
| `SnapshotAuthority.tla` | 13,767 | 4,850 | 9 |

Adding the settlement reason, the operation ID, and snapshot custody did not
change any distinct-state count, because each is fully determined by state the
models already carried. That is the point: the invariants re-derive the same
fact from independently maintained history and would diverge if a step
mislabelled its outcome.

Deadlock checking is disabled in all three configs. A behaviour that has issued
every intent, drained its queue, and consumed its last effect has nothing left
to do; termination is the intended end state, not a defect.

### Action coverage

`tlc2.TLC -coverage 1` reports that every action in every model contributes
transitions in the shipped configuration, so no modelled step is dead. Two
actions contribute transitions but no new distinct states: `VisibleEffect` in
`NavigationSession.tla` and `ExecuteEffectWork` in `SnapshotAuthority.tla` only
latch a witness that is already true, which is the intended shape for a
consumer-side revalidation step.

### Mutation probes

Coverage and exhaustive checking do not show that a claim would catch anything.
Each probe below breaks one design rule in a scratch copy and re-runs TLC with
a configuration that enables exactly one claim, so the reported violation names
the claim under test rather than whichever claim happens to be listed first.

Every safety invariant and liveness property in the tables above has a probe,
with one deliberate exception: `TypeOK` in each model is a typing guard, not a
headline claim, and is not probed. The probes are not committed; the table
records how to reproduce them.

| Probe | Mutation | Claim | Result |
| --- | --- | --- | --- |
| NS1 | Admit maintenance while an effect is unconsumed | `MaintenanceAdmissionDiscipline` | violated |
| NS2 | Admit the newest ready request instead of the oldest | `MaintenanceRequestOrder` | violated |
| NS3 | Let a superseded explicit result return effect authority | `LatestIntentSafety` | violated |
| NS4 | Return a retained outcome under the previous effect epoch | `ExactCurrentAuthority` | violated |
| NS5 | Run a visible effect without revalidating authority | `NoStaleVisibleEffect` | violated |
| NS6 | Stop releasing the effect on acknowledgement | `EveryQueuedRequestIsAdmitted` | violated |
| NS7 | Add an admission condition with no disclosed release path | `BlockedMaintenanceResumes` | violated |
| NS8 | Make an abort effect impossible to acknowledge | `MaintenanceResumesAfterAbort` | violated |
| NS9 | Remove the rebuild path for a request with a stale basis | `StaleBasisMaintenanceResumes` | violated |
| NS10 | Drop fairness on explicit resolution | `ExplicitWorkEventuallyResolves` | violated |
| NS11 | Stop releasing the effect on acknowledgement | `EffectEventuallyConsumed` | violated |
| NS12 | Stop releasing the effect on acknowledgement | `MaintenanceEventuallyDrains` | violated |
| NS13 | Let a superseded operation stay outstanding forever | `EveryExplicitIntentSettles` | violated |
| AR1 | Commit the restored subject without the restored lens | `NoPartialInstallation` | violated |
| AR2 | Drop the current-intent requirement from commit | `CommitRequiresReadyParticipantsAndCurrentIntent` | violated |
| AR3 | Drop the current-intent requirement from commit | `NoSupersededCommit` | violated |
| AR4 | Reset the visible pair on abort | `VisiblePairIsLastCommit` | violated |
| AR5 | Commit with a failed navigation half | `FailedPreparationNeverVisible` | violated |
| AR6 | Show the subject half as soon as it is prepared | `PreparationIsInvisible` | violated |
| AR7 | Abort only on peer failure, never on a navigation half-failure | `HalfFailedAttemptsSettle` | violated |
| AR8 | Abort only on peer failure, never on a navigation half-failure | `EveryAttemptSettles` | violated |
| AR9 | Abort only on peer failure, never on a navigation half-failure | `FailedAttemptsAbort` | violated |
| AR10 | Let the superseded-discard path swallow a failed preparation | `FailedAttemptSettlesAsAborted` | violated |
| AR11 | Record an abort as a discard | `FailedAttemptsAbort` | violated |
| AR12 | Let the superseded-discard path swallow a failed preparation | `FailedAttemptsAbort` | violated |
| AR13 | Record a commit while the preparation is still live | `LiveAttemptHasNoSettlement` | violated |
| AR14 | Drop the current-intent requirement from commit | `CommittedAttemptWasNeitherFailedNorSuperseded` | violated |
| AR15 | Record a commit under the discard reason | `VisiblePairComesFromACommittedAttempt` | violated |
| SA1 | Take the retained basis from supplied prior state | `InstalledSnapshotIsSessionCustody` | violated |
| SA2 | Take the retained basis from supplied prior state | `RetainedPriorStateIsInstalledSnapshot` | violated |
| SA3 | Take the retained basis from supplied prior state | `SuppliedRetainedPriorStateIsAlwaysRejected` | violated |
| SA4 | Let stateless evaluation install session state | `OnlyRetainedExecutionInstalls` | violated |
| SA5 | Rewrite the installed lens from a stateless step, leaving the revision alone | `NonApplyStepsPreserveInstalledSnapshot` | violated |
| SA6 | The same lens rewrite, checked against revision arithmetic only | `OnlyRetainedExecutionInstalls` | not violated |
| SA7 | Return a retained rejection without advancing the effect epoch | `RetainedRejectionHasExactAuthorityAndInstallsNothing` | violated |
| SA8 | Report an applied retained lens the installed snapshot does not carry | `RetainedCommittedLensEqualsInstalledLens` | violated |
| SA9 | Issue retained effect authority from a stateless evaluation | `StatelessIssuesNoRetainedAuthority` | violated |
| SA10 | Return applied authority naming the previous snapshot revision | `ExactCurrentAuthority` | violated |
| SA11 | Skip authority revalidation before deferred work | `StaleOrForeignAuthorityNeverExecutes` | violated |
| SA12 | Drop fairness on command resolution | `EveryCommandResolves` | violated |
| SA13 | Drop fairness on acknowledgement | `EffectEventuallyConsumed` | violated |
| SA14 | Apply a later supplied-prior operation once an earlier one was rejected | `SuppliedRetainedPriorStateIsAlwaysRejected` | violated |
| SA15 | Adopt only the stale same-session supplied snapshot | `InstalledSnapshotIsSessionCustody` | violated |
| SA16 | Adopt only the stale same-session supplied snapshot | `RetainedPriorStateIsInstalledSnapshot` | violated |
| SA17 | Return a result that does not record its operation ID | `OperationAndResultAreCorrelated` | violated |

Forty-five probes, forty-four expected violations and one expected pass. `SA6`
is the one probe expected not to fire: it applies the same mutation as `SA5`
and checks the revision-arithmetic invariant instead, which does not notice a
snapshot rewritten in place. That pair is why
`NonApplyStepsPreserveInstalledSnapshot` compares the record.

Three probes exist specifically because a claim used to be satisfiable by the
wrong thing. `AR11` records an abort under the discard reason, `SA14` applies a
later supplied-prior operation after an earlier one was rejected, and `SA15`
adopts only the stale same-session supplied snapshot, whose origin and lens are
indistinguishable from session data.

## Changing a model

Keep each model independent and finite. Raising `MaxIntent`, `MaxMaintenance`,
`MaxCommands`, or the `Peers`, `Subjects`, and `Lenses` sets grows the state
space quickly and buys little: the shipped bounds are the smallest that reach
supersession, out-of-order fact completion, half-failed preparation, stale
authority, abort, and one operation's outcome standing next to another's. When
a design rule changes, change the action that states it, keep the paired
witness an independent re-derivation, and re-run TLC before updating the counts
above. A claim added to a table needs a probe added with it, and a claim about
one operation's outcome should name that operation.
