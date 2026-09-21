using System.IO.Compression;
using System.Runtime.Versioning;
using System.Text.Json;

using DotnetInspect.Web.Interop.Package;
using DotnetInspector.Packages;

namespace DotnetInspect.Web.Tests;

[Collection("Exact Library API operations")]
[SupportedOSPlatform("browser")]
public sealed class BrowserLibraryQueryOperationTests
{
    const string Version = "1.0.0";
    const string Framework = "net11.0";

    [Fact]
    public async Task QueryLibrariesProjectsLandedResultToExactAsset()
    {
        (BrowserPackage package, PackageCompileAsset packageAsset) =
            await RegisterAsync();

        BrowserLibraryQueryInspection inspection = Read(
            await PackageExports.QueryLibraries(
                package.PackageId,
                package.Version,
                Framework,
                AdmittedAssetIds(package),
                """["DotnetInspect.Web.Core"]"""));

        BrowserLibraryQueryRow result =
            Assert.Single(inspection.Content.Results);
        Assert.Equal(packageAsset.Id, result.AssetId);
        Assert.Equal(
            "DotnetInspect.Web.Interop.Package",
            result.Library);
        Assert.Equal(packageAsset.Path, result.Path);
        Assert.Equal(package.PackageId, result.Source);
        Assert.Equal(package.Version, result.Version);
        Assert.Equal("Package", result.SourceKind);
        Assert.Equal(Framework, result.TargetFramework);
        Assert.Contains(
            "DotnetInspect.Web.Core",
            result.MatchedReferences);
        Assert.Empty(inspection.Content.Failures);
        Assert.Equal(
            inspection.Content.Summary.PopulationCandidates,
            inspection.Content.Summary.Candidates);
        Assert.Equal(1, inspection.Content.Summary.Matches);
        Assert.Equal("None", inspection.Content.Summary.IncompleteReasons);
        Assert.True(inspection.Content.Summary.IsComplete);
        Assert.Equal(
            BrowserInspectionPortableProjectionKind.NonProjectable,
            inspection.PortableProjection.Kind);
    }

    [Fact]
    public async Task QueryLibrariesEmptyPopulationReturnsExactEnvelope()
    {
        string id =
            "Browser.Library.Query.Empty."
            + Guid.NewGuid().ToString("N");
        var package = new BrowserPackage(
            id,
            Version,
            Archive(($"ref/{Framework}/_._", [])),
            fromCache: false);
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(package);

        BrowserLibraryQueryInspection inspection = Read(
            await PackageExports.QueryLibraries(
                id,
                Version,
                Framework,
                "[]",
                """["System.Runtime"]"""));

        Assert.Empty(inspection.Content.Results);
        Assert.Empty(inspection.Content.Failures);
        Assert.Equal(0, inspection.Content.Summary.PopulationCandidates);
        Assert.Equal(0, inspection.Content.Summary.Candidates);
        Assert.Equal(0, inspection.Content.Summary.Matches);
        Assert.Equal("None", inspection.Content.Summary.IncompleteReasons);
        Assert.True(inspection.Content.Summary.IsComplete);
    }

    [Fact]
    public async Task QueryLibrariesRejectsAssetOutsideCurrentSurface()
    {
        (BrowserPackage package, _) = await RegisterAsync();

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(
            () => PackageExports.QueryLibraries(
                package.PackageId,
                package.Version,
                Framework,
                """["compile:ref/net11.0/Missing.dll"]""",
                """["System.Runtime"]"""));

        Assert.Contains(
            "outside the current package surface",
            error.Message,
            StringComparison.Ordinal);
    }

    static BrowserLibraryQueryInspection Read(string json) =>
        JsonSerializer.Deserialize(
            json,
            BrowserPackageJsonContext
                .Default
                .BrowserLibraryQueryInspection)!;

    static string AdmittedAssetIds(BrowserPackage package)
    {
        PackageCompileAssetSelection selection =
            PackageCompileAssetSelector.Select(
                package.Content,
                package.PackageId,
                Framework);
        Assert.True(selection.IsSelected);
        return JsonSerializer.Serialize(
            selection.Assets.Select(asset => asset.Id).ToArray(),
            BrowserPackageJsonContext.Default.StringArray);
    }

    static async Task<(BrowserPackage Package, PackageCompileAsset PackageAsset)>
        RegisterAsync()
    {
        string id =
            "Browser.Library.Query."
            + Guid.NewGuid().ToString("N");
        var package = new BrowserPackage(
            id,
            Version,
            Archive(
                ($"ref/{Framework}/DotnetInspect.Web.Core.dll",
                    await File.ReadAllBytesAsync(
                        typeof(BrowserInspectionScope).Assembly.Location,
                        TestContext.Current.CancellationToken)),
                ($"ref/{Framework}/DotnetInspect.Web.Interop.Package.dll",
                    await File.ReadAllBytesAsync(
                        typeof(PackageExports).Assembly.Location,
                        TestContext.Current.CancellationToken))),
            fromCache: false);
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(package);
        PackageCompileAssetSelection selection =
            PackageCompileAssetSelector.Select(
                package.Content,
                id,
                Framework);
        Assert.True(selection.IsSelected);
        Assert.Equal(2, selection.Assets.Count);
        return (
            package,
            Assert.Single(
                selection.Assets,
                asset => asset.AssemblyName
                    == "DotnetInspect.Web.Interop.Package.dll"));
    }

    static byte[] Archive(
        params (string EntryPath, byte[] Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(
            buffer,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach ((string path, byte[] content) in entries)
            {
                using Stream entry = archive.CreateEntry(path).Open();
                entry.Write(content);
            }
        }
        return buffer.ToArray();
    }
}
