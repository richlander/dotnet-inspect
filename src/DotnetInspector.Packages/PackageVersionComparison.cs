using System.Collections.Immutable;
using NuGet.Versioning;

namespace DotnetInspector.Packages;

/// <summary>The first version component at which a later version exceeds a current one.</summary>
public enum PackageVersionLag
{
    None,
    Major,
    Minor,
    Patch,
    Revision,
    Prerelease,
}

/// <summary>
/// Latest In Major, lag, and majors behind for one current version against
/// the latest version and the admitted listed population that produced it.
/// </summary>
public sealed record PackageVersionComparison(
    string? LatestInMajor,
    PackageVersionLag Behind,
    int MajorsBehind)
{
    // The same ordering that selects latest during version resolution.
    private static readonly IVersionComparer Comparer =
        VersionComparer.VersionReleaseMetadata;

    /// <summary>
    /// Compares Current with Latest and the population that produced Latest.
    /// Current need not be a member of that population.
    /// </summary>
    public static PackageVersionComparison Create(
        NuGetVersion current,
        NuGetVersion latest,
        IEnumerable<NuGetVersion> listed)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(latest);
        ArgumentNullException.ThrowIfNull(listed);
        ImmutableArray<NuGetVersion> population = [.. listed];
        NuGetVersion? latestInMajor = population
            .Where(version => version.Major == current.Major)
            .OrderByDescending(static version => version, Comparer)
            .FirstOrDefault();
        if (Comparer.Compare(latest, current) <= 0)
        {
            return new(
                latestInMajor?.ToNormalizedString(),
                PackageVersionLag.None,
                MajorsBehind: 0);
        }

        int majorsBehind = population
            .Where(version =>
                version.Major > current.Major
                && Comparer.Compare(version, latest) <= 0)
            .Select(static version => version.Major)
            .Distinct()
            .Count();
        return new(
            latestInMajor?.ToNormalizedString(),
            Lag(current, latest),
            majorsBehind);
    }

    /// <summary>
    /// Returns the inclusive minimum of a canonical NuGet version constraint,
    /// or null when the constraint has no inclusive lower bound.
    /// </summary>
    public static NuGetVersion? InclusiveMinimum(string versionConstraint) =>
        VersionRange.TryParse(versionConstraint, out VersionRange? range)
        && range.HasLowerBound
        && range.IsMinInclusive
            ? range.MinVersion
            : null;

    private static PackageVersionLag Lag(
        NuGetVersion current,
        NuGetVersion latest) =>
        latest.Major != current.Major
            ? PackageVersionLag.Major
            : latest.Minor != current.Minor
                ? PackageVersionLag.Minor
                : latest.Patch != current.Patch
                    ? PackageVersionLag.Patch
                    : latest.Revision != current.Revision
                        ? PackageVersionLag.Revision
                        : PackageVersionLag.Prerelease;
}
