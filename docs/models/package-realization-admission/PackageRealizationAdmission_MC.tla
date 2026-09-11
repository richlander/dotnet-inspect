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
    doubleReturnWitness,
    capacityRejectionWitness

BaseBindings == <<<<c1, g1, s1>>, <<c2, g2, s2>>>>

RequestBindingsOfMC ==
    (d1 :> BaseBindings)
        @@ (d2 :> BaseBindings)
        @@ (
            d3 :>
                CASE Scenario \in {
                        OptionScenario, CancellationScenario
                    } -> BaseBindings
                  [] Scenario = ContentScenario
                    -> <<<<c1, g1, s1>>, <<c2, g4, s2>>>>
                  [] Scenario = SelectionScenario
                    -> <<<<c1, g1, s1>>, <<c2, g2, s4>>>>
                  [] Scenario = DuplicateScenario
                    -> <<<<c1, g1, s1>>, <<c1, g4, s4>>>>
                  [] Scenario = RootOnlyScenario -> <<>>
                  [] Scenario = ReorderedScenario
                    -> <<<<c2, g2, s2>>, <<c1, g1, s1>>>>
                  [] OTHER -> <<<<c2, g2, s2>>, <<c3, g3, s3>>>>
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

ReservationOfMC == [d \in {d1, d2, d3} |-> 1]

INSTANCE PackageRealizationAdmission WITH
    PackageCoordinates <- {c1, c2, c3},
    Generations <- {g1, g2, g3, g4},
    Selections <- {s1, s2, s3, s4},
    Options <- {strictOptions, looseOptions},
    Demands <- {d1, d2, d3},
    RequestBindingsOf <- RequestBindingsOfMC,
    OptionsOf <- OptionsOfMC,
    ReservationOf <- ReservationOfMC,
    MaxEntries <- 2,
    MaxInFlight <- 2,
    MaxReservedByteUnits <- 2,
    AllowOverCapacity <- FALSE,
    AllowDuplicateBindingAsDistinct <- FALSE,
    AllowRootOnlyAdmission <- FALSE

=============================================================================
