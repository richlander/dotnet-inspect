-------------- MODULE PackageRealizationAdmission_RootOnlyMutation_MC --------------
EXTENDS Naturals, TLC

CONSTANTS
    c1,
    g1,
    s1,
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

INSTANCE PackageRealizationAdmission WITH
    PackageCoordinates <- {c1},
    Generations <- {g1},
    Selections <- {s1},
    Options <- {strictOptions},
    Demands <- {d1},
    RequestBindingsOf <- (d1 :> <<>>),
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
    AllowDuplicateBindingAsDistinct <- FALSE,
    AllowRootOnlyAdmission <- TRUE

=============================================================================
