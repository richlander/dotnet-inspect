using System.Text.Json;
using DotnetInspect.Cli.Commands;
using DotnetInspector.Queries.Definitions;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Tests;

public sealed partial class ConfiguredPayloadAcquisitionTests
{
    private const string PublicDependencyFeed = "https://api.nuget.org/v3/index.json";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Depends_Envelope_PreservesCompleteContentAndSelectedRows(bool compact)
    {
        ConfigureAuthenticDependencyFeed();
        string[] modifiers = ["--rows", "2..2", .. compact ? new[] { "--compact" } : []];
        var content = await RunEnvelopeCommandAsync(
            [.. AuthenticDependencyArguments(true), .. modifiers]);
        var envelope = await RunEnvelopeCommandAsync(
            [.. AuthenticDependencyArguments(true, envelope: true), .. modifiers]);

        Assert.True(content.Exit == 0, content.Error);
        Assert.True(envelope.Exit == 0, envelope.Error);
        Assert.Empty(content.Error);
        Assert.Empty(envelope.Error);
        using JsonDocument contentDocument = JsonDocument.Parse(content.Output);
        using JsonDocument envelopeDocument = JsonDocument.Parse(envelope.Output);
        JsonElement root = envelopeDocument.RootElement;
        Assert.Equal(2, root.GetProperty("schema_version").GetInt32());
        Assert.Equal("type-dependencies", root.GetProperty("result_kind").GetString());
        Assert.Equal("document", root.GetProperty("content_kind").GetString());
        Assert.True(JsonElement.DeepEquals(contentDocument.RootElement, root.GetProperty("content")));
        Assert.False(root.TryGetProperty("evidence", out _));
        Assert.Equal("nonProjectable", root.GetProperty("portable_projection").GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("portable_projection").GetProperty("full_url").ValueKind);
        Assert.Empty(root.GetProperty("diagnostics").EnumerateArray());

        JsonElement body = root.GetProperty("content");
        Assert.Equal(2, body.GetProperty("queryResult").GetProperty("dependency")
            .GetProperty("relationships").GetArrayLength());
        JsonElement selected = Assert.Single(
            body.GetProperty("rowSelection").GetProperty("relationships").EnumerateArray());
        Assert.Equal(RelationalOptions, selected.GetProperty("sourceTypeName").GetString());
        Assert.Equal(OptionsInterface, selected.GetProperty("targetTypeName").GetString());
        JsonElement participant = Assert.Single(
            body.GetProperty("queryResult").GetProperty("participants").EnumerateArray(),
            entry => entry.GetProperty("subject").GetProperty("identity").GetProperty("name")
                .GetString() == NpgsqlPackage);
        Assert.Equal("completed", participant.GetProperty("kind").GetString());
        JsonElement subject = participant.GetProperty("subject");
        Assert.False(subject.TryGetProperty("registration", out _));
        JsonElement provenance = subject.GetProperty("provenance");
        Assert.Equal("package", provenance.GetProperty("kind").GetString());
        Assert.Equal(NpgsqlPackage, provenance.GetProperty("packageId").GetString(), ignoreCase: true);
        Assert.Equal(AuthenticDependencyVersion, provenance.GetProperty("packageVersion").GetString());
        Assert.True(provenance.TryGetProperty("tfm", out _));
        Assert.True(provenance.TryGetProperty("rid", out _));
        Assert.EndsWith(Environment.NewLine, envelope.Output, StringComparison.Ordinal);
        Assert.DoesNotContain('\u001b', envelope.Output);
        if (compact)
            Assert.DoesNotContain('\n', envelope.Output.TrimEnd('\r', '\n'));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Depends_Envelope_ShareIsIndependentOfStderrSelection(bool missingType)
    {
        ConfigureAuthenticDependencyFeed(source: PublicDependencyFeed);
        string[] arguments = AuthenticDependencyArguments(false, true, PublicDependencyFeed);
        if (missingType)
            arguments[1] = "No.Such.Type";
        var baseline = await RunEnvelopeCommandAsync(arguments);
        var shared = await RunEnvelopeCommandAsync([.. arguments, "--share", "packet"]);

        Assert.True(baseline.Exit == (missingType ? 1 : 0), baseline.Error);
        Assert.Equal(baseline.Exit, shared.Exit);
        Assert.True(baseline.Output.Length > 0, baseline.Error);
        Assert.True(shared.Output.Length > 0, shared.Error);
        using JsonDocument baselineDocument = JsonDocument.Parse(baseline.Output);
        using JsonDocument sharedDocument = JsonDocument.Parse(shared.Output);
        Assert.True(JsonElement.DeepEquals(baselineDocument.RootElement, sharedDocument.RootElement));
        JsonElement share = sharedDocument.RootElement.GetProperty("portable_projection");
        Assert.Equal("available", share.GetProperty("kind").GetString());
        string encoded = Assert.IsType<string>(share.GetProperty("packet").GetString());
        Assert.Equal("https://dotnet-inspect.net/?w=" + encoded,
            share.GetProperty("full_url").GetString());
        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.Decode(
            encoded, TestContext.Current.CancellationToken);
        Assert.Equal(NpgsqlPackage, Assert.Single(packet.Tabs).Source, ignoreCase: true);
        Assert.Equal(AuthenticDependencyVersion, Assert.Single(packet.Tabs).Version);
        Assert.Equal(missingType ? "No.Such.Type" : NpgsqlOptions, packet.Type);
        Assert.Equal(encoded,
            shared.Error.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Last());
        Assert.DoesNotContain(encoded, baseline.Error);
        if (missingType)
            Assert.Contains("not found in the specified scope", shared.Error);
        else
            Assert.Empty(baseline.Error);
    }

    [Theory]
    [InlineData("--rows", "2..2")]
    [InlineData("-n", "1")]
    [InlineData("--head", "1")]
    [InlineData("--tail", "1")]
    public async Task Depends_Envelope_ItemWindowsDoNotClipJson(string option, string value)
    {
        ConfigureAuthenticDependencyFeed();
        string[] window = option is "--head" or "--tail"
            ? ["-n", value, option]
            : [option, value];
        var result = await RunEnvelopeCommandAsync(
            [.. AuthenticDependencyArguments(true, envelope: true), .. window]);
        Assert.True(result.Exit == 0, result.Error);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        Assert.Single(document.RootElement.GetProperty("content").GetProperty("rowSelection")
            .GetProperty("relationships").EnumerateArray());
        Assert.True(result.Output.Count(character => character == '\n') > 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Depends_Envelope_DepthRetainsContentAndRefusesUnrepresentableShare(bool requestShare)
    {
        ConfigureAuthenticDependencyFeed(source: PublicDependencyFeed);
        var result = await RunEnvelopeCommandAsync(
            [.. AuthenticDependencyArguments(false, true, PublicDependencyFeed),
                "--depth", "1", .. requestShare ? new[] { "--share", "url" } : []]);
        Assert.True(result.Exit == (requestShare ? 1 : 0), result.Error);
        Assert.True(result.Output.Length > 0, result.Error);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        Assert.True(document.RootElement.GetProperty("content").GetProperty("queryResult")
            .GetProperty("dependency").GetProperty("found").GetBoolean());
        JsonElement share = document.RootElement.GetProperty("portable_projection");
        Assert.Equal("nonProjectable", share.GetProperty("kind").GetString());
        Assert.Equal("notSupported", share.GetProperty("reason").GetString());
        Assert.Contains(
            "--depth",
            share.GetProperty("explanation").GetString());
        if (requestShare)
            Assert.Contains("--share is not projectable", result.Error);
        else
            Assert.Empty(result.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Depends_Envelope_QueryRetainsContentAndRefusesUnrepresentableShare(
        bool requestShare)
    {
        string[][] queries =
        [
            ["--where", "Kind=Interface"],
            ["--order-by", "Target desc"],
            ["--order-by", "Target desc", "--top", "1"],
        ];

        foreach (string[] query in queries)
        {
            ConfigureAuthenticDependencyFeed(source: PublicDependencyFeed);
            var result = await RunEnvelopeCommandAsync(
                [
                    .. AuthenticDependencyArguments(
                        false,
                        true,
                        PublicDependencyFeed),
                    .. query,
                    .. requestShare ? new[] { "--share", "url" } : [],
                ]);

            Assert.True(
                result.Exit == (requestShare ? 1 : 0),
                result.Error);
            Assert.True(result.Output.Length > 0, result.Error);
            using JsonDocument document = JsonDocument.Parse(result.Output);
            Assert.True(
                document.RootElement.GetProperty("content")
                    .GetProperty("queryResult")
                    .GetProperty("dependency")
                    .GetProperty("found")
                    .GetBoolean());
            JsonElement share =
                document.RootElement.GetProperty("portable_projection");
            Assert.Equal(
                "nonProjectable",
                share.GetProperty("kind").GetString());
            Assert.Equal(
                "notSupported",
                share.GetProperty("reason").GetString());
            Assert.Contains(
                "query predicates, ordering, or --top",
                share.GetProperty("explanation").GetString());
            if (requestShare)
            {
                Assert.Contains(
                    "--share is not projectable",
                    result.Error);
            }
            else
            {
                Assert.Empty(result.Error);
            }
        }
    }

    [Theory]
    [InlineData("--json", null)]
    [InlineData("--markdown", null)]
    [InlineData("--plaintext", null)]
    [InlineData("--table", null)]
    [InlineData("--tsv", null)]
    [InlineData("--jsonl", null)]
    [InlineData("--tree", null)]
    [InlineData("--mermaid", null)]
    [InlineData("--no-headers", null)]
    [InlineData("--count", null)]
    [InlineData("--fields", "Source")]
    [InlineData("--columns", "Source")]
    [InlineData("-S", "Dependency Graph")]
    [InlineData("-v", "q")]
    [InlineData("--discover", null)]
    [InlineData("--schema", null)]
    [InlineData("--effective", null)]
    [InlineData("--lines", "2")]
    [InlineData("--tail-lines", "2")]
    [InlineData("--envelope=false", null)]
    [InlineData("--envelope:true", null)]
    [InlineData("--envelope", "true")]
    [InlineData("--evidence-envelope", null)]
    public async Task Depends_Envelope_RejectsIncompatibleOptionsBeforeAcquisition(
        string option, string? value)
    {
        int requests = 0;
        ConfigureAuthenticDependencyFeed(() => requests++);
        var result = await RunEnvelopeCommandAsync(
            [.. AuthenticDependencyArguments(false, envelope: true),
                option, .. value is null ? Array.Empty<string>() : [value]]);
        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(option == "--discover" ? "-D" : option.Split('=', ':')[0], result.Error);
        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task Depends_Envelope_RejectsAssetModeBeforeAcquisition()
    {
        int requests = 0;
        ConfigureAuthenticDependencyFeed(() => requests++);
        var result = await RunEnvelopeCommandAsync(
            ["depends", "--package", $"{NpgsqlPackage}@{AuthenticDependencyVersion}",
                "--source", FirstFeed, "--envelope", "--tips", "q"]);
        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains("positional type", result.Error);
        Assert.Equal(0, requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Depends_Envelope_SerializesSuccessfulEmptyContent(bool envelope)
    {
        var result = await RunEnvelopeCommandAsync(
            ["depends", "System.Object", "--library", typeof(object).Assembly.Location,
                envelope ? "--envelope" : "--json", "--tips", "q"]);
        Assert.True(result.Exit == 0, result.Error);
        Assert.Empty(result.Error);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        JsonElement content = envelope ? document.RootElement.GetProperty("content") : document.RootElement;
        Assert.True(content.GetProperty("queryResult").GetProperty("dependency").GetProperty("found").GetBoolean());
        Assert.Empty(content.GetProperty("rowSelection").GetProperty("relationships").EnumerateArray());
    }

    [Fact]
    public async Task Depends_Envelope_SerializesRowFailureWithItsDiagnostic()
    {
        ConfigureAuthenticDependencyFeed();
        var result = await RunEnvelopeCommandAsync(
            [.. AuthenticDependencyArguments(false, envelope: true), "--rows", "9999..9999"]);
        Assert.Equal(1, result.Exit);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        Assert.False(document.RootElement.GetProperty("content").GetProperty("rowSelection")
            .GetProperty("isSuccess").GetBoolean());
        Assert.Equal(JsonValueKind.Object, document.RootElement.GetProperty("content")
            .GetProperty("rowSelection").GetProperty("failure").ValueKind);
        JsonElement diagnostic = Assert.Single(document.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("Error", diagnostic.GetProperty("severity").GetString());
        Assert.Contains("row selection", result.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Depends_Envelope_RetainsRejectedParticipantDiagnostics(bool includeValidAssembly)
    {
        string rejected = Path.Combine(_root, "unsupported.dll");
        File.WriteAllBytes(rejected, MetadataTestImages.BuildWindowsMetadataImage());
        var result = await RunEnvelopeCommandAsync(
            ["depends", typeof(AssemblyReferenceIdentity).FullName!,
                "--library", rejected,
                .. includeValidAssembly
                    ? new[] { "--library", typeof(AssemblyReferenceIdentity).Assembly.Location }
                    : [],
                "--envelope", "--tips", "q"]);
        Assert.Equal(includeValidAssembly ? DependsCommand.UncertifiedScanExitCode : 1, result.Exit);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        Assert.Equal(includeValidAssembly,
            document.RootElement.GetProperty("content").GetProperty("queryResult")
                .GetProperty("dependency").GetProperty("found").GetBoolean());
        Assert.NotEmpty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.Contains("Excluded", result.Error);
    }

    [Fact]
    public async Task Depends_Envelope_DoesNotManufactureAnAcquisitionResult()
    {
        DotnetInspector.Networking.HttpClientFactory.SetAuthenticationDecorator(
            _ => new MissingDependencyFeedHandler());
        DotnetInspector.Networking.HttpClientFactory.ResetSharedForTesting();
        var result = await RunEnvelopeCommandAsync(
            AuthenticDependencyArguments(false, envelope: true));
        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.NotEmpty(result.Error);
    }

    private sealed class MissingDependencyFeedHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)
            {
                RequestMessage = request,
            });
    }

    private static Task<(int Exit, string Output, string Error)> RunEnvelopeCommandAsync(string[] arguments) =>
        ConsoleCapture.RunAsync(() =>
        {
            var root = CommandLineBuilder.CreateRootCommand();
            string[] normalized = CommandLineBuilder.PreprocessArgs(arguments, root);
            return CommandLineBuilder.InvokeWithLineWindowAsync(root.Parse(normalized), normalized);
        });
}
