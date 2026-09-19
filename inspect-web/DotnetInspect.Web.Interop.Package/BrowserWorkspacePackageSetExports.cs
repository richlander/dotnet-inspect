using System.Collections.Immutable;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Ecosystems;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.SourceSelection;

namespace DotnetInspect.Web.Interop.Package;

internal static class BrowserWorkspacePackageSetOperations
{
    internal const int CatalogVersion = 1;
    internal const string PackageGroupSourceKind = "PackageGroup";
    internal const string CompletedKind = "Completed";
    internal const string CapacityRejectedKind = "CapacityRejected";

    internal static BrowserWorkspacePackageSetCatalog PackageSets() =>
        new(
            CatalogVersion,
            [
                .. PackageSetCatalog.Discover().Select(Declare),
            ]);

    internal static BrowserWorkspacePackageSetPlan Plan(
        IReadOnlyList<BrowserWorkspacePackage> workspace,
        int occupiedCoordinateCount,
        string packageSetId)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (occupiedCoordinateCount < workspace.Count
            || occupiedCoordinateCount > WorkspaceScopeLimits.DefaultMaxPackages)
        {
            throw new ArgumentOutOfRangeException(
                nameof(occupiedCoordinateCount));
        }

        PackageSetDescriptor descriptor = Resolve(packageSetId);
        SourceSelector.PackageGroup group = DeclareGroup(descriptor);
        int availableSlots =
            WorkspaceScopeLimits.DefaultMaxPackages - occupiedCoordinateCount;
        if (group.Coordinates.Count > availableSlots)
        {
            return new BrowserWorkspacePackageSetPlan.CapacityRejected(
                descriptor,
                group.Coordinates.Count,
                availableSlots);
        }

        ImmutableArray<BrowserPackageRequest> requests =
        [
            .. workspace.Select(package =>
                new BrowserPackageRequest(
                    package.Package,
                    package.Version,
                    string.IsNullOrWhiteSpace(package.Framework)
                        ? null
                        : package.Framework)),
            .. group.Coordinates.Select(coordinate =>
                new BrowserPackageRequest(
                    coordinate.PackageId,
                    coordinate.Version,
                    coordinate.Framework)),
        ];
        return new BrowserWorkspacePackageSetPlan.Ready(
            descriptor,
            group.Coordinates.Count,
            availableSlots,
            requests);
    }

    [SupportedOSPlatform("browser")]
    internal static async Task<BrowserWorkspacePackageSetRealization>
        RealizeAsync(
            IReadOnlyList<BrowserWorkspacePackage> workspace,
            int occupiedCoordinateCount,
            string packageSetId)
    {
        BrowserWorkspacePackageSetPlan plan =
            Plan(workspace, occupiedCoordinateCount, packageSetId);
        if (plan
            is BrowserWorkspacePackageSetPlan.CapacityRejected rejected)
        {
            return new BrowserWorkspacePackageSetRealization(
                CapacityRejectedKind,
                rejected.Descriptor.Id.Value,
                rejected.Descriptor.Title,
                rejected.RequestedPackageCount,
                rejected.AvailableSlots,
                []);
        }

        var ready = (BrowserWorkspacePackageSetPlan.Ready)plan;
        return await BrowserPackageWorkspace.RunPackageOperationAsync(
            async deadline =>
            {
                await using BrowserScopeResolution resolution =
                    await BrowserPackageWorkspace.ResolveAndOpenScopeAsync(
                        ready.Requests,
                        deadline.Token).ConfigureAwait(false);
                BrowserInspectionScope scope = resolution.Scope;
                ImmutableArray<BrowserPackageSurface> packages =
                [
                    .. resolution.RequestedCoordinates.Select(coordinate =>
                        BrowserPackageWireProjection.Project(
                            BrowserPackageSurfaceProjection.ProjectSurface(
                                scope,
                                coordinate))),
                ];
                return new BrowserWorkspacePackageSetRealization(
                    CompletedKind,
                    ready.Descriptor.Id.Value,
                    ready.Descriptor.Title,
                    ready.RequestedPackageCount,
                    ready.AvailableSlots,
                    packages);
            },
            BrowserPackageWorkspace.PackageChangesOperationTimeout);
    }

    private static BrowserWorkspacePackageSetDescriptor Declare(
        PackageSetDescriptor descriptor)
    {
        SourceSelector.PackageGroup group = DeclareGroup(descriptor);
        ImmutableArray<BrowserWorkspacePackageSetCoordinate> coordinates =
        [
            .. group.Coordinates.Select(Project),
        ];
        return new BrowserWorkspacePackageSetDescriptor(
            descriptor.Id.Value,
            descriptor.Title,
            descriptor.Summary,
            descriptor.Order,
            PackageGroupSourceKind,
            coordinates);
    }

    private static SourceSelector.PackageGroup DeclareGroup(
        PackageSetDescriptor descriptor)
    {
        SourceIntent intent = SourceIntent.Create(
        [
            new SourceSelector.PackageGroup(descriptor.Members),
        ]);
        return intent.Selectors.Single() as SourceSelector.PackageGroup
            ?? throw new InvalidOperationException(
                "A browser package set must declare one package group.");
    }

    private static PackageSetDescriptor Resolve(string packageSetId)
    {
        if (!PackageSetId.TryCreate(
                packageSetId,
                out PackageSetId? id))
        {
            throw new ArgumentException(
                "A canonical package-set identity is required.",
                nameof(packageSetId));
        }

        return PackageSetCatalog.Lookup(id) switch
        {
            PackageSetLookupResult.Known known => known.Descriptor,
            PackageSetLookupResult.Unknown =>
                throw new ArgumentException(
                    "The package-set identity is not registered.",
                    nameof(packageSetId)),
            _ => throw new InvalidOperationException(
                "Package-set lookup returned an unknown result."),
        };
    }

    private static BrowserWorkspacePackageSetCoordinate Project(
        PackageCoordinate coordinate) =>
        new(
            coordinate.PackageId,
            coordinate.Version,
            coordinate.Framework,
            coordinate.RuntimeIdentifier);
}

