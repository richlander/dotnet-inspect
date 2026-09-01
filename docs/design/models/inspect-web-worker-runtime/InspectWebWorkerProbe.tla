---------------------- MODULE InspectWebWorkerProbe ----------------------
EXTENDS Naturals, TLC

\* This finite model composes the two reasons that can require a physical
\* worker probe:
\*
\* - proving completion of an earlier serialized protocol command; and
\* - the first stage of the post-readiness silence watchdog.
\*
\* One command and at most two probe sequence values are enough to exercise
\* the ordering seam. The protocol and lifecycle models own the larger state
\* machines on either side.

CONSTANTS
    MaxProbeSequence,
    MUTATION_DUPLICATE_WATCHDOG_PROBE,
    MUTATION_OLDER_WATCHDOG_COVERS_CONTROL,
    MUTATION_IGNORE_COVERED_OMISSION,
    MUTATION_ACK_LEAVES_SUSPECT,
    MUTATION_ACCEPT_WRONG_ACK,
    MUTATION_RESUME_RETIRES_REGISTER,
    MUTATION_TASK_EVIDENCE_RETIRES_REGISTER,
    MUTATION_RETAIN_AFTER_PROBE_EXHAUSTION,
    MUTATION_MISCLASSIFY_EXHAUSTION_AS_PROTOCOL_FAILURE,
    MUTATION_EVIDENCE_BOUND_BLOCKS_ACK,
    MUTATION_IGNORE_MISSING_PROBE_ACK,
    MUTATION_SERIALIZED_RESPONSE_LEAVES_SUSPECT,
    MUTATION_STALE_PROBE_MARK,
    MUTATION_DEFERRED_PROBE_STALL

ASSUME MaxProbeSequence = 2

NoProbe == "NoProbe"
ControlProbe == "ControlProbe"
WatchdogProbe == "WatchdogProbe"
CombinedProbe == "CombinedProbe"
ProbeKinds == {NoProbe, ControlProbe, WatchdogProbe, CombinedProbe}

NoCommand == "NoCommand"
PendingResponse == "PendingResponse"
ResponsePresent == "ResponsePresent"
ResponseOmitted == "ResponseOmitted"
ResponseStates ==
    {NoCommand, PendingResponse, ResponsePresent, ResponseOmitted}

NormalWatchdog == "NormalWatchdog"
SuspectWatchdog == "SuspectWatchdog"
DrainingWatchdog == "DrainingWatchdog"
WatchdogStates == {NormalWatchdog, SuspectWatchdog, DrainingWatchdog}

\* protocolFailure is specifically the control-response classification.
\* Invalid acknowledgments retain separate protocol-failure evidence.
VARIABLES
    probeCount,
    probeKind,
    probeSequence,
    inFlightProbeSequence,
    workerReplySequence,
    nextProbeSequence,
    coveredResponse,
    responseState,
    probePredatesCommand,
    controlGraceExpired,
    deferredControlProbe,
    watchdogState,
    protocolFailure,
    taskEvidenceCount,
    olderProbeCoveredLaterCommand,
    ignoredCoveredOmission,
    suspectSurvivedAcknowledgment,
    wrongAcknowledgmentAccepted,
    probeExhaustionFailure,
    invalidAcknowledgmentReceived,
    invalidAcknowledgmentFailure,
    laterSerializedResponseObserved,
    markedProbeSequence

vars ==
    <<probeCount,
      probeKind,
      probeSequence,
    inFlightProbeSequence,
    workerReplySequence,
    nextProbeSequence,
      coveredResponse,
      responseState,
      probePredatesCommand,
      controlGraceExpired,
      deferredControlProbe,
      watchdogState,
      protocolFailure,
      taskEvidenceCount,
      olderProbeCoveredLaterCommand,
      ignoredCoveredOmission,
      suspectSurvivedAcknowledgment,
      wrongAcknowledgmentAccepted,
      probeExhaustionFailure,
      invalidAcknowledgmentReceived,
      invalidAcknowledgmentFailure,
      laterSerializedResponseObserved,
      markedProbeSequence>>

