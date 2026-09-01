# Inspect-web worker runtime models

This directory model-checks four mechanisms owned by
[Inspect-web worker runtime](../../inspect-web-worker-runtime.md). The models
supplement the readable design. They prove nothing about TypeScript, browser
workers, clocks, .NET, generated facades, managed callbacks, or feature
implementations.

## Model split

`InspectWebWorkerValidation.tla` models registered and worker-advertised
allowances plus epoch-work inputs. It covers:

- exact allowance echo on `Accepted`;
- mismatched allowance entering protocol-failure draining rather than active
  liveness accounting;
- replayed or active-duplicate work starts and unmatched or duplicate work
  finishes entering protocol-failure draining; and
- eventual realm closure after invalid input.

`InspectWebWorkerProtocol.tla` models two operation references assigned to one
worker epoch. It covers:

- held starts and readiness-ordered dispatch;
- readiness flush excluding a later warm activation until every held start is
  posted;
- cancellation before dispatch;
- explicit acceptance or rejection;
- progress/settlement ordering through one atomic physical `Settled` record;
- cancellation acknowledgment ordering, meaning, and retention;
- serialized command-response commitment and response-probe proof of a missing
  earlier response;
- operation-sequence high-water replay rejection without completed-ID
  tombstones;
- epoch-work sequence high-water and active-set validation, including explicit
  receipt-to-failure evidence for duplicate starts and finishes;
- exact replacement binding from worker source plus epoch token, including
  valid replacement traffic and independently stale source or token; and
- failed draining, realm release, and callbacks after release.

`InspectWebWorkerLifecycle.tla` models one worker epoch with two assigned
operations. It covers:

- a non-renewable startup active-time budget;
- matching readiness as the only successful startup transition;
- lifecycle suspension and main-loop discontinuity;
- idle, accepted-operation, and epoch-work liveness;
- bounded and unbounded silence;
- task-loop evidence distinguished from progress and managed-callback activity;
- the two-stage post-readiness probe;
- immediate startup rejection, planned restart, worker-declared failure, and
  other unexpected loss;
- bounded draining, natural release, hard realm destruction, and quiescence;
  and
- source and callback revocation after release.

`InspectWebWorkerProbe.tla` composes the two probe triggers over one physical
probe register. It covers:

- watchdog adoption of an already-outstanding control-response probe;
- deferred control coverage when any outstanding probe predates the command;
- immutable response-obligation snapshots;
- exact probe acknowledgment, including independently generated mismatched and
  no-outstanding acknowledgment inputs;
- a host register that remains bound to the exact physical probe sequence
  through lifecycle recovery;
- finite task-evidence saturation that cannot disable a matching
  acknowledgment;
- maximum probe-sequence retirement entering failed draining rather than
  leaving a live epoch without another sequence;
- missing-response failure after serialized command completion;
- task-loop evidence clearing watchdog suspicion; and
- lifecycle recovery preserving an outstanding register and acknowledgment.

Keeping the models separate prevents protocol bookkeeping from multiplying
every clock state. The validation model isolates response-field and epoch-work
input validation. The protocol model abstracts time and liveness arithmetic.
The lifecycle model abstracts payload parsing, sequence replay, and operation
message inventory. The probe model isolates the cross-cutting arbitration seam
that would otherwise be abstracted differently by those models.

## Assumptions and bounds

The validation, protocol, and lifecycle models use two abstract operations.
Their identities stand for complete opaque operation-ID and numeric-sequence
pairs; they do not model string construction or feature meaning.

The protocol model assumes:

- operation A has sequence one and operation B has sequence two;
- page operation authority supplies non-reused IDs and increasing safe-integer
  sequences;
- matching readiness enters an internal flush phase in which held starts are
  posted in sequence order before the epoch becomes available to warm
  activation or cancellation;
- one `Settled` message carries both the managed terminal result and proof that
  the operation-scoped managed release barrier has completed;
- `Start`, `Cancel`, and `Probe` use one serialized command lane; operation A
  represents the one response obligation covered by the finite model's probe,
  and a same-operation later control waits until that probe retires;
- a handler can complete without its required response, after which a later
  matching probe acknowledgment supplies positive evidence of the omission;
