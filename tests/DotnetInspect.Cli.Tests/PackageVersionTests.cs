using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using System.CommandLine;
using System.Text.Json;

namespace DotnetInspect.Cli.Tests;

/// <summary>
/// Tests for package subcommand --version and --versions behavior.
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
    public async Task Version_ValueSelectsExactPackage()
    {
        var (exit, output, error) = await RunAppAsync(
            "package",
            "Newtonsoft.Json",
            "--version",
            "13.0.4",
            "-S",
            "Package Info");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("13.0.4", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Versions_SingleRowJsonIsOneRowNotAScalar()
    {
        var (exit, output, error) = await RunAppAsync(
            "package",
            "System.CommandLine",
            "--versions",
            "-n",
            "1",
            "--json");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        var row = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Matches(
            @"^\d+\.\d+\.\d+",
            row.GetProperty("version").GetString());
    }

    [Fact]
    public async Task LatestCoordinate_AlwaysQueriesNuGet()
    {
        var root = CommandLineBuilder.CreateRootCommand();
        var args = new[] { "package", "System.CommandLine@latest", "--versions" };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => Task.FromResult(root.Parse(args).InvokeAsync().Result));

        Assert.Equal(0, exit);
        var version = output.Trim();
        Assert.Matches(@"^\d+\.\d+\.\d+", version);
    }

    [Fact]
    public async Task Version_WithoutValueRejectsBeforeAcquisition()
    {
        var (exit, output, error) = await RunAppAsync(
            "package",
            "ThisQueryMustNotReachTheNetwork",
            "--version");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "Required argument missing for option: '--version'.",
            error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "not found",
            error,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("--latest-version")]
    [InlineData("--latest-version=true")]
    public async Task LatestVersion_IsAnOrdinaryUnrecognizedOption(string option)
    {
        var (exit, output, error) = await RunAppAsync(
            "package",
            "ThisQueryMustNotReachTheNetwork",
            option);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Equal(
            $"Error: Unrecognized option '{option}'.{Environment.NewLine}",
            error);
    }

    [Fact]
    public async Task PackageHelp_AdvertisesVersionSelectors()
    {
        var (exit, output, error) = await RunAppAsync("package", "--help");

        Assert.Equal(0, exit);
        Assert.Contains("--version", output);
        Assert.Contains("--versions", output);
        Assert.DoesNotContain("--latest-version", output);
        Assert.Empty(error);
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
    public async Task Versions_EnvelopePreservesTheCompleteListing()
    {
        var result = await RunAppAsync(
            "package",
            "System.Text.Json",
            "--versions",
            "--envelope");

        Assert.Equal(0, result.Exit);
        Assert.Empty(result.Error);
        using JsonDocument json = JsonDocument.Parse(result.Output);
        JsonElement root = json.RootElement;
        Assert.Equal(1, root.GetProperty("schema_version").GetInt32());
        Assert.Equal(
            "package-version-listing",
            root.GetProperty("result_kind").GetString());
        JsonElement content = root.GetProperty("content");
        Assert.Equal("available", content.GetProperty("kind").GetString());
        JsonElement document = content.GetProperty("document");
        Assert.Equal(
            "system.text.json",
            document.GetProperty("request").GetProperty("packageId").GetString());
        Assert.Equal(
            "Authoritative",
            document.GetProperty("completeness").GetString());
        Assert.True(document.GetProperty("versions").GetArrayLength() > 1);
        Assert.False(content.TryGetProperty("count", out _));
        Assert.Equal(
            "nonProjectable",
            root.GetProperty("share").GetProperty("kind").GetString());
        Assert.Empty(root.GetProperty("diagnostics").EnumerateArray());
    }

    [Fact]
    public async Task Versions_CountSupportsScalarJsonAndScalarEnvelope()
    {
        var scalar = await RunAppAsync(
            "package",
            "System.Text.Json",
            "--versions",
            "--count");
        var scalarJson = await RunAppAsync(
            "package",
            "System.Text.Json",
            "--versions",
            "--count",
            "--json");
        var envelope = await RunAppAsync(
            "package",
            "System.Text.Json",
            "--versions",
            "--count",
            "--envelope");
        var selectedScalar = await RunAppAsync(
            "package",
            "System.Text.Json",
            "--versions",
            "--count",
            "-n",
            "1");
        var selectedEnvelope = await RunAppAsync(
            "package",
            "System.Text.Json",
            "--versions",
            "--count",
            "-n",
            "1",
            "--envelope");

        Assert.Equal(0, scalar.Exit);
        Assert.Empty(scalar.Error);
        int count = int.Parse(scalar.Output.Trim());
        Assert.True(count > 1);
        Assert.Equal((0, count.ToString(), ""), (
            scalarJson.Exit,
            scalarJson.Output.Trim(),
            scalarJson.Error));
        Assert.Equal(0, envelope.Exit);
        Assert.Empty(envelope.Error);
        using JsonDocument json = JsonDocument.Parse(envelope.Output);
        Assert.Equal(
            "package-version-count",
            json.RootElement.GetProperty("result_kind").GetString());
        Assert.Equal(
            count,
            json.RootElement.GetProperty("content").GetInt32());
        Assert.Equal(
            "nonProjectable",
            json.RootElement.GetProperty("share").GetProperty("kind").GetString());
        Assert.Empty(
            json.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal((0, "1", ""), (
            selectedScalar.Exit,
            selectedScalar.Output.Trim(),
            selectedScalar.Error));
        Assert.Equal(0, selectedEnvelope.Exit);
        Assert.Empty(selectedEnvelope.Error);
        using JsonDocument selectedJson =
            JsonDocument.Parse(selectedEnvelope.Output);
        Assert.Equal(
            1,
            selectedJson.RootElement.GetProperty("content").GetInt32());
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
    [InlineData("--versions", "--lines", false)]
    [InlineData("--versions", "--tail-lines", false)]
    [InlineData("--versions-with-feed", "--lines", false)]
    [InlineData("--versions-with-feed", "--tail-lines", false)]
    [InlineData("--versions", "--lines", true)]
    [InlineData("--versions", "--tail-lines", true)]
    [InlineData("--versions-with-feed", "--lines", true)]
    [InlineData("--versions-with-feed", "--tail-lines", true)]
    public async Task Versions_QueryDiscoveryPreservesJsonFormatContract(
        string selector,
        string modifier,
        bool environmentJson)
    {
        string? originalFormat = Environment.GetEnvironmentVariable("DOTNET_INSPECT_FORMAT");
        try
        {
            Environment.SetEnvironmentVariable(
                "DOTNET_INSPECT_FORMAT",
                environmentJson ? "json" : null);
            string[] arguments =
            [
                "--offline", "package", selector, "-Q", "-n", "1",
                .. environmentJson ? Array.Empty<string>() : ["--json"],
            ];
            var rejected = await RunAppAsync([.. arguments, modifier]);

            Assert.Equal(1, rejected.Exit);
            Assert.Empty(rejected.Output);
            Assert.Contains("cannot be combined with JSON output", rejected.Error);

            var complete = await RunAppAsync(arguments);
            Assert.Equal(0, complete.Exit);
            Assert.Empty(complete.Error);
            using JsonDocument document = JsonDocument.Parse(complete.Output);
            Assert.Equal("package", document.RootElement.GetProperty("command").GetString());

            Environment.SetEnvironmentVariable("DOTNET_INSPECT_FORMAT", "json");
            var text = await RunAppAsync(
                "--offline", "package", selector, "-Q", "-n", "1", modifier, "--markdown");
            Assert.Equal(0, text.Exit);
            Assert.Empty(text.Error);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_INSPECT_FORMAT", originalFormat);
        }
    }

    [Fact]
    public async Task Versions_QueryDiscoveryRetainsLegacyLenientRows()
    {
        var (exit, output, error) = await RunAppAsync(
            "--offline",
            "package",
            "--versions",
            "-Q",
            "Package Info",
            "--rows",
            "999..999",
            "--count");

        Assert.Equal(0, exit);
        Assert.Equal("0", output.Trim());
        Assert.Empty(error);
    }

    [Theory]
    [InlineData("--versions")]
    [InlineData("--versions-with-feed")]
    public async Task Versions_QueryDiscoveryReportsConflictingFormats(string selector)
    {
        var (exit, output, error) = await RunAppAsync(
            "--offline", "package", selector, "-Q", "-n", "1", "--lines", "--json", "--tsv");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("--json cannot be combined with --table, --tsv, or --jsonl.", error);
    }

    [Theory]
    [InlineData("--json")]
    [InlineData("--jsonl")]
    [InlineData("--tsv")]
    [InlineData("--table")]
    [InlineData("--markdown")]
    [InlineData("--plaintext")]
    public async Task Bare_RejectsExplicitFormatsBeforeAcquisition(string format)
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "ThisQueryMustNotReachTheNetwork", "--versions", "-n", "1", "--bare", format);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "--bare cannot be combined with --json, --jsonl, --tsv, --table, --markdown, --plaintext, or --mermaid.",
            error);
        Assert.DoesNotContain("not found", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Bare_AloneKeepsTheUndecoratedListing()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "System.Text.Json", "--versions", "-n", "1", "--bare");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Matches(@"^\d+\.\d+\.\d+\S*$", output.Trim());
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
    public async Task Versions_ValuedDirectionWithRangeUsesZeroArityDiagnostic(
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
        Assert.Equal($"Error: {modifier} does not accept a value.{Environment.NewLine}", error);

        var (correctedExit, correctedOutput, correctedError) = await RunAppAsync(
            [
                .. implicitCommand ? Array.Empty<string>() : ["package"],
                "System.CommandLine",
                selector,
                "-n",
                "1",
                modifier,
                "-Q",
                "--json"
            ]);

        Assert.Equal(0, correctedExit);
        Assert.Empty(correctedError);
        using JsonDocument document = JsonDocument.Parse(correctedOutput);
        Assert.True(document.RootElement.TryGetProperty("sections", out _));
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
    public async Task VersionSelectors_RejectValuesBeforeAcquisition(
        params string[] selectorArguments)
    {
        var (exit, output, error) = await RunAppAsync(
            ["package", "System.CommandLine", .. selectorArguments]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        string selector = selectorArguments[0].Split('=', ':')[0];
        string expected =
            $"Error: {selector} does not accept a value.{Environment.NewLine}";
        Assert.Equal(expected, error);
    }

    [Theory]
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
    [InlineData("--versions")]
    [InlineData("--versions-with-feed")]
    public async Task Versions_ExactVersionSelectorRejectsBeforeAcquisition(
        string pluralSelector)
    {
        var (exit, output, error) = await RunAppAsync(
            "package",
            "ThisQueryMustNotReachTheNetwork",
            pluralSelector,
            "--version",
            "2.0.10");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "cannot be combined with --version",
            error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "not found",
            error,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VersionedCoordinateAndVersionOptionRejectBeforeAcquisition()
    {
        var (exit, output, error) = await RunAppAsync(
            [
                "package",
                "ThisQueryMustNotReachTheNetwork@1.0.0..2.0.0",
                "--version",
                "2.0.10",
            ]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "--version cannot be combined with a versioned Package coordinate",
            error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "not found",
            error,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LocalPackageAndVersionOptionRejectBeforeAcquisition()
    {
        var (exit, output, error) = await RunAppAsync(
            "package",
            "ThisPackageMustNotExist.nupkg",
            "--version",
            "2.0.10");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "--version cannot be combined with a local Package file",
            error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "File not found",
            error,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--version=latest")]
    [InlineData("--version=1.*")]
    [InlineData("--version=1.0.0..2.0.0")]
    public async Task Version_RequiresExactValue(string versionArgument)
    {
        var (exit, output, error) = await RunAppAsync(
            "package",
            "ThisQueryMustNotReachTheNetwork",
            versionArgument);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "--version requires an exact Package version",
            error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "not found",
            error,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("--version", "2.0.10")]
    [InlineData("--version=2.0.10")]
    [InlineData("--version:2.0.10")]
    public async Task Version_ValueSelectsPackage(
        params string[] versionArguments)
    {
        var (exit, output, error) = await RunAppAsync(
            [
                "package",
                "System.CommandLine",
                .. versionArguments,
                "-S",
                "Package Info",
            ]);

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("2.0.10", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--version")]
    [InlineData("--version", "2.0.10")]
    [InlineData("--version=2.0.10")]
    [InlineData("--version:2.0.10")]
    public async Task CommandlessVersion_IsIllegalBeforeAcquisition(
        params string[] versionArguments)
    {
        var (exit, output, error) = await RunAppAsync(
            [
                "ThisQueryMustNotReachTheNetwork",
                .. versionArguments,
            ]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Equal(
            "Error: '--version' requires the explicit 'package' command. "
                + "Use 'package Package --version VERSION'."
                + Environment.NewLine,
            error);
        Assert.DoesNotContain(
            "not found",
            error,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Version_ValueRejectsBeforeCommandlessStructuralRouting()
    {
        var (exit, output, error) = await RunAppAsync(
            "Newtonsoft.Json",
            "--version",
            "13.0.3",
            "--library",
            "Newtonsoft.Json",
            "-S",
            "Library Info");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Equal(
            "Error: '--version' requires the explicit 'package' command. "
                + "Use 'package Package --version VERSION'."
                + Environment.NewLine,
            error);
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
    public async Task Versions_WithRange_EnvelopePreservesTheCompletePopulation()
    {
        var result = await RunAppAsync(
            "package",
            "System.Text.Json@8.0.0..8.0.5",
            "--versions",
            "--envelope");

        Assert.Equal(0, result.Exit);
        Assert.Empty(result.Error);
        using JsonDocument json = JsonDocument.Parse(result.Output);
        JsonElement root = json.RootElement;
        Assert.Equal(1, root.GetProperty("schema_version").GetInt32());
        Assert.Equal(
            "package-version-population",
            root.GetProperty("result_kind").GetString());
        JsonElement content = root.GetProperty("content");
        Assert.Equal("available", content.GetProperty("kind").GetString());
        JsonElement document = content.GetProperty("document");
        Assert.Equal(
            "system.text.json",
            document.GetProperty("request").GetProperty("packageId").GetString());
        Assert.Equal(
            ["8.0.0", "8.0.1", "8.0.2", "8.0.3", "8.0.4", "8.0.5"],
            document.GetProperty("versions").EnumerateArray()
                .Select(row => row.GetProperty("version").GetString()));
        Assert.Equal(
            ["#1", "#2", "#3", "#4", "#5", "#6"],
            document.GetProperty("versions").EnumerateArray()
                .Select(row => row.GetProperty("selector").GetString()));
        Assert.False(content.TryGetProperty("count", out _));
        Assert.Equal(
            "nonProjectable",
            root.GetProperty("share").GetProperty("kind").GetString());
        Assert.Empty(root.GetProperty("diagnostics").EnumerateArray());
    }

    [Fact]
    public async Task Versions_WithRange_CountSupportsScalarJsonAndScalarEnvelope()
    {
        var scalar = await RunAppAsync(
            "package",
            "System.Text.Json@8.0.0..8.0.5",
            "--versions",
            "--count");
        var scalarJson = await RunAppAsync(
            "package",
            "System.Text.Json@8.0.0..8.0.5",
            "--count",
            "--json");
        var envelope = await RunAppAsync(
            "package",
            "System.Text.Json@8.0.0..8.0.5",
            "--count",
            "--envelope");
        var selectedScalar = await RunAppAsync(
            "package",
            "System.Text.Json@8.0.0..8.0.5",
            "--count",
            "-n",
            "1");
        var selectedEnvelope = await RunAppAsync(
            "package",
            "System.Text.Json@8.0.0..8.0.5",
            "--count",
            "-n",
            "1",
            "--envelope");

        Assert.Equal((0, "6", ""), (
            scalar.Exit,
            scalar.Output.Trim(),
            scalar.Error));
        Assert.Equal((0, "6", ""), (
            scalarJson.Exit,
            scalarJson.Output.Trim(),
            scalarJson.Error));
        Assert.Equal(0, envelope.Exit);
        Assert.Empty(envelope.Error);
        using JsonDocument json = JsonDocument.Parse(envelope.Output);
        Assert.Equal(
            "package-version-count",
            json.RootElement.GetProperty("result_kind").GetString());
        Assert.Equal(
            6,
            json.RootElement.GetProperty("content").GetInt32());
        Assert.Equal((0, "1", ""), (
            selectedScalar.Exit,
            selectedScalar.Output.Trim(),
            selectedScalar.Error));
        Assert.Equal(0, selectedEnvelope.Exit);
        Assert.Empty(selectedEnvelope.Error);
        using JsonDocument selectedJson =
            JsonDocument.Parse(selectedEnvelope.Output);
        Assert.Equal(
            1,
            selectedJson.RootElement.GetProperty("content").GetInt32());
    }

    [Theory]
    [InlineData("--json")]
    [InlineData("--table")]
    [InlineData("--rows")]
    public async Task RangeEnvelope_RejectsIncompatibleShapeBeforeExecution(
        string incompatible)
    {
        string[] value = incompatible == "--rows" ? ["1..2"] : [];
        string[] args =
        [
            "package",
            "System.Text.Json@8.0.0..8.0.5",
            "--versions",
            "--envelope",
            incompatible,
            .. value,
        ];
        var result = await RunAppAsync(args);

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains("--envelope cannot be combined", result.Error);
    }

    [Theory]
    [InlineData("System.Text.Json@8.0.0")]
    [InlineData("System.Text.Json@latest")]
    public async Task ListingEnvelope_RejectsNonPopulationPackageReference(
        string packageReference)
    {
        var result = await RunAppAsync(
            "package",
            packageReference,
            "--versions",
            "--envelope");

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "--envelope on package requires one unversioned package",
            result.Error);
    }

    [Fact]
    public async Task ListingEnvelope_RequiresAnExplicitPluralVersionGesture()
    {
        var result = await RunAppAsync(
            "package",
            "System.Text.Json",
            "--count",
            "--envelope");

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "with --versions or --versions-with-feed",
            result.Error);
    }

    [Fact]
    public async Task ListingEnvelope_RejectsLocalFile()
    {
        var result = await RunAppAsync(
            "package",
            typeof(PackageVersionTests).Assembly.Location,
            "--versions",
            "--envelope");

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "--envelope on package requires one unversioned package",
            result.Error);
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
