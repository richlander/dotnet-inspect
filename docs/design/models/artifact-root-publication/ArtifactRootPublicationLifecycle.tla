------------------- MODULE ArtifactRootPublicationLifecycle -------------------
(***************************************************************************)
(* Reusable Artifact Acquisition owner boundary for publishing one complete *)
(* prepared physical Root batch with one sealed Scope participant. The      *)
(* module owns the runtime Workspace identity, physical-composition and     *)
(* opaque Scope publication currencies, receipt and participant one-shot    *)
(* state, cancellation/deadline races, the shared composition gate, and     *)
(* release of provisional authority. Logical membership and operation       *)
(* results remain opaque in CompleteDesiredRoots and the participant swap.  *)
(***************************************************************************)
EXTENDS FiniteSets, Naturals, TLC

CONSTANTS
    Workspace,
    Receipt,
    PlanCancellationAuthority,
    ReceiptCancellationAuthority,
    PlanDeadline,
    ReceiptDeadline,
    InitialComposition,
    CompositionIdentities,
    InitialScopeBase,
    ScopeBaseIdentities,
    InitialRoots

VARIABLES ExpectedComposition, ExpectedScopeBase, CompleteDesiredRoots,
          PreparedRoots, SubmittedDesiredRoots, SubmittedPreparedRoots,
          PlanWorkspace, PlanReceipt, CandidateComposition, CandidateScopeBase

OwnerAssumptions ==
    /\ IsFiniteSet(CompositionIdentities)
    /\ {InitialComposition, ExpectedComposition, CandidateComposition}
        \subseteq CompositionIdentities
    /\ IsFiniteSet(ScopeBaseIdentities)
    /\ {InitialScopeBase, ExpectedScopeBase, CandidateScopeBase}
        \subseteq ScopeBaseIdentities
    /\ IsFiniteSet(InitialRoots)
    /\ IsFiniteSet(CompleteDesiredRoots)
    /\ IsFiniteSet(PreparedRoots)
    /\ IsFiniteSet(SubmittedDesiredRoots)
    /\ IsFiniteSet(SubmittedPreparedRoots)
    /\ PreparedRoots \subseteq CompleteDesiredRoots

RuntimeStates == {"Open", "Closed"}
ReceiptStates == {"Prepared", "Publishing", "Published", "Released"}
ParticipantStates == {"Available", "Refused", "TokenReady", "Committed"}
CandidateStates == {"Unreserved", "Staged", "TokenReady", "Published", "Discarded"}
Phases == {"Idle", "Rejected", "Staged", "TokenReady", "Settled"}
CancellationStates == {"Active", "Cancelled"}
DeadlineStates == {"Live", "Expired"}
Results == {"None", "Rejected", "Refused", "Published"}

ValidationKeys ==
    {"Workspace", "Composition", "ScopeBase", "Receipt", "DesiredSet",
     "Participant", "CancellationAuthority", "DeadlineIdentity",
     "Cancellation", "Deadline", "Runtime"}

EventNames ==
    {"Staged", "TokenPrepared", "Published",
     "ForeignWorkspaceRejected", "ForeignReceiptRejected",
     "CancellationAuthorityMismatchRejected", "DeadlineMismatchRejected",
     "IncompleteDesiredSetRejected", "StaleCompositionRefused",
     "StaleScopeBaseRefused", "CancelledRefused", "ExpiredRefused",
     "RuntimeClosedRefused", "ParticipantRefused",
     "ParticipantConsumedRefused", "CandidateIdentityRefused",
     "ReceiptReplay", "ParticipantReplay", "PostCommitCancellation"}

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

vars ==
    <<runtimeState, currentComposition, currentRoots, currentScopeBase,
      receiptState, participantState, candidateState, phase,
      cancellationState, deadlineState, provisionalAuthority,
      stagingAuthority, result, compositionIssueCount, scopeIssueCount,
      receiptTerminalCount, receiptPublicationCount, participantUseCount,
      participantCommitCount, commitCount, validationAtCommit, events,
      closedBeforePublication>>

TerminalReceipt ==
    receiptState \in {"Published", "Released"}

TerminalParticipant ==
    participantState \in {"Refused", "Committed"}

ShapeValid ==
    /\ PlanWorkspace = Workspace
    /\ PlanReceipt = Receipt
    /\ PlanCancellationAuthority = ReceiptCancellationAuthority
    /\ PlanDeadline = ReceiptDeadline
    /\ SubmittedDesiredRoots = CompleteDesiredRoots
    /\ SubmittedPreparedRoots = PreparedRoots
    /\ SubmittedPreparedRoots \subseteq SubmittedDesiredRoots

CandidateCompositionIsFresh ==
    compositionIssueCount[CandidateComposition] = 0

CandidateScopeBaseIsFresh ==
    scopeIssueCount[CandidateScopeBase] = 0

