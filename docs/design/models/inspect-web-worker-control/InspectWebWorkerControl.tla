-------------------- MODULE InspectWebWorkerControl --------------------
(***************************************************************************)
(* Finite model for one operation-addressed Worker control request.         *)
(*                                                                         *)
(* The model separates operation identity from the per-operation control    *)
(* sequence and permits settlement while the serialized control handler is  *)
(* awaiting completion.                                                     *)
(***************************************************************************)
EXTENDS Naturals, TLC

CONSTANTS
    MaxControlSequence,
    MUTATION_ALLOW_SECOND_OUTSTANDING,
    MUTATION_ACCEPT_REPLAY,
    MUTATION_DROP_PENDING_ON_SETTLEMENT,
    MUTATION_ACCEPT_MISMATCHED_ACKNOWLEDGMENT

Active == "Active"
Settled == "Settled"
OperationStates == {Active, Settled}

Idle == "Idle"
Pending == "Pending"
Acknowledged == "Acknowledged"
NotActive == "NotActive"
Failed == "Failed"
HostStates == {Idle, Pending, Acknowledged, NotActive, Failed}

NoOperation == "NoOperation"
PrimaryOperation == "PrimaryOperation"
OtherOperation == "OtherOperation"
OperationReferences == {NoOperation, PrimaryOperation, OtherOperation}

NoHandler == "NoHandler"
Running == "Running"
HandlerStates == {NoHandler, Running}

NoAck == "NoAck"
AckResult == "AckResult"
AckNotActive == "AckNotActive"
AckStates == {NoAck, AckResult, AckNotActive}

VARIABLES
    operationState,
    controllable,
    hostState,
    nextControlSequence,
    requestOperation,
    requestSequence,
    workerHighWater,
    handlerState,
    ackState,
    ackOperation,
    ackSequence,
    outcomeOperation,
    outcomeSequence,
    payloadReleased,
    busyRejected,
    secondOutstanding,
    replayObserved,
    replayAcknowledged,
    protocolFailure

vars ==
    <<operationState,
      controllable,
      hostState,
      nextControlSequence,
      requestOperation,
      requestSequence,
      workerHighWater,
      handlerState,
      ackState,
      ackOperation,
      ackSequence,
      outcomeOperation,
      outcomeSequence,
      payloadReleased,
      busyRejected,
      secondOutstanding,
      replayObserved,
      replayAcknowledged,
      protocolFailure>>

Init ==
    /\ operationState = Active
    /\ controllable = TRUE
    /\ hostState = Idle
    /\ nextControlSequence = 1
    /\ requestOperation = NoOperation
    /\ requestSequence = 0
    /\ workerHighWater = 0
    /\ handlerState = NoHandler
    /\ ackState = NoAck
    /\ ackOperation = NoOperation
    /\ ackSequence = 0
    /\ outcomeOperation = NoOperation
    /\ outcomeSequence = 0
    /\ payloadReleased = FALSE
    /\ busyRejected = FALSE
    /\ secondOutstanding = FALSE
    /\ replayObserved = FALSE
    /\ replayAcknowledged = FALSE
    /\ protocolFailure = FALSE

RequestControl ==
    /\ ~protocolFailure
    /\ operationState = Active
    /\ hostState = Idle
    /\ nextControlSequence <= MaxControlSequence
    /\ hostState' = Pending
    /\ requestOperation' = PrimaryOperation
    /\ requestSequence' = nextControlSequence
    /\ nextControlSequence' = nextControlSequence + 1
    /\ UNCHANGED
        <<operationState,
          controllable,
          workerHighWater,
          handlerState,
          ackState,
          ackOperation,
          ackSequence,
          outcomeOperation,
          outcomeSequence,
          payloadReleased,
          busyRejected,
          secondOutstanding,
          replayObserved,
          replayAcknowledged,
          protocolFailure>>

ConcurrentRequest ==
    /\ ~protocolFailure
    /\ hostState = Pending
    /\ IF MUTATION_ALLOW_SECOND_OUTSTANDING
       THEN
           /\ secondOutstanding' = TRUE
           /\ UNCHANGED busyRejected
       ELSE
           /\ busyRejected' = TRUE
           /\ UNCHANGED secondOutstanding
    /\ UNCHANGED
        <<operationState,
          controllable,
          hostState,
          nextControlSequence,
          requestOperation,
          requestSequence,
          workerHighWater,
          handlerState,
          ackState,
          ackOperation,
          ackSequence,
          outcomeOperation,
          outcomeSequence,
          payloadReleased,
          replayObserved,
          replayAcknowledged,
          protocolFailure>>

Settle ==
    /\ ~protocolFailure
    /\ operationState = Active
    /\ operationState' = Settled
    /\ controllable' = FALSE
    /\ payloadReleased' = TRUE
    /\ hostState' =
        IF MUTATION_DROP_PENDING_ON_SETTLEMENT /\ hostState = Pending
        THEN Idle
        ELSE hostState
    /\ UNCHANGED
        <<nextControlSequence,
          requestOperation,
          requestSequence,
          workerHighWater,
          handlerState,
          ackState,
          ackOperation,
          ackSequence,
          outcomeOperation,
          outcomeSequence,
          busyRejected,
          secondOutstanding,
          replayObserved,
          replayAcknowledged,
          protocolFailure>>

