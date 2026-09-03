using System.Collections;
using DotnetInspector.RowSelection;

namespace DotnetInspector.Sections.Tests;

public sealed class RowsCohortContractTests
{
    [Fact]
    public void RowsCohortPreservesOwnerIdentityAndSelection()
    {
        RankedRow[] alpha =
            Enumerable.Range(1, 8)
                .Select(value => new RankedRow(value))
                .ToArray();
        RankedRow[] beta =
            Enumerable.Range(11, 8)
                .Select(value => new RankedRow(value))
                .ToArray();

        RowsCohortResult<string, RankedRow> result =
            RowsCohortExecutor.Apply(
                [
                    RowsCohortSequence<string, RankedRow>.Create(
                        "alpha",
                        alpha),
                    RowsCohortSequence<string, RankedRow>.Create(
                        "beta",
                        beta)
                ],
                Plan(
                    RowSelectionStage<string>.Window(
                        3,
                        6),
                    RowSelectionStage<string>.Tail(2)));

        Assert.True(result.IsSuccess);
        Assert.Null(result.Failure);
        Assert.Equal(
            ["alpha", "beta"],
            result.RowSets.Select(rowSet => rowSet.Identity));
        Assert.Equal(
            [5, 6],
            result.RowSets[0].Values.Select(row => row.Value));
        Assert.Equal(
            [15, 16],
            result.RowSets[1].Values.Select(row => row.Value));
        Assert.Same(alpha[4], result.RowSets[0].Values[0]);
        Assert.Same(beta[5], result.RowSets[1].Values[1]);

        int resolverCalls = 0;
        RowsCohortResult<string, RankedRow> ranked =
            RowsCohortExecutor.Apply(
                [
                    RowsCohortSequence<string, RankedRow>.Create(
                        "alpha",
                        alpha),
                    RowsCohortSequence<string, RankedRow>.Create(
                        "beta",
                        beta)
                ],
                Plan(
                    RowSelectionStage<string>.Top(
                        2,
                        "descending")),
                order =>
                {
                    Assert.Equal("descending", order);
                    resolverCalls++;
                    return Comparer<RankedRow>.Create(
                        (left, right) =>
                            right.Value.CompareTo(left.Value));
                });

        Assert.True(ranked.IsSuccess);
        Assert.Equal(1, resolverCalls);
        Assert.Equal(
            [8, 7],
            ranked.RowSets[0].Values.Select(row => row.Value));
        Assert.Equal(
            [18, 17],
            ranked.RowSets[1].Values.Select(row => row.Value));
    }

    [Fact]
    public void RowsCohortBindsStrictFailureAtomically()
    {
        RowsCohortResult<string, int> result =
            RowsCohortExecutor.Apply(
                [
                    RowsCohortSequence<string, int>.Create(
                        "alpha",
                        [1, 2]),
                    RowsCohortSequence<string, int>.Create(
                        "beta",
                        [11, 12, 13])
                ],
                Plan(
                    RowSelectionStage<string>.Head(2),
                    RowSelectionStage<string>.Window(
                        2,
                        3)));

        Assert.False(result.IsSuccess);
        Assert.Empty(result.RowSets);
        RowsCohortSemanticFailure<string> failure =
            Assert.IsType<RowsCohortSemanticFailure<string>>(
                result.Failure);
        Assert.Equal("alpha", failure.Identity);
        Assert.Equal(2, failure.Failure.StageNumber);
        Assert.Equal(3, failure.Failure.RequiredPosition);
        Assert.Equal(2, failure.Failure.AvailableCount);
    }

