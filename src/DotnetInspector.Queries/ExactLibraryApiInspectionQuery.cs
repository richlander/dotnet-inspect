using System.Collections.Immutable;

using DotnetInspector.Packages;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

public enum ExactLibraryApiSelectionKind
{
    Query,
    AssetId,
}

public sealed record ExactLibraryApiInspectionRequest
{
    public ExactLibraryApiInspectionRequest(
        string packageId,
        string packageVersion,
        string targetFramework,
        string library,
        ExactLibraryApiSelectionKind selectionKind =
            ExactLibraryApiSelectionKind.Query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFramework);
        ArgumentException.ThrowIfNullOrWhiteSpace(library);
        if (targetFramework.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Exact Library API inspection requires one target framework.",
                nameof(targetFramework));
        }
        if (!Enum.IsDefined(selectionKind))
            throw new ArgumentOutOfRangeException(nameof(selectionKind));

        PackageId = packageId;
        PackageVersion = packageVersion;
        TargetFramework = targetFramework;
        Library = library;
        SelectionKind = selectionKind;
    }

    public string PackageId { get; }
    public string PackageVersion { get; }
    public string TargetFramework { get; }
    public string Library { get; }
    public ExactLibraryApiSelectionKind SelectionKind { get; }
}

public enum ExactLibraryApiInspectionOutcome
{
    Available,
    NotFound,
    Ambiguous,
    Unavailable,
}

public enum ExactLibraryApiInspectionFailureKind
{
    ContextLoad,
    PackageMismatch,
    CompileSelectionUnavailable,
    LibraryNotFound,
    LibraryAmbiguous,
    ParticipantUnavailable,
    InspectionIncomplete,
    ProjectionTruncated,
}

public sealed record ExactLibraryApiInspectionFailure(
    ExactLibraryApiInspectionFailureKind Kind,
    string Detail,
    AssemblyReferenceIdentity? SubjectAssembly = null);

public sealed record ExactLibraryApiAsset(
    string Id,
    string Path,
    string AssemblyName,
    string TargetFramework,
    PackageCompileAssetKind Kind);

public sealed record ExactLibraryApiAssemblyIdentity(
    AssemblyReferenceIdentity Identity,
    Guid ModuleVersionId);

public sealed record ExactLibraryApiSourceCoordinate(
    string PackageId,
    string PackageVersion,
    string Producer,
    string? Framework);

public sealed record ExactLibraryApiInventory(
    int PublicTypeCount,
    int PublicMemberCount,
    int PublicMethodCount,
    int PublicPropertyCount,
    ImmutableArray<ApiFacetDescriptor> TypeKinds,
    ImmutableArray<ApiNamespaceDescriptor> Namespaces);

public sealed record ExactLibraryApiInspectionResult(
    ExactLibraryApiInspectionOutcome Outcome,
    string PackageId,
    string PackageVersion,
    string RequestedTargetFramework,
    string RequestedLibrary,
    ExactLibraryApiSourceCoordinate? Source,
    ExactLibraryApiAsset? Asset,
    ExactLibraryApiAssemblyIdentity? Assembly,
    ExactLibraryApiInventory? Inventory,
    ApiSurfaceProjectionTruncation? Truncation,
    ImmutableArray<ExactLibraryApiInspectionFailure> Failures,
    bool IsComplete)
{
    public bool IsAvailable =>
        Outcome == ExactLibraryApiInspectionOutcome.Available
        && Inventory is not null;
}

public sealed record ExactLibraryApiQueryExecution(
    ExactLibraryApiInspectionResult Result,
    ApiSurface? Surface);

public static class ExactLibraryApiInspectionQuery
{
    public static ExactLibraryApiQueryExecution Execute(
        PackageRootBinding package,
        PackageAssemblyContextRealization realization,
        ExactLibraryApiInspectionRequest request,
        ApiSurfaceProjectionLimits limits)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(realization);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(limits);

        PackageRootRealization root = package.Root;
        if (!root.PackageId.Equals(
                request.PackageId,
                StringComparison.OrdinalIgnoreCase)
            || !root.PackageVersion.Equals(
                request.PackageVersion,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                root.RequestedTargetFramework,
                request.TargetFramework,
                StringComparison.OrdinalIgnoreCase))
        {
            return Unavailable(
                request,
                ExactLibraryApiInspectionFailureKind.PackageMismatch,
                "The package Root does not match the exact Library API request.",
                package);
        }

