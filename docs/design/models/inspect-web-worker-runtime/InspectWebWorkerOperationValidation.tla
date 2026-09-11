---------------- MODULE InspectWebWorkerOperationValidation ----------------
(***************************************************************************)
(* Finite exogenous operation-message validation model.                    *)
(*                                                                         *)
(* Operation ID and sequence are separate so the model contrasts a valid    *)
(* newer sequence for a fresh ID with the same sequence for an active ID.    *)
(* Sequence receipt is observable before ID validation and possible failure. *)
(***************************************************************************)
EXTENDS FiniteSets, Naturals, TLC

CONSTANTS
    OperationIdA,
    OperationIdB,
    MaxOperationSequence,
    Mutation

OperationIds == {OperationIdA, OperationIdB}

NoMutation == "None"
IgnoreDuplicateAcceptance == "IgnoreDuplicateAcceptance"
IgnoreRejectionAfterAcceptance == "IgnoreRejectionAfterAcceptance"
IgnoreProgressBeforeAcceptance == "IgnoreProgressBeforeAcceptance"
IgnoreDuplicateSettlement == "IgnoreDuplicateSettlement"
IgnoreAbsentRecordMessage == "IgnoreAbsentRecordMessage"
IgnoreActiveDuplicateId == "IgnoreActiveDuplicateId"
TreatAnyNewSequenceAsDuplicate == "TreatAnyNewSequenceAsDuplicate"
DropSequenceOnActiveDuplicate == "DropSequenceOnActiveDuplicate"
StallPendingValidation == "StallPendingValidation"
Mutations ==
    {NoMutation,
     IgnoreDuplicateAcceptance,
     IgnoreRejectionAfterAcceptance,
     IgnoreProgressBeforeAcceptance,
     IgnoreDuplicateSettlement,
     IgnoreAbsentRecordMessage,
     IgnoreActiveDuplicateId,
     TreatAnyNewSequenceAsDuplicate,
     DropSequenceOnActiveDuplicate,
     StallPendingValidation}

NoRecord == "NoRecord"
Assigned == "Assigned"
Accepted == "Accepted"
Settled == "Settled"
RecordPhases == {NoRecord, Assigned, Accepted, Settled}

Live == "Live"
ValidatingStart == "ValidatingStart"
Draining == "Draining"
EpochStates == {Live, ValidatingStart, Draining}

NoPendingId == "NoPendingId"
PendingIds == OperationIds \cup {NoPendingId}

NoInvalidMessage == "NoInvalidMessage"
DuplicateAcceptance == "DuplicateAcceptance"
RejectionAfterAcceptance == "RejectionAfterAcceptance"
ProgressBeforeAcceptance == "ProgressBeforeAcceptance"
DuplicateSettlement == "DuplicateSettlement"
AbsentRecordMessage == "AbsentRecordMessage"
ActiveDuplicateId == "ActiveDuplicateId"
InvalidMessages ==
    {NoInvalidMessage,
     DuplicateAcceptance,
     RejectionAfterAcceptance,
     ProgressBeforeAcceptance,
     DuplicateSettlement,
     AbsentRecordMessage,
     ActiveDuplicateId}

ASSUME
    /\ OperationIdA # OperationIdB
    /\ Cardinality(OperationIds) = 2
    /\ MaxOperationSequence = 2
    /\ Mutation \in Mutations

VARIABLES
    epochState,
    recordPhase,
    activeSequence,
    highWaterSequence,
    invalidMessage,
    invalidMessageReceived,
    protocolFailure,
    validNewerSequenceReceived,
    validNewerSequenceAdmitted,
    pendingId,
    pendingSequence

vars ==
    <<epochState,
      recordPhase,
      activeSequence,
      highWaterSequence,
      invalidMessage,
      invalidMessageReceived,
      protocolFailure,
      validNewerSequenceReceived,
      validNewerSequenceAdmitted,
      pendingId,
      pendingSequence>>

