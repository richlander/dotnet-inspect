using DotnetInspect.Cli.Inspectors;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using DotnetInspector.SourceSelection;

namespace DotnetInspect.Cli.Tests;

public sealed class ConfiguredPackageSearchWorkspaceTests
{
    [Fact]
    public void Eligibility_IsLimitedToOneExplicitPackageAndTfm()
    {
        SearchSourceSelection selection =
            SearchSourceNormalizer.Normalize(
                SourceIntent.Create(
                    [new SourceSelector.PackageReference(
                        "Example.Package",
                        "1.0.0")]));
        var request = new AssemblySetRequest
        {
            Packages = ["Example.Package@1.0.0"],
        };

        Assert.True(
            ConfiguredPackageSearchWorkspace.IsEligible(
                selection,
                request,
                "net11.0"));
        Assert.False(
            ConfiguredPackageSearchWorkspace.IsEligible(
                selection,
                request,
                targetFramework: null));
        Assert.False(
            ConfiguredPackageSearchWorkspace.IsEligible(
                selection,
                request,
                "net11.0",
                resultLimit: 1));
        Assert.False(
            ConfiguredPackageSearchWorkspace.IsEligible(
                selection,
                request with
                {
                    Packages =
                    [
                        "Example.Package@1.0.0",
                        "Other.Package@1.0.0",
                    ],
                },
                "net11.0"));
        Assert.False(
            ConfiguredPackageSearchWorkspace.IsEligible(
                selection,
                request with
                {
                    Assemblies = ["Example.dll"],
                },
                "net11.0"));
    }

    [Fact]
    public void Eligibility_RejectsArchiveGroupAndPrefixSources()
    {
        var request = new AssemblySetRequest
        {
            Packages = ["Example.Package@1.0.0"],
        };

        Assert.False(
            ConfiguredPackageSearchWorkspace.IsEligible(
                Normalize(
                    new SourceSelector.PackageArchive(
                        "Example.Package.1.0.0.nupkg")),
                request,
                "net11.0"));
        Assert.False(
            ConfiguredPackageSearchWorkspace.IsEligible(
                Normalize(
                    new SourceSelector.PackageGroup(
                        [new PackageCoordinate(
                            "Example.Package",
                            "1.0.0")])),
                request,
                "net11.0"));
        Assert.False(
            ConfiguredPackageSearchWorkspace.IsEligible(
                Normalize(
                    new SourceSelector.PackagePrefix(
                        new PackagePrefixRequest("Example", 10))),
                request,
                "net11.0"));
    }

    private static SearchSourceSelection Normalize(
        SourceSelector selector) =>
        SearchSourceNormalizer.Normalize(
            SourceIntent.Create([selector]));
}
