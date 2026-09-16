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
    superseder, progress, realization, refreshed, seen, validationOK, readOK,
    requests, requestStates, requestDeadlineValid, submissionCounts,
    settlementCounts, results, cancellationResponses
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
handoffVars == <<requests, requestStates, requestDeadlineValid, submissionCounts,
    settlementCounts, results, cancellationResponses>>
scopeVars == <<scenario, perturbation, secondKind, plans, sealed, active, admitted, outcomes,
    snapshots, snapshot, tokens, stopReason, superseder, progress,
    realization, refreshed, seen, validationOK, readOK, handoffVars>>
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
Terminal ==
    {"Committed", "NoEffect", "Rejected", "Failed", "Cancelled",
     "Superseded", "Unavailable"}
Successful == {"Committed", "NoEffect"}

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
InputBatch(i) ==
    CASE Kind(i) \in {"Clear", "Remove", "Refresh", "Closure"} -> <<>>
      [] i = 1 -> <<"a", "b">>
      [] Kind(i) = "Replace" -> <<"b", "d">>
      [] scenario \in {"HandoffDuplicate", "HandoffDuplicateNoIntent"} -> <<"a">>
      [] scenario = "HandoffMixed" -> <<"a", "d">>
      [] scenario = "HandoffInvalidTarget" -> <<"d">>
      [] scenario = "Readd" -> <<"a">>
      [] OTHER -> <<"c", "d">>
TargetOptions(i) ==
    IF Kind(i) \notin {"Add", "Replace"} THEN {"None"}
    ELSE IF scenario = "HandoffReplace" /\ i = 1 THEN {"b"}
    ELSE IF scenario = "HandoffDuplicate" /\ i = 2 THEN {"a"}
    ELSE IF scenario = "HandoffMixed" /\ i = 2 THEN {"a", "d"}
    ELSE IF scenario = "HandoffInvalidTarget" /\ i = 2 THEN {"c"}
    ELSE {"None"}
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
      [] Kind(i) = "Replace" -> requests[i].input
      [] Kind(i) = "Add" ->
            [j \in DOMAIN snapshot.occurrences |-> snapshot.occurrences[j].root]
            \o (IF scenario \in {"HandoffDuplicate", "HandoffDuplicateNoIntent"}
                THEN <<>>
                ELSE IF scenario = "HandoffMixed" THEN <<"d">>
                ELSE requests[i].input)
CandidateOccurrences(i) ==
    LET req == Requested(i) IN [j \in DOMAIN req |-> OccurrenceFor(i, req[j])]
MakeSnapshot(rev, occ, policy, epoch, scopeBase, generation, coverage, closure, preparing) ==
    [workspace |-> "workspace", revision |-> rev, occurrences |-> occ,
     policy |-> policy, epoch |-> epoch, base |-> scopeBase,
     projections |-> [j \in DOMAIN occ |->
        [occurrence |-> occ[j].id, root |-> occ[j].root,
         epoch |-> epoch,
         generation |-> IF occ[j].root = "a" /\ generation.status # "Ready"
                       THEN "None" ELSE generation.identity,
         status |-> IF occ[j].root = "a" THEN generation.status ELSE "Ready"]],
     coverage |-> coverage, closure |-> closure, preparing |-> preparing]
InitialRealization == [identity |-> 0, status |-> "Ready"]
InitialSnapshot == MakeSnapshot(0, <<>>, FALSE, 0, 0, InitialRealization, {}, "Closed", 0)
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
EmptyRequest ==
    [operation |-> 0, workspace |-> "none", revision |-> 0,
     hasBaseGuard |-> FALSE, baseGuard |-> 0, deadline |-> 0, kind |-> "None",
     input |-> <<>>, target |-> "None", evidence |-> FALSE]
EmptyResult ==
    [operation |-> 0, outcome |-> "None", reason |-> "None",
     snapshot |-> InitialSnapshot, requested |-> "None",
     superseder |-> 0, authority |-> "None"]
EmptyCancellationResponse ==
    [kind |-> "None", operation |-> 0, observedOutcome |-> "None",
     observedSettlements |-> 0]
RequestFor(i, target) ==
    [operation |-> i, workspace |-> "workspace",
     revision |-> snapshot.revision,
     hasBaseGuard |-> (scenario = "HandoffBaseGuard" /\ i = 2),
     baseGuard |-> base,
     deadline |-> i, kind |-> Kind(i), input |-> InputBatch(i),
     target |-> target, evidence |-> TRUE]
RequestedResult(i, resultSnapshot, outcome) ==
    IF outcome \notin Successful \/ requests[i].target = "None"
    THEN "None"
    ELSE IF Fault = "RequestedOccurrence" /\ requests[i].target = "d"
            /\ \E o \in SetOf(resultSnapshot.occurrences) : o.root = "a"
         THEN (CHOOSE o \in SetOf(resultSnapshot.occurrences) : o.root = "a").id
         ELSE (CHOOSE o \in SetOf(resultSnapshot.occurrences) :
                    o.root = requests[i].target).id