- cancellation acknowledgment commits only after the earlier Start response;
- cancellation and settlement may race in either order;
- `AckNotActive` on an accepted wire record abstracts the managed bridge's
  settling state before physical `Settled`;
- the closed old-epoch lifecycle cell reuses its high-water value only to
  represent replacement traffic after the binding switches; one common
  receive transition compares exact source and token, records whether an
  accepted mutation came from the unmodified current binding, and exercises
  same-token stale-source and same-source stale-token negatives;
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
- worker crash has already destroyed the realm;
- bootstrap rejection is a distinct startup cause that destroys and releases
  the partial realm immediately;
- a current-source protocol fault before readiness retains its protocol
  classification but uses the same immediate partial-realm closure mechanics;
- worker-declared epoch failure refines the same unexpected-closure transition
  as other post-readiness live-realm failures while retaining its distinct
  cause; and
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
  outstanding register and nondeterministically represents matching, stale, or
  future input, so ordinary invalid-acknowledgment rejection is reachable
  without enabling a receiver mutation;
- invalid acknowledgments have distinct failure evidence from covered omitted
  responses;
- the finite task-evidence counter saturates independently from acknowledgment
  eligibility;
- retiring the maximum bounded sequence represents exhaustion of the product's
  safe-integer allocator and commits failed draining; and
- `MaxProbeSequence = 2` is a state-space bound, not a product limit.

The probe model's `protocolFailure` variable is the specific
`control-response` classification. `invalidAcknowledgmentFailure` separately
records invalid-acknowledgment protocol failure so the proof cannot substitute
one cause for the other.

The validation model assumes:

- operation A is registered as bounded and operation B as unbounded;
- `Accepted` carries exactly one of those two abstract allowance classes; and
- `MaxWorkSequence = 2` is a state-space bound, not a product limit.

Weak fairness applies to realm destruction after invalid-input-driven
draining.

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
| Cancellation acknowledgment follows committed admission | `CancellationAcknowledgmentRequiresCommittedAdmission` |
| A warm activation cannot strand an older held start | `HeldStartsRemainDispatchable` |
| Startup failure cannot overwrite a completed held cancellation | `CanceledHeldKeepsCanceledOutcome` |
| A sequence at or below high-water cannot reenter admission | `ReplayNeverReentersAdmission` |
| Probe proof of a missing response fails the epoch | `MissingResponseProofFailsEpoch` |
| Missing-response proof requires a completed omission | `MissingResponseProofRequiresCompletedOmission` |
| Probe acknowledgment cannot overtake its earlier command | `ProbeCannotOvertakeControl` |
| `not-active` cannot acknowledge a future sequence | `NotActiveRequiresReceivedSequence` |
| Epoch-work sequences do not restart or reuse | `WorkSequenceNeverReused`, `StartedWorkTracksHighWater` |
| Work finish requires a recorded start | `WorkFinishRequiresActiveStart` |
| Any invalid work start fails and drains the epoch | `InvalidWorkStartFailsEpoch` |
| Any invalid work finish fails and drains the epoch | `InvalidWorkFinishFailsEpoch` |
| Only exact replacement worker-source and token identity can mutate replacement state | `OnlyCurrentBindingMutatesReplacement` |
| No callback runs after realm release | `NoCallbackAfterRealmRelease` |
| Protocol failure leaves ready operation | `ProtocolFailureLeavesReadyState` |
| Draining eventually closes | `DrainingEventuallyCloses` |

### Lifecycle

