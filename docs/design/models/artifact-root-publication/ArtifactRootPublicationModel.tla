--------------------- MODULE ArtifactRootPublicationModel ---------------------
(***************************************************************************)
(* Finite model-checking harness for ArtifactRootPublicationLifecycle.      *)
(* Scenario bounds and deliberate broken transitions live here so the      *)
(* reusable owner module remains free of model-checking switches.           *)
(***************************************************************************)
EXTENDS FiniteSets, Naturals, TLC

CONSTANTS
    workspace,
    foreignWorkspace,
    receipt,
    foreignReceipt,
    composition0,
    composition1,
    staleComposition,
    scope0,
    scope1,
    staleScope,
    oldRoot,
    retainedRoot,
    preparedRoot,
    BaseScenario,
    StaleCompositionScenario,
    StaleScopeScenario,
    ForeignWorkspaceScenario,
    ForeignReceiptScenario,
    IncompleteDesiredScenario,
    Scenario

CancellationAuthorityMismatchScenario == "CancellationAuthorityMismatch"
DeadlineMismatchScenario == "DeadlineMismatch"

Scenarios ==
    {BaseScenario, StaleCompositionScenario, StaleScopeScenario,
     ForeignWorkspaceScenario, ForeignReceiptScenario,
     IncompleteDesiredScenario, CancellationAuthorityMismatchScenario,
     DeadlineMismatchScenario}

ASSUME
    /\ Scenario \in Scenarios
    /\ workspace # foreignWorkspace
    /\ receipt # foreignReceipt
    /\ Cardinality({composition0, composition1, staleComposition}) = 3
    /\ Cardinality({scope0, scope1, staleScope}) = 3
    /\ Cardinality({oldRoot, retainedRoot, preparedRoot}) = 3

PlanWorkspaceMC ==
    IF Scenario = ForeignWorkspaceScenario
    THEN foreignWorkspace
    ELSE workspace

PlanReceiptMC ==
    IF Scenario = ForeignReceiptScenario
    THEN foreignReceipt
    ELSE receipt

ExpectedCompositionMC ==
    IF Scenario = StaleCompositionScenario
    THEN staleComposition
    ELSE composition0

ExpectedScopeBaseMC ==
    IF Scenario = StaleScopeScenario
    THEN staleScope
    ELSE scope0

InitialRootsMC == {oldRoot, retainedRoot}
CompleteDesiredRootsMC == {retainedRoot, preparedRoot}
PreparedRootsMC == {preparedRoot}

SubmittedDesiredRootsMC ==
    IF Scenario = IncompleteDesiredScenario
    THEN {retainedRoot}
    ELSE CompleteDesiredRootsMC

SubmittedPreparedRootsMC == PreparedRootsMC

PlanCancellationAuthorityMC == "CancellationAuthority"

ReceiptCancellationAuthorityMC ==
    IF Scenario = CancellationAuthorityMismatchScenario
    THEN "OtherCancellationAuthority"
    ELSE PlanCancellationAuthorityMC

PlanDeadlineMC == "Deadline"

ReceiptDeadlineMC ==
    IF Scenario = DeadlineMismatchScenario
    THEN "OtherDeadline"
    ELSE PlanDeadlineMC

VARIABLES
    runtimeState,
    currentComposition,
    currentRoots,
    currentScopeBase,
    receiptState,
    participantState,
    candidateState,
    phase,
    cancellationState,
    deadlineState,
    provisionalAuthority,
    stagingAuthority,
    result,
    compositionIssueCount,
    scopeIssueCount,
    receiptTerminalCount,
    receiptPublicationCount,
    participantUseCount,
    participantCommitCount,
    commitCount,
    validationAtCommit,
    events,
    closedBeforePublication

