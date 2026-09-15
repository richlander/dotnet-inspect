---------------------- MODULE InspectWebWorkerProtocol ----------------------
(***************************************************************************)
(* Finite protocol model for docs/design/inspect-web-worker-runtime.md.     *)
(*                                                                         *)
(* The model covers two operation references assigned to one worker epoch: *)
(* held starts, readiness flush, admission ordering, cancellation response, *)
(* atomic physical settlement, bounded replay evidence, epoch-work identity,*)
(* response-probe proof, draining, realm release, and replacement binding.  *)
(*                                                                         *)
(* It deliberately abstracts feature payloads, DOM authority, managed body  *)
(* behavior, clocks, watchdog allowance arithmetic, and facade generation.  *)
(***************************************************************************)
EXTENDS FiniteSets, Naturals, TLC

CONSTANTS
    OperationA,
    OperationB,
    EpochCurrent,
    EpochStale,
    MaxWorkSequence,
    Mutation

Operations == {OperationA, OperationB}

OperationSequence(o) == IF o = OperationA THEN 1 ELSE 2

Starting == "Starting"
Flushing == "Flushing"
Ready == "Ready"
Draining == "Draining"
Closed == "Closed"
EpochStates == {Starting, Flushing, Ready, Draining, Closed}

NoRecord == "NoRecord"
Held == "Held"
AwaitingAdmission == "AwaitingAdmission"
AdmissionResponseOmitted == "AdmissionResponseOmitted"
Accepted == "Accepted"
Rejected == "Rejected"
Settled == "Settled"
Retired == "Retired"
RecordPhases ==
    {NoRecord,
     Held,
     AwaitingAdmission,
     AdmissionResponseOmitted,
     Accepted,
     Rejected,
     Settled,
     Retired}

NoOutcome == "NoOutcome"
SucceededOutcome == "SucceededOutcome"
FailedOutcome == "FailedOutcome"
CanceledOutcome == "CanceledOutcome"
Outcomes == {NoOutcome, SucceededOutcome, FailedOutcome, CanceledOutcome}

NoAck == "NoAck"
AckRunning == "AckRunning"
AckNotActive == "AckNotActive"
AckOmitted == "AckOmitted"
CancelAcks == {NoAck, AckRunning, AckNotActive, AckOmitted}
CommittedCancelAcks == {AckRunning, AckNotActive}

NoMutation == "None"
DispatchBeforeReady == "DispatchBeforeReady"
DispatchCanceledHeld == "DispatchCanceledHeld"
AcceptMismatchedReady == "AcceptMismatchedReady"
MismatchedReadyDrains == "MismatchedReadyDrains"
ReplayAccepted == "ReplayAccepted"
SettleBeforeAccepted == "SettleBeforeAccepted"
DuplicateSettlement == "DuplicateSettlement"
RetireBeforeAck == "RetireBeforeAck"
IgnoreMissingResponse == "IgnoreMissingResponse"
ProbeOvertakesControl == "ProbeOvertakesControl"
FutureCancelNotActive == "FutureCancelNotActive"
ReuseWorkSequence == "ReuseWorkSequence"
AcceptActiveDuplicateWorkStart == "AcceptActiveDuplicateWorkStart"
UnmatchedWorkFinish == "UnmatchedWorkFinish"
AcceptDuplicateWorkFinish == "AcceptDuplicateWorkFinish"
AcceptDuringDrain == "AcceptDuringDrain"
StaleEpochMutation == "StaleEpochMutation"
WrongEpochTokenMutation == "WrongEpochTokenMutation"
CallbackAfterClose == "CallbackAfterClose"
CancelAckBeforeAdmission == "CancelAckBeforeAdmission"
WarmActivationBeforeHeldFlush == "WarmActivationBeforeHeldFlush"
OverwriteCanceledHeldOnStartupFailure ==
    "OverwriteCanceledHeldOnStartupFailure"
Mutations ==
    {NoMutation,
     DispatchBeforeReady,
     DispatchCanceledHeld,
     AcceptMismatchedReady,
     MismatchedReadyDrains,
     ReplayAccepted,
     SettleBeforeAccepted,
     DuplicateSettlement,
     RetireBeforeAck,
     IgnoreMissingResponse,
     ProbeOvertakesControl,
     FutureCancelNotActive,
     ReuseWorkSequence,
     AcceptActiveDuplicateWorkStart,
     UnmatchedWorkFinish,
     AcceptDuplicateWorkFinish,
     AcceptDuringDrain,
     StaleEpochMutation,
     WrongEpochTokenMutation,
     CallbackAfterClose,
     CancelAckBeforeAdmission,
     WarmActivationBeforeHeldFlush,
     OverwriteCanceledHeldOnStartupFailure}

ASSUME
    /\ Cardinality(Operations) = 2
    /\ OperationA # OperationB
    /\ EpochCurrent # EpochStale
    /\ MaxWorkSequence \in Nat
    /\ Mutation \in Mutations

VARIABLES
    epochState,
    readyMatched,
    recordPhase,
    acceptedEver,
    rejectedEver,
    dispatched,
    canceledHeld,
    operationHighWater,
    outcome,
    quiesced,
    cancelSent,
    cancelAck,
    settlementCount,
    responseProbeOutstanding,
    missingResponseProven,
    probeOvertakeObserved,
    protocolFailure,
    sourceRevoked,
    startDuringDrain,
    dispatchBeforeReadyObserved,
    replayAcceptedObserved,
    settlementBeforeAcceptanceObserved,
    retirementBeforeAckObserved,
    futureNotActiveObserved,
    workHighWater,
    activeWork,
    startedWork,
    finishedWork,
    workSequenceReuseObserved,
    unmatchedWorkFinishObserved,
    invalidWorkStartObserved,
    invalidWorkFinishObserved,
    replacementOpen,
    staleEpochChangedState,
    callbackAfterCloseObserved

vars ==
    <<epochState,
      readyMatched,
      recordPhase,
      acceptedEver,
      rejectedEver,
      dispatched,
      canceledHeld,
      operationHighWater,
      outcome,
      quiesced,
      cancelSent,
      cancelAck,
      settlementCount,
      responseProbeOutstanding,
      missingResponseProven,
      probeOvertakeObserved,
      protocolFailure,
      sourceRevoked,
      startDuringDrain,
      dispatchBeforeReadyObserved,
      replayAcceptedObserved,
      settlementBeforeAcceptanceObserved,
      retirementBeforeAckObserved,
      futureNotActiveObserved,
      workHighWater,
      activeWork,
      startedWork,
      finishedWork,
      workSequenceReuseObserved,
      unmatchedWorkFinishObserved,
      invalidWorkStartObserved,
      invalidWorkFinishObserved,
      replacementOpen,
      staleEpochChangedState,
      callbackAfterCloseObserved>>

