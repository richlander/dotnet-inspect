-------------------- MODULE WorkspaceRealizationCutover --------------------
EXTENDS FiniteSets, Naturals, TLC

CONSTANT Fault

ASSUME Fault \in {
    "None",
    "PostCutoverAdmission",
    "EarlyClose",
    "StaleCandidatePublish",
    "TransferAuthority",
    "ForgetFailure",
    "NeverSettle",
    "BypassCandidateSettlement",
    "ReadyWithConstruction",
    "PrematureCoordinatorClose",
    "PostCloseAdmission",
    "ReviveAfterClose"
}

Realizations == 1..3
Operations == 1..2
ConstructionOperations == 1..2
NoRealization == 0
NoDefinition == <<"None", 0>>
Definition(r) == <<"Definition", r>>
Definitions == {Definition(r) : r \in Realizations}

Phases ==
    {"Unused", "Preparing", "Completing", "Ready", "Active", "Draining",
     "Settled", "Failed"}
CoordinatorStates == {"Open", "Closing", "Closed"}

VARIABLES
    coordinatorState,
    closeTargets,
    phase,
    active,
    candidate,
    pendingCandidate,
    candidateBarrier,
    admissionOpen,
    constructionAdmissionOpen,
    constructionRealization,
    constructionAdmittedWhileCurrent,
    operationRealization,
    operationDefinition,
    admittedWhileSelected,
    closeRequested,
    failures,
    witnesses

vars ==
    <<coordinatorState, closeTargets,
      phase, active, candidate, pendingCandidate, candidateBarrier,
      admissionOpen, constructionAdmissionOpen, constructionRealization,
      constructionAdmittedWhileCurrent, operationRealization,
      operationDefinition,
      admittedWhileSelected, closeRequested, failures, witnesses>>

Holders(r) ==
    {operation \in Operations :
        operationRealization[operation] = r}

ConstructionHolders(r) ==
    {operation \in ConstructionOperations :
        constructionRealization[operation] = r}

UnusedRealizations ==
    {r \in Realizations : phase[r] = "Unused"}

CandidateBarrierPhase ==
    IF candidateBarrier = NoRealization
    THEN "None"
    ELSE phase[candidateBarrier]

NoHolders(r) ==
    ConstructionHolders(r) = {} /\ Holders(r) = {}

Init ==
    /\ coordinatorState = "Open"
    /\ closeTargets = {}
    /\ phase = [r \in Realizations |-> "Unused"]
    /\ active = NoRealization
    /\ candidate = NoRealization
    /\ pendingCandidate = NoRealization
    /\ candidateBarrier = NoRealization
    /\ admissionOpen = [r \in Realizations |-> FALSE]
    /\ constructionAdmissionOpen =
        [r \in Realizations |-> FALSE]
    /\ constructionRealization =
        [operation \in ConstructionOperations |-> NoRealization]
    /\ constructionAdmittedWhileCurrent =
        [operation \in ConstructionOperations |-> TRUE]
    /\ operationRealization =
        [operation \in Operations |-> NoRealization]
    /\ operationDefinition =
        [operation \in Operations |-> NoDefinition]
    /\ admittedWhileSelected =
        [operation \in Operations |-> TRUE]
    /\ closeRequested = [r \in Realizations |-> FALSE]
    /\ failures = {}
    /\ witnesses = {}

BeginCandidate(r) ==
    /\ coordinatorState = "Open"
    /\ candidate = NoRealization
    /\ pendingCandidate = NoRealization
    /\ CandidateBarrierPhase \in {"None", "Settled", "Failed"}
    /\ r \in UnusedRealizations
    /\ phase' = [phase EXCEPT ![r] = "Preparing"]
    /\ candidate' = r
    /\ candidateBarrier' = NoRealization
    /\ constructionAdmissionOpen' =
        [constructionAdmissionOpen EXCEPT ![r] = TRUE]
    /\ UNCHANGED <<coordinatorState, closeTargets,
                   active, pendingCandidate,
                   constructionRealization,
                   constructionAdmittedWhileCurrent, admissionOpen,
                   operationRealization,
                   operationDefinition, admittedWhileSelected,
                   closeRequested, failures, witnesses>>

