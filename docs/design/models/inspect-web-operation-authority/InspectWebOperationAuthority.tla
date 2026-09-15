----------------------- MODULE InspectWebOperationAuthority -----------------------
EXTENDS FiniteSets, Naturals, TLC

CONSTANTS
    OperationA,
    OperationB,
    NoOperation,
    MaxProgress,
    Mutation

Operations == {OperationA, OperationB}

OwnerActive == "OwnerActive"
OwnerDisposed == "OwnerDisposed"
OwnerPhases == {OwnerActive, OwnerDisposed}

NotStarted == "NotStarted"
Queued == "Queued"
Running == "Running"
Succeeded == "Succeeded"
Failed == "Failed"
Canceled == "Canceled"
ProducerPhases == {NotStarted, Queued, Running, Succeeded, Failed, Canceled}
ProducerTerminal == {Succeeded, Failed, Canceled}

NoOutcome == "NoOutcome"
SuccessOutcome == "SuccessOutcome"
FailureOutcome == "FailureOutcome"
CanceledOutcome == "CanceledOutcome"
LogicalOutcomes ==
    {NoOutcome, SuccessOutcome, FailureOutcome, CanceledOutcome}

NoMutation == "None"
StaleProgress == "StaleProgress"
StaleSuccess == "StaleSuccess"
StaleFailure == "StaleFailure"
DuplicateTerminal == "DuplicateTerminal"
CleanupMutatesNewer == "CleanupMutatesNewer"
CallbackAfterRelease == "CallbackAfterRelease"
StartAfterDispose == "StartAfterDispose"
Mutations ==
    {NoMutation,
     StaleProgress,
     StaleSuccess,
     StaleFailure,
     DuplicateTerminal,
     CleanupMutatesNewer,
     CallbackAfterRelease,
     StartAfterDispose}

ASSUME
    /\ Cardinality(Operations) = 2
    /\ OperationA # OperationB
    /\ NoOperation \notin Operations
    /\ MaxProgress \in Nat
    /\ MaxProgress > 0
    /\ Mutation \in Mutations

VARIABLES
    ownerPhase,
    current,
    visibleOperation,
    producerPhase,
    logicalOutcome,
    completionCount,
    cancelRequested,
    cancelForwardCount,
    progressAttempts,
    progressDeliveries,
    released,
    postReleaseReportAttempted,
    publicationWasAuthorized,
    callbackObservedAfterRelease,
    operationStartedAfterDispose

vars ==
    <<ownerPhase,
      current,
      visibleOperation,
      producerPhase,
      logicalOutcome,
      completionCount,
      cancelRequested,
      cancelForwardCount,
      progressAttempts,
      progressDeliveries,
      released,
      postReleaseReportAttempted,
      publicationWasAuthorized,
      callbackObservedAfterRelease,
      operationStartedAfterDispose>>

Init ==
    /\ ownerPhase = OwnerActive
    /\ current = NoOperation
    /\ visibleOperation = NoOperation
    /\ producerPhase = [op \in Operations |-> NotStarted]
    /\ logicalOutcome = [op \in Operations |-> NoOutcome]
    /\ completionCount = [op \in Operations |-> 0]
    /\ cancelRequested = [op \in Operations |-> FALSE]
    /\ cancelForwardCount = [op \in Operations |-> 0]
    /\ progressAttempts = [op \in Operations |-> 0]
    /\ progressDeliveries = [op \in Operations |-> 0]
    /\ released = [op \in Operations |-> FALSE]
    /\ postReleaseReportAttempted = [op \in Operations |-> FALSE]
    /\ publicationWasAuthorized = TRUE
    /\ callbackObservedAfterRelease = FALSE
    /\ operationStartedAfterDispose = FALSE

HasPublicationAuthority(op) ==
    /\ ownerPhase = OwnerActive
    /\ current = op
    /\ logicalOutcome[op] = NoOutcome

