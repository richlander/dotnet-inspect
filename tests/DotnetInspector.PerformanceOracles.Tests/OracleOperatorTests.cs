using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using NLinq;

namespace DotnetInspector.PerformanceOracles.Tests;

/// <summary>
/// Pins the fixture's operators to System.Linq on small inputs, and pins the
/// properties the scorecard relies on: Take never pulls past its count, Skip
/// discards only when pulled, and a strict window fails rather than truncates.
/// </summary>
public sealed class OracleOperatorTests
{
    static readonly int[][] Inputs = [[], [1], [1, 2, 3], [5, 4, 3, 2, 1, 0, 9, 8]];
    static readonly int[] Counts = [0, 1, 3, 8, 11];

    [Fact]
    public void ToImmutableLookup_MatchesSystemLinqInSourceOrder()
    {
        ImmutableArray<int> input = [5, 2, 3, 4, 1, 6];
        IReadOnlyDictionary<int, ImmutableArray<int>> actual =
            input.AsNLinq()
                .ToImmutableLookup<
                    ImmutableArrayEnumerator<int>,
                    int,
                    int,
                    Parity>(default);
        ILookup<int, int> expected =
            input.ToLookup(static value => value % 2);

        Assert.Equal(
            expected.Select(static group => group.Key),
            actual.Keys);
        foreach (IGrouping<int, int> group in expected)
        {
            Assert.Equal(
                group,
                actual[group.Key]);
        }
    }

    [Fact]
    public void ToImmutableLookup_UsesOnlyRemainingElements()
    {
        ImmutableArray<int> input = [1, 2, 3, 4, 5];
        ImmutableArrayEnumerator<int> source = input.AsNLinq();
        source.TryGetNext(out _);

        IReadOnlyDictionary<int, ImmutableArray<int>> actual =
            source.ToImmutableLookup<
                ImmutableArrayEnumerator<int>,
                int,
                int,
                Parity>(default);

        Assert.Equal([2, 4], actual[0]);
        Assert.Equal([3, 5], actual[1]);
    }

    [Fact]
    public void ToImmutableRunLookup_MatchesSystemLinqForEveryRunShape()
    {
        ImmutableArray<int>[] inputs =
        [
            [],
            [1],
            [1, 1, 2, 2, 3],
            [3, 3, 2, 2, 1],
            [1, 2, 1, 3, 2],
        ];

        foreach (ImmutableArray<int> input in inputs)
        {
            IReadOnlyDictionary<int, ImmutableArray<int>> actual =
                input.ToImmutableRunLookup<
                    int,
                    int,
                    Identity,
                    AscendingInt32>(
                        default,
                        default);
            ILookup<int, int> expected =
                input.ToLookup(static value => value);

            Assert.Equal(
                expected.Select(static group => group.Key),
                actual.Keys);
            foreach (IGrouping<int, int> group in expected)
                Assert.Equal(group, actual[group.Key]);
            Assert.Equal(
                expected.Select(static group => group.ToArray()),
                actual.Values.Select(static group => group.ToArray()));
            Assert.Equal(
                expected.Select(static group =>
                    KeyValuePair.Create(
                        group.Key,
                        group.ToArray())),
                actual.Select(static group =>
                    KeyValuePair.Create(
                        group.Key,
                        group.Value.ToArray())));
            Assert.False(actual.TryGetValue(-1, out var missing));
            Assert.True(missing.IsDefault);
        }
    }

    [Fact]
    public void Take_MatchesSystemLinq()
    {
        foreach (int[] input in Inputs)
        {
            foreach (int count in Counts)
            {
                List<int> actual = Drain(input.AsNLinq().Take<ArrayEnumerator<int>, int>(count));
                Assert.Equal(input.Take(count), actual);
            }
        }
    }

    [Fact]
    public void Take_NeverPullsPastItsCount()
    {
        var pulls = new PullCounter();
        List<int> taken = Drain(new CountingSource(10, pulls).Take<CountingSource, int>(3));

        Assert.Equal([0, 1, 2], taken);
        Assert.Equal(3, pulls.Count);
    }

    [Fact]
    public void Skip_MatchesSystemLinq()
    {
        foreach (int[] input in Inputs)
        {
            foreach (int count in Counts)
            {
                List<int> actual = Drain(input.AsNLinq().Skip<ArrayEnumerator<int>, int>(count));
                Assert.Equal(input.Skip(count), actual);
            }
        }
    }

    [Fact]
    public void Skip_DiscardsOnlyWhenPulled()
    {
        var pulls = new PullCounter();
        SkipEnumerator<CountingSource, int> skipped = new CountingSource(10, pulls).Skip<CountingSource, int>(4);

        Assert.Equal(0, pulls.Count);
        Assert.Equal(4, skipped.TryGetNext(out bool hasMore));
        Assert.True(hasMore);
        Assert.Equal(5, pulls.Count);
    }