Init ==
    /\ probeCount = 0
    /\ probeKind = NoProbe
    /\ probeSequence = 0
    /\ inFlightProbeSequence = 0
    /\ workerReplySequence = 0
    /\ nextProbeSequence = 1
    /\ coveredResponse = FALSE
    /\ responseState = NoCommand
    /\ probePredatesCommand = FALSE
    /\ controlGraceExpired = FALSE
    /\ deferredControlProbe = FALSE
    /\ watchdogState = NormalWatchdog
    /\ protocolFailure = FALSE
    /\ taskEvidenceCount = 0
    /\ olderProbeCoveredLaterCommand = FALSE
    /\ ignoredCoveredOmission = FALSE
    /\ suspectSurvivedAcknowledgment = FALSE
    /\ wrongAcknowledgmentAccepted = FALSE
    /\ probeExhaustionFailure = FALSE
    /\ invalidAcknowledgmentReceived = FALSE
    /\ invalidAcknowledgmentFailure = FALSE
    /\ laterSerializedResponseObserved = FALSE
    /\ markedProbeSequence = 0

SaturatingEvidenceIncrement ==
    IF taskEvidenceCount < MaxProbeSequence
    THEN taskEvidenceCount + 1
    ELSE MaxProbeSequence

UnchangedMutationFlags ==
    UNCHANGED
        <<workerReplySequence,
          probePredatesCommand,
          olderProbeCoveredLaterCommand,
          ignoredCoveredOmission,
          suspectSurvivedAcknowledgment,
          wrongAcknowledgmentAccepted,
          probeExhaustionFailure,
          invalidAcknowledgmentReceived,
          invalidAcknowledgmentFailure,
          laterSerializedResponseObserved,
          markedProbeSequence,
          inFlightProbeSequence>>

CurrentProbeMark ==
    /\ markedProbeSequence # 0
    /\ (MUTATION_STALE_PROBE_MARK
          \/ markedProbeSequence = probeSequence)

PostControlCommand ==
    /\ responseState = NoCommand
    /\ responseState' = PendingResponse
    /\ probePredatesCommand' = (probeCount = 1)
    /\ markedProbeSequence' =
        IF probeCount = 1 THEN probeSequence ELSE 0
    /\ UNCHANGED
        <<probeCount,
          probeKind,
          probeSequence,
          workerReplySequence,
          nextProbeSequence,
          coveredResponse,
          controlGraceExpired,
          deferredControlProbe,
          watchdogState,
          protocolFailure,
          taskEvidenceCount>>
    /\ UNCHANGED
        <<olderProbeCoveredLaterCommand,
          ignoredCoveredOmission,
          suspectSurvivedAcknowledgment,
          wrongAcknowledgmentAccepted,
          probeExhaustionFailure,
          invalidAcknowledgmentReceived,
          invalidAcknowledgmentFailure,
          laterSerializedResponseObserved,
          inFlightProbeSequence>>

CommitControlResponse ==
    /\ responseState = PendingResponse
    /\ ~(probeCount = 1 /\ CurrentProbeMark)
    /\ responseState' = ResponsePresent
    /\ deferredControlProbe' = FALSE
    /\ IF watchdogState \in {NormalWatchdog, SuspectWatchdog}
       THEN
           /\ taskEvidenceCount' = SaturatingEvidenceIncrement
           /\ IF MUTATION_SERIALIZED_RESPONSE_LEAVES_SUSPECT
                 /\ watchdogState = SuspectWatchdog
              THEN watchdogState' = SuspectWatchdog
              ELSE watchdogState' = NormalWatchdog
       ELSE
           /\ UNCHANGED <<watchdogState, taskEvidenceCount>>
    /\ UNCHANGED
        <<probeCount,
          probeKind,
          probeSequence,
          nextProbeSequence,
          coveredResponse,
          controlGraceExpired,
          protocolFailure>>
    /\ UnchangedMutationFlags

