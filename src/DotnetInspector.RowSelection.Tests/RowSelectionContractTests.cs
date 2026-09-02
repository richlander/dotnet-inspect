using System.Collections;
using DotnetInspector.RowSelection;

namespace DotnetInspector.RowSelection.Tests;

public sealed class RowSelectionContractTests
{
    [Fact]
    public void SelectionStagesComposeInDeclaredOrder()
    {
        RowSelectionResult<int> demo =
            RowSelectionExecutor.Apply(
                [1, 2, 3, 4, 5, 6, 7, 8],
                Plan(
                    RowSelectionStage<string>.Window(
                        3,
                        6),
                    RowSelectionStage<string>.Tail(2)));
        Assert.True(demo.IsSuccess);
        Assert.Equal([5, 6], demo.Values);

        RowSelectionResult<int> reindexed =
            RowSelectionExecutor.Apply(
                [1, 2, 3, 4, 5, 6, 7, 8],
                Plan(
                    RowSelectionStage<string>.Tail(4),
                    RowSelectionStage<string>.Window(
                        2,
                        3)));
        Assert.Equal([6, 7], reindexed.Values);

        RowSelectionResult<int> pathological =
            RowSelectionExecutor.Apply(
                [1, 2, 3, 4],
                Plan(
                    RowSelectionStage<string>.Head(2),
                    RowSelectionStage<string>.Window(
                        2,
                        3)));
        Assert.False(pathological.IsSuccess);
        Assert.Empty(pathological.Values);
        AssertFailure(
            pathological.Failure,
            stage: 2,
            required: 3,
            available: 2);

        int[] values = [3, 1, 2];
        RowSelectionResult<int> identity =
            RowSelectionExecutor.Apply(
                values,
                RowSelectionPlan<string>.Empty);
        values[0] = 99;
        Assert.Equal([3, 1, 2], identity.Values);

        var first = new RankedRow("first", 1);
        var second = new RankedRow("second", 1);
        var third = new RankedRow("third", 0);
        RowSelectionResult<RankedRow> ranked =
            RowSelectionExecutor.Apply(
                [first, second, third],
                Plan(
                    RowSelectionStage<string>.Top(
                        10,
                        "rank")),
                _ => Comparer<RankedRow>.Create(
                    (left, right) =>
                        left.Score.CompareTo(
                            right.Score)));
        Assert.Equal(
            [third, first, second],
            ranked.Values);
        Assert.Same(third, ranked.Values[0]);
        Assert.Same(first, ranked.Values[1]);
        Assert.Same(second, ranked.Values[2]);
    }

    [Fact]
    public void SelectionCountsAreLenientAndWindowsAreStrict()
    {
        Assert.Equal(
            [1, 2, 3],
            RowSelectionExecutor.Apply(
                [1, 2, 3],
                Plan(
                    RowSelectionStage<string>.Head(10)))
                .Values);
        Assert.Equal(
            [1, 2, 3],
            RowSelectionExecutor.Apply(
                [1, 2, 3],
                Plan(
                    RowSelectionStage<string>.Tail(10)))
                .Values);
        Assert.Equal(
            [1, 2, 3],
            RowSelectionExecutor.Apply(
                [3, 1, 2],
                Plan(
                    RowSelectionStage<string>.Top(
                        10,
                        "ascending")),
                _ => Comparer<int>.Default)
                .Values);

        AssertFailure(
            RowSelectionExecutor.Apply(
                [1, 2],
                Plan(
                    RowSelectionStage<string>.Window(
                        1,
                        3)))
                .Failure,
            stage: 1,
            required: 3,
            available: 2);
        AssertFailure(
            RowSelectionExecutor.Apply(
                [1, 2],
                Plan(
                    RowSelectionStage<string>.Window(
                        null,
                        3)))
                .Failure,
            stage: 1,
            required: 3,
            available: 2);
        AssertFailure(
            RowSelectionExecutor.Apply(
                [1, 2],
                Plan(
                    RowSelectionStage<string>.Window(
                        3,
                        null)))
                .Failure,
            stage: 1,
            required: 3,
            available: 2);
        Assert.Equal(
            [1, 2],
            RowSelectionExecutor.Apply(
                [1, 2],
                Plan(
                    RowSelectionStage<string>.Window(
                        null,
                        null)))
                .Values);
    }

