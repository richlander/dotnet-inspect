using System.CommandLine;
using System.Text.Json;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Services;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using NuGetFetch;

namespace DotnetInspect.Cli.Tests;

// PR-fast: production parsing and completed inspection of the pinned NuGet archive.
[Collection("Console")]
public sealed class MemberWorkspaceRouteTests
{
    const string SourceUrl = "https://example.test/v3/index.json";
    const string Type = "System.Text.Json.JsonSerializer";

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public async Task ExactMemberPreservesContentAndCompleteEnvironment(int format)
    {
        string input = Packet(format);
        var ordinary = await RunAsync(input, "Serialize:1", share: false);
        var shared = await RunAsync(input, "Serialize:1", share: true);
        Assert.Equal(0, ordinary.ExitCode);
        Assert.True(shared.ExitCode == 0, shared.Error);
        Assert.Equal(NormalizePaths(ordinary.Output), NormalizePaths(shared.Output));
        Assert.Contains("Serialize", shared.Output, StringComparison.Ordinal);
        Assert.Empty(ordinary.Error);
        WorkspaceSharePacket basis = WorkspaceSharePacketCodec.Decode(input,
            TestContext.Current.CancellationToken);
        WorkspaceSharePacket derived = WorkspaceSharePacketCodec.Decode(shared.Error.Trim(),
            TestContext.Current.CancellationToken);
        Assert.Equal(format, derived.FormatVersion);
        Assert.Equal(basis.Tabs, derived.Tabs);
        Assert.Equal(basis.Contexts.Select(context => string.Join(',', context.TabIndexes)),
            derived.Contexts.Select(context => string.Join(',', context.TabIndexes)));
        Assert.Equal(basis.Registrations, derived.Registrations);
        Assert.Equal(0, derived.SelectedContextIndex);
        Assert.Equal(0, derived.FocusedTabIndex); // Input focus is the other context.
        using JsonDocument before = JsonDocument.Parse(WorkspaceSharePacketCodec.SerializeJson(basis));
        using JsonDocument after = JsonDocument.Parse(WorkspaceSharePacketCodec.SerializeJson(derived));
        Assert.True(JsonElement.DeepEquals(before.RootElement.GetProperty("v")[2],
            after.RootElement.GetProperty("v")[2]));
        Assert.IsType<PortableSubjectRequest.Member>(derived.ViewStates[1].Subject);
        var retained = Assert.IsType<PortableRetainedSubjectContext.EscapedMember>(
            derived.ViewStates[1].Context);
        Assert.Matches("^[0-9a-f]{10}$", retained.MemberAnchor);
        Assert.Equal(Type, retained.EscapedTypeIdentity);
        Assert.Equal("System.Text.Json", retained.LibraryIdentity.Name);
        Assert.Equal("member.overview", derived.ViewStates[1].Facet);
        Assert.Equal(input, WorkspaceSharePacketCodec.Encode(basis));
        using var client = new HttpClient(new FailingHandler());
        WorkspacePacketRestorationResult reopened = await WorkspacePacketRestoration.RestoreAsync(
            shared.Error.Trim(), LoadOptions(client, await StoreAsync()),
            TestContext.Current.CancellationToken);
        WorkspacePacketRestoration restoration =
            Assert.IsType<WorkspacePacketRestorationResult.Restored>(reopened).Value;
        await restoration.ExecuteAsync(active =>
        {
            IReadOnlyList<CompleteRestorationResolvedViewState> states =
                active.Activation.Snapshot.Resolved switch
                {
                    CompleteRestorationResolvedState.Version4 version4 => version4.States,
                    CompleteRestorationResolvedState.Version5 version5 => version5.States,
                    _ => throw new InvalidOperationException(),
                };
            Assert.Equal(StructuralSubjectKind.Member,
                states.Single(state => state.NavigationId == "t0").Initialization!.Subject!.Kind);
            return Task.FromResult(0);
        });
    }