INSTANCE ArtifactRootPublicationLifecycle WITH
    Workspace <- workspace,
    PlanWorkspace <- PlanWorkspaceMC,
    Receipt <- receipt,
    PlanReceipt <- PlanReceiptMC,
    PlanCancellationAuthority <- PlanCancellationAuthorityMC,
    ReceiptCancellationAuthority <- ReceiptCancellationAuthorityMC,
    PlanDeadline <- PlanDeadlineMC,
    ReceiptDeadline <- ReceiptDeadlineMC,
    InitialComposition <- composition0,
    ExpectedComposition <- ExpectedCompositionMC,
    CandidateComposition <- composition1,
    CompositionIdentities <-
        {composition0, composition1, staleComposition},
    InitialScopeBase <- scope0,
    ExpectedScopeBase <- ExpectedScopeBaseMC,
    CandidateScopeBase <- scope1,
    ScopeBaseIdentities <- {scope0, scope1, staleScope},
    InitialRoots <- InitialRootsMC,
    CompleteDesiredRoots <- CompleteDesiredRootsMC,
    PreparedRoots <- PreparedRootsMC,
    SubmittedDesiredRoots <- SubmittedDesiredRootsMC,
    SubmittedPreparedRoots <- SubmittedPreparedRootsMC

BrokenCommitIgnoringValidation ==
    /\ phase = "Idle"
    /\ runtimeState = "Open"
    /\ receiptState = "Prepared"
    /\ currentComposition' = composition1
    /\ currentRoots' = SubmittedDesiredRootsMC
    /\ currentScopeBase' = scope1
    /\ receiptState' = "Published"
    /\ participantState' = "Committed"
    /\ candidateState' = "Published"
    /\ phase' = "Settled"
    /\ provisionalAuthority' = FALSE
    /\ stagingAuthority' = FALSE
    /\ result' = "Published"
    /\ compositionIssueCount' =
        [compositionIssueCount EXCEPT
            ![composition1] = @ + 1]
    /\ scopeIssueCount' =
        [scopeIssueCount EXCEPT ![scope1] = @ + 1]
    /\ receiptTerminalCount' = receiptTerminalCount + 1
    /\ receiptPublicationCount' = receiptPublicationCount + 1
    /\ participantUseCount' = participantUseCount + 1
    /\ participantCommitCount' = participantCommitCount + 1
    /\ commitCount' = commitCount + 1
    /\ validationAtCommit' = ValidationSnapshot
    /\ events' = events \cup {"Published"}
    /\ UNCHANGED <<
        runtimeState, cancellationState, deadlineState,
        closedBeforePublication
        >>

BrokenCommitAfterClose ==
    /\ phase = "Idle"
    /\ runtimeState = "Closed"
    /\ receiptState = "Prepared"
    /\ currentComposition' = composition1
    /\ currentRoots' = CompleteDesiredRootsMC
    /\ currentScopeBase' = scope1
    /\ receiptState' = "Published"
    /\ participantState' = "Committed"
    /\ candidateState' = "Published"
    /\ phase' = "Settled"
    /\ provisionalAuthority' = FALSE
    /\ stagingAuthority' = FALSE
    /\ result' = "Published"
    /\ compositionIssueCount' =
        [compositionIssueCount EXCEPT
            ![composition1] = @ + 1]
    /\ scopeIssueCount' =
        [scopeIssueCount EXCEPT ![scope1] = @ + 1]
    /\ receiptTerminalCount' = receiptTerminalCount + 1
    /\ receiptPublicationCount' = receiptPublicationCount + 1
    /\ participantUseCount' = participantUseCount + 1
    /\ participantCommitCount' = participantCommitCount + 1
    /\ commitCount' = commitCount + 1
    /\ validationAtCommit' = ValidationSnapshot
    /\ events' = events \cup {"Published"}
    /\ UNCHANGED <<
        runtimeState, cancellationState, deadlineState,
        closedBeforePublication
        >>

BrokenCancellationRetainsAuthority ==
    /\ receiptState = "Prepared"
    /\ cancellationState = "Cancelled"
    /\ receiptState' = "Released"
    /\ phase' = "Settled"
    /\ provisionalAuthority' = TRUE
    /\ stagingAuthority' = FALSE
    /\ result' = "Refused"
    /\ receiptTerminalCount' = receiptTerminalCount + 1
    /\ events' = events \cup {"CancelledRefused"}
    /\ UNCHANGED <<
        runtimeState, currentComposition, currentRoots, currentScopeBase,
        participantState, candidateState, cancellationState, deadlineState,
        compositionIssueCount, scopeIssueCount, receiptPublicationCount,
        participantUseCount, participantCommitCount, commitCount,
        validationAtCommit, closedBeforePublication
        >>

