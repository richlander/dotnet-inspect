------------- MODULE PackageRealizationAdmission_DuplicateMutation_MC -------------
EXTENDS Naturals, TLC

CONSTANTS
    c1,
    g1, g2,
    s1, s2,
    strictOptions,
    d1

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

RequestBindingsOfMC ==
    d1 :> <<<<c1, g1, s1>>, <<c1, g2, s2>>>>

INSTANCE PackageRealizationAdmission WITH
    PackageCoordinates <- {c1},
    Generations <- {g1, g2},
    Selections <- {s1, s2},
    Options <- {strictOptions},
    Demands <- {d1},
    RequestBindingsOf <- RequestBindingsOfMC,
    OptionsOf <- (d1 :> strictOptions),
    ReservationOf <- (d1 :> 1),
    MaxEntries <- 1,
    MaxInFlight <- 1,
    MaxReservedByteUnits <- 1,
    AllowLeaseAfterClose <- FALSE,
    AllowReleaseWithActiveLease <- FALSE,
    AllowLatePublish <- FALSE,
    AllowDoubleCleanup <- FALSE,
    AllowResurrection <- FALSE,
    AllowInexactReuse <- FALSE,
    AllowPartialPublish <- FALSE,
    AllowCancellationAbandon <- FALSE,
    AllowCancellationFailure <- FALSE,
    AllowOverCapacity <- FALSE,
    AllowDuplicateBindingAsDistinct <- TRUE,
    AllowRootOnlyAdmission <- FALSE

=============================================================================