ResultFor(i, outcome, reason, resultSnapshot) ==
    [operation |->
        IF Fault = "Association" /\ outcome = "Superseded"
        THEN superseder ELSE i,
     outcome |-> outcome, reason |-> reason, snapshot |-> resultSnapshot,
     requested |-> RequestedResult(i, resultSnapshot, outcome),
     superseder |-> IF outcome = "Superseded" THEN superseder ELSE 0,
     authority |-> IF outcome = "Unavailable" THEN "Historical" ELSE "Settlement"]
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
    /\ realization =
        IF scenario \in {"HandoffDuplicate", "HandoffDuplicateNoIntent"}
        THEN [identity |-> 0, status |-> "Pending"]
        ELSE InitialRealization
    /\ refreshed = FALSE
    /\ seen = {} /\ validationOK = TRUE /\ readOK = TRUE
    /\ requests = [i \in Ops |-> EmptyRequest]
    /\ requestStates = [i \in Ops |-> "Unissued"]
    /\ requestDeadlineValid = [i \in Ops |-> TRUE]
    /\ submissionCounts = [i \in Ops |-> 0]
    /\ settlementCounts = [i \in Ops |-> 0]
    /\ results = [i \in Ops |-> EmptyResult]
    /\ cancellationResponses = [i \in Ops |-> EmptyCancellationResponse]

IssueEnabled(i) ==
    /\ requestStates[i] = "Unissued"
    /\ CASE i = 1 -> TRUE
       [] i = 2 ->
            /\ requestStates[1] \in {"Submitted", "Settled"}
            /\ \/ outcomes[1] \in Terminal
               \/ /\ active = 1
                  /\ ((/\ perturbation = "Supersede"
                       /\ secondKind \in {"Replace", "Clear"})
                      \/ scenario \in {"HandoffRevision",
                             "HandoffBaseGuard", "HandoffInvalidTarget"})
       [] OTHER ->
            /\ requestStates[1] = "Settled"
            /\ IF scenario = "Refresh"
               THEN outcomes[1] = "Committed" /\ 2 \notin admitted
               ELSE IF scenario = "Readd"
                    THEN outcomes[2] = "Committed"
                    ELSE scenario = "PhysicalRace"
                         /\ outcomes[2] = "AwaitRefresh"
Issue(i, target) ==
    /\ IssueEnabled(i) /\ target \in TargetOptions(i)
    /\ requests' = [requests EXCEPT ![i] = RequestFor(i, target)]
    /\ requestStates' = [requestStates EXCEPT ![i] = "Issued"]
    /\ seen' = seen \union {"Issued", "Issued" \o ToString(i)}
    /\ UNCHANGED <<artifactVars, physicalHistory, scopeHistory, scenario,
        perturbation, secondKind, plans, sealed, active, admitted, outcomes,
        snapshots, snapshot, tokens, stopReason, superseder, progress,
        realization, refreshed, validationOK, readOK, requestDeadlineValid,
        submissionCounts, settlementCounts, results, cancellationResponses>>
IssueAny(i) == \E target \in TargetOptions(i) : Issue(i, target)
Abandon(i) ==
    /\ scenario = "HandoffIssuance" /\ requestStates[i] = "Issued"
    /\ requestStates' = [requestStates EXCEPT ![i] = "Abandoned"]
    /\ seen' = seen \union {"Abandoned"}
    /\ UNCHANGED <<artifactVars, physicalHistory, scopeHistory, scenario,
        perturbation, secondKind, plans, sealed, active, admitted, outcomes,
        snapshots, snapshot, tokens, stopReason, superseder, progress,
        realization, refreshed, validationOK, readOK, requests,
        requestDeadlineValid, submissionCounts, settlementCounts, results,
        cancellationResponses>>
ExpireIssuedDeadline(i) ==
    /\ scenario = "HandoffDeadline" /\ requestStates[i] = "Issued"
    /\ requestDeadlineValid[i]
    /\ requestDeadlineValid' =
        [requestDeadlineValid EXCEPT ![i] = FALSE]
    /\ seen' = seen \union {"IssuedDeadlineExpired"}
    /\ UNCHANGED <<artifactVars, physicalHistory, scopeHistory, scenario,
        perturbation, secondKind, plans, sealed, active, admitted, outcomes,
        snapshots, snapshot, tokens, stopReason, superseder, progress,
        realization, refreshed, validationOK, readOK, requests, requestStates,
        submissionCounts, settlementCounts, results, cancellationResponses>>
ActivationValid(request) ==
    request.target = "None"
    \/ /\ request.kind \in {"Add", "Replace"}
       /\ request.target \in SetOf(request.input)
ValidateIssued(i) ==
    CASE runtime = "Closed" -> "Unavailable"
      [] requests[i].workspace # "workspace" -> "ForeignWorkspace"
      [] requests[i].revision # snapshot.revision -> "RevisionMismatch"
      [] requests[i].hasBaseGuard
            /\ requests[i].baseGuard # snapshot.base -> "ScopeBaseMismatch"
      [] ~requestDeadlineValid[requests[i].deadline] -> "DeadlineExpired"
      [] ~requests[i].evidence -> "EvidenceMismatch"
      [] ~ActivationValid(requests[i]) -> "InvalidTarget"
      [] OTHER -> "Valid"
SubmissionDecisionFor(i) ==
    IF Fault = "HandoffValidation" /\ active # 0 THEN "Busy"
    ELSE IF ValidateIssued(i) # "Valid" THEN ValidateIssued(i)
    ELSE IF active # 0
         THEN IF requests[i].kind \in {"Replace", "Clear"}
              THEN "Supersede" ELSE "Busy"
    ELSE IF requests[i].kind = "Add"
            /\ PlanFor(i).prepared = {}
         THEN "NoEffect"
         ELSE "Admit"
