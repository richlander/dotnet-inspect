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
        : PackageSourceCoordinateResolution;

    public sealed record Invalid(string Message)
        : PackageSourceCoordinateResolution;

    public sealed record Unavailable(string Message)
        : PackageSourceCoordinateResolution;

    public sealed record Failed(PackageSourceFailure Failure)
        : PackageSourceCoordinateResolution;
}

/// <summary>
/// Resolves exact pins directly and floating coordinates through a typed
/// source's listed search results.
/// </summary>
public static class PackageSourceCoordinateResolver
{
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
        PackageCandidateObservation? selected = null;
        NuGetVersion? selectedVersion = null;
        foreach (PackageCandidateObservation candidate in
                 result.Matches.Select(match => match.Candidate))
        {
            if (!ReferenceEquals(candidate.Source, source.Source)
                || !candidate.Coordinate.PackageId.Equals(
                    coordinate.PackageId,
                    StringComparison.OrdinalIgnoreCase)
                || candidate.ListingState != PackageListingState.Listed
                || !NuGetVersion.TryParse(
                    candidate.Coordinate.Version,
                    out NuGetVersion? parsed)
                || parsed.IsPrerelease
                || selectedVersion is not null
                    && parsed <= selectedVersion)
            {
                continue;
            }

            selected = candidate;
            selectedVersion = parsed;
        }

        return selected is null
            ? new PackageSourceCoordinateResolution.Unavailable(
                $"Package '{coordinate.PackageId}' has no listed stable version in the selected source.")
            : new PackageSourceCoordinateResolution.Resolved(
                selected.Coordinate,
                WasFloating: true);
    }
}
