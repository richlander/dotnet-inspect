using DotnetInspector.PerformanceOracles;

namespace ILInspector.Metadata.Tests;

public sealed class MetadataMethodGroupAnalysisProjectionTests
{
    public static IEnumerable<object[]> ScenarioRows() =>
        from scenario in MemberGroupPopulation.Scenarios
        from terminal in new[] { "Count", "Rows" }
        select new object[] { scenario, terminal };

    [Theory]
    [MemberData(nameof(ScenarioRows))]
    public void LinqNlinqAndPlannerProjectTheSameModel(
        MemberGroupScorecardScenario scenario,
        string terminal)
    {
        string path = typeof(System.Text.Json.JsonSerializer)
            .Assembly.Location;
        bool rows = terminal == "Rows";

        MemberGroupProjectionAnswer oracle =
            MemberGroupPopulation.Execute(
                path,
                scenario,
                "NLinq",
                rows);
        MemberGroupProjectionAnswer linq =
            MemberGroupPopulation.Execute(
                path,
                scenario,
                "LINQ",
                rows);
        MemberGroupProjectionAnswer planner =
            MemberGroupPopulation.Execute(
                path,
                scenario,
                "Planner",
                rows);

        Assert.Equal(scenario.ExpectedCount, oracle.Count);
        AssertAnswer(oracle, linq);
        AssertAnswer(oracle, planner);
    }

    [Fact]
    public void SingleSubjectPreparationDoesNotBuildWholeTypeIndex()
    {
        string path = typeof(System.Text.Json.JsonSerializer)
            .Assembly.Location;
        MemberGroupScorecardScenario scenario =
            MemberGroupPopulation.Scenarios[0];

        long allocated =
            MemberGroupPopulation.PreparationAllocatedBytes(
                path,
                scenario);

        Assert.True(
            allocated < 4 * 1024,
            $"Single-subject preparation allocated "
                + $"{allocated:N0} bytes.");
    }

    private static void AssertAnswer(
        MemberGroupProjectionAnswer expected,
        MemberGroupProjectionAnswer actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        Assert.Equal(expected.Rows, actual.Rows);
        Assert.Equal(expected.NextOrdinal, actual.NextOrdinal);
        Assert.Equal(
            expected.ContinuationOutOfRange,
            actual.ContinuationOutOfRange);
        Assert.Equal(
            expected.IncompleteRetainedTextCharacters,
            actual.IncompleteRetainedTextCharacters);
        Assert.Equal(expected.RowsFailed, actual.RowsFailed);
    }
}
