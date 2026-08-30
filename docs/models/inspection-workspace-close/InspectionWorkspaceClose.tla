--------------------- MODULE InspectionWorkspaceClose ---------------------
(***************************************************************************)
(* Models the TARGET workspace-level close contract in                     *)
(* docs/inspection-space.md, "Workspace close and group release authority". *)
(*                                                                         *)
(* The model owns workspace admission, publication versus late-result      *)
(* routing, direct versus coordinated release authority, and asynchronous  *)
(* close completion. It abstracts the package-role lease owner and the     *)
(* AssemblyContextGroup callback/resource protocol as adjacent components. *)
(* Coordinated lease return is therefore an external authorization event,  *)
(* while groupBusy represents the group owner's internal quiescence.        *)
(*                                                                         *)
(* THIS MODEL DOES NOT CLAIM CURRENT PRODUCT BEHAVIOR. The current          *)
(* InspectionWorkspace synchronously disposes raw registered groups and    *)
(* cannot await coordinated release or route a late construction result    *)
(* into one shared cleanup completion.                                     *)
(***************************************************************************)
EXTENDS FiniteSets, Naturals, TLC

CONSTANTS
    Groups,
    CoordinatedGroups,
    MaxLeases,
    AllowAdmissionAfterClose,
    AllowLeaseAfterClose,
    AllowWrongReleaseAuthority,
    AllowReleaseWithActiveLease,
    AllowReleaseBeforeGroupQuiescence,
    AllowLatePublication,
    AllowEarlyWorkspaceCompletion,
    AllowCleanupOmission,
    AllowDoubleRelease

ASSUME
    /\ Groups # {}
    /\ IsFiniteSet(Groups)
    /\ CoordinatedGroups \subseteq Groups
    /\ MaxLeases \in Nat \ {0}
    /\ AllowAdmissionAfterClose \in BOOLEAN
    /\ AllowLeaseAfterClose \in BOOLEAN
    /\ AllowWrongReleaseAuthority \in BOOLEAN
    /\ AllowReleaseWithActiveLease \in BOOLEAN
    /\ AllowReleaseBeforeGroupQuiescence \in BOOLEAN
    /\ AllowLatePublication \in BOOLEAN
    /\ AllowEarlyWorkspaceCompletion \in BOOLEAN
    /\ AllowCleanupOmission \in BOOLEAN
    /\ AllowDoubleRelease \in BOOLEAN

DirectGroups == Groups \ CoordinatedGroups

WorkspaceStates == {"Open", "Closing", "Closed"}
BuildStates == {"NotStarted", "InFlight", "Finished"}
GroupStates ==
    {"Absent", "Published", "ReleaseOnly", "ReleaseRequested", "Released"}
ReleaseOwners == {"None", "Workspace", "Completion"}
CleanupOutcomes == {"None", "Succeeded", "Failed"}
ReportEntries == {"None", "Succeeded", "Failed"}

VARIABLES
    workspaceState,
    buildState,
    groupState,
    leaseCount,
    groupBusy,
    releaseOwner,
    releaseStarts,
    cleanupOutcome,
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
    lateCleanupObserved,
    cleanupFailureObserved

vars == <<
    workspaceState, buildState, groupState, leaseCount, groupBusy,
    releaseOwner, releaseStarts, cleanupOutcome, reportEntry, lateGroup,
    disposedWithLease, buildAdmissionWitness, leaseAdmissionWitness,
    authorityWitness, leaseDrainWitness, lateRoutingWitness,
    groupQuiescenceWitness, workspaceCompletionWitness,
    cleanupVisibilityWitness, directReleaseObserved,
    coordinatedDrainObserved, lateCleanupObserved, cleanupFailureObserved
    >>

KnownGroups ==
    {g \in Groups : buildState[g] = "Finished"
        /\ groupState[g] # "Absent"}

ExpectedReleaseOwner(g) ==
    IF g \in CoordinatedGroups THEN "Completion" ELSE "Workspace"

TerminalGroups ==
    \A g \in KnownGroups : groupState[g] = "Released"

CompleteReport ==
    \A g \in KnownGroups :
        /\ reportEntry[g] # "None"
        /\ reportEntry[g] = cleanupOutcome[g]

TypeOK ==
    /\ workspaceState \in WorkspaceStates
    /\ buildState \in [Groups -> BuildStates]
    /\ groupState \in [Groups -> GroupStates]
    /\ leaseCount \in [Groups -> 0..MaxLeases]
    /\ groupBusy \in [Groups -> BOOLEAN]
    /\ releaseOwner \in [Groups -> ReleaseOwners]
    /\ releaseStarts \in [Groups -> Nat]
    /\ cleanupOutcome \in [Groups -> CleanupOutcomes]
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
    /\ lateCleanupObserved \in BOOLEAN
    /\ cleanupFailureObserved \in BOOLEAN

Init ==
    /\ workspaceState = "Open"
    /\ buildState = [g \in Groups |-> "NotStarted"]
    /\ groupState = [g \in Groups |-> "Absent"]
    /\ leaseCount = [g \in Groups |-> 0]
    /\ groupBusy = [g \in Groups |-> FALSE]
    /\ releaseOwner = [g \in Groups |-> "None"]
    /\ releaseStarts = [g \in Groups |-> 0]
    /\ cleanupOutcome = [g \in Groups |-> "None"]
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
    /\ lateCleanupObserved = FALSE
    /\ cleanupFailureObserved = FALSE

StartBuild(g) ==
    /\ buildState[g] = "NotStarted"
    /\ \/ workspaceState = "Open"
       \/ AllowAdmissionAfterClose
    /\ buildState' = [buildState EXCEPT ![g] = "InFlight"]
    /\ buildAdmissionWitness' =
        (buildAdmissionWitness /\ workspaceState = "Open")
    /\ UNCHANGED <<
        workspaceState, groupState, leaseCount, groupBusy, releaseOwner,
        releaseStarts, cleanupOutcome, reportEntry, lateGroup,
        disposedWithLease, leaseAdmissionWitness, authorityWitness,
        leaseDrainWitness, lateRoutingWitness, groupQuiescenceWitness,
        workspaceCompletionWitness, cleanupVisibilityWitness,
        directReleaseObserved, coordinatedDrainObserved, lateCleanupObserved,
        cleanupFailureObserved
        >>

CompleteBuild(g) ==
    /\ buildState[g] = "InFlight"
    /\ LET nextState ==
            IF workspaceState = "Open" \/ AllowLatePublication
            THEN "Published"
            ELSE "ReleaseOnly"
       IN
        /\ buildState' = [buildState EXCEPT ![g] = "Finished"]
        /\ groupState' = [groupState EXCEPT ![g] = nextState]
        /\ lateGroup' =
            [lateGroup EXCEPT ![g] = workspaceState # "Open"]
        /\ lateRoutingWitness' =
            (lateRoutingWitness
             /\ (workspaceState = "Open" \/ nextState = "ReleaseOnly"))
    /\ UNCHANGED <<
        workspaceState, leaseCount, groupBusy, releaseOwner, releaseStarts,
        cleanupOutcome, reportEntry, disposedWithLease,
        buildAdmissionWitness, leaseAdmissionWitness, authorityWitness,
        leaseDrainWitness, groupQuiescenceWitness,
        workspaceCompletionWitness, cleanupVisibilityWitness,
        directReleaseObserved, coordinatedDrainObserved, lateCleanupObserved,
        cleanupFailureObserved
        >>

AcquireLease(g) ==
    /\ g \in CoordinatedGroups
    /\ groupState[g] = "Published"
    /\ leaseCount[g] < MaxLeases
    /\ \/ workspaceState = "Open"
       \/ AllowLeaseAfterClose
    /\ leaseCount' = [leaseCount EXCEPT ![g] = @ + 1]
    /\ leaseAdmissionWitness' =
        (leaseAdmissionWitness /\ workspaceState = "Open")
    /\ UNCHANGED <<
        workspaceState, buildState, groupState, groupBusy, releaseOwner,
        releaseStarts, cleanupOutcome, reportEntry, lateGroup,
        disposedWithLease, buildAdmissionWitness, authorityWitness,
        leaseDrainWitness, lateRoutingWitness, groupQuiescenceWitness,
        workspaceCompletionWitness, cleanupVisibilityWitness,
        directReleaseObserved, coordinatedDrainObserved, lateCleanupObserved,
        cleanupFailureObserved
        >>

ReturnLease(g) ==
    /\ g \in CoordinatedGroups
    /\ leaseCount[g] > 0
    /\ leaseCount' = [leaseCount EXCEPT ![g] = @ - 1]
    /\ UNCHANGED <<
        workspaceState, buildState, groupState, groupBusy, releaseOwner,
        releaseStarts, cleanupOutcome, reportEntry, lateGroup,
        disposedWithLease, buildAdmissionWitness, leaseAdmissionWitness,
        authorityWitness, leaseDrainWitness, lateRoutingWitness,
        groupQuiescenceWitness, workspaceCompletionWitness,
        cleanupVisibilityWitness, directReleaseObserved,
        coordinatedDrainObserved, lateCleanupObserved, cleanupFailureObserved
        >>

BeginGroupWork(g) ==
    /\ groupState[g] = "Published"
    /\ workspaceState = "Open"
    /\ ~groupBusy[g]
    /\ groupBusy' = [groupBusy EXCEPT ![g] = TRUE]
    /\ UNCHANGED <<
        workspaceState, buildState, groupState, leaseCount, releaseOwner,
        releaseStarts, cleanupOutcome, reportEntry, lateGroup,
        disposedWithLease, buildAdmissionWitness, leaseAdmissionWitness,
        authorityWitness, leaseDrainWitness, lateRoutingWitness,
        groupQuiescenceWitness, workspaceCompletionWitness,
        cleanupVisibilityWitness, directReleaseObserved,
        coordinatedDrainObserved, lateCleanupObserved, cleanupFailureObserved
        >>

EndGroupWork(g) ==
    /\ groupBusy[g]
    /\ groupBusy' = [groupBusy EXCEPT ![g] = FALSE]
    /\ UNCHANGED <<
        workspaceState, buildState, groupState, leaseCount, releaseOwner,
        releaseStarts, cleanupOutcome, reportEntry, lateGroup,
        disposedWithLease, buildAdmissionWitness, leaseAdmissionWitness,
        authorityWitness, leaseDrainWitness, lateRoutingWitness,
        groupQuiescenceWitness, workspaceCompletionWitness,
        cleanupVisibilityWitness, directReleaseObserved,
        coordinatedDrainObserved, lateCleanupObserved, cleanupFailureObserved
        >>

CloseWorkspace ==
    /\ workspaceState = "Open"
    /\ workspaceState' = "Closing"
    /\ disposedWithLease' =
        [g \in Groups |-> leaseCount[g] > 0]
    /\ UNCHANGED <<
        buildState, groupState, leaseCount, groupBusy, releaseOwner,
        releaseStarts, cleanupOutcome, reportEntry, lateGroup,
        buildAdmissionWitness, leaseAdmissionWitness, authorityWitness,
        leaseDrainWitness, lateRoutingWitness, groupQuiescenceWitness,
        workspaceCompletionWitness, cleanupVisibilityWitness,
        directReleaseObserved, coordinatedDrainObserved, lateCleanupObserved,
        cleanupFailureObserved
        >>

RequestDirectRelease(g) ==
    /\ g \in DirectGroups
    /\ workspaceState = "Closing"
    /\ groupState[g] \in {"Published", "ReleaseOnly"}
    /\ groupState' = [groupState EXCEPT ![g] = "ReleaseRequested"]
    /\ releaseOwner' = [releaseOwner EXCEPT ![g] = "Workspace"]
    /\ releaseStarts' = [releaseStarts EXCEPT ![g] = @ + 1]
    /\ authorityWitness' =
        (authorityWitness /\ ExpectedReleaseOwner(g) = "Workspace")
    /\ UNCHANGED <<
        workspaceState, buildState, leaseCount, groupBusy, cleanupOutcome,
        reportEntry, lateGroup, disposedWithLease, buildAdmissionWitness,
        leaseAdmissionWitness, leaseDrainWitness, lateRoutingWitness,
        groupQuiescenceWitness, workspaceCompletionWitness,
        cleanupVisibilityWitness, directReleaseObserved,
        coordinatedDrainObserved, lateCleanupObserved, cleanupFailureObserved
        >>

OwnerRequestsCoordinatedRelease(g) ==
    /\ g \in CoordinatedGroups
    /\ workspaceState = "Closing"
    /\ groupState[g] \in {"Published", "ReleaseOnly"}
    /\ \/ leaseCount[g] = 0
       \/ AllowReleaseWithActiveLease
    /\ LET owner ==
            IF AllowWrongReleaseAuthority
            THEN "Workspace"
            ELSE "Completion"
       IN
        /\ groupState' =
            [groupState EXCEPT ![g] = "ReleaseRequested"]
        /\ releaseOwner' = [releaseOwner EXCEPT ![g] = owner]
        /\ authorityWitness' =
            (authorityWitness /\ owner = ExpectedReleaseOwner(g))
    /\ releaseStarts' = [releaseStarts EXCEPT ![g] = @ + 1]
    /\ leaseDrainWitness' =
        (leaseDrainWitness /\ leaseCount[g] = 0)
    /\ coordinatedDrainObserved' =
        (coordinatedDrainObserved
         \/ (disposedWithLease[g] /\ leaseCount[g] = 0))
    /\ UNCHANGED <<
        workspaceState, buildState, leaseCount, groupBusy, cleanupOutcome,
        reportEntry, lateGroup, disposedWithLease, buildAdmissionWitness,
        leaseAdmissionWitness, lateRoutingWitness, groupQuiescenceWitness,
        workspaceCompletionWitness, cleanupVisibilityWitness,
        directReleaseObserved, lateCleanupObserved, cleanupFailureObserved
        >>

CompleteRelease(g, outcome) ==
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
        workspaceState, buildState, leaseCount, groupBusy, releaseOwner,
        releaseStarts, reportEntry, lateGroup, disposedWithLease,
        buildAdmissionWitness, leaseAdmissionWitness, authorityWitness,
        leaseDrainWitness, lateRoutingWitness, workspaceCompletionWitness,
        cleanupVisibilityWitness, coordinatedDrainObserved
        >>

CompleteAnyRelease(g) ==
    \/ CompleteRelease(g, "Succeeded")
    \/ CompleteRelease(g, "Failed")

RecordReport(g) ==
    /\ groupState[g] = "Released"
    /\ cleanupOutcome[g] # "None"
    /\ reportEntry[g] = "None"
    /\ reportEntry' =
        [reportEntry EXCEPT ![g] = cleanupOutcome[g]]
    /\ UNCHANGED <<
        workspaceState, buildState, groupState, leaseCount, groupBusy,
        releaseOwner, releaseStarts, cleanupOutcome, lateGroup,
        disposedWithLease, buildAdmissionWitness, leaseAdmissionWitness,
        authorityWitness, leaseDrainWitness, lateRoutingWitness,
        groupQuiescenceWitness, workspaceCompletionWitness,
        cleanupVisibilityWitness, directReleaseObserved,
        coordinatedDrainObserved, lateCleanupObserved, cleanupFailureObserved
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
        buildState, groupState, leaseCount, groupBusy, releaseOwner,
        releaseStarts, cleanupOutcome, reportEntry, lateGroup,
        disposedWithLease, buildAdmissionWitness, leaseAdmissionWitness,
        authorityWitness, leaseDrainWitness, lateRoutingWitness,
        groupQuiescenceWitness, directReleaseObserved,
        coordinatedDrainObserved, lateCleanupObserved, cleanupFailureObserved
        >>

RepeatRelease(g) ==
    /\ AllowDoubleRelease
    /\ groupState[g] = "Released"
    /\ releaseStarts' = [releaseStarts EXCEPT ![g] = @ + 1]
    /\ UNCHANGED <<
        workspaceState, buildState, groupState, leaseCount, groupBusy,
        releaseOwner, cleanupOutcome, reportEntry, lateGroup,
        disposedWithLease, buildAdmissionWitness, leaseAdmissionWitness,
        authorityWitness, leaseDrainWitness, lateRoutingWitness,
        groupQuiescenceWitness, workspaceCompletionWitness,
        cleanupVisibilityWitness, directReleaseObserved,
        coordinatedDrainObserved, lateCleanupObserved, cleanupFailureObserved
        >>

Next ==
    \/ \E g \in Groups : StartBuild(g)
    \/ \E g \in Groups : CompleteBuild(g)
    \/ \E g \in Groups : AcquireLease(g)
    \/ \E g \in Groups : ReturnLease(g)
    \/ \E g \in Groups : BeginGroupWork(g)
    \/ \E g \in Groups : EndGroupWork(g)
    \/ CloseWorkspace
    \/ \E g \in Groups : RequestDirectRelease(g)
    \/ \E g \in Groups : OwnerRequestsCoordinatedRelease(g)
    \/ \E g \in Groups : CompleteAnyRelease(g)
    \/ \E g \in Groups : RecordReport(g)
    \/ FinalizeWorkspace
    \/ \E g \in Groups : RepeatRelease(g)

Fairness ==
    /\ \A g \in Groups : WF_vars(CompleteBuild(g))
    /\ \A g \in Groups : WF_vars(ReturnLease(g))
    /\ \A g \in Groups : WF_vars(EndGroupWork(g))
    /\ \A g \in Groups : WF_vars(RequestDirectRelease(g))
    /\ \A g \in Groups : WF_vars(OwnerRequestsCoordinatedRelease(g))
    /\ \A g \in Groups : WF_vars(CompleteAnyRelease(g))
    /\ \A g \in Groups : WF_vars(RecordReport(g))
    /\ WF_vars(FinalizeWorkspace)

Spec == Init /\ [][Next]_vars /\ Fairness

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

ReleaseBeginsAtMostOnce ==
    \A g \in Groups : releaseStarts[g] <= 1

ExistingLeasesRemainUsable ==
    \A g \in CoordinatedGroups :
        leaseCount[g] > 0 => groupState[g] # "Released"

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

(***************************************************************************)
(* Reachability probes. Configurations negate these observations so TLC     *)
(* emits a trace when the intended path is reachable.                       *)
(***************************************************************************)
NoDirectReleaseObserved == ~directReleaseObserved
NoCoordinatedDrainObserved == ~coordinatedDrainObserved
NoLateCleanupObserved == ~lateCleanupObserved
NoCleanupFailureObserved == ~cleanupFailureObserved

=============================================================================
