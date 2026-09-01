---------------------- MODULE PackageSourceComposition ----------------------
EXTENDS FiniteSets, TLC

\* Owned by docs/design/package-source-model.md.
\* Classification, mapping, source protocol, authentication, persistent cache
\* construction, and payload bytes are inputs or explicit non-claims. The
\* model owns only package-level route, association, aggregate, and shared
\* operation-ceiling interactions.

CONSTANTS
    AuthorityOne,
    AuthorityTwo,
    PrimaryRoute,
    FallbackRoute,
    PeerRoute,
    NoAuthority,
    OperationKind,
    BindingMode,
    CompletionMode,
    DeadlineMode,
    FailureMode

Authorities == {AuthorityOne, AuthorityTwo}
Routes == {PrimaryRoute, FallbackRoute, PeerRoute}

ASSUME
    /\ Cardinality(Authorities) = 2
    /\ Cardinality(Routes) = 3
    /\ NoAuthority \notin Authorities
    /\ OperationKind \in {"Discovery", "Pinned"}
    /\ BindingMode \in {"Association", "Producer"}
    /\ CompletionMode \in {"AllAuthorities", "HealthySubset"}
    /\ DeadlineMode \in {"Terminal", "Restart"}
    /\ FailureMode \in {"Visible", "AsAbsence"}

\* Every route deliberately has the same abstract producer. RouteAuthority is
\* the only distinction the package owner may use to recover authority.
RouteAuthority ==
    [route \in Routes |->
        IF route = PeerRoute THEN AuthorityTwo ELSE AuthorityOne]

RouteStates ==
    {"Idle", "Running", "Found", "Absent",
     "RequestTimeout", "Failed", "Skipped"}
AuthorityStates == {"Pending", "Found", "Absent", "Failed"}
AggregateStates == {"Pending", "Complete", "Partial", "Failed", "Payload"}

VARIABLES
    routeState,
    authorityState,
    adoptedAuthority,
    terminalFailure,
    aggregate,
    operationExpired,
    payloadRoute,
    payloadAuthority

vars ==
    <<routeState, authorityState, adoptedAuthority, terminalFailure,
      aggregate, operationExpired, payloadRoute, payloadAuthority>>

Init ==
    /\ routeState = [route \in Routes |-> "Idle"]
    /\ authorityState = [authority \in Authorities |-> "Pending"]
    /\ adoptedAuthority = [route \in Routes |-> NoAuthority]
    /\ terminalFailure = [authority \in Authorities |-> FALSE]
    /\ aggregate = "Pending"
    /\ operationExpired = FALSE
    /\ payloadRoute = PrimaryRoute
    /\ payloadAuthority = NoAuthority

IsTerminalRoute(route) ==
    route \in {FallbackRoute, PeerRoute}

CanStart(route) ==
    /\ routeState[route] = "Idle"
    /\ CASE route = PrimaryRoute -> TRUE
          [] route = PeerRoute -> TRUE
          [] OTHER ->
                routeState[PrimaryRoute]
                    \in {"RequestTimeout", "Failed"}

CanAdvance ==
    /\ aggregate = "Pending"
    /\ (~operationExpired \/ DeadlineMode = "Restart")

StartRoute(route) ==
    /\ CanAdvance
    /\ authorityState[RouteAuthority[route]] = "Pending"
    /\ CanStart(route)
    /\ routeState' =
        [routeState EXCEPT ![route] = "Running"]
    /\ UNCHANGED
        <<authorityState, adoptedAuthority, terminalFailure, aggregate,
          operationExpired, payloadRoute, payloadAuthority>>

AdoptionAllowed(route, authority) ==
    /\ authority \in Authorities
    /\ (BindingMode = "Producer"
        \/ authority = RouteAuthority[route])

RouteFound(route, authority) ==
    /\ CanAdvance
    /\ routeState[route] = "Running"
    /\ AdoptionAllowed(route, authority)
    /\ routeState' = [routeState EXCEPT ![route] = "Found"]
    /\ authorityState' =
        [authorityState EXCEPT ![authority] = "Found"]
    /\ adoptedAuthority' =
        [adoptedAuthority EXCEPT ![route] = authority]
    /\ UNCHANGED
        <<terminalFailure, aggregate, operationExpired, payloadRoute,
          payloadAuthority>>

RouteAbsent(route, authority) ==
    /\ CanAdvance
    /\ routeState[route] = "Running"
    /\ AdoptionAllowed(route, authority)
    /\ routeState' = [routeState EXCEPT ![route] = "Absent"]
    /\ authorityState' =
        [authorityState EXCEPT ![authority] = "Absent"]
    /\ adoptedAuthority' =
        [adoptedAuthority EXCEPT ![route] = authority]
    /\ UNCHANGED
        <<terminalFailure, aggregate, operationExpired, payloadRoute,
          payloadAuthority>>