    [Fact]
    public void RowSelectionConstructionRejectsInvalidInputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RowSelectionStage<string>.Head(0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RowSelectionStage<string>.Tail(-1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RowSelectionStage<string>.Top(
                0,
                "rank"));
        Assert.Throws<ArgumentNullException>(
            () => RowSelectionStage<string>.Top(
                1,
                null!));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RowSelectionStage<string>.Window(
                0,
                null));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RowSelectionStage<string>.Window(
                null,
                0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RowSelectionStage<string>.Window(
                3,
                2));
        Assert.Throws<ArgumentNullException>(
            () => RowSelectionPlan<string>.Create(
                null!));
        Assert.Throws<ArgumentNullException>(
            () => RowSelectionPlan<string>.Create(
                [null!]));
        Assert.Throws<ArgumentNullException>(
            () => RowSelectionPlan<string>.Empty.Append(
                null!));
        Assert.Throws<ArgumentNullException>(
            () => NamedRowSequence<int>.Create(
                null!,
                [1]));
        Assert.Throws<ArgumentNullException>(
            () => NamedRowSequence<int>.Create(
                RowSequenceKey.Create(1),
                null!));
        Assert.Throws<ArgumentNullException>(
            () => RowSelectionExecutor.Apply<int, string>(
                null!,
                RowSelectionPlan<string>.Empty));
        Assert.Throws<ArgumentNullException>(
            () => RowSelectionExecutor.Apply(
                [1],
                (RowSelectionPlan<string>)null!));
        Assert.Throws<ArgumentNullException>(
            () => RowSelectionExecutor.ApplyNamed<int, string>(
                null!,
                RowSelectionPlan<string>.Empty));
        Assert.Throws<ArgumentNullException>(
            () => RowSelectionExecutor.ApplyNamed(
                [NamedRowSequence<int>.Create(
                    RowSequenceKey.Create(1),
                    [1])],
                (RowSelectionPlan<string>)null!));
        Assert.Throws<ArgumentNullException>(
            () => RowSelectionExecutor.ApplyNamed<int, string>(
                [null!],
                RowSelectionPlan<string>.Empty));

        RowSelectionStage<string> head =
            RowSelectionStage<string>.Head(1);
        RowSelectionStage<string> window =
            RowSelectionStage<string>.Window(
                1,
                1);
        Assert.Throws<InvalidOperationException>(
            () => _ = head.Start);
        Assert.Throws<InvalidOperationException>(
            () => _ = head.End);
        Assert.Throws<InvalidOperationException>(
            () => _ = head.Order);
        Assert.Throws<InvalidOperationException>(
            () => _ = window.Count);
        Assert.Throws<InvalidOperationException>(
            () => _ = window.Order);

        InvalidOperationException missing =
            Assert.Throws<InvalidOperationException>(
                () => RowSelectionExecutor.Apply(
                    Array.Empty<int>(),
                    Plan(
                        RowSelectionStage<string>.Top(
                            1,
                            "rank"))));
        Assert.Contains("stage 1", missing.Message);
        InvalidOperationException unresolved =
            Assert.Throws<InvalidOperationException>(
                () => RowSelectionExecutor.Apply(
                    [1],
                    Plan(
                        RowSelectionStage<string>.Top(
                            1,
                            "rank")),
                    _ => null));
        Assert.Contains("stage 1", unresolved.Message);

        RowSelectionResult<string?> nullable =
            RowSelectionExecutor.Apply(
                new string?[] { null, "value" },
                Plan(
                    RowSelectionStage<string>.Head(2)));
        Assert.Null(nullable.Values[0]);
        Assert.Equal("value", nullable.Values[1]);

