using System.Security.Cryptography;
using System.Text;

namespace DotnetInspector.Core;

/// <summary>
/// Generic disk cache with category-based partitioning.
/// Uses SHA256-hashed keys and subdirectory bucketing for filesystem safety.
/// Call <see cref="Initialize"/> before using any cache operations.
/// </summary>
public static class CoreCache
{
    private static string? _appName;

    /// <summary>
    /// Initializes the cache with the application name used for the cache directory.
    /// Must be called before any cache operations.
    /// </summary>
    public static void Initialize(string appName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appName);
        _appName = appName;
    }

    private static string AppName => _appName
        ?? throw new InvalidOperationException("CoreCache.Initialize(appName) must be called before using cache methods.");

    /// <summary>
    /// Gets the base path for all caches.
    /// Windows: %LOCALAPPDATA%\{appName}
    /// macOS/Linux: ~/.local/share/{appName}
    /// </summary>
    public static string GetBasePath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, AppName);
    }

    /// <summary>
    /// Gets the path for a specific cache category.
    /// </summary>
    public static string GetCategoryPath(string category)
    {
        return Path.Combine(GetBasePath(), category);
    }

    /// <summary>
    /// Tries to read cached content by category and key.
    /// </summary>
    /// <returns>The cached content, or null if not found.</returns>
    public static string? TryGet(string category, string key, string extension = "json")
    {
        var path = GetFilePath(category, key, extension);
        if (File.Exists(path))
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    /// <summary>
    /// Tries to read cached content as raw bytes. Avoids the StreamReader
    /// overhead of <see cref="TryGet(string, string, string)"/>.
    /// </summary>
    public static byte[]? TryGetBytes(string category, string key, string extension = "json")
    {
        var path = GetFilePath(category, key, extension);
        if (File.Exists(path))
        {
            try
            {
                return File.ReadAllBytes(path);
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    /// <summary>
    /// Tries to read cached content with a maximum age. Returns null if the entry
    /// is missing or older than <paramref name="maxAge"/>.
    /// </summary>
    public static string? TryGet(string category, string key, TimeSpan maxAge, string extension = "json")
    {
        var path = GetFilePath(category, key, extension);
        try
        {
            var info = new FileInfo(path);
            if (info.Exists && (DateTime.UtcNow - info.LastWriteTimeUtc) < maxAge)
            {
                return File.ReadAllText(path);
            }
        }
        catch
        {
            // Best-effort
        }
        return null;
    }

    /// <summary>
    /// Stores content in the cache under the given category and key.
    /// Best-effort — failures are silently ignored.
    /// </summary>
    public static void Set(string category, string key, string content, string extension = "json")
    {
        try
        {
            var path = GetFilePath(category, key, extension);
            var dir = Path.GetDirectoryName(path);
            if (dir != null && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(path, content);
        }
        catch
        {
            // Caching is best-effort
        }
    }

    /// <summary>
    /// Stores raw byte content in the cache. Avoids the string encoding
    /// overhead of <see cref="Set(string, string, string, string)"/>.
    /// </summary>
    public static void SetBytes(string category, string key, byte[] content, string extension = "json")
    {
        try
        {
            var path = GetFilePath(category, key, extension);
            var dir = Path.GetDirectoryName(path);
            if (dir != null && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllBytes(path, content);
        }
        catch
        {
            // Caching is best-effort
        }
    }

    /// <summary>
    /// Clears a specific cache category, or all categories if none specified.
    /// </summary>
    /// <returns>The number of bytes freed.</returns>
    public static long Clear(string? category = null)
    {
        var targetPath = category != null ? GetCategoryPath(category) : GetBasePath();
        if (!Directory.Exists(targetPath))
            return 0;

        var size = GetDirectorySize(targetPath);
        try
        {
            Directory.Delete(targetPath, recursive: true);
        }
        catch
        {
            // Best-effort
        }
        return size;
    }

    /// <summary>
    /// Gets cache statistics for a specific category or all categories.
    /// </summary>
    public static CacheInfo GetCacheInfo(string? category = null)
    {
        var targetPath = category != null ? GetCategoryPath(category) : GetBasePath();
        if (!Directory.Exists(targetPath))
            return new CacheInfo(targetPath, 0, 0);

        var size = GetDirectorySize(targetPath);
        var fileCount = Directory.GetFiles(targetPath, "*", SearchOption.AllDirectories).Length;
        return new CacheInfo(targetPath, size, fileCount);
    }

    /// <summary>
    /// Gets the file path for a cached item using SHA256 hash partitioning.
    /// Format: {basePath}/{category}/{hash[0:2]}/{hash[2:]}.{extension}
    /// </summary>
    internal static string GetFilePath(string category, string key, string extension = "json")
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        var hashString = Convert.ToHexString(hash).ToLowerInvariant();

        var subDir = hashString[..2];
        var fileName = $"{hashString[2..]}.{extension}";

        return Path.Combine(GetCategoryPath(category), subDir, fileName);
    }

    private static long GetDirectorySize(string path)
    {
        if (!Directory.Exists(path))
            return 0;

        return new DirectoryInfo(path)
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Sum(f => f.Length);
    }
}

/// <summary>
/// Cache statistics for a category or the entire cache.
/// </summary>
public record CacheInfo(string Path, long SizeBytes, int FileCount);