    [Fact]
    public void RowsCohortRejectsAmbiguousOrInvalidInput()
    {
        RowsCohortSequence<string, int> alpha =
            RowsCohortSequence<string, int>.Create(
                "alpha",
                [2, 1]);

        Assert.Throws<ArgumentNullException>(
            () => RowsCohortSequence<string, int>.Create(
                null!,
                [1]));
        Assert.Throws<ArgumentNullException>(
            () => RowsCohortSequence<string, int>.Create(
                "alpha",
                null!));
        Assert.Throws<ArgumentNullException>(
            () => RowsCohortExecutor.Apply(
                (IReadOnlyList<RowsCohortSequence<string, int>>)null!,
                RowSelectionPlan<string>.Empty));
        Assert.Throws<ArgumentNullException>(
            () => RowsCohortExecutor.Apply(
                [alpha],
                (RowSelectionPlan<string>)null!));
        Assert.Throws<ArgumentException>(
            () => RowsCohortExecutor.Apply(
                Array.Empty<RowsCohortSequence<string, int>>(),
                RowSelectionPlan<string>.Empty));
        Assert.Throws<ArgumentNullException>(
            () => RowsCohortExecutor.Apply(
                [alpha, null!],
                RowSelectionPlan<string>.Empty));
        int duplicateResolverCalls = 0;
        Assert.Throws<ArgumentException>(
            () => RowsCohortExecutor.Apply(
                [
                    alpha,
                    RowsCohortSequence<string, int>.Create(
                        "alpha",
                        [3])
                ],
                Plan(
                    RowSelectionStage<string>.Top(
                        1,
                        "ascending")),
                _ =>
                {
                    duplicateResolverCalls++;
                    return Comparer<int>.Default;
                }));
        Assert.Equal(0, duplicateResolverCalls);
        Assert.Throws<InvalidOperationException>(
            () => RowsCohortExecutor.Apply(
                [alpha],
                Plan(
                    RowSelectionStage<string>.Top(
                        1,
                        "ascending"))));

        var resolverException =
            new SentinelException("resolver");
        SentinelException observedResolver =
            Assert.Throws<SentinelException>(
                () => RowsCohortExecutor.Apply(
                    [alpha],
                    Plan(
                        RowSelectionStage<string>.Top(
                            1,
                            "ascending")),
                    _ => throw resolverException));
        Assert.Same(
            resolverException,
            observedResolver);

        var comparerException =
            new SentinelException("comparer");
        SentinelException observedComparer =
            Assert.Throws<SentinelException>(
                () => RowsCohortExecutor.Apply(
                    [alpha],
                    Plan(
                        RowSelectionStage<string>.Top(
                            2,
                            "ascending")),
                    _ => Comparer<int>.Create(
                        (_, _) => throw comparerException)));
        Assert.Same(
            comparerException,
            observedComparer);
    }

    [Fact]
    public void RowsCohortSnapshotsInputsAndResults()
    {
        var first = new RankedRow(1);
        var second = new RankedRow(2);
        var values = new List<RankedRow>
        {
            first,
            second
        };
        RowsCohortSequence<string, RankedRow> alpha =
            RowsCohortSequence<string, RankedRow>.Create(
                "alpha",
                values);

        values.Clear();
        Assert.Equal(
            [1, 2],
            alpha.Values.Select(row => row.Value));
        AssertReadOnly(alpha.Values);

        RowsCohortResult<string, RankedRow> result =
            RowsCohortExecutor.Apply(
                [alpha],
                RowSelectionPlan<string>.Empty);

        Assert.True(result.IsSuccess);
        Assert.Single(result.RowSets);
        Assert.Equal("alpha", result.RowSets[0].Identity);
        Assert.Equal(
            [1, 2],
            result.RowSets[0].Values.Select(row => row.Value));
        Assert.Same(first, result.RowSets[0].Values[0]);
        Assert.Same(second, result.RowSets[0].Values[1]);
        AssertReadOnly(result.RowSets);
        AssertReadOnly(result.RowSets[0].Values);

        first.Value = 10;
        Assert.Equal(10, result.RowSets[0].Values[0].Value);
    }

    private static RowSelectionPlan<string> Plan(
        params RowSelectionStage<string>[] stages) =>
        RowSelectionPlan<string>.Create(stages);

    private static void AssertReadOnly<T>(
        IReadOnlyList<T> values)
    {
        IList list =
            Assert.IsAssignableFrom<IList>(values);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(
            () => list[0] = values[0]);
    }

    private sealed class RankedRow(int value)
    {
        public int Value { get; set; } = value;
    }

    private sealed class SentinelException(string message)
        : Exception(message);
}