ReceiveLaterSerializedResponse ==
    /\ watchdogState # DrainingWatchdog
    /\ responseState = PendingResponse
    /\ probeCount = 1
    /\ CurrentProbeMark
    /\ responseState' = ResponsePresent
    /\ laterSerializedResponseObserved' = TRUE
    /\ IF MUTATION_IGNORE_MISSING_PROBE_ACK
       THEN
           /\ watchdogState' = NormalWatchdog
           /\ taskEvidenceCount' = SaturatingEvidenceIncrement
           /\ UNCHANGED protocolFailure
       ELSE
           /\ watchdogState' = DrainingWatchdog
           /\ UNCHANGED taskEvidenceCount
           /\ protocolFailure' = TRUE
    /\ UNCHANGED
        <<probeCount,
          probeKind,
          probeSequence,
          inFlightProbeSequence,
          workerReplySequence,
          nextProbeSequence,
          coveredResponse,
          probePredatesCommand,
          controlGraceExpired,
          deferredControlProbe,
          olderProbeCoveredLaterCommand,
          ignoredCoveredOmission,
          suspectSurvivedAcknowledgment,
          wrongAcknowledgmentAccepted,
          probeExhaustionFailure,
          invalidAcknowledgmentReceived,
          invalidAcknowledgmentFailure,
          markedProbeSequence>>

CompleteCommandWithoutResponse ==
    /\ responseState = PendingResponse
    /\ responseState' = ResponseOmitted
    /\ UNCHANGED
        <<probeCount,
          probeKind,
          probeSequence,
          nextProbeSequence,
          coveredResponse,
          controlGraceExpired,
          deferredControlProbe,
          watchdogState,
          protocolFailure,
          taskEvidenceCount>>
    /\ UnchangedMutationFlags

ExpireControlGraceNoProbe ==
    /\ responseState \in {PendingResponse, ResponseOmitted}
    /\ ~controlGraceExpired
    /\ watchdogState # DrainingWatchdog
    /\ probeCount = 0
    /\ nextProbeSequence <= MaxProbeSequence
    /\ controlGraceExpired' = TRUE
    /\ probeCount' = 1
    /\ probeKind' = ControlProbe
    /\ probeSequence' = nextProbeSequence
    /\ inFlightProbeSequence' = nextProbeSequence
    /\ workerReplySequence' \in 1..MaxProbeSequence
    /\ nextProbeSequence' = nextProbeSequence + 1
    /\ coveredResponse' = TRUE
    /\ probePredatesCommand' = FALSE
    /\ UNCHANGED
        <<responseState,
          deferredControlProbe,
          watchdogState,
          protocolFailure,
          taskEvidenceCount>>
    /\ UNCHANGED
        <<olderProbeCoveredLaterCommand,
          ignoredCoveredOmission,
          suspectSurvivedAcknowledgment,
          wrongAcknowledgmentAccepted,
          probeExhaustionFailure,
          invalidAcknowledgmentReceived,
          invalidAcknowledgmentFailure,
          laterSerializedResponseObserved,
          markedProbeSequence>>

ExpireControlGraceBehindOutstandingProbe ==
    /\ responseState \in {PendingResponse, ResponseOmitted}
    /\ ~controlGraceExpired
    /\ watchdogState # DrainingWatchdog
    /\ probeCount = 1
    /\ probeKind # NoProbe
    /\ ~coveredResponse
    /\ controlGraceExpired' = TRUE
    /\ IF MUTATION_OLDER_WATCHDOG_COVERS_CONTROL
       THEN
           /\ probeKind' = CombinedProbe
           /\ coveredResponse' = TRUE
           /\ deferredControlProbe' = FALSE
           /\ olderProbeCoveredLaterCommand' = TRUE
       ELSE
           /\ UNCHANGED <<probeKind, coveredResponse>>
           /\ deferredControlProbe' = TRUE
           /\ UNCHANGED olderProbeCoveredLaterCommand
    /\ UNCHANGED
        <<probeCount,
          probeSequence,
          workerReplySequence,
          nextProbeSequence,
          responseState,
          probePredatesCommand,
          watchdogState,
          protocolFailure,
          taskEvidenceCount,
          ignoredCoveredOmission,
          suspectSurvivedAcknowledgment,
          wrongAcknowledgmentAccepted,
          probeExhaustionFailure,
          invalidAcknowledgmentReceived,
          invalidAcknowledgmentFailure,
          laterSerializedResponseObserved,
          inFlightProbeSequence,
          markedProbeSequence>>

