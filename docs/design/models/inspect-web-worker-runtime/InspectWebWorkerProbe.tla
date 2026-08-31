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
    MUTATION_TASK_EVIDENCE_RETIRES_REGISTER

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

VARIABLES
    probeCount,
    probeKind,
    probeSequence,
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
    wrongAcknowledgmentAccepted

vars ==
    <<probeCount,
      probeKind,
      probeSequence,
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
      wrongAcknowledgmentAccepted>>

Init ==
    /\ probeCount = 0
    /\ probeKind = NoProbe
    /\ probeSequence = 0
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

UnchangedMutationFlags ==
    UNCHANGED
        <<workerReplySequence,
          probePredatesCommand,
          olderProbeCoveredLaterCommand,
          ignoredCoveredOmission,
          suspectSurvivedAcknowledgment,
          wrongAcknowledgmentAccepted>>

PostControlCommand ==
    /\ responseState = NoCommand
    /\ responseState' = PendingResponse
    /\ probePredatesCommand' = (probeCount = 1)
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
          wrongAcknowledgmentAccepted>>

CommitControlResponse ==
    /\ responseState = PendingResponse
    /\ responseState' = ResponsePresent
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
    /\ probeCount = 0
    /\ nextProbeSequence <= MaxProbeSequence
    /\ controlGraceExpired' = TRUE
    /\ probeCount' = 1
    /\ probeKind' = ControlProbe
    /\ probeSequence' = nextProbeSequence
    /\ workerReplySequence' =
        IF MUTATION_ACCEPT_WRONG_ACK
        THEN IF nextProbeSequence = 1 THEN 2 ELSE 1
        ELSE nextProbeSequence
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
          wrongAcknowledgmentAccepted>>

ExpireControlGraceBehindOutstandingProbe ==
    /\ responseState \in {PendingResponse, ResponseOmitted}
    /\ ~controlGraceExpired
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
          wrongAcknowledgmentAccepted>>

FirstWatchdogExpiryNoProbe ==
    /\ watchdogState = NormalWatchdog
    /\ ~protocolFailure
    /\ probeCount = 0
    /\ nextProbeSequence <= MaxProbeSequence
    /\ watchdogState' = SuspectWatchdog
    /\ probeCount' = 1
    /\ probeSequence' = nextProbeSequence
    /\ workerReplySequence' =
        IF MUTATION_ACCEPT_WRONG_ACK
        THEN IF nextProbeSequence = 1 THEN 2 ELSE 1
        ELSE nextProbeSequence
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
          wrongAcknowledgmentAccepted>>

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
    /\ taskEvidenceCount < MaxProbeSequence
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
    /\ workerReplySequence' = 0
    /\ coveredResponse' = FALSE
    /\ probePredatesCommand' = FALSE
    /\ taskEvidenceCount' = taskEvidenceCount + 1
    /\ UNCHANGED wrongAcknowledgmentAccepted
    /\ IF coveredResponse
          /\ responseState \in {PendingResponse, ResponseOmitted}
       THEN
           /\ IF MUTATION_IGNORE_COVERED_OMISSION
              THEN
                  /\ protocolFailure' = FALSE
                  /\ watchdogState' = NormalWatchdog
                  /\ ignoredCoveredOmission' = TRUE
                  /\ UNCHANGED suspectSurvivedAcknowledgment
              ELSE
                  /\ protocolFailure' = TRUE
                  /\ watchdogState' = DrainingWatchdog
                  /\ UNCHANGED
                      <<ignoredCoveredOmission,
                        suspectSurvivedAcknowledgment>>
       ELSE
           /\ UNCHANGED protocolFailure
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
    /\ taskEvidenceCount < MaxProbeSequence
    /\ workerReplySequence \in 1..MaxProbeSequence
    /\ workerReplySequence # probeSequence
    /\ probeCount' = 0
    /\ probeKind' = NoProbe
    /\ probeSequence' = 0
    /\ workerReplySequence' = 0
    /\ coveredResponse' = FALSE
    /\ probePredatesCommand' = FALSE
    /\ IF MUTATION_ACCEPT_WRONG_ACK
       THEN
           /\ UNCHANGED protocolFailure
           /\ watchdogState' = NormalWatchdog
           /\ taskEvidenceCount' = taskEvidenceCount + 1
           /\ wrongAcknowledgmentAccepted' = TRUE
       ELSE
           /\ protocolFailure' = TRUE
           /\ watchdogState' = DrainingWatchdog
           /\ UNCHANGED taskEvidenceCount
           /\ UNCHANGED wrongAcknowledgmentAccepted
    /\ UNCHANGED
        <<nextProbeSequence,
          responseState,
          controlGraceExpired,
          deferredControlProbe,
          olderProbeCoveredLaterCommand,
          ignoredCoveredOmission,
          suspectSurvivedAcknowledgment>>

