------------------- MODULE PackageRealizationAdmission_MC -------------------
(***************************************************************************)
(* Model-checking harness: TLC's .cfg constant grammar does not accept a   *)
(* function-literal expression (`[d1 |-> c1, ...]`) directly as a CONSTANT *)
(* value, so the concrete `CoordinateOf` mapping used for checking is      *)
(* defined here instead, over the same model values the .cfg declares.    *)
(***************************************************************************)
EXTENDS Naturals, TLC

CONSTANTS
    c1, c2, d1, d2, d3, d4,
    AllowLeaseAfterClose,
    AllowReleaseWithActiveLease,
    AllowLatePublish,
    AllowDoubleCleanup,
    AllowResurrection

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

CoordinateOfMC == (d1 :> c1) @@ (d2 :> c1) @@ (d3 :> c2) @@ (d4 :> c2)

INSTANCE PackageRealizationAdmission WITH
    Coordinates <- {c1, c2},
    Demands <- {d1, d2, d3, d4},
    CoordinateOf <- CoordinateOfMC

================================================================================
