-------------------- MODULE DeadlineStreamLifecycle --------------------
EXTENDS Naturals, TLC

StreamStates ==
    {"Open", "Eof", "Disposing", "Disposed"}

ReadPhases ==
    {"Idle", "Checking", "Reading", "InnerDone", "WaitingAbort", "Done"}

InnerPlans ==
    {"Data", "Eof", "DeadlineAbort", "StallUntilAbort"}

InnerResults ==
    {"None", "Data", "Eof", "DeadlineAbort", "Abort"}

ReadResults ==
    {"None", "Data", "Eof", "ReadCanceled", "CallerCanceled",
     "OperationTimeout", "RequestTimeout", "TransportFailure"}

DeadlineStates ==
    {"Active", "Completing", "Completed"}

DeadlineOwners ==
    {"None", "Eof", "Dispose"}

AbortStates ==
    {"Idle", "Running", "Done"}

AbortOrigins ==
    {"None", "Callback", "Read"}

DisposeModes ==
    {"None", "Sync", "Async"}

DisposePhases ==
    {"Idle", "Inner", "Deadline", "Owner", "Done"}

OwnerDisposeStates ==
    {"Idle", "Running", "Done"}

DeadlineResults ==
    {"CallerCanceled", "OperationTimeout", "RequestTimeout"}

VARIABLE state

vars == <<state>>

InitialState(innerPlan) ==
    [stream |-> "Open",
     readPhase |-> "Idle",
     innerPlan |-> innerPlan,
     innerResult |-> "None",
     readResult |-> "None",
     readCancelled |-> FALSE,
     callerCancelled |-> FALSE,
     operationExpired |-> FALSE,
     requestExpired |-> FALSE,
     endOfStream |-> FALSE,
     deadlineState |-> "Active",
     deadlineOwner |-> "None",
     abortState |-> "Idle",
     abortOrigin |-> "None",
     abortFailure |-> FALSE,
     abortStarts |-> 0,
     disposeMode |-> "None",
     disposePhase |-> "Idle",
     callerOwnerDispose |-> "Idle",
     resultIncludesAbortFailure |-> FALSE,
     resultWrites |-> 0,
     transportFailureObserved |-> FALSE,
     readCancellationWitness |-> TRUE,
     precedenceWitness |-> TRUE,
     successWitness |-> TRUE]

Init ==
    state \in {InitialState(innerPlan) : innerPlan \in InnerPlans}

DeadlineExpired ==
    state.callerCancelled
    \/ state.operationExpired
    \/ state.requestExpired

ReadCancellationWinsNow ==
    /\ state.readCancelled
    /\ (state.readPhase = "Checking"
        \/ (state.readPhase = "InnerDone"
            /\ (state.innerResult \in {"Data", "Eof"}
                \/ DeadlineExpired)))

ExpectedDeadlineResult ==
    IF state.callerCancelled
    THEN "CallerCanceled"
    ELSE IF state.operationExpired
         THEN "OperationTimeout"
         ELSE "RequestTimeout"

DeadlinePrecedenceHolds(result) ==
    /\ state.callerCancelled => result = "CallerCanceled"
    /\ ~state.callerCancelled /\ state.operationExpired =>
        result = "OperationTimeout"
    /\ ~state.callerCancelled
       /\ ~state.operationExpired
       /\ state.requestExpired =>
        result = "RequestTimeout"

WithReadResult(result, includesAbortFailure, precedenceOK, successOK) ==
    [state EXCEPT
        !.readPhase = "Done",
        !.readResult = result,
        !.resultIncludesAbortFailure = includesAbortFailure,
        !.resultWrites = @ + 1,
        !.precedenceWitness = @ /\ precedenceOK,
        !.successWitness = @ /\ successOK]

WithReadCancellationResult ==
    LET producedResult == "ReadCanceled"
    IN [WithReadResult(
            producedResult,
            FALSE,
            TRUE,
            TRUE) EXCEPT
            !.readCancellationWitness =
                @
                /\ ReadCancellationWinsNow
                /\ producedResult = "ReadCanceled"]

WithTransportFailureResult ==
    LET producedResult == "TransportFailure"
    IN [WithReadResult(
            producedResult,
            FALSE,
            TRUE,
            TRUE) EXCEPT
            !.transportFailureObserved = TRUE]

WithAbortStarted(origin, nextReadPhase) ==
    [state EXCEPT
        !.readPhase = nextReadPhase,
        !.abortState = "Running",
        !.abortOrigin = origin,
        !.abortStarts = @ + 1]

