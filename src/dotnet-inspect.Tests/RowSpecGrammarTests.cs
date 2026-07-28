using DotnetInspector.Output;

namespace DotnetInspector.Tests;

/// <summary>
/// Pins the <c>--rows</c> grammar.
///
/// The central claim is that identical digits mean different things in different
/// forms -- <c>2..10</c> is nine rows and <c>2+10</c> is ten -- so the tests
/// assert the resolved extent rather than just that parsing succeeded. A parser
/// that accepted every form but conflated two of them would satisfy a
/// "parses without error" suite while still selecting the wrong rows, which is
/// the failure mode this flag exists to remove.
/// </summary>
public class RowSpecGrammarTests
{
    [Fact]
    public void BareNumber_IsACount_NotARange()
    {
        Assert.True(RowSpec.TryParse("6", out var spec, out var error));
        Assert.Null(error);

        Assert.Equal(RowSpecKind.Count, spec.Kind);
        Assert.Equal(6, spec.Count);
        Assert.False(spec.IsRange);

        // A count has no absolute extent until a direction and a section supply
        // one, so it deliberately does not answer Contains.
        Assert.False(spec.Contains(1));
        Assert.Null(spec.RowCount);
    }

    [Fact]
    public void DotDotRange_IsInclusive_AtBothEnds()
    {
        Assert.True(RowSpec.TryParse("2..10", out var spec, out var error));
        Assert.Null(error);

        Assert.Equal(RowSpecKind.Range, spec.Kind);
        Assert.Equal(2, spec.Start);
        Assert.Equal(10, spec.End);

        // Inclusive at both ends: 2 through 10 is nine rows, not eight.
        Assert.Equal(9, spec.RowCount);
        Assert.True(spec.Contains(2));
        Assert.True(spec.Contains(10));
        Assert.False(spec.Contains(1));
        Assert.False(spec.Contains(11));
    }

    [Fact]
    public void PlusRange_IsStartPlusCount()
    {
        Assert.True(RowSpec.TryParse("2+10", out var spec, out var error));
        Assert.Null(error);

        Assert.Equal(RowSpecKind.Range, spec.Kind);
        Assert.Equal(2, spec.Start);

        // Ten rows starting at 2 ends at 11, not 12.
        Assert.Equal(11, spec.End);
        Assert.Equal(10, spec.RowCount);
        Assert.True(spec.Contains(11));
        Assert.False(spec.Contains(12));
    }

    [Fact]
    public void SameDigits_SelectDifferentRows_AcrossTheTwoRangeForms()
    {
        Assert.True(RowSpec.TryParse("2..10", out var inclusive, out _));
        Assert.True(RowSpec.TryParse("2+10", out var startPlusCount, out _));

        // The whole reason both forms exist: the digits are read differently, so
        // conflating them silently shifts the selection by one row at the end.
        Assert.Equal(9, inclusive.RowCount);
        Assert.Equal(10, startPlusCount.RowCount);
        Assert.NotEqual(inclusive.End, startPlusCount.End);
    }

    [Fact]
    public void OpenRange_RunsToTheEndOfTheSection()
    {
        Assert.True(RowSpec.TryParse("10..", out var spec, out var error));
        Assert.Null(error);

        Assert.Equal(RowSpecKind.Range, spec.Kind);
        Assert.Equal(10, spec.Start);
        Assert.Null(spec.End);
        Assert.True(spec.IsOpenEnded);

        // The extent depends on the section, so the count is unknown here, but
        // membership above the start is still decidable.
        Assert.Null(spec.RowCount);
        Assert.True(spec.Contains(10));
        Assert.True(spec.Contains(40_000));
        Assert.False(spec.Contains(9));
    }

    [Fact]
    public void SingleRowRange_IsOneRow()
    {
        Assert.True(RowSpec.TryParse("7..7", out var spec, out _));
        Assert.Equal(1, spec.RowCount);
        Assert.True(spec.Contains(7));
        Assert.False(spec.Contains(8));
    }

    [Fact]
    public void ColonForm_IsRejected_WithAnExplanationRatherThanAParseError()
    {
        Assert.False(RowSpec.TryParse("2:10", out _, out var error));

        // A colon form would be read as a Python slice (0-based, end-exclusive),
        // so for the same digits it names a different set of rows. Rejecting it
        // generically would leave the user to rediscover that difference.
        Assert.NotNull(error);
        Assert.Contains("':'", error);
        Assert.Contains("2..10", error);
        Assert.Contains("2+10", error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void MissingValue_IsRejected(string? token)
    {
        Assert.False(RowSpec.TryParse(token, out _, out var error));
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("0")]           // rows are 1-based
    [InlineData("-1")]          // a leading sign is not part of the grammar
    [InlineData("0..5")]
    [InlineData("2..0")]
    [InlineData("0+3")]
    [InlineData("2+0")]
    [InlineData("abc")]
    [InlineData("2..abc")]
    [InlineData("abc..2")]
    [InlineData("..5")]         // an open range still needs a start
    [InlineData("2..10..3")]    // one range operator only
    [InlineData("2+10+3")]
    [InlineData("2..-1")]
    [InlineData("+5")]
    [InlineData("2 .. 10")]     // internal spaces are not part of the grammar
    [InlineData("1e3")]
    [InlineData("99999999999999999999")]
    public void MalformedTokens_AreRejected(string token)
    {
        Assert.False(RowSpec.TryParse(token, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void DescendingRange_IsRejected_AndSuggestsTheAscendingForm()
    {
        Assert.False(RowSpec.TryParse("10..2", out _, out var error));

        Assert.NotNull(error);
        Assert.Contains("2..10", error);
    }

    [Fact]
    public void StartPlusCount_PastTheLargestRow_IsReportedRatherThanWrapped()
    {
        // start + count - 1 overflows int here; computing it in int would wrap to
        // a negative end and produce a range that silently selects nothing.
        Assert.False(RowSpec.TryParse($"{int.MaxValue}+2", out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void SurroundingWhitespace_IsTolerated()
    {
        Assert.True(RowSpec.TryParse("  2..10  ", out var spec, out _));
        Assert.Equal(2, spec.Start);
        Assert.Equal(10, spec.End);
    }

    [Theory]
    [InlineData("6", "6")]
    [InlineData("2..10", "2..10")]
    [InlineData("10..", "10..")]
    [InlineData("2+10", "2..11")]   // normalized to the inclusive spelling
    public void ToString_RoundTripsThroughTheGrammar(string token, string expected)
    {
        Assert.True(RowSpec.TryParse(token, out var spec, out _));
        Assert.Equal(expected, spec.ToString());

        // Whatever ToString produces must itself parse, and to the same spec.
        Assert.True(RowSpec.TryParse(spec.ToString(), out var reparsed, out _));
        Assert.Equal(spec, reparsed);
    }

    [Fact]
    public void FactoryMethods_RejectOutOfRangeInput()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RowSpec.FromCount(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => RowSpec.FromRange(0, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => RowSpec.FromRange(5, 4));
    }
}