Init ==
    /\ epochState = Starting
    /\ readyMatched = FALSE
    /\ recordPhase = [o \in Operations |-> NoRecord]
    /\ acceptedEver = {}
    /\ rejectedEver = {}
    /\ dispatched = {}
    /\ canceledHeld = {}
    /\ operationHighWater = 0
    /\ outcome = [o \in Operations |-> NoOutcome]
    /\ quiesced = {}
    /\ cancelSent = {}
    /\ cancelAck = [o \in Operations |-> NoAck]
    /\ settlementCount = [o \in Operations |-> 0]
    /\ responseProbeOutstanding = FALSE
    /\ missingResponseProven = FALSE
    /\ probeOvertakeObserved = FALSE
    /\ protocolFailure = FALSE
    /\ sourceRevoked = FALSE
    /\ startDuringDrain = FALSE
    /\ dispatchBeforeReadyObserved = FALSE
    /\ replayAcceptedObserved = FALSE
    /\ settlementBeforeAcceptanceObserved = FALSE
    /\ retirementBeforeAckObserved = FALSE
    /\ futureNotActiveObserved = FALSE
    /\ workHighWater = 0
    /\ activeWork = {}
    /\ startedWork = {}
    /\ finishedWork = {}
    /\ workSequenceReuseObserved = FALSE
    /\ unmatchedWorkFinishObserved = FALSE
    /\ invalidWorkStartObserved = FALSE
    /\ invalidWorkFinishObserved = FALSE
    /\ replacementOpen = FALSE
    /\ staleEpochChangedState = FALSE
    /\ callbackAfterCloseObserved = FALSE

UnchangedProtocolFlags ==
    UNCHANGED
        <<startDuringDrain,
          dispatchBeforeReadyObserved,
          replayAcceptedObserved,
          settlementBeforeAcceptanceObserved,
          retirementBeforeAckObserved,
          probeOvertakeObserved,
          futureNotActiveObserved,
          workSequenceReuseObserved,
          unmatchedWorkFinishObserved,
          invalidWorkStartObserved,
          invalidWorkFinishObserved,
          replacementOpen,
          staleEpochChangedState,
          callbackAfterCloseObserved>>

UnchangedWork ==
    UNCHANGED
        <<workHighWater, activeWork, startedWork, finishedWork>>

OldWorkerSource == "OldWorkerSource"
ReplacementWorkerSource == "ReplacementWorkerSource"

CurrentWorkerSource ==
    IF replacementOpen THEN ReplacementWorkerSource ELSE OldWorkerSource

CurrentEpochToken ==
    IF replacementOpen THEN EpochCurrent ELSE EpochStale

ExactMessageIsCurrent(workerSource, epochToken) ==
    /\ workerSource = CurrentWorkerSource
    /\ epochToken = CurrentEpochToken

MessageIsCurrent(workerSource, epochToken) ==
    /\ (Mutation = StaleEpochMutation
          \/ workerSource = CurrentWorkerSource)
    /\ (Mutation = WrongEpochTokenMutation
          \/ epochToken = CurrentEpochToken)

MissingRequiredResponseForA ==
    \/ recordPhase[OperationA]
       \in {AwaitingAdmission, AdmissionResponseOmitted}
    \/ /\ OperationA \in cancelSent
       /\ cancelAck[OperationA] \in {NoAck, AckOmitted}

OmittedResponseForA ==
    \/ recordPhase[OperationA] = AdmissionResponseOmitted
    \/ cancelAck[OperationA] = AckOmitted

ActivateHeld(o) ==
    /\ epochState = Starting
    /\ recordPhase[o] = NoRecord
    /\ recordPhase' = [recordPhase EXCEPT ![o] = Held]
    /\ UNCHANGED
        <<epochState,
          readyMatched,
          acceptedEver,
          rejectedEver,
          dispatched,
          canceledHeld,
          operationHighWater,
          outcome,
          quiesced,
          cancelSent,
          cancelAck,
          settlementCount,
          responseProbeOutstanding,
          missingResponseProven,
          protocolFailure,
          sourceRevoked>>
    /\ UnchangedWork
    /\ UnchangedProtocolFlags

WorkerOmitsStartResponse(o) ==
    /\ epochState = Ready
    /\ ~sourceRevoked
    /\ recordPhase[o] = AwaitingAdmission
    /\ recordPhase' = [recordPhase EXCEPT ![o] = AdmissionResponseOmitted]
    /\ UNCHANGED
        <<epochState,
          readyMatched,
          acceptedEver,
          rejectedEver,
          dispatched,
          canceledHeld,
          operationHighWater,
          outcome,
          quiesced,
          cancelSent,
          cancelAck,
          settlementCount,
          responseProbeOutstanding,
          missingResponseProven,
          protocolFailure,
          sourceRevoked>>
    /\ UnchangedWork
    /\ UnchangedProtocolFlags

CancelHeld(o) ==
    /\ epochState = Starting
    /\ recordPhase[o] = Held
    /\ recordPhase' = [recordPhase EXCEPT ![o] = Retired]
    /\ canceledHeld' = canceledHeld \cup {o}
    /\ outcome' = [outcome EXCEPT ![o] = CanceledOutcome]
    /\ quiesced' = quiesced \cup {o}
    /\ UNCHANGED
        <<epochState,
          readyMatched,
          acceptedEver,
          rejectedEver,
          dispatched,
          operationHighWater,
          cancelSent,
          cancelAck,
          settlementCount,
          responseProbeOutstanding,
          missingResponseProven,
          protocolFailure,
          sourceRevoked>>
    /\ UnchangedWork
    /\ UnchangedProtocolFlags

WorkerOmitsCancelAck(o) ==
    /\ epochState \in {Ready, Draining}
    /\ ~sourceRevoked
    /\ o \in cancelSent
    /\ cancelAck[o] = NoAck
    /\ cancelAck' = [cancelAck EXCEPT ![o] = AckOmitted]
    /\ UNCHANGED
        <<epochState,
          readyMatched,
          recordPhase,
          acceptedEver,
          rejectedEver,
          dispatched,
          canceledHeld,
          operationHighWater,
          outcome,
          quiesced,
          cancelSent,
          settlementCount,
          responseProbeOutstanding,
          missingResponseProven,
          protocolFailure,
          sourceRevoked>>
    /\ UnchangedWork
    /\ UnchangedProtocolFlags

ReceiveReady ==
    /\ epochState = Starting
    /\ ~sourceRevoked
    /\ epochState' = Flushing
    /\ readyMatched' = TRUE
    /\ UNCHANGED
        <<recordPhase,
          acceptedEver,
          rejectedEver,
          dispatched,
          canceledHeld,
          operationHighWater,
          outcome,
          quiesced,
          cancelSent,
          cancelAck,
          settlementCount,
          responseProbeOutstanding,
          missingResponseProven,
          protocolFailure,
          sourceRevoked>>
    /\ UnchangedWork
    /\ UnchangedProtocolFlags

