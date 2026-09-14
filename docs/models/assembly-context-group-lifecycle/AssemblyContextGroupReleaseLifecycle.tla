---------------- MODULE AssemblyContextGroupReleaseLifecycle ----------------
EXTENDS FiniteSets

CONSTANTS
    Group,
    NoGroup,
    ReleaseResults,
    NoReleaseResult

ASSUME
    /\ Group # NoGroup
    /\ ReleaseResults # {}
    /\ IsFiniteSet(ReleaseResults)
    /\ NoReleaseResult \notin ReleaseResults

VARIABLES
    requestedGroup,
    completedGroup,
    completionResult

vars == <<requestedGroup, completedGroup, completionResult>>

Init ==
    /\ requestedGroup = NoGroup
    /\ completedGroup = NoGroup
    /\ completionResult = NoReleaseResult

RequestRelease ==
    /\ requestedGroup = NoGroup
    /\ completedGroup = NoGroup
    /\ requestedGroup' = Group
    /\ UNCHANGED <<completedGroup, completionResult>>

CompleteRelease(result, quiescent) ==
    /\ result \in ReleaseResults
    /\ quiescent
    /\ requestedGroup = Group
    /\ completedGroup = NoGroup
    /\ completedGroup' = Group
    /\ completionResult' = result
    /\ UNCHANGED requestedGroup

Next(quiescent) ==
    \/ RequestRelease
    \/ \E result \in ReleaseResults:
        CompleteRelease(result, quiescent)

SafetySpec(quiescent) ==
    Init /\ [][Next(quiescent)]_vars

Fairness(quiescent) ==
    \A result \in ReleaseResults:
        WF_vars(CompleteRelease(result, quiescent))

Spec(quiescent) ==
    SafetySpec(quiescent) /\ Fairness(quiescent)

\* The standalone harness isolates identity/result lifecycle behavior. Each
\* consumer supplies its owner-issued quiescence predicate.
HarnessSpec ==
    Spec(TRUE)

BlockedHarnessSpec ==
    SafetySpec(FALSE)

TypeOK ==
    /\ requestedGroup \in {NoGroup, Group}
    /\ completedGroup \in {NoGroup, Group}
    /\ completionResult \in ReleaseResults \cup {NoReleaseResult}

CompletionMatchesRequest ==
    completedGroup # NoGroup
        => /\ requestedGroup = Group
           /\ completedGroup = requestedGroup

CompletionCarriesResult ==
    (completedGroup = Group) = (completionResult \in ReleaseResults)

RequestedGroupEventuallyCompletes ==
    requestedGroup = Group ~> completedGroup = Group

NoCompletionWhileBlocked ==
    completedGroup = NoGroup

=============================================================================
