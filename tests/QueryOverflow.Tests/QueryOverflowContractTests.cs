using System.Globalization;
using QueryOverflow.Consumer;
using QuerySpace.Composition;
using QuerySpace.Rows;

namespace QueryOverflow.Tests;

public sealed class QueryOverflowContractTests
{
    [Fact]
    public void EveryBatchPartitionMatchesCompleteExecution()
    {
        int[] source = [1, 2, 3, 4, 5, 6, 7];
        ResolvedRowQueryPlan<int> rowsPlan =
            Resolve(selection: [Head(4)]);
        IReadOnlyList<int> expectedRows =
            RowQueryExecutor.Apply(source, rowsPlan).Values;

        ResolvedRowQueryPlan<int> countPlan =
            Resolve(
                predicates: [AtLeast(3)]);
        int expectedCount =
            RowQueryExecutor.Apply(source, countPlan).Values.Count;

        foreach (IReadOnlyList<int[]> partition
            in EnumeratePartitions(source))
        {
            Assert.Equal(
                expectedRows,
                ExecuteRows(partition, rowsPlan));
            Assert.Equal(
                expectedCount,
                ExecuteCount(partition, countPlan));
        }
    }

    [Fact]
    public void BatchDemandIsNotSemanticSelection()
    {
        QueryOverflowExecution<int> rows =
            RequirePlan(
                Resolve(selection: [Head(100)]),
                QuerySpaceTerminalRequirement.Rows)
            .Start();

        Assert.False(
            rows.TryRequestInput(
                ownerBatchCeiling: 32,
                finalRowCredit: 0,
                out _));
        Assert.True(
            rows.TryRequestInput(
                ownerBatchCeiling: 32,
                finalRowCredit: 10,
                out QueryOverflowInputRequest first));
        Assert.Equal(10, first.MaximumCandidateRows);
        QueryOverflowStep<int> firstStep =
            rows.Advance(
                first,
                Enumerable.Range(1, 10).ToArray(),
                sourceCompleted: false);
        Assert.Equal(
            QueryOverflowExecutionState.RowsAvailable,
            firstStep.State);

        Assert.True(
            rows.TryRequestInput(
                ownerBatchCeiling: 32,
                finalRowCredit: 50,
                out QueryOverflowInputRequest second));
        Assert.Equal(32, second.MaximumCandidateRows);

        QueryOverflowExecution<int> count =
            RequirePlan(
                Resolve(selection: [Head(100)]),
                QuerySpaceTerminalRequirement.Count)
            .Start();
        Assert.True(
            count.TryRequestInput(
                ownerBatchCeiling: 32,
                finalRowCredit: 0,
                out QueryOverflowInputRequest countRequest));
        Assert.Equal(32, countRequest.MaximumCandidateRows);
    }

    [Fact]
    public void HeadStopsOpenEndedSourcePull()
    {
        QueryOverflowExecution<int> execution =
            RequirePlan(
                Resolve(selection: [Head(100)]),
                QuerySpaceTerminalRequirement.Rows)
            .Start();
        var published = new List<int>();
        var requests = new List<int>();
        int next = 1;

        while (execution.TryRequestInput(
            ownerBatchCeiling: 32,
            finalRowCredit: 64,
            out QueryOverflowInputRequest request))
        {
            Assert.InRange(
                request.MaximumCandidateRows,
                1,
                32);
            requests.Add(request.MaximumCandidateRows);
            int[] batch =
                Enumerable.Range(
                    next,
                    request.MaximumCandidateRows)
                .ToArray();
            next += batch.Length;
            QueryOverflowStep<int> step =
                execution.Advance(
                    request,
                    batch,
                    sourceCompleted: false);
            published.AddRange(step.Rows);
        }

        Assert.Equal(
            QueryOverflowExecutionState.Completed,
            execution.State);
        Assert.Equal(
            Enumerable.Range(1, 100),
            published);
        Assert.Equal([32, 32, 32, 4], requests);
        Assert.Equal(101, next);
    }

