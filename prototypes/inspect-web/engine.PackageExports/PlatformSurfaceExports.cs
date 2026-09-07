using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using InspectWeb.Engine;
using InspectWeb.Engine.PackageFacade;

/// <summary>
/// Platform acquisition. A runtime pack load returns the same browsable package surface as a
/// NuGet package load, so both live in the package facade.
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class PackageExports
{
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
        string pack)
    {
        await using BrowserPlatformScopeResolution resolution =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                targetFramework,
                platformVersion,
                assemblyFileName,
                pack);
        return ProjectPlatformSurface(resolution);
    }

    public static Task<string> LoadRuntimePackAssembly(
        string targetFramework,
        string assemblyFileName,
        string pack) =>
        LoadRuntimePackAssembly(
            targetFramework,
            "",
            assemblyFileName,
            pack);

    internal static string ProjectPlatformSurface(
        BrowserPlatformScopeResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        BrowserPlatformProjectionInfo projection =
            BrowserPlatformSurfaceProjection.Project(
                resolution.Scope,
                resolution.Participant,
                resolution.Coordinate);
        return JsonSerializer.Serialize(
            BrowserPackageWireProjection.Project(projection.Surface),
            BrowserPackageJsonContext.Default.BrowserPackageSurface);
    }
}
