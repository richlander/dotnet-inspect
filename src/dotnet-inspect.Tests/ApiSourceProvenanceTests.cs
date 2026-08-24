using DotnetInspector.Inspectors;
using DotnetInspector.Models;
using ILInspector.Metadata;

namespace DotnetInspector.Tests;

public sealed class ApiSourceProvenanceTests
{
    [Fact]
    public void PackageSource_RetainsPackageAcquisition()
    {
        var provenance = Assert.IsType<
            AssemblyResolutionProvenance.PackageAsset>(
            ApiSourceResolver.CreateProvenance(
                SourceKind.NuGet,
                "Example.Package",
                "1.2.3",
                "net11.0",
                platformFramework: null,
                apiVersion: null,
                projectAssetsPath: null));

        Assert.Equal("Example.Package", provenance.PackageId);
        Assert.Equal("1.2.3", provenance.PackageVersion);
        Assert.Equal("net11.0", provenance.Tfm);
    }

    [Fact]
    public void ExplicitLibrarySource_IsDesignated()
    {
        Assert.IsType<AssemblyResolutionProvenance.DesignatedAsset>(
            ApiSourceResolver.CreateProvenance(
                SourceKind.Library,
                packageName: null,
                packageVersion: null,
                selectedTfm: null,
                platformFramework: null,
                apiVersion: null,
                projectAssetsPath: null));
    }

    [Fact]
    public void ProjectSource_RetainsProjectAcquisition()
    {
        Assert.IsType<AssemblyResolutionProvenance.ProjectAsset>(
            ApiSourceResolver.CreateProvenance(
                SourceKind.Project,
                packageName: null,
                packageVersion: null,
                selectedTfm: "net11.0",
                platformFramework: null,
                apiVersion: null,
                projectAssetsPath: "/tmp/project.assets.json"));
    }

    [Fact]
    public void PlatformSource_RetainsPlatformAcquisition()
    {
        Assert.IsType<AssemblyResolutionProvenance.PlatformAsset>(
            ApiSourceResolver.CreateProvenance(
                SourceKind.Platform,
                packageName: null,
                packageVersion: null,
                selectedTfm: "net11.0",
                platformFramework: "runtime",
                apiVersion: "11.0.0",
                projectAssetsPath: null));
    }

    [Fact]
    public void UnknownSource_DoesNotDefaultToDesignation()
    {
        Assert.Throws<InvalidOperationException>(
            () => ApiSourceResolver.CreateProvenance(
                apiSource: null,
                packageName: null,
                packageVersion: null,
                selectedTfm: null,
                platformFramework: null,
                apiVersion: null,
                projectAssetsPath: null));
    }
}