CancelRead ==
    /\ ~state.readCancelled
    /\ state.readPhase # "Done"
    /\ state' = [state EXCEPT !.readCancelled = TRUE]

CancelCaller ==
    /\ ~state.callerCancelled
    /\ state' = [state EXCEPT !.callerCancelled = TRUE]

ExpireOperation ==
    /\ ~state.operationExpired
    /\ state' = [state EXCEPT !.operationExpired = TRUE]

ExpireRequest ==
    /\ ~state.requestExpired
    /\ state' = [state EXCEPT !.requestExpired = TRUE]

StartRead ==
    /\ state.stream = "Open"
    /\ state.readPhase = "Idle"
    /\ state' = [state EXCEPT !.readPhase = "Checking"]

CheckReadStartCanceled ==
    /\ state.readPhase = "Checking"
    /\ ReadCancellationWinsNow
    /\ state' = WithReadCancellationResult

CheckReadStartDeadline ==
    /\ state.readPhase = "Checking"
    /\ ~ReadCancellationWinsNow
    /\ DeadlineExpired
    /\ IF state.abortState = "Idle"
       THEN state' =
                [WithAbortStarted("Read", "WaitingAbort") EXCEPT
                    !.readCancellationWitness =
                        @ /\ ~ReadCancellationWinsNow]
       ELSE state' =
                [state EXCEPT
                    !.readPhase = "WaitingAbort",
                    !.readCancellationWitness =
                        @ /\ ~ReadCancellationWinsNow]

CheckReadStartContinue ==
    /\ state.readPhase = "Checking"
    /\ ~ReadCancellationWinsNow
    /\ ~DeadlineExpired
    /\ state' = [state EXCEPT !.readPhase = "Reading"]

CompletePlannedInner ==
    /\ state.readPhase = "Reading"
    /\ state.innerPlan \in {"Data", "Eof", "DeadlineAbort"}
    /\ state' =
        [state EXCEPT
            !.readPhase = "InnerDone",
            !.innerResult = state.innerPlan]

CompleteStalledInner ==
    /\ state.readPhase = "Reading"
    /\ state.innerPlan = "StallUntilAbort"
    /\ (state.abortState = "Done"
        \/ state.callerOwnerDispose = "Done")
    /\ state' =
        [state EXCEPT
            !.readPhase = "InnerDone",
            !.innerResult = "Abort"]

ObserveCanceledInner ==
    /\ state.readPhase = "InnerDone"
    /\ ReadCancellationWinsNow
    /\ state' = WithReadCancellationResult

ObserveDeadlineAfterInner ==
    /\ state.readPhase = "InnerDone"
    /\ ~ReadCancellationWinsNow
    /\ DeadlineExpired
    /\ IF state.abortState = "Idle"
       THEN state' =
                [WithAbortStarted("Read", "WaitingAbort") EXCEPT
                    !.readCancellationWitness =
                        @ /\ ~ReadCancellationWinsNow]
       ELSE state' =
                [state EXCEPT
                    !.readPhase = "WaitingAbort",
                    !.readCancellationWitness =
                        @ /\ ~ReadCancellationWinsNow]

ObserveData ==
    /\ state.readPhase = "InnerDone"
    /\ ~ReadCancellationWinsNow
    /\ ~DeadlineExpired
    /\ state.innerResult = "Data"
    /\ state' =
        WithReadResult(
            "Data",
            FALSE,
            TRUE,
            ~DeadlineExpired)

ObserveEof ==
    /\ state.readPhase = "InnerDone"
    /\ ~ReadCancellationWinsNow
    /\ ~DeadlineExpired
    /\ state.innerResult = "Eof"
    /\ state' =
        [WithReadResult(
            "Eof",
            FALSE,
            TRUE,
            ~DeadlineExpired) EXCEPT
            !.endOfStream = TRUE,
            !.stream =
                IF state.stream = "Open"
                THEN "Eof"
                ELSE @]

ObserveTransportFailure ==
    /\ state.readPhase = "InnerDone"
    /\ ~DeadlineExpired
    /\ state.innerResult \in {"DeadlineAbort", "Abort"}
    /\ state' = WithTransportFailureResult

ClassifyDeadlineAfterAbort ==
    /\ state.readPhase = "WaitingAbort"
    /\ state.abortState = "Done"
    /\ DeadlineExpired
    /\ LET producedResult == ExpectedDeadlineResult
       IN state' =
            WithReadResult(
                producedResult,
                state.abortFailure,
                DeadlinePrecedenceHolds(producedResult),
                TRUE)

