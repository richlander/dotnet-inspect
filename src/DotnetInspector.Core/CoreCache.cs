using System.Globalization;
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
    private static readonly Dictionary<string, VersionedCacheCategory> s_versionedCategories =
        new(StringComparer.OrdinalIgnoreCase);
    private static CancellationTokenSource? s_maintenanceCts;
    private static Task<CacheMaintenanceResult>? s_maintenanceTask;
    private static CacheMaintenanceProgress? s_maintenanceProgress;
    private static Dictionary<VersionedCacheCleanupKey, Task> s_maintenanceTasks = [];

    /// <summary>
    /// Initializes the cache with the application name used for the cache directory.
    /// Must be called before any cache operations.
    /// </summary>
    public static void Initialize(string appName, string? basePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appName);
        lock (s_maintenanceLock)
        {
            string? previousRoot = _appName is null ? null : GetBasePath();
            s_maintenanceCts?.Cancel();
            WaitForMaintenanceTasksBestEffort();
            CacheMaintenanceResult previousProgress =
                s_maintenanceProgress?.TakeSnapshot() ?? default;
            s_maintenanceCts?.Dispose();
            _appName = appName;
            _basePathOverride = basePath;
            s_maintenanceCts = new CancellationTokenSource();
            s_maintenanceTask = null;
            s_maintenanceProgress = new CacheMaintenanceProgress();
            s_maintenanceTasks = [];
            if (previousRoot is not null
                && IsSamePath(previousRoot, GetBasePath()))
            {
                s_maintenanceProgress.Record(previousProgress);
            }

            foreach (VersionedCacheCategory category in s_versionedCategories.Values)
                ScheduleVersionedCategoryCleanup(category);
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
    /// causes maintenance to delete older sibling directories such as
    /// <c>pkg-index-v7</c> while preserving future versions.
    /// </summary>
    public static void RegisterVersionedCategory(string prefix, string current)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(current);
        VersionedCacheCategory category = CreateVersionedCacheCategory(prefix, current);

        lock (s_maintenanceLock)
        {
            if (s_versionedCategories.TryGetValue(prefix, out VersionedCacheCategory registered))
            {
                if (registered.CurrentVersion != category.CurrentVersion
                    || !registered.Current.Equals(current, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Cache category prefix '{prefix}' is already registered "
                        + $"with current category '{registered.Current}'.");
                }

                ScheduleVersionedCategoryCleanup(registered);
                return;
            }

            s_versionedCategories.Add(prefix, category);
            ScheduleVersionedCategoryCleanup(category);
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
        lock (s_maintenanceLock)
        {
            CacheMaintenanceResult maintenance =
                WaitForMaintenance(
                    Timeout.InfiniteTimeSpan,
                    consumeProgress: category is null);
            var targetPath = category != null ? GetCategoryPath(category) : GetBasePath();
            EnsurePathInCacheContext(targetPath);
            long maintenanceBytes = category is null ? maintenance.BytesFreed : 0;
            if (!Directory.Exists(targetPath))
                return maintenanceBytes;

            var size = GetDirectorySize(targetPath);
            try
            {
                Directory.Delete(targetPath, recursive: true);
            }
            catch (DirectoryNotFoundException) when (!Directory.Exists(targetPath))
            {
                // Another process completed the same cache deletion.
            }

            return maintenanceBytes + size;
        }
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
    /// the amount of obsolete cache data removed since the previous drain.
    /// </summary>
    public static CacheMaintenanceResult CancelAndWaitForMaintenance(TimeSpan timeout)
        => WaitForMaintenance(timeout, consumeProgress: true);

    private static CacheMaintenanceResult WaitForMaintenance(
        TimeSpan timeout,
        bool consumeProgress)
    {
        lock (s_maintenanceLock)
        {
            Task<CacheMaintenanceResult> task = RequestVersionedCategoryCleanupAsync();
            CancellationTokenSource? cts = s_maintenanceCts;
            CacheMaintenanceProgress? progress = s_maintenanceProgress;

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

            if (progress is null)
                return default;

            return consumeProgress
                ? progress.TakeSnapshot()
                : progress.Snapshot();
        }
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
    }

    /// <summary>
    /// Returns the aggregate versioned-category cleanup task so callers can
    /// observe or drain the real operation.
    /// </summary>
    internal static Task<CacheMaintenanceResult> RequestVersionedCategoryCleanupAsync()
    {
        lock (s_maintenanceLock)
        {
            if (_appName is null || s_versionedCategories.Count == 0)
                return Task.FromResult(default(CacheMaintenanceResult));

            foreach (VersionedCacheCategory category in s_versionedCategories.Values)
                ScheduleVersionedCategoryCleanup(category);

            return s_maintenanceTask ??= AwaitMaintenanceAsync(
                [.. s_maintenanceTasks.Values],
                s_maintenanceProgress!);
        }
    }

    private static void ScheduleVersionedCategoryCleanup(
        VersionedCacheCategory category)
    {
        if (_appName is null)
            return;

        StartNewMaintenanceGenerationIfCanceled();
        string root = GetBasePath();
        var key = new VersionedCacheCleanupKey(
            root,
            category.Prefix,
            category.CurrentVersion);
        if (s_maintenanceTasks.ContainsKey(key))
            return;

        s_maintenanceCts ??= new CancellationTokenSource();
        s_maintenanceProgress ??= new CacheMaintenanceProgress();
        CancellationToken token = s_maintenanceCts.Token;
        CacheMaintenanceProgress progress = s_maintenanceProgress;
        s_maintenanceTasks.Add(
            key,
            Task.Run(
                () => CleanupVersionedCategory(
                    root,
                    category,
                    token,
                    progress),
                CancellationToken.None));
        s_maintenanceTask = null;
    }

    private static void StartNewMaintenanceGenerationIfCanceled()
    {
        if (s_maintenanceCts is not { IsCancellationRequested: true })
            return;

        WaitForMaintenanceTasksBestEffort();

        CacheMaintenanceResult carriedProgress =
            s_maintenanceProgress?.TakeSnapshot() ?? default;
        s_maintenanceCts.Dispose();
        s_maintenanceCts = new CancellationTokenSource();
        s_maintenanceTask = null;
        s_maintenanceProgress = new CacheMaintenanceProgress();
        s_maintenanceProgress.Record(carriedProgress);
        s_maintenanceTasks = [];
    }

    private static void WaitForMaintenanceTasksBestEffort()
    {
        try
        {
            Task.WaitAll([.. s_maintenanceTasks.Values]);
        }
        catch
        {
            // Replacement and shutdown still proceed after best-effort maintenance.
        }
    }

    private static async Task<CacheMaintenanceResult> AwaitMaintenanceAsync(
        Task[] tasks,
        CacheMaintenanceProgress progress)
    {
        await Task.WhenAll(tasks).ConfigureAwait(false);
        return progress.Snapshot();
    }

    private static void CleanupVersionedCategory(
        string root,
        VersionedCacheCategory category,
        CancellationToken cancellationToken,
        CacheMaintenanceProgress progress)
    {
        if (!Directory.Exists(root))
            return;

        string[] directories;
        try
        {
            directories = Directory.GetDirectories(root);
        }
        catch
        {
            return;
        }

        foreach (string directory in directories)
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            try
            {
                var name = Path.GetFileName(directory);
                if (!name.StartsWith(category.Prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                ReadOnlySpan<char> suffix = name.AsSpan(category.Prefix.Length);
                // A newer executable may run beside or before this one, so a
                // downgrade must preserve contracts it does not understand.
                if (!int.TryParse(
                    suffix,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int version)
                    || version >= category.CurrentVersion)
                {
                    continue;
                }

                if (!IsSameOrChildPath(directory, root))
                    continue;

                var size = GetDirectorySizeBestEffort(directory);
                if (cancellationToken.IsCancellationRequested)
                    return;

                Directory.Delete(directory, recursive: true);
                progress.RecordDeletion(size);
            }
            catch
            {
                // Cache cleanup is best-effort and retried on the next initialization.
            }
        }
    }

    private static VersionedCacheCategory CreateVersionedCacheCategory(
        string prefix,
        string current)
    {
        if (!current.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(
                current.AsSpan(prefix.Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int currentVersion))
        {
            throw new ArgumentException(
                $"Current cache category '{current}' must be the prefix '{prefix}' "
                + "followed by a non-negative integer contract version.",
                nameof(current));
        }

        return new VersionedCacheCategory(prefix, current, currentVersion);
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

    private static bool IsSamePath(string left, string right) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left))
            .Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
                StringComparison.OrdinalIgnoreCase);
}

public readonly record struct CacheMaintenanceResult(long BytesFreed, int DirectoriesDeleted);

internal readonly record struct VersionedCacheCategory(
    string Prefix,
    string Current,
    int CurrentVersion);

internal readonly record struct VersionedCacheCleanupKey(
    string Root,
    string Prefix,
    int CurrentVersion);

internal sealed class CacheMaintenanceProgress
{
    // Both fields are read and written together under this single lock so that
    // no caller can observe a byte count and directory count from different
    // deletions (or a reset that only applies to one field). See
    // docs/design/corecache-maintenance-lifecycle.md and
    // docs/models/corecache-maintenance-progress/ for the accounting contract
    // this enforces; the TLA+ model's Safety.cfg validates that guarding all
    // four methods with one lock (AllowTornWrite = FALSE, AllowTornRead =
    // FALSE) eliminates the torn-accounting race.
    private readonly object _lock = new();
    private long _bytesFreed;
    private int _directoriesDeleted;

    public void RecordDeletion(long bytesFreed)
    {
        lock (_lock)
        {
            _bytesFreed += bytesFreed;
            _directoriesDeleted++;
        }
    }

    public void Record(CacheMaintenanceResult result)
    {
        lock (_lock)
        {
            _bytesFreed += result.BytesFreed;
            _directoriesDeleted += result.DirectoriesDeleted;
        }
    }

    public CacheMaintenanceResult Snapshot()
    {
        lock (_lock)
        {
            return new(_bytesFreed, _directoriesDeleted);
        }
    }

    public CacheMaintenanceResult TakeSnapshot()
    {
        lock (_lock)
        {
            var result = new CacheMaintenanceResult(_bytesFreed, _directoriesDeleted);
            _bytesFreed = 0;
            _directoriesDeleted = 0;
            return result;
        }
    }
}

/// <summary>
/// Cache statistics for a category or the entire cache.
/// </summary>
public record CacheInfo(string Path, long SizeBytes, int FileCount);
