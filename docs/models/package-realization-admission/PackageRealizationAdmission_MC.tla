------------------- MODULE PackageRealizationAdmission_MC -------------------
(***************************************************************************)
(* Model-checking harness. Request sequences are defined here because TLC's *)
(* .cfg constant grammar does not accept sequence-valued function literals. *)
(* The scenario switch supplies focused duplicate, Root-only, and reordered *)
(* request bounds without enlarging every correctness run.                  *)
(***************************************************************************)
EXTENDS Naturals, TLC

CONSTANTS
    c1, c2, c3,
    strictOptions, looseOptions,
    d1, d2, d3, d4,
    BaseScenario, OptionScenario, DuplicateScenario, RootOnlyScenario,
    ReorderedScenario,
    Scenario,
    AllowLeaseAfterClose,
    AllowReleaseWithActiveLease,
    AllowLatePublish,
    AllowDoubleCleanup,
    AllowResurrection,
    AllowInexactReuse,
    AllowPartialPublish

ASSUME
    Scenario
        \in {BaseScenario, OptionScenario, DuplicateScenario,
             RootOnlyScenario, ReorderedScenario}

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

RequestSequenceOfMC ==
    (d1 :> <<c1, c2>>)
        @@ (d2 :> <<c1, c2>>)
        @@ (
            d3 :>
                CASE Scenario = OptionScenario -> <<c1, c2>>
                  [] Scenario = DuplicateScenario -> <<c1, c1>>
                  [] Scenario = RootOnlyScenario -> <<>>
                  [] Scenario = ReorderedScenario -> <<c2, c1>>
                  [] OTHER -> <<c2, c3>>
        )

OptionsOfMC ==
    (d1 :> strictOptions)
        @@ (d2 :> strictOptions)
        @@ (
            d3 :>
                IF Scenario = OptionScenario
                THEN looseOptions
                ELSE strictOptions
        )

INSTANCE PackageRealizationAdmission WITH
    PackageCoordinates <- {c1, c2, c3},
    Options <- {strictOptions, looseOptions},
    Demands <- {d1, d2, d3},
    RequestSequenceOf <- RequestSequenceOfMC,
    OptionsOf <- OptionsOfMC

=============================================================================