StartAbortCallback ==
    /\ state.deadlineState = "Active"
    /\ DeadlineExpired
    /\ state.abortState = "Idle"
    /\ state' = WithAbortStarted("Callback", state.readPhase)

CompleteAbortWithoutFailure ==
    /\ state.abortState = "Running"
    /\ state' =
        [state EXCEPT
            !.abortState = "Done",
            !.abortFailure = FALSE]

CompleteAbortWithFailure ==
    /\ state.abortState = "Running"
    /\ state' =
        [state EXCEPT
            !.abortState = "Done",
            !.abortFailure = TRUE]

StartSyncDispose ==
    /\ state.stream \in {"Open", "Eof"}
    /\ state.disposeMode = "None"
    /\ state' =
        [state EXCEPT
            !.stream = "Disposing",
            !.disposeMode = "Sync",
            !.disposePhase = "Inner"]

StartAsyncDispose ==
    /\ state.stream \in {"Open", "Eof"}
    /\ state.disposeMode = "None"
    /\ state' =
        [state EXCEPT
            !.stream = "Disposing",
            !.disposeMode = "Async",
            !.disposePhase = "Inner"]

CompleteInnerDispose ==
    /\ state.disposePhase = "Inner"
    /\ state' =
        [state EXCEPT
            !.disposePhase =
                IF state.disposeMode = "Sync"
                THEN "Owner"
                ELSE "Deadline"]

StartCallerOwnerDispose ==
    /\ state.disposePhase = "Owner"
    /\ state.callerOwnerDispose = "Idle"
    /\ state' =
        [state EXCEPT
            !.callerOwnerDispose = "Running"]

CompleteCallerOwnerDispose ==
    /\ state.disposePhase = "Owner"
    /\ state.callerOwnerDispose = "Running"
    /\ state' =
        [state EXCEPT
            !.callerOwnerDispose = "Done",
            !.disposePhase =
                IF state.disposeMode = "Sync"
                THEN "Deadline"
                ELSE "Done",
            !.stream =
                IF state.disposeMode = "Async"
                THEN "Disposed"
                ELSE @]

BeginEofDeadlineCompletion ==
    /\ state.deadlineState = "Active"
    /\ state.endOfStream
    /\ state' =
        [state EXCEPT
            !.deadlineState = "Completing",
            !.deadlineOwner = "Eof"]

BeginDisposeDeadlineCompletion ==
    /\ state.deadlineState = "Active"
    /\ state.disposePhase = "Deadline"
    /\ state' =
        [state EXCEPT
            !.deadlineState = "Completing",
            !.deadlineOwner = "Dispose"]

CompleteDeadlineState ==
    /\ state.deadlineState = "Completing"
    /\ ~(state.abortState = "Running"
         /\ state.abortOrigin = "Callback")
    /\ state' =
        [state EXCEPT
            !.deadlineState = "Completed",
            !.disposePhase =
                IF state.deadlineOwner = "Dispose"
                   /\ state.disposePhase = "Deadline"
                THEN IF state.disposeMode = "Sync"
                     THEN "Done"
                     ELSE "Owner"
                ELSE @,
            !.stream =
                IF state.deadlineOwner = "Dispose"
                   /\ state.disposePhase = "Deadline"
                   /\ state.disposeMode = "Sync"
                THEN "Disposed"
                ELSE @]

AdvancePastClaimedDeadline ==
    /\ state.disposePhase = "Deadline"
    /\ state.deadlineOwner = "Eof"
    /\ state.deadlineState \in {"Completing", "Completed"}
    /\ state' =
        [state EXCEPT
            !.disposePhase =
                IF state.disposeMode = "Sync"
                THEN "Done"
                ELSE "Owner",
            !.stream =
                IF state.disposeMode = "Sync"
                THEN "Disposed"
                ELSE @]

ReadProgress ==
    CheckReadStartCanceled
    \/ CheckReadStartDeadline
    \/ CheckReadStartContinue
    \/ CompletePlannedInner
    \/ CompleteStalledInner
    \/ ObserveCanceledInner
    \/ ObserveDeadlineAfterInner
    \/ ObserveData
    \/ ObserveEof
    \/ ObserveTransportFailure
    \/ ClassifyDeadlineAfterAbort

AbortProgress ==
    StartAbortCallback
    \/ CompleteAbortWithoutFailure
    \/ CompleteAbortWithFailure

