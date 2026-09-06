using NuGet.Versioning;
using NuGetFetch;

namespace DotnetInspector.Packages;

/// <summary>The result of resolving one coordinate through a typed source client.</summary>
public abstract record PackageSourceCoordinateResolution
{
    private protected PackageSourceCoordinateResolution()
    {
    }

    public sealed record Resolved(
        PackageSourceCoordinate Coordinate,
        bool WasFloating)
        : PackageSourceCoordinateResolution
    {
        public PackageCandidateObservation? Candidate { get; init; }
    }

    public sealed record Invalid(string Message)
        : PackageSourceCoordinateResolution;

    public sealed record Unavailable(string Message)
        : PackageSourceCoordinateResolution;

    /// <summary>Authoritative version observations yielded no eligible listed version.</summary>
    public sealed record NoEligibleVersion(string Message)
        : PackageSourceCoordinateResolution;

    public sealed record Failed(PackageSourceFailure Failure)
        : PackageSourceCoordinateResolution;
}

/// <summary>
/// Resolves exact pins and floating coordinates through a typed source.
/// </summary>
public static class PackageSourceCoordinateResolver
{
    /// <summary>
    /// Selects one listed version from exact-ID version observations, retaining
    /// the source-issued candidate. An incomplete listing is not an empty result.
    /// </summary>
    public static async Task<PackageSourceCoordinateResolution> ResolveLatestListedAsync(
        IPackageSourceClient source,
        string packageId,
        bool includePrerelease = false,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (PackageCoordinateResolver.Validate(new PackageCoordinate(packageId))
            is { } invalid)
            return new PackageSourceCoordinateResolution.Invalid(invalid.Message);

        cancellationToken = operationContext?.ResolveInvocationToken(
            cancellationToken) ?? cancellationToken;
        cancellationToken.ThrowIfCancellationRequested();
        operationContext?.ThrowIfExpired();
        PackageSourceOperationResult<PackageVersionResult> operation =
            await source.GetVersionsAsync(
                packageId, cancellationToken, operationContext).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        operationContext?.ThrowIfExpired();
        if (operation.Failure is { } failure)
            return new PackageSourceCoordinateResolution.Failed(failure);

        PackageVersionResult result = operation.Value
            ?? throw new InvalidOperationException(
                "Package version enumeration returned no value or failure.");
        if (!result.HasAuthoritativeListingState)
        {
            return new PackageSourceCoordinateResolution.Unavailable(
                $"The listed versions of package '{packageId}' could not be determined.");
        }

        PackageCandidateObservation? selected = SelectLatestListed(
            result.Candidates, source, packageId, includePrerelease);
        return selected is null
            ? new PackageSourceCoordinateResolution.NoEligibleVersion(
                $"Package '{packageId}' has no eligible listed version in the selected source.")
            : new PackageSourceCoordinateResolution.Resolved(
                selected.Coordinate, WasFloating: true)
                {
                    Candidate = selected,
                };
    }

    public static async Task<PackageSourceCoordinateResolution> ResolveAsync(
        IPackageSourceClient source,
        PackageCoordinate coordinate,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(coordinate);
        if (PackageCoordinateResolver.Validate(coordinate) is { } invalid)
            return new PackageSourceCoordinateResolution.Invalid(invalid.Message);

        cancellationToken = operationContext?.ResolveInvocationToken(
            cancellationToken) ?? cancellationToken;
        operationContext?.ThrowIfExpired();
        if (coordinate.Version is { } exactVersion)
        {
            return new PackageSourceCoordinateResolution.Resolved(
                PackageSourceCoordinate.Create(
                    coordinate.PackageId,
                    exactVersion),
                WasFloating: false);
        }

        PackageSourceOperationResult<PackageSearchResult> search =
            await source.SearchAsync(
                $"packageid:{coordinate.PackageId}",
                take: 20,
                prerelease: false,
                cancellationToken,
                operationContext).ConfigureAwait(false);
        if (search.Failure is { } failure)
            return new PackageSourceCoordinateResolution.Failed(failure);

        PackageSearchResult result =
            search.Value
            ?? throw new InvalidOperationException(
                "The package source search completed without a value or failure.");
        PackageCandidateObservation? selected = SelectLatestListed(
            result.Matches.Select(match => match.Candidate),
            source,
            coordinate.PackageId,
            includePrerelease: false);
        return selected is null
            ? new PackageSourceCoordinateResolution.Unavailable(
                $"Package '{coordinate.PackageId}' has no listed stable version in the selected source.")
            : new PackageSourceCoordinateResolution.Resolved(
                selected.Coordinate,
                WasFloating: true);
    }

    static PackageCandidateObservation? SelectLatestListed(
        IEnumerable<PackageCandidateObservation> candidates,
        IPackageSourceClient source,
        string packageId,
        bool includePrerelease)
    {
        PackageCandidateObservation? selected = null;
        NuGetVersion? selectedVersion = null;
        foreach (PackageCandidateObservation candidate in candidates)
        {
            if (!ReferenceEquals(candidate.Source, source.Source)
                || !candidate.Coordinate.PackageId.Equals(
                    packageId,
                    StringComparison.OrdinalIgnoreCase)
                || candidate.ListingState != PackageListingState.Listed
                || !NuGetVersion.TryParse(
                    candidate.Coordinate.Version,
                    out NuGetVersion? parsed)
                || !includePrerelease && parsed.IsPrerelease
                || selectedVersion is not null
                    && parsed <= selectedVersion)
            {
                continue;
            }

            selected = candidate;
            selectedVersion = parsed;
        }

        return selected;
    }
}