GateApplicable ==
    /\ runtimeState = "Open"
    /\ cancellationState = "Active"
    /\ deadlineState = "Live"
    /\ receiptState = "Prepared"
    /\ currentComposition = ExpectedComposition
    /\ CandidateCompositionIsFresh

PreParticipantChecksHold ==
    /\ runtimeState = "Open"
    /\ cancellationState = "Active"
    /\ deadlineState = "Live"
    /\ receiptState = "Publishing"
    /\ currentComposition = ExpectedComposition

ParticipantApplicable ==
    /\ participantState = "Available"
    /\ currentScopeBase = ExpectedScopeBase
    /\ CandidateScopeBaseIsFresh

FinalChecksHold ==
    /\ runtimeState = "Open"
    /\ cancellationState = "Active"
    /\ deadlineState = "Live"
    /\ receiptState = "Publishing"
    /\ participantState = "TokenReady"
    /\ currentComposition = ExpectedComposition
    /\ currentScopeBase = ExpectedScopeBase

ValidationSnapshot ==
    [key \in ValidationKeys |->
        CASE key = "Workspace" ->
                PlanWorkspace = Workspace
          [] key = "Composition" ->
                currentComposition = ExpectedComposition
          [] key = "ScopeBase" ->
                currentScopeBase = ExpectedScopeBase
          [] key = "Receipt" ->
                PlanReceipt = Receipt
          [] key = "CancellationAuthority" ->
                PlanCancellationAuthority = ReceiptCancellationAuthority
          [] key = "DeadlineIdentity" ->
                PlanDeadline = ReceiptDeadline
          [] key = "DesiredSet" ->
                /\ SubmittedDesiredRoots = CompleteDesiredRoots
                /\ SubmittedPreparedRoots = PreparedRoots
                /\ SubmittedPreparedRoots \subseteq SubmittedDesiredRoots
          [] key = "Participant" ->
                participantState = "TokenReady"
          [] key = "Cancellation" ->
                cancellationState = "Active"
          [] key = "Deadline" ->
                deadlineState = "Live"
          [] OTHER ->
                runtimeState = "Open"]

MalformedEvents ==
    (IF PlanWorkspace # Workspace
     THEN {"ForeignWorkspaceRejected"}
     ELSE {})
    \union
    (IF PlanReceipt # Receipt
     THEN {"ForeignReceiptRejected"}
     ELSE {})
    \union
    (IF PlanCancellationAuthority # ReceiptCancellationAuthority
     THEN {"CancellationAuthorityMismatchRejected"}
     ELSE {})
    \union
    (IF PlanDeadline # ReceiptDeadline
     THEN {"DeadlineMismatchRejected"}
     ELSE {})
    \union
    (IF \/ SubmittedDesiredRoots # CompleteDesiredRoots
        \/ SubmittedPreparedRoots # PreparedRoots
        \/ ~(SubmittedPreparedRoots \subseteq SubmittedDesiredRoots)
     THEN {"IncompleteDesiredSetRejected"}
     ELSE {})

GateRefusalEvents ==
    (IF currentComposition # ExpectedComposition
     THEN {"StaleCompositionRefused"}
     ELSE {})
    \union
    (IF cancellationState = "Cancelled"
     THEN {"CancelledRefused"}
     ELSE {})
    \union
    (IF deadlineState = "Expired"
     THEN {"ExpiredRefused"}
     ELSE {})
    \union
    (IF runtimeState = "Closed"
     THEN {"RuntimeClosedRefused"}
     ELSE {})
    \union
    (IF ~CandidateCompositionIsFresh
     THEN {"CandidateIdentityRefused"}
     ELSE {})

StagedRefusalEvents ==
    (IF currentComposition # ExpectedComposition
     THEN {"StaleCompositionRefused"}
     ELSE {})
    \union
    (IF cancellationState = "Cancelled"
     THEN {"CancelledRefused"}
     ELSE {})
    \union
    (IF deadlineState = "Expired"
     THEN {"ExpiredRefused"}
     ELSE {})
    \union
    (IF runtimeState = "Closed"
     THEN {"RuntimeClosedRefused"}
     ELSE {})

Init ==
    /\ runtimeState = "Open"
    /\ currentComposition = InitialComposition
    /\ currentRoots = InitialRoots
    /\ currentScopeBase = InitialScopeBase
    /\ receiptState = "Prepared"
    /\ participantState = "Available"
    /\ candidateState = "Unreserved"
    /\ phase = "Idle"
    /\ cancellationState = "Active"
    /\ deadlineState = "Live"
    /\ provisionalAuthority = TRUE
    /\ stagingAuthority = FALSE
    /\ result = "None"
    /\ compositionIssueCount =
        [c \in CompositionIdentities |->
            IF c = InitialComposition THEN 1 ELSE 0]
    /\ scopeIssueCount =
        [s \in ScopeBaseIdentities |->
            IF s = InitialScopeBase THEN 1 ELSE 0]
    /\ receiptTerminalCount = 0
    /\ receiptPublicationCount = 0
    /\ participantUseCount = 0
    /\ participantCommitCount = 0
    /\ commitCount = 0
    /\ validationAtCommit = [key \in ValidationKeys |-> FALSE]
    /\ events = {}
    /\ closedBeforePublication = FALSE

