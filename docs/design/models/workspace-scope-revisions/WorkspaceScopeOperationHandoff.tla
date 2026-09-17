-------------------- MODULE WorkspaceScopeOperationHandoff --------------------
(***************************************************************************)
(* Workspace Scope-owned request/result association boundary.              *)
(*                                                                         *)
(* Consumers bind the live variables through a named INSTANCE. The module  *)
(* deliberately excludes validation, mutation admission, physical work,    *)
(* and membership policy; it owns only the inert issue/submit lifecycle,    *)
(* exact terminal association, requested-occurrence projection, and typed  *)
(* cancellation-control distinction already locked by Scope.               *)
(***************************************************************************)
EXTENDS Naturals, FiniteSets

CONSTANTS
    Operations,
    NoOperation,
    NoWorkspace,
    NoKind,
    NoSnapshot,
    NoTarget,
    NoRequestedOccurrence,
    NoOutcome,
    InitialSnapshot,
    SuccessfulOutcomes,
    TerminalOutcomes,
    UnavailableOutcome,
    TargetOccurrence(_, _),
    WrongOccurrence(_, _),
    Fault

VARIABLES
    requests,
    requestStates,
    submissionCounts,
    settlementCounts,
    results,
    cancellationResponses

vars ==
    <<requests, requestStates, submissionCounts, settlementCounts, results,
      cancellationResponses>>

Association(workspace, operation, kind, revision, hasExplicitTarget) ==
    [workspace |-> workspace,
     operation |-> operation,
     kind |-> kind,
     expectedRevision |-> revision,
     hasExplicitTarget |-> hasExplicitTarget]

