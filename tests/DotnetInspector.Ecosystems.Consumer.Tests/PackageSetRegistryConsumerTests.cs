using System.Collections.Immutable;
using DotnetInspector.Ecosystems;
using DotnetInspector.Packages;
using ILInspector.Metadata;

namespace DotnetInspector.Ecosystems.Consumer.Tests;

public sealed class PackageSetRegistryConsumerTests
{
    [Fact]
    public void PublicSurfaceSupportsDiscoveryAndLookup()
    {
        PackageSetDescriptor[] descriptors = [.. PackageSetCatalog.Discover()];
        PackageSetDescriptor selected = descriptors.Single(
            descriptor =>
                descriptor.Id == PackageSetIds.MicrosoftExtensions);
        var lookup = Assert.IsType<PackageSetLookupResult.Known>(
            PackageSetCatalog.Lookup(selected.Id));

        Assert.Same(selected, lookup.Descriptor);
        Assert.Equal("Microsoft.Extensions", selected.Title);
        Assert.Equal(44, selected.Members.Length);
        Assert.All(
            selected.Members,
            member => Assert.StartsWith(
                "Microsoft.Extensions.",
                member.PackageId,
                StringComparison.Ordinal));
    }

    [Fact]
    public void PublicSurfaceSupportsEcosystemDiscoveryAndDemoSelection()
    {
        EcosystemPackDescriptor[] packs = [.. EcosystemPackCatalog.Discover()];
        EcosystemDemoDescriptor[] demos = [.. EcosystemPackCatalog.DiscoverDemos()];

        Assert.Equal(4, packs.Length);
        Assert.Equal(10, demos.Length);
        Assert.Equal(ProductDemoIds.StjSerializer, demos[0].ScenarioId);
        Assert.Equal(ProductDemoIds.AspireRedisCallGraph, demos[^1].ScenarioId);
        var selected = Assert.IsType<EcosystemDemoSelectionResult.Known>(
            EcosystemPackCatalog.SelectDemo(ProductDemoIds.StjSerializer));
        Assert.Same(demos[0], selected.Selection.Descriptor);
        Assert.Equal(
            ProductDemoIds.StjSerializer,
            selected.Selection.Scenario.ScenarioId);
    }

    [Fact]
    public void PublicSurfaceKeepsCoreReferencesSeparateFromCuratedMembership()
    {
        EcosystemPackDescriptor descriptor = Assert.IsType<EcosystemPackLookupResult.Known>(
            EcosystemPackCatalog.Lookup(EcosystemPackIds.MicrosoftExtensions)).Descriptor;
        ImmutableArray<string> roots = descriptor.NamespaceRoots;
        ImmutableArray<PackageCoordinate> core = descriptor.CorePackages;

        Assert.Same(
            descriptor,
            EcosystemPackCatalog.Discover().Single(pack => pack.Id == descriptor.Id));
        Assert.Equal(["Microsoft.Extensions"], roots);
        Assert.Equal(
            [
                "Microsoft.Extensions.DependencyInjection.Abstractions",
                "Microsoft.Extensions.Configuration.Abstractions",
                "Microsoft.Extensions.Logging.Abstractions",
            ],
            core.Select(package => package.PackageId));
        Assert.Equal(PackageSetIds.MicrosoftExtensions, descriptor.PackageSet);
        PackageSetDescriptor curated = Assert.IsType<PackageSetLookupResult.Known>(
            PackageSetCatalog.Lookup(descriptor.PackageSet!)).Descriptor;
        Assert.Equal(44, curated.Members.Length);
        Assert.Contains(curated.Members, member => member.PackageId == "Microsoft.Extensions.Http.Resilience");
        Assert.DoesNotContain(core, member => member.PackageId == "Microsoft.Extensions.Http.Resilience");
        Assert.DoesNotContain(
            curated.Members,
            member => member.PackageId == "Microsoft.Extensions.DependencyInjection.Abstractions");

        EcosystemPackDescriptor platform = Assert.IsType<EcosystemPackLookupResult.Known>(
            EcosystemPackCatalog.Lookup(EcosystemPackIds.Platform)).Descriptor;
        Assert.Equal(["System"], platform.NamespaceRoots);
        Assert.Empty(platform.CorePackages);
        Assert.Null(platform.PackageSet);
        Assert.False(platform.HasScanner);
        Assert.Equal(3, platform.Demos.Length);
    }

    [Fact]
    public void PublicSurfaceSeparatesToolsFromInspectionPackages()
    {
        EcosystemPackDescriptor aspire = Assert.IsType<EcosystemPackLookupResult.Known>(
            EcosystemPackCatalog.Lookup(EcosystemPackIds.Aspire)).Descriptor;
        ImmutableArray<PackageCoordinate> tools = aspire.ToolPackages;

        Assert.Same(aspire, EcosystemPackCatalog.Discover().Single(
            pack => pack.Id == EcosystemPackIds.Aspire));
        Assert.Equal(new PackageCoordinate("Aspire.Cli"), Assert.Single(tools));
        Assert.Equal(new PackageCoordinate("Aspire.Hosting"), Assert.Single(aspire.CorePackages));
        Assert.Equal(PackageSetIds.Aspire, aspire.PackageSet);
        PackageSetDescriptor curated = Assert.IsType<PackageSetLookupResult.Known>(
            PackageSetCatalog.Lookup(aspire.PackageSet!)).Descriptor;
        Assert.DoesNotContain(curated.Members, package => package.PackageId == "Aspire.Cli");
        Assert.All(
            EcosystemPackCatalog.Discover().Where(pack => pack.Id != EcosystemPackIds.Aspire),
            pack => Assert.Empty(pack.ToolPackages));
    }

    [Fact]
    public void PublicSurfaceHandsSelectedScannerToIntegrationOwner()
    {
        EcosystemPackDescriptor aspire = Assert.Single(
            EcosystemPackCatalog.Discover().Where(pack => pack.HasScanner));
        Assert.Equal(EcosystemPackIds.Aspire, aspire.Id);
        var selected = Assert.IsType<EcosystemScannerSelectionResult.Known>(
            EcosystemPackCatalog.SelectScanner(aspire.Id));

        using var session = AssemblyInspectionSession.Open(
            typeof(EcosystemPackCatalog).Assembly.Location);
        Assert.Empty(session.EcosystemIntegrations(selected.Binding));
        Assert.IsType<EcosystemScannerSelectionResult.Unavailable>(
            EcosystemPackCatalog.SelectScanner(EcosystemPackIds.MicrosoftExtensions));
        Assert.True(EcosystemPackId.TryCreate(
            "ecosystem.not-shipped", out EcosystemPackId? unknownId));
        Assert.IsType<EcosystemScannerSelectionResult.Unknown>(
            EcosystemPackCatalog.SelectScanner(unknownId));
    }
}
