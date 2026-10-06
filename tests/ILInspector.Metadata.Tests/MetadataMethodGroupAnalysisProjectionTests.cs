using DotnetInspector.PerformanceOracles;

namespace ILInspector.Metadata.Tests;

public sealed class MetadataMethodGroupAnalysisProjectionTests
{
    public static IEnumerable<object[]> ScenarioRows() =>
        from scenario in MemberGroupPopulation.Scenarios
        from terminal in new[] { "Count", "Rows" }
        select new object[] { scenario, terminal };

    public static IEnumerable<object[]> ExactScenarioRows() =>
        from scenario in MemberGroupPopulation.ExactScenarios
        select new object[] { scenario };

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

    [Theory]
    [MemberData(nameof(ExactScenarioRows))]
    public void LinqNlinqAndPlannerSelectTheSameExactMember(
        MemberExactScorecardScenario scenario)
    {
        string path = typeof(System.Text.Json.JsonSerializer)
            .Assembly.Location;

        MemberExactProjectionAnswer oracle =
            MemberGroupPopulation.ExecuteExact(
                path,
                scenario,
                "NLinq");
        MemberExactProjectionAnswer linq =
            MemberGroupPopulation.ExecuteExact(
                path,
                scenario,
                "LINQ");
        MemberExactProjectionAnswer planner =
            MemberGroupPopulation.ExecuteExact(
                path,
                scenario,
                "Planner");

        Assert.Equal(scenario.ExpectedCount, oracle.PopulationCount);
        Assert.Equal(scenario.TargetOrdinal, oracle.BaselineOrdinal);
        Assert.Equal(oracle, linq);
        Assert.Equal(oracle, planner);
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

    [Fact]
    public void ScorecardReportShowsEveryComparatorAllocation()
    {
        MemberGroupScorecardCell[] cells =
            [..
                from scenario in MemberGroupPopulation.Scenarios
                from terminal in new[] { "Count", "Rows" }
                from phase in new[] { "Kernel", "Composed" }
                from column in new[]
                {
                    (Name: "LINQ", Time: 4d, Allocation: 360L),
                    (Name: "NLinq", Time: 2d, Allocation: 240L),
                    (Name: "Planner", Time: 1d, Allocation: 112L),
                }
                select new MemberGroupScorecardCell(
                    scenario.Name,
                    terminal,
                    phase,
                    column.Name,
                    Microseconds:
                        column.Time
                        * (terminal == "Rows" ? 3 : 1),
                    column.Allocation
                        * (terminal == "Rows" ? 3 : 1))];
        var result = new MemberGroupScorecardResult(
            new(
                Compared: 0,
                Mismatches: [],
                AnswerHashes: []),
            cells);

        string report = MemberGroupPopulation.Report(result);

        Assert.Contains(
            "| LINQ alloc | NLinq alloc | Planner alloc |",
            report,
            StringComparison.Ordinal);
        Assert.Contains(
            "| 360 B | 240 B | 112 B |",
            report,
            StringComparison.Ordinal);
        Assert.Contains(
            "| 4.00x (4.000 us) | 2.00x (2.000 us) | "
                + "1.00x (1.000 us) |",
            report,
            StringComparison.Ordinal);
        Assert.Contains(
            "| Planner | Kernel | Time | 1.00x | 3.00x |",
            report,
            StringComparison.Ordinal);
        Assert.Contains(
            "| Planner | Kernel | Allocation | 1.00x | 3.00x |",
            report,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ScorecardReportOmitsZeroAllocationCountBaseline()
    {
        MemberGroupScorecardCell[] cells =
        [
            .. from scenario in MemberGroupPopulation.Scenarios
               from terminal in new[] { "Count", "Rows" }
               from phase in new[] { "Kernel", "Composed" }
               from column in new[] { "LINQ", "NLinq", "Planner" }
               select new MemberGroupScorecardCell(
                   scenario.Name,
                   terminal,
                   phase,
                   column,
                   Microseconds: 1,
                   AllocatedBytes: 0),
        ];
        var result = new MemberGroupScorecardResult(
            new(
                Compared: 0,
                Mismatches: [],
                AnswerHashes: []),
            cells);

        string report = MemberGroupPopulation.Report(result);

        Assert.Contains(
            "| Planner | Kernel | Allocation | - | - |",
            report,
            StringComparison.Ordinal);
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