SubmitRejected(i) ==
    LET reason == SubmissionDecisionFor(i) IN
    /\ requestStates[i] = "Issued"
    /\ reason \in {"ForeignWorkspace", "RevisionMismatch",
                   "ScopeBaseMismatch", "DeadlineExpired",
                   "EvidenceMismatch", "InvalidTarget", "Busy"}
    /\ requestStates' = [requestStates EXCEPT ![i] = "Settled"]
    /\ submissionCounts' = [submissionCounts EXCEPT ![i] = @ + 1]
    /\ settlementCounts' = [settlementCounts EXCEPT ![i] = @ + 1]
    /\ outcomes' = [outcomes EXCEPT ![i] = "Rejected"]
    /\ snapshots' = [snapshots EXCEPT ![i] = snapshot]
    /\ results' =
        [results EXCEPT ![i] = ResultFor(i, "Rejected", reason, snapshot)]
    /\ seen' = seen \union {"Rejected", reason}
    /\ UNCHANGED <<artifactVars, physicalHistory, scopeHistory, scenario,
        perturbation, secondKind, plans, sealed, active, admitted, snapshot,
        tokens, stopReason, superseder, progress, realization, refreshed,
        validationOK, readOK, requests, requestDeadlineValid,
        cancellationResponses>>
SubmitNoEffect(i) ==
    /\ requestStates[i] = "Issued"
    /\ SubmissionDecisionFor(i) = "NoEffect"
    /\ requestStates' = [requestStates EXCEPT ![i] = "Settled"]
    /\ submissionCounts' = [submissionCounts EXCEPT ![i] = @ + 1]
    /\ settlementCounts' = [settlementCounts EXCEPT ![i] = @ + 1]
    /\ outcomes' = [outcomes EXCEPT ![i] = "NoEffect"]
    /\ snapshots' = [snapshots EXCEPT ![i] = snapshot]
    /\ results' =
        [results EXCEPT ![i] = ResultFor(i, "NoEffect", "None", snapshot)]
    /\ seen' = seen \union {"NoEffect"}
        \union (IF requests[i].target = "None"
                THEN {"NoEffectWithoutTarget"}
                ELSE {"NoEffectWithTarget"})
        \union (IF \E j \in DOMAIN snapshot.projections :
                    snapshot.occurrences[j].root = requests[i].target
                    /\ snapshot.projections[j].status # "Ready"
                THEN {"NoEffectRetainedNonReady"} ELSE {})
    /\ UNCHANGED <<artifactVars, physicalHistory, scopeHistory, scenario,
        perturbation, secondKind, plans, sealed, active, admitted, snapshot,
        tokens, stopReason, superseder, progress, realization, refreshed,
        validationOK, readOK, requests, requestDeadlineValid,
        cancellationResponses>>
SubmitUnavailable(i) ==
    /\ requestStates[i] = "Issued"
    /\ SubmissionDecisionFor(i) = "Unavailable"
    /\ requestStates' = [requestStates EXCEPT ![i] = "Settled"]
    /\ submissionCounts' = [submissionCounts EXCEPT ![i] = @ + 1]
    /\ settlementCounts' = [settlementCounts EXCEPT ![i] = @ + 1]
    /\ outcomes' = [outcomes EXCEPT ![i] = "Unavailable"]
    /\ snapshots' = [snapshots EXCEPT ![i] = snapshot]
    /\ results' =
        [results EXCEPT ![i] = ResultFor(i, "Unavailable", "Closed", snapshot)]
    /\ seen' = seen \union {"ClosedSubmission", "Unavailable"}
    /\ UNCHANGED <<artifactVars, physicalHistory, scopeHistory, scenario,
        perturbation, secondKind, plans, sealed, active, admitted, snapshot,
        tokens, stopReason, superseder, progress, realization, refreshed,
        validationOK, readOK, requests, requestDeadlineValid,
        cancellationResponses>>

AdmissionEnabled(i) ==
    /\ runtime = "Open" /\ GateFree /\ active = 0
    /\ i \notin admitted /\ requestStates[i] \in {"Issued", "Submitted"}
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
    /\ IF requestStates[i] = "Issued"
       THEN /\ SubmissionDecisionFor(i) = "Admit"
            /\ requestStates' = [requestStates EXCEPT ![i] = "Submitted"]
            /\ submissionCounts' = [submissionCounts EXCEPT ![i] = @ + 1]
       ELSE /\ superseder = i
            /\ UNCHANGED <<requestStates, submissionCounts>>
    /\ ScopeAdvance
    /\ plans' = [plans EXCEPT ![i] = PlanFor(i)]
    /\ active' = i /\ admitted' = admitted \union {i}
    /\ superseder' = IF i = 2 THEN 0 ELSE superseder
    /\ outcomes' = [outcomes EXCEPT ![i] = "Pending"]
    /\ snapshot' = [snapshot EXCEPT !.base = base', !.preparing = i]
    /\ seen' = seen \union {IF i = 1 THEN "Admitted" ELSE "Readmitted"}
    /\ UNCHANGED <<scenario, secondKind, sealed, snapshots, tokens, stopReason,
        progress, realization, refreshed, validationOK, readOK, requests,
        requestDeadlineValid, settlementCounts, results, cancellationResponses>>
PrepareBatch(i) ==
    /\ active = i /\ outcomes[i] = "Pending" /\ stopReason[i] = "None"
    /\ Kind(i) # "Refresh"
    /\ Activate(i)
    /\ UNCHANGED scopeVars
