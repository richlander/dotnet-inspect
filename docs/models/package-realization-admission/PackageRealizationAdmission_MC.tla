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
    g1, g2, g3, g4,
    s1, s2, s3, s4,
    strictOptions, looseOptions,
    d1, d2, d3,
    BaseScenario, OptionScenario, ContentScenario, SelectionScenario,
    DuplicateScenario, RootOnlyScenario, ReorderedScenario,
    CancellationScenario,
    Scenario,
    AllowLeaseAfterClose,
    AllowReleaseWithActiveLease,
    AllowLatePublish,
    AllowDoubleCleanup,
    AllowResurrection,
    AllowInexactReuse,
    AllowPartialPublish,
    AllowCancellationAbandon,
    AllowCancellationFailure

ASSUME
    Scenario
        \in {BaseScenario, OptionScenario, ContentScenario,
             SelectionScenario, DuplicateScenario, RootOnlyScenario,
             ReorderedScenario, CancellationScenario}

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
    doubleReturnWitness

RequestSequenceOfMC ==
    (d1 :> <<c1, c2>>)
        @@ (d2 :> <<c1, c2>>)
        @@ (
            d3 :>
                CASE Scenario \in {
                        OptionScenario, ContentScenario, SelectionScenario,
                        CancellationScenario
                    } -> <<c1, c2>>
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

BaseGeneration ==
    (c1 :> g1) @@ (c2 :> g2) @@ (c3 :> g3)

AlternateGeneration ==
    (c1 :> g1) @@ (c2 :> g4) @@ (c3 :> g3)

GenerationOfMC ==
    (d1 :> BaseGeneration)
        @@ (d2 :> BaseGeneration)
        @@ (
            d3 :>
                IF Scenario = ContentScenario
                THEN AlternateGeneration
                ELSE BaseGeneration
        )

BaseSelection ==
    (c1 :> s1) @@ (c2 :> s2) @@ (c3 :> s3)

AlternateSelection ==
    (c1 :> s1) @@ (c2 :> s4) @@ (c3 :> s3)

SelectionOfMC ==
    (d1 :> BaseSelection)
        @@ (d2 :> BaseSelection)
        @@ (
            d3 :>
                IF Scenario = SelectionScenario
                THEN AlternateSelection
                ELSE BaseSelection
        )

INSTANCE PackageRealizationAdmission WITH
    PackageCoordinates <- {c1, c2, c3},
    Generations <- {g1, g2, g3, g4},
    Selections <- {s1, s2, s3, s4},
    Options <- {strictOptions, looseOptions},
    Demands <- {d1, d2, d3},
    RequestSequenceOf <- RequestSequenceOfMC,
    GenerationOf <- GenerationOfMC,
    SelectionOf <- SelectionOfMC,
    OptionsOf <- OptionsOfMC

=============================================================================
