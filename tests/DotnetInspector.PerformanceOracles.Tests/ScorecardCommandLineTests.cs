namespace DotnetInspector.PerformanceOracles.Tests;

public sealed class ScorecardCommandLineTests
{
    [Theory]
    [InlineData("check")]
    [InlineData("time")]
    [InlineData("check", "--rounds", "1")]
    [InlineData("time", "--rounds", "1", "--budget-ms", "10", "--tsv", "out.tsv")]
    public void ACommandWithNoAssemblies_IsRejected(params string[] args)
    {
        Assert.False(ScorecardCommandLine.TryParse(args, out ScorecardOptions? options, out string? error));
        Assert.Null(options);
        Assert.Contains("no assemblies", error);
        Assert.Contains(ScorecardCommandLine.Usage, error);
    }

    [Theory]
    [InlineData]
    [InlineData("measure", "a.dll")]
    [InlineData("time", "--rounds")]
    [InlineData("time", "--rounds", "0", "a.dll")]
    [InlineData("time", "--budget-ms", "soon", "a.dll")]
    public void MalformedCommandLines_AreRejected(params string[] args)
    {
        Assert.False(ScorecardCommandLine.TryParse(args, out ScorecardOptions? options, out string? error));
        Assert.Null(options);
        Assert.Contains(ScorecardCommandLine.Usage, error);
    }

    [Fact]
    public void ACompleteCommandLine_IsParsed()
    {
        Assert.True(ScorecardCommandLine.TryParse(
            ["time", "--rounds", "3", "--budget-ms", "500", "--tsv", "out.tsv", "a.dll", "b.dll"],
            out ScorecardOptions? options,
            out string? error));

        Assert.Null(error);
        Assert.Equal(ScorecardCommand.Time, options!.Command);
        Assert.Equal((3, 500), (options.Timing.Rounds, options.Timing.BudgetMilliseconds));
        Assert.Equal("out.tsv", options.TsvPath);
        Assert.Equal(["a.dll", "b.dll"], options.Assets);
    }
}