    [Fact]
    public async Task GroupShareRefusalPreservesAllRows()
    {
        var ordinary = await RunAsync(Packet(), "Serialize", share: false, ["--json"]);
        var shared = await RunAsync(Packet(), "Serialize", share: true, ["--json"]);
        Assert.Equal(0, ordinary.ExitCode);
        Assert.Equal(1, shared.ExitCode);
        Assert.Equal(NormalizePaths(ordinary.Output), NormalizePaths(shared.Output));
        Assert.Contains("one exact member overload", shared.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(WorkspaceShareUrl.Prefix, shared.Error, StringComparison.Ordinal);
        using JsonDocument rows = JsonDocument.Parse(shared.Output);
        Assert.True(shared.Output.Split("Serialize", StringSplitOptions.None).Length > 3);
    }

    [Theory]
    [InlineData("--all")]
    [InlineData("-S", "Signature")]
    public async Task UnsupportedShareChoicePreservesOrdinaryContent(params string[] extra)
    {
        var ordinary = await RunAsync(Packet(), "Serialize:1", false, extra);
        var shared = await RunAsync(Packet(), "Serialize:1", true, extra);
        Assert.Equal(0, ordinary.ExitCode);
        Assert.Equal(1, shared.ExitCode);
        Assert.Equal(NormalizePaths(ordinary.Output), NormalizePaths(shared.Output));
        Assert.Contains("not projectable", shared.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("NoSuchMember")]
    [InlineData("Serialize:99999")]
    public async Task MissingMemberEmitsNoShare(string member)
    {
        var result = await RunAsync(Packet(), member, true);
        Assert.Equal(1, result.ExitCode);
        Assert.DoesNotContain(WorkspaceShareUrl.Prefix, result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingTypeDoesNotUseFocusedContext()
    {
        var result = await RunAsync(Packet(), "Serialize:1", true,
            type: "Missing.Type");
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Schema3RefusesOnlyShare()
    {
        var ordinary = await RunAsync(Packet(3), "Serialize:1", false);
        var shared = await RunAsync(Packet(3), "Serialize:1", true);
        Assert.Equal(0, ordinary.ExitCode);
        Assert.Equal(1, shared.ExitCode);
        Assert.Equal(NormalizePaths(ordinary.Output), NormalizePaths(shared.Output));
        Assert.Contains("schema", shared.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RegistrationOnlyPacketReportsMissingSelectedContext()
    {
        string packet = WorkspaceSharePacketCodec.Encode(WorkspaceSharePacketCodec.ParseJson(
            """{"f":4,"t":[],"g":[],"r":[["p","Polly."]],"a":null,"x":null,"v":[{"t":null,"u":{"k":"workspace"}}]}""",
            TestContext.Current.CancellationToken));
        var result = await RunAsync(packet, "Serialize:1", true);
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("selected context", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("https://dotnet-inspect.ca/?w=invalid")]
    public async Task InvalidPacketFailsVisibly(string packet)
    {
        var result = await RunAsync(packet, "Serialize:1", true);
        Assert.Equal(1, result.ExitCode);
        Assert.NotEmpty(result.Error);
        Assert.Empty(result.Output);
    }

    [Theory]
    [InlineData("--package", "System.Text.Json")]
    [InlineData("--library", "test.dll")]
    [InlineData("--platform", "System.Text.Json")]
    [InlineData("--project", "project.assets.json")]
    [InlineData("--tfm", "net10.0")]
    [InlineData("--framework", "runtime")]
    [InlineData("--at", "first")]
    public async Task ParserRejectsConflictingSources(params string[] extra)
    {
        var (root, opts, args) = Parser();
        var parsed = root.Parse(["member", Type, "-m", "Serialize:1",
            "--workspace", Packet(), .. extra]);
        Assert.Empty(parsed.Errors);
        var result = await MemberOptionsParser.ParseAsync(parsed, opts, args);
        var error = Assert.IsType<MemberOptionsParser.VersionError>(result);
        Assert.Contains("sole Member location source", error.Error.Message, StringComparison.Ordinal);
    }

    static (RootCommand, SharedOptions, MemberOptionsParser.MemberCommandArgs) Parser()
    {
        ArgumentPreprocessor.Reset();
        var opts = new SharedOptions();
        Command member = ApiCommandDefinitions.CreateMemberCommand(opts, out var args);
        return (new RootCommand { member }, opts, args);
    }

    static async Task<(int ExitCode, string Output, string Error)> RunAsync(
        string packet, string member, bool share, string[]? extra = null, string type = Type)
    {
        var (root, opts, args) = Parser();
        var parsed = root.Parse(["member", type, "-m", member,
            "--workspace", packet, .. share ? new[] { "--share", "packet" } : [],
            .. extra ?? []]);
        Assert.Empty(parsed.Errors);
        var success = Assert.IsType<MemberOptionsParser.Success>(
            await MemberOptionsParser.ParseAsync(parsed, opts, args));
        using var client = new HttpClient(new FailingHandler());
        InMemoryPackageStore store = await StoreAsync();
        return await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(
            success.Options with { CompanionOutput = CompanionOutput.None },
            LoadOptions(client, store)));
    }

    static WorkspaceContextLoadOptions LoadOptions(HttpClient client, IPackageStore store) =>
        new()
        {
            HttpClient = client,
            SourceAuthorization = new UniformPackageSourceAuthorization(
                [new PackageSource("test", SourceUrl)]),
            PackageStore = store,
        };

    static async Task<InMemoryPackageStore> StoreAsync()
    {
        var store = new InMemoryPackageStore();
        await using Stream archive = File.OpenRead(Path.Combine(
            AppContext.BaseDirectory, "RealAssets", "PackageEndpointScope",
            "system.text.json.10.0.0.nupkg"));
        await store.CommitAsync("System.Text.Json", "10.0.0",
            NuGetCache.GetSourceKey(SourceUrl), archive,
            TestContext.Current.CancellationToken);
        return store;
    }

    internal static string Packet(int format = 4) => WorkspaceSharePacketCodec.Encode(
        WorkspaceSharePacketCodec.ParseJson(
            "{\"f\":" + format + (format == 5 ? ",\"s\":[[\"https://example.test/v3/index.json\"]]" : "") + """
            ,"t":[["System.Text.Json","10.0.0","net10.0",null],
                   ["System.Text.Json","10.0.0","net8.0",null]],
             "g":[[0],[1]],"r":[["p","Polly."]],"a":1,"x":0,
             "v":[{"t":null,"u":{"k":"workspace"}},
                   {"t":0,"u":{"k":"workspace"}},
                   {"t":1,"u":{"k":"workspace"}}]}
            """, TestContext.Current.CancellationToken));

    static string NormalizePaths(string value) =>
        System.Text.RegularExpressions.Regex.Replace(value,
            @"inspect-type-workspace[^/\\\s]+", "inspect-type-workspaceTEMP");

    sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Unexpected acquisition: " + request.RequestUri);
    }
}