        PackageCompileAssetSelection selection = root.AssetSelection;
        if (!selection.IsSelected)
        {
            return Unavailable(
                request,
                ExactLibraryApiInspectionFailureKind.CompileSelectionUnavailable,
                selection.Message
                    ?? "The package has no selected compile-asset set.",
                package);
        }

        IReadOnlyList<PackageCompileAsset> matches =
            SelectAssets(selection, request);
        if (matches.Count == 0)
        {
            return new ExactLibraryApiQueryExecution(
                new ExactLibraryApiInspectionResult(
                    ExactLibraryApiInspectionOutcome.NotFound,
                    request.PackageId,
                    request.PackageVersion,
                    request.TargetFramework,
                    request.Library,
                    Source(package),
                    null,
                    null,
                    null,
                    null,
                    [
                        new ExactLibraryApiInspectionFailure(
                            ExactLibraryApiInspectionFailureKind.LibraryNotFound,
                            $"Library '{request.Library}' not found in package."),
                    ],
                    IsComplete: false),
                Surface: null);
        }
        if (matches.Count > 1)
        {
            return new ExactLibraryApiQueryExecution(
                new ExactLibraryApiInspectionResult(
                    ExactLibraryApiInspectionOutcome.Ambiguous,
                    request.PackageId,
                    request.PackageVersion,
                    request.TargetFramework,
                    request.Library,
                    Source(package),
                    null,
                    null,
                    null,
                    null,
                    [
                        new ExactLibraryApiInspectionFailure(
                            ExactLibraryApiInspectionFailureKind.LibraryAmbiguous,
                            $"Library '{request.Library}' matches multiple compile assets: "
                            + string.Join(
                                ", ",
                                matches
                                    .Select(asset => asset.Path)
                                    .Order(StringComparer.Ordinal))
                            + "."),
                    ],
                    IsComplete: false),
                Surface: null);
        }

        PackageCompileAsset asset = matches[0];
        PackageAssemblyRoleParticipant[] participants =
        [
            .. realization.SurfaceParticipants.Where(candidate =>
                ReferenceEquals(candidate.Package, root.Identity)
                && candidate.Asset.Id.Equals(
                    asset.Id,
                    StringComparison.Ordinal)),
        ];
        if (participants.Length != 1)
        {
            return Unavailable(
                request,
                ExactLibraryApiInspectionFailureKind.ParticipantUnavailable,
                participants.Length == 0
                    ? $"The selected compile asset '{asset.Id}' has no surface participant."
                    : $"The selected compile asset '{asset.Id}' has multiple surface participants.",
                package,
                asset);
        }

        PackageAssemblyRoleParticipant selected = participants[0];
        AssemblyContextApiSurfaceResult projection =
            AssemblyContextApiSurfaceQuery.ExecuteBoundedResolved(
                realization.SurfaceGroup,
                ApiSurfaceScope.Public,
                limits,
                [selected.Participant]);
        if (projection.Truncation is { } truncation)
        {
            return Unavailable(
                request,
                ExactLibraryApiInspectionFailureKind.ProjectionTruncated,
                $"The exact Library API projection exceeded the {truncation.Limit} bound "
                + $"of {truncation.Bound}.",
                package,
                asset,
                truncation);
        }

        if (projection.Assemblies.Assemblies.Length != 1)
        {
            return Unavailable(
                request,
                ExactLibraryApiInspectionFailureKind.ParticipantUnavailable,
                "The exact Library API projection did not return one participant.",
                package,
                asset);
        }
        AssemblyContextEntry<AssemblyApiSurface> entry =
            projection.Assemblies.Assemblies[0];
        if (entry is not AssemblyContextEntry<AssemblyApiSurface>.Available available)
        {
            string detail = entry switch
            {
                AssemblyContextEntry<AssemblyApiSurface>.Rejected rejected =>
                    rejected.Failure.Detail,
                AssemblyContextEntry<AssemblyApiSurface>.Failed failed =>
                    failed.Error.Message,
                _ => "The selected Library participant could not be projected.",
            };
            return Unavailable(
                request,
                ExactLibraryApiInspectionFailureKind.ParticipantUnavailable,
                detail,
                package,
                asset);
        }

        ApiSurface surface = available.Value.Surface;
        surface.Name = request.PackageId;
        surface.Version = request.PackageVersion;
        surface.Source = "NuGet";
        surface.Library = asset.AssemblyName;
        surface.Tfm = asset.TargetFramework;

