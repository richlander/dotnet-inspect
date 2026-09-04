using System.Runtime.Versioning;
using DotnetInspector.Packages;
using DotnetInspector.Queries;

namespace InspectWeb.Engine;

/// <summary>
/// One package coordinate's browsable surface plus the product API surface it was projected from.
/// </summary>
internal sealed record BrowserPackageProjectionInfo(
    BrowserPackageSurfaceInfo Surface,
    AssemblyContextApiSurfaceResult? ApiSurfaces);

/// <summary>
/// The shared package-surface projection. Several capability facades need the same browsable
/// surface for one exact package/version/framework workspace — the package facade publishes it,
/// the catalog facade returns it from a demo run, and the Analysis facade uses it to decide which
/// ranked members are navigable — so the projection has one implementation over one workspace.
/// </summary>
/// <remarks>
/// <c>AssemblyContextApiSurfaceQuery</c> owns every session and accessibility bucket; this
/// composes no evidence, no classification, and no ordering of its own.
/// </remarks>
[SupportedOSPlatform("browser")]
internal static class BrowserPackageSurfaceProjection
{
    internal static BrowserPackageSurfaceInfo ProjectSurface(
        BrowserInspectionScope scope,
        BrowserPackageCoordinate coordinate) =>
        Project(scope, coordinate).Surface;

    internal static BrowserPackageProjectionInfo Project(
        BrowserInspectionScope scope,
        BrowserPackageCoordinate coordinate)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(coordinate);
        BrowserCompileLibraryInfo compileLibrary =
            BrowserCompileLibraryProjection.Project(coordinate.Selection);
        if (!coordinate.Selection.IsSelected)
        {
            return new BrowserPackageProjectionInfo(
                new BrowserPackageSurfaceInfo(
                    coordinate.PackageId,
                    coordinate.Version,
                    BrowserFrameworkText.Available(coordinate.Selection),
                    BrowserFrameworkText.Active(coordinate),
                    coordinate.Package.Icon,
                    DefaultAssemblyId: null,
                    compileLibrary,
                    Assemblies: [],
                    Types: [],
                    Accessibility: [],
                    TotalMembers: 0,
                    coordinate.Package.Documents(),
                    InspectionErrors: [],
                    InspectionError: null),
                ApiSurfaces: null);
        }

        PackageCompileAsset defaultAsset = coordinate.DefaultAsset
            ?? throw new InvalidOperationException(
                "A selected compile-library outcome did not identify its default asset.");
        // Only this coordinate's assemblies are projected. A composite workspace may hold several
        // packages, and projecting all of them here materialized every other package's surface
        // only to discard it.
        BrowserWorkspaceParticipant[] requested =
        [
            .. scope.SurfaceParticipants.Where(candidate =>
                ReferenceEquals(
                    candidate.Coordinate.Root.Identity,
                    coordinate.Root.Identity)),
        ];

        // The site's default path shows public types by default and reaches non-public ones
        // through the accessibility filter, so it asks for the composed scope: a public type
        // keeps its public member list even though non-public types are present. The projection
        // runs under the browser's explicit bounds; an early stop is reported, never silent.
        AssemblyContextApiSurfaceResult surfaces = scope.UseSurface(group =>
            AssemblyContextApiSurfaceQuery.ExecuteBounded(
                group,
                ApiSurfaceScope.PublicWithNonPublicTypes,
                BrowserApiSurfacePolicy.Limits,
                [.. requested.Select(participant => participant.Participant)]));
        BrowserSurfaceProjection.Surface projected =
            BrowserSurfaceProjection.Project(
                surfaces,
                [
                    .. requested.Select(participant =>
                        new BrowserSurfaceProjection.Participant(
                            participant.Participant,
                            participant.Asset.AssemblyName,
                            participant.Asset.Id,
                            participant.Asset.Path)),
                ]);
        if (projected.Assemblies.Length == 0
            && !projected.IsTruncated)
        {
            throw new InvalidOperationException(
                $"No assembly of {coordinate.PackageId} {coordinate.Version} "
                + "produced an API surface. "
                + (projected.InspectionError
                    ?? "The workspace reported no failure."));
        }

        string defaultAssemblyId = projected.Assemblies.FirstOrDefault(
                assembly => assembly.Id.Equals(
                    defaultAsset.Id,
                    StringComparison.Ordinal))
            ?.Id
            ?? projected.Assemblies.FirstOrDefault()?.Id
            ?? defaultAsset.Id;

        return new BrowserPackageProjectionInfo(
            new BrowserPackageSurfaceInfo(
                coordinate.PackageId,
                coordinate.Version,
                BrowserFrameworkText.Available(coordinate.Selection),
                BrowserFrameworkText.Active(coordinate),
                coordinate.Package.Icon,
                defaultAssemblyId,
                compileLibrary,
                projected.Assemblies,
                projected.Types,
                projected.Accessibility,
                projected.TotalMembers,
                coordinate.Package.Documents(),
                projected.InspectionErrors,
                projected.InspectionError),
            surfaces);
    }
}