Seal(i) ==
    /\ active = i /\ receipts[i] = "Prepared" /\ i \notin sealed
    /\ stopReason[i] = "None" /\ superseder = 0
    /\ plans' = [plans EXCEPT ![i].base = base]
    /\ sealed' = sealed \union {i}
    /\ UNCHANGED <<artifactVars, scenario, secondKind, active, admitted, outcomes,
        snapshots, snapshot, tokens, stopReason, superseder, progress,
        realization, refreshed, seen, validationOK, readOK, handoffVars>>
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
        refreshed, validationOK, readOK, handoffVars>>
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
            ELSE {},
            IF plans[i].kind = "Closure" THEN "Complete"
            ELSE IF plans[i].policy THEN "NotEvaluated" ELSE "Closed", 0)]
    /\ UNCHANGED <<scenario, secondKind, plans, sealed, active, admitted,
        outcomes, snapshots, snapshot, stopReason, superseder, progress,
        realization, refreshed, seen, validationOK, readOK, handoffVars>>
Commit(i) ==
    LET finalSnapshot ==
            IF Fault = "Partial" /\ Len(tokens[i].occurrences) > 1
            THEN [tokens[i] EXCEPT !.occurrences = <<@[1]>>]
            ELSE tokens[i]
        settles(j) == j = i \/ outcomes[j] = "AwaitRefresh"
        finalOutcome(j) ==
            IF j = i THEN "Committed"
            ELSE IF stopReason[j] # "None" THEN stopReason[j] ELSE "Failed"
    IN
    /\ active = i /\ Publish(i)
    /\ snapshot' = finalSnapshot
    /\ outcomes' = [j \in Ops |->
        IF settles(j) THEN finalOutcome(j) ELSE outcomes[j]]
    /\ snapshots' = [j \in Ops |->
        IF settles(j) THEN finalSnapshot ELSE snapshots[j]]
    /\ requestStates' = [j \in Ops |->
        IF settles(j) THEN "Settled" ELSE requestStates[j]]
    /\ settlementCounts' = [j \in Ops |->
        IF settles(j) THEN settlementCounts[j] + 1 ELSE settlementCounts[j]]
    /\ results' = [j \in Ops |->
        IF settles(j)
        THEN ResultFor(j, finalOutcome(j), "None", finalSnapshot)
        ELSE results[j]]
    /\ active' = 0
    /\ seen' = seen \union {plans[i].kind \o "Committed"}
        \union (IF plans[i].kind = "Refresh" THEN {"RefreshedSnapshot"} ELSE {})
        \union (IF requests[i].target = "a" THEN {"RequestedExistingCommitted"}
                ELSE IF requests[i].target = "d"
                     THEN {"RequestedNewCommitted"}
                     ELSE IF requests[i].target = "b"
                          THEN {"RequestedReplaceCommitted"} ELSE {})
    /\ UNCHANGED <<scenario, secondKind, plans, sealed, admitted, tokens,
        stopReason, superseder, progress, realization, refreshed, validationOK,
        readOK, requests, requestDeadlineValid, submissionCounts,
        cancellationResponses>>

Supersede ==
    /\ perturbation = "Supersede"
    /\ runtime = "Open" /\ GateFree /\ active = 1 /\ superseder = 0
    /\ 2 \notin admitted /\ secondKind \in {"Replace", "Clear"}
    /\ scenario = "Edits" /\ stopReason[1] = "None"
    /\ requestStates[2] = "Issued"
    /\ SubmissionDecisionFor(2) = "Supersede"
    /\ ScopeAdvance
    /\ superseder' = 2
    /\ stopReason' = [stopReason EXCEPT ![1] = "Superseded"]
    /\ requestStates' = [requestStates EXCEPT ![2] = "Submitted"]
    /\ submissionCounts' = [submissionCounts EXCEPT ![2] = @ + 1]
    /\ snapshot' = [snapshot EXCEPT !.base = base']
    /\ seen' = seen \union {secondKind \o "Supersedes"}
    /\ UNCHANGED <<scenario, secondKind, plans, sealed, active, admitted,
        outcomes, snapshots, tokens, progress, realization, refreshed,
        validationOK, readOK, requests, requestDeadlineValid, settlementCounts,
        results, cancellationResponses>>
Signal(i, reason) ==
    /\ i \in 1..2
    /\ Kind(i) \notin {"Refresh", "Closure"}
    /\ perturbation = reason
    /\ reason \notin seen
    /\ active = i /\ stopReason[i] = "None" /\ superseder = 0
    /\ IF reason = "Deadline" THEN Expire(i) ELSE Cancel(i)
    /\ stopReason' = [stopReason EXCEPT ![i] = "Cancelled"]
    /\ cancellationResponses' =
        IF reason = "Cancellation"
        THEN [cancellationResponses EXCEPT ![i] =
                [kind |-> "Accepted", operation |-> i,
                 observedOutcome |-> outcomes[i],
                 observedSettlements |-> settlementCounts[i]]]
        ELSE cancellationResponses
    /\ seen' = seen \union {reason}
    /\ UNCHANGED <<scenario, secondKind, plans, sealed, active, admitted,
        outcomes, snapshots, snapshot, tokens, superseder, progress,
        realization, refreshed, validationOK, readOK, requests, requestStates,
        requestDeadlineValid, submissionCounts, settlementCounts, results>>
Fail(i) ==
    /\ i \in 1..2
    /\ Kind(i) \notin {"Refresh", "Closure"}
    /\ perturbation = "Failure" /\ "Failure" \notin seen
    /\ active = i /\ GateFree /\ stopReason[i] = "None"
    /\ stopReason' = [stopReason EXCEPT ![i] = "Failed"]
    /\ seen' = seen \union {"Failure"}
    /\ UNCHANGED <<artifactVars, scenario, secondKind, plans, sealed, active,
        admitted, outcomes, snapshots, snapshot, tokens, superseder, progress,
        realization, refreshed, validationOK, readOK, handoffVars>>
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
        realization, refreshed, seen, validationOK, readOK, handoffVars>>