CompleteCandidate(r) ==
    /\ coordinatorState = "Open"
    /\ candidate = r
    /\ phase[r] = "Preparing"
    /\ phase' = [phase EXCEPT ![r] = "Completing"]
    /\ constructionAdmissionOpen' =
        [constructionAdmissionOpen EXCEPT ![r] = FALSE]
    /\ UNCHANGED <<coordinatorState, closeTargets,
                   active, candidate, pendingCandidate, candidateBarrier,
                   admissionOpen,
                   constructionRealization,
                   constructionAdmittedWhileCurrent,
                   operationRealization, operationDefinition,
                   admittedWhileSelected, closeRequested, failures,
                   witnesses>>

ReadyCandidate(r) ==
    /\ coordinatorState = "Open"
    /\ candidate = r
    /\ phase[r] = "Completing"
    /\ ConstructionHolders(r) = {} \/ Fault = "ReadyWithConstruction"
    /\ phase' = [phase EXCEPT ![r] = "Ready"]
    /\ UNCHANGED <<coordinatorState, closeTargets,
                   active, candidate, pendingCandidate, candidateBarrier,
                   admissionOpen, constructionAdmissionOpen,
                   constructionRealization,
                   constructionAdmittedWhileCurrent,
                   operationRealization, operationDefinition,
                   admittedWhileSelected, closeRequested, failures,
                   witnesses>>

FailCandidate(r) ==
    /\ coordinatorState = "Open"
    /\ candidate = r
    /\ phase[r] \in {"Preparing", "Completing", "Ready"}
    /\ phase' = [phase EXCEPT ![r] = "Draining"]
    /\ candidate' = NoRealization
    /\ candidateBarrier' = r
    /\ constructionAdmissionOpen' =
        [constructionAdmissionOpen EXCEPT ![r] = FALSE]
    /\ closeRequested' =
        [closeRequested EXCEPT
            ![r] = ConstructionHolders(r) = {} /\ Holders(r) = {}]
    /\ witnesses' = witnesses \cup {"CandidateFailed"}
    /\ UNCHANGED <<coordinatorState, closeTargets,
                   active, pendingCandidate, admissionOpen,
                   constructionRealization,
                   constructionAdmittedWhileCurrent,
                   operationRealization,
                   operationDefinition, admittedWhileSelected, failures>>

SupersedeCandidate(old, replacement) ==
    /\ coordinatorState = "Open"
    /\ candidate = old
    /\ phase[old] \in {"Preparing", "Completing", "Ready"}
    /\ replacement \in UnusedRealizations
    /\ phase' = [phase EXCEPT ![old] = "Draining"]
    /\ candidate' = NoRealization
    /\ pendingCandidate' = replacement
    /\ candidateBarrier' = old
    /\ constructionAdmissionOpen' =
        [constructionAdmissionOpen EXCEPT ![old] = FALSE]
    /\ closeRequested' =
        [closeRequested EXCEPT
            ![old] =
                ConstructionHolders(old) = {} /\ Holders(old) = {}]
    /\ witnesses' = witnesses \cup {"CandidateSuperseded"}
    /\ UNCHANGED <<coordinatorState, closeTargets,
                   active, admissionOpen, constructionRealization,
                   constructionAdmittedWhileCurrent, operationRealization,
                   operationDefinition, admittedWhileSelected, failures>>

SupersedePendingCandidate(old, replacement) ==
    /\ coordinatorState = "Open"
    /\ candidate = NoRealization
    /\ pendingCandidate = old
    /\ old \in UnusedRealizations
    /\ replacement \in UnusedRealizations \ {old}
    /\ pendingCandidate' = replacement
    /\ UNCHANGED <<coordinatorState, closeTargets,
                   phase, active, candidate, candidateBarrier,
                   admissionOpen, constructionAdmissionOpen,
                   constructionRealization,
                   constructionAdmittedWhileCurrent, operationRealization,
                   operationDefinition, admittedWhileSelected,
                   closeRequested, failures, witnesses>>

