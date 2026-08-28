---------------------- MODULE ArtifactContextRealization ----------------------
(***************************************************************************)
(* Target design for workspace-owned realization of assembly universes     *)
(* from already-sealed artifact generations.                               *)
(*                                                                         *)
(* An AssemblyUniverse is the exact candidate-artifact selection plus its  *)
(* binding-policy and authorization-policy generations. Target requirements *)
(* belong to demands, not to that universe key: two demands may inspect    *)
(* different participants through one binding-consistent group. The target *)
(* check can therefore reject one demand without poisoning the reusable    *)
(* universe another demand successfully consumes.                          *)
(*                                                                         *)
(* Artifact acquisition, byte identity, package-coordinate resolution,     *)
(* binding itself, query callbacks, group disposal, and artifact-session   *)
(* quiescence are outside this model. ArtifactSessionAdmission,             *)
(* PackageRealizationAdmission, and AssemblyContextGroupLifecycle own those *)
(* interactions. UniverseOf and TargetAccepted are fixed owner-issued       *)
(* inputs whose construction this model assumes rather than proves.         *)
(***************************************************************************)
EXTENDS FiniteSets, Naturals, TLC

CONSTANTS
    Universes,
    Demands,
    UniverseOf,
    UniverseValid,
    TargetAccepted

NoDemand == "NoDemand_"

ASSUME
    /\ NoDemand \notin Demands
    /\ UniverseOf \in [Demands -> Universes]
    /\ UniverseValid \in [Universes -> BOOLEAN]
    /\ TargetAccepted \in [Demands -> BOOLEAN]

CacheStates == {"Absent", "InFlight", "Ready"}
DemandStates == {
    "Pending",
    "Leading",
    "Joined",
    "Ready",
    "Rejected",
    "Failed"
}

VARIABLES
    cacheState,
    groupOf,
    leader,
    demandState,
    demandGroup,
    nextGroupId,
    joinWitness,
    reuseWitness,
    targetIsolationWitness,
    distinctUniverseWitness,
    retryAfterFailureWitness

vars == <<
    cacheState,
    groupOf,
    leader,
    demandState,
    demandGroup,
    nextGroupId,
    joinWitness,
    reuseWitness,
    targetIsolationWitness,
    distinctUniverseWitness,
    retryAfterFailureWitness
>>

TerminalStates == {"Ready", "Rejected", "Failed"}

TypeOK ==
    /\ cacheState \in [Universes -> CacheStates]
    /\ groupOf \in [Universes -> Nat]
    /\ leader \in [Universes -> Demands \union {NoDemand}]
    /\ demandState \in [Demands -> DemandStates]
    /\ demandGroup \in [Demands -> Nat]
    /\ nextGroupId \in Nat \ {0}
    /\ joinWitness \in BOOLEAN
    /\ reuseWitness \in BOOLEAN
    /\ targetIsolationWitness \in BOOLEAN
    /\ distinctUniverseWitness \in BOOLEAN
    /\ retryAfterFailureWitness \in BOOLEAN

Init ==
    /\ cacheState = [u \in Universes |-> "Absent"]
    /\ groupOf = [u \in Universes |-> 0]
    /\ leader = [u \in Universes |-> NoDemand]
    /\ demandState = [d \in Demands |-> "Pending"]
    /\ demandGroup = [d \in Demands |-> 0]
    /\ nextGroupId = 1
    /\ joinWitness = FALSE
    /\ reuseWitness = FALSE
    /\ targetIsolationWitness = FALSE
    /\ distinctUniverseWitness = FALSE
    /\ retryAfterFailureWitness = FALSE

(***************************************************************************)
(* The first demand for an absent universe starts its one realization. A   *)
(* failed attempt returns the universe to Absent, so a later demand may     *)
(* retry instead of inheriting a cached failure.                            *)
(***************************************************************************)
Admit(d) ==
    LET u == UniverseOf[d]
        priorFailure ==
            \E e \in Demands :
                UniverseOf[e] = u /\ demandState[e] = "Failed"
    IN  /\ demandState[d] = "Pending"
        /\ cacheState[u] = "Absent"
        /\ cacheState' = [cacheState EXCEPT ![u] = "InFlight"]
        /\ leader' = [leader EXCEPT ![u] = d]
        /\ demandState' = [demandState EXCEPT ![d] = "Leading"]
        /\ retryAfterFailureWitness' =
            (retryAfterFailureWitness \/ priorFailure)
        /\ UNCHANGED << groupOf, demandGroup, nextGroupId, joinWitness,
                        reuseWitness, targetIsolationWitness,
                        distinctUniverseWitness >>

