using System.Collections.Immutable;
using NuGet.Versioning;

namespace DotnetInspector.Packages;

/// <summary>
/// Indicates that no configured package source supplied version metadata for a package.
/// </summary>
public sealed class PackageVersionsUnavailableException(
    string packageId,
    bool hasIncompleteMetadata)
    : InvalidOperationException(
        $"Could not retrieve versions for package '{packageId}'.")
{
    /// <summary>The package whose version metadata was unavailable.</summary>
    public string PackageId { get; } = packageId;

    /// <summary>
    /// Whether a source returned a version list whose listing metadata was incomplete.
    /// </summary>
    public bool HasIncompleteMetadata { get; } = hasIncompleteMetadata;
}

/// <summary>
/// An inclusive package-version range supplied in the familiar <c>Package@A..B</c> form.
/// </summary>
public sealed record PackageVersionRange
{
    PackageVersionRange(string packageId, NuGetVersion start, NuGetVersion end)
    {
        PackageId = packageId;
        Start = start;
        End = end;
    }

    public string PackageId { get; }
    public NuGetVersion Start { get; }
    public NuGetVersion End { get; }
    public string StartVersion => Start.ToNormalizedString();
    public string EndVersion => End.ToNormalizedString();
    public bool IncludesPrerelease => Start.IsPrerelease || End.IsPrerelease;

    /// <summary>
    /// Parses a package range. A false result with no error means the reference is not a range.
    /// </summary>
    public static bool TryParse(
        string packageReference,
        out PackageVersionRange? range,
        out string? error)
    {
        range = null;
        error = null;

        if (string.IsNullOrWhiteSpace(packageReference))
            return false;

        int atIndex = packageReference.LastIndexOf('@');
        if (atIndex < 0 || !packageReference[(atIndex + 1)..].Contains("..", StringComparison.Ordinal))
            return false;

        string packageId = packageReference[..atIndex];
        string versionRange = packageReference[(atIndex + 1)..];
        int separatorIndex = versionRange.IndexOf("..", StringComparison.Ordinal);

        if (string.IsNullOrWhiteSpace(packageId)
            || separatorIndex <= 0
            || separatorIndex + 2 >= versionRange.Length
            || versionRange.IndexOf("..", separatorIndex + 2, StringComparison.Ordinal) >= 0)
        {
            error = $"Invalid package version range '{packageReference}'. Expected Package@A..B.";
            return false;
        }

        string startText = versionRange[..separatorIndex];
        string endText = versionRange[(separatorIndex + 2)..];
        if (!NuGetVersion.TryParse(startText, out var start))
        {
            error = $"Invalid package version '{startText}' in range '{packageReference}'.";
            return false;
        }

        if (!NuGetVersion.TryParse(endText, out var end))
        {
            error = $"Invalid package version '{endText}' in range '{packageReference}'.";
            return false;
        }

        range = new PackageVersionRange(packageId, start, end);
        return true;
    }
}

/// <summary>
/// Controls how a package version population admits the range boundaries.
/// </summary>
public enum PackageVersionPopulationPolicy
{
    /// <summary>Require both literal range endpoint versions to be published.</summary>
    ExactEndpoints,

    /// <summary>Use the endpoint majors as semantic population boundaries.</summary>
    MajorBounds,
}

/// <summary>
/// Selects one deterministic representative from each admitted major bucket.
/// </summary>
public enum PackageVersionMajorRepresentativePolicy
{
    /// <summary>
    /// Select the lowest admitted stable version in each major, falling back to
    /// the highest admitted prerelease when that major has no stable version.
    /// </summary>
    FirstStable,

    /// <summary>Select the highest admitted version in each major.</summary>
    Latest,
}

/// <summary>
/// One stable address in an ordered package-version vector.
/// </summary>
public sealed record PackageVersionAddress
{
    internal PackageVersionAddress(
        int position,
        NuGetVersion version,
        IReadOnlyList<string>? reportingSourceUrls = null)
    {
        Position = position;
        Version = version;
        ReportingSourceUrls = reportingSourceUrls ?? [];
    }

    public int Position { get; }
    public int Ordinal => Position + 1;
    public string Selector => $"#{Ordinal}";
    public NuGetVersion Version { get; }
    public string NormalizedVersion => Version.ToNormalizedString();
    public IReadOnlyList<string> ReportingSourceUrls { get; }
}

