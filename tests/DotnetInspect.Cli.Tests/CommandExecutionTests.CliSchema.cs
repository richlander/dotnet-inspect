namespace DotnetInspect.Cli.Tests;

public partial class CommandExecutionTests
{
    [Fact]
    public async Task RootVerbosityMinimal_ListsCommandsWithoutChildren()
    {
        var (exit, output, error) = await RunAppAsync("-v:m");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.StartsWith(
            "# dotnet-inspect ",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "├─ cache  Manage the dotnet-inspect cache",
            output);
        Assert.DoesNotContain("│  ├─ --json", output);
    }

    [Fact]
    public async Task RootVerbosityNormal_ListsCommandChildrenWithoutDetails()
    {
        var (exit, output, error) = await RunAppAsync("-v:n");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("│  ├─ --json", output);
        Assert.DoesNotContain("--json  Output as JSON", output);
    }

    [Fact]
    public async Task RootVerbosityDetailed_ListsCommandChildDescriptions()
    {
        var (exit, output, error) = await RunAppAsync("-v:d");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("│  ├─ --json  Output as JSON", output);
    }
}