FinalFailureState ==
    IF FailureMode = "Visible" THEN "Failed" ELSE "Absent"

RouteRequestTimeout(route, authority) ==
    /\ CanAdvance
    /\ routeState[route] = "Running"
    /\ AdoptionAllowed(route, authority)
    /\ routeState' =
        [routeState EXCEPT ![route] = "RequestTimeout"]
    /\ authorityState' =
        IF IsTerminalRoute(route)
        THEN
            [authorityState EXCEPT ![authority] = FinalFailureState]
        ELSE authorityState
    /\ terminalFailure' =
        IF IsTerminalRoute(route)
        THEN
            [terminalFailure EXCEPT ![authority] = TRUE]
        ELSE terminalFailure
    /\ adoptedAuthority' =
        [adoptedAuthority EXCEPT ![route] = authority]
    /\ UNCHANGED
        <<aggregate, operationExpired, payloadRoute, payloadAuthority>>

RouteTransportFailure(route, authority) ==
    /\ CanAdvance
    /\ routeState[route] = "Running"
    /\ AdoptionAllowed(route, authority)
    /\ routeState' = [routeState EXCEPT ![route] = "Failed"]
    /\ authorityState' =
        IF IsTerminalRoute(route)
        THEN
            [authorityState EXCEPT ![authority] = FinalFailureState]
        ELSE authorityState
    /\ terminalFailure' =
        IF IsTerminalRoute(route)
        THEN
            [terminalFailure EXCEPT ![authority] = TRUE]
        ELSE terminalFailure
    /\ adoptedAuthority' =
        [adoptedAuthority EXCEPT ![route] = authority]
    /\ UNCHANGED
        <<aggregate, operationExpired, payloadRoute, payloadAuthority>>

ExpireOperation ==
    /\ aggregate = "Pending"
    /\ ~operationExpired
    /\ operationExpired' = TRUE
    /\ aggregate' =
        IF DeadlineMode = "Terminal" THEN "Failed" ELSE aggregate
    /\ routeState' =
        IF DeadlineMode = "Terminal"
        THEN
            [route \in Routes |->
                IF routeState[route] = "Running"
                THEN "Skipped"
                ELSE routeState[route]]
        ELSE routeState
    /\ UNCHANGED
        <<authorityState, adoptedAuthority, terminalFailure, payloadRoute,
          payloadAuthority>>

EveryAuthoritySettled ==
    \A authority \in Authorities:
        authorityState[authority] # "Pending"

SomeAuthoritySettled ==
    \E authority \in Authorities:
        authorityState[authority] # "Pending"

SomeUsableEvidence ==
    \E authority \in Authorities:
        authorityState[authority] \in {"Found", "Absent"}

AllEvidenceAuthoritative ==
    \A authority \in Authorities:
        authorityState[authority] \in {"Found", "Absent"}

DiscoveryReady ==
    IF CompletionMode = "AllAuthorities"
    THEN EveryAuthoritySettled
    ELSE SomeAuthoritySettled

DiscoveryOutcome ==
    IF CompletionMode = "HealthySubset" /\ SomeUsableEvidence
    THEN "Complete"
    ELSE
        IF AllEvidenceAuthoritative
        THEN "Complete"
        ELSE
            IF SomeUsableEvidence
            THEN "Partial"
            ELSE "Failed"

FinalizeDiscovery ==
    /\ OperationKind = "Discovery"
    /\ CanAdvance
    /\ DiscoveryReady
    /\ aggregate' = DiscoveryOutcome
    /\ UNCHANGED
        <<routeState, authorityState, adoptedAuthority, terminalFailure,
          operationExpired, payloadRoute, payloadAuthority>>

PublishPinned(route) ==
    /\ OperationKind = "Pinned"
    /\ CanAdvance
    /\ routeState[route] = "Found"
    /\ adoptedAuthority[route] # NoAuthority
    /\ aggregate' = "Payload"
    /\ payloadRoute' = route
    /\ payloadAuthority' = adoptedAuthority[route]
    /\ UNCHANGED
        <<routeState, authorityState, adoptedAuthority, terminalFailure,
          operationExpired>>