ProbeOvertakesControlMutation ==
    /\ Mutation = ProbeOvertakesControl
    /\ epochState = Ready
    /\ ~sourceRevoked
    /\ responseProbeOutstanding
    /\ recordPhase[OperationA] = Accepted
    /\ OperationA \in cancelSent
    /\ cancelAck[OperationA] = NoAck
    /\ MissingRequiredResponseForA
    /\ ~OmittedResponseForA
    /\ responseProbeOutstanding' = FALSE
    /\ missingResponseProven' = TRUE
    /\ probeOvertakeObserved' = TRUE
    /\ epochState' = Draining
    /\ protocolFailure' = TRUE
    /\ UNCHANGED
        <<readyMatched,
          recordPhase,
          acceptedEver,
          rejectedEver,
          dispatched,
          canceledHeld,
          operationHighWater,
          outcome,
          quiesced,
          cancelSent,
          cancelAck,
          settlementCount,
          sourceRevoked,
          startDuringDrain,
          dispatchBeforeReadyObserved,
          replayAcceptedObserved,
          settlementBeforeAcceptanceObserved,
          retirementBeforeAckObserved,
          futureNotActiveObserved,
          workHighWater,
          activeWork,
          startedWork,
          finishedWork,
          workSequenceReuseObserved,
          unmatchedWorkFinishObserved,
          invalidWorkStartObserved,
          invalidWorkFinishObserved,
          replacementOpen,
          staleEpochChangedState,
          callbackAfterCloseObserved>>