AdmitPendingCandidate(r) ==
    /\ coordinatorState = "Open"
    /\ candidate = NoRealization
    /\ pendingCandidate = r
    /\ r \in UnusedRealizations
    /\ candidateBarrier # NoRealization
    /\ \/ CandidateBarrierPhase \in {"Settled", "Failed"}
       \/ Fault = "BypassCandidateSettlement"
    /\ phase' = [phase EXCEPT ![r] = "Preparing"]
    /\ candidate' = r
    /\ pendingCandidate' = NoRealization
    /\ constructionAdmissionOpen' =
        [constructionAdmissionOpen EXCEPT ![r] = TRUE]
    /\ candidateBarrier' =
        IF Fault = "BypassCandidateSettlement"
            /\ CandidateBarrierPhase \notin {"Settled", "Failed"}
        THEN candidateBarrier
        ELSE NoRealization
    /\ UNCHANGED <<coordinatorState, closeTargets,
                   active, admissionOpen, constructionRealization,
                   constructionAdmittedWhileCurrent, operationRealization,
                   operationDefinition, admittedWhileSelected,
                   closeRequested, failures, witnesses>>

CutOver(r) ==
    /\ coordinatorState = "Open"
    /\ candidate = r
    /\ phase[r] = "Ready"
    /\ LET predecessor == active
       IN /\ phase' =
              [current \in Realizations |->
                  IF current = r THEN "Active"
                  ELSE IF current = predecessor
                          /\ predecessor # NoRealization
                       THEN "Draining"
                       ELSE phase[current]]
          /\ active' = r
          /\ candidate' = NoRealization
          /\ pendingCandidate' = NoRealization
          /\ candidateBarrier' = NoRealization
          /\ admissionOpen' =
              [current \in Realizations |-> current = r]
          /\ constructionAdmissionOpen' =
              [current \in Realizations |-> FALSE]
          /\ closeRequested' =
              [current \in Realizations |->
                  IF current = predecessor
                      /\ predecessor # NoRealization
                      /\ ConstructionHolders(predecessor) = {}
                      /\ Holders(predecessor) = {}
                  THEN TRUE
                  ELSE closeRequested[current]]
          /\ operationRealization' =
              [operation \in Operations |->
                  IF Fault = "TransferAuthority"
                      /\ predecessor # NoRealization
                      /\ operationRealization[operation] = predecessor
                  THEN r
                  ELSE operationRealization[operation]]
          /\ witnesses' =
              witnesses
              \cup {"Cutover"}
              \cup (IF predecessor # NoRealization
                         /\ Holders(predecessor) # {}
                    THEN {"PredecessorDrainingWithLease"}
                    ELSE {})
    /\ UNCHANGED <<coordinatorState, closeTargets,
                   constructionRealization,
                   constructionAdmittedWhileCurrent,
                   operationDefinition, admittedWhileSelected, failures>>

PublishStaleCandidate(r) ==
    /\ Fault = "StaleCandidatePublish"
    /\ coordinatorState = "Open"
    /\ candidate # r
    /\ phase[r] = "Draining"
    /\ phase' = [phase EXCEPT ![r] = "Active"]
    /\ active' = r
    /\ admissionOpen' =
        [current \in Realizations |-> current = r]
    /\ UNCHANGED <<coordinatorState, closeTargets,
                   candidate, pendingCandidate, candidateBarrier,
                   constructionAdmissionOpen, constructionRealization,
                   constructionAdmittedWhileCurrent,
                   operationRealization, operationDefinition,
                   admittedWhileSelected, closeRequested, failures,
                   witnesses>>

Admit(operation, r) ==
    /\ operationRealization[operation] = NoRealization
    /\ IF Fault = "PostCloseAdmission"
       THEN \/ /\ coordinatorState = "Open"
                 /\ r = active
                 /\ admissionOpen[r]
                 /\ phase[r] = "Active"
            \/ /\ coordinatorState # "Open"
                 /\ phase[r] = "Draining"
       ELSE /\ coordinatorState = "Open"
            /\ IF Fault = "PostCutoverAdmission"
               THEN \/ /\ r = active
                       /\ admissionOpen[r]
                       /\ phase[r] = "Active"
                    \/ phase[r] = "Draining"
               ELSE /\ r = active
                    /\ admissionOpen[r]
                    /\ phase[r] = "Active"
    /\ operationRealization' =
        [operationRealization EXCEPT ![operation] = r]
    /\ operationDefinition' =
        [operationDefinition EXCEPT
            ![operation] = Definition(r)]
    /\ admittedWhileSelected' =
        [admittedWhileSelected EXCEPT
            ![operation] =
                r = active /\ admissionOpen[r]
                    /\ phase[r] = "Active"]
    /\ UNCHANGED <<coordinatorState, closeTargets,
                   phase, active, candidate, pendingCandidate,
                   candidateBarrier, admissionOpen,
                   constructionAdmissionOpen, constructionRealization,
                   constructionAdmittedWhileCurrent,
                   closeRequested, failures, witnesses>>