FinalizePinnedFailure ==
    /\ OperationKind = "Pinned"
    /\ CanAdvance
    /\ EveryAuthoritySettled
    /\ ~(\E authority \in Authorities:
            authorityState[authority] = "Found")
    /\ aggregate' = "Failed"
    /\ UNCHANGED
        <<routeState, authorityState, adoptedAuthority, terminalFailure,
          operationExpired, payloadRoute, payloadAuthority>>

StartSomeRoute ==
    \E route \in Routes:
        StartRoute(route)

SettleSomeRoute ==
    \/ \E route \in Routes, authority \in Authorities:
        RouteFound(route, authority)
        \/ RouteAbsent(route, authority)
        \/ RouteRequestTimeout(route, authority)
        \/ RouteTransportFailure(route, authority)

PublishSomePinnedPayload ==
    \E route \in Routes:
        PublishPinned(route)

Finalize ==
    FinalizeDiscovery \/ FinalizePinnedFailure

Next ==
    StartSomeRoute
    \/ SettleSomeRoute
    \/ ExpireOperation
    \/ PublishSomePinnedPayload
    \/ Finalize

Spec ==
    /\ Init
    /\ [][Next]_vars
    /\ WF_vars(StartSomeRoute)
    /\ WF_vars(SettleSomeRoute)
    /\ WF_vars(PublishSomePinnedPayload)
    /\ WF_vars(Finalize)

TypeOK ==
    /\ routeState \in [Routes -> RouteStates]
    /\ authorityState \in [Authorities -> AuthorityStates]
    /\ adoptedAuthority
        \in [Routes -> Authorities \union {NoAuthority}]
    /\ terminalFailure \in [Authorities -> BOOLEAN]
    /\ aggregate \in AggregateStates
    /\ operationExpired \in BOOLEAN
    /\ payloadRoute \in Routes
    /\ payloadAuthority \in Authorities \union {NoAuthority}

AdoptedResultsKeepAssociation ==
    \A route \in Routes:
        adoptedAuthority[route] # NoAuthority =>
            adoptedAuthority[route] = RouteAuthority[route]

AbsentResultsKeepAssociation ==
    \A route \in Routes:
        routeState[route] = "Absent" =>
            adoptedAuthority[route] = RouteAuthority[route]

TerminalFailureResultsKeepAssociation ==
    \A route \in Routes:
        /\ IsTerminalRoute(route)
        /\ routeState[route] \in {"RequestTimeout", "Failed"}
        =>
            adoptedAuthority[route] = RouteAuthority[route]

CompleteRequiresEveryAuthority ==
    aggregate = "Complete" =>
        \A authority \in Authorities:
            authorityState[authority] \in {"Found", "Absent"}

CompleteAbsenceIsAuthoritative ==
    /\ aggregate = "Complete"
    /\ ~(\E authority \in Authorities:
            authorityState[authority] = "Found")
    =>
        \A authority \in Authorities:
            authorityState[authority] = "Absent"

TerminalFailuresRemainVisible ==
    \A authority \in Authorities:
        terminalFailure[authority] =>
            authorityState[authority] = "Failed"

PartialResultsAreExplicitlyIncomplete ==
    aggregate = "Partial" =>
        /\ SomeUsableEvidence
        /\ \E authority \in Authorities:
            authorityState[authority] = "Failed"

OperationTimeoutIsTerminal ==
    operationExpired => aggregate = "Failed"

PayloadKeepsReportingAuthority ==
    aggregate = "Payload" =>
        payloadAuthority = RouteAuthority[payloadRoute]

PayloadPublishesBeforeOperationTimeout ==
    aggregate = "Payload" => ~operationExpired

AggregateSettles ==
    <> (aggregate # "Pending")

PartialAfterSourceFailureObserved ==
    /\ OperationKind = "Discovery"
    /\ aggregate = "Partial"
    /\ \E authority \in Authorities:
        authorityState[authority] = "Failed"

PartialAfterSourceFailureNotObserved ==
    ~PartialAfterSourceFailureObserved

RequestTimeoutFallbackObserved ==
    /\ routeState[PrimaryRoute] = "RequestTimeout"
    /\ routeState[FallbackRoute] = "Found"

RequestTimeoutFallbackNotObserved ==
    ~RequestTimeoutFallbackObserved

PinnedSuccessWithPeerFailureObserved ==
    /\ OperationKind = "Pinned"
    /\ aggregate = "Payload"
    /\ authorityState[AuthorityTwo] = "Failed"
    /\ payloadAuthority = AuthorityOne

PinnedSuccessWithPeerFailureNotObserved ==
    ~PinnedSuccessWithPeerFailureObserved

OperationTimeoutObserved == operationExpired

OperationTimeoutNotObserved == ~OperationTimeoutObserved

=============================================================================
