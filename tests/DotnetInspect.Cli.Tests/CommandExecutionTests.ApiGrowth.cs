namespace DotnetInspect.Cli.Tests;

public partial class CommandExecutionTests
{
    [Theory]
    [InlineData("Classes")]
    [InlineData("Structs")]
    [InlineData("Interfaces")]
    [InlineData("Enums")]
    [InlineData("Delegates")]
    public async Task Type_PlatformSurfaceKindCountsExceedInformativeRange(
        string section)
    {
        var (exit, output, error) = await RunAppAsync(
            "type",
            "--platform",
            "System.Runtime",
            "-S",
            section,
            "--count",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.True(int.TryParse(output.Trim(), out int count));
        Assert.True(
            count > 24,
            $"Expected {section} to exceed the informative range; observed {count}.");
    }

    [Fact]
    public async Task Type_PlatformClassInventoryUsesPrimaryAndDetailedViews()
    {
        var (minimalExit, minimal, minimalError) = await RunAppAsync(
            "type",
            "--platform",
            "System.Runtime",
            "-v:m",
            "--format=markdown",
            "--tips",
            "q");
        var (normalExit, normal, normalError) = await RunAppAsync(
            "type",
            "--platform",
            "System.Runtime",
            "-v:n",
            "--format=markdown",
            "--tips",
            "q");
        var (detailedExit, detailed, detailedError) = await RunAppAsync(
            "type",
            "--platform",
            "System.Runtime",
            "-v:d",
            "--format=markdown",
            "--tips",
            "q");

        Assert.Equal(0, minimalExit);
        Assert.Equal(0, normalExit);
        Assert.Equal(0, detailedExit);
        Assert.Empty(minimalError);
        Assert.Empty(normalError);
        Assert.Empty(detailedError);
        Assert.Contains("Classes", SectionHeadings(minimal));
        Assert.DoesNotContain("Classes", SectionHeadings(normal));
        Assert.Contains("Classes", SectionHeadings(detailed));
    }
}
