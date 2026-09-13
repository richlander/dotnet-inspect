---------------------- MODULE InspectWebWorkerControls ----------------------
(***************************************************************************)
(* Finite model for one operation-addressed Worker control port.            *)
(*                                                                         *)
(* The model separates caller completion from wire-response retirement and  *)
(* covers cancellation, settlement, not-active closure, hard destruction,   *)
(* overlap rejection, exact acknowledgment correlation, and sequence use.   *)
(***************************************************************************)
EXTENDS Naturals, TLC

CONSTANT Mutation

NoMutation == "None"
PostAfterClose == "PostAfterClose"
RetireBeforeAcknowledgment == "RetireBeforeAcknowledgment"
AcceptWrongAcknowledgment == "AcceptWrongAcknowledgment"
Mutations ==
    {NoMutation,
     PostAfterClose,
     RetireBeforeAcknowledgment,
     AcceptWrongAcknowledgment}

ASSUME Mutation \in Mutations

MaxControlSequence == 2
ControlSequences == 1..MaxControlSequence
NoControl == 0

Active == "Active"
PhysicallyClosed == "PhysicallyClosed"
Retired == "Retired"
OperationPhases == {Active, PhysicallyClosed, Retired}

Unused == "Unused"
Pending == "Pending"
Acknowledged == "Acknowledged"
NotActive == "NotActive"
Failed == "Failed"
CallerStates == {Unused, Pending, Acknowledged, NotActive, Failed}

NoStatus == "NoStatus"
AckStatus == "Acknowledged"
NotActiveStatus == "NotActive"
Statuses == {AckStatus, NotActiveStatus}

VARIABLES
    operationPhase,
    controlOpen,
    nextSequence,
    outstanding,
    wireAckSequence,
    wireAckStatus,
    callerState,
    busyRejected,
    postedAfterClose,
    retiredBeforeAck,
    wrongAckAccepted

vars ==
    <<operationPhase,
      controlOpen,
      nextSequence,
      outstanding,
      wireAckSequence,
      wireAckStatus,
      callerState,
      busyRejected,
      postedAfterClose,
      retiredBeforeAck,
      wrongAckAccepted>>

Init ==
    /\ operationPhase = Active
    /\ controlOpen = TRUE
    /\ nextSequence = 1
    /\ outstanding = NoControl
    /\ wireAckSequence = NoControl
    /\ wireAckStatus = NoStatus
    /\ callerState = [sequence \in ControlSequences |-> Unused]
    /\ busyRejected = FALSE
    /\ postedAfterClose = FALSE
    /\ retiredBeforeAck = FALSE
    /\ wrongAckAccepted = FALSE

RequestControl ==
    /\ operationPhase = Active
    /\ outstanding = NoControl
    /\ nextSequence \in ControlSequences
    /\ controlOpen \/ Mutation = PostAfterClose
    /\ outstanding' = nextSequence
    /\ callerState' =
        [callerState EXCEPT ![nextSequence] = Pending]
    /\ nextSequence' = nextSequence + 1
    /\ postedAfterClose' =
        (postedAfterClose \/ ~controlOpen)
    /\ UNCHANGED
        <<operationPhase,
          controlOpen,
          wireAckSequence,
          wireAckStatus,
          busyRejected,
          retiredBeforeAck,
          wrongAckAccepted>>

RejectBusy ==
    /\ operationPhase = Active
    /\ controlOpen
    /\ outstanding \in ControlSequences
    /\ busyRejected' = TRUE
    /\ UNCHANGED
        <<operationPhase,
          controlOpen,
          nextSequence,
          outstanding,
          wireAckSequence,
          wireAckStatus,
          callerState,
          postedAfterClose,
          retiredBeforeAck,
          wrongAckAccepted>>

Cancel ==
    /\ operationPhase = Active
    /\ controlOpen
    /\ controlOpen' = FALSE
    /\ UNCHANGED
        <<operationPhase,
          nextSequence,
          outstanding,
          wireAckSequence,
          wireAckStatus,
          callerState,
          busyRejected,
          postedAfterClose,
          retiredBeforeAck,
          wrongAckAccepted>>

Settle ==
    /\ operationPhase = Active
    /\ operationPhase' =
        IF Mutation = RetireBeforeAcknowledgment
              /\ outstanding \in ControlSequences
        THEN Retired
        ELSE PhysicallyClosed
    /\ retiredBeforeAck' =
        (retiredBeforeAck
          \/ (operationPhase' = Retired
                /\ outstanding \in ControlSequences))
    /\ controlOpen' = FALSE
    /\ UNCHANGED
        <<nextSequence,
          outstanding,
          wireAckSequence,
          wireAckStatus,
          callerState,
          busyRejected,
          postedAfterClose,
          wrongAckAccepted>>

