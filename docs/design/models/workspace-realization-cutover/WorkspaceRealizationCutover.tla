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
    "BypassCandidateSettlement"
}

Realizations == 1..3
Operations == 1..2
NoRealization == 0
NoDefinition == <<"None", 0>>
Definition(r) == <<"Definition", r>>
Definitions == {Definition(r) : r \in Realizations}

Phases ==
    {"Unused", "Preparing", "Ready", "Active", "Draining",
     "Settled", "Failed"}

VARIABLES
    phase,
    active,
    candidate,
    pendingCandidate,
    candidateBarrier,
    admissionOpen,
    operationRealization,
    operationDefinition,
    admittedWhileSelected,
    closeRequested,
    failures,
    witnesses

vars ==
    <<phase, active, candidate, pendingCandidate, candidateBarrier,
      admissionOpen, operationRealization, operationDefinition,
      admittedWhileSelected, closeRequested, failures, witnesses>>

Holders(r) ==
    {operation \in Operations :
        operationRealization[operation] = r}

UnusedRealizations ==
    {r \in Realizations : phase[r] = "Unused"}

CandidateBarrierPhase ==
    IF candidateBarrier = NoRealization
    THEN "None"
    ELSE phase[candidateBarrier]

Init ==
    /\ phase = [r \in Realizations |-> "Unused"]
    /\ active = NoRealization
    /\ candidate = NoRealization
    /\ pendingCandidate = NoRealization
    /\ candidateBarrier = NoRealization
    /\ admissionOpen = [r \in Realizations |-> FALSE]
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
    /\ candidate = NoRealization
    /\ pendingCandidate = NoRealization
    /\ CandidateBarrierPhase \in {"None", "Settled", "Failed"}
    /\ r \in UnusedRealizations
    /\ phase' = [phase EXCEPT ![r] = "Preparing"]
    /\ candidate' = r
    /\ candidateBarrier' = NoRealization
    /\ UNCHANGED <<active, pendingCandidate, admissionOpen,
                   operationRealization,
                   operationDefinition, admittedWhileSelected,
                   closeRequested, failures, witnesses>>

ReadyCandidate(r) ==
    /\ candidate = r
    /\ phase[r] = "Preparing"
    /\ phase' = [phase EXCEPT ![r] = "Ready"]
    /\ UNCHANGED <<active, candidate, pendingCandidate, candidateBarrier,
                   admissionOpen,
                   operationRealization, operationDefinition,
                   admittedWhileSelected, closeRequested, failures,
                   witnesses>>

FailCandidate(r) ==
    /\ candidate = r
    /\ phase[r] \in {"Preparing", "Ready"}
    /\ phase' = [phase EXCEPT ![r] = "Draining"]
    /\ candidate' = NoRealization
    /\ candidateBarrier' = r
    /\ closeRequested' = [closeRequested EXCEPT ![r] = TRUE]
    /\ witnesses' = witnesses \cup {"CandidateFailed"}
    /\ UNCHANGED <<active, pendingCandidate, admissionOpen,
                   operationRealization,
                   operationDefinition, admittedWhileSelected, failures>>

SupersedeCandidate(old, replacement) ==
    /\ candidate = old
    /\ phase[old] \in {"Preparing", "Ready"}
    /\ replacement \in UnusedRealizations
    /\ phase' = [phase EXCEPT ![old] = "Draining"]
    /\ candidate' = NoRealization
    /\ pendingCandidate' = replacement
    /\ candidateBarrier' = old
    /\ closeRequested' =
        [closeRequested EXCEPT ![old] = TRUE]
    /\ witnesses' = witnesses \cup {"CandidateSuperseded"}
    /\ UNCHANGED <<active, admissionOpen, operationRealization,
                   operationDefinition, admittedWhileSelected, failures>>

SupersedePendingCandidate(old, replacement) ==
    /\ candidate = NoRealization
    /\ pendingCandidate = old
    /\ old \in UnusedRealizations
    /\ replacement \in UnusedRealizations \ {old}
    /\ pendingCandidate' = replacement
    /\ UNCHANGED <<phase, active, candidate, candidateBarrier,
                   admissionOpen, operationRealization,
                   operationDefinition, admittedWhileSelected,
                   closeRequested, failures, witnesses>>

AdmitPendingCandidate(r) ==
    /\ candidate = NoRealization
    /\ pendingCandidate = r
    /\ r \in UnusedRealizations
    /\ candidateBarrier # NoRealization
    /\ \/ CandidateBarrierPhase \in {"Settled", "Failed"}
       \/ Fault = "BypassCandidateSettlement"
    /\ phase' = [phase EXCEPT ![r] = "Preparing"]
    /\ candidate' = r
    /\ pendingCandidate' = NoRealization
    /\ candidateBarrier' =
        IF Fault = "BypassCandidateSettlement"
            /\ CandidateBarrierPhase \notin {"Settled", "Failed"}
        THEN candidateBarrier
        ELSE NoRealization
    /\ UNCHANGED <<active, admissionOpen, operationRealization,
                   operationDefinition, admittedWhileSelected,
                   closeRequested, failures, witnesses>>

