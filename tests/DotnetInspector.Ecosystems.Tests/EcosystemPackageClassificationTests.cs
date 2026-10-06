using System.Text.Json;
using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using NuGet.Versioning;

namespace DotnetInspector.Ecosystems.Tests;

// PR-fast: pure classification over the checked-in production platform catalog.
public sealed class EcosystemPackageClassificationTests
{
    private static readonly PlatformPruneInventory Net10 = CatalogInventory("net10.0");
    private static readonly PlatformPruneInventory Net9 = CatalogInventory("net9.0");

    [Theory]
    [InlineData("System.Linq", "4.3.0")]
    [InlineData("System.Text.Json", "9.0.0")]
    public void PublishedMotivatingPackagesAreRuntimeOwnedAndPruned(
        string packageId, string version)
    {
        EcosystemPackage package = Known(new(packageId, version), "net10.0", Net10);

        Assert.Equal(EcosystemPackIds.Runtime, package.Ecosystem.Id);
        PlatformOwnedInfo platform = Assert.IsType<PlatformOwnedInfo>(package.PlatformOwnedInfo);
        Assert.Equal(PlatformFamily.DotNetRuntime, platform.Layer);
        Assert.True(platform.IsPruned);
        Assert.Equal(PlatformPrunePrecision.Exact,
            Assert.Single(platform.Evidence.Inventory.Entries,
                entry => entry.PackageId == packageId).Precision);
        Assert.Equal("net10.0", platform.Evidence.Coordinate.Framework);
        Assert.Equal(version, platform.Evidence.Coordinate.Version);
    }

    [Fact]
    public void SelectionFrameworkDoesNotChooseThePruningTarget()
    {
        var coordinate = new PackageCoordinate("System.Text.Json", "10.0.0", "net12.0");

        EcosystemPackage onNet10 = Known(coordinate, "net10.0", Net10);
        EcosystemPackage onNet9 = Known(coordinate, "net9.0", Net9);

        Assert.True(onNet10.PlatformOwnedInfo!.IsPruned);
        Assert.False(onNet9.PlatformOwnedInfo!.IsPruned);
        Assert.Equal("net12.0", coordinate.Framework);
        Assert.Equal("net9.0", onNet9.PlatformOwnedInfo.Evidence.Coordinate.Framework);
        Assert.Equal(EcosystemPackIds.Runtime, onNet9.Ecosystem.Id);
    }

    [Fact]
    public void NewerVersionKeepsPlatformOwnershipWithoutPruning()
    {
        EcosystemPackage package = Known(new("System.Text.Json", "11.0.0"), "net10.0", Net10);

        Assert.Equal(EcosystemPackIds.Runtime, package.Ecosystem.Id);
        Assert.Equal(PlatformFamily.DotNetRuntime, package.PlatformOwnedInfo!.Layer);
        Assert.False(package.PlatformOwnedInfo.IsPruned);
        Assert.Equal("10.0.12", package.PlatformOwnedInfo.Evidence.Supply.SuppliedVersion!.ToNormalizedString());
    }

    [Fact]
    public void UnresolvedVersionKeepsMembershipAndUnknownPruning()
    {
        EcosystemPackage package = Known(new("System.Text.Json"), "net10.0", Net10);

        Assert.Equal(EcosystemPackIds.Runtime, package.Ecosystem.Id);
        Assert.NotNull(package.PlatformOwnedInfo);
        Assert.Null(package.PlatformOwnedInfo.IsPruned);
        Assert.Equal(PlatformSubsumption.NotComparable, package.PlatformOwnedInfo.Evidence.Supply.Subsumption);
    }

    [Fact]
    public void CrossPatchProjectionCannotBecomePruned()
    {
        PlatformPruneInventory projection = PlatformPruneInventory.FromProjectedFamily(
            new("Microsoft.NETCore.App", "net10.0", NuGetVersion.Parse("10.0.12")),
            NuGetVersion.Parse("10.0.0"),
            ["System.Text.Json|10.0.0"]);

        EcosystemPackage package = Known(new("System.Text.Json", "9.0.0"), "net10.0", projection);

        Assert.Equal(PlatformFamily.DotNetRuntime, package.PlatformOwnedInfo!.Layer);
        Assert.Null(package.PlatformOwnedInfo.IsPruned);
        Assert.Equal("10.0.0", Assert.Single(projection.Entries).SourcePackVersion.ToNormalizedString());
    }

