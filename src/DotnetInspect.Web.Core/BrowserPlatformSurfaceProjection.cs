using System.Runtime.Versioning;
using DotnetInspector.Libraries;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Sections;

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
    internal static async Task<LibraryDocument> ReadForwardersAsync(
        BrowserPlatformScope scope,
        WorkspaceContextMember participant,
        ICollection<string> cleanupFailures,
        CancellationToken cancellationToken = default)
    {
        AssemblyContextLibraryInspectionRun<InspectionEnvelope<LibraryInspectionOutcome>> run =
            await AssemblyContextLibraryInspection.ExecuteAsync(
                scope.UseParticipant(
                    participant,
                    (group, selected) =>
                        AssemblyContextLibraryAdapter.MaterializeAsync(
                            group,
                            selected,
                            AssemblyContextLibraryRole.ApiOnly,
                            new(
                                BrowserInspectionScope.MaxRetainedImageBytes,
                                BrowserInspectionScope.MaxRetainedImageBytes),
                            cancellationToken)),
                (reference, owner) =>
                    AssemblyContextLibraryInspection.ExecuteOperation(
                        reference,
                        owner,
                        lease =>
                            LibraryInspectionOperation.Execute(
                                new(
                                    reference,
                                    new(
                                        new(
                                            LibraryTypeAccessibility.Public,
                                            count: null,
                                            new(BrowserApiSurfacePolicy
                                                .MaxTypeForwarders),
                                            LibraryTypeDeclarationSelection
                                                .Forwarders),
                                        BrowserApiSurfacePolicy
                                            .ExtractionBounds)),
                                lease,
                                cancellationToken)),
                cleanupFailures);
        cancellationToken.ThrowIfCancellationRequested();
        if (run.Result?.Content is not LibraryInspectionOutcome.Available available)
        {
            throw new InvalidOperationException(
                run.Failure
                    ?? $"Forwarded Type inventory could not inspect the selected Library ({run.Result?.Content}).");
        }
        return available.Document;
    }

    internal static BrowserPackageSurfaceInfo Project(
        CompleteRestorationPlatformInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        BrowserSurfaceProjection.Surface projected =
            BrowserSurfaceProjection.Project(
                inventory.Surface,
                [
                    .. inventory.Libraries.Select(
                        static library => new BrowserSurfaceProjection.Participant(
                            library.Subject.Registration,
                            library.Subject.Identity.Name,
                            library.Subject.Identity.Name,
                            library.AssetFileName
                                ?? $"{library.Subject.Identity.Name}.dll")),
                ],
                qualifyTypeIds: true,
                platformPack: BrowserPlatformWorkspace.Pack(inventory.Family));
        if (projected.Assemblies.Length == 0 && !projected.IsTruncated)
        {
            throw new InvalidOperationException(
                $"Platform '{inventory.Family}' produced no API surface. "
                    + (projected.InspectionError
                        ?? "The workspace reported no failure."));
        }

        string framework = BrowserFrameworkText.Require(inventory.Framework);
        return new(
            BrowserPlatformIdentity.PackageName,
            inventory.Version,
            [framework],
            framework,
            Icon: null,
            projected.Assemblies.FirstOrDefault()?.Id,
            BrowserCompileLibraryProjection.Selected(framework),
            projected.Assemblies,
            projected.Types,
            projected.TypeKinds,
            projected.TypeTraits,
            projected.Accessibility,
            projected.TotalMembers,
            Documents: [],
            InspectionErrors: projected.InspectionErrors,
            InspectionError: projected.InspectionError);
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
            projected.TypeKinds,
            projected.TypeTraits,
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
