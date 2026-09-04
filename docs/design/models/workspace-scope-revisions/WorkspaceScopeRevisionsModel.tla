-------------------- MODULE WorkspaceScopeRevisionsModel --------------------
(***************************************************************************)
(* Finite Scope consumer harness. Artifact owns every physical publication *)
(* and every shared currency transition. Scope owns requests, ordered      *)
(* occurrences, snapshots, mutation admission, and complete results.       *)
(***************************************************************************)
EXTENDS Naturals, FiniteSets, Sequences, TLC
CONSTANTS Scenarios, Perturbations, Fault, Witness
VARIABLES runtime, physical, physicalRoots, base, physicalIssues, baseIssues,
    receipt1, participant1, candidate1, phase1, cancellation1, deadline1,
    provisional1, staging1, result1, terminal1, publication1, use1,
    participantCommit1, commit1, validation1, events1, closed1,
    receipt2, participant2, candidate2, phase2, cancellation2, deadline2,
    provisional2, staging2, result2, terminal2, publication2, use2,
    participantCommit2, commit2, validation2, events2, closed2,
    receipt3, participant3, candidate3, phase3, cancellation3, deadline3,
    provisional3, staging3, result3, terminal3, publication3, use3,
    participantCommit3, commit3, validation3, events3, closed3,
    physicalHistory, scopeHistory, scenario, secondKind, plans, sealed,
    active, admitted, outcomes, snapshots, snapshot, tokens, stopReason,
    superseder, progress, realization, refreshed, seen, validationOK, readOK
VARIABLE perturbation

Ops == 1..3
PhysicalIds == 0..4
BaseIds == (0..16) \union (21..23)
RootIds == {"a", "b", "c", "d"}
ScopeCandidate(i) ==
    IF i = 2 /\ scenario = "ScopeCandidate" THEN 21 ELSE 20 + i
PhysicalCandidate(i) ==
    IF i = 2 /\ scenario = "PhysicalCandidate" THEN 1 ELSE i
First == INSTANCE ArtifactRootPublicationLifecycle WITH
    Workspace <- "workspace", PlanWorkspace <- "workspace",
    Receipt <- 1, PlanReceipt <- 1,
    PlanCancellationAuthority <- 1, ReceiptCancellationAuthority <- 1,
    PlanDeadline <- 1, ReceiptDeadline <- 1,
    InitialComposition <- 0, ExpectedComposition <- plans[1].physical,
    CandidateComposition <- 1, CompositionIdentities <- PhysicalIds,
    InitialScopeBase <- 0, ExpectedScopeBase <- plans[1].base,
    CandidateScopeBase <- 21, ScopeBaseIdentities <- BaseIds,
    InitialRoots <- {}, CompleteDesiredRoots <- plans[1].desired,
    PreparedRoots <- plans[1].prepared,
    SubmittedDesiredRoots <- plans[1].desired,
    SubmittedPreparedRoots <- plans[1].prepared,
    runtimeState <- runtime, currentComposition <- physical,
    currentRoots <- physicalRoots, currentScopeBase <- base,
    compositionIssueCount <- physicalIssues, scopeIssueCount <- baseIssues,
    receiptState <- receipt1, participantState <- participant1,
    candidateState <- candidate1, phase <- phase1,
    cancellationState <- cancellation1, deadlineState <- deadline1,
    provisionalAuthority <- provisional1, stagingAuthority <- staging1,
    result <- result1, receiptTerminalCount <- terminal1,
    receiptPublicationCount <- publication1, participantUseCount <- use1,
    participantCommitCount <- participantCommit1, commitCount <- commit1,
    validationAtCommit <- validation1, events <- events1,
    closedBeforePublication <- closed1
Second == INSTANCE ArtifactRootPublicationLifecycle WITH
    Workspace <- "workspace",
    PlanWorkspace <- IF scenario = "ForeignWorkspace" THEN "foreign" ELSE "workspace",
    Receipt <- 2,
    PlanReceipt <- IF scenario = "ForeignReceipt" THEN 1 ELSE 2,
    PlanCancellationAuthority <- 2, ReceiptCancellationAuthority <- 2,
    PlanDeadline <- 2, ReceiptDeadline <- 2,
    InitialComposition <- 0, ExpectedComposition <- plans[2].physical,
    CandidateComposition <- PhysicalCandidate(2), CompositionIdentities <- PhysicalIds,
    InitialScopeBase <- 0, ExpectedScopeBase <- plans[2].base,
    CandidateScopeBase <- ScopeCandidate(2), ScopeBaseIdentities <- BaseIds,
    InitialRoots <- {}, CompleteDesiredRoots <- plans[2].desired,
    PreparedRoots <- plans[2].prepared,
    SubmittedDesiredRoots <- plans[2].desired,
    SubmittedPreparedRoots <- plans[2].prepared,
    runtimeState <- runtime, currentComposition <- physical,
    currentRoots <- physicalRoots, currentScopeBase <- base,
    compositionIssueCount <- physicalIssues, scopeIssueCount <- baseIssues,
    receiptState <- receipt2, participantState <- participant2,
    candidateState <- candidate2, phase <- phase2,
    cancellationState <- cancellation2, deadlineState <- deadline2,
    provisionalAuthority <- provisional2, stagingAuthority <- staging2,
    result <- result2, receiptTerminalCount <- terminal2,
    receiptPublicationCount <- publication2, participantUseCount <- use2,
    participantCommitCount <- participantCommit2, commitCount <- commit2,
    validationAtCommit <- validation2, events <- events2,
    closedBeforePublication <- closed2