        Assert.Equal(
            int.MinValue,
            RowSequenceKey.Create(int.MinValue).Value);
        Assert.Equal(
            int.MaxValue,
            RowSequenceKey.Create(int.MaxValue).Value);
    }

    [Fact]
    public void SelectionCallbacksFollowStageOrder()
    {
        int calls = 0;
        RowSelectionResult<int> failure =
            RowSelectionExecutor.Apply(
                [1],
                Plan(
                    RowSelectionStage<string>.Window(
                        1,
                        2),
                    RowSelectionStage<string>.Top(
                        1,
                        "late")),
                _ =>
                {
                    calls++;
                    return Comparer<int>.Default;
                });
        Assert.False(failure.IsSuccess);
        Assert.Equal(0, calls);

        var orders = new List<string>();
        NamedRowSelectionResult<int> named =
            RowSelectionExecutor.ApplyNamed(
                [
                    NamedRowSequence<int>.Create(
                        RowSequenceKey.Create(1),
                        [3, 1, 2]),
                    NamedRowSequence<int>.Create(
                        RowSequenceKey.Create(2),
                        [6, 4, 5]),
                ],
                Plan(
                    RowSelectionStage<string>.Top(
                        3,
                        "first"),
                    RowSelectionStage<string>.Top(
                        2,
                        "first")),
                order =>
                {
                    orders.Add(order);
                    return Comparer<int>.Default;
                });
        Assert.True(named.IsSuccess);
        Assert.Equal(["first", "first"], orders);

        int emptyResolverCalls = 0;
        NamedRowSelectionResult<int> emptyNamed =
            RowSelectionExecutor.ApplyNamed(
                Array.Empty<NamedRowSequence<int>>(),
                Plan(
                    RowSelectionStage<string>.Top(
                        1,
                        "rank")),
                _ =>
                {
                    emptyResolverCalls++;
                    return Comparer<int>.Default;
                });
        Assert.True(emptyNamed.IsSuccess);
        Assert.Empty(emptyNamed.Sequences);
        Assert.Equal(0, emptyResolverCalls);
        Assert.Throws<InvalidOperationException>(
            () => RowSelectionExecutor.ApplyNamed(
                Array.Empty<NamedRowSequence<int>>(),
                Plan(
                    RowSelectionStage<string>.Top(
                        1,
                        "rank"))));

        int crossSequenceCalls = 0;
        NamedRowSelectionResult<int> crossSequence =
            RowSelectionExecutor.ApplyNamed(
                [
                    NamedRowSequence<int>.Create(
                        RowSequenceKey.Create(11),
                        [2, 1]),
                    NamedRowSequence<int>.Create(
                        RowSequenceKey.Create(22),
                        [3]),
                ],
                Plan(
                    RowSelectionStage<string>.Top(
                        2,
                        "rank"),
                    RowSelectionStage<string>.Window(
                        1,
                        2)),
                _ =>
                {
                    crossSequenceCalls++;
                    return Comparer<int>.Default;
                });
        Assert.False(crossSequence.IsSuccess);
        Assert.Equal(1, crossSequenceCalls);
        Assert.Equal(
            22,
            Assert.IsType<NamedRowWindowFailure>(
                crossSequence.Failure)
                .Key.Value);

        var resolverFailure =
            new ExpectedException("resolver");
        Exception? resolverThrown =
            Record.Exception(
                () => RowSelectionExecutor.Apply(
                    [2, 1],
                    Plan(
                        RowSelectionStage<string>.Top(
                            1,
                            "rank")),
                    _ => throw resolverFailure));
        Assert.Same(resolverFailure, resolverThrown);

        var comparerFailure =
            new ExpectedException("comparer");
        Exception? comparerThrown =
            Record.Exception(
                () => RowSelectionExecutor.Apply(
                    [2, 1],
                    Plan(
                        RowSelectionStage<string>.Top(
                            1,
                            "rank")),
                    _ =>
                        new ThrowingComparer<int>(
                            comparerFailure)));
        Assert.Same(comparerFailure, comparerThrown);
    }

    [Fact]
    public void NamedSelectionIsAtomicAndDeterministic()
    {
        NamedRowSequence<int> first =
            NamedRowSequence<int>.Create(
                RowSequenceKey.Create(10),
                [1, 2, 3]);
        NamedRowSequence<int> second =
            NamedRowSequence<int>.Create(
                RowSequenceKey.Create(20),
                [4]);
        NamedRowSelectionResult<int> failed =
            RowSelectionExecutor.ApplyNamed(
                [first, second],
                Plan(
                    RowSelectionStage<string>.Window(
                        1,
                        2)));
        Assert.False(failed.IsSuccess);
        Assert.Empty(failed.Sequences);
        NamedRowWindowFailure failure =
            Assert.IsType<NamedRowWindowFailure>(
                failed.Failure);
        Assert.Equal(20, failure.Key.Value);
        AssertFailure(
            failure.Failure,
            stage: 1,
            required: 2,
            available: 1);

        NamedRowSelectionResult<int> firstSequenceFailure =
            RowSelectionExecutor.ApplyNamed(
                [
                    NamedRowSequence<int>.Create(
                        RowSequenceKey.Create(11),
                        [1]),
                    NamedRowSequence<int>.Create(
                        RowSequenceKey.Create(22),
                        Array.Empty<int>()),
                ],
                Plan(
                    RowSelectionStage<string>.Window(
                        1,
                        2)));
        Assert.Equal(
            11,
            Assert.IsType<NamedRowWindowFailure>(
                firstSequenceFailure.Failure)
                .Key.Value);

        NamedRowSelectionResult<int> firstStageFailure =
            RowSelectionExecutor.ApplyNamed(
                [
                    NamedRowSequence<int>.Create(
                        RowSequenceKey.Create(33),
                        [1, 2, 3]),
                ],
                Plan(
                    RowSelectionStage<string>.Window(
                        1,
                        5),
                    RowSelectionStage<string>.Window(
                        1,
                        9)));
        AssertFailure(
            Assert.IsType<NamedRowWindowFailure>(
                firstStageFailure.Failure)
                .Failure,
            stage: 1,
            required: 5,
            available: 3);

        NamedRowSelectionResult<int> success =
            RowSelectionExecutor.ApplyNamed(
                [
                    NamedRowSequence<int>.Create(
                        RowSequenceKey.Create(30),
                        [3, 2, 1]),
                    NamedRowSequence<int>.Create(
                        RowSequenceKey.Create(40),
                        [6, 5, 4]),
                ],
                Plan(
                    RowSelectionStage<string>.Head(2)));
        Assert.True(success.IsSuccess);
        Assert.Null(success.Failure);
        Assert.Equal(
            [30, 40],
            success.Sequences.Select(
                sequence => sequence.Key.Value));
        Assert.Equal(
            [3, 2],
            success.Sequences[0].Values);
        Assert.Equal(
            [6, 5],
            success.Sequences[1].Values);

        int resolverCalls = 0;
        Assert.Throws<ArgumentException>(
            () => RowSelectionExecutor.ApplyNamed(
                [
                    NamedRowSequence<int>.Create(
                        RowSequenceKey.Create(7),
                        [1]),
                    NamedRowSequence<int>.Create(
                        RowSequenceKey.Create(7),
                        [2]),
                ],
                Plan(
                    RowSelectionStage<string>.Top(
                        1,
                        "rank")),
                _ =>
                {
                    resolverCalls++;
                    return Comparer<int>.Default;
                }));
        Assert.Equal(0, resolverCalls);

        RowSequenceKey left =
            RowSequenceKey.Create(-1);
        RowSequenceKey right =
            RowSequenceKey.Create(-1);
        Assert.NotSame(left, right);
        Assert.Equal(left, right);
        Assert.True(left.Equals(right));
        Assert.True(left.Equals((object)right));
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
        Assert.False(left.Equals(null));
        Assert.False(left.Equals("not a key"));
    }

    [Fact]
    public void RowSelectionSnapshotsAreImmutable()
    {
        var stages =
            new List<RowSelectionStage<string>>
            {
                RowSelectionStage<string>.Head(2),
            };
        RowSelectionPlan<string> plan =
            RowSelectionPlan<string>.Create(stages);
        stages[0] =
            RowSelectionStage<string>.Tail(1);
        stages.Add(
            RowSelectionStage<string>.Head(1));
        Assert.Single(plan.Stages);
        Assert.Equal(
            RowSelectionStageKind.Head,
            plan.Stages[0].Kind);

        RowSelectionPlan<string> appended =
            plan.Append(
                RowSelectionStage<string>.Tail(1));
        Assert.Single(plan.Stages);
        Assert.Equal(2, appended.Stages.Count);

        var values = new List<int> { 1, 2, 3 };
        NamedRowSequence<int> named =
            NamedRowSequence<int>.Create(
                RowSequenceKey.Create(1),
                values);
        values[0] = 99;
        values.Add(4);
        Assert.Equal([1, 2, 3], named.Values);

        int[] input = [1, 2, 3];
        RowSelectionResult<int> result =
            RowSelectionExecutor.Apply(
                input,
                RowSelectionPlan<string>.Empty);
        input[0] = 99;
        Assert.Equal([1, 2, 3], result.Values);

        AssertReadOnly(plan.Stages);
        AssertReadOnly(named.Values);
        AssertReadOnly(result.Values);

        var row = new MutableRow("before");
        RowSelectionResult<MutableRow> rowResult =
            RowSelectionExecutor.Apply(
                [row],
                RowSelectionPlan<string>.Empty);
        Assert.Same(row, rowResult.Values[0]);
        row.Value = "after";
        Assert.Equal(
            "after",
            rowResult.Values[0].Value);

        NamedRowSelectionResult<int> namedResult =
            RowSelectionExecutor.ApplyNamed(
                [named],
                RowSelectionPlan<string>.Empty);
        AssertReadOnly(namedResult.Sequences);
        AssertReadOnly(
            namedResult.Sequences[0].Values);
    }

    private static RowSelectionPlan<string> Plan(
        params RowSelectionStage<string>[] stages) =>
        RowSelectionPlan<string>.Create(stages);

    private static void AssertFailure(
        RowWindowFailure? failure,
        int stage,
        int required,
        int available)
    {
        RowWindowFailure value =
            Assert.IsType<RowWindowFailure>(failure);
        Assert.Equal(stage, value.StageNumber);
        Assert.Equal(
            required,
            value.RequiredPosition);
        Assert.Equal(
            available,
            value.AvailableCount);
    }

    private static void AssertReadOnly<T>(
        IReadOnlyList<T> values)
    {
        IList list = Assert.IsAssignableFrom<IList>(
            values);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(
            () => list[0] = values[0]);
    }

    private sealed record RankedRow(
        string Name,
        int Score);

    private sealed class MutableRow(string value)
    {
        public string Value { get; set; } = value;
    }

    private sealed class ExpectedException(
        string message) :
        Exception(message);

    private sealed class ThrowingComparer<T>(
        Exception exception) :
        IComparer<T>
    {
        public int Compare(T? left, T? right) =>
            throw exception;
    }
}
