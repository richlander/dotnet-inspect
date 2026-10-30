using System.Text.Json;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Views;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using NuGetFetch;

namespace DotnetInspect.Cli.Tests;

// PR-fast: small admitted contexts and existing pinned archives; feed work fails fast.
public sealed partial class ExactPackageWorkspaceRouteTests
{
    // PR-fast: the actual pinned NuGet archive is provisioned by PackageDownload.
    [Fact]
    public async Task EcosystemBaseAddsRealPackageAndSharesTheDerivedEnvironment()
    {
        WorkspaceSharePacket basis = WorkspaceRegistrationEditor.AddEcosystem(
            null, WorkspaceCommand.ParseEcosystemRegistration("runtime").Declaration).Packet!;
        string input = WorkspaceSharePacketCodec.Encode(basis);
        var store = new InMemoryPackageStore();
        await using (Stream archive = File.OpenRead(Path.Combine(
            AppContext.BaseDirectory, "RealAssets", "PackageEndpointScope",
            "system.text.json.10.0.0.nupkg")))
        {
            await store.CommitAsync("System.Text.Json", "10.0.0",
                NuGetCache.GetSourceKey(SourceUrl), archive,
                TestContext.Current.CancellationToken);
        }
        using var client = new HttpClient(new FailingHandler());
        var options = new InspectionOptions
        {
            PackageArgs = ["System.Text.Json@10.0.0"],
            WorkspacePacket = input,
            CompanionOutput = CompanionOutput.None,
            Verbosity = Verbosity.Quiet,
        };
        var ordinary = await ConsoleCapture.RunAsync(() => PackageCommand.ExecuteAsync(
            options, new CommandContext(false), LoadOptions(client, store)));
        var shared = await ConsoleCapture.RunAsync(() => PackageCommand.ExecuteAsync(
            options with { ShareFormat = WorkspaceShareFormat.Packet },
            new CommandContext(false), LoadOptions(client, store)));

        Assert.Equal(0, ordinary.ExitCode);
        Assert.Equal(0, shared.ExitCode);
        Assert.Equal(ordinary.Output, shared.Output);
        Assert.Contains("System.Text.Json@10.0.0", shared.Output, StringComparison.Ordinal);
        Assert.Empty(ordinary.Error);
        WorkspaceSharePacket derived = WorkspaceSharePacketCodec.Decode(shared.Error.Trim(), TestContext.Current.CancellationToken);
        Assert.Equal(input, WorkspaceSharePacketCodec.Encode(basis));
        Assert.Empty(basis.Tabs);
        Assert.Equal(basis.FormatVersion, derived.FormatVersion);
        using JsonDocument before = JsonDocument.Parse(WorkspaceSharePacketCodec.SerializeJson(basis));
        using JsonDocument after = JsonDocument.Parse(WorkspaceSharePacketCodec.SerializeJson(derived));
        Assert.True(JsonElement.DeepEquals(
            before.RootElement.GetProperty("r"), after.RootElement.GetProperty("r")));
        WorkspaceShareTab added = Assert.Single(derived.Tabs);
        Assert.Equal("system.text.json", added.Source);
        Assert.Equal("10.0.0", added.Version);
        Assert.Equal(TraversalTargetFrameworkPolicy.ProductDefault.TargetFramework, added.Framework);
        Assert.Equal(0, derived.FocusedTabIndex);
        Assert.Equal(0, derived.SelectedContextIndex);
        Assert.IsType<PortableSubjectRequest.Package>(derived.ViewStates[1].Subject);
        Assert.Equal("package.libraries", derived.ViewStates[1].Facet);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task AdditionPreservesNeighborContextTargetSourcesAndInput(int format)
    {
        var source = WorkspaceSharePacketCodec.Decode(EncodePacket(
            format: 4, tabs: [(FocusedPackage, Version, Framework)],
            contexts: [[0]], focusedTab: 0, selectedContext: 0), TestContext.Current.CancellationToken);
        WorkspaceRegistration[] registrations = [new WorkspaceRegistration.PackagePrefix(
            new DotnetInspector.SourceSelection.PackagePrefixDeclaration("Polly."))];
        WorkspaceSharePacket basis = format switch
        {
            3 => new([.. source.Tabs], [.. source.Contexts], registrations,
                source.FocusedTabIndex, source.SelectedContextIndex, [.. source.ViewStates]),
            4 => WorkspaceSharePacket.CreateV4([.. source.Tabs], [.. source.Contexts], registrations,
                source.FocusedTabIndex, source.SelectedContextIndex, [.. source.ViewStates]),
            _ => WorkspaceSharePacket.CreateV5([.. source.Tabs], [.. source.Contexts], registrations,
                [new(SourceUrl, WorkspacePackageSourceAuthentication.Anonymous)],
                source.FocusedTabIndex, source.SelectedContextIndex, [.. source.ViewStates]),
        };
        string input = WorkspaceSharePacketCodec.Encode(basis);
        var store = await StoreAsync(FocusedPackage, SelectedPackage);
        using var client = new HttpClient(new FailingHandler());
        var result = await ConsoleCapture.RunAsync(() => PackageCommand.ExecuteAsync(
            new InspectionOptions
            {
                PackageArgs = [$"{SelectedPackage}@{Version}"],
                WorkspacePacket = input, ShareFormat = WorkspaceShareFormat.Packet,
                CompanionOutput = CompanionOutput.None, Verbosity = Verbosity.Quiet,
            }, new CommandContext(false), LoadOptions(client, store)));

        Assert.Equal(0, result.ExitCode);
        WorkspaceSharePacket derived = WorkspaceSharePacketCodec.Decode(result.Error.Trim(), TestContext.Current.CancellationToken);
        Assert.Equal(input, WorkspaceSharePacketCodec.Encode(basis));
        Assert.Equal(format, derived.FormatVersion);
        Assert.Equal(basis.PackageSources, derived.PackageSources);
        Assert.Equal(basis.Registrations, derived.Registrations);
        Assert.Equal(basis.Tabs[0], derived.Tabs[0]);
        Assert.Equal([0, 1], derived.Contexts[0].TabIndexes);
        Assert.Equal(Framework, derived.Tabs[1].Framework);
        Assert.Equal(1, derived.FocusedTabIndex);
        Assert.Equal(0, derived.SelectedContextIndex);
    }

    [Fact]
    public async Task RegistrationOnlyBaseCanAddAFloatingPackage()
    {
        string packet = WorkspaceSharePacketCodec.Encode(
            WorkspaceRegistrationEditor.AddPrefix(null,
                new DotnetInspector.SourceSelection.PackagePrefixDeclaration("Polly.")).Packet!);
        var store = await StoreAsync(SelectedPackage);
        using var client = new HttpClient(new VersionListingHandler());
        var result = await ConsoleCapture.RunAsync(() => PackageCommand.ExecuteAsync(
            new InspectionOptions
            {
                PackageArgs = [SelectedPackage], WorkspacePacket = packet,
                ShareFormat = WorkspaceShareFormat.Packet,
                CompanionOutput = CompanionOutput.None, Verbosity = Verbosity.Quiet,
            }, new CommandContext(false), LoadOptions(client, store)));

        Assert.Equal(0, result.ExitCode);
        WorkspaceSharePacket derived = WorkspaceSharePacketCodec.Decode(result.Error.Trim(), TestContext.Current.CancellationToken);
        Assert.Null(Assert.Single(derived.Tabs).Version);
        Assert.Equal(0, derived.FocusedTabIndex);
    }

    [Fact]
    public async Task AdditionShareRefusalPreservesOrdinaryContent()
    {
        string packet = WorkspaceSharePacketCodec.Encode(
            WorkspaceRegistrationEditor.AddPrefix(null,
                new DotnetInspector.SourceSelection.PackagePrefixDeclaration("Polly.")).Packet!);
        var store = await StoreAsync(SelectedPackage);
        using var client = new HttpClient(new FailingHandler());
        var result = await ConsoleCapture.RunAsync(() => PackageCommand.ExecuteAsync(
            new InspectionOptions
            {
                PackageArgs = [$"{SelectedPackage}@{Version}"], WorkspacePacket = packet,
                ShareFormat = WorkspaceShareFormat.Packet,
                IncludeSections = [PackageSections.Files], CompanionOutput = CompanionOutput.None,
            }, new CommandContext(false), LoadOptions(client, store)));

        Assert.Equal(1, result.ExitCode);
        Assert.Contains($"{SelectedPackage}.dll", result.Output, StringComparison.Ordinal);
        Assert.Contains("--share is not projectable", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJ", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdditionCannotTurnSourceDenialIntoSuccessfulShare()
    {
        string packet = WorkspaceSharePacketCodec.Encode(
            WorkspaceRegistrationEditor.AddPrefix(null,
                new DotnetInspector.SourceSelection.PackagePrefixDeclaration("Polly.")).Packet!);
        using var client = new HttpClient(new FailingHandler());
        var result = await ConsoleCapture.RunAsync(() => PackageCommand.ExecuteAsync(
            new InspectionOptions
            {
                PackageArgs = [$"{SelectedPackage}@{Version}"], WorkspacePacket = packet,
                ShareFormat = WorkspaceShareFormat.Packet, CompanionOutput = CompanionOutput.None,
            }, new CommandContext(false), new WorkspaceContextLoadOptions
            {
                HttpClient = client, PackageStore = new InMemoryPackageStore(),
                SourceAuthorization = new UniformPackageSourceAuthorization([]),
            }));

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("could not be restored", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJ", result.Error, StringComparison.Ordinal);
    }
}