ReleaseStopped(i) ==
    /\ active = i /\ stopReason[i] # "None"
    /\ Release(i)
    /\ UNCHANGED scopeVars
Finish(i) ==
    LET finalSnapshot == [snapshot EXCEPT !.base = base', !.preparing = 0]
        finalOutcome ==
            IF snapshot.epoch # physical THEN "AwaitRefresh"
            ELSE IF stopReason[i] # "None" THEN stopReason[i] ELSE "Failed"
    IN
    /\ active = i /\ GateFree
    /\ \/ artifactResults[i] = "Refused"
       \/ phases[i] = "Dormant" /\ stopReason[i] # "None"
       \/ Fault = "Cleanup" /\ stopReason[i] = "Cancelled"
    /\ runtime = "Open" /\ ScopeAdvance
    /\ snapshot' = finalSnapshot
    /\ outcomes' = [outcomes EXCEPT ![i] = finalOutcome]
    /\ snapshots' = [snapshots EXCEPT ![i] = finalSnapshot]
    /\ IF finalOutcome = "AwaitRefresh"
       THEN UNCHANGED <<requestStates, settlementCounts, results>>
       ELSE /\ requestStates' = [requestStates EXCEPT ![i] = "Settled"]
            /\ settlementCounts' =
                [settlementCounts EXCEPT ![i] = @ + 1]
            /\ results' =
                [results EXCEPT ![i] =
                    ResultFor(i, finalOutcome, stopReason[i], finalSnapshot)]
    /\ active' = 0
    /\ seen' = seen \union {"Settled", finalOutcome}
        \union (IF finalOutcome = "Superseded"
                THEN {"SupersededAssociation"} ELSE {})
    /\ UNCHANGED <<scenario, secondKind, plans, sealed, admitted, tokens,
        stopReason, superseder, progress, realization, refreshed, validationOK,
        readOK, requests, requestDeadlineValid, submissionCounts,
        cancellationResponses>>

PhysicalChange ==
    /\ scenario \in {"Refresh", "PhysicalRace"} /\ ~refreshed
    /\ runtime = "Open" /\ GateFree
    /\ IF scenario = "Refresh" THEN outcomes[3] = "Committed"
       ELSE active = 2 /\ 2 \in sealed /\ outcomes[1] = "Committed"
    /\ First!RefreshPhysical(4) /\ UNCHANGED <<local2, local3>>
    /\ realization' \in {[identity |-> 1, status |-> state] :
                             state \in {"Ready", "Pending", "Failed"}}
    /\ refreshed' = TRUE
    /\ seen' = seen \union {"PhysicalChanged"}
    /\ UNCHANGED <<scenario, secondKind, plans, sealed, active, admitted,
        outcomes, snapshots, snapshot, tokens, stopReason, superseder, progress,
        validationOK, readOK, handoffVars>>
RefreshScope(i) ==
    LET finalSnapshot ==
            MakeSnapshot(snapshot.revision, snapshot.occurrences,
                snapshot.policy, physical, base', realization,
                IF Fault = "Refresh" THEN snapshot.coverage ELSE {},
                IF snapshot.policy THEN "NotEvaluated" ELSE "Closed", 0)
        settles(j) == j = i \/ outcomes[j] = "AwaitRefresh"
        finalOutcome(j) ==
            IF j = i THEN "Committed"
            ELSE IF stopReason[j] # "None" THEN stopReason[j] ELSE "Failed"
    IN
    /\ active = i /\ Kind(i) = "Refresh" /\ outcomes[i] = "Pending"
    /\ runtime = "Open" /\ GateFree
    /\ ScopeAdvance
    /\ snapshot' = finalSnapshot
    /\ outcomes' = [j \in Ops |->
        IF settles(j) THEN finalOutcome(j) ELSE outcomes[j]]
    /\ snapshots' = [j \in Ops |->
        IF settles(j) THEN finalSnapshot ELSE snapshots[j]]
    /\ requestStates' = [j \in Ops |->
        IF settles(j) THEN "Settled" ELSE requestStates[j]]
    /\ settlementCounts' = [j \in Ops |->
        IF settles(j) THEN settlementCounts[j] + 1 ELSE settlementCounts[j]]
    /\ results' = [j \in Ops |->
        IF settles(j)
        THEN ResultFor(j, finalOutcome(j), "None", finalSnapshot)
        ELSE results[j]]
    /\ active' = 0
    /\ seen' = seen \union {"RefreshCommitted", "RefreshedSnapshot",
        "Refreshed" \o realization.status}
    /\ UNCHANGED <<scenario, secondKind, plans, sealed, admitted, tokens,
        stopReason, superseder, progress, realization, refreshed, validationOK,
        readOK, requests, requestDeadlineValid, submissionCounts,
        cancellationResponses>>
Observe ==
    /\ runtime = "Open" /\ GateFree
    /\ "ReadCurrent" \notin seen /\ snapshot.epoch = physical
    /\ readOK' = (snapshot.base = base
        /\ Correspondences(snapshot.occurrences) = physicalRoots)
    /\ seen' = seen \union {"ReadCurrent"}
    /\ UNCHANGED <<artifactVars, scenario, secondKind, plans, sealed,
        active, admitted, outcomes, snapshots, snapshot, tokens, stopReason,
        superseder, progress, realization, refreshed, validationOK,
        handoffVars>>
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
        outcomes, snapshots, tokens, superseder, progress, realization,
        refreshed, readOK, handoffVars>>
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
        realization, refreshed, validationOK, readOK, handoffVars>>
