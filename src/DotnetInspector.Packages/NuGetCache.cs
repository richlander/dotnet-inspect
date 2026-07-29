using ILInspector.MetadataPrimitives;
using DotnetInspector.Core;
using NuGet.Versioning;

namespace DotnetInspector.Packages;

/// <summary>
/// A package directory that became visible only after its complete contents
/// were validated and atomically published.
/// </summary>
public sealed record CommittedPackage(string ExtractPath, string? NupkgPath);

/// <summary>
/// Utilities for working with NuGet package caches.
/// Uses platform-appropriate cache directories (XDG on Linux, ~/Library/Caches on macOS).
/// Never writes to ~/.nuget/packages (read-only).
/// Call <see cref="Initialize"/> before using app cache methods.
/// Source content caching is delegated to <see cref="CoreCache"/>.
/// </summary>
public static class NuGetCache
{
    private const string PackageContentCategory = "package-content-v2";
    private const string PackageContentCategoryPrefix = "package-content-v";
    public const string CommitMarkerFileName = ".dotnet-inspect.complete";
    private static string? _appName;
    private static bool _skipNuGetCache;

    /// <summary>
    /// Initializes the cache with the application name used for the cache directory.
    /// Must be called before any app cache operations.
    /// Also initializes <see cref="CoreCache"/> with the same app name.
    /// </summary>
    /// <param name="appName">Application name used as the cache subdirectory (e.g., "dotnet-inspect")</param>
    /// <param name="basePath">Optional override for the cache base directory</param>
    /// <param name="skipNuGetCache">When true, skip the NuGet global cache (~/.nuget/packages)</param>
    public static void Initialize(string appName, string? basePath = null, bool skipNuGetCache = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appName);
        _appName = appName;
        _skipNuGetCache = skipNuGetCache;
        CoreCache.Initialize(appName, basePath);
        CoreCache.RegisterVersionedCategory(
            PackageContentCategoryPrefix,
            PackageContentCategory);
    }

    private static string AppName => _appName
        ?? throw new InvalidOperationException("NuGetCache.Initialize(appName) must be called before using app cache methods.");

    /// <summary>
    /// Validates that a value is safe to use as a path component, so an attacker-influenced
    /// package coordinate cannot escape or reset the cache root. Delegates to
    /// <see cref="HardenedPath.ValidatePathComponent"/>, which is the single owner of this rule;
    /// this method's own copy of it had drifted (it accepted reserved device names, every control
    /// character other than <c>\0</c>, and names the host rewrites before opening).
    /// </summary>
    internal static void ValidatePathComponent(string value, string name)
        => HardenedPath.ValidatePathComponent(value, name);

    /// <summary>
    /// Gets the path to the NuGet package cache (read-only).
    /// </summary>
    public static string GetNuGetCachePath()
    {
        // NEVER write to ~/.nuget/packages; OK to read
        
        // Check NUGET_PACKAGES environment variable first
        var nugetPackages = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrEmpty(nugetPackages) && Directory.Exists(nugetPackages))
        {
            return nugetPackages;
        }

        // Default: ~/.nuget/packages on all platforms
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".nuget", "packages");
    }

    /// <summary>
    /// Gets the base path for application caches (read-write).
    /// Delegates to <see cref="CoreCache.GetBasePath"/>.
    /// </summary>
    public static string GetAppCacheBasePath() => CoreCache.GetBasePath();

    /// <summary>
    /// Gets the default (non-overridden) base path for application caches.
    /// Always returns the platform-default directory, ignoring isolation overrides.
    /// </summary>
    public static string GetDefaultAppCacheBasePath() => CoreCache.GetDefaultBasePath();

    /// <summary>
    /// Gets the legacy package-artifact root used by symbol caches.
    /// Extracted package contents use <see cref="GetPackageContentCachePath"/>.
    /// </summary>
    public static string GetAppCachePath()
    {
        return Path.Combine(GetAppCacheBasePath(), "packages");
    }

    /// <summary>
    /// Gets the versioned application cache for transactionally published
    /// package contents.
    /// </summary>
    public static string GetPackageContentCachePath()
    {
        return CoreCache.GetCategoryPath(PackageContentCategory);
    }

    /// <summary>
    /// Gets the path to the source content cache (read-write).
    /// </summary>
    public static string GetSourceCachePath()
    {
        return CoreCache.GetCategoryPath("sources");
    }

    /// <summary>
    /// Tries to find a cached package in either the NuGet cache or the app cache.
    /// </summary>
    /// <param name="packageName">The package name (case-insensitive)</param>
    /// <param name="version">The package version</param>
    /// <returns>The path to the cached package directory, or null if not found</returns>
    /// <remarks>
    /// An unsafe coordinate answers null rather than throwing. No cache entry can be named by one,
    /// so "not found" is the truthful answer, and it is the answer this method's contract promises;
    /// throwing from a <c>Try</c> method crashed the CLI on <c>package CON@1.0.0 --version</c> and
    /// made <see cref="PackageExtractor"/>'s nuspec probe report a hostile coordinate as an error
    /// rather than refusing it. The refusal stays visible where it matters: every path that
    /// acquires or extracts a package still refuses the coordinate loudly.
    /// </remarks>
    public static string? TryGetCachedPackage(string packageName, string version)
    {
        if (!HardenedPath.IsSafePathComponent(packageName) || !HardenedPath.IsSafePathComponent(version))
            return null;

        var normalizedName = packageName.ToLowerInvariant();
        var normalizedVersion = version.ToLowerInvariant();
        var cacheKey = $"{normalizedName}@{normalizedVersion}";

        // Check NuGet cache first (more likely to have packages) — skip in isolated mode
        if (!_skipNuGetCache)
        {
            var nugetCachePath = GetNuGetCachePath();
            if (Directory.Exists(nugetCachePath))
            {
                var nugetPackageDir = Path.Combine(nugetCachePath, normalizedName, normalizedVersion);
                if (Directory.Exists(nugetPackageDir) && IsCachedPackageValid(nugetPackageDir, normalizedName))
                {
                    InfoTracker.RecordCacheHit();
                    CacheTelemetry.Record("nuget-global-packages", cacheKey, CacheAccessResult.Hit);
                    return nugetPackageDir;
                }
            }
        }

        // Check app cache
        var appCachePath = GetPackageContentCachePath();
        if (Directory.Exists(appCachePath))
        {
            var appPackageDir = Path.Combine(appCachePath, normalizedName, normalizedVersion);
            if (IsCommittedPackageValid(
                appPackageDir,
                normalizedName,
                normalizedVersion))
            {
                InfoTracker.RecordCacheHit();
                CacheTelemetry.Record("packages", cacheKey, CacheAccessResult.Hit);
                return appPackageDir;
            }
        }

        CacheTelemetry.Record("packages", cacheKey, CacheAccessResult.Miss);
        InfoTracker.RecordCacheMiss();
        return null;
    }

    /// <summary>
    /// Gets the final path for a package in the transactional content cache.
    /// </summary>
    public static string GetPackageCachePath(string packageName, string version)
    {
        ValidatePathComponent(packageName, "package name");
        ValidatePathComponent(version, "version");

        var appCachePath = GetPackageContentCachePath();
        var packageDir = Path.Combine(appCachePath, packageName.ToLowerInvariant(), version.ToLowerInvariant());
        return packageDir;
    }

    /// <summary>
    /// Validates and atomically publishes an extracted package to the app cache.
    /// Concurrent publishers either win the final rename or converge on the
    /// already committed winner.
    /// </summary>
    /// <param name="extractedPath">Path to the extracted package contents</param>
    /// <param name="nupkgPath">Optional source archive to retain with the committed contents</param>
    /// <param name="packageName">The package name</param>
    /// <param name="version">The package version</param>
    public static CommittedPackage CommitPackage(
        string extractedPath,
        string? nupkgPath,
        string packageName,
        string version)
    {
        ValidatePathComponent(packageName, "package name");
        ValidatePathComponent(version, "version");

        string normalizedName = packageName.ToLowerInvariant();
        string normalizedVersion = version.ToLowerInvariant();
        string targetPath = GetPackageCachePath(
            normalizedName,
            normalizedVersion);
        string? parentDir = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException(
                $"Package cache path has no parent: {targetPath}");

        CoreCache.EnsurePathInCacheContext(targetPath);
        Directory.CreateDirectory(parentDir);

        if (IsCommittedPackageValid(
            targetPath,
            normalizedName,
            normalizedVersion))
        {
            return OpenCommittedPackage(
                targetPath,
                normalizedName,
                normalizedVersion);
        }

        if (Directory.Exists(targetPath))
        {
            throw new InvalidDataException(
                $"Package cache entry '{targetPath}' is incomplete or corrupt. Clear the cache before retrying.");
        }

        string stagingPath = Path.Combine(
            parentDir,
            $".{normalizedVersion}.tmp-{Guid.NewGuid():N}");
        CoreCache.EnsurePathInCacheContext(stagingPath);

        try
        {
            CopyDirectory(extractedPath, stagingPath);

            string? committedNupkgPath = null;
            if (nupkgPath is not null)
            {
                committedNupkgPath = Path.Combine(
                    stagingPath,
                    $"{normalizedName}.{normalizedVersion}.nupkg");
                File.Copy(nupkgPath, committedNupkgPath, overwrite: false);
            }

            if (!IsCachedPackageValid(stagingPath))
            {
                throw new InvalidDataException(
                    $"Package '{packageName}@{version}' has no valid extracted package structure.");
            }

            using (var marker = new FileStream(
                Path.Combine(stagingPath, CommitMarkerFileName),
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            using (var writer = new StreamWriter(marker))
            {
                writer.Write(
                    GetCommitMarkerContent(
                        normalizedName,
                        normalizedVersion));
            }

            try
            {
                Directory.Move(stagingPath, targetPath);
            }
            catch (IOException) when (IsCommittedPackageValid(
                targetPath,
                normalizedName,
                normalizedVersion))
            {
                return OpenCommittedPackage(
                    targetPath,
                    normalizedName,
                    normalizedVersion);
            }

            CacheTelemetry.Record(
                "packages",
                $"{normalizedName}@{normalizedVersion}",
                CacheAccessResult.Store);

            return new CommittedPackage(
                targetPath,
                committedNupkgPath is null
                    ? null
                    : Path.Combine(targetPath, Path.GetFileName(committedNupkgPath)));
        }
        finally
        {
            if (Directory.Exists(stagingPath))
            {
                try
                {
                    Directory.Delete(stagingPath, recursive: true);
                }
                catch (IOException)
                {
                    // The committed destination is authoritative; abandoned
                    // staging cleanup remains best-effort.
                }
                catch (UnauthorizedAccessException)
                {
                    // The committed destination is authoritative; abandoned
                    // staging cleanup remains best-effort.
                }
            }
        }
    }

    /// <summary>
    /// Returns the newest cached version of a package from the NuGet or app cache.
    /// Pure disk I/O — never hits the network.
    /// </summary>
    public static string? TryGetLatestCachedVersion(string packageName)
    {
        var normalizedName = packageName.ToLowerInvariant();

        // Newest non-prerelease, structurally-valid version across both caches.
        bool IsNuGetCacheValid(string dir) =>
            IsCachedPackageValid(dir, normalizedName);
        bool IsAppCacheValid(string dir)
        {
            string version = Path.GetFileName(dir);
            return IsCommittedPackageValid(dir, normalizedName, version);
        }
        VersionDir? best = null;

        // Check NuGet global cache — skip in isolated mode
        if (!_skipNuGetCache)
        {
            best = VersionDirectory.Higher(best, VersionDirectory.SelectBest(
                Path.Combine(GetNuGetCachePath(), normalizedName),
                includePrerelease: false,
                IsNuGetCacheValid));
        }

        // Check app cache
        try
        {
            best = VersionDirectory.Higher(best, VersionDirectory.SelectBest(
                Path.Combine(GetPackageContentCachePath(), normalizedName),
                includePrerelease: false,
                IsAppCacheValid));
        }
        catch (InvalidOperationException)
        {
            // App cache not initialized
        }

        return best?.DirName;
    }

    /// <summary>
    /// Checks if a cached package has the expected structure.
    /// </summary>
    public static bool IsCachedPackageValid(string cachedPath) => IsCachedPackageValid(cachedPath, null);

    /// <summary>
    /// Checks if a cached package has the expected structure.
    /// When packageName is provided, uses direct file check instead of directory scan.
    /// </summary>
    public static bool IsCachedPackageValid(string cachedPath, string? packageName)
    {
        if (!Directory.Exists(cachedPath))
            return false;

        // Valid if it contains a .nuspec file (extracted) or lib/tools directory
        bool hasNuspec;
        if (packageName != null)
        {
            // Fast path: check for expected nuspec name directly
            hasNuspec = File.Exists(Path.Combine(cachedPath, $"{packageName}.nuspec"));
        }
        else
        {
            // Extracted archives preserve authored casing for the nuspec name.
            hasNuspec = Directory.EnumerateFiles(cachedPath)
                .Any(path => path.EndsWith(
                    ".nuspec",
                    StringComparison.OrdinalIgnoreCase));
        }
        var hasLib = Directory.Exists(Path.Combine(cachedPath, "lib"));
        var hasTools = Directory.Exists(Path.Combine(cachedPath, "tools"));

        return hasNuspec || hasLib || hasTools;
    }

    private static bool IsCommittedPackageValid(
        string cachedPath,
        string packageName,
        string version)
    {
        try
        {
            if (!IsCachedPackageValid(cachedPath))
                return false;

            return File.ReadAllText(
                Path.Combine(cachedPath, CommitMarkerFileName))
                .Equals(
                    GetCommitMarkerContent(packageName, version),
                    StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static CommittedPackage OpenCommittedPackage(
        string targetPath,
        string packageName,
        string version)
    {
        string nupkgPath = Path.Combine(
            targetPath,
            $"{packageName}.{version}.nupkg");
        return new CommittedPackage(
            targetPath,
            File.Exists(nupkgPath) ? nupkgPath : null);
    }

    private static string GetCommitMarkerContent(
        string packageName,
        string version)
        => $"{PackageContentCategory}:{packageName}@{version}";

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var targetFile = Path.Combine(targetDir, Path.GetFileName(file));
            File.Copy(file, targetFile, overwrite: true);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var targetSubDir = Path.Combine(targetDir, Path.GetFileName(dir));
            CopyDirectory(dir, targetSubDir);
        }
    }

    /// <summary>
    /// Gets cached source content for a URL, if available.
    /// Delegates to <see cref="CoreCache"/>.
    /// </summary>
    public static string? TryGetCachedSource(string url) => CoreCache.TryGet("sources", url, "txt");

    /// <summary>
    /// Caches source content for a URL.
    /// Delegates to <see cref="CoreCache"/>.
    /// </summary>
    public static void CacheSource(string url, string content) => CoreCache.Set("sources", url, content, "txt");
}