    [Fact]
    public void ExactCountRequiresTerminalSufficiency()
    {
        QueryOverflowExecution<int> draining =
            RequirePlan(
                Resolve(predicates: [AtLeast(3)]),
                QuerySpaceTerminalRequirement.Count)
            .Start();
        QueryOverflowStep<int> partial =
            Advance(
                draining,
                [1, 3, 4],
                sourceCompleted: false);
        Assert.False(partial.HasCount);
        Assert.Throws<InvalidOperationException>(
            () => partial.Count);

        QueryOverflowStep<int> completed =
            Advance(
                draining,
                [2, 5],
                sourceCompleted: true);
        Assert.True(completed.HasCount);
        Assert.Equal(3, completed.Count);

        QueryOverflowExecution<int> bounded =
            RequirePlan(
                Resolve(selection: [Head(2)]),
                QuerySpaceTerminalRequirement.Count)
            .Start();
        QueryOverflowStep<int> first =
            Advance(
                bounded,
                [1],
                sourceCompleted: false);
        Assert.False(first.HasCount);
        QueryOverflowStep<int> witness =
            Advance(
                bounded,
                [2],
                sourceCompleted: false);
        Assert.Equal(
            QueryOverflowExecutionState.Completed,
            witness.State);
        Assert.Equal(1, witness.ConsumedRows);
        Assert.True(witness.HasCount);
        Assert.Equal(2, witness.Count);

        QueryOverflowExecution<int> shortSource =
            RequirePlan(
                Resolve(
                    selection: [Head(5)]),
                QuerySpaceTerminalRequirement.Count)
            .Start();
        QueryOverflowStep<int> shortResult =
            Advance(
                shortSource,
                [1, 2, 3],
                sourceCompleted: true);
        Assert.Equal(
            QueryOverflowExecutionState.Completed,
            shortResult.State);
        Assert.Equal(3, shortResult.Count);
    }

    [Fact]
    public void UnsupportedPlansDeclineBeforeInput()
    {
        QueryOverflowAdmission<int> rowsWithPredicate =
            QueryOverflowPlan<int>.Admit(
                Resolve(predicates: [AtLeast(3)]),
                QuerySpaceTerminalRequirement.Rows);
        Assert.False(rowsWithPredicate.IsAccepted);
        Assert.Equal(
            QueryOverflowDeclineReason
                .RowsWithPredicatesRequireAtomicPublication,
            rowsWithPredicate.DeclineReason);

        QueryOverflowAdmission<int> ordered =
            QueryOverflowPlan<int>.Admit(
                Resolve(
                    baselineOrder:
                        RowQueryOrderIntent.Keys(
                            [
                                new(
                                    "value",
                                    RowQueryOrderDirection.Ascending),
                            ])),
                QuerySpaceTerminalRequirement.Count);
        Assert.False(ordered.IsAccepted);
        Assert.Equal(
            QueryOverflowDeclineReason
                .BaselineOrderRequiresCompletePopulation,
            ordered.DeclineReason);

        QueryOverflowAdmission<int> predicateHead =
            QueryOverflowPlan<int>.Admit(
                Resolve(
                    predicates: [AtLeast(3)],
                    selection: [Head(2)]),
                QuerySpaceTerminalRequirement.Count);
        Assert.False(predicateHead.IsAccepted);
        Assert.Equal(
            QueryOverflowDeclineReason
                .PredicatesWithHeadRequireCompletePopulation,
            predicateHead.DeclineReason);

        foreach (RowSelectionIntentOperation<RowQueryOrderIntent>
            unsupported in new[]
            {
                RowSelectionIntentOperation<
                    RowQueryOrderIntent>.Tail(2),
                RowSelectionIntentOperation<
                    RowQueryOrderIntent>.Window(2, 3),
                RowSelectionIntentOperation<
                    RowQueryOrderIntent>.Top(
                        2,
                        RowQueryOrderIntent.Keys(
                            [
                                new(
                                    "value",
                                    RowQueryOrderDirection.Ascending),
                            ])),
            })
        {
            QueryOverflowAdmission<int> admission =
                QueryOverflowPlan<int>.Admit(
                    Resolve(selection: [unsupported]),
                    QuerySpaceTerminalRequirement.Rows);
            Assert.False(admission.IsAccepted);
            Assert.Equal(
                QueryOverflowDeclineReason
                    .UnsupportedSelectionStage,
                admission.DeclineReason);
        }
    }

    [Fact]
    public void CheckpointOwnsNoReleasedBatchValue()
    {
        QueryOverflowExecution<string> execution =
            RequirePlan(
                ResolveString(selection: [Head(2)]),
                QuerySpaceTerminalRequirement.Rows)
            .Start();
        string[] batch = ["first", "second"];
        QueryOverflowStep<string> step =
            Advance(
                execution,
                batch,
                sourceCompleted: false);

        batch[0] = "replacement";

        Assert.Equal(["first", "second"], step.Rows);
        Assert.False(
            execution.TryRequestInput(
                ownerBatchCeiling: 1,
                finalRowCredit: 1,
                out _));
    }