CancelPublication ==
    /\ cancellationState = "Active"
    /\ cancellationState' = "Cancelled"
    /\ events' =
        IF receiptState = "Published"
        THEN events \cup {"PostCommitCancellation"}
        ELSE events
    /\ UNCHANGED <<
        runtimeState, currentComposition, currentRoots, currentScopeBase,
        receiptState, participantState, candidateState, phase, deadlineState,
        provisionalAuthority, stagingAuthority, result,
        compositionIssueCount, scopeIssueCount, receiptTerminalCount,
        receiptPublicationCount, participantUseCount, participantCommitCount,
        commitCount, validationAtCommit, closedBeforePublication
        >>

ExpireDeadline ==
    /\ deadlineState = "Live"
    /\ deadlineState' = "Expired"
    /\ UNCHANGED <<
        runtimeState, currentComposition, currentRoots, currentScopeBase,
        receiptState, participantState, candidateState, phase,
        cancellationState, provisionalAuthority, stagingAuthority, result,
        compositionIssueCount, scopeIssueCount, receiptTerminalCount,
        receiptPublicationCount, participantUseCount, participantCommitCount,
        commitCount, validationAtCommit, events, closedBeforePublication
        >>

CloseRuntime ==
    /\ runtimeState = "Open"
    /\ runtimeState' = "Closed"
    /\ closedBeforePublication' =
        (closedBeforePublication \/ receiptState # "Published")
    /\ UNCHANGED <<
        currentComposition, currentRoots, currentScopeBase, receiptState,
        participantState, candidateState, phase, cancellationState,
        deadlineState, provisionalAuthority, stagingAuthority, result,
        compositionIssueCount, scopeIssueCount, receiptTerminalCount,
        receiptPublicationCount, participantUseCount, participantCommitCount,
        commitCount, validationAtCommit, events
        >>

RejectMalformed ==
    /\ phase = "Idle"
    /\ receiptState = "Prepared"
    /\ ~ShapeValid
    /\ phase' = "Rejected"
    /\ result' = "Rejected"
    /\ events' = events \union MalformedEvents
    /\ UNCHANGED <<
        runtimeState, currentComposition, currentRoots, currentScopeBase,
        receiptState, participantState, candidateState, cancellationState,
        deadlineState, provisionalAuthority, stagingAuthority,
        compositionIssueCount, scopeIssueCount, receiptTerminalCount,
        receiptPublicationCount, participantUseCount, participantCommitCount,
        commitCount, validationAtCommit, closedBeforePublication
        >>

ReleasePrepared ==
    /\ receiptState = "Prepared"
    /\ \/ cancellationState = "Cancelled"
       \/ deadlineState = "Expired"
       \/ runtimeState = "Closed"
    /\ receiptState' = "Released"
    /\ phase' = "Settled"
    /\ provisionalAuthority' = FALSE
    /\ stagingAuthority' = FALSE
    /\ result' = "Refused"
    /\ receiptTerminalCount' = receiptTerminalCount + 1
    /\ events' = events \union GateRefusalEvents
    /\ UNCHANGED <<
        runtimeState, currentComposition, currentRoots, currentScopeBase,
        participantState, candidateState, cancellationState, deadlineState,
        compositionIssueCount, scopeIssueCount, receiptPublicationCount,
        participantUseCount, participantCommitCount, commitCount,
        validationAtCommit, closedBeforePublication
        >>

RefuseAtGate ==
    /\ phase = "Idle"
    /\ ShapeValid
    /\ ~GateApplicable
    /\ receiptState = "Prepared"
    /\ receiptState' = "Released"
    /\ phase' = "Settled"
    /\ provisionalAuthority' = FALSE
    /\ stagingAuthority' = FALSE
    /\ result' = "Refused"
    /\ receiptTerminalCount' = receiptTerminalCount + 1
    /\ events' = events \union GateRefusalEvents
    /\ UNCHANGED <<
        runtimeState, currentComposition, currentRoots, currentScopeBase,
        participantState, candidateState, cancellationState, deadlineState,
        compositionIssueCount, scopeIssueCount, receiptPublicationCount,
        participantUseCount, participantCommitCount, commitCount,
        validationAtCommit, closedBeforePublication
        >>

BeginStaging ==
    /\ phase = "Idle"
    /\ ShapeValid
    /\ GateApplicable
    /\ receiptState' = "Publishing"
    /\ candidateState' = "Staged"
    /\ phase' = "Staged"
    /\ stagingAuthority' = TRUE
    /\ compositionIssueCount' =
        [compositionIssueCount EXCEPT
            ![CandidateComposition] = @ + 1]
    /\ events' = events \cup {"Staged"}
    /\ UNCHANGED <<
        runtimeState, currentComposition, currentRoots, currentScopeBase,
        participantState, cancellationState, deadlineState,
        provisionalAuthority, result, scopeIssueCount, receiptTerminalCount,
        receiptPublicationCount, participantUseCount, participantCommitCount,
        commitCount, validationAtCommit, closedBeforePublication
        >>

RefuseStaged ==
    /\ phase = "Staged"
    /\ ~PreParticipantChecksHold
    /\ receiptState' = "Released"
    /\ candidateState' = "Discarded"
    /\ phase' = "Settled"
    /\ provisionalAuthority' = FALSE
    /\ stagingAuthority' = FALSE
    /\ result' = "Refused"
    /\ receiptTerminalCount' = receiptTerminalCount + 1
    /\ events' = events \union StagedRefusalEvents
    /\ UNCHANGED <<
        runtimeState, currentComposition, currentRoots, currentScopeBase,
        participantState, cancellationState, deadlineState,
        compositionIssueCount, scopeIssueCount, receiptPublicationCount,
        participantUseCount, participantCommitCount, commitCount,
        validationAtCommit, closedBeforePublication
        >>

RejectConsumedParticipant ==
    /\ phase = "Staged"
    /\ PreParticipantChecksHold
    /\ participantState # "Available"
    /\ receiptState' = "Released"
    /\ candidateState' = "Discarded"
    /\ phase' = "Settled"
    /\ provisionalAuthority' = FALSE
    /\ stagingAuthority' = FALSE
    /\ result' = "Refused"
    /\ receiptTerminalCount' = receiptTerminalCount + 1
    /\ events' = events \cup {"ParticipantConsumedRefused"}
    /\ UNCHANGED <<
        runtimeState, currentComposition, currentRoots, currentScopeBase,
        participantState, cancellationState, deadlineState,
        compositionIssueCount, scopeIssueCount, receiptPublicationCount,
        participantUseCount, participantCommitCount, commitCount,
        validationAtCommit, closedBeforePublication
        >>

RejectStaleScopeBase ==
    /\ phase = "Staged"
    /\ PreParticipantChecksHold
    /\ participantState = "Available"
    /\ currentScopeBase # ExpectedScopeBase
    /\ receiptState' = "Released"
    /\ participantState' = "Refused"
    /\ candidateState' = "Discarded"
    /\ phase' = "Settled"
    /\ provisionalAuthority' = FALSE
    /\ stagingAuthority' = FALSE
    /\ result' = "Refused"
    /\ receiptTerminalCount' = receiptTerminalCount + 1
    /\ participantUseCount' = participantUseCount + 1
    /\ events' = events \cup {"StaleScopeBaseRefused"}
    /\ UNCHANGED <<
        runtimeState, currentComposition, currentRoots, currentScopeBase,
        cancellationState, deadlineState, compositionIssueCount,
        scopeIssueCount, receiptPublicationCount, participantCommitCount,
        commitCount, validationAtCommit, closedBeforePublication
        >>

RejectScopeCandidateIdentity ==
    /\ phase = "Staged"
    /\ PreParticipantChecksHold
    /\ participantState = "Available"
    /\ currentScopeBase = ExpectedScopeBase
    /\ ~CandidateScopeBaseIsFresh
    /\ receiptState' = "Released"
    /\ participantState' = "Refused"
    /\ candidateState' = "Discarded"
    /\ phase' = "Settled"
    /\ provisionalAuthority' = FALSE
    /\ stagingAuthority' = FALSE
    /\ result' = "Refused"
    /\ receiptTerminalCount' = receiptTerminalCount + 1
    /\ participantUseCount' = participantUseCount + 1
    /\ events' = events \cup {"CandidateIdentityRefused"}
    /\ UNCHANGED <<
        runtimeState, currentComposition, currentRoots, currentScopeBase,
        cancellationState, deadlineState, compositionIssueCount,
        scopeIssueCount, receiptPublicationCount, participantCommitCount,
        commitCount, validationAtCommit, closedBeforePublication
        >>

ParticipantRefuses ==
    /\ phase = "Staged"
    /\ PreParticipantChecksHold
    /\ ParticipantApplicable
    /\ receiptState' = "Released"
    /\ participantState' = "Refused"
    /\ candidateState' = "Discarded"
    /\ phase' = "Settled"
    /\ provisionalAuthority' = FALSE
    /\ stagingAuthority' = FALSE
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

PrepareCommit ==
    /\ phase = "Staged"
    /\ PreParticipantChecksHold
    /\ ParticipantApplicable
    /\ participantState' = "TokenReady"
    /\ candidateState' = "TokenReady"
    /\ phase' = "TokenReady"
    /\ participantUseCount' = participantUseCount + 1
    /\ scopeIssueCount' =
        [scopeIssueCount EXCEPT ![CandidateScopeBase] = @ + 1]
    /\ events' = events \cup {"TokenPrepared"}
    /\ UNCHANGED <<
        runtimeState, currentComposition, currentRoots, currentScopeBase,
        receiptState, cancellationState, deadlineState,
        provisionalAuthority, stagingAuthority, result,
        compositionIssueCount, receiptTerminalCount, receiptPublicationCount,
        participantCommitCount, commitCount, validationAtCommit,
        closedBeforePublication
        >>

RefuseCommitToken ==
    /\ phase = "TokenReady"
    /\ ~FinalChecksHold
    /\ receiptState' = "Released"
    /\ participantState' = "Refused"
    /\ candidateState' = "Discarded"
    /\ phase' = "Settled"
    /\ provisionalAuthority' = FALSE
    /\ stagingAuthority' = FALSE
    /\ result' = "Refused"
    /\ receiptTerminalCount' = receiptTerminalCount + 1
    /\ events' = events \union StagedRefusalEvents
    /\ UNCHANGED <<
        runtimeState, currentComposition, currentRoots, currentScopeBase,
        cancellationState, deadlineState, compositionIssueCount,
        scopeIssueCount, receiptPublicationCount, participantUseCount,
        participantCommitCount, commitCount, validationAtCommit,
        closedBeforePublication
        >>

CommitPublication ==
    /\ phase = "TokenReady"
    /\ FinalChecksHold
    /\ currentComposition' = CandidateComposition
    /\ currentRoots' = SubmittedDesiredRoots
    /\ currentScopeBase' = CandidateScopeBase
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

AttemptReceiptReplay ==
    /\ receiptState = "Published"
    /\ "ReceiptReplay" \notin events
    /\ events' = events \cup {"ReceiptReplay"}
    /\ UNCHANGED <<
        runtimeState, currentComposition, currentRoots, currentScopeBase,
        receiptState, participantState, candidateState, phase,
        cancellationState, deadlineState, provisionalAuthority,
        stagingAuthority, result, compositionIssueCount, scopeIssueCount,
        receiptTerminalCount, receiptPublicationCount, participantUseCount,
        participantCommitCount, commitCount, validationAtCommit,
        closedBeforePublication
        >>

AttemptParticipantReplay ==
    /\ participantState = "Committed"
    /\ "ParticipantReplay" \notin events
    /\ events' = events \cup {"ParticipantReplay"}
    /\ UNCHANGED <<
        runtimeState, currentComposition, currentRoots, currentScopeBase,
        receiptState, participantState, candidateState, phase,
        cancellationState, deadlineState, provisionalAuthority,
        stagingAuthority, result, compositionIssueCount, scopeIssueCount,
        receiptTerminalCount, receiptPublicationCount, participantUseCount,
        participantCommitCount, commitCount, validationAtCommit,
        closedBeforePublication
        >>

Next ==
    \/ CancelPublication
    \/ ExpireDeadline
    \/ CloseRuntime
    \/ RejectMalformed
    \/ ReleasePrepared
    \/ RefuseAtGate
    \/ BeginStaging
    \/ RefuseStaged
    \/ RejectConsumedParticipant
    \/ RejectStaleScopeBase
    \/ RejectScopeCandidateIdentity
    \/ ParticipantRefuses
    \/ PrepareCommit
    \/ RefuseCommitToken
    \/ CommitPublication
    \/ AttemptReceiptReplay
    \/ AttemptParticipantReplay

SafetySpec ==
    Init /\ [][Next]_vars

TypeOK ==
    /\ runtimeState \in RuntimeStates
    /\ currentComposition \in CompositionIdentities
    /\ IsFiniteSet(currentRoots)
    /\ currentScopeBase \in ScopeBaseIdentities
    /\ receiptState \in ReceiptStates
    /\ participantState \in ParticipantStates
    /\ candidateState \in CandidateStates
    /\ phase \in Phases
    /\ cancellationState \in CancellationStates
    /\ deadlineState \in DeadlineStates
    /\ provisionalAuthority \in BOOLEAN
    /\ stagingAuthority \in BOOLEAN
    /\ result \in Results
    /\ compositionIssueCount \in [CompositionIdentities -> Nat]
    /\ scopeIssueCount \in [ScopeBaseIdentities -> Nat]
    /\ receiptTerminalCount \in Nat
    /\ receiptPublicationCount \in Nat
    /\ participantUseCount \in Nat
    /\ participantCommitCount \in Nat
    /\ commitCount \in Nat
    /\ validationAtCommit \in [ValidationKeys -> BOOLEAN]
    /\ events \subseteq EventNames
    /\ closedBeforePublication \in BOOLEAN

CompositionIdentityNeverReused ==
    \A c \in CompositionIdentities : compositionIssueCount[c] <= 1

ScopeBaseNeverReused ==
    \A s \in ScopeBaseIdentities : scopeIssueCount[s] <= 1

CurrentPointersArePaired ==
    \/ /\ currentComposition = InitialComposition
       /\ currentScopeBase = InitialScopeBase
    \/ /\ currentComposition = CandidateComposition
       /\ currentScopeBase = CandidateScopeBase

CurrentRootsMatchComposition ==
    /\ (currentComposition = InitialComposition => currentRoots = InitialRoots)
    /\ (currentComposition = CandidateComposition
        => currentRoots = CompleteDesiredRoots)

UnpublishedCandidateIsNotCurrent ==
    receiptState # "Published"
        => /\ currentComposition = InitialComposition
           /\ currentScopeBase = InitialScopeBase
           /\ currentRoots = InitialRoots

PublishedPairIsExact ==
    receiptState = "Published"
        => /\ currentComposition = CandidateComposition
           /\ currentScopeBase = CandidateScopeBase
           /\ currentRoots = CompleteDesiredRoots

TerminalReceiptReleasesProvisionalAuthority ==
    TerminalReceipt => /\ ~provisionalAuthority /\ ~stagingAuthority

ParticipantRefusalReleasesStaging ==
    participantState = "Refused"
        => /\ receiptState = "Released"
           /\ ~provisionalAuthority
           /\ ~stagingAuthority

ReceiptHasExactlyOneTerminalOutcome ==
    TerminalReceipt <=> receiptTerminalCount = 1

ReceiptPublishesAtMostOnce ==
    receiptPublicationCount <= 1

ParticipantIsSingleUse ==
    participantUseCount <= 1

ParticipantCommitsAtMostOnce ==
    participantCommitCount <= 1

PublicationCommitsAtMostOnce ==
    commitCount <= 1

PublishedReceiptMatchesCommit ==
    (receiptState = "Published")
        <=> /\ result = "Published"
            /\ receiptPublicationCount = 1
            /\ participantCommitCount = 1
            /\ commitCount = 1

RefusalPreservesBothPointers ==
    result \in {"Rejected", "Refused"}
        => /\ currentComposition = InitialComposition
           /\ currentScopeBase = InitialScopeBase
           /\ currentRoots = InitialRoots

MalformedRejectionPreservesCallerAuthority ==
    result = "Rejected"
        => /\ receiptState = "Prepared"
           /\ participantState = "Available"
           /\ provisionalAuthority
           /\ ~stagingAuthority

CommittedWorkspaceWasExact ==
    receiptState = "Published"
        => validationAtCommit["Workspace"]

CommittedCompositionWasCurrent ==
    receiptState = "Published"
        => validationAtCommit["Composition"]

CommittedScopeBaseWasCurrent ==
    receiptState = "Published"
        => validationAtCommit["ScopeBase"]

CommittedReceiptWasExact ==
    receiptState = "Published"
        => validationAtCommit["Receipt"]

CommittedCancellationAuthorityWasExact ==
    receiptState = "Published"
        => validationAtCommit["CancellationAuthority"]

CommittedDeadlineWasExact ==
    receiptState = "Published"
        => validationAtCommit["DeadlineIdentity"]

CommittedDesiredSetWasComplete ==
    receiptState = "Published"
        => validationAtCommit["DesiredSet"]

CommittedParticipantWasPrepared ==
    receiptState = "Published"
        => validationAtCommit["Participant"]

CommittedBeforeCancellationOrExpiry ==
    receiptState = "Published"
        => /\ validationAtCommit["Cancellation"]
           /\ validationAtCommit["Deadline"]

CommittedWhileRuntimeAcceptedWork ==
    receiptState = "Published"
        => validationAtCommit["Runtime"]

NoPublicationAfterRuntimeClose ==
    closedBeforePublication => receiptState # "Published"

FinalCommitWins ==
    receiptState = "Published"
        => /\ result = "Published"
           /\ currentComposition = CandidateComposition
           /\ currentScopeBase = CandidateScopeBase

ReplayDoesNotRepublish ==
    "ReceiptReplay" \in events \/ "ParticipantReplay" \in events
        => /\ receiptPublicationCount <= 1
           /\ participantCommitCount <= 1
           /\ commitCount <= 1

EveryPreparedReceiptEventuallySettles ==
    receiptState = "Prepared"
        ~> receiptState \in {"Published", "Released"}

EveryStartedPublicationEventuallySettles ==
    phase \in {"Staged", "TokenReady"}
        ~> phase = "Settled"

EveryPreparedTokenEventuallySettles ==
    participantState = "TokenReady"
        ~> TerminalParticipant

NoPublicationObserved ==
    "Published" \notin events

NoStaleCompositionRefusalObserved ==
    "StaleCompositionRefused" \notin events

NoStaleScopeBaseRefusalObserved ==
    "StaleScopeBaseRefused" \notin events

NoForeignWorkspaceRejectionObserved ==
    "ForeignWorkspaceRejected" \notin events

NoForeignReceiptRejectionObserved ==
    "ForeignReceiptRejected" \notin events

NoCancellationAuthorityMismatchRejectionObserved ==
    "CancellationAuthorityMismatchRejected" \notin events

NoDeadlineMismatchRejectionObserved ==
    "DeadlineMismatchRejected" \notin events

NoIncompleteDesiredSetRejectionObserved ==
    "IncompleteDesiredSetRejected" \notin events

NoCancelledRefusalObserved ==
    "CancelledRefused" \notin events

NoExpiredRefusalObserved ==
    "ExpiredRefused" \notin events

NoParticipantRefusalObserved ==
    "ParticipantRefused" \notin events

NoReceiptReplayObserved ==
    "ReceiptReplay" \notin events

NoParticipantReplayObserved ==
    "ParticipantReplay" \notin events

NoPostCommitCancellationObserved ==
    "PostCommitCancellation" \notin events

NoCommittedReplaySequenceObserved ==
    ~({"ReceiptReplay", "ParticipantReplay", "PostCommitCancellation"}
        \subseteq events)

NoRuntimeClosedRefusalObserved ==
    "RuntimeClosedRefused" \notin events

(***************************************************************************)
(* Open-world projection for a composition of publication operations.      *)
(* Init/Next/SafetySpec above remain the closed one-operation harness       *)
(* contract. In this projection pointers and issuance counts stay live,    *)
(* including after this receipt settles. Transaction-local terminal facts *)
(* do not assert that later operations leave the global pointers frozen.  *)
(* The two history arguments are shared owner-issued publication sets;    *)
(* they distinguish reserved identities from identities already current.  *)
(***************************************************************************)
LocalState ==
    <<receiptState, participantState, candidateState, phase,
      cancellationState, deadlineState, provisionalAuthority,
      stagingAuthority, result, receiptTerminalCount, receiptPublicationCount,
      participantUseCount, participantCommitCount, commitCount,
      validationAtCommit, events, closedBeforePublication>>

SharedState ==
    <<runtimeState, currentComposition, currentRoots, currentScopeBase,
      compositionIssueCount, scopeIssueCount>>

DormantInit ==
    /\ runtimeState = "Open"
    /\ currentComposition = InitialComposition
    /\ currentRoots = InitialRoots
    /\ currentScopeBase = InitialScopeBase
    /\ receiptState = "Released"
    /\ participantState = "Available"
    /\ candidateState = "Unreserved"
    /\ phase = "Dormant"
    /\ cancellationState = "Active"
    /\ deadlineState = "Live"
    /\ provisionalAuthority = FALSE
    /\ stagingAuthority = FALSE
    /\ result = "None"
    /\ compositionIssueCount =
        [c \in CompositionIdentities |->
            IF c = InitialComposition THEN 1 ELSE 0]
    /\ scopeIssueCount =
        [s \in ScopeBaseIdentities |->
            IF s = InitialScopeBase THEN 1 ELSE 0]
    /\ receiptTerminalCount = 0
    /\ receiptPublicationCount = 0
    /\ participantUseCount = 0
    /\ participantCommitCount = 0
    /\ commitCount = 0
    /\ validationAtCommit = [key \in ValidationKeys |-> FALSE]
    /\ events = {}
    /\ closedBeforePublication = FALSE

ActivatePublication(hasPreparation) ==
    /\ phase = "Dormant"
    /\ runtimeState = "Open"
    /\ receiptState' = "Prepared"
    /\ phase' = "Idle"
    /\ provisionalAuthority' = hasPreparation
    /\ UNCHANGED <<
        SharedState, participantState, candidateState, cancellationState,
        deadlineState, stagingAuthority, result, receiptTerminalCount,
        receiptPublicationCount, participantUseCount, participantCommitCount,
        commitCount, validationAtCommit, events, closedBeforePublication
        >>

ActivatePreparation == ActivatePublication(TRUE)

ReleasePreparation ==
    /\ phase \in {"Idle", "Rejected"}
    /\ receiptState = "Prepared"
    /\ receiptState' = "Released"
    /\ phase' = "Settled"
    /\ provisionalAuthority' = FALSE
    /\ stagingAuthority' = FALSE
    /\ result' = "Refused"
    /\ receiptTerminalCount' = receiptTerminalCount + 1
    /\ UNCHANGED <<
        SharedState, participantState, candidateState, cancellationState,
        deadlineState, receiptPublicationCount, participantUseCount,
        participantCommitCount, commitCount, validationAtCommit, events,
        closedBeforePublication
        >>

ScopeOnlyAdvance(nextBase) ==
    /\ runtimeState = "Open"
    /\ nextBase \in ScopeBaseIdentities
    /\ scopeIssueCount[nextBase] = 0
    /\ currentScopeBase' = nextBase
    /\ scopeIssueCount' =
        [scopeIssueCount EXCEPT ![nextBase] = @ + 1]
    /\ UNCHANGED <<
        runtimeState, currentComposition, currentRoots, compositionIssueCount,
        LocalState
        >>

RefreshPhysical(nextComposition) ==
    /\ runtimeState = "Open"
    /\ nextComposition \in CompositionIdentities
    /\ compositionIssueCount[nextComposition] = 0
    /\ currentComposition' = nextComposition
    /\ compositionIssueCount' =
        [compositionIssueCount EXCEPT ![nextComposition] = @ + 1]
    /\ UNCHANGED <<
        runtimeState, currentScopeBase, currentRoots, scopeIssueCount, LocalState
        >>

ObserveReservation ==
    /\ runtimeState = "Open"
    /\ UNCHANGED <<runtimeState, currentComposition, currentScopeBase,
                   currentRoots, LocalState>>
    /\ \/ /\ \E c \in CompositionIdentities :
                /\ compositionIssueCount[c] = 0
                /\ compositionIssueCount' =
                    [compositionIssueCount EXCEPT ![c] = 1]
          /\ UNCHANGED scopeIssueCount
       \/ /\ \E s \in ScopeBaseIdentities :
                /\ scopeIssueCount[s] = 0
                /\ scopeIssueCount' = [scopeIssueCount EXCEPT ![s] = 1]
          /\ UNCHANGED compositionIssueCount

ObserveOtherCommit(physicalHistory, scopeHistory) ==
    /\ runtimeState = "Open"
    /\ currentComposition' \in CompositionIdentities \ physicalHistory
    /\ currentScopeBase' \in ScopeBaseIdentities \ scopeHistory
    /\ compositionIssueCount[currentComposition'] = 1
    /\ scopeIssueCount[currentScopeBase'] = 1
    /\ IsFiniteSet(currentRoots')
    /\ UNCHANGED <<runtimeState, compositionIssueCount, scopeIssueCount,
                   LocalState>>

CompositionNext(physicalHistory, scopeHistory) ==
    \/ \E hasPreparation \in BOOLEAN : ActivatePublication(hasPreparation)
    \/ ReleasePreparation
    \/ CancelPublication
    \/ ExpireDeadline
    \/ /\ phase # "Dormant"
       /\ Next
    \/ CloseRuntime
    \/ /\ phase \in {"Dormant", "Idle", "Rejected", "Settled"}
       /\ \/ \E s \in ScopeBaseIdentities : ScopeOnlyAdvance(s)
          \/ \E c \in CompositionIdentities : RefreshPhysical(c)
          \/ ObserveReservation
          \/ ObserveOtherCommit(physicalHistory, scopeHistory)

RecordPublicationHistory(physicalHistory, scopeHistory) ==
    /\ (currentComposition' # currentComposition
        => currentComposition' \notin physicalHistory)
    /\ (currentScopeBase' # currentScopeBase
        => currentScopeBase' \notin scopeHistory)
    /\ physicalHistory' = physicalHistory \union {currentComposition'}
    /\ scopeHistory' = scopeHistory \union {currentScopeBase'}

CompositionSafetySpec(physicalHistory, scopeHistory) ==
    /\ DormantInit
    /\ physicalHistory = {InitialComposition}
    /\ scopeHistory = {InitialScopeBase}
    /\ [][CompositionNext(physicalHistory, scopeHistory)
          /\ RecordPublicationHistory(physicalHistory, scopeHistory)
         ]_<<vars, physicalHistory, scopeHistory>>

CompositionTypeOK ==
    IF phase = "Dormant"
    THEN /\ receiptState = "Released"
         /\ ~provisionalAuthority
         /\ ~stagingAuthority
         /\ receiptTerminalCount = 0
         /\ commitCount = 0
    ELSE TypeOK

ActiveReceiptHasExactlyOneTerminalOutcome ==
    phase # "Dormant" => ReceiptHasExactlyOneTerminalOutcome

CommittedResultIsTerminal ==
    receiptState = "Published"
        => /\ result = "Published"
           /\ participantState = "Committed"
           /\ receiptPublicationCount = 1
           /\ participantCommitCount = 1
           /\ commitCount = 1

=============================================================================
