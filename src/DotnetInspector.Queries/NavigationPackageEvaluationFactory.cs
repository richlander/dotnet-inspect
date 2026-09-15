using System.Collections.Immutable;

namespace DotnetInspector.Queries;

/// <summary>
/// Builds the bounded Navigation evidence for one exact ready Package
/// occurrence from its owner-issued artifact realization.
/// </summary>
public static class NavigationPackageEvaluationFactory
{
    public static ApiSurfaceProjectionLimits DefaultSurfaceLimits { get; } =
        new(
            maxParticipants: WorkspaceScopeLimits.DefaultMaxPackages,
            maxTypes: 10_000,
            maxMembers: 100_000,
            maxInspectionFailures: 1_000,
            maxTypeForwarders: 10_000,
            maxMetadataRows: 1_000_000,
            maxRetainedTextCharacters: 8_000_000);

    public static NavigationPackageEvaluation Create(
        WorkspacePackageOccurrenceDescriptor occurrence,
        PackageRootBinding binding,
        PackageAssemblyContextRealization realization,
        CancellationToken cancellationToken = default) =>
        Create(
            occurrence,
            binding,
            realization,
            DefaultSurfaceLimits,
            cancellationToken);

    public static NavigationPackageEvaluation Create(
        WorkspacePackageOccurrenceDescriptor occurrence,
        PackageRootBinding binding,
        PackageAssemblyContextRealization realization,
        ApiSurfaceProjectionLimits surfaceLimits,
        CancellationToken cancellationToken = default)
        => Create(
            occurrence,
            binding,
            realization,
            ApiSurfaceScope.Public,
            surfaceLimits,
            cancellationToken);

    public static NavigationPackageEvaluation Create(
        WorkspacePackageOccurrenceDescriptor occurrence,
        PackageRootBinding binding,
        PackageAssemblyContextRealization realization,
        ApiSurfaceScope surfaceScope,
        ApiSurfaceProjectionLimits surfaceLimits,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(realization);
        ArgumentNullException.ThrowIfNull(surfaceLimits);
        if (!Enum.IsDefined(surfaceScope))
            throw new ArgumentOutOfRangeException(nameof(surfaceScope));
        cancellationToken.ThrowIfCancellationRequested();
        if (!realization.HasAssemblyContexts)
        {
            return new NavigationPackageEvaluation(
                occurrence,
                binding,
                [],
                new AssemblyContextApiSurfaceResult(
                    new AssemblyContextResult<AssemblyApiSurface>([]),
                    [],
                    Truncation: null));
        }

        RealizedMemberCoordinate.Package coordinate =
            occurrence.Occurrence.Package.Coordinate;
        ImmutableArray<NavigationLibraryEvaluation> libraries =
        [
            .. realization.SurfaceParticipants.Select(participant =>
                new NavigationLibraryEvaluation(
                    coordinate,
                    participant)),
        ];
        AssemblyContextApiSurfaceResult surface =
            AssemblyContextApiSurfaceQuery.ExecuteBounded(
                realization.SurfaceGroup,
                surfaceScope,
                surfaceLimits,
                [
                    .. libraries.Select(
                        static library =>
                            library.Library.Participant),
                ]);
        return new NavigationPackageEvaluation(
            occurrence,
            binding,
            libraries,
            surface);
    }
}
