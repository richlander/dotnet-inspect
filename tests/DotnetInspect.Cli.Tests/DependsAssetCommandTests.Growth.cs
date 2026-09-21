using DotnetInspect.Cli.Sections;

namespace DotnetInspect.Cli.Tests;

public partial class DependsAssetCommandTests
{
    [Fact]
    public async Task
        Dependencies_RealPackagePreservesMeasuredRowsAboveInformativeRange()
    {
        string packagePath = Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "SectionGrowth",
            "microsoft.aspnetcore.app.2.2.8.nupkg");

        (int exitCode, string output, string error) =
            await RunCapturedAsync(
            [
                "depends",
                "--package",
                packagePath,
                "-S",
                DependsAssetSections.Dependencies,
                "--count",
                "--tips",
                "q",
            ]);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Equal("150", output.Trim());
    }

    [Fact]
    public async Task
        DependencyGraph_PlatformTypePreservesMeasuredRowsAboveInformativeRange()
    {
        (int exitCode, string output, string error) =
            await RunCapturedAsync(
            [
                "depends",
                "System.Int128",
                "--platform",
                "-S",
                DependsTypeSections.DependencyGraph,
                "--count",
                "--tips",
                "q",
            ]);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Equal("32", output.Trim());
    }

    [Fact]
    public async Task NormalAssetMode_OmitsUnboundedInventories()
    {
        string packagePath = Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "SectionGrowth",
            "microsoft.aspnetcore.app.2.2.8.nupkg");

        (int exitCode, string output, string error) =
            await RunCapturedAsync(
            [
                "depends",
                "--package",
                packagePath,
                "-v:n",
                "--tips",
                "q",
            ]);

        Assert.Equal(0, exitCode);
        Assert.Empty(output);
        Assert.Empty(error);
    }
}