Third == INSTANCE ArtifactRootPublicationLifecycle WITH
    Workspace <- "workspace", PlanWorkspace <- "workspace",
    Receipt <- 3, PlanReceipt <- 3,
    PlanCancellationAuthority <- 3, ReceiptCancellationAuthority <- 3,
    PlanDeadline <- 3, ReceiptDeadline <- 3,
    InitialComposition <- 0, ExpectedComposition <- plans[3].physical,
    CandidateComposition <- 3, CompositionIdentities <- PhysicalIds,
    InitialScopeBase <- 0, ExpectedScopeBase <- plans[3].base,
    CandidateScopeBase <- 23, ScopeBaseIdentities <- BaseIds,
    InitialRoots <- {}, CompleteDesiredRoots <- plans[3].desired,
    PreparedRoots <- plans[3].prepared,
    SubmittedDesiredRoots <- plans[3].desired,
    SubmittedPreparedRoots <- plans[3].prepared,
    runtimeState <- runtime, currentComposition <- physical,
    currentRoots <- physicalRoots, currentScopeBase <- base,
    compositionIssueCount <- physicalIssues, scopeIssueCount <- baseIssues,
    receiptState <- receipt3, participantState <- participant3,
    candidateState <- candidate3, phase <- phase3,
    cancellationState <- cancellation3, deadlineState <- deadline3,
    provisionalAuthority <- provisional3, stagingAuthority <- staging3,
    result <- result3, receiptTerminalCount <- terminal3,
    receiptPublicationCount <- publication3, participantUseCount <- use3,
    participantCommitCount <- participantCommit3, commitCount <- commit3,
    validationAtCommit <- validation3, events <- events3,
    closedBeforePublication <- closed3

local1 == <<receipt1, participant1, candidate1, phase1, cancellation1, deadline1,
    provisional1, staging1, result1, terminal1, publication1, use1,
    participantCommit1, commit1, validation1, events1, closed1>>
local2 == <<receipt2, participant2, candidate2, phase2, cancellation2, deadline2,
    provisional2, staging2, result2, terminal2, publication2, use2,
    participantCommit2, commit2, validation2, events2, closed2>>
local3 == <<receipt3, participant3, candidate3, phase3, cancellation3, deadline3,
    provisional3, staging3, result3, terminal3, publication3, use3,
    participantCommit3, commit3, validation3, events3, closed3>>
shared == <<runtime, physical, physicalRoots, base, physicalIssues, baseIssues>>
artifactVars == <<shared, local1, local2, local3>>
scopeVars == <<scenario, perturbation, secondKind, plans, sealed, active, admitted, outcomes,
    snapshots, snapshot, tokens, stopReason, superseder, progress,
    realization, refreshed, seen, validationOK, readOK>>
vars == <<artifactVars, physicalHistory, scopeHistory, scopeVars>>
phases == <<phase1, phase2, phase3>>
receipts == <<receipt1, receipt2, receipt3>>
artifactResults == <<result1, result2, result3>>
provisional == <<provisional1, provisional2, provisional3>>
staging == <<staging1, staging2, staging3>>
cancellations == <<cancellation1, cancellation2, cancellation3>>
deadlines == <<deadline1, deadline2, deadline3>>
ownerEvents == events1 \union events2 \union events3
GateFree == \A i \in Ops : phases[i] \notin {"Staged", "TokenReady"}
FreshBase == CHOOSE b \in 1..16 : baseIssues[b] = 0
SetOf(seq) == {seq[j] : j \in DOMAIN seq}
Correspondences(occ) == {occ[j].root : j \in DOMAIN occ}
Kind(i) ==
    CASE i = 1 -> "Replace"
      [] i = 2 -> IF scenario = "Refresh" THEN "Refresh" ELSE secondKind
      [] OTHER -> CASE scenario = "Refresh" -> "Closure"
                      [] scenario = "Readd" -> "Add"
                      [] OTHER -> "Refresh"
Terminal == {"Committed", "Failed", "Cancelled", "Superseded", "Unavailable"}

OwnerStep(i, firstAction, secondAction, thirdAction) ==
    CASE i = 1 -> firstAction /\ UNCHANGED <<local2, local3>>
      [] i = 2 -> secondAction /\ UNCHANGED <<local1, local3>>
      [] OTHER -> thirdAction /\ UNCHANGED <<local1, local2>>
