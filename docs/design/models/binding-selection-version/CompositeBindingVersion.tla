------------------------- MODULE CompositeBindingVersion ----------------------
EXTENDS FiniteSets, Integers, TLC

\* Owned by docs/design/type-forwarding-resolution.md.
\* Delegate answers and routing behavior are abstract. The model owns only the
\* composite token attached to matching results, drift propagation, immutable
\* state refresh, route replacement, and retry progress.

CONSTANTS
    DelegateVersionOne,
    DelegateVersionTwo,
    CompositeVersionOne,
    CompositeVersionTwo,
    AnswerOne,
    AnswerTwo,
    NoVersion,
    NoAnswer,
    SuccessTokenMode,
    MismatchMode,
    RouteMode

DelegateVersions == {DelegateVersionOne, DelegateVersionTwo}
CompositeVersions == {CompositeVersionOne, CompositeVersionTwo}
Answers == {AnswerOne, AnswerTwo}
Scenarios == {"Stable", "DelegateDrift", "RouteChange"}
Phases == {"Ready", "Evaluating", "Retry", "Done"}

ASSUME
    /\ Cardinality(DelegateVersions) = 2
    /\ Cardinality(CompositeVersions) = 2
    /\ Cardinality(Answers) = 2
    /\ NoVersion \notin DelegateVersions \union CompositeVersions
    /\ NoAnswer \notin Answers
    /\ SuccessTokenMode \in {"Composite", "Delegate"}
    /\ MismatchMode \in
        {"RefreshAndForward", "Relabel", "ForwardWithoutRefresh"}
    /\ RouteMode \in {"FreshVersion", "SameVersion"}

VARIABLES
    phase,
    scenario,
    delegateVersion,
    compositeVersion,
    capturedDelegateVersion,
    routeGeneration,
    returnedVersion,
    returnedAnswer,
    interpreted,
    refreshed,
    completed

vars ==
    <<phase, scenario, delegateVersion, compositeVersion,
      capturedDelegateVersion, routeGeneration, returnedVersion,
      returnedAnswer, interpreted, refreshed, completed>>

Init ==
    /\ phase = "Ready"
    /\ scenario \in Scenarios
    /\ delegateVersion = DelegateVersionOne
    /\ compositeVersion = CompositeVersionOne
    /\ capturedDelegateVersion = DelegateVersionOne
    /\ routeGeneration = 1
    /\ returnedVersion = NoVersion
    /\ returnedAnswer = NoAnswer
    /\ interpreted = FALSE
    /\ refreshed = FALSE
    /\ completed = FALSE

Begin ==
    /\ phase = "Ready"
    /\ phase' = "Evaluating"
    /\ delegateVersion' =
        IF scenario = "DelegateDrift"
        THEN DelegateVersionTwo
        ELSE delegateVersion
    /\ routeGeneration' =
        IF scenario = "RouteChange"
        THEN 2
        ELSE routeGeneration
    /\ UNCHANGED
        <<scenario, compositeVersion, capturedDelegateVersion,
          returnedVersion, returnedAnswer, interpreted, refreshed, completed>>

EvaluateStable ==
    /\ phase = "Evaluating"
    /\ scenario = "Stable"
    /\ phase' = "Done"
    /\ returnedVersion' =
        IF SuccessTokenMode = "Composite"
        THEN compositeVersion
        ELSE delegateVersion
    /\ returnedAnswer' = AnswerOne
    /\ interpreted' = TRUE
    /\ completed' = (SuccessTokenMode = "Composite")
    /\ UNCHANGED
        <<scenario, delegateVersion, compositeVersion,
          capturedDelegateVersion, routeGeneration, refreshed>>