Init ==
    /\ epochState = Live
    /\ recordPhase = [id \in OperationIds |-> NoRecord]
    /\ activeSequence = [id \in OperationIds |-> 0]
    /\ highWaterSequence = 0
    /\ invalidMessage = NoInvalidMessage
    /\ invalidMessageReceived = FALSE
    /\ protocolFailure = FALSE
    /\ validNewerSequenceReceived = FALSE
    /\ validNewerSequenceAdmitted = FALSE
    /\ pendingId = NoPendingId
    /\ pendingSequence = 0

AssignFirstOperation ==
    /\ epochState = Live
    /\ highWaterSequence = 0
    /\ recordPhase[OperationIdA] = NoRecord
    /\ recordPhase' =
        [recordPhase EXCEPT ![OperationIdA] = Assigned]
    /\ activeSequence' =
        [activeSequence EXCEPT ![OperationIdA] = 1]
    /\ highWaterSequence' = 1
    /\ UNCHANGED
        <<epochState,
          invalidMessage,
          invalidMessageReceived,
          protocolFailure,
          validNewerSequenceReceived,
          validNewerSequenceAdmitted,
          pendingId,
          pendingSequence>>

ReceiveNewerStartForFreshId ==
    /\ epochState = Live
    /\ highWaterSequence = 1
    /\ recordPhase[OperationIdB] = NoRecord
    /\ epochState' = ValidatingStart
    /\ highWaterSequence' = 2
    /\ pendingId' = OperationIdB
    /\ pendingSequence' = 2
    /\ validNewerSequenceReceived' = TRUE
    /\ UNCHANGED
        <<recordPhase,
          activeSequence,
          invalidMessage,
          invalidMessageReceived,
          protocolFailure,
          validNewerSequenceAdmitted>>

ValidateFreshPendingStart ==
    /\ epochState = ValidatingStart
    /\ pendingId = OperationIdB
    /\ pendingSequence = 2
    /\ Mutation # StallPendingValidation
    /\ pendingId' = NoPendingId
    /\ pendingSequence' = 0
    /\ IF Mutation = TreatAnyNewSequenceAsDuplicate
       THEN
           /\ epochState' = Draining
           /\ protocolFailure' = TRUE
           /\ validNewerSequenceAdmitted' = FALSE
           /\ UNCHANGED <<recordPhase, activeSequence>>
       ELSE
           /\ epochState' = Live
           /\ recordPhase' =
               [recordPhase EXCEPT ![OperationIdB] = Assigned]
           /\ activeSequence' =
               [activeSequence EXCEPT ![OperationIdB] = 2]
           /\ validNewerSequenceAdmitted' = TRUE
           /\ UNCHANGED protocolFailure
    /\ UNCHANGED
        <<highWaterSequence,
          invalidMessage,
          invalidMessageReceived,
          validNewerSequenceReceived>>

ReceiveNewerStartForActiveId ==
    /\ epochState = Live
    /\ recordPhase[OperationIdA] \in {Assigned, Accepted}
    /\ activeSequence[OperationIdA] = 1
    /\ highWaterSequence = 1
    /\ IF Mutation = DropSequenceOnActiveDuplicate
       THEN
           /\ epochState' = Draining
           /\ highWaterSequence' = highWaterSequence
           /\ invalidMessage' = ActiveDuplicateId
           /\ invalidMessageReceived' = TRUE
           /\ protocolFailure' = TRUE
           /\ UNCHANGED <<pendingId, pendingSequence>>
       ELSE
           /\ epochState' = ValidatingStart
           /\ highWaterSequence' = 2
           /\ pendingId' = OperationIdA
           /\ pendingSequence' = 2
           /\ UNCHANGED
               <<invalidMessage,
                 invalidMessageReceived,
                 protocolFailure>>
    /\ UNCHANGED
        <<recordPhase,
          activeSequence,
          validNewerSequenceReceived,
          validNewerSequenceAdmitted>>