BrokenParticipantRefusalRetainsStaging ==
    /\ phase = "Staged"
    /\ PreParticipantChecksHold
    /\ ParticipantApplicable
    /\ receiptState' = "Released"
    /\ participantState' = "Refused"
    /\ candidateState' = "Discarded"
    /\ phase' = "Settled"
    /\ provisionalAuthority' = FALSE
    /\ stagingAuthority' = TRUE
    /\ result' = "Refused"
    /\ receiptTerminalCount' = receiptTerminalCount + 1
    /\ participantUseCount' = participantUseCount + 1
    /\ events' = events \cup {"ParticipantRefused"}
    /\ UNCHANGED <<
        runtimeState, currentComposition, currentRoots, currentScopeBase,
        cancellationState, deadlineState, compositionIssueCount,
        scopeIssueCount, receiptPublicationCount, participantCommitCount,
        commitCount, validationAtCommit, closedBeforePublication
        >>

BrokenReceiptReplayPublication ==
    /\ receiptState = "Published"
    /\ compositionIssueCount' =
        [compositionIssueCount EXCEPT ![composition1] = @ + 1]
    /\ scopeIssueCount' =
        [scopeIssueCount EXCEPT ![scope1] = @ + 1]
    /\ receiptPublicationCount' = receiptPublicationCount + 1
    /\ commitCount' = commitCount + 1
    /\ events' = events \cup {"ReceiptReplay"}
    /\ UNCHANGED <<
        runtimeState, currentComposition, currentRoots, currentScopeBase,
        receiptState, participantState, candidateState, phase,
        cancellationState, deadlineState, provisionalAuthority,
        stagingAuthority, result, receiptTerminalCount, participantUseCount,
        participantCommitCount, validationAtCommit, closedBeforePublication
        >>

BrokenParticipantReplayPublication ==
    /\ participantState = "Committed"
    /\ participantUseCount' = participantUseCount + 1
    /\ participantCommitCount' = participantCommitCount + 1
    /\ commitCount' = commitCount + 1
    /\ events' = events \cup {"ParticipantReplay"}
    /\ UNCHANGED <<
        runtimeState, currentComposition, currentRoots, currentScopeBase,
        receiptState, participantState, candidateState, phase,
        cancellationState, deadlineState, provisionalAuthority,
        stagingAuthority, result, compositionIssueCount, scopeIssueCount,
        receiptTerminalCount, receiptPublicationCount, validationAtCommit,
        closedBeforePublication
        >>

BrokenTornCommit ==
    /\ phase = "TokenReady"
    /\ FinalChecksHold
    /\ currentComposition' = composition1
    /\ currentRoots' = CompleteDesiredRootsMC
    /\ UNCHANGED currentScopeBase
    /\ receiptState' = "Published"
    /\ participantState' = "Committed"
    /\ candidateState' = "Published"
    /\ phase' = "Settled"
    /\ provisionalAuthority' = FALSE
    /\ stagingAuthority' = FALSE
    /\ result' = "Published"
    /\ receiptTerminalCount' = receiptTerminalCount + 1
    /\ receiptPublicationCount' = receiptPublicationCount + 1
    /\ participantCommitCount' = participantCommitCount + 1
    /\ commitCount' = commitCount + 1
    /\ validationAtCommit' = ValidationSnapshot
    /\ events' = events \cup {"Published"}
    /\ UNCHANGED <<
        runtimeState, cancellationState, deadlineState, compositionIssueCount,
        scopeIssueCount, participantUseCount, closedBeforePublication
        >>

BrokenValidationSpec ==
    Init /\ [][Next \/ BrokenCommitIgnoringValidation]_vars

BrokenAfterCloseSpec ==
    Init /\ [][Next \/ BrokenCommitAfterClose]_vars

BrokenCancellationAuthoritySpec ==
    Init /\ [][Next \/ BrokenCancellationRetainsAuthority]_vars

BrokenParticipantRefusalAuthoritySpec ==
    Init /\ [][Next \/ BrokenParticipantRefusalRetainsStaging]_vars

BrokenReceiptReplaySpec ==
    Init /\ [][Next \/ BrokenReceiptReplayPublication]_vars

BrokenParticipantReplaySpec ==
    Init /\ [][Next \/ BrokenParticipantReplayPublication]_vars

BrokenTornCommitSpec ==
    Init /\ [][Next \/ BrokenTornCommit]_vars

=============================================================================