Activate(i) == OwnerStep(i,
    First!ActivatePublication(plans[1].prepared # {}),
    Second!ActivatePublication(plans[2].prepared # {}),
    Third!ActivatePublication(plans[3].prepared # {}))
Stage(i) == OwnerStep(i, First!BeginStaging, Second!BeginStaging, Third!BeginStaging)
Prepare(i) == OwnerStep(i, First!PrepareCommit, Second!PrepareCommit, Third!PrepareCommit)
Publish(i) == OwnerStep(i, First!CommitPublication, Second!CommitPublication, Third!CommitPublication)
Release(i) == OwnerStep(i, First!ReleasePreparation, Second!ReleasePreparation, Third!ReleasePreparation)
Cancel(i) == OwnerStep(i, First!CancelPublication, Second!CancelPublication, Third!CancelPublication)
Expire(i) == OwnerStep(i, First!ExpireDeadline, Second!ExpireDeadline, Third!ExpireDeadline)
Refuse(i) == OwnerStep(i,
    First!ReleasePrepared \/ First!RefuseAtGate \/ First!RefuseStaged
        \/ First!RejectStaleScopeBase \/ First!RejectScopeCandidateIdentity
        \/ First!RefuseCommitToken,
    Second!ReleasePrepared \/ Second!RefuseAtGate \/ Second!RefuseStaged
        \/ Second!RejectStaleScopeBase \/ Second!RejectScopeCandidateIdentity
        \/ Second!RefuseCommitToken,
    Third!ReleasePrepared \/ Third!RefuseAtGate \/ Third!RefuseStaged
        \/ Third!RejectStaleScopeBase \/ Third!RejectScopeCandidateIdentity
        \/ Third!RefuseCommitToken)
ScopeAdvance ==
    /\ First!ScopeOnlyAdvance(FreshBase)
    /\ UNCHANGED <<local2, local3>>

NewOccurrence(i, root) == [id |-> <<"workspace", i, root>>, root |-> root]
Display(root) == IF root \in {"a", "d"} THEN "same-label" ELSE root
OccurrenceFor(i, root) ==
    LET matches == {o \in SetOf(snapshot.occurrences) : o.root = root} IN
    IF matches # {} THEN CHOOSE o \in matches : TRUE
    ELSE IF Fault = "Correspondence"
            /\ \E o \in SetOf(snapshot.occurrences) : Display(o.root) = Display(root)
         THEN LET old == CHOOSE o \in SetOf(snapshot.occurrences) :
                            Display(o.root) = Display(root)
              IN [id |-> old.id, root |-> root]
         ELSE NewOccurrence(i, root)
Requested(i) ==
    CASE Kind(i) = "Clear" -> <<>>
      [] Kind(i) \in {"Refresh", "Closure"} ->
            [j \in DOMAIN snapshot.occurrences |-> snapshot.occurrences[j].root]
      [] Kind(i) = "Remove" ->
            [j \in 1..(Len(snapshot.occurrences) - 1) |->
                snapshot.occurrences[j + 1].root]
      [] i = 1 -> <<"a", "b">>
      [] Kind(i) = "Add" ->
            [j \in DOMAIN snapshot.occurrences |-> snapshot.occurrences[j].root]
            \o (IF scenario = "Readd" THEN <<"a">> ELSE <<"c", "d">>)
      [] OTHER -> <<"b", "d">>
CandidateOccurrences(i) ==
    LET req == Requested(i) IN [j \in DOMAIN req |-> OccurrenceFor(i, req[j])]
MakeSnapshot(rev, occ, policy, epoch, scopeBase, generation, coverage, closure, preparing) ==
    [workspace |-> "workspace", revision |-> rev, occurrences |-> occ,
     policy |-> policy, epoch |-> epoch, base |-> scopeBase,
     projections |-> [j \in DOMAIN occ |->
        [occurrence |-> occ[j].id, root |-> occ[j].root,
         epoch |-> epoch, generation |-> generation, status |-> "Ready"]],
     coverage |-> coverage, closure |-> closure, preparing |-> preparing]
InitialSnapshot == MakeSnapshot(0, <<>>, FALSE, 0, 0, 0, {}, "Closed", 0)
PlanFor(i) ==
    LET occ == CandidateOccurrences(i)
        policy == IF Kind(i) = "Clear" THEN FALSE ELSE scenario = "Refresh"
    IN [physical |-> physical, base |-> base, revision |-> snapshot.revision,
        occurrences |-> occ, desired |-> Correspondences(occ),
        prepared |-> Correspondences(occ) \ physicalRoots,
        policy |-> policy, kind |-> Kind(i)]
EmptyPlan ==
    [physical |-> 0, base |-> 0, revision |-> 0, occurrences |-> <<>>,
     desired |-> {}, prepared |-> {}, policy |-> FALSE, kind |-> "None"]
Init ==
    /\ scenario \in Scenarios
    /\ perturbation \in Perturbations
    /\ secondKind \in {"Add", "Replace", "Clear", "Remove"}
    /\ (scenario = "Readd" => secondKind = "Remove")
    /\ plans = [i \in Ops |-> EmptyPlan]
    /\ First!DormantInit /\ Second!DormantInit /\ Third!DormantInit
    /\ physicalHistory = {0} /\ scopeHistory = {0}
    /\ sealed = {} /\ active = 0 /\ admitted = {}
    /\ outcomes = [i \in Ops |-> "None"]
    /\ snapshots = [i \in Ops |-> InitialSnapshot]
    /\ snapshot = InitialSnapshot
    /\ tokens = [i \in Ops |-> InitialSnapshot]
    /\ stopReason = [i \in Ops |-> "None"]
    /\ superseder = 0 /\ progress = {}
    /\ realization = 0 /\ refreshed = FALSE
    /\ seen = {} /\ validationOK = TRUE /\ readOK = TRUE

AdmissionEnabled(i) ==
    /\ runtime = "Open" /\ GateFree /\ active = 0
    /\ i \notin admitted
    /\ CASE i = 1 -> TRUE
       [] i = 2 ->
            /\ scenario # "DeadlineOnly"
            /\ outcomes[1] \in Terminal
            /\ (secondKind = "Remove" /\ Kind(i) = "Remove"
                => Len(snapshot.occurrences) > 0)
            /\ IF scenario = "Refresh"
               THEN outcomes[3] = "Committed" /\ refreshed
               ELSE TRUE
       [] OTHER ->
            IF scenario = "Refresh"
            THEN outcomes[1] = "Committed" /\ 2 \notin admitted
            ELSE IF scenario = "Readd" THEN outcomes[2] = "Committed"
                 ELSE scenario = "PhysicalRace" /\ outcomes[2] = "AwaitRefresh"
Admit(i) ==
    /\ AdmissionEnabled(i)
    /\ ScopeAdvance
    /\ plans' = [plans EXCEPT ![i] = PlanFor(i)]
    /\ active' = i /\ admitted' = admitted \union {i}
    /\ superseder' = IF i = 2 THEN 0 ELSE superseder
    /\ outcomes' = [outcomes EXCEPT ![i] = "Pending"]
    /\ snapshot' = [snapshot EXCEPT !.base = base', !.preparing = i]
    /\ seen' = seen \union {IF i = 1 THEN "Admitted" ELSE "Readmitted"}
    /\ UNCHANGED <<scenario, secondKind, sealed, snapshots, tokens, stopReason,
        progress, realization, refreshed, validationOK, readOK>>
PrepareBatch(i) ==
    /\ active = i /\ outcomes[i] = "Pending" /\ stopReason[i] = "None"
    /\ Activate(i)
    /\ UNCHANGED scopeVars
Seal(i) ==
    /\ active = i /\ receipts[i] = "Prepared" /\ i \notin sealed
    /\ stopReason[i] = "None" /\ superseder = 0
    /\ plans' = [plans EXCEPT ![i].base = base]
    /\ sealed' = sealed \union {i}
    /\ UNCHANGED <<artifactVars, scenario, secondKind, active, admitted, outcomes,
        snapshots, snapshot, tokens, stopReason, superseder, progress,
        realization, refreshed, seen, validationOK, readOK>>
Progress(i) ==
    /\ perturbation = "Progress"
    /\ active = i /\ i \notin sealed /\ i \notin progress
    /\ GateFree /\ stopReason[i] = "None"
    /\ ScopeAdvance
    /\ progress' = progress \union {i}
    /\ snapshot' = [snapshot EXCEPT !.base = base']
    /\ seen' = seen \union {"Progress"}
    /\ UNCHANGED <<scenario, secondKind, plans, sealed, active, admitted,
        outcomes, snapshots, tokens, stopReason, superseder, realization,
        refreshed, validationOK, readOK>>
StagePublication(i) ==
    /\ active = i /\ i \in sealed
    /\ stopReason[i] \in {"None", "Superseded"}
    /\ Stage(i)
    /\ UNCHANGED scopeVars
PrepareToken(i) ==
    /\ active = i /\ Prepare(i)
    /\ tokens' = [tokens EXCEPT ![i] =
        MakeSnapshot(
            IF plans[i].kind \in {"Refresh", "Closure"}
            THEN snapshot.revision ELSE i,
            plans[i].occurrences, plans[i].policy, PhysicalCandidate(i),
            ScopeCandidate(i), realization,
            IF plans[i].kind = "Closure"
            THEN {<<o.id, realization>> : o \in SetOf(plans[i].occurrences)}
            ELSE IF Fault = "Refresh" /\ plans[i].kind = "Refresh"
                 THEN snapshot.coverage ELSE {},
            IF plans[i].kind = "Closure" THEN "Complete"
            ELSE IF plans[i].policy THEN "NotEvaluated" ELSE "Closed", 0)]
    /\ UNCHANGED <<scenario, secondKind, plans, sealed, active, admitted,
        outcomes, snapshots, snapshot, stopReason, superseder, progress,
        realization, refreshed, seen, validationOK, readOK>>
Commit(i) ==
    /\ active = i /\ Publish(i)
    /\ snapshot' =
        IF Fault = "Partial" /\ Len(tokens[i].occurrences) > 1
        THEN [tokens[i] EXCEPT !.occurrences = <<@[1]>>]
        ELSE tokens[i]
    /\ outcomes' = [j \in Ops |->
        IF j = i THEN "Committed"
        ELSE IF outcomes[j] = "AwaitRefresh"
             THEN IF stopReason[j] # "None" THEN stopReason[j] ELSE "Failed"
             ELSE outcomes[j]]
    /\ snapshots' = [j \in Ops |->
        IF j = i \/ outcomes[j] = "AwaitRefresh" THEN snapshot' ELSE snapshots[j]]
    /\ active' = 0
    /\ seen' = seen \union {plans[i].kind \o "Committed"}
        \union (IF plans[i].kind = "Refresh" THEN {"RefreshedSnapshot"} ELSE {})
    /\ UNCHANGED <<scenario, secondKind, plans, sealed, admitted, tokens,
        stopReason, superseder, progress, realization, refreshed, validationOK, readOK>>

