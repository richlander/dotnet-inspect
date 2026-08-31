# Inspect-web worker runtime models

This directory model-checks three mechanisms owned by
[Inspect-web worker runtime](../../inspect-web-worker-runtime.md). The models
supplement the readable design. They prove nothing about TypeScript, browser
workers, clocks, .NET, generated facades, managed callbacks, or feature
implementations.

## Model split

`InspectWebWorkerProtocol.tla` models two operation references assigned to one
worker epoch. It covers:

- held starts and readiness-ordered dispatch;
- cancellation before dispatch;
- explicit acceptance or rejection;
- progress/settlement ordering through one atomic physical `Settled` record;
- cancellation acknowledgment retention;
- serialized command-response commitment and response-probe proof of a missing
  earlier response;
- operation-sequence high-water replay rejection without completed-ID
  tombstones;
- epoch-work sequence high-water and active-set validation;
- failed draining and realm release; and
- stale old-epoch messages and callbacks after release.

`InspectWebWorkerLifecycle.tla` models one worker epoch with two assigned
operations. It covers:

- a non-renewable startup active-time budget;
- matching readiness as the only successful startup transition;
- lifecycle suspension and main-loop discontinuity;
- idle, accepted-operation, and epoch-work liveness;
- bounded and unbounded silence;
- task-loop evidence distinguished from progress and managed-callback activity;
- the two-stage post-readiness probe;
- planned restart versus unexpected loss;
- bounded draining, natural release, hard realm destruction, and quiescence;
  and
- source and callback revocation after release.

`InspectWebWorkerProbe.tla` composes the two probe triggers over one physical
probe register. It covers:

- watchdog adoption of an already-outstanding control-response probe;
- deferred control coverage when any outstanding probe predates the command;
- immutable response-obligation snapshots;
- exact probe acknowledgment;
- missing-response failure after serialized command completion;
- task-loop evidence clearing watchdog suspicion; and
- lifecycle recovery preserving an outstanding register and acknowledgment.

Keeping the models separate prevents protocol bookkeeping from multiplying
every clock state. The protocol model abstracts time and liveness arithmetic.
The lifecycle model abstracts payload parsing, sequence replay, and operation
message inventory. The probe model isolates the cross-cutting arbitration seam
that would otherwise be abstracted differently by those two models.

## Assumptions and bounds

Both models use two abstract operations. Their identities stand for complete
opaque operation-ID and numeric-sequence pairs; they do not model string
construction or feature meaning.

The protocol model assumes:

- operation A has sequence one and operation B has sequence two;
- page operation authority supplies non-reused IDs and increasing safe-integer
  sequences;
- one `Settled` message carries both the managed terminal result and proof that
  the operation-scoped managed release barrier has completed;
- `Start`, `Cancel`, and `Probe` use one serialized command lane; operation A
  represents the one response obligation covered by the finite model's probe,
  and a same-operation later control waits until that probe retires;
- a handler can complete without its required response, after which a later
  matching probe acknowledgment supplies positive evidence of the omission;
- cancellation and settlement may race in either order;
- `MaxWorkSequence = 2` is a state-space bound, not a product limit; and
- weak fairness applies only to realm destruction after draining.

The lifecycle model assumes:

- operation A has a structurally bounded allowance and operation B is
  unbounded;
- one epoch-work lease can be absent, bounded, or unbounded;
- startup, silence, and drain budgets are abstract positive counters, each set
  to one in the checked configuration;
- one lifecycle suspension and one main-loop discontinuity are enough to expose
  pause-versus-reset and false-termination defects;
- only heartbeat, probe acknowledgment, and serialized command responses are
  task-loop evidence; non-task messages do not renew the watchdog;
- `Accepted` renews the liveness origin because its serialized response is
  task-loop evidence;
- bounded settlement and work start/finish recompute from the retained
  task-evidence origin rather than renewing it;
- the final unbounded close grants one fresh bounded interval because no
  bounded deadline was enforceable while unbounded work was active;
- lifecycle resume and main-loop recovery discard the interrupted judgment and
  grant one fresh interval while preserving an outstanding probe;
- an operation or work lease can release naturally during draining;
- worker crash has already destroyed the realm; and
- weak fairness covers lifecycle and main-loop resume, startup ticking and
  expiry, both silence-expiry stages, drain ticking, and realm destruction.

The bounded-silence configuration removes task-loop evidence, lifecycle
discontinuities, and unbounded work while retaining bounded work-start and
work-finish churn. It checks that the retained liveness origin still drives the
epoch into draining.

The probe model assumes:

- one response obligation is enough to expose ordering across the seam;
- a control probe's response snapshot is immutable after send;
- a covered pending response means the serialized command is unfinished, so a
  later probe acknowledgment cannot yet commit;
