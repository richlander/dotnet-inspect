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
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(listing);
        if (!listingIsAuthoritative)
        {
            Validate(packageId, policy, depth);
            window = null;
            failure = PackageVersionWindowFailure.ListingNotAuthoritative;
            return false;
        }

        NuGetVersion[] candidates = Candidates(
            current,
            listing
                .Select(info => (Version: NuGetVersion.Parse(info.Version), info.Listed))
                .OrderByDescending(row => row.Version, ReleaseOrder));
        return TryProject(packageId, policy, depth, current, candidates, out window, out failure);
    }

    /// <summary>
    /// The window candidates, ascending, from a listing already in descending
    /// release order. Equal releases keep their first listed row.
    /// </summary>
    internal static NuGetVersion[] Candidates(
        NuGetVersion current,
        IEnumerable<(NuGetVersion Version, bool Listed)> descending)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(descending);

        var candidates = new List<NuGetVersion>();
        foreach ((NuGetVersion version, bool listed) in descending)
        {
            if (!listed
                || (!current.IsPrerelease && version.IsPrerelease)
                || VersionComparer.VersionRelease.Compare(version, current) >= 0
                || (candidates.Count > 0
                    && VersionComparer.VersionRelease.Equals(candidates[^1], version)))
            {
                continue;
            }

            candidates.Add(version);
        }

        candidates.Reverse();
        return [.. candidates];
    }

    /// <summary>
    /// Projects a window over candidates from <see cref="Candidates"/>, which
    /// come from an authoritative listing.
    /// </summary>
    internal static bool TryProject(
        string packageId,
        PackageVersionWindowPolicy policy,
        int depth,
        NuGetVersion current,
        NuGetVersion[] candidates,
        out PackageVersionWindow? window,
        out PackageVersionWindowFailure failure)
    {
        Validate(packageId, policy, depth);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(candidates);

        window = null;
        failure = PackageVersionWindowFailure.NoEarlierVersion;
        int[] earlier = policy == PackageVersionWindowPolicy.SupplyChain
            ? [.. Enumerable.Range(0, candidates.Length).TakeLast(depth)]
            : LowerMajorRepresentatives(candidates, current, depth);
        if (earlier.Length == 0)
            return false;

        int first = earlier[0];
        PackageVersionVector population = PackageVersionVector.CreateOrdered(
            packageId,
            [.. candidates.AsSpan(first), current]);
        ImmutableArray<PackageVersionAddress> points =
        [
            .. earlier.Select(index => population.Addresses[index - first]),
            population.Addresses[^1],
        ];
        window = new PackageVersionWindow(policy, depth, population, points);
        return true;
    }

    // The LatestStable representative of each of the n highest majors below
    // the current version's, as ascending candidate indexes: the highest
    // stable version in the major, else its highest prerelease.
    static int[] LowerMajorRepresentatives(
        NuGetVersion[] candidates,
        NuGetVersion current,
        int depth)
    {
        var representatives = new List<int>(depth);
        int index = candidates.Length - 1;
        while (index >= 0 && candidates[index].Major >= current.Major)
            index--;

        while (index >= 0 && representatives.Count < depth)
        {
            int major = candidates[index].Major;
            int representative = index;
            while (index >= 0 && candidates[index].Major == major)
            {
                if (candidates[representative].IsPrerelease && !candidates[index].IsPrerelease)
                    representative = index;
                index--;
            }

            representatives.Add(representative);
        }

        representatives.Reverse();
        return [.. representatives];
    }

    static void Validate(string packageId, PackageVersionWindowPolicy policy, int depth)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        if (!Enum.IsDefined(policy))
            throw new ArgumentOutOfRangeException(nameof(policy));
        ArgumentOutOfRangeException.ThrowIfLessThan(depth, 1);
    }
}
