------------------- MODULE ArtifactContextRealization_MC -------------------
(***************************************************************************)
(* Model-checking harness with three universe keys. Two demands share one   *)
(* valid universe but have different target outcomes; one demand targets an *)
(* invalid universe; two demands target another valid universe so both      *)
(* distinct-group and reuse behavior are reachable.                         *)
(***************************************************************************)
EXTENDS Naturals, TLC

CONSTANTS u1, u2, u3, d1, d2, d3, d4, d5

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

UniverseOfMC ==
    (d1 :> u1)
        @@ (d2 :> u1)
        @@ (d3 :> u2)
        @@ (d4 :> u3)
        @@ (d5 :> u3)

UniverseValidMC ==
    (u1 :> TRUE) @@ (u2 :> FALSE) @@ (u3 :> TRUE)

TargetAcceptedMC ==
    (d1 :> TRUE)
        @@ (d2 :> FALSE)
        @@ (d3 :> TRUE)
        @@ (d4 :> TRUE)
        @@ (d5 :> TRUE)

INSTANCE ArtifactContextRealization WITH
    Universes <- {u1, u2, u3},
    Demands <- {d1, d2, d3, d4, d5},
    UniverseOf <- UniverseOfMC,
    UniverseValid <- UniverseValidMC,
    TargetAccepted <- TargetAcceptedMC

=============================================================================
