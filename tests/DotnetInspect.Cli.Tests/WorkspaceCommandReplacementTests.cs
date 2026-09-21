using System.Net;
using System.Text.Json;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using NuGetFetch;

namespace DotnetInspect.Cli.Tests;

public sealed partial class WorkspaceCommandTests
{
    const string AvaloniaComponent =
        "packages/avalonia@11.3.14/net8.0/~";

    static readonly PackageSource ReplacementSource =
        new("nuget.org", "https://api.nuget.org/v3/index.json");

    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData("type", "type.metadata")]
    [InlineData("member", "member.overview")]
    public async Task Replacement_EnvelopeFollowsAvaloniaForwarder(
        string subject, string facet)
    {
        InMemoryPackageStore store = await ReplacementStoreAsync();
        using var handler = new ReplacementHandler();
        using var client = new HttpClient(handler);
        string packet = ReplacementPacket(subject, facet);
        var result = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(new WorkspaceOptions
            {
                Packet = packet,
                UpdatePackage = Component(AvaloniaComponent),
                ReplacementVersion = "12.1.2",
                Format = OutputFormat.Json,
                EnvelopeOutput = true,
            }, ReplacementLoad(client, store), TestContext.Current.CancellationToken));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Equal(0, handler.Requests);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        JsonElement envelope = document.RootElement;
        Assert.Equal(
            "workspace-package-update",
            envelope.GetProperty("result_kind").GetString());
        Assert.Equal(1, envelope.GetProperty("schema_version").GetInt32());
        JsonElement content = envelope.GetProperty("content");
        Assert.True(content.GetProperty("succeeded").GetBoolean());
        Assert.Equal(
            AvaloniaComponent,
            content.GetProperty("component").GetString());
        Assert.Equal("Committed", content.GetProperty("scope").GetProperty("kind").GetString());
        Assert.Equal("ExactPath", content.GetProperty("retention").GetProperty("disposition").GetString());
        Assert.Equal(facet, content.GetProperty("inspector").GetProperty("requestedFacet").GetString());
        Assert.Equal(subject, content.GetProperty("activeSubject").GetProperty("kind")
            .GetString()!.ToLowerInvariant());
        string derived = envelope.GetProperty("portable_projection").GetProperty("packet").GetString()!;
        using JsonDocument portable = JsonDocument.Parse(
            WorkspaceSharePacketCodec.SerializeJson(WorkspaceSharePacketCodec.Decode(
                derived, TestContext.Current.CancellationToken)));
        Assert.Equal("12.1.2", portable.RootElement.GetProperty("t")[0][1].GetString());
        JsonElement state = portable.RootElement.GetProperty("v")[1];
        Assert.Equal("Avalonia.Base", state.GetProperty("r").GetProperty("l")[0].GetString());
        Assert.Equal(subject, state.GetProperty("u").GetProperty("k").GetString());
        Assert.Equal(facet, state.GetProperty("f").GetString());
        var restored = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(new WorkspaceOptions
            {
                Packet = derived, Format = OutputFormat.Json,
            }, ReplacementLoad(client, store), TestContext.Current.CancellationToken));
        Assert.Equal(0, restored.ExitCode);
    }

    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData(WorkspaceShareFormat.Packet, "12.1.2", null)]
    [InlineData(WorkspaceShareFormat.Url, "11.3.14", null)]
    [InlineData(WorkspaceShareFormat.Packet, null, "net9.0")]
    public async Task Replacement_ScalarOutputIsComplete(
        WorkspaceShareFormat format, string? version, string? framework)
    {
        InMemoryPackageStore store = await ReplacementStoreAsync();
        using var handler = new ReplacementHandler();
        using var client = new HttpClient(handler);
        string input = ReplacementPacket();
        var result = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(new WorkspaceOptions
            {
                Packet = input, UpdatePackage = Component(AvaloniaComponent),
                ReplacementVersion = version, ReplacementTfm = framework,
                ShareFormat = format,
            }, ReplacementLoad(client, store), TestContext.Current.CancellationToken));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Equal(0, handler.Requests);
        string output = result.Output.Trim();
        if (format == WorkspaceShareFormat.Url)
        {
            Assert.StartsWith("https://dotnet-inspect.net/?w=", output);
            output = new Uri(output).Query["?w=".Length..];
        }
        using JsonDocument portable = JsonDocument.Parse(
            WorkspaceSharePacketCodec.SerializeJson(WorkspaceSharePacketCodec.Decode(
                output, TestContext.Current.CancellationToken)));
        Assert.Equal(version ?? "11.3.14", portable.RootElement.GetProperty("t")[0][1].GetString());
        Assert.Equal(framework ?? "net8.0", portable.RootElement.GetProperty("t")[0][2].GetString());
        if (version == "11.3.14")
            Assert.Equal(input, output);
    }

    [Fact]
    public async Task Replacement_RejectsUrlInput()
    {
        using var client = new HttpClient(new FailingHandler());
        var result = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions
                {
                    Packet = "https://dotnet-inspect.net/?w=packet",
                    UpdatePackage = Component(AvaloniaComponent),
                    ReplacementVersion = "12.1.2",
                    ShareFormat = WorkspaceShareFormat.Packet,
                },
                LoadOptions(client, new FailOnAccessPackageStore()),
                TestContext.Current.CancellationToken));

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "URLs are not supported",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task Replacement_AcquisitionFailureDoesNotEmitInputPacket()
    {
        InMemoryPackageStore store = await ReplacementStoreAsync(includeDestination: false);
        using var handler = new ReplacementHandler();
        using var client = new HttpClient(handler);
        var result = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(new WorkspaceOptions
            {
                Packet = ReplacementPacket(),
                UpdatePackage = Component(AvaloniaComponent),
                ReplacementVersion = "12.1.2", ShareFormat = WorkspaceShareFormat.Packet,
            }, ReplacementLoad(client, store), TestContext.Current.CancellationToken));

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("DestinationAcquisitionFailed", result.Error);
        Assert.True(handler.Requests > 0);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task Replacement_WorkspaceActiveRowEmitsRestorablePacket()
    {
        InMemoryPackageStore store = await ReplacementStoreAsync();
        using var handler = new ReplacementHandler();
        using var client = new HttpClient(handler);
        string input = WorkspaceSharePacketCodec.Encode(
            WorkspaceSharePacketCodec.ParseJson(
                """
                {"f":4,"t":[["Avalonia","11.3.14","net8.0",null]],"g":[[0]],"r":[],"a":0,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0,"u":{"k":"workspace"}}]}
                """, TestContext.Current.CancellationToken));
        var result = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(new WorkspaceOptions
            {
                Packet = input, UpdatePackage = Component(AvaloniaComponent),
                ReplacementVersion = "12.1.2", ShareFormat = WorkspaceShareFormat.Packet,
            }, ReplacementLoad(client, store), TestContext.Current.CancellationToken));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        string derived = result.Output.Trim();
        using JsonDocument portable = JsonDocument.Parse(
            WorkspaceSharePacketCodec.SerializeJson(WorkspaceSharePacketCodec.Decode(
                derived, TestContext.Current.CancellationToken)));
        Assert.Equal("12.1.2", portable.RootElement.GetProperty("t")[0][1].GetString());
        JsonElement state = portable.RootElement.GetProperty("v")[1];
        Assert.Equal("workspace", state.GetProperty("u").GetProperty("k").GetString());
        Assert.False(state.TryGetProperty("r", out _));
        var restored = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(new WorkspaceOptions
            {
                Packet = derived, Format = OutputFormat.Json,
            }, ReplacementLoad(client, store), TestContext.Current.CancellationToken));
        Assert.Equal(0, restored.ExitCode);
        Assert.Equal(0, handler.Requests);
    }

    [Theory]
    [InlineData("", "--version")]
    [InlineData("--version 12.1.2 --envelope", "--json")]
    [InlineData(
        "--version 12.1.2 --envelope --json --share packet",
        "Choose")]
    [InlineData("--version not-a-version", "InvalidDestination")]
    public async Task Replacement_InvalidOptionsRefuseBeforeAcquisition(
        string arguments, string diagnostic)
    {
        var result = await RunCliAsync([
            "workspace", "package", "update", AvaloniaComponent,
            "--packet", ReplacementPacket(),
            .. arguments.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries),
        ]);
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(diagnostic, result.Error);
    }

    [Fact]
    public async Task RemovedReplacementFlagsAreNotAccepted()
    {
        var result = await RunCliAsync([
            "workspace", "--packet", ReplacementPacket(),
            "--replace-package", "1", "--to-version", "12.1.2",
            "--share", "packet"]);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "--replace-package",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PackageUpdate_HonorsExplicitNuGetSourceOptions()
    {
        const string component =
            "packages/microsoft.codeanalysis.csharp@5.9.0/netstandard2.0/~";
        string packet = WorkspaceSharePacketCodec.Encode(
            WorkspaceSharePacketCodec.ParseJson(
                """
                {"f":4,"t":[["Microsoft.CodeAnalysis.CSharp","5.9.0","netstandard2.0",null]],"g":[[0]],"r":[],"a":null,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0}]}
                """,
                TestContext.Current.CancellationToken));
        string missingConfig = Path.Combine(
            Path.GetTempPath(),
            $"missing-nuget-{Guid.NewGuid():N}.config");

        var baseline = await RunCliAsync([
            "workspace",
            "package",
            "update",
            component,
            "--packet",
            packet,
            "--version",
            "5.9.0",
        ]);
        var restricted = await RunCliAsync([
            "workspace",
            "--nugetconfig",
            missingConfig,
            "package",
            "update",
            component,
            "--packet",
            packet,
            "--version",
            "5.9.0",
        ]);

        Assert.Equal(0, baseline.ExitCode);
        Assert.NotEmpty(baseline.Output);
        Assert.Equal(1, restricted.ExitCode);
        Assert.Empty(restricted.Output);
        Assert.Contains(
            "InputRestorationFailed",
            restricted.Error,
            StringComparison.Ordinal);
    }

    static string ReplacementPacket(string subject = "type", string facet = "type.metadata")
    {
        string json = $$$"""
            {"f":4,"t":[["Avalonia","11.3.14","net8.0",null]],"g":[[0]],"r":[],"a":0,"x":0,
             "v":[{"t":null,"u":{"k":"workspace"}},
                  {"t":0,"r":{"k":"member","l":["Avalonia.Markup","11.3.14.0",null,"c8d484a7012f9a8b"],
                   "y":"Avalonia.Data.MultiBinding","s":"M:Avalonia.Data.MultiBinding.#ctor()"},
                   "u":{"k":"{{{subject}}}"},"f":"{{{facet}}}"}]}
            """;
        return WorkspaceSharePacketCodec.Encode(WorkspaceSharePacketCodec.ParseJson(
            json, TestContext.Current.CancellationToken));
    }

    static WorkspacePackageComponentPath Component(string value)
    {
        Assert.True(
            WorkspacePackageComponentPath.TryCreate(
                value,
                out WorkspacePackageComponentPath? component,
                out string? error),
            error);
        return component!;
    }

    static WorkspaceContextLoadOptions ReplacementLoad(HttpClient client, IPackageStore store) => new()
    {
        HttpClient = client,
        SourceAuthorization = new UniformPackageSourceAuthorization([ReplacementSource]),
        PackageStore = store,
    };

    static async Task<InMemoryPackageStore> ReplacementStoreAsync(bool includeDestination = true)
    {
        var store = new InMemoryPackageStore();
        string producer = PackageSourceClientFactory.GetProducerIdentity(ReplacementSource).Key;
        string[] versions = includeDestination ? ["11.3.14", "12.1.2"] : ["11.3.14"];
        foreach (string version in versions)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "RealAssets", "ApiMatching",
                $"avalonia.{version}.nupkg");
            foreach (string key in new[] { producer, NuGetCache.GetSourceKey(ReplacementSource.Url) })
            {
                await using var content = File.OpenRead(path);
                await store.CommitAsync("Avalonia", version, key, content,
                    TestContext.Current.CancellationToken);
            }
        }
        return store;
    }

    sealed class ReplacementHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("Package unavailable."),
            });
        }
    }
}
