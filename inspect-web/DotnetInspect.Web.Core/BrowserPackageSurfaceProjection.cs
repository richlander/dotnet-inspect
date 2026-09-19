using System.Runtime.Versioning;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;

namespace DotnetInspect.Web;

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
    internal static BrowserPackageSurfaceInfo Project(
        CompleteRestorationPackageInventory inventory,
        BrowserPackageIconPayload? icon = null)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        PackageCompileAssetSelection selection = inventory.Selection;
        string? framework =
            inventory.Package.Coordinate.Framework ?? selection.TargetFramework;
        BrowserCompileLibraryInfo compileLibrary =
            BrowserCompileLibraryProjection.Project(selection);
        BrowserSurfaceProjection.Surface projected =
            BrowserSurfaceProjection.Project(
                inventory.Surface,
                [
                    .. inventory.Libraries.Select(
                        static library => new BrowserSurfaceProjection.Participant(
                            library.Subject.Registration,
                            library.Asset.AssemblyName,
                            library.Asset.Id,
                            library.Asset.Path)),
                ]);
        if (selection.IsSelected
            && projected.Assemblies.Length == 0
            && !projected.IsTruncated)
        {
            throw new InvalidOperationException(
                $"No assembly of {inventory.Package.PackageId} "
                    + $"{inventory.Package.Coordinate.Version} produced an API surface. "
                    + (projected.InspectionError
                        ?? "The workspace reported no failure."));
        }

        string? defaultAssetId = selection.DefaultAsset?.Id;
        string? defaultAssemblyId = projected.Assemblies.FirstOrDefault(
                assembly => assembly.Id == defaultAssetId)?.Id
            ?? projected.Assemblies.FirstOrDefault()?.Id
            ?? defaultAssetId;
        return new(
            inventory.Package.PackageId,
            inventory.Package.Coordinate.Version,
            BrowserFrameworkText.Available(selection, framework),
            BrowserFrameworkText.Project(framework) ?? "",
            icon,
            defaultAssemblyId,
            compileLibrary,
            projected.Assemblies,
            projected.Types,
            projected.Accessibility,
            projected.TotalMembers,
            BrowserPackage.ProjectDocuments(
                inventory.Entries,
                inventory.Package.PackageId,
                inventory.Package.Coordinate.Version),
            projected.InspectionErrors,
            projected.InspectionError);
    }

    internal static BrowserPackageSurfaceInfo ProjectSurface(
        BrowserInspectionScope scope,
        BrowserPackageCoordinate coordinate) =>
        Project(scope, coordinate).Surface;

    internal static BrowserSurfaceProjection.Surface ProjectParticipantSurface(
        BrowserInspectionScope scope,
        BrowserWorkspaceParticipant participant) =>
        ProjectSurfaces(scope, [participant]).Surface;

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
                    BrowserFrameworkText.Available(coordinate),
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

        (AssemblyContextApiSurfaceResult surfaces,
            BrowserSurfaceProjection.Surface projected) =
            ProjectSurfaces(scope, requested);
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
                BrowserFrameworkText.Available(coordinate),
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

    internal static BrowserPackageProjectionInfo Project(
        NavigationPackageEvaluation evaluation,
        BrowserPackageCoordinate coordinate)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        ArgumentNullException.ThrowIfNull(coordinate);
        if (evaluation.Libraries.Any(
            library => !ReferenceEquals(
                library.Association.Package,
                coordinate.Root.Identity)))
        {
            throw new ArgumentException(
                "The Navigation evaluation and Browser coordinate must share "
                    + "one exact Package Root.",
                nameof(coordinate));
        }

        BrowserCompileLibraryInfo compileLibrary =
            BrowserCompileLibraryProjection.Project(coordinate.Selection);
        if (!coordinate.Selection.IsSelected)
        {
            return new BrowserPackageProjectionInfo(
                new BrowserPackageSurfaceInfo(
                    coordinate.PackageId,
                    coordinate.Version,
                    BrowserFrameworkText.Available(coordinate),
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
                ApiSurfaces: evaluation.Surface);
        }

        PackageCompileAsset defaultAsset = coordinate.DefaultAsset
            ?? throw new InvalidOperationException(
                "A selected compile-library outcome did not identify its "
                    + "default asset.");
        BrowserSurfaceProjection.Surface projected =
            BrowserSurfaceProjection.Project(
                evaluation.Surface,
                [
                    .. evaluation.Libraries.Select(
                        static library =>
                            new BrowserSurfaceProjection.Participant(
                                library.Association.Participant,
                                library.Asset.AssemblyName,
                                library.Asset.Id,
                                library.Asset.Path)),
                ]);
        if (projected.Assemblies.Length == 0 && !projected.IsTruncated)
        {
            throw new InvalidOperationException(
                $"No assembly of {coordinate.PackageId} "
                    + $"{coordinate.Version} produced an API surface. "
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
                BrowserFrameworkText.Available(coordinate),
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
            evaluation.Surface);
    }

    static (AssemblyContextApiSurfaceResult ApiSurfaces,
        BrowserSurfaceProjection.Surface Surface) ProjectSurfaces(
            BrowserInspectionScope scope,
            IReadOnlyList<BrowserWorkspaceParticipant> requested)
    {
        // Public types retain their public member lists while non-public types remain
        // reachable through the accessibility filter. Only requested participants share
        // this operation's extraction and transport budgets.
        AssemblyContextApiSurfaceResult surfaces = scope.UseSurface(group =>
            AssemblyContextApiSurfaceQuery.ExecuteBounded(
                group,
                ApiSurfaceScope.PublicWithNonPublicTypes,
                BrowserApiSurfacePolicy.Limits,
                [.. requested.Select(participant => participant.Participant)]));
        return (surfaces, BrowserSurfaceProjection.Project(
            surfaces,
            [
                .. requested.Select(participant =>
                    new BrowserSurfaceProjection.Participant(
                        participant.Participant,
                        participant.Asset.AssemblyName,
                        participant.Asset.Id,
                        participant.Asset.Path)),
            ]));
    }
}