- a covered omitted response means the command completed without its required
  response, so acknowledgment proves a protocol defect; and
- the worker's next reply sequence is retained independently from the host's
  outstanding register so accidental register replacement produces an
  ordinary mismatched-acknowledgment failure; and
- `MaxProbeSequence = 2` is a state-space bound, not a product limit.

The models do not establish that any concrete operation is structurally
bounded. That requires the product-owned event-loop-return gate and browser
measurement required by the design. The lifecycle model represents bounded
allowances as one abstract class; it does not model numeric maximum arithmetic
among several distinct bounded durations.

## Checked properties

### Protocol

| Design property | Model property |
| --- | --- |
| Only a matching ready epoch can dispatch held starts | `MatchingReadyRequired`, `NoDispatchBeforeReady` |
| Held cancellation never posts the operation | `CanceledHeldNeverDispatches` |
| Draining refuses new assignments | `DrainingRefusesStarts` |
| Acceptance and rejection are exclusive | `AcceptedAndRejectedAreExclusive` |
| Every settlement requires prior acceptance | `SettlementRequiresAcceptance` |
| One `Settled` record includes physical quiescence | `AtomicSettlementIncludesQuiescence` |
| An operation settles at most once | `OneSettlementPerOperation` |
| A canceled record retains its pending acknowledgment before retirement | `RetirementRequiresClosureAndAcknowledgment` |
| A sequence at or below high-water cannot reenter admission | `ReplayNeverReentersAdmission` |
| Probe proof of a missing response fails the epoch | `MissingResponseProofFailsEpoch` |
| Missing-response proof requires a completed omission | `MissingResponseProofRequiresCompletedOmission` |
| Probe acknowledgment cannot overtake its earlier command | `ProbeCannotOvertakeControl` |
| `not-active` cannot acknowledge a future sequence | `NotActiveRequiresReceivedSequence` |
| Epoch-work sequences do not restart or reuse | `WorkSequenceNeverReused`, `StartedWorkTracksHighWater` |
| Work finish requires an active start | `WorkFinishRequiresActiveStart` |
| An old epoch cannot mutate the current host | `StaleEpochCannotMutateCurrentState` |
| No callback runs after realm release | `NoCallbackAfterRealmRelease` |
| Protocol failure leaves ready operation | `ProtocolFailureLeavesReadyState` |
| Draining eventually closes | `DrainingEventuallyCloses` |

### Lifecycle

| Design property | Model property |
| --- | --- |
| Startup messages and resume cannot renew the budget | `StartupBudgetDoesNotRenew` |
| Only matching readiness opens the epoch | `MatchingReadyIsRequired`, `ProbeCannotSatisfyStartup`, `MismatchedReadyCannotOpenEpoch` |
| Draining refuses assignments | `DrainingRefusesAssignments` |
| Accepted liveness matches the registered declaration | `RegisteredAllowanceMustMatch` |
| Progress-like non-task messages do not renew liveness | `NonTaskMessagesDoNotRenewWatchdog` |
| First bounded expiry probes rather than terminates | `FirstWatchdogExpiryOnlyProbes` |
| Suspect state retains the issued probe | `SuspectRequiresIssuedProbe` |
| Unbounded silence cannot fail the watchdog | `UnboundedSilenceCannotFailWatchdog` |
| A main-loop gap cannot fail the worker watchdog | `MainLoopGapCannotFailWatchdog` |
| Planned restart cancels pending work | `PlannedRestartCancelsPendingOperations` |
| Unexpected loss fails pending work | `UnexpectedLossFailsPendingOperations` |
| One fixed closure cause determines every affected outcome | `ClosureCauseDeterminesOutcome` |
| Quiescence follows natural or realm release | `QuiescenceRequiresPhysicalRelease` |
| Realm destruction revokes the source and leaves no live records | `RealmReleaseRevokesSource`, `ClosedEpochHasNoLiveResources` |
| No callback runs after release | `NoCallbackAfterRealmRelease` |
| Startup eventually succeeds or fails | `StartingEventuallyLeaves` |
| Draining eventually destroys the realm | `DrainingEventuallyCloses` |
| Continuous bounded silence drains despite bounded callback churn | `ContinuousBoundedSilenceEventuallyDrains` |

### Probe arbitration

| Design property | Model property |
| --- | --- |
| At most one physical probe is in flight | `OnePhysicalProbe` |
| Acknowledgment must match the outstanding sequence | `ProbeSequenceIsExact` |
| An older probe cannot cover a later command | `OlderProbeDoesNotCoverLaterCommand` |
| A covered omitted response fails the epoch | `CoveredOmissionFails` |
| Probe acknowledgment clears watchdog suspicion | `ProbeAcknowledgmentClearsSuspicion` |
| Probe-driven protocol failure is limited to a covered omission | `ProtocolFailureIsOnlyCoveredOmission` |

