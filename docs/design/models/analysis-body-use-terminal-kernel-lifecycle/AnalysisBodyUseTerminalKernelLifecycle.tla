---------------- MODULE AnalysisBodyUseTerminalKernelLifecycle ----------------
EXTENDS Naturals, Sequences, TLC

CONSTANTS Mutation, Scenario

NoMutation == "None"
InvalidateSettled == "InvalidateSettled"
SplitAdmission == "SplitAdmission"
Mutations == {NoMutation, InvalidateSettled, SplitAdmission}

FailureAfterSettlement == "FailureAfterSettlement"
CompleteSource == "CompleteSource"
Scenarios == {FailureAfterSettlement, CompleteSource}

ASSUME /\ Mutation \in Mutations
       /\ Scenario \in Scenarios

Exists == "Exists"
Count == "Count"
Rows == "Rows"
Terminals == {Exists, Count, Rows}

Unselected == "Unselected"
Running == "Running"
SourceDone == "SourceDone"
Done == "Done"
Phases == {Unselected, Running, SourceDone, Done}

Active == "Active"
Settled == "Settled"
Exhausted == "Exhausted"
Failed == "Failed"
Statuses == {Active, Settled, Exhausted, Failed}

NoCompletion == "NoCompletion"
SettledCompletion == "Settled"
ExhaustedCompletion == "Exhausted"
Completions == {NoCompletion, SettledCompletion, ExhaustedCompletion}

NoError == "NoError"
DecodeFailure == "DecodeFailure"
Errors == {NoError, DecodeFailure}

NoValue == "NoValue"

NoKernel == "NoKernel"
FusedKernel == "FusedKernel"
Kernels == {NoKernel, FusedKernel}

Rejected == "Rejected"
AdmittedZero == "AdmittedZero"
AdmittedOne == "AdmittedOne"
AdmittedTwo == "AdmittedTwo"
Failure == "Failure"
Operands == {Rejected, AdmittedZero, AdmittedOne, AdmittedTwo, Failure}

Source ==
    IF Scenario = FailureAfterSettlement
    THEN <<Rejected, AdmittedTwo, Failure, AdmittedOne>>
    ELSE <<Rejected, AdmittedTwo, AdmittedZero, AdmittedOne>>

Row(operand, occurrence) ==
    [operand |-> operand, occurrence |-> occurrence]

RowsFor(operand, position) ==
    CASE operand = AdmittedZero -> <<>>
      [] operand = AdmittedOne -> <<Row(position, 1)>>
      [] operand = AdmittedTwo ->
             <<Row(position, 1), Row(position, 2)>>
      [] OTHER -> <<>>

VARIABLES
    phase,
    selectedKernel,
    cursor,
    physicalVisits,
    status,
    completion,
    error,
    existsValue,
    countValue,
    rowsValue,
    terminalVisits,
    settledAt,
    published,
    publishedObservation

vars ==
    <<phase, selectedKernel, cursor, physicalVisits, status, completion,
      error, existsValue, countValue, rowsValue, terminalVisits, settledAt,
      published, publishedObservation>>

InitialStatus == [terminal \in Terminals |-> Active]
InitialCompletion == [terminal \in Terminals |-> NoCompletion]
InitialError == [terminal \in Terminals |-> NoError]
InitialPosition == [terminal \in Terminals |-> 0]
InitialPublication == [terminal \in Terminals |-> FALSE]
InitialPublishedObservation == [terminal \in Terminals |-> <<>>]

Init ==
    /\ phase = Unselected
    /\ selectedKernel = NoKernel
    /\ cursor = 1
    /\ physicalVisits = 0
    /\ status = InitialStatus
    /\ completion = InitialCompletion
    /\ error = InitialError
    /\ existsValue = FALSE
    /\ countValue = 0
    /\ rowsValue = <<>>
    /\ terminalVisits = InitialPosition
    /\ settledAt = InitialPosition
    /\ published = InitialPublication
    /\ publishedObservation = InitialPublishedObservation

SelectKernel ==
    /\ phase = Unselected
    /\ phase' = Running
    /\ selectedKernel' = FusedKernel
    /\ UNCHANGED
         <<cursor, physicalVisits, status, completion, error, existsValue,
           countValue, rowsValue, terminalVisits, settledAt,
           published, publishedObservation>>

VisitActive ==
    [terminal \in Terminals |->
        IF status[terminal] = Active
        THEN physicalVisits + 1
        ELSE terminalVisits[terminal]]

ProcessRejected ==
    /\ terminalVisits' = VisitActive
    /\ physicalVisits' = physicalVisits + 1
    /\ cursor' = cursor + 1
    /\ UNCHANGED
         <<phase, selectedKernel, status, completion, error, existsValue,
           countValue, rowsValue, settledAt, published,
           publishedObservation>>

