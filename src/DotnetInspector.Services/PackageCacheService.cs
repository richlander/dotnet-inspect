using DotnetInspector.Core;
using DotnetInspector.Packages;

namespace DotnetInspector.Services;

/// <summary>
/// Provides cache management operations.
/// Enforces the invariant: NuGet cache is read-only, writes go to the app cache.
/// </summary>
public static class PackageCacheService
{
    private static readonly Dictionary<string, string> CategoryDisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["packages"] = "Packages",
        ["packs"] = "Platform packs",
        ["sources"] = "Sources",
        ["symbols"] = "Symbols",
        ["symbol-misses"] = "Symbol misses",
        ["source-audit"] = "Source audit",
        ["versions"] = "Versions",
        ["metadata"] = "Metadata"
    };

    /// <summary>
    /// Per-category cache statistics.
    /// </summary>
    public record CacheCategoryInfo(string Name, long Size, int Count);

    /// <summary>
    /// Overall cache information.
    /// </summary>
    public record CacheInfo(string Location, List<CacheCategoryInfo> Categories, long TotalSize);

    /// <summary>
    /// Returns cache location and per-category statistics.
    /// Reports on the active cache (session cache if isolated, default otherwise).
    /// </summary>
    public static CacheInfo GetCacheInfo()
    {
        var basePath = NuGetCache.GetAppCacheBasePath();
        List<CacheCategoryInfo> categories = [];
        long totalSize = 0;

        if (Directory.Exists(basePath))
        {
            foreach (var path in Directory.EnumerateDirectories(basePath).OrderBy(Path.GetFileName))
            {
                var subdir = Path.GetFileName(path);
                var name = CategoryDisplayNames.TryGetValue(subdir, out var displayName)
                    ? displayName
                    : subdir;
                var (size, count) = GetDirectoryStats(path);
                categories.Add(new CacheCategoryInfo(name, size, count));
                totalSize += size;
            }
        }

        return new CacheInfo(basePath, categories, totalSize);
    }

    /// <summary>
    /// Clears the active app cache (session cache if isolated, default otherwise).
    /// Also clears the legacy cache location (pre-XDG) if it exists.
    /// Returns the number of bytes freed.
    /// </summary>
    public static long ClearCache()
    {
        long totalFreed = 0;

        totalFreed += DeleteCacheDirectory(NuGetCache.GetAppCacheBasePath());

        // Clean up legacy cache location (pre-XDG: ~/.local/share/dotnet-inspect)
        var legacyPath = CoreCache.GetLegacyBasePath();
        if (legacyPath != null)
            totalFreed += DeleteCacheDirectory(legacyPath);

        return totalFreed;
    }

    private static long DeleteCacheDirectory(string path)
    {
        if (!Directory.Exists(path))
            return 0;

        var (size, _) = GetDirectoryStats(path);
        Directory.Delete(path, recursive: true);
        return size;
    }

    /// <summary>
    /// Retrieves cached source content for a URL, if available.
    /// </summary>
    public static string? TryGetCachedSource(string url)
    {
        return NuGetCache.TryGetCachedSource(url);
    }

    /// <summary>
    /// Caches source content for a URL (writes to app cache only).
    /// </summary>
    public static void CacheSource(string url, string content)
    {
        NuGetCache.CacheSource(url, content);
    }

    private static (long size, int count) GetDirectoryStats(string path)
    {
        if (!Directory.Exists(path))
            return (0, 0);

        long size = 0;
        int count = 0;

        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try
                {
                    size += new FileInfo(file).Length;
                    count++;
                }
                catch
                {
                    // Skip files we can't access
                }
            }
        }
        catch
        {
            // Skip directories we can't access
        }

        return (size, count);
    }
}