ValidateActiveDuplicate ==
    /\ epochState = ValidatingStart
    /\ pendingId = OperationIdA
    /\ pendingSequence = 2
    /\ Mutation # StallPendingValidation
    /\ invalidMessage' = ActiveDuplicateId
    /\ invalidMessageReceived' = TRUE
    /\ pendingId' = NoPendingId
    /\ pendingSequence' = 0
    /\ IF Mutation = IgnoreActiveDuplicateId
       THEN
           /\ epochState' = Live
           /\ UNCHANGED protocolFailure
       ELSE
           /\ epochState' = Draining
           /\ protocolFailure' = TRUE
    /\ UNCHANGED
        <<recordPhase,
          activeSequence,
          highWaterSequence,
          validNewerSequenceReceived,
          validNewerSequenceAdmitted>>

ReceiveAccepted(id) ==
    /\ epochState = Live
    /\ id \in OperationIds
    /\ recordPhase[id] = Assigned
    /\ recordPhase' = [recordPhase EXCEPT ![id] = Accepted]
    /\ UNCHANGED
        <<epochState,
          activeSequence,
          highWaterSequence,
          invalidMessage,
          invalidMessageReceived,
          protocolFailure,
          validNewerSequenceReceived,
          validNewerSequenceAdmitted,
          pendingId,
          pendingSequence>>

ReceiveRejected(id) ==
    /\ epochState = Live
    /\ id \in OperationIds
    /\ recordPhase[id] = Assigned
    /\ recordPhase' = [recordPhase EXCEPT ![id] = NoRecord]
    /\ activeSequence' = [activeSequence EXCEPT ![id] = 0]
    /\ UNCHANGED
        <<epochState,
          highWaterSequence,
          invalidMessage,
          invalidMessageReceived,
          protocolFailure,
          validNewerSequenceReceived,
          validNewerSequenceAdmitted,
          pendingId,
          pendingSequence>>

ReceiveProgress(id) ==
    /\ epochState = Live
    /\ id \in OperationIds
    /\ recordPhase[id] = Accepted
    /\ UNCHANGED vars

ReceiveSettled(id) ==
    /\ epochState = Live
    /\ id \in OperationIds
    /\ recordPhase[id] = Accepted
    /\ recordPhase' = [recordPhase EXCEPT ![id] = Settled]
    /\ UNCHANGED
        <<epochState,
          activeSequence,
          highWaterSequence,
          invalidMessage,
          invalidMessageReceived,
          protocolFailure,
          validNewerSequenceReceived,
          validNewerSequenceAdmitted,
          pendingId,
          pendingSequence>>

ReleaseSettled(id) ==
    /\ epochState = Live
    /\ id \in OperationIds
    /\ recordPhase[id] = Settled
    /\ recordPhase' = [recordPhase EXCEPT ![id] = NoRecord]
    /\ activeSequence' = [activeSequence EXCEPT ![id] = 0]
    /\ UNCHANGED
        <<epochState,
          highWaterSequence,
          invalidMessage,
          invalidMessageReceived,
          protocolFailure,
          validNewerSequenceReceived,
          validNewerSequenceAdmitted,
          pendingId,
          pendingSequence>>

ReceiveInvalid(kind, ignored) ==
    /\ epochState = Live
    /\ kind \in InvalidMessages \ {NoInvalidMessage}
    /\ invalidMessage' = kind
    /\ invalidMessageReceived' = TRUE
    /\ IF ignored
       THEN
           /\ UNCHANGED <<epochState, protocolFailure>>
       ELSE
           /\ epochState' = Draining
           /\ protocolFailure' = TRUE
    /\ UNCHANGED
        <<recordPhase,
          activeSequence,
          highWaterSequence,
          validNewerSequenceReceived,
          validNewerSequenceAdmitted,
          pendingId,
          pendingSequence>>

ReceiveDuplicateAcceptance(id) ==
    /\ id \in OperationIds
    /\ recordPhase[id] = Accepted
    /\ ReceiveInvalid(
        DuplicateAcceptance,
        Mutation = IgnoreDuplicateAcceptance)

ReceiveRejectionAfterAcceptance(id) ==
    /\ id \in OperationIds
    /\ recordPhase[id] = Accepted
    /\ ReceiveInvalid(
        RejectionAfterAcceptance,
        Mutation = IgnoreRejectionAfterAcceptance)

