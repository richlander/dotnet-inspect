using System.Security.Cryptography;
using System.Text;

namespace NuGetFetch;

/// <summary>
/// Generic disk cache with category-based partitioning and TTL support.
/// Uses SHA256-hashed keys and subdirectory bucketing for filesystem safety.
/// Caching is best-effort: all read/write failures are silently ignored
/// to ensure cache problems never disrupt normal operation.
/// </summary>
public class ResponseCache
{
    private readonly string _basePath;

    public ResponseCache(string appName, string? basePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appName);

        _basePath = basePath
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                appName);
    }

    /// <summary>
    /// Gets the base path for this cache.
    /// </summary>
    public string BasePath => _basePath;

    /// <summary>
    /// Gets the path for a specific cache category.
    /// </summary>
    public string GetCategoryPath(string category) =>
        Path.Combine(_basePath, category);

    /// <summary>
    /// Tries to read cached content by category and key.
    /// </summary>
    public string? TryGet(string category, string key, string extension = "json")
    {
        string path = GetFilePath(category, key, extension);

        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return File.ReadAllText(path);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Tries to read cached content with a maximum age.
    /// Returns null if missing or older than maxAge.
    /// </summary>
    public string? TryGet(string category, string key, TimeSpan maxAge, string extension = "json")
    {
        string path = GetFilePath(category, key, extension);

        try
        {
            FileInfo info = new(path);

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
    /// Tries to read cached content as raw bytes.
    /// </summary>
    public byte[]? TryGetBytes(string category, string key, string extension = "json")
    {
        string path = GetFilePath(category, key, extension);

        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return File.ReadAllBytes(path);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Tries to read cached bytes with a maximum age.
    /// </summary>
    public byte[]? TryGetBytes(string category, string key, TimeSpan maxAge, string extension = "json")
    {
        string path = GetFilePath(category, key, extension);

        try
        {
            FileInfo info = new(path);

            if (info.Exists && (DateTime.UtcNow - info.LastWriteTimeUtc) < maxAge)
            {
                return File.ReadAllBytes(path);
            }
        }
        catch
        {
            // Best-effort
        }

        return null;
    }

    /// <summary>
    /// Stores content in the cache. Best-effort — failures are silently ignored
    /// to ensure cache problems never disrupt normal operation.
    /// </summary>
    public void Set(string category, string key, string content, string extension = "json")
    {
        try
        {
            string path = GetFilePath(category, key, extension);
            string? dir = Path.GetDirectoryName(path);

            if (dir is not null && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // Atomic write: write to temp file, then move into place
            string tempPath = path + $".tmp-{Guid.NewGuid():N}";
            File.WriteAllText(tempPath, content);

            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            // Caching is best-effort
        }
    }

    /// <summary>
    /// Stores raw byte content in the cache. Best-effort — failures are silently
    /// ignored to ensure cache problems never disrupt normal operation.
    /// </summary>
    public void SetBytes(string category, string key, byte[] content, string extension = "json")
    {
        try
        {
            string path = GetFilePath(category, key, extension);
            string? dir = Path.GetDirectoryName(path);

            if (dir is not null && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // Atomic write: write to temp file, then move into place
            string tempPath = path + $".tmp-{Guid.NewGuid():N}";
            File.WriteAllBytes(tempPath, content);

            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            // Caching is best-effort
        }
    }

    /// <summary>
    /// Clears a specific cache category, or all categories if none specified.
    /// Returns the number of bytes freed.
    /// </summary>
    public long Clear(string? category = null)
    {
        string targetPath = category is not null ? GetCategoryPath(category) : _basePath;

        if (!Directory.Exists(targetPath))
        {
            return 0;
        }

        long size = GetDirectorySize(targetPath);

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
    public CacheInfo GetCacheInfo(string? category = null)
    {
        string targetPath = category is not null ? GetCategoryPath(category) : _basePath;

        if (!Directory.Exists(targetPath))
        {
            return new CacheInfo(targetPath, 0, 0);
        }

        long size = GetDirectorySize(targetPath);
        int fileCount = Directory.GetFiles(targetPath, "*", SearchOption.AllDirectories).Length;
        return new CacheInfo(targetPath, size, fileCount);
    }

    /// <summary>
    /// Gets the file path for a cached item using SHA256 hash partitioning.
    /// Format: {basePath}/{category}/{hash[0:2]}/{hash[2:]}.{extension}
    /// </summary>
    internal string GetFilePath(string category, string key, string extension = "json")
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        string hashString = Convert.ToHexString(hash).ToLowerInvariant();
        string subDir = hashString[..2];
        string fileName = $"{hashString[2..]}.{extension}";

        return Path.Combine(GetCategoryPath(category), subDir, fileName);
    }

    private static long GetDirectorySize(string path)
    {
        if (!Directory.Exists(path))
        {
            return 0;
        }

        return new DirectoryInfo(path)
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Sum(f => f.Length);
    }
}

public record CacheInfo(string Path, long SizeBytes, int FileCount);
