using DotnetInspector.Output;

namespace DotnetInspector.Tests;

/// <summary>
/// <see cref="RowWindow.Resolve"/> is the one place a window's meaning is
/// interpreted; both row limiters call it rather than branching on the window's
/// shape. These tests assert the extents it produces, because a window that
/// resolves to the wrong rows still renders a perfectly well-formed table -- the
/// failure is silent everywhere except in the row numbers themselves.
/// </summary>
public class RowWindowResolutionTests
{
    [Theory]
    [InlineData(3, 10, 0, 3)]
    [InlineData(10, 3, 0, 3)]   // asking for more rows than exist keeps them all
    [InlineData(0, 10, 0, 0)]
    [InlineData(3, 0, 0, 0)]
    public void Head_KeepsTheLeadingRows(int count, int dataCount, int expectedStart, int expectedEnd)
        => Assert.Equal((expectedStart, expectedEnd), RowWindow.Head(count).Resolve(dataCount));

    [Theory]
    [InlineData(3, 10, 7, 10)]
    [InlineData(10, 3, 0, 3)]   // asking for more rows than exist keeps them all
    [InlineData(0, 10, 10, 10)]
    [InlineData(3, 0, 0, 0)]
    public void Tail_KeepsTheTrailingRows(int count, int dataCount, int expectedStart, int expectedEnd)
        => Assert.Equal((expectedStart, expectedEnd), RowWindow.Tail(count).Resolve(dataCount));

    [Fact]
    public void Range_IsInclusiveAtBothEnds_AndOneBased()
    {
        // Rows 2..4 are three rows, and row 2 is at index 1.
        Assert.Equal((1, 4), RowWindow.Range(2, 4).Resolve(10));
        Assert.Equal((0, 1), RowWindow.Range(1, 1).Resolve(10));
    }

    [Fact]
    public void Range_AndCount_DisagreeForTheSameDigits()
    {
        // The whole reason the grammar keeps 6 and 2..10 as different kinds: a
        // count anchors to an end, a range does not. Over the same table they
        // select different rows, so collapsing them would be a silent bug.
        Assert.NotEqual(RowWindow.Head(9).Resolve(20), RowWindow.Range(2, 10).Resolve(20));
    }

    [Fact]
    public void OpenRange_RunsToTheLastRow()
    {
        Assert.Equal((9, 20), RowWindow.Range(10, null).Resolve(20));
        Assert.Equal((0, 5), RowWindow.Range(1, null).Resolve(5));
    }

    [Fact]
    public void Range_ClampsToTheRowsThatExist_RatherThanOverrunning()
    {
        // A caller uses the extent directly, so an end past the table would index
        // out of bounds instead of simply selecting fewer rows.
        Assert.Equal((1, 3), RowWindow.Range(2, 100).Resolve(3));
    }

    [Fact]
    public void Range_StartingPastTheEnd_IsEmptyRatherThanInverted()
    {
        // keepStart must never exceed keepEnd: `Take(keepEnd - keepStart)` on an
        // inverted window throws, and a clamped-but-unordered pair would silently
        // select from the wrong place.
        var (start, end) = RowWindow.Range(50, 60).Resolve(3);
        Assert.True(start <= end, $"expected an ordered window, got ({start}, {end})");
        Assert.Equal(0, end - start);
    }

    [Fact]
    public void ANegativeCount_MeansNoLimit_ForEitherDirection()
    {
        Assert.True(RowWindow.Head(-1).IsUnlimited);
        Assert.True(RowWindow.Tail(-1).IsUnlimited);
        Assert.Equal((0, 7), RowWindow.Head(-1).Resolve(7));
        Assert.Equal((0, 7), RowWindow.Tail(-1).Resolve(7));
    }

    [Fact]
    public void ARangeIsNeverUnlimited()
    {
        // Range has no negative-count spelling, so nothing should route it into the
        // "skip windowing entirely" fast path that IsUnlimited guards.
        Assert.False(RowWindow.Range(1, null).IsUnlimited);
        Assert.False(RowWindow.Range(1, 1).IsUnlimited);
    }

    [Fact]
    public void Range_RefusesAStartBeforeTheFirstRow()
    {
        // Row numbers are 1-based; a 0 start would silently shift every row by one.
        Assert.Throws<ArgumentOutOfRangeException>(() => RowWindow.Range(0, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => RowWindow.Range(-1, 5));
    }

    [Fact]
    public void Range_RefusesAnEndBeforeItsStart()
        => Assert.Throws<ArgumentOutOfRangeException>(() => RowWindow.Range(10, 2));

    [Fact]
    public void EveryWindowResolvesToAnOrderedRangeWithinTheTable()
    {
        // The contract callers rely on: 0 <= keepStart <= keepEnd <= dataCount, so
        // no caller needs to re-clamp and none can index outside the table.
        RowWindow[] windows =
        [
            RowWindow.Head(0), RowWindow.Head(3), RowWindow.Head(999), RowWindow.Head(-1),
            RowWindow.Tail(0), RowWindow.Tail(3), RowWindow.Tail(999), RowWindow.Tail(-1),
            RowWindow.Range(1, null), RowWindow.Range(3, 5), RowWindow.Range(99, null), RowWindow.Range(99, 200),
        ];

        foreach (var window in windows)
        {
            foreach (var dataCount in (int[])[0, 1, 4, 50])
            {
                var (start, end) = window.Resolve(dataCount);
                Assert.True(start >= 0 && start <= end && end <= dataCount,
                    $"{window.Kind} window over {dataCount} rows resolved to ({start}, {end})");
            }
        }
    }
}
