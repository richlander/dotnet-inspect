using System.CommandLine;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Services;
using DotnetInspect.Cli.Output;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;

namespace DotnetInspect.Cli.Tests;

public sealed partial class WorkspacePacketActivationTests
{
    // PR-fast: real pinned archive, full production Member parsing and two offline contexts.
    [Theory]
    [InlineData("")]
    [InlineData("--markdown")]
    [InlineData("--plaintext")]
    [InlineData("--table")]
    [InlineData("--tsv")]
    [InlineData("--json")]
    [InlineData("--jsonl")]
    public async Task SavedMemberReplaysOrdinaryOverview(string terminal)
    {
        string input = MemberWorkspaceRouteTests.Packet(5);
        using var client = new HttpClient(new FailingHandler());
        var load = LoadOptions(client, await RealPackageStoreAsync());
        string[] flags = terminal.Length == 0 ? [] : [terminal];
        MemberOptions producer = await ParseMemberReplayOptionsAsync(input, "Serialize:1", []);
        var savedOutput = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(
            producer with { ShareFormat = WorkspaceShareFormat.Packet }, load));
        Assert.True(savedOutput.ExitCode == 0, savedOutput.Error);
        string packet = savedOutput.Error.Trim();
        WorkspaceSharePacket saved = WorkspaceSharePacketCodec.Decode(packet,
            TestContext.Current.CancellationToken);
        Assert.Equal(0, saved.FocusedTabIndex);
        Assert.Equal(1, WorkspaceSharePacketCodec.Decode(input,
            TestContext.Current.CancellationToken).FocusedTabIndex);
        Assert.Equal(2, saved.Contexts.Count);
        Assert.Equal("member.overview", saved.ViewStates[1].Facet);
        var ordinary = await ConsoleCapture.RunAsync(async () => await MemberCommand.ExecuteAsync(
            await ParseMemberReplayOptionsAsync(packet, "Serialize:1", flags), load));
        Assert.True(ordinary.ExitCode == 0, ordinary.Error);
        var actual = await ConsoleCapture.RunAsync(() => WorkspacePacketActivation.ExecuteAsync(
            ReplayPresentation(terminal) with { Packet = packet },
            TestContext.Current.CancellationToken, load));
        Assert.True(actual.ExitCode == 0, actual.Error);
        Assert.Equal(NormalizeTypePaths(ordinary.Output), NormalizeTypePaths(actual.Output));
        Assert.Empty(actual.Error);
        if (terminal != "--count")
            Assert.Contains("Serialize", actual.Output, StringComparison.Ordinal);
        Assert.Equal(packet, WorkspaceSharePacketCodec.Encode(saved));
    }

    [Theory]
    [InlineData("Serialize:2")]
    [InlineData("IsReflectionEnabledByDefault:1")]
    public async Task MemberReplayKeepsExactOverloadOrProperty(string member)
    {
        using var client = new HttpClient(new FailingHandler());
        var load = LoadOptions(client, await RealPackageStoreAsync());
        MemberOptions options = await ParseMemberReplayOptionsAsync(
            MemberWorkspaceRouteTests.Packet(4), member, ["--json"]);
        var source = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(
            options with { ShareFormat = WorkspaceShareFormat.Packet }, load));
        Assert.True(source.ExitCode == 0, source.Error);
        var replay = await ConsoleCapture.RunAsync(() => WorkspacePacketActivation.ExecuteAsync(
            new WorkspaceOptions { Packet = source.Error.Trim(), Format = OutputFormat.Json,
                FormatExplicitlySet = true, FormatFlagExplicitlySet = true },
            TestContext.Current.CancellationToken, load));
        Assert.True(replay.ExitCode == 0, replay.Error);
        Assert.Equal(NormalizeTypePaths(source.Output), NormalizeTypePaths(replay.Output));
        Assert.Empty(replay.Error);
    }

    [Fact]
    public async Task MemberCountFailsBeforeAcquisitionWithOrdinaryDiagnostic()
    {
        string json = await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(),
            "docs", "examples", "workspace-member-replay.json"), TestContext.Current.CancellationToken);
        string packet = CanonicalMemberPacket(json);
        using var client = new HttpClient(new FailingHandler());
        var actual = await ConsoleCapture.RunAsync(() => WorkspacePacketActivation.ExecuteAsync(
            new WorkspaceOptions { Packet = packet, Count = true }, TestContext.Current.CancellationToken,
            LoadOptions(client, new DotnetInspector.Packages.InMemoryPackageStore())));
        var ordinary = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(
            new MemberOptions { TypeName = "System.Text.Json.JsonSerializer", MemberFilter = ["Serialize"],
                MemberDigest = "1dc14dd1fb", Count = true }));
        Assert.Equal(1, actual.ExitCode);
        Assert.Equal(1, ordinary.ExitCode);
        Assert.Empty(actual.Output);
        Assert.Equal(ordinary.Error, actual.Error);
        foreach (string[] entrance in new[] { new[] { "-w", packet }, new[] { packet } })
        {
            var routed = await RunAsync([.. entrance, "--count"]);
            Assert.Equal(actual.Error, routed.Error);
            Assert.Empty(routed.Output);
        }
    }

    [Theory]
    [InlineData("member.signature")]
    [InlineData("member.il")]
    public async Task UnsupportedMemberFacetIsRefusedBeforeAcquisition(string facet)
    {
        string json = await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(),
            "docs", "examples", "workspace-member-replay.json"), TestContext.Current.CancellationToken);
        using var client = new HttpClient(new FailingHandler());
        var actual = await ConsoleCapture.RunAsync(() => WorkspacePacketActivation.ExecuteAsync(
            new WorkspaceOptions { Packet = CanonicalMemberPacket(json.Replace("member.overview", facet, StringComparison.Ordinal)) },
            TestContext.Current.CancellationToken,
            LoadOptions(client, new DotnetInspector.Packages.InMemoryPackageStore())));
        Assert.Equal(1, actual.ExitCode);
        Assert.Empty(actual.Output);
        Assert.Contains(facet, actual.Error, StringComparison.Ordinal);
        Assert.Contains("no CLI activation execution binding", actual.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingMemberAnchorDoesNotSelectAnotherOverload()
    {
        string json = await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(),
            "docs", "examples", "workspace-member-replay.json"), TestContext.Current.CancellationToken);
        using var client = new HttpClient(new FailingHandler());
        var load = LoadOptions(client, await RealPackageStoreAsync());
        var actual = await ConsoleCapture.RunAsync(() => WorkspacePacketActivation.ExecuteAsync(
            new WorkspaceOptions { Packet = CanonicalMemberPacket(json.Replace("1dc14dd1fb", "0000000000", StringComparison.Ordinal)) },
            TestContext.Current.CancellationToken, load));
        Assert.Equal(1, actual.ExitCode);
        Assert.Empty(actual.Output);
        Assert.NotEmpty(actual.Error);
    }

    [Theory]
    [InlineData("-w")]
    [InlineData("raw")]
    public async Task MemberRowControlsRetainOrdinaryUnits(string entrance)
    {
        string json = await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(),
            "docs", "examples", "workspace-member-replay.json"), TestContext.Current.CancellationToken);
        string packet = CanonicalMemberPacket(json);
        ArgumentPreprocessor.Reset();
        var root = CommandLineBuilder.CreateRootCommand();
        string[] args = [.. entrance == "-w" ? new[] { "-w", packet } : [packet], "-n", "1"];
        string[] processed = entrance == "raw"
            ? [WorkspaceActivationCommandDefinitions.CommandName, .. args]
            : CommandLineBuilder.PreprocessArgs(args, root);
        var prepared = CliRowSelectionCommandRegistry.Prepare(root.Parse(processed), processed);
        Assert.Null(prepared.Error);
        Assert.NotNull(prepared.Lowering!.LineIntent);
        Assert.Equal(1, prepared.Lowering.LineIntent.Count);
        Assert.Empty(prepared.Lowering.SemanticIntent.Operations);
        var invalid = await RunAsync([.. args, "--json"]);
        Assert.Equal(1, invalid.ExitCode);
        Assert.Empty(invalid.Output);
        Assert.Contains("line", invalid.Error, StringComparison.OrdinalIgnoreCase);
    }

    static string CanonicalMemberPacket(string json) => WorkspaceSharePacketCodec.Encode(
        WorkspaceSharePacketCodec.ParseJson(json, TestContext.Current.CancellationToken));

    static WorkspaceOptions ReplayPresentation(string terminal) => new()
    {
        Format = terminal switch
        {
            "--plaintext" => OutputFormat.PlainText,
            "--table" => OutputFormat.Table,
            "--tsv" => OutputFormat.Tsv,
            "--json" => OutputFormat.Json,
            "--jsonl" => OutputFormat.Jsonl,
            _ => OutputFormat.Markdown,
        },
        FormatExplicitlySet = terminal is not ("" or "--count"),
        FormatFlagExplicitlySet = terminal is not ("" or "--count"),
        TabularExplicitlySet = terminal is "--table" or "--tsv" or "--jsonl",
        MarkdownExplicitlySet = terminal == "--markdown",
        Count = terminal == "--count",
    };

    static async Task<MemberOptions> ParseMemberReplayOptionsAsync(
        string packet, string member, string[] flags)
    {
        ArgumentPreprocessor.Reset();
        var opts = new SharedOptions();
        Command command = ApiCommandDefinitions.CreateMemberCommand(opts, out var args);
        var root = new RootCommand { command };
        var parsed = root.Parse(["member", "System.Text.Json.JsonSerializer", "-m", member,
            "--workspace", packet, .. flags]);
        Assert.Empty(parsed.Errors);
        var result = Assert.IsType<MemberOptionsParser.Success>(
            await MemberOptionsParser.ParseAsync(parsed, opts, args));
        return result.Options with { CompanionOutput = CompanionOutput.None };
    }
}