| Design property | Model property |
| --- | --- |
| Startup messages and resume cannot renew the budget | `StartupBudgetDoesNotRenew` |
| Readiness cannot open an epoch after startup expiry | `ReadyRequiresUnexpiredStartup` |
| Only matching readiness opens the epoch | `MatchingReadyIsRequired`, `ProbeCannotSatisfyStartup`, `MismatchedReadyCannotOpenEpoch` |
| Draining refuses assignments | `DrainingRefusesAssignments` |
| Progress-like non-task messages do not renew liveness | `NonTaskMessagesDoNotRenewWatchdog` |
| First bounded expiry probes rather than terminates | `FirstWatchdogExpiryOnlyProbes` |
| Suspect state retains the issued probe | `SuspectRequiresIssuedProbe` |
| Unbounded silence cannot fail the watchdog | `UnboundedSilenceCannotFailWatchdog` |
| A main-loop gap cannot fail the worker watchdog | `MainLoopGapCannotFailWatchdog` |
| Planned restart cancels pending work | `PlannedRestartCancelsPendingOperations` |
| Unexpected loss fails pending work | `UnexpectedLossFailsPendingOperations` |
| Startup failure closes and releases the partial realm immediately | `StartupFailureClosesImmediately` |
| A current-source protocol fault before readiness closes the partial realm immediately | `PreReadyProtocolFailureClosesImmediately` |
| Pre-readiness protocol and worker-message faults retain their classifications | `PreReadyFaultClassificationIsPreserved` |
| Failed draining begins only after matching readiness | `FailedDrainingRequiresReadiness` |
| Worker-declared failure records an unexpected cause and fails pending work | `ClosureCauseDeterminesOutcome` |
| One fixed closure cause determines every affected outcome | `ClosureCauseDeterminesOutcome` |
| Quiescence follows natural or realm release | `QuiescenceRequiresPhysicalRelease` |
| Realm destruction revokes the source and leaves no live records | `RealmReleaseRevokesSource`, `ClosedEpochHasNoLiveResources` |
| No callback runs after release | `NoCallbackAfterRealmRelease` |
| Startup eventually succeeds or fails | `StartingEventuallyLeaves` |
| Draining eventually destroys the realm | `DrainingEventuallyCloses` |
| Continuous bounded silence drains despite bounded callback churn | `ContinuousBoundedSilenceEventuallyDrains` |

### Input validation

| Design property | Model property |
| --- | --- |
| Accepted allowance matches the registered declaration | `AcceptedAllowanceMatchesRegistration` |
| Mismatched allowance fails and drains the epoch | `MismatchedAllowanceFailsEpoch` |
| Active work was not already finished | `ActiveWorkWasNotFinished` |
| Finished work has a recorded start | `FinishedWorkWasStarted` |
| Any invalid work start fails and drains the epoch | `InvalidWorkStartFailsEpoch` |
| Any invalid work finish fails and drains the epoch | `InvalidWorkFinishFailsEpoch` |
| Invalid-input draining eventually closes | `DrainingEventuallyCloses` |

### Probe arbitration

| Design property | Model property |
| --- | --- |
| At most one physical probe is in flight | `OnePhysicalProbe` |
| An accepted acknowledgment must match the outstanding sequence | `ProbeSequenceIsExact` |
| A mismatched or no-outstanding acknowledgment fails the epoch | `InvalidAcknowledgmentFails` |
| A matching acknowledgment remains processable after finite evidence saturation | `MatchingProbeAcknowledgmentRemainsProcessable` |
| An older probe cannot cover a later command | `OlderProbeDoesNotCoverLaterCommand` |
| A covered omitted response fails the epoch | `CoveredOmissionFails` |
| Probe acknowledgment clears watchdog suspicion | `ProbeAcknowledgmentClearsSuspicion` |
| Lifecycle recovery preserves the exact physical outstanding register | `OutstandingRegisterMatchesPhysicalProbe` |
| `control-response` failure is limited to a covered omission | `ProtocolFailureIsOnlyCoveredOmission` |
| A control-response failure requires proof that the retiring probe covered the omitted response | `ProtocolFailureHasCoveredOmissionProof` |
| Probe exhaustion cannot leave a live epoch | `NoLiveEpochAfterProbeSequenceExhaustion` |

## Running TLC

Use the repository-pinned tools from the
[TLA+ setup runbook](../../../runbooks/tla-plus-setup.md):