FirstWatchdogExpiryNoProbe ==
    /\ watchdogState = NormalWatchdog
    /\ ~protocolFailure
    /\ probeCount = 0
    /\ nextProbeSequence <= MaxProbeSequence
    /\ watchdogState' = SuspectWatchdog
    /\ probeCount' = 1
    /\ probeSequence' = nextProbeSequence
    /\ inFlightProbeSequence' = nextProbeSequence
    /\ workerReplySequence' \in 1..MaxProbeSequence
    /\ nextProbeSequence' = nextProbeSequence + 1
    /\ probeKind' =
        IF deferredControlProbe THEN CombinedProbe ELSE WatchdogProbe
    /\ coveredResponse' = deferredControlProbe
    /\ deferredControlProbe' = FALSE
    /\ probePredatesCommand' = FALSE
    /\ UNCHANGED
        <<responseState,
          controlGraceExpired,
          protocolFailure,
          taskEvidenceCount>>
    /\ UNCHANGED
        <<olderProbeCoveredLaterCommand,
          ignoredCoveredOmission,
          suspectSurvivedAcknowledgment,
          wrongAcknowledgmentAccepted,
          probeExhaustionFailure,
          invalidAcknowledgmentReceived,
          invalidAcknowledgmentFailure,
          laterSerializedResponseObserved,
          markedProbeSequence>>

FirstWatchdogExpiryWithOutstandingProbe ==
    /\ watchdogState = NormalWatchdog
    /\ ~protocolFailure
    /\ probeCount = 1
    /\ probeKind # NoProbe
    /\ watchdogState' = SuspectWatchdog
    /\ IF MUTATION_DUPLICATE_WATCHDOG_PROBE
       THEN
           /\ probeCount' = 2
           /\ UNCHANGED probeKind
       ELSE
           /\ probeCount' = 1
           /\ probeKind' =
               IF probeKind = ControlProbe
               THEN CombinedProbe
               ELSE probeKind
    /\ UNCHANGED
        <<probeSequence,
          nextProbeSequence,
          coveredResponse,
          responseState,
          controlGraceExpired,
          deferredControlProbe,
          protocolFailure,
          taskEvidenceCount>>
    /\ UnchangedMutationFlags

SecondWatchdogExpiry ==
    /\ watchdogState = SuspectWatchdog
    /\ probeCount = 1
    /\ ~protocolFailure
    /\ watchdogState' = DrainingWatchdog
    /\ UNCHANGED
        <<probeCount,
          probeKind,
          probeSequence,
          nextProbeSequence,
          coveredResponse,
          responseState,
          controlGraceExpired,
          deferredControlProbe,
          protocolFailure,
          taskEvidenceCount>>
    /\ UnchangedMutationFlags

