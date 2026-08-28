--------------------- MODULE PackageRealizationAdmission ---------------------
(***************************************************************************)
(* Models the TARGET DESIGN for admission into                             *)
(* `InspectionWorkspace.RealizePackageAssemblyContextRoles`, keyed by a     *)
(* package coordinate that is stable and decidable today (package id,      *)
(* version, target framework, and resolved producer -- see                 *)
(* `RealizedMemberCoordinate.Package` in                                    *)
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
(* THIS MODEL DOES NOT CLAIM CURRENT PRODUCT BEHAVIOR. As of this writing, *)
(* `RealizePackageAssemblyContextRoles` has no caching or admission logic  *)
(* at all: two calls with an identical package coordinate independently    *)
(* reopen content and mint two independent groups. That gap is            *)
(* demonstrated by                                                        *)
(* `PackageAssemblyContextRealizationConcurrentDemandTests` (PR #4958) and *)
(* tracked by issue #4960. This model checks the target design's own      *)
(* internal soundness -- the single-flight, coordinate-keyed admission     *)
(* cache this issue calls for -- not that the shipped code implements it.  *)
(*                                                                         *)
(* THE COORDINATE'S CONTENT-STABILITY IS AN ASSUMPTION, NOT A CLAIM THIS   *)
(* MODEL PROVES. It follows the runtime's existing, separately-owned       *)
(* identity work (`PackageCoordinateResolver`, source mapping/producer     *)
(* resolution for the cross-feed case) rather than redefining it here.     *)
(*                                                                         *)
(* Once a coordinate's realization succeeds it is retained for the rest of *)
(* the workspace's life (packages are immutable per coordinate within one  *)
(* workspace), so this model does not need to re-derive                    *)
(* `AssemblyContextGroup`'s own disposal/quiescence lifecycle -- that is   *)
(* `AssemblyContextGroupLifecycle.tla`'s scope, not this one's.            *)
(***************************************************************************)
EXTENDS FiniteSets, Naturals, TLC

CONSTANTS
    Coordinates,
    Demands,
    CoordinateOf

NoDemand == "NoDemand_"

ASSUME
    /\ NoDemand \notin Demands
    /\ CoordinateOf \in [Demands -> Coordinates]

DemandStates == {"Pending", "Admitting", "Joined", "Ready", "Failed"}
CacheStates == {"Absent", "InFlight", "Ready"}

VARIABLES
    cacheState,
    cacheRealization,
    leader,
    demandState,
    demandResult,
    nextRealizationId,
    joinWitness,
    retryAfterFailureWitness,
    consistentOutcomeWitness

vars == <<
    cacheState, cacheRealization, leader, demandState, demandResult,
    nextRealizationId, joinWitness, retryAfterFailureWitness,
    consistentOutcomeWitness
>>

TypeOK ==
    /\ cacheState \in [Coordinates -> CacheStates]
    /\ cacheRealization \in [Coordinates -> Nat]
    /\ leader \in [Coordinates -> Demands \union {NoDemand}]
    /\ demandState \in [Demands -> DemandStates]
    /\ demandResult \in [Demands -> Nat]
    /\ nextRealizationId \in Nat \ {0}
    /\ joinWitness \in BOOLEAN
    /\ retryAfterFailureWitness \in BOOLEAN
    /\ consistentOutcomeWitness \in BOOLEAN

Init ==
    /\ cacheState = [c \in Coordinates |-> "Absent"]
    /\ cacheRealization = [c \in Coordinates |-> 0]
    /\ leader = [c \in Coordinates |-> NoDemand]
    /\ demandState = [d \in Demands |-> "Pending"]
    /\ demandResult = [d \in Demands |-> 0]
    /\ nextRealizationId = 1
    /\ joinWitness = FALSE
    /\ retryAfterFailureWitness = FALSE
    /\ consistentOutcomeWitness = FALSE

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
    IN  /\ demandState[d] = "Pending"
        /\ cacheState[c] = "Absent"
        /\ cacheState' = [cacheState EXCEPT ![c] = "InFlight"]
        /\ leader' = [leader EXCEPT ![c] = d]
        /\ demandState' = [demandState EXCEPT ![d] = "Admitting"]
        /\ retryAfterFailureWitness' =
            (retryAfterFailureWitness \/ priorFailureExists)
        /\ UNCHANGED <<cacheRealization, demandResult, nextRealizationId,
                        joinWitness, consistentOutcomeWitness>>

(***************************************************************************)
(* A demand for an InFlight coordinate joins the admitting demand's        *)
(* operation instead of starting a second, redundant one -- this is the    *)
(* single-flight join this model exists to check.                         *)
(***************************************************************************)
Join(d) ==
    /\ demandState[d] = "Pending"
    /\ cacheState[CoordinateOf[d]] = "InFlight"
    /\ demandState' = [demandState EXCEPT ![d] = "Joined"]
    /\ joinWitness' = TRUE
    /\ UNCHANGED <<cacheState, cacheRealization, leader, demandResult,
                    nextRealizationId, retryAfterFailureWitness,
                    consistentOutcomeWitness>>

(***************************************************************************)
(* A demand for a coordinate already realized reuses the retained          *)
(* realization directly: no new work, no new group.                       *)
(***************************************************************************)
ReuseReady(d) ==
    LET c == CoordinateOf[d]
    IN  /\ demandState[d] = "Pending"
        /\ cacheState[c] = "Ready"
        /\ demandState' = [demandState EXCEPT ![d] = "Ready"]
        /\ demandResult' = [demandResult EXCEPT ![d] = cacheRealization[c]]
        /\ UNCHANGED <<cacheState, cacheRealization, leader, nextRealizationId,
                        joinWitness, retryAfterFailureWitness,
                        consistentOutcomeWitness>>

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
    IN  /\ cacheState[c] = "InFlight"
        /\ cacheState' = [cacheState EXCEPT ![c] = "Ready"]
        /\ cacheRealization' = [cacheRealization EXCEPT ![c] = rid]
        /\ nextRealizationId' = nextRealizationId + 1
        /\ demandState' =
            [d \in Demands |-> IF d \in waiting THEN "Ready" ELSE demandState[d]]
        /\ demandResult' =
            [d \in Demands |-> IF d \in waiting THEN rid ELSE demandResult[d]]
        /\ consistentOutcomeWitness' =
            (consistentOutcomeWitness \/ (Cardinality(waiting) > 1))
        /\ UNCHANGED <<leader, joinWitness, retryAfterFailureWitness>>

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
    IN  /\ cacheState[c] = "InFlight"
        /\ cacheState' = [cacheState EXCEPT ![c] = "Absent"]
        /\ leader' = [leader EXCEPT ![c] = NoDemand]
        /\ demandState' =
            [d \in Demands |-> IF d \in waiting THEN "Failed" ELSE demandState[d]]
        /\ UNCHANGED <<cacheRealization, demandResult, nextRealizationId,
                        joinWitness, consistentOutcomeWitness,
                        retryAfterFailureWitness>>

Next ==
    \/ \E d \in Demands : Admit(d) \/ Join(d) \/ ReuseReady(d)
    \/ \E c \in Coordinates : CompleteSuccess(c) \/ CompleteFailure(c)

Fairness ==
    /\ \A d \in Demands : WF_vars(Admit(d) \/ Join(d) \/ ReuseReady(d))
    /\ \A c \in Coordinates : WF_vars(CompleteSuccess(c))

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

(* Every demand that reaches "Ready" for a given coordinate observes the   *)
(* one same realization identity as every other demand that reached        *)
(* "Ready" for that coordinate -- joiners and reusers never see a          *)
(* different result than the leader or than each other.                   *)
ConsistentOutcomeAmongReadyDemands ==
    \A d1, d2 \in Demands :
        (
            /\ CoordinateOf[d1] = CoordinateOf[d2]
            /\ demandState[d1] = "Ready"
            /\ demandState[d2] = "Ready"
        ) => (demandResult[d1] = demandResult[d2])

(* The cache never claims readiness without a realization identity to back *)
(* it, and never leaves a leader recorded once the coordinate is Absent.   *)
CacheStateConsistent ==
    /\ \A c \in Coordinates : cacheState[c] = "Ready" => cacheRealization[c] # 0
    /\ \A c \in Coordinates : cacheState[c] = "Absent" => leader[c] = NoDemand

(***************************************************************************)
(* LIVENESS                                                                *)
(***************************************************************************)

(* Every demand eventually leaves "Pending": it is admitted and reused     *)
(* (Ready) or it observes the leader's failure (Failed) -- no demand waits *)
(* forever.                                                                *)
EveryDemandEventuallyResolves ==
    \A d \in Demands : (demandState[d] = "Pending") ~> (demandState[d] # "Pending")

(***************************************************************************)
(* REACHABILITY PROBES (not part of the correctness gate)                 *)
(*                                                                         *)
(* `<>joinWitness` cannot be used as a genuine liveness PROPERTY: nothing  *)
(* in Spec's fairness forces a Join to happen in every behavior (a         *)
(* coordinate's operation may always complete before a second demand gets *)
(* a turn), so it would not hold universally and is not the claim being   *)
(* made. Instead, each negated witness below is checked as an INVARIANT in *)
(* its own single-invariant .cfg (ReachabilityJoin.cfg,                   *)
(* ReachabilityRetryAfterFailure.cfg,                                     *)
(* ReachabilityMultiDemandConsistency.cfg) that TLC is EXPECTED to report  *)
(* as violated: the counterexample TLC prints is the proof that the       *)
(* corresponding transition is reachable at all, not permitted only by an  *)
(* unreachable guard. This is the same reachability-via-deliberate-        *)
(* violation technique the repo's `AssemblyContextGroupLifecycle.tla`      *)
(* companion `Broken*.cfg` configs use. Do not add these to the main       *)
(* correctness gate -- an expected failure there would look like a         *)
(* regression.                                                            *)
(***************************************************************************)
NoJoinObserved == ~joinWitness
NoRetryAfterFailureObserved == ~retryAfterFailureWitness
NoMultiDemandConsistencyObserved == ~consistentOutcomeWitness

================================================================================
