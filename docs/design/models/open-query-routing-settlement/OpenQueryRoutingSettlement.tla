-------------------- MODULE OpenQueryRoutingSettlement --------------------
EXTENDS Naturals, Sequences, TLC

CONSTANTS FirstUnit, SecondUnit, ThirdUnit, Mutation, Scenario

ASSUME /\ FirstUnit # SecondUnit
       /\ FirstUnit # ThirdUnit
       /\ SecondUnit # ThirdUnit

Units == <<FirstUnit, SecondUnit, ThirdUnit>>
UnitSet == {FirstUnit, SecondUnit, ThirdUnit}

UnitPosition(unit) ==
    CASE unit = FirstUnit -> 1
      [] unit = SecondUnit -> 2
      [] unit = ThirdUnit -> 3

AllMatches == "AllMatches"
ExactOnly == "ExactOnly"
FacetA == "FacetA"
FacetB == "FacetB"
Consumers == {AllMatches, ExactOnly, FacetA, FacetB}

ExactClass == "Exact"
SimilarClass == "Similar"
FacetAClass == "FacetA"
FacetBClass == "FacetB"
Classes == {ExactClass, SimilarClass, FacetAClass, FacetBClass}

NoMutation == "None"
ExclusiveAllMatches == "ExclusiveAllMatches"
StopAfterAny == "StopAfterAny"
ChargeSettled == "ChargeSettled"
Mutations ==
    {NoMutation, ExclusiveAllMatches, StopAfterAny, ChargeSettled}

ExhaustionScenario == "Exhaustion"
EarlySettlementScenario == "EarlySettlement"
Scenarios == {ExhaustionScenario, EarlySettlementScenario}

ASSUME Scenario \in Scenarios

Running == "Running"
Done == "Done"
Phases == {Running, Done}

Active == "Active"
Reached == "Reached"
Exhausted == "Exhausted"
Statuses == {Active, Reached, Exhausted}

ExactUnits == {SecondUnit}
SimilarUnits == UnitSet
FacetAUnits == {SecondUnit}
FacetBUnits == {SecondUnit, ThirdUnit}

HeadLimit(consumer) ==
    CASE consumer = AllMatches ->
             IF Scenario = EarlySettlementScenario THEN 2 ELSE 3
      [] consumer = ExactOnly -> 1
      [] consumer = FacetA -> 1
      [] consumer = FacetB ->
             IF Scenario = EarlySettlementScenario THEN 1 ELSE 3

Min(left, right) == IF left < right THEN left ELSE right

Take(sequence, count) ==
    IF Len(sequence) = 0
    THEN <<>>
    ELSE SubSeq(sequence, 1, Min(count, Len(sequence)))

Row(unit, class) == [unit |-> unit, class |-> class]
RowDomain == [unit : UnitSet, class : Classes]

CorrectExclusiveEmission(unit) ==
    IF unit \in ExactUnits
    THEN <<Row(unit, ExactClass)>>
    ELSE IF unit \in SimilarUnits
         THEN <<Row(unit, SimilarClass)>>
         ELSE <<>>

ExecutionExclusiveEmission(unit) ==
    IF /\ Mutation = ExclusiveAllMatches
       /\ unit \in ExactUnits
       /\ unit \in SimilarUnits
    THEN <<Row(unit, ExactClass), Row(unit, SimilarClass)>>
    ELSE CorrectExclusiveEmission(unit)

CorrectEmissions(consumer, unit) ==
    CASE consumer = AllMatches ->
             CorrectExclusiveEmission(unit)
      [] consumer = ExactOnly ->
             IF unit \in ExactUnits
             THEN <<Row(unit, ExactClass)>>
             ELSE <<>>
      [] consumer = FacetA ->
             IF unit \in FacetAUnits
             THEN <<Row(unit, FacetAClass)>>
             ELSE <<>>
      [] consumer = FacetB ->
             IF unit \in FacetBUnits
             THEN <<Row(unit, FacetBClass)>>
             ELSE <<>>

ExecutionEmissions(consumer, unit) ==
    IF consumer = AllMatches
    THEN ExecutionExclusiveEmission(unit)
    ELSE CorrectEmissions(consumer, unit)

RECURSIVE CorrectRowsThrough(_, _)
CorrectRowsThrough(consumer, count) ==
    IF count = 0
    THEN <<>>
    ELSE CorrectRowsThrough(consumer, count - 1)
         \o CorrectEmissions(consumer, Units[count])

ExpectedRows(consumer) ==
    Take(CorrectRowsThrough(consumer, Len(Units)), HeadLimit(consumer))

VARIABLES
    phase,
    cursor,
    pending,
    status,
    rows,
    visited,
    settledAt

vars == <<phase, cursor, pending, status, rows, visited, settledAt>>

InitialStatus == [consumer \in Consumers |-> Active]
InitialRows == [consumer \in Consumers |-> <<>>]
InitialPosition == [consumer \in Consumers |-> 0]

Init ==
    /\ phase = Running
    /\ cursor = 1
    /\ pending = Consumers
    /\ status = InitialStatus
    /\ rows = InitialRows
    /\ visited = InitialPosition
    /\ settledAt = InitialPosition

