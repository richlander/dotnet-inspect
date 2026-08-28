----------------------- MODULE SemanticRowSelection -----------------------
EXTENDS FiniteSets, Integers, Sequences, TLC

CONSTANTS MaxValue, MaxStages, MaxSequences

ASSUME /\ MaxValue \in Nat \ {0}
       /\ MaxStages \in Nat \ {0}
       /\ MaxSequences \in Nat \ {0}

Values == 1..MaxValue
Keys == 1..MaxSequences
Counts == 1..(MaxValue + 1)
Orders == {"Ascending", "Descending"}
CallbackKinds == {"None", "ResolverFailure", "ComparerFailure"}
StageKinds == {"Head", "Tail", "Range", "Top"}

Min(a, b) == IF a < b THEN a ELSE b
Max(a, b) == IF a > b THEN a ELSE b

BoundedSequences(S, bound) ==
    UNION {[1..length -> S] : length \in 0..bound}

NoDuplicates(sequence) ==
    \A left, right \in 1..Len(sequence):
        left # right => sequence[left] # sequence[right]

Rows ==
    {sequence \in BoundedSequences(Values, MaxValue):
        NoDuplicates(sequence)}

BaseStage(kind) ==
    [kind |-> kind,
     count |-> 1,
     start |-> 1,
     upper |-> 1,
     order |-> "Ascending",
     callback |-> "None"]

HeadStages ==
    {[BaseStage("Head") EXCEPT !.count = count] : count \in Counts}

TailStages ==
    {[BaseStage("Tail") EXCEPT !.count = count] : count \in Counts}

RangeStageCandidates ==
    {[BaseStage("Range") EXCEPT
        !.start = start,
        !.upper = upper]
      : start \in Counts,
        upper \in 0..(MaxValue + 1)}

RangeStages ==
    {stage \in RangeStageCandidates:
        stage.upper = 0 \/ stage.upper >= stage.start}

TopStages ==
    {[BaseStage("Top") EXCEPT
        !.count = count,
        !.order = order,
        !.callback = callback]
      : count \in Counts,
        order \in Orders,
        callback \in CallbackKinds}

Stages == HeadStages \union TailStages \union RangeStages \union TopStages
Plans == BoundedSequences(Stages, MaxStages)
Inputs == [Keys -> Rows]

Take(sequence, count) ==
    IF Len(sequence) = 0
    THEN <<>>
    ELSE SubSeq(sequence, 1, Min(count, Len(sequence)))

TakeTail(sequence, count) ==
    IF Len(sequence) = 0
    THEN <<>>
    ELSE SubSeq(sequence, Max(1, Len(sequence) - count + 1), Len(sequence))

RequiredPosition(rowRange) ==
    IF rowRange.upper = 0 THEN rowRange.start ELSE rowRange.upper

ApplyRange(sequence, rowRange) ==
    SubSeq(
        sequence,
        rowRange.start,
        IF rowRange.upper = 0 THEN Len(sequence) ELSE rowRange.upper)

Elements(sequence) ==
    {sequence[index] : index \in 1..Len(sequence)}

RowPermutations(sequence) ==
    {candidate \in BoundedSequences(Values, Len(sequence)):
        /\ Len(candidate) = Len(sequence)
        /\ Elements(candidate) = Elements(sequence)
        /\ NoDuplicates(candidate)}

IsRanked(sequence, order) ==
    \A left, right \in 1..Len(sequence):
        left < right =>
            IF order = "Ascending"
            THEN sequence[left] < sequence[right]
            ELSE sequence[left] > sequence[right]

Rank(sequence, order) ==
    CHOOSE candidate \in RowPermutations(sequence):
        IsRanked(candidate, order)

VARIABLES
    input,
    plan,
    keyPosition,
    stagePosition,
    currentRows,
    results,
    status,
    published,
    resolverCalls,
    resolverFirstKey,
    failure,
    history

vars ==
    <<input, plan, keyPosition, stagePosition, currentRows, results,
      status, published, resolverCalls, resolverFirstKey, failure, history>>

FailureKinds == {"RangeFailure", "ResolverFailure", "ComparerFailure"}

FailureRecord(kind, key, stage, required, available) ==
    [kind |-> kind,
     key |-> key,
     stage |-> stage,
     required |-> required,
     available |-> available]

NoFailure == FailureRecord("None", 0, 0, 0, 0)

HistoryEntries ==
    [key : Keys,
     stage : 1..MaxStages,
     kind : StageKinds,
     inputRows : Rows,
     rows : Rows]