CancellationControl(i, responseKind) ==
    /\ scenario = "HandoffCancellation"
    /\ requestStates[i] \in {"Issued", "Settled"}
    /\ active # i
    /\ cancellationResponses[i].kind = "None"
    /\ responseKind \in {"ObservedNoEffect", "Settlement"}
    /\ (responseKind = "Settlement" => settlementCounts[i] = 1)
    /\ (requestStates[i] = "Issued" => responseKind = "ObservedNoEffect")
    /\ cancellationResponses' =
        [cancellationResponses EXCEPT ![i] =
            [kind |-> responseKind,
             operation |->
                IF responseKind = "Settlement"
                THEN results[i].operation ELSE i,
             observedOutcome |-> outcomes[i],
             observedSettlements |-> settlementCounts[i]]]
    /\ IF Fault = "CancelControl" /\ responseKind = "ObservedNoEffect"
       THEN /\ outcomes' = [outcomes EXCEPT ![i] = "NoEffect"]
            /\ snapshots' = [snapshots EXCEPT ![i] = snapshot]
            /\ requestStates' = [requestStates EXCEPT ![i] = "Settled"]
            /\ settlementCounts' =
                [settlementCounts EXCEPT ![i] = @ + 1]
            /\ results' =
                [results EXCEPT ![i] =
                    ResultFor(i, "NoEffect", "None", snapshot)]
       ELSE UNCHANGED <<outcomes, snapshots, requestStates,
            settlementCounts, results>>
    /\ seen' = seen \union {responseKind}
    /\ UNCHANGED <<artifactVars, physicalHistory, scopeHistory, scenario,
        perturbation, secondKind, plans, sealed, active, admitted, snapshot,
        tokens, stopReason, superseder, progress, realization, refreshed,
        validationOK, readOK, requests, requestDeadlineValid,
        submissionCounts>>
Close ==
    /\ perturbation = "Close"
    /\ First!CloseRuntime /\ Second!CloseRuntime /\ Third!CloseRuntime
    /\ seen' = seen \union {"Closed"}
    /\ UNCHANGED <<scenario, secondKind, plans, sealed, active, admitted,
        outcomes, snapshots, snapshot, tokens, stopReason, superseder, progress,
        realization, refreshed, validationOK, readOK, handoffVars>>
FinishClosed(i) ==
    LET settles(j) == j = i \/ outcomes[j] = "AwaitRefresh" IN
    /\ runtime = "Closed" /\ active = i
    /\ receipts[i] = "Released"
    /\ active' = 0
    /\ outcomes' = [j \in Ops |->
        IF settles(j) THEN "Unavailable" ELSE outcomes[j]]
    /\ snapshots' = [j \in Ops |->
        IF settles(j) THEN snapshot ELSE snapshots[j]]
    /\ requestStates' = [j \in Ops |->
        IF settles(j) THEN "Settled" ELSE requestStates[j]]
    /\ settlementCounts' = [j \in Ops |->
        IF settles(j) THEN settlementCounts[j] + 1 ELSE settlementCounts[j]]
    /\ results' = [j \in Ops |->
        IF settles(j)
        THEN ResultFor(j, "Unavailable", "Closed", snapshot)
        ELSE results[j]]
    /\ seen' = seen \union {"Unavailable"}
    /\ UNCHANGED <<artifactVars, scenario, secondKind, plans, sealed, admitted,
        snapshot, tokens, stopReason, superseder, progress, realization,
        refreshed, validationOK, readOK, requests, requestDeadlineValid,
        submissionCounts, cancellationResponses>>
FinishClosedRefresh ==
    LET settles(i) == outcomes[i] = "AwaitRefresh" IN
    /\ runtime = "Closed" /\ active = 0
    /\ \E i \in Ops : settles(i)
    /\ outcomes' = [i \in Ops |->
        IF settles(i) THEN "Unavailable" ELSE outcomes[i]]
    /\ snapshots' = [i \in Ops |->
        IF settles(i) THEN snapshot ELSE snapshots[i]]
    /\ requestStates' = [i \in Ops |->
        IF settles(i) THEN "Settled" ELSE requestStates[i]]
    /\ settlementCounts' = [i \in Ops |->
        IF settles(i) THEN settlementCounts[i] + 1 ELSE settlementCounts[i]]
    /\ results' = [i \in Ops |->
        IF settles(i)
        THEN ResultFor(i, "Unavailable", "Closed", snapshot)
        ELSE results[i]]
    /\ UNCHANGED <<artifactVars, scenario, secondKind, plans, sealed, active,
        admitted, snapshot, tokens, stopReason, superseder, progress,
        realization, refreshed, seen, validationOK, readOK, requests,
        requestDeadlineValid, submissionCounts, cancellationResponses>>
RejectLateCompletion ==
    /\ outcomes[1] = "Superseded" /\ outcomes[2] = "Committed"
    /\ "LateSupersededCompletion" \notin seen
    /\ seen' = seen \union {"LateSupersededCompletion"}
    /\ UNCHANGED <<artifactVars, scenario, secondKind, plans, sealed, active,
        admitted, outcomes, snapshots, snapshot, tokens, stopReason,
        superseder, progress, realization, refreshed, validationOK, readOK,
        handoffVars>>
