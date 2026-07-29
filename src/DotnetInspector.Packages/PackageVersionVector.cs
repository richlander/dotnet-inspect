using System.Collections.Immutable;
using DotnetInspector.Core;
using NuGet.Versioning;

namespace DotnetInspector.Packages;

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
/// One stable address in an ordered package-version vector.
/// </summary>
public sealed record PackageVersionAddress
{
    internal PackageVersionAddress(int position, NuGetVersion version)
    {
        Position = position;
        Version = version;
    }

    public int Position { get; }
    public int Ordinal => Position + 1;
    public string Selector => $"#{Ordinal}";
    public NuGetVersion Version { get; }
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
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(range);

        var versions = await PackageExtractor.GetVersionsAsync(
            client,
            range.PackageId,
            range.IncludesPrerelease || includePrerelease,
            int.MaxValue,
            log,
            sourceOptions);

        if (versions is null)
            throw new InvalidOperationException($"Could not retrieve versions for package '{range.PackageId}'.");

        return Create(range, versions, includePrerelease);
    }

    public static PackageVersionVector Create(
        PackageVersionRange range,
        IEnumerable<string> availableVersions,
        bool includePrerelease = false)
    {
        ArgumentNullException.ThrowIfNull(range);
        ArgumentNullException.ThrowIfNull(availableVersions);

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

        if (!versions.Any(version => VersionComparer.Equals(version, range.Start)))
            throw new ArgumentException($"Package '{range.PackageId}' does not contain range endpoint {range.Start}.", nameof(range));
        if (!versions.Any(version => VersionComparer.Equals(version, range.End)))
            throw new ArgumentException($"Package '{range.PackageId}' does not contain range endpoint {range.End}.", nameof(range));

        int direction = VersionComparer.Compare(range.Start, range.End);
        var minimum = direction <= 0 ? range.Start : range.End;
        var maximum = direction <= 0 ? range.End : range.Start;

        IEnumerable<NuGetVersion> ordered = versions
            .Where(version =>
                VersionComparer.Compare(version, minimum) >= 0
                && VersionComparer.Compare(version, maximum) <= 0
                && (range.IncludesPrerelease || includePrerelease || !version.IsPrerelease))
            .OrderBy(version => version, SortComparer);

        if (direction > 0)
            ordered = ordered.Reverse();

        var addresses = ordered
            .Select((version, position) => new PackageVersionAddress(position, version))
            .ToImmutableArray();

        return new PackageVersionVector(range.PackageId, range.Start, range.End, addresses);
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