StartFirst ==
    /\ ownerPhase = OwnerActive
    /\ current = NoOperation
    /\ producerPhase[OperationA] = NotStarted
    /\ current' = OperationA
    /\ visibleOperation' = OperationA
    /\ producerPhase' = [producerPhase EXCEPT ![OperationA] = Queued]
    /\ UNCHANGED
        <<ownerPhase,
          logicalOutcome,
          completionCount,
          cancelRequested,
          cancelForwardCount,
          progressAttempts,
          progressDeliveries,
          released,
          postReleaseReportAttempted,
          publicationWasAuthorized,
          callbackObservedAfterRelease,
          operationStartedAfterDispose>>

StartSecond ==
    /\ ownerPhase = OwnerActive
    /\ producerPhase[OperationA] # NotStarted
    /\ producerPhase[OperationB] = NotStarted
    /\ current' = OperationB
    /\ visibleOperation' = OperationB
    /\ producerPhase' = [producerPhase EXCEPT ![OperationB] = Queued]
    /\ logicalOutcome' =
        IF logicalOutcome[OperationA] = NoOutcome
        THEN [logicalOutcome EXCEPT ![OperationA] = CanceledOutcome]
        ELSE logicalOutcome
    /\ completionCount' =
        IF logicalOutcome[OperationA] = NoOutcome
        THEN [completionCount EXCEPT ![OperationA] = @ + 1]
        ELSE completionCount
    /\ cancelRequested' =
        IF logicalOutcome[OperationA] = NoOutcome
        THEN [cancelRequested EXCEPT ![OperationA] = TRUE]
        ELSE cancelRequested
    /\ cancelForwardCount' =
        IF logicalOutcome[OperationA] = NoOutcome
        THEN [cancelForwardCount EXCEPT ![OperationA] = @ + 1]
        ELSE cancelForwardCount
    /\ UNCHANGED
        <<ownerPhase,
          progressAttempts,
          progressDeliveries,
          released,
          postReleaseReportAttempted,
          publicationWasAuthorized,
          callbackObservedAfterRelease,
          operationStartedAfterDispose>>

RequestCancel(op) ==
    /\ HasPublicationAuthority(op)
    /\ producerPhase[op] \in {Queued, Running}
    /\ logicalOutcome' = [logicalOutcome EXCEPT ![op] = CanceledOutcome]
    /\ completionCount' = [completionCount EXCEPT ![op] = @ + 1]
    /\ cancelRequested' = [cancelRequested EXCEPT ![op] = TRUE]
    /\ cancelForwardCount' = [cancelForwardCount EXCEPT ![op] = @ + 1]
    /\ UNCHANGED
        <<ownerPhase,
          current,
          visibleOperation,
          producerPhase,
          progressAttempts,
          progressDeliveries,
          released,
          postReleaseReportAttempted,
          publicationWasAuthorized,
          callbackObservedAfterRelease,
          operationStartedAfterDispose>>

BeginProducer(op) ==
    /\ producerPhase[op] = Queued
    /\ producerPhase' = [producerPhase EXCEPT ![op] = Running]
    /\ UNCHANGED
        <<ownerPhase,
          current,
          visibleOperation,
          logicalOutcome,
          completionCount,
          cancelRequested,
          cancelForwardCount,
          progressAttempts,
          progressDeliveries,
          released,
          postReleaseReportAttempted,
          publicationWasAuthorized,
          callbackObservedAfterRelease,
          operationStartedAfterDispose>>

CancelQueuedProducer(op) ==
    /\ producerPhase[op] = Queued
    /\ cancelRequested[op]
    /\ producerPhase' = [producerPhase EXCEPT ![op] = Canceled]
    /\ UNCHANGED
        <<ownerPhase,
          current,
          visibleOperation,
          logicalOutcome,
          completionCount,
          cancelRequested,
          cancelForwardCount,
          progressAttempts,
          progressDeliveries,
          released,
          postReleaseReportAttempted,
          publicationWasAuthorized,
          callbackObservedAfterRelease,
          operationStartedAfterDispose>>

