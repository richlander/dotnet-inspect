using System.IO.Compression;
using System.Runtime.Versioning;
using System.Text.Json;

using DotnetInspect.Web.Interop.Package;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;

namespace DotnetInspect.Web.Tests;

[CollectionDefinition(
    "Exact Library API operations",
    DisableParallelization = true)]
public sealed class BrowserExactLibraryApiOperationCollection;

[Collection("Exact Library API operations")]
[SupportedOSPlatform("browser")]
public sealed class BrowserExactLibraryApiOperationTests
{
    const string Version = "1.0.0";
    const string Framework = "net11.0";

    [Fact]
    public async Task QueryLibraryApiProjectsExactAssetEnvelope()
    {
        (BrowserPackage package, PackageCompileAsset first,
            PackageCompileAsset second) = await RegisterAsync();

        BrowserExactLibraryApiInspection inspection = Read(
            await PackageExports.QueryLibraryApi(
                package.PackageId,
                package.Version,
                Framework,
                first.Id));
        BrowserExactLibraryApiInspection neighbor = Read(
            await PackageExports.QueryLibraryApi(
                package.PackageId,
                package.Version,
                Framework,
                second.Id));

        Assert.Equal(
            BrowserExactLibraryApiInspectionOutcome.Available,
            inspection.Content.Outcome);
        Assert.Equal(first.Id, inspection.Content.Asset?.Id);
        Assert.Equal(first.Path, inspection.Content.Asset?.Path);
        Assert.Equal(
            package.PackageId.ToLowerInvariant(),
            inspection.Content.Source?.PackageId);
        Assert.Equal(package.Version, inspection.Content.Source?.PackageVersion);
        Assert.False(string.IsNullOrWhiteSpace(
            inspection.Content.Source?.Producer));
        Assert.Equal(Framework, inspection.Content.Source?.Framework);
        Assert.Equal(
            inspection.Content.Inventory?.PublicTypeCount,
            inspection.Content.Inventory?.TypeKinds.Sum(facet => facet.Count));
        Assert.True(inspection.Content.Inventory!.PublicTypeCount > 0);
        Assert.NotEmpty(inspection.Content.Inventory?.Namespaces ?? []);
        Assert.NotEqual(
            Guid.Empty,
            inspection.Content.Assembly?.ModuleVersionId);
        Assert.Equal(
            BrowserInspectionShareKind.Available,
            inspection.Share.Kind);
        Assert.NotEqual(
            inspection.Content.Asset?.Id,
            neighbor.Content.Asset?.Id);
        Assert.NotEqual(
            inspection.Content.Assembly?.ModuleVersionId,
            neighbor.Content.Assembly?.ModuleVersionId);
    }

    [Fact]
    public async Task QueryLibraryApiMissingAssetIsTypedAndDetached()
    {
        (BrowserPackage package, _, _) = await RegisterAsync();

        BrowserExactLibraryApiInspection inspection = Read(
            await PackageExports.QueryLibraryApi(
                package.PackageId,
                package.Version,
                Framework,
                "compile:ref/net11.0/Missing.dll"));

        Assert.Equal(
            BrowserExactLibraryApiInspectionOutcome.NotFound,
            inspection.Content.Outcome);
        Assert.Null(inspection.Content.Inventory);
        Assert.Null(inspection.Content.Assembly);
        Assert.Contains(
            inspection.Diagnostics,
            diagnostic =>
                diagnostic.Code == "exact-library-api.not-found");
        Assert.Equal(
            BrowserInspectionShareKind.Available,
            inspection.Share.Kind);
    }

    static BrowserExactLibraryApiInspection Read(
        string json) =>
        JsonSerializer.Deserialize(
            json,
            BrowserPackageJsonContext
                .Default
                .BrowserExactLibraryApiInspection)!;

    static async Task<(
        BrowserPackage Package,
        PackageCompileAsset First,
        PackageCompileAsset Second)> RegisterAsync()
    {
        string id =
            "Browser.Exact.Library.Api."
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
                    == "DotnetInspect.Web.Interop.Package.dll"),
            Assert.Single(
                selection.Assets,
                asset => asset.AssemblyName
                    == "DotnetInspect.Web.Core.dll"));
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