ReceiveProbeAcknowledgment ==
    /\ watchdogState # DrainingWatchdog
    /\ probeCount = 1
    /\ (~MUTATION_EVIDENCE_BOUND_BLOCKS_ACK
          \/ taskEvidenceCount < MaxProbeSequence)
    /\ probeSequence = inFlightProbeSequence
    /\ workerReplySequence = probeSequence
    \* A covered command that is still unfinished prevents the serialized lane
    \* from committing the later probe acknowledgment. A mutation that lets a
    \* pre-command probe cover that command exposes the false-positive path.
    /\ ~(coveredResponse
          /\ ~probePredatesCommand
          /\ responseState = PendingResponse)
    /\ probeCount' = 0
    /\ probeKind' = NoProbe
    /\ probeSequence' = 0
    /\ inFlightProbeSequence' = 0
    /\ workerReplySequence' = 0
    /\ markedProbeSequence' =
        IF MUTATION_STALE_PROBE_MARK
        THEN markedProbeSequence
        ELSE 0
    /\ taskEvidenceCount' = SaturatingEvidenceIncrement
    /\ UNCHANGED wrongAcknowledgmentAccepted
    /\ UNCHANGED <<invalidAcknowledgmentReceived,
                   invalidAcknowledgmentFailure,
                   laterSerializedResponseObserved>>
    /\ IF coveredResponse
          /\ responseState \in {PendingResponse, ResponseOmitted}
       THEN
           /\ IF MUTATION_IGNORE_COVERED_OMISSION
              THEN
                  /\ protocolFailure' = FALSE
                  /\ watchdogState' = NormalWatchdog
                  /\ ignoredCoveredOmission' = TRUE
                  /\ coveredResponse' = FALSE
                  /\ probePredatesCommand' = FALSE
                  /\ UNCHANGED
                      <<suspectSurvivedAcknowledgment,
                        probeExhaustionFailure>>
              ELSE
                  /\ protocolFailure' = TRUE
                  /\ watchdogState' = DrainingWatchdog
                  /\ coveredResponse' = TRUE
                  /\ probePredatesCommand' = probePredatesCommand
                  /\ UNCHANGED
                      <<ignoredCoveredOmission,
                        suspectSurvivedAcknowledgment,
                        probeExhaustionFailure>>
       ELSE
           /\ IF nextProbeSequence > MaxProbeSequence
              THEN
                  /\ IF MUTATION_MISCLASSIFY_EXHAUSTION_AS_PROTOCOL_FAILURE
                        /\ responseState = ResponseOmitted
                        /\ probePredatesCommand
                     THEN
                         /\ protocolFailure' = TRUE
                         /\ watchdogState' = DrainingWatchdog
                         /\ probeExhaustionFailure' = FALSE
                         /\ coveredResponse' = FALSE
                         /\ UNCHANGED probePredatesCommand
                     ELSE
                         /\ UNCHANGED protocolFailure
                         /\ coveredResponse' = FALSE
                         /\ probePredatesCommand' = FALSE
                         /\ IF MUTATION_RETAIN_AFTER_PROBE_EXHAUSTION
                            THEN
                                /\ watchdogState' = NormalWatchdog
                                /\ probeExhaustionFailure' = FALSE
                            ELSE
                                /\ watchdogState' = DrainingWatchdog
                                /\ probeExhaustionFailure' = TRUE
                  /\ UNCHANGED suspectSurvivedAcknowledgment
              ELSE
                  /\ UNCHANGED protocolFailure
                  /\ coveredResponse' = FALSE
                  /\ probePredatesCommand' = FALSE
                  /\ probeExhaustionFailure' = FALSE
                  /\ IF watchdogState = SuspectWatchdog
                     THEN
                         /\ IF MUTATION_ACK_LEAVES_SUSPECT
                            THEN
                                /\ watchdogState' = SuspectWatchdog
                                /\ suspectSurvivedAcknowledgment' = TRUE
                            ELSE
                                /\ watchdogState' = NormalWatchdog
                                /\ UNCHANGED suspectSurvivedAcknowledgment
                     ELSE
                         /\ UNCHANGED watchdogState
                         /\ UNCHANGED suspectSurvivedAcknowledgment
           /\ UNCHANGED ignoredCoveredOmission
    /\ UNCHANGED
        <<nextProbeSequence,
          responseState,
          controlGraceExpired,
          deferredControlProbe,
          olderProbeCoveredLaterCommand>>

ReceiveMismatchedProbeAcknowledgment ==
    /\ watchdogState # DrainingWatchdog
    /\ probeCount = 1
    /\ (~MUTATION_EVIDENCE_BOUND_BLOCKS_ACK
          \/ taskEvidenceCount < MaxProbeSequence)
    /\ workerReplySequence \in 1..MaxProbeSequence
    /\ workerReplySequence # inFlightProbeSequence
    /\ probeCount' = 0
    /\ probeKind' = NoProbe
    /\ probeSequence' = 0
    /\ inFlightProbeSequence' = 0
    /\ workerReplySequence' = 0
    /\ markedProbeSequence' = 0
    /\ coveredResponse' = FALSE
    /\ probePredatesCommand' = FALSE
    /\ invalidAcknowledgmentReceived' = TRUE
    /\ IF MUTATION_ACCEPT_WRONG_ACK
       THEN
           /\ UNCHANGED protocolFailure
           /\ watchdogState' = NormalWatchdog
           /\ taskEvidenceCount' = SaturatingEvidenceIncrement
           /\ wrongAcknowledgmentAccepted' = TRUE
           /\ UNCHANGED invalidAcknowledgmentFailure
       ELSE
           /\ UNCHANGED protocolFailure
           /\ watchdogState' = DrainingWatchdog
           /\ UNCHANGED taskEvidenceCount
           /\ UNCHANGED wrongAcknowledgmentAccepted
           /\ invalidAcknowledgmentFailure' = TRUE
    /\ UNCHANGED
        <<nextProbeSequence,
          responseState,
          controlGraceExpired,
          deferredControlProbe,
          olderProbeCoveredLaterCommand,
          ignoredCoveredOmission,
          suspectSurvivedAcknowledgment,
          probeExhaustionFailure,
          laterSerializedResponseObserved>>