/// <summary>
/// A typed projection of a version vector's original addresses, with one
/// representative retained for each admitted major.
/// </summary>
public sealed class PackageVersionMajorRepresentativeProjection
{
    internal PackageVersionMajorRepresentativeProjection(
        PackageVersionMajorRepresentativePolicy policy,
        ImmutableArray<PackageVersionAddress> addresses)
    {
        if (!Enum.IsDefined(policy))
            throw new ArgumentOutOfRangeException(nameof(policy));
        if (addresses.IsDefault)
            throw new ArgumentException(
                "A representative projection requires initialized addresses.",
                nameof(addresses));
        if (addresses.Any(address => address is null))
            throw new ArgumentException(
                "A representative projection cannot contain a null address.",
                nameof(addresses));

        Policy = policy;
        Addresses = addresses;
    }

    public PackageVersionMajorRepresentativePolicy Policy { get; }

    public ImmutableArray<PackageVersionAddress> Addresses { get; }
}

/// <summary>
/// An immutable, ordered vector resolved from a package-version range. Resolving the vector
/// enumerates version metadata only; package payload acquisition remains caller-driven and lazy.
/// </summary>
public sealed class PackageVersionVector
{
    static readonly IVersionComparer VersionComparer = NuGet.Versioning.VersionComparer.VersionReleaseMetadata;
    static readonly IComparer<NuGetVersion> SortComparer
        = Comparer<NuGetVersion>.Create(VersionComparer.Compare);

    PackageVersionVector(
        string packageId,
        NuGetVersion start,
        NuGetVersion end,
        ImmutableArray<PackageVersionAddress> addresses)
    {
        PackageId = packageId;
        Start = start;
        End = end;
        Addresses = addresses;
    }

    public string PackageId { get; }
    public NuGetVersion Start { get; }
    public NuGetVersion End { get; }
    public ImmutableArray<PackageVersionAddress> Addresses { get; }

    public static async Task<PackageVersionVector> ResolveAsync(
        HttpClient client,
        PackageVersionRange range,
        NuGetSourceOptions? sourceOptions = null,
        Action<string>? log = null,
        bool includePrerelease = false)
        => await ResolveAsync(
            client,
            range,
            sourceOptions,
            log,
            includePrerelease,
            PackageVersionPopulationPolicy.ExactEndpoints).ConfigureAwait(false);

    public static async Task<PackageVersionVector> ResolveAsync(
        HttpClient client,
        PackageVersionRange range,
        NuGetSourceOptions? sourceOptions,
        Action<string>? log,
        bool includePrerelease,
        PackageVersionPopulationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(range);

        var (candidates, hasIncompleteMetadata) =
            await PackageExtractor.GetVersionCandidatesAsync(
            client,
            range.PackageId,
            range.IncludesPrerelease || includePrerelease,
            log,
            sourceOptions);

        if (candidates is null)
            throw new PackageVersionsUnavailableException(
                range.PackageId,
                hasIncompleteMetadata);

        PackageVersionVector vector = Create(
            range,
            candidates.Select(candidate => candidate.Version),
            includePrerelease,
            policy);
        var addresses = vector.Addresses
            .Select(address =>
            {
                PackageVersionResolution candidate = candidates.Single(
                    item => NuGetVersion.TryParse(
                            item.Version,
                            out var parsed)
                        && VersionComparer.Equals(
                            parsed,
                            address.Version));
                return new PackageVersionAddress(
                    address.Position,
                    address.Version,
                    [
                        .. candidate.ReportingSources.Select(
                            source => source.Url),
                    ]);
            })
            .ToImmutableArray();
        return new PackageVersionVector(
            vector.PackageId,
            vector.Start,
            vector.End,
            addresses);
    }

    public static PackageVersionVector Create(
        PackageVersionRange range,
        IEnumerable<string> availableVersions,
        bool includePrerelease = false)
        => Create(
            range,
            availableVersions,
            includePrerelease,
            PackageVersionPopulationPolicy.ExactEndpoints);

