--------------------- MODULE PackageRealizationAdmission ---------------------
(***************************************************************************)
(* Models the TARGET DESIGN for admission into                             *)
(* `InspectionWorkspace.RealizePackageAssemblyContextRoles`, keyed by a     *)
(* package coordinate that is stable and decidable today (package id,      *)
(* version, target framework, runtime identifier, and resolved producer -- *)
(* see `RealizedMemberCoordinate.Package` in                               *)
(* src/DotnetInspector.Queries/WorkspaceAcquisitionCoordinates.cs, whose    *)
(* own remarks explain why `Producer` is part of the identity: id, version, *)
(* framework, and runtime identifier alone do not determine bytes across   *)
(* two feeds). This model does NOT cover assembly-content identity: within *)
(* one realized group, individual assembly images (e.g. two copies of      *)
(* Foo.dll from two different packages, or the same asset path shipped     *)
(* twice) have no independently decidable identity, so admission by nominal*)
(* assembly coordinate is out of scope -- see the non-goals below and      *)
(* docs/inspection-space.md's "Workspace" section, which documents the     *)
(* existing, narrower per-participant single-flight snapshot cache that IS *)
(* safe today (`AssemblyContextGroupLifecycle.tla` models that layer).     *)
(*                                                                         *)
(* Owning design: docs/design/inspection-layers.md's "Package-realization  *)
(* coordinate admission" section. That section is deliberately separate   *)
(* from the same document's "Package-role planning and cleanup boundary"  *)
(* (target design for #4745): this model checks whether an admitting       *)
(* operation starts for a repeated coordinate and how the cache retains    *)
(* and releases its result. #4745 still owns the internal plan/open, group *)
(* quiescence, and cleanup operation.                                      *)
(*                                                                         *)
(* THIS MODEL DOES NOT CLAIM CURRENT PRODUCT BEHAVIOR. As of this writing, *)
(* `RealizePackageAssemblyContextRoles` has no caching or admission logic  *)
(* at all: two calls with an identical package coordinate independently    *)
(* reopen content and mint two independent groups. That gap is             *)
(* demonstrated by                                                        *)
(* `PackageAssemblyContextRealizationConcurrentDemandTests` and is tracked *)
(* by issue #4960. This model checks the target design's own internal      *)
(* soundness -- the single-flight, coordinate-keyed admission cache and    *)
(* lease-scoped lifetime -- not that the shipped code implements them.     *)
(*                                                                         *)
(* Coordinates in this model represent only package Roots with a selected, *)
(* non-empty surface role that can produce a package-role session. A       *)
(* Root-only success owns no assembly context and bypasses this admission   *)
(* cache without a lease or cleanup request; host Root retention is outside *)
(* this model.                                                             *)
(*                                                                         *)
(* It does not resolve request granularity at the real API surface: the    *)
(* real call admits a whole caller-supplied package set under shared       *)
(* options, not one coordinate. The owning design's "Shared-realization    *)
(* lifetime" section and issue #5015 define the lease and disposal contract*)
(* checked here; multi-coordinate decomposition and rollback remain open.  *)
(*                                                                         *)
(* THE COORDINATE'S CONTENT-STABILITY IS AN ASSUMPTION, NOT A CLAIM THIS   *)
(* MODEL PROVES. It follows the runtime's existing, separately-owned       *)
(* identity work (`PackageCoordinateResolver`, source mapping/producer     *)
(* resolution for the cross-feed case) rather than redefining it here.     *)
(*                                                                         *)
(* Once a coordinate's realization succeeds it is retained while the      *)
(* workspace remains open, even with zero leases. Disposal then drains     *)
(* leases and requests the adjacent package-role cleanup exactly once.     *)
(* This model does not re-derive `AssemblyContextGroup`'s own              *)
(* disposal/quiescence lifecycle -- that is                                *)
(* `AssemblyContextGroupLifecycle.tla`'s scope, not this one's.            *)
(***************************************************************************)
EXTENDS FiniteSets, Naturals, TLC

CONSTANTS
    Coordinates,
    Demands,
    CoordinateOf,
    AllowLeaseAfterClose,
    AllowReleaseWithActiveLease,
    AllowLatePublish,
    AllowDoubleCleanup,
    AllowResurrection

NoDemand == "NoDemand_"

ASSUME
    /\ NoDemand \notin Demands
    /\ CoordinateOf \in [Demands -> Coordinates]
    /\ AllowLeaseAfterClose \in BOOLEAN
    /\ AllowReleaseWithActiveLease \in BOOLEAN
    /\ AllowLatePublish \in BOOLEAN
    /\ AllowDoubleCleanup \in BOOLEAN
    /\ AllowResurrection \in BOOLEAN

DemandStates ==
    {"Pending", "Admitting", "Joined", "Leased", "Returned", "Failed",
     "Rejected"}
CacheStates ==
    {"Absent", "InFlight", "Draining", "Ready", "Closing", "Releasing",
     "Released"}
WorkspaceStates == {"Open", "Disposed"}
CleanupOutcomes == {"None", "Released", "Failed"}

VARIABLES
    workspaceState,
    cacheState,
    cacheRealization,
    leader,
    demandState,
    demandResult,
    nextRealizationId,
    cleanupStarts,
    cleanupOutcome,
    returnAttempts,
    disposedWithLease,
    drainedSuccess,
    leaseSafetyWitness,
    publishSafetyWitness,
    cleanupSafetyWitness,
    joinWitness,
    retryAfterFailureWitness,
    consistentOutcomeWitness,
    zeroLeaseRetentionWitness,
    disposalWaitWitness,
    drainedSuccessWitness,
    doubleReturnWitness

vars == <<
    workspaceState, cacheState, cacheRealization, leader, demandState,
    demandResult, nextRealizationId, cleanupStarts, cleanupOutcome,
    returnAttempts, disposedWithLease, drainedSuccess, leaseSafetyWitness,
    publishSafetyWitness, cleanupSafetyWitness, joinWitness,
    retryAfterFailureWitness, consistentOutcomeWitness,
    zeroLeaseRetentionWitness, disposalWaitWitness, drainedSuccessWitness,
    doubleReturnWitness
    >>

ActiveLeases(c) ==
    {d \in Demands :
        CoordinateOf[d] = c /\ demandState[d] = "Leased"}

TypeOK ==
    /\ workspaceState \in WorkspaceStates
    /\ cacheState \in [Coordinates -> CacheStates]
    /\ cacheRealization \in [Coordinates -> Nat]
    /\ leader \in [Coordinates -> Demands \union {NoDemand}]
    /\ demandState \in [Demands -> DemandStates]
    /\ demandResult \in [Demands -> Nat]
    /\ nextRealizationId \in Nat \ {0}
    /\ cleanupStarts \in [Coordinates -> Nat]
    /\ cleanupOutcome \in [Coordinates -> CleanupOutcomes]
    /\ returnAttempts \in [Demands -> 0..2]
    /\ disposedWithLease \in [Coordinates -> BOOLEAN]
    /\ drainedSuccess \in [Coordinates -> BOOLEAN]
    /\ leaseSafetyWitness \in BOOLEAN
    /\ publishSafetyWitness \in BOOLEAN
    /\ cleanupSafetyWitness \in BOOLEAN
    /\ joinWitness \in BOOLEAN
    /\ retryAfterFailureWitness \in BOOLEAN
    /\ consistentOutcomeWitness \in BOOLEAN
    /\ zeroLeaseRetentionWitness \in BOOLEAN
    /\ disposalWaitWitness \in BOOLEAN
    /\ drainedSuccessWitness \in BOOLEAN
    /\ doubleReturnWitness \in BOOLEAN

Init ==
    /\ workspaceState = "Open"
    /\ cacheState = [c \in Coordinates |-> "Absent"]
    /\ cacheRealization = [c \in Coordinates |-> 0]
    /\ leader = [c \in Coordinates |-> NoDemand]
    /\ demandState = [d \in Demands |-> "Pending"]
    /\ demandResult = [d \in Demands |-> 0]
    /\ nextRealizationId = 1
    /\ cleanupStarts = [c \in Coordinates |-> 0]
    /\ cleanupOutcome = [c \in Coordinates |-> "None"]
    /\ returnAttempts = [d \in Demands |-> 0]
    /\ disposedWithLease = [c \in Coordinates |-> FALSE]
    /\ drainedSuccess = [c \in Coordinates |-> FALSE]
    /\ leaseSafetyWitness = TRUE
    /\ publishSafetyWitness = TRUE
    /\ cleanupSafetyWitness = TRUE
    /\ joinWitness = FALSE
    /\ retryAfterFailureWitness = FALSE
    /\ consistentOutcomeWitness = FALSE
    /\ zeroLeaseRetentionWitness = FALSE
    /\ disposalWaitWitness = FALSE
    /\ drainedSuccessWitness = FALSE
    /\ doubleReturnWitness = FALSE

(***************************************************************************)
(* A demand for an Absent coordinate becomes the admitting (leading)       *)
(* demand: it starts the one realization operation for that coordinate.    *)
(* If some earlier demand for the same coordinate already failed, this     *)
(* Admit is a retry -- failures are not cached, only successful            *)
(* realizations are, so a transient failure never poisons the coordinate.  *)
(***************************************************************************)
Admit(d) ==
    LET c == CoordinateOf[d]
        priorFailureExists ==
            \E e \in Demands :
                CoordinateOf[e] = c /\ demandState[e] = "Failed"
    IN  /\ workspaceState = "Open"
        /\ demandState[d] = "Pending"
        /\ cacheState[c] = "Absent"
        /\ cacheState' = [cacheState EXCEPT ![c] = "InFlight"]
        /\ leader' = [leader EXCEPT ![c] = d]
        /\ demandState' = [demandState EXCEPT ![d] = "Admitting"]
        /\ retryAfterFailureWitness' =
            (retryAfterFailureWitness \/ priorFailureExists)
        /\ UNCHANGED <<
            workspaceState, cacheRealization, demandResult, nextRealizationId,
            cleanupStarts, cleanupOutcome, returnAttempts, disposedWithLease,
            drainedSuccess, leaseSafetyWitness, publishSafetyWitness,
            cleanupSafetyWitness, joinWitness, consistentOutcomeWitness,
            zeroLeaseRetentionWitness, disposalWaitWitness,
            drainedSuccessWitness, doubleReturnWitness
            >>

(***************************************************************************)
(* A demand for an InFlight coordinate joins the admitting demand's        *)
(* operation instead of starting a second, redundant one -- this is the    *)
(* single-flight join this model exists to check.                         *)
(***************************************************************************)
Join(d) ==
    /\ workspaceState = "Open"
    /\ demandState[d] = "Pending"
    /\ cacheState[CoordinateOf[d]] = "InFlight"
    /\ demandState' = [demandState EXCEPT ![d] = "Joined"]
    /\ joinWitness' = TRUE
    /\ UNCHANGED <<
        workspaceState, cacheState, cacheRealization, leader, demandResult,
        nextRealizationId, cleanupStarts, cleanupOutcome, returnAttempts,
        disposedWithLease, drainedSuccess, leaseSafetyWitness,
        publishSafetyWitness, cleanupSafetyWitness, retryAfterFailureWitness,
        consistentOutcomeWitness, zeroLeaseRetentionWitness,
        disposalWaitWitness, drainedSuccessWitness, doubleReturnWitness
        >>

(***************************************************************************)
(* A demand for a coordinate already realized reuses the retained          *)
(* realization directly: no new work, no new group.                       *)
(***************************************************************************)
ReuseReady(d) ==
    LET c == CoordinateOf[d]
    IN  /\ demandState[d] = "Pending"
        /\ (
            \/ /\ workspaceState = "Open"
               /\ cacheState[c] = "Ready"
            \/ /\ AllowLeaseAfterClose
               /\ workspaceState = "Disposed"
               /\ cacheState[c] = "Closing"
            )
        /\ demandState' = [demandState EXCEPT ![d] = "Leased"]
        /\ demandResult' = [demandResult EXCEPT ![d] = cacheRealization[c]]
        /\ leaseSafetyWitness' =
            (
                leaseSafetyWitness
                /\ workspaceState = "Open"
                /\ cacheState[c] = "Ready"
                )
        /\ zeroLeaseRetentionWitness' =
            (
                zeroLeaseRetentionWitness
                \/ (
                    workspaceState = "Open"
                    /\ cacheState[c] = "Ready"
                    /\ ActiveLeases(c) = {}
                    )
                )
        /\ UNCHANGED <<
            workspaceState, cacheState, cacheRealization, leader,
            nextRealizationId, cleanupStarts, cleanupOutcome, returnAttempts,
            disposedWithLease, drainedSuccess, publishSafetyWitness,
            cleanupSafetyWitness, joinWitness, retryAfterFailureWitness,
            consistentOutcomeWitness, disposalWaitWitness,
            drainedSuccessWitness, doubleReturnWitness
            >>

(***************************************************************************)
(* The admitting demand's realization succeeds: every demand that admitted *)
(* or joined this operation receives the SAME new realization identity,   *)
(* and it is retained (cacheState becomes "Ready") for the rest of the     *)
(* workspace's life.                                                       *)
(***************************************************************************)
CompleteSuccess(c) ==
    LET rid == nextRealizationId
        waiting ==
            {d \in Demands :
                CoordinateOf[d] = c /\ demandState[d] \in {"Admitting", "Joined"}}
    IN  /\ (
            \/ /\ cacheState[c] = "InFlight"
               /\ workspaceState = "Open"
            \/ /\ AllowLatePublish
               /\ cacheState[c] = "Draining"
               /\ workspaceState = "Disposed"
            )
        /\ cacheState' = [cacheState EXCEPT ![c] = "Ready"]
        /\ cacheRealization' = [cacheRealization EXCEPT ![c] = rid]
        /\ leader' = [leader EXCEPT ![c] = NoDemand]
        /\ nextRealizationId' = nextRealizationId + 1
        /\ demandState' =
            [d \in Demands |->
                IF d \in waiting THEN "Leased" ELSE demandState[d]]
        /\ demandResult' =
            [d \in Demands |-> IF d \in waiting THEN rid ELSE demandResult[d]]
        /\ leaseSafetyWitness' =
            (
                leaseSafetyWitness
                /\ workspaceState = "Open"
                /\ cacheState[c] = "InFlight"
                )
        /\ publishSafetyWitness' =
            (
                publishSafetyWitness
                /\ workspaceState = "Open"
                /\ cacheState[c] = "InFlight"
            )
        /\ consistentOutcomeWitness' =
            (consistentOutcomeWitness \/ (Cardinality(waiting) > 1))
        /\ UNCHANGED <<
            workspaceState, cleanupStarts, cleanupOutcome, returnAttempts,
            disposedWithLease, drainedSuccess, cleanupSafetyWitness,
            joinWitness, retryAfterFailureWitness,
            zeroLeaseRetentionWitness, disposalWaitWitness,
            drainedSuccessWitness, doubleReturnWitness
            >>

(***************************************************************************)
(* The admitting demand's realization fails: every demand that admitted or *)
(* joined it observes the failure, and the cache entry clears (returns to  *)
(* "Absent") rather than remembering the failure -- so a later demand for  *)
(* the same coordinate can retry.                                         *)
(***************************************************************************)
CompleteFailure(c) ==
    LET waiting ==
            {d \in Demands :
                CoordinateOf[d] = c /\ demandState[d] \in {"Admitting", "Joined"}}
    IN  /\ workspaceState = "Open"
        /\ cacheState[c] = "InFlight"
        /\ cacheState' = [cacheState EXCEPT ![c] = "Absent"]
        /\ cacheRealization' = [cacheRealization EXCEPT ![c] = 0]
        /\ leader' = [leader EXCEPT ![c] = NoDemand]
        /\ demandState' =
            [d \in Demands |-> IF d \in waiting THEN "Failed" ELSE demandState[d]]
        /\ UNCHANGED <<
            workspaceState, demandResult, nextRealizationId, cleanupStarts,
            cleanupOutcome, returnAttempts, disposedWithLease, drainedSuccess,
            leaseSafetyWitness, publishSafetyWitness, cleanupSafetyWitness,
            joinWitness, retryAfterFailureWitness, consistentOutcomeWitness,
            zeroLeaseRetentionWitness, disposalWaitWitness,
            drainedSuccessWitness, doubleReturnWitness
            >>

(***************************************************************************)
(* Disposal closes admission atomically for every coordinate. In-flight    *)
(* operations become draining; ready realizations become closing and retain *)
(* their existing demand leases until those holders return them.            *)
(***************************************************************************)
Dispose ==
    /\ workspaceState = "Open"
    /\ workspaceState' = "Disposed"
    /\ cacheState' =
        [c \in Coordinates |->
            CASE cacheState[c] = "InFlight" -> "Draining"
              [] cacheState[c] = "Ready" -> "Closing"
              [] OTHER -> cacheState[c]]
    /\ disposedWithLease' =
        [c \in Coordinates |->
            disposedWithLease[c]
                \/ (cacheState[c] = "Ready" /\ ActiveLeases(c) # {})]
    /\ UNCHANGED <<
        cacheRealization, leader, demandState, demandResult,
        nextRealizationId, cleanupStarts, cleanupOutcome, returnAttempts,
        drainedSuccess, leaseSafetyWitness, publishSafetyWitness,
        cleanupSafetyWitness, joinWitness, retryAfterFailureWitness,
        consistentOutcomeWitness, zeroLeaseRetentionWitness,
        disposalWaitWitness, drainedSuccessWitness, doubleReturnWitness
        >>

(***************************************************************************)
(* A pending demand after disposal receives a visible terminal rejection.  *)
(***************************************************************************)
RejectAfterClose(d) ==
    /\ workspaceState = "Disposed"
    /\ demandState[d] = "Pending"
    /\ demandState' = [demandState EXCEPT ![d] = "Rejected"]
    /\ UNCHANGED <<
        workspaceState, cacheState, cacheRealization, leader, demandResult,
        nextRealizationId, cleanupStarts, cleanupOutcome, returnAttempts,
        disposedWithLease, drainedSuccess, leaseSafetyWitness,
        publishSafetyWitness, cleanupSafetyWitness, joinWitness,
        retryAfterFailureWitness, consistentOutcomeWitness,
        zeroLeaseRetentionWitness, disposalWaitWitness,
        drainedSuccessWitness, doubleReturnWitness
            >>

(***************************************************************************)
(* A successful result that arrives after disposal owns a real realization *)
(* but may not publish or issue leases. It moves directly to closing.       *)
(***************************************************************************)
CompleteDrainedSuccess(c) ==
    LET rid == nextRealizationId
        waiting ==
            {d \in Demands :
                CoordinateOf[d] = c /\ demandState[d] \in {"Admitting", "Joined"}}
    IN  /\ workspaceState = "Disposed"
        /\ cacheState[c] = "Draining"
        /\ cacheState' = [cacheState EXCEPT ![c] = "Closing"]
        /\ cacheRealization' = [cacheRealization EXCEPT ![c] = rid]
        /\ leader' = [leader EXCEPT ![c] = NoDemand]
        /\ nextRealizationId' = nextRealizationId + 1
        /\ demandState' =
            [d \in Demands |->
                IF d \in waiting THEN "Rejected" ELSE demandState[d]]
        /\ drainedSuccess' = [drainedSuccess EXCEPT ![c] = TRUE]
        /\ drainedSuccessWitness' = TRUE
        /\ UNCHANGED <<
            workspaceState, demandResult, cleanupStarts, cleanupOutcome,
            returnAttempts, disposedWithLease, leaseSafetyWitness,
            publishSafetyWitness, cleanupSafetyWitness, joinWitness,
            retryAfterFailureWitness, consistentOutcomeWitness,
            zeroLeaseRetentionWitness, disposalWaitWitness,
            doubleReturnWitness
            >>

(***************************************************************************)
(* A failed result arriving while disposal drains the operation leaves no  *)
(* reusable realization and settles every attached demand visibly.         *)
(***************************************************************************)
CompleteDrainedFailure(c) ==
    LET waiting ==
            {d \in Demands :
                CoordinateOf[d] = c /\ demandState[d] \in {"Admitting", "Joined"}}
    IN  /\ workspaceState = "Disposed"
        /\ cacheState[c] = "Draining"
        /\ cacheState' = [cacheState EXCEPT ![c] = "Absent"]
        /\ cacheRealization' = [cacheRealization EXCEPT ![c] = 0]
        /\ leader' = [leader EXCEPT ![c] = NoDemand]
        /\ demandState' =
            [d \in Demands |-> IF d \in waiting THEN "Failed" ELSE demandState[d]]
        /\ UNCHANGED <<
            workspaceState, demandResult, nextRealizationId, cleanupStarts,
            cleanupOutcome, returnAttempts, disposedWithLease, drainedSuccess,
            leaseSafetyWitness, publishSafetyWitness, cleanupSafetyWitness,
            joinWitness, retryAfterFailureWitness, consistentOutcomeWitness,
            zeroLeaseRetentionWitness, disposalWaitWitness,
            drainedSuccessWitness, doubleReturnWitness
            >>

(***************************************************************************)
(* The first return removes the demand's lease. A ready entry stays cached *)
(* with zero leases until workspace disposal.                              *)
(***************************************************************************)
ReturnLease(d) ==
    /\ demandState[d] = "Leased"
    /\ returnAttempts[d] = 0
    /\ demandState' = [demandState EXCEPT ![d] = "Returned"]
    /\ returnAttempts' = [returnAttempts EXCEPT ![d] = 1]
    /\ UNCHANGED <<
        workspaceState, cacheState, cacheRealization, leader, demandResult,
        nextRealizationId, cleanupStarts, cleanupOutcome, disposedWithLease,
        drainedSuccess, leaseSafetyWitness, publishSafetyWitness,
        cleanupSafetyWitness, joinWitness, retryAfterFailureWitness,
        consistentOutcomeWitness, zeroLeaseRetentionWitness,
        disposalWaitWitness, drainedSuccessWitness, doubleReturnWitness
        >>

(***************************************************************************)
(* A repeated return is observable for reachability evidence but leaves all *)
(* lease accounting and cleanup state unchanged.                           *)
(***************************************************************************)
ReturnLeaseAgain(d) ==
    /\ demandState[d] = "Returned"
    /\ returnAttempts[d] = 1
    /\ returnAttempts' = [returnAttempts EXCEPT ![d] = 2]
    /\ doubleReturnWitness' = TRUE
    /\ UNCHANGED <<
        workspaceState, cacheState, cacheRealization, leader, demandState,
        demandResult, nextRealizationId, cleanupStarts, cleanupOutcome,
        disposedWithLease, drainedSuccess, leaseSafetyWitness,
        publishSafetyWitness, cleanupSafetyWitness, joinWitness,
        retryAfterFailureWitness, consistentOutcomeWitness,
        zeroLeaseRetentionWitness, disposalWaitWitness,
        drainedSuccessWitness
        >>

(***************************************************************************)
(* Cleanup begins only after every demand lease is returned and only once. *)
(* The explicit mutation switches make both guards independently falsifiable. *)
(***************************************************************************)
BeginCleanup(c) ==
    /\ workspaceState = "Disposed"
    /\ (
        \/ cacheState[c] = "Closing"
        \/ /\ AllowDoubleCleanup
           /\ cacheState[c] = "Releasing"
        )
    /\ (ActiveLeases(c) = {} \/ AllowReleaseWithActiveLease)
    /\ (cleanupStarts[c] = 0 \/ AllowDoubleCleanup)
    /\ cacheState' = [cacheState EXCEPT ![c] = "Releasing"]
    /\ cleanupStarts' =
        [cleanupStarts EXCEPT ![c] = @ + 1]
    /\ cleanupSafetyWitness' =
        (
            cleanupSafetyWitness
            /\ cacheState[c] = "Closing"
            /\ ActiveLeases(c) = {}
            /\ cleanupStarts[c] = 0
        )
    /\ disposalWaitWitness' =
        (disposalWaitWitness \/ disposedWithLease[c])
    /\ UNCHANGED <<
        workspaceState, cacheRealization, leader, demandState, demandResult,
        nextRealizationId, cleanupOutcome, returnAttempts, disposedWithLease,
        drainedSuccess, leaseSafetyWitness, publishSafetyWitness, joinWitness,
        retryAfterFailureWitness, consistentOutcomeWitness,
        zeroLeaseRetentionWitness, drainedSuccessWitness,
        doubleReturnWitness
        >>

(***************************************************************************)
(* The consumed package-role cleanup completes once with its exact typed    *)
(* success or failure outcome. The realization is terminal either way.      *)
(***************************************************************************)
CompleteCleanup(c, outcome) ==
    /\ outcome \in {"Released", "Failed"}
    /\ cacheState[c] = "Releasing"
    /\ cleanupStarts[c] > 0
    /\ cacheState' = [cacheState EXCEPT ![c] = "Released"]
    /\ cleanupOutcome' = [cleanupOutcome EXCEPT ![c] = outcome]
    /\ UNCHANGED <<
        workspaceState, cacheRealization, leader, demandState, demandResult,
        nextRealizationId, cleanupStarts, returnAttempts, disposedWithLease,
        drainedSuccess, leaseSafetyWitness, publishSafetyWitness,
        cleanupSafetyWitness, joinWitness, retryAfterFailureWitness,
        consistentOutcomeWitness, zeroLeaseRetentionWitness,
        disposalWaitWitness, drainedSuccessWitness, doubleReturnWitness
        >>

(***************************************************************************)
(* Deliberate mutation: a disposed closing or released entry becomes ready *)
(* again. Normal configurations disable it.                                *)
(***************************************************************************)
Resurrect(c) ==
    /\ AllowResurrection
    /\ workspaceState = "Disposed"
    /\ cacheState[c] \in {"Closing", "Releasing", "Released"}
    /\ cacheState' = [cacheState EXCEPT ![c] = "Ready"]
    /\ UNCHANGED <<
        workspaceState, cacheRealization, leader, demandState, demandResult,
        nextRealizationId, cleanupStarts, cleanupOutcome, returnAttempts,
        disposedWithLease, drainedSuccess, leaseSafetyWitness,
        publishSafetyWitness, cleanupSafetyWitness, joinWitness,
        retryAfterFailureWitness, consistentOutcomeWitness,
        zeroLeaseRetentionWitness, disposalWaitWitness,
        drainedSuccessWitness, doubleReturnWitness
        >>

Next ==
    \/ \E d \in Demands :
        Admit(d) \/ Join(d) \/ ReuseReady(d) \/ RejectAfterClose(d)
            \/ ReturnLease(d) \/ ReturnLeaseAgain(d)
    \/ \E c \in Coordinates :
        CompleteSuccess(c) \/ CompleteFailure(c)
            \/ CompleteDrainedSuccess(c) \/ CompleteDrainedFailure(c)
            \/ BeginCleanup(c) \/ Resurrect(c)
            \/ \E outcome \in {"Released", "Failed"} :
                CompleteCleanup(c, outcome)
    \/ Dispose

Fairness ==
    /\ \A d \in Demands :
        WF_vars(
            Admit(d) \/ Join(d) \/ ReuseReady(d) \/ RejectAfterClose(d)
        )
    /\ \A d \in Demands : WF_vars(ReturnLease(d))
    /\ \A c \in Coordinates :
        WF_vars(CompleteSuccess(c) \/ CompleteFailure(c))
    /\ \A c \in Coordinates :
        WF_vars(CompleteDrainedSuccess(c) \/ CompleteDrainedFailure(c))
    /\ \A c \in Coordinates : WF_vars(BeginCleanup(c))
    /\ \A c \in Coordinates :
        WF_vars(
            \E outcome \in {"Released", "Failed"} :
                CompleteCleanup(c, outcome)
        )

Spec == Init /\ [][Next]_vars /\ Fairness

(***************************************************************************)
(* SAFETY                                                                  *)
(***************************************************************************)

(* At most one demand per coordinate is ever the admitting (leading)       *)
(* operation at a time -- no coordinate ever has two independent           *)
(* realizations running concurrently.                                     *)
SingleFlightPerCoordinate ==
    \A c \in Coordinates :
        Cardinality(
            {d \in Demands : CoordinateOf[d] = c /\ demandState[d] = "Admitting"}
        ) <= 1

(* Every demand that has ever received a lease for one coordinate observes *)
(* the same realization identity. Returning a lease cannot make this       *)
(* history-based check vacuous.                                            *)
ConsistentLeaseOutcomeHistory ==
    \A d1, d2 \in Demands :
        (
            /\ CoordinateOf[d1] = CoordinateOf[d2]
            /\ demandResult[d1] # 0
            /\ demandResult[d2] # 0
        ) => (demandResult[d1] = demandResult[d2])

(* Cache state, realization identity, leader ownership, cleanup, and demand *)
(* lease history remain mutually consistent.                               *)
CacheStateConsistent ==
    /\ \A c \in Coordinates :
        cacheState[c] \in {"Absent", "InFlight", "Draining"}
            => cacheRealization[c] = 0
    /\ \A c \in Coordinates :
        cacheState[c] \in {"Ready", "Closing", "Releasing", "Released"}
            => cacheRealization[c] # 0
    /\ \A c \in Coordinates :
        leader[c] # NoDemand
            => cacheState[c] \in {"InFlight", "Draining"}
    /\ \A c \in Coordinates :
        cacheState[c] \in {"Absent", "Ready", "Closing", "Releasing", "Released"}
            => leader[c] = NoDemand
    /\ \A c \in Coordinates :
        cacheState[c] \in {"Absent", "InFlight", "Draining", "Ready", "Closing"}
            => cleanupStarts[c] = 0
    /\ \A c \in Coordinates :
        cacheState[c] \in {"Releasing", "Released"}
            => cleanupStarts[c] > 0
    /\ \A c \in Coordinates :
        cacheState[c] = "Released"
            <=> cleanupOutcome[c] \in {"Released", "Failed"}
    /\ \A d \in Demands :
        demandState[d] \in {"Leased", "Returned"}
            <=> demandResult[d] # 0
    /\ \A d \in Demands :
        returnAttempts[d] > 0 => demandState[d] = "Returned"

(* Each load-bearing transition independently records the condition it was  *)
(* required to observe in its pre-state. Mutation configurations weaken the *)
(* corresponding action and falsify these witnesses.                        *)
NoLeaseAfterAdmissionCloses == leaseSafetyWitness
NoPublicationAfterDisposal == publishSafetyWitness
ReleaseStartsOnlyAfterLeasesReturn == cleanupSafetyWitness

CleanupStartsAtMostOnce ==
    \A c \in Coordinates : cleanupStarts[c] <= 1

DisposedCacheCannotReopen ==
    workspaceState = "Disposed"
        => \A c \in Coordinates :
            cacheState[c] \notin {"InFlight", "Ready"}

ReleasedRealizationsHaveNoActiveLeases ==
    \A c \in Coordinates :
        cacheState[c] \in {"Releasing", "Released"}
            => ActiveLeases(c) = {}

(***************************************************************************)
(* LIVENESS                                                                *)
(***************************************************************************)

(* Every demand eventually receives a lease or a visible failed/rejected     *)
(* outcome. Lease return is a separate product obligation below.             *)
EveryDemandEventuallyResolves ==
    \A d \in Demands :
        (demandState[d] = "Pending")
            ~> (demandState[d] \in {"Leased", "Returned", "Failed", "Rejected"})

(* The fairness assumption on ReturnLease is explicit: the model cannot make *)
(* a caller return a leaked lease. The implementation must separately prove  *)
(* it uses awaited, non-blocking continuations.                               *)
EveryIssuedLeaseEventuallyReturns ==
    \A d \in Demands :
        (demandState[d] = "Leased") ~> (demandState[d] = "Returned")

EveryDisposedRealizationEventuallyReleases ==
    \A c \in Coordinates :
        (
            /\ workspaceState = "Disposed"
            /\ cacheRealization[c] # 0
        ) ~> (cacheState[c] = "Released")

EveryDrainingAdmissionEventuallySettles ==
    \A c \in Coordinates :
        (cacheState[c] = "Draining")
            ~> (cacheState[c] \in {"Absent", "Closing", "Releasing", "Released"})

(***************************************************************************)
(* REACHABILITY PROBES (not part of the correctness gate)                 *)
(*                                                                         *)
(* Each negated witness below is checked as an INVARIANT in its own         *)
(* expected-failure configuration. The counterexample proves that the       *)
(* transition is reachable rather than merely permitted by an unreachable  *)
(* guard. Do not add these probes to the main correctness gate.             *)
(***************************************************************************)
NoJoinObserved == ~joinWitness
NoRetryAfterFailureObserved == ~retryAfterFailureWitness
NoMultiDemandConsistencyObserved == ~consistentOutcomeWitness
NoZeroLeaseRetentionObserved == ~zeroLeaseRetentionWitness
NoDisposalWaitObserved == ~disposalWaitWitness
NoDrainedSuccessObserved == ~drainedSuccessWitness
NoDoubleReturnObserved == ~doubleReturnWitness

================================================================================
