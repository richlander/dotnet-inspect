using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using NuGetFetch;

namespace DotnetInspect.Cli.Tests;

public sealed partial class WorkspacePacketActivationTests
{
    // PR-fast: real pinned archive, two contexts, no feed access. The source
    // command selects net10.0 even though the input packet focuses net8.0.
    [Theory]
    [InlineData(OutputFormat.Markdown, false)]
    [InlineData(OutputFormat.PlainText, false)]
    [InlineData(OutputFormat.Table, false)]
    [InlineData(OutputFormat.Tsv, false)]
    [InlineData(OutputFormat.Json, false)]
    [InlineData(OutputFormat.Jsonl, false)]
    [InlineData(OutputFormat.Markdown, true)]
    public async Task SavedPackageReplaysOrdinaryLibraries(OutputFormat format, bool count)
    {
        string input = MemberWorkspaceRouteTests.Packet(5);
        using var client = new HttpClient(new FailingHandler());
        var load = LoadOptions(client, await RealPackageStoreAsync());
        var ordinary = await ConsoleCapture.RunAsync(() => PackageCommand.ExecuteAsync(
            new InspectionOptions
            {
                PackageArgs = ["System.Text.Json"], WorkspacePacket = input,
                ShareFormat = WorkspaceShareFormat.Packet, Format = format,
                JsonOutput = format == OutputFormat.Json, Count = count,
            }, new CommandContext(false), load));
        Assert.True(ordinary.ExitCode == 0, ordinary.Error);
        string packet = ordinary.Error.Trim();
        WorkspaceSharePacket saved = WorkspaceSharePacketCodec.Decode(packet,
            TestContext.Current.CancellationToken);
        Assert.Equal(0, saved.FocusedTabIndex);
        Assert.Equal(1, WorkspaceSharePacketCodec.Decode(input,
            TestContext.Current.CancellationToken).FocusedTabIndex);
        Assert.Equal(2, saved.Contexts.Count);
        Assert.Equal("package.libraries", saved.ViewStates[1].Facet);
        var actual = await ConsoleCapture.RunAsync(() => WorkspacePacketActivation.ExecuteAsync(
            new WorkspaceOptions { Packet = packet, Format = format, Count = count },
            TestContext.Current.CancellationToken, load));
        Assert.True(actual.ExitCode == 0, actual.Error);
        Assert.Equal(ordinary.Output, actual.Output);
        Assert.Empty(actual.Error);
        Assert.DoesNotContain("net8.0", actual.Output, StringComparison.Ordinal);
        if (count)
            Assert.Equal("1", actual.Output.Trim());
        else
            Assert.Contains("System.Text.Json", actual.Output, StringComparison.Ordinal);
        Assert.Equal(packet, WorkspaceSharePacketCodec.Encode(saved));
    }

    [Theory]
    [InlineData("package.overview")]
    [InlineData("package.dependencies")]
    public async Task UnsupportedPackageFacetIsRefusedBeforeAcquisition(string facet)
    {
        string packet = Encode(TypeJson.Replace(
            "\"r\":{\"k\":\"type\",\"l\":[\"System.Text.Json\",\"10.0.0.0\",null,\"cc7b13ffcd2ddd51\"],\"y\":\"System.Text.Json.JsonSerializer\"},\"u\":{\"k\":\"type\"},\"f\":\"type.api\"",
            "\"r\":{\"k\":\"package\"},\"u\":{\"k\":\"package\"},\"f\":\"" + facet + "\"",
            StringComparison.Ordinal));
        using var client = new HttpClient(new FailingHandler());
        var actual = await ConsoleCapture.RunAsync(() => WorkspacePacketActivation.ExecuteAsync(
            new WorkspaceOptions { Packet = packet }, TestContext.Current.CancellationToken,
            LoadOptions(client, new InMemoryPackageStore())));
        Assert.Equal(1, actual.ExitCode);
        Assert.Empty(actual.Output);
        Assert.Contains(facet, actual.Error, StringComparison.Ordinal);
        Assert.Contains("no CLI activation execution binding", actual.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdmittedPackagePreservesSourceDenialWithoutOutput()
    {
        string json = await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(),
            "docs", "examples", "workspace-package-derived.json"),
            TestContext.Current.CancellationToken);
        using var client = new HttpClient(new FailingHandler());
        var actual = await ConsoleCapture.RunAsync(() => WorkspacePacketActivation.ExecuteAsync(
            new WorkspaceOptions { Packet = WorkspaceSharePacketCodec.Encode(
                WorkspaceSharePacketCodec.ParseJson(json, TestContext.Current.CancellationToken)) },
            TestContext.Current.CancellationToken, new WorkspaceContextLoadOptions
            {
                HttpClient = client, PackageStore = new InMemoryPackageStore(),
                SourceAuthorization = new UniformPackageSourceAuthorization([]),
            }));
        Assert.Equal(1, actual.ExitCode);
        Assert.Empty(actual.Output);
        Assert.Contains("could not be restored", actual.Error, StringComparison.Ordinal);
    }