Release(operation) ==
    /\ operationRealization[operation] # NoRealization
    /\ operationRealization' =
        [operationRealization EXCEPT
            ![operation] = NoRealization]
    /\ operationDefinition' =
        [operationDefinition EXCEPT
            ![operation] = NoDefinition]
    /\ admittedWhileSelected' =
        [admittedWhileSelected EXCEPT ![operation] = TRUE]
    /\ UNCHANGED <<coordinatorState, closeTargets,
                   phase, active, candidate, pendingCandidate,
                   candidateBarrier, admissionOpen,
                   constructionAdmissionOpen, constructionRealization,
                   constructionAdmittedWhileCurrent,
                   closeRequested, failures, witnesses>>

AdmitConstruction(operation, r) ==
    /\ coordinatorState = "Open"
    /\ constructionRealization[operation] = NoRealization
    /\ r = candidate
    /\ constructionAdmissionOpen[r]
    /\ phase[r] = "Preparing"
    /\ constructionRealization' =
        [constructionRealization EXCEPT ![operation] = r]
    /\ constructionAdmittedWhileCurrent' =
        [constructionAdmittedWhileCurrent EXCEPT
            ![operation] =
                r = candidate /\ constructionAdmissionOpen[r]
                    /\ phase[r] = "Preparing"]
    /\ UNCHANGED <<coordinatorState, closeTargets,
                   phase, active, candidate, pendingCandidate,
                   candidateBarrier, admissionOpen,
                   constructionAdmissionOpen, operationRealization,
                   operationDefinition, admittedWhileSelected,
                   closeRequested, failures, witnesses>>

ReleaseConstruction(operation) ==
    /\ constructionRealization[operation] # NoRealization
    /\ constructionRealization' =
        [constructionRealization EXCEPT
            ![operation] = NoRealization]
    /\ constructionAdmittedWhileCurrent' =
        [constructionAdmittedWhileCurrent EXCEPT
            ![operation] = TRUE]
    /\ UNCHANGED <<coordinatorState, closeTargets,
                   phase, active, candidate, pendingCandidate,
                   candidateBarrier, admissionOpen,
                   constructionAdmissionOpen, operationRealization,
                   operationDefinition, admittedWhileSelected,
                   closeRequested, failures, witnesses>>

RequestClose(r) ==
    /\ phase[r] = "Draining"
    /\ ~closeRequested[r]
    /\ (NoHolders(r) \/ Fault = "EarlyClose")
    /\ closeRequested' = [closeRequested EXCEPT ![r] = TRUE]
    /\ UNCHANGED <<coordinatorState, closeTargets,
                   phase, active, candidate, pendingCandidate,
                   candidateBarrier, admissionOpen,
                   constructionAdmissionOpen, constructionRealization,
                   constructionAdmittedWhileCurrent,
                   operationRealization, operationDefinition,
                   admittedWhileSelected, failures, witnesses>>

Settle(r, result) ==
    /\ Fault # "NeverSettle"
    /\ phase[r] = "Draining"
    /\ closeRequested[r]
    /\ ConstructionHolders(r) = {}
    /\ Holders(r) = {}
    /\ result \in {"Succeeded", "Failed"}
    /\ phase' =
        [phase EXCEPT
            ![r] = IF result = "Succeeded"
                    THEN "Settled"
                    ELSE "Failed"]
    /\ failures' =
        IF result = "Failed" /\ Fault # "ForgetFailure"
        THEN failures \cup {r}
        ELSE failures
    /\ witnesses' =
        IF result = "Failed"
        THEN witnesses \cup {"SettlementFailed"}
        ELSE witnesses
    /\ UNCHANGED <<coordinatorState, closeTargets,
                   active, candidate, pendingCandidate, candidateBarrier,
                   admissionOpen, constructionAdmissionOpen,
                   constructionRealization,
                   constructionAdmittedWhileCurrent,
                   operationRealization, operationDefinition,
                   admittedWhileSelected, closeRequested>>