ReceiveUnexpectedProbeAcknowledgment ==
    /\ watchdogState # DrainingWatchdog
    /\ probeCount = 0
    /\ invalidAcknowledgmentReceived' = TRUE
    /\ IF MUTATION_ACCEPT_WRONG_ACK
       THEN
           /\ watchdogState' = NormalWatchdog
           /\ wrongAcknowledgmentAccepted' = TRUE
           /\ UNCHANGED invalidAcknowledgmentFailure
       ELSE
           /\ watchdogState' = DrainingWatchdog
           /\ UNCHANGED wrongAcknowledgmentAccepted
           /\ invalidAcknowledgmentFailure' = TRUE
    /\ UNCHANGED
        <<probeCount,
          probeKind,
          probeSequence,
          inFlightProbeSequence,
          workerReplySequence,
          nextProbeSequence,
          coveredResponse,
          responseState,
          probePredatesCommand,
          controlGraceExpired,
          deferredControlProbe,
          protocolFailure,
          taskEvidenceCount,
          olderProbeCoveredLaterCommand,
          ignoredCoveredOmission,
          suspectSurvivedAcknowledgment,
          probeExhaustionFailure,
          laterSerializedResponseObserved,
          markedProbeSequence>>

DispatchDeferredControlProbe ==
    /\ ~MUTATION_DEFERRED_PROBE_STALL
    /\ probeCount = 0
    /\ deferredControlProbe
    /\ responseState \in {PendingResponse, ResponseOmitted}
    /\ watchdogState # DrainingWatchdog
    /\ nextProbeSequence <= MaxProbeSequence
    /\ probeCount' = 1
    /\ probeKind' = ControlProbe
    /\ probeSequence' = nextProbeSequence
    /\ inFlightProbeSequence' = nextProbeSequence
    /\ workerReplySequence' \in 1..MaxProbeSequence
    /\ nextProbeSequence' = nextProbeSequence + 1
    /\ coveredResponse' = TRUE
    /\ probePredatesCommand' = FALSE
    /\ deferredControlProbe' = FALSE
    /\ UNCHANGED
        <<responseState,
          controlGraceExpired,
          watchdogState,
          protocolFailure,
          taskEvidenceCount>>
    /\ UNCHANGED
        <<olderProbeCoveredLaterCommand,
          ignoredCoveredOmission,
          suspectSurvivedAcknowledgment,
          wrongAcknowledgmentAccepted,
          probeExhaustionFailure,
          invalidAcknowledgmentReceived,
          invalidAcknowledgmentFailure,
          laterSerializedResponseObserved,
          markedProbeSequence>>

LifecycleResume ==
    /\ watchdogState = SuspectWatchdog
    /\ probeCount = 1
    /\ watchdogState' = NormalWatchdog
    /\ UNCHANGED workerReplySequence
    /\ IF MUTATION_RESUME_RETIRES_REGISTER
       THEN
           /\ nextProbeSequence <= MaxProbeSequence
           /\ probeKind' = WatchdogProbe
           /\ probeSequence' = nextProbeSequence
           /\ nextProbeSequence' = nextProbeSequence + 1
           /\ coveredResponse' = FALSE
           /\ probePredatesCommand' = FALSE
       ELSE
           /\ UNCHANGED
               <<probeKind,
                 probeSequence,
                 nextProbeSequence,
                 coveredResponse,
                 probePredatesCommand>>
    /\ UNCHANGED
        <<probeCount,
          inFlightProbeSequence,
          responseState,
          controlGraceExpired,
          deferredControlProbe,
          protocolFailure,
          taskEvidenceCount>>
    /\ UNCHANGED
        <<olderProbeCoveredLaterCommand,
          ignoredCoveredOmission,
          suspectSurvivedAcknowledgment,
          wrongAcknowledgmentAccepted,
          probeExhaustionFailure,
          invalidAcknowledgmentReceived,
          invalidAcknowledgmentFailure,
          laterSerializedResponseObserved,
          markedProbeSequence>>

