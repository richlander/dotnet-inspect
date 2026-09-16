--------------------- MODULE PackageRealizationAdmission ---------------------
(***************************************************************************)
(* Models the TARGET DESIGN for admission into                             *)
(* `InspectionWorkspace.RealizePackageAssemblyContextRoles`, keyed by one   *)
(* exact ordered request of selected package-coordinate/content-generation/ *)
(* selection bindings and one exact realization-options value. Each package *)
(* coordinate includes package id, version, target framework, runtime        *)
(* identifier, and resolved producer (see `RealizedMemberCoordinate.Package` *)
(* src/DotnetInspector.Queries/WorkspaceAcquisitionCoordinates.cs).         *)
(*                                                                         *)
(* The whole request is the cache unit because the product creates one      *)
(* combined binding topology, applies identity-collision checks across it,  *)
(* and enforces request-wide assembly-count and retained-byte limits.        *)
(* Overlapping requests do not share partial realizations. Request order is *)
(* part of identity because construction, binding, and demand projection    *)
(* consume ordered participants.                                           *)
(*                                                                         *)
(* Owning design: docs/design/inspection-layers.md's "Package-realization   *)
(* exact-request admission" section. That section is deliberately separate *)
(* from the same document's "Package-role planning and cleanup boundary"  *)
(* (target design for #4745): this model checks whether an admitting       *)
(* operation starts for a repeated request and how the cache retains and    *)
(* releases its result. #4745 still owns the internal plan/open, group      *)
(* quiescence, and cleanup operation.                                      *)
(*                                                                         *)
(* THIS MODEL DOES NOT CLAIM CURRENT PRODUCT BEHAVIOR. As of this writing, *)
(* `RealizePackageAssemblyContextRoles` has no caching or admission logic  *)
(* at all: two calls with an identical package request independently       *)
(* reopen content and mint two independent groups. That gap is             *)
(* demonstrated by                                                        *)
(* `PackageAssemblyContextRealizationConcurrentDemandTests` and is tracked *)
(* by issue #4960. This model checks the target design's own internal      *)
(* soundness -- the single-flight, exact-request admission cache and        *)
(* lease-scoped lifetime -- not that the shipped code implements them.     *)
(*                                                                         *)
(* Request sequences contain only package Roots with a selected, non-empty  *)
(* surface role. Root-only successes are omitted; an empty selected sequence *)
(* bypasses this admission cache without a lease or cleanup request. A      *)
(* duplicate normalized coordinate is rejected before cache lookup even     *)
(* when its positional generation or selection tokens differ. Each          *)
(* generation token is acquisition-owned proof that equal coordinates still *)
(* name the same immutable content generation. The selection token proves   *)
(* that equal entries chose the same ordered surface/implementation assets. *)
(*                                                                         *)
(* THE GENERATION TOKEN'S IMMUTABILITY GUARANTEE IS AN ASSUMPTION, NOT A    *)
(* CLAIM THIS MODEL PROVES. #5121 owns issuing that token and proving that   *)
(* replacement content receives a different identity. This model only      *)
(* compares the owner-issued value as part of exact request identity.       *)
(*                                                                         *)
(* Admission reserves workspace-wide retained-entry, in-flight-operation,   *)
(* and aggregate retained-byte capacity before physical work starts. Once a *)
(* request's realization succeeds it is retained while the workspace remains *)
(* open, even with zero leases. Disposal then drains leases and requests the *)
(* adjacent package-role cleanup exactly once.                              *)
(* This model does not re-derive `AssemblyContextGroup`'s own              *)
(* disposal/quiescence lifecycle -- that is                                *)
(* `AssemblyContextGroupLifecycle.tla`'s scope, not this one's.            *)
(***************************************************************************)
EXTENDS FiniteSets, Naturals, Sequences, TLC

CONSTANTS
    PackageCoordinates,
    Generations,
    Selections,
    Options,
    Demands,
    RequestBindingsOf,
    OptionsOf,
    ReservationOf,
    MaxEntries,
    MaxInFlight,
    MaxReservedByteUnits,
    AllowLeaseAfterClose,
    AllowReleaseWithActiveLease,
    AllowLatePublish,
    AllowDoubleCleanup,
    AllowResurrection,
    AllowInexactReuse,
    AllowPartialPublish,
    AllowCancellationAbandon,
    AllowCancellationFailure,
    AllowOverCapacity,
    AllowDuplicateBindingAsDistinct,
    AllowRootOnlyAdmission

NoDemand == "NoDemand_"

ASSUME
    /\ NoDemand \notin Demands
    /\ RequestBindingsOf
        \in [
            Demands ->
                Seq(
                    {
                        <<c, g, s>> :
                            c \in PackageCoordinates,
                            g \in Generations,
                            s \in Selections
                    }
                )
        ]
    /\ OptionsOf \in [Demands -> Options]
    /\ ReservationOf \in [Demands -> Nat]
    /\ MaxEntries \in Nat \ {0}
    /\ MaxInFlight \in Nat \ {0}
    /\ MaxReservedByteUnits \in Nat \ {0}
    /\ AllowLeaseAfterClose \in BOOLEAN
    /\ AllowReleaseWithActiveLease \in BOOLEAN
    /\ AllowLatePublish \in BOOLEAN
    /\ AllowDoubleCleanup \in BOOLEAN
    /\ AllowResurrection \in BOOLEAN
    /\ AllowInexactReuse \in BOOLEAN
    /\ AllowPartialPublish \in BOOLEAN
    /\ AllowCancellationAbandon \in BOOLEAN
    /\ AllowCancellationFailure \in BOOLEAN
    /\ AllowOverCapacity \in BOOLEAN
    /\ AllowDuplicateBindingAsDistinct \in BOOLEAN
    /\ AllowRootOnlyAdmission \in BOOLEAN

SequenceSet(s) == {s[i] : i \in 1..Len(s)}
CoordinateSetOfBoundSequence(s) ==
    {s[i][1] : i \in 1..Len(s)}
CoordinateSequenceOfBoundSequence(s) ==
    [i \in 1..Len(s) |-> s[i][1]]
CoordinateGenerationSequenceOfBoundSequence(s) ==
    [i \in 1..Len(s) |-> <<s[i][1], s[i][2]>>]

HasNormalizedCoordinateDuplicate(d) ==
    Len(RequestBindingsOf[d])
        # Cardinality(CoordinateSetOfBoundSequence(RequestBindingsOf[d]))

HasDuplicateCoordinate(d) ==
    IF AllowDuplicateBindingAsDistinct
    THEN
        Len(RequestBindingsOf[d])
            # Cardinality(SequenceSet(RequestBindingsOf[d]))
    ELSE HasNormalizedCoordinateDuplicate(d)

Eligible(d) ==
    /\ (Len(RequestBindingsOf[d]) > 0 \/ AllowRootOnlyAdmission)
    /\ ~HasDuplicateCoordinate(d)

BoundRequestSequence(d) == RequestBindingsOf[d]

RequestIdentity(d) == <<BoundRequestSequence(d), OptionsOf[d]>>
EligibleDemands == {d \in Demands : Eligible(d)}

\* `Coordinates` is retained as an internal model symbol so the previously
\* checked lifetime state machine remains mechanically recognizable. Its
\* values are exact request identities, not individual package coordinates.
Coordinates == {RequestIdentity(d) : d \in EligibleDemands}
CoordinateOf == [d \in Demands |-> RequestIdentity(d)]

ASSUME
    \A d1, d2 \in EligibleDemands :
        CoordinateOf[d1] = CoordinateOf[d2]
            => ReservationOf[d1] = ReservationOf[d2]

ASSUME
    \A d1, d2 \in Demands :
        OptionsOf[d1] = OptionsOf[d2]
            => ReservationOf[d1] = ReservationOf[d2]

ReservationFor(c) ==
    ReservationOf[CHOOSE d \in EligibleDemands : CoordinateOf[d] = c]

RECURSIVE ReservationSum(_)

ReservationSum(entries) ==
    IF entries = {}
    THEN 0
    ELSE
        LET c == CHOOSE entry \in entries : TRUE
        IN ReservationFor(c) + ReservationSum(entries \ {c})

DemandStates ==
    {"Pending", "Bypassed", "Admitting", "Joined", "Leased", "Returned",
     "Canceled", "Failed", "Rejected"}
CacheStates ==
    {"Absent", "InFlight", "Draining", "Ready", "Closing", "Releasing",
     "Released"}
WorkspaceStates == {"Open", "Disposed"}
CleanupOutcomes == {"None", "Released", "Failed"}

VARIABLES
    workspaceState,
    cacheState,
    cacheRealization,
    cacheOperation,
    leader,
    demandState,
    demandResult,
    canceledOperation,
    nextRealizationId,
    settledOperations,
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
    doubleReturnWitness,
    capacityRejectionWitness

vars == <<
    workspaceState, cacheState, cacheRealization, cacheOperation, leader,
    demandState, demandResult, canceledOperation, nextRealizationId,
    settledOperations, cleanupStarts, cleanupOutcome,
    returnAttempts, disposedWithLease, drainedSuccess, leaseSafetyWitness,
    publishSafetyWitness, cleanupSafetyWitness, joinWitness,
    retryAfterFailureWitness, consistentOutcomeWitness,
    zeroLeaseRetentionWitness, disposalWaitWitness, drainedSuccessWitness,
    doubleReturnWitness, capacityRejectionWitness
    >>

capacityVars == <<capacityRejectionWitness>>

ActiveLeases(c) ==
    {d \in Demands :
        CoordinateOf[d] = c /\ demandState[d] = "Leased"}

ActiveEntries ==
    {c \in Coordinates : cacheState[c] \notin {"Absent", "Released"}}

InFlightEntries ==
    {c \in Coordinates : cacheState[c] \in {"InFlight", "Draining"}}

ReservedByteUnits == ReservationSum(ActiveEntries)

HasCapacity(d) ==
    /\ Cardinality(ActiveEntries) < MaxEntries
    /\ Cardinality(InFlightEntries) < MaxInFlight
    /\ ReservedByteUnits + ReservationOf[d] <= MaxReservedByteUnits

TypeOK ==
    /\ workspaceState \in WorkspaceStates
    /\ cacheState \in [Coordinates -> CacheStates]
    /\ cacheRealization \in [Coordinates -> Nat]
    /\ cacheOperation \in [Coordinates -> Nat]
    /\ leader \in [Coordinates -> Demands \union {NoDemand}]
    /\ demandState \in [Demands -> DemandStates]
    /\ demandResult \in [Demands -> Nat]
    /\ canceledOperation \in [Demands -> Nat]
    /\ nextRealizationId \in Nat \ {0}
    /\ settledOperations \subseteq Nat
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
    /\ capacityRejectionWitness \in BOOLEAN

Init ==
    /\ workspaceState = "Open"
    /\ cacheState = [c \in Coordinates |-> "Absent"]
    /\ cacheRealization = [c \in Coordinates |-> 0]
    /\ cacheOperation = [c \in Coordinates |-> 0]
    /\ leader = [c \in Coordinates |-> NoDemand]
    /\ demandState = [d \in Demands |-> "Pending"]
    /\ demandResult = [d \in Demands |-> 0]
    /\ canceledOperation = [d \in Demands |-> 0]
    /\ nextRealizationId = 1
    /\ settledOperations = {}
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
    /\ capacityRejectionWitness = FALSE

(***************************************************************************)
(* A request with no selected assembly-role packages remains a host-owned   *)
(* Root-only result and never enters this admission owner.                  *)
(***************************************************************************)
BypassRootOnly(d) ==
    /\ workspaceState = "Open"
    /\ demandState[d] = "Pending"
    /\ Len(RequestBindingsOf[d]) = 0
    /\ demandState' = [demandState EXCEPT ![d] = "Bypassed"]
    /\ UNCHANGED <<
        workspaceState, cacheState, cacheRealization, leader, demandResult,
        nextRealizationId, cleanupStarts, cleanupOutcome, returnAttempts,
        disposedWithLease, drainedSuccess, leaseSafetyWitness,
        publishSafetyWitness, cleanupSafetyWitness, joinWitness,
        retryAfterFailureWitness, consistentOutcomeWitness,
        zeroLeaseRetentionWitness, disposalWaitWitness,
        drainedSuccessWitness, doubleReturnWitness,
            cacheOperation, canceledOperation, settledOperations,
            capacityVars
        >>

(***************************************************************************)
(* Duplicate normalized selected coordinates are rejected before cache     *)
(* lookup, so one request cannot alias the same package occurrence twice.   *)
(***************************************************************************)
RejectDuplicate(d) ==
    /\ workspaceState = "Open"
    /\ demandState[d] = "Pending"
    /\ HasDuplicateCoordinate(d)
    /\ demandState' = [demandState EXCEPT ![d] = "Rejected"]
    /\ UNCHANGED <<
        workspaceState, cacheState, cacheRealization, leader, demandResult,
        nextRealizationId, cleanupStarts, cleanupOutcome, returnAttempts,
        disposedWithLease, drainedSuccess, leaseSafetyWitness,
        publishSafetyWitness, cleanupSafetyWitness, joinWitness,
        retryAfterFailureWitness, consistentOutcomeWitness,
        zeroLeaseRetentionWitness, disposalWaitWitness,
        drainedSuccessWitness, doubleReturnWitness,
            cacheOperation, canceledOperation, settledOperations,
            capacityVars
        >>

(***************************************************************************)
(* A demand for an Absent exact request becomes the admitting (leading)     *)
(* demand. If an earlier demand for the same request failed, this Admit is  *)
(* a retry: failures are not retained as reusable cache entries.            *)
(***************************************************************************)
Admit(d) ==
    LET c == CoordinateOf[d]
        priorFailureExists ==
            \E e \in Demands :
                CoordinateOf[e] = c /\ demandState[e] = "Failed"
    IN  /\ workspaceState = "Open"
        /\ demandState[d] = "Pending"
        /\ Eligible(d)
        /\ cacheState[c] = "Absent"
        /\ (HasCapacity(d) \/ AllowOverCapacity)
        /\ cacheState' = [cacheState EXCEPT ![c] = "InFlight"]
        /\ cacheOperation' =
            [cacheOperation EXCEPT ![c] = nextRealizationId]
        /\ leader' = [leader EXCEPT ![c] = d]
        /\ demandState' = [demandState EXCEPT ![d] = "Admitting"]
        /\ nextRealizationId' = nextRealizationId + 1
        /\ retryAfterFailureWitness' =
            (retryAfterFailureWitness \/ priorFailureExists)
        /\ UNCHANGED <<
            workspaceState, cacheRealization, demandResult, canceledOperation,
            settledOperations, cleanupStarts, cleanupOutcome, returnAttempts,
            disposedWithLease, drainedSuccess, leaseSafetyWitness,
            publishSafetyWitness, cleanupSafetyWitness, joinWitness,
            consistentOutcomeWitness, zeroLeaseRetentionWitness,
            disposalWaitWitness, drainedSuccessWitness, doubleReturnWitness,
            capacityRejectionWitness
            >>

(***************************************************************************)
(* A demand for an InFlight exact request joins the admitting demand's     *)
(* operation instead of starting a second, redundant one.                  *)
(***************************************************************************)
Join(d) ==
    /\ workspaceState = "Open"
    /\ demandState[d] = "Pending"
    /\ Eligible(d)
    /\ cacheState[CoordinateOf[d]] = "InFlight"
    /\ demandState' = [demandState EXCEPT ![d] = "Joined"]
    /\ joinWitness' = TRUE
    /\ UNCHANGED <<
        workspaceState, cacheState, cacheRealization, leader, demandResult,
        nextRealizationId, cleanupStarts, cleanupOutcome, returnAttempts,
        disposedWithLease, drainedSuccess, leaseSafetyWitness,
        publishSafetyWitness, cleanupSafetyWitness, retryAfterFailureWitness,
        consistentOutcomeWitness, zeroLeaseRetentionWitness,
        disposalWaitWitness, drainedSuccessWitness, doubleReturnWitness,
            cacheOperation, canceledOperation, settledOperations,
            capacityVars
        >>

(***************************************************************************)
(* A demand for an exact request already realized reuses the retained      *)
(* realization directly: no new work, no new group.                       *)
(***************************************************************************)
ReuseReadyFrom(d, c) ==
    /\ demandState[d] = "Pending"
    /\ Eligible(d)
    /\ c \in Coordinates
    /\ (c = CoordinateOf[d] \/ AllowInexactReuse)
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
        drainedSuccessWitness, doubleReturnWitness,
            cacheOperation, canceledOperation, settledOperations,
            capacityVars
        >>

ReuseReady(d) ==
    \E c \in Coordinates : ReuseReadyFrom(d, c)

(***************************************************************************)
(* Caller cancellation detaches only that demand. It does not cancel a     *)
(* workspace-owned operation that another demand joined or may later reuse. *)
(***************************************************************************)
CancelDemand(d) ==
    LET prior == demandState[d]
        c == CoordinateOf[d]
    IN  /\ prior \in {"Pending", "Admitting", "Joined"}
        /\ demandState' = [demandState EXCEPT ![d] = "Canceled"]
        /\ canceledOperation' =
            [canceledOperation EXCEPT
                ![d] =
                    IF prior \in {"Admitting", "Joined"}
                    THEN cacheOperation[c]
                    ELSE 0]
        /\ UNCHANGED <<
            workspaceState, cacheState, cacheRealization, cacheOperation,
            leader, demandResult, nextRealizationId, settledOperations,
            cleanupStarts, cleanupOutcome, returnAttempts, disposedWithLease,
            drainedSuccess, leaseSafetyWitness, publishSafetyWitness,
            cleanupSafetyWitness, joinWitness, retryAfterFailureWitness,
            consistentOutcomeWitness, zeroLeaseRetentionWitness,
            disposalWaitWitness, drainedSuccessWitness, doubleReturnWitness,
            capacityVars
            >>

(***************************************************************************)
(* Deliberate mutation: the final attached caller abandons the physical     *)
(* operation without an operation-completion transition.                    *)
(***************************************************************************)
AbandonOnFinalCancellation(d) ==
    LET c == CoordinateOf[d]
        attached ==
            {e \in Demands :
                CoordinateOf[e] = c
                    /\ demandState[e] \in {"Admitting", "Joined"}}
        operation == cacheOperation[c]
    IN  /\ AllowCancellationAbandon
        /\ demandState[d] \in {"Admitting", "Joined"}
        /\ attached = {d}
        /\ operation # 0
        /\ demandState' = [demandState EXCEPT ![d] = "Canceled"]
        /\ canceledOperation' =
            [canceledOperation EXCEPT ![d] = operation]
        /\ cacheState' = [cacheState EXCEPT ![c] = "Absent"]
        /\ cacheOperation' = [cacheOperation EXCEPT ![c] = 0]
        /\ leader' = [leader EXCEPT ![c] = NoDemand]
        /\ UNCHANGED <<
            workspaceState, cacheRealization, demandResult,
            nextRealizationId, settledOperations, cleanupStarts,
            cleanupOutcome, returnAttempts, disposedWithLease,
            drainedSuccess, leaseSafetyWitness, publishSafetyWitness,
            cleanupSafetyWitness, joinWitness, retryAfterFailureWitness,
            consistentOutcomeWitness, zeroLeaseRetentionWitness,
            disposalWaitWitness, drainedSuccessWitness, doubleReturnWitness,
            capacityRejectionWitness
            >>

(***************************************************************************)
(* Deliberate mutation: final-caller cancellation settles the shared        *)
(* operation as though caller cancellation were an operation failure.       *)
(***************************************************************************)
FailOnFinalCancellation(d) ==
    LET c == CoordinateOf[d]
        attached ==
            {e \in Demands :
                CoordinateOf[e] = c
                    /\ demandState[e] \in {"Admitting", "Joined"}}
        operation == cacheOperation[c]
    IN  /\ AllowCancellationFailure
        /\ demandState[d] \in {"Admitting", "Joined"}
        /\ attached = {d}
        /\ operation # 0
        /\ demandState' = [demandState EXCEPT ![d] = "Canceled"]
        /\ canceledOperation' =
            [canceledOperation EXCEPT ![d] = operation]
        /\ cacheState' = [cacheState EXCEPT ![c] = "Absent"]
        /\ cacheOperation' = [cacheOperation EXCEPT ![c] = 0]
        /\ leader' = [leader EXCEPT ![c] = NoDemand]
        /\ settledOperations' = settledOperations \union {operation}
        /\ UNCHANGED <<
            workspaceState, cacheRealization, demandResult,
            nextRealizationId, cleanupStarts, cleanupOutcome, returnAttempts,
            disposedWithLease, drainedSuccess, leaseSafetyWitness,
            publishSafetyWitness, cleanupSafetyWitness, joinWitness,
            retryAfterFailureWitness, consistentOutcomeWitness,
            zeroLeaseRetentionWitness, disposalWaitWitness,
            drainedSuccessWitness, doubleReturnWitness,
            capacityRejectionWitness
            >>

(***************************************************************************)
(* The admitting demand's realization succeeds: every demand that admitted *)
(* or joined this operation receives the SAME new realization identity,   *)
(* and it is retained (cacheState becomes "Ready") for the rest of the     *)
(* workspace's life.                                                       *)
(***************************************************************************)
CompleteSuccess(c) ==
    LET rid == cacheOperation[c]
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
        /\ cacheOperation' = [cacheOperation EXCEPT ![c] = 0]
        /\ leader' = [leader EXCEPT ![c] = NoDemand]
        /\ settledOperations' = settledOperations \union {rid}
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
            workspaceState, canceledOperation, nextRealizationId,
            cleanupStarts, cleanupOutcome, returnAttempts, disposedWithLease,
            drainedSuccess, cleanupSafetyWitness, joinWitness,
            retryAfterFailureWitness, zeroLeaseRetentionWitness,
            disposalWaitWitness, drainedSuccessWitness, doubleReturnWitness,
            capacityVars
            >>

(***************************************************************************)
(* Deliberate mutation: publish one attached demand before the exact        *)
(* request's remaining attached demands receive the same whole result.      *)
(* Normal configurations disable it.                                       *)
(***************************************************************************)
PublishPartial(c) ==
    LET rid == cacheOperation[c]
        waiting ==
            {d \in Demands :
                CoordinateOf[d] = c /\ demandState[d] \in {"Admitting", "Joined"}}
        published == CHOOSE d \in waiting : TRUE
    IN  /\ AllowPartialPublish
        /\ workspaceState = "Open"
        /\ cacheState[c] = "InFlight"
        /\ Cardinality(waiting) > 1
        /\ cacheState' = [cacheState EXCEPT ![c] = "Ready"]
        /\ cacheRealization' = [cacheRealization EXCEPT ![c] = rid]
        /\ cacheOperation' = [cacheOperation EXCEPT ![c] = 0]
        /\ leader' = [leader EXCEPT ![c] = NoDemand]
        /\ settledOperations' = settledOperations \union {rid}
        /\ demandState' = [demandState EXCEPT ![published] = "Leased"]
        /\ demandResult' = [demandResult EXCEPT ![published] = rid]
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
        /\ consistentOutcomeWitness' = TRUE
        /\ UNCHANGED <<
            workspaceState, canceledOperation, nextRealizationId,
            cleanupStarts, cleanupOutcome, returnAttempts, disposedWithLease,
            drainedSuccess, cleanupSafetyWitness, joinWitness,
            retryAfterFailureWitness, zeroLeaseRetentionWitness,
            disposalWaitWitness, drainedSuccessWitness, doubleReturnWitness,
            capacityVars
            >>

(***************************************************************************)
(* The admitting demand's realization fails: every demand that admitted or *)
(* joined it observes the failure, and the cache entry clears (returns to  *)
(* "Absent") rather than remembering the failure -- so a later demand for  *)
(* the same exact request can retry.                                      *)
(***************************************************************************)
CompleteFailure(c) ==
    LET operation == cacheOperation[c]
        waiting ==
            {d \in Demands :
                CoordinateOf[d] = c /\ demandState[d] \in {"Admitting", "Joined"}}
    IN  /\ workspaceState = "Open"
        /\ cacheState[c] = "InFlight"
        /\ cacheState' = [cacheState EXCEPT ![c] = "Absent"]
        /\ cacheRealization' = [cacheRealization EXCEPT ![c] = 0]
        /\ cacheOperation' = [cacheOperation EXCEPT ![c] = 0]
        /\ leader' = [leader EXCEPT ![c] = NoDemand]
        /\ settledOperations' = settledOperations \union {operation}
        /\ demandState' =
            [d \in Demands |-> IF d \in waiting THEN "Failed" ELSE demandState[d]]
        /\ UNCHANGED <<
            workspaceState, demandResult, nextRealizationId, cleanupStarts,
            cleanupOutcome, returnAttempts, disposedWithLease, drainedSuccess,
            leaseSafetyWitness, publishSafetyWitness, cleanupSafetyWitness,
            joinWitness, retryAfterFailureWitness, consistentOutcomeWitness,
            zeroLeaseRetentionWitness, disposalWaitWitness,
            drainedSuccessWitness, doubleReturnWitness,
            canceledOperation, capacityRejectionWitness
            >>

(***************************************************************************)
(* Disposal closes admission atomically for every exact request. In-flight *)
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
        disposalWaitWitness, drainedSuccessWitness, doubleReturnWitness,
            cacheOperation, canceledOperation, settledOperations,
            capacityVars
        >>

(***************************************************************************)
(* An absent exact request that cannot reserve all workspace-wide capacity *)
(* is rejected before an operation id is minted or physical work starts.   *)
(***************************************************************************)
RejectAtCapacity(d) ==
    LET c == CoordinateOf[d]
    IN  /\ workspaceState = "Open"
        /\ demandState[d] = "Pending"
        /\ Eligible(d)
        /\ cacheState[c] = "Absent"
        /\ ~HasCapacity(d)
        /\ demandState' = [demandState EXCEPT ![d] = "Rejected"]
        /\ capacityRejectionWitness' = TRUE
        /\ UNCHANGED <<
            workspaceState, cacheState, cacheRealization, cacheOperation,
            leader, demandResult, canceledOperation, nextRealizationId,
            settledOperations, cleanupStarts, cleanupOutcome, returnAttempts,
            disposedWithLease, drainedSuccess,
            leaseSafetyWitness, publishSafetyWitness, cleanupSafetyWitness,
            joinWitness, retryAfterFailureWitness, consistentOutcomeWitness,
            zeroLeaseRetentionWitness, disposalWaitWitness,
            drainedSuccessWitness, doubleReturnWitness
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
        drainedSuccessWitness, doubleReturnWitness,
            cacheOperation, canceledOperation, settledOperations,
            capacityVars
            >>

(***************************************************************************)
(* A successful result that arrives after disposal owns a real realization *)
(* but may not publish or issue leases. It moves directly to closing.       *)
(***************************************************************************)
CompleteDrainedSuccess(c) ==
    LET rid == cacheOperation[c]
        waiting ==
            {d \in Demands :
                CoordinateOf[d] = c /\ demandState[d] \in {"Admitting", "Joined"}}
    IN  /\ workspaceState = "Disposed"
        /\ cacheState[c] = "Draining"
        /\ cacheState' = [cacheState EXCEPT ![c] = "Closing"]
        /\ cacheRealization' = [cacheRealization EXCEPT ![c] = rid]
        /\ cacheOperation' = [cacheOperation EXCEPT ![c] = 0]
        /\ leader' = [leader EXCEPT ![c] = NoDemand]
        /\ settledOperations' = settledOperations \union {rid}
        /\ demandState' =
            [d \in Demands |->
                IF d \in waiting THEN "Rejected" ELSE demandState[d]]
        /\ drainedSuccess' = [drainedSuccess EXCEPT ![c] = TRUE]
        /\ drainedSuccessWitness' = TRUE
        /\ UNCHANGED <<
            workspaceState, demandResult, canceledOperation,
            nextRealizationId, cleanupStarts, cleanupOutcome, returnAttempts,
            disposedWithLease, leaseSafetyWitness, publishSafetyWitness,
            cleanupSafetyWitness, joinWitness, retryAfterFailureWitness,
            consistentOutcomeWitness, zeroLeaseRetentionWitness,
            disposalWaitWitness, doubleReturnWitness, capacityVars
            >>

(***************************************************************************)
(* A failed result arriving while disposal drains the operation leaves no  *)
(* reusable realization and settles every attached demand visibly.         *)
(***************************************************************************)
CompleteDrainedFailure(c) ==
    LET operation == cacheOperation[c]
        waiting ==
            {d \in Demands :
                CoordinateOf[d] = c /\ demandState[d] \in {"Admitting", "Joined"}}
    IN  /\ workspaceState = "Disposed"
        /\ cacheState[c] = "Draining"
        /\ cacheState' = [cacheState EXCEPT ![c] = "Absent"]
        /\ cacheRealization' = [cacheRealization EXCEPT ![c] = 0]
        /\ cacheOperation' = [cacheOperation EXCEPT ![c] = 0]
        /\ leader' = [leader EXCEPT ![c] = NoDemand]
        /\ settledOperations' = settledOperations \union {operation}
        /\ demandState' =
            [d \in Demands |-> IF d \in waiting THEN "Failed" ELSE demandState[d]]
        /\ UNCHANGED <<
            workspaceState, demandResult, nextRealizationId, cleanupStarts,
            cleanupOutcome, returnAttempts, disposedWithLease, drainedSuccess,
            leaseSafetyWitness, publishSafetyWitness, cleanupSafetyWitness,
            joinWitness, retryAfterFailureWitness, consistentOutcomeWitness,
            zeroLeaseRetentionWitness, disposalWaitWitness,
            drainedSuccessWitness, doubleReturnWitness,
            canceledOperation, capacityRejectionWitness
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
        disposalWaitWitness, drainedSuccessWitness, doubleReturnWitness,
        cacheOperation, canceledOperation, settledOperations,
        capacityVars
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
        drainedSuccessWitness, cacheOperation, canceledOperation,
        settledOperations, capacityVars
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
        doubleReturnWitness, cacheOperation, canceledOperation,
        settledOperations, capacityVars
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
        disposalWaitWitness, drainedSuccessWitness, doubleReturnWitness,
            cacheOperation, canceledOperation, settledOperations,
            capacityRejectionWitness
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
        drainedSuccessWitness, doubleReturnWitness,
            cacheOperation, canceledOperation, settledOperations,
            capacityVars
        >>

Next ==
    \/ \E d \in Demands :
        BypassRootOnly(d) \/ RejectDuplicate(d) \/ RejectAtCapacity(d)
            \/ Admit(d) \/ Join(d) \/ ReuseReady(d) \/ CancelDemand(d)
            \/ AbandonOnFinalCancellation(d) \/ FailOnFinalCancellation(d)
            \/ RejectAfterClose(d)
            \/ ReturnLease(d) \/ ReturnLeaseAgain(d)
    \/ \E c \in Coordinates :
        CompleteSuccess(c) \/ PublishPartial(c) \/ CompleteFailure(c)
            \/ CompleteDrainedSuccess(c) \/ CompleteDrainedFailure(c)
            \/ BeginCleanup(c) \/ Resurrect(c)
            \/ \E outcome \in {"Released", "Failed"} :
                CompleteCleanup(c, outcome)
    \/ Dispose

Fairness ==
    /\ \A d \in Demands :
        WF_vars(
            BypassRootOnly(d) \/ RejectDuplicate(d) \/ RejectAtCapacity(d)
                \/ Admit(d) \/ Join(d) \/ ReuseReady(d)
                \/ RejectAfterClose(d)
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

(* At most one demand per exact request identity is ever the admitting      *)
(* operation at a time.                                                     *)
SingleFlightPerRequest ==
    \A c \in Coordinates :
        Cardinality(
            {d \in Demands : CoordinateOf[d] = c /\ demandState[d] = "Admitting"}
        ) <= 1

DuplicateCoordinatesCannotAdmit ==
    \A d \in Demands :
        HasNormalizedCoordinateDuplicate(d)
            => demandState[d]
                \notin {"Admitting", "Joined", "Leased", "Returned"}

RootOnlyCannotAdmit ==
    \A d \in Demands :
        Len(RequestBindingsOf[d]) = 0
            =>
                /\ RequestIdentity(d) \notin ActiveEntries
                /\ demandState[d]
                    \notin {"Admitting", "Joined", "Leased", "Returned"}
                /\ IF RequestIdentity(d) \in Coordinates
                   THEN
                        /\ cacheRealization[RequestIdentity(d)] = 0
                        /\ cacheOperation[RequestIdentity(d)] = 0
                        /\ cleanupStarts[RequestIdentity(d)] = 0
                   ELSE TRUE

AdmissionCapacityBounded ==
    /\ Cardinality(ActiveEntries) <= MaxEntries
    /\ Cardinality(InFlightEntries) <= MaxInFlight
    /\ ReservedByteUnits <= MaxReservedByteUnits

(* Every demand that has ever received a lease for one exact request sees  *)
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
        cacheState[c] \in {"InFlight", "Draining"}
            <=> cacheOperation[c] # 0
    /\ \A c \in Coordinates :
        cacheRealization[c] # 0
            => cacheRealization[c] \in settledOperations
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
    /\ \A d \in Demands :
        canceledOperation[d] # 0 => demandState[d] = "Canceled"

(* A caller may detach, but every physical operation it had joined remains  *)
(* represented as active or reaches an explicit completion transition.      *)
CancellationCannotAbandonOperation ==
    \A d \in Demands :
        IF canceledOperation[d] = 0
        THEN TRUE
        ELSE
            \/ canceledOperation[d] = cacheOperation[CoordinateOf[d]]
            \/ canceledOperation[d] \in settledOperations

(* Cancellation of an attached caller changes only caller-local state. It  *)
(* cannot remove, replace, or settle the workspace-owned operation.         *)
CallerCancellationCannotSettleOperation ==
    [][
        \A d \in Demands :
            (
                /\ demandState[d] \in {"Admitting", "Joined"}
                /\ demandState'[d] = "Canceled"
            ) =>
                /\ cacheState'[CoordinateOf[d]] =
                    cacheState[CoordinateOf[d]]
                /\ cacheOperation'[CoordinateOf[d]] =
                    cacheOperation[CoordinateOf[d]]
                /\ settledOperations' = settledOperations
    ]_vars

(* A reusable result is issued only from the demand's exact ordered request *)
(* identity, including exact options.                                      *)
ExactRequestReuse ==
    \A d \in Demands :
        IF demandResult[d] = 0
        THEN TRUE
        ELSE
            /\ Eligible(d)
            /\ demandResult[d] = cacheRealization[CoordinateOf[d]]

(* Publication is atomic for every demand attached to one request. A ready *)
(* entry cannot coexist with an admitting or joined demand for that key.   *)
WholeRequestPublication ==
    \A c \in Coordinates :
        cacheState[c] = "Ready"
            => ~\E d \in Demands :
                CoordinateOf[d] = c
                    /\ demandState[d] \in {"Admitting", "Joined"}

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
            ~> (
                demandState[d]
                    \in {
                        "Bypassed", "Leased", "Returned", "Canceled", "Failed",
                        "Rejected"
                    }
            )

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

EveryInFlightAdmissionEventuallySettles ==
    \A c \in Coordinates :
        (cacheState[c] = "InFlight") ~> (cacheState[c] # "InFlight")

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
NoOverlappingRequestsObserved ==
    ~\E c1, c2 \in Coordinates :
        /\ c1 # c2
        /\ CoordinateSetOfBoundSequence(c1[1])
            \intersect CoordinateSetOfBoundSequence(c2[1]) # {}
        /\ cacheState[c1] = "InFlight"
        /\ cacheState[c2] = "InFlight"
NoOptionIsolationObserved ==
    ~\E c1, c2 \in Coordinates :
        /\ c1 # c2
        /\ c1[1] = c2[1]
        /\ c1[2] # c2[2]
        /\ cacheState[c1] = "InFlight"
        /\ cacheState[c2] = "InFlight"
NoContentGenerationIsolationObserved ==
    ~\E c1, c2 \in Coordinates :
        /\ c1 # c2
        /\ CoordinateSequenceOfBoundSequence(c1[1])
            = CoordinateSequenceOfBoundSequence(c2[1])
        /\ c1[1] # c2[1]
        /\ c1[2] = c2[2]
        /\ cacheState[c1] = "InFlight"
        /\ cacheState[c2] = "InFlight"
NoSelectionIsolationObserved ==
    ~\E c1, c2 \in Coordinates :
        /\ c1 # c2
        /\ CoordinateGenerationSequenceOfBoundSequence(c1[1])
            = CoordinateGenerationSequenceOfBoundSequence(c2[1])
        /\ c1[1] # c2[1]
        /\ c1[2] = c2[2]
        /\ cacheState[c1] = "InFlight"
        /\ cacheState[c2] = "InFlight"
NoReorderedRequestIsolationObserved ==
    ~\E c1, c2 \in Coordinates :
        /\ c1 # c2
        /\ c1[1] # c2[1]
        /\ CoordinateSetOfBoundSequence(c1[1])
            = CoordinateSetOfBoundSequence(c2[1])
        /\ c1[2] = c2[2]
        /\ cacheState[c1] = "InFlight"
        /\ cacheState[c2] = "InFlight"
NoDuplicateRejectionObserved ==
    ~\E d \in Demands :
        /\ HasNormalizedCoordinateDuplicate(d)
        /\ Len(RequestBindingsOf[d])
            = Cardinality(SequenceSet(RequestBindingsOf[d]))
        /\ demandState[d] = "Rejected"
NoRootOnlyBypassObserved ==
    ~\E d \in Demands :
        Len(RequestBindingsOf[d]) = 0 /\ demandState[d] = "Bypassed"
NoCapacityRejectionObserved == ~capacityRejectionWitness
NoDetachedCancellationObserved ==
    ~\E d \in Demands :
        /\ demandState[d] = "Canceled"
        /\ Eligible(d)
        /\ cacheState[CoordinateOf[d]] \in {"InFlight", "Ready"}
        /\ \E e \in Demands :
            /\ e # d
            /\ CoordinateOf[e] = CoordinateOf[d]
            /\ demandState[e] \in {"Admitting", "Joined", "Leased"}
NoCanceledOperationReuseObserved ==
    ~\E c \in Coordinates :
        /\ zeroLeaseRetentionWitness
        /\ cacheState[c] = "Ready"
        /\ \E d1, d2, d3 \in Demands :
            /\ d1 # d2
            /\ d1 # d3
            /\ d2 # d3
            /\ demandState[d1] = "Canceled"
            /\ demandState[d2] = "Canceled"
            /\ canceledOperation[d1] = cacheRealization[c]
            /\ canceledOperation[d2] = cacheRealization[c]
            /\ demandState[d3] = "Leased"
            /\ CoordinateOf[d3] = c
            /\ demandResult[d3] = cacheRealization[c]

================================================================================