BoundaryClose ==
    /\ operationPhase = Active
    /\ operationPhase' = PhysicallyClosed
    /\ controlOpen' = FALSE
    /\ callerState' =
        [sequence \in ControlSequences |->
            IF callerState[sequence] = Pending
            THEN Failed
            ELSE callerState[sequence]]
    /\ UNCHANGED
        <<nextSequence,
          outstanding,
          wireAckSequence,
          wireAckStatus,
          busyRejected,
          postedAfterClose,
          retiredBeforeAck,
          wrongAckAccepted>>

WorkerAcknowledge(status) ==
    /\ outstanding \in ControlSequences
    /\ wireAckSequence = NoControl
    /\ status \in Statuses
    /\ wireAckSequence' =
        IF Mutation = AcceptWrongAcknowledgment
        THEN IF outstanding = 1 THEN 2 ELSE 1
        ELSE outstanding
    /\ wireAckStatus' = status
    /\ UNCHANGED
        <<operationPhase,
          controlOpen,
          nextSequence,
          outstanding,
          callerState,
          busyRejected,
          postedAfterClose,
          retiredBeforeAck,
          wrongAckAccepted>>

ReceiveAcknowledgment ==
    /\ wireAckSequence \in ControlSequences
    /\ IF wireAckSequence = outstanding
          \/ Mutation = AcceptWrongAcknowledgment
       THEN
           /\ callerState' =
               IF callerState[outstanding] = Pending
               THEN
                   [callerState EXCEPT
                       ![outstanding] =
                           IF wireAckStatus = AckStatus
                           THEN Acknowledged
                           ELSE NotActive]
               ELSE callerState
           /\ wrongAckAccepted' =
               (wrongAckAccepted \/ wireAckSequence # outstanding)
           /\ outstanding' = NoControl
           /\ controlOpen' =
               (controlOpen /\ wireAckStatus # NotActiveStatus)
           /\ operationPhase' =
               IF operationPhase = PhysicallyClosed
               THEN Retired
               ELSE operationPhase
       ELSE
           /\ callerState' = callerState
           /\ wrongAckAccepted' = wrongAckAccepted
           /\ outstanding' = outstanding
           /\ controlOpen' = FALSE
           /\ operationPhase' = PhysicallyClosed
    /\ wireAckSequence' = NoControl
    /\ wireAckStatus' = NoStatus
    /\ UNCHANGED
        <<nextSequence,
          busyRejected,
          postedAfterClose,
          retiredBeforeAck>>

Retire ==
    /\ operationPhase = PhysicallyClosed
    /\ outstanding = NoControl
    /\ wireAckSequence = NoControl
    /\ operationPhase' = Retired
    /\ UNCHANGED
        <<controlOpen,
          nextSequence,
          outstanding,
          wireAckSequence,
          wireAckStatus,
          callerState,
          busyRejected,
          postedAfterClose,
          retiredBeforeAck,
          wrongAckAccepted>>

HardDestroy ==
    /\ operationPhase # Retired
    /\ operationPhase' = Retired
    /\ controlOpen' = FALSE
    /\ callerState' =
        [sequence \in ControlSequences |->
            IF callerState[sequence] = Pending
            THEN Failed
            ELSE callerState[sequence]]
    /\ outstanding' = NoControl
    /\ wireAckSequence' = NoControl
    /\ wireAckStatus' = NoStatus
    /\ UNCHANGED
        <<nextSequence,
          busyRejected,
          postedAfterClose,
          retiredBeforeAck,
          wrongAckAccepted>>

Next ==
    \/ RequestControl
    \/ RejectBusy
    \/ Cancel
    \/ Settle
    \/ BoundaryClose
    \/ \E status \in Statuses : WorkerAcknowledge(status)
    \/ ReceiveAcknowledgment
    \/ Retire
    \/ HardDestroy

Spec == Init /\ [][Next]_vars

TypeOK ==
    /\ operationPhase \in OperationPhases
    /\ controlOpen \in BOOLEAN
    /\ nextSequence \in 1..(MaxControlSequence + 1)
    /\ outstanding \in ControlSequences \cup {NoControl}
    /\ wireAckSequence \in ControlSequences \cup {NoControl}
    /\ wireAckStatus \in Statuses \cup {NoStatus}
    /\ callerState \in [ControlSequences -> CallerStates]
    /\ busyRejected \in BOOLEAN
    /\ postedAfterClose \in BOOLEAN
    /\ retiredBeforeAck \in BOOLEAN
    /\ wrongAckAccepted \in BOOLEAN

NoControlPostsAfterClose == ~postedAfterClose

PostedControlRetainedUntilAcknowledgment ==
    ~retiredBeforeAck

AcknowledgmentsMatchOutstandingControl ==
    ~wrongAckAccepted

TerminalControlRaceReachable ==
    ~(operationPhase = PhysicallyClosed
        /\ outstanding \in ControlSequences)

BusyRejectionReachable == ~busyRejected
=============================================================================
