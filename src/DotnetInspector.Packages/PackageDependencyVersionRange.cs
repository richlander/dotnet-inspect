using NuGet.Versioning;

namespace DotnetInspector.Packages;

/// <summary>NuGet-owned matching and selection for declared dependency version ranges.</summary>
public static class PackageDependencyVersionRange
{
    public static bool Satisfies(
        string packageVersion,
        string? declaredRange)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageVersion);

        if (!NuGetVersion.TryParse(packageVersion, out NuGetVersion? version))
        {
            throw new ArgumentException(
                "The package version is invalid.",
                nameof(packageVersion));
        }

        VersionRange range = Parse(declaredRange);
        return Matches(range, version);
    }

    public static string? SelectBestSatisfying(
        IEnumerable<string> availableVersions,
        string? declaredRange)
    {
        ArgumentNullException.ThrowIfNull(availableVersions);

        VersionRange range = Parse(declaredRange);
        var candidates = new List<(NuGetVersion Version, string Text)>();
        foreach (string versionText in availableVersions)
        {
            if (!NuGetVersion.TryParse(
                    versionText,
                    out NuGetVersion? version))
            {
                throw new InvalidDataException(
                    "The package version index contains an invalid version.");
            }

            if (Matches(range, version))
                candidates.Add((version, versionText));
        }

        NuGetVersion? best = range.FindBestMatch(
            candidates.Select(candidate => candidate.Version));
        return best is null
            ? null
            : candidates.First(candidate => candidate.Version == best).Text;
    }

    /// <summary>
    /// Returns the canonical version when the declaration names exactly one
    /// coordinate, or <see langword="null"/> for a range or floating declaration.
    /// </summary>
    public static string? GetExactVersion(string? declaredRange)
    {
        VersionRange range = Parse(declaredRange);
        return !range.IsFloating
            && range.MinVersion is { } minimum
            && range.MaxVersion is { } maximum
            && range.IsMinInclusive
            && range.IsMaxInclusive
            && minimum == maximum
                ? minimum.ToNormalizedString()
                : null;
    }

    internal static void Validate(string? declaredRange) =>
        _ = Parse(declaredRange);

    private static bool Matches(VersionRange range, NuGetVersion version)
    {
        if (!range.Satisfies(version)
            || (range.IsFloating && !range.Float.Satisfies(version)))
        {
            return false;
        }

        return range.FindBestMatch([version]) == version;
    }

    private static VersionRange Parse(string? declaredRange)
    {
        if (string.IsNullOrWhiteSpace(declaredRange))
            return VersionRange.All;

        if (!VersionRange.TryParse(
                declaredRange,
                out VersionRange? range))
        {
            throw new InvalidDataException(
                "The declared dependency version range is invalid.");
        }

        return range;
    }
}
