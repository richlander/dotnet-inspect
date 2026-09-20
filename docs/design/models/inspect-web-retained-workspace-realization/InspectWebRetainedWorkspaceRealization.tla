---------------- MODULE InspectWebRetainedWorkspaceRealization ----------------
EXTENDS FiniteSets, Naturals, TLC

CONSTANT BrowserFault, CoordinatorFault, Capacity

BrowserFaults == {
    "None",
    "ReviveDefinition",
    "StaleActivation",
    "FailureReplacesActive",
    "DeleteBeforeReplacement",
    "DeleteBeforeCandidateSettlement",
    "ForgetSettlementFailure"
}

ASSUME BrowserFault \in BrowserFaults
ASSUME CoordinatorFault \in {
    "None",
    "PostCutoverAdmission",
    "EarlyClose",
    "StaleCandidatePublish",
    "TransferAuthority",
    "ForgetFailure",
    "NeverSettle",
    "BypassCandidateSettlement",
    "ReadyWithConstruction"
}
ASSUME Capacity \in 1..3

Definitions == 1..2
NoDefinition == 0
MaxIntent == 4

VARIABLES
    coordinatorLifecycle,
    coordinatorCloseTargets,
    coordinatorPhase,
    coordinatorActive,
    coordinatorCandidate,
    coordinatorPendingCandidate,
    coordinatorCandidateBarrier,
    coordinatorAdmissionOpen,
    coordinatorConstructionAdmissionOpen,
    coordinatorConstructionRealization,
    coordinatorConstructionAdmittedWhileCurrent,
    coordinatorOperationRealization,
    coordinatorOperationDefinition,
    coordinatorAdmittedWhileSelected,
    coordinatorCloseRequested,
    coordinatorFailures,
    coordinatorWitnesses,
    retained,
    selectedDefinition,
    requestedDefinition,
    currentIntent,
    targetDefinition,
    targetIntent,
    pendingRemoval,
    lastRealization,
    observedSettlementFailures,
    staleActivationOccurred,
    witnesses

Coordinator ==
    INSTANCE WorkspaceRealizationCutover WITH
        Fault <- CoordinatorFault,
        coordinatorState <- coordinatorLifecycle,
        closeTargets <- coordinatorCloseTargets,
        phase <- coordinatorPhase,
        active <- coordinatorActive,
        candidate <- coordinatorCandidate,
        pendingCandidate <- coordinatorPendingCandidate,
        candidateBarrier <- coordinatorCandidateBarrier,
        admissionOpen <- coordinatorAdmissionOpen,
        constructionAdmissionOpen <- coordinatorConstructionAdmissionOpen,
        constructionRealization <- coordinatorConstructionRealization,
        constructionAdmittedWhileCurrent <-
            coordinatorConstructionAdmittedWhileCurrent,
        operationRealization <- coordinatorOperationRealization,
        operationDefinition <- coordinatorOperationDefinition,
        admittedWhileSelected <- coordinatorAdmittedWhileSelected,
        closeRequested <- coordinatorCloseRequested,
        failures <- coordinatorFailures,
        witnesses <- coordinatorWitnesses

Realizations == Coordinator!Realizations
Operations == Coordinator!Operations
ConstructionOperations == Coordinator!ConstructionOperations
NoRealization == Coordinator!NoRealization

coordinatorVars == <<
    coordinatorLifecycle,
    coordinatorCloseTargets,
    coordinatorPhase,
    coordinatorActive,
    coordinatorCandidate,
    coordinatorPendingCandidate,
    coordinatorCandidateBarrier,
    coordinatorAdmissionOpen,
    coordinatorConstructionAdmissionOpen,
    coordinatorConstructionRealization,
    coordinatorConstructionAdmittedWhileCurrent,
    coordinatorOperationRealization,
    coordinatorOperationDefinition,
    coordinatorAdmittedWhileSelected,
    coordinatorCloseRequested,
    coordinatorFailures,
    coordinatorWitnesses
>>

browserVars == <<
    retained,
    selectedDefinition,
    requestedDefinition,
    currentIntent,
    targetDefinition,
    targetIntent,
    pendingRemoval,
    lastRealization,
    observedSettlementFailures,
    staleActivationOccurred,
    witnesses
>>

