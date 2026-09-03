using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using DotnetInspector.Core;
using NuGet.Versioning;
using NuGetFetch;

namespace DotnetInspector.Packages;

/// <summary>
/// A package directory that became visible only after its complete contents
/// were validated and atomically published.
/// </summary>
public sealed record CommittedPackage(
    string ExtractPath,
    string? NupkgPath,
    string ProducerKey);

/// <summary>
/// An exact cached package payload and the canonical identity of the source
/// that produced it.
/// </summary>
internal sealed record CachedPackage(
        string ExtractPath,
        string ProducerKey,
        bool RequiresArchiveTreeMatch);

/// <summary>
/// Utilities for working with NuGet package caches.
/// Uses platform-appropriate cache directories (XDG on Linux, ~/Library/Caches on macOS).
/// Never writes to ~/.nuget/packages (read-only).
/// Call <see cref="Initialize"/> before using app cache methods.
/// Source content caching is delegated to <see cref="CoreCache"/>.
/// </summary>
public static class NuGetCache
{
    private const string PackageContentCategory = "package-content-v5";
    private const string PackageContentCategoryPrefix = "package-content-v";
    public const string CommitMarkerFileName = ".dotnet-inspect.complete";
    private static readonly Encoding s_utf8Strict =
        new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);
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
    /// Validates that a value is safe to use as a path component. Rejects empty
    /// or whitespace values, traversal (<c>..</c>), separators, volume
    /// qualifiers (<c>:</c>), null characters, and otherwise rooted values, so an
    /// attacker-influenced package coordinate cannot escape or reset the cache
    /// root (a legitimate package id or version contains none of these).
    /// </summary>
    internal static void ValidatePathComponent(string value, string name)
    {
        if (!IsValidPathComponent(value))
        {
            throw new ArgumentException($"Invalid {name}: '{value}'");
        }
    }

    internal static bool IsValidPathComponent(string value) =>
        !(string.IsNullOrWhiteSpace(value)
            || value.Contains("..")
            || value.Contains('/')
            || value.Contains('\\')
            || value.Contains(':')
            || value.Contains('\0')
            || Path.IsPathRooted(value));

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
    /// Gets every NuGet global-packages root dependency resolution can read, with an explicit
    /// <c>NUGET_PACKAGES</c> override first and the platform-default root second.
    /// </summary>
    public static IReadOnlyList<string> GetNuGetPackageRoots()
    {
        List<string> roots = [];
        StringComparer comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        void Add(string? root)
        {
            if (string.IsNullOrWhiteSpace(root))
                return;

            string fullPath = Path.GetFullPath(root);
            if (!roots.Contains(fullPath, comparer))
                roots.Add(fullPath);
        }

        Add(Environment.GetEnvironmentVariable("NUGET_PACKAGES"));
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Add(string.IsNullOrEmpty(home)
            ? null
            : Path.Combine(home, ".nuget", "packages"));
        return roots;
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
    /// Gets the product package-content cache when cache services have already
    /// been initialized.
    /// </summary>
    public static bool TryGetPackageContentCachePath(out string path)
    {
        if (_appName is null)
        {
            path = "";
            return false;
        }

        path = GetPackageContentCachePath();
        return true;
    }

    /// <summary>
    /// Recovers the exact package coordinate and asset path represented by a
    /// file inside a product-owned package-content cache slot.
    /// </summary>
    public static bool TryGetPackageContentIdentity(
        string path,
        out string packageName,
        out string version,
        out string assetPath,
        out string packageDirectory)
    {
        packageName = "";
        version = "";
        assetPath = "";
        packageDirectory = "";
        if (!TryGetPackageContentCachePath(out string cacheRoot))
            return false;

        string relative = Path.GetRelativePath(
            Path.GetFullPath(cacheRoot),
            Path.GetFullPath(path));
        if (Path.IsPathRooted(relative)
            || relative.Equals("..", StringComparison.Ordinal)
            || relative.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal)
            || relative.StartsWith(
                $"..{Path.AltDirectorySeparatorChar}",
                StringComparison.Ordinal))
        {
            return false;
        }

        string[] segments = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 4)
            return false;

        packageName = segments[0];
        version = segments[1];
        assetPath = string.Join('/', segments[3..]);
        packageDirectory = Path.Combine(
            cacheRoot,
            segments[0],
            segments[1],
            segments[2]);
        return true;
    }

    internal static bool UsesGlobalPackages => !_skipNuGetCache;

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
    /// <param name="allowedSourceKeys">
    /// Keys (per <see cref="GetSourceKey"/>) of the sources the caller is
    /// currently configured to read from, in configured order. Cached content
    /// committed by a source outside this set is treated as a miss, so an empty
    /// or <see langword="null"/> list never hits the app cache. The reserved
    /// <c>local</c> key must be included explicitly; <see langword="null"/> is
    /// not shorthand for it. Order matters: slots are consulted in it, so a
    /// higher-precedence source's cached copy answers ahead of a lower one's,
    /// matching the order a cold run would have tried the feeds in.
    /// </param>
    /// <returns>The path to the cached package directory, or null if not found</returns>
    public static string? TryGetCachedPackage(
        string packageName,
        string version,
        IReadOnlyList<string>? allowedSourceKeys)
        => TryGetCachedPackageContent(
            packageName,
            version,
            allowedSourceKeys)?.ExtractPath;

    /// <summary>
    /// Tries to find an exact cached payload and returns its producer identity.
    /// </summary>
    internal static CachedPackage? TryGetCachedPackageContent(
        string packageName,
        string version,
        IReadOnlyList<string>? allowedSourceKeys,
        string? globalPackagesPath = null)
    {
        foreach (CachedPackage candidate in EnumerateCachedPackageContent(
                     packageName,
                     version,
                     allowedSourceKeys,
                     globalPackagesPath))
        {
            return candidate;
        }

        return null;
    }

    /// <summary>
    /// Materializes <see cref="EnumerateCachedPackageContent"/> (tests / callers
    /// that need the full tier set). Prefer the lazy enumerator on admission
    /// paths so global-packages is not touched when an earlier tier admits.
    /// </summary>
    internal static IReadOnlyList<CachedPackage> ListCachedPackageContent(
        string packageName,
        string version,
        IReadOnlyList<string>? allowedSourceKeys,
        string? globalPackagesPath = null,
        IReadOnlyList<string>? globalPackagesPaths = null)
        => [.. EnumerateCachedPackageContent(
            packageName,
            version,
            allowedSourceKeys,
            globalPackagesPath,
            globalPackagesPaths)];

    /// <summary>
    /// Cache tiers for a coordinate, preferred order: product-owned app-cache
    /// slots (configured producer order), then the ordered NuGet global-packages
    /// roots. Yields lazily so a usable app-cache hit never opens global
    /// <c>.nupkg.metadata</c> or inspects a corrupt foreign tree.
    /// </summary>
    internal static IEnumerable<CachedPackage> EnumerateCachedPackageContent(
        string packageName,
        string version,
        IReadOnlyList<string>? allowedSourceKeys,
        string? globalPackagesPath = null,
        IReadOnlyList<string>? globalPackagesPaths = null)
    {
        ValidatePathComponent(packageName, "package name");
        ValidatePathComponent(version, "version");

        var normalizedName = packageName.ToLowerInvariant();
        var normalizedVersion = version.ToLowerInvariant();
        var cacheKey = $"{normalizedName}@{normalizedVersion}";
        bool any = false;

        // App cache first: product-owned and preferred when both tiers exist.
        // A read happens before the tool knows which source would serve the
        // package, so it asks every source the caller is currently configured
        // to read from, in configured order. A slot belonging to any other
        // source is not consulted: those bytes were fetched under an authority
        // this caller no longer claims.
        if (TryGetPackageContentCachePath(out string appCachePath)
            && Directory.Exists(appCachePath))
        {
            foreach (var sourceKey in allowedSourceKeys ?? [])
            {
                var appPackageDir = Path.Combine(
                    appCachePath,
                    normalizedName,
                    normalizedVersion,
                    sourceKey);
                // Marker match alone is enough to surface the slot. Layout and
                // archive admission decide usability so a damaged extracted
                // tree still reaches a typed offline diagnostic.
                if (IsCommittedPackageSlotPresent(
                    appPackageDir,
                    normalizedName,
                    normalizedVersion,
                    sourceKey))
                {
                    if (!any)
                    {
                        any = true;
                        InfoTracker.RecordCacheHit();
                        CacheTelemetry.Record(
                            "packages",
                            cacheKey,
                            CacheAccessResult.Hit);
                    }

                    yield return new CachedPackage(
                        appPackageDir,
                        sourceKey,
                        RequiresArchiveTreeMatch: true);
                }
            }
        }

        // Global-packages only after the caller has exhausted earlier tiers
        // (admission rejected them or none existed).
        if (!_skipNuGetCache)
        {
            IEnumerable<string> roots = globalPackagesPath is not null
                ? [globalPackagesPath]
                : globalPackagesPaths ?? GetNuGetPackageRoots();
            foreach (string root in roots)
            {
                CachedPackage? global = TryGetGlobalPackageContent(
                    root,
                    normalizedName,
                    normalizedVersion,
                    allowedSourceKeys);
                if (global is null)
                    continue;

                if (!any)
                {
                    any = true;
                    InfoTracker.RecordCacheHit();
                    CacheTelemetry.Record(
                        "nuget-global-packages",
                        cacheKey,
                        CacheAccessResult.Hit);
                }

                yield return global;
            }
        }

        if (!any)
        {
            CacheTelemetry.Record("packages", cacheKey, CacheAccessResult.Miss);
            InfoTracker.RecordCacheMiss();
        }
    }

    internal static CachedPackage? TryGetGlobalPackageContent(
        string globalPackagesPath,
        string packageName,
        string version,
        IReadOnlyList<string>? allowedSourceKeys)
    {
        string packageDirectory = Path.Combine(
            globalPackagesPath,
            packageName,
            version);
        // Do not require a full valid layout here: admission decides whether a
        // retained nupkg or extracted tree is usable. A damaged global-packages
        // slot must still surface so offline errors are not "not found".
        if (!Directory.Exists(packageDirectory)
            || !TryReadGlobalPackageSourceKey(
                packageDirectory,
                out string? producerKey)
            || !(allowedSourceKeys?.Contains(producerKey) ?? false))
        {
            return null;
        }

        return new CachedPackage(
            packageDirectory,
            producerKey,
            RequiresArchiveTreeMatch: false);
    }

    /// <summary>
    /// Hard cap on NuGet's <c>.nupkg.metadata</c> sidecar. Real files are tiny
    /// JSON; an unbounded read would let a hostile global-packages tree OOM
    /// the host when the tier is consulted.
    /// </summary>
    internal const int MaxGlobalPackageMetadataBytes = 64 * 1024;

    /// <summary>
    /// Hard cap on the product-owned commit marker
    /// (<see cref="CommitMarkerFileName"/>). Legitimate content is a short
    /// ASCII line (<c>package-content-v5:id@ver:sourceKey</c>); an unbounded
    /// <c>ReadAllText</c> would let a hostile app-cache slot OOM the host on
    /// every <see cref="EnumerateCachedPackageContent"/> probe.
    /// </summary>
    internal const int MaxCommitMarkerBytes = 4 * 1024;

    private static bool TryReadGlobalPackageSourceKey(
        string packageDirectory,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? sourceKey)
    {
        string metadataPath = Path.Combine(packageDirectory, ".nupkg.metadata");
        try
        {
            var info = new FileInfo(metadataPath);
            if (!info.Exists
                || info.Length <= 0
                || info.Length > MaxGlobalPackageMetadataBytes)
            {
                sourceKey = null;
                return false;
            }

            byte[] metadata = new byte[checked((int)info.Length)];
            using (FileStream stream = new(
                metadataPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
            {
                int offset = 0;
                while (offset < metadata.Length)
                {
                    int read = stream.Read(metadata.AsSpan(offset));
                    if (read == 0)
                    {
                        sourceKey = null;
                        return false;
                    }

                    offset += read;
                }

                // One more byte means the file grew past the admitted length.
                if (stream.ReadByte() != -1)
                {
                    sourceKey = null;
                    return false;
                }
            }

            using var document = HardenedJson.Parse(metadata);
            if (!document.RootElement.TryGetProperty("source", out var source)
                || source.ValueKind != System.Text.Json.JsonValueKind.String
                || string.IsNullOrWhiteSpace(source.GetString()))
            {
                sourceKey = null;
                return false;
            }

            sourceKey = GetSourceKey(source.GetString());
            return true;
        }
        catch (Exception ex) when (ex is
            ArgumentException
            or IOException
            or UnauthorizedAccessException
            or System.Text.Json.JsonException
            or InvalidOperationException
            or OverflowException)
        {
            sourceKey = null;
            return false;
        }
    }

    /// <summary>
    /// Gets the final path for a package in the transactional content cache.
    /// </summary>
    /// <remarks>
    /// The source is part of the path, not merely recorded inside the entry, so
    /// one coordinate served by two feeds occupies two slots. A single slot
    /// cannot hold both: the second feed would have to either overwrite content
    /// another feed is entitled to or fail to commit a package it downloaded
    /// successfully. The cost is duplicated bytes when two feeds carry the same
    /// package, which is cheaper than either alternative.
    /// </remarks>
    public static string GetPackageCachePath(string packageName, string version, string sourceKey)
    {
        ValidatePathComponent(packageName, "package name");
        ValidatePathComponent(version, "version");
        ValidatePathComponent(sourceKey, "source key");

        var appCachePath = GetPackageContentCachePath();
        return Path.Combine(
            appCachePath,
            packageName.ToLowerInvariant(),
            version.ToLowerInvariant(),
            sourceKey);
    }

    /// <summary>
    /// Validates and atomically publishes an extracted package to the app cache.
    /// Concurrent publishers either win the final rename or converge on the
    /// already committed winner.
    /// </summary>
    /// <param name="extractedPath">Path to the extracted package contents</param>
    /// <param name="nupkgPath">
    /// Source archive to retain with the committed contents. When null, a
    /// matching <c>.nupkg</c> is synthesized from the staged extract so
    /// product-owned admission (<c>RequiresArchiveTreeMatch</c>) can open it.
    /// </param>
    /// <param name="packageName">The package name</param>
    /// <param name="version">The package version</param>
    /// <param name="sourceKey">
    /// Identity (per <see cref="GetSourceKey"/>) of the source that served
    /// these bytes, recorded so a later read from a different source set does
    /// not receive them.
    /// </param>
    public static CommittedPackage CommitPackage(
        string extractedPath,
        string? nupkgPath,
        string packageName,
        string version,
        string sourceKey)
    {
        ValidatePathComponent(packageName, "package name");
        ValidatePathComponent(version, "version");

        string normalizedName = packageName.ToLowerInvariant();
        string normalizedVersion = version.ToLowerInvariant();
        string targetPath = GetPackageCachePath(
            normalizedName,
            normalizedVersion,
            sourceKey);
        string? parentDir = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException(
                $"Package cache path has no parent: {targetPath}");

        CoreCache.EnsurePathInCacheContext(targetPath);
        Directory.CreateDirectory(parentDir);

        if (IsCommittedPackageValid(
            targetPath,
            normalizedName,
            normalizedVersion,
            sourceKey))
        {
            return OpenCommittedPackage(
                targetPath,
                normalizedName,
                normalizedVersion,
                sourceKey);
        }

        if (Directory.Exists(targetPath))
        {
            // A concurrent winner may have published between the validity
            // check and Exists. Re-check before treating the slot as corrupt.
            if (IsCommittedPackageValid(
                targetPath,
                normalizedName,
                normalizedVersion,
                sourceKey))
            {
                return OpenCommittedPackage(
                    targetPath,
                    normalizedName,
                    normalizedVersion,
                    sourceKey);
            }

            throw new InvalidDataException(
                $"Package cache entry '{targetPath}' is incomplete or corrupt. Clear the cache before retrying.");
        }

        string stagingPath = Path.Combine(
            parentDir,
            $".{sourceKey}.tmp-{Guid.NewGuid():N}");
        CoreCache.EnsurePathInCacheContext(stagingPath);

        try
        {
            CopyDirectory(extractedPath, stagingPath);

            if (!IsCachedPackageValid(stagingPath))
            {
                throw new InvalidDataException(
                    $"Package '{packageName}@{version}' has no valid extracted package structure.");
            }

            // Product-owned admission requires a retained archive that matches
            // the extract (RequiresArchiveTreeMatch). Always publish one: copy
            // the caller's nupkg when provided, otherwise zip the staged extract
            // before the commit marker is written so the archive does not
            // contain the marker (marker + nupkg remain the only allowed extras).
            string committedNupkgPath = Path.Combine(
                stagingPath,
                $"{normalizedName}.{normalizedVersion}.nupkg");
            if (nupkgPath is not null)
            {
                File.Copy(nupkgPath, committedNupkgPath, overwrite: false);
            }
            else
            {
                string tempNupkg = Path.Combine(
                    parentDir,
                    $".{sourceKey}.nupkg-{Guid.NewGuid():N}");
                try
                {
                    ZipFile.CreateFromDirectory(
                        stagingPath,
                        tempNupkg,
                        CompressionLevel.NoCompression,
                        includeBaseDirectory: false);
                    File.Move(tempNupkg, committedNupkgPath);
                }
                finally
                {
                    if (File.Exists(tempNupkg))
                        File.Delete(tempNupkg);
                }
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
                        normalizedVersion,
                        sourceKey));
            }

            try
            {
                Directory.Move(stagingPath, targetPath);
            }
            catch (IOException) when (IsCommittedPackageValid(
                targetPath,
                normalizedName,
                normalizedVersion,
                sourceKey))
            {
                return OpenCommittedPackage(
                    targetPath,
                    normalizedName,
                    normalizedVersion,
                    sourceKey);
            }

            CacheTelemetry.Record(
                "packages",
                $"{normalizedName}@{normalizedVersion}",
                CacheAccessResult.Store);

            return new CommittedPackage(
                targetPath,
                Path.Combine(targetPath, Path.GetFileName(committedNupkgPath)),
                sourceKey);
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
    public static string? TryGetLatestCachedVersion(
        string packageName,
        IReadOnlyList<string>? allowedSourceKeys)
    {
        return GetCachedVersions(
            packageName,
            allowedSourceKeys,
            includePrerelease: false,
            limit: 1).FirstOrDefault();
    }

    /// <summary>
    /// Returns cached package versions, newest first, without touching the network.
    /// Entries are included only when their recorded producer is a source the
    /// caller is currently configured to read. These versions are diagnostic
    /// exact-pin suggestions; package payloads do not become discovery candidates.
    /// </summary>
    public static IReadOnlyList<string> GetCachedVersions(
        string packageName,
        IReadOnlyList<string>? allowedSourceKeys,
        bool includePrerelease = true,
        int? limit = null)
    {
        if (!IsValidPathComponent(packageName))
            return [];

        var normalizedName = packageName.ToLowerInvariant();
        var versions = new Dictionary<NuGetVersion, string>();

        void AddVersions(string root, Func<string, string, bool> isValid)
        {
            if (!Directory.Exists(root))
                return;

            try
            {
                foreach (var dir in Directory.GetDirectories(root))
                {
                    string version = Path.GetFileName(dir);
                    if (!NuGetVersion.TryParse(version, out var parsed)
                        || (!includePrerelease && parsed.IsPrerelease)
                        || !isValid(dir, version))
                    {
                        continue;
                    }

                    versions.TryAdd(parsed, version);
                }
            }
            catch (IOException)
            {
                // A cache that cannot be enumerated contributes no suggestions.
            }
            catch (UnauthorizedAccessException)
            {
                // A cache that cannot be enumerated contributes no suggestions.
            }
        }

        if (!_skipNuGetCache)
        {
            string globalPackagesPath = GetNuGetCachePath();
            AddVersions(
                Path.Combine(globalPackagesPath, normalizedName),
                (_, version) => TryGetGlobalPackageContent(
                    globalPackagesPath,
                    normalizedName,
                    version,
                    allowedSourceKeys) is not null);
        }

        try
        {
            AddVersions(
                Path.Combine(GetPackageContentCachePath(), normalizedName),
                (dir, version) =>
                {
                    foreach (var sourceKey in allowedSourceKeys ?? [])
                    {
                        if (IsCommittedPackageValid(
                                Path.Combine(dir, sourceKey),
                                normalizedName,
                                version,
                                sourceKey))
                        {
                            return true;
                        }
                    }

                    return false;
                });
        }
        catch (InvalidOperationException)
        {
            // App cache not initialized.
        }

        IEnumerable<string> ordered = versions
            .OrderByDescending(pair => pair.Key)
            .Select(pair => pair.Value);
        if (limit is { } count)
            ordered = ordered.Take(count);
        return ordered.ToArray();
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
        string version,
        string sourceKey) =>
        IsCachedPackageValid(cachedPath)
        && IsCommittedPackageSlotPresent(
            cachedPath,
            packageName,
            version,
            sourceKey);

    /// <summary>
    /// True when the app-cache slot exists and carries this source's commit
    /// marker, regardless of whether the extracted tree is still a usable
    /// package layout.
    /// </summary>
    private static bool IsCommittedPackageSlotPresent(
        string cachedPath,
        string packageName,
        string version,
        string sourceKey)
    {
        try
        {
            if (!Directory.Exists(cachedPath))
                return false;

            // The source is already selected by the path; the marker restates it
            // so an entry that was moved or hand-copied between slots is not
            // mistaken for one this source committed. Bound the read the same
            // way as global .nupkg.metadata: Length gate, fixed buffer, trailing
            // growth probe — never File.ReadAllText.
            string markerPath = Path.Combine(cachedPath, CommitMarkerFileName);
            var info = new FileInfo(markerPath);
            if (!info.Exists
                || info.Length <= 0
                || info.Length > MaxCommitMarkerBytes)
            {
                return false;
            }

            byte[] markerBytes = new byte[checked((int)info.Length)];
            using (FileStream stream = new(
                markerPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
            {
                int offset = 0;
                while (offset < markerBytes.Length)
                {
                    int read = stream.Read(markerBytes.AsSpan(offset));
                    if (read == 0)
                        return false;

                    offset += read;
                }

                if (stream.ReadByte() != -1)
                    return false;
            }

            string actual = Encoding.UTF8.GetString(markerBytes);
            return actual.Equals(
                GetCommitMarkerContent(packageName, version, sourceKey),
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
        string version,
        string sourceKey)
    {
        string nupkgPath = Path.Combine(
            targetPath,
            $"{packageName}.{version}.nupkg");
        return new CommittedPackage(
            targetPath,
            File.Exists(nupkgPath) ? nupkgPath : null,
            sourceKey);
    }

    private static string GetCommitMarkerContent(
        string packageName,
        string version,
        string sourceKey)
        => $"{PackageContentCategory}:{packageName}@{version}:{sourceKey}";

    /// <summary>
    /// Identity used when a source URL is absent or blank. It occupies its own
    /// cache slot like any other source and is only matched when a caller asks
    /// for it by name. Note that a local <c>.nupkg</c> path does not reach this
    /// slot: <c>PackageExtractor.ExtractLocalPackage</c> unpacks it to a
    /// temporary directory and never commits it to the content cache.
    /// </summary>
    private const string LocalSourceKey = "local";

    /// <summary>
    /// Derives a stable identity for a NuGet source from its URL, used as a
    /// path segment in the content cache.
    /// </summary>
    /// <remarks>
    /// The digest keeps source URLs out of cache paths and makes every identity
    /// a safe path segment regardless of the URL's characters. It is opacity,
    /// not confidentiality: a feed URL is low entropy, and anyone who can read
    /// the cache can already see which packages were fetched. Protecting feed
    /// identity from a local reader would require cache permissions, not a hash.
    ///
    /// HTTP canonicalization is delegated to
    /// <see cref="NuGetCredentialScope.CanonicalizeEndpoint"/>. Local
    /// canonicalization is delegated to
    /// <see cref="LocalPackageSourceIdentity"/>. Each source kind therefore has
    /// one identity across resolution, authorization, and cache provenance.
    /// </remarks>
    /// <param name="sourceUrl">
    /// The source URL, or an absolute local folder path.
    /// </param>
    /// <returns>A short hex digest identifying the source.</returns>
    public static string GetSourceKey(string? sourceUrl)
    {
        if (string.IsNullOrWhiteSpace(sourceUrl))
            return LocalSourceKey;

        var trimmed = sourceUrl.Trim();
        string normalized;

        if (LocalPackageSourceIdentity.IsLocalSource(trimmed))
        {
            normalized =
                LocalPackageSourceIdentity.CreateAbsolute(trimmed).PersistentValue;
        }
        else
        {
            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri))
                throw new ArgumentException("The package source is unusable.", nameof(sourceUrl));

            normalized = NuGetCredentialScope.CanonicalizeEndpoint(uri);
        }

        var digest = SHA256.HashData(s_utf8Strict.GetBytes(normalized));
        return Convert.ToHexStringLower(digest.AsSpan(0, 16));
    }

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

}