Supersede ==
    /\ perturbation = "Supersede"
    /\ runtime = "Open" /\ GateFree /\ active = 1 /\ superseder = 0
    /\ 2 \notin admitted /\ secondKind \in {"Replace", "Clear"}
    /\ scenario = "Edits" /\ stopReason[1] = "None"
    /\ ScopeAdvance
    /\ superseder' = 2
    /\ stopReason' = [stopReason EXCEPT ![1] = "Superseded"]
    /\ snapshot' = [snapshot EXCEPT !.base = base']
    /\ seen' = seen \union {secondKind \o "Supersedes"}
    /\ UNCHANGED <<scenario, secondKind, plans, sealed, active, admitted,
        outcomes, snapshots, tokens, progress, realization, refreshed, validationOK, readOK>>
Signal(i, reason) ==
    /\ i \in 1..2
    /\ Kind(i) \notin {"Refresh", "Closure"}
    /\ perturbation = reason
    /\ reason \notin seen
    /\ active = i /\ stopReason[i] = "None" /\ superseder = 0
    /\ IF reason = "Deadline" THEN Expire(i) ELSE Cancel(i)
    /\ stopReason' = [stopReason EXCEPT ![i] = "Cancelled"]
    /\ seen' = seen \union {reason}
    /\ UNCHANGED <<scenario, secondKind, plans, sealed, active, admitted,
        outcomes, snapshots, snapshot, tokens, superseder, progress,
        realization, refreshed, validationOK, readOK>>
