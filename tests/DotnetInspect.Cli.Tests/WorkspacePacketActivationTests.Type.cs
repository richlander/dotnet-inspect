using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Planning;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;

namespace DotnetInspect.Cli.Tests;

public sealed partial class WorkspacePacketActivationTests
{
    // PR-fast: pinned real archive, two contexts, no feed access.
    [Theory]
    [InlineData(OutputFormat.Markdown, false)]
    [InlineData(OutputFormat.PlainText, false)]
    [InlineData(OutputFormat.Table, false)]
    [InlineData(OutputFormat.Tsv, false)]
    [InlineData(OutputFormat.Json, false)]
    [InlineData(OutputFormat.Jsonl, false)]
    [InlineData(OutputFormat.Markdown, true)]
    public async Task SavedTypeReplaysOrdinaryApi(OutputFormat format, bool count)
    {
        string input = MemberWorkspaceRouteTests.Packet(5);
        using var client = new HttpClient(new FailingHandler());
        var load = LoadOptions(client, await RealPackageStoreAsync());
        var savedOutput = await ConsoleCapture.RunAsync(() => ExecuteOrdinaryTypeAsync(
            TypeReplayOptions(format, false) with
            {
                WorkspacePacket = input, ShareFormat = WorkspaceShareFormat.Packet,
            }, load));
        Assert.True(savedOutput.ExitCode == 0, savedOutput.Error);
        string packet = savedOutput.Error.Trim();
        var ordinary = count
            ? await ConsoleCapture.RunAsync(() => ExecuteOrdinaryTypeAsync(
                TypeReplayOptions(format, true) with { WorkspacePacket = packet }, load))
            : savedOutput;
        Assert.True(ordinary.ExitCode == 0, ordinary.Error);
        WorkspaceSharePacket saved = WorkspaceSharePacketCodec.Decode(packet,
            TestContext.Current.CancellationToken);
        Assert.Equal(0, saved.FocusedTabIndex);
        Assert.Equal(1, WorkspaceSharePacketCodec.Decode(input,
            TestContext.Current.CancellationToken).FocusedTabIndex);
        Assert.Equal(2, saved.Contexts.Count);
        Assert.Equal("type.api", saved.ViewStates[1].Facet);
        var actual = await ConsoleCapture.RunAsync(() => WorkspacePacketActivation.ExecuteAsync(
            new WorkspaceOptions { Packet = packet, Format = format, Count = count },
            TestContext.Current.CancellationToken, load));
        Assert.True(actual.ExitCode == 0, actual.Error);
        Assert.Equal(NormalizeTypePaths(ordinary.Output), NormalizeTypePaths(actual.Output));
        Assert.Empty(actual.Error);
        Assert.DoesNotContain("net8.0", actual.Output, StringComparison.Ordinal);
        if (count)
            Assert.True(int.Parse(actual.Output.Trim(), System.Globalization.CultureInfo.InvariantCulture) > 100);
        else
            Assert.Contains("Serialize", actual.Output, StringComparison.Ordinal);
        Assert.Equal(packet, WorkspaceSharePacketCodec.Encode(saved));
    }

    [Theory]
    [InlineData(4, "System.Text.Json.Serialization.IJsonOnDeserialized", 1)]
    [InlineData(5, "System.Text.Json.JsonNamingPolicy", 6)]
    public async Task TypeReplayPreservesSmallPopulation(
        int packetFormat, string typeName, int expectedCount)
    {
        using var client = new HttpClient(new FailingHandler());
        var load = LoadOptions(client, await RealPackageStoreAsync());
        var ordinary = await ConsoleCapture.RunAsync(() => ExecuteOrdinaryTypeAsync(
            TypeReplayOptions(OutputFormat.Json, false) with
            {
                TypeName = typeName, WorkspacePacket = MemberWorkspaceRouteTests.Packet(packetFormat),
                ShareFormat = WorkspaceShareFormat.Packet,
            }, load));
        Assert.True(ordinary.ExitCode == 0, ordinary.Error);
        var actual = await ConsoleCapture.RunAsync(() => WorkspacePacketActivation.ExecuteAsync(
            new WorkspaceOptions { Packet = ordinary.Error.Trim(), Format = OutputFormat.Json, Count = true },
            TestContext.Current.CancellationToken, load));
        Assert.True(actual.ExitCode == 0, actual.Error);
        var counted = await ConsoleCapture.RunAsync(() => ExecuteOrdinaryTypeAsync(
            TypeReplayOptions(OutputFormat.Json, true) with
            {
                TypeName = typeName, WorkspacePacket = ordinary.Error.Trim(),
            }, load));
        Assert.True(counted.ExitCode == 0, counted.Error);
        Assert.Equal(counted.Output, actual.Output);
        Assert.Equal(expectedCount.ToString(System.Globalization.CultureInfo.InvariantCulture), actual.Output.Trim());
        Assert.Empty(actual.Error);
    }

    [Theory]
    [InlineData("-w")]
    [InlineData("raw")]
    public async Task TypeRowControlsRetainOrdinaryUnits(string entrance)
    {
        string packet = Encode(TypeJson);
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

    [Fact]
    public async Task AdmittedTypePreservesSourceDenialWithoutOutput()
    {
        using var client = new HttpClient(new FailingHandler());
        var actual = await ConsoleCapture.RunAsync(() => WorkspacePacketActivation.ExecuteAsync(
            new WorkspaceOptions { Packet = Encode(TypeJson) },
            TestContext.Current.CancellationToken, new WorkspaceContextLoadOptions
            {
                HttpClient = client, PackageStore = new InMemoryPackageStore(),
                SourceAuthorization = new UniformPackageSourceAuthorization([]),
            }));
        Assert.Equal(1, actual.ExitCode);
        Assert.Empty(actual.Output);
        Assert.Contains("could not be restored", actual.Error, StringComparison.Ordinal);
    }

    static string NormalizeTypePaths(string value) =>
        System.Text.RegularExpressions.Regex.Replace(value,
            @"inspect-type-workspace[^/\\s]+", "inspect-type-workspaceTEMP");

    static Task<int> ExecuteOrdinaryTypeAsync(TypeOptions options, WorkspaceContextLoadOptions load) =>
        TypeCommand.ExecuteAsync(options, ResolvedMemberInspectionPlan.FromCompatibilityOptions(options), load);

    static TypeOptions TypeReplayOptions(OutputFormat format, bool count) => new()
    {
        TypeName = "System.Text.Json.JsonSerializer", Format = format,
        JsonOutput = format == OutputFormat.Json, PlainText = format == OutputFormat.PlainText,
        Tabular = format is OutputFormat.Table or OutputFormat.Tsv or OutputFormat.Jsonl,
        Tsv = format == OutputFormat.Tsv, Jsonl = format == OutputFormat.Jsonl, Count = count,
        CompanionOutput = CompanionOutput.None,
    };
}
