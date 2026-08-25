using DotnetInspector.Inspectors;
using DotnetInspector.Models;
using DotnetInspector.Options;
using DotnetInspector.Packages;
using ILInspector.Metadata;
using System.IO.Compression;

namespace DotnetInspector.Tests;

[Collection("Console")]
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

    [Fact]
    public async Task PackageAcquisitionFailure_CleansExtractionDirectory()
    {
        string packagePath = Path.Combine(
            Path.GetTempPath(),
            $"Broken.Package.{Guid.NewGuid():N}.1.0.0.nupkg");
        string[] before = InspectApiDirectories();
        try
        {
            using (ZipArchive archive = ZipFile.Open(
                packagePath,
                ZipArchiveMode.Create))
            {
                ZipArchiveEntry nuspec =
                    archive.CreateEntry("Broken.Package.nuspec");
                await using (StreamWriter writer = new(nuspec.Open()))
                {
                    await writer.WriteAsync(
                        """
                        <?xml version="1.0"?>
                        <package>
                          <metadata>
                            <id>Broken.Package</id>
                            <version>1.0.0</version>
                            <authors>test</authors>
                            <description>test</description>
                          </metadata>
                        </package>
                        """);
                }

                ZipArchiveEntry library =
                    archive.CreateEntry("lib/net11.0/Broken.Package.dll");
                await using Stream stream = library.Open();
                await stream.WriteAsync(
                    "not a managed image"u8.ToArray(),
                    TestContext.Current.CancellationToken);
            }

            var (_, error) = await ApiSourceResolver.ResolveAsync(
                new ApiOptions
                {
                    PackagePath = packagePath,
                    Tfm = "net11.0",
                });

            Assert.Equal(1, error);
            Assert.Empty(InspectApiDirectories().Except(before));
        }
        finally
        {
            File.Delete(packagePath);
            foreach (string directory in InspectApiDirectories().Except(before))
                Directory.Delete(directory, recursive: true);
        }

        static string[] InspectApiDirectories() =>
            Directory.GetDirectories(
                Path.GetTempPath(),
                "inspect-api*");
    }

    [Fact]
    public async Task SingleRootPackageAssembly_IsSelectedWithoutATfm()
    {
        string packagePath = Path.Combine(
            Path.GetTempPath(),
            $"RootOnly.{Guid.NewGuid():N}.1.0.0.nupkg");
        try
        {
            using (ZipArchive archive = ZipFile.Open(
                packagePath,
                ZipArchiveMode.Create))
            {
                ZipArchiveEntry library =
                    archive.CreateEntry("RootOnly.dll");
                await using Stream destination = library.Open();
                await using FileStream source = File.OpenRead(
                    typeof(ApiSourceProvenanceTests).Assembly.Location);
                await source.CopyToAsync(
                    destination,
                    TestContext.Current.CancellationToken);
            }

            var (result, error) = await ApiSourceResolver.ResolveAsync(
                new ApiOptions
                {
                    PackagePath = packagePath,
                });
            try
            {
                Assert.Null(error);
                Assert.Equal(
                    "RootOnly.dll",
                    Path.GetFileName(result.SearchPath));
                Assert.Null(result.SelectedTfm);
                Assert.IsType<
                    AssemblyResolutionProvenance.PackageAsset>(
                    result.AssemblyReference!.Provenance);
            }
            finally
            {
                PackageExtractor.Cleanup(result?.TempDir);
            }
        }
        finally
        {
            File.Delete(packagePath);
        }
    }

    [Fact]
    public async Task MetadataOverflow_IsReportedAsAcquisitionFailure()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(
                path,
                LibraryFindingConsumerTests.CorruptMetadataStreamCount(
                    File.ReadAllBytes(
                        typeof(ApiSourceProvenanceTests).Assembly.Location)));

            var (result, error) = await ApiSourceResolver.ResolveAsync(
                new ApiOptions
                {
                    DllPath = path,
                });

            Assert.Null(result);
            Assert.Equal(1, error);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