EvaluateDelegateDrift ==
    /\ phase = "Evaluating"
    /\ scenario = "DelegateDrift"
    /\ IF MismatchMode = "Relabel"
       THEN
            /\ phase' = "Done"
            /\ returnedVersion' = compositeVersion
            /\ returnedAnswer' = AnswerTwo
            /\ interpreted' = TRUE
            /\ completed' = TRUE
            /\ UNCHANGED
                <<compositeVersion, capturedDelegateVersion, refreshed>>
       ELSE
            /\ phase' = "Retry"
            /\ returnedVersion' = delegateVersion
            /\ returnedAnswer' = AnswerTwo
            /\ interpreted' = FALSE
            /\ completed' = FALSE
            /\ IF MismatchMode = "RefreshAndForward"
               THEN
                    /\ compositeVersion' = CompositeVersionTwo
                    /\ capturedDelegateVersion' = DelegateVersionTwo
                    /\ refreshed' = TRUE
               ELSE
                    /\ UNCHANGED
                        <<compositeVersion, capturedDelegateVersion, refreshed>>
    /\ UNCHANGED <<scenario, delegateVersion, routeGeneration>>

EvaluateRouteChange ==
    /\ phase = "Evaluating"
    /\ scenario = "RouteChange"
    /\ returnedAnswer' = AnswerTwo
    /\ interpreted' = TRUE
    /\ IF RouteMode = "FreshVersion"
       THEN
            /\ phase' = "Retry"
            /\ compositeVersion' = CompositeVersionTwo
            /\ returnedVersion' = CompositeVersionTwo
            /\ refreshed' = TRUE
            /\ completed' = FALSE
       ELSE
            /\ phase' = "Done"
            /\ returnedVersion' = compositeVersion
            /\ UNCHANGED <<compositeVersion, refreshed>>
            /\ completed' = TRUE
    /\ UNCHANGED
        <<scenario, delegateVersion, capturedDelegateVersion, routeGeneration>>

Retry ==
    /\ phase = "Retry"
    /\ refreshed
    /\ phase' = "Done"
    /\ returnedVersion' = compositeVersion
    /\ returnedAnswer' = AnswerTwo
    /\ interpreted' = TRUE
    /\ completed' = TRUE
    /\ UNCHANGED
        <<scenario, delegateVersion, compositeVersion,
          capturedDelegateVersion, routeGeneration, refreshed>>

Next ==
    Begin
    \/ EvaluateStable
    \/ EvaluateDelegateDrift
    \/ EvaluateRouteChange
    \/ Retry

Spec ==
    /\ Init
    /\ [][Next]_vars
    /\ WF_vars(Begin)
    /\ WF_vars(EvaluateStable)
    /\ WF_vars(EvaluateDelegateDrift)
    /\ WF_vars(EvaluateRouteChange)
    /\ WF_vars(Retry)

TypeOK ==
    /\ phase \in Phases
    /\ scenario \in Scenarios
    /\ delegateVersion \in DelegateVersions
    /\ compositeVersion \in CompositeVersions
    /\ capturedDelegateVersion \in DelegateVersions
    /\ routeGeneration \in 1..2
    /\ returnedVersion \in
        DelegateVersions \union CompositeVersions \union {NoVersion}
    /\ returnedAnswer \in Answers \union {NoAnswer}
    /\ interpreted \in BOOLEAN
    /\ refreshed \in BOOLEAN
    /\ completed \in BOOLEAN

StableMatchUsesCompositeVersion ==
    /\ phase = "Done"
    /\ scenario = "Stable"
    =>
        /\ returnedVersion = CompositeVersionOne
        /\ completed

DelegateMismatchIsForwardedUninterpreted ==
    /\ phase = "Retry"
    /\ scenario = "DelegateDrift"
    =>
        /\ returnedVersion = DelegateVersionTwo
        /\ ~interpreted

DelegateMismatchRetiresCompositeState ==
    /\ phase = "Retry"
    /\ scenario = "DelegateDrift"
    =>
        /\ compositeVersion = CompositeVersionTwo
        /\ capturedDelegateVersion = DelegateVersionTwo
        /\ refreshed

RouteChangeRetiresCompositeState ==
    /\ phase = "Retry"
    /\ scenario = "RouteChange"
    =>
        /\ compositeVersion = CompositeVersionTwo
        /\ refreshed

OldCompositeTokenNeverGovernsChangedAnswer ==
    returnedAnswer = AnswerTwo =>
        returnedVersion # CompositeVersionOne

RefreshedRetryCompletes ==
    /\ phase = "Done"
    /\ scenario \in {"DelegateDrift", "RouteChange"}
    =>
        /\ completed
        /\ returnedVersion = CompositeVersionTwo
        /\ interpreted

EvaluationConverges == <>(phase = "Done")

=============================================================================
