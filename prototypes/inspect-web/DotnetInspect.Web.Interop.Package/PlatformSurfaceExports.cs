using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspect.Web;
using DotnetInspect.Web.Interop.Package;

/// <summary>
/// Platform acquisition. A runtime pack load returns the same browsable package surface as a
/// NuGet package load, so both live in the package facade.
/// </summary>
namespace DotnetInspect.Web.Interop.Package;

[SupportedOSPlatform("browser")]
public static partial class PackageExports
{
    [JSExport]
    public static async Task<string> GetPlatformVersions(string targetFramework) =>
        JsonSerializer.Serialize(
            await DotnetInspect.Web.BrowserPlatformCatalog.GetVersionsAsync(targetFramework),
            BrowserPackageJsonContext.Default.StringArray);

    [JSExport]
    public static async Task<string> GetPlatformCatalog(
        string targetFramework,
        string platformVersion)
    {
        BrowserPlatformCatalogResult catalog =
            await DotnetInspect.Web.BrowserPlatformCatalog.GetCatalogAsync(
                targetFramework, platformVersion);
        return JsonSerializer.Serialize(
            ProjectPlatformCatalog(catalog),
            BrowserPackageJsonContext.Default.BrowserPlatformCatalog);
    }

    internal static DotnetInspect.Web.Interop.Package.BrowserPlatformCatalog ProjectPlatformCatalog(
        BrowserPlatformCatalogResult catalog) =>
            new(
                catalog.Tfm, catalog.Version,
                [.. catalog.Rows.Select(row => new BrowserPlatformLibrary(
                    row.Tfm, row.Pack, row.Assembly, row.File, row.Kind, row.ForwardsTo,
                    row.Version, row.PublicTypes, row.InReferencePack,
                    row.HasImplementation, row.PackVersion))]);

    [JSExport]
    public static Task PrefetchPlatformPacks(
        string targetFramework,
        string platformVersion) =>
        DotnetInspect.Web.BrowserPlatformCatalog.PrefetchAsync(
            targetFramework, platformVersion);

    [JSExport]
    public static async Task<string> LoadRuntimePack(
        string targetFramework,
        string platformVersion)
    {
        await using BrowserPlatformScopeResolution resolution =
            await BrowserPlatformWorkspace.OpenRuntimeAsync(
                targetFramework,
                platformVersion);
        return ProjectPlatformSurface(resolution);
    }

    public static Task<string> LoadRuntimePack(
        string targetFramework) =>
        LoadRuntimePack(targetFramework, "");

    [JSExport]
    public static async Task<string> LoadRuntimePackAssembly(
        string targetFramework,
        string platformVersion,
        string assemblyFileName,
        string pack,
        string assetFileName)
    {
        await using BrowserPlatformScopeResolution resolution =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                targetFramework,
                platformVersion,
                assemblyFileName,
                pack);
        return ProjectPlatformSurface(resolution, assetFileName);
    }

    public static Task<string> LoadRuntimePackAssembly(
        string targetFramework,
        string assemblyFileName,
        string pack) =>
        LoadRuntimePackAssembly(
            targetFramework,
            "",
            assemblyFileName,
            pack,
            assemblyFileName);

    internal static string ProjectPlatformSurface(
        BrowserPlatformScopeResolution resolution,
        string? assetFileName = null)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        BrowserPlatformProjectionInfo projection =
            BrowserPlatformSurfaceProjection.Project(
                resolution.Scope,
                resolution.Participant,
                resolution.Coordinate,
                assetFileName);
        return JsonSerializer.Serialize(
            BrowserPackageWireProjection.Project(projection.Surface),
            BrowserPackageJsonContext.Default.BrowserPackageSurface);
    }
}
