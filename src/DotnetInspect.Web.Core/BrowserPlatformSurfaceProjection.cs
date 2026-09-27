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
        CancellationToken cancellationToken = default)
    {
        AssemblyContextLibraryAdapterResult materialization =
            await scope.UseParticipant(
                participant,
                (group, selected) =>
                    AssemblyContextLibraryAdapter.MaterializeAsync(
                        group,
                        selected,
                        AssemblyContextLibraryRole.ApiOnly,
                        new(
                            BrowserInspectionScope.MaxRetainedImageBytes,
                            BrowserInspectionScope.MaxRetainedImageBytes),
                        cancellationToken));
        if (materialization is not AssemblyContextLibraryAdapterResult.Completed completed)
        {
            throw new InvalidOperationException(
                $"Forwarded Type inventory could not acquire the selected Library ({materialization.GetType().Name}).");
        }

        InspectionEnvelope<LibraryInspectionOutcome> inspection;
        try
        {
            if (completed.Owner.IssueOperationLease(completed.Reference)
                is not LibraryOperationLeaseIssueOutcome.Issued issued)
            {
                throw new InvalidOperationException(
                    "The selected Library could not issue its forwarding inspection lease.");
            }
            inspection = LibraryInspectionOperation.Execute(
                new(
                    completed.Reference,
                    new(
                        new(
                            LibraryTypeAccessibility.Public,
                            count: null,
                            new(BrowserApiSurfacePolicy.MaxTypeForwarders),
                            LibraryTypeDeclarationSelection.Forwarders),
                        BrowserApiSurfacePolicy.ExtractionBounds)),
                issued.Lease,
                cancellationToken);
        }
        finally
        {
            try
            {
                await completed.Owner.DisposeAsync();
            }
            finally
            {
                await completed.Artifacts.DisposeAsync();
            }
            if (completed.Owner.CleanupFailures.Count != 0
                || completed.Owner.ReleaseFailures.Count != 0
                || completed.Artifacts.CleanupFailures.Count != 0)
            {
                throw new AggregateException(
                    "The forwarded Type inspection could not release its Library content.",
                    completed.Owner.CleanupFailures
                        .Concat(completed.Owner.ReleaseFailures.Select(failure => failure.Failure))
                        .Concat(completed.Artifacts.CleanupFailures));
            }
        }

        return inspection.Content switch
        {
            LibraryInspectionOutcome.Available available => available.Document,
            _ => throw new InvalidOperationException(
                $"Forwarded Type inventory could not inspect the selected Library ({inspection.Content})."),
        };
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