ReceiveProgressBeforeAcceptance(id) ==
    /\ id \in OperationIds
    /\ recordPhase[id] = Assigned
    /\ ReceiveInvalid(
        ProgressBeforeAcceptance,
        Mutation = IgnoreProgressBeforeAcceptance)

ReceiveDuplicateSettlement(id) ==
    /\ id \in OperationIds
    /\ recordPhase[id] = Settled
    /\ ReceiveInvalid(
        DuplicateSettlement,
        Mutation = IgnoreDuplicateSettlement)

ReceiveAbsentRecordMessage(absentId, presentId) ==
    /\ absentId \in OperationIds
    /\ presentId \in OperationIds \ {absentId}
    /\ recordPhase[absentId] = NoRecord
    /\ recordPhase[presentId] # NoRecord
    /\ ReceiveInvalid(
        AbsentRecordMessage,
        Mutation = IgnoreAbsentRecordMessage)

Next ==
    \/ AssignFirstOperation
    \/ ReceiveNewerStartForFreshId
    \/ ValidateFreshPendingStart
    \/ ReceiveNewerStartForActiveId
    \/ ValidateActiveDuplicate
    \/ \E id \in OperationIds: ReceiveAccepted(id)
    \/ \E id \in OperationIds: ReceiveRejected(id)
    \/ \E id \in OperationIds: ReceiveProgress(id)
    \/ \E id \in OperationIds: ReceiveSettled(id)
    \/ \E id \in OperationIds: ReleaseSettled(id)
    \/ \E id \in OperationIds: ReceiveDuplicateAcceptance(id)
    \/ \E id \in OperationIds: ReceiveRejectionAfterAcceptance(id)
    \/ \E id \in OperationIds: ReceiveProgressBeforeAcceptance(id)
    \/ \E id \in OperationIds: ReceiveDuplicateSettlement(id)
    \/ \E absentId \in OperationIds:
        \E presentId \in OperationIds:
            ReceiveAbsentRecordMessage(absentId, presentId)

Spec ==
    /\ Init
    /\ [][Next]_vars
    /\ WF_vars(ValidateFreshPendingStart)
    /\ WF_vars(ValidateActiveDuplicate)

TypeOK ==
    /\ epochState \in EpochStates
    /\ recordPhase \in [OperationIds -> RecordPhases]
    /\ activeSequence \in [OperationIds -> 0..MaxOperationSequence]
    /\ highWaterSequence \in 0..MaxOperationSequence
    /\ invalidMessage \in InvalidMessages
    /\ invalidMessageReceived \in BOOLEAN
    /\ protocolFailure \in BOOLEAN
    /\ validNewerSequenceReceived \in BOOLEAN
    /\ validNewerSequenceAdmitted \in BOOLEAN
    /\ pendingId \in PendingIds
    /\ pendingSequence \in 0..MaxOperationSequence

InvalidOperationMessageFailsEpoch ==
    invalidMessageReceived
    =>
    /\ protocolFailure
    /\ epochState = Draining

ActiveDuplicateFailureRequiresConsumedSequence ==
    invalidMessage = ActiveDuplicateId
    =>
    highWaterSequence = 2

ValidNewIdWithNewerSequenceIsAdmitted ==
    validNewerSequenceReceived
    =>
    \/ validNewerSequenceAdmitted
    \/ /\ epochState = ValidatingStart
       /\ pendingId = OperationIdB
       /\ pendingSequence = 2

PendingStartHasConsumedSequence ==
    epochState = ValidatingStart
    =>
    /\ pendingId \in OperationIds
    /\ pendingSequence = 2
    /\ highWaterSequence = pendingSequence
    /\ ~protocolFailure
    /\ invalidMessage = NoInvalidMessage

ActiveRecordIdentityIsExact ==
    \A id \in OperationIds:
        recordPhase[id] # NoRecord
        =>
        /\ activeSequence[id] > 0
        /\ activeSequence[id] <= highWaterSequence

PendingStartEventuallyResolves ==
    epochState = ValidatingStart ~> epochState # ValidatingStart

=============================================================================
