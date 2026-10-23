using System.Text;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using NuGetFetch;

namespace DotnetInspect.Cli.Tests;

// PR-fast: bounded routing, registration-only restoration and pinned package-store inputs.
[Collection("Console")]
public sealed partial class WorkspacePacketActivationTests
{
    const string RuntimeJson = """{"f":3,"t":[],"g":[],"r":[["e","ecosystem.runtime",["System"],[],[["t","DotNetRuntime"],["p","System."]]]],"a":null,"x":null,"v":[{"t":null,"u":{"k":"workspace"}}]}""";
    const string TypeJson = """{"f":5,"s":[["https://api.nuget.org/v3/index.json"]],"t":[["System.Text.Json","10.0.0","net10.0",null]],"g":[[0]],"r":[],"a":0,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0,"r":{"k":"type","l":["System.Text.Json","10.0.0.0",null,"cc7b13ffcd2ddd51"],"y":"System.Text.Json.JsonSerializer"},"u":{"k":"type"},"f":"type.api"}]}""";

    [Theory]
    [InlineData("--json")]
    [InlineData("--markdown")]
    [InlineData("--plaintext")]
    [InlineData("--table")]
    [InlineData("--tsv")]
    [InlineData("--jsonl")]
    [InlineData("--count")]
    public async Task BothEntrancesReturnOrdinaryInventory(string terminal)
    {
        string packet = Encode(RuntimeJson);
        var ordinary = await RunAsync(["workspace", "--packet", packet, "--inventory", terminal]);
        var explicitInput = await RunAsync(["-w", packet, terminal]);
        var raw = await RunAsync([packet, terminal]);
        Assert.Equal(0, ordinary.ExitCode);
        Assert.Equal(0, explicitInput.ExitCode);
        Assert.Equal(0, raw.ExitCode);
        Assert.Equal(ordinary.Output, explicitInput.Output);
        Assert.Equal(ordinary.Output, raw.Output);
        Assert.Empty(raw.Error);
        Assert.DoesNotContain(packet, raw.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--rows", "1..1")]
    [InlineData("-n", "1")]
    [InlineData("--tail", "-n", "1")]
    public async Task InventoryUsesOrdinarySemanticRowSelection(params string[] selection)
    {
        string packet = Encode(RuntimeJson);
        var ordinary = await RunAsync(["workspace", "--packet", packet, "--inventory", "--json", .. selection]);
        var actual = await RunAsync(["-w", packet, "--json", .. selection]);
        Assert.True(actual.ExitCode == 0, actual.Error);
        Assert.Equal(ordinary.Output, actual.Output);
    }

    [Fact]
    public async Task RootOptionAcceptsAttachedAndPrefixPresentation()
    {
        string packet = Encode(RuntimeJson);
        var separated = await RunAsync(["-w", packet, "--count"]);
        var attached = await RunAsync(["--count", "-w=" + packet]);
        Assert.Equal(0, attached.ExitCode);
        Assert.Equal(separated.Output, attached.Output);
    }

    [Theory]
    [InlineData("-w")]
    [InlineData("raw")]
    public async Task HelpDoesNotRestoreOrAcquire(string entrance)
    {
        string packet = Encode(TypeJson);
        var actual = await RunAsync([.. entrance == "-w" ? new[] { "-w", packet } : [packet], "--help"]);
        Assert.Equal(0, actual.ExitCode);
        Assert.Contains("dotnet-inspect -w <packet>", actual.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(WorkspaceActivationCommandDefinitions.CommandName, actual.Output, StringComparison.Ordinal);
        Assert.Empty(actual.Error);
    }

    [Theory]
    [InlineData("--package", "Polly")]
    [InlineData("--source", "https://example.test/index.json")]
    [InlineData("--workspace", "packet")]
    [InlineData("--tfm", "net8.0")]
    [InlineData("-m", "Serialize")]
    [InlineData("-S", "API")]
    [InlineData("--share", "packet")]
    [InlineData("type", "Foo")]
    [InlineData("second-target")]
    public async Task NewSourcesSubjectsQueriesAndSecondTargetsAreRefused(params string[] extra)
    {
        string packet = Encode(RuntimeJson);
        foreach (string[] entrance in new[] { new[] { "-w", packet }, new[] { packet } })
        {
            var actual = await RunAsync([.. entrance, .. extra]);
            Assert.Equal(1, actual.ExitCode);
            Assert.Empty(actual.Output);
            Assert.NotEmpty(actual.Error);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-packet")]
    [InlineData("https://dotnet-inspect.net/?w=packet")]
    [InlineData("packet.json")]
    [InlineData("{\"f\":3}")]
    public async Task ExplicitInputNeverFallsBack(string input)
    {
        var actual = await RunAsync(["-w", input]);
        Assert.Equal(1, actual.ExitCode);
        Assert.Empty(actual.Output);
        Assert.DoesNotContain("NuGet", actual.Error, StringComparison.Ordinal);
        Assert.NotEmpty(actual.Error);
    }

    [Theory]
    [InlineData("{\"f\":6}")]
    [InlineData("{\"f\":3}")]
    [InlineData("{\"f\":")]
    public async Task RecognizedFailuresNeverReachOrdinaryTargetRouting(string json)
    {
        string packet = Encode(json);
        foreach (string[] entrance in new[] { new[] { "-w", packet }, new[] { packet } })
        {
            var actual = await RunAsync(entrance);
            Assert.Equal(1, actual.ExitCode);
            Assert.Empty(actual.Output);
            Assert.DoesNotContain("NuGet", actual.Error, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ExplicitOverLimitPacketIsDiagnosedWithoutFallback()
    {
        var actual = await RunAsync(["-w", new string('a', 100_000)]);
        Assert.Equal(1, actual.ExitCode);
        Assert.Empty(actual.Output);
        Assert.Contains("limit", actual.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Zm9v")]
    [InlineData("System.Text.Json@10.0.0")]
    [InlineData("Newtonsoft.Json")]
    public void NonPacketsRetainOrdinaryRouter(string target)
    {
        Assert.Equal(new[] { "router", target, "--offline" },
            CommandLineBuilder.PreprocessArgs([target, "--offline"]));
    }

    [Fact]
    public async Task ExplicitCommandKeepsPacketOptionValueOwnership()
    {
        string packet = Encode(RuntimeJson);
        var actual = await RunAsync(["workspace", "--packet", packet]);
        Assert.Equal(0, actual.ExitCode);
        Assert.Equal(packet, actual.Output.Trim());
        Assert.Equal(new[] { "package", packet }, CommandLineBuilder.PreprocessArgs(["package", packet]));
        var conflict = await RunAsync(["-w", packet, "package", "System.Text.Json"]);
        Assert.Equal(1, conflict.ExitCode);
        Assert.Empty(conflict.Output);
    }

    [Theory]
    [InlineData("{\"f\":3,\"t\":[],\"g\":[],\"r\":[],\"a\":null,\"x\":null,\"q\":[[\"package-query/v1\",{\"t\":[[\"prefix\",\"eq\",\"Microsoft.Extensions.\"],[\"prerelease\",\"eq\",\"stable\"]],\"b\":[[\"candidates\",10]]}]],\"v\":[{\"t\":null,\"u\":{\"k\":\"workspace\"},\"q\":[0]}]}", "saved query")]
    [InlineData("{\"f\":1,\"t\":[[\"System.Text.Json\",\"10.0.0\",\"net10.0\",null]],\"g\":[[0]],\"a\":0,\"x\":0}", "format 1")]
    public async Task UnsupportedQueriesAndLegacyActivationNeverFallback(string json, string diagnostic)
    {
        foreach (string[] entrance in new[] { new[] { "-w", Encode(json) }, new[] { Encode(json) } })
        {
            var actual = await RunAsync(entrance);
            Assert.Equal(1, actual.ExitCode);
            Assert.Empty(actual.Output);
            Assert.Contains(diagnostic, actual.Error, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task UnsupportedTypeFacetIsRefusedBeforeAnyAcquisition()
    {
        using var client = new HttpClient(new FailingHandler());
        var actual = await ConsoleCapture.RunAsync(() => WorkspacePacketActivation.ExecuteAsync(
            new WorkspaceOptions { Packet = Encode(TypeJson.Replace("type.api", "type.metadata", StringComparison.Ordinal)), Format = OutputFormat.Json },
            TestContext.Current.CancellationToken, LoadOptions(client, new InMemoryPackageStore())));
        Assert.Equal(1, actual.ExitCode);
        Assert.Empty(actual.Output);
        Assert.Contains("type.metadata", actual.Error, StringComparison.Ordinal);
        Assert.Contains("no CLI activation execution binding", actual.Error, StringComparison.Ordinal);
        foreach (string[] entrance in new[] { new[] { "-w", Encode(TypeJson.Replace("type.api", "type.metadata", StringComparison.Ordinal)) }, new[] { Encode(TypeJson.Replace("type.api", "type.metadata", StringComparison.Ordinal)) } })
        {
            var routed = await RunAsync(entrance);
            Assert.Equal(actual.Error, routed.Error);
            Assert.Empty(routed.Output);
        }
    }

    [Theory]
    [InlineData(OutputFormat.Json)]
    [InlineData(OutputFormat.Markdown)]
    [InlineData(OutputFormat.Jsonl)]
    public async Task InventoryIncludesAllContextsWithoutChangingFocusedLanding(OutputFormat format)
    {
        string packet = MemberWorkspaceRouteTests.Packet();
        using var client = new HttpClient(new FailingHandler());
        var store = new InMemoryPackageStore();
        await using Stream archive = File.OpenRead(Path.Combine(AppContext.BaseDirectory,
            "RealAssets", "PackageEndpointScope", "system.text.json.10.0.0.nupkg"));
        await store.CommitAsync("System.Text.Json", "10.0.0", NuGetCache.GetSourceKey(SourceUrl),
            archive, TestContext.Current.CancellationToken);
        WorkspaceContextLoadOptions load = LoadOptions(client, store);
        var options = new WorkspaceOptions { Packet = packet, Inventory = true, Format = format, Verbose = true };
        var ordinary = await ConsoleCapture.RunAsync(() => WorkspaceCommand.ExecuteAsync(options, load, TestContext.Current.CancellationToken));
        var actual = await ConsoleCapture.RunAsync(() => WorkspacePacketActivation.ExecuteAsync(
            options, TestContext.Current.CancellationToken, load));
        Assert.True(actual.ExitCode == 0, actual.Error);
        Assert.Equal(ordinary.Output, actual.Output);
        Assert.Contains("net10.0", actual.Output, StringComparison.Ordinal);
        Assert.Contains("net8.0", actual.Output, StringComparison.Ordinal);
        Assert.Equal(packet, WorkspaceSharePacketCodec.Encode(WorkspaceSharePacketCodec.Decode(packet, TestContext.Current.CancellationToken)));
    }

    const string SourceUrl = "https://example.test/v3/index.json";

    static WorkspaceContextLoadOptions LoadOptions(HttpClient client, IPackageStore store) => new()
    {
        HttpClient = client,
        SourceAuthorization = new UniformPackageSourceAuthorization([new PackageSource("test", SourceUrl)]),
        PackageStore = store,
    };

    static string Encode(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    static Task<(int ExitCode, string Output, string Error)> RunAsync(string[] args)
    {
        ArgumentPreprocessor.Reset();
        var root = CommandLineBuilder.CreateRootCommand();
        string[] processed = CommandLineBuilder.PreprocessArgs(args, root);
        return ConsoleCapture.RunAsync(() => CommandLineBuilder.InvokeWithLineWindowAsync(root.Parse(processed), processed));
    }

    sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => throw new InvalidOperationException("Unexpected acquisition: " + request.RequestUri);
    }
}
