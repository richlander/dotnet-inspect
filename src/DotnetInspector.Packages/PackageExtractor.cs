// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO.Compression;
using System.Net.Http.Headers;
using DotnetInspector.Core;
using ILInspector.MetadataPrimitives;
using NuGetFetch;
using NuGetSource = NuGetFetch.PackageSource;

namespace DotnetInspector.Packages;

/// <summary>
/// Result of a package extraction operation.
/// </summary>
/// <param name="ExtractPath">Path to the extracted package contents</param>
/// <param name="TempDir">Temporary directory to clean up (null if using cache)</param>
/// <param name="PackageName">Package name</param>
/// <param name="Version">Package version (may be null for local files)</param>
/// <param name="NupkgPath">Path to the .nupkg file for signature verification (null if not available)</param>
/// <param name="FromCache">Whether the package was served from the local cache</param>
public record PackageExtractionResult(
    string ExtractPath,
    string? TempDir,
    string? PackageName,
    string? Version,
    string? NupkgPath = null,
    bool FromCache = false);

/// <summary>
/// Outcome of a package extraction operation, carrying either a successful result or an error message.
/// </summary>
/// <param name="Result">The extraction result on success, or null on failure</param>
/// <param name="ErrorMessage">Error description on failure, or null on success</param>
public readonly record struct PackageExtractionOutcome(
    PackageExtractionResult? Result,
    string? ErrorMessage)
{
    public bool IsSuccess => Result is not null;
    public static implicit operator PackageExtractionOutcome(PackageExtractionResult result) => new(result, null);
    public static PackageExtractionOutcome Error(string message) => new(null, message);
}

public sealed record PackageReferenceTarget(
    string OriginalArgument,
    bool IsLocalFile,
    string PackageName,
    string Version);

/// <summary>
/// Shared utility for extracting NuGet packages from local files or NuGet feeds.
/// </summary>
public static class PackageExtractor
{
    private const int MaxToolWrapperRedirectHops = 8;

    private static readonly AsyncCache<PackageAcquisitionRequest, PackageExtractionOutcome>
        s_packageRequests = new();

    // PackageExtractor is the desktop acquisition path: its outputs are on-disk
    // extracted directories (IPackageContent.RootPath) that the CLI's existing
    // consumers open by path, so it is intentionally bound to the filesystem
    // store. A host-neutral consumer reuses IPackageStore/IPackageContent
    // directly rather than this extractor.
    private static readonly IPackageStore s_packageStore = new FileSystemPackageStore();

    /// <summary>
    /// Extracts a package from a local .nupkg file or downloads from NuGet sources.
    /// </summary>
    /// <param name="client">HTTP client for downloading packages</param>
    /// <param name="packageSource">Local .nupkg path or package reference (name or name@version)</param>
    /// <param name="log">Optional logging callback</param>
    /// <param name="tempDirPrefix">Prefix for temporary directory name (e.g., "inspect-api")</param>
    /// <param name="sourceOptions">NuGet source configuration (defaults to nuget.org)</param>
    /// <param name="version">Explicit version (overrides any version embedded in packageSource)</param>
    /// <param name="forceLatest">When true, always resolve version from network (bypass cache-first)</param>
    /// <param name="includePrerelease">When true, latest resolution includes prerelease/preview versions</param>
    /// <returns>Extraction outcome carrying result on success or error message on failure</returns>
    public static async Task<PackageExtractionOutcome> ExtractPackageAsync(
        HttpClient client,
        string packageSource,
        Action<string>? log = null,
        string tempDirPrefix = "inspect-pkg",
        NuGetSourceOptions? sourceOptions = null,
        string? version = null,
        bool forceLatest = false,
        bool includePrerelease = false)
    {
        bool isLocalFile = packageSource.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase);

        if (isLocalFile)
        {
            return ExtractLocalPackage(packageSource, log, tempDirPrefix);
        }

        // Keep redirect traversal outside exact-coordinate acquisition so one
        // package flight never waits on another package key.
        var visitedPackageIds = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        List<string> redirectChain = [];
        string currentPackageSource = packageSource;
        string? currentVersion = version;
        bool currentForceLatest = forceLatest;
        bool currentIncludePrerelease = includePrerelease;

