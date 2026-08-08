using NuGet.Versioning;

namespace NuGetFetch;

/// <summary>
/// Two-tier package cache: reads from ~/.nuget/packages (shared, read-only)
/// and writes to an app-specific cache directory.
/// </summary>
public class PackageCache
{
    private readonly string _appCacheBase;
    private readonly string _nugetCachePath;
    private readonly bool _skipNuGetCache;

    public PackageCache(string appName, bool skipNuGetCache = false)
    {
        _appCacheBase = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            appName,
            "packages");
        _nugetCachePath = GetNuGetCachePath();
        _skipNuGetCache = skipNuGetCache;
    }

    /// <summary>
    /// Gets the app-specific cache base directory.
    /// </summary>
    public string CachePath => _appCacheBase;

    /// <summary>
    /// Gets the NuGet global packages cache path (~/.nuget/packages).
    /// </summary>
    public string NuGetCachePath => _nugetCachePath;

    /// <summary>
    /// Tries to find a cached package. Checks NuGet cache first, then app cache.
    /// </summary>
    public string? TryGet(string id, string version)
    {
        string normalizedId = id.ToLowerInvariant();
        string normalizedVersion = NuGetClient.NormalizeVersion(version);

        // Check NuGet global cache (read-only)
        if (!_skipNuGetCache)
        {
            string nugetPath = Path.Combine(_nugetCachePath, normalizedId, normalizedVersion);

            if (Directory.Exists(nugetPath) && PackageExtractor.IsValidPackage(nugetPath))
            {
                return nugetPath;
            }
        }

        // Check app cache
        string appPath = Path.Combine(_appCacheBase, normalizedId, normalizedVersion);

        if (Directory.Exists(appPath) && PackageExtractor.IsValidPackage(appPath))
        {
            return appPath;
        }

        return null;
    }

    /// <summary>
    /// Caches an extracted package in the app cache directory.
    /// Uses atomic directory rename to prevent partial-copy races.
    /// Returns the cache path, or null on failure.
    /// </summary>
    public string? Cache(string id, string version, string sourcePath)
    {
        string normalizedId = id.ToLowerInvariant();
        string normalizedVersion = NuGetClient.NormalizeVersion(version);
        string targetPath = Path.Combine(_appCacheBase, normalizedId, normalizedVersion);

        if (Directory.Exists(targetPath))
        {
            return targetPath;
        }

        try
        {
            // Copy to a temp directory first, then atomically move into place
            string tempPath = targetPath + $".tmp-{Guid.NewGuid():N}";
            CopyDirectory(sourcePath, tempPath);

            try
            {
                Directory.Move(tempPath, targetPath);
            }
            catch (IOException) when (Directory.Exists(targetPath))
            {
                // Another process won the race — use their copy
                try { Directory.Delete(tempPath, recursive: true); } catch { }
            }

            return targetPath;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets the path where a package would be cached.
    /// </summary>
    public string GetCachePath(string id, string version) =>
        Path.Combine(_appCacheBase, id.ToLowerInvariant(), NuGetClient.NormalizeVersion(version));

    /// <summary>
    /// Finds the latest cached version of a package across both caches.
    /// </summary>
    public string? TryGetLatestCachedVersion(string id, bool includePrerelease = false)
    {
        string normalizedId = id.ToLowerInvariant();
        NuGetVersion? best = null;

        ScanCacheDir(Path.Combine(_nugetCachePath, normalizedId), includePrerelease, ref best);
        ScanCacheDir(Path.Combine(_appCacheBase, normalizedId), includePrerelease, ref best);

        return best?.OriginalVersion;
    }

    private static void ScanCacheDir(string packageDir, bool includePrerelease, ref NuGetVersion? best)
    {
        if (!Directory.Exists(packageDir))
        {
            return;
        }

        foreach (string versionDir in Directory.GetDirectories(packageDir))
        {
            string dirName = Path.GetFileName(versionDir);

            if (NuGetVersion.TryParse(dirName, out NuGetVersion? parsed))
            {
                if (!includePrerelease && parsed.IsPrerelease)
                {
                    continue;
                }

                if (best is null || parsed > best)
                {
                    best = parsed;
                }
            }
        }
    }

    private static string GetNuGetCachePath()
    {
        string? envPath = Environment.GetEnvironmentVariable("NUGET_PACKAGES");

        if (!string.IsNullOrEmpty(envPath))
        {
            return envPath;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget",
            "packages");
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (string file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }

        foreach (string dir in Directory.GetDirectories(source))
        {
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
        }
    }
}