(***************************************************************************)
(* A demand for an in-flight universe joins even when its target set differs *)
(* from the leader's. The universe work is shared; target validation is     *)
(* demand-specific when that work completes.                               *)
(***************************************************************************)
Join(d) ==
    /\ demandState[d] = "Pending"
    /\ cacheState[UniverseOf[d]] = "InFlight"
    /\ demandState' = [demandState EXCEPT ![d] = "Joined"]
    /\ joinWitness' = TRUE
    /\ UNCHANGED << cacheState, groupOf, leader, demandGroup, nextGroupId,
                    reuseWitness, targetIsolationWitness,
                    distinctUniverseWitness, retryAfterFailureWitness >>

(***************************************************************************)
(* A later demand reuses a ready universe. The target check can still reject *)
(* that demand; rejection does not remove or replace the cached group.      *)
(***************************************************************************)
ReuseReady(d) ==
    LET u == UniverseOf[d]
        nextState ==
            IF TargetAccepted[d] THEN "Ready" ELSE "Rejected"
        nextDemandState ==
            [demandState EXCEPT ![d] = nextState]
    IN  /\ demandState[d] = "Pending"
        /\ cacheState[u] = "Ready"
        /\ demandState' = nextDemandState
        /\ demandGroup' =
            [demandGroup EXCEPT
                ![d] = IF TargetAccepted[d] THEN groupOf[u] ELSE 0]
        /\ reuseWitness' = TRUE
        /\ targetIsolationWitness' =
            (
                targetIsolationWitness
                \/ \E accepted, rejected \in Demands :
                    /\ UniverseOf[accepted] = u
                    /\ UniverseOf[rejected] = u
                    /\ nextDemandState[accepted] = "Ready"
                    /\ nextDemandState[rejected] = "Rejected"
            )
        /\ UNCHANGED << cacheState, groupOf, leader, nextGroupId, joinWitness,
                        distinctUniverseWitness, retryAfterFailureWitness >>

(***************************************************************************)
(* Successful realization publishes one group for the universe. Every      *)
(* attached demand evaluates its own required targets against that result.  *)
(***************************************************************************)
CompleteSuccess(u) ==
    LET gid == nextGroupId
        waiting ==
            {d \in Demands :
                /\ UniverseOf[d] = u
                /\ demandState[d] \in {"Leading", "Joined"}}
        nextDemandState ==
            [d \in Demands |->
                IF d \in waiting
                THEN IF TargetAccepted[d] THEN "Ready" ELSE "Rejected"
                ELSE demandState[d]]
    IN  /\ cacheState[u] = "InFlight"
        /\ UniverseValid[u]
        /\ cacheState' = [cacheState EXCEPT ![u] = "Ready"]
        /\ groupOf' = [groupOf EXCEPT ![u] = gid]
        /\ leader' = [leader EXCEPT ![u] = NoDemand]
        /\ demandState' = nextDemandState
        /\ demandGroup' =
            [d \in Demands |->
                IF d \in waiting /\ TargetAccepted[d]
                THEN gid
                ELSE demandGroup[d]]
        /\ nextGroupId' = nextGroupId + 1
        /\ targetIsolationWitness' =
            (
                targetIsolationWitness
                \/ \E accepted, rejected \in waiting :
                    /\ nextDemandState[accepted] = "Ready"
                    /\ nextDemandState[rejected] = "Rejected"
            )
        /\ distinctUniverseWitness' =
            (
                distinctUniverseWitness
                \/ \E other \in Universes \ {u} :
                    cacheState[other] = "Ready"
            )
        /\ UNCHANGED << joinWitness, reuseWitness,
                        retryAfterFailureWitness >>

(***************************************************************************)
(* A failed or invalid universe publishes nothing and is not cached. Every  *)
(* attached demand sees failure, allowing a later demand to retry.          *)
(***************************************************************************)
CompleteFailure(u) ==
    LET waiting ==
            {d \in Demands :
                /\ UniverseOf[d] = u
                /\ demandState[d] \in {"Leading", "Joined"}}
    IN  /\ cacheState[u] = "InFlight"
        /\ cacheState' = [cacheState EXCEPT ![u] = "Absent"]
        /\ groupOf' = [groupOf EXCEPT ![u] = 0]
        /\ leader' = [leader EXCEPT ![u] = NoDemand]
        /\ demandState' =
            [d \in Demands |->
                IF d \in waiting THEN "Failed" ELSE demandState[d]]
        /\ UNCHANGED << demandGroup, nextGroupId, joinWitness, reuseWitness,
                        targetIsolationWitness, distinctUniverseWitness,
                        retryAfterFailureWitness >>

Next ==
    \/ \E d \in Demands : Admit(d) \/ Join(d) \/ ReuseReady(d)
    \/ \E u \in Universes :
        CompleteSuccess(u) \/ CompleteFailure(u)

Fairness ==
    /\ \A d \in Demands :
        WF_vars(Admit(d) \/ Join(d) \/ ReuseReady(d))
    /\ \A u \in Universes :
        WF_vars(CompleteSuccess(u) \/ CompleteFailure(u))

Spec == Init /\ [][Next]_vars /\ Fairness

(***************************************************************************)
(* Safety.                                                                 *)
(***************************************************************************)

SingleFlightPerUniverse ==
    \A u \in Universes :
        Cardinality(
            {d \in Demands :
                UniverseOf[d] = u /\ demandState[d] = "Leading"}
        ) <= 1

CacheStateConsistent ==
    /\ \A u \in Universes :
        (cacheState[u] = "InFlight") <=> (leader[u] # NoDemand)
    /\ \A u \in Universes :
        (cacheState[u] = "Ready") <=> (groupOf[u] # 0)

ReadyDemandUsesExactCachedGroup ==
    \A d \in Demands :
        demandState[d] = "Ready" =>
            /\ cacheState[UniverseOf[d]] = "Ready"
            /\ demandGroup[d] = groupOf[UniverseOf[d]]

ReadyDemandHasAcceptedTargets ==
    \A d \in Demands :
        demandState[d] = "Ready" => TargetAccepted[d]

RejectedTargetDoesNotPoisonUniverse ==
    \A d \in Demands :
        demandState[d] = "Rejected" =>
            /\ ~TargetAccepted[d]
            /\ cacheState[UniverseOf[d]] = "Ready"
            /\ demandGroup[d] = 0

FailedDemandReceivesNoGroup ==
    \A d \in Demands :
        demandState[d] = "Failed" => demandGroup[d] = 0

ReadyDemandsForOneUniverseShareGroup ==
    \A d1, d2 \in Demands :
        (
            /\ UniverseOf[d1] = UniverseOf[d2]
            /\ demandState[d1] = "Ready"
            /\ demandState[d2] = "Ready"
        ) => demandGroup[d1] = demandGroup[d2]

DistinctReadyUniversesHaveDistinctGroups ==
    \A u1, u2 \in Universes :
        (
            /\ u1 # u2
            /\ cacheState[u1] = "Ready"
            /\ cacheState[u2] = "Ready"
        ) => groupOf[u1] # groupOf[u2]

(***************************************************************************)
(* Liveness.                                                               *)
(***************************************************************************)

EveryDemandEventuallyResolves ==
    \A d \in Demands :
        (demandState[d] = "Pending")
            ~> (demandState[d] \in TerminalStates)

(***************************************************************************)
(* Reachability probes. Each negated witness is checked in a separate       *)
(* configuration that TLC is expected to report as violated.               *)
(***************************************************************************)

NoJoinObserved == ~joinWitness
NoReuseObserved == ~reuseWitness
NoTargetIsolationObserved == ~targetIsolationWitness
NoDistinctUniversesObserved == ~distinctUniverseWitness
NoRetryAfterFailureObserved == ~retryAfterFailureWitness

=============================================================================