CutOver(r) ==
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
          /\ closeRequested' =
              [current \in Realizations |->
                  IF current = predecessor
                      /\ predecessor # NoRealization
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
    /\ UNCHANGED <<operationDefinition, admittedWhileSelected, failures>>

PublishStaleCandidate(r) ==
    /\ Fault = "StaleCandidatePublish"
    /\ candidate # r
    /\ phase[r] = "Draining"
    /\ phase' = [phase EXCEPT ![r] = "Active"]
    /\ active' = r
    /\ admissionOpen' =
        [current \in Realizations |-> current = r]
    /\ UNCHANGED <<candidate, pendingCandidate, candidateBarrier,
                   operationRealization, operationDefinition,
                   admittedWhileSelected, closeRequested, failures,
                   witnesses>>

Admit(operation, r) ==
    /\ operationRealization[operation] = NoRealization
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
    /\ UNCHANGED <<phase, active, candidate, pendingCandidate,
                   candidateBarrier, admissionOpen,
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
    /\ UNCHANGED <<phase, active, candidate, pendingCandidate,
                   candidateBarrier, admissionOpen,
                   closeRequested, failures, witnesses>>

RequestClose(r) ==
    /\ phase[r] = "Draining"
    /\ ~closeRequested[r]
    /\ Holders(r) = {} \/ Fault = "EarlyClose"
    /\ closeRequested' = [closeRequested EXCEPT ![r] = TRUE]
    /\ UNCHANGED <<phase, active, candidate, pendingCandidate,
                   candidateBarrier, admissionOpen,
                   operationRealization, operationDefinition,
                   admittedWhileSelected, failures, witnesses>>

Settle(r, result) ==
    /\ Fault # "NeverSettle"
    /\ phase[r] = "Draining"
    /\ closeRequested[r]
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
    /\ UNCHANGED <<active, candidate, pendingCandidate, candidateBarrier,
                   admissionOpen,
                   operationRealization, operationDefinition,
                   admittedWhileSelected, closeRequested>>

SettleAny(r) ==
    \E result \in {"Succeeded", "Failed"} : Settle(r, result)

Next ==
    \/ \E r \in Realizations :
        BeginCandidate(r)
        \/ ReadyCandidate(r)
        \/ FailCandidate(r)
        \/ CutOver(r)
        \/ PublishStaleCandidate(r)
        \/ RequestClose(r)
        \/ SettleAny(r)
    \/ \E old, replacement \in Realizations :
        SupersedeCandidate(old, replacement)
        \/ SupersedePendingCandidate(old, replacement)
    \/ \E r \in Realizations :
        AdmitPendingCandidate(r)
    \/ \E operation \in Operations, r \in Realizations :
        Admit(operation, r)
    \/ \E operation \in Operations :
        Release(operation)

Spec ==
    Init /\ [][Next]_vars

FairSpec ==
    /\ Spec
    /\ \A operation \in Operations : WF_vars(Release(operation))
    /\ \A r \in Realizations :
        WF_vars(RequestClose(r)) /\ WF_vars(SettleAny(r))

TypeOK ==
    /\ phase \in [Realizations -> Phases]
    /\ active \in Realizations \cup {NoRealization}
    /\ candidate \in Realizations \cup {NoRealization}
    /\ pendingCandidate \in Realizations \cup {NoRealization}
    /\ candidateBarrier \in Realizations \cup {NoRealization}
    /\ admissionOpen \in [Realizations -> BOOLEAN]
    /\ operationRealization \in
        [Operations -> Realizations \cup {NoRealization}]
    /\ operationDefinition \in
        [Operations -> Definitions \cup {NoDefinition}]
    /\ admittedWhileSelected \in [Operations -> BOOLEAN]
    /\ closeRequested \in [Realizations -> BOOLEAN]
    /\ failures \subseteq Realizations
    /\ witnesses \subseteq
        {"CandidateFailed", "CandidateSuperseded", "Cutover",
         "PredecessorDrainingWithLease", "SettlementFailed"}

OnlySelectedRealizationIsActive ==
    \A r \in Realizations :
        phase[r] = "Active" <=> r = active

OnlySelectedRealizationAdmits ==
    \A r \in Realizations :
        admissionOpen[r] <=> r = active

CandidateIsUnpublished ==
    candidate # NoRealization =>
        /\ candidate # active
        /\ phase[candidate] \in {"Preparing", "Ready"}
        /\ ~admissionOpen[candidate]

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

NoPostCutoverPredecessorAdmission ==
    \A operation \in Operations :
        operationRealization[operation] # NoRealization =>
            admittedWhileSelected[operation]

CloseBeginsAfterDrain ==
    \A r \in Realizations :
        closeRequested[r] => Holders(r) = {}

SettledRealizationsAreDetached ==
    \A r \in Realizations :
        phase[r] \in {"Settled", "Failed"} =>
            /\ r # active
            /\ r # candidate
            /\ ~admissionOpen[r]
            /\ Holders(r) = {}

SettlementFailureIsVisible ==
    \A r \in Realizations :
        phase[r] = "Failed" => r \in failures

Safety ==
    /\ TypeOK
    /\ OnlySelectedRealizationIsActive
    /\ OnlySelectedRealizationAdmits
    /\ CandidateIsUnpublished
    /\ PendingCandidateWaitsForSettlement
    /\ CandidateConstructionHasNoBarrier
    /\ OperationAuthorityIsExact
    /\ NoPostCutoverPredecessorAdmission
    /\ CloseBeginsAfterDrain
    /\ SettledRealizationsAreDetached
    /\ SettlementFailureIsVisible

DrainageTerminates ==
    \A r \in Realizations :
        phase[r] = "Draining" ~> phase[r] \in {"Settled", "Failed"}

NeverCandidateFailure ==
    "CandidateFailed" \notin witnesses

NeverCandidateSupersession ==
    "CandidateSuperseded" \notin witnesses

NeverPredecessorDrainage ==
    "PredecessorDrainingWithLease" \notin witnesses

NeverSettlementFailure ==
    "SettlementFailed" \notin witnesses
=============================================================================