ReceiveMismatchedReady ==
    /\ epochState = Starting
    /\ ~sourceRevoked
    /\ IF Mutation \in {AcceptMismatchedReady, MismatchedReadyDrains}
       THEN
           /\ epochState' =
               IF Mutation = AcceptMismatchedReady THEN Ready ELSE Draining
           /\ readyMatched' = FALSE
           /\ protocolFailure' = (Mutation = MismatchedReadyDrains)
           /\ UNCHANGED
               <<recordPhase,
                 outcome,
                 quiesced,
                 sourceRevoked>>
       ELSE
           /\ epochState' = Closed
           /\ readyMatched' = FALSE
           /\ recordPhase' =
               [o \in Operations |->
                   IF recordPhase[o] = NoRecord
                   THEN NoRecord
                   ELSE Retired]
           /\ outcome' =
               [o \in Operations |->
                   IF Mutation = OverwriteCanceledHeldOnStartupFailure
                      /\ recordPhase[o] # NoRecord
                   THEN FailedOutcome
                   ELSE
                       IF outcome[o] = NoOutcome
                          /\ recordPhase[o] # NoRecord
                       THEN FailedOutcome
                       ELSE outcome[o]]
           /\ quiesced' =
               quiesced \cup
                   {o \in Operations: recordPhase[o] # NoRecord}
           /\ protocolFailure' = FALSE
           /\ sourceRevoked' = TRUE
    /\ UNCHANGED
        <<acceptedEver,
          rejectedEver,
          dispatched,
          canceledHeld,
          operationHighWater,
          cancelSent,
          cancelAck,
          settlementCount,
          responseProbeOutstanding,
          missingResponseProven>>
    /\ UnchangedWork
    /\ UnchangedProtocolFlags

HeldInSequenceOrder(o) ==
    \A p \in Operations:
        recordPhase[p] = Held => OperationSequence(o) <= OperationSequence(p)

DispatchHeld(o) ==
    /\ recordPhase[o] = Held
    /\ HeldInSequenceOrder(o)
    /\ IF epochState = Flushing
       THEN
           /\ dispatchBeforeReadyObserved' = dispatchBeforeReadyObserved
       ELSE
           /\ Mutation = DispatchBeforeReady
           /\ epochState = Starting
           /\ dispatchBeforeReadyObserved' = TRUE
    /\ OperationSequence(o) > operationHighWater
    /\ recordPhase' = [recordPhase EXCEPT ![o] = AwaitingAdmission]
    /\ dispatched' = dispatched \cup {o}
    /\ operationHighWater' = OperationSequence(o)
    /\ UNCHANGED
        <<epochState,
          readyMatched,
          acceptedEver,
          rejectedEver,
          canceledHeld,
          outcome,
          quiesced,
          cancelSent,
          cancelAck,
          settlementCount,
          responseProbeOutstanding,
          missingResponseProven,
          protocolFailure,
          sourceRevoked,
          startDuringDrain,
          replayAcceptedObserved,
          settlementBeforeAcceptanceObserved,
          retirementBeforeAckObserved,
          probeOvertakeObserved,
          futureNotActiveObserved,
          workSequenceReuseObserved,
          unmatchedWorkFinishObserved,
          invalidWorkStartObserved,
          invalidWorkFinishObserved,
          replacementOpen,
          staleEpochChangedState,
          callbackAfterCloseObserved>>
    /\ UnchangedWork

FinishReadyFlush ==
    /\ epochState = Flushing
    /\ IF Mutation = WarmActivationBeforeHeldFlush
       THEN TRUE
       ELSE \A o \in Operations: recordPhase[o] # Held
    /\ epochState' = Ready
    /\ UNCHANGED
        <<readyMatched,
          recordPhase,
          acceptedEver,
          rejectedEver,
          dispatched,
          canceledHeld,
          operationHighWater,
          outcome,
          quiesced,
          cancelSent,
          cancelAck,
          settlementCount,
          responseProbeOutstanding,
          missingResponseProven,
          protocolFailure,
          sourceRevoked>>
    /\ UnchangedWork
    /\ UnchangedProtocolFlags

DispatchCanceledHeldMutation(o) ==
    /\ Mutation = DispatchCanceledHeld
    /\ epochState = Ready
    /\ o \in canceledHeld
    /\ o \notin dispatched
    /\ dispatched' = dispatched \cup {o}
    /\ recordPhase' = [recordPhase EXCEPT ![o] = AwaitingAdmission]
    /\ operationHighWater' = OperationSequence(o)
    /\ UNCHANGED
        <<epochState,
          readyMatched,
          acceptedEver,
          rejectedEver,
          canceledHeld,
          outcome,
          quiesced,
          cancelSent,
          cancelAck,
          settlementCount,
          responseProbeOutstanding,
          missingResponseProven,
          protocolFailure,
          sourceRevoked,
          startDuringDrain,
          dispatchBeforeReadyObserved,
          replayAcceptedObserved,
          settlementBeforeAcceptanceObserved,
          retirementBeforeAckObserved,
          probeOvertakeObserved,
          futureNotActiveObserved,
          workSequenceReuseObserved,
          unmatchedWorkFinishObserved,
          invalidWorkStartObserved,
          invalidWorkFinishObserved,
          replacementOpen,
          staleEpochChangedState,
          callbackAfterCloseObserved>>
    /\ UnchangedWork

ActivateReady(o) ==
    /\ epochState = Ready
    /\ recordPhase[o] = NoRecord
    /\ \A p \in Operations: recordPhase[p] # Held
    /\ OperationSequence(o) > operationHighWater
    /\ recordPhase' = [recordPhase EXCEPT ![o] = AwaitingAdmission]
    /\ dispatched' = dispatched \cup {o}
    /\ operationHighWater' = OperationSequence(o)
    /\ UNCHANGED
        <<epochState,
          readyMatched,
          acceptedEver,
          rejectedEver,
          canceledHeld,
          outcome,
          quiesced,
          cancelSent,
          cancelAck,
          settlementCount,
          responseProbeOutstanding,
          missingResponseProven,
          protocolFailure,
          sourceRevoked>>
    /\ UnchangedWork
    /\ UnchangedProtocolFlags

WarmActivationBeforeHeldFlushMutation(o) ==
    /\ Mutation = WarmActivationBeforeHeldFlush
    /\ epochState = Ready
    /\ recordPhase[o] = NoRecord
    /\ \E p \in Operations:
           /\ recordPhase[p] = Held
           /\ OperationSequence(p) < OperationSequence(o)
    /\ OperationSequence(o) > operationHighWater
    /\ recordPhase' = [recordPhase EXCEPT ![o] = AwaitingAdmission]
    /\ dispatched' = dispatched \cup {o}
    /\ operationHighWater' = OperationSequence(o)
    /\ UNCHANGED
        <<epochState,
          readyMatched,
          acceptedEver,
          rejectedEver,
          canceledHeld,
          outcome,
          quiesced,
          cancelSent,
          cancelAck,
          settlementCount,
          responseProbeOutstanding,
          missingResponseProven,
          protocolFailure,
          sourceRevoked>>
    /\ UnchangedWork
    /\ UnchangedProtocolFlags

AcceptDuringDrainMutation(o) ==
    /\ Mutation = AcceptDuringDrain
    /\ epochState = Draining
    /\ recordPhase[o] = NoRecord
    /\ recordPhase' = [recordPhase EXCEPT ![o] = AwaitingAdmission]
    /\ startDuringDrain' = TRUE
    /\ UNCHANGED
        <<epochState,
          readyMatched,
          acceptedEver,
          rejectedEver,
          dispatched,
          canceledHeld,
          operationHighWater,
          outcome,
          quiesced,
          cancelSent,
          cancelAck,
          settlementCount,
          responseProbeOutstanding,
          missingResponseProven,
          protocolFailure,
          sourceRevoked,
          dispatchBeforeReadyObserved,
          replayAcceptedObserved,
          settlementBeforeAcceptanceObserved,
          retirementBeforeAckObserved,
          probeOvertakeObserved,
          futureNotActiveObserved,
          workSequenceReuseObserved,
          unmatchedWorkFinishObserved,
          invalidWorkStartObserved,
          invalidWorkFinishObserved,
          replacementOpen,
          staleEpochChangedState,
          callbackAfterCloseObserved>>
    /\ UnchangedWork

WorkerAccept(o) ==
    /\ epochState = Ready
    /\ ~sourceRevoked
    /\ recordPhase[o] = AwaitingAdmission
    /\ recordPhase' = [recordPhase EXCEPT ![o] = Accepted]
    /\ acceptedEver' = acceptedEver \cup {o}
    /\ UNCHANGED
        <<epochState,
          readyMatched,
          rejectedEver,
          dispatched,
          canceledHeld,
          operationHighWater,
          outcome,
          quiesced,
          cancelSent,
          cancelAck,
          settlementCount,
          responseProbeOutstanding,
          missingResponseProven,
          protocolFailure,
          sourceRevoked>>
    /\ UnchangedWork
    /\ UnchangedProtocolFlags

WorkerReject(o) ==
    /\ epochState = Ready
    /\ ~sourceRevoked
    /\ recordPhase[o] = AwaitingAdmission
    /\ recordPhase' = [recordPhase EXCEPT ![o] = Rejected]
    /\ rejectedEver' = rejectedEver \cup {o}
    /\ outcome' = [outcome EXCEPT ![o] = FailedOutcome]
    /\ quiesced' = quiesced \cup {o}
    /\ UNCHANGED
        <<epochState,
          readyMatched,
          acceptedEver,
          dispatched,
          canceledHeld,
          operationHighWater,
          cancelSent,
          cancelAck,
          settlementCount,
          responseProbeOutstanding,
          missingResponseProven,
          protocolFailure,
          sourceRevoked>>
    /\ UnchangedWork
    /\ UnchangedProtocolFlags

SendCancel(o) ==
    /\ epochState = Ready
    /\ recordPhase[o] \in {AwaitingAdmission, Accepted}
    /\ o \notin cancelSent
    /\ o # OperationA \/ ~responseProbeOutstanding
    /\ cancelSent' = cancelSent \cup {o}
    /\ UNCHANGED
        <<epochState,
          readyMatched,
          recordPhase,
          acceptedEver,
          rejectedEver,
          dispatched,
          canceledHeld,
          operationHighWater,
          outcome,
          quiesced,
          cancelAck,
          settlementCount,
          responseProbeOutstanding,
          missingResponseProven,
          protocolFailure,
          sourceRevoked>>
    /\ UnchangedWork
    /\ UnchangedProtocolFlags

WorkerCancelAck(o, ack) ==
    /\ epochState \in {Ready, Draining}
    /\ ~sourceRevoked
    /\ o \in cancelSent
    /\ cancelAck[o] = NoAck
    /\ ack \in CommittedCancelAcks
    /\ CASE ack = AckRunning ->
                recordPhase[o] = Accepted
            [] ack = AckNotActive ->
                recordPhase[o] \in {Accepted, Rejected, Settled}
    /\ cancelAck' = [cancelAck EXCEPT ![o] = ack]
    /\ UNCHANGED
        <<epochState,
          readyMatched,
          recordPhase,
          acceptedEver,
          rejectedEver,
          dispatched,
          canceledHeld,
          operationHighWater,
          outcome,
          quiesced,
          cancelSent,
          settlementCount,
          responseProbeOutstanding,
          missingResponseProven,
          protocolFailure,
          sourceRevoked>>
    /\ UnchangedWork
    /\ UnchangedProtocolFlags

WorkerSettle(o, result) ==
    /\ epochState = Ready
    /\ ~sourceRevoked
    /\ recordPhase[o] = Accepted
    /\ result \in {SucceededOutcome, FailedOutcome, CanceledOutcome}
    /\ recordPhase' = [recordPhase EXCEPT ![o] = Settled]
    /\ outcome' = [outcome EXCEPT ![o] = result]
    /\ quiesced' = quiesced \cup {o}
    /\ settlementCount' = [settlementCount EXCEPT ![o] = @ + 1]
    /\ UNCHANGED
        <<epochState,
          readyMatched,
          acceptedEver,
          rejectedEver,
          dispatched,
          canceledHeld,
          operationHighWater,
          cancelSent,
          cancelAck,
          responseProbeOutstanding,
          missingResponseProven,
          protocolFailure,
          sourceRevoked>>
    /\ UnchangedWork
    /\ UnchangedProtocolFlags

CancelAckBeforeAdmissionMutation(o) ==
    /\ Mutation = CancelAckBeforeAdmission
    /\ epochState = Ready
    /\ ~sourceRevoked
    /\ recordPhase[o] = AwaitingAdmission
    /\ o \in cancelSent
    /\ cancelAck[o] = NoAck
    /\ cancelAck' = [cancelAck EXCEPT ![o] = AckRunning]
    /\ UNCHANGED
        <<epochState,
          readyMatched,
          recordPhase,
          acceptedEver,
          rejectedEver,
          dispatched,
          canceledHeld,
          operationHighWater,
          outcome,
          quiesced,
          cancelSent,
          settlementCount,
          responseProbeOutstanding,
          missingResponseProven,
          protocolFailure,
          sourceRevoked>>
    /\ UnchangedWork
    /\ UnchangedProtocolFlags

SettleBeforeAcceptedMutation(o) ==
    /\ Mutation = SettleBeforeAccepted
    /\ epochState = Ready
    /\ recordPhase[o] = AwaitingAdmission
    /\ recordPhase' = [recordPhase EXCEPT ![o] = Settled]
    /\ outcome' = [outcome EXCEPT ![o] = SucceededOutcome]
    /\ quiesced' = quiesced \cup {o}
    /\ settlementCount' = [settlementCount EXCEPT ![o] = @ + 1]
    /\ settlementBeforeAcceptanceObserved' = TRUE
    /\ UNCHANGED
        <<epochState,
          readyMatched,
          acceptedEver,
          rejectedEver,
          dispatched,
          canceledHeld,
          operationHighWater,
          cancelSent,
          cancelAck,
          responseProbeOutstanding,
          missingResponseProven,
          protocolFailure,
          sourceRevoked,
          startDuringDrain,
          dispatchBeforeReadyObserved,
          replayAcceptedObserved,
          retirementBeforeAckObserved,
          probeOvertakeObserved,
          futureNotActiveObserved,
          workSequenceReuseObserved,
          unmatchedWorkFinishObserved,
          invalidWorkStartObserved,
          invalidWorkFinishObserved,
          replacementOpen,
          staleEpochChangedState,
          callbackAfterCloseObserved>>
    /\ UnchangedWork

DuplicateSettlementMutation(o) ==
    /\ Mutation = DuplicateSettlement
    /\ recordPhase[o] = Settled
    /\ settlementCount' = [settlementCount EXCEPT ![o] = @ + 1]
    /\ UNCHANGED
        <<epochState,
          readyMatched,
          recordPhase,
          acceptedEver,
          rejectedEver,
          dispatched,
          canceledHeld,
          operationHighWater,
          outcome,
          quiesced,
          cancelSent,
          cancelAck,
          responseProbeOutstanding,
          missingResponseProven,
          protocolFailure,
          sourceRevoked>>
    /\ UnchangedWork
    /\ UnchangedProtocolFlags

CanRetire(o) ==
    /\ recordPhase[o] \in {Rejected, Settled}
    /\ o \notin cancelSent \/ cancelAck[o] \in CommittedCancelAcks

Retire(o) ==
    /\ CanRetire(o)
    /\ recordPhase' = [recordPhase EXCEPT ![o] = Retired]
    /\ UNCHANGED
        <<epochState,
          readyMatched,
          acceptedEver,
          rejectedEver,
          dispatched,
          canceledHeld,
          operationHighWater,
          outcome,
          quiesced,
          cancelSent,
          cancelAck,
          settlementCount,
          responseProbeOutstanding,
          missingResponseProven,
          protocolFailure,
          sourceRevoked>>
    /\ UnchangedWork
    /\ UnchangedProtocolFlags

RetireBeforeAckMutation(o) ==
    /\ Mutation = RetireBeforeAck
    /\ recordPhase[o] \in {Rejected, Settled}
    /\ o \in cancelSent
    /\ cancelAck[o] = NoAck
    /\ recordPhase' = [recordPhase EXCEPT ![o] = Retired]
    /\ retirementBeforeAckObserved' = TRUE
    /\ UNCHANGED
        <<epochState,
          readyMatched,
          acceptedEver,
          rejectedEver,
          dispatched,
          canceledHeld,
          operationHighWater,
          outcome,
          quiesced,
          cancelSent,
          cancelAck,
          settlementCount,
          responseProbeOutstanding,
          missingResponseProven,
          protocolFailure,
          sourceRevoked,
          startDuringDrain,
          dispatchBeforeReadyObserved,
          replayAcceptedObserved,
          settlementBeforeAcceptanceObserved,
          probeOvertakeObserved,
          futureNotActiveObserved,
          workSequenceReuseObserved,
          unmatchedWorkFinishObserved,
          invalidWorkStartObserved,
          invalidWorkFinishObserved,
          replacementOpen,
          staleEpochChangedState,
          callbackAfterCloseObserved>>
    /\ UnchangedWork

ReplayStart(o) ==
    /\ epochState = Ready
    /\ OperationSequence(o) <= operationHighWater
    /\ recordPhase[o] \in {NoRecord, Retired}
    /\ IF Mutation = ReplayAccepted
       THEN
           /\ recordPhase' = [recordPhase EXCEPT ![o] = AwaitingAdmission]
           /\ replayAcceptedObserved' = TRUE
           /\ UNCHANGED <<epochState, protocolFailure>>
       ELSE
           /\ epochState' = Draining
           /\ protocolFailure' = TRUE
           /\ replayAcceptedObserved' = FALSE
           /\ UNCHANGED recordPhase
    /\ UNCHANGED
        <<readyMatched,
          acceptedEver,
          rejectedEver,
          dispatched,
          canceledHeld,
          operationHighWater,
          outcome,
          quiesced,
          cancelSent,
          cancelAck,
          settlementCount,
          responseProbeOutstanding,
          missingResponseProven,
          sourceRevoked,
          startDuringDrain,
          dispatchBeforeReadyObserved,
          settlementBeforeAcceptanceObserved,
          retirementBeforeAckObserved,
          probeOvertakeObserved,
          futureNotActiveObserved,
          workSequenceReuseObserved,
          unmatchedWorkFinishObserved,
          invalidWorkStartObserved,
          invalidWorkFinishObserved,
          replacementOpen,
          staleEpochChangedState,
          callbackAfterCloseObserved>>
    /\ UnchangedWork

SendResponseProbe ==
    /\ epochState = Ready
    /\ ~sourceRevoked
    /\ MissingRequiredResponseForA
    /\ ~responseProbeOutstanding
    /\ responseProbeOutstanding' = TRUE
    /\ UNCHANGED
        <<epochState,
          readyMatched,
          recordPhase,
          acceptedEver,
          rejectedEver,
          dispatched,
          canceledHeld,
          operationHighWater,
          outcome,
          quiesced,
          cancelSent,
          cancelAck,
          settlementCount,
          missingResponseProven,
          protocolFailure,
          sourceRevoked>>
    /\ UnchangedWork
    /\ UnchangedProtocolFlags

ReceiveResponseProbeAck ==
    /\ epochState = Ready
    /\ ~sourceRevoked
    /\ responseProbeOutstanding
    /\ \/ ~MissingRequiredResponseForA
       \/ OmittedResponseForA
    /\ responseProbeOutstanding' = FALSE
    /\ missingResponseProven' = MissingRequiredResponseForA
    /\ IF MissingRequiredResponseForA
       THEN
           /\ IF Mutation = IgnoreMissingResponse
              THEN
                  /\ UNCHANGED <<epochState, protocolFailure>>
              ELSE
                  /\ epochState' = Draining
                  /\ protocolFailure' = TRUE
       ELSE
           /\ UNCHANGED <<epochState, protocolFailure>>
    /\ UNCHANGED
        <<readyMatched,
          recordPhase,
          acceptedEver,
          rejectedEver,
          dispatched,
          canceledHeld,
          operationHighWater,
          outcome,
          quiesced,
          cancelSent,
          cancelAck,
          settlementCount,
          sourceRevoked>>
    /\ UnchangedWork
    /\ UnchangedProtocolFlags

FutureCancelNotActiveMutation(o) ==
    /\ Mutation = FutureCancelNotActive
    /\ epochState = Ready
    /\ OperationSequence(o) > operationHighWater
    /\ cancelAck[o] = NoAck
    /\ cancelAck' = [cancelAck EXCEPT ![o] = AckNotActive]
    /\ futureNotActiveObserved' = TRUE
    /\ UNCHANGED
        <<epochState,
          readyMatched,
          recordPhase,
          acceptedEver,
          rejectedEver,
          dispatched,
          canceledHeld,
          operationHighWater,
          outcome,
          quiesced,
          cancelSent,
          settlementCount,
          responseProbeOutstanding,
          missingResponseProven,
          protocolFailure,
          sourceRevoked,
          startDuringDrain,
          dispatchBeforeReadyObserved,
          replayAcceptedObserved,
          settlementBeforeAcceptanceObserved,
          retirementBeforeAckObserved,
          probeOvertakeObserved,
          workSequenceReuseObserved,
          unmatchedWorkFinishObserved,
          invalidWorkStartObserved,
          invalidWorkFinishObserved,
          replacementOpen,
          staleEpochChangedState,
          callbackAfterCloseObserved>>
    /\ UnchangedWork

StartEpochWork(sequence) ==
    /\ epochState = Ready
    /\ ~sourceRevoked
    /\ sequence \in 1..MaxWorkSequence
    /\ sequence > workHighWater
    /\ workHighWater' = sequence
    /\ activeWork' = activeWork \cup {sequence}
    /\ startedWork' = startedWork \cup {sequence}
    /\ UNCHANGED finishedWork
    /\ UNCHANGED
        <<epochState,
          readyMatched,
          recordPhase,
          acceptedEver,
          rejectedEver,
          dispatched,
          canceledHeld,
          operationHighWater,
          outcome,
          quiesced,
          cancelSent,
          cancelAck,
          settlementCount,
          responseProbeOutstanding,
          missingResponseProven,
          protocolFailure,
          sourceRevoked>>
    /\ UnchangedProtocolFlags

FinishEpochWork(sequence) ==
    /\ epochState \in {Ready, Draining}
    /\ ~sourceRevoked
    /\ sequence \in activeWork
    /\ activeWork' = activeWork \ {sequence}
    /\ finishedWork' = finishedWork \cup {sequence}
    /\ UNCHANGED <<workHighWater, startedWork>>
    /\ UNCHANGED
        <<epochState,
          readyMatched,
          recordPhase,
          acceptedEver,
          rejectedEver,
          dispatched,
          canceledHeld,
          operationHighWater,
          outcome,
          quiesced,
          cancelSent,
          cancelAck,
          settlementCount,
          responseProbeOutstanding,
          missingResponseProven,
          protocolFailure,
          sourceRevoked>>
    /\ UnchangedProtocolFlags

ReceiveInvalidEpochWorkStart(sequence) ==
    /\ epochState = Ready
    /\ ~sourceRevoked
    /\ sequence \in 1..MaxWorkSequence
    /\ sequence <= workHighWater
    /\ invalidWorkStartObserved' = TRUE
    /\ IF Mutation = ReuseWorkSequence
          /\ sequence \in finishedWork
       THEN
           /\ activeWork' = activeWork \cup {sequence}
           /\ workSequenceReuseObserved' = TRUE
           /\ UNCHANGED
               <<epochState,
                 protocolFailure,
                 workHighWater,
                 startedWork,
                 finishedWork>>
       ELSE
           /\ IF Mutation = AcceptActiveDuplicateWorkStart
                 /\ sequence \in activeWork
              THEN
                  /\ UNCHANGED
                      <<epochState,
                        protocolFailure,
                        workHighWater,
                        activeWork,
                        startedWork,
                        finishedWork,
                        workSequenceReuseObserved>>
              ELSE
                  /\ epochState' = Draining
                  /\ protocolFailure' = TRUE
                  /\ UNCHANGED
                      <<workHighWater,
                        activeWork,
                        startedWork,
                        finishedWork,
                        workSequenceReuseObserved>>
    /\ UNCHANGED
        <<readyMatched,
          recordPhase,
          acceptedEver,
          rejectedEver,
          dispatched,
          canceledHeld,
          operationHighWater,
          outcome,
          quiesced,
          cancelSent,
          cancelAck,
          settlementCount,
          responseProbeOutstanding,
          missingResponseProven,
          sourceRevoked,
          startDuringDrain,
          dispatchBeforeReadyObserved,
          replayAcceptedObserved,
          settlementBeforeAcceptanceObserved,
          retirementBeforeAckObserved,
          probeOvertakeObserved,
          futureNotActiveObserved,
          unmatchedWorkFinishObserved,
          invalidWorkFinishObserved,
          replacementOpen,
          staleEpochChangedState,
          callbackAfterCloseObserved>>

ReceiveInvalidEpochWorkFinish(sequence) ==
    /\ epochState \in {Ready, Draining}
    /\ ~sourceRevoked
    /\ sequence \in 1..MaxWorkSequence
    /\ sequence \notin activeWork
    /\ invalidWorkFinishObserved' = TRUE
    /\ IF Mutation = UnmatchedWorkFinish
          /\ sequence \notin startedWork
       THEN
           /\ finishedWork' = finishedWork \cup {sequence}
           /\ unmatchedWorkFinishObserved' = TRUE
           /\ UNCHANGED
               <<epochState,
                 protocolFailure,
                 workHighWater,
                 activeWork,
                 startedWork>>
       ELSE
           /\ IF Mutation = AcceptDuplicateWorkFinish
                 /\ sequence \in finishedWork
              THEN
                  /\ UNCHANGED
                      <<epochState,
                        protocolFailure,
                        workHighWater,
                        activeWork,
                        startedWork,
                        finishedWork,
                        unmatchedWorkFinishObserved>>
              ELSE
                  /\ epochState' = Draining
                  /\ protocolFailure' = TRUE
                  /\ UNCHANGED
                      <<workHighWater,
                        activeWork,
                        startedWork,
                        finishedWork,
                        unmatchedWorkFinishObserved>>
    /\ UNCHANGED
        <<readyMatched,
          recordPhase,
          acceptedEver,
          rejectedEver,
          dispatched,
          canceledHeld,
          operationHighWater,
          outcome,
          quiesced,
          cancelSent,
          cancelAck,
          settlementCount,
          responseProbeOutstanding,
          missingResponseProven,
          sourceRevoked,
          startDuringDrain,
          dispatchBeforeReadyObserved,
          replayAcceptedObserved,
          settlementBeforeAcceptanceObserved,
          retirementBeforeAckObserved,
          probeOvertakeObserved,
          futureNotActiveObserved,
          workSequenceReuseObserved,
          invalidWorkStartObserved,
          replacementOpen,
          staleEpochChangedState,
          callbackAfterCloseObserved>>

DestroyRealm ==
    /\ epochState = Draining
    /\ epochState' = Closed
    /\ sourceRevoked' = TRUE
    /\ recordPhase' =
        [o \in Operations |->
            IF recordPhase[o] = NoRecord
            THEN NoRecord
            ELSE Retired]
    /\ outcome' =
        [o \in Operations |->
            IF recordPhase[o] = NoRecord
            THEN outcome[o]
            ELSE IF outcome[o] = NoOutcome THEN FailedOutcome ELSE outcome[o]]
    /\ quiesced' =
        quiesced \cup {o \in Operations: recordPhase[o] # NoRecord}
    /\ activeWork' = {}
    /\ responseProbeOutstanding' = FALSE
    /\ UNCHANGED
        <<readyMatched,
          acceptedEver,
          rejectedEver,
          dispatched,
          canceledHeld,
          operationHighWater,
          cancelSent,
          cancelAck,
          settlementCount,
          missingResponseProven,
          protocolFailure,
          workHighWater,
          startedWork,
          finishedWork>>
    /\ UnchangedProtocolFlags

OpenReplacement ==
    /\ epochState = Closed
    /\ ~replacementOpen
    /\ replacementOpen' = TRUE
    \* The cell is reused as the replacement epoch's receive high-water.
    /\ operationHighWater' = 0
    /\ UNCHANGED
        <<epochState,
          readyMatched,
          recordPhase,
          acceptedEver,
          rejectedEver,
          dispatched,
          canceledHeld,
          outcome,
          quiesced,
          cancelSent,
          cancelAck,
          settlementCount,
          responseProbeOutstanding,
          missingResponseProven,
          probeOvertakeObserved,
          protocolFailure,
          sourceRevoked,
          startDuringDrain,
          dispatchBeforeReadyObserved,
          replayAcceptedObserved,
          settlementBeforeAcceptanceObserved,
          retirementBeforeAckObserved,
          futureNotActiveObserved,
          workHighWater,
          activeWork,
          startedWork,
          finishedWork,
          workSequenceReuseObserved,
          unmatchedWorkFinishObserved,
          invalidWorkStartObserved,
          invalidWorkFinishObserved,
          staleEpochChangedState,
          callbackAfterCloseObserved>>

ReceiveReplacementMessage(workerSource, epochToken) ==
    /\ epochState = Closed
    /\ replacementOpen
    /\ operationHighWater \in 0..1
    /\ IF MessageIsCurrent(workerSource, epochToken)
       THEN
           /\ operationHighWater' = 1
           /\ staleEpochChangedState' =
               (staleEpochChangedState
                  \/ ~ExactMessageIsCurrent(workerSource, epochToken))
       ELSE
           /\ UNCHANGED operationHighWater
           /\ UNCHANGED staleEpochChangedState
    /\ UNCHANGED
        <<epochState,
          readyMatched,
          recordPhase,
          acceptedEver,
          rejectedEver,
          dispatched,
          canceledHeld,
          outcome,
          quiesced,
          cancelSent,
          cancelAck,
          settlementCount,
          responseProbeOutstanding,
          missingResponseProven,
          probeOvertakeObserved,
          protocolFailure,
          sourceRevoked,
          startDuringDrain,
          dispatchBeforeReadyObserved,
          replayAcceptedObserved,
          settlementBeforeAcceptanceObserved,
          retirementBeforeAckObserved,
          futureNotActiveObserved,
          workHighWater,
          activeWork,
          startedWork,
          finishedWork,
          workSequenceReuseObserved,
          unmatchedWorkFinishObserved,
          invalidWorkStartObserved,
          invalidWorkFinishObserved,
          replacementOpen,
          callbackAfterCloseObserved>>

CallbackAfterCloseMutation ==
    /\ Mutation = CallbackAfterClose
    /\ epochState = Closed
    /\ replacementOpen
    /\ operationHighWater' = 1
    /\ callbackAfterCloseObserved' = TRUE
    /\ UNCHANGED
        <<epochState,
          readyMatched,
          recordPhase,
          acceptedEver,
          rejectedEver,
          dispatched,
          canceledHeld,
          outcome,
          quiesced,
          cancelSent,
          cancelAck,
          settlementCount,
          responseProbeOutstanding,
          missingResponseProven,
          probeOvertakeObserved,
          protocolFailure,
          sourceRevoked,
          startDuringDrain,
          dispatchBeforeReadyObserved,
          replayAcceptedObserved,
          settlementBeforeAcceptanceObserved,
          retirementBeforeAckObserved,
          futureNotActiveObserved,
          workHighWater,
          activeWork,
          startedWork,
          finishedWork,
          workSequenceReuseObserved,
          unmatchedWorkFinishObserved,
          invalidWorkStartObserved,
          invalidWorkFinishObserved,
          replacementOpen,
          staleEpochChangedState>>

Next ==
    \/ \E o \in Operations: ActivateHeld(o)
    \/ \E o \in Operations: CancelHeld(o)
    \/ ReceiveReady
    \/ ReceiveMismatchedReady
    \/ \E o \in Operations: DispatchHeld(o)
    \/ FinishReadyFlush
    \/ \E o \in Operations: DispatchCanceledHeldMutation(o)
    \/ \E o \in Operations: ActivateReady(o)
    \/ \E o \in Operations: WarmActivationBeforeHeldFlushMutation(o)
    \/ \E o \in Operations: AcceptDuringDrainMutation(o)
    \/ \E o \in Operations: WorkerAccept(o)
    \/ \E o \in Operations: WorkerReject(o)
    \/ \E o \in Operations: WorkerOmitsStartResponse(o)
    \/ \E o \in Operations: SendCancel(o)
    \/ \E o \in Operations:
           \E ack \in {AckRunning, AckNotActive}:
               WorkerCancelAck(o, ack)
    \/ \E o \in Operations: CancelAckBeforeAdmissionMutation(o)
    \/ \E o \in Operations: WorkerOmitsCancelAck(o)
    \/ \E o \in Operations:
           \E result \in {SucceededOutcome, FailedOutcome, CanceledOutcome}:
               WorkerSettle(o, result)
    \/ \E o \in Operations: SettleBeforeAcceptedMutation(o)
    \/ \E o \in Operations: DuplicateSettlementMutation(o)
    \/ \E o \in Operations: Retire(o)
    \/ \E o \in Operations: RetireBeforeAckMutation(o)
    \/ \E o \in Operations: ReplayStart(o)
    \/ SendResponseProbe
    \/ ReceiveResponseProbeAck
    \/ ProbeOvertakesControlMutation
    \/ \E o \in Operations: FutureCancelNotActiveMutation(o)
    \/ \E sequence \in 1..MaxWorkSequence: StartEpochWork(sequence)
    \/ \E sequence \in 1..MaxWorkSequence: FinishEpochWork(sequence)
    \/ \E sequence \in 1..MaxWorkSequence:
           ReceiveInvalidEpochWorkStart(sequence)
    \/ \E sequence \in 1..MaxWorkSequence:
           ReceiveInvalidEpochWorkFinish(sequence)
    \/ DestroyRealm
    \/ OpenReplacement
    \/ ReceiveReplacementMessage(ReplacementWorkerSource, EpochCurrent)
    \/ ReceiveReplacementMessage(OldWorkerSource, EpochCurrent)
    \/ ReceiveReplacementMessage(ReplacementWorkerSource, EpochStale)
    \/ CallbackAfterCloseMutation

Spec ==
    /\ Init
    /\ [][Next]_vars
    /\ WF_vars(DestroyRealm)

TypeOK ==
    /\ epochState \in EpochStates
    /\ readyMatched \in BOOLEAN
    /\ recordPhase \in [Operations -> RecordPhases]
    /\ acceptedEver \subseteq Operations
    /\ rejectedEver \subseteq Operations
    /\ dispatched \subseteq Operations
    /\ canceledHeld \subseteq Operations
    /\ operationHighWater \in 0..2
    /\ outcome \in [Operations -> Outcomes]
    /\ quiesced \subseteq Operations
    /\ cancelSent \subseteq Operations
    /\ cancelAck \in [Operations -> CancelAcks]
    /\ settlementCount \in [Operations -> Nat]
    /\ responseProbeOutstanding \in BOOLEAN
    /\ missingResponseProven \in BOOLEAN
    /\ probeOvertakeObserved \in BOOLEAN
    /\ protocolFailure \in BOOLEAN
    /\ sourceRevoked \in BOOLEAN
    /\ startDuringDrain \in BOOLEAN
    /\ dispatchBeforeReadyObserved \in BOOLEAN
    /\ replayAcceptedObserved \in BOOLEAN
    /\ settlementBeforeAcceptanceObserved \in BOOLEAN
    /\ retirementBeforeAckObserved \in BOOLEAN
    /\ futureNotActiveObserved \in BOOLEAN
    /\ workHighWater \in 0..MaxWorkSequence
    /\ activeWork \subseteq 1..MaxWorkSequence
    /\ startedWork \subseteq 1..MaxWorkSequence
    /\ finishedWork \subseteq 1..MaxWorkSequence
    /\ workSequenceReuseObserved \in BOOLEAN
    /\ unmatchedWorkFinishObserved \in BOOLEAN
    /\ invalidWorkStartObserved \in BOOLEAN
    /\ invalidWorkFinishObserved \in BOOLEAN
    /\ replacementOpen \in BOOLEAN
    /\ staleEpochChangedState \in BOOLEAN
    /\ callbackAfterCloseObserved \in BOOLEAN

MatchingReadyRequired ==
    epochState \in {Ready, Draining} => readyMatched

NoDispatchBeforeReady ==
    ~dispatchBeforeReadyObserved

CanceledHeldNeverDispatches ==
    canceledHeld \cap dispatched = {}

DrainingRefusesStarts ==
    ~startDuringDrain

AcceptedAndRejectedAreExclusive ==
    acceptedEver \cap rejectedEver = {}

SettlementRequiresAcceptance ==
    \A o \in Operations:
        settlementCount[o] > 0 => o \in acceptedEver

AtomicSettlementIncludesQuiescence ==
    \A o \in Operations:
        recordPhase[o] = Settled => o \in quiesced

OneSettlementPerOperation ==
    \A o \in Operations: settlementCount[o] <= 1

RetirementRequiresClosureAndAcknowledgment ==
    \A o \in Operations:
        recordPhase[o] = Retired
        =>
        /\ outcome[o] # NoOutcome
        /\ o \in quiesced
        /\ sourceRevoked
           \/ o \notin cancelSent
           \/ cancelAck[o] \in CommittedCancelAcks

CancellationAcknowledgmentRequiresCommittedAdmission ==
    \A o \in Operations:
        cancelAck[o] \in CommittedCancelAcks
        =>
        recordPhase[o]
            \notin {NoRecord, Held, AwaitingAdmission,
                    AdmissionResponseOmitted}

ReplayNeverReentersAdmission ==
    ~replayAcceptedObserved

MissingResponseProofFailsEpoch ==
    missingResponseProven => protocolFailure

MissingResponseProofRequiresCompletedOmission ==
    missingResponseProven /\ ~sourceRevoked => OmittedResponseForA

ProbeCannotOvertakeControl ==
    ~probeOvertakeObserved

NotActiveRequiresReceivedSequence ==
    \A o \in Operations:
        cancelAck[o] = AckNotActive /\ ~sourceRevoked
        => OperationSequence(o) <= operationHighWater

HeldStartsRemainDispatchable ==
    \A o \in Operations:
        recordPhase[o] = Held
        => OperationSequence(o) > operationHighWater

CanceledHeldKeepsCanceledOutcome ==
    \A o \in canceledHeld: outcome[o] = CanceledOutcome

WorkSequenceNeverReused ==
    activeWork \cap finishedWork = {}

WorkFinishRequiresActiveStart ==
    finishedWork \subseteq startedWork

StartedWorkTracksHighWater ==
    \A sequence \in startedWork: sequence <= workHighWater

InvalidWorkStartFailsEpoch ==
    invalidWorkStartObserved
    =>
    /\ protocolFailure
    /\ epochState \in {Draining, Closed}

InvalidWorkFinishFailsEpoch ==
    invalidWorkFinishObserved
    =>
    /\ protocolFailure
    /\ epochState \in {Draining, Closed}

OnlyCurrentBindingMutatesReplacement ==
    ~staleEpochChangedState

NoCallbackAfterRealmRelease ==
    ~callbackAfterCloseObserved

ProtocolFailureLeavesReadyState ==
    protocolFailure => epochState \in {Draining, Closed}

DrainingEventuallyCloses ==
    epochState = Draining ~> epochState = Closed

=============================================================================