    [Fact]
    public void TryTakeExactly_SucceedsOnlyWithEnoughElements()
    {
        foreach (int[] input in Inputs)
        {
            foreach (int count in Counts)
            {
                bool taken = input.AsNLinq().TryTakeExactly<ArrayEnumerator<int>, int>(count, out List<int> rows);
                Assert.Equal(input.Length >= count, taken);
                Assert.Equal(taken ? input.Take(count) : [], rows);
            }
        }
    }

    [Theory]
    [InlineData(2, 4, true)]    // positions 2..4 exist
    [InlineData(1, 8, true)]    // the whole input, exactly
    [InlineData(6, 9, false)]   // runs out while taking
    [InlineData(10, 12, false)] // runs out while skipping
    public void StrictWindow_FailsRatherThanTruncates(int first, int last, bool expected)
    {
        int[] input = [5, 4, 3, 2, 1, 0, 9, 8];
        int take = last - first + 1;

        bool succeeded = input.AsNLinq()
            .Skip<ArrayEnumerator<int>, int>(first - 1)
            .TryTakeExactly<SkipEnumerator<ArrayEnumerator<int>, int>, int>(take, out List<int> rows);

        Assert.Equal(expected, succeeded);
        Assert.Equal(expected ? input.Skip(first - 1).Take(take) : [], rows);
    }

    [Fact]
    public void TakeLast_MatchesSystemLinqInSourceOrder()
    {
        foreach (int[] input in Inputs)
        {
            foreach (int count in Counts)
            {
                List<int> actual = input.AsNLinq().TakeLast<ArrayEnumerator<int>, int>(count);
                Assert.Equal(input.TakeLast(count), actual);
            }
        }
    }

    [Fact]
    public void EveryOperator_ActsOnlyOnTheElementsThatRemain()
    {
        // The pinned NLinq's array fold restarts from index zero; the fixture's
        // operators must act only on what a partial pull left.
        int[] input = [1, 2, 3, 4, 5];
        IEnumerable<int> remaining = input.Skip(1);

        ArrayEnumerator<int> source = input.AsNLinq();
        source.TryGetNext(out _);
        Assert.Equal(remaining.TakeLast(5), source.TakeLast<ArrayEnumerator<int>, int>(5));

        source = input.AsNLinq();
        source.TryGetNext(out _);
        Assert.Equal(remaining.Take(2), Drain(source.Take<ArrayEnumerator<int>, int>(2)));

        source = input.AsNLinq();
        source.TryGetNext(out _);
        Assert.Equal(remaining.Skip(2), Drain(source.Skip<ArrayEnumerator<int>, int>(2)));

        source = input.AsNLinq();
        source.TryGetNext(out _);
        Assert.True(source.TryTakeExactly<ArrayEnumerator<int>, int>(4, out List<int> exact));
        Assert.Equal(remaining, exact);

        source = input.AsNLinq();
        source.TryGetNext(out _);
        Assert.False(source.TryTakeExactly<ArrayEnumerator<int>, int>(5, out _));
    }

    [Fact]
    public void NegativeCounts_AreRejected()
    {
        int[] input = [1, 2, 3];
        Assert.Throws<ArgumentOutOfRangeException>(() => { input.AsNLinq().Take<ArrayEnumerator<int>, int>(-1); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { input.AsNLinq().Skip<ArrayEnumerator<int>, int>(-1); });
        Assert.Throws<ArgumentOutOfRangeException>(() => input.AsNLinq().TakeLast<ArrayEnumerator<int>, int>(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => input.AsNLinq().TryTakeExactly<ArrayEnumerator<int>, int>(-1, out _));
    }

    static List<int> Drain<TEnum>(TEnum source)
        where TEnum : IEnumerator<TEnum, int>, allows ref struct
    {
        var items = new List<int>();
        while (true)
        {
            int item = source.TryGetNext(out bool hasMore);
            if (!hasMore)
                return items;
            items.Add(item);
        }
    }

    sealed class PullCounter
    {
        public int Count;
    }

    struct CountingSource(int length, PullCounter pulls) : IEnumerator<CountingSource, int>
    {
        int _next;

        public int TryGetNext(out bool hasMore)
        {
            pulls.Count++;
            hasMore = _next < length;
            return hasMore ? _next++ : default;
        }
    }

    readonly struct Parity : IFunc<int, int>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Invoke(int value) => value % 2;
    }

    readonly struct Identity : IFunc<int, int>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Invoke(int value) => value;
    }

    readonly struct AscendingInt32 : IComparer<int>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Compare(int x, int y) => x.CompareTo(y);
    }
}
