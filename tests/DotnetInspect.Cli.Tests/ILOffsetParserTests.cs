using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Models;

namespace DotnetInspect.Cli.Tests;

/// <summary>
/// Tests for the IL offset token+offset parser used by the library Context: Source Location section.
/// </summary>
public class ILOffsetParserTests
{
    [Theory]
    [InlineData("0x6000001+0x5", 0x6000001, 0x5)]
    [InlineData("0x06000001+0x0005", 0x06000001, 0x0005)]
    [InlineData("0x6000004+0x15", 0x6000004, 0x15)]
    [InlineData("0x6000002+0x0", 0x6000002, 0x0)]
    public void ValidTokenAndOffset_ParsesCorrectly(string input, int expectedToken, int expectedOffset)
    {
        Assert.True(ILOffsetQuery.TryParse(input, out var token, out var offset));
        Assert.Equal(expectedToken, token);
        Assert.Equal(expectedOffset, offset);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0x6000001")]           // missing +offset
    [InlineData("0x6000001-0x5")]       // wrong separator
    [InlineData("0x6000001:0x5")]       // wrong separator
    [InlineData("0x2000001+0x5")]       // TypeDef table (0x02), not MethodDef (0x06)
    [InlineData("0x0A000001+0x5")]      // MemberRef table (0x0A), not MethodDef
    [InlineData("garbage+0x5")]         // non-numeric token
    [InlineData("0x6000001+garbage")]   // non-numeric offset
    public void InvalidInput_ReturnsFalse(string input)
    {
        Assert.False(ILOffsetQuery.TryParse(input, out _, out _));
    }

    [Fact]
    public void ZeroILOffset_IsValid()
    {
        Assert.True(ILOffsetQuery.TryParse("0x6000001+0x0", out var token, out var offset));
        Assert.Equal(0x6000001, token);
        Assert.Equal(0, offset);
    }

    [Fact]
    public void CoordinatePopulation_PreservesSignificantRecordOrder()
    {
        ILCoordinatePopulationOutcome outcome =
            ILOffsetQuery.ParsePopulation(
                [
                    "# comment",
                    "first 0x06000001+0x1",
                    "malformed record",
                    "",
                    "0x06000002+0x0 last",
                ],
                "coordinates.txt");

        ILCoordinatePopulation population =
            Assert.IsType<ILCoordinatePopulation>(outcome.Population);
        Assert.Collection(
            population.Records,
            record =>
            {
                var coordinate =
                    Assert.IsType<ILCoordinatePopulationRecord.Coordinate>(
                        record);
                Assert.Equal(2, coordinate.LineNumber);
                Assert.Equal("0x06000001+0x1", coordinate.Value);
                Assert.Equal("first", coordinate.Label);
            },
            record =>
            {
                var malformed =
                    Assert.IsType<ILCoordinatePopulationRecord.Malformed>(
                        record);
                Assert.Equal(3, malformed.LineNumber);
                Assert.Equal("coordinates.txt:3", malformed.Label);
            },
            record =>
            {
                var coordinate =
                    Assert.IsType<ILCoordinatePopulationRecord.Coordinate>(
                        record);
                Assert.Equal(5, coordinate.LineNumber);
                Assert.Equal("0x06000002+0x0", coordinate.Value);
                Assert.Equal("last", coordinate.Label);
            });
    }

    [Fact]
    public void CoordinatePopulation_AcceptsMaximumMixedPopulation()
    {
        IEnumerable<string> records = Enumerable
            .Range(1, ILOffsetQuery.MaximumCoordinatePopulation)
            .Select(
                index =>
                    index % 2 == 0
                        ? $"sample-{index} 0x06000001+0x0"
                        : $"malformed-{index}");

        ILCoordinatePopulationOutcome outcome =
            ILOffsetQuery.ParsePopulation(
                ["", "# comment", .. records],
                "coordinates.txt");

        Assert.True(outcome.Succeeded);
        Assert.Equal(
            ILOffsetQuery.MaximumCoordinatePopulation,
            outcome.Population!.Records.Count);
    }

    [Fact]
    public void CoordinatePopulation_RejectsNextSignificantRecordWithTypedFailure()
    {
        IEnumerable<string> records = Enumerable
            .Range(1, ILOffsetQuery.MaximumCoordinatePopulation + 1)
            .Select(index => $"record-{index}");

        ILCoordinatePopulationOutcome outcome =
            ILOffsetQuery.ParsePopulation(
                ["", "# comment", .. records],
                "coordinates.txt");

        ILCoordinatePopulationFailure failure =
            Assert.IsType<ILCoordinatePopulationFailure>(outcome.Failure);
        Assert.Equal(
            ILCoordinatePopulationFailureKind
                .CoordinatePopulationLimitExceeded,
            failure.Kind);
        Assert.Equal(
            ILOffsetQuery.MaximumCoordinatePopulation,
            failure.Limit);
        Assert.Equal(ILOffsetQuery.MaximumCoordinatePopulation + 3, failure.ObservedLineNumber);
        Assert.Null(outcome.Population);
    }
}