SettleAny(r) ==
    \E result \in {"Succeeded", "Failed"} : Settle(r, result)

CloseCoordinator ==
    /\ coordinatorState = "Open"
    /\ LET retiring == {active, candidate} \ {NoRealization}
           recorded == {r \in Realizations : phase[r] # "Unused"}
       IN /\ coordinatorState' = "Closing"
          /\ closeTargets' = recorded
          /\ phase' =
              [r \in Realizations |->
                  IF r \in retiring THEN "Draining" ELSE phase[r]]
          /\ active' = NoRealization
          /\ candidate' = NoRealization
          /\ pendingCandidate' = NoRealization
          /\ candidateBarrier' = NoRealization
          /\ admissionOpen' =
              [r \in Realizations |-> FALSE]
          /\ constructionAdmissionOpen' =
              [r \in Realizations |-> FALSE]
          /\ closeRequested' =
              [r \in Realizations |->
                  IF r \in retiring
                  THEN NoHolders(r)
                  ELSE closeRequested[r]]
          /\ witnesses' =
              witnesses
              \cup {"CoordinatorClose"}
              \cup (IF active # NoRealization
                         /\ candidate # NoRealization
                    THEN {"CloseWithActiveAndCandidate"}
                    ELSE {})
              \cup (IF pendingCandidate # NoRealization
                    THEN {"CloseCancelsPendingCandidate"}
                    ELSE {})
              \cup (IF "PredecessorDrainingWithLease" \in witnesses
                         /\ \E r \in Realizations :
                                phase[r] = "Draining"
                    THEN {"CloseWithDrainingPredecessor"}
                    ELSE {})
    /\ UNCHANGED <<constructionRealization,
                   constructionAdmittedWhileCurrent,
                   operationRealization, operationDefinition,
                   admittedWhileSelected, failures>>

AllCloseTargetsTerminal ==
    \A r \in closeTargets : phase[r] \in {"Settled", "Failed"}

FinishCoordinatorClose ==
    /\ coordinatorState = "Closing"
    /\ \/ AllCloseTargetsTerminal
       \/ /\ Fault = "PrematureCoordinatorClose"
          /\ \E r \in closeTargets :
              ConstructionHolders(r) # {} \/ Holders(r) # {}
    /\ coordinatorState' = "Closed"
    /\ witnesses' =
        witnesses
        \cup {"CoordinatorClosed"}
        \cup (IF closeTargets \intersect failures # {}
              THEN {"CloseCompletedWithFailure"}
              ELSE {})
    /\ UNCHANGED <<closeTargets, phase, active, candidate, pendingCandidate,
                   candidateBarrier, admissionOpen,
                   constructionAdmissionOpen, constructionRealization,
                   constructionAdmittedWhileCurrent,
                   operationRealization, operationDefinition,
                   admittedWhileSelected, closeRequested, failures>>

ReviveAfterCoordinatorClose(r) ==
    /\ Fault = "ReviveAfterClose"
    /\ coordinatorState = "Closed"
    /\ r \in closeTargets
    /\ phase[r] \in {"Settled", "Failed"}
    /\ phase' = [phase EXCEPT ![r] = "Active"]
    /\ active' = r
    /\ admissionOpen' =
        [current \in Realizations |-> current = r]
    /\ witnesses' = witnesses \cup {"RevivedAfterClose"}
    /\ UNCHANGED <<coordinatorState, closeTargets,
                   candidate, pendingCandidate, candidateBarrier,
                   constructionAdmissionOpen, constructionRealization,
                   constructionAdmittedWhileCurrent,
                   operationRealization, operationDefinition,
                   admittedWhileSelected, closeRequested, failures>>

Next ==
    \/ CloseCoordinator
    \/ FinishCoordinatorClose
    \/ \E r \in Realizations :
        BeginCandidate(r)
        \/ CompleteCandidate(r)
        \/ ReadyCandidate(r)
        \/ FailCandidate(r)
        \/ CutOver(r)
        \/ PublishStaleCandidate(r)
        \/ RequestClose(r)
        \/ SettleAny(r)
        \/ ReviveAfterCoordinatorClose(r)
    \/ \E old, replacement \in Realizations :
        SupersedeCandidate(old, replacement)
        \/ SupersedePendingCandidate(old, replacement)
    \/ \E r \in Realizations :
        AdmitPendingCandidate(r)
    \/ \E operation \in Operations, r \in Realizations :
        Admit(operation, r)
    \/ \E operation \in Operations :
        Release(operation)
    \/ \E operation \in ConstructionOperations, r \in Realizations :
        AdmitConstruction(operation, r)
    \/ \E operation \in ConstructionOperations :
        ReleaseConstruction(operation)

Spec ==
    Init /\ [][Next]_vars

FairSpec ==
    /\ Spec
    /\ \A operation \in Operations : WF_vars(Release(operation))
    /\ \A operation \in ConstructionOperations :
        WF_vars(ReleaseConstruction(operation))
    /\ \A r \in Realizations :
        WF_vars(RequestClose(r)) /\ WF_vars(SettleAny(r))
    /\ WF_vars(FinishCoordinatorClose)

TypeOK ==
    /\ coordinatorState \in CoordinatorStates
    /\ closeTargets \subseteq Realizations
    /\ phase \in [Realizations -> Phases]
    /\ active \in Realizations \cup {NoRealization}
    /\ candidate \in Realizations \cup {NoRealization}
    /\ pendingCandidate \in Realizations \cup {NoRealization}
    /\ candidateBarrier \in Realizations \cup {NoRealization}
    /\ admissionOpen \in [Realizations -> BOOLEAN]
    /\ constructionAdmissionOpen \in [Realizations -> BOOLEAN]
    /\ constructionRealization \in
        [ConstructionOperations -> Realizations \cup {NoRealization}]
    /\ constructionAdmittedWhileCurrent \in
        [ConstructionOperations -> BOOLEAN]
    /\ operationRealization \in
        [Operations -> Realizations \cup {NoRealization}]
    /\ operationDefinition \in
        [Operations -> Definitions \cup {NoDefinition}]
    /\ admittedWhileSelected \in [Operations -> BOOLEAN]
    /\ closeRequested \in [Realizations -> BOOLEAN]
    /\ failures \subseteq Realizations
    /\ witnesses \subseteq
        {"CandidateFailed", "CandidateSuperseded", "Cutover",
         "PredecessorDrainingWithLease", "SettlementFailed",
         "CoordinatorClose", "CloseWithActiveAndCandidate",
         "CloseCancelsPendingCandidate",
         "CloseWithDrainingPredecessor", "CoordinatorClosed",
         "CloseCompletedWithFailure", "RevivedAfterClose"}

OnlySelectedRealizationIsActive ==
    \A r \in Realizations :
        phase[r] = "Active" <=> r = active

OnlySelectedRealizationAdmits ==
    \A r \in Realizations :
        admissionOpen[r] <=> r = active

CandidateIsUnpublished ==
    candidate # NoRealization =>
        /\ candidate # active
        /\ phase[candidate] \in {"Preparing", "Completing", "Ready"}
        /\ ~admissionOpen[candidate]

OnlyCurrentCandidateAdmitsConstruction ==
    \A r \in Realizations :
        constructionAdmissionOpen[r] <=>
            r = candidate /\ phase[r] = "Preparing"

PendingCandidateWaitsForSettlement ==
    pendingCandidate # NoRealization =>
        /\ candidate = NoRealization
        /\ phase[pendingCandidate] = "Unused"
        /\ candidateBarrier # NoRealization
        /\ CandidateBarrierPhase \in
            {"Draining", "Settled", "Failed"}

CandidateConstructionHasNoBarrier ==
    candidate # NoRealization => candidateBarrier = NoRealization

OperationAuthorityIsExact ==
    \A operation \in Operations :
        operationRealization[operation] # NoRealization =>
            /\ operationDefinition[operation] =
                Definition(operationRealization[operation])
            /\ phase[operationRealization[operation]]
                \in {"Active", "Draining"}

ConstructionAuthorityIsExact ==
    \A operation \in ConstructionOperations :
        constructionRealization[operation] # NoRealization =>
            /\ constructionAdmittedWhileCurrent[operation]
            /\ phase[constructionRealization[operation]]
                \in {"Preparing", "Completing", "Draining"}

ReadyHasNoConstructionAuthority ==
    \A r \in Realizations :
        phase[r] \in {"Ready", "Active"} =>
            ConstructionHolders(r) = {}

NoPostCutoverPredecessorAdmission ==
    \A operation \in Operations :
        operationRealization[operation] # NoRealization =>
            admittedWhileSelected[operation]

CloseBeginsAfterDrain ==
    \A r \in Realizations :
        closeRequested[r] =>
            /\ ConstructionHolders(r) = {}
            /\ Holders(r) = {}

SettledRealizationsAreDetached ==
    \A r \in Realizations :
        phase[r] \in {"Settled", "Failed"} =>
            /\ r # active
            /\ r # candidate
            /\ ~admissionOpen[r]
            /\ ~constructionAdmissionOpen[r]
            /\ ConstructionHolders(r) = {}
            /\ Holders(r) = {}

SettlementFailureIsVisible ==
    \A r \in Realizations :
        phase[r] = "Failed" => r \in failures

OpenCoordinatorHasNoCloseTargets ==
    coordinatorState = "Open" => closeTargets = {}

ClosingCoordinatorHasNoAdmission ==
    coordinatorState # "Open" =>
        /\ active = NoRealization
        /\ candidate = NoRealization
        /\ pendingCandidate = NoRealization
        /\ \A r \in Realizations :
            /\ ~admissionOpen[r]
            /\ ~constructionAdmissionOpen[r]

CloseTracksEveryIssuedRealization ==
    coordinatorState # "Open" =>
        \A r \in Realizations :
            phase[r] # "Unused" => r \in closeTargets

ClosedCoordinatorHasTerminalSettlements ==
    coordinatorState = "Closed" => AllCloseTargetsTerminal

Safety ==
    /\ TypeOK
    /\ OnlySelectedRealizationIsActive
    /\ OnlySelectedRealizationAdmits
    /\ CandidateIsUnpublished
    /\ OnlyCurrentCandidateAdmitsConstruction
    /\ PendingCandidateWaitsForSettlement
    /\ CandidateConstructionHasNoBarrier
    /\ ConstructionAuthorityIsExact
    /\ ReadyHasNoConstructionAuthority
    /\ OperationAuthorityIsExact
    /\ NoPostCutoverPredecessorAdmission
    /\ CloseBeginsAfterDrain
    /\ SettledRealizationsAreDetached
    /\ SettlementFailureIsVisible
    /\ OpenCoordinatorHasNoCloseTargets
    /\ ClosingCoordinatorHasNoAdmission
    /\ CloseTracksEveryIssuedRealization
    /\ ClosedCoordinatorHasTerminalSettlements

DrainageTerminates ==
    \A r \in Realizations :
        phase[r] = "Draining" ~> phase[r] \in {"Settled", "Failed"}

CoordinatorCloseTerminates ==
    coordinatorState = "Closing" ~> coordinatorState = "Closed"

NeverCandidateFailure ==
    "CandidateFailed" \notin witnesses

NeverCandidateSupersession ==
    "CandidateSuperseded" \notin witnesses

NeverPredecessorDrainage ==
    "PredecessorDrainingWithLease" \notin witnesses

NeverSettlementFailure ==
    "SettlementFailed" \notin witnesses

NeverCloseWithActiveAndCandidate ==
    "CloseWithActiveAndCandidate" \notin witnesses

NeverCloseCancelsPendingCandidate ==
    "CloseCancelsPendingCandidate" \notin witnesses

NeverCloseWithDrainingPredecessor ==
    "CloseWithDrainingPredecessor" \notin witnesses

NeverCloseCompletedWithFailure ==
    "CloseCompletedWithFailure" \notin witnesses
=============================================================================
