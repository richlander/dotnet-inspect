namespace DotnetInspect.Cli.Tests;

public partial class CommandExecutionTests
{
    private const string TfmAllMissingPackage = "missing-tfm-all-package.nupkg";

    // --tfm all is package discovery (docs/design/package-tfm-all-discovery.md):
    // every other command and target fails before acquisition, so a missing
    // package or file is never opened and the admission error is the only one.
    [Theory]
    [InlineData("library", "Lib.dll", "--package", TfmAllMissingPackage)]
    [InlineData("library", "--package", TfmAllMissingPackage)]
    [InlineData("type", "Contoso.Widget", "--package", TfmAllMissingPackage)]
    [InlineData("member", "Contoso.Widget.Run", "--package", TfmAllMissingPackage)]
    [InlineData("find", "Widget", "--package", TfmAllMissingPackage)]
    [InlineData("extensions", "Contoso.Widget", "--package", TfmAllMissingPackage)]
    [InlineData("depends", "--package", TfmAllMissingPackage)]
    public async Task TfmAll_PackageTargetOutsidePackageNamesTheDiscoveryRoute(
        params string[] target)
    {
        var (exit, output, error) = await RunAppAsync([.. target, "--tfm", "all"]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Equal(
            "Error: --tfm all applies only to 'package' discovery. "
                + $"Use 'dotnet-inspect package {TfmAllMissingPackage} --tfm all'.",
            error.Trim());
    }

    [Theory]
    [InlineData("library", "--platform", "System.Text.Json")]
    [InlineData("library", "missing-tfm-all-library.dll")]
    [InlineData("type", "System.Text.Json.JsonElement", "--platform", "System.Text.Json")]
    [InlineData("find", "--packages", "Contoso")]
    [InlineData("diff", "--package", "Contoso@1.0.0..2.0.0")]
    [InlineData("find", "Widget", "--package", "first.nupkg", "--package", "second.nupkg")]
    public async Task TfmAll_NonPackageRouteFailsBeforeAcquisition(
        params string[] target)
    {
        var (exit, output, error) = await RunAppAsync([.. target, "--tfm", "ALL"]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Equal(
            "Error: --tfm all applies only to 'package' discovery.",
            error.Trim());
    }

    [Fact]
    public async Task TfmAll_PackageGrainSelectorOrderingFailsBeforeAcquisition()
    {
        var (exit, output, error) = await RunAppAsync(
            "find", "--tfm", "all", "--packages", "Contoso",
            "--where", "library-literal=Widget");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Equal(
            "Error: --tfm all applies only to 'package' discovery.",
            error.Trim());
    }

    [Theory]
    [InlineData("--library")]
    [InlineData("--library", "Lib.dll")]
    public async Task TfmAll_PackageLibraryInspectionFailsBeforeAcquisition(
        params string[] library)
    {
        var (exit, output, error) = await RunAppAsync(
            ["package", TfmAllMissingPackage, .. library, "--tfm", "all"]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Equal(
            "Error: --library cannot be combined with --tfm all, which is package discovery.",
            error.Trim());
    }
}
