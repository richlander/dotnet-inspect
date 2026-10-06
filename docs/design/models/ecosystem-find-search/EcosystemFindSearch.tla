--------------------------- MODULE EcosystemFindSearch ---------------------------
EXTENDS FiniteSets, Naturals, TLC

CONSTANTS
    EcosystemA,
    EcosystemB,
    SharedBounded,
    AOnlyBounded,
    SharedPrefix,
    BOnlyPrefix,
    Mutation

Ecosystems == {EcosystemA, EcosystemB}
BoundedSources == {SharedBounded, AOnlyBounded}
PrefixSources == {SharedPrefix, BOnlyPrefix}
Sources == BoundedSources \cup PrefixSources

BoundedMembership(source) ==
    CASE source = SharedBounded -> {EcosystemA, EcosystemB}
      [] source = AOnlyBounded -> {EcosystemA}

PrefixMembership(source) ==
    CASE source = SharedPrefix -> {EcosystemA, EcosystemB}
      [] source = BOnlyPrefix -> {EcosystemB}

NoMutation == "None"
PrefixBeforeBounded == "PrefixBeforeBounded"
Mutations == {NoMutation, PrefixBeforeBounded}

BoundedPhase == "Bounded"
PrefixPhase == "Prefix"
CompletedPhase == "Completed"
CanceledPhase == "Canceled"
Phases == {BoundedPhase, PrefixPhase, CompletedPhase, CanceledPhase}

NotStarted == "NotStarted"
Running == "Running"
Settled == "Settled"
WorkStates == {NotStarted, Running, Settled}

VARIABLES
    phase,
    workState,
    startCount,
    boundedPublished,
    boundedPublishCount,
    prefixPublished,
    prefixPublishCount,
    publishedPrefixMembership,
    cancelWorkState,
    cancelBoundedPublished,
    cancelPrefixPublished

vars ==
    <<phase,
      workState,
      startCount,
      boundedPublished,
      boundedPublishCount,
      prefixPublished,
      prefixPublishCount,
      publishedPrefixMembership,
      cancelWorkState,
      cancelBoundedPublished,
      cancelPrefixPublished>>

InitialWorkState == [source \in Sources |-> NotStarted]
InitialCount == [source \in Sources |-> 0]
InitialBoundedCount == [ecosystem \in Ecosystems |-> 0]
InitialPrefixMembership == [source \in PrefixSources |-> {}]

Init ==
    /\ phase = BoundedPhase
    /\ workState = InitialWorkState
    /\ startCount = InitialCount
    /\ boundedPublished = {}
    /\ boundedPublishCount = InitialBoundedCount
    /\ prefixPublished = {}
    /\ prefixPublishCount = InitialCount
    /\ publishedPrefixMembership = InitialPrefixMembership
    /\ cancelWorkState = InitialWorkState
    /\ cancelBoundedPublished = {}
    /\ cancelPrefixPublished = {}

AllBoundedSettled ==
    \A source \in BoundedSources : workState[source] = Settled

AllBoundedPublished ==
    boundedPublished = Ecosystems

AllPrefixSettled ==
    \A source \in PrefixSources : workState[source] = Settled

AllPrefixPublished ==
    prefixPublished = PrefixSources

BoundedContributorsSettled(ecosystem) ==
    \A source \in BoundedSources :
        ecosystem \in BoundedMembership(source)
        => workState[source] = Settled

StartBounded(source) ==
    /\ phase = BoundedPhase
    /\ source \in BoundedSources
    /\ workState[source] = NotStarted
    /\ workState' = [workState EXCEPT ![source] = Running]
    /\ startCount' = [startCount EXCEPT ![source] = @ + 1]
    /\ UNCHANGED
        <<phase,
          boundedPublished,
          boundedPublishCount,
          prefixPublished,
          prefixPublishCount,
          publishedPrefixMembership,
          cancelWorkState,
          cancelBoundedPublished,
          cancelPrefixPublished>>

SettleBounded(source) ==
    /\ phase = BoundedPhase
    /\ source \in BoundedSources
    /\ workState[source] = Running
    /\ workState' = [workState EXCEPT ![source] = Settled]
    /\ UNCHANGED
        <<phase,
          startCount,
          boundedPublished,
          boundedPublishCount,
          prefixPublished,
          prefixPublishCount,
          publishedPrefixMembership,
          cancelWorkState,
          cancelBoundedPublished,
          cancelPrefixPublished>>

PublishBounded(ecosystem) ==
    /\ phase = BoundedPhase
    /\ ecosystem \in Ecosystems
    /\ ecosystem \notin boundedPublished
    /\ BoundedContributorsSettled(ecosystem)
    /\ boundedPublished' = boundedPublished \cup {ecosystem}
    /\ boundedPublishCount' =
        [boundedPublishCount EXCEPT ![ecosystem] = @ + 1]
    /\ UNCHANGED
        <<phase,
          workState,
          startCount,
          prefixPublished,
          prefixPublishCount,
          publishedPrefixMembership,
          cancelWorkState,
          cancelBoundedPublished,
          cancelPrefixPublished>>

EnterPrefix ==
    /\ phase = BoundedPhase
    /\ (IF Mutation = PrefixBeforeBounded
        THEN TRUE
        ELSE /\ AllBoundedSettled
             /\ AllBoundedPublished)
    /\ phase' = PrefixPhase
    /\ UNCHANGED
        <<workState,
          startCount,
          boundedPublished,
          boundedPublishCount,
          prefixPublished,
          prefixPublishCount,
          publishedPrefixMembership,
          cancelWorkState,
          cancelBoundedPublished,
          cancelPrefixPublished>>

