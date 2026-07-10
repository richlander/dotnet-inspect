using DotnetInspector.Core;
using DotnetInspector.Packages;

namespace DotnetInspector.Tests;

/// <summary>
/// Tests for package subcommand --version, --latest-version, and --versions behavior.
/// Mirrors RouterVersionTests to validate parity between router and package paths.
/// </summary>
[Collection("Console")]
public class PackageVersionTests
{
    public PackageVersionTests()
    {
        NuGetCache.Initialize("dotnet-inspect");
    }

    [Fact]
    public async Task Version_Bare_WithCachedPackage_ReturnsCachedVersion()
    {
        await EnsurePackageCached("System.CommandLine");

        var cachedVersion = NuGetCache.TryGetLatestCachedVersion("System.CommandLine");
        Assert.NotNull(cachedVersion);

        var root = CommandLineBuilder.CreateRootCommand();
        var args = new[] { "package", "System.CommandLine", "--version" };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => Task.FromResult(root.Parse(args).InvokeAsync().Result));

        Assert.Equal(0, exit);
        Assert.Equal(cachedVersion, output.Trim());
    }

    [Fact]
    public async Task LatestVersion_AlwaysQueriesNuGet()
    {
        var root = CommandLineBuilder.CreateRootCommand();
        var args = new[] { "package", "System.CommandLine", "--latest-version" };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => Task.FromResult(root.Parse(args).InvokeAsync().Result));

        Assert.Equal(0, exit);
        var version = output.Trim();
        Assert.Matches(@"^\d+\.\d+\.\d+", version);
    }

    [Fact]
    public async Task Versions_ListsMultipleVersions()
    {
        var root = CommandLineBuilder.CreateRootCommand();
        var args = new[] { "package", "System.CommandLine", "--versions" };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => Task.FromResult(root.Parse(args).InvokeAsync().Result));

        Assert.Equal(0, exit);
        var lines = output.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length > 1, "Expected multiple versions");
    }

    [Fact]
    public async Task Versions_WithLimit_RespectsLimit()
    {
        var root = CommandLineBuilder.CreateRootCommand();
        var args = new[] { "package", "System.CommandLine", "--versions", "2" };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => Task.FromResult(root.Parse(args).InvokeAsync().Result));

        Assert.Equal(0, exit);
        var lines = output.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
    }

    [Fact]
    public async Task Versions_WithRange_ListsTheInclusiveAddressVector()
    {
        var root = CommandLineBuilder.CreateRootCommand();
        var args = new[] { "package", "System.Text.Json@8.0.0..8.0.5", "--versions" };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => Task.FromResult(root.Parse(args).InvokeAsync().Result));

        Assert.Equal(0, exit);
        Assert.Equal(
            ["8.0.0", "8.0.1", "8.0.2", "8.0.3", "8.0.4", "8.0.5"],
            output.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task Type_WithRange_RequiresAnExplicitAddress()
    {
        var root = CommandLineBuilder.CreateRootCommand();
        var args = new[]
        {
            "type", "JsonSerializer",
            "--package", "System.Text.Json@8.0.0..8.0.5",
        };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => Task.FromResult(root.Parse(args).InvokeAsync().Result));

        Assert.Equal(1, exit);
        Assert.Contains("requires --at <version|#N|first|last>", error);
        Assert.Contains("package System.Text.Json@8.0.0..8.0.5 --versions", error);
    }

    [Fact]
    public async Task Type_WithRangeAddress_InspectsOnlyTheSelectedVersion()
    {
        var root = CommandLineBuilder.CreateRootCommand();
        var args = new[]
        {
            "type", "JsonSerializer",
            "--package", "System.Text.Json@8.0.0..8.0.5",
            "--at", "#6",
            "--verbose",
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => Task.FromResult(root.Parse(args).InvokeAsync().Result));

        Assert.Equal(0, exit);
        Assert.Contains("JsonSerializer", output);
        Assert.Contains("to 8.0.5 (#6 of #6)", error);
    }

    [Fact]
    public async Task Version_Bare_MatchesRouterBehavior()
    {
        await EnsurePackageCached("System.CommandLine");

        var root = CommandLineBuilder.CreateRootCommand();

        // Router path: bare name --version
        var routerArgs = CommandLineBuilder.PreprocessArgs(["System.CommandLine", "--version"]);
        var (routerExit, routerOutput, _) = await ConsoleCapture.RunAsync(
            () => Task.FromResult(root.Parse(routerArgs).InvokeAsync().Result));

        // Package path: package --version
        var packageArgs = new[] { "package", "System.CommandLine", "--version" };
        var (packageExit, packageOutput, _) = await ConsoleCapture.RunAsync(
            () => Task.FromResult(root.Parse(packageArgs).InvokeAsync().Result));

        Assert.Equal(0, routerExit);
        Assert.Equal(0, packageExit);
        Assert.Equal(routerOutput.Trim(), packageOutput.Trim());
    }

    [Fact]
    public async Task Package_InvalidPinnedVersion_ReturnsHelpfulError()
    {
        var root = CommandLineBuilder.CreateRootCommand();
        var args = new[] { "package", "System.Text.Json@badversion" };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => Task.FromResult(root.Parse(args).InvokeAsync().Result));

        Assert.Equal(1, exit);
        Assert.Contains("Error: 'badversion' is not a valid package version.", error);
        Assert.Contains("To list available versions: dotnet-inspect package system.text.json --versions", error);
    }

    [Fact]
    public async Task MultiPackage_InvalidPinnedVersion_ReturnsPerPackageHint()
    {
        var root = CommandLineBuilder.CreateRootCommand();
        var args = new[] { "package", "System.Text.Json@badversion", "Newtonsoft.Json", "--table" };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => Task.FromResult(root.Parse(args).InvokeAsync().Result));

        Assert.Equal(1, exit);
        Assert.Contains("Error: 'badversion' is not a valid package version.", error);
        Assert.Contains("Use id@version for per-package version pins.", error);
    }

    /// <summary>
    /// Downloads a package so it's in the NuGet cache for subsequent tests.
    /// </summary>
    private static async Task EnsurePackageCached(string packageName, string? version = null)
    {
        var client = HttpClientFactory.Shared;
        var outcome = await PackageExtractor.ExtractPackageAsync(
            client, packageName, log: null, version: version);
        Assert.True(outcome.IsSuccess, $"Failed to download {packageName}: {outcome.ErrorMessage}");
        if (outcome.Result?.TempDir is string tempDir && Directory.Exists(tempDir))
            Directory.Delete(tempDir, recursive: true);
    }
}
