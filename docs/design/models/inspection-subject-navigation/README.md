# Inspection subject navigation design models

These are executable TLA+ models of the concurrency and authority mechanisms
described in [`../../inspection-subject-navigation.md`](../../inspection-subject-navigation.md).
They replace prose state-machine description with specifications a model
checker can exhaust.

There are three independent models. None imports another, and each is small
enough for TLC to explore its entire state space in about a second.

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
| `NoStuckQueuedMaintenance` | Queued maintenance is only ever blocked by gathering facts, an owed rebuild, unresolved explicit work, or an unconsumed effect |

| Liveness property | Claim |
| --- | --- |
| `ExplicitWorkEventuallyResolves` | Explicit work always reaches a result, a retained outcome, or an abort |
| `EffectEventuallyConsumed` | Unconsumed authority is eventually acknowledged, abandoned, or superseded |
| `MaintenanceEventuallyDrains` | Queued maintenance eventually drains, including after an abort, rather than waiting forever behind an unconsumed effect |

Liveness uses weak fairness on explicit resolution, per-request fact gathering
and rebuilding, maintenance admission, and acknowledgement. Beginning a new
explicit intent is deliberately unfair and bounded, so TLC can show that the
queue drains once intents stop arriving.

## `AtomicRestoration.tla`

Canonical restoration prepares one subject and one lens together under one
explicit intent, coordinates with at least one other restoration participant,
and commits or aborts as a transaction.

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
| `CommitRequiresReadyParticipantsAndCurrentIntent` | A commit happened only with every participant ready and the preparation's token still current |
| `NoSupersededCommit` | A preparation a newer intent replaced never became visible |
| `PreparationIsInvisible` | A live preparation never leaks its subject or lens before commit |

| Liveness property | Claim |
| --- | --- |
| `EveryAttemptSettles` | Every attempt reaches commit, abort, or discard rather than leaving the transaction open |

## `SnapshotAuthority.tla`

Retained and stateless execution of the same navigation work. A retained
operation reads prior state only from its session's installed snapshot and
rejects caller-supplied prior state with a typed outcome. A stateless
evaluation may consume an explicit prior snapshot as data and retains nothing.

Snapshots carry provenance, and only a session snapshot can carry a session
lens, so a committed lens that came from caller data is detectable rather than
indistinguishable. The replacement snapshot records the origin of the snapshot
it was derived from.

A retained typed rejection is still a retained result. It changes no installed
state, but it advances the effect epoch and returns current retained
authority, so a consumer renders and acknowledges a rejection exactly as it
does an applied outcome. Stateless evaluation is unaffected: it issues no
retained authority, whether it succeeds or is rejected.

| Invariant | Claim |
| --- | --- |
| `TypeOK` | State stays within its declared shape |
| `NoForeignRetainedState` | No caller or foreign snapshot becomes retained authority; the installed snapshot is always session-owned and session-derived |
| `OnlyRetainedExecutionInstalls` | Only retained execution installs session state |
| `RetainedPriorStateIsInstalledSnapshot` | Every retained operation used the installed snapshot as its only prior state |
| `RetainedRejectsCallerSuppliedPriorState` | Caller-supplied prior state in retained mode is rejected, never adopted |
| `RetainedRejectionHasExactAuthorityAndInstallsNothing` | Every retained rejection advanced the effect epoch, returned authority naming this session, the unchanged installed revision, the current operation, and the new epoch, and installed nothing |
| `RetainedCommittedLensEqualsInstalledLens` | The retained committed lens equals the installed snapshot's lens and is never a caller-only lens |
| `StatelessIssuesNoRetainedAuthority` | A stateless evaluation issues no retained effect authority |
| `ExactCurrentAuthority` | Unconsumed authority matches the session identity, installed revision, current operation, and epoch exactly |
| `StaleOrForeignAuthorityNeverExecutes` | Stale or foreign authority never executes deferred work |

| Liveness property | Claim |
| --- | --- |
| `EveryCommandResolves` | Every submitted operation reaches a result or a typed rejection |
| `EffectEventuallyConsumed` | Unconsumed authority is eventually acknowledged, abandoned, or superseded |

## Alignment with the owning document

The three models were scanned once against the current owning document. One
mismatch was found and fixed: retained typed rejections resolved without effect
authority, while the document requires every admitted result to carry exact
session, state-revision, intent, and effect-epoch authority, and classifies
`Rejected` and `Failed` as outcomes that retain state rather than as outcomes
that produce nothing. `SnapshotAuthority.tla` now issues that authority for
both retained rejection paths. `NavigationSession.tla` already did, through its
`retained` outcome class.