    /// <summary>
    /// Creates a vector using either literal endpoint admission or semantic
    /// major-bound admission while preserving the caller's direction.
    /// </summary>
    public static PackageVersionVector Create(
        PackageVersionRange range,
        IEnumerable<string> availableVersions,
        bool includePrerelease,
        PackageVersionPopulationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(range);
        ArgumentNullException.ThrowIfNull(availableVersions);
        if (!Enum.IsDefined(policy))
            throw new ArgumentOutOfRangeException(nameof(policy));

        var parsedVersions = availableVersions
            .Select(version =>
            {
                if (!NuGetVersion.TryParse(version, out var parsed))
                    throw new ArgumentException($"Available version '{version}' is not a valid NuGet version.", nameof(availableVersions));
                return parsed;
            })
            .ToArray();
        var versions = parsedVersions
            .Where((version, index) => !parsedVersions
                .Take(index)
                .Any(previous => VersionComparer.Equals(previous, version)))
            .ToArray();

        if (policy == PackageVersionPopulationPolicy.ExactEndpoints)
        {
            if (!versions.Any(version => VersionComparer.Equals(version, range.Start)))
                throw new ArgumentException($"Package '{range.PackageId}' does not contain range endpoint {range.Start}.", nameof(range));
            if (!versions.Any(version => VersionComparer.Equals(version, range.End)))
                throw new ArgumentException($"Package '{range.PackageId}' does not contain range endpoint {range.End}.", nameof(range));
        }

        int direction = VersionComparer.Compare(range.Start, range.End);
        var minimum = direction <= 0 ? range.Start : range.End;
        var maximum = direction <= 0 ? range.End : range.Start;

        IEnumerable<NuGetVersion> ordered = versions
            .Where(version =>
                VersionComparer.Compare(version, minimum) >= 0
                && VersionComparer.Compare(version, maximum) <= 0
                && (range.IncludesPrerelease || includePrerelease || !version.IsPrerelease))
            .OrderBy(version => version, SortComparer);

        if (policy == PackageVersionPopulationPolicy.MajorBounds)
        {
            ordered = ordered.ToArray();
            if (!ordered.Any(version => version.Major == range.Start.Major))
                throw new ArgumentException(
                    $"Package '{range.PackageId}' does not contain an admitted version in lower boundary major {range.Start.Major}.",
                    nameof(range));
            if (!ordered.Any(version => version.Major == range.End.Major))
                throw new ArgumentException(
                    $"Package '{range.PackageId}' does not contain an admitted version in upper boundary major {range.End.Major}.",
                    nameof(range));
        }

        if (direction > 0)
            ordered = ordered.Reverse();

        var addresses = ordered
            .Select((version, position) => new PackageVersionAddress(position, version))
            .ToImmutableArray();

        return new PackageVersionVector(range.PackageId, range.Start, range.End, addresses);
    }

    /// <summary>
    /// Returns whether a complete candidate set contains an admitted version
    /// in both semantic boundary majors.
    /// </summary>
    internal static bool ContainsMajorBounds(
        PackageVersionRange range,
        IEnumerable<string> availableVersions,
        bool includePrerelease = false)
    {
        ArgumentNullException.ThrowIfNull(range);
        ArgumentNullException.ThrowIfNull(availableVersions);

        var versions = availableVersions
            .Select(version =>
            {
                if (!NuGetVersion.TryParse(version, out var parsed))
                    return null;
                return parsed;
            })
            .Where(version => version is not null)
            .Select(version => version!)
            .Distinct(VersionComparer)
            .ToArray();
        int direction = VersionComparer.Compare(range.Start, range.End);
        var minimum = direction <= 0 ? range.Start : range.End;
        var maximum = direction <= 0 ? range.End : range.Start;
        var admitted = versions.Where(version =>
            VersionComparer.Compare(version, minimum) >= 0
            && VersionComparer.Compare(version, maximum) <= 0
            && (range.IncludesPrerelease || includePrerelease || !version.IsPrerelease));

        return admitted.Any(version => version.Major == range.Start.Major)
            && admitted.Any(version => version.Major == range.End.Major);
    }