BrokenRebase ==
    /\ Fault = "ScopeBase" /\ active = 1 /\ superseder = 2
    /\ 1 \in sealed /\ receipts[1] = "Prepared"
    /\ plans[1].base # base
    /\ plans' = [plans EXCEPT ![1].base = base]
    /\ UNCHANGED <<artifactVars, scenario, secondKind, sealed, active, admitted,
        outcomes, snapshots, snapshot, tokens, stopReason, superseder, progress,
        realization, refreshed, seen, validationOK, readOK, handoffVars>>
BrokenGate ==
    /\ Fault = "Gate" /\ phase1 = "Staged"
    /\ ScopeAdvance
    /\ snapshot' = [snapshot EXCEPT !.base = base']
    /\ UNCHANGED <<scenario, secondKind, plans, sealed, active, admitted,
        outcomes, snapshots, tokens, stopReason, superseder, progress,
        realization, refreshed, seen, validationOK, readOK, handoffVars>>
Next ==
    /\ \/ \E i \in Ops :
            IssueAny(i)
            \/ Abandon(i) \/ ExpireIssuedDeadline(i)
            \/ SubmitRejected(i) \/ SubmitNoEffect(i) \/ SubmitUnavailable(i)
            \/ Admit(i) \/ PrepareBatch(i) \/ Seal(i) \/ Progress(i)
            \/ StagePublication(i) \/ PrepareToken(i) \/ Commit(i)
            \/ (\E reason \in {"Cancellation", "Deadline"} : Signal(i, reason))
            \/ Fail(i) \/ RejectCompletion(i) \/ ReleaseStopped(i) \/ Finish(i)
            \/ RefreshScope(i)
            \/ Replay(i) \/ LateCancel(i) \/ FinishClosed(i)
            \/ (\E responseKind \in {"ObservedNoEffect", "Settlement"} :
                    CancellationControl(i, responseKind))
       \/ Supersede \/ PhysicalChange \/ Observe
       \/ \E error \in {"RevisionMismatch", "ForeignWorkspace", "InvalidReplace",
                       "DeadlineExpired", "Busy", "EvidenceMismatch"} :
            ProbeValidation(error)
       \/ RejectForeignCompletion \/ Close
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
        /\ Correspondences(snapshot.occurrences) = physicalRoots
        /\ \A j \in DOMAIN snapshot.projections :
            /\ snapshot.projections[j].status =
                IF snapshot.projections[j].root = "a"
                THEN realization.status ELSE "Ready"
            /\ snapshot.projections[j].generation =
                IF snapshot.projections[j].status = "Ready"
                THEN realization.identity ELSE "None")
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
RequestLifecycleIsOneShot ==
    \A i \in Ops :
        /\ submissionCounts[i] \in 0..1
        /\ settlementCounts[i] \in 0..1
        /\ settlementCounts[i] <= submissionCounts[i]
        /\ requests[i].operation \in {0, i}
        /\ CASE requestStates[i] = "Unissued" ->
                /\ submissionCounts[i] = 0
                /\ settlementCounts[i] = 0
                /\ outcomes[i] = "None"
           [] requestStates[i] = "Issued" ->
                /\ requests[i].operation = i
                /\ submissionCounts[i] = 0
                /\ settlementCounts[i] = 0
                /\ outcomes[i] = "None"
           [] requestStates[i] = "Abandoned" ->
                /\ requests[i].operation = i
                /\ submissionCounts[i] = 0
                /\ settlementCounts[i] = 0
                /\ outcomes[i] = "None"
           [] requestStates[i] = "Submitted" ->
                /\ requests[i].operation = i
                /\ submissionCounts[i] = 1
                /\ settlementCounts[i] = 0
                /\ outcomes[i] \in {"None", "Pending", "AwaitRefresh"}
           [] OTHER ->
                /\ requestStates[i] = "Settled"
                /\ requests[i].operation = i
                /\ submissionCounts[i] = 1
                /\ settlementCounts[i] = 1
                /\ outcomes[i] \in Terminal
TerminalResultsPreserveAssociation ==
    \A i \in Ops : outcomes[i] \in Terminal =>
        /\ results[i].operation = i
        /\ results[i].outcome = outcomes[i]
        /\ results[i].snapshot = snapshots[i]
        /\ results[i].authority =
            IF outcomes[i] = "Unavailable" THEN "Historical" ELSE "Settlement"
        /\ IF outcomes[i] = "Superseded"
           THEN /\ results[i].superseder \in Ops \ {i}
                /\ requests[results[i].superseder].operation =
                    results[i].superseder
           ELSE results[i].superseder = 0
RequestedOccurrenceIsExact ==
    \A i \in Ops :
        /\ (outcomes[i] \notin Successful =>
                results[i].requested = "None")
        /\ (outcomes[i] \in Successful /\ requests[i].target = "None" =>
                results[i].requested = "None")
        /\ (outcomes[i] \in Successful /\ requests[i].target # "None" =>
                \E o \in SetOf(results[i].snapshot.occurrences) :
                    /\ o.id = results[i].requested
                    /\ o.root = requests[i].target
                    /\ requests[i].target \in SetOf(requests[i].input))
InvalidActivationNeverSubmits ==
    \A i \in Ops :
        ~ActivationValid(requests[i])
        /\ requestStates[i] = "Settled"
        /\ requests[i].revision = results[i].snapshot.revision
        /\ (~requests[i].hasBaseGuard
            \/ requests[i].baseGuard = results[i].snapshot.base)
        /\ requestDeadlineValid[requests[i].deadline]
        /\ requests[i].evidence =>
            /\ outcomes[i] = "Rejected"
            /\ results[i].reason = "InvalidTarget"
            /\ i \notin admitted