## Running TLC

Use the repository-pinned tools from the
[TLA+ setup runbook](../../../runbooks/tla-plus-setup.md):

```bash
TLA_TOOLS_JAR=/path/to/tla2tools.jar
cd docs/design/models/inspect-web-worker-runtime

java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers 4 -cleanup \
  -config InspectWebWorkerProtocol.cfg \
  InspectWebWorkerProtocol.tla

java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers 4 -cleanup \
  -config InspectWebWorkerLifecycle.cfg \
  InspectWebWorkerLifecycle.tla

java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers 4 -cleanup \
  -config InspectWebWorkerLifecycle_BoundedSilence.cfg \
  InspectWebWorkerLifecycle.tla

java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers 4 -cleanup \
  -config InspectWebWorkerProbe.cfg \
  InspectWebWorkerProbe.tla
```

Substitute any mutation configuration filename for the positive configuration.

The recorded runs used OpenJDK 21.0.12 and TLA+ tools 1.8.0
(`TLC2 2026.08.21.155922`, revision `9787e65`). The checked
`tla2tools.jar` SHA-256 is
`eabd140a70f49eb9305a3bd3f3df944eddf87e5a90d329789085f8953a80533a`.

## Positive results

| Configuration | Bounds | Generated | Distinct | Depth | Result |
| --- | --- | ---: | ---: | ---: | --- |
| `InspectWebWorkerProtocol.cfg` | 2 operations, `MaxWorkSequence = 2` | 455,220 | 129,105 | 20 | No error |
| `InspectWebWorkerLifecycle.cfg` | 2 operations, all budgets = 1 | 131,999 | 28,872 | 19 | No error |
| `InspectWebWorkerLifecycle_BoundedSilence.cfg` | No recurring task evidence or unbounded work; all budgets = 1 | 3,713 | 1,424 | 14 | No error |
| `InspectWebWorkerProbe.cfg` | 1 command, `MaxProbeSequence = 2` | 827 | 316 | 10 | No error |

## Mutation results

Every checked mutation produced its named invariant or temporal-property
violation.

### Protocol mutations

| Configuration | Injected defect | Observed violation |
| --- | --- | --- |
| `InspectWebWorkerProtocolDispatchBeforeReady.cfg` | Posts a held start during startup | `NoDispatchBeforeReady` |
| `InspectWebWorkerProtocolDispatchCanceledHeld.cfg` | Posts a locally canceled held start | `CanceledHeldNeverDispatches` |
| `InspectWebWorkerProtocolAcceptMismatchedReady.cfg` | Opens on mismatched readiness | `MatchingReadyRequired` |
| `InspectWebWorkerProtocolReplayAccepted.cfg` | Re-admits a sequence at high-water | `ReplayNeverReentersAdmission` |
| `InspectWebWorkerProtocolSettleBeforeAccepted.cfg` | Settles before acceptance | `SettlementRequiresAcceptance` |
| `InspectWebWorkerProtocolDuplicateSettlement.cfg` | Emits a second settlement | `OneSettlementPerOperation` |
| `InspectWebWorkerProtocolRetireBeforeAck.cfg` | Drops a canceled record before acknowledgment | `RetirementRequiresClosureAndAcknowledgment` |
| `InspectWebWorkerProtocolIgnoreMissingResponse.cfg` | Ignores probe proof of a missing response | `MissingResponseProofFailsEpoch` |
| `InspectWebWorkerProtocolProbeOvertakesControl.cfg` | A probe overtakes unfinished cancellation for an accepted operation | `MissingResponseProofRequiresCompletedOmission` |
| `InspectWebWorkerProtocolFutureCancelNotActive.cfg` | Acknowledges a never-received future sequence | `NotActiveRequiresReceivedSequence` |
| `InspectWebWorkerProtocolReuseWorkSequence.cfg` | Restarts a completed work sequence | `WorkSequenceNeverReused` |
| `InspectWebWorkerProtocolUnmatchedWorkFinish.cfg` | Finishes work without an active lease | `WorkFinishRequiresActiveStart` |
| `InspectWebWorkerProtocolAcceptDuringDrain.cfg` | Accepts a new assignment while draining | `DrainingRefusesStarts` |
| `InspectWebWorkerProtocolStaleEpochMutation.cfg` | Lets an old epoch mutate current state | `StaleEpochCannotMutateCurrentState` |
| `InspectWebWorkerProtocolCallbackAfterClose.cfg` | Delivers a callback after realm release | `NoCallbackAfterRealmRelease` |

### Lifecycle mutations

