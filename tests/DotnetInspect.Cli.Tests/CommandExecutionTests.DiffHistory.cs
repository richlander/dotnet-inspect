using DotnetInspect.Cli.Sections;

namespace DotnetInspect.Cli.Tests;

public partial class CommandExecutionTests
{
    [Fact]
    public void DiffHistory_DeclaresSectionsAndHistoryCategory()
    {
        Assert.Equal(
            [
                DiffHistorySections.Outcome,
                DiffHistorySections.ProbeTrace,
                DiffHistorySections.Evaluations,
                DiffHistorySections.Transitions,
                DiffHistorySections.ChangedVersions,
            ],
            DiffHistorySections.Catalog.SelectableSectionNames);
        Assert.Equal(
            [
                DiffHistorySections.Outcome,
                DiffHistorySections.ProbeTrace,
            ],
            DiffHistorySections.Catalog.SelectionCategoryMap[
                DiffHistorySections.HistoryCategory]);
    }

    [Fact]
    public async Task TimelineCommand_IsRemovedFromRoot()
    {
        var (exit, output, error) = await RunAppAsync(
            "timeline",
            "--help");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "Unrecognized command or argument 'timeline'",
            error);
        Assert.DoesNotContain("Package 'timeline' not found", error);
    }

    [Fact]
    public async Task DiffHistory_AnalysisRequiresMemberBeforeAcquisition()
    {
        var (exit, output, error) = await RunAppAsync(
            "diff",
            "--history",
            "--package",
            "Definitely.Does.Not.Exist@1.0.0..2.0.0",
            "--type",
            "Example.Widget",
            "--finding",
            "analysis.unsafety");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "--finding analysis.unsafety requires exactly one --member target",
            error);
        Assert.DoesNotContain("Package 'Definitely.Does.Not.Exist'", error);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("0")]
    [InlineData("101")]
    public async Task
        DiffHistory_RejectsInvalidSurveyPercentageBeforeAcquisition(
            string percentage)
    {
        var (exit, output, error) = await RunAppAsync(
            "diff",
            "--history",
            "--package",
            "Definitely.Does.Not.Exist@1.0.0..2.0.0",
            "--type",
            "Example.Widget",
            "--sample-percent",
            percentage);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "--sample-percent must be from 1 through 100",
            error);
        Assert.DoesNotContain("Package 'Definitely.Does.Not.Exist'", error);
    }

    [Fact]
    public async Task
        DiffHistory_RejectsExplicitAndSurveyPoliciesBeforeAcquisition()
    {
        var (exit, output, error) = await RunAppAsync(
            "diff",
            "--history",
            "--package",
            "Definitely.Does.Not.Exist@1.0.0..2.0.0",
            "--type",
            "Example.Widget",
            "--at",
            "first",
            "--sample-percent",
            "50");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "--at cannot be combined with --max-probes, --sample-percent, or --major-versions",
            error);
        Assert.DoesNotContain("Package 'Definitely.Does.Not.Exist'", error);
    }

    [Fact]
    public async Task DiffSurveyPolicyRequiresHistory()
    {
        var (exit, output, error) = await RunAppAsync(
            "diff",
            "--package",
            "Definitely.Does.Not.Exist@1.0.0..2.0.0",
            "--sample-percent",
            "50");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("--sample-percent", error);
        Assert.Contains("require --history", error);
        Assert.DoesNotContain("Package 'Definitely.Does.Not.Exist'", error);
    }

    [Fact]
    public async Task DiffMajorVersionsPolicyRequiresHistory()
    {
        var (exit, output, error) = await RunAppAsync(
            "diff",
            "--package",
            "Definitely.Does.Not.Exist@8.0.0..11.0.0",
            "--major-versions");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("--major-versions", error);
        Assert.Contains("require --history", error);
        Assert.DoesNotContain("Package 'Definitely.Does.Not.Exist'", error);
    }

    [Theory]
    [InlineData("--at", "first")]
    [InlineData("--sample-percent", "50")]
    [InlineData("--max-probes", "4")]
    public async Task
        DiffHistory_MajorVersionsRejectsAnotherEvaluationPolicy(
            string option,
            string value)
    {
        var (exit, output, error) = await RunAppAsync(
            "diff",
            "--history",
            "--package",
            "Definitely.Does.Not.Exist@8.0.0..11.0.0",
            "--type",
            "Example.Widget",
            "--major-versions",
            option,
            value);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("--major-versions", error);
        Assert.DoesNotContain("Package 'Definitely.Does.Not.Exist'", error);
    }

    [Fact]
    public async Task DiffHistory_CountAdmitsOnlyChangedVersions()
    {
        var (exit, output, error) = await RunAppAsync(
            "diff",
            "--history",
            "--package",
            "Definitely.Does.Not.Exist@1.0.0..2.0.0",
            "--type",
            "Example.Widget",
            "--count",
            "-S",
            DiffHistorySections.Evaluations);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "Count admits only the Changed Versions section",
            error);
        Assert.DoesNotContain("Package 'Definitely.Does.Not.Exist'", error);
    }

    [Fact]
    public async Task DiffHistory_EnvelopeRejectsProjectionBeforeAcquisition()
    {
        var (exit, output, error) = await RunAppAsync(
            "diff",
            "--history",
            "--package",
            "Definitely.Does.Not.Exist@1.0.0..2.0.0",
            "--type",
            "Example.Widget",
            "--envelope",
            "-S",
            DiffHistorySections.Outcome);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("--envelope cannot be combined with -S", error);
        Assert.DoesNotContain("Package 'Definitely.Does.Not.Exist'", error);
    }

    [Fact]
    public async Task DiffHistory_EnvelopeRejectsSemanticRowsWithoutCountBeforeAcquisition()
    {
        var (exit, output, error) = await RunAppAsync(
            "diff",
            "--history",
            "--package",
            "Definitely.Does.Not.Exist@1.0.0..2.0.0",
            "--type",
            "Example.Widget",
            "--envelope",
            "--rows",
            "1..1");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("--envelope cannot be combined with --rows", error);
        Assert.DoesNotContain("Package 'Definitely.Does.Not.Exist'", error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DiffHistory_RejectsTreeBeforeAcquisition(bool json)
    {
        List<string> arguments =
        [
            "diff",
            "--history",
            "--package",
            "Definitely.Does.Not.Exist@1.0.0..2.0.0",
            "--type",
            "Example.Widget",
            "--tree",
        ];
        if (json)
            arguments.Add("--json");

        var (exit, output, error) = await RunAppAsync([.. arguments]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "Diff History does not support --tree",
            error);
        Assert.DoesNotContain("Package 'Definitely.Does.Not.Exist'", error);
    }

    [Fact]
    public async Task DiffHistory_CompactRejectsScalarCountBeforeAcquisition()
    {
        var (exit, output, error) = await RunAppAsync(
            "diff",
            "--history",
            "--package",
            "Definitely.Does.Not.Exist@1.0.0..2.0.0",
            "--type",
            "Example.Widget",
            "--count",
            "--json",
            "--compact");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "--compact is supported only for complete Diff History JSON or envelope output",
            error);
        Assert.DoesNotContain("Package 'Definitely.Does.Not.Exist'", error);
    }

    [Fact]
    public async Task DiffHistory_RejectsNonReplayableSourceBeforeAcquisition()
    {
        const string Secret = "do-not-print";
        var (exit, output, error) = await RunAppAsync(
            "diff",
            "--history",
            "--package",
            "Definitely.Does.Not.Exist@1.0.0..2.0.0",
            "--type",
            "Example.Widget",
            "--finding",
            "api.type",
            "--source",
            $"https://feed.invalid/index.json?token={Secret}");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "diff --history cannot disclose a replayable package command",
            error);
        Assert.DoesNotContain(Secret, error);
        Assert.DoesNotContain("Package 'Definitely.Does.Not.Exist'", error);
    }
}