StartPrefix(source) ==
    /\ phase = PrefixPhase
    /\ source \in PrefixSources
    /\ workState[source] = NotStarted
    /\ workState' = [workState EXCEPT ![source] = Running]
    /\ startCount' = [startCount EXCEPT ![source] = @ + 1]
    /\ UNCHANGED
        <<phase,
          boundedPublished,
          boundedPublishCount,
          prefixPublished,
          prefixPublishCount,
          publishedPrefixMembership,
          cancelWorkState,
          cancelBoundedPublished,
          cancelPrefixPublished>>

SettlePrefix(source) ==
    /\ phase = PrefixPhase
    /\ source \in PrefixSources
    /\ workState[source] = Running
    /\ workState' = [workState EXCEPT ![source] = Settled]
    /\ UNCHANGED
        <<phase,
          startCount,
          boundedPublished,
          boundedPublishCount,
          prefixPublished,
          prefixPublishCount,
          publishedPrefixMembership,
          cancelWorkState,
          cancelBoundedPublished,
          cancelPrefixPublished>>

PublishPrefix(source) ==
    /\ phase = PrefixPhase
    /\ source \in PrefixSources
    /\ workState[source] = Settled
    /\ source \notin prefixPublished
    /\ prefixPublished' = prefixPublished \cup {source}
    /\ prefixPublishCount' =
        [prefixPublishCount EXCEPT ![source] = @ + 1]
    /\ publishedPrefixMembership' =
        [publishedPrefixMembership EXCEPT
            ![source] = PrefixMembership(source)]
    /\ UNCHANGED
        <<phase,
          workState,
          startCount,
          boundedPublished,
          boundedPublishCount,
          cancelWorkState,
          cancelBoundedPublished,
          cancelPrefixPublished>>

Complete ==
    /\ phase = PrefixPhase
    /\ AllPrefixSettled
    /\ AllPrefixPublished
    /\ phase' = CompletedPhase
    /\ UNCHANGED
        <<workState,
          startCount,
          boundedPublished,
          boundedPublishCount,
          prefixPublished,
          prefixPublishCount,
          publishedPrefixMembership,
          cancelWorkState,
          cancelBoundedPublished,
          cancelPrefixPublished>>

Cancel ==
    /\ phase \in {BoundedPhase, PrefixPhase}
    /\ phase' = CanceledPhase
    /\ cancelWorkState' = workState
    /\ cancelBoundedPublished' = boundedPublished
    /\ cancelPrefixPublished' = prefixPublished
    /\ UNCHANGED
        <<workState,
          startCount,
          boundedPublished,
          boundedPublishCount,
          prefixPublished,
          prefixPublishCount,
          publishedPrefixMembership>>

Next ==
    \/ \E source \in BoundedSources : StartBounded(source)
    \/ \E source \in BoundedSources : SettleBounded(source)
    \/ \E ecosystem \in Ecosystems : PublishBounded(ecosystem)
    \/ EnterPrefix
    \/ \E source \in PrefixSources : StartPrefix(source)
    \/ \E source \in PrefixSources : SettlePrefix(source)
    \/ \E source \in PrefixSources : PublishPrefix(source)
    \/ Complete
    \/ Cancel

TypeOK ==
    /\ phase \in Phases
    /\ workState \in [Sources -> WorkStates]
    /\ startCount \in [Sources -> Nat]
    /\ boundedPublished \subseteq Ecosystems
    /\ boundedPublishCount \in [Ecosystems -> Nat]
    /\ prefixPublished \subseteq PrefixSources
    /\ prefixPublishCount \in [Sources -> Nat]
    /\ publishedPrefixMembership \in
        [PrefixSources -> SUBSET Ecosystems]
    /\ cancelWorkState \in [Sources -> WorkStates]
    /\ cancelBoundedPublished \subseteq Ecosystems
    /\ cancelPrefixPublished \subseteq PrefixSources

SourceStartsAtMostOnce ==
    \A source \in Sources : startCount[source] <= 1

BoundedPublishesAtMostOnce ==
    \A ecosystem \in Ecosystems :
        boundedPublishCount[ecosystem] <= 1

PrefixPublishesAtMostOnce ==
    \A source \in PrefixSources :
        prefixPublishCount[source] <= 1

BoundedPublicationIsSettled ==
    \A ecosystem \in boundedPublished :
        BoundedContributorsSettled(ecosystem)

PrefixStartsAfterBoundedBarrier ==
    (\E source \in PrefixSources :
        workState[source] # NotStarted)
    => /\ AllBoundedSettled
       /\ AllBoundedPublished

PrefixPublicationIsSettled ==
    \A source \in prefixPublished :
        workState[source] = Settled

PrefixMembershipIsComplete ==
    \A source \in prefixPublished :
        publishedPrefixMembership[source] = PrefixMembership(source)

CompletedIsComplete ==
    (phase = CompletedPhase)
    => /\ AllBoundedSettled
       /\ AllBoundedPublished
       /\ AllPrefixSettled
       /\ AllPrefixPublished

CanceledStateIsStable ==
    (phase = CanceledPhase)
    => /\ workState = cancelWorkState
       /\ boundedPublished = cancelBoundedPublished
       /\ prefixPublished = cancelPrefixPublished

Spec ==
    /\ Init
    /\ [][Next]_vars

=============================================================================