    /// <summary>
    /// Selects one original vector address for each admitted major in the
    /// caller-directed major order.
    /// </summary>
    public PackageVersionMajorRepresentativeProjection
        ProjectMajorRepresentatives(
            PackageVersionMajorRepresentativePolicy policy)
    {
        if (!Enum.IsDefined(policy))
            throw new ArgumentOutOfRangeException(nameof(policy));

        ImmutableArray<PackageVersionAddress> addresses =
        [
            .. Addresses
                .GroupBy(address => address.Version.Major)
                .Select(group =>
                {
                    IEnumerable<PackageVersionAddress> stable =
                        group.Where(address => !address.Version.IsPrerelease);
                    return policy switch
                    {
                        PackageVersionMajorRepresentativePolicy.FirstStable =>
                            stable.OrderBy(address => address.Version, SortComparer)
                                .FirstOrDefault()
                            ?? group
                                .OrderByDescending(
                                    address => address.Version,
                                    SortComparer)
                                .FirstOrDefault(),
                        PackageVersionMajorRepresentativePolicy.Latest =>
                            group.OrderByDescending(
                                    address => address.Version,
                                    SortComparer)
                                .FirstOrDefault(),
                        _ => throw new ArgumentOutOfRangeException(nameof(policy)),
                    };
                })
                .Where(address => address is not null)!,
        ];
        return new PackageVersionMajorRepresentativeProjection(policy, addresses);
    }

    internal static bool ContainsVersion(
        IEnumerable<string> availableVersions,
        NuGetVersion version)
    {
        ArgumentNullException.ThrowIfNull(availableVersions);
        ArgumentNullException.ThrowIfNull(version);
        return availableVersions.Any(candidate =>
            NuGetVersion.TryParse(candidate, out NuGetVersion? parsed)
            && VersionComparer.Equals(parsed, version));
    }

    /// <summary>
    /// Resolves a range against a listing-aware version set (each version tagged listed/unlisted)
    /// and projects the in-range versions in caller direction, carrying each version's listed
    /// status. Building the vector from the full set — unlisted versions included — lets an
    /// unlisted endpoint resolve rather than being reported as a missing endpoint. A resolved
    /// version absent from <paramref name="listings"/> defaults to listed.
    /// </summary>
    public static IEnumerable<PackageVersionInfo> CreateListingAware(
        PackageVersionRange range,
        IReadOnlyCollection<PackageVersionInfo> listings,
        bool includePrerelease = false)
        => CreateListingAware(
            range,
            listings,
            includePrerelease,
            PackageVersionPopulationPolicy.ExactEndpoints);

    public static IEnumerable<PackageVersionInfo> CreateListingAware(
        PackageVersionRange range,
        IReadOnlyCollection<PackageVersionInfo> listings,
        bool includePrerelease,
        PackageVersionPopulationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(range);
        ArgumentNullException.ThrowIfNull(listings);

        var listedByVersion = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var listing in listings)
            if (NuGetVersion.TryParse(listing.Version, out var parsed))
                listedByVersion[parsed.ToNormalizedString()] = listing.Listed;

        var vector = Create(
            range,
            listings.Select(listing => listing.Version),
            includePrerelease,
            policy);
        return vector.Addresses.Select(address =>
        {
            var normalized = address.Version.ToNormalizedString();
            bool listed = !listedByVersion.TryGetValue(normalized, out var flag) || flag;
            return new PackageVersionInfo(normalized, listed);
        });
    }

    /// <summary>
    /// Resolves an exact version, one-based <c>#N</c> index, or endpoint alias.
    /// </summary>
    public bool TrySelect(string selector, out PackageVersionAddress? address, out string? error)
    {
        address = null;
        error = null;

        if (string.IsNullOrWhiteSpace(selector))
        {
            error = "A package range address is required.";
            return false;
        }

        if (selector.Equals("first", StringComparison.OrdinalIgnoreCase))
        {
            address = Addresses[0];
            return true;
        }

        if (selector.Equals("last", StringComparison.OrdinalIgnoreCase))
        {
            address = Addresses[^1];
            return true;
        }

        if (selector[0] == '#')
        {
            if (!int.TryParse(selector.AsSpan(1), out int ordinal) || ordinal < 1 || ordinal > Addresses.Length)
            {
                error = $"Range address '{selector}' is outside #1..#{Addresses.Length}.";
                return false;
            }

            address = Addresses[ordinal - 1];
            return true;
        }

        if (!NuGetVersion.TryParse(selector, out var version))
        {
            error = $"Range address '{selector}' must be an exact version, #N, first, or last.";
            return false;
        }

        address = Addresses.FirstOrDefault(candidate => VersionComparer.Equals(candidate.Version, version));
        if (address is null)
        {
            error = $"Version '{selector}' is not in range {Start}..{End}.";
            return false;
        }

        return true;
    }
}
