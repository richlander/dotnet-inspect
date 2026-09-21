using System.CommandLine;
using System.Text.Json;
using DotnetInspect.Cli.CommandLine;
using DotnetInspector.Fixtures;
using DotnetInspector.Packages;
using DotnetInspector.Presentation;
using DotnetInspector.Queries;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class LibraryApiDiffEnvelopeCommandTests
{
    public LibraryApiDiffEnvelopeCommandTests() => NuGetCache.Initialize("dotnet-inspect");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PairedJson_PreservesCompleteContentAndBaseline(bool compact)
    {
        string[] formatting = compact ? ["--compact"] : [];
        var content = await Run(["--json", .. formatting]);
        var envelope = await Run(["--envelope", .. formatting]);

        Assert.Equal(0, content.Exit);
        Assert.Equal(0, envelope.Exit);
        Assert.Empty(content.Error);
        Assert.Empty(envelope.Error);
        using JsonDocument contentJson = JsonDocument.Parse(content.Output);
        using JsonDocument envelopeJson = JsonDocument.Parse(envelope.Output);
        JsonElement root = envelopeJson.RootElement;
        Assert.Equal(1, root.GetProperty("schema_version").GetInt32());
        Assert.Equal("library-api-diff", root.GetProperty("result_kind").GetString());
        Assert.True(JsonElement.DeepEquals(contentJson.RootElement, root.GetProperty("content")));
        Assert.Equal("available", root.GetProperty("content").GetProperty("outcome").GetString());
        JsonElement document = root.GetProperty("content").GetProperty("document");
        Assert.True(document.GetProperty("summary").GetProperty("changedTypeCount").GetInt32() > 0);
        Assert.NotEmpty(document.GetProperty("comparison").GetProperty("subjects").EnumerateArray());
        Assert.Equal(JsonValueKind.Number, document.GetProperty("before").GetProperty("scope").ValueKind);
        Assert.Contains("TypeDefinitionOnly", document.GetRawText());
        Assert.Equal("nonProjectable", root.GetProperty("portable_projection").GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("portable_projection").GetProperty("full_url").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("portable_projection").GetProperty("packet").ValueKind);
        Assert.Empty(root.GetProperty("diagnostics").EnumerateArray());
        Assert.False(root.TryGetProperty("evidence", out _));
        Assert.EndsWith(Environment.NewLine, envelope.Output);
        Assert.DoesNotContain("\u001b", envelope.Output, StringComparison.Ordinal);
        if (compact)
            Assert.DoesNotContain('\n', envelope.Output.TrimEnd('\r', '\n'));
    }

    [Theory]
    [InlineData("--json")]
    [InlineData("--envelope")]
    public async Task SameLibrary_ProducesAvailableEmptyDocument(string format)
    {
        string path = FixtureCatalog.LibraryApiDiffV1.AssemblyPath();
        var result = await Invoke("--library", $"{path}..{path}", format);

        Assert.Equal(0, result.Exit);
        Assert.Empty(result.Error);
        using JsonDocument json = JsonDocument.Parse(result.Output);
        JsonElement content = Content(json.RootElement, format);
        Assert.Equal("available", content.GetProperty("outcome").GetString());
        Assert.Equal(0, content.GetProperty("document").GetProperty("summary")
            .GetProperty("changedTypeCount").GetInt32());
        Assert.Empty(content.GetProperty("document").GetProperty("comparison")
            .GetProperty("subjects").EnumerateArray());
    }

    [Theory]
    [InlineData("--json")]
    [InlineData("--envelope")]
    public async Task LogicalMismatch_PreservesRejectedOutcomeAndFailureExit(string format)
    {
        string before = FixtureCatalog.LibraryApiDiffV1.AssemblyPath();
        string after = FixtureCatalog.DiffV1.AssemblyPath();
        var result = await Invoke("--library", $"{before}..{after}", format);

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Error);
        using JsonDocument json = JsonDocument.Parse(result.Output);
        JsonElement content = Content(json.RootElement, format);
        Assert.Equal("rejected", content.GetProperty("outcome").GetString());
        Assert.Equal((int)LibraryApiDiffRejectionKind.LogicalLibraryMismatch,
            content.GetProperty("kind").GetInt32());
        Assert.True(content.GetProperty("before").GetProperty("isComplete").GetBoolean());
        Assert.True(content.GetProperty("after").GetProperty("isComplete").GetBoolean());
        Assert.False(content.TryGetProperty("document", out _));
    }

    [Theory]
    [InlineData("--json", null)]
    [InlineData("--markdown", null)]
    [InlineData("--table", null)]
    [InlineData("--tsv", null)]
    [InlineData("--jsonl", null)]
    [InlineData("--tree", null)]
    [InlineData("--name-only", null)]
    [InlineData("--no-headers", null)]
    [InlineData("--type", "Widget")]
    [InlineData("--member", "Method")]
    [InlineData("--breaking", null)]
    [InlineData("--additive", null)]
    [InlineData("--changed", null)]
    [InlineData("--alloc-regressions", null)]
    [InlineData("--pdb-source", null)]
    [InlineData("--repo", ".")]
    [InlineData("--finding", "api.type")]
    [InlineData("--legend", null)]
    [InlineData("-S", "Changes")]
    [InlineData("-v", "q")]
    [InlineData("--rows", "1..1")]
    [InlineData("-n", "1")]
    [InlineData("--lines", "1")]
    [InlineData("--tail-lines", "1")]
    [InlineData("--discover", null)]
    [InlineData("--envelope=false", null)]
    [InlineData("--envelope:true", null)]
    public async Task Envelope_RejectsUnsupportedModifiersBeforeResolution(
        string option, string? value)
    {
        var result = await Invoke([
            "--library", "missing-before.dll..missing-after.dll",
            "--envelope", option, .. value is null ? Array.Empty<string>() : [value]]);

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains("--envelope", result.Error);
        Assert.DoesNotContain("Error resolving", result.Error);
    }

    [Fact]
    public async Task Envelope_RejectsPositionalTypeProjectionBeforeResolution()
    {
        var result = await Invoke(
            "--library", "missing-before.dll..missing-after.dll",
            "Widget", "--envelope");

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains("positional type filters", result.Error);
    }

    [Fact]
    public async Task AcquisitionFailure_DoesNotFabricateEnvelope()
    {
        var result = await Invoke(
            "--library", "missing-before.dll..missing-after.dll", "--envelope");

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.NotEmpty(result.Error);
    }

    [Theory]
    [InlineData("json")]
    [InlineData("table")]
    public async Task Envelope_IgnoresImplicitRenderingFormat(string format)
    {
        string? previous = Environment.GetEnvironmentVariable("DOTNET_INSPECT_FORMAT");
        try
        {
            Environment.SetEnvironmentVariable("DOTNET_INSPECT_FORMAT", format);
            var result = await Run("--envelope");
            Assert.Equal(0, result.Exit);
            using JsonDocument json = JsonDocument.Parse(result.Output);
            Assert.Equal("library-api-diff", json.RootElement.GetProperty("result_kind").GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_INSPECT_FORMAT", previous);
        }
    }

    [Theory]
    [InlineData("--json", "--head")]
    [InlineData("--json", "--tail-lines")]
    [InlineData("--envelope", "--head")]
    [InlineData("--envelope", "--tail")]
    public async Task ServiceJson_RejectsRenderedLineClipping(string format, string direction)
    {
        string[] lineUnit =
            format == "--json" && direction == "--head"
                ? ["--lines"]
                : [];
        var result = await Run([
            format,
            "-n",
            "2",
            direction,
            .. lineUnit,
        ]);
        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            format == "--json" ? "JSON" : format,
            result.Error,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Envelope_AcceptsServiceApiScope()
    {
        var result = await Run("--envelope", "--all");
        Assert.Equal(0, result.Exit);
        using JsonDocument json = JsonDocument.Parse(result.Output);
        JsonElement before = json.RootElement.GetProperty("content")
            .GetProperty("document").GetProperty("before");
        Assert.Equal((int)ApiSurfaceScope.IncludeAll, before.GetProperty("scope").GetInt32());
    }

    [Fact]
    public async Task Compact_RequiresJsonOutput()
    {
        var result = await Run("--compact");
        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains("--compact", result.Error);
    }

    [Theory]
    [InlineData("--type", "Widget")]
    [InlineData("-S", "Changes")]
    [InlineData("--member", "Method")]
    [InlineData("--discover", null)]
    public async Task Compact_RejectsProjectedOrOtherOperationsBeforeResolution(
        string option, string? value)
    {
        var result = await Invoke([
            "--library", "missing-before.dll..missing-after.dll",
            "--json", "--compact", option,
            .. value is null ? Array.Empty<string>() : [value]]);

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains("--compact", result.Error);
        Assert.DoesNotContain("Error resolving", result.Error);
    }

    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData("--envelope")]
    [InlineData("--compact")]
    public async Task PlatformReferencePackage_RejectsResolvedMultiLibraryEndpoints(string option)
    {
        var result = await Invoke([
            "--package", "Microsoft.NETCore.App.Ref@9.0.0..10.0.0", option,
            .. option == "--compact" ? new[] { "--json" } : Array.Empty<string>()]);

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(option, result.Error);
        Assert.True(
            result.Error.Contains("exactly one Library at each API diff endpoint", StringComparison.Ordinal),
            result.Error);
        Assert.Contains("resolved 164 before and 167 after", result.Error);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task SystemTextJson_PairedTransportPreservesRealApiAddition()
    {
        string[] source = ["--package", "System.Text.Json@9.0.0..10.0.0", "--tfm", "net8.0"];
        var content = await Invoke([.. source, "--json", "--compact"]);
        var envelope = await Invoke([.. source, "--envelope", "--compact"]);

        Assert.True(content.Exit == 0, content.Error);
        Assert.True(envelope.Exit == 0, envelope.Error);
        using JsonDocument contentJson = JsonDocument.Parse(content.Output);
        using JsonDocument envelopeJson = JsonDocument.Parse(envelope.Output);
        Assert.True(JsonElement.DeepEquals(
            contentJson.RootElement, envelopeJson.RootElement.GetProperty("content")));
        Assert.Contains("AllowDuplicateProperties", content.Output);
        Assert.Contains("JsonSerializerOptions", content.Output);
    }

    static JsonElement Content(JsonElement root, string format) =>
        format == "--envelope" ? root.GetProperty("content") : root;

    static Task<(int Exit, string Output, string Error)> Run(params string[] options) =>
        Invoke([
            "--library",
            $"{FixtureCatalog.LibraryApiDiffV1.AssemblyPath()}..{FixtureCatalog.LibraryApiDiffV2.AssemblyPath()}",
            .. options]);

    static Task<(int Exit, string Output, string Error)> Invoke(params string[] arguments) =>
        ConsoleCapture.RunAsync(() =>
        {
            string[] normalized = CommandLineBuilder.PreprocessArgs(
                ["diff", .. arguments, "--tips", "q"]);
            return CommandLineBuilder.InvokeWithLineWindowAsync(
                CommandLineBuilder.CreateRootCommand().Parse(normalized), normalized);
        });
}
