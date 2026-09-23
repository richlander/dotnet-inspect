using System.CommandLine;
using System.Text.Json;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Services;
using CoreHttpClientFactory = DotnetInspector.Networking.HttpClientFactory;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class ApiCoordinateMatchCommandTests
{
    [Fact]
    public void TypeMatch_FormsTypedRequestInCallerDirection()
    {
        var options = new SharedOptions();
        Command command =
            ApiCommandDefinitions.CreateTypeCommand(
                options,
                out TypeOptionsParser.TypeCommandArgs args);
        Option<bool> matchOption =
            Assert.IsType<Option<bool>>(
                command.Options.Single(option =>
                    option.Name == "--match"));
        var parseResult = command.Parse(
            [
                "System.Text.Json.Schema.JsonSchemaExporter",
                "--package", "System.Text.Json@9.0.0..8.0.6",
                "--library", "ref/net8.0/System.Text.Json.dll",
                "--tfm", "net8.0",
                "--source", "https://example.invalid/v3/index.json",
                "--all",
                "--match",
            ]);

        Assert.Empty(parseResult.Errors);
        var success = Assert.IsType<ApiCoordinateMatchOptionsParser.Success>(
            ApiCoordinateMatchOptionsParser.ParseType(
                parseResult,
                options,
                args,
                matchOption));
        Assert.Equal("System.Text.Json", success.Request.PackageId);
        Assert.Equal("9.0.0", success.Request.SourceVersion);
        Assert.Equal("8.0.6", success.Request.DestinationVersion);
        Assert.Equal(
            "System.Text.Json.Schema.JsonSchemaExporter",
            success.Request.Type);
        Assert.Null(success.Request.Member);
        Assert.Equal("net8.0", success.Request.TargetFramework);
        Assert.Equal(
            "ref/net8.0/System.Text.Json.dll",
            success.Request.Library);
        Assert.True(success.Request.IncludeAll);
        Assert.Equal(
            ["https://example.invalid/v3/index.json"],
            success.SourceOptions.Sources);
    }

    [Theory]
    [InlineData("Deserialize:1", "Deserialize:1")]
    [InlineData("Name~abcdef", "Name~abcdef")]
    [InlineData("RegisterAttached<TOwner,THost,TValue>:1", "RegisterAttached<TOwner,THost,TValue>:1")]
    public void MemberMatch_FormsOneSourceSelector(
        string selector,
        string expected)
    {
        var options = new SharedOptions();
        Command command =
            ApiCommandDefinitions.CreateMemberCommand(
                options,
                out MemberOptionsParser.MemberCommandArgs args);
        Option<bool> matchOption =
            Assert.IsType<Option<bool>>(
                command.Options.Single(option =>
                    option.Name == "--match"));
        var parseResult = command.Parse(
            [
                "System.Text.Json.JsonSerializer",
                selector,
                "--package", "System.Text.Json@9.0.0..10.0.0",
                "--match",
            ]);

        Assert.Empty(parseResult.Errors);
        var success = Assert.IsType<ApiCoordinateMatchOptionsParser.Success>(
            ApiCoordinateMatchOptionsParser.ParseMember(
                parseResult,
                options,
                args,
                matchOption));
        Assert.Equal(
            "System.Text.Json.JsonSerializer",
            success.Request.Type);
        Assert.Equal(expected, success.Request.Member);
    }

    [Theory]
    [InlineData("Deserialize", "Deserialize:2")]
    [InlineData("RegisterAttached<TOwner,THost,TValue>", "RegisterAttached<TOwner,THost,TValue>:2")]
    public void MemberMatch_IndexUsesEstablishedSelectorSpelling(string selector, string expected)
    {
        var options = new SharedOptions();
        Command command =
            ApiCommandDefinitions.CreateMemberCommand(
                options,
                out MemberOptionsParser.MemberCommandArgs args);
        Option<bool> matchOption =
            Assert.IsType<Option<bool>>(
                command.Options.Single(option =>
                    option.Name == "--match"));
        var parseResult = command.Parse(
            [
                "System.Text.Json.JsonSerializer",
                selector,
                "--index", "2",
                "--package", "System.Text.Json@9.0.0..10.0.0",
                "--match",
            ]);

        Assert.Empty(parseResult.Errors);
        var success = Assert.IsType<ApiCoordinateMatchOptionsParser.Success>(
            ApiCoordinateMatchOptionsParser.ParseMember(
                parseResult,
                options,
                args,
                matchOption));
        Assert.Equal(expected, success.Request.Member);
    }

    [Theory]
    [InlineData("type", "--at", "first")]
    [InlineData("type", "--count", null)]
    [InlineData("type", "--project", ".")]
    [InlineData("type", "--platform", "System.Text.Json")]
    [InlineData("type", "--table", null)]
    [InlineData("member", "--rows", "1..1")]
    [InlineData("member", "-S", "IL")]
    [InlineData("member", "--repo", ".")]
    public async Task Match_RejectsUnsupportedModesBeforeAcquisition(
        string command,
        string option,
        string? value)
    {
        string[] subject = command == "type"
            ? ["type", "Example.Widget"]
            : ["member", "Example.Widget", "Run:1"];
        var result = await InvokeWithoutAcquisition(
            [
                .. subject,
                "--package", "never.acquire@1.0.0..2.0.0",
                "--match",
                option,
                .. value is null ? [] : new[] { value },
            ]);

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains("--match", result.Error);
        Assert.DoesNotContain("MATCH_ACQUIRED", result.Error);
    }

    [Theory]
    [InlineData("type", "Example.*", null)]
    [InlineData("member", "Example.Widget", "R*")]
    public async Task Match_RejectsPopulationSelectorsBeforeAcquisition(
        string command,
        string type,
        string? member)
    {
        string[] subject = member is null
            ? [command, type]
            : [command, type, member];
        var result = await InvokeWithoutAcquisition(
            [
                .. subject,
                "--package", "never.acquire@1.0.0..2.0.0",
                "--match",
            ]);

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains("exact", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MATCH_ACQUIRED", result.Error);
    }

    [Fact]
    public async Task Match_RejectsCompetingOutputFormatsBeforeAcquisition()
    {
        var result = await InvokeWithoutAcquisition(
            [
                "type", "Example.Widget",
                "--package", "never.acquire@1.0.0..2.0.0",
                "--match", "--json", "--markdown",
            ]);

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains("only one", result.Error);
        Assert.DoesNotContain("MATCH_ACQUIRED", result.Error);
    }

    [Fact]
    public async Task Match_DoesNotBypassUnknownOptionChecks()
    {
        var result = await InvokeWithoutAcquisition(
            [
                "member", "Example.Widget", "Run:1",
                "--package", "never.acquire@1.0.0..2.0.0",
                "--match", "--destination-index", "2",
            ]);

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "--destination-index",
            result.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain("MATCH_ACQUIRED", result.Error);
    }

    [Fact]
    public async Task MemberEnvelope_RequiresMatch()
    {
        var result = await Invoke(
        [
            "member", "Example.Widget", "Run:1",
            "--package", "Example@1.0.0",
            "--envelope",
        ]);

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains("requires --match", result.Error);
    }

    [Fact]
    public async Task TypeEnvelope_RequiresExactSharedRoute()
    {
        var result = await Invoke(
        [
            "type", "Example.Widget",
            "--package", "Example@1.0.0",
            "--envelope",
        ]);

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "requires exact package-backed Type or Library API inspection",
            result.Error);
    }

    [Theory]
    [InlineData("System.Text")]
    [InlineData("Regex")]
    public async Task TypeEnvelopeRejectsPlatformFallbacks(
        string type)
    {
        var result = await Invoke(
        [
            "type", type,
            "--envelope",
        ]);

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "requires exact package-backed Type or Library API inspection",
            result.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "best-effort platform prefix matches",
            result.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "resolved via platform find",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task TypeEnvelopeRejectsLibraryPackageRangeBeforeAcquisition()
    {
        var result = await InvokeWithoutAcquisition(
        [
            "type",
            "--package", "Example@1.0.0..2.0.0",
            "--library", "Example.dll",
            "--tfm", "net8.0",
            "--envelope",
        ]);

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "requires exact package-backed Type or Library API inspection",
            result.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain("MATCH_ACQUIRED", result.Error);
    }

    [Fact]
    public async Task TypeEnvelopeRejectsPlatformFrameworkBeforeAcquisition()
    {
        string[][] requests =
        [
            [
                "type", "Example.Widget",
                "--package", "Example@1.0.0",
                "--tfm", "net8.0",
                "--framework", "net9.0",
                "--envelope",
            ],
            [
                "type",
                "--package", "Example@1.0.0",
                "--library", "Example.dll",
                "--tfm", "net8.0",
                "--framework", "net9.0",
                "--envelope",
            ],
        ];

        foreach (string[] request in requests)
        {
            var result = await InvokeWithoutAcquisition(request);

            Assert.Equal(1, result.Exit);
            Assert.Empty(result.Output);
            Assert.Contains(
                "requires exact package-backed Type or Library API inspection",
                result.Error,
                StringComparison.Ordinal);
            Assert.DoesNotContain("MATCH_ACQUIRED", result.Error);
        }
    }

    [Fact]
    public async Task TypeEnvelopeRejectsWorkspaceBeforeRestoration()
    {
        var result = await Invoke(
        [
            "type", "Example.Widget",
            "--workspace", "not-a-workspace-packet",
            "--envelope",
        ]);

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "requires exact package-backed Type or Library API inspection",
            result.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Workspace packet",
            result.Error,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--tree")]
    [InlineData("--count")]
    [InlineData("-Q")]
    [InlineData("-v:n")]
    [InlineData("-v:d")]
    public async Task TypeEnvelopeRejectsPresentationOptions(
        string incompatibleOption)
    {
        var result = await InvokeWithoutAcquisition(
        [
            "type", "Example.Widget",
            "--package", "Example@1.0.0",
            "--tfm", "net8.0",
            "--envelope",
            incompatibleOption,
        ]);

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "--envelope cannot be combined with",
            result.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain("MATCH_ACQUIRED", result.Error);
    }

    [Fact]
    public async Task TypeEnvelopeRejectsRetiredBareBeforeAcquisition()
    {
        var result = await InvokeWithoutAcquisition(
        [
            "type", "Example.Widget",
            "--package", "Example@1.0.0",
            "--tfm", "net8.0",
            "--envelope",
            "--raw",
        ]);

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains("Unrecognized option '--raw'", result.Error);
        Assert.DoesNotContain("MATCH_ACQUIRED", result.Error);
    }

    [Theory]
    [InlineData("-v:q")]
    [InlineData("-v:m")]
    public async Task TypeEnvelopeAdmitsCompleteVerbosityBeforeAcquisition(
        string verbosity)
    {
        var result = await InvokeWithoutAcquisition(
        [
            "type", "Example.Widget",
            "--package", "Example@1.0.0",
            "--tfm", "net8.0",
            "--envelope",
            verbosity,
        ]);

        Assert.Equal(1, result.Exit);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        Assert.Equal(
            "exact-type",
            document.RootElement.GetProperty("result_kind").GetString());
        Assert.DoesNotContain(
            "--envelope cannot be combined with",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task TypeContentJsonRejectsTreeBeforeAcquisition()
    {
        var result = await InvokeWithoutAcquisition(
        [
            "type", "Example.Widget",
            "--package", "Example@1.0.0",
            "--tfm", "net8.0",
            "--json",
            "--tree",
        ]);

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "--tree is not supported with JSON output on type",
            result.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain("MATCH_ACQUIRED", result.Error);
    }

    [Fact]
    public async Task TypeImplicitJsonRejectsTreeBeforeAcquisition()
    {
        string? previous =
            Environment.GetEnvironmentVariable("DOTNET_INSPECT_FORMAT");
        try
        {
            Environment.SetEnvironmentVariable(
                "DOTNET_INSPECT_FORMAT",
                "json");
            var result = await InvokeWithoutAcquisition(
            [
                "type", "Example.Widget",
                "--package", "Example@1.0.0",
                "--tfm", "net8.0",
                "--tree",
            ]);

            Assert.Equal(1, result.Exit);
            Assert.Empty(result.Output);
            Assert.Contains(
                "--tree is not supported with JSON output on type",
                result.Error,
                StringComparison.Ordinal);
            Assert.DoesNotContain("MATCH_ACQUIRED", result.Error);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "DOTNET_INSPECT_FORMAT",
                previous);
        }
    }

    [Fact]
    public void SubjectHelpAdvertisesMatchAndEnvelope()
    {
        RootCommand root = CommandLineBuilder.CreateRootCommand();

        foreach (string name in new[] { "type", "member" })
        {
            Command command =
                root.Subcommands.Single(candidate =>
                    candidate.Name == name);
            Assert.Contains(command.Options, option =>
                option.Name == "--match");
            Assert.Contains(command.Options, option =>
                option.Name == "--envelope");
        }
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task SystemTextJson_ContentAndEnvelopeAreIdentical()
    {
        string[] request =
        [
            "type",
            "System.Text.Json.Schema.JsonSchemaExporter",
            "--package", "System.Text.Json@9.0.0..8.0.6",
            "--tfm", "net8.0",
            "--source", "https://api.nuget.org/v3/index.json",
            "--match",
            "--compact",
            "--tips", "q",
        ];
        var content = await Invoke([.. request, "--json"]);
        var envelope = await Invoke([.. request, "--envelope"]);

        Assert.True(content.Exit == 0, content.Error);
        Assert.True(envelope.Exit == 0, envelope.Error);
        Assert.Empty(content.Error);
        Assert.Empty(envelope.Error);
        using JsonDocument contentJson =
            JsonDocument.Parse(content.Output);
        using JsonDocument envelopeJson =
            JsonDocument.Parse(envelope.Output);
        JsonElement root = envelopeJson.RootElement;
        Assert.Equal(
            "api-coordinate-match",
            root.GetProperty("result_kind").GetString());
        Assert.True(JsonElement.DeepEquals(
            contentJson.RootElement,
            root.GetProperty("content")));
        Assert.Equal(
            "Absent",
            contentJson.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            "9.0.0",
            contentJson.RootElement.GetProperty("before")
                .GetProperty("version").GetString());
        Assert.Equal(
            "8.0.6",
            contentJson.RootElement.GetProperty("after")
                .GetProperty("version").GetString());
    }

    [Theory]
    [InlineData("RegisterAttached<TOwner,THost,TValue>:1", "RegisterAttached~6a22f40030")]
    [InlineData("RegisterAttached<THost,TValue>:1", "RegisterAttached~8192c3d837")]
    public async Task Avalonia_GenericArityPreservesTheSelectedSource(string selector, string expected)
    {
        var result = await Invoke(
            [
                "member", "Avalonia.AvaloniaProperty", selector,
                "--package", "Avalonia@11.3.14..11.3.14",
                "--tfm", "net8.0",
                "--source", "https://api.nuget.org/v3/index.json",
                "--match", "--json", "--compact", "--tips", "q",
            ]);

        Assert.True(result.Exit == 0, result.Error);
        Assert.Empty(result.Error);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        JsonElement content = document.RootElement;
        Assert.Equal("Exact", content.GetProperty("status").GetString());
        Assert.Equal(expected, content.GetProperty("source").GetProperty("member").GetString());
        Assert.Equal(expected, content.GetProperty("destination").GetProperty("member").GetString());
    }

    private static Task<(int Exit, string Output, string Error)> Invoke(
        string[] arguments) =>
        ConsoleCapture.RunAsync(() =>
        {
            string[] normalized =
                CommandLineBuilder.PreprocessArgs(arguments);
            return CommandLineBuilder.InvokeWithLineWindowAsync(
                CommandLineBuilder.CreateRootCommand().Parse(normalized),
                normalized);
        });

    private static async Task<(int Exit, string Output, string Error)>
        InvokeWithoutAcquisition(string[] arguments)
    {
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(_ =>
            throw new InvalidOperationException("MATCH_ACQUIRED"));
        try
        {
            return await Invoke(arguments);
        }
        finally
        {
            CoreHttpClientFactory.SetPackageSourceHandlerForTesting(null);
        }
    }
}