        while (true)
        {
            var outcome = await DownloadAndExtractPackageAsync(
                client,
                currentPackageSource,
                log,
                tempDirPrefix,
                sourceOptions,
                currentVersion,
                currentForceLatest,
                currentIncludePrerelease).ConfigureAwait(false);
            if (!outcome.IsSuccess)
                return outcome;

            PackageExtractionResult result = outcome.Result!;
            string? redirectId =
                NuGetFetch.PackageExtractor.TryGetToolWrapperRedirect(
                    result.ExtractPath);
            if (redirectId is null)
                return result;

            if (string.IsNullOrWhiteSpace(result.PackageName))
            {
                return PackageExtractionOutcome.Error(
                    $"Tool wrapper at '{result.ExtractPath}' has no package identity.");
            }

            if (!visitedPackageIds.Add(result.PackageName))
            {
                return ToolWrapperRedirectCycle(
                    redirectChain,
                    result.PackageName);
            }

            redirectChain.Add(result.PackageName);
            if (visitedPackageIds.Contains(redirectId))
                return ToolWrapperRedirectCycle(redirectChain, redirectId);

            if (redirectChain.Count > MaxToolWrapperRedirectHops)
            {
                return PackageExtractionOutcome.Error(
                    $"Tool wrapper redirect limit of {MaxToolWrapperRedirectHops} exceeded: " +
                    $"{string.Join(" -> ", redirectChain)} -> {redirectId}.");
            }

            log?.Invoke(
                $"'{result.PackageName}' is a tool wrapper with no managed libraries; inspecting '{redirectId}' instead.");

            currentPackageSource = redirectId;
            currentVersion = result.Version;
            currentForceLatest = false;
            currentIncludePrerelease = false;
        }
    }

    private static PackageExtractionOutcome ToolWrapperRedirectCycle(
        IReadOnlyList<string> redirectChain,
        string repeatedPackageId)
        => PackageExtractionOutcome.Error(
            $"Tool wrapper redirect cycle detected: {string.Join(" -> ", redirectChain)} -> {repeatedPackageId}.");

    private static PackageExtractionOutcome ExtractLocalPackage(
        string packageSource,
        Action<string>? log,
        string tempDirPrefix)
    {
        if (!File.Exists(packageSource))
        {
            return PackageExtractionOutcome.Error($"File not found: {packageSource}");
        }

        string tempDir = Directory.CreateTempSubdirectory(tempDirPrefix).FullName;
        string extractPath = Path.Combine(tempDir, "extracted");

        log?.Invoke($"Extracting package: {Path.GetFileName(packageSource)}");
        ZipFile.ExtractToDirectory(packageSource, extractPath);

        var (pkgName, pkgVersion) = ParsePackageReference(packageSource);
        return new PackageExtractionResult(extractPath, tempDir, pkgName, pkgVersion, packageSource);
    }

    private static async Task<PackageExtractionOutcome> DownloadAndExtractPackageAsync(
        HttpClient client,
        string packageSource,
        Action<string>? log,
        string tempDirPrefix,
        NuGetSourceOptions? sourceOptions,
        string? explicitVersion = null,
        bool forceLatest = false,
        bool includePrerelease = false)
    {
        var (packageName, parsedVersion) = ParsePackageReference(packageSource);
        var version = explicitVersion ?? parsedVersion;

        // @latest is a special tag: resolve to newest version via network
        if (string.Equals(version, "latest", StringComparison.OrdinalIgnoreCase))
        {
            version = null;
            forceLatest = true;
        }

        // Resolve NuGet sources
        var sources = NuGetSourceResolver.ResolveSources(sourceOptions);

        // Resolve wildcard version patterns (e.g., 11.0.0-preview*)
        if (version != null && version.Contains('*'))
        {
            version = await ResolveVersionPatternAsync(client, packageName, version, sources, log).ConfigureAwait(false);
            if (version == null)
            {
                return PackageExtractionOutcome.Error($"No version matching pattern found for '{packageName}'.");
            }
        }

        // Get version if not specified
        if (version == null)
        {
            version = await GetLatestVersionAsync(client, packageName, sources, log, skipCache: forceLatest, includePrerelease: includePrerelease).ConfigureAwait(false);
            if (version == null)
            {
                if (HttpClientFactory.IsOffline)
                    return PackageExtractionOutcome.Error($"Package '{packageName}' is not available offline; no cached version was found.");

                return PackageExtractionOutcome.Error($"Package '{packageName}' not found.");
            }
        }

        // Normalize to lowercase for NuGet API
        string normalizedName = packageName.ToLowerInvariant();
        string normalizedVersion = version.ToLowerInvariant();

        // The coordinate becomes a cache directory name, so it has to be a safe path component.
        // NuGetCache validates it too, but by throwing, which leaves the command with an unhandled
        // ArgumentException and a stack trace. That throw used to be unreachable for an ordinary
        // name: the local copy of the rule this PR deleted could only reject separators, traversal
        // and volume qualifiers, all of which the argument parser rejects earlier. Tightening the
        // rule made it reachable -- `package CON@1.0.0` printed 1846 bytes of stack trace where it
        // previously printed `Error: Package 'con' not found.` -- so refuse it here as an ordinary
        // acquisition failure instead.
        if (!HardenedPath.IsSafePathComponent(normalizedName))
            return PackageExtractionOutcome.Error($"Invalid package name: '{packageName}'.");

        if (!HardenedPath.IsSafePathComponent(normalizedVersion))
            return PackageExtractionOutcome.Error($"Invalid package version: '{version}'.");

        var request = new PackageAcquisitionRequest(
            Path.GetFullPath(
                NuGetCache.GetPackageCachePath(
                    normalizedName,
                    normalizedVersion)));
        return await s_packageRequests.GetOrAddAsync(
            request,
            _ => AcquireResolvedPackageAsync(
                client,
                packageName,
                version,
                normalizedName,
                normalizedVersion,
                sources,
                sourceOptions,
                log,
                tempDirPrefix),
            // This is an in-flight registry. The committed filesystem entry is
            // authoritative and is revalidated by every later request.
            static _ => false).ConfigureAwait(false);
    }

    private static async Task<PackageExtractionOutcome> AcquireResolvedPackageAsync(
        HttpClient client,
        string packageName,
        string version,
        string normalizedName,
        string normalizedVersion,
        IReadOnlyList<NuGetSource> sources,
        NuGetSourceOptions? sourceOptions,
        Action<string>? log,
        string tempDirPrefix)
    {
        // Check NuGet cache first
        IPackageContent? cached = s_packageStore.TryGetCached(
            normalizedName,
            normalizedVersion,
            log);
        if (cached != null)
        {
            var cachedNupkg = cached.NupkgPath;
            return new PackageExtractionResult(cached.RootPath!, null, packageName, version, cachedNupkg, FromCache: true);
        }

        if (HttpClientFactory.IsOffline)
            return PackageExtractionOutcome.Error($"Package '{packageName}' version '{version}' is not available offline; no cached package was found.");

        string tempDir = Directory.CreateTempSubdirectory(tempDirPrefix).FullName;

        try
        {
            // Try each source in order, streaming the package straight to disk
            // without an in-memory archive buffer.
            string nupkgPath = Path.Combine(
                tempDir,
                $"{packageName}.{version}.nupkg");
            string? successfulSource = null;

            foreach (var source in sources)
            {
                var nupkgUrl = await GetPackageDownloadUrlAsync(
                    client,
                    source,
                    normalizedName,
                    normalizedVersion,
                    log).ConfigureAwait(false);
                if (nupkgUrl == null)
                    continue;

                log?.Invoke(
                    $"Downloading: {packageName} {version} from {source.Name}");

                try
                {
                    var ok = await HttpRetryHelper.DownloadToFileWithRetryAsync(
                        client,
                        nupkgUrl,
                        nupkgPath,
                        log: log,
                        auth: source.GetAuthHeader(),
                        trafficKind: NetworkTrafficKind.PackageDownload)
                        .ConfigureAwait(false);
                    if (ok)
                    {
                        successfulSource = source.Name;
                        break;
                    }
                }
                catch (HttpRequestException ex)
                {
                    log?.Invoke($"Source {source.Name} failed: {ex.Message}");
                }
            }

            if (successfulSource == null)
            {
                // Differentiate "package doesn't exist" from "version doesn't exist"
                var knownVersions = await GetVersionsAsync(
                    client,
                    packageName,
                    includePrerelease: true,
                    limit: null,
                    log: null,
                    sourceOptions: sourceOptions).ConfigureAwait(false);
                if (knownVersions == null || knownVersions.Count == 0)
                {
                    return PackageExtractionOutcome.Error(
                        $"Package '{packageName}' not found.");
                }

                return PackageExtractionOutcome.Error(
                    $"Version '{version}' of package '{packageName}' not found. Use --versions to see available versions.");
            }

            log?.Invoke($"Package downloaded successfully from {successfulSource}.");

            // Persist the downloaded nupkg through the package store. The
            // filesystem store extracts and transactionally commits it to the
            // cache; the stream is a plain FileStream, so nothing is buffered
            // in memory beyond the store's own copy loop.
            IPackageContent content;
            await using (var nupkgStream = File.OpenRead(nupkgPath))
            {
                content = await s_packageStore.CommitAsync(
                    packageName,
                    version,
                    nupkgStream).ConfigureAwait(false);
            }
            log?.Invoke($"Cached to: {content.RootPath}");

            return new PackageExtractionResult(
                content.RootPath!,
                TempDir: null,
                packageName,
                version,
                content.NupkgPath,
                FromCache: true);
        }
        catch (IOException ex)
        {
            return PackageExtractionOutcome.Error($"Failed to extract package '{packageName}@{version}': {ex.Message}");
        }
        catch (InvalidDataException ex)
        {
            return PackageExtractionOutcome.Error($"Failed to extract package '{packageName}@{version}': {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return PackageExtractionOutcome.Error($"Failed to extract package '{packageName}@{version}': {ex.Message}");
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch (IOException)
            {
                // The committed cache entry is independent of this temporary
                // download workspace.
            }
            catch (UnauthorizedAccessException)
            {
                // The committed cache entry is independent of this temporary
                // download workspace.
            }
        }
    }

    private readonly record struct PackageAcquisitionRequest(string CachePath);

    /// <summary>
    /// Gets the download URL for a package from a specific source.
    /// </summary>
    public static async Task<string?> GetPackageDownloadUrlAsync(
        HttpClient client,
        NuGetSource source,
        string packageName,
        string version,
        Action<string>? log)
    {
        // Check for well-known flat-container URL (nuget.org optimization)
        var flatContainerUrl = source.GetFlatContainerUrl();
        if (flatContainerUrl != null)
        {
            return $"{flatContainerUrl}/{packageName}/{version}/{packageName}.{version}.nupkg";
        }

        // Query V3 service index to discover PackageBaseAddress (flat-container) endpoint
        var baseAddress = await GetPackageBaseAddressAsync(client, source, log).ConfigureAwait(false);
        if (baseAddress != null)
        {
            // Ensure trailing slash
            if (!baseAddress.EndsWith('/'))
                baseAddress += "/";

            return $"{baseAddress}{packageName}/{version}/{packageName}.{version}.nupkg";
        }

        return null;
    }

    /// <summary>
    /// Builds the flat-container URL for a package's .nuspec ({base}/{id}/{version}/{id}.nuspec),
    /// or null if the source exposes no flat-container endpoint.
    /// </summary>
    private static async Task<string?> GetNuspecUrlAsync(
        HttpClient client,
        NuGetSource source,
        string packageName,
        string version,
        Action<string>? log)
    {
        var flatContainerUrl = source.GetFlatContainerUrl();
        if (flatContainerUrl != null)
            return $"{flatContainerUrl}/{packageName}/{version}/{packageName}.nuspec";

        var baseAddress = await GetPackageBaseAddressAsync(client, source, log).ConfigureAwait(false);
        if (baseAddress != null)
        {
            if (!baseAddress.EndsWith('/'))
                baseAddress += "/";
            return $"{baseAddress}{packageName}/{version}/{packageName}.nuspec";
        }

        return null;
    }

    /// <summary>
    /// Fetches just a package's .nuspec XML — from the extracted NuGet cache if present, otherwise
    /// downloading only the nuspec (not the whole .nupkg) from the flat-container endpoint. Used by
    /// transitive dependency resolution, which needs nothing but the dependency groups. Returns null
    /// if the nuspec could not be obtained from any source.
    /// </summary>
    public static async Task<string?> TryGetNuspecXmlAsync(
        HttpClient client,
        string packageId,
        string version,
        Action<string>? log = null,
        NuGetSourceOptions? sourceOptions = null)
    {
        string normalizedName = packageId.ToLowerInvariant();
        string normalizedVersion = version.ToLowerInvariant();

        // Cache hit: read the nuspec straight from the already-extracted package.
        var cachedPath = NuGetCache.TryGetCachedPackage(normalizedName, normalizedVersion);
        if (cachedPath != null && NuGetCache.IsCachedPackageValid(cachedPath))
        {
            var cachedNuspec = Directory
                .GetFiles(cachedPath, "*.nuspec", SearchOption.TopDirectoryOnly)
                .FirstOrDefault();
            if (cachedNuspec != null)
                return await File.ReadAllTextAsync(cachedNuspec).ConfigureAwait(false);
        }

        foreach (var source in NuGetSourceResolver.ResolveSources(sourceOptions))
        {
            var url = await GetNuspecUrlAsync(client, source, normalizedName, normalizedVersion, log).ConfigureAwait(false);
            if (url == null)
                continue;

            try
            {
                var xml = await HttpRetryHelper.GetStringWithRetryAsync(
                    client, url, log: log, auth: source.GetAuthHeader(),
                    trafficKind: NetworkTrafficKind.PackageManifest).ConfigureAwait(false);
                if (xml != null)
                    return xml;
            }
            catch (HttpRequestException ex)
            {
                log?.Invoke($"Nuspec fetch from {source.Name} failed: {ex.Message}");
            }
        }

        return null;
    }

    /// <summary>
    /// Returns true when <paramref name="source"/>'s URL is an absolute http/https URL.
    /// Local folder sources (e.g. `D:\packages`, `/var/packages`, `file://...`) and
    /// otherwise unparseable URLs return false — they cannot be queried by the
    /// remote-only operations in this class.
    /// </summary>
    private static bool IsHttpSource(NuGetSource source) =>
        Uri.TryCreate(source.Url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>
    /// Discovers the PackageBaseAddress (flat-container) endpoint from a V3 service index.
    /// </summary>
    private static async Task<string?> GetPackageBaseAddressAsync(
        HttpClient client,
        NuGetSource source,
        Action<string>? log)
    {
        // Skip non-HTTP sources (e.g. local folder feeds from NuGet.Config).
        // Passing a file: URL or raw filesystem path to HttpClient throws
        // NotSupportedException ("net_http_unsupported_requesturi_scheme, file"),
        // which would crash version resolution / package download. Issue #310.
        if (!IsHttpSource(source))
        {
            log?.Invoke($"Skipping non-HTTP NuGet source '{source.Name}': {source.Url}");
            return null;
        }

        // The source URL should be the V3 index.json
        var indexUrl = source.Url;
        if (!indexUrl.EndsWith("index.json", StringComparison.OrdinalIgnoreCase))
        {
            // Try appending /v3/index.json for common feed patterns
            if (indexUrl.EndsWith('/'))
                indexUrl += "v3/index.json";
            else
                indexUrl += "/v3/index.json";
        }

        log?.Invoke($"Querying service index: {indexUrl}");

        string? json = await HttpRetryHelper.GetStringWithRetryAsync(
            client, indexUrl, auth: source.GetAuthHeader(),
            trafficKind: NetworkTrafficKind.PackageSourceDiscovery).ConfigureAwait(false);
        if (json == null)
            return null;

        try
        {
            using var doc = HardenedJson.Parse(json);
            var resources = doc.RootElement.GetProperty("resources");

            foreach (var resource in resources.EnumerateArray())
            {
                var type = resource.GetProperty("@type").GetString();
                if (type != null && type.StartsWith("PackageBaseAddress", StringComparison.OrdinalIgnoreCase))
                {
                    return resource.GetProperty("@id").GetString();
                }
            }
        }
        catch
        {
            // Invalid service index
        }

        return null;
    }

    /// Parses a package reference string into name and optional version.
    /// Handles formats: "PackageName", "PackageName@1.0.0", "Package.Name.1.0.0.nupkg"
    /// </summary>
    public static (string name, string? version) ParsePackageReference(string packageSource)
    {
        // Handle local .nupkg files
        if (packageSource.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
        {
            string fileName = Path.GetFileNameWithoutExtension(packageSource);
            // Try to parse name.version pattern (e.g., "System.Text.Json.8.0.0")
            // Scan left-to-right: the first segment starting with a digit begins the version
            var parts = fileName.Split('.');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length > 0 && char.IsDigit(parts[i][0]))
                {
                    var name = string.Join(".", parts.Take(i));
                    var version = string.Join(".", parts.Skip(i));
                    return (name, version);
                }
            }
            return (fileName, null);
        }

        // Handle package@version format
        int atIndex = packageSource.IndexOf('@');
        if (atIndex > 0)
        {
            return (packageSource[..atIndex], packageSource[(atIndex + 1)..]);
        }

        return (packageSource, null);
    }

    public static PackageReferenceTarget ParsePackageTarget(string packageArg, string? explicitVersion = null)
    {
        bool isLocalFile = packageArg.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase);
        if (isLocalFile)
        {
            return new PackageReferenceTarget(
                packageArg,
                IsLocalFile: true,
                Path.GetFileNameWithoutExtension(packageArg),
                Version: "local");
        }

        var (name, parsedVersion) = ParsePackageReference(packageArg);
        string version = explicitVersion ?? parsedVersion ?? "";
        return new PackageReferenceTarget(
            packageArg,
            IsLocalFile: false,
            name.ToLowerInvariant(),
            version.ToLowerInvariant());
    }

    public static bool IsValidPackageReferenceVersion(string? version)
    {
        return string.IsNullOrEmpty(version)
            || string.Equals(version, "latest", StringComparison.OrdinalIgnoreCase)
            || version.Contains('*', StringComparison.Ordinal)
            || NuGet.Versioning.NuGetVersion.TryParse(version, out _);
    }

    private static readonly TimeSpan VersionCacheTtl = TimeSpan.FromHours(1);

    // Bumped to -v2 for #3388: the pre-fix tool populated this category from the UNFILTERED
    // flat-container, so entries written by an older build may include unlisted versions. A new
    // category name means those stale entries are never read after upgrading, so the listed-status
    // filter takes effect immediately rather than being delayed by up to the cache TTL.
    private const string VersionCacheCategory = "versions-v2";
    private const string VersionCacheCategoryPrefix = "versions-v";

    static PackageExtractor()
    {
        CoreCache.RegisterVersionedCategory(
            VersionCacheCategoryPrefix,
            VersionCacheCategory);
    }

    public static async Task<string?> GetLatestVersionAsync(
        HttpClient client,
        string packageName,
        List<NuGetSource> sources,
        Action<string>? log,
        bool skipCache = false,
        bool includePrerelease = false)
    {
        string normalizedName = packageName.ToLowerInvariant();
        string cacheKey = includePrerelease ? $"{normalizedName}-prerelease" : normalizedName;

        // Cache nuget.org results even when additional custom sources are configured.
        bool canCache = !skipCache && sources.Any(s => s.IsNuGetOrg);

        if (canCache)
        {
            string? cached;
            using (NetworkTelemetry.Scope(NetworkTrafficKind.PackageVersionList))
            {
                cached = CoreCache.TryGet(VersionCacheCategory, cacheKey, VersionCacheTtl, extension: "txt");
            }
            if (cached != null)
            {
                log?.Invoke($"Using cached version: {cached}");
                return cached;
            }
        }

        foreach (var source in sources)
        {
            var version = await GetLatestVersionFromSourceAsync(client, normalizedName, source, log, includePrerelease).ConfigureAwait(false);
            if (version != null)
            {
                if (canCache && source.IsNuGetOrg)
                {
                    using var cacheScope = NetworkTelemetry.Scope(NetworkTrafficKind.PackageVersionList);
                    CoreCache.Set(VersionCacheCategory, cacheKey, version, extension: "txt");
                }
                return version;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves a wildcard version pattern (e.g., "11.0.0-preview*") to the latest matching version.
    /// </summary>
    public static async Task<string?> ResolveVersionPatternAsync(
        HttpClient client,
        string packageName,
        string pattern,
        List<NuGetSource> sources,
        Action<string>? log)
    {
        string normalizedName = packageName.ToLowerInvariant();
        string prefix = pattern.Replace("*", "");

        log?.Invoke($"Resolving version pattern: {pattern}");

        NuGet.Versioning.NuGetVersion? best = null;
        string? bestOriginal = null;

        var versions = await GetAllVersionsWithCacheAsync(client, normalizedName, sources, log).ConfigureAwait(false);
        if (versions.Versions == null)
            return null;

        // Wildcard resolution auto-selects a single "latest matching" version. If the list is a
        // fail-open snapshot (nuget.org registration index unavailable) it may contain unlisted
        // versions, and we cannot tell which — so refuse to resolve rather than risk selecting an
        // unlisted version. Raw enumeration (GetVersionsAsync) intentionally still fails open.
        if (!versions.Authoritative)
        {
            log?.Invoke($"Could not resolve pattern '{pattern}': listing status unavailable");
            return null;
        }

        foreach (var ver in versions.Versions)
        {
            if (ver.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && NuGet.Versioning.NuGetVersion.TryParse(ver, out var parsed)
                && (best == null || parsed > best))
            {
                best = parsed;
                bestOriginal = ver;
            }
        }

        if (bestOriginal != null)
        {
            log?.Invoke($"Resolved pattern '{pattern}' to version: {bestOriginal}");
        }

        return bestOriginal;
    }

    /// <summary>
    /// Fetches all version strings for a package from a single source.
    /// </summary>
    private static async Task<List<string>?> FetchAllVersionsFromSourceAsync(
        HttpClient client,
        string packageName,
        NuGetSource source,
        Action<string>? log)
    {
        var auth = source.GetAuthHeader();

        // Try flat-container index first
        var flatContainerUrl = source.GetFlatContainerUrl();
        if (flatContainerUrl != null)
        {
            string indexUrl = $"{flatContainerUrl}/{packageName}/index.json";
            var versions = await FetchVersionListAsync(client, indexUrl, log, auth).ConfigureAwait(false);
            if (versions != null)
                return versions;
        }

        // Fall back to V3 service index discovery
        var baseAddress = await GetPackageBaseAddressAsync(client, source, log).ConfigureAwait(false);
        if (baseAddress != null)
        {
            if (!baseAddress.EndsWith('/'))
                baseAddress += "/";

            string indexUrl = $"{baseAddress}{packageName}/index.json";
            var versions = await FetchVersionListAsync(client, indexUrl, log, auth).ConfigureAwait(false);
            if (versions != null)
                return versions;
        }

        return null;
    }

    private static async Task<List<string>?> FetchVersionListAsync(
        HttpClient client, string indexUrl, Action<string>? log,
        AuthenticationHeaderValue? auth = null)
    {
        log?.Invoke($"Fetching versions from: {indexUrl}");
        string? json = await HttpRetryHelper.GetStringWithRetryAsync(
            client, indexUrl, auth: auth,
            trafficKind: NetworkTrafficKind.PackageVersionList).ConfigureAwait(false);
        if (json == null) return null;

        try
        {
            using var doc = HardenedJson.Parse(json);
            if (doc.RootElement.TryGetProperty("versions", out var versions))
            {
                return versions.EnumerateArray()
                    .Select(v => v.GetString())
                    .Where(v => v != null)
                    .Cast<string>()
                    .ToList();
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Ignore parse errors
        }

        return null;
    }

    // nuget.org registration index — the only nuget.org endpoint that carries the
    // per-version `catalogEntry.listed` flag. The flat-container index.json omits it.
    // The gz-semver2 hive is used (not semver1) because the semver1 hive omits SemVer2
    // versions entirely, which would let unlisted SemVer2 prereleases escape filtering.
    // Its responses are gzip-encoded and transparently decompressed by the shared
    // HttpClient (HttpClientFactory sets AutomaticDecompression = DecompressionMethods.All).
    private const string NuGetOrgRegistrationBase =
        "https://api.nuget.org/v3/registration5-gz-semver2";

    /// <summary>
    /// Fetches all version strings for a package from a single source, then removes NuGet
    /// <em>unlisted</em> versions so that discovery (enumeration and "latest" resolution) never
    /// surfaces a version hidden on nuget.org. Only nuget.org exposes a listed flag (via the
    /// registration index); other feeds are returned unfiltered. Explicit <c>Package@Version</c>
    /// access does not enumerate, so it still resolves and loads a known unlisted version.
    /// <para>
    /// <c>Authoritative</c> is <see langword="true"/> when the returned list reflects a definitive
    /// listing decision — a successful registration read (nuget.org) or a feed with no listed
    /// concept (other feeds). It is <see langword="false"/> only when nuget.org's registration index
    /// could not be read and the list is therefore a fail-open, <em>unfiltered</em> snapshot;
    /// callers must not persist such a snapshot, or a transient registration outage would re-surface
    /// unlisted versions for the whole cache TTL.
    /// </para>
    /// </summary>
    private static async Task<(List<string>? Versions, bool Authoritative)> FetchListedVersionsFromSourceAsync(
        HttpClient client,
        string packageName,
        NuGetSource source,
        Action<string>? log)
    {
        var versions = await FetchAllVersionsFromSourceAsync(client, packageName, source, log).ConfigureAwait(false);
        if (versions == null || !source.IsNuGetOrg)
            return (versions, Authoritative: true);

        var unlisted = await FetchUnlistedVersionsFromNuGetOrgAsync(client, packageName, log).ConfigureAwait(false);
        if (unlisted == null)
            return (versions, Authoritative: false);
        if (unlisted.Count == 0)
            return (versions, Authoritative: true);

        var filtered = versions.Where(v => !IsUnlisted(v, unlisted)).ToList();
        int removed = versions.Count - filtered.Count;
        if (removed > 0)
            log?.Invoke($"Excluded {removed} unlisted version(s) from enumeration");
        return (filtered, Authoritative: true);
    }

    private static bool IsUnlisted(string version, HashSet<NuGet.Versioning.NuGetVersion> unlisted) =>
        NuGet.Versioning.NuGetVersion.TryParse(version, out var parsed) && unlisted.Contains(parsed);

    /// <summary>
    /// Reads the nuget.org registration index and returns the set of versions whose
    /// <c>catalogEntry.listed</c> is explicitly <c>false</c>. A missing <c>listed</c> property is
    /// treated as listed (older catalog entries omit it). Returns <c>null</c> when the index cannot
    /// be fetched or parsed, so callers fail open (no filtering) rather than dropping real versions.
    /// </summary>
    private static async Task<HashSet<NuGet.Versioning.NuGetVersion>?> FetchUnlistedVersionsFromNuGetOrgAsync(
        HttpClient client,
        string packageName,
        Action<string>? log)
    {
        string indexUrl = $"{NuGetOrgRegistrationBase}/{packageName}/index.json";
        log?.Invoke($"Fetching listing status from: {indexUrl}");

        string? json = await HttpRetryHelper.GetStringWithRetryAsync(
            client, indexUrl,
            trafficKind: NetworkTrafficKind.PackageMetadata).ConfigureAwait(false);
        if (json == null)
            return null;

        var unlisted = new HashSet<NuGet.Versioning.NuGetVersion>();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("items", out var pages))
                return null;

            foreach (var page in pages.EnumerateArray())
            {
                // A page either inlines its entries under "items" or points at a URL that must be
                // fetched separately (registration pages are split for large version histories).
                if (page.TryGetProperty("items", out var inlineItems))
                {
                    CollectUnlisted(inlineItems, unlisted);
                }
                else if (page.TryGetProperty("@id", out var pageIdElement)
                    && pageIdElement.GetString() is string pageUrl)
                {
                    string? pageJson = await HttpRetryHelper.GetStringWithRetryAsync(
                        client, pageUrl,
                        trafficKind: NetworkTrafficKind.PackageMetadata).ConfigureAwait(false);
                    if (pageJson == null)
                        return null;
                    using var pageDoc = System.Text.Json.JsonDocument.Parse(pageJson);
                    if (pageDoc.RootElement.TryGetProperty("items", out var pageItems))
                        CollectUnlisted(pageItems, unlisted);
                }
            }
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
        {
            // Fail open on any malformed or unexpectedly shaped registration document
            // (JsonException = invalid JSON; InvalidOperationException = valid JSON whose
            // shape defies the accessors, e.g. `items` not an array or `version` not a string).
            log?.Invoke($"Could not parse listing status: {ex.Message}");
            return null;
        }

        return unlisted;
    }

    private static void CollectUnlisted(
        System.Text.Json.JsonElement items,
        HashSet<NuGet.Versioning.NuGetVersion> unlisted)
    {
        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("catalogEntry", out var entry))
                continue;
            if (!entry.TryGetProperty("listed", out var listedElement))
                continue; // absent -> listed by default (matches NuGet's own default)
            if (listedElement.ValueKind == System.Text.Json.JsonValueKind.True)
                continue; // explicitly listed
            if (listedElement.ValueKind != System.Text.Json.JsonValueKind.False)
            {
                // A present-but-non-boolean `listed` (e.g. the string "false") is a schema
                // violation. Rather than silently treating the entry as listed — which would let a
                // hostile or buggy feed smuggle an unlisted version through as "latest" — fail the
                // whole parse. The caller catches this and returns no filter (non-authoritative),
                // so enumeration fails open and auto-selecting callers fail closed, consistent with
                // how every other malformed registration shape is handled.
                throw new InvalidOperationException(
                    $"Unexpected 'listed' value kind '{listedElement.ValueKind}' in registration entry");
            }
            if (entry.TryGetProperty("version", out var versionElement)
                && versionElement.GetString() is string version
                && NuGet.Versioning.NuGetVersion.TryParse(version, out var parsed))
            {
                unlisted.Add(parsed);
            }
        }
    }

    /// <summary>
    /// Selects the newest version from a set of version strings. When
    /// <paramref name="includePrerelease"/> is false, prefers the newest stable version and only
    /// falls back to a prerelease when no stable version exists. Unparseable entries are ignored.
    /// </summary>
    private static string? PickLatest(IEnumerable<string?> versions, bool includePrerelease)
    {
        NuGet.Versioning.NuGetVersion? latestStable = null;
        NuGet.Versioning.NuGetVersion? latestAny = null;
        foreach (var ver in versions)
        {
            if (ver != null && NuGet.Versioning.NuGetVersion.TryParse(ver, out var parsed))
            {
                if (latestAny == null || parsed > latestAny)
                    latestAny = parsed;
                if (!parsed.IsPrerelease && (latestStable == null || parsed > latestStable))
                    latestStable = parsed;
            }
        }
        return (includePrerelease ? latestAny : latestStable ?? latestAny)?.OriginalVersion;
    }

    private static async Task<string?> GetLatestVersionFromSourceAsync(
        HttpClient client,
        string packageName,
        NuGetSource source,
        Action<string>? log,
        bool includePrerelease)
    {
        var auth = source.GetAuthHeader();

        // For nuget.org, use the search API — returns latest version directly without listing all versions
        if (source.IsNuGetOrg && !includePrerelease)
        {
            var version = await GetLatestVersionFromSearchAsync(client, packageName, log).ConfigureAwait(false);
            if (version != null)
                return version;
        }

        // For nuget.org, the flat-container/prerelease path must exclude unlisted versions.
        // FetchListedVersionsFromSourceAsync consults the registration index; only an
        // authoritative result (registration index read successfully) is trustworthy. If the
        // registration index is unavailable the method fails open to an UNFILTERED list flagged
        // Authoritative=false; picking a "latest" from that could surface an unlisted version, so
        // return null (couldn't determine a latest) instead of falling through to the unfiltered
        // index below.
        if (source.IsNuGetOrg)
        {
            var (listed, authoritative) = await FetchListedVersionsFromSourceAsync(client, packageName, source, log).ConfigureAwait(false);
            if (listed != null && authoritative)
                return PickLatest(listed, includePrerelease);
            return null;
        }

        // Fall back to flat-container index (enumerates all versions)
        var flatContainerUrl = source.GetFlatContainerUrl();
        if (flatContainerUrl != null)
        {
            string indexUrl = $"{flatContainerUrl}/{packageName}/index.json";
            log?.Invoke($"Fetching versions from: {indexUrl}");

            var version = await ParseVersionIndexAsync(client, indexUrl, auth, includePrerelease).ConfigureAwait(false);
            if (version != null)
                return version;
        }

        // Fall back to V3 service index discovery
        var baseAddress = await GetPackageBaseAddressAsync(client, source, log).ConfigureAwait(false);
        if (baseAddress != null)
        {
            if (!baseAddress.EndsWith('/'))
                baseAddress += "/";

            string indexUrl = $"{baseAddress}{packageName}/index.json";
            log?.Invoke($"Fetching versions from: {indexUrl}");

            var version = await ParseVersionIndexAsync(client, indexUrl, auth, includePrerelease).ConfigureAwait(false);
            if (version != null)
                return version;
        }

        return null;
    }

    private static async Task<string?> GetLatestVersionFromSearchAsync(
        HttpClient client,
        string packageName,
        Action<string>? log)
    {
        string searchUrl = $"https://azuresearch-usnc.nuget.org/query?q=packageid:{packageName}&take=1&prerelease=false";
        log?.Invoke($"Fetching latest version from: {searchUrl}");

        try
        {
            string? json = await HttpRetryHelper.GetStringWithRetryAsync(
                client, searchUrl,
                trafficKind: NetworkTrafficKind.PackageSearch).ConfigureAwait(false);
            if (json == null)
                return null;

            using var doc = HardenedJson.Parse(json);
            if (doc.RootElement.TryGetProperty("data", out var data) &&
                data.GetArrayLength() > 0)
            {
                var package = data[0];
                if (package.TryGetProperty("version", out var version))
                {
                    return version.GetString();
                }
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"Search API failed: {ex.Message}");
        }

        return null;
    }

    private static async Task<string?> ParseVersionIndexAsync(
        HttpClient client, string indexUrl,
        AuthenticationHeaderValue? auth = null,
        bool includePrerelease = false)
    {
        string? json = await HttpRetryHelper.GetStringWithRetryAsync(
            client, indexUrl, auth: auth,
            trafficKind: NetworkTrafficKind.PackageVersionList).ConfigureAwait(false);
        if (json == null)
            return null;

        try
        {
            using var doc = HardenedJson.Parse(json);
            var versions = doc.RootElement.GetProperty("versions");
            if (versions.GetArrayLength() > 0)
            {
                // Use NuGetVersion for proper comparison — feeds may return
                // versions in any order (nuget.org ascending, Azure DevOps descending).
                return PickLatest(
                    versions.EnumerateArray().Select(v => v.GetString()),
                    includePrerelease);
            }
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }

        return null;
    }

    /// <summary>
    /// Lists available versions of a package from NuGet, newest first.
    /// </summary>
    public static async Task<List<string>?> GetVersionsAsync(
        HttpClient client, string packageName, bool includePrerelease,
        int? limit, Action<string>? log,
        NuGetSourceOptions? sourceOptions = null)
    {
        string normalizedName = packageName.ToLowerInvariant();
        var sources = NuGetSourceResolver.ResolveSources(sourceOptions);

        var allVersions = await GetAllVersionsWithCacheAsync(client, normalizedName, sources, log).ConfigureAwait(false);
        if (allVersions.Versions == null)
            return null;

        // Raw enumeration fails open: showing the unfiltered list during a registration outage is
        // preferable to dropping real versions. (Auto-selecting callers fail closed instead.)
        var filtered = includePrerelease
            ? allVersions.Versions
            : allVersions.Versions.Where(v => !v.Contains('-')).ToList();

        // Newest first, with optional limit
        List<string> result = [];
        for (int i = filtered.Count - 1; i >= 0; i--)
        {
            result.Add(filtered[i]);
            if (limit.HasValue && result.Count >= limit.Value)
                break;
        }

        return result;
    }

    /// <summary>
    /// Lists available versions of a package annotated with their NuGet listing status, newest
    /// first. Unlike <see cref="GetVersionsAsync"/>, which hides unlisted versions, this preserves
    /// the <see cref="PackageVersionInfo.Listed"/> bit so a surface can <em>mark</em> unlisted
    /// versions. With <paramref name="includeUnlisted"/> false, the result is the same listed-only
    /// set as <see cref="GetVersionsAsync"/>.
    /// </summary>
    public static async Task<List<PackageVersionInfo>?> GetVersionListingsAsync(
        HttpClient client, string packageName, bool includePrerelease, bool includeUnlisted,
        int? limit, Action<string>? log,
        NuGetSourceOptions? sourceOptions = null)
    {
        string normalizedName = packageName.ToLowerInvariant();
        var sources = NuGetSourceResolver.ResolveSources(sourceOptions);

        var allListings = await GetAllVersionListingsWithCacheAsync(client, normalizedName, sources, log).ConfigureAwait(false);
        if (allListings == null)
            return null;

        IEnumerable<PackageVersionInfo> filtered = allListings;
        if (!includeUnlisted)
            filtered = filtered.Where(v => v.Listed);
        if (!includePrerelease)
            filtered = filtered.Where(v => !v.Version.Contains('-', StringComparison.Ordinal));

        // allListings is ascending (newest last); emit newest first with optional limit.
        var ordered = filtered.ToList();
        List<PackageVersionInfo> result = [];
        for (int i = ordered.Count - 1; i >= 0; i--)
        {
            result.Add(ordered[i]);
            if (limit.HasValue && result.Count >= limit.Value)
                break;
        }

        return result;
    }

    private static async Task<(List<string>? Versions, bool Authoritative)> GetAllVersionsWithCacheAsync(
        HttpClient client,
        string normalizedName,
        List<NuGetSource> sources,
        Action<string>? log)
    {
        // Cache only nuget.org's own version list (keyed by package name); custom/private
        // sources must always be queried so a pattern can resolve to a version that exists
        // only on a secondary feed.
        bool canCacheNuGetOrg = sources.Any(s => s.IsNuGetOrg);
        var merged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool anyFound = false;

        // The merged list is authoritative unless a nuget.org source failed open (registration
        // index unavailable), in which case it may contain unlisted versions. Callers that
        // auto-select a single version (e.g. wildcard resolution) must treat a non-authoritative
        // list as "cannot determine safely"; raw enumeration may still fail open.
        bool authoritative = true;

        foreach (var source in sources)
        {
            List<string>? versions = null;
            bool fetchedAuthoritative = false;
            bool fromCache = false;

            if (source.IsNuGetOrg && canCacheNuGetOrg)
            {
                var cached = CoreCache.TryGet(VersionCacheCategory, $"{normalizedName}-all", VersionCacheTtl, extension: "txt");
                if (cached != null)
                {
                    log?.Invoke("Using cached version list");
                    versions = [.. cached.Split('\n', StringSplitOptions.RemoveEmptyEntries)];
                    fromCache = true;
                }
            }

            if (versions == null)
                (versions, fetchedAuthoritative) = await FetchListedVersionsFromSourceAsync(client, normalizedName, source, log).ConfigureAwait(false);
            if (versions == null)
                continue;

            // A cache hit is authoritative (only an authoritatively filtered list is ever
            // persisted). A fresh fetch reports authoritativeness itself (false only on a
            // nuget.org registration fail-open).
            if (!fromCache && !fetchedAuthoritative)
                authoritative = false;

            anyFound = true;
            merged.UnionWith(versions);

            // Persist nuget.org's list (not the merged set) so private-feed versions don't
            // pollute the shared, name-keyed cache. Only cache a freshly fetched, authoritatively
            // filtered list: a fail-open snapshot taken while the registration index was
            // unavailable is unfiltered, so caching it would re-surface unlisted versions for the
            // whole TTL. A list served from cache is already persisted and need not be rewritten.
            if (source.IsNuGetOrg && canCacheNuGetOrg && fetchedAuthoritative)
                CoreCache.Set(VersionCacheCategory, $"{normalizedName}-all", string.Join('\n', versions), extension: "txt");
        }

        if (!anyFound)
            return (null, authoritative);

        // Sort ascending by SemVer (newest last) so callers that assume ordered input
        // (e.g. GetVersionsAsync) stay correct across merged feeds; unparseable entries sort last.
        var parseable = new List<(NuGet.Versioning.NuGetVersion Parsed, string Original)>();
        var unparseable = new List<string>();
        foreach (var v in merged)
        {
            if (NuGet.Versioning.NuGetVersion.TryParse(v, out var parsed))
                parseable.Add((parsed, v));
            else
                unparseable.Add(v);
        }

        parseable.Sort((a, b) => a.Parsed.CompareTo(b.Parsed));
        return ([.. parseable.Select(p => p.Original), .. unparseable], authoritative);
    }

    private const string ListingsCacheSuffix = "-listings";

    /// <summary>
    /// Produces the full annotated version list (listed and unlisted) across sources, ascending by
    /// SemVer. Mirrors <see cref="GetAllVersionsWithCacheAsync"/> but carries the listing bit. Only
    /// nuget.org's own annotated list is cached (keyed by package name); private feeds are always
    /// re-queried. A version listed on any source is reported as listed.
    /// </summary>
    private static async Task<List<PackageVersionInfo>?> GetAllVersionListingsWithCacheAsync(
        HttpClient client,
        string normalizedName,
        List<NuGetSource> sources,
        Action<string>? log)
    {
        bool canCacheNuGetOrg = sources.Any(s => s.IsNuGetOrg);
        var merged = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        bool anyFound = false;

        foreach (var source in sources)
        {
            List<PackageVersionInfo>? listings = null;
            bool fetchedAuthoritative = false;

            if (source.IsNuGetOrg && canCacheNuGetOrg)
            {
                var cached = CoreCache.TryGet(VersionCacheCategory, $"{normalizedName}{ListingsCacheSuffix}", VersionCacheTtl, extension: "txt");
                if (cached != null)
                {
                    log?.Invoke("Using cached version listings");
                    listings = DeserializeListings(cached);
                }
            }

            if (listings == null)
                (listings, fetchedAuthoritative) = await FetchVersionListingsFromSourceAsync(client, normalizedName, source, log).ConfigureAwait(false);
            if (listings == null)
                continue;

            anyFound = true;
            foreach (var listing in listings)
            {
                // A version listed on any source counts as listed.
                merged[listing.Version] = merged.TryGetValue(listing.Version, out var existing)
                    ? existing || listing.Listed
                    : listing.Listed;
            }

            // Only cache a freshly fetched, authoritatively annotated list: a fail-open snapshot
            // taken while the registration index was unavailable marks every version listed, so
            // caching it would hide real unlisted versions for the whole TTL. A list served from
            // cache is already persisted and need not be rewritten.
            if (source.IsNuGetOrg && canCacheNuGetOrg && fetchedAuthoritative)
                CoreCache.Set(VersionCacheCategory, $"{normalizedName}{ListingsCacheSuffix}", SerializeListings(listings), extension: "txt");
        }

        if (!anyFound)
            return null;

        // Sort ascending by SemVer (newest last); unparseable entries sort last.
        var parseable = new List<(NuGet.Versioning.NuGetVersion Parsed, PackageVersionInfo Info)>();
        var unparseable = new List<PackageVersionInfo>();
        foreach (var (version, listed) in merged)
        {
            var info = new PackageVersionInfo(version, listed);
            if (NuGet.Versioning.NuGetVersion.TryParse(version, out var parsed))
                parseable.Add((parsed, info));
            else
                unparseable.Add(info);
        }

        parseable.Sort((a, b) => a.Parsed.CompareTo(b.Parsed));
        return [.. parseable.Select(p => p.Info), .. unparseable];
    }

    /// <summary>
    /// Fetches all versions from a single source and annotates each with its listing status.
    /// nuget.org's status comes from the registration index; other feeds have no listed concept and
    /// are reported as listed. When the registration index is unavailable, versions fail open to
    /// listed rather than being dropped or mislabeled unlisted.
    /// <para>
    /// <c>Authoritative</c> is <see langword="false"/> only when the nuget.org registration index
    /// could not be read, so the annotations are a fail-open (all-listed) snapshot that a caller
    /// must not cache — otherwise a transient outage would mark real unlisted versions as listed for
    /// the whole cache TTL.
    /// </para>
    /// </summary>
    private static async Task<(List<PackageVersionInfo>? Listings, bool Authoritative)> FetchVersionListingsFromSourceAsync(
        HttpClient client,
        string packageName,
        NuGetSource source,
        Action<string>? log)
    {
        var versions = await FetchAllVersionsFromSourceAsync(client, packageName, source, log).ConfigureAwait(false);
        if (versions == null)
            return (null, Authoritative: true);

        HashSet<NuGet.Versioning.NuGetVersion>? unlisted = source.IsNuGetOrg
            ? await FetchUnlistedVersionsFromNuGetOrgAsync(client, packageName, log).ConfigureAwait(false)
            : null;

        bool authoritative = !source.IsNuGetOrg || unlisted != null;
        var listings = versions
            .Select(v => new PackageVersionInfo(v, unlisted == null || !IsUnlisted(v, unlisted)))
            .ToList();
        return (listings, authoritative);
    }

    // Cache line format: "<version>\tL" for listed, "<version>\tU" for unlisted. Both statuses
    // carry an explicit two-char tab suffix so the encoding is unambiguous for ANY version text:
    // decoding always strips exactly two trailing chars and reads the flag from the first, which
    // round-trips even a (SemVer-impossible) version that itself ends in "\tL"/"\tU". Legacy caches
    // wrote bare "<version>" for listed; a line with no recognized suffix is decoded as listed for
    // backward compatibility.
    private static string SerializeListings(IEnumerable<PackageVersionInfo> listings) =>
        string.Join('\n', listings.Select(l => l.Listed ? $"{l.Version}\tL" : $"{l.Version}\tU"));

    private static List<PackageVersionInfo> DeserializeListings(string cached)
    {
        List<PackageVersionInfo> result = [];
        foreach (var line in cached.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.EndsWith("\tU", StringComparison.Ordinal))
                result.Add(new PackageVersionInfo(line[..^2], Listed: false));
            else if (line.EndsWith("\tL", StringComparison.Ordinal))
                result.Add(new PackageVersionInfo(line[..^2], Listed: true));
            else
                result.Add(new PackageVersionInfo(line, Listed: true)); // legacy bare-listed line
        }
        return result;
    }

    /// <summary>
    /// Extracts version from a cached package path.
    /// Path format: .../packages/packagename/version/lib/tfm/assembly.dll
    /// </summary>
    public static string? ExtractVersionFromPath(string dllPath, string packageName)
    {
        var normalizedPath = dllPath.Replace('\\', '/');
        var normalizedPackageName = packageName.ToLowerInvariant();

        var searchPattern = $"/{normalizedPackageName}/";
        var index = normalizedPath.ToLowerInvariant().IndexOf(searchPattern, StringComparison.Ordinal);
        if (index < 0)
            return null;

        var afterPackage = normalizedPath[(index + searchPattern.Length)..];
        var nextSlash = afterPackage.IndexOf('/');
        if (nextSlash > 0)
        {
            var possibleVersion = afterPackage[..nextSlash];
            if (possibleVersion.Length > 0 && char.IsDigit(possibleVersion[0]))
            {
                return possibleVersion;
            }
        }

        return null;
    }

    /// <summary>
    /// Cleans up temporary directory if it exists.
    /// </summary>
    public static void Cleanup(string? tempDir)
    {
        if (tempDir != null)
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }
}