[SupportedOSPlatform("browser")]
public static partial class PackageExports
{
    [JSExport]
    public static string ListWorkspacePackageSets() =>
        JsonSerializer.Serialize(
            BrowserWorkspacePackageSetOperations.PackageSets(),
            BrowserPackageJsonContext.Default
                .BrowserWorkspacePackageSetCatalog);

    [JSExport]
    public static async Task<string> RealizeWorkspacePackageSet(
        string workspaceJson,
        int occupiedCoordinateCount,
        string packageSetId)
    {
        BrowserWorkspacePackage[] workspace =
            JsonSerializer.Deserialize(
                workspaceJson,
                BrowserPackageJsonContext.Default
                    .BrowserWorkspacePackageArray)
            ?? [];
        BrowserWorkspacePackageSetRealization result =
            await BrowserWorkspacePackageSetOperations.RealizeAsync(
                workspace,
                occupiedCoordinateCount,
                packageSetId);
        return JsonSerializer.Serialize(
            result,
            BrowserPackageJsonContext.Default
                .BrowserWorkspacePackageSetRealization);
    }
}

internal abstract record BrowserWorkspacePackageSetPlan(
    PackageSetDescriptor Descriptor,
    int RequestedPackageCount,
    int AvailableSlots)
{
    internal sealed record CapacityRejected(
        PackageSetDescriptor Descriptor,
        int RequestedPackageCount,
        int AvailableSlots)
        : BrowserWorkspacePackageSetPlan(
            Descriptor,
            RequestedPackageCount,
            AvailableSlots);

    internal sealed record Ready(
        PackageSetDescriptor Descriptor,
        int RequestedPackageCount,
        int AvailableSlots,
        ImmutableArray<BrowserPackageRequest> Requests)
        : BrowserWorkspacePackageSetPlan(
            Descriptor,
            RequestedPackageCount,
            AvailableSlots);
}