DispatchDeferredControlProbe ==
    /\ probeCount = 0
    /\ deferredControlProbe
    /\ responseState \in {PendingResponse, ResponseOmitted}
    /\ nextProbeSequence <= MaxProbeSequence
    /\ probeCount' = 1
    /\ probeKind' = ControlProbe
    /\ probeSequence' = nextProbeSequence
    /\ workerReplySequence' =
        IF MUTATION_ACCEPT_WRONG_ACK
        THEN IF nextProbeSequence = 1 THEN 2 ELSE 1
        ELSE nextProbeSequence
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
          wrongAcknowledgmentAccepted>>

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
          responseState,
          controlGraceExpired,
          deferredControlProbe,
          protocolFailure,
          taskEvidenceCount>>
    /\ UNCHANGED
        <<olderProbeCoveredLaterCommand,
          ignoredCoveredOmission,
          suspectSurvivedAcknowledgment,
          wrongAcknowledgmentAccepted>>

OtherTaskLoopEvidence ==
    /\ watchdogState \in {NormalWatchdog, SuspectWatchdog}
    /\ probeCount = 1
    /\ taskEvidenceCount < MaxProbeSequence
    /\ watchdogState' = NormalWatchdog
    /\ taskEvidenceCount' = taskEvidenceCount + 1
    /\ IF MUTATION_TASK_EVIDENCE_RETIRES_REGISTER
       THEN
           /\ probeCount' = 0
           /\ probeKind' = NoProbe
           /\ probeSequence' = 0
           /\ workerReplySequence' = 0
           /\ coveredResponse' = FALSE
           /\ probePredatesCommand' = FALSE
       ELSE
           /\ UNCHANGED
               <<probeCount,
                 probeKind,
                 probeSequence,
                 workerReplySequence,
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
          wrongAcknowledgmentAccepted>>

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
    \/ CompleteCommandWithoutResponse
    \/ ExpireControlGraceNoProbe
    \/ ExpireControlGraceBehindOutstandingProbe
    \/ FirstWatchdogExpiryNoProbe
    \/ FirstWatchdogExpiryWithOutstandingProbe
    \/ SecondWatchdogExpiry
    \/ ReceiveProbeAcknowledgment
    \/ ReceiveMismatchedProbeAcknowledgment
    \/ DispatchDeferredControlProbe
    \/ LifecycleResume
    \/ OtherTaskLoopEvidence
    \/ RetireDeferredAfterResponse

Spec == Init /\ [][Next]_vars

TypeOK ==
    /\ probeCount \in 0..2
    /\ probeKind \in ProbeKinds
    /\ probeSequence \in 0..MaxProbeSequence
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

OnePhysicalProbe ==
    /\ probeCount <= 1
    /\ (probeCount = 0) = (probeKind = NoProbe)
    /\ (probeCount = 0) = (workerReplySequence = 0)

ProbeSequenceIsExact ==
    /\ ~wrongAcknowledgmentAccepted
    /\ probeSequence < nextProbeSequence

OlderProbeDoesNotCoverLaterCommand ==
    ~olderProbeCoveredLaterCommand

CoveredOmissionFails ==
    /\ ~ignoredCoveredOmission
    /\ (responseState = ResponseOmitted
          /\ controlGraceExpired
          /\ probeCount = 0
          /\ ~deferredControlProbe)
       => protocolFailure

ProbeAcknowledgmentClearsSuspicion ==
    ~suspectSurvivedAcknowledgment

ProtocolFailureIsOnlyCoveredOmission ==
    protocolFailure => responseState = ResponseOmitted

=============================================================================
