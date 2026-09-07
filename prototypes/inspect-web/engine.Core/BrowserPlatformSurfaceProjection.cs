using System.Runtime.Versioning;
using DotnetInspector.Queries;

namespace InspectWeb.Engine;

internal sealed record BrowserPlatformProjectionInfo(
    BrowserPackageSurfaceInfo Surface,
    AssemblyContextApiSurfaceResult ApiSurfaces,
    WorkspaceContextMember Participant,
    RealizedMemberCoordinate.Platform Coordinate);

/// <summary>
/// Projects one source-native Platform participant through the browser's ordinary API surface.
/// </summary>
[SupportedOSPlatform("browser")]
internal static class BrowserPlatformSurfaceProjection
{
    internal static BrowserPlatformProjectionInfo Project(
        BrowserPlatformScope scope,
        WorkspaceContextMember participant,
        RealizedMemberCoordinate.Platform coordinate)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentNullException.ThrowIfNull(coordinate);

        string assembly = participant.Participant.Assembly.Identity.Name;
        AssemblyContextApiSurfaceResult surfaces =
            scope.UseParticipant(
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
                    BrowserPlatformWorkspace.Pack(coordinate.Family));
        if (projected.Assemblies.Length == 0
            && !projected.IsTruncated)
        {
            throw new InvalidOperationException(
                $"Platform assembly '{assembly}' produced no API surface. "
                + (projected.InspectionError
                    ?? "The workspace reported no failure."));
        }

        string framework = BrowserFrameworkText.Require(scope.Framework);
        var surface = new BrowserPackageSurfaceInfo(
            BrowserPlatformIdentity.PackageName,
            coordinate.Version,
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
        return new BrowserPlatformProjectionInfo(
            surface,
            surfaces,
            participant,
            coordinate);
    }
}
