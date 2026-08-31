------------------- MODULE InspectWebWorkerValidation -------------------
EXTENDS FiniteSets, Naturals, TLC

\* Focused input-validation model. The lifecycle model consumes a validated
\* operation allowance, while the protocol model consumes validated epoch-work
\* identities. This model checks those two boundary decisions directly.

CONSTANTS
    OperationA,
    OperationB,
    MaxWorkSequence,
    Mutation

Operations == {OperationA, OperationB}

NoAllowance == "NoAllowance"
BoundedAllowance == "BoundedAllowance"
UnboundedAllowance == "UnboundedAllowance"
Allowances == {BoundedAllowance, UnboundedAllowance}
AdvertisedAllowances == Allowances \cup {NoAllowance}

RegisteredAllowance(o) ==
    IF o = OperationA THEN BoundedAllowance ELSE UnboundedAllowance

Ready == "Ready"
Draining == "Draining"
Closed == "Closed"
EpochStates == {Ready, Draining, Closed}

NoMutation == "None"
AcceptMismatchedAllowance == "AcceptMismatchedAllowance"
AcceptReusedWorkSequence == "AcceptReusedWorkSequence"
AcceptUnmatchedWorkFinish == "AcceptUnmatchedWorkFinish"
Mutations ==
    {NoMutation,
     AcceptMismatchedAllowance,
     AcceptReusedWorkSequence,
     AcceptUnmatchedWorkFinish}

ASSUME
    /\ Cardinality(Operations) = 2
    /\ OperationA # OperationB
    /\ MaxWorkSequence \in Nat
    /\ MaxWorkSequence > 0
    /\ Mutation \in Mutations

VARIABLES
    epochState,
    assigned,
    accepted,
    advertisedAllowance,
    workHighWater,
    activeWork,
    startedWork,
    finishedWork,
    protocolFailure

vars ==
    <<epochState,
      assigned,
      accepted,
      advertisedAllowance,
      workHighWater,
      activeWork,
      startedWork,
      finishedWork,
      protocolFailure>>

Init ==
    /\ epochState = Ready
    /\ assigned = {}
    /\ accepted = {}
    /\ advertisedAllowance =
        [o \in Operations |-> NoAllowance]
    /\ workHighWater = 0
    /\ activeWork = {}
    /\ startedWork = {}
    /\ finishedWork = {}
    /\ protocolFailure = FALSE

UnchangedWork ==
    UNCHANGED
        <<workHighWater,
          activeWork,
          startedWork,
          finishedWork>>

AssignOperation(o) ==
    /\ epochState = Ready
    /\ o \notin assigned
    /\ assigned' = assigned \cup {o}
    /\ UNCHANGED
        <<epochState,
          accepted,
          advertisedAllowance,
          protocolFailure>>
    /\ UnchangedWork

ReceiveAccepted(o, allowance) ==
    /\ epochState = Ready
    /\ o \in assigned
    /\ o \notin accepted
    /\ advertisedAllowance[o] = NoAllowance
    /\ allowance \in Allowances
    /\ advertisedAllowance' =
        [advertisedAllowance EXCEPT ![o] = allowance]
    /\ IF allowance = RegisteredAllowance(o)
       THEN
           /\ accepted' = accepted \cup {o}
           /\ UNCHANGED <<epochState, protocolFailure>>
       ELSE
           /\ IF Mutation = AcceptMismatchedAllowance
              THEN
                  /\ accepted' = accepted \cup {o}
                  /\ UNCHANGED <<epochState, protocolFailure>>
              ELSE
                  /\ UNCHANGED accepted
                  /\ epochState' = Draining
                  /\ protocolFailure' = TRUE
    /\ UNCHANGED assigned
    /\ UnchangedWork

