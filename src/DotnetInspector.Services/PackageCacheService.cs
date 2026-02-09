using DotnetInspector.Packages;

namespace DotnetInspector.Services;

/// <summary>
/// Provides cache management operations.
/// Enforces the invariant: NuGet cache is read-only, writes go to the app cache.
/// </summary>
public static class PackageCacheService
{
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
    /// </summary>
    public static CacheInfo GetCacheInfo()
    {
        var basePath = NuGetCache.GetAppCacheBasePath();
        var categories = new List<CacheCategoryInfo>();
        long totalSize = 0;

        if (Directory.Exists(basePath))
        {
            foreach (var (name, subdir) in new[] { ("Packages", "packages"), ("Sources", "sources"), ("Symbols", "symbols") })
            {
                var path = Path.Combine(basePath, subdir);
                if (Directory.Exists(path))
                {
                    var (size, count) = GetDirectoryStats(path);
                    categories.Add(new CacheCategoryInfo(name, size, count));
                    totalSize += size;
                }
            }
        }

        return new CacheInfo(basePath, categories, totalSize);
    }

    /// <summary>
    /// Clears the app cache. Returns the number of bytes freed.
    /// </summary>
    public static long ClearCache()
    {
        var basePath = NuGetCache.GetAppCacheBasePath();

        if (!Directory.Exists(basePath))
            return 0;

        var (size, _) = GetDirectoryStats(basePath);
        Directory.Delete(basePath, recursive: true);
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