    [Fact]
    public void SemanticFailurePreservesAtomicRows()
    {
        ResolvedRowQueryPlan<int> failingPlan =
            ResolveWithPredicate(
                static value =>
                    value == 3
                        ? throw new FormatException("invalid row")
                        : true);
        QueryOverflowExecution<int> failing =
            RequirePlan(
                failingPlan,
                QuerySpaceTerminalRequirement.Count)
            .Start();
        Assert.Throws<FormatException>(
            () => Advance(
                failing,
                [1, 2, 3],
                sourceCompleted: false));
        Assert.Equal(
            QueryOverflowExecutionState.Failed,
            failing.State);
        Assert.False(
            failing.TryRequestInput(1, 0, out _));
    }

    [Fact]
    public void SourceFailureDoesNotBecomeCompletion()
    {
        QueryOverflowExecution<int> sourceFailure =
            RequirePlan(
                Resolve(),
                QuerySpaceTerminalRequirement.Rows)
            .Start();
        QueryOverflowStep<int> partial =
            Advance(
                sourceFailure,
                [1, 2],
                sourceCompleted: false);
        sourceFailure.Fail();
        Assert.Equal([1, 2], partial.Rows);
        Assert.Equal(
            QueryOverflowExecutionState.Failed,
            sourceFailure.State);
        Assert.False(
            sourceFailure.TryRequestInput(1, 1, out _));
    }

    [Fact]
    public void CancellationIsTerminal()
    {
        QueryOverflowExecution<int> cancelled =
            RequirePlan(
                Resolve(),
                QuerySpaceTerminalRequirement.Rows)
            .Start();
        Assert.True(
            cancelled.TryRequestInput(
                4,
                4,
                out _));
        cancelled.Cancel();
        Assert.Equal(
            QueryOverflowExecutionState.Cancelled,
            cancelled.State);
        Assert.False(
            cancelled.TryRequestInput(1, 1, out _));
    }

    [Fact]
    public void IndependentConsumerRunsQueryOverflow()
    {
        QueryOverflowConsumerObservation observation =
            QueryOverflowDirectConsumer.Execute();

        Assert.Equal(
            ["one", "two", "three"],
            observation.Names);
        Assert.Equal(2, observation.Count);
        Assert.DoesNotContain(
            observation.RowType.GetInterfaces(),
            IsQueryOverflowInterface);
        Assert.DoesNotContain(
            observation.BatchType.GetInterfaces(),
            IsQueryOverflowInterface);
    }

    [Fact]
    public void InvalidBatchTransitionsAreRejected()
    {
        QueryOverflowExecution<int> execution =
            RequirePlan(
                Resolve(),
                QuerySpaceTerminalRequirement.Rows)
            .Start();
        Assert.Throws<ArgumentOutOfRangeException>(
            () => execution.TryRequestInput(0, 1, out _));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => execution.TryRequestInput(1, -1, out _));

        Assert.True(
            execution.TryRequestInput(
                2,
                2,
                out QueryOverflowInputRequest request));
        Assert.Throws<InvalidOperationException>(
            () => execution.TryRequestInput(2, 2, out _));
        Assert.Throws<ArgumentException>(
            () => execution.Advance(
                request,
                [1, 2, 3],
                sourceCompleted: false));
        Assert.Throws<ArgumentException>(
            () => execution.Advance(
                request,
                [],
                sourceCompleted: false));

