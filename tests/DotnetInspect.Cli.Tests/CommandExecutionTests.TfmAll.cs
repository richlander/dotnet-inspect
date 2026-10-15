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
    [InlineData("package", "query", "Contoso")]
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

    // `package query` inherits the parent's --tfm, so the spelling before the
    // subcommand must fail the same way.
    [Fact]
    public async Task TfmAll_InheritedByPackageQueryFailsBeforeAcquisition()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "--tfm", "all", "query", "Contoso",
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

    // Outside the Library routes, package --tfm all admits only the Files
    // sections, so every other request fails before the package is opened.
    [Theory]
    [InlineData]
    [InlineData("-S", "Package Info")]
    [InlineData("-S", "Dependencies")]
    [InlineData("-S", "Dependency Hierarchy")]
    [InlineData("-S", "Target Frameworks")]
    [InlineData("--files", "-S", "Package Info")]
    [InlineData("--files", "--versions")]
    public async Task TfmAll_PackageRequestOtherThanFilesFailsBeforeAcquisition(
        params string[] request)
    {
        var (exit, output, error) = await RunAppAsync(
            ["package", TfmAllMissingPackage, .. request, "--tfm", "all"]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Equal(
            "Error: --tfm all applies only to -S Files (or --files).",
            error.Trim());
    }

    [Fact]
    public async Task TfmAll_PackageFilesListsEveryFramework()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.Package.TfmAllFiles", "README.md", "readme",
            extraFiles:
            [
                ("lib/net8.0/A.dll", "a"),
                ("lib/net6.0/A.dll", "old"),
            ]);
        try
        {
            var section = await RunAppAsync(
                "package", packagePath, "-S", "Files", "--tfm", "all", "--paths");
            var shortcut = await RunAppAsync(
                "package", packagePath, "--files", "--tfm", "all", "--paths");
            var path = await RunAppAsync(
                "package", packagePath, "--path", "lib/**", "--tfm", "all", "--paths");

            Assert.Equal(0, section.Exit);
            Assert.Empty(section.Error);
            Assert.Contains("lib/net8.0/A.dll", section.Output);
            Assert.Contains("lib/net6.0/A.dll", section.Output);
            Assert.Contains("README.md", section.Output);
            Assert.Equal(section.Output, shortcut.Output);
            Assert.Equal(0, path.Exit);
            Assert.Contains("lib/net6.0/A.dll", path.Output);
            Assert.DoesNotContain("README.md", path.Output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