Fail(i) ==
    /\ i \in 1..2
    /\ Kind(i) \notin {"Refresh", "Closure"}
    /\ perturbation = "Failure" /\ "Failure" \notin seen
    /\ active = i /\ GateFree /\ stopReason[i] = "None"
    /\ stopReason' = [stopReason EXCEPT ![i] = "Failed"]
    /\ seen' = seen \union {"Failure"}
    /\ UNCHANGED <<artifactVars, scenario, secondKind, plans, sealed, active,
        admitted, outcomes, snapshots, snapshot, tokens, superseder, progress,
        realization, refreshed, validationOK, readOK>>
RejectCompletion(i) ==
    /\ active = i
    /\ Refuse(i)
    /\ UNCHANGED scopeVars
RejectForeignCompletion ==
    /\ active = 2 /\ Second!RejectMalformed
    /\ UNCHANGED <<local1, local3>>
    /\ stopReason' = [stopReason EXCEPT ![2] = "Failed"]
    /\ UNCHANGED <<scenario, secondKind, plans, sealed, active, admitted,
        outcomes, snapshots, snapshot, tokens, superseder, progress,
        realization, refreshed, seen, validationOK, readOK>>
ReleaseStopped(i) ==
    /\ active = i /\ stopReason[i] # "None"
    /\ Release(i)
    /\ UNCHANGED scopeVars
Finish(i) ==
    /\ active = i /\ GateFree
    /\ \/ artifactResults[i] = "Refused"
       \/ phases[i] = "Dormant" /\ stopReason[i] # "None"
       \/ Fault = "Cleanup" /\ stopReason[i] = "Cancelled"
    /\ runtime = "Open" /\ ScopeAdvance
    /\ snapshot' = [snapshot EXCEPT !.base = base', !.preparing = 0]
    /\ outcomes' = [outcomes EXCEPT ![i] =
        IF snapshot.epoch # physical THEN "AwaitRefresh"
        ELSE IF stopReason[i] # "None" THEN stopReason[i] ELSE "Failed"]
    /\ snapshots' = [snapshots EXCEPT ![i] = snapshot']
    /\ active' = 0
    /\ seen' = seen \union {"Settled", outcomes'[i]}
    /\ UNCHANGED <<scenario, secondKind, plans, sealed, admitted, tokens,
        stopReason, superseder, progress, realization, refreshed, validationOK, readOK>>

PhysicalChange ==
    /\ scenario \in {"Refresh", "PhysicalRace"} /\ ~refreshed
    /\ runtime = "Open" /\ GateFree
    /\ IF scenario = "Refresh" THEN outcomes[3] = "Committed"
       ELSE active = 2 /\ 2 \in sealed /\ outcomes[1] = "Committed"
    /\ First!RefreshPhysical(4) /\ UNCHANGED <<local2, local3>>
    /\ realization' = 1 /\ refreshed' = TRUE
    /\ seen' = seen \union {"PhysicalChanged"}
    /\ UNCHANGED <<scenario, secondKind, plans, sealed, active, admitted,
        outcomes, snapshots, snapshot, tokens, stopReason, superseder, progress,
        validationOK, readOK>>
Observe ==
    /\ runtime = "Open" /\ GateFree
    /\ "ReadCurrent" \notin seen /\ snapshot.epoch = physical
    /\ readOK' = (snapshot.base = base
        /\ Correspondences(snapshot.occurrences) = physicalRoots)
    /\ seen' = seen \union {"ReadCurrent"}
    /\ UNCHANGED <<artifactVars, scenario, secondKind, plans, sealed,
        active, admitted, outcomes, snapshots, snapshot, tokens, stopReason,
        superseder, progress, realization, refreshed, validationOK>>
Submission(error) ==
    [shape |-> error # "InvalidReplace", deadline |-> error # "DeadlineExpired",
     workspace |-> IF error = "ForeignWorkspace" THEN "foreign" ELSE "workspace",
     revision |-> IF error = "RevisionMismatch" THEN snapshot.revision + 10 ELSE snapshot.revision,
     evidence |-> error # "EvidenceMismatch",
     kind |-> IF error = "InvalidReplace" THEN "Replace" ELSE "Add"]
Validate(request) ==
    CASE ~request.shape -> "InvalidReplace"
      [] ~request.deadline -> "DeadlineExpired"
      [] request.workspace # "workspace" -> "ForeignWorkspace"
      [] request.revision # snapshot.revision -> "RevisionMismatch"
      [] ~request.evidence -> "EvidenceMismatch"
      [] OTHER -> "Valid"
