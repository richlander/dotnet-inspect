namespace DotnetInspector.Queries;

/// <summary>The provenance of one loaded candidate for a declared NuGet dependency.</summary>
public enum PackageDependencyCoordinateKind
{
    NuGetPackage,
    PlatformRuntime,
}

/// <summary>
/// One host-loaded coordinate that may satisfy a declared NuGet dependency.
/// <see cref="Key"/> is opaque host identity; the query never parses it.
/// </summary>
public sealed record PackageDependencyCoordinateCandidate(
    string Key,
    PackageDependencyCoordinateKind Kind,
    string PackageId,
    string Version,
    string TargetFramework);

/// <summary>The cardinality of loaded candidates matching one declared dependency.</summary>
public enum PackageDependencyCoordinateMatchStatus
{
    NoMatch,
    Unique,
    Ambiguous,
}

/// <summary>A product-owned match between a declared dependency and loaded coordinates.</summary>
public sealed record PackageDependencyCoordinateMatch(
    PackageDependencyCoordinateMatchStatus Status,
    string? CandidateKey);

/// <summary>
/// Matches loaded package coordinates to a declared NuGet dependency using package provenance,
/// case-insensitive NuGet package identity, and product-owned version-range semantics.
/// </summary>
public static class PackageDependencyCoordinateMatchQuery
{
    public static PackageDependencyCoordinateMatch Execute(
        IEnumerable<PackageDependencyCoordinateCandidate> candidates,
        string packageId,
        string? declaredRange)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        string? matchKey = null;
        int matchCount = 0;
        foreach (PackageDependencyCoordinateCandidate candidate in candidates)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(candidate.Key);
            ArgumentException.ThrowIfNullOrWhiteSpace(candidate.PackageId);

            if (candidate.Kind != PackageDependencyCoordinateKind.NuGetPackage
                || !string.Equals(candidate.PackageId, packageId, StringComparison.OrdinalIgnoreCase)
                || !PackageDependencyVersionRange.Satisfies(candidate.Version, declaredRange))
            {
                continue;
            }

            matchKey ??= candidate.Key;
            matchCount++;
        }

        return matchCount switch
        {
            0 => new(PackageDependencyCoordinateMatchStatus.NoMatch, CandidateKey: null),
            1 => new(PackageDependencyCoordinateMatchStatus.Unique, matchKey),
            _ => new(PackageDependencyCoordinateMatchStatus.Ambiguous, CandidateKey: null),
        };
    }
}
