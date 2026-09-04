using DotnetInspector.Ecosystems;

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
}
