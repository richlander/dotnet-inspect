namespace DotnetInspector.Tests;

public partial class CommandExecutionTests
{
    [Fact]
    public async Task TypedDocumentJsonItemWindowsFailClosedUntilAdopted()
    {
        var find = await RunAppAsync(
            "find", "*",
            "--library", TestAssemblyPath,
            "-n", "1",
            "--json",
            "--tips", "q");
        var type = await RunAppAsync(
            "type",
            "--library", TestAssemblyPath,
            "-n", "1",
            "--json",
            "--tips", "q");
        var implements = await RunAppAsync(
            "implements", "System.IDisposable",
            "--library", TestAssemblyPath,
            "-n", "1",
            "--json",
            "--tips", "q");

        Assert.All(
            new[] { find, type, implements },
            result =>
            {
                Assert.Equal(1, result.Exit);
                Assert.Empty(result.Output);
                Assert.Contains(
                    "Document --json item windows are not yet supported",
                    result.Error,
                    StringComparison.Ordinal);
            });
    }

    [Fact]
    public async Task UnadoptedProjectionItemWindowsFailClosedTruthfully()
    {
        var result = await RunAppAsync(
            "library", TestAssemblyPath,
            "-S", "References",
            "--value",
            "-n", "1",
            "--tips", "q");

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "-n item windows are not yet supported with --value",
            result.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "use -n N",
            result.Error,
            StringComparison.Ordinal);
    }
}
