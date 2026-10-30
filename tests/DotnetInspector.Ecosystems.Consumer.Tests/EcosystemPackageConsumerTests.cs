using DotnetInspector.Ecosystems;
using DotnetInspector.Packages;

namespace DotnetInspector.Ecosystems.Consumer.Tests;

// PR-fast: exercises the public API without friend access or package acquisition.
public sealed class EcosystemPackageConsumerTests
{
    [Fact]
    public void PublicSurfaceDistinguishesOwnershipNonMatchAndUnavailable()
    {
        PlatformPruneInventory inventory = PlatformPruneInventory.None("net10.0");
        var known = Assert.IsType<EcosystemPackageResult.Known>(
            EcosystemPackCatalog.IsEcosystemPackage(new("OpenAI", "2.0.0"), "net10.0", inventory));
        Assert.Equal(EcosystemPackIds.AI, known.Package.Ecosystem.Id);
        Assert.Null(known.Package.PlatformOwnedInfo);

        Assert.IsType<EcosystemPackageResult.NotEcosystem>(
            EcosystemPackCatalog.IsEcosystemPackage(new("Newtonsoft.Json", "13.0.3"), "net10.0", inventory));
        var unavailable = Assert.IsType<EcosystemPackageResult.Unavailable>(
            EcosystemPackCatalog.IsEcosystemPackage(new("OpenAI", "2.0.0"), "net10.0", null));
        Assert.Equal(EcosystemPackageUnavailableReason.InventoryUnavailable, unavailable.Reason);
        Assert.Equal(EcosystemPackIds.AI, unavailable.Ecosystem!.Id);
    }
}
