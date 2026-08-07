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
    private static string? _basePathOverride;
    private static readonly object s_maintenanceLock = new();
    private static readonly List<VersionedCacheCategory> s_versionedCategories = [];
    private static CancellationTokenSource? s_maintenanceCts;
    private static Task<CacheMaintenanceResult>? s_maintenanceTask;
    private static CacheMaintenanceProgress? s_maintenanceProgress;
    private static AsyncCache<string, CacheMaintenanceResult> s_maintenanceRequests =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes the cache with the application name used for the cache directory.
    /// Must be called before any cache operations.
    /// </summary>
    public static void Initialize(string appName, string? basePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appName);
        _appName = appName;
        _basePathOverride = basePath;
        lock (s_maintenanceLock)
        {
            s_maintenanceCts?.Cancel();
            s_maintenanceCts?.Dispose();
            s_maintenanceCts = null;
            s_maintenanceTask = null;
            s_maintenanceProgress = null;
            s_maintenanceRequests = new AsyncCache<string, CacheMaintenanceResult>(
                StringComparer.Ordinal);
        }
    }

    private static string AppName => _appName
        ?? throw new InvalidOperationException("CoreCache.Initialize(appName) must be called before using cache methods.");

    /// <summary>
    /// Gets the base path for all caches.
    /// Returns the override path if set via <see cref="Initialize"/>,
    /// otherwise uses the platform-default local application data directory.
    /// </summary>
    public static string GetBasePath()
    {
        if (_basePathOverride != null)
            return _basePathOverride;

        return GetDefaultBasePath();
    }

    /// <summary>
    /// Gets the default (non-overridden) base path for caches.
    /// Uses XDG-appropriate directories per platform:
    /// Linux: <c>$XDG_CACHE_HOME/appName</c> (defaults to <c>~/.cache/appName</c>),
    /// macOS: <c>~/Library/Caches/appName</c>,
    /// Windows: <c>%LOCALAPPDATA%\appName</c>.
    /// </summary>
    public static string GetDefaultBasePath()
    {
        if (OperatingSystem.IsLinux())
        {
            var xdgCache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
            if (!string.IsNullOrEmpty(xdgCache))
                return Path.Combine(xdgCache, AppName);
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".cache", AppName);
        }

        if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Caches", AppName);
        }

        // Windows: %LOCALAPPDATA%
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, AppName);
    }

    /// <summary>
    /// Returns the pre-XDG cache path (<c>~/.local/share/appName</c>) on Linux/macOS
    /// if it differs from the current default path, or <c>null</c> on Windows.
    /// Used by cache-clear to clean up the old location.
    /// </summary>
    public static string? GetLegacyBasePath()
    {
        if (OperatingSystem.IsWindows())
            return null;

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var legacyPath = Path.Combine(localAppData, AppName);

        return legacyPath != GetDefaultBasePath() ? legacyPath : null;
    }

    /// <summary>
    /// Gets the path for a specific cache category.
    /// </summary>
    public static string GetCategoryPath(string category)
    {
        return Path.Combine(GetBasePath(), category);
    }

    /// <summary>
    /// Throws unless <paramref name="path"/> is inside the active cache root or the legacy cache root.
    /// Use before deleting cache paths to prevent path traversal or accidental user-file deletion.
    /// </summary>
    public static void EnsurePathInCacheContext(string path)
    {
        if (!IsPathInCacheContext(path))
        {
            // The throw carries the same text, and the CLI renders it through
            // the one writer that contains it. Writing here as well produced a
            // second, uncontained copy of the path.
            throw new InvalidOperationException($"Refusing to delete path outside dotnet-inspect cache: {path}");
        }
    }

    /// <summary>
    /// Returns true when a path is the active cache root, the legacy cache root, or a child of either.
    /// </summary>
    public static bool IsPathInCacheContext(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            var fullPath = Path.GetFullPath(path);
            if (IsSameOrChildPath(fullPath, GetBasePath()))
                return true;

            var legacy = GetLegacyBasePath();
            return legacy != null && IsSameOrChildPath(fullPath, legacy);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Registers a versioned cache category family for best-effort cleanup.
    /// For example, prefix <c>pkg-index-v</c> with current <c>pkg-index-v8</c>
    /// causes maintenance to delete sibling directories such as <c>pkg-index-v7</c>.
    /// </summary>
    public static void RegisterVersionedCategory(string prefix, string current)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(current);

        lock (s_maintenanceLock)
        {
            if (s_versionedCategories.Any(c =>
                c.Prefix.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                && c.Current.Equals(current, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            s_versionedCategories.Add(new(prefix, current));
        }
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
                var result = File.ReadAllText(path);
                InfoTracker.RecordCacheHit();
                CacheTelemetry.Record(GetTelemetryCategory(category, extension), key, CacheAccessResult.Hit);
                return result;
            }
            catch
            {
                return null;
            }
        }
        CacheTelemetry.Record(GetTelemetryCategory(category, extension), key, CacheAccessResult.Miss);
        RecordCacheMiss();
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
                var result = File.ReadAllBytes(path);
                InfoTracker.RecordCacheHit();
                CacheTelemetry.Record(GetTelemetryCategory(category, extension), key, CacheAccessResult.Hit);
                return result;
            }
            catch
            {
                return null;
            }
        }
        CacheTelemetry.Record(GetTelemetryCategory(category, extension), key, CacheAccessResult.Miss);
        RecordCacheMiss();
        return null;
    }

    /// <summary>
    /// Tries to read cached content as raw bytes with a maximum age.
    /// Returns null if the entry is missing or older than <paramref name="maxAge"/>.
    /// </summary>
    public static byte[]? TryGetBytes(string category, string key, TimeSpan maxAge, string extension = "json")
    {
        var path = GetFilePath(category, key, extension);
        try
        {
            var info = new FileInfo(path);
            if (info.Exists && (DateTime.UtcNow - info.LastWriteTimeUtc) < maxAge)
            {
                var result = File.ReadAllBytes(path);
                InfoTracker.RecordCacheHit();
                CacheTelemetry.Record(GetTelemetryCategory(category, extension), key, CacheAccessResult.Hit);
                return result;
            }
        }
        catch
        {
            // Best-effort
        }
        CacheTelemetry.Record(GetTelemetryCategory(category, extension), key, CacheAccessResult.Miss);
        RecordCacheMiss();
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
                var result = File.ReadAllText(path);
                InfoTracker.RecordCacheHit();
                CacheTelemetry.Record(GetTelemetryCategory(category, extension), key, CacheAccessResult.Hit);
                return result;
            }
        }
        catch
        {
            // Best-effort
        }
        CacheTelemetry.Record(GetTelemetryCategory(category, extension), key, CacheAccessResult.Miss);
        RecordCacheMiss();
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
            WriteAtomically(path, tempPath => File.WriteAllText(tempPath, content));
            CacheTelemetry.Record(GetTelemetryCategory(category, extension), key, CacheAccessResult.Store);
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
            WriteAtomically(path, tempPath => File.WriteAllBytes(tempPath, content));
            CacheTelemetry.Record(GetTelemetryCategory(category, extension), key, CacheAccessResult.Store);
        }
        catch
        {
            // Caching is best-effort
        }
    }

    private static void WriteAtomically(string path, Action<string> write)
    {
        string tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            write(tempPath);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    /// <summary>
    /// Clears a specific cache category, or all categories if none specified.
    /// </summary>
    /// <returns>The number of bytes freed.</returns>
    public static long Clear(string? category = null)
    {
        var targetPath = category != null ? GetCategoryPath(category) : GetBasePath();
        EnsurePathInCacheContext(targetPath);
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
    /// Waits briefly for in-flight cleanup to finish, cancels if it exceeds the timeout, and returns
    /// the amount of obsolete cache data removed so far.
    /// </summary>
    public static CacheMaintenanceResult CancelAndWaitForMaintenance(TimeSpan timeout)
    {
        Task<CacheMaintenanceResult>? task;
        CancellationTokenSource? cts;
        CacheMaintenanceProgress? progress;
        lock (s_maintenanceLock)
        {
            task = s_maintenanceTask;
            cts = s_maintenanceCts;
            progress = s_maintenanceProgress;
        }

        if (task != null)
        {
            try
            {
                if (!task.Wait(timeout))
                {
                    cts?.Cancel();
                    try { task.Wait(TimeSpan.FromMilliseconds(25)); } catch { }
                }
            }
            catch
            {
                // Best-effort shutdown hook; cleanup must never affect command success.
            }
        }

        if (task is { IsCompletedSuccessfully: true })
            return task.Result;

        return progress?.Snapshot() ?? default;
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

    private static string GetTelemetryCategory(string category, string extension)
        => category.Equals("symbol-misses", StringComparison.OrdinalIgnoreCase)
            ? $"{category}/{extension}"
            : category;

    private static void RecordCacheMiss()
    {
        InfoTracker.RecordCacheMiss();
        _ = RequestVersionedCategoryCleanupAsync();
    }

    /// <summary>
    /// Returns the single versioned-category cleanup task used by cache-miss
    /// maintenance so callers can observe or drain the real operation.
    /// </summary>
    internal static Task<CacheMaintenanceResult> RequestVersionedCategoryCleanupAsync()
    {
        lock (s_maintenanceLock)
        {
            if (s_versionedCategories.Count == 0)
                return Task.FromResult(default(CacheMaintenanceResult));

            string root = GetBasePath();

            if (s_maintenanceTask is { IsFaulted: false, IsCanceled: false })
                return s_maintenanceTask;

            VersionedCacheCategory[] categories = [.. s_versionedCategories];
            s_maintenanceCts?.Dispose();
            s_maintenanceCts = new CancellationTokenSource();
            s_maintenanceProgress = new CacheMaintenanceProgress();
            var token = s_maintenanceCts.Token;
            var progress = s_maintenanceProgress;
            s_maintenanceTask = s_maintenanceRequests.GetOrAddAsync(
                root,
                _ => Task.Run(
                    () => CleanupVersionedCategories(
                        root,
                        categories,
                        token,
                        progress),
                    CancellationToken.None));
            return s_maintenanceTask;
        }
    }

    private static CacheMaintenanceResult CleanupVersionedCategories(
        string root,
        IReadOnlyList<VersionedCacheCategory> categories,
        CancellationToken cancellationToken,
        CacheMaintenanceProgress progress)
    {
        if (!Directory.Exists(root))
            return progress.Snapshot();

        foreach (var category in categories)
        {
            if (cancellationToken.IsCancellationRequested)
                return progress.Snapshot();

            string[] directories;
            try
            {
                directories = Directory.GetDirectories(root, $"{category.Prefix}*");
            }
            catch
            {
                continue;
            }

            foreach (var directory in directories)
            {
                if (cancellationToken.IsCancellationRequested)
                    return progress.Snapshot();

                var name = Path.GetFileName(directory);
                if (name.Equals(category.Current, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!name.StartsWith(category.Prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!IsSameOrChildPath(directory, root))
                    continue;

                var size = GetDirectorySizeBestEffort(directory);
                try
                {
                    Directory.Delete(directory, recursive: true);
                    progress.RecordDeletion(size);
                }
                catch
                {
                    // Cache cleanup is best-effort.
                }
            }
        }

        return progress.Snapshot();
    }

    private static long GetDirectorySizeBestEffort(string path)
    {
        try
        {
            return GetDirectorySize(path);
        }
        catch
        {
            return 0;
        }
    }

    private static long GetDirectorySize(string path)
    {
        if (!Directory.Exists(path))
            return 0;

        return new DirectoryInfo(path)
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Sum(f => f.Length);
    }

    private static bool IsSameOrChildPath(string path, string root)
    {
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));

        return fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(fullRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}

public readonly record struct CacheMaintenanceResult(long BytesFreed, int DirectoriesDeleted);

internal readonly record struct VersionedCacheCategory(string Prefix, string Current);

internal sealed class CacheMaintenanceProgress
{
    private long _bytesFreed;
    private int _directoriesDeleted;

    public void RecordDeletion(long bytesFreed)
    {
        Interlocked.Add(ref _bytesFreed, bytesFreed);
        Interlocked.Increment(ref _directoriesDeleted);
    }

    public CacheMaintenanceResult Snapshot()
        => new(
            Interlocked.Read(ref _bytesFreed),
            Volatile.Read(ref _directoriesDeleted));
}

/// <summary>
/// Cache statistics for a category or the entire cache.
/// </summary>
public record CacheInfo(string Path, long SizeBytes, int FileCount);
