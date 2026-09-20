using System.Globalization;
using DotnetInspect.Cli.CommandLine;
using DotnetInspector.Packages;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class PackageSectionGrowthTests
{
    public PackageSectionGrowthTests()
    {
        NuGetCache.Initialize("dotnet-inspect");
    }

    [Theory]
    [InlineData(
        "system.valuetuple.4.5.0.nupkg",
        "Target Frameworks",
        13)]
    [InlineData(
        "microsoft.aspnetcore.app.2.2.8.nupkg",
        "Dependencies",
        150)]
    [InlineData(
        "dotnet-outdated-tool.4.8.1.nupkg",
        "Runtime Dependencies",
        44)]
    [InlineData(
        "crestapps.agentskills.mcp.orchardcore.1.2.0.nupkg",
        "Package skill files",
        172)]
    public async Task PackageBaseInventory_RealPackagePreservesMeasuredRows(
        string archive,
        string section,
        int expectedCount)
    {
        string packagePath = Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "SectionGrowth",
            archive);

        var result = await Run(
            "package",
            packagePath,
            "-S",
            section,
            "--count",
            "--tips",
            "q");

        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal(
            expectedCount.ToString(CultureInfo.InvariantCulture),
            result.Output.Trim());
    }

    static Task<(int ExitCode, string Output, string Error)> Run(
        params string[] args) =>
        ConsoleCapture.RunAsync(() =>
        {
            var root = CommandLineBuilder.CreateRootCommand();
            string[] processed =
                CommandLineBuilder.PreprocessArgs(args, root);
            return CommandLineBuilder.InvokeAsync(
                root.Parse(processed),
                processed);
        });
}