        QueryOverflowStep<int> completed =
            execution.Advance(
                request,
                [],
                sourceCompleted: true);
        Assert.Equal(
            QueryOverflowExecutionState.Completed,
            completed.State);
        Assert.False(
            execution.TryRequestInput(1, 1, out _));
        Assert.Throws<InvalidOperationException>(
            execution.Cancel);
    }

    [Fact]
    public void RequestsAreExecutionAndGenerationBound()
    {
        QueryOverflowPlan<int> plan =
            RequirePlan(
                Resolve(),
                QuerySpaceTerminalRequirement.Rows);
        QueryOverflowExecution<int> first = plan.Start();
        QueryOverflowExecution<int> second = plan.Start();
        Assert.True(
            first.TryRequestInput(
                1,
                1,
                out QueryOverflowInputRequest firstRequest));
        Assert.True(
            second.TryRequestInput(
                1,
                1,
                out QueryOverflowInputRequest secondRequest));

        Assert.Throws<InvalidOperationException>(
            () => second.Advance(
                firstRequest,
                [1],
                sourceCompleted: false));
        QueryOverflowStep<int> secondStep =
            second.Advance(
                secondRequest,
                [2],
                sourceCompleted: true);
        Assert.Equal([2], secondStep.Rows);

        QueryOverflowStep<int> firstStep =
            first.Advance(
                firstRequest,
                [1],
                sourceCompleted: false);
        Assert.Equal([1], firstStep.Rows);
        Assert.True(
            first.TryRequestInput(
                1,
                1,
                out QueryOverflowInputRequest nextRequest));
        Assert.Throws<InvalidOperationException>(
            () => first.Advance(
                firstRequest,
                [2],
                sourceCompleted: true));
        QueryOverflowStep<int> completed =
            first.Advance(
                nextRequest,
                [2],
                sourceCompleted: true);
        Assert.Equal([2], completed.Rows);
    }

    private static IReadOnlyList<int> ExecuteRows(
        IReadOnlyList<int[]> batches,
        ResolvedRowQueryPlan<int> rowPlan)
    {
        QueryOverflowExecution<int> execution =
            RequirePlan(
                rowPlan,
                QuerySpaceTerminalRequirement.Rows)
            .Start();
        var result = new List<int>();
        int batchIndex = 0;
        int batchOffset = 0;
        while (execution.TryRequestInput(
            batches[batchIndex].Length - batchOffset,
            int.MaxValue,
            out QueryOverflowInputRequest request))
        {
            int[] sourceBatch = batches[batchIndex];
            int count =
                Math.Min(
                    request.MaximumCandidateRows,
                    sourceBatch.Length - batchOffset);
            int[] batch =
                sourceBatch
                    .Skip(batchOffset)
                    .Take(count)
                    .ToArray();
            bool sourceCompleted =
                batchIndex == batches.Count - 1
                && batchOffset + count == sourceBatch.Length;
            QueryOverflowStep<int> step =
                execution.Advance(
                    request,
                    batch,
                    sourceCompleted);
            result.AddRange(step.Rows);
            AdvanceSourceCursor(
                batches,
                ref batchIndex,
                ref batchOffset,
                step.ConsumedRows);
        }

        return result;
    }

    private static int ExecuteCount(
        IReadOnlyList<int[]> batches,
        ResolvedRowQueryPlan<int> rowPlan)
    {
        QueryOverflowExecution<int> execution =
            RequirePlan(
                rowPlan,
                QuerySpaceTerminalRequirement.Count)
            .Start();
        int batchIndex = 0;
        int batchOffset = 0;
        while (execution.TryRequestInput(
            batches[batchIndex].Length - batchOffset,
            finalRowCredit: 0,
            out QueryOverflowInputRequest request))
        {
            int[] sourceBatch = batches[batchIndex];
            int count =
                Math.Min(
                    request.MaximumCandidateRows,
                    sourceBatch.Length - batchOffset);
            int[] batch =
                sourceBatch
                    .Skip(batchOffset)
                    .Take(count)
                    .ToArray();
            bool sourceCompleted =
                batchIndex == batches.Count - 1
                && batchOffset + count == sourceBatch.Length;
            QueryOverflowStep<int> step =
                execution.Advance(
                    request,
                    batch,
                    sourceCompleted);
            AdvanceSourceCursor(
                batches,
                ref batchIndex,
                ref batchOffset,
                step.ConsumedRows);
            if (step.HasCount)
                return step.Count;
        }

        throw new InvalidOperationException(
            "The Count execution ended without a result.");
    }

    private static IEnumerable<IReadOnlyList<int[]>>
        EnumeratePartitions(IReadOnlyList<int> source)
    {
        int boundaryCount = source.Count - 1;
        for (int boundaries = 0;
             boundaries < 1 << boundaryCount;
             boundaries++)
        {
            var batches = new List<int[]>();
            int start = 0;
            for (int index = 0; index < boundaryCount; index++)
            {
                if ((boundaries & 1 << index) == 0)
                    continue;

                batches.Add(
                    source
                        .Skip(start)
                        .Take(index + 1 - start)
                        .ToArray());
                start = index + 1;
            }

            batches.Add(source.Skip(start).ToArray());
            yield return batches;
        }
    }

    private static void AdvanceSourceCursor(
        IReadOnlyList<int[]> batches,
        ref int batchIndex,
        ref int batchOffset,
        int consumedRows)
    {
        batchOffset += consumedRows;
        if (batchOffset != batches[batchIndex].Length)
            return;

        batchIndex++;
        batchOffset = 0;
    }

    private static QueryOverflowStep<T> Advance<T>(
        QueryOverflowExecution<T> execution,
        IReadOnlyList<T> batch,
        bool sourceCompleted)
    {
        Assert.True(
            execution.TryRequestInput(
                Math.Max(1, batch.Count),
                int.MaxValue,
                out QueryOverflowInputRequest request));
        return execution.Advance(
            request,
            batch,
            sourceCompleted);
    }

    private static QueryOverflowPlan<T> RequirePlan<T>(
        ResolvedRowQueryPlan<T> rowPlan,
        QuerySpaceTerminalRequirement terminal)
    {
        QueryOverflowAdmission<T> admission =
            QueryOverflowPlan<T>.Admit(
                rowPlan,
                terminal);
        Assert.True(
            admission.IsAccepted,
            $"Plan declined: {admission.DeclineReason}.");
        Assert.Null(admission.DeclineReason);
        return Assert.IsType<QueryOverflowPlan<T>>(
            admission.Plan);
    }

    private static ResolvedRowQueryPlan<int> Resolve(
        IReadOnlyList<RowQueryPredicateIntent>? predicates = null,
        RowQueryOrderIntent? baselineOrder = null,
        IReadOnlyList<
            RowSelectionIntentOperation<RowQueryOrderIntent>>?
            selection = null) =>
        Resolve(
            CreateIntVocabulary(),
            predicates ?? [],
            baselineOrder,
            selection ?? []);

    private static ResolvedRowQueryPlan<int> ResolveWithPredicate(
        Predicate<int> predicate)
    {
        RowQueryKey<int> key =
            RowQueryKey<int>.Create(
                RowQueryKeyIdentity.Create(),
                "value",
                [RowQueryOperator.Equals],
                static value =>
                    RowQueryValue<int>.Present(value),
                (_, _) => predicate);
        return Resolve(
            RowQueryVocabulary<int>.Create(
                RowQueryVocabularyIdentity.Create(),
                [key],
                []),
            [new("value", RowQueryOperator.Equals, new("any"))],
            baselineOrder: null,
            selection: []);
    }

    private static ResolvedRowQueryPlan<string> ResolveString(
        IReadOnlyList<
            RowSelectionIntentOperation<RowQueryOrderIntent>>
            selection)
    {
        RowQueryVocabulary<string> vocabulary =
            RowQueryVocabulary<string>.Create(
                RowQueryVocabularyIdentity.Create(),
                [],
                []);
        return Resolve(
            vocabulary,
            [],
            baselineOrder: null,
            selection);
    }

    private static ResolvedRowQueryPlan<T> Resolve<T>(
        RowQueryVocabulary<T> vocabulary,
        IReadOnlyList<RowQueryPredicateIntent> predicates,
        RowQueryOrderIntent? baselineOrder,
        IReadOnlyList<
            RowSelectionIntentOperation<RowQueryOrderIntent>>
            selection)
    {
        RowQueryResolutionResult<T> resolution =
            RowQueryResolver.Resolve(
                vocabulary,
                RowQueryIntent.Create(
                    predicates,
                    baselineOrder,
                    RowSelectionIntent<
                        RowQueryOrderIntent>.Create(
                            selection)));
        return Assert.IsType<ResolvedRowQueryPlan<T>>(
            resolution.Plan);
    }

    private static RowQueryVocabulary<int> CreateIntVocabulary()
    {
        RowQueryKey<int> key =
            RowQueryKey<int>.Create(
                RowQueryKeyIdentity.Create(),
                "value",
                [RowQueryOperator.GreaterOrEqual],
                static value =>
                    RowQueryValue<int>.Present(value),
                static (@operator, token) =>
                    @operator
                        is RowQueryOperator.GreaterOrEqual
                    && int.TryParse(
                        token.Text,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out int expected)
                        ? actual => actual >= expected
                        : null,
                static direction =>
                    RowQueryValueOrder.Create(
                        Comparer<int>.Default,
                        direction,
                        missingLast: true));
        return RowQueryVocabulary<int>.Create(
            RowQueryVocabularyIdentity.Create(),
            [key],
            []);
    }

    private static RowQueryPredicateIntent AtLeast(int value) =>
        new(
            "value",
            RowQueryOperator.GreaterOrEqual,
            new(
                value.ToString(
                    CultureInfo.InvariantCulture)));

    private static RowSelectionIntentOperation<RowQueryOrderIntent>
        Head(int count) =>
        RowSelectionIntentOperation<
            RowQueryOrderIntent>.Head(count);

    private static bool IsQueryOverflowInterface(Type type) =>
        type.Namespace?.StartsWith(
            "QueryOverflow",
            StringComparison.Ordinal) is true;
}