vars == <<
    coordinatorLifecycle,
    coordinatorCloseTargets,
    coordinatorPhase,
    coordinatorActive,
    coordinatorCandidate,
    coordinatorPendingCandidate,
    coordinatorCandidateBarrier,
    coordinatorAdmissionOpen,
    coordinatorConstructionAdmissionOpen,
    coordinatorConstructionRealization,
    coordinatorConstructionAdmittedWhileCurrent,
    coordinatorOperationRealization,
    coordinatorOperationDefinition,
    coordinatorAdmittedWhileSelected,
    coordinatorCloseRequested,
    coordinatorFailures,
    coordinatorWitnesses,
    retained,
    selectedDefinition,
    requestedDefinition,
    currentIntent,
    targetDefinition,
    targetIntent,
    pendingRemoval,
    lastRealization,
    observedSettlementFailures,
    staleActivationOccurred,
    witnesses
>>

CoordinatorUnchanged ==
    UNCHANGED coordinatorVars

BrowserUnchanged ==
    UNCHANGED browserVars

ChargedRealizations ==
    {r \in Realizations :
        coordinatorPhase[r] \in {
            "Preparing", "Completing", "Ready", "Active", "Draining", "Failed"
        }}

CapacityAvailable ==
    Cardinality(ChargedRealizations) < Capacity

NoCoordinatorCandidate ==
    coordinatorCandidate = NoRealization
    /\ coordinatorPendingCandidate = NoRealization

Init ==
    /\ Coordinator!Init
    /\ retained = {}
    /\ selectedDefinition = NoDefinition
    /\ requestedDefinition = NoDefinition
    /\ currentIntent = 0
    /\ targetDefinition = [r \in Realizations |-> NoDefinition]
    /\ targetIntent = [r \in Realizations |-> 0]
    /\ pendingRemoval = NoDefinition
    /\ lastRealization = [d \in Definitions |-> NoRealization]
    /\ observedSettlementFailures = {}
    /\ staleActivationOccurred = FALSE
    /\ witnesses = {}

PublishDefinition(d) ==
    /\ d \in Definitions \ retained
    /\ retained' = retained \cup {d}
    /\ UNCHANGED <<
        selectedDefinition,
        requestedDefinition,
        currentIntent,
        targetDefinition,
        targetIntent,
        pendingRemoval,
        lastRealization,
        observedSettlementFailures,
        staleActivationOccurred,
        witnesses
        >>
    /\ CoordinatorUnchanged

RequestActivation(d) ==
    /\ d \in retained
    /\ d # selectedDefinition
    /\ NoCoordinatorCandidate
    /\ currentIntent < MaxIntent
    /\ requestedDefinition' = d
    /\ currentIntent' = currentIntent + 1
    /\ pendingRemoval' = NoDefinition
    /\ UNCHANGED <<
        retained,
        selectedDefinition,
        targetDefinition,
        targetIntent,
        lastRealization,
        observedSettlementFailures,
        staleActivationOccurred,
        witnesses
        >>
    /\ CoordinatorUnchanged

RequestReplacementWhileCandidate(d, replacement) ==
    /\ BrowserFault # "StaleActivation"
    /\ coordinatorCandidate # NoRealization
    /\ d \in retained
    /\ d # selectedDefinition
    /\ currentIntent < MaxIntent
    /\ replacement \in Realizations
    /\ Coordinator!SupersedeCandidate(
        coordinatorCandidate,
        replacement)
    /\ requestedDefinition' = d
    /\ currentIntent' = currentIntent + 1
    /\ targetDefinition' =
        [targetDefinition EXCEPT
            ![replacement] = d]
    /\ targetIntent' =
        [targetIntent EXCEPT
            ![replacement] = currentIntent + 1]
    /\ pendingRemoval' = NoDefinition
    /\ UNCHANGED <<
        retained,
        selectedDefinition,
        lastRealization,
        observedSettlementFailures,
        staleActivationOccurred,
        witnesses
        >>

RequestReplacementWhilePending(d, replacement) ==
    /\ coordinatorPendingCandidate # NoRealization
    /\ d \in retained
    /\ d # selectedDefinition
    /\ currentIntent < MaxIntent
    /\ replacement \in Realizations
    /\ Coordinator!SupersedePendingCandidate(
        coordinatorPendingCandidate,
        replacement)
    /\ requestedDefinition' = d
    /\ currentIntent' = currentIntent + 1
    /\ targetDefinition' =
        [targetDefinition EXCEPT
            ![coordinatorPendingCandidate] = NoDefinition,
            ![replacement] = d]
    /\ targetIntent' =
        [targetIntent EXCEPT
            ![coordinatorPendingCandidate] = 0,
            ![replacement] = currentIntent + 1]
    /\ pendingRemoval' = NoDefinition
    /\ UNCHANGED <<
        retained,
        selectedDefinition,
        lastRealization,
        observedSettlementFailures,
        staleActivationOccurred,
        witnesses
        >>