| Configuration | Injected defect | Observed violation |
| --- | --- | --- |
| `InspectWebWorkerLifecycleRenewStartupFromMessage.cfg` | A startup message renews the budget | `StartupBudgetDoesNotRenew` |
| `InspectWebWorkerLifecycleResetStartupOnResume.cfg` | Lifecycle resume resets startup | `StartupBudgetDoesNotRenew` |
| `InspectWebWorkerLifecycleProbeSatisfiesStartup.cfg` | Probe acknowledgment opens the epoch | `ProbeCannotSatisfyStartup` |
| `InspectWebWorkerLifecycleAcceptMismatchedReady.cfg` | Mismatched readiness opens the epoch | `MismatchedReadyCannotOpenEpoch` |
| `InspectWebWorkerLifecycleTerminateAtFirstExpiry.cfg` | First silence interval terminates | `FirstWatchdogExpiryOnlyProbes` |
| `InspectWebWorkerLifecycleTerminateWhileUnbounded.cfg` | Silence kills an unbounded epoch | `UnboundedSilenceCannotFailWatchdog` |
| `InspectWebWorkerLifecycleTerminateAcrossMainGap.cfg` | A delayed main watchdog kills the worker | `MainLoopGapCannotFailWatchdog` |
| `InspectWebWorkerLifecycleAcceptDuringDrain.cfg` | Draining accepts another operation | `DrainingRefusesAssignments` |
| `InspectWebWorkerLifecyclePlannedAsFailure.cfg` | Planned restart reports failure | `PlannedRestartCancelsPendingOperations` |
| `InspectWebWorkerLifecycleUnexpectedAsCancellation.cfg` | Unexpected loss reports cancellation | `UnexpectedLossFailsPendingOperations` |
| `InspectWebWorkerLifecycleQuiesceBeforeRelease.cfg` | Quiescence precedes physical release | `QuiescenceRequiresPhysicalRelease` |
| `InspectWebWorkerLifecycleCallbackAfterRelease.cfg` | Callback survives realm release | `NoCallbackAfterRealmRelease` |
| `InspectWebWorkerLifecycleDrainNeverCloses.cfg` | Failed draining cannot destroy the realm | `DrainingEventuallyCloses` |
| `InspectWebWorkerLifecycleAllowanceMismatch.cfg` | Acceptance uses an unregistered allowance | `RegisteredAllowanceMustMatch` |
| `InspectWebWorkerLifecycleNonTaskMessageRenews.cfg` | Progress-like callback activity renews the watchdog | `NonTaskMessagesDoNotRenewWatchdog` |
| `InspectWebWorkerLifecycleAllowanceChurnRenews.cfg` | Bounded allowance churn repeatedly clears suspicion and renews the origin | `ContinuousBoundedSilenceEventuallyDrains` |

### Probe-arbitration mutations

| Configuration | Injected defect | Observed violation |
| --- | --- | --- |
| `InspectWebWorkerProbe_MutationDuplicateWatchdogProbe.cfg` | Watchdog sends a second probe instead of adopting the control probe | `OnePhysicalProbe` |
| `InspectWebWorkerProbe_MutationOlderWatchdogCoversControl.cfg` | An older watchdog probe falsely proves a later command missing | `ProtocolFailureIsOnlyCoveredOmission` |
| `InspectWebWorkerProbe_MutationIgnoreCoveredOmission.cfg` | Acknowledgment ignores a covered omitted response | `CoveredOmissionFails` |
| `InspectWebWorkerProbe_MutationAckLeavesSuspect.cfg` | Valid acknowledgment leaves watchdog suspicion active | `ProbeAcknowledgmentClearsSuspicion` |
| `InspectWebWorkerProbe_MutationAcceptWrongAck.cfg` | Acknowledgment accepts the wrong probe sequence | `ProbeSequenceIsExact` |
| `InspectWebWorkerProbe_MutationResumeRetiresRegister.cfg` | Lifecycle recovery replaces a live probe and the old acknowledgment fails the epoch | `ProtocolFailureIsOnlyCoveredOmission` |
| `InspectWebWorkerProbe_MutationTaskEvidenceRetiresRegister.cfg` | Non-acknowledgment task evidence discards a covered omission | `CoveredOmissionFails` |

## Abstraction boundary

The models do not cover:

- operation-authority publication and logical-outcome rules;
- managed active-table admission, cancellation-token callbacks, or callback
  release;
- generated-facade initialization internals;
- concrete protocol parsing, structured-clone limits, or payload schemas;
- actual browser scheduling, timer throttling, `Worker.terminate()`, or memory
  reclamation;
- feature-owned event-loop-return checkpoints or maximum duration;
- numeric maximum arithmetic among different bounded allowances;
- prepared-binding activation after epoch closure, including its planned versus
  unexpected outcome classification;
- replacement policy beyond one stale old-epoch event racing the
  replacement's empty operation high-water; or
- arbitrary operation and epoch-work cardinality.

Those claims remain with adjacent owners and the implementation gates named by
the design.
