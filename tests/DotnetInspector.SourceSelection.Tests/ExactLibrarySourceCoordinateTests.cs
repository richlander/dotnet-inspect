using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.Platforms;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.SourceSelection.Tests;

public sealed class ExactLibrarySourceCoordinateTests
{
    private const string RealPackageVersion =
        "11.0.0-preview.7.26381.103";

    [Fact]
    public void PublicConsumerRetainsAndPatternMatchesBothSourceArms()
    {
        PackageSourceCoordinate package =
            PackageSourceCoordinate.Create("Contoso.Json", "1.2.3");
        var population = new PlatformLibraryPopulationDeclaration(
            PlatformFamily.DotNetRuntime);
        ManagedMetadataIdentity.Assembly assembly = Identity(
            "Contoso.Json",
            new Version(1, 2, 3, 0));

        ExactLibrarySourceCoordinate packageCoordinate =
            new ExactLibrarySourceCoordinate.Package(package, assembly);
        ExactLibrarySourceCoordinate platformCoordinate =
            new ExactLibrarySourceCoordinate.Platform(population, assembly);

        var packageArm = Assert.IsType<
            ExactLibrarySourceCoordinate.Package>(packageCoordinate);
        var platformArm = Assert.IsType<
            ExactLibrarySourceCoordinate.Platform>(platformCoordinate);
        Assert.Same(package, packageArm.PackageCoordinate);
        Assert.Same(population, platformArm.Population);
        Assert.Same(assembly, packageArm.LibraryIdentity);
        Assert.Same(assembly, platformArm.LibraryIdentity);
    }

    [Fact]
    public void SameSourceUsesMetadataEquivalenceForEqualityAndHashing()
    {
        PackageSourceCoordinate package =
            PackageSourceCoordinate.Create("Contoso.Json", "1.2.3");
        ManagedMetadataIdentity.Assembly firstIdentity = Identity(
            "Contoso.Json",
            new Version(1, 2, 3, 0),
            culture: null,
            publicKeyToken: "ABCDEF0123456789");
        ManagedMetadataIdentity.Assembly equivalentIdentity = Identity(
            "contoso.json",
            new Version(1, 2, 3, 0),
            culture: "neutral",
            publicKeyToken: "abcdef0123456789");
        var firstPackage = new ExactLibrarySourceCoordinate.Package(
            package,
            firstIdentity);
        var equivalentPackage = new ExactLibrarySourceCoordinate.Package(
            package,
            equivalentIdentity);
        var population = new PlatformLibraryPopulationDeclaration(
            PlatformFamily.DotNetRuntime);
        var firstPlatform = new ExactLibrarySourceCoordinate.Platform(
            population,
            firstIdentity);
        var equivalentPlatform = new ExactLibrarySourceCoordinate.Platform(
            population,
            equivalentIdentity);

        Assert.Equal(firstPackage, equivalentPackage);
        Assert.True(firstPackage == equivalentPackage);
        Assert.False(firstPackage != equivalentPackage);
        Assert.Equal(
            firstPackage.GetHashCode(),
            equivalentPackage.GetHashCode());
        Assert.Equal(firstPlatform, equivalentPlatform);
        Assert.Equal(
            firstPlatform.GetHashCode(),
            equivalentPlatform.GetHashCode());
        Assert.Single(new HashSet<ExactLibrarySourceCoordinate>
        {
            firstPackage,
            equivalentPackage,
        });
        Assert.Single(new HashSet<ExactLibrarySourceCoordinate>
        {
            firstPlatform,
            equivalentPlatform,
        });
    }

    [Fact]
    public void SourceAndIdentityNeighborsRemainDistinct()
    {
        ManagedMetadataIdentity.Assembly baseline = Identity(
            "Contoso.Json",
            new Version(1, 2, 3, 0),
            publicKeyToken: "abcdef0123456789");
        var package = new ExactLibrarySourceCoordinate.Package(
            PackageSourceCoordinate.Create("Contoso.Json", "1.2.3"),
            baseline);

        ExactLibrarySourceCoordinate[] neighbors =
        [
            new ExactLibrarySourceCoordinate.Package(
                PackageSourceCoordinate.Create("Contoso.Json", "1.2.4"),
                baseline),
            new ExactLibrarySourceCoordinate.Platform(
                new(PlatformFamily.DotNetRuntime),
                baseline),
            new ExactLibrarySourceCoordinate.Platform(
                new(PlatformFamily.AspNetCore),
                baseline),
            new ExactLibrarySourceCoordinate.Package(
                package.PackageCoordinate,
                Identity(
                    "Contoso.Json",
                    new Version(1, 2, 4, 0),
                    publicKeyToken: "abcdef0123456789")),
            new ExactLibrarySourceCoordinate.Package(
                package.PackageCoordinate,
                Identity(
                    "Contoso.Json",
                    new Version(1, 2, 3, 0),
                    culture: "fr-FR",
                    publicKeyToken: "abcdef0123456789")),
            new ExactLibrarySourceCoordinate.Package(
                package.PackageCoordinate,
                Identity(
                    "Contoso.Json",
                    new Version(1, 2, 3, 0),
                    publicKeyToken: "0123456789abcdef")),
        ];

        Assert.All(neighbors, neighbor => Assert.NotEqual(package, neighbor));
        Assert.Equal(
            neighbors.Length + 1,
            new HashSet<ExactLibrarySourceCoordinate>(
                [package, .. neighbors]).Count);
    }

    [Fact]
    public void VersionlessAssemblyPatternIsRejected()
    {
        var partial = new ManagedMetadataIdentity.Assembly(
            new AssemblyReferenceIdentity(
                "Contoso.Json",
                Version: null,
                Culture: null,
                PublicKeyToken: null));

        Assert.Throws<ArgumentException>(
            () => new ExactLibrarySourceCoordinate.Package(
                PackageSourceCoordinate.Create("Contoso.Json", "1.2.3"),
                partial));
    }

    [Fact]
    public void RealPackageAndPlatformAssembliesRemainDistinctCoordinates()
    {
        ManagedMetadataIdentity.Assembly packageIdentity =
            RealIdentity("package");
        ManagedMetadataIdentity.Assembly platformIdentity =
            RealIdentity("platform");

        Assert.True(
            packageIdentity.Identity.IsEquivalentTo(
                platformIdentity.Identity));

        var package = new ExactLibrarySourceCoordinate.Package(
            PackageSourceCoordinate.Create(
                "System.Text.Json",
                RealPackageVersion),
            packageIdentity);
        var platform = new ExactLibrarySourceCoordinate.Platform(
            new(PlatformFamily.DotNetRuntime),
            platformIdentity);

        Assert.NotEqual<ExactLibrarySourceCoordinate>(package, platform);
        Assert.Equal(
            2,
            new HashSet<ExactLibrarySourceCoordinate>
            {
                package,
                platform,
            }.Count);
    }

    private static ManagedMetadataIdentity.Assembly Identity(
        string name,
        Version version,
        string? culture = null,
        string? publicKeyToken = null) =>
        new(
            new AssemblyReferenceIdentity(
                name,
                version,
                culture,
                publicKeyToken));

    private static ManagedMetadataIdentity.Assembly RealIdentity(
        string source)
    {
        using var stream = File.OpenRead(
            Path.Combine(
                AppContext.BaseDirectory,
                "RealAssets",
                "ExactLibraryCoordinate",
                source,
                "System.Text.Json.dll"));
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        return new(
            AssemblyReferenceIdentity.FromAssemblyDefinition(reader));
    }
}