SubmissionDecision(request) ==
    IF Fault = "Validation" /\ active # 0 THEN "Busy"
    ELSE IF Fault = "Supersession" /\ request.kind = "Replace" THEN "Supersede"
    ELSE IF Validate(request) # "Valid" THEN Validate(request)
    ELSE IF active # 0 THEN "Busy" ELSE "Admit"
ProbeValidation(error) ==
    /\ perturbation = "Validation"
    /\ active # 0 /\ GateFree /\ error \notin seen
    /\ seen \intersect {"RevisionMismatch", "ForeignWorkspace", "InvalidReplace",
                       "DeadlineExpired", "Busy", "EvidenceMismatch"} = {}
    /\ error \in {"RevisionMismatch", "ForeignWorkspace", "InvalidReplace",
                  "DeadlineExpired", "Busy", "EvidenceMismatch"}
    /\ validationOK' = (SubmissionDecision(Submission(error)) = error)
    /\ seen' = seen \union {SubmissionDecision(Submission(error))}
    /\ IF SubmissionDecision(Submission(error)) = "Supersede"
       THEN /\ ScopeAdvance
            /\ snapshot' = [snapshot EXCEPT !.base = base']
            /\ stopReason' = [stopReason EXCEPT ![active] = "Superseded"]
       ELSE UNCHANGED <<artifactVars, snapshot, stopReason>>
    /\ UNCHANGED <<scenario, secondKind, plans, sealed, active, admitted,
        outcomes, snapshots, tokens, superseder, progress, realization, refreshed, readOK>>
Replay(i) ==
    /\ outcomes[i] = "Committed"
    /\ OwnerStep(i,
        First!AttemptReceiptReplay \/ First!AttemptParticipantReplay,
        Second!AttemptReceiptReplay \/ Second!AttemptParticipantReplay,
        Third!AttemptReceiptReplay \/ Third!AttemptParticipantReplay)
    /\ UNCHANGED scopeVars
LateCancel(i) ==
    /\ outcomes[i] = "Committed" /\ Cancel(i)
    /\ outcomes' = IF Fault = "FinalCommit"
        THEN [outcomes EXCEPT ![i] = "Cancelled"] ELSE outcomes
    /\ seen' = seen \union {"FinalCommitWins"}
    /\ UNCHANGED <<scenario, secondKind, plans, sealed, active, admitted,
        snapshots, snapshot, tokens, stopReason, superseder, progress,
        realization, refreshed, validationOK, readOK>>
Close ==
    /\ perturbation = "Close"
    /\ First!CloseRuntime /\ Second!CloseRuntime /\ Third!CloseRuntime
    /\ seen' = seen \union {"Closed"}
    /\ UNCHANGED <<scenario, secondKind, plans, sealed, active, admitted,
        outcomes, snapshots, snapshot, tokens, stopReason, superseder, progress,
        realization, refreshed, validationOK, readOK>>
FinishClosed(i) ==
    /\ runtime = "Closed" /\ active = i
    /\ receipts[i] = "Released"
    /\ active' = 0
    /\ outcomes' = [j \in Ops |->
        IF j = i \/ outcomes[j] = "AwaitRefresh" THEN "Unavailable" ELSE outcomes[j]]
    /\ snapshots' = [j \in Ops |->
        IF j = i \/ outcomes[j] = "AwaitRefresh" THEN snapshot ELSE snapshots[j]]
    /\ seen' = seen \union {"Unavailable"}
    /\ UNCHANGED <<artifactVars, scenario, secondKind, plans, sealed, admitted,
        snapshot, tokens, stopReason, superseder, progress, realization,
        refreshed, validationOK, readOK>>
FinishClosedRefresh ==
    /\ runtime = "Closed" /\ active = 0
    /\ \E i \in Ops : outcomes[i] = "AwaitRefresh"
    /\ outcomes' = [i \in Ops |->
        IF outcomes[i] = "AwaitRefresh" THEN "Unavailable" ELSE outcomes[i]]
    /\ snapshots' = [i \in Ops |->
        IF outcomes[i] = "AwaitRefresh" THEN snapshot ELSE snapshots[i]]
    /\ UNCHANGED <<artifactVars, scenario, secondKind, plans, sealed, active,
        admitted, snapshot, tokens, stopReason, superseder, progress,
        realization, refreshed, seen, validationOK, readOK>>
ClosedSubmission ==
    /\ runtime = "Closed" /\ "ClosedSubmission" \notin seen
    /\ seen' = seen \union {"ClosedSubmission"}
    /\ UNCHANGED <<artifactVars, scenario, secondKind, plans, sealed, active,
        admitted, outcomes, snapshots, snapshot, tokens, stopReason,
        superseder, progress, realization, refreshed, validationOK, readOK>>
RejectLateCompletion ==
    /\ outcomes[1] = "Superseded" /\ outcomes[2] = "Committed"
    /\ "LateSupersededCompletion" \notin seen
    /\ seen' = seen \union {"LateSupersededCompletion"}
    /\ UNCHANGED <<artifactVars, scenario, secondKind, plans, sealed, active,
        admitted, outcomes, snapshots, snapshot, tokens, stopReason,
        superseder, progress, realization, refreshed, validationOK, readOK>>
