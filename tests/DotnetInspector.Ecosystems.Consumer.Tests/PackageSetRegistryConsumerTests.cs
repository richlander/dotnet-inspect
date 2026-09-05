using DotnetInspector.Ecosystems;
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