DuplicateNoEffectPreservesRetainedState ==
    \A i \in Ops : outcomes[i] = "NoEffect" =>
        /\ snapshots[i] = results[i].snapshot
        /\ snapshots[i].revision = requests[i].revision
        /\ Correspondences(snapshots[i].occurrences) = physicalRoots
        /\ (scenario \in {"HandoffDuplicate", "HandoffDuplicateNoIntent"} =>
            \E j \in DOMAIN snapshots[i].projections :
                /\ snapshots[i].occurrences[j].root = "a"
                /\ snapshots[i].projections[j].status = "Pending")
CancellationResponsesAreTyped ==
    \A i \in Ops :
        /\ cancellationResponses[i].kind \in
            {"None", "Accepted", "ObservedNoEffect", "Settlement"}
        /\ (cancellationResponses[i].kind # "None" =>
                cancellationResponses[i].operation = i)
        /\ (cancellationResponses[i].kind = "Settlement" =>
                /\ cancellationResponses[i].observedSettlements = 1
                /\ cancellationResponses[i].observedOutcome \in Terminal
                /\ cancellationResponses[i].operation =
                    results[i].operation)
inertScopeState ==
    <<artifactVars, physicalHistory, scopeHistory, plans, sealed, active,
      admitted, outcomes, snapshots, snapshot, tokens, stopReason, superseder,
      progress, realization, refreshed, validationOK, readOK>>
IssueOrAbandonStep(i) ==
    \/ requestStates[i] = "Unissued" /\ requestStates'[i] = "Issued"
    \/ requestStates[i] = "Issued" /\ requestStates'[i] = "Abandoned"
IssuanceAndAbandonmentAreInert ==
    [][\A i \in Ops : IssueOrAbandonStep(i) =>
        UNCHANGED inertScopeState]_vars
IssuedRequestIsFrozen ==
    [][\A i \in Ops :
        requestStates[i] # "Unissued"
        /\ requestStates'[i] # "Unissued" =>
            requests'[i] = requests[i]]_vars
ControlNoEffectCannotSettleMutation ==
    [][\A i \in Ops :
        cancellationResponses[i].kind = "None"
        /\ cancellationResponses'[i].kind = "ObservedNoEffect" =>
            /\ settlementCounts' = settlementCounts
            /\ outcomes' = outcomes
            /\ results' = results]_vars
DuplicateNoEffectHasNoPhysicalWork ==
    [][\A i \in Ops :
        outcomes[i] # "NoEffect" /\ outcomes'[i] = "NoEffect" =>
            UNCHANGED <<artifactVars, physicalHistory, scopeHistory, plans,
                sealed, active, admitted, snapshot, tokens, stopReason,
                superseder, progress, realization, refreshed>>]_vars
ScopeSafety ==
    /\ CompleteSnapshot /\ ExactOccurrenceRetention /\ NoPartialPublication
    /\ OnePreparingMutation /\ SettledReleasesAuthority /\ SupersededCannotCommit
    /\ FinalCommitWins /\ CurrentCoverageIsExact /\ ClosureRefreshRetainsOccurrences
    /\ RefusalPreservesLogicalRevision
    /\ ReaddedOccurrenceIsFresh /\ RequestLifecycleIsOneShot
    /\ TerminalResultsPreserveAssociation /\ RequestedOccurrenceIsExact
    /\ InvalidActivationNeverSubmits
    /\ DuplicateNoEffectPreservesRetainedState /\ CancellationResponsesAreTyped
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
        /\ WF_vars(Framed(IssueAny(i)))
        /\ WF_vars(Framed(PrepareBatch(i)))
        /\ WF_vars(Framed(Seal(i)))
        /\ WF_vars(Framed(StagePublication(i)))
        /\ WF_vars(Framed(PrepareToken(i)))
        /\ WF_vars(Framed(Commit(i)))
        /\ WF_vars(Framed(RefreshScope(i)))
        /\ WF_vars(Framed(Signal(i, "Deadline")))
        /\ WF_vars(Framed(RejectCompletion(i)))
        /\ WF_vars(Framed(RejectForeignCompletion))
        /\ WF_vars(Framed(ReleaseStopped(i)))
        /\ WF_vars(Framed(Finish(i)))
        /\ WF_vars(Framed(FinishClosed(i)))
    /\ WF_vars(Framed(Admit(3)))
    /\ WF_vars(Framed(FinishClosedRefresh))
Spec == SafetySpec /\ Fairness
HandoffSpec == SafetySpec /\ secondKind = "Add"
HandoffSupersedeSpec == SafetySpec /\ secondKind = "Replace"
RefreshProgressAddSpec == Spec /\ secondKind = "Add"
RefreshProgressReplaceSpec == Spec /\ secondKind = "Replace"
RefreshProgressRemoveSpec == Spec /\ secondKind = "Remove"
RefreshProgressClearSpec == Spec /\ secondKind = "Clear"
EveryAdmittedOperationSettles ==
    \A i \in Ops : i \in admitted ~> outcomes[i] \in Terminal
DeadlineSpec ==
    /\ SafetySpec
    /\ WF_vars(Framed(Signal(1, "Deadline")))
    /\ WF_vars(Framed(RejectCompletion(1)))
    /\ WF_vars(Framed(ReleaseStopped(1)))
    /\ WF_vars(Framed(Finish(1)))
=============================================================================
