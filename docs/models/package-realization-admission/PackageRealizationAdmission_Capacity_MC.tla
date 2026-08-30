---------------- MODULE PackageRealizationAdmission_Capacity_MC ----------------
EXTENDS Naturals, TLC

CONSTANTS
    c1, c2,
    g1, g2,
    s1, s2,
    strictOptions, largeOptions,
    d1, d2,
    EntryCapacityScenario,
    InFlightCapacityScenario,
    ByteCapacityScenario,
    ZeroReservationScenario,
    Scenario,
    AllowOverCapacity

ASSUME
    Scenario
        \in {
            EntryCapacityScenario,
            InFlightCapacityScenario,
            ByteCapacityScenario,
            ZeroReservationScenario
        }

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
    (d1 :> <<<<c1, g1, s1>>>>)
        @@ (d2 :> <<<<c2, g2, s2>>>>)

MaxEntriesMC ==
    IF Scenario = EntryCapacityScenario THEN 1 ELSE 2

MaxInFlightMC ==
    IF Scenario = InFlightCapacityScenario THEN 1 ELSE 2

INSTANCE PackageRealizationAdmission WITH
    PackageCoordinates <- {c1, c2},
    Generations <- {g1, g2},
    Selections <- {s1, s2},
    Options <- {strictOptions, largeOptions},
    Demands <- {d1, d2},
    RequestBindingsOf <- RequestBindingsOfMC,
    OptionsOf <-
        IF Scenario = ByteCapacityScenario
        THEN (d1 :> strictOptions @@ d2 :> largeOptions)
        ELSE [d \in {d1, d2} |-> strictOptions],
    ReservationOf <-
        IF Scenario = ByteCapacityScenario
        THEN (d1 :> 1 @@ d2 :> 2)
        ELSE IF Scenario = ZeroReservationScenario
        THEN [d \in {d1, d2} |-> 0]
        ELSE [d \in {d1, d2} |-> 1],
    MaxEntries <- MaxEntriesMC,
    MaxInFlight <- MaxInFlightMC,
    MaxReservedByteUnits <- 2,
    AllowLeaseAfterClose <- FALSE,
    AllowReleaseWithActiveLease <- FALSE,
    AllowLatePublish <- FALSE,
    AllowDoubleCleanup <- FALSE,
    AllowResurrection <- FALSE,
    AllowInexactReuse <- FALSE,
    AllowPartialPublish <- FALSE,
    AllowCancellationAbandon <- FALSE,
    AllowCancellationFailure <- FALSE,
    AllowDuplicateBindingAsDistinct <- FALSE

ZeroReservationAdmissionObserved ==
    /\ Scenario = ZeroReservationScenario
    /\ ReservedByteUnits = 0
    /\ \E c \in Coordinates :
        /\ cacheState[c] = "InFlight"
        /\ cacheOperation[c] # 0

NoZeroReservationAdmissionObserved ==
    ~ZeroReservationAdmissionObserved

=============================================================================