ReportProgress(op) ==
    /\ producerPhase[op] = Running
    /\ progressAttempts[op] < MaxProgress
    /\ progressAttempts' = [progressAttempts EXCEPT ![op] = @ + 1]
    /\ progressDeliveries' =
        IF HasPublicationAuthority(op)
           \/ (Mutation = StaleProgress /\ ~HasPublicationAuthority(op))
        THEN [progressDeliveries EXCEPT ![op] = @ + 1]
        ELSE progressDeliveries
    /\ visibleOperation' =
        IF Mutation = StaleProgress /\ ~HasPublicationAuthority(op)
        THEN op
        ELSE visibleOperation
    /\ publicationWasAuthorized' =
        IF progressDeliveries'[op] > progressDeliveries[op]
           /\ ~HasPublicationAuthority(op)
        THEN FALSE
        ELSE publicationWasAuthorized
    /\ UNCHANGED
        <<ownerPhase,
          current,
          producerPhase,
          logicalOutcome,
          completionCount,
          cancelRequested,
          cancelForwardCount,
          released,
          postReleaseReportAttempted,
          callbackObservedAfterRelease,
          operationStartedAfterDispose>>

CompleteSuccess(op) ==
    /\ producerPhase[op] = Running
    /\ producerPhase' = [producerPhase EXCEPT ![op] = Succeeded]
    /\ logicalOutcome' =
        IF HasPublicationAuthority(op)
        THEN [logicalOutcome EXCEPT ![op] = SuccessOutcome]
        ELSE IF Mutation = DuplicateTerminal
                /\ logicalOutcome[op] # NoOutcome
             THEN [logicalOutcome EXCEPT ![op] = SuccessOutcome]
             ELSE logicalOutcome
    /\ completionCount' =
        IF HasPublicationAuthority(op)
           \/ (Mutation = DuplicateTerminal
               /\ logicalOutcome[op] # NoOutcome)
        THEN [completionCount EXCEPT ![op] = @ + 1]
        ELSE completionCount
    /\ visibleOperation' =
        IF Mutation = StaleSuccess /\ ~HasPublicationAuthority(op)
        THEN op
        ELSE visibleOperation
    /\ publicationWasAuthorized' =
        IF Mutation = StaleSuccess /\ ~HasPublicationAuthority(op)
        THEN FALSE
        ELSE publicationWasAuthorized
    /\ UNCHANGED
        <<ownerPhase,
          current,
          cancelRequested,
          cancelForwardCount,
          progressAttempts,
          progressDeliveries,
          released,
          postReleaseReportAttempted,
          callbackObservedAfterRelease,
          operationStartedAfterDispose>>

CompleteFailure(op) ==
    /\ producerPhase[op] = Running
    /\ producerPhase' = [producerPhase EXCEPT ![op] = Failed]
    /\ logicalOutcome' =
        IF HasPublicationAuthority(op)
        THEN [logicalOutcome EXCEPT ![op] = FailureOutcome]
        ELSE logicalOutcome
    /\ completionCount' =
        IF HasPublicationAuthority(op)
        THEN [completionCount EXCEPT ![op] = @ + 1]
        ELSE completionCount
    /\ visibleOperation' =
        IF Mutation = StaleFailure /\ ~HasPublicationAuthority(op)
        THEN op
        ELSE visibleOperation
    /\ publicationWasAuthorized' =
        IF Mutation = StaleFailure /\ ~HasPublicationAuthority(op)
        THEN FALSE
        ELSE publicationWasAuthorized
    /\ UNCHANGED
        <<ownerPhase,
          current,
          cancelRequested,
          cancelForwardCount,
          progressAttempts,
          progressDeliveries,
          released,
          postReleaseReportAttempted,
          callbackObservedAfterRelease,
          operationStartedAfterDispose>>

CompleteCanceled(op) ==
    /\ producerPhase[op] = Running
    /\ cancelRequested[op]
    /\ producerPhase' = [producerPhase EXCEPT ![op] = Canceled]
    /\ UNCHANGED
        <<ownerPhase,
          current,
          visibleOperation,
          logicalOutcome,
          completionCount,
          cancelRequested,
          cancelForwardCount,
          progressAttempts,
          progressDeliveries,
          released,
          postReleaseReportAttempted,
          publicationWasAuthorized,
          callbackObservedAfterRelease,
          operationStartedAfterDispose>>

SettleProducer(op) ==
    \/ CompleteSuccess(op)
    \/ CompleteFailure(op)
    \/ CompleteCanceled(op)

Release(op) ==
    /\ producerPhase[op] \in ProducerTerminal
    /\ ~released[op]
    /\ released' = [released EXCEPT ![op] = TRUE]
    /\ visibleOperation' =
        IF Mutation = CleanupMutatesNewer /\ current # op
        THEN op
        ELSE visibleOperation
    /\ UNCHANGED
        <<ownerPhase,
          current,
          producerPhase,
          logicalOutcome,
          completionCount,
          cancelRequested,
          cancelForwardCount,
          progressAttempts,
          progressDeliveries,
          postReleaseReportAttempted,
          publicationWasAuthorized,
          callbackObservedAfterRelease,
          operationStartedAfterDispose>>

ReportAfterRelease(op) ==
    /\ released[op]
    /\ ~postReleaseReportAttempted[op]
    /\ postReleaseReportAttempted' =
        [postReleaseReportAttempted EXCEPT ![op] = TRUE]
    /\ progressDeliveries' =
        IF Mutation = CallbackAfterRelease
        THEN [progressDeliveries EXCEPT ![op] = @ + 1]
        ELSE progressDeliveries
    /\ callbackObservedAfterRelease' =
        IF progressDeliveries'[op] > progressDeliveries[op]
        THEN TRUE
        ELSE callbackObservedAfterRelease
    /\ UNCHANGED
        <<ownerPhase,
          current,
          visibleOperation,
          producerPhase,
          logicalOutcome,
          completionCount,
          cancelRequested,
          cancelForwardCount,
          progressAttempts,
          released,
          publicationWasAuthorized,
          operationStartedAfterDispose>>

DisposeOwner ==
    /\ ownerPhase = OwnerActive
    /\ ownerPhase' = OwnerDisposed
    /\ current' = NoOperation
    /\ visibleOperation' = NoOperation
    /\ logicalOutcome' =
        IF current \in Operations /\ logicalOutcome[current] = NoOutcome
        THEN [logicalOutcome EXCEPT ![current] = CanceledOutcome]
        ELSE logicalOutcome
    /\ completionCount' =
        IF current \in Operations /\ logicalOutcome[current] = NoOutcome
        THEN [completionCount EXCEPT ![current] = @ + 1]
        ELSE completionCount
    /\ cancelRequested' =
        IF current \in Operations /\ logicalOutcome[current] = NoOutcome
        THEN [cancelRequested EXCEPT ![current] = TRUE]
        ELSE cancelRequested
    /\ cancelForwardCount' =
        IF current \in Operations /\ logicalOutcome[current] = NoOutcome
        THEN [cancelForwardCount EXCEPT ![current] = @ + 1]
        ELSE cancelForwardCount
    /\ UNCHANGED
        <<producerPhase,
          progressAttempts,
          progressDeliveries,
          released,
          postReleaseReportAttempted,
          publicationWasAuthorized,
          callbackObservedAfterRelease,
          operationStartedAfterDispose>>

StartSecondAfterDispose ==
    /\ Mutation = StartAfterDispose
    /\ ownerPhase = OwnerDisposed
    /\ producerPhase[OperationB] = NotStarted
    /\ current' = OperationB
    /\ visibleOperation' = OperationB
    /\ producerPhase' = [producerPhase EXCEPT ![OperationB] = Queued]
    /\ operationStartedAfterDispose' = TRUE
    /\ UNCHANGED
        <<ownerPhase,
          logicalOutcome,
          completionCount,
          cancelRequested,
          cancelForwardCount,
          progressAttempts,
          progressDeliveries,
          released,
          postReleaseReportAttempted,
          publicationWasAuthorized,
          callbackObservedAfterRelease>>

Next ==
    \/ StartFirst
    \/ StartSecond
    \/ \E op \in Operations : RequestCancel(op)
    \/ \E op \in Operations : BeginProducer(op)
    \/ \E op \in Operations : CancelQueuedProducer(op)
    \/ \E op \in Operations : ReportProgress(op)
    \/ \E op \in Operations : SettleProducer(op)
    \/ \E op \in Operations : Release(op)
    \/ \E op \in Operations : ReportAfterRelease(op)
    \/ DisposeOwner
    \/ StartSecondAfterDispose

TypeOK ==
    /\ ownerPhase \in OwnerPhases
    /\ current \in Operations \cup {NoOperation}
    /\ visibleOperation \in Operations \cup {NoOperation}
    /\ producerPhase \in [Operations -> ProducerPhases]
    /\ logicalOutcome \in [Operations -> LogicalOutcomes]
    /\ completionCount \in [Operations -> Nat]
    /\ cancelRequested \in [Operations -> BOOLEAN]
    /\ cancelForwardCount \in [Operations -> Nat]
    /\ progressAttempts \in [Operations -> 0..MaxProgress]
    /\ progressDeliveries \in [Operations -> Nat]
    /\ released \in [Operations -> BOOLEAN]
    /\ postReleaseReportAttempted \in [Operations -> BOOLEAN]
    /\ publicationWasAuthorized \in BOOLEAN
    /\ callbackObservedAfterRelease \in BOOLEAN
    /\ operationStartedAfterDispose \in BOOLEAN

OneLogicalCompletion ==
    \A op \in Operations : completionCount[op] <= 1

OutcomeCountAgrees ==
    \A op \in Operations :
        (logicalOutcome[op] = NoOutcome) <=> (completionCount[op] = 0)

CancellationForwardedAtMostOnce ==
    \A op \in Operations : cancelForwardCount[op] <= 1

PublicationRequiresAuthority ==
    publicationWasAuthorized

VisibleStateOwnedByCurrent ==
    IF ownerPhase = OwnerActive
    THEN visibleOperation = current
    ELSE /\ current = NoOperation
         /\ visibleOperation = NoOperation

ReleasedProducerIsTerminal ==
    \A op \in Operations : released[op] => producerPhase[op] \in ProducerTerminal

NoCallbackAfterRelease ==
    ~callbackObservedAfterRelease

DisposedOwnerStartsNothing ==
    ~operationStartedAfterDispose

StartedEventuallySettles ==
    \A op \in Operations :
        (producerPhase[op] # NotStarted)
        ~> (producerPhase[op] \in ProducerTerminal)

StartedEventuallyCompletesLogically ==
    \A op \in Operations :
        (producerPhase[op] # NotStarted)
        ~> (logicalOutcome[op] # NoOutcome)

SettledEventuallyReleases ==
    \A op \in Operations :
        (producerPhase[op] \in ProducerTerminal)
        ~> released[op]

Spec ==
    /\ Init
    /\ [][Next]_vars
    /\ WF_vars(StartFirst)
    /\ WF_vars(StartSecond)
    /\ \A op \in Operations : WF_vars(BeginProducer(op))
    /\ \A op \in Operations : WF_vars(CancelQueuedProducer(op))
    /\ \A op \in Operations : WF_vars(SettleProducer(op))
    /\ \A op \in Operations : WF_vars(Release(op))

=============================================================================