Init ==
    /\ input \in Inputs
    /\ plan \in Plans
    /\ keyPosition = 1
    /\ stagePosition = 1
    /\ currentRows = input[1]
    /\ results = [key \in Keys |-> <<>>]
    /\ status = "Running"
    /\ published = FALSE
    /\ resolverCalls = [stage \in 1..MaxStages |-> 0]
    /\ resolverFirstKey = [stage \in 1..MaxStages |-> 0]
    /\ failure = NoFailure
    /\ history = <<>>

AdvanceSequence ==
    /\ status = "Running"
    /\ stagePosition > Len(plan)
    /\ results' = [results EXCEPT ![keyPosition] = currentRows]
    /\ IF keyPosition = MaxSequences
       THEN /\ status' = "Success"
            /\ published' = TRUE
            /\ UNCHANGED <<keyPosition, stagePosition, currentRows>>
       ELSE /\ keyPosition' = keyPosition + 1
            /\ stagePosition' = 1
            /\ currentRows' = input[keyPosition + 1]
            /\ UNCHANGED <<status, published>>
    /\ UNCHANGED
        <<input, plan, resolverCalls, resolverFirstKey, failure, history>>

RecordStage(nextRows) ==
    Append(
        history,
        [key |-> keyPosition,
         stage |-> stagePosition,
         kind |-> plan[stagePosition].kind,
         inputRows |-> currentRows,
         rows |-> nextRows])

ApplyPositionalStage ==
    /\ status = "Running"
    /\ stagePosition <= Len(plan)
    /\ plan[stagePosition].kind \in {"Head", "Tail"}
    /\ LET nextRows ==
               IF plan[stagePosition].kind = "Head"
               THEN Take(currentRows, plan[stagePosition].count)
               ELSE TakeTail(currentRows, plan[stagePosition].count)
       IN /\ currentRows' = nextRows
          /\ history' = RecordStage(nextRows)
    /\ stagePosition' = stagePosition + 1
    /\ UNCHANGED
        <<input, plan, keyPosition, results, status, published,
          resolverCalls, resolverFirstKey, failure>>

ApplyValidRange ==
    /\ status = "Running"
    /\ stagePosition <= Len(plan)
    /\ plan[stagePosition].kind = "Range"
    /\ RequiredPosition(plan[stagePosition]) <= Len(currentRows)
    /\ LET nextRows == ApplyRange(currentRows, plan[stagePosition])
       IN /\ currentRows' = nextRows
          /\ history' = RecordStage(nextRows)
    /\ stagePosition' = stagePosition + 1
    /\ UNCHANGED
        <<input, plan, keyPosition, results, status, published,
          resolverCalls, resolverFirstKey, failure>>

FailRange ==
    /\ status = "Running"
    /\ stagePosition <= Len(plan)
    /\ plan[stagePosition].kind = "Range"
    /\ RequiredPosition(plan[stagePosition]) > Len(currentRows)
    /\ status' = "RangeFailure"
    /\ published' = FALSE
    /\ failure' =
        FailureRecord(
            "RangeFailure",
            keyPosition,
            stagePosition,
            RequiredPosition(plan[stagePosition]),
            Len(currentRows))
    /\ UNCHANGED
        <<input, plan, keyPosition, stagePosition, currentRows, results,
          resolverCalls, resolverFirstKey, history>>

ResolveTop ==
    /\ status = "Running"
    /\ stagePosition <= Len(plan)
    /\ plan[stagePosition].kind = "Top"
    /\ resolverCalls[stagePosition] = 0
    /\ resolverCalls' =
        [resolverCalls EXCEPT ![stagePosition] = 1]
    /\ resolverFirstKey' =
        [resolverFirstKey EXCEPT ![stagePosition] = keyPosition]
    /\ IF plan[stagePosition].callback = "ResolverFailure"
       THEN /\ status' = "ResolverFailure"
            /\ published' = FALSE
            /\ failure' =
                FailureRecord(
                    "ResolverFailure",
                    keyPosition,
                    stagePosition,
                    0,
                    Len(currentRows))
       ELSE /\ UNCHANGED <<status, published, failure>>
    /\ UNCHANGED
        <<input, plan, keyPosition, stagePosition, currentRows, results,
          history>>

ApplyTop ==
    /\ status = "Running"
    /\ stagePosition <= Len(plan)
    /\ plan[stagePosition].kind = "Top"
    /\ resolverCalls[stagePosition] = 1
    /\ plan[stagePosition].callback # "ResolverFailure"
    /\ IF /\ plan[stagePosition].callback = "ComparerFailure"
           /\ Len(currentRows) >= 2
       THEN /\ status' = "ComparerFailure"
            /\ published' = FALSE
            /\ failure' =
                FailureRecord(
                    "ComparerFailure",
                    keyPosition,
                    stagePosition,
                    0,
                    Len(currentRows))
            /\ UNCHANGED
                <<stagePosition, currentRows, results, history>>
       ELSE /\ LET nextRows ==
                       Take(
                           Rank(
                               currentRows,
                               plan[stagePosition].order),
                           plan[stagePosition].count)
               IN /\ currentRows' = nextRows
                  /\ history' = RecordStage(nextRows)
            /\ stagePosition' = stagePosition + 1
            /\ UNCHANGED <<results, status, published, failure>>
    /\ UNCHANGED
        <<input, plan, keyPosition, resolverCalls, resolverFirstKey>>