ProcessAdmittedCorrect(operand) ==
    LET operandRows == RowsFor(operand, cursor)
        settlesExists ==
            /\ status[Exists] = Active
            /\ Len(operandRows) > 0
    IN /\ status' =
              [status EXCEPT
                  ![Exists] =
                      IF settlesExists
                      THEN Settled
                      ELSE @]
       /\ completion' =
              [completion EXCEPT
                  ![Exists] =
                      IF settlesExists
                      THEN SettledCompletion
                      ELSE @]
       /\ existsValue' =
              IF settlesExists
              THEN TRUE
              ELSE existsValue
       /\ countValue' =
              IF status[Count] = Active
              THEN countValue + Len(operandRows)
              ELSE countValue
       /\ rowsValue' =
              IF status[Rows] = Active
              THEN rowsValue \o operandRows
              ELSE rowsValue
       /\ terminalVisits' = VisitActive
       /\ settledAt' =
              [settledAt EXCEPT
                  ![Exists] =
                      IF settlesExists
                      THEN physicalVisits + 1
                      ELSE @]
       /\ physicalVisits' = physicalVisits + 1
       /\ cursor' = cursor + 1
       /\ UNCHANGED
            <<phase, selectedKernel, error, published,
              publishedObservation>>

ProcessAdmittedSplit(operand) ==
    LET operandRows == RowsFor(operand, cursor)
        settlesExists ==
            /\ status[Exists] = Active
            /\ Len(operandRows) > 0
    IN /\ status' =
              [status EXCEPT
                  ![Exists] =
                      IF settlesExists
                      THEN Settled
                      ELSE @]
       /\ completion' =
              [completion EXCEPT
                  ![Exists] =
                      IF settlesExists
                      THEN SettledCompletion
                      ELSE @]
       /\ existsValue' =
              IF settlesExists
              THEN TRUE
              ELSE existsValue
       /\ terminalVisits' =
              [terminalVisits EXCEPT
                  ![Exists] =
                      IF status[Exists] = Active
                      THEN physicalVisits + 1
                      ELSE @]
       /\ settledAt' =
              [settledAt EXCEPT
                  ![Exists] =
                      IF settlesExists
                      THEN physicalVisits + 1
                      ELSE @]
       /\ physicalVisits' = physicalVisits + 1
       /\ cursor' = cursor + 1
       /\ UNCHANGED
            <<phase, selectedKernel, error, countValue, rowsValue,
              published, publishedObservation>>

ProcessAdmitted(operand) ==
    IF Mutation = SplitAdmission
    THEN ProcessAdmittedSplit(operand)
    ELSE ProcessAdmittedCorrect(operand)

ProcessFailureCorrect ==
    /\ phase' = SourceDone
    /\ status' =
         [terminal \in Terminals |->
             IF status[terminal] = Active
             THEN Failed
             ELSE status[terminal]]
    /\ error' =
         [terminal \in Terminals |->
             IF status[terminal] = Active
             THEN DecodeFailure
             ELSE error[terminal]]
    /\ terminalVisits' = VisitActive
    /\ physicalVisits' = physicalVisits + 1
    /\ cursor' = cursor + 1
    /\ UNCHANGED
         <<selectedKernel, completion, existsValue, countValue, rowsValue,
           settledAt, published, publishedObservation>>

ProcessFailureInvalidatingSettled ==
    /\ phase' = SourceDone
    /\ status' = [terminal \in Terminals |-> Failed]
    /\ error' = [terminal \in Terminals |-> DecodeFailure]
    /\ terminalVisits' =
         [terminal \in Terminals |-> physicalVisits + 1]
    /\ physicalVisits' = physicalVisits + 1
    /\ cursor' = cursor + 1
    /\ UNCHANGED
         <<selectedKernel, completion, existsValue, countValue, rowsValue,
           settledAt, published, publishedObservation>>

ProcessFailure ==
    IF Mutation = InvalidateSettled
    THEN ProcessFailureInvalidatingSettled
    ELSE ProcessFailureCorrect

ProcessOperand ==
    /\ phase = Running
    /\ cursor <= Len(Source)
    /\ LET operand == Source[cursor]
       IN CASE operand = Rejected -> ProcessRejected
            [] operand = Failure -> ProcessFailure
            [] OTHER -> ProcessAdmitted(operand)

ExhaustSource ==
    /\ phase = Running
    /\ cursor > Len(Source)
    /\ phase' = SourceDone
    /\ status' =
         [terminal \in Terminals |->
             IF status[terminal] = Active
             THEN Exhausted
             ELSE status[terminal]]
    /\ completion' =
         [terminal \in Terminals |->
             IF status[terminal] = Active
             THEN ExhaustedCompletion
             ELSE completion[terminal]]
    /\ UNCHANGED
         <<selectedKernel, cursor, physicalVisits, error, existsValue,
           countValue, rowsValue, terminalVisits, settledAt,
           published, publishedObservation>>

CurrentObservation(terminal) ==
    CASE terminal = Exists ->
             <<status[terminal], completion[terminal],
               IF status[terminal] = Failed
               THEN NoValue
               ELSE existsValue,
               error[terminal]>>
      [] terminal = Count ->
             <<status[terminal], completion[terminal],
               IF status[terminal] = Failed
               THEN NoValue
               ELSE countValue,
               error[terminal]>>
      [] terminal = Rows ->
             <<status[terminal], completion[terminal],
               IF status[terminal] = Failed
               THEN NoValue
               ELSE rowsValue,
               error[terminal]>>

