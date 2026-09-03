-------------------- MODULE ArtifactSessionGroupRelease --------------------
(***************************************************************************)
(* Models the shipped InspectionWorkspace handoff from one transferred     *)
(* artifact session to the complete exact set of dependent group-release   *)
(* receipts. The workspace requests release through each group's recorded  *)
(* owner. Artifact cleanup only observes physical terminal settlement; it   *)
(* never becomes another release authority.                                *)
(*                                                                         *)
(* Two exact dependent groups and one unrelated group bound the set-valued *)
(* join. Each group consumes one named AssemblyContextGroup release-owner   *)
(* instance. A faulted workspace-level request may settle without requesting *)
(* the underlying group owner. Artifact state then remains retained until an *)
(* explicit adjacent-owner recovery request reaches terminal settlement.    *)
(***************************************************************************)
EXTENDS FiniteSets, TLC

CONSTANTS
    DependentOne,
    DependentTwo,
    ForeignGroup,
    NoGroup,
    AllowOwnerCompletionBeforeQuiescence,
    AllowArtifactCleanupRequest,
    AllowTransferDuringAdmission

ReleaseResults == {"Succeeded", "Failed"}
NoReleaseResult == "None"
Groups == {DependentOne, DependentTwo, ForeignGroup}
ExactDependentGroups == {DependentOne, DependentTwo}

ASSUME
    /\ DependentOne # DependentTwo
    /\ ForeignGroup \notin ExactDependentGroups
    /\ NoGroup \notin Groups
    /\ ReleaseResults # {}
    /\ IsFiniteSet(ReleaseResults)
    /\ NoReleaseResult \notin ReleaseResults
    /\ AllowOwnerCompletionBeforeQuiescence \in BOOLEAN
    /\ AllowArtifactCleanupRequest \in BOOLEAN
    /\ AllowTransferDuringAdmission \in BOOLEAN

WorkspaceStates == {"Open", "ClosingGroups", "CleaningArtifacts", "Closed"}
GroupCloseStates ==
    {"NotStarted", "Pending", "Succeeded", "Failed", "Faulted"}
ArtifactStates == {"Retained", "Released"}
CleanupResults == {"None", "Succeeded", "Failed"}
CloseOutcomes == {"None", "Succeeded", "Faulted"}
RequestIssuers ==
    {"None", "WorkspaceClose", "AdjacentOwner", "ArtifactCleanup"}

VARIABLES
    workspaceState,
    admissionInFlight,
    unrelatedAdmitted,
    distinctTransferWitness,
    transferred,
    transferredFirst,
    transferredSecond,
    groupCloseStatus,
    groupQuiescent,
    requestIssuer,
    artifactState,
    artifactCleanupResult,
    reportedArtifactCleanupResult,
    closeOutcome,
    transferAdmissionWitness,
    faultRecoveryRequestObserved,
    alreadyTerminalTransferObserved,
    unrelatedAfterTransferObserved,
    mixedReceiptCleanupObserved,
    oneRequestedGroup,
    oneCompletedGroup,
    oneCompletionResult,
    twoRequestedGroup,
    twoCompletedGroup,
    twoCompletionResult,
    foreignRequestedGroup,
    foreignCompletedGroup,
    foreignCompletionResult

consumerVars == <<
    workspaceState, admissionInFlight, unrelatedAdmitted,
    distinctTransferWitness, transferred, transferredFirst, transferredSecond,
    groupCloseStatus, groupQuiescent, requestIssuer, artifactState,
    artifactCleanupResult, reportedArtifactCleanupResult, closeOutcome,
    transferAdmissionWitness, faultRecoveryRequestObserved,
    alreadyTerminalTransferObserved, unrelatedAfterTransferObserved,
    mixedReceiptCleanupObserved
    >>

consumerVarsExceptRequestIssuer == <<
    workspaceState, admissionInFlight, unrelatedAdmitted,
    distinctTransferWitness, transferred, transferredFirst, transferredSecond,
    groupCloseStatus, groupQuiescent, artifactState,
    artifactCleanupResult, reportedArtifactCleanupResult, closeOutcome,
    transferAdmissionWitness, faultRecoveryRequestObserved,
    alreadyTerminalTransferObserved,
    unrelatedAfterTransferObserved, mixedReceiptCleanupObserved
    >>

consumerVarsExceptRequestIssuerAndFaultRecovery == <<
    workspaceState, admissionInFlight, unrelatedAdmitted,
    distinctTransferWitness, transferred, transferredFirst, transferredSecond,
    groupCloseStatus, groupQuiescent, artifactState,
    artifactCleanupResult, reportedArtifactCleanupResult, closeOutcome,
    transferAdmissionWitness, alreadyTerminalTransferObserved,
    unrelatedAfterTransferObserved, mixedReceiptCleanupObserved
    >>

oneReleaseVars ==
    <<oneRequestedGroup, oneCompletedGroup, oneCompletionResult>>

twoReleaseVars ==
    <<twoRequestedGroup, twoCompletedGroup, twoCompletionResult>>

foreignReleaseVars ==
    <<foreignRequestedGroup, foreignCompletedGroup, foreignCompletionResult>>

ownerVars == <<oneReleaseVars, twoReleaseVars, foreignReleaseVars>>
vars == <<consumerVars, ownerVars>>

RegisteredGroups ==
    {transferredFirst, transferredSecond} \ {NoGroup}

CurrentWorkspaceGroups ==
    IF unrelatedAdmitted
    THEN ExactDependentGroups \cup {ForeignGroup}
    ELSE ExactDependentGroups

TerminalGroupCloseStatuses == ReleaseResults \cup {"Faulted"}

AllWorkspaceGroupClosesSettled ==
    \A g \in CurrentWorkspaceGroups :
        groupCloseStatus[g] \in TerminalGroupCloseStatuses

AllExactReceiptsTerminal ==
    /\ oneCompletedGroup = DependentOne
    /\ twoCompletedGroup = DependentTwo

AnyWorkspaceGroupCloseFaulted ==
    \E g \in CurrentWorkspaceGroups :
        groupCloseStatus[g] = "Faulted"

DependentOneRelease ==
    INSTANCE AssemblyContextGroupReleaseLifecycle
        WITH Group <- DependentOne,
             NoGroup <- NoGroup,
             ReleaseResults <- ReleaseResults,
             NoReleaseResult <- NoReleaseResult,
             requestedGroup <- oneRequestedGroup,
             completedGroup <- oneCompletedGroup,
             completionResult <- oneCompletionResult

DependentTwoRelease ==
    INSTANCE AssemblyContextGroupReleaseLifecycle
        WITH Group <- DependentTwo,
             NoGroup <- NoGroup,
             ReleaseResults <- ReleaseResults,
             NoReleaseResult <- NoReleaseResult,
             requestedGroup <- twoRequestedGroup,
             completedGroup <- twoCompletedGroup,
             completionResult <- twoCompletionResult

ForeignGroupRelease ==
    INSTANCE AssemblyContextGroupReleaseLifecycle
        WITH Group <- ForeignGroup,
             NoGroup <- NoGroup,
             ReleaseResults <- ReleaseResults,
             NoReleaseResult <- NoReleaseResult,
             requestedGroup <- foreignRequestedGroup,
             completedGroup <- foreignCompletedGroup,
             completionResult <- foreignCompletionResult

RequestedGroup(g) ==
    CASE g = DependentOne -> oneRequestedGroup
      [] g = DependentTwo -> twoRequestedGroup
      [] g = ForeignGroup -> foreignRequestedGroup

CompletedGroup(g) ==
    CASE g = DependentOne -> oneCompletedGroup
      [] g = DependentTwo -> twoCompletedGroup
      [] g = ForeignGroup -> foreignCompletedGroup

CompletionResult(g) ==
    CASE g = DependentOne -> oneCompletionResult
      [] g = DependentTwo -> twoCompletionResult
      [] g = ForeignGroup -> foreignCompletionResult

TypeOK ==
    /\ workspaceState \in WorkspaceStates
    /\ admissionInFlight \in BOOLEAN
    /\ unrelatedAdmitted \in BOOLEAN
    /\ distinctTransferWitness \in BOOLEAN
    /\ transferred \in BOOLEAN
    /\ transferredFirst \in Groups \cup {NoGroup}
    /\ transferredSecond \in Groups \cup {NoGroup}
    /\ groupCloseStatus \in [Groups -> GroupCloseStates]
    /\ groupQuiescent \in [Groups -> BOOLEAN]
    /\ requestIssuer \in [Groups -> RequestIssuers]
    /\ artifactState \in ArtifactStates
    /\ artifactCleanupResult \in CleanupResults
    /\ reportedArtifactCleanupResult \in CleanupResults
    /\ closeOutcome \in CloseOutcomes
    /\ transferAdmissionWitness \in BOOLEAN
    /\ faultRecoveryRequestObserved \in BOOLEAN
    /\ alreadyTerminalTransferObserved \in BOOLEAN
    /\ unrelatedAfterTransferObserved \in BOOLEAN
    /\ mixedReceiptCleanupObserved \in BOOLEAN
    /\ oneRequestedGroup \in {NoGroup, DependentOne}
    /\ oneCompletedGroup \in {NoGroup, DependentOne}
    /\ oneCompletionResult \in ReleaseResults \cup {NoReleaseResult}
    /\ twoRequestedGroup \in {NoGroup, DependentTwo}
    /\ twoCompletedGroup \in {NoGroup, DependentTwo}
    /\ twoCompletionResult \in ReleaseResults \cup {NoReleaseResult}
    /\ foreignRequestedGroup \in {NoGroup, ForeignGroup}
    /\ foreignCompletedGroup \in {NoGroup, ForeignGroup}
    /\ foreignCompletionResult \in ReleaseResults \cup {NoReleaseResult}

Init ==
    /\ workspaceState = "Open"
    /\ admissionInFlight = FALSE
    /\ unrelatedAdmitted = FALSE
    /\ distinctTransferWitness = TRUE
    /\ transferred = FALSE
    /\ transferredFirst = NoGroup
    /\ transferredSecond = NoGroup
    /\ groupCloseStatus = [g \in Groups |-> "NotStarted"]
    /\ groupQuiescent = [g \in Groups |-> FALSE]
    /\ requestIssuer = [g \in Groups |-> "None"]
    /\ artifactState = "Retained"
    /\ artifactCleanupResult = "None"
    /\ reportedArtifactCleanupResult = "None"
    /\ closeOutcome = "None"
    /\ transferAdmissionWitness = TRUE
    /\ faultRecoveryRequestObserved = FALSE
    /\ alreadyTerminalTransferObserved = FALSE
    /\ unrelatedAfterTransferObserved = FALSE
    /\ mixedReceiptCleanupObserved = FALSE
    /\ DependentOneRelease!Init
    /\ DependentTwoRelease!Init
    /\ ForeignGroupRelease!Init

StartAdmission ==
    /\ workspaceState = "Open"
    /\ ~admissionInFlight
    /\ ~unrelatedAdmitted
    /\ admissionInFlight' = TRUE
    /\ UNCHANGED <<
        workspaceState, unrelatedAdmitted, distinctTransferWitness, transferred,
        transferredFirst, transferredSecond, groupCloseStatus,
        groupQuiescent, requestIssuer, artifactState,
        artifactCleanupResult, reportedArtifactCleanupResult, closeOutcome,
        transferAdmissionWitness, faultRecoveryRequestObserved,
        alreadyTerminalTransferObserved, unrelatedAfterTransferObserved,
        mixedReceiptCleanupObserved, ownerVars
        >>

CompleteUnrelatedAdmission ==
    /\ workspaceState = "Open"
    /\ admissionInFlight
    /\ ~unrelatedAdmitted
    /\ admissionInFlight' = FALSE
    /\ unrelatedAdmitted' = TRUE
    /\ unrelatedAfterTransferObserved' =
        (unrelatedAfterTransferObserved \/ transferred)
    /\ UNCHANGED <<
        workspaceState, distinctTransferWitness, transferred, transferredFirst,
        transferredSecond, groupCloseStatus, groupQuiescent,
        requestIssuer, artifactState, artifactCleanupResult,
        reportedArtifactCleanupResult, closeOutcome,
        transferAdmissionWitness, faultRecoveryRequestObserved,
        alreadyTerminalTransferObserved, mixedReceiptCleanupObserved,
        ownerVars
        >>

TransferCore(first, second) ==
    /\ workspaceState = "Open"
    /\ ~transferred
    /\ first \in Groups
    /\ second \in Groups \cup {NoGroup}
    /\ transferred' = TRUE
    /\ transferredFirst' = first
    /\ transferredSecond' = second
    /\ transferAdmissionWitness' =
        (transferAdmissionWitness /\ ~admissionInFlight)
    /\ distinctTransferWitness' =
        (distinctTransferWitness /\ first # second)
    /\ alreadyTerminalTransferObserved' =
        (alreadyTerminalTransferObserved
         \/ oneCompletedGroup = DependentOne
         \/ twoCompletedGroup = DependentTwo)
    /\ UNCHANGED <<
        workspaceState, admissionInFlight, unrelatedAdmitted,
        groupCloseStatus, groupQuiescent,
        requestIssuer, artifactState, artifactCleanupResult,
        reportedArtifactCleanupResult, closeOutcome,
        faultRecoveryRequestObserved, unrelatedAfterTransferObserved,
        mixedReceiptCleanupObserved, ownerVars
        >>

TransferExactSession ==
    /\ \/ ~admissionInFlight
       \/ AllowTransferDuringAdmission
    /\ TransferCore(DependentOne, DependentTwo)

TransferForeignGroup ==
    TransferCore(DependentOne, ForeignGroup)

TransferIncompleteSet ==
    TransferCore(DependentOne, NoGroup)

TransferDuplicateGroup ==
    TransferCore(DependentOne, DependentOne)

RequestOwnerRelease(g) ==
    /\ g \in ExactDependentGroups
       \/ /\ g = ForeignGroup
          /\ unrelatedAdmitted
    /\ ~(g = DependentTwo
         /\ groupCloseStatus[DependentTwo] = "Faulted")
    /\ IF g = DependentOne
       THEN DependentOneRelease!RequestRelease
       ELSE UNCHANGED oneReleaseVars
    /\ IF g = DependentTwo
       THEN DependentTwoRelease!RequestRelease
       ELSE UNCHANGED twoReleaseVars
    /\ IF g = ForeignGroup
       THEN ForeignGroupRelease!RequestRelease
       ELSE UNCHANGED foreignReleaseVars
    /\ requestIssuer' = [requestIssuer EXCEPT ![g] = "AdjacentOwner"]
    /\ UNCHANGED consumerVarsExceptRequestIssuer

RecoverFaultedGroupRelease ==
    /\ workspaceState = "CleaningArtifacts"
    /\ groupCloseStatus[DependentTwo] = "Faulted"
    /\ twoRequestedGroup = NoGroup
    /\ DependentTwoRelease!RequestRelease
    /\ requestIssuer' =
        [requestIssuer EXCEPT ![DependentTwo] = "AdjacentOwner"]
    /\ faultRecoveryRequestObserved' = TRUE
    /\ UNCHANGED <<
        consumerVarsExceptRequestIssuerAndFaultRecovery,
        oneReleaseVars, foreignReleaseVars
        >>

GroupBecomesQuiescent(g) ==
    /\ g \in Groups
    /\ RequestedGroup(g) = g
    /\ ~groupQuiescent[g]
    /\ groupQuiescent' = [groupQuiescent EXCEPT ![g] = TRUE]
    /\ UNCHANGED <<
        workspaceState, admissionInFlight, unrelatedAdmitted,
        distinctTransferWitness, transferred, transferredFirst,
        transferredSecond, groupCloseStatus, requestIssuer,
        artifactState, artifactCleanupResult,
        reportedArtifactCleanupResult, closeOutcome,
        transferAdmissionWitness, faultRecoveryRequestObserved,
        alreadyTerminalTransferObserved, unrelatedAfterTransferObserved,
        mixedReceiptCleanupObserved, ownerVars
        >>

CompleteOwnerRelease(g, result) ==
    /\ g \in Groups
    /\ result \in ReleaseResults
    /\ IF g = DependentOne
       THEN DependentOneRelease!CompleteRelease(
            result,
            groupQuiescent[g] \/ AllowOwnerCompletionBeforeQuiescence)
       ELSE UNCHANGED oneReleaseVars
    /\ IF g = DependentTwo
       THEN DependentTwoRelease!CompleteRelease(
            result,
            groupQuiescent[g] \/ AllowOwnerCompletionBeforeQuiescence)
       ELSE UNCHANGED twoReleaseVars
    /\ IF g = ForeignGroup
       THEN ForeignGroupRelease!CompleteRelease(
            result,
            groupQuiescent[g] \/ AllowOwnerCompletionBeforeQuiescence)
       ELSE UNCHANGED foreignReleaseVars
    /\ UNCHANGED consumerVars

CompleteAnyOwnerRelease(g) ==
    \E result \in ReleaseResults :
        CompleteOwnerRelease(g, result)

BeginWorkspaceClose ==
    /\ workspaceState = "Open"
    /\ transferred
    /\ ~admissionInFlight
    /\ workspaceState' = "ClosingGroups"
    /\ UNCHANGED <<
        admissionInFlight, unrelatedAdmitted, distinctTransferWitness,
        transferred, transferredFirst, transferredSecond, groupCloseStatus,
        groupQuiescent, requestIssuer, artifactState,
        artifactCleanupResult, reportedArtifactCleanupResult, closeOutcome,
        transferAdmissionWitness, faultRecoveryRequestObserved,
        alreadyTerminalTransferObserved, unrelatedAfterTransferObserved,
        mixedReceiptCleanupObserved, ownerVars
        >>

StartWorkspaceGroupClose(g) ==
    /\ workspaceState = "ClosingGroups"
    /\ g \in CurrentWorkspaceGroups
    /\ groupCloseStatus[g] = "NotStarted"
    /\ groupCloseStatus' =
        [groupCloseStatus EXCEPT ![g] = "Pending"]
    /\ IF g = DependentOne
       THEN
        IF oneRequestedGroup = NoGroup
        THEN DependentOneRelease!RequestRelease
        ELSE /\ oneRequestedGroup = DependentOne
             /\ UNCHANGED oneReleaseVars
       ELSE UNCHANGED oneReleaseVars
    /\ IF g = DependentTwo
       THEN
        IF twoRequestedGroup = NoGroup
        THEN DependentTwoRelease!RequestRelease
        ELSE /\ twoRequestedGroup = DependentTwo
             /\ UNCHANGED twoReleaseVars
       ELSE UNCHANGED twoReleaseVars
    /\ IF g = ForeignGroup
       THEN
        IF foreignRequestedGroup = NoGroup
        THEN ForeignGroupRelease!RequestRelease
        ELSE /\ foreignRequestedGroup = ForeignGroup
             /\ UNCHANGED foreignReleaseVars
       ELSE UNCHANGED foreignReleaseVars
    /\ requestIssuer' =
        IF RequestedGroup(g) = NoGroup
        THEN [requestIssuer EXCEPT ![g] = "WorkspaceClose"]
        ELSE requestIssuer
    /\ UNCHANGED <<
        workspaceState, admissionInFlight, unrelatedAdmitted,
        distinctTransferWitness, transferred, transferredFirst,
        transferredSecond, groupQuiescent, artifactState,
        artifactCleanupResult, reportedArtifactCleanupResult, closeOutcome,
        transferAdmissionWitness, faultRecoveryRequestObserved,
        alreadyTerminalTransferObserved, unrelatedAfterTransferObserved,
        mixedReceiptCleanupObserved
        >>

FaultSecondWorkspaceGroupClose ==
    /\ workspaceState = "ClosingGroups"
    /\ groupCloseStatus[DependentTwo] = "NotStarted"
    /\ twoRequestedGroup = NoGroup
    /\ groupCloseStatus' =
        [groupCloseStatus EXCEPT ![DependentTwo] = "Faulted"]
    /\ UNCHANGED <<
        workspaceState, admissionInFlight, unrelatedAdmitted,
        distinctTransferWitness, transferred, transferredFirst,
        transferredSecond, groupQuiescent, requestIssuer, artifactState,
        artifactCleanupResult, reportedArtifactCleanupResult, closeOutcome,
        transferAdmissionWitness, faultRecoveryRequestObserved,
        alreadyTerminalTransferObserved, unrelatedAfterTransferObserved,
        mixedReceiptCleanupObserved, ownerVars
        >>

AdvanceWorkspaceGroupClose(g) ==
    \/ StartWorkspaceGroupClose(g)
    \/ /\ g = DependentTwo
       /\ FaultSecondWorkspaceGroupClose

ObserveWorkspaceGroupReceipt(g) ==
    /\ workspaceState = "ClosingGroups"
    /\ g \in CurrentWorkspaceGroups
    /\ groupCloseStatus[g] = "Pending"
    /\ CompletedGroup(g) = g
    /\ CompletionResult(g) \in ReleaseResults
    /\ groupCloseStatus' =
        [groupCloseStatus EXCEPT ![g] = CompletionResult(g)]
    /\ UNCHANGED <<
        workspaceState, admissionInFlight, unrelatedAdmitted,
        distinctTransferWitness, transferred, transferredFirst,
        transferredSecond, groupQuiescent, requestIssuer, artifactState,
        artifactCleanupResult, reportedArtifactCleanupResult, closeOutcome,
        transferAdmissionWitness, faultRecoveryRequestObserved,
        alreadyTerminalTransferObserved, unrelatedAfterTransferObserved,
        mixedReceiptCleanupObserved, ownerVars
        >>

BeginArtifactCleanup ==
    /\ workspaceState = "ClosingGroups"
    /\ AllWorkspaceGroupClosesSettled
    /\ workspaceState' = "CleaningArtifacts"
    /\ UNCHANGED <<
        admissionInFlight, unrelatedAdmitted, distinctTransferWitness,
        transferred, transferredFirst, transferredSecond, groupCloseStatus,
        groupQuiescent, requestIssuer, artifactState,
        artifactCleanupResult, reportedArtifactCleanupResult, closeOutcome,
        transferAdmissionWitness, faultRecoveryRequestObserved,
        alreadyTerminalTransferObserved, unrelatedAfterTransferObserved,
        mixedReceiptCleanupObserved, ownerVars
        >>

ArtifactCleanupRequestsRelease ==
    /\ AllowArtifactCleanupRequest
    /\ workspaceState = "CleaningArtifacts"
    /\ groupCloseStatus[DependentTwo] = "Faulted"
    /\ twoRequestedGroup = NoGroup
    /\ DependentTwoRelease!RequestRelease
    /\ requestIssuer' =
        [requestIssuer EXCEPT ![DependentTwo] = "ArtifactCleanup"]
    /\ UNCHANGED <<
        workspaceState, admissionInFlight, unrelatedAdmitted,
        distinctTransferWitness, transferred, transferredFirst,
        transferredSecond, groupCloseStatus, groupQuiescent, artifactState,
        artifactCleanupResult, reportedArtifactCleanupResult, closeOutcome,
        transferAdmissionWitness, faultRecoveryRequestObserved,
        alreadyTerminalTransferObserved,
        unrelatedAfterTransferObserved, mixedReceiptCleanupObserved,
        oneReleaseVars, foreignReleaseVars
        >>

ReleaseArtifactSessionCore(result) ==
    /\ result \in ReleaseResults
    /\ workspaceState = "CleaningArtifacts"
    /\ artifactState = "Retained"
    /\ artifactState' = "Released"
    /\ artifactCleanupResult' = result
    /\ mixedReceiptCleanupObserved' =
        (mixedReceiptCleanupObserved
         \/ /\ oneCompletionResult \in ReleaseResults
            /\ twoCompletionResult \in ReleaseResults
            /\ oneCompletionResult # twoCompletionResult)
    /\ UNCHANGED <<
        workspaceState, admissionInFlight, unrelatedAdmitted,
        distinctTransferWitness, transferred, transferredFirst,
        transferredSecond, groupCloseStatus, groupQuiescent,
        requestIssuer, reportedArtifactCleanupResult, closeOutcome,
        transferAdmissionWitness, faultRecoveryRequestObserved,
        alreadyTerminalTransferObserved, unrelatedAfterTransferObserved,
        ownerVars
        >>

ReleaseArtifactSession ==
    /\ AllExactReceiptsTerminal
    /\ \E result \in ReleaseResults :
        ReleaseArtifactSessionCore(result)

ReleaseAfterPartialReceipt ==
    /\ oneCompletedGroup = DependentOne
    /\ twoCompletedGroup = NoGroup
    /\ ReleaseArtifactSessionCore("Succeeded")

ReleaseWithForeignReceipt ==
    /\ oneCompletedGroup = DependentOne
    /\ twoCompletedGroup = NoGroup
    /\ foreignCompletedGroup = ForeignGroup
    /\ ReleaseArtifactSessionCore("Succeeded")

FinalizeWorkspaceCore(reportedResult, finalOutcome) ==
    /\ workspaceState = "CleaningArtifacts"
    /\ artifactState = "Released"
    /\ reportedResult \in CleanupResults
    /\ finalOutcome \in CloseOutcomes \ {"None"}
    /\ workspaceState' = "Closed"
    /\ reportedArtifactCleanupResult' = reportedResult
    /\ closeOutcome' = finalOutcome
    /\ UNCHANGED <<
        admissionInFlight, unrelatedAdmitted, distinctTransferWitness,
        transferred, transferredFirst, transferredSecond, groupCloseStatus,
        groupQuiescent, requestIssuer, artifactState,
        artifactCleanupResult, transferAdmissionWitness,
        faultRecoveryRequestObserved, alreadyTerminalTransferObserved,
        unrelatedAfterTransferObserved, mixedReceiptCleanupObserved,
        ownerVars
        >>

FinalizeWorkspace ==
    FinalizeWorkspaceCore(
        artifactCleanupResult,
        IF AnyWorkspaceGroupCloseFaulted
        THEN "Faulted"
        ELSE "Succeeded")

FinalizeWithoutCleanupFailure ==
    /\ artifactCleanupResult = "Failed"
    /\ FinalizeWorkspaceCore(
        "None",
        IF AnyWorkspaceGroupCloseFaulted
        THEN "Faulted"
        ELSE "Succeeded")

FinalizeWithoutGroupCloseFault ==
    /\ AnyWorkspaceGroupCloseFaulted
    /\ FinalizeWorkspaceCore(
        artifactCleanupResult,
        "Succeeded")

Next ==
    \/ StartAdmission
    \/ CompleteUnrelatedAdmission
    \/ TransferExactSession
    \/ \E g \in Groups : RequestOwnerRelease(g)
    \/ \E g \in Groups : GroupBecomesQuiescent(g)
    \/ \E g \in Groups : CompleteAnyOwnerRelease(g)
    \/ BeginWorkspaceClose
    \/ \E g \in Groups : AdvanceWorkspaceGroupClose(g)
    \/ \E g \in Groups : ObserveWorkspaceGroupReceipt(g)
    \/ BeginArtifactCleanup
    \/ RecoverFaultedGroupRelease
    \/ ArtifactCleanupRequestsRelease
    \/ ReleaseArtifactSession
    \/ FinalizeWorkspace

Fairness ==
    /\ WF_vars(CompleteUnrelatedAdmission)
    /\ \A g \in Groups : WF_vars(GroupBecomesQuiescent(g))
    /\ \A g \in Groups : WF_vars(CompleteAnyOwnerRelease(g))
    /\ \A g \in Groups : WF_vars(AdvanceWorkspaceGroupClose(g))
    /\ \A g \in Groups : WF_vars(ObserveWorkspaceGroupReceipt(g))
    /\ WF_vars(BeginArtifactCleanup)
    /\ WF_vars(ReleaseArtifactSession)
    /\ WF_vars(FinalizeWorkspace)

SafetySpec == Init /\ [][Next]_vars
Spec == SafetySpec /\ Fairness

ForeignTransferSpec ==
    Init /\ [][Next \/ TransferForeignGroup]_vars

IncompleteTransferSpec ==
    Init /\ [][Next \/ TransferIncompleteSet]_vars

DuplicateTransferSpec ==
    Init /\ [][Next \/ TransferDuplicateGroup]_vars

ForeignReceiptSpec ==
    Init /\ [][Next \/ ReleaseWithForeignReceipt]_vars

PartialReceiptSpec ==
    Init /\ [][Next \/ ReleaseAfterPartialReceipt]_vars

CleanupOmissionSpec ==
    Init /\ [][Next \/ FinalizeWithoutCleanupFailure]_vars

GroupCloseFaultOmissionSpec ==
    Init /\ [][Next \/ FinalizeWithoutGroupCloseFault]_vars

(***************************************************************************)
(* Safety properties.                                                      *)
(***************************************************************************)
TransferUsesCompleteExactSet ==
    transferred
        => RegisteredGroups = ExactDependentGroups

TransferUsesDistinctGroups ==
    distinctTransferWitness

TransferWaitsForCompletedAdmissions ==
    transferAdmissionWitness

ArtifactReleaseWaitsForExactReceipts ==
    artifactState = "Released" => AllExactReceiptsTerminal

ReleaseRequestsCarryOwnerAuthority ==
    \A g \in Groups :
        /\ (RequestedGroup(g) = NoGroup) = (requestIssuer[g] = "None")
        /\ requestIssuer[g] # "ArtifactCleanup"

ArtifactCleanupResultRemainsVisible ==
    workspaceState = "Closed"
        => reportedArtifactCleanupResult = artifactCleanupResult

GroupCloseFailureRemainsVisible ==
    workspaceState = "Closed"
        => (closeOutcome = "Faulted") = AnyWorkspaceGroupCloseFaulted

DependentOneCompletionMatchesRequest ==
    DependentOneRelease!CompletionMatchesRequest

DependentOneCompletionCarriesResult ==
    DependentOneRelease!CompletionCarriesResult

DependentTwoCompletionMatchesRequest ==
    DependentTwoRelease!CompletionMatchesRequest

DependentTwoCompletionCarriesResult ==
    DependentTwoRelease!CompletionCarriesResult

ForeignCompletionMatchesRequest ==
    ForeignGroupRelease!CompletionMatchesRequest

ForeignCompletionCarriesResult ==
    ForeignGroupRelease!CompletionCarriesResult

DependentOneBehaviorRefinesOwner ==
    DependentOneRelease!SafetySpec(groupQuiescent[DependentOne])

DependentTwoBehaviorRefinesOwner ==
    DependentTwoRelease!SafetySpec(groupQuiescent[DependentTwo])

ForeignBehaviorRefinesOwner ==
    ForeignGroupRelease!SafetySpec(groupQuiescent[ForeignGroup])

(***************************************************************************)
(* Liveness properties. Close is caller-initiated; once it starts, weak     *)
(* fairness reaches terminal close or the safe retained wait for an owner.  *)
(* After an owner request exists, settlement and cleanup remain live.       *)
(***************************************************************************)
ClosingWorkspaceEventuallySettles ==
    workspaceState = "ClosingGroups"
        ~> \/ workspaceState = "Closed"
           \/ /\ workspaceState = "CleaningArtifacts"
              /\ groupCloseStatus[DependentTwo] = "Faulted"
              /\ twoRequestedGroup = NoGroup
              /\ artifactState = "Retained"

RecoveredFaultedGroupCloseEventuallyCleansArtifacts ==
    (/\ groupCloseStatus[DependentTwo] = "Faulted"
     /\ twoRequestedGroup = DependentTwo)
        ~> artifactState = "Released"

(***************************************************************************)
(* Reachability probes. Their configurations negate these observations.    *)
(***************************************************************************)
NoOwnerRecoveryRequestObserved == ~faultRecoveryRequestObserved
NoFaultedSettlementWaitObserved ==
    ~(/\ workspaceState = "CleaningArtifacts"
      /\ groupCloseStatus[DependentTwo] = "Faulted"
      /\ twoRequestedGroup = NoGroup
      /\ artifactState = "Retained")
NoAlreadyTerminalTransferObserved == ~alreadyTerminalTransferObserved
NoUnrelatedAfterTransferObserved == ~unrelatedAfterTransferObserved
NoMixedReceiptCleanupObserved == ~mixedReceiptCleanupObserved

=============================================================================