SealControl ==
    /\ ~protocolFailure
    /\ operationState = Active
    /\ controllable
    /\ controllable' = FALSE
    /\ UNCHANGED
        <<operationState,
          hostState,
          nextControlSequence,
          requestOperation,
          requestSequence,
          workerHighWater,
          handlerState,
          ackState,
          ackOperation,
          ackSequence,
          outcomeOperation,
          outcomeSequence,
          payloadReleased,
          busyRejected,
          secondOutstanding,
          replayObserved,
          replayAcknowledged,
          protocolFailure>>

WorkerBeginControl ==
    /\ ~protocolFailure
    /\ operationState = Active
    /\ hostState = Pending
    /\ handlerState = NoHandler
    /\ ackState = NoAck
    /\ requestSequence > workerHighWater
    /\ workerHighWater' = requestSequence
    /\ handlerState' = Running
    /\ UNCHANGED
        <<operationState,
          controllable,
          hostState,
          nextControlSequence,
          requestOperation,
          requestSequence,
          ackState,
          ackOperation,
          ackSequence,
          outcomeOperation,
          outcomeSequence,
          payloadReleased,
          busyRejected,
          secondOutstanding,
          replayObserved,
          replayAcknowledged,
          protocolFailure>>

WorkerObserveNotActive ==
    /\ ~protocolFailure
    /\ operationState = Settled
    /\ hostState = Pending
    /\ handlerState = NoHandler
    /\ ackState = NoAck
    /\ ackState' = AckNotActive
    /\ ackOperation' = requestOperation
    /\ ackSequence' = requestSequence
    /\ UNCHANGED
        <<operationState,
          controllable,
          hostState,
          nextControlSequence,
          requestOperation,
          requestSequence,
          workerHighWater,
          handlerState,
          outcomeOperation,
          outcomeSequence,
          payloadReleased,
          busyRejected,
          secondOutstanding,
          replayObserved,
          replayAcknowledged,
          protocolFailure>>

CompleteHandler ==
    /\ ~protocolFailure
    /\ handlerState = Running
    /\ ackState = NoAck
    /\ handlerState' = NoHandler
    /\ ackState' = AckResult
    /\ ackOperation' = requestOperation
    /\ ackSequence' = requestSequence
    /\ UNCHANGED
        <<operationState,
          controllable,
          hostState,
          nextControlSequence,
          requestOperation,
          requestSequence,
          workerHighWater,
          outcomeOperation,
          outcomeSequence,
          payloadReleased,
          busyRejected,
          secondOutstanding,
          replayObserved,
          replayAcknowledged,
          protocolFailure>>

CompleteHandlerNotActive ==
    /\ ~protocolFailure
    /\ handlerState = Running
    /\ ~controllable
    /\ ackState = NoAck
    /\ handlerState' = NoHandler
    /\ ackState' = AckNotActive
    /\ ackOperation' = requestOperation
    /\ ackSequence' = requestSequence
    /\ UNCHANGED
        <<operationState,
          controllable,
          hostState,
          nextControlSequence,
          requestOperation,
          requestSequence,
          workerHighWater,
          outcomeOperation,
          outcomeSequence,
          payloadReleased,
          busyRejected,
          secondOutstanding,
          replayObserved,
          replayAcknowledged,
          protocolFailure>>

ReceiveAcknowledgment ==
    /\ ~protocolFailure
    /\ hostState = Pending
    /\ ackState \in {AckResult, AckNotActive}
    /\ (MUTATION_ACCEPT_MISMATCHED_ACKNOWLEDGMENT
        \/ /\ ackOperation = requestOperation
           /\ ackSequence = requestSequence)
    /\ hostState' =
        IF ackState = AckResult THEN Acknowledged ELSE NotActive
    /\ outcomeOperation' = ackOperation
    /\ outcomeSequence' = ackSequence
    /\ ackState' = NoAck
    /\ ackOperation' = NoOperation
    /\ ackSequence' = 0
    /\ UNCHANGED
        <<operationState,
          controllable,
          nextControlSequence,
          requestOperation,
          requestSequence,
          workerHighWater,
          handlerState,
          payloadReleased,
          busyRejected,
          secondOutstanding,
          replayObserved,
          replayAcknowledged,
          protocolFailure>>

ClearOutcome ==
    /\ ~protocolFailure
    /\ hostState \in {Acknowledged, NotActive}
    /\ hostState' = Idle
    /\ requestOperation' = NoOperation
    /\ requestSequence' = 0
    /\ outcomeOperation' = NoOperation
    /\ outcomeSequence' = 0
    /\ UNCHANGED
        <<operationState,
          controllable,
          nextControlSequence,
          workerHighWater,
          handlerState,
          ackState,
          ackOperation,
          ackSequence,
          payloadReleased,
          busyRejected,
          secondOutstanding,
          replayObserved,
          replayAcknowledged,
          protocolFailure>>