BrokenRebase ==
    /\ Fault = "ScopeBase" /\ active = 1 /\ superseder = 2
    /\ 1 \in sealed /\ receipts[1] = "Prepared"
    /\ plans[1].base # base
    /\ plans' = [plans EXCEPT ![1].base = base]
    /\ UNCHANGED <<artifactVars, scenario, secondKind, sealed, active, admitted,
        outcomes, snapshots, snapshot, tokens, stopReason, superseder, progress,
        realization, refreshed, seen, validationOK, readOK>>
BrokenGate ==
    /\ Fault = "Gate" /\ phase1 = "Staged"
    /\ ScopeAdvance
    /\ snapshot' = [snapshot EXCEPT !.base = base']
    /\ UNCHANGED <<scenario, secondKind, plans, sealed, active, admitted,
        outcomes, snapshots, tokens, stopReason, superseder, progress,
        realization, refreshed, seen, validationOK, readOK>>
Next ==
    /\ \/ \E i \in Ops :
            Admit(i) \/ PrepareBatch(i) \/ Seal(i) \/ Progress(i)
            \/ StagePublication(i) \/ PrepareToken(i) \/ Commit(i)
            \/ (\E reason \in {"Cancellation", "Deadline"} : Signal(i, reason))
            \/ Fail(i) \/ RejectCompletion(i) \/ ReleaseStopped(i) \/ Finish(i)
            \/ Replay(i) \/ LateCancel(i) \/ FinishClosed(i)
       \/ Supersede \/ PhysicalChange \/ Observe
       \/ \E error \in {"RevisionMismatch", "ForeignWorkspace", "InvalidReplace",
                       "DeadlineExpired", "Busy", "EvidenceMismatch"} :
            ProbeValidation(error)
       \/ RejectForeignCompletion \/ Close \/ ClosedSubmission
       \/ FinishClosedRefresh
       \/ RejectLateCompletion
       \/ BrokenRebase \/ BrokenGate
    /\ First!RecordPublicationHistory(physicalHistory, scopeHistory)
    /\ UNCHANGED perturbation
SafetySpec == Init /\ [][Next]_vars

OwnerAssumptionsHold ==
    First!OwnerAssumptions /\ Second!OwnerAssumptions /\ Third!OwnerAssumptions
OwnerSafety ==
    /\ First!CompositionTypeOK /\ Second!CompositionTypeOK /\ Third!CompositionTypeOK
    /\ First!CompositionIdentityNeverReused /\ First!ScopeBaseNeverReused
    /\ First!TerminalReceiptReleasesProvisionalAuthority
    /\ Second!TerminalReceiptReleasesProvisionalAuthority
    /\ Third!TerminalReceiptReleasesProvisionalAuthority
    /\ First!ActiveReceiptHasExactlyOneTerminalOutcome
    /\ Second!ActiveReceiptHasExactlyOneTerminalOutcome
    /\ Third!ActiveReceiptHasExactlyOneTerminalOutcome
    /\ First!ParticipantIsSingleUse /\ Second!ParticipantIsSingleUse /\ Third!ParticipantIsSingleUse
    /\ First!CommittedResultIsTerminal /\ Second!CommittedResultIsTerminal /\ Third!CommittedResultIsTerminal
    /\ First!CommittedScopeBaseWasCurrent /\ Second!CommittedScopeBaseWasCurrent /\ Third!CommittedScopeBaseWasCurrent
    /\ First!CommittedCompositionWasCurrent /\ Second!CommittedCompositionWasCurrent /\ Third!CommittedCompositionWasCurrent
    /\ First!CommittedWorkspaceWasExact /\ Second!CommittedWorkspaceWasExact /\ Third!CommittedWorkspaceWasExact
    /\ First!CommittedReceiptWasExact /\ Second!CommittedReceiptWasExact /\ Third!CommittedReceiptWasExact
    /\ First!CommittedCancellationAuthorityWasExact /\ Second!CommittedCancellationAuthorityWasExact /\ Third!CommittedCancellationAuthorityWasExact
    /\ First!CommittedDeadlineWasExact /\ Second!CommittedDeadlineWasExact /\ Third!CommittedDeadlineWasExact
    /\ First!CommittedDesiredSetWasComplete /\ Second!CommittedDesiredSetWasComplete /\ Third!CommittedDesiredSetWasComplete
    /\ First!CommittedBeforeCancellationOrExpiry /\ Second!CommittedBeforeCancellationOrExpiry /\ Third!CommittedBeforeCancellationOrExpiry
    /\ First!NoPublicationAfterRuntimeClose /\ Second!NoPublicationAfterRuntimeClose /\ Third!NoPublicationAfterRuntimeClose
ArtifactBehaviorRefinement ==
    /\ First!CompositionSafetySpec(physicalHistory, scopeHistory)
    /\ Second!CompositionSafetySpec(physicalHistory, scopeHistory)
    /\ Third!CompositionSafetySpec(physicalHistory, scopeHistory)
CompleteSnapshot ==
    /\ snapshot.workspace = "workspace"
    /\ DOMAIN snapshot.occurrences = DOMAIN snapshot.projections
    /\ \A j \in DOMAIN snapshot.occurrences :
        /\ snapshot.projections[j].occurrence = snapshot.occurrences[j].id
        /\ snapshot.projections[j].root = snapshot.occurrences[j].root
        /\ snapshot.projections[j].epoch = snapshot.epoch
    /\ (snapshot.epoch = physical =>
        /\ snapshot.base = base
        /\ Correspondences(snapshot.occurrences) = physicalRoots)