    [Fact]
    public void ExtensionsOwnerAndAspNetCoreLayerAreIndependent()
    {
        EcosystemPackage package = Known(new("Microsoft.Extensions.Logging", "9.0.0"), "net10.0", Net10);

        Assert.Equal(EcosystemPackIds.MicrosoftExtensions, package.Ecosystem.Id);
        Assert.Equal(PlatformFamily.AspNetCore, package.PlatformOwnedInfo!.Layer);
        Assert.True(package.PlatformOwnedInfo.IsPruned);
    }

    [Theory]
    [InlineData("Microsoft.Extensions.AI", "ecosystem.ai")]
    [InlineData("Microsoft.Extensions.AI.Abstractions", "ecosystem.ai")]
    [InlineData("Microsoft.AspNetCore.Components.WebAssembly", "ecosystem.blazor")]
    [InlineData("Microsoft.AspNetCore.Components.WebView.Maui", "ecosystem.maui")]
    [InlineData("OpenAI", "ecosystem.ai")]
    [InlineData("Anthropic", "ecosystem.ai")]
    [InlineData("Google.GenAI", "ecosystem.ai")]
    [InlineData("Aspire.Hosting", "ecosystem.aspire")]
    public void MostSpecificProductAssociationOwnsThePackage(string id, string ecosystem)
    {
        EcosystemPackage package = Known(new(id, "1.0.0"), "net10.0", Net10);

        Assert.Equal(ecosystem, package.Ecosystem.Id.Value);
        Assert.Null(package.PlatformOwnedInfo);
    }

    [Fact]
    public void SpecificPresentationOwnerDoesNotEraseOverlappingRecognition()
    {
        EcosystemPackage package = Known(new("Microsoft.Extensions.AI", "1.0.0"), "net10.0", Net10);
        EcosystemDependencyProfileEntry[] associations =
        [
            .. EcosystemPackCatalog.DependencyRecognitionProfile.Entries.Where(entry =>
                entry.Associations.Any(association => association.Matches(
                    EcosystemDependencyIdentityDomain.PackageId, "Microsoft.Extensions.AI"))),
        ];

        Assert.Equal(EcosystemPackIds.AI, package.Ecosystem.Id);
        Assert.Equal([EcosystemPackIds.MicrosoftExtensions, EcosystemPackIds.AI],
            associations.Select(entry => entry.Ecosystem.Id));
    }

    [Theory]
    [InlineData("Systematic.Linq")]
    [InlineData("OpenAI.Client")]
    [InlineData("OpenAIish")]
    [InlineData("Newtonsoft.Json")]
    public void NearMissesAndExternalPackagesAreNotEcosystem(string id)
    {
        Assert.IsType<EcosystemPackageResult.NotEcosystem>(
            EcosystemPackCatalog.IsEcosystemPackage(new(id, "1.0.0"), "net10.0", Net10));
    }

    [Fact]
    public void PlatformInventoryCanEstablishOwnershipWithoutAProductAssociation()
    {
        EcosystemPackage package = Known(new("Microsoft.CSharp", "4.7.0"), "net10.0", Net10);

        Assert.Equal(EcosystemPackIds.Runtime, package.Ecosystem.Id);
        Assert.Equal(PlatformFamily.DotNetRuntime, package.PlatformOwnedInfo!.Layer);
        Assert.True(package.PlatformOwnedInfo.IsPruned);
    }

    [Fact]
    public void MissingInventoryPreservesIndependentlyRecognizedOwner()
    {
        var unavailable = Assert.IsType<EcosystemPackageResult.Unavailable>(
            EcosystemPackCatalog.IsEcosystemPackage(new("System.Text.Json", "9.0.0"), "net10.0", null));

        Assert.Equal(EcosystemPackageUnavailableReason.InventoryUnavailable, unavailable.Reason);
        Assert.Equal(EcosystemPackIds.Runtime, unavailable.Ecosystem!.Id);
        var unknown = Assert.IsType<EcosystemPackageResult.Unavailable>(
            EcosystemPackCatalog.IsEcosystemPackage(new("Microsoft.CSharp", "4.7.0"), "net10.0", null));
        Assert.Null(unknown.Ecosystem);
    }

