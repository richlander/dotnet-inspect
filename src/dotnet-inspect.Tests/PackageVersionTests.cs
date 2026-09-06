using DotnetInspector.Core;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using System.CommandLine;
using System.Text.Json;

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

        var cachedVersion = PackageExtractor.TryGetLatestCachedCandidateVersion(
            "System.CommandLine",
            NuGetSourceResolver.ResolveSourceKeys(null));
        Assert.NotNull(cachedVersion);

        var root = CommandLineBuilder.CreateRootCommand();
        var args = new[] { "package", "System.CommandLine", "--version" };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => Task.FromResult(root.Parse(args).InvokeAsync().Result));

        Assert.Equal(0, exit);
        Assert.Equal(cachedVersion, output.Trim());
    }

    [Fact]
    public async Task Version_Bare_PreservesSingularJsonBehavior()
    {
        var (exit, output, error) = await RunAppAsync(
            "package",
            "System.CommandLine",
            "--version",
            "--json");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Matches(@"^\d+\.\d+\.\d+", output.Trim());
        Assert.DoesNotContain("{", output, StringComparison.Ordinal);
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
        var (exit, output, error) = await RunAppAsync(
            "package",
            "System.CommandLine",
            "--versions",
            "-n",
            "2");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        var lines = output.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
    }

    [Fact]
    public async Task Versions_WithLimit_ProducesCompleteJsonRows()
    {
        var (exit, output, error) = await RunAppAsync(
            "package",
            "System.CommandLine",
            "--versions",
            "-n",
            "2",
            "--json");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.Equal(2, document.RootElement.GetArrayLength());
        Assert.All(
            document.RootElement.EnumerateArray(),
            row => Assert.True(
                row.TryGetProperty("version", out _)));
    }

    [Fact]
    public async Task Versions_BareShorthandAndTailSelectRows()
    {
        var (headExit, headOutput, headError) = await RunAppAsync(
            "System.CommandLine",
            "--versions",
            "-2");
        var (tailExit, tailOutput, tailError) = await RunAppAsync(
            "System.CommandLine",
            "--versions",
            "-2",
            "--tail");

        Assert.Equal(0, headExit);
        Assert.Equal(0, tailExit);
        Assert.Empty(headError);
        Assert.Empty(tailError);
        string[] headRows =
            headOutput.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries);
        string[] tailRows =
            tailOutput.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, headRows.Length);
        Assert.Equal(2, tailRows.Length);
        Assert.False(headRows.SequenceEqual(tailRows));
    }

    [Theory]
    [InlineData("--head")]
    [InlineData("--tail")]
    public async Task Versions_ModifierBeforeBareShorthandSelectsRows(
        string direction)
    {
        var (exit, output, error) = await RunAppAsync(
            "package",
            "System.CommandLine",
            "--versions",
            direction,
            "-2");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal(
            2,
            output.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public async Task VersionsWithFeed_WithLimit_ProducesCompleteJsonRows()
    {
        var (exit, output, error) = await RunAppAsync(
            "package",
            "System.CommandLine",
            "--versions-with-feed",
            "-n",
            "2",
            "--json");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.Equal(2, document.RootElement.GetArrayLength());
        Assert.All(
            document.RootElement.EnumerateArray(),
            row =>
            {
                Assert.True(row.TryGetProperty("version", out _));
                Assert.True(row.TryGetProperty("feed", out _));
                Assert.True(row.GetProperty("listed").GetBoolean());
            });
    }

    [Fact]
    public async Task VersionsWithFeed_LinesMakesRenderedClippingExplicit()
    {
        var (semanticExit, semanticOutput, semanticError) =
            await RunAppAsync(
                "package",
                "System.CommandLine",
                "--versions-with-feed",
                "-n",
                "1",
                "--tsv");
        var (linesExit, linesOutput, linesError) =
            await RunAppAsync(
                "package",
                "System.CommandLine",
                "--versions-with-feed",
                "-n",
                "1",
                "--lines",
                "--tsv");

        Assert.Equal(0, semanticExit);
        Assert.Equal(0, linesExit);
        Assert.Empty(semanticError);
        Assert.Empty(linesError);
        Assert.Equal(
            2,
            semanticOutput.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Single(
            linesOutput.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries));
    }

    [Theory]
    [InlineData("--versions")]
    [InlineData("--versions-with-feed")]
    [InlineData("--lines")]
    [InlineData("--tail-lines")]
    public void Versions_ZeroArityFlagsPreserveFollowingPackageInput(
        string optionName)
    {
        var root = CommandLineBuilder.CreateRootCommand();
        var package = root.Subcommands.Single(command => command.Name == "package");
        var option = Assert.IsType<Option<bool>>(
            package.Options.Single(option => option.Name == optionName));
        var packageArgument = Assert.IsType<Argument<string[]>>(
            Assert.Single(package.Arguments));
        string[] arguments = optionName is "--versions" or "--versions-with-feed"
            ? ["package", optionName, "false"]
            : ["package", "--versions", "-n", "1", optionName, "false"];

        var result = root.Parse(arguments);

        Assert.Empty(result.Errors);
        Assert.True(result.GetValue(option));
        Assert.Equal(
            ["false"],
            Assert.IsType<string[]>(result.GetValue(packageArgument)));
    }

    [Theory]
    [InlineData("--versions", "--lines", null)]
    [InlineData("--versions", "--tail-lines", null)]
    [InlineData("--versions", "--lines", "false")]
    [InlineData("--versions", "--tail-lines", "false")]
    [InlineData("--versions-with-feed", "--lines", "false")]
    [InlineData("--versions-with-feed", "--tail-lines", "false")]
    [InlineData("--versions", "--lines", "true")]
    [InlineData("--versions", "--tail-lines", "true")]
    public async Task Versions_LinesRejectsDocumentJsonBeforeAcquisition(
        string selector,
        string modifier,
        string? followingInput)
    {
        var (exit, output, error) = await RunAppAsync(
            [
                "package",
                "ThisQueryMustNotReachTheNetwork",
                selector,
                "-n",
                "2",
                modifier,
                .. followingInput is null ? Array.Empty<string>() : [followingInput],
                "--json"
            ]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "cannot be combined with JSON output",
            error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "not found",
            error,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("--lines", null)]
    [InlineData("--lines", "false")]
    [InlineData("--tail-lines", "false")]
    public async Task Versions_LinesRejectsEnvironmentDocumentJsonBeforeAcquisition(
        string modifier,
        string? followingInput)
    {
        string? originalFormat =
            Environment.GetEnvironmentVariable("DOTNET_INSPECT_FORMAT");
        try
        {
            Environment.SetEnvironmentVariable(
                "DOTNET_INSPECT_FORMAT",
                "json");
            var (exit, output, error) = await RunAppAsync(
                [
                    "package",
                    "ThisQueryMustNotReachTheNetwork",
                    "--versions",
                    "-n",
                    "2",
                    modifier,
                    .. followingInput is null ? Array.Empty<string>() : [followingInput]
                ]);

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains(
                "cannot be combined with JSON output",
                error,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "not found",
                error,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "DOTNET_INSPECT_FORMAT",
                originalFormat);
        }
    }

    [Theory]
    [InlineData("--head")]
    [InlineData("--tail")]
    [InlineData("--lines")]
    [InlineData("--tail-lines")]
    public async Task Versions_ModifierRequiresCountReportsUsableRemedy(
        string modifier)
    {
        var (exit, output, error) = await RunAppAsync(
            "package",
            "ThisQueryMustNotReachTheNetwork",
            "--versions",
            "--rows",
            "1..2",
            modifier);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains($"{modifier} requires -n.", error, StringComparison.Ordinal);

        var (correctedExit, correctedOutput, correctedError) = await RunAppAsync(
            "package",
            "System.CommandLine",
            "--versions",
            "--rows",
            "1..2",
            modifier,
            "-n",
            "1");

        Assert.Equal(0, correctedExit);
        Assert.Empty(correctedError);
        Assert.Single(correctedOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    [Theory]
    [InlineData("--versions", "--head", false)]
    [InlineData("--versions", "--tail", false)]
    [InlineData("--versions-with-feed", "--head", false)]
    [InlineData("--versions-with-feed", "--tail", false)]
    [InlineData("--versions", "--head", true)]
    [InlineData("--versions", "--tail", true)]
    [InlineData("--versions-with-feed", "--head", true)]
    [InlineData("--versions-with-feed", "--tail", true)]
    public async Task Versions_ValuedDirectionWithRangeReportsAdoptedCountRemedy(
        string selector,
        string modifier,
        bool implicitCommand)
    {
        var (exit, output, error) = await RunAppAsync(
            [
                .. implicitCommand ? Array.Empty<string>() : ["package"],
                "ThisQueryMustNotReachTheNetwork",
                selector,
                "--rows",
                "1..2",
                modifier,
                "1"
            ]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains($"Use '-n 1 {modifier}'", error, StringComparison.Ordinal);
        Assert.DoesNotContain("Use '--rows", error, StringComparison.Ordinal);

        var (correctedExit, correctedOutput, correctedError) = await RunAppAsync(
            [
                .. implicitCommand ? Array.Empty<string>() : ["package"],
                "System.CommandLine",
                selector,
                "--rows",
                "1..2",
                "-n",
                "1",
                modifier,
                "--json"
            ]);

        Assert.Equal(0, correctedExit);
        Assert.Empty(correctedError);
        using JsonDocument document = JsonDocument.Parse(correctedOutput);
        Assert.Equal(1, document.RootElement.GetArrayLength());
    }

    [Theory]
    [InlineData("--versions", "2")]
    [InlineData("--versions", "2147483648")]
    [InlineData("--versions-with-feed", "2")]
    [InlineData("--versions-with-feed", "2147483648")]
    public void Versions_SelectorLeavesNumericInputAsPackageArgument(
        string selector,
        string packageName)
    {
        var root = CommandLineBuilder.CreateRootCommand();
        var package = root.Subcommands.Single(command => command.Name == "package");
        var option = Assert.IsType<Option<bool>>(
            package.Options.Single(option => option.Name == selector));
        var packageArgument = Assert.IsType<Argument<string[]>>(
            Assert.Single(package.Arguments));
        string[] args = ["package", selector, packageName];

        Assert.False(
            CommandLineBuilder.TryGetStaleArgumentError(
                args,
                root,
                out _));
        var result = root.Parse(args);
        Assert.Empty(result.Errors);
        Assert.True(result.GetValue(option));
        Assert.Equal(
            [packageName],
            Assert.IsType<string[]>(result.GetValue(packageArgument)));
    }

    [Theory]
    [InlineData("--versions", "2")]
    [InlineData("--versions-with-feed", "2")]
    [InlineData("--versions=2")]
    [InlineData("--versions:2")]
    [InlineData("--versions-with-feed=2")]
    [InlineData("--versions-with-feed:2")]
    public async Task Versions_AdditionalPackageUsesMultiPackageValidation(
        params string[] selectorArguments)
    {
        var (exit, output, error) = await RunAppAsync(
            ["package", "System.CommandLine", .. selectorArguments]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Multiple package inspection cannot be combined", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--versions", "--version")]
    [InlineData("--versions", "--latest-version")]
    [InlineData("--versions-with-feed", "--version")]
    [InlineData("--versions-with-feed", "--latest-version")]
    [InlineData("--versions", "--versions-with-feed")]
    public async Task Versions_ConflictingSelectorsRejectBeforeAcquisition(
        string pluralSelector,
        string conflictingSelector)
    {
        var (exit, output, error) = await RunAppAsync(
            "package",
            "ThisQueryMustNotReachTheNetwork",
            pluralSelector,
            "-n",
            "2",
            conflictingSelector);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "cannot be combined",
            error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "not found",
            error,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("--version", "2.0.10")]
    [InlineData("--version=2.0.10", null)]
    public async Task Versions_ValuedSingularSelectorConflictsBeforeAcquisition(
        string versionSelector,
        string? versionValue)
    {
        string[] selectorArgs = versionValue is null
            ? [versionSelector]
            : [versionSelector, versionValue];
        var (exit, output, error) = await RunAppAsync(
            [
                "package",
                "ThisQueryMustNotReachTheNetwork",
                "--versions",
                "-n",
                "2",
                .. selectorArgs,
            ]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "cannot be combined",
            error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "not found",
            error,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Versions_RowSelectionCannotBypassInvocationLowering()
    {
        var root = CommandLineBuilder.CreateRootCommand();
        string[] args =
        [
            "package",
            "System.CommandLine",
            "--versions",
            "-n",
            "2"
        ];

        var (exit, output, error) =
            await ConsoleCapture.RunAsync(
                () => root.Parse(args).InvokeAsync());

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "was not lowered before execution",
            error,
            StringComparison.Ordinal);
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
            output.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries));
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
    [Trait("Speed", "Slow")]
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
    public async Task Member_WithRangeAddress_UsesSelectedVersionForSourceAcquisition()
    {
        var root = CommandLineBuilder.CreateRootCommand();
        var args = new[]
        {
            "member", "Serilog.Core.Logger", "Write:1",
            "--package", "Serilog@4.0.0..4.2.0",
            "--at", "4.2.0",
            "-S", "PDB Source",
            "--print",
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => Task.FromResult(root.Parse(args).InvokeAsync().Result));

        Assert.Equal(0, exit);
        Assert.Contains("public void Write(LogEvent logEvent)", output);
        Assert.DoesNotContain("Invalid package version", error);
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

    private static Task<(int Exit, string Output, string Error)> RunAppAsync(
        params string[] args)
    {
        return ConsoleCapture.RunAsync(async () =>
        {
            var root = CommandLineBuilder.CreateRootCommand();
            if (CommandLineBuilder.TryGetStaleArgumentError(
                    args,
                    root,
                    out string? error))
            {
                CommandError.Write(error!);
                return 1;
            }

            args = CommandLineBuilder.PreprocessArgs(args, root);
            return await CommandLineBuilder.InvokeWithLineWindowAsync(
                root.Parse(args),
                args);
        });
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