HeartbeatEvidence ==
    /\ watchdogState \in {NormalWatchdog, SuspectWatchdog}
    /\ probeCount = 1
    /\ watchdogState' = NormalWatchdog
    /\ taskEvidenceCount' = SaturatingEvidenceIncrement
    /\ IF MUTATION_TASK_EVIDENCE_RETIRES_REGISTER
       THEN
           /\ probeCount' = 0
           /\ probeKind' = NoProbe
           /\ probeSequence' = 0
           /\ inFlightProbeSequence' = 0
           /\ workerReplySequence' = 0
           /\ markedProbeSequence' = 0
           /\ coveredResponse' = FALSE
           /\ probePredatesCommand' = FALSE
       ELSE
           /\ UNCHANGED
               <<probeCount,
                 probeKind,
                 probeSequence,
                 inFlightProbeSequence,
                 workerReplySequence,
                 markedProbeSequence,
                 coveredResponse,
                 probePredatesCommand>>
    /\ UNCHANGED
        <<nextProbeSequence,
          responseState,
          controlGraceExpired,
          deferredControlProbe,
          protocolFailure>>
    /\ UNCHANGED
        <<olderProbeCoveredLaterCommand,
          ignoredCoveredOmission,
          suspectSurvivedAcknowledgment,
          wrongAcknowledgmentAccepted,
          probeExhaustionFailure,
          invalidAcknowledgmentReceived,
          invalidAcknowledgmentFailure,
          laterSerializedResponseObserved>>

RetireDeferredAfterResponse ==
    /\ deferredControlProbe
    /\ responseState = ResponsePresent
    /\ deferredControlProbe' = FALSE
    /\ UNCHANGED
        <<probeCount,
          probeKind,
          probeSequence,
          nextProbeSequence,
          coveredResponse,
          responseState,
          controlGraceExpired,
          watchdogState,
          protocolFailure,
          taskEvidenceCount>>
    /\ UnchangedMutationFlags

Next ==
    \/ PostControlCommand
    \/ CommitControlResponse
    \/ ReceiveLaterSerializedResponse
    \/ CompleteCommandWithoutResponse
    \/ ExpireControlGraceNoProbe
    \/ ExpireControlGraceBehindOutstandingProbe
    \/ FirstWatchdogExpiryNoProbe
    \/ FirstWatchdogExpiryWithOutstandingProbe
    \/ SecondWatchdogExpiry
    \/ ReceiveProbeAcknowledgment
    \/ ReceiveMismatchedProbeAcknowledgment
    \/ ReceiveUnexpectedProbeAcknowledgment
    \/ DispatchDeferredControlProbe
    \/ LifecycleResume
    \/ HeartbeatEvidence
    \/ RetireDeferredAfterResponse

Spec ==
    /\ Init
    /\ [][Next]_vars
    /\ WF_vars(DispatchDeferredControlProbe)

TypeOK ==
    /\ probeCount \in 0..2
    /\ probeKind \in ProbeKinds
    /\ probeSequence \in 0..MaxProbeSequence
    /\ inFlightProbeSequence \in 0..MaxProbeSequence
    /\ workerReplySequence \in 0..MaxProbeSequence
    /\ nextProbeSequence \in 1..(MaxProbeSequence + 1)
    /\ coveredResponse \in BOOLEAN
    /\ responseState \in ResponseStates
    /\ probePredatesCommand \in BOOLEAN
    /\ controlGraceExpired \in BOOLEAN
    /\ deferredControlProbe \in BOOLEAN
    /\ watchdogState \in WatchdogStates
    /\ protocolFailure \in BOOLEAN
    /\ taskEvidenceCount \in 0..MaxProbeSequence
    /\ olderProbeCoveredLaterCommand \in BOOLEAN
    /\ ignoredCoveredOmission \in BOOLEAN
    /\ suspectSurvivedAcknowledgment \in BOOLEAN
    /\ wrongAcknowledgmentAccepted \in BOOLEAN
    /\ probeExhaustionFailure \in BOOLEAN
    /\ invalidAcknowledgmentReceived \in BOOLEAN
    /\ invalidAcknowledgmentFailure \in BOOLEAN
    /\ laterSerializedResponseObserved \in BOOLEAN
    /\ markedProbeSequence \in 0..MaxProbeSequence