        var failures = available.Value.InspectionFailures
            .Select(failure => new ExactLibraryApiInspectionFailure(
                ExactLibraryApiInspectionFailureKind.InspectionIncomplete,
                $"{failure.Operation}: {failure.Kind}: {failure.Detail}",
                failure.SubjectAssembly))
            .ToImmutableArray();
        ApiTypeInventoryResult inventory = ApiInventoryQuery.Types(surface);
        return new ExactLibraryApiQueryExecution(
            new ExactLibraryApiInspectionResult(
                ExactLibraryApiInspectionOutcome.Available,
                request.PackageId,
                request.PackageVersion,
                request.TargetFramework,
                request.Library,
                Source(package),
                Asset(asset),
                AssemblyIdentity(
                    realization.SurfaceGroup,
                    selected.Participant),
                new ExactLibraryApiInventory(
                    surface.PublicTypeCount,
                    surface.Types.Sum(type => type.Members.Count),
                    surface.PublicMethodCount,
                    surface.PublicPropertyCount,
                    [.. inventory.KindFacets],
                    [.. ApiInventoryQuery.Namespaces(surface)]),
                null,
                failures,
                IsComplete: available.Value.InspectionFailures.All(failure =>
                    failure.Operation == ApiSurface.ConstraintResolutionOperation)),
            surface);
    }

    static IReadOnlyList<PackageCompileAsset> SelectAssets(
        PackageCompileAssetSelection selection,
        ExactLibraryApiInspectionRequest request)
    {
        if (request.SelectionKind == ExactLibraryApiSelectionKind.AssetId)
        {
            PackageCompileAsset? asset = selection.FindAsset(request.Library);
            return asset is null ? [] : [asset];
        }

        string normalized = request.Library.Replace('\\', '/');
        string normalizedPath = normalized.EndsWith(
            ".dll",
            StringComparison.OrdinalIgnoreCase)
            ? normalized
            : normalized + ".dll";
        PackageCompileAsset? exact = selection.Assets.FirstOrDefault(asset =>
            asset.Path.Equals(normalizedPath, StringComparison.Ordinal));
        if (exact is not null)
            return [exact];
        if (normalized.Contains('/'))
            return [];

        string leaf = Path.GetFileName(normalized);
        string bareName = leaf.EndsWith(
            ".dll",
            StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(leaf)
            : leaf;
        string fileName = leaf.EndsWith(
            ".dll",
            StringComparison.OrdinalIgnoreCase)
            ? leaf
            : bareName + ".dll";
        return selection.Assets
            .Where(asset =>
                asset.AssemblyName.Equals(
                    fileName,
                    StringComparison.OrdinalIgnoreCase)
                || Path.GetFileNameWithoutExtension(asset.AssemblyName).Equals(
                    bareName,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    static ExactLibraryApiQueryExecution Unavailable(
        ExactLibraryApiInspectionRequest request,
        ExactLibraryApiInspectionFailureKind kind,
        string detail,
        PackageRootBinding? package = null,
        PackageCompileAsset? asset = null,
        ApiSurfaceProjectionTruncation? truncation = null) =>
        new(
            new ExactLibraryApiInspectionResult(
                ExactLibraryApiInspectionOutcome.Unavailable,
                request.PackageId,
                request.PackageVersion,
                request.TargetFramework,
                request.Library,
                package is null ? null : Source(package),
                asset is null ? null : Asset(asset),
                null,
                null,
                truncation,
                [new ExactLibraryApiInspectionFailure(kind, detail)],
                IsComplete: false),
            Surface: null);

    static ExactLibraryApiSourceCoordinate Source(
        PackageRootBinding package) =>
        new(
            package.Coordinate.PackageId,
            package.Coordinate.Version,
            package.Coordinate.Producer,
            package.Coordinate.Framework);

    static ExactLibraryApiAsset Asset(PackageCompileAsset asset) =>
        new(
            asset.Id,
            asset.Path,
            asset.AssemblyName,
            asset.TargetFramework,
            asset.Kind);

    static ExactLibraryApiAssemblyIdentity AssemblyIdentity(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant)
    {
        AssemblyImageAccessResult<Guid> access =
            group.UseAssemblySession(
                participant.Assembly,
                static session => session.ModuleVersionId());
        return access switch
        {
            AssemblyImageAccessResult<Guid>.Available available =>
                new ExactLibraryApiAssemblyIdentity(
                    participant.Assembly.Identity,
                    available.Value),
            AssemblyImageAccessResult<Guid>.Rejected rejected =>
                throw new InvalidOperationException(
                    "The exact Library assembly identity could not be read: "
                    + rejected.Failure.Detail),
            _ => throw new InvalidOperationException(
                "The exact Library assembly identity could not be read."),
        };
    }
}