RequestWithoutCoordinatorSupersession(d) ==
    /\ BrowserFault = "StaleActivation"
    /\ coordinatorCandidate # NoRealization
    /\ d \in retained
    /\ d # selectedDefinition
    /\ currentIntent < MaxIntent
    /\ requestedDefinition' = d
    /\ currentIntent' = currentIntent + 1
    /\ pendingRemoval' = NoDefinition
    /\ UNCHANGED <<
        retained,
        selectedDefinition,
        targetDefinition,
        targetIntent,
        lastRealization,
        observedSettlementFailures,
        staleActivationOccurred,
        witnesses
        >>
    /\ CoordinatorUnchanged

BeginCandidate(r) ==
    /\ requestedDefinition \in retained
    /\ CapacityAvailable
    /\ Coordinator!BeginCandidate(r)
    /\ targetDefinition' =
        [targetDefinition EXCEPT ![r] = requestedDefinition]
    /\ targetIntent' =
        [targetIntent EXCEPT ![r] = currentIntent]
    /\ UNCHANGED <<
        retained,
        selectedDefinition,
        requestedDefinition,
        currentIntent,
        pendingRemoval,
        lastRealization,
        observedSettlementFailures,
        staleActivationOccurred,
        witnesses
        >>

AdmitPendingCandidate(r) ==
    /\ targetDefinition[r] = requestedDefinition
    /\ targetIntent[r] = currentIntent
    /\ targetDefinition[r] \in retained
    /\ CapacityAvailable
    /\ Coordinator!AdmitPendingCandidate(r)
    /\ BrowserUnchanged

CompleteCandidate(r) ==
    /\ Coordinator!CompleteCandidate(r)
    /\ BrowserUnchanged

ReadyCandidate(r) ==
    /\ Coordinator!ReadyCandidate(r)
    /\ BrowserUnchanged

InstalledWitnesses(r) ==
    witnesses
    \cup IF lastRealization[targetDefinition[r]] # NoRealization
             /\ lastRealization[targetDefinition[r]] # r
         THEN {"FreshReactivation"}
         ELSE {}
    \cup IF pendingRemoval # NoDefinition
         THEN {"DeleteThroughReplacement"}
         ELSE {}

