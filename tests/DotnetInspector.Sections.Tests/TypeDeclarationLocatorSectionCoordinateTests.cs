using DotnetInspector.Platforms;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Sections.Tests;

public sealed class TypeDeclarationLocatorSectionCoordinateTests
{
    [Fact]
    public void ProjectionRetainsEveryCoordinateArmAndOwnerEquality()
    {
        ManagedMetadataIdentity.Assembly first =
            Identity(
                "Contoso.Json",
                culture: null,
                publicKeyToken: "ABCDEF0123456789");
        ManagedMetadataIdentity.Assembly equivalent =
            Identity(
                "contoso.json",
                culture: "neutral",
                publicKeyToken: "abcdef0123456789");
        PackageSourceCoordinate package =
            PackageSourceCoordinate.Create(
                "Contoso.Json",
                "1.2.3");
        var platform =
            new PlatformLibraryPopulationDeclaration(
                PlatformFamily.DotNetRuntime);
        ExactLibrarySourceCoordinate[] source =
        [
            new ExactLibrarySourceCoordinate.Package(
                package,
                first),
            new ExactLibrarySourceCoordinate.Platform(
                platform,
                first),
            new ExactLibrarySourceCoordinate.Project(first),
            new ExactLibrarySourceCoordinate.Local(first),
        ];
        ExactLibrarySourceCoordinate[] equalSource =
        [
            new ExactLibrarySourceCoordinate.Package(
                package,
                equivalent),
            new ExactLibrarySourceCoordinate.Platform(
                platform,
                equivalent),
            new ExactLibrarySourceCoordinate.Project(equivalent),
            new ExactLibrarySourceCoordinate.Local(equivalent),
        ];

        TypeDeclarationLocatorSectionCoordinate[] projected =
        [
            .. source.Select(
                TypeDeclarationLocatorSectionCoordinate.FromSource),
        ];
        TypeDeclarationLocatorSectionCoordinate[] equalProjected =
        [
            .. equalSource.Select(
                TypeDeclarationLocatorSectionCoordinate.FromSource),
        ];

        Assert.IsType<
            TypeDeclarationLocatorSectionCoordinate.PackageCoordinate>(
                projected[0]);
        Assert.IsType<
            TypeDeclarationLocatorSectionCoordinate.PlatformCoordinate>(
                projected[1]);
        Assert.IsType<
            TypeDeclarationLocatorSectionCoordinate.ProjectCoordinate>(
                projected[2]);
        Assert.IsType<
            TypeDeclarationLocatorSectionCoordinate.LocalCoordinate>(
                projected[3]);
        for (int index = 0; index < projected.Length; index++)
        {
            Assert.Equal(
                projected[index],
                equalProjected[index]);
            Assert.Equal(
                projected[index].GetHashCode(),
                equalProjected[index].GetHashCode());
        }
    }

    private static ManagedMetadataIdentity.Assembly Identity(
        string name,
        string? culture,
        string? publicKeyToken) =>
        new(
            new AssemblyReferenceIdentity(
                name,
                new Version(1, 2, 3, 0),
                culture,
                publicKeyToken));
}