Next ==
    AdvanceSequence
    \/ ApplyPositionalStage
    \/ ApplyValidRange
    \/ FailRange
    \/ ResolveTop
    \/ ApplyTop

Spec ==
    Init /\ [][Next]_vars /\ WF_vars(Next)

TypeOK ==
    /\ input \in Inputs
    /\ plan \in Plans
    /\ keyPosition \in Keys
    /\ stagePosition \in 1..(MaxStages + 1)
    /\ currentRows \in Rows
    /\ results \in [Keys -> Rows]
    /\ status \in {"Running", "Success"} \union FailureKinds
    /\ published \in BOOLEAN
    /\ resolverCalls \in [1..MaxStages -> 0..MaxSequences]
    /\ resolverFirstKey \in [1..MaxStages -> 0..MaxSequences]
    /\ failure \in
        [kind : {"None"} \union FailureKinds,
         key : 0..MaxSequences,
         stage : 0..MaxStages,
         required : 0..(MaxValue + 1),
         available : 0..MaxValue]
    /\ Len(history) <= MaxSequences * MaxStages
    /\ \A index \in 1..Len(history):
        history[index] \in HistoryEntries

PublicationIsAtomic ==
    published <=> status = "Success"

SuccessFollowsEverySequence ==
    status = "Success" =>
        /\ keyPosition = MaxSequences
        /\ stagePosition > Len(plan)

ResolverRunsAtMostOnce ==
    \A stage \in 1..MaxStages:
        resolverCalls[stage] <= 1

ResolverMetadataIsConsistent ==
    \A stage \in 1..MaxStages:
        (resolverCalls[stage] = 0) <=> (resolverFirstKey[stage] = 0)

LexicallyAtOrBefore(leftKey, leftStage, rightKey, rightStage) ==
    leftKey < rightKey
    \/ (leftKey = rightKey /\ leftStage <= rightStage)

ResolversFollowTraversal ==
    \A stage \in 1..MaxStages:
        resolverCalls[stage] = 1 =>
            /\ stage <= Len(plan)
            /\ plan[stage].kind = "Top"
            /\ resolverFirstKey[stage] \in Keys
            /\ LexicallyAtOrBefore(
                resolverFirstKey[stage],
                stage,
                keyPosition,
                stagePosition)

FailuresRespectTraversal ==
    status \in FailureKinds =>
        \A stage \in 1..MaxStages:
            resolverFirstKey[stage] # 0 =>
                LexicallyAtOrBefore(
                    resolverFirstKey[stage],
                    stage,
                    failure.key,
                    failure.stage)

ExpectedRangeEndpoint(rowRange) ==
    IF rowRange.upper = 0 THEN rowRange.start ELSE rowRange.upper

FailuresMatchCurrentCause ==
    status \in FailureKinds =>
        /\ stagePosition <= Len(plan)
        /\ failure.kind = status
        /\ failure.key = keyPosition
        /\ failure.stage = stagePosition
        /\ failure.available = Len(currentRows)
        /\ CASE plan[stagePosition].kind = "Range" ->
                    /\ status = "RangeFailure"
                    /\ failure.required =
                        ExpectedRangeEndpoint(plan[stagePosition])
                    /\ ExpectedRangeEndpoint(plan[stagePosition]) >
                        Len(currentRows)
             [] plan[stagePosition].kind = "Top" ->
                    /\ failure.required = 0
                    /\ CASE
                        plan[stagePosition].callback =
                            "ResolverFailure" ->
                            /\ status = "ResolverFailure"
                            /\ resolverCalls[stagePosition] = 1
                            /\ resolverFirstKey[stagePosition] = keyPosition
                         []
                        plan[stagePosition].callback =
                            "ComparerFailure" ->
                            /\ status = "ComparerFailure"
                            /\ resolverCalls[stagePosition] = 1
                            /\ Len(currentRows) >= 2
                         [] OTHER -> FALSE
             [] OTHER -> FALSE

HistoryIndex(key, stage) ==
    (key - 1) * Len(plan) + stage