DeliverMismatchedAcknowledgment ==
    /\ ~protocolFailure
    /\ hostState = Pending
    /\ ackState = NoAck
    /\ requestSequence < MaxControlSequence
    /\ IF MUTATION_ACCEPT_MISMATCHED_ACKNOWLEDGMENT
       THEN
           /\ ackState' = AckResult
           /\ ackOperation' = OtherOperation
           /\ ackSequence' = requestSequence + 1
           /\ UNCHANGED <<hostState, protocolFailure>>
       ELSE
           /\ hostState' = Failed
           /\ protocolFailure' = TRUE
           /\ UNCHANGED <<ackState, ackOperation, ackSequence>>
    /\ UNCHANGED
        <<operationState,
          controllable,
          nextControlSequence,
          requestOperation,
          requestSequence,
          workerHighWater,
          handlerState,
          outcomeOperation,
          outcomeSequence,
          payloadReleased,
          busyRejected,
          secondOutstanding,
          replayObserved,
          replayAcknowledged>>

ReplayControl ==
    /\ ~protocolFailure
    /\ operationState = Active
    /\ hostState = Idle
    /\ workerHighWater > 0
    /\ replayObserved' = TRUE
    /\ IF MUTATION_ACCEPT_REPLAY
       THEN
           /\ replayAcknowledged' = TRUE
           /\ UNCHANGED protocolFailure
       ELSE
           /\ protocolFailure' = TRUE
           /\ UNCHANGED replayAcknowledged
    /\ UNCHANGED
        <<operationState,
          controllable,
          hostState,
          nextControlSequence,
          requestOperation,
          requestSequence,
          workerHighWater,
          handlerState,
          ackState,
          ackOperation,
          ackSequence,
          outcomeOperation,
          outcomeSequence,
          payloadReleased,
          busyRejected,
          secondOutstanding>>

Next ==
    \/ RequestControl
    \/ ConcurrentRequest
    \/ Settle
    \/ SealControl
    \/ WorkerBeginControl
    \/ WorkerObserveNotActive
    \/ CompleteHandler
    \/ CompleteHandlerNotActive
    \/ ReceiveAcknowledgment
    \/ ClearOutcome
    \/ DeliverMismatchedAcknowledgment
    \/ ReplayControl

Spec ==
    /\ Init
    /\ [][Next]_vars
    /\ WF_vars(WorkerBeginControl)
    /\ WF_vars(WorkerObserveNotActive)
    /\ WF_vars(CompleteHandler)
    /\ WF_vars(CompleteHandlerNotActive)
    /\ WF_vars(ReceiveAcknowledgment)

TypeOK ==
    /\ operationState \in OperationStates
    /\ controllable \in BOOLEAN
    /\ hostState \in HostStates
    /\ nextControlSequence \in 1..(MaxControlSequence + 1)
    /\ requestOperation \in OperationReferences
    /\ requestSequence \in 0..MaxControlSequence
    /\ workerHighWater \in 0..MaxControlSequence
    /\ handlerState \in HandlerStates
    /\ ackState \in AckStates
    /\ ackOperation \in OperationReferences
    /\ ackSequence \in 0..MaxControlSequence
    /\ outcomeOperation \in OperationReferences
    /\ outcomeSequence \in 0..MaxControlSequence
    /\ payloadReleased \in BOOLEAN
    /\ busyRejected \in BOOLEAN
    /\ secondOutstanding \in BOOLEAN
    /\ replayObserved \in BOOLEAN
    /\ replayAcknowledged \in BOOLEAN
    /\ protocolFailure \in BOOLEAN

AtMostOneOutstanding ==
    ~secondOutstanding

DeliveredAcknowledgmentNamesPendingRequest ==
    ackState # NoAck
    =>
    /\ hostState = Pending
    /\ ackOperation = requestOperation
    /\ requestSequence > 0
    /\ ackSequence = requestSequence

CommittedOutcomeNamesPendingRequest ==
    hostState \in {Acknowledged, NotActive}
    =>
    /\ outcomeOperation = requestOperation
    /\ outcomeSequence = requestSequence

NotActiveRequiresUncontrollable ==
    (ackState = AckNotActive \/ hostState = NotActive)
    =>
    ~controllable

SettlementRetainsPendingRequest ==
    /\ ~protocolFailure
    /\ payloadReleased
    /\ requestSequence > 0
    /\ ackState = NoAck
    /\ handlerState = Running
    =>
    hostState = Pending

ReplayNeverAcknowledged ==
    ~replayAcknowledged

FreshSequenceOnly ==
    handlerState = Running
    =>
    /\ requestSequence > 0
    /\ requestSequence = workerHighWater

PendingEventuallyCompletes ==
    hostState = Pending
    ~>
    hostState \in {Acknowledged, NotActive, Failed}

SettlementBeforeAcknowledgmentReachable ==
    ~(/\ operationState = Settled
      /\ payloadReleased
      /\ hostState = Pending
      /\ handlerState = Running)

=============================================================================