OnePhysicalProbe ==
    /\ probeCount <= 1
    /\ (probeCount = 0) = (probeKind = NoProbe)
    /\ (probeCount = 0) = (workerReplySequence = 0)
    /\ (probeCount = 0) = (inFlightProbeSequence = 0)

ProbeSequenceIsExact ==
    /\ ~wrongAcknowledgmentAccepted
    /\ probeSequence < nextProbeSequence

MatchingProbeAcknowledgmentRemainsProcessable ==
    /\ watchdogState # DrainingWatchdog
    /\ probeCount = 1
    /\ workerReplySequence = probeSequence
    /\ ~(coveredResponse
          /\ ~probePredatesCommand
          /\ responseState = PendingResponse)
    =>
    ENABLED ReceiveProbeAcknowledgment

InvalidAcknowledgmentFails ==
    invalidAcknowledgmentReceived
    =>
    /\ invalidAcknowledgmentFailure
    /\ watchdogState = DrainingWatchdog

LaterSerializedResponseProvesMissingProbeAcknowledgment ==
    laterSerializedResponseObserved
    =>
    /\ protocolFailure
    /\ watchdogState = DrainingWatchdog

OutstandingRegisterMatchesPhysicalProbe ==
    probeCount = 1 => probeSequence = inFlightProbeSequence

ProbeMarkMatchesOutstandingRegister ==
    \/ markedProbeSequence = 0
    \/ /\ probeCount = 1
       /\ markedProbeSequence = probeSequence

TaskEvidenceHasNotSaturated ==
    taskEvidenceCount < MaxProbeSequence

OlderProbeDoesNotCoverLaterCommand ==
    ~olderProbeCoveredLaterCommand

CoveredOmissionFails ==
    /\ ~ignoredCoveredOmission
    /\ (responseState = ResponseOmitted
          /\ controlGraceExpired
          /\ probeCount = 0
          /\ ~deferredControlProbe
          /\ ~invalidAcknowledgmentReceived)
       => protocolFailure

ProbeAcknowledgmentClearsSuspicion ==
    ~suspectSurvivedAcknowledgment

ControlResponseFailureHasKnownOmission ==
    protocolFailure
    =>
    \/ responseState = ResponseOmitted
    \/ /\ probeCount = 1
       /\ markedProbeSequence # 0
       /\ markedProbeSequence = probeSequence
       /\ responseState = ResponsePresent

ControlResponseFailureHasProof ==
    protocolFailure
    =>
    \/ /\ coveredResponse
       /\ ~probePredatesCommand
    \/ /\ probeCount = 1
       /\ markedProbeSequence # 0
       /\ markedProbeSequence = probeSequence
       /\ responseState = ResponsePresent

OrdinarySerializedResponseClearsSuspicion ==
    [][(/\ responseState = PendingResponse
         /\ responseState' = ResponsePresent
         /\ ~(probeCount = 1 /\ CurrentProbeMark)
         /\ watchdogState = SuspectWatchdog)
       => watchdogState' = NormalWatchdog]_vars

DeferredControlProbeEventuallyResolves ==
    /\ deferredControlProbe
    /\ probeCount = 0
    ~>
    \/ ~deferredControlProbe
    \/ watchdogState = DrainingWatchdog

NoLiveEpochAfterProbeSequenceExhaustion ==
    /\ nextProbeSequence > MaxProbeSequence
    /\ probeCount = 0
    =>
    \/ probeExhaustionFailure
    \/ watchdogState = DrainingWatchdog

=============================================================================