    // PR-fast: real neighboring compile populations, including compatible fallback.
    [Theory]
    [InlineData("System.ValueTuple", "4.5.0", "SectionGrowth", 0)]
    [InlineData("NUnit", "4.2.2", "PackageEndpointScope", 2)]
    public async Task PackageReplayPreservesCompatibleTargetAndPopulation(
        string package, string version, string assetDirectory, int count)
    {
        using var client = new HttpClient(new FailingHandler());
        var store = new InMemoryPackageStore();
        await using (Stream archive = File.OpenRead(Path.Combine(AppContext.BaseDirectory,
            "RealAssets", assetDirectory, $"{package.ToLowerInvariant()}.{version}.nupkg")))
            await store.CommitAsync(package, version, NuGetCache.GetSourceKey(SourceUrl),
                archive, TestContext.Current.CancellationToken);
        var load = LoadOptions(client, store);
        WorkspaceSharePacket basis = WorkspaceSharePacketCodec.ParseJson(
            "{\"f\":5,\"s\":[[\"" + SourceUrl + "\"]],\"t\":[[\"" + package
            + "\",\"" + version + "\",\"net10.0\",null]],\"g\":[[0]],\"r\":[],\"a\":0,\"x\":0,"
            + "\"v\":[{\"t\":null,\"u\":{\"k\":\"workspace\"}},{\"t\":0,\"u\":{\"k\":\"workspace\"}}]}",
            TestContext.Current.CancellationToken);
        var ordinary = await ConsoleCapture.RunAsync(() => PackageCommand.ExecuteAsync(
            new InspectionOptions
            {
                PackageArgs = [package], WorkspacePacket = WorkspaceSharePacketCodec.Encode(basis),
                ShareFormat = WorkspaceShareFormat.Packet, Format = OutputFormat.Json, JsonOutput = true,
            }, new CommandContext(false), load));
        Assert.True(ordinary.ExitCode == 0, ordinary.Error);
        string packet = ordinary.Error.Trim();
        var replay = await ConsoleCapture.RunAsync(() => WorkspacePacketActivation.ExecuteAsync(
            new WorkspaceOptions { Packet = packet, Format = OutputFormat.Json },
            TestContext.Current.CancellationToken, load));
        Assert.True(replay.ExitCode == 0, replay.Error);
        Assert.Equal(ordinary.Output, replay.Output);
        Assert.Empty(replay.Error);
        var counted = await ConsoleCapture.RunAsync(() => WorkspacePacketActivation.ExecuteAsync(
            new WorkspaceOptions { Packet = packet, Count = true },
            TestContext.Current.CancellationToken, load));
        Assert.True(counted.ExitCode == 0, counted.Error);
        Assert.Equal(count.ToString(System.Globalization.CultureInfo.InvariantCulture), counted.Output.Trim());
    }

    // PR-fast: same presentation lowering as the explicit Package command.
    [Theory]
    [InlineData("-w")]
    [InlineData("raw")]
    public async Task PackageRowControlsRetainOrdinaryUnits(string entrance)
    {
        string json = await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(),
            "docs", "examples", "workspace-package-replay.json"),
            TestContext.Current.CancellationToken);
        string packet = WorkspaceSharePacketCodec.Encode(WorkspaceSharePacketCodec.ParseJson(json,
            TestContext.Current.CancellationToken));
        ArgumentPreprocessor.Reset();
        var root = CommandLineBuilder.CreateRootCommand();
        string[] args = [.. entrance == "-w" ? new[] { "-w", packet } : [packet], "-n", "1"];
        string[] processed = CommandLineBuilder.PreprocessArgs(args, root);
        // The raw router reparses a recognized packet as this same command.
        if (entrance == "raw")
            processed = [WorkspaceActivationCommandDefinitions.CommandName, .. args];
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

    static async Task<InMemoryPackageStore> RealPackageStoreAsync()
    {
        var store = new InMemoryPackageStore();
        await using Stream archive = File.OpenRead(Path.Combine(AppContext.BaseDirectory,
            "RealAssets", "PackageEndpointScope", "system.text.json.10.0.0.nupkg"));
        await store.CommitAsync("System.Text.Json", "10.0.0", NuGetCache.GetSourceKey(SourceUrl),
            archive, TestContext.Current.CancellationToken);
        return store;
    }

    static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
            directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
                return directory.FullName;
        throw new InvalidOperationException("Repository root not found.");
    }
}