ExactOccurrenceRetention ==
    \A o \in SetOf(snapshot.occurrences) :
        o.id[1] = "workspace" /\ o.id[3] = o.root
NoPartialPublication ==
    \A i \in Ops : outcomes[i] = "Committed" =>
        /\ snapshots[i].occurrences = plans[i].occurrences
        /\ Correspondences(snapshots[i].occurrences) = plans[i].desired
OnePreparingMutation ==
    /\ active \in 0..3
    /\ {i \in Ops : outcomes[i] = "Pending"} =
        (IF active = 0 THEN {} ELSE {active})
    /\ (runtime = "Open" => snapshot.preparing = active)
SettledReleasesAuthority ==
    \A i \in Ops : outcomes[i] \in Terminal =>
        ~provisional[i] /\ ~staging[i]
SupersededCannotCommit ==
    \A i \in Ops : stopReason[i] = "Superseded" => outcomes[i] # "Committed"
FinalCommitWins ==
    \A i \in Ops : artifactResults[i] = "Published" => outcomes[i] = "Committed"
CurrentCoverageIsExact ==
    snapshot.epoch = physical =>
        \A c \in snapshot.coverage : c[2] = realization
ClosureRefreshRetainsOccurrences ==
    \A i \in Ops : outcomes[i] = "Committed" /\ plans[i].kind = "Refresh" =>
        /\ snapshots[i].revision = plans[i].revision
        /\ snapshots[i].occurrences = plans[i].occurrences
        /\ snapshots[i].coverage = {}
RefusalPreservesLogicalRevision ==
    \A i \in Ops : outcomes[i] \in {"Failed", "Cancelled", "Superseded"} =>
        snapshots[i].revision = plans[i].revision
ReaddedOccurrenceIsFresh ==
    scenario = "Readd" /\ outcomes[3] = "Committed" =>
        \A o \in SetOf(snapshots[3].occurrences) :
            o.root = "a" => o.id = <<"workspace", 3, "a">>
ValidationPrecedesAdmission == validationOK
NoAdmissionAfterClose == [][runtime = "Closed" => UNCHANGED admitted]_vars
SnapshotPointerSwapIsFresh ==
    [][snapshot' # snapshot =>
        /\ base' # base
        /\ base' \notin scopeHistory]_vars
ScopeSafety ==
    /\ CompleteSnapshot /\ ExactOccurrenceRetention /\ NoPartialPublication
    /\ OnePreparingMutation /\ SettledReleasesAuthority /\ SupersededCannotCommit
    /\ FinalCommitWins /\ CurrentCoverageIsExact /\ ClosureRefreshRetainsOccurrences
    /\ RefusalPreservesLogicalRevision
    /\ ReaddedOccurrenceIsFresh
    /\ validationOK /\ readOK
ObservedEvents ==
    seen \union ownerEvents
    \union (IF outcomes[2] = "Committed" /\ stopReason[1] = "Superseded"
            THEN {plans[2].kind \o "AfterSupersession"} ELSE {})
    \union (IF \E i \in Ops :
                outcomes[i] = "Failed" /\ artifactResults[i] = "Refused"
                /\ stopReason[i] = "Failed"
            THEN {"ReleasedFailure"} ELSE {})
    \union (IF \E i \in Ops :
                outcomes[i] = "Cancelled" /\ artifactResults[i] = "Refused"
            THEN {"ReleasedCancellation"} ELSE {})
    \union (IF \E events \in {events1, events2, events3} :
                {"ReceiptReplay", "ParticipantReplay", "PostCommitCancellation"}
                    \subseteq events
            THEN {"ReplayComplete"} ELSE {})
    \union (IF scenario = "Readd" /\ outcomes[3] = "Committed"
            THEN {"ReaddedFresh"} ELSE {})
NoWitness == Witness \notin ObservedEvents
Framed(action) ==
    /\ action
    /\ First!RecordPublicationHistory(physicalHistory, scopeHistory)
    /\ UNCHANGED perturbation
Fairness ==
    /\ \A i \in Ops :
        /\ WF_vars(Framed(PrepareBatch(i)))
        /\ WF_vars(Framed(Seal(i)))
        /\ WF_vars(Framed(StagePublication(i)))
        /\ WF_vars(Framed(PrepareToken(i)))
        /\ WF_vars(Framed(Commit(i)))
        /\ WF_vars(Framed(Signal(i, "Deadline")))
        /\ WF_vars(Framed(RejectCompletion(i)))
        /\ WF_vars(Framed(RejectForeignCompletion))
        /\ WF_vars(Framed(ReleaseStopped(i)))
        /\ WF_vars(Framed(Finish(i)))
        /\ WF_vars(Framed(FinishClosed(i)))
    /\ WF_vars(Framed(Admit(3)))
    /\ WF_vars(Framed(FinishClosedRefresh))
Spec == SafetySpec /\ Fairness
EveryAdmittedOperationSettles ==
    \A i \in Ops : i \in admitted ~> outcomes[i] \in Terminal
DeadlineSpec ==
    /\ SafetySpec
    /\ WF_vars(Framed(Signal(1, "Deadline")))
    /\ WF_vars(Framed(RejectCompletion(1)))
    /\ WF_vars(Framed(ReleaseStopped(1)))
    /\ WF_vars(Framed(Finish(1)))
=============================================================================