    [Fact]
    public void DifferentTraversalInventoryIsUnavailable()
    {
        var unavailable = Assert.IsType<EcosystemPackageResult.Unavailable>(
            EcosystemPackCatalog.IsEcosystemPackage(new("System.Text.Json", "9.0.0"), "net10.0", Net9));

        Assert.Equal(EcosystemPackageUnavailableReason.InventoryTargetMismatch, unavailable.Reason);
        Assert.Equal(EcosystemPackIds.Runtime, unavailable.Ecosystem!.Id);
    }

    [Fact]
    public void ExplicitNoPlatformInventoryIsComplete()
    {
        PlatformPruneInventory inventory = PlatformPruneInventory.None("net10.0");
        EcosystemPackage package = Known(new("System.Text.Json", "9.0.0"), "net10.0", inventory);

        Assert.Equal(EcosystemPackIds.Runtime, package.Ecosystem.Id);
        Assert.Null(package.PlatformOwnedInfo);
        Assert.IsType<EcosystemPackageResult.NotEcosystem>(
            EcosystemPackCatalog.IsEcosystemPackage(new("Newtonsoft.Json", "13.0.3"), "net10.0", inventory));
    }

    [Fact]
    public void IdentityAndTraversalFrameworkAreCaseInsensitive()
    {
        EcosystemPackage package = Known(new("sYsTeM.lInQ", "4.3.0"), "NET10.0", Net10);

        Assert.Equal(EcosystemPackIds.Runtime, package.Ecosystem.Id);
        Assert.True(package.PlatformOwnedInfo!.IsPruned);
        Assert.Equal("net10.0", package.PlatformOwnedInfo.Evidence.Coordinate.Framework);
    }

    [Theory]
    [InlineData(" ", "9.0.0", "net10.0")]
    [InlineData("System..Linq", "4.3.0", "net10.0")]
    [InlineData("System.Text.Json", "latest", "net10.0")]
    [InlineData("System.Text.Json", "9.0.0", "not-a-tfm")]
    [InlineData("System.Text.Json", "9.0.0", " net10.0")]
    public void InvalidInputsFailBeforeClassification(string id, string version, string tfm)
    {
        Assert.Throws<ArgumentException>(() =>
            EcosystemPackCatalog.IsEcosystemPackage(new(id, version), tfm, Net10));
    }

    [Fact]
    public void FamilyNearMissKeepsTheBroaderOwner()
    {
        EcosystemPackage package = Known(new("Microsoft.Extensions.AIish", "1.0.0"), "net10.0", Net10);

        Assert.Equal(EcosystemPackIds.MicrosoftExtensions, package.Ecosystem.Id);
        Assert.Null(package.PlatformOwnedInfo);
    }

    [Fact]
    public void UnsupportedPlatformFamilyFailsVisibly()
    {
        PlatformPruneInventory inventory = PlatformPruneInventory.FromExactFamily(
            new("Microsoft.WindowsDesktop.App", "net10.0", NuGetVersion.Parse("10.0.12")),
            ["External.Package|1.0.0"]);

        Assert.Throws<ArgumentException>(() => EcosystemPackCatalog.IsEcosystemPackage(
            new("External.Package", "1.0.0"), "net10.0", inventory));
    }

    private static EcosystemPackage Known(PackageCoordinate coordinate, string tfm, PlatformPruneInventory inventory) =>
        Assert.IsType<EcosystemPackageResult.Known>(
            EcosystemPackCatalog.IsEcosystemPackage(coordinate, tfm, inventory)).Package;

    private static PlatformPruneInventory CatalogInventory(string tfm)
    {
        using Stream stream = typeof(EcosystemPackageClassificationTests).Assembly
            .GetManifestResourceStream("platform-index.json")!;
        using JsonDocument catalog = JsonDocument.Parse(stream);
        JsonElement target = catalog.RootElement.GetProperty("targets").EnumerateArray()
            .Single(target => target.GetProperty("tfm").GetString() == tfm);
        NuGetVersion version = NuGetVersion.Parse(target.GetProperty("version").GetString()!);
        return PlatformPruneInventory.Compose(
            target.GetProperty("supplies").EnumerateArray()
                .GroupBy(row => row.GetProperty("family").GetString()!)
                .Select(family => PlatformPruneInventory.FromExactFamily(
                    new(family.Key, tfm, version),
                    family.Select(row =>
                        $"{row.GetProperty("package").GetString()}|{row.GetProperty("version").GetString()}"))));
    }
}
