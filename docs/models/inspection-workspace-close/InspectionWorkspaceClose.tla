--------------------- MODULE InspectionWorkspaceClose ---------------------
(***************************************************************************)
(* Models the workspace-level close contract in                            *)
(* docs/inspection-space.md, "Workspace close and group release authority". *)
(*                                                                         *)
(* The model owns workspace admission, publication versus late-result      *)
(* routing, direct versus coordinated release authority, and asynchronous  *)
(* close completion. It abstracts the package-role lease owner and the     *)
(* AssemblyContextGroup callback/resource protocol as adjacent components. *)
(* The direct path consumes the exact group's imported terminal receipt;   *)
(* coordinated lease return remains an external authorization event, while *)
(* groupBusy represents the group owner's internal quiescence.              *)
(***************************************************************************)
EXTENDS FiniteSets, Naturals, TLC

CONSTANTS
    Groups,
    CoordinatedGroups,
    MaxLeases,
    AllowAdmissionAfterClose,
    AllowLeaseAfterClose,
    AllowWrongDirectReleaseAuthority,
    AllowWrongCoordinatedReleaseAuthority,
    AllowReleaseWithActiveLease,
    AllowReleaseBeforeGroupQuiescence,
    AllowLatePublication,
    AllowEarlyWorkspaceCompletion,
    AllowCleanupOmission,
    AllowDoubleRelease,
    AllowStrandedNoGroupCompletion,
    AllowOwnerFirstHistoryLoss,
    AllowNoGroupCleanupEntry

ASSUME
    /\ Groups # {}
    /\ IsFiniteSet(Groups)
    /\ CoordinatedGroups \subseteq Groups
    /\ MaxLeases \in Nat \ {0}
    /\ AllowAdmissionAfterClose \in BOOLEAN
    /\ AllowLeaseAfterClose \in BOOLEAN
    /\ AllowWrongDirectReleaseAuthority \in BOOLEAN
    /\ AllowWrongCoordinatedReleaseAuthority \in BOOLEAN
    /\ AllowReleaseWithActiveLease \in BOOLEAN
    /\ AllowReleaseBeforeGroupQuiescence \in BOOLEAN
    /\ AllowLatePublication \in BOOLEAN
    /\ AllowEarlyWorkspaceCompletion \in BOOLEAN
    /\ AllowCleanupOmission \in BOOLEAN
    /\ AllowDoubleRelease \in BOOLEAN
    /\ AllowStrandedNoGroupCompletion \in BOOLEAN
    /\ AllowOwnerFirstHistoryLoss \in BOOLEAN
    /\ AllowNoGroupCleanupEntry \in BOOLEAN

DirectGroups == Groups \ CoordinatedGroups
NoGroupIdentity == "NoGroupIdentity"
HasComposedDirectGroup == DirectGroups # {}
ComposedDirectGroup ==
    IF HasComposedDirectGroup
    THEN CHOOSE g \in DirectGroups : TRUE
    ELSE CHOOSE g \in Groups : TRUE
HasForeignDirectGroup == Cardinality(DirectGroups) > 1
\* The model tracks one composed direct group and at most one valid foreign
\* owner receipt for the focused direct-vs-direct isolation control.
ForeignDirectGroup ==
    IF HasForeignDirectGroup
    THEN CHOOSE g \in DirectGroups \ {ComposedDirectGroup} : TRUE
    ELSE ComposedDirectGroup

ASSUME
    /\ ComposedDirectGroup # NoGroupIdentity
    /\ ForeignDirectGroup # NoGroupIdentity
    /\ {"Succeeded", "Failed"} # {}
    /\ "None" \notin {"Succeeded", "Failed"}

WorkspaceStates == {"Open", "Closing", "Closed"}
BuildStates == {"NotStarted", "InFlight", "Finished"}
BuildOutcomes == {"None", "Group", "Failed", "Canceled"}
GroupStates ==
    {"Absent", "Published", "ReleaseOnly", "ReleaseRequested", "Released"}
ReleaseOwners == {"None", "Workspace", "Completion"}
CleanupOutcomes == {"None", "Succeeded", "Failed"}
ReportEntries == {"None", "Succeeded", "Failed"}

VARIABLES
    workspaceState,
    buildState,
    buildOutcome,
    registeredGroup,
    groupState,
    leaseCount,
    ownerReleaseRequested,
    groupBusy,
    releaseOwner,
    releaseStarts,
    cleanupOutcome,
    directRequestedGroup,
    directCompletedGroup,
    directCompletionResult,
    foreignRequestedGroup,
    foreignCompletedGroup,
    foreignCompletionResult,
    reportEntry,
    lateGroup,
    disposedWithLease,
    buildAdmissionWitness,
    leaseAdmissionWitness,
    authorityWitness,
    leaseDrainWitness,
    lateRoutingWitness,
    groupQuiescenceWitness,
    workspaceCompletionWitness,
    cleanupVisibilityWitness,
    directReleaseObserved,
    coordinatedDrainObserved,
    ownerFirstReleaseObserved,
    postCloseLeaseWorkObserved,
    noGroupCompletionObserved,
    lateCleanupObserved,
    cleanupFailureObserved

vars == <<
    workspaceState, buildState, buildOutcome, registeredGroup, groupState,
    leaseCount, ownerReleaseRequested, groupBusy, releaseOwner, releaseStarts,
    cleanupOutcome, directRequestedGroup, directCompletedGroup,
    directCompletionResult, foreignRequestedGroup, foreignCompletedGroup,
    foreignCompletionResult, reportEntry, lateGroup,
    disposedWithLease, buildAdmissionWitness, leaseAdmissionWitness,
    authorityWitness, leaseDrainWitness, lateRoutingWitness,
    groupQuiescenceWitness, workspaceCompletionWitness,
    cleanupVisibilityWitness, directReleaseObserved,
    coordinatedDrainObserved, ownerFirstReleaseObserved,
    postCloseLeaseWorkObserved, noGroupCompletionObserved,
    lateCleanupObserved, cleanupFailureObserved
    >>

directGroupReleaseVars ==
    <<directRequestedGroup, directCompletedGroup, directCompletionResult>>

foreignDirectGroupReleaseVars ==
    <<foreignRequestedGroup, foreignCompletedGroup, foreignCompletionResult>>

KnownGroups ==
    {g \in Groups : registeredGroup[g]}

ExpectedReleaseOwner(g) ==
    IF g \in CoordinatedGroups THEN "Completion" ELSE "Workspace"

TerminalGroups ==
    \A g \in KnownGroups : groupState[g] = "Released"

CompleteReport ==
    \A g \in Groups :
        IF registeredGroup[g]
        THEN /\ reportEntry[g] # "None"
             /\ reportEntry[g] = cleanupOutcome[g]
        ELSE /\ reportEntry[g] = "None"
             /\ cleanupOutcome[g] = "None"

DirectGroupRelease ==
    INSTANCE AssemblyContextGroupReleaseLifecycle
        WITH Group <- ComposedDirectGroup,
             NoGroup <- NoGroupIdentity,
             ReleaseResults <- {"Succeeded", "Failed"},
             NoReleaseResult <- "None",
             requestedGroup <- directRequestedGroup,
             completedGroup <- directCompletedGroup,
             completionResult <- directCompletionResult

ForeignDirectGroupRelease ==
    INSTANCE AssemblyContextGroupReleaseLifecycle
        WITH Group <- ForeignDirectGroup,
             NoGroup <- NoGroupIdentity,
             ReleaseResults <- {"Succeeded", "Failed"},
             NoReleaseResult <- "None",
             requestedGroup <- foreignRequestedGroup,
             completedGroup <- foreignCompletedGroup,
             completionResult <- foreignCompletionResult

TypeOK ==
    /\ workspaceState \in WorkspaceStates
    /\ buildState \in [Groups -> BuildStates]
    /\ buildOutcome \in [Groups -> BuildOutcomes]
    /\ registeredGroup \in [Groups -> BOOLEAN]
    /\ groupState \in [Groups -> GroupStates]
    /\ leaseCount \in [Groups -> 0..MaxLeases]
    /\ ownerReleaseRequested \in [Groups -> BOOLEAN]
    /\ groupBusy \in [Groups -> BOOLEAN]
    /\ releaseOwner \in [Groups -> ReleaseOwners]
    /\ releaseStarts \in [Groups -> Nat]
    /\ cleanupOutcome \in [Groups -> CleanupOutcomes]
    /\ directRequestedGroup \in {ComposedDirectGroup, NoGroupIdentity}
    /\ directCompletedGroup \in Groups \union {NoGroupIdentity}
    /\ directCompletionResult \in CleanupOutcomes
    /\ foreignRequestedGroup \in {ForeignDirectGroup, NoGroupIdentity}
    /\ foreignCompletedGroup \in {ForeignDirectGroup, NoGroupIdentity}
    /\ foreignCompletionResult \in CleanupOutcomes
    /\ reportEntry \in [Groups -> ReportEntries]
    /\ lateGroup \in [Groups -> BOOLEAN]
    /\ disposedWithLease \in [Groups -> BOOLEAN]
    /\ buildAdmissionWitness \in BOOLEAN
    /\ leaseAdmissionWitness \in BOOLEAN
    /\ authorityWitness \in BOOLEAN
    /\ leaseDrainWitness \in BOOLEAN
    /\ lateRoutingWitness \in BOOLEAN
    /\ groupQuiescenceWitness \in BOOLEAN
    /\ workspaceCompletionWitness \in BOOLEAN
    /\ cleanupVisibilityWitness \in BOOLEAN
    /\ directReleaseObserved \in BOOLEAN
    /\ coordinatedDrainObserved \in BOOLEAN
    /\ ownerFirstReleaseObserved \in BOOLEAN
    /\ postCloseLeaseWorkObserved \in BOOLEAN
    /\ noGroupCompletionObserved \in BOOLEAN
    /\ lateCleanupObserved \in BOOLEAN
    /\ cleanupFailureObserved \in BOOLEAN

Init ==
    /\ workspaceState = "Open"
    /\ buildState = [g \in Groups |-> "NotStarted"]
    /\ buildOutcome = [g \in Groups |-> "None"]
    /\ registeredGroup = [g \in Groups |-> FALSE]
    /\ groupState = [g \in Groups |-> "Absent"]
    /\ leaseCount = [g \in Groups |-> 0]
    /\ ownerReleaseRequested = [g \in Groups |-> FALSE]
    /\ groupBusy = [g \in Groups |-> FALSE]
    /\ releaseOwner = [g \in Groups |-> "None"]
    /\ releaseStarts = [g \in Groups |-> 0]
    /\ cleanupOutcome = [g \in Groups |-> "None"]
    /\ directRequestedGroup = NoGroupIdentity
    /\ directCompletedGroup = NoGroupIdentity
    /\ directCompletionResult = "None"
    /\ foreignRequestedGroup = NoGroupIdentity
    /\ foreignCompletedGroup = NoGroupIdentity
    /\ foreignCompletionResult = "None"
    /\ reportEntry = [g \in Groups |-> "None"]
    /\ lateGroup = [g \in Groups |-> FALSE]
    /\ disposedWithLease = [g \in Groups |-> FALSE]
    /\ buildAdmissionWitness = TRUE
    /\ leaseAdmissionWitness = TRUE
    /\ authorityWitness = TRUE
    /\ leaseDrainWitness = TRUE
    /\ lateRoutingWitness = TRUE
    /\ groupQuiescenceWitness = TRUE
    /\ workspaceCompletionWitness = TRUE
    /\ cleanupVisibilityWitness = TRUE
    /\ directReleaseObserved = FALSE
    /\ coordinatedDrainObserved = FALSE
    /\ ownerFirstReleaseObserved = FALSE
    /\ postCloseLeaseWorkObserved = FALSE
    /\ noGroupCompletionObserved = FALSE
    /\ lateCleanupObserved = FALSE
    /\ cleanupFailureObserved = FALSE
    /\ DirectGroupRelease!Init
    /\ ForeignDirectGroupRelease!Init

StartBuild(g) ==
    /\ buildState[g] = "NotStarted"
    /\ \/ workspaceState = "Open"
       \/ AllowAdmissionAfterClose
    /\ buildState' = [buildState EXCEPT ![g] = "InFlight"]
    /\ buildAdmissionWitness' =
        (buildAdmissionWitness /\ workspaceState = "Open")
    /\ UNCHANGED <<
        workspaceState, buildOutcome, registeredGroup, groupState, leaseCount,
        ownerReleaseRequested, groupBusy, releaseOwner, releaseStarts,
        cleanupOutcome, directRequestedGroup, directCompletedGroup,
        directCompletionResult, foreignRequestedGroup, foreignCompletedGroup,
        foreignCompletionResult, reportEntry, lateGroup,
        disposedWithLease, leaseAdmissionWitness, authorityWitness,
        leaseDrainWitness, lateRoutingWitness, groupQuiescenceWitness,
        workspaceCompletionWitness, cleanupVisibilityWitness,
        directReleaseObserved, coordinatedDrainObserved,
        ownerFirstReleaseObserved, postCloseLeaseWorkObserved,
        noGroupCompletionObserved, lateCleanupObserved, cleanupFailureObserved
        >>

CompleteBuild(g) ==
    /\ buildState[g] = "InFlight"
    /\ buildOutcome[g] = "None"
    /\ LET nextState ==
            IF workspaceState = "Open" \/ AllowLatePublication
            THEN "Published"
            ELSE "ReleaseOnly"
       IN
        /\ buildState' = [buildState EXCEPT ![g] = "Finished"]
        /\ buildOutcome' = [buildOutcome EXCEPT ![g] = "Group"]
        /\ registeredGroup' = [registeredGroup EXCEPT ![g] = TRUE]
        /\ groupState' = [groupState EXCEPT ![g] = nextState]
        /\ lateGroup' =
            [lateGroup EXCEPT ![g] = workspaceState # "Open"]
        /\ lateRoutingWitness' =
            (lateRoutingWitness
             /\ (workspaceState = "Open" \/ nextState = "ReleaseOnly"))
    /\ UNCHANGED <<
        workspaceState, leaseCount, ownerReleaseRequested, groupBusy,
        releaseOwner, releaseStarts, cleanupOutcome, directRequestedGroup,
        directCompletedGroup, directCompletionResult, foreignRequestedGroup,
        foreignCompletedGroup, foreignCompletionResult, reportEntry,
        disposedWithLease,
        buildAdmissionWitness, leaseAdmissionWitness, authorityWitness,
        leaseDrainWitness, groupQuiescenceWitness,
        workspaceCompletionWitness, cleanupVisibilityWitness,
        directReleaseObserved, coordinatedDrainObserved,
        ownerFirstReleaseObserved, postCloseLeaseWorkObserved,
        noGroupCompletionObserved, lateCleanupObserved, cleanupFailureObserved
        >>

CompleteBuildWithoutGroup(g, outcome) ==
    /\ outcome \in {"Failed", "Canceled"}
    /\ buildState[g] = "InFlight"
    /\ buildOutcome[g] = "None"
    /\ buildState' =
        [buildState EXCEPT
            ![g] = IF AllowStrandedNoGroupCompletion
                    THEN "InFlight"
                    ELSE "Finished"]
    /\ buildOutcome' = [buildOutcome EXCEPT ![g] = outcome]
    /\ cleanupOutcome' =
        [cleanupOutcome EXCEPT
            ![g] = IF AllowNoGroupCleanupEntry THEN "Failed" ELSE @]
    /\ reportEntry' =
        [reportEntry EXCEPT
            ![g] = IF AllowNoGroupCleanupEntry THEN "Failed" ELSE @]
    /\ noGroupCompletionObserved' =
        (noGroupCompletionObserved
         \/ (workspaceState # "Open" /\ ~AllowStrandedNoGroupCompletion))
    /\ UNCHANGED <<
        workspaceState, registeredGroup, groupState, leaseCount,
        ownerReleaseRequested, groupBusy, releaseOwner, releaseStarts,
        directRequestedGroup, directCompletedGroup, directCompletionResult,
        foreignRequestedGroup, foreignCompletedGroup, foreignCompletionResult,
        lateGroup,
        disposedWithLease, buildAdmissionWitness, leaseAdmissionWitness,
        authorityWitness, leaseDrainWitness, lateRoutingWitness,
        groupQuiescenceWitness, workspaceCompletionWitness,
        cleanupVisibilityWitness, directReleaseObserved,
        coordinatedDrainObserved, ownerFirstReleaseObserved,
        postCloseLeaseWorkObserved, lateCleanupObserved,
        cleanupFailureObserved
        >>

AcquireLease(g) ==
    /\ g \in CoordinatedGroups
    /\ groupState[g] = "Published"
    /\ ~ownerReleaseRequested[g]
    /\ leaseCount[g] < MaxLeases
    /\ \/ workspaceState = "Open"
       \/ AllowLeaseAfterClose
    /\ leaseCount' = [leaseCount EXCEPT ![g] = @ + 1]
    /\ leaseAdmissionWitness' =
        (leaseAdmissionWitness /\ workspaceState = "Open")
    /\ UNCHANGED <<
        workspaceState, buildState, buildOutcome, registeredGroup, groupState,
        ownerReleaseRequested, groupBusy, releaseOwner, releaseStarts,
        cleanupOutcome, directRequestedGroup, directCompletedGroup,
        directCompletionResult, foreignRequestedGroup, foreignCompletedGroup,
        foreignCompletionResult, reportEntry, lateGroup,
        disposedWithLease, buildAdmissionWitness, authorityWitness,
        leaseDrainWitness, lateRoutingWitness, groupQuiescenceWitness,
        workspaceCompletionWitness, cleanupVisibilityWitness,
        directReleaseObserved, coordinatedDrainObserved,
        ownerFirstReleaseObserved, postCloseLeaseWorkObserved,
        noGroupCompletionObserved, lateCleanupObserved, cleanupFailureObserved
        >>

ReturnLease(g) ==
    /\ g \in CoordinatedGroups
    /\ leaseCount[g] > 0
    /\ leaseCount' = [leaseCount EXCEPT ![g] = @ - 1]
    /\ UNCHANGED <<
        workspaceState, buildState, buildOutcome, registeredGroup, groupState,
        ownerReleaseRequested, groupBusy, releaseOwner, releaseStarts,
        cleanupOutcome, directRequestedGroup, directCompletedGroup,
        directCompletionResult, foreignRequestedGroup, foreignCompletedGroup,
        foreignCompletionResult, reportEntry, lateGroup,
        disposedWithLease, buildAdmissionWitness, leaseAdmissionWitness,
        authorityWitness, leaseDrainWitness, lateRoutingWitness,
        groupQuiescenceWitness, workspaceCompletionWitness,
        cleanupVisibilityWitness, directReleaseObserved,
        coordinatedDrainObserved,
        ownerFirstReleaseObserved, postCloseLeaseWorkObserved,
        noGroupCompletionObserved, lateCleanupObserved, cleanupFailureObserved
        >>

BeginGroupWork(g) ==
    /\ groupState[g] = "Published"
    /\ \/ /\ workspaceState = "Open"
          /\ ~ownerReleaseRequested[g]
       \/ /\ g \in CoordinatedGroups
          /\ leaseCount[g] > 0
    /\ ~groupBusy[g]
    /\ groupBusy' = [groupBusy EXCEPT ![g] = TRUE]
    /\ postCloseLeaseWorkObserved' =
        (postCloseLeaseWorkObserved \/ workspaceState = "Closing")
    /\ UNCHANGED <<
        workspaceState, buildState, buildOutcome, registeredGroup, groupState,
        leaseCount, ownerReleaseRequested, releaseOwner, releaseStarts,
        cleanupOutcome, directRequestedGroup, directCompletedGroup,
        directCompletionResult, foreignRequestedGroup, foreignCompletedGroup,
        foreignCompletionResult, reportEntry, lateGroup,
        disposedWithLease, buildAdmissionWitness, leaseAdmissionWitness,
        authorityWitness, leaseDrainWitness, lateRoutingWitness,
        groupQuiescenceWitness, workspaceCompletionWitness,
        cleanupVisibilityWitness, directReleaseObserved,
        coordinatedDrainObserved,
        ownerFirstReleaseObserved, noGroupCompletionObserved,
        lateCleanupObserved, cleanupFailureObserved
        >>

EndGroupWork(g) ==
    /\ groupBusy[g]
    /\ groupBusy' = [groupBusy EXCEPT ![g] = FALSE]
    /\ UNCHANGED <<
        workspaceState, buildState, buildOutcome, registeredGroup, groupState,
        leaseCount, ownerReleaseRequested, releaseOwner, releaseStarts,
        cleanupOutcome, directRequestedGroup, directCompletedGroup,
        directCompletionResult, foreignRequestedGroup, foreignCompletedGroup,
        foreignCompletionResult, reportEntry, lateGroup,
        disposedWithLease, buildAdmissionWitness, leaseAdmissionWitness,
        authorityWitness, leaseDrainWitness, lateRoutingWitness,
        groupQuiescenceWitness, workspaceCompletionWitness,
        cleanupVisibilityWitness, directReleaseObserved,
        coordinatedDrainObserved,
        ownerFirstReleaseObserved, postCloseLeaseWorkObserved,
        noGroupCompletionObserved, lateCleanupObserved, cleanupFailureObserved
        >>

CloseWorkspace ==
    /\ workspaceState = "Open"
    /\ workspaceState' = "Closing"
    /\ disposedWithLease' =
        [g \in Groups |-> leaseCount[g] > 0]
    /\ UNCHANGED <<
        buildState, buildOutcome, registeredGroup, groupState, leaseCount,
        ownerReleaseRequested, groupBusy, releaseOwner, releaseStarts,
        cleanupOutcome, directRequestedGroup, directCompletedGroup,
        directCompletionResult, foreignRequestedGroup, foreignCompletedGroup,
        foreignCompletionResult, reportEntry, lateGroup,
        buildAdmissionWitness, leaseAdmissionWitness, authorityWitness,
        leaseDrainWitness, lateRoutingWitness, groupQuiescenceWitness,
        workspaceCompletionWitness, cleanupVisibilityWitness,
        directReleaseObserved, coordinatedDrainObserved,
        ownerFirstReleaseObserved, postCloseLeaseWorkObserved,
        noGroupCompletionObserved, lateCleanupObserved, cleanupFailureObserved
        >>

RequestDirectRelease(g) ==
    /\ g \in DirectGroups
    /\ workspaceState = "Closing"
    /\ groupState[g] \in {"Published", "ReleaseOnly"}
    /\ LET owner ==
            IF AllowWrongDirectReleaseAuthority
            THEN "Completion"
            ELSE "Workspace"
       IN
        /\ groupState' =
            [groupState EXCEPT ![g] = "ReleaseRequested"]
        /\ releaseOwner' = [releaseOwner EXCEPT ![g] = owner]
        /\ authorityWitness' =
            (authorityWitness /\ owner = ExpectedReleaseOwner(g))
    /\ releaseStarts' = [releaseStarts EXCEPT ![g] = @ + 1]
    /\ IF HasComposedDirectGroup /\ g = ComposedDirectGroup
       THEN DirectGroupRelease!RequestRelease
       ELSE UNCHANGED directGroupReleaseVars
    /\ IF HasForeignDirectGroup /\ g = ForeignDirectGroup
       THEN ForeignDirectGroupRelease!RequestRelease
       ELSE UNCHANGED foreignDirectGroupReleaseVars
    /\ UNCHANGED <<
        workspaceState, buildState, buildOutcome, registeredGroup, leaseCount,
        ownerReleaseRequested, groupBusy, cleanupOutcome, reportEntry,
        lateGroup, disposedWithLease, buildAdmissionWitness,
        leaseAdmissionWitness, leaseDrainWitness, lateRoutingWitness,
        groupQuiescenceWitness, workspaceCompletionWitness,
        cleanupVisibilityWitness, directReleaseObserved,
        coordinatedDrainObserved, ownerFirstReleaseObserved,
        postCloseLeaseWorkObserved, noGroupCompletionObserved,
        lateCleanupObserved, cleanupFailureObserved
        >>

RequestOwnerRelease(g) ==
    \* External owner input is intentionally not fair: owner-first close is
    \* optional, while workspace-triggered processing below must make progress.
    /\ g \in CoordinatedGroups
    /\ workspaceState \in {"Open", "Closing"}
    /\ groupState[g] = "Published"
    /\ ~ownerReleaseRequested[g]
    /\ ownerReleaseRequested' =
        [ownerReleaseRequested EXCEPT ![g] = TRUE]
    /\ UNCHANGED <<
        workspaceState, buildState, buildOutcome, registeredGroup, groupState,
        leaseCount, groupBusy, releaseOwner, releaseStarts, cleanupOutcome,
        directRequestedGroup, directCompletedGroup, directCompletionResult,
        foreignRequestedGroup, foreignCompletedGroup, foreignCompletionResult,
        reportEntry, lateGroup, disposedWithLease, buildAdmissionWitness,
        leaseAdmissionWitness, authorityWitness, leaseDrainWitness,
        lateRoutingWitness, groupQuiescenceWitness,
        workspaceCompletionWitness, cleanupVisibilityWitness,
        directReleaseObserved, coordinatedDrainObserved,
        ownerFirstReleaseObserved, postCloseLeaseWorkObserved,
        noGroupCompletionObserved, lateCleanupObserved,
        cleanupFailureObserved
        >>

ProcessCoordinatedRelease(g) ==
    /\ g \in CoordinatedGroups
    /\ \/ workspaceState = "Closing"
       \/ ownerReleaseRequested[g]
    /\ groupState[g] \in {"Published", "ReleaseOnly"}
    /\ \/ leaseCount[g] = 0
       \/ AllowReleaseWithActiveLease
    /\ LET owner ==
            IF AllowWrongCoordinatedReleaseAuthority
            THEN "Workspace"
            ELSE "Completion"
       IN
        /\ groupState' =
            [groupState EXCEPT ![g] = "ReleaseRequested"]
        /\ registeredGroup' =
            [registeredGroup EXCEPT
                ![g] = IF AllowOwnerFirstHistoryLoss
                        /\ workspaceState = "Open"
                        THEN FALSE
                        ELSE @]
        /\ releaseOwner' = [releaseOwner EXCEPT ![g] = owner]
        /\ authorityWitness' =
            (authorityWitness /\ owner = ExpectedReleaseOwner(g))
    /\ releaseStarts' = [releaseStarts EXCEPT ![g] = @ + 1]
    /\ leaseDrainWitness' =
        (leaseDrainWitness /\ leaseCount[g] = 0)
    /\ coordinatedDrainObserved' =
        (coordinatedDrainObserved
         \/ (disposedWithLease[g] /\ leaseCount[g] = 0))
    /\ ownerFirstReleaseObserved' =
        (ownerFirstReleaseObserved \/ workspaceState = "Open")
    /\ UNCHANGED <<
        workspaceState, buildState, buildOutcome, leaseCount,
        ownerReleaseRequested, groupBusy, cleanupOutcome,
        directRequestedGroup, directCompletedGroup, directCompletionResult,
        foreignRequestedGroup, foreignCompletedGroup, foreignCompletionResult,
        reportEntry,
        lateGroup, disposedWithLease, buildAdmissionWitness,
        leaseAdmissionWitness, lateRoutingWitness, groupQuiescenceWitness,
        workspaceCompletionWitness, cleanupVisibilityWitness,
        directReleaseObserved, postCloseLeaseWorkObserved,
        noGroupCompletionObserved, lateCleanupObserved,
        cleanupFailureObserved
        >>

CompleteReleaseCore(g, outcome) ==
    /\ outcome \in {"Succeeded", "Failed"}
    /\ groupState[g] = "ReleaseRequested"
    /\ \/ ~groupBusy[g]
       \/ AllowReleaseBeforeGroupQuiescence
    /\ groupState' = [groupState EXCEPT ![g] = "Released"]
    /\ cleanupOutcome' = [cleanupOutcome EXCEPT ![g] = outcome]
    /\ groupQuiescenceWitness' =
        (groupQuiescenceWitness /\ ~groupBusy[g])
    /\ directReleaseObserved' =
        (directReleaseObserved \/ g \in DirectGroups)
    /\ lateCleanupObserved' =
        (lateCleanupObserved \/ lateGroup[g])
    /\ cleanupFailureObserved' =
        (cleanupFailureObserved \/ outcome = "Failed")
    /\ UNCHANGED <<
        workspaceState, buildState, buildOutcome, registeredGroup, leaseCount,
        ownerReleaseRequested, groupBusy, releaseOwner, releaseStarts,
        reportEntry, lateGroup, disposedWithLease,
        buildAdmissionWitness, leaseAdmissionWitness, authorityWitness,
        leaseDrainWitness, lateRoutingWitness, workspaceCompletionWitness,
        cleanupVisibilityWitness, coordinatedDrainObserved,
        ownerFirstReleaseObserved, postCloseLeaseWorkObserved,
        noGroupCompletionObserved
        >>

CompleteRelease(g, outcome) ==
    /\ CompleteReleaseCore(g, outcome)
    /\ IF HasComposedDirectGroup /\ g = ComposedDirectGroup
       THEN DirectGroupRelease!CompleteRelease(
            outcome,
            ~groupBusy[ComposedDirectGroup]
                \/ AllowReleaseBeforeGroupQuiescence)
       ELSE UNCHANGED directGroupReleaseVars
    /\ IF HasForeignDirectGroup /\ g = ForeignDirectGroup
       THEN ForeignDirectGroupRelease!CompleteRelease(
            outcome,
            ~groupBusy[ForeignDirectGroup]
                \/ AllowReleaseBeforeGroupQuiescence)
       ELSE UNCHANGED foreignDirectGroupReleaseVars

CompleteAnyRelease(g) ==
    \/ CompleteRelease(g, "Succeeded")
    \/ CompleteRelease(g, "Failed")

CompleteForeignDirectReceipt ==
    /\ HasForeignDirectGroup
    /\ foreignCompletedGroup = ForeignDirectGroup
    /\ CompleteReleaseCore(
        ComposedDirectGroup,
        foreignCompletionResult)
    /\ directCompletedGroup' = ForeignDirectGroup
    /\ directCompletionResult' = foreignCompletionResult
    /\ UNCHANGED
        <<directRequestedGroup, foreignRequestedGroup,
          foreignCompletedGroup, foreignCompletionResult>>

CompleteMismatchedDirectResult ==
    /\ HasComposedDirectGroup
    /\ \E outcome \in {"Succeeded", "Failed"} :
        /\ CompleteReleaseCore(ComposedDirectGroup, outcome)
        /\ directCompletedGroup' = ComposedDirectGroup
        /\ directCompletionResult' =
            IF outcome = "Succeeded" THEN "Failed" ELSE "Succeeded"
        /\ UNCHANGED
            <<directRequestedGroup, foreignRequestedGroup,
              foreignCompletedGroup, foreignCompletionResult>>

RecordReport(g) ==
    /\ groupState[g] = "Released"
    /\ cleanupOutcome[g] # "None"
    /\ reportEntry[g] = "None"
    /\ reportEntry' =
        [reportEntry EXCEPT ![g] = cleanupOutcome[g]]
    /\ UNCHANGED <<
        workspaceState, buildState, buildOutcome, registeredGroup, groupState,
        leaseCount, ownerReleaseRequested, groupBusy, releaseOwner,
        releaseStarts, cleanupOutcome, directRequestedGroup,
        directCompletedGroup, directCompletionResult, foreignRequestedGroup,
        foreignCompletedGroup, foreignCompletionResult, lateGroup,
        disposedWithLease, buildAdmissionWitness, leaseAdmissionWitness,
        authorityWitness, leaseDrainWitness, lateRoutingWitness,
        groupQuiescenceWitness, workspaceCompletionWitness,
        cleanupVisibilityWitness, directReleaseObserved,
        coordinatedDrainObserved, ownerFirstReleaseObserved,
        postCloseLeaseWorkObserved, noGroupCompletionObserved,
        lateCleanupObserved, cleanupFailureObserved
        >>

FinalizeWorkspace ==
    /\ workspaceState = "Closing"
    /\ \A g \in Groups : buildState[g] # "InFlight"
    /\ \/ AllowEarlyWorkspaceCompletion
       \/ /\ TerminalGroups
          /\ \/ CompleteReport
             \/ AllowCleanupOmission
    /\ workspaceState' = "Closed"
    /\ workspaceCompletionWitness' =
        (workspaceCompletionWitness /\ TerminalGroups)
    /\ cleanupVisibilityWitness' =
        (cleanupVisibilityWitness /\ CompleteReport)
    /\ UNCHANGED <<
        buildState, buildOutcome, registeredGroup, groupState, leaseCount,
        ownerReleaseRequested, groupBusy, releaseOwner, releaseStarts,
        cleanupOutcome, directRequestedGroup, directCompletedGroup,
        directCompletionResult, foreignRequestedGroup, foreignCompletedGroup,
        foreignCompletionResult, reportEntry, lateGroup,
        disposedWithLease, buildAdmissionWitness, leaseAdmissionWitness,
        authorityWitness, leaseDrainWitness, lateRoutingWitness,
        groupQuiescenceWitness, directReleaseObserved,
        coordinatedDrainObserved, ownerFirstReleaseObserved,
        postCloseLeaseWorkObserved, noGroupCompletionObserved,
        lateCleanupObserved, cleanupFailureObserved
        >>

RepeatRelease(g) ==
    /\ AllowDoubleRelease
    /\ groupState[g] = "Released"
    /\ releaseStarts' = [releaseStarts EXCEPT ![g] = @ + 1]
    /\ UNCHANGED <<
        workspaceState, buildState, buildOutcome, registeredGroup, groupState,
        leaseCount, ownerReleaseRequested, groupBusy, releaseOwner,
        cleanupOutcome, directRequestedGroup, directCompletedGroup,
        directCompletionResult, foreignRequestedGroup, foreignCompletedGroup,
        foreignCompletionResult, reportEntry, lateGroup,
        disposedWithLease, buildAdmissionWitness, leaseAdmissionWitness,
        authorityWitness, leaseDrainWitness, lateRoutingWitness,
        groupQuiescenceWitness, workspaceCompletionWitness,
        cleanupVisibilityWitness, directReleaseObserved,
        coordinatedDrainObserved, ownerFirstReleaseObserved,
        postCloseLeaseWorkObserved, noGroupCompletionObserved,
        lateCleanupObserved, cleanupFailureObserved
        >>

Next ==
    \/ \E g \in Groups : StartBuild(g)
    \/ \E g \in Groups : CompleteBuild(g)
    \/ \E g \in Groups :
        \E outcome \in {"Failed", "Canceled"} :
            CompleteBuildWithoutGroup(g, outcome)
    \/ \E g \in Groups : AcquireLease(g)
    \/ \E g \in Groups : ReturnLease(g)
    \/ \E g \in Groups : BeginGroupWork(g)
    \/ \E g \in Groups : EndGroupWork(g)
    \/ CloseWorkspace
    \/ \E g \in Groups : RequestDirectRelease(g)
    \/ \E g \in Groups : RequestOwnerRelease(g)
    \/ \E g \in Groups : ProcessCoordinatedRelease(g)
    \/ \E g \in Groups : CompleteAnyRelease(g)
    \/ \E g \in Groups : RecordReport(g)
    \/ FinalizeWorkspace
    \/ \E g \in Groups : RepeatRelease(g)

Fairness ==
    /\ \A g \in Groups : WF_vars(CompleteBuild(g))
    /\ \A g \in Groups :
        \A outcome \in {"Failed", "Canceled"} :
            WF_vars(CompleteBuildWithoutGroup(g, outcome))
    /\ \A g \in Groups : WF_vars(ReturnLease(g))
    /\ \A g \in Groups : WF_vars(EndGroupWork(g))
    /\ \A g \in Groups : WF_vars(RequestDirectRelease(g))
    /\ \A g \in Groups : WF_vars(ProcessCoordinatedRelease(g))
    /\ \A g \in Groups : WF_vars(CompleteAnyRelease(g))
    /\ \A g \in Groups : WF_vars(RecordReport(g))
    /\ WF_vars(FinalizeWorkspace)

Spec == Init /\ [][Next]_vars /\ Fairness

ForeignReceiptSpec ==
    Init
    /\ [][Next \/ CompleteForeignDirectReceipt]_vars
    /\ Fairness

MismatchedResultSpec ==
    Init
    /\ [][Next \/ CompleteMismatchedDirectResult]_vars
    /\ Fairness

(***************************************************************************)
(* Safety properties. Witnesses preserve each action's required pre-state   *)
(* so a mutation cannot make its own weakened guard vacuously authoritative.*)
(***************************************************************************)
NoBuildAdmissionAfterClose == buildAdmissionWitness
NoLeaseAdmissionAfterClose == leaseAdmissionWitness
ReleaseUsesSingleOwner == authorityWitness
CoordinatedReleaseWaitsForLeases == leaseDrainWitness
LateCompletionRoutesToCleanup == lateRoutingWitness
ReleaseWaitsForGroupQuiescence == groupQuiescenceWitness
WorkspaceCloseWaitsForQuiescence == workspaceCompletionWitness
CleanupFailuresRemainVisible == cleanupVisibilityWitness

DirectReceiptMatchesRegistration ==
    ~HasComposedDirectGroup
        \/ (directCompletedGroup # NoGroupIdentity
            => directCompletedGroup = ComposedDirectGroup)

DirectReceiptResultMatchesReportSource ==
    ~HasComposedDirectGroup
        \/ (directCompletedGroup = ComposedDirectGroup
            => cleanupOutcome[ComposedDirectGroup] = directCompletionResult)

DirectGroupReleaseBehaviorRefinesOwner ==
    DirectGroupRelease!SafetySpec(~groupBusy[ComposedDirectGroup])

DirectGroupReleaseCompletionCarriesResult ==
    DirectGroupRelease!CompletionCarriesResult

DirectGroupReleaseCompletionMatchesRequest ==
    DirectGroupRelease!CompletionMatchesRequest

ForeignDirectGroupReleaseCompletionMatchesRequest ==
    ForeignDirectGroupRelease!CompletionMatchesRequest

ForeignDirectGroupReleaseCompletionCarriesResult ==
    ForeignDirectGroupRelease!CompletionCarriesResult

ForeignDirectGroupReleaseBehaviorRefinesOwner ==
    ForeignDirectGroupRelease!SafetySpec(~groupBusy[ForeignDirectGroup])

ReleaseBeginsAtMostOnce ==
    \A g \in Groups : releaseStarts[g] <= 1

ActiveLeasesPreventRelease ==
    \A g \in CoordinatedGroups :
        leaseCount[g] > 0 => groupState[g] # "Released"

RegistrationHistoryMatchesBuildOutcome ==
    \A g \in Groups :
        registeredGroup[g] = (buildOutcome[g] = "Group")

NoCleanupWithoutRegisteredGroup ==
    \A g \in Groups :
        ~registeredGroup[g]
            => /\ cleanupOutcome[g] = "None"
               /\ reportEntry[g] = "None"

ClosedWorkspaceIsDrained ==
    workspaceState = "Closed"
        => /\ \A g \in Groups : buildState[g] # "InFlight"
           /\ TerminalGroups
           /\ CompleteReport

(***************************************************************************)
(* Liveness properties. The model does not require StartBuild or            *)
(* CloseWorkspace to occur; once those operations start, weak fairness      *)
(* requires their owner-visible completion.                                 *)
(***************************************************************************)
EveryStartedBuildFinishes ==
    \A g \in Groups :
        buildState[g] = "InFlight" ~> buildState[g] = "Finished"

EveryRequestedReleaseCompletes ==
    \A g \in Groups :
        groupState[g] = "ReleaseRequested" ~> groupState[g] = "Released"

ClosingWorkspaceEventuallyCloses ==
    workspaceState = "Closing" ~> workspaceState = "Closed"

ComposedDirectReleaseEventuallyCompletes ==
    ~HasComposedDirectGroup
        \/ DirectGroupRelease!RequestedGroupEventuallyCompletes

(***************************************************************************)
(* Reachability probes. Configurations negate these observations so TLC     *)
(* emits a trace when the intended path is reachable.                       *)
(***************************************************************************)
NoDirectReleaseObserved == ~directReleaseObserved
NoCoordinatedDrainObserved == ~coordinatedDrainObserved
NoOwnerFirstReleaseObserved == ~ownerFirstReleaseObserved
NoPostCloseLeaseWorkObserved == ~postCloseLeaseWorkObserved
NoNoGroupCompletionObserved == ~noGroupCompletionObserved
NoLateCleanupObserved == ~lateCleanupObserved
NoCleanupFailureObserved == ~cleanupFailureObserved

=============================================================================
