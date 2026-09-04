using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Queries;

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
        using BrowserPlatformScopeResolution resolution =
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
        using BrowserPlatformScopeResolution resolution =
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
        WorkspaceContextMember participant = resolution.Participant;
        string assembly = participant.Participant.Assembly.Identity.Name;
        AssemblyContextApiSurfaceResult surfaces =
            resolution.Scope.UseParticipant(
                participant,
                (group, selected) =>
                    AssemblyContextApiSurfaceQuery.ExecuteBounded(
                        group,
                        ApiSurfaceScope.PublicWithNonPublicTypes,
                        BrowserApiSurfacePolicy.Limits,
                        [selected]));
        BrowserSurfaceProjection.Surface projected =
            BrowserSurfaceProjection.Project(
                surfaces,
                [
                    new BrowserSurfaceProjection.Participant(
                        participant.Participant,
                        assembly,
                        assembly,
                        $"{assembly}.dll"),
                ],
                qualifyTypeIds: true,
                platformPack:
                    BrowserPlatformWorkspace.Pack(
                        resolution.Coordinate.Family));
        if (projected.Assemblies.Length == 0
            && !projected.IsTruncated)
        {
            throw new InvalidOperationException(
                $"Platform assembly '{assembly}' produced no API surface. "
                + (projected.InspectionError
                    ?? "The workspace reported no failure."));
        }

        string framework = BrowserFrameworkText.Require(resolution.Scope.Framework);
        return JsonSerializer.Serialize(
            new BrowserPackageSurface(
                BrowserPlatformIdentity.PackageName,
                resolution.Coordinate.Version,
                [framework],
                framework,
                Icon: null,
                assembly,
                BrowserPackageWireProjection.Project(
                    BrowserCompileLibraryProjection.Selected(framework)),
                [.. projected.Assemblies.Select(BrowserPackageWireProjection.Project)],
                [.. projected.Types.Select(BrowserPackageWireProjection.Project)],
                [.. projected.Accessibility.Select(BrowserPackageWireProjection.Project)],
                projected.TotalMembers,
                Documents: [],
                InspectionErrors: projected.InspectionErrors,
                InspectionError: projected.InspectionError),
            BrowserPackageJsonContext.Default.BrowserPackageSurface);
    }
}
