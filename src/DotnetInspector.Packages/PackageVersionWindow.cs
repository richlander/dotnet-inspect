using System.Collections.Immutable;
using NuGet.Versioning;

namespace DotnetInspector.Packages;

/// <summary>
/// A named question a version window answers
/// (version-resolution.md#version-window-projection).
/// </summary>
public enum PackageVersionWindowPolicy
{
    /// <summary>
    /// The n highest candidates strictly below the current version, then the
    /// current version. Every step compares API and Body.
    /// </summary>
    SupplyChain,

    /// <summary>
    /// The <see cref="PackageVersionMajorRepresentativePolicy.LatestStable"/>
    /// representative of each of the n highest majors below the current
    /// version's, then the current version. Every step compares API only.
    /// </summary>
    ApiEvolution,
}

/// <summary>Why a version window could not be projected.</summary>
public enum PackageVersionWindowFailure
{
    /// <summary>The listing names no candidate before the current version.</summary>
    NoEarlierVersion,

    /// <summary>The listing is not authoritative, so no window can be built from it.</summary>
    ListingNotAuthoritative,
}

/// <summary>
/// The points of one version window over a population vector that ends at the
/// caller's pinned current version. The current version is the vector's end
/// address whether or not the listing names it; every other address is a
/// listed candidate.
/// </summary>
public sealed class PackageVersionWindow
{
    static readonly IComparer<NuGetVersion> ReleaseOrder =
        Comparer<NuGetVersion>.Create(VersionComparer.VersionRelease.Compare);

    PackageVersionWindow(
        PackageVersionWindowPolicy policy,
        int requestedDepth,
        PackageVersionVector population,
        ImmutableArray<PackageVersionAddress> points)
    {
        Policy = policy;
        RequestedDepth = requestedDepth;
        Population = population;
        Points = points;
    }

    public PackageVersionWindowPolicy Policy { get; }

    /// <summary>The number of steps back from the current version the caller asked for.</summary>
    public int RequestedDepth { get; }

    /// <summary>The number of steps back the listing supported, at most <see cref="RequestedDepth"/>.</summary>
    public int ReachedDepth => Points.Length - 1;

    /// <summary>The ascending population from the first point to the current version.</summary>
    public PackageVersionVector Population { get; }

    /// <summary>The ascending points, addresses of <see cref="Population"/>, ending at the current version.</summary>
    public ImmutableArray<PackageVersionAddress> Points { get; }

    /// <summary>The current version, the window's end address.</summary>
    public PackageVersionAddress Current => Points[^1];

    /// <summary>
    /// Projects a window from the current version and its package's listing.
    /// Candidates are listed versions strictly below <paramref name="current"/>
    /// in NuGet release precedence, stable only unless it is a prerelease.
    /// </summary>
    public static bool TryProject(
        string packageId,
        PackageVersionWindowPolicy policy,
        int depth,
        NuGetVersion current,
        IEnumerable<PackageVersionInfo> listing,
        bool listingIsAuthoritative,
        out PackageVersionWindow? window,
        out PackageVersionWindowFailure failure)
    {
        ArgumentNullException.ThrowIfNull(listing);
        return TryProject(
            packageId,
            policy,
            depth,
            current,
            listing.Select(info => (NuGetVersion.Parse(info.Version), info.Listed)),
            listingIsAuthoritative,
            out window,
            out failure);
    }

    /// <summary>
    /// Projects a window from an already parsed listing, for a caller that
    /// parsed it for its own use.
    /// </summary>
    internal static bool TryProject(
        string packageId,
        PackageVersionWindowPolicy policy,
        int depth,
        NuGetVersion current,
        IEnumerable<(NuGetVersion Version, bool Listed)> listing,
        bool listingIsAuthoritative,
        out PackageVersionWindow? window,
        out PackageVersionWindowFailure failure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(listing);
        if (!Enum.IsDefined(policy))
            throw new ArgumentOutOfRangeException(nameof(policy));
        ArgumentOutOfRangeException.ThrowIfLessThan(depth, 1);

        window = null;
        failure = default;
        if (!listingIsAuthoritative)
        {
            failure = PackageVersionWindowFailure.ListingNotAuthoritative;
            return false;
        }

        NuGetVersion[] candidates =
        [
            .. listing
                .Where(row => row.Listed)
                .Select(row => row.Version)
                .Where(version =>
                    (current.IsPrerelease || !version.IsPrerelease)
                    && VersionComparer.VersionRelease.Compare(version, current) < 0)
                .Distinct<NuGetVersion>(VersionComparer.VersionRelease)
                .Order(ReleaseOrder),
        ];
        if (candidates.Length == 0)
        {
            failure = PackageVersionWindowFailure.NoEarlierVersion;
            return false;
        }

        NuGetVersion[] admitted = [.. candidates, current];
        bool includePrerelease = current.IsPrerelease;
        NuGetVersion[] earlier = policy switch
        {
            PackageVersionWindowPolicy.SupplyChain => [.. candidates.TakeLast(depth)],
            PackageVersionWindowPolicy.ApiEvolution => LowerMajorRepresentatives(
                packageId, candidates, current, includePrerelease, depth),
            _ => throw new ArgumentOutOfRangeException(nameof(policy)),
        };
        if (earlier.Length == 0)
        {
            failure = PackageVersionWindowFailure.NoEarlierVersion;
            return false;
        }

        PackageVersionVector population = PackageVersionVector.Create(
            PackageVersionRange.Create(packageId, earlier[0], current),
            admitted,
            includePrerelease);
        ImmutableArray<PackageVersionAddress> points =
        [
            .. earlier.Select(version => population.Addresses.Single(
                address => VersionComparer.VersionRelease.Equals(address.Version, version))),
            population.Addresses[^1],
        ];
        window = new PackageVersionWindow(policy, depth, population, points);
        return true;
    }

    static NuGetVersion[] LowerMajorRepresentatives(
        string packageId,
        NuGetVersion[] candidates,
        NuGetVersion current,
        bool includePrerelease,
        int depth)
    {
        HashSet<int> majors =
        [
            .. candidates
                .Select(version => version.Major)
                .Where(major => major < current.Major)
                .Distinct()
                .OrderDescending()
                .Take(depth),
        ];
        NuGetVersion[] lower = [.. candidates.Where(version => majors.Contains(version.Major))];
        if (lower.Length == 0)
            return [];

        PackageVersionVector all = PackageVersionVector.Create(
            PackageVersionRange.Create(packageId, lower[0], lower[^1]),
            lower,
            includePrerelease);
        return
        [
            .. all.ProjectMajorRepresentatives(
                    PackageVersionMajorRepresentativePolicy.LatestStable)
                .Addresses
                .Select(address => address.Version),
        ];
    }
}