ActiveConsumers == {consumer \in Consumers : status[consumer] = Active}
ReachedConsumers == {consumer \in Consumers : status[consumer] = Reached}

CanVisit(consumer) ==
    /\ consumer \in pending
    /\ \/ status[consumer] = Active
       \/ /\ Mutation = ChargeSettled
          /\ status[consumer] = Reached

Visit(consumer) ==
    /\ phase = Running
    /\ cursor <= Len(Units)
    /\ CanVisit(consumer)
    /\ LET nextRows ==
               Take(
                   rows[consumer]
                   \o ExecutionEmissions(consumer, Units[cursor]),
                   HeadLimit(consumer))
           reaches ==
               /\ status[consumer] = Active
               /\ Len(nextRows) = HeadLimit(consumer)
       IN /\ rows' = [rows EXCEPT ![consumer] = nextRows]
          /\ status' =
               [status EXCEPT
                   ![consumer] =
                       IF reaches
                       THEN Reached
                       ELSE @]
          /\ settledAt' =
               [settledAt EXCEPT
                   ![consumer] =
                       IF reaches
                       THEN cursor
                       ELSE @]
    /\ visited' = [visited EXCEPT ![consumer] = cursor]
    /\ pending' = pending \ {consumer}
    /\ UNCHANGED <<phase, cursor>>

NextPending ==
    IF Mutation = ChargeSettled
    THEN ActiveConsumers \cup ReachedConsumers
    ELSE ActiveConsumers

Advance ==
    /\ phase = Running
    /\ pending = {}
    /\ cursor < Len(Units)
    /\ ActiveConsumers # {}
    /\ cursor' = cursor + 1
    /\ pending' = NextPending
    /\ UNCHANGED <<phase, status, rows, visited, settledAt>>

FinishReached ==
    /\ phase = Running
    /\ pending = {}
    /\ ActiveConsumers = {}
    /\ phase' = Done
    /\ UNCHANGED <<cursor, pending, status, rows, visited, settledAt>>

ExhaustSource ==
    /\ phase = Running
    /\ pending = {}
    /\ cursor = Len(Units)
    /\ ActiveConsumers # {}
    /\ phase' = Done
    /\ status' =
         [consumer \in Consumers |->
             IF status[consumer] = Active
             THEN Exhausted
             ELSE status[consumer]]
    /\ UNCHANGED <<cursor, pending, rows, visited, settledAt>>

PrematureFinish ==
    /\ Mutation = StopAfterAny
    /\ phase = Running
    /\ pending = {}
    /\ cursor < Len(Units)
    /\ ReachedConsumers # {}
    /\ ActiveConsumers # {}
    /\ phase' = Done
    /\ status' =
         [consumer \in Consumers |->
             IF status[consumer] = Active
             THEN Exhausted
             ELSE status[consumer]]
    /\ UNCHANGED <<cursor, pending, rows, visited, settledAt>>

VisitOne == \E consumer \in Consumers : Visit(consumer)

Next ==
    \/ VisitOne
    \/ Advance
    \/ FinishReached
    \/ ExhaustSource
    \/ PrematureFinish

TypeOK ==
    /\ phase \in Phases
    /\ cursor \in 1..Len(Units)
    /\ pending \subseteq Consumers
    /\ status \in [Consumers -> Statuses]
    /\ rows \in [Consumers -> Seq(RowDomain)]
    /\ visited \in [Consumers -> 0..Len(Units)]
    /\ settledAt \in [Consumers -> 0..Len(Units)]

ExclusiveClassificationIsSingle ==
    \A unit \in UnitSet :
        visited[AllMatches] >= UnitPosition(unit)
        => Len(ExecutionExclusiveEmission(unit)) <= 1

RowsFollowUnitOrder ==
    \A consumer \in Consumers :
        rows[consumer] =
            Take(
                CorrectRowsThrough(consumer, visited[consumer]),
                HeadLimit(consumer))

ReachedMeansHeadSatisfied ==
    \A consumer \in Consumers :
        status[consumer] = Reached
        => /\ Len(rows[consumer]) = HeadLimit(consumer)
           /\ rows[consumer] = ExpectedRows(consumer)

SettledConsumersAreNotCharged ==
    \A consumer \in Consumers :
        status[consumer] = Reached
        => visited[consumer] = settledAt[consumer]

ExhaustionFollowsSource ==
    \A consumer \in Consumers :
        status[consumer] = Exhausted
        => /\ visited[consumer] = Len(Units)
           /\ Len(rows[consumer]) < HeadLimit(consumer)

DoneHasReferenceResults ==
    phase = Done
    => \A consumer \in Consumers :
           rows[consumer] = ExpectedRows(consumer)

DoneHasNoActiveConsumer ==
    phase = Done
    => ActiveConsumers = {}

EarlySettlementLeavesUnreadUnit ==
    /\ Scenario = EarlySettlementScenario
    /\ phase = Done
    => /\ cursor < Len(Units)
       /\ status \in [Consumers -> {Reached}]

EventuallyDone == <>(phase = Done)

Spec ==
    /\ Init
    /\ [][Next]_vars
    /\ WF_vars(VisitOne)
    /\ WF_vars(Advance)
    /\ WF_vars(FinishReached)
    /\ WF_vars(ExhaustSource)

=============================================================================
