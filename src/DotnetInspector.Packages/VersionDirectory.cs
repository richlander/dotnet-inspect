using NuGet.Versioning;

namespace DotnetInspector.Packages;

/// <summary>A version-named subdirectory: the parsed version plus its path and directory name.</summary>
public readonly record struct VersionDir(NuGetVersion Version, string DirPath, string DirName);

/// <summary>
/// Selects the newest valid version-named subdirectory of a cache root. Shared by the package cache
/// and the platform-pack cache, which previously each had their own copy of the scan loop.
/// </summary>
public static class VersionDirectory
{
    /// <summary>
    /// Returns the highest-versioned subdirectory of <paramref name="root"/> whose name parses as a
    /// semantic version and that <paramref name="isValid"/> accepts, or null if none qualifies.
    /// Validity is only checked for a candidate that would become the new best (matching the original
    /// loops, where the structure check is the last and most expensive condition).
    /// </summary>
    public static VersionDir? SelectBest(string root, bool includePrerelease, Func<string, bool> isValid)
    {
        if (!Directory.Exists(root))
            return null;

        NuGetVersion? best = null;
        string? bestDir = null;
        string? bestName = null;

        foreach (var dir in Directory.GetDirectories(root))
        {
            var dirName = Path.GetFileName(dir);
            if (NuGetVersion.TryParse(dirName, out var parsed)
                && (includePrerelease || !parsed.IsPrerelease)
                && (best is null || parsed > best)
                && isValid(dir))
            {
                best = parsed;
                bestDir = dir;
                bestName = dirName;
            }
        }

        return best is null ? null : new VersionDir(best, bestDir!, bestName!);
    }

    /// <summary>Returns whichever of two results has the higher version (null-safe).</summary>
    public static VersionDir? Higher(VersionDir? a, VersionDir? b)
    {
        if (a is null) return b;
        if (b is null) return a;
        return b.Value.Version > a.Value.Version ? b : a;
    }
}