HistoryIsExactTraversalPrefix ==
    /\ Len(history) =
        (keyPosition - 1) * Len(plan) + stagePosition - 1
    /\ \A key \in 1..keyPosition:
        \A stage \in 1..Len(plan):
            LET index == HistoryIndex(key, stage)
            IN index <= Len(history) =>
                /\ history[index].key = key
                /\ history[index].stage = stage

StageInputsFollowDeclaredOrder ==
    \A index \in 1..Len(history):
        LET entry == history[index]
        IN /\ entry.kind = plan[entry.stage].kind
           /\ IF entry.stage = 1
              THEN entry.inputRows = input[entry.key]
              ELSE /\ index > 1
                   /\ history[index - 1].key = entry.key
                   /\ history[index - 1].stage = entry.stage - 1
                   /\ entry.inputRows = history[index - 1].rows

ExpectedCount(count, rows) ==
    IF count < Len(rows) THEN count ELSE Len(rows)

MatchesHead(inputRows, actualRows, count) ==
    /\ Len(actualRows) = ExpectedCount(count, inputRows)
    /\ \A index \in 1..Len(actualRows):
        actualRows[index] = inputRows[index]

MatchesTail(inputRows, actualRows, count) ==
    /\ Len(actualRows) = ExpectedCount(count, inputRows)
    /\ \A index \in 1..Len(actualRows):
        actualRows[index] =
            inputRows[Len(inputRows) - Len(actualRows) + index]

MatchesRange(inputRows, actualRows, rowRange) ==
    /\ ExpectedRangeEndpoint(rowRange) <= Len(inputRows)
    /\ Len(actualRows) =
        IF rowRange.upper = 0
        THEN Len(inputRows) - rowRange.start + 1
        ELSE rowRange.upper - rowRange.start + 1
    /\ \A index \in 1..Len(actualRows):
        actualRows[index] = inputRows[rowRange.start + index - 1]

ContainsRow(rows, value) ==
    \E index \in 1..Len(rows): rows[index] = value

MatchesTop(inputRows, actualRows, count, order) ==
    /\ Len(actualRows) = ExpectedCount(count, inputRows)
    /\ \A index \in 1..Len(actualRows):
        ContainsRow(inputRows, actualRows[index])
    /\ \A left, right \in 1..Len(actualRows):
        left < right =>
            IF order = "Ascending"
            THEN actualRows[left] < actualRows[right]
            ELSE actualRows[left] > actualRows[right]
    /\ \A selected \in 1..Len(actualRows):
        \A candidate \in 1..Len(inputRows):
            ~ContainsRow(actualRows, inputRows[candidate]) =>
                IF order = "Ascending"
                THEN actualRows[selected] < inputRows[candidate]
                ELSE actualRows[selected] > inputRows[candidate]

StageOutputsMatchSemantics ==
    \A index \in 1..Len(history):
        LET entry == history[index]
            stage == plan[entry.stage]
        IN CASE stage.kind = "Head" ->
                    MatchesHead(
                        entry.inputRows,
                        entry.rows,
                        stage.count)
             [] stage.kind = "Tail" ->
                    MatchesTail(
                        entry.inputRows,
                        entry.rows,
                        stage.count)
             [] stage.kind = "Range" ->
                    MatchesRange(
                        entry.inputRows,
                        entry.rows,
                        stage)
             [] stage.kind = "Top" ->
                    MatchesTop(
                        entry.inputRows,
                        entry.rows,
                        stage.count,
                        stage.order)
             [] OTHER -> FALSE

CurrentRowsMatchCompletedStages ==
    IF stagePosition = 1
    THEN currentRows = input[keyPosition]
    ELSE /\ Len(history) > 0
         /\ history[Len(history)].key = keyPosition
         /\ history[Len(history)].stage = stagePosition - 1
         /\ currentRows = history[Len(history)].rows

CompletedTopStagesRespectCallbacks ==
    \A index \in 1..Len(history):
        history[index].kind = "Top" =>
            /\ plan[history[index].stage].callback # "ResolverFailure"
            /\ (plan[history[index].stage].callback # "ComparerFailure"
                \/ Len(history[index].inputRows) < 2)

SuccessPublishesFinalRows ==
    status = "Success" =>
        /\ Len(history) = MaxSequences * Len(plan)
        /\ \A key \in Keys:
            IF Len(plan) = 0
            THEN results[key] = input[key]
            ELSE \E index \in 1..Len(history):
                /\ history[index].key = key
                /\ history[index].stage = Len(plan)
                /\ results[key] = history[index].rows

TopResolutionCoversEveryOutput ==
    \A index \in 1..Len(history):
        history[index].kind = "Top" =>
            resolverCalls[history[index].stage] = 1

Termination ==
    <>(status # "Running")

=============================================================================
