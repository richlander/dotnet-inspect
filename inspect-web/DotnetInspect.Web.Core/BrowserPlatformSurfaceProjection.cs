using System.Runtime.Versioning;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;

namespace DotnetInspect.Web;

internal sealed record BrowserPlatformProjectionInfo(
    BrowserPackageSurfaceInfo Surface,
    AssemblyContextApiSurfaceResult ApiSurfaces,
    WorkspaceContextMember Participant,
    RealizedMemberCoordinate.Platform Coordinate);

internal sealed record BrowserPlatformSurfaceProjectionValue(
    BrowserPackageSurfaceInfo Surface,
    AssemblyContextApiSurfaceResult ApiSurfaces);

internal abstract record BrowserPlatformSurfaceProjectionResult
{
    private protected BrowserPlatformSurfaceProjectionResult()
    {
    }

    internal sealed record Projected(
        BrowserPlatformSurfaceProjectionValue Projection) :
        BrowserPlatformSurfaceProjectionResult;

    internal sealed record Unavailable(
        BrowserPackageSurfaceInfo Surface) :
        BrowserPlatformSurfaceProjectionResult;
}

/// <summary>
/// Projects one source-native Platform participant through the browser's ordinary API surface.
/// </summary>
[SupportedOSPlatform("browser")]
internal static class BrowserPlatformSurfaceProjection
{
    internal static BrowserPackageSurfaceInfo Project(
        CompleteRestorationPlatformEvaluation platform)
    {
        ArgumentNullException.ThrowIfNull(platform);
        BrowserSurfaceProjection.Surface projected =
            BrowserSurfaceProjection.Project(
                platform.Surface,
                [
                    .. platform.Members.Select(
                        static member =>
                            new BrowserSurfaceProjection.Participant(
                                member.Participant,
                                member.Participant.Assembly.Identity.Name,
                                member.Participant.Assembly.Identity.Name,
                                member.Participant.Assembly.AssetFileName
                                    ?? $"{member.Participant.Assembly.Identity.Name}.dll")),
                ],
                qualifyTypeIds: true,
                platformPack:
                    BrowserPlatformWorkspace.Pack(platform.Family));
        string framework = BrowserFrameworkText.Require(platform.Framework);
        string? preferredAssembly = platform.Family switch
        {
            "runtime" => "System.Private.CoreLib",
            "aspnetcore" => "Microsoft.AspNetCore",
            _ => null,
        };
        string? defaultAssemblyId = projected.Assemblies.FirstOrDefault(
                assembly => string.Equals(
                    assembly.Id,
                    preferredAssembly,
                    StringComparison.Ordinal))
            ?.Id
            ?? projected.Assemblies.FirstOrDefault()?.Id;
        return new BrowserPackageSurfaceInfo(
            platform.Family switch
            {
                "runtime" => BrowserPlatformIdentity.PackageName,
                "aspnetcore" => "Microsoft.AspNetCore.App",
                _ => throw new InvalidOperationException(
                    "The retained Platform projection has an unknown family."),
            },
            platform.Version,
            [framework],
            framework,
            Icon: null,
            defaultAssemblyId,
            BrowserCompileLibraryProjection.Selected(framework),
            projected.Assemblies,
            projected.Types,
            projected.Accessibility,
            projected.TotalMembers,
            Documents: [],
            projected.InspectionErrors,
            projected.InspectionError);
    }

    internal static BrowserPlatformProjectionInfo Project(
        BrowserPlatformScope scope,
        WorkspaceContextMember participant,
        RealizedMemberCoordinate.Platform coordinate,
        string? assetFileName = null)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentNullException.ThrowIfNull(coordinate);

        BrowserPlatformSurfaceProjectionResult result =
            scope.UseParticipant(
                participant,
                (group, selected) => TryProject(
                    group,
                    selected,
                    coordinate.Family,
                    coordinate.Version,
                    scope.Framework,
                    assetFileName));
        return result switch
        {
            BrowserPlatformSurfaceProjectionResult.Projected projected =>
                new BrowserPlatformProjectionInfo(
                    projected.Projection.Surface,
                    projected.Projection.ApiSurfaces,
                    participant,
                    coordinate),
            BrowserPlatformSurfaceProjectionResult.Unavailable unavailable =>
                throw new InvalidOperationException(
                    $"Platform assembly '{participant.Participant.Assembly.Identity.Name}' produced no API surface. "
                    + (unavailable.Surface.InspectionError
                        ?? "The workspace reported no failure.")),
            _ => throw new InvalidOperationException(
                "Platform surface projection returned an unknown result."),
        };
    }

    internal static BrowserPlatformSurfaceProjectionResult TryProject(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        string family,
        string version,
        string framework,
        string? assetFileName = null)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(framework);

        string assembly = participant.Assembly.Identity.Name;
        AssemblyContextApiSurfaceResult surfaces =
            AssemblyContextApiSurfaceQuery.ExecuteBounded(
                group,
                ApiSurfaceScope.PublicWithNonPublicTypes,
                BrowserApiSurfacePolicy.Limits,
                [participant]);
        BrowserSurfaceProjection.Surface projected =
            BrowserSurfaceProjection.Project(
                surfaces,
                [
                    new BrowserSurfaceProjection.Participant(
                        participant,
                        assembly,
                        assembly,
                        assetFileName ?? $"{assembly}.dll"),
                ],
                qualifyTypeIds: true,
                platformPack:
                    BrowserPlatformWorkspace.Pack(family));

        framework = BrowserFrameworkText.Require(framework);
        var surface = new BrowserPackageSurfaceInfo(
            BrowserPlatformIdentity.PackageName,
            version,
            [framework],
            framework,
            Icon: null,
            assembly,
            BrowserCompileLibraryProjection.Selected(framework),
            projected.Assemblies,
            projected.Types,
            projected.Accessibility,
            projected.TotalMembers,
            Documents: [],
            InspectionErrors: projected.InspectionErrors,
            InspectionError: projected.InspectionError);
        if (projected.Assemblies.Length == 0
            && !projected.IsTruncated)
        {
            return new BrowserPlatformSurfaceProjectionResult.Unavailable(
                surface);
        }
        return new BrowserPlatformSurfaceProjectionResult.Projected(
            new BrowserPlatformSurfaceProjectionValue(
                surface,
                surfaces));
    }
}