Request(
        workspace,
        operation,
        kind,
        revision,
        hasBaseGuard,
        baseGuard,
        deadline,
        input,
        target,
        evidence) ==
    [association |->
        Association(
            workspace,
            operation,
            kind,
            revision,
            target # NoTarget),
     operation |-> operation,
     workspace |-> workspace,
     revision |-> revision,
     hasBaseGuard |-> hasBaseGuard,
     baseGuard |-> baseGuard,
     deadline |-> deadline,
     kind |-> kind,
     input |-> input,
     target |-> target,
     evidence |-> evidence]

EmptyRequest ==
    Request(
        NoWorkspace,
        NoOperation,
        NoKind,
        0,
        FALSE,
        0,
        0,
        <<>>,
        NoTarget,
        FALSE)

RequestedFor(request, outcome, snapshot) ==
    IF outcome \notin SuccessfulOutcomes
        \/ ~request.association.hasExplicitTarget
    THEN NoRequestedOccurrence
    ELSE IF Fault = "RequestedOccurrence"
    THEN WrongOccurrence(request, snapshot)
    ELSE TargetOccurrence(request, snapshot)

ResultFor(request, outcome, reason, snapshot, superseder) ==
    LET originalAssociation == request.association
        association ==
            IF Fault = "Association" /\ outcome = "Superseded"
            THEN [originalAssociation EXCEPT !.operation = superseder]
            ELSE originalAssociation
    IN
    [association |-> association,
     operation |-> association.operation,
     outcome |-> outcome,
     reason |-> reason,
     snapshot |-> snapshot,
     requested |-> RequestedFor(request, outcome, snapshot),
     superseder |->
        IF outcome = "Superseded" THEN superseder ELSE NoOperation,
     authority |->
        IF outcome = UnavailableOutcome
        THEN "Historical"
        ELSE "Settlement"]

EmptyResult ==
    [association |->
        Association(
            NoWorkspace,
            NoOperation,
            NoKind,
            0,
            FALSE),
     operation |-> NoOperation,
     outcome |-> NoOutcome,
     reason |-> "None",
     snapshot |-> InitialSnapshot,
     requested |-> NoRequestedOccurrence,
     superseder |-> NoOperation,
     authority |-> "None"]

CancellationResponse(kind, operation, observedOutcome, observedSettlements) ==
    [kind |-> kind,
     operation |-> operation,
     observedOutcome |-> observedOutcome,
     observedSettlements |-> observedSettlements]

EmptyCancellationResponse ==
    CancellationResponse("None", NoOperation, NoOutcome, 0)

Init ==
    /\ requests = [operation \in Operations |-> EmptyRequest]
    /\ requestStates = [operation \in Operations |-> "Unissued"]
    /\ submissionCounts = [operation \in Operations |-> 0]
    /\ settlementCounts = [operation \in Operations |-> 0]
    /\ results = [operation \in Operations |-> EmptyResult]
    /\ cancellationResponses =
        [operation \in Operations |-> EmptyCancellationResponse]

Issue(operation, request) ==
    /\ operation \in Operations
    /\ requestStates[operation] = "Unissued"
    /\ request.association.operation = operation
    /\ requests' = [requests EXCEPT ![operation] = request]
    /\ requestStates' =
        [requestStates EXCEPT ![operation] = "Issued"]
    /\ UNCHANGED
        <<submissionCounts, settlementCounts, results,
          cancellationResponses>>

Abandon(operation) ==
    /\ operation \in Operations
    /\ requestStates[operation] = "Issued"
    /\ requestStates' =
        [requestStates EXCEPT ![operation] = "Abandoned"]
    /\ UNCHANGED
        <<requests, submissionCounts, settlementCounts, results,
          cancellationResponses>>

Submit(operation) ==
    /\ operation \in Operations
    /\ requestStates[operation] = "Issued"
    /\ requestStates' =
        [requestStates EXCEPT ![operation] = "Submitted"]
    /\ submissionCounts' =
        [submissionCounts EXCEPT ![operation] = @ + 1]
    /\ UNCHANGED
        <<requests, settlementCounts, results, cancellationResponses>>

ValidResult(operation, result) ==
    /\ operation \in Operations
    /\ result.outcome \in TerminalOutcomes
    /\ result.association = requests[operation].association
    /\ result.operation = operation
    /\ result.authority =
        IF result.outcome = UnavailableOutcome
        THEN "Historical"
        ELSE "Settlement"
    /\ IF result.outcome = "Superseded"
       THEN /\ result.superseder \in Operations \ {operation}
            /\ requests[result.superseder].association.operation =
                result.superseder
       ELSE result.superseder = NoOperation
    /\ IF result.outcome \in SuccessfulOutcomes
            /\ requests[operation].association.hasExplicitTarget
       THEN /\ result.requested # NoRequestedOccurrence
            /\ result.requested =
                TargetOccurrence(requests[operation], result.snapshot)
       ELSE result.requested = NoRequestedOccurrence

SubmitAndSettle(
        operation,
        outcome,
        reason,
        snapshot,
        superseder) ==
    /\ operation \in Operations
    /\ requestStates[operation] = "Issued"
    /\ outcome \in TerminalOutcomes
    /\ requestStates' =
        [requestStates EXCEPT ![operation] = "Settled"]
    /\ submissionCounts' =
        [submissionCounts EXCEPT ![operation] = @ + 1]
    /\ settlementCounts' =
        [settlementCounts EXCEPT ![operation] = @ + 1]
    /\ results' =
        [results EXCEPT
            ![operation] =
                ResultFor(
                    requests[operation],
                    outcome,
                    reason,
                    snapshot,
                    superseder)]
    /\ UNCHANGED <<requests, cancellationResponses>>

Settle(operation, outcome, reason, snapshot, superseder) ==
    /\ operation \in Operations
    /\ requestStates[operation] = "Submitted"
    /\ outcome \in TerminalOutcomes
    /\ requestStates' =
        [requestStates EXCEPT ![operation] = "Settled"]
    /\ settlementCounts' =
        [settlementCounts EXCEPT ![operation] = @ + 1]
    /\ results' =
        [results EXCEPT
            ![operation] =
                ResultFor(
                    requests[operation],
                    outcome,
                    reason,
                    snapshot,
                    superseder)]
    /\ UNCHANGED
        <<requests, submissionCounts, cancellationResponses>>

SettleSet(
        settling,
        outcomeByOperation,
        reasonByOperation,
        snapshotByOperation,
        supersederByOperation) ==
    /\ settling \subseteq Operations
    /\ settling # {}
    /\ \A operation \in settling :
        /\ requestStates[operation] = "Submitted"
        /\ outcomeByOperation[operation] \in TerminalOutcomes
    /\ requestStates' =
        [operation \in Operations |->
            IF operation \in settling
            THEN "Settled"
            ELSE requestStates[operation]]
    /\ settlementCounts' =
        [operation \in Operations |->
            IF operation \in settling
            THEN settlementCounts[operation] + 1
            ELSE settlementCounts[operation]]
    /\ results' =
        [operation \in Operations |->
            IF operation \in settling
            THEN ResultFor(
                requests[operation],
                outcomeByOperation[operation],
                reasonByOperation[operation],
                snapshotByOperation[operation],
                supersederByOperation[operation])
            ELSE results[operation]]
    /\ UNCHANGED
        <<requests, submissionCounts, cancellationResponses>>

AcceptCancellation(operation, observedOutcome) ==
    /\ operation \in Operations
    /\ requestStates[operation] = "Submitted"
    /\ cancellationResponses[operation].kind = "None"
    /\ cancellationResponses' =
        [cancellationResponses EXCEPT
            ![operation] =
                CancellationResponse(
                    "Accepted",
                    operation,
                    observedOutcome,
                    settlementCounts[operation])]
    /\ UNCHANGED
        <<requests, requestStates, submissionCounts, settlementCounts,
          results>>

ObserveCancellationNoEffect(operation, observedOutcome) ==
    /\ operation \in Operations
    /\ requestStates[operation] \in {"Issued", "Settled"}
    /\ cancellationResponses[operation].kind = "None"
    /\ cancellationResponses' =
        [cancellationResponses EXCEPT
            ![operation] =
                CancellationResponse(
                    "ObservedNoEffect",
                    operation,
                    observedOutcome,
                    settlementCounts[operation])]
    /\ UNCHANGED
        <<requests, requestStates, submissionCounts, settlementCounts,
          results>>

ReturnCancellationSettlement(operation) ==
    /\ operation \in Operations
    /\ requestStates[operation] = "Settled"
    /\ settlementCounts[operation] = 1
    /\ cancellationResponses[operation].kind = "None"
    /\ cancellationResponses' =
        [cancellationResponses EXCEPT
            ![operation] =
                CancellationResponse(
                    "Settlement",
                    results[operation].operation,
                    results[operation].outcome,
                    settlementCounts[operation])]
    /\ UNCHANGED
        <<requests, requestStates, submissionCounts, settlementCounts,
          results>>

Next ==
    \/ \E operation \in Operations :
        Issue(operation, requests'[operation])
    \/ \E operation \in Operations :
        Abandon(operation)
        \/ Submit(operation)
        \/ AcceptCancellation(
            operation,
            cancellationResponses'[operation].observedOutcome)
        \/ ObserveCancellationNoEffect(
            operation,
            cancellationResponses'[operation].observedOutcome)
        \/ ReturnCancellationSettlement(operation)
    \/ \E operation \in Operations :
        SubmitAndSettle(
            operation,
            results'[operation].outcome,
            results'[operation].reason,
            results'[operation].snapshot,
            results'[operation].superseder)
    \/ LET settling ==
            {operation \in Operations :
                requestStates[operation] = "Submitted"
                /\ requestStates'[operation] = "Settled"}
       IN
        SettleSet(
            settling,
            [operation \in Operations |->
                results'[operation].outcome],
            [operation \in Operations |->
                results'[operation].reason],
            [operation \in Operations |->
                results'[operation].snapshot],
            [operation \in Operations |->
                results'[operation].superseder])

SafetySpec == Init /\ [][Next]_vars

OwnerAssumptions ==
    /\ Operations # {}
    /\ IsFiniteSet(Operations)
    /\ NoOperation \notin Operations
    /\ SuccessfulOutcomes \subseteq TerminalOutcomes
    /\ UnavailableOutcome \in TerminalOutcomes

TypeOK ==
    /\ DOMAIN requests = Operations
    /\ DOMAIN requestStates = Operations
    /\ DOMAIN submissionCounts = Operations
    /\ DOMAIN settlementCounts = Operations
    /\ DOMAIN results = Operations
    /\ DOMAIN cancellationResponses = Operations
    /\ \A operation \in Operations :
        /\ requestStates[operation] \in
            {"Unissued", "Issued", "Abandoned", "Submitted", "Settled"}
        /\ submissionCounts[operation] \in 0..1
        /\ settlementCounts[operation] \in 0..1
        /\ settlementCounts[operation] <= submissionCounts[operation]
        /\ cancellationResponses[operation].kind \in
            {"None", "Accepted", "ObservedNoEffect", "Settlement"}

RequestLifecycleIsOneShot ==
    \A operation \in Operations :
        /\ CASE requestStates[operation] = "Unissued" ->
                /\ submissionCounts[operation] = 0
                /\ settlementCounts[operation] = 0
                /\ results[operation].outcome = NoOutcome
           [] requestStates[operation] = "Issued" ->
                /\ requests[operation].association.operation = operation
                /\ submissionCounts[operation] = 0
                /\ settlementCounts[operation] = 0
                /\ results[operation].outcome = NoOutcome
           [] requestStates[operation] = "Abandoned" ->
                /\ requests[operation].association.operation = operation
                /\ submissionCounts[operation] = 0
                /\ settlementCounts[operation] = 0
                /\ results[operation].outcome = NoOutcome
           [] requestStates[operation] = "Submitted" ->
                /\ requests[operation].association.operation = operation
                /\ submissionCounts[operation] = 1
                /\ settlementCounts[operation] = 0
                /\ results[operation].outcome = NoOutcome
           [] OTHER ->
                /\ requestStates[operation] = "Settled"
                /\ requests[operation].association.operation = operation
                /\ submissionCounts[operation] = 1
                /\ settlementCounts[operation] = 1
                /\ results[operation].outcome \in TerminalOutcomes

TerminalResultsPreserveAssociation ==
    \A operation \in Operations :
        requestStates[operation] = "Settled" =>
            ValidResult(operation, results[operation])

CancellationResponsesAreTyped ==
    \A operation \in Operations :
        /\ (cancellationResponses[operation].kind # "None" =>
                cancellationResponses[operation].operation = operation)
        /\ (cancellationResponses[operation].kind = "Settlement" =>
                /\ cancellationResponses[operation].observedSettlements = 1
                /\ cancellationResponses[operation].observedOutcome =
                    results[operation].outcome
                /\ cancellationResponses[operation].operation =
                    results[operation].operation)

IssueOrAbandonStep(operation) ==
    \/ requestStates[operation] = "Unissued"
        /\ requestStates'[operation] = "Issued"
    \/ requestStates[operation] = "Issued"
        /\ requestStates'[operation] = "Abandoned"

IssuedRequestIsFrozen ==
    [][\A operation \in Operations :
        requestStates[operation] # "Unissued"
        /\ requestStates'[operation] # "Unissued" =>
            requests'[operation] = requests[operation]]_vars

ControlNoEffectCannotSettleMutation ==
    [][\A operation \in Operations :
        cancellationResponses[operation].kind = "None"
        /\ cancellationResponses'[operation].kind = "ObservedNoEffect" =>
            /\ settlementCounts' = settlementCounts
            /\ results' = results]_vars

=============================================================================