The remaining differences are deliberate abstractions rather than
disagreements:

- The document's five explicit-activation outcomes collapse to three classes in
  `NavigationSession.tla`. `applied` installs a replacement snapshot,
  `retained` covers `Unavailable`, `Rejected`, and `Failed` because all three
  keep the revision and still take a new effect epoch, and a superseded
  operation returns with no effect at all.
- A newer explicit intent invalidates in-flight maintenance results. The model
  preserves the queued request, clears its completed-facts marker, and requires
  it to rebuild from the replacement snapshot before admission.
- Canonical restoration's subject and lens are optional in the packet. The
  model always prepares both, because the claim under test is that a prepared
  pair installs together.
- Action IDs, generations, descriptor states, diagnostics, and correspondence
  are not modelled. Subjects, lenses, and snapshots are opaque values.

## Guard witnesses

Some claims are about a step rather than a state: "no maintenance was admitted
while an effect was unconsumed" is not visible in any single state after the
fact. Those claims use latching boolean witness variables, named
`admissionWitness`, `orderWitness`, `visibleWitness`, `commitWitness`,
`basisWitness`, `rejectWitness`, `rejectionAuthorityWitness`, and
`executeWitness`.

A witness re-derives, in the pre-state and independently of the action's own
guard, the exact condition the design requires for the step being taken, and
conjoins it into the witness. The paired invariant then asserts the witness was
never falsified. If a future edit weakens an action guard, the witness still
evaluates the real pre-state, so TLC reports a counterexample. Witnesses are
model bookkeeping, not product state.

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

The recorded results below came from:

- TLC `TLC2 Version 2026.08.21.155922 (rev: 9787e65)`, from the official
  `v1.8.0` `tla2tools.jar` asset downloaded on 2026-08-27.
- Eclipse Temurin JRE `21.0.12.1+1`, macOS `arm64`.

### Recorded results

Each run is an exhaustive breadth-first exploration of the shipped `.cfg`, so
the counts are stable across repeated runs on the same tools version. All three
report `Model checking completed. No error has been found.`

| Model | States generated | Distinct states | Search depth |
| --- | --- | --- | --- |
| `NavigationSession.tla` | 9,834 | 1,867 | 15 |
| `AtomicRestoration.tla` | 13,349 | 2,900 | 10 |
| `SnapshotAuthority.tla` | 6,987 | 2,534 | 9 |

Deadlock checking is disabled in all three configs. A behaviour that has issued
every intent, drained its queue, and consumed its last effect has nothing left
to do; termination is the intended end state, not a defect.

## Non-vacuity probes

Each invariant was confirmed to have teeth by mutating the corresponding design
rule in a scratch copy and re-running TLC. These probes are not committed; the
table records how to reproduce them.

| Mutation | Reported violation |
| --- | --- |
| Drop the unconsumed-effect blocker from `MaintenanceAdmissible` | `MaintenanceAdmissionDiscipline` |
| Admit the newest ready maintenance request instead of the oldest | `MaintenanceRequestOrder` |
| Let a superseded explicit result return effect authority | `LatestIntentSafety` |
| Add an admission condition with no matching release path | `NoStuckQueuedMaintenance` |
| Stop releasing the effect on acknowledgement | Temporal properties (the queue never drains) |
| Commit the restored subject without the restored lens | `NoPartialInstallation` |
| Drop the current-intent requirement from commit | `CommitRequiresReadyParticipantsAndCurrentIntent` |
| Reset the visible pair on abort | `VisiblePairIsLastCommit` |
| Take the retained basis from caller-supplied prior state | `NoForeignRetainedState` |
| Adopt the caller snapshot while rejecting a retained operation | `NoForeignRetainedState` |
| Return a retained rejection without advancing the effect epoch | `RetainedRejectionHasExactAuthorityAndInstallsNothing` |
| Let stateless evaluation install session state | `OnlyRetainedExecutionInstalls` |
| Skip authority revalidation before deferred work | `StaleOrForeignAuthorityNeverExecutes` |

## Changing a model

Keep each model independent and finite. Raising `MaxIntent`, `MaxMaintenance`,
`MaxCommands`, or the `Peers`, `Subjects`, and `Lenses` sets grows the state
space quickly and buys little: the shipped bounds are the smallest that reach
supersession, out-of-order fact completion, stale authority, and abort. When a
design rule changes, change the action that states it, keep the paired witness
an independent re-derivation, and re-run TLC before updating the counts above.