ReceiveWorkStarted(sequence) ==
    /\ epochState = Ready
    /\ sequence \in 1..MaxWorkSequence
    /\ IF sequence > workHighWater
       THEN
           /\ workHighWater' = sequence
           /\ activeWork' = activeWork \cup {sequence}
           /\ startedWork' = startedWork \cup {sequence}
           /\ UNCHANGED
               <<epochState,
                 finishedWork,
                 protocolFailure>>
       ELSE
           /\ IF Mutation = AcceptReusedWorkSequence
                 /\ sequence \in finishedWork
              THEN
                  /\ activeWork' = activeWork \cup {sequence}
                  /\ UNCHANGED
                      <<epochState,
                        workHighWater,
                        startedWork,
                        finishedWork,
                        protocolFailure>>
              ELSE
                  /\ epochState' = Draining
                  /\ protocolFailure' = TRUE
                  /\ UNCHANGED
                      <<workHighWater,
                        activeWork,
                        startedWork,
                        finishedWork>>
    /\ UNCHANGED
        <<assigned,
          accepted,
          advertisedAllowance>>

ReceiveWorkFinished(sequence) ==
    /\ epochState = Ready
    /\ sequence \in 1..MaxWorkSequence
    /\ IF sequence \in activeWork
       THEN
           /\ activeWork' = activeWork \ {sequence}
           /\ finishedWork' = finishedWork \cup {sequence}
           /\ UNCHANGED
               <<epochState,
                 workHighWater,
                 startedWork,
                 protocolFailure>>
       ELSE
           /\ IF Mutation = AcceptUnmatchedWorkFinish
                 /\ sequence \notin startedWork
              THEN
                  /\ finishedWork' = finishedWork \cup {sequence}
                  /\ UNCHANGED
                      <<epochState,
                        workHighWater,
                        activeWork,
                        startedWork,
                        protocolFailure>>
              ELSE
                  /\ epochState' = Draining
                  /\ protocolFailure' = TRUE
                  /\ UNCHANGED
                      <<workHighWater,
                        activeWork,
                        startedWork,
                        finishedWork>>
    /\ UNCHANGED
        <<assigned,
          accepted,
          advertisedAllowance>>

DestroyRealm ==
    /\ epochState = Draining
    /\ epochState' = Closed
    /\ activeWork' = {}
    /\ UNCHANGED
        <<assigned,
          accepted,
          advertisedAllowance,
          workHighWater,
          startedWork,
          finishedWork,
          protocolFailure>>

Next ==
    \/ \E o \in Operations: AssignOperation(o)
    \/ \E o \in Operations:
           \E allowance \in Allowances:
               ReceiveAccepted(o, allowance)
    \/ \E sequence \in 1..MaxWorkSequence:
           ReceiveWorkStarted(sequence)
    \/ \E sequence \in 1..MaxWorkSequence:
           ReceiveWorkFinished(sequence)
    \/ DestroyRealm

Spec ==
    /\ Init
    /\ [][Next]_vars
    /\ WF_vars(DestroyRealm)

TypeOK ==
    /\ epochState \in EpochStates
    /\ assigned \subseteq Operations
    /\ accepted \subseteq Operations
    /\ advertisedAllowance
       \in [Operations -> AdvertisedAllowances]
    /\ workHighWater \in 0..MaxWorkSequence
    /\ activeWork \subseteq 1..MaxWorkSequence
    /\ startedWork \subseteq 1..MaxWorkSequence
    /\ finishedWork \subseteq 1..MaxWorkSequence
    /\ protocolFailure \in BOOLEAN

AcceptedAllowanceMatchesRegistration ==
    \A o \in accepted:
        advertisedAllowance[o] = RegisteredAllowance(o)

MismatchedAllowanceFailsEpoch ==
    \A o \in Operations:
        advertisedAllowance[o] # NoAllowance
          /\ advertisedAllowance[o] # RegisteredAllowance(o)
        =>
        /\ protocolFailure
        /\ epochState \in {Draining, Closed}

ActiveWorkWasNotFinished ==
    activeWork \cap finishedWork = {}

FinishedWorkWasStarted ==
    finishedWork \subseteq startedWork

DrainingEventuallyCloses ==
    epochState = Draining ~> epochState = Closed

========================================================================