```bash
TLA_TOOLS_JAR=/path/to/tla2tools.jar
cd docs/design/models/inspect-web-worker-runtime

java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers 4 -cleanup \
  -config InspectWebWorkerValidation.cfg \
  InspectWebWorkerValidation.tla

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
The repository-wide `eng/run-tla-checks.sh` gate resolves every shared-module
configuration in this directory through `eng/tla-module-overrides.txt`; keep
those mappings synchronized when adding or renaming a configuration.

The recorded runs used OpenJDK 21.0.12 and TLA+ tools 1.8.0
(`TLC2 2026.08.21.155922`, revision `9787e65`). The checked
`tla2tools.jar` SHA-256 is
`eabd140a70f49eb9305a3bd3f3df944eddf87e5a90d329789085f8953a80533a`.

## Positive results

| Configuration | Bounds | Generated | Distinct | Depth | Result |
| --- | --- | ---: | ---: | ---: | --- |
| `InspectWebWorkerValidation.cfg` | 2 operations, 2 allowance classes, `MaxWorkSequence = 2` | 685 | 477 | 11 | No error |
| `InspectWebWorkerProtocol.cfg` | 2 operations, `MaxWorkSequence = 2` | 866,584 | 222,090 | 23 | No error |
| `InspectWebWorkerLifecycle.cfg` | 2 operations, all budgets = 1 | 87,452 | 20,268 | 18 | No error |
| `InspectWebWorkerLifecycle_BoundedSilence.cfg` | No recurring task evidence or unbounded work; all budgets = 1 | 2,569 | 1,036 | 13 | No error |
| `InspectWebWorkerProbe.cfg` | 1 command, `MaxProbeSequence = 2` | 2,195 | 655 | 10 | No error |

Generated and distinct counts are stable across worker counts. The protocol
row's depth 23 is the single-worker breadth-first result; parallel runs can
report depth 23-24 because worker discovery order changes the reported maximum.

## Reachability witness

`InspectWebWorkerProbe_TaskEvidenceSaturationReachable.cfg` checks
`TaskEvidenceHasNotSaturated` and is expected to violate it. That counterexample
gates the reachable finite-bound precondition used by the
`MatchingProbeAcknowledgmentRemainsProcessable` negative control.

## Mutation results

Every checked mutation produced its named invariant or temporal-property
violation.

### Protocol mutations

| Configuration | Injected defect | Observed violation |
| --- | --- | --- |
| `InspectWebWorkerProtocolDispatchBeforeReady.cfg` | Posts a held start during startup | `NoDispatchBeforeReady` |
| `InspectWebWorkerProtocolDispatchCanceledHeld.cfg` | Posts a locally canceled held start | `CanceledHeldNeverDispatches` |
| `InspectWebWorkerProtocolAcceptMismatchedReady.cfg` | Opens on mismatched readiness | `MatchingReadyRequired` |
| `InspectWebWorkerProtocolMismatchedReadyDrains.cfg` | Mismatched readiness enters draining instead of closing the partial realm | `MatchingReadyRequired` |
| `InspectWebWorkerProtocolOverwriteCanceledHeldOnStartupFailure.cfg` | Startup failure overwrites an already completed held cancellation | `CanceledHeldKeepsCanceledOutcome` |
| `InspectWebWorkerProtocolReplayAccepted.cfg` | Re-admits a sequence at high-water | `ReplayNeverReentersAdmission` |
| `InspectWebWorkerProtocolSettleBeforeAccepted.cfg` | Settles before acceptance | `SettlementRequiresAcceptance` |
| `InspectWebWorkerProtocolDuplicateSettlement.cfg` | Emits a second settlement | `OneSettlementPerOperation` |
| `InspectWebWorkerProtocolRetireBeforeAck.cfg` | Drops a canceled record before acknowledgment | `RetirementRequiresClosureAndAcknowledgment` |
| `InspectWebWorkerProtocolCancelAckBeforeAdmission.cfg` | A cancellation acknowledgment overtakes the Start response | `CancellationAcknowledgmentRequiresCommittedAdmission` |
| `InspectWebWorkerProtocolWarmActivationBeforeHeldFlush.cfg` | A warm activation advances high-water before an older held start flushes | `HeldStartsRemainDispatchable` |
| `InspectWebWorkerProtocolIgnoreMissingResponse.cfg` | Ignores probe proof of a missing response | `MissingResponseProofFailsEpoch` |
| `InspectWebWorkerProtocolProbeOvertakesControl.cfg` | A probe overtakes unfinished cancellation for an accepted operation | `MissingResponseProofRequiresCompletedOmission` |
| `InspectWebWorkerProtocolFutureCancelNotActive.cfg` | Acknowledges a never-received future sequence | `NotActiveRequiresReceivedSequence` |
| `InspectWebWorkerProtocolReuseWorkSequence.cfg` | Restarts a completed work sequence | `WorkSequenceNeverReused` |
| `InspectWebWorkerProtocolAcceptActiveDuplicateWorkStart.cfg` | Accepts an active duplicate work start as an idempotent no-op | `InvalidWorkStartFailsEpoch` |
| `InspectWebWorkerProtocolUnmatchedWorkFinish.cfg` | Finishes work without an active lease | `WorkFinishRequiresActiveStart` |
| `InspectWebWorkerProtocolAcceptDuplicateWorkFinish.cfg` | Accepts a duplicate completed work finish as an idempotent no-op | `InvalidWorkFinishFailsEpoch` |
| `InspectWebWorkerProtocolAcceptDuringDrain.cfg` | Accepts a new assignment while draining | `DrainingRefusesStarts` |
| `InspectWebWorkerProtocolStaleEpochMutation.cfg` | Accepts a stale worker source carrying the current token | `OnlyCurrentBindingMutatesReplacement` |
| `InspectWebWorkerProtocolWrongEpochTokenMutation.cfg` | Accepts the current worker source carrying a stale token | `OnlyCurrentBindingMutatesReplacement` |
| `InspectWebWorkerProtocolCallbackAfterClose.cfg` | Delivers a callback after realm release | `NoCallbackAfterRealmRelease` |

### Lifecycle mutations

| Configuration | Injected defect | Observed violation |
| --- | --- | --- |
| `InspectWebWorkerLifecycleRenewStartupFromMessage.cfg` | A startup message renews the budget | `StartupBudgetDoesNotRenew` |
| `InspectWebWorkerLifecycleResetStartupOnResume.cfg` | Lifecycle resume resets startup | `StartupBudgetDoesNotRenew` |
| `InspectWebWorkerLifecycleProbeSatisfiesStartup.cfg` | Probe acknowledgment opens the epoch | `ProbeCannotSatisfyStartup` |
| `InspectWebWorkerLifecycleAcceptMismatchedReady.cfg` | Mismatched readiness opens the epoch | `MismatchedReadyCannotOpenEpoch` |
| `InspectWebWorkerLifecycleAcceptReadyAfterStartupExpiry.cfg` | Matching readiness opens the epoch after the startup budget expires | `ReadyRequiresUnexpiredStartup` |
| `InspectWebWorkerLifecyclePreReadyProtocolFailureDrains.cfg` | A pre-readiness protocol fault enters bounded draining | `PreReadyProtocolFailureClosesImmediately` |
| `InspectWebWorkerLifecyclePreReadyProtocolAsStartup.cfg` | A pre-readiness protocol fault is reclassified as startup failure | `PreReadyFaultClassificationIsPreserved` |
| `InspectWebWorkerLifecyclePreReadyWorkerMessageAsProtocol.cfg` | A pre-readiness worker-message fault is reclassified as protocol failure | `PreReadyFaultClassificationIsPreserved` |
| `InspectWebWorkerLifecycleTerminateAtFirstExpiry.cfg` | First silence interval terminates | `FirstWatchdogExpiryOnlyProbes` |
| `InspectWebWorkerLifecycleTerminateWhileUnbounded.cfg` | Silence kills an unbounded epoch | `UnboundedSilenceCannotFailWatchdog` |
| `InspectWebWorkerLifecycleTerminateAcrossMainGap.cfg` | A delayed main watchdog kills the worker | `MainLoopGapCannotFailWatchdog` |
| `InspectWebWorkerLifecycleAcceptDuringDrain.cfg` | Draining accepts another operation | `DrainingRefusesAssignments` |
| `InspectWebWorkerLifecyclePlannedAsFailure.cfg` | Planned restart reports failure | `PlannedRestartCancelsPendingOperations` |
| `InspectWebWorkerLifecycleUnexpectedAsCancellation.cfg` | Unexpected loss reports cancellation | `UnexpectedLossFailsPendingOperations` |
| `InspectWebWorkerLifecycleBootstrapFailureDrains.cfg` | Bootstrap rejection waits in draining instead of releasing the partial realm | `StartupFailureClosesImmediately` |
| `InspectWebWorkerLifecycleWorkerDeclaredAsCancellation.cfg` | Worker-declared failure reports cancellation | `ClosureCauseDeterminesOutcome` |
| `InspectWebWorkerLifecycleQuiesceBeforeRelease.cfg` | Quiescence precedes physical release | `QuiescenceRequiresPhysicalRelease` |
| `InspectWebWorkerLifecycleCallbackAfterRelease.cfg` | Callback survives realm release | `NoCallbackAfterRealmRelease` |
| `InspectWebWorkerLifecycleDrainNeverCloses.cfg` | Failed draining cannot destroy the realm | `DrainingEventuallyCloses` |
| `InspectWebWorkerLifecycleNonTaskMessageRenews.cfg` | Progress-like callback activity renews the watchdog | `NonTaskMessagesDoNotRenewWatchdog` |
| `InspectWebWorkerLifecycleAllowanceChurnRenews.cfg` | Bounded allowance churn repeatedly clears suspicion and renews the origin | `ContinuousBoundedSilenceEventuallyDrains` |

### Probe-arbitration mutations

| Configuration | Injected defect | Observed violation |
| --- | --- | --- |
| `InspectWebWorkerProbe_MutationDuplicateWatchdogProbe.cfg` | Watchdog sends a second probe instead of adopting the control probe | `OnePhysicalProbe` |
| `InspectWebWorkerProbe_MutationOlderWatchdogCoversControl.cfg` | An older watchdog probe falsely proves a later command missing | `ProtocolFailureIsOnlyCoveredOmission` |
| `InspectWebWorkerProbe_MutationIgnoreCoveredOmission.cfg` | Acknowledgment ignores a covered omitted response | `CoveredOmissionFails` |
| `InspectWebWorkerProbe_MutationAckLeavesSuspect.cfg` | Valid acknowledgment leaves watchdog suspicion active | `ProbeAcknowledgmentClearsSuspicion` |
| `InspectWebWorkerProbe_MutationAcceptWrongAck.cfg` | Acknowledgment accepts a mismatched or no-outstanding probe sequence | `InvalidAcknowledgmentFails` |
| `InspectWebWorkerProbe_MutationAcceptWrongAckSequence.cfg` | Acknowledgment accepts a sequence other than the physical outstanding probe | `ProbeSequenceIsExact` |
| `InspectWebWorkerProbe_MutationEvidenceBoundBlocksAck.cfg` | Finite task-evidence saturation disables a matching acknowledgment | `MatchingProbeAcknowledgmentRemainsProcessable` |
| `InspectWebWorkerProbe_MutationResumeRetiresRegister.cfg` | Lifecycle recovery replaces the outstanding probe register | `OutstandingRegisterMatchesPhysicalProbe` |
| `InspectWebWorkerProbe_MutationTaskEvidenceRetiresRegister.cfg` | Non-acknowledgment task evidence discards a covered omission | `CoveredOmissionFails` |
| `InspectWebWorkerProbe_MutationRetainAfterProbeExhaustion.cfg` | The epoch remains live after retiring its maximum probe sequence | `NoLiveEpochAfterProbeSequenceExhaustion` |
| `InspectWebWorkerProbe_MutationMisclassifyExhaustionAsProtocolFailure.cfg` | An uncovered omitted response replaces maximum-sequence exhaustion with control-response failure | `ProtocolFailureHasCoveredOmissionProof` |

### Input-validation mutations

| Configuration | Injected defect | Observed violation |
| --- | --- | --- |
| `InspectWebWorkerValidationAcceptMismatch.cfg` | Mismatched advertised allowance becomes active | `AcceptedAllowanceMatchesRegistration` |
| `InspectWebWorkerValidationAcceptReusedWork.cfg` | Replayed completed work sequence becomes active | `ActiveWorkWasNotFinished` |
| `InspectWebWorkerValidationAcceptActiveDuplicateStart.cfg` | Active duplicate work start is accepted as an idempotent no-op | `InvalidWorkStartFailsEpoch` |
| `InspectWebWorkerValidationAcceptUnmatchedFinish.cfg` | Unstarted work sequence becomes finished | `FinishedWorkWasStarted` |
| `InspectWebWorkerValidationAcceptDuplicateFinish.cfg` | Duplicate completed work finish is accepted as an idempotent no-op | `InvalidWorkFinishFailsEpoch` |

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
