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
    public void PublicConsumerRetainsAndPatternMatchesAllSourceArms()
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
        ExactLibrarySourceCoordinate projectCoordinate =
            new ExactLibrarySourceCoordinate.Project(assembly);
        ExactLibrarySourceCoordinate localCoordinate =
            new ExactLibrarySourceCoordinate.Local(assembly);

        var packageArm = Assert.IsType<
            ExactLibrarySourceCoordinate.Package>(packageCoordinate);
        var platformArm = Assert.IsType<
            ExactLibrarySourceCoordinate.Platform>(platformCoordinate);
        var projectArm = Assert.IsType<
            ExactLibrarySourceCoordinate.Project>(projectCoordinate);
        var localArm = Assert.IsType<
            ExactLibrarySourceCoordinate.Local>(localCoordinate);
        Assert.Same(package, packageArm.PackageCoordinate);
        Assert.Same(population, platformArm.Population);
        Assert.Same(assembly, packageArm.LibraryIdentity);
        Assert.Same(assembly, platformArm.LibraryIdentity);
        Assert.Same(assembly, projectArm.LibraryIdentity);
        Assert.Same(assembly, localArm.LibraryIdentity);
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
        ExactLibrarySourceCoordinate[] first =
        [
            firstPackage,
            firstPlatform,
            new ExactLibrarySourceCoordinate.Project(firstIdentity),
            new ExactLibrarySourceCoordinate.Local(firstIdentity),
        ];
        ExactLibrarySourceCoordinate[] equivalent =
        [
            equivalentPackage,
            equivalentPlatform,
            new ExactLibrarySourceCoordinate.Project(equivalentIdentity),
            new ExactLibrarySourceCoordinate.Local(equivalentIdentity),
        ];

        Assert.Equal(firstPackage, equivalentPackage);
        Assert.True(firstPackage == equivalentPackage);
        Assert.False(firstPackage != equivalentPackage);
        for (int i = 0; i < first.Length; i++)
        {
            Assert.Equal(first[i], equivalent[i]);
            Assert.Equal(
                first[i].GetHashCode(),
                equivalent[i].GetHashCode());
            Assert.Single(new HashSet<ExactLibrarySourceCoordinate>
            {
                first[i],
                equivalent[i],
            });
        }
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
            new ExactLibrarySourceCoordinate.Project(baseline),
            new ExactLibrarySourceCoordinate.Local(baseline),
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
        Assert.Throws<ArgumentException>(
            () => new ExactLibrarySourceCoordinate.Platform(
                new(PlatformFamily.DotNetRuntime),
                partial));
        Assert.Throws<ArgumentException>(
            () => new ExactLibrarySourceCoordinate.Project(partial));
        Assert.Throws<ArgumentException>(
            () => new ExactLibrarySourceCoordinate.Local(partial));
    }

    [Fact]
    public void CopiedPlatformAssemblyRemainsLocal()
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
        var local = new ExactLibrarySourceCoordinate.Local(platformIdentity);

        Assert.NotEqual<ExactLibrarySourceCoordinate>(package, platform);
        Assert.NotEqual<ExactLibrarySourceCoordinate>(package, local);
        Assert.NotEqual<ExactLibrarySourceCoordinate>(platform, local);
        Assert.Equal(
            3,
            new HashSet<ExactLibrarySourceCoordinate>
            {
                package,
                platform,
                local,
            }.Count);
    }

    [Fact]
    public void RealProjectOutputRetainsProjectSourceDomain()
    {
        ManagedMetadataIdentity.Assembly projectIdentity =
            IdentityFromPath(
                typeof(ExactLibrarySourceCoordinate).Assembly.Location);
        var project =
            new ExactLibrarySourceCoordinate.Project(projectIdentity);
        var local = new ExactLibrarySourceCoordinate.Local(projectIdentity);

        Assert.Same(projectIdentity, project.LibraryIdentity);
        Assert.Same(projectIdentity, local.LibraryIdentity);
        Assert.NotEqual<ExactLibrarySourceCoordinate>(project, local);
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
        => IdentityFromPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "RealAssets",
                "ExactLibraryCoordinate",
                source,
                "System.Text.Json.dll"));

    private static ManagedMetadataIdentity.Assembly IdentityFromPath(
        string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        return new(
            AssemblyReferenceIdentity.FromAssemblyDefinition(reader));
    }
}