CutOverCandidate(r) ==
    /\ Coordinator!CutOver(r)
    /\ (targetIntent[r] = currentIntent
        \/ BrowserFault = "StaleActivation")
    /\ selectedDefinition' = targetDefinition[r]
    /\ retained' =
        IF pendingRemoval = NoDefinition
        THEN retained
        ELSE retained \ {pendingRemoval}
    /\ requestedDefinition' = NoDefinition
    /\ pendingRemoval' = NoDefinition
    /\ lastRealization' =
        [lastRealization EXCEPT ![targetDefinition[r]] = r]
    /\ staleActivationOccurred' =
        (staleActivationOccurred \/ (targetIntent[r] # currentIntent))
    /\ witnesses' = InstalledWitnesses(r)
    /\ UNCHANGED <<
        currentIntent,
        targetDefinition,
        targetIntent,
        observedSettlementFailures
        >>

FailCandidate(r) ==
    /\ Coordinator!FailCandidate(r)
    /\ IF BrowserFault = "FailureReplacesActive"
           /\ selectedDefinition # NoDefinition
       THEN
           selectedDefinition' = NoDefinition
       ELSE
           UNCHANGED selectedDefinition
    /\ requestedDefinition' =
        IF targetIntent[r] = currentIntent
        THEN NoDefinition
        ELSE requestedDefinition
    /\ pendingRemoval' =
        IF targetIntent[r] = currentIntent
        THEN NoDefinition
        ELSE pendingRemoval
    /\ witnesses' =
        witnesses
        \cup IF selectedDefinition # NoDefinition
             THEN {"FailureWithIncumbent"}
             ELSE {}
    /\ UNCHANGED <<
        retained,
        currentIntent,
        targetDefinition,
        targetIntent,
        lastRealization,
        observedSettlementFailures,
        staleActivationOccurred
        >>

RequestDeleteActive(successor) ==
    /\ selectedDefinition # NoDefinition
    /\ successor \in retained \ {selectedDefinition}
    /\ requestedDefinition = NoDefinition
    /\ NoCoordinatorCandidate
    /\ currentIntent < MaxIntent
    /\ requestedDefinition' = successor
    /\ currentIntent' = currentIntent + 1
    /\ pendingRemoval' = selectedDefinition
    /\ retained' =
        IF BrowserFault = "DeleteBeforeReplacement"
        THEN retained \ {selectedDefinition}
        ELSE retained
    /\ UNCHANGED <<
        selectedDefinition,
        targetDefinition,
        targetIntent,
        lastRealization,
        observedSettlementFailures,
        staleActivationOccurred,
        witnesses
        >>
    /\ CoordinatorUnchanged

DeleteInactive(d) ==
    /\ d \in retained
    /\ d # selectedDefinition
    /\ d # requestedDefinition
    /\ IF coordinatorCandidate = NoRealization
       THEN TRUE
       ELSE d # targetDefinition[coordinatorCandidate]
    /\ IF coordinatorPendingCandidate = NoRealization
       THEN TRUE
       ELSE d # targetDefinition[coordinatorPendingCandidate]
    /\ IF BrowserFault = "DeleteBeforeCandidateSettlement"
       THEN TRUE
       ELSE IF coordinatorCandidateBarrier = NoRealization
            THEN TRUE
            ELSE IF coordinatorPhase[coordinatorCandidateBarrier]
                        \in {"Settled", "Failed"}
                 THEN TRUE
                 ELSE d # targetDefinition[coordinatorCandidateBarrier]
    /\ d # pendingRemoval
    /\ retained' = retained \ {d}
    /\ UNCHANGED <<
        selectedDefinition,
        requestedDefinition,
        currentIntent,
        targetDefinition,
        targetIntent,
        pendingRemoval,
        lastRealization,
        observedSettlementFailures,
        staleActivationOccurred,
        witnesses
        >>
    /\ CoordinatorUnchanged

ReviveDefinition(d) ==
    /\ BrowserFault = "ReviveDefinition"
    /\ selectedDefinition # NoDefinition
    /\ d \in retained \ {selectedDefinition}
    /\ selectedDefinition' = d
    /\ UNCHANGED <<
        retained,
        requestedDefinition,
        currentIntent,
        targetDefinition,
        targetIntent,
        pendingRemoval,
        lastRealization,
        observedSettlementFailures,
        staleActivationOccurred,
        witnesses
        >>
    /\ CoordinatorUnchanged

AdmitOperation(operation, r) ==
    /\ Coordinator!Admit(operation, r)
    /\ BrowserUnchanged

ReleaseOperation(operation) ==
    /\ Coordinator!Release(operation)
    /\ BrowserUnchanged

AdmitConstruction(operation, r) ==
    /\ Coordinator!AdmitConstruction(operation, r)
    /\ BrowserUnchanged

ReleaseConstruction(operation) ==
    /\ Coordinator!ReleaseConstruction(operation)
    /\ BrowserUnchanged

RequestClose(r) ==
    /\ Coordinator!RequestClose(r)
    /\ BrowserUnchanged

Settle(r, result) ==
    /\ Coordinator!Settle(r, result)
    /\ observedSettlementFailures' =
        IF result = "Failed"
            /\ BrowserFault # "ForgetSettlementFailure"
        THEN observedSettlementFailures \cup {r}
        ELSE observedSettlementFailures
    /\ UNCHANGED <<
        retained,
        selectedDefinition,
        requestedDefinition,
        currentIntent,
        targetDefinition,
        targetIntent,
        pendingRemoval,
        lastRealization,
        staleActivationOccurred,
        witnesses
        >>

SettleAny(r) ==
    \E result \in {"Succeeded", "Failed"} : Settle(r, result)

PublishStaleCoordinatorCandidate(r) ==
    /\ Coordinator!PublishStaleCandidate(r)
    /\ BrowserUnchanged

Next ==
    \/ \E d \in Definitions :
        PublishDefinition(d)
        \/ RequestActivation(d)
        \/ RequestWithoutCoordinatorSupersession(d)
        \/ RequestDeleteActive(d)
        \/ DeleteInactive(d)
        \/ ReviveDefinition(d)
    \/ \E d \in Definitions, replacement \in Realizations :
        RequestReplacementWhileCandidate(d, replacement)
        \/ RequestReplacementWhilePending(d, replacement)
    \/ \E r \in Realizations :
        BeginCandidate(r)
        \/ AdmitPendingCandidate(r)
        \/ CompleteCandidate(r)
        \/ ReadyCandidate(r)
        \/ CutOverCandidate(r)
        \/ FailCandidate(r)
        \/ RequestClose(r)
        \/ SettleAny(r)
        \/ PublishStaleCoordinatorCandidate(r)
    \/ \E operation \in Operations, r \in Realizations :
        AdmitOperation(operation, r)
    \/ \E operation \in Operations :
        ReleaseOperation(operation)
    \/ \E operation \in ConstructionOperations, r \in Realizations :
        AdmitConstruction(operation, r)
    \/ \E operation \in ConstructionOperations :
        ReleaseConstruction(operation)

Spec ==
    Init /\ [][Next]_vars

FairSpec ==
    /\ Spec
    /\ \A operation \in Operations :
        WF_vars(ReleaseOperation(operation))
    /\ \A operation \in ConstructionOperations :
        WF_vars(ReleaseConstruction(operation))
    /\ \A r \in Realizations :
        WF_vars(RequestClose(r)) /\ WF_vars(SettleAny(r))

TypeOK ==
    /\ retained \subseteq Definitions
    /\ selectedDefinition \in Definitions \cup {NoDefinition}
    /\ requestedDefinition \in Definitions \cup {NoDefinition}
    /\ currentIntent \in 0..MaxIntent
    /\ targetDefinition \in
        [Realizations -> (Definitions \cup {NoDefinition})]
    /\ targetIntent \in [Realizations -> 0..MaxIntent]
    /\ pendingRemoval \in Definitions \cup {NoDefinition}
    /\ lastRealization \in
        [Definitions -> (Realizations \cup {NoRealization})]
    /\ observedSettlementFailures \subseteq Realizations
    /\ staleActivationOccurred \in BOOLEAN
    /\ witnesses \subseteq {
        "FreshReactivation",
        "FailureWithIncumbent",
        "DeleteThroughReplacement"
        }

ActiveAssociationExact ==
    IF coordinatorActive = NoRealization
    THEN selectedDefinition = NoDefinition
    ELSE /\ selectedDefinition \in retained
         /\ targetDefinition[coordinatorActive] = selectedDefinition

CandidateAssociationExact ==
    /\ IF coordinatorCandidate = NoRealization
       THEN TRUE
       ELSE /\ targetDefinition[coordinatorCandidate] \in retained
            /\ targetIntent[coordinatorCandidate] > 0
            /\ coordinatorCandidate # coordinatorActive
    /\ IF coordinatorPendingCandidate = NoRealization
       THEN TRUE
       ELSE /\ targetDefinition[coordinatorPendingCandidate] \in retained
            /\ targetIntent[coordinatorPendingCandidate] > 0

FreshCandidateIdentity ==
    /\ IF coordinatorCandidate = NoRealization
       THEN TRUE
       ELSE lastRealization[targetDefinition[coordinatorCandidate]]
            # coordinatorCandidate
    /\ IF coordinatorPendingCandidate = NoRealization
       THEN TRUE
       ELSE lastRealization[targetDefinition[coordinatorPendingCandidate]]
            # coordinatorPendingCandidate

NoStaleActivation ==
    ~staleActivationOccurred

SettlementFailureVisible ==
    coordinatorFailures \subseteq observedSettlementFailures

PendingRemovalWaitsForReplacement ==
    pendingRemoval = NoDefinition
    \/ /\ pendingRemoval = selectedDefinition
       /\ pendingRemoval \in retained

CandidateBarrierDefinitionRetained ==
    IF coordinatorCandidateBarrier = NoRealization
    THEN TRUE
    ELSE IF coordinatorPhase[coordinatorCandidateBarrier]
                \in {"Settled", "Failed"}
         THEN TRUE
         ELSE targetDefinition[coordinatorCandidateBarrier] \in retained

CapacityBound ==
    Cardinality(ChargedRealizations) <= Capacity

CoordinatorBehaviorRefinesOwner ==
    Coordinator!Safety

Safety ==
    /\ TypeOK
    /\ CoordinatorBehaviorRefinesOwner
    /\ ActiveAssociationExact
    /\ CandidateAssociationExact
    /\ FreshCandidateIdentity
    /\ NoStaleActivation
    /\ SettlementFailureVisible
    /\ PendingRemovalWaitsForReplacement
    /\ CandidateBarrierDefinitionRetained
    /\ CapacityBound

DrainingTerminates ==
    Coordinator!DrainageTerminates

NeverFreshReactivation ==
    "FreshReactivation" \notin witnesses

NeverFailureWithIncumbent ==
    "FailureWithIncumbent" \notin witnesses

NeverDeleteThroughReplacement ==
    "DeleteThroughReplacement" \notin witnesses

=============================================================================