DisposeProgress ==
    CompleteInnerDispose
    \/ StartCallerOwnerDispose
    \/ CompleteCallerOwnerDispose
    \/ AdvancePastClaimedDeadline

DeadlineProgress ==
    BeginEofDeadlineCompletion
    \/ BeginDisposeDeadlineCompletion
    \/ CompleteDeadlineState

Next ==
    CancelRead
    \/ CancelCaller
    \/ ExpireOperation
    \/ ExpireRequest
    \/ StartRead
    \/ ReadProgress
    \/ AbortProgress
    \/ StartSyncDispose
    \/ StartAsyncDispose
    \/ DisposeProgress
    \/ DeadlineProgress

Spec ==
    /\ Init
    /\ [][Next]_vars
    /\ WF_vars(ReadProgress)
    /\ WF_vars(AbortProgress)
    /\ WF_vars(DisposeProgress)
    /\ WF_vars(DeadlineProgress)

TypeOK ==
    state \in
        [stream : StreamStates,
         readPhase : ReadPhases,
         innerPlan : InnerPlans,
         innerResult : InnerResults,
         readResult : ReadResults,
         readCancelled : BOOLEAN,
         callerCancelled : BOOLEAN,
         operationExpired : BOOLEAN,
         requestExpired : BOOLEAN,
         endOfStream : BOOLEAN,
         deadlineState : DeadlineStates,
         deadlineOwner : DeadlineOwners,
         abortState : AbortStates,
         abortOrigin : AbortOrigins,
         abortFailure : BOOLEAN,
         abortStarts : 0..2,
         disposeMode : DisposeModes,
         disposePhase : DisposePhases,
         callerOwnerDispose : OwnerDisposeStates,
         resultIncludesAbortFailure : BOOLEAN,
         resultWrites : 0..2,
         transportFailureObserved : BOOLEAN,
         readCancellationWitness : BOOLEAN,
         precedenceWitness : BOOLEAN,
         successWitness : BOOLEAN]

ResultShapeIsConsistent ==
    /\ (state.readResult = "None") <=> (state.resultWrites = 0)
    /\ (state.readPhase = "Done") <=> (state.readResult # "None")
    /\ state.resultIncludesAbortFailure =>
        state.readResult \in DeadlineResults

ReadResultIsWrittenAtMostOnce ==
    state.resultWrites <= 1

ReadCancellationPrecedesDeadlineTranslation ==
    state.readCancellationWitness

TransportFailureIsNotReclassified ==
    state.transportFailureObserved =>
        state.readResult = "TransportFailure"

ClassificationFollowsPrecedence ==
    state.precedenceWitness

NoLateSuccess ==
    state.successWitness

EofDisarmsDeadlineTranslation ==
    state.endOfStream =>
        /\ state.readResult = "Eof"
        /\ state.stream \in {"Eof", "Disposing", "Disposed"}

AbortFailureIsRetained ==
    /\ state.abortFailure
       /\ state.readResult \in DeadlineResults =>
        state.resultIncludesAbortFailure

AbortStartsAtMostOnce ==
    state.abortStarts <= 1

DeadlineOwnershipIsSafe ==
    /\ (state.deadlineState = "Active") <=>
        (state.deadlineOwner = "None")
    /\ state.deadlineState = "Completed" =>
        ~(state.abortState = "Running"
          /\ state.abortOrigin = "Callback")

CompletedDisposalLeavesDeadlineOwned ==
    state.disposePhase = "Done" =>
        /\ state.stream = "Disposed"
        /\ state.deadlineState \in {"Completing", "Completed"}
        /\ state.deadlineOwner # "None"
        /\ state.callerOwnerDispose = "Done"

StartedAbortEventuallyCompletes ==
    [](state.abortState = "Running" => <>(state.abortState = "Done"))

StartedDisposalEventuallyCompletes ==
    [](state.disposeMode # "None" => <>(state.disposePhase = "Done"))

ImmediateReadEventuallyCompletes ==
    []((state.readPhase # "Idle"
        /\ state.innerPlan # "StallUntilAbort") =>
        <>(state.readPhase = "Done"))

UnblockedStalledReadEventuallyCompletes ==
    []((state.readPhase # "Idle"
        /\ state.innerPlan = "StallUntilAbort"
        /\ (DeadlineExpired \/ state.disposeMode # "None")) =>
        <>(state.readPhase = "Done"))

EofEventuallyCompletesDeadline ==
    [](state.endOfStream => <>(state.deadlineState = "Completed"))

======================================================================