Publish(terminal) ==
    /\ terminal \in Terminals
    /\ status[terminal] # Active
    /\ ~published[terminal]
    /\ published' = [published EXCEPT ![terminal] = TRUE]
    /\ publishedObservation' =
         [publishedObservation EXCEPT
             ![terminal] = CurrentObservation(terminal)]
    /\ UNCHANGED
         <<phase, selectedKernel, cursor, physicalVisits, status, completion,
           error, existsValue, countValue, rowsValue, terminalVisits,
           settledAt>>

PublishOne == \E terminal \in Terminals : Publish(terminal)

Complete ==
    /\ phase = SourceDone
    /\ \A terminal \in Terminals :
          published[terminal]
    /\ phase' = Done
    /\ UNCHANGED
         <<selectedKernel, cursor, physicalVisits, status, completion, error,
           existsValue, countValue, rowsValue, terminalVisits, settledAt,
           published, publishedObservation>>

Next ==
    \/ SelectKernel
    \/ ProcessOperand
    \/ ExhaustSource
    \/ PublishOne
    \/ Complete

TypeOK ==
    /\ phase \in Phases
    /\ selectedKernel \in Kernels
    /\ cursor \in 1..(Len(Source) + 1)
    /\ physicalVisits \in 0..Len(Source)
    /\ status \in [Terminals -> Statuses]
    /\ completion \in [Terminals -> Completions]
    /\ error \in [Terminals -> Errors]
    /\ existsValue \in BOOLEAN
    /\ countValue \in 0..3
    /\ rowsValue \in Seq([operand : 1..Len(Source), occurrence : 1..2])
    /\ terminalVisits \in [Terminals -> 0..Len(Source)]
    /\ settledAt \in [Terminals -> 0..Len(Source)]
    /\ published \in [Terminals -> BOOLEAN]
    /\ DOMAIN publishedObservation = Terminals

SelectionPrecedesWork ==
    physicalVisits > 0 => selectedKernel = FusedKernel

PhysicalTraversalFollowsCursor ==
    physicalVisits = cursor - 1

ActiveTerminalsFollowPhysicalTraversal ==
    \A terminal \in Terminals :
        status[terminal] = Active
        => terminalVisits[terminal] = physicalVisits

SettledTerminalsStopAtSettlement ==
    \A terminal \in Terminals :
        status[terminal] = Settled
        => /\ settledAt[terminal] > 0
           /\ terminalVisits[terminal] = settledAt[terminal]

SettlementIsStable ==
    \A terminal \in Terminals :
        settledAt[terminal] > 0
        => /\ status[terminal] = Settled
           /\ completion[terminal] = SettledCompletion

CountAndRowsShareAdmission ==
    countValue = Len(rowsValue)

PublishedObservationsRemainCurrent ==
    \A terminal \in Terminals :
        published[terminal]
        => publishedObservation[terminal] = CurrentObservation(terminal)

FailureScenarioMatchesLeanKernel ==
    /\ Scenario = FailureAfterSettlement
    /\ phase \in {SourceDone, Done}
    => /\ status[Exists] = Settled
       /\ completion[Exists] = SettledCompletion
       /\ existsValue = TRUE
       /\ error[Exists] = NoError
       /\ status[Count] = Failed
       /\ error[Count] = DecodeFailure
       /\ status[Rows] = Failed
       /\ error[Rows] = DecodeFailure
       /\ countValue = 2
       /\ Len(rowsValue) = 2
       /\ physicalVisits = 3

CompleteScenarioMatchesLeanKernel ==
    /\ Scenario = CompleteSource
    /\ phase \in {SourceDone, Done}
    => /\ status[Exists] = Settled
       /\ completion[Exists] = SettledCompletion
       /\ existsValue = TRUE
       /\ status[Count] = Exhausted
       /\ completion[Count] = ExhaustedCompletion
       /\ countValue = 3
       /\ status[Rows] = Exhausted
       /\ completion[Rows] = ExhaustedCompletion
       /\ Len(rowsValue) = 3
       /\ physicalVisits = Len(Source)

EarlyPublicationBeforeFailure ==
    /\ Scenario = FailureAfterSettlement
    /\ phase = Running
    /\ cursor = 3
    /\ published[Exists]
    /\ status[Count] = Active
    /\ status[Rows] = Active

EarlyPublicationBeforeFailureNotReached ==
    ~EarlyPublicationBeforeFailure

EventuallyDone == <>(phase = Done)

Spec ==
    /\ Init
    /\ [][Next]_vars
    /\ WF_vars(SelectKernel)
    /\ WF_vars(ProcessOperand)
    /\ WF_vars(ExhaustSource)
    /\ WF_vars(PublishOne)
    /\ WF_vars(Complete)

=============================================================================
