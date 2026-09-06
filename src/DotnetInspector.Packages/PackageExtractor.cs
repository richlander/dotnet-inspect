// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using DotnetInspector.Core;
using InertText;
using NuGetFetch;
using NuGet.Versioning;
using NuGetSource = NuGetFetch.PackageSource;

namespace DotnetInspector.Packages;

/// <summary>
/// Result of a package extraction operation.
/// </summary>
/// <param name="ExtractPath">Path to the extracted package contents</param>
/// <param name="TempDir">Owned temporary directory to clean up, including wrapper storage when the final payload is cached; null if none</param>
/// <param name="PackageName">Package name</param>
/// <param name="Version">Package version (may be null for local files)</param>
/// <param name="NupkgPath">Path to the .nupkg file for signature verification (null if not available)</param>
/// <param name="FromCache">Whether the package was served from the local cache</param>
/// <param name="ProducerKey">Canonical identity of the source that produced the package payload</param>
public record PackageExtractionResult(
    string ExtractPath,
    string? TempDir,
    string? PackageName,
    string? Version,
    string? NupkgPath = null,
    bool FromCache = false,
    string? ProducerKey = null)
{
    /// <summary>
    /// Sources that reported a version selected from a floating or wildcard coordinate.
    /// </summary>
    public IReadOnlyList<string>? SelectedVersionSourceUrls { get; init; }

    /// <summary>
    /// Whether the ambient package-specific source policy already names only the selected
    /// version's reporting sources.
    /// </summary>
    public bool SelectedVersionUsesOriginalSources { get; init; }
    public ConfiguredPackageAuthority? Authority { get; init; }

    public string? CacheScopeKey => Authority is null ? ProducerKey : Authority.PersistentCacheKey;

    /// <summary>
    /// Tool wrapper packages traversed before reaching this inspectable payload,
    /// ordered from the requested package to the final redirect hop.
    /// </summary>
    public IReadOnlyList<ToolWrapperPackage> ToolWrapperChain { get; init; } = [];
}

/// <summary>
/// A tool wrapper package whose managed inspection payload lives in another package.
/// </summary>
/// <param name="ExtractPath">Path to the extracted wrapper contents.</param>
/// <param name="PackageName">Wrapper package identity.</param>
/// <param name="Version">Wrapper package version.</param>
/// <param name="ProducerKey">Canonical identity of the source that produced the wrapper.</param>
public sealed record ToolWrapperPackage(
    string ExtractPath,
    string PackageName,
    string? Version,
    string? ProducerKey)
{
    public ConfiguredPackageAuthority? Authority { get; init; }
}

public enum NuspecProbeStatus
{
    Present,
    Absent,
    Indeterminate
}

public readonly record struct NuspecProbeResult(
    string? Xml,
    NuspecProbeStatus Status);

/// <summary>
/// Bounds aggregate compressed archive bytes across related local probes.
/// </summary>
public sealed class PackageArchiveReadBudget
{
    private long _remaining;

    public PackageArchiveReadBudget(long maxBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        _remaining = maxBytes;
    }

    internal bool TryReserve(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        while (true)
        {
            long remaining = Volatile.Read(ref _remaining);
            if (bytes > remaining)
                return false;

            if (Interlocked.CompareExchange(
                    ref _remaining,
                    remaining - bytes,
                    remaining)
                == remaining)
            {
                return true;
            }
        }
    }
}

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
/// A selected package version and the sources that reported that exact
/// candidate.
/// </summary>
internal sealed record PackageVersionResolution(
    string Version,
    IReadOnlyList<NuGetSource> ReportingSources,
    bool IsComplete = true);

/// <summary>
/// Shared utility for extracting NuGet packages from local files or NuGet feeds.
/// </summary>
public static class PackageExtractor
{
    private const int MaxEquivalentSearchEndpoints = 4;
    private const int MaxToolWrapperRedirectHops = 8;
    private static readonly UTF8Encoding StrictUtf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static bool IsValidPackageId(string packageId)
        => PackageCoordinateResolver.IsCanonicalPackageId(packageId);

    public static bool TryNormalizePackageVersion(
        string? version,
        out string normalizedVersion)
    {
        if (NuGetVersion.TryParse(version, out NuGetVersion? parsed))
        {
            normalizedVersion = parsed.ToNormalizedString();
            return true;
        }

        normalizedVersion = "";
        return false;
    }

    /// <summary>
    /// Hard cap on package <c>.nuspec</c> bodies (cache file or remote manifest
    /// GET). Real nuspecs are small XML; an unbounded string read would let a
    /// hostile app-cache slot or feed OOM dependency-resolution hot paths.
    /// </summary>
    internal const int MaxNuspecBytes = 1 * 1024 * 1024;

    internal static TimeSpan CachedVersionResolutionTimeout { get; } =
        TimeSpan.FromSeconds(1);

    private static readonly AsyncCache<PackageAcquisitionRequest, PackageExtractionOutcome>
        s_packageRequests = new();
    private static readonly ConditionalWeakTable<HttpClient, HttpClientIdentity>
        s_httpClientIdentities = new();
    private static readonly byte[] s_transportScopeKey =
        RandomNumberGenerator.GetBytes(32);
    private static long s_nextHttpClientIdentity;

    // PackageExtractor is the desktop acquisition path: its outputs are on-disk
    // extracted directories (IPackageContent.RootPath) that the CLI's existing
    // consumers open by path, so it is intentionally bound to the filesystem
    // store. A host-neutral consumer reuses IPackageStore/IPackageContent
    // directly rather than this extractor.
    private static readonly IPackageStore s_packageStore = new FileSystemPackageStore();

    /// <summary>
    /// Selects the first exact cached package that the current source policy
    /// authorizes and the normal payload admission contract accepts.
    /// </summary>
    public static string? TryGetAdmittedCachedPackagePath(
        string packageName,
        string version,
        NuGetSourceOptions? sourceOptions,
        IReadOnlyList<string>? globalPackageRoots = null)
    {
        if (!IsValidPackageId(packageName)
            || !TryNormalizePackageVersion(
                version,
                out string normalizedVersion))
        {
            return null;
        }

        string normalizedName = packageName.ToLowerInvariant();
        normalizedVersion = normalizedVersion.ToLowerInvariant();
        PackageSourceAuthorization authorization =
            new SourcePolicyPackageSourceAuthorization(sourceOptions)
                .AuthorizeSourcesFor(normalizedName);
        if (authorization.Sources.Count == 0)
            return null;

        string[] sourceKeys =
        [
            .. authorization.Sources.Select(
                source => NuGetCache.GetSourceKey(source.Url)),
        ];
        foreach (CachedPackage cached in NuGetCache.EnumerateCachedPackageContent(
                     normalizedName,
                     normalizedVersion,
                     sourceKeys,
                     globalPackagesPaths: globalPackageRoots))
        {
            string expectedNupkg = Path.Combine(
                cached.ExtractPath,
                $"{normalizedName}.{normalizedVersion}.nupkg");
            var content = new FileSystemPackageContent(
                cached.ExtractPath,
                File.Exists(expectedNupkg) ? expectedNupkg : null,
                fromCache: true,
                cached.ProducerKey,
                cached.RequiresArchiveTreeMatch);
            if (PackageContentAdmission.EvaluateFileSystem(
                    content,
                    PackagePayloadLimits.Default,
                    CancellationToken.None)
                == PackageContentAdmission.Outcome.Admissible)
            {
                return cached.ExtractPath;
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts a package from a local .nupkg file or downloads from NuGet sources.
    /// </summary>
    /// <param name="client">HTTP client for downloading packages</param>
    /// <param name="packageSource">Local .nupkg path or package reference (name or name@version)</param>
    /// <param name="log">Optional logging callback</param>
    /// <param name="tempDirPrefix">Prefix for temporary directory name (e.g., "inspect-api")</param>
    /// <param name="sourceOptions">NuGet source configuration (defaults to nuget.org)</param>
    /// <param name="version">Explicit version (overrides any version embedded in packageSource)</param>
    /// <param name="forceLatest">When true, always resolve version from network (bypass candidate metadata caches)</param>
    /// <param name="includePrerelease">When true, latest resolution includes prerelease/preview versions</param>
    /// <returns>Extraction outcome carrying result on success or error message on failure</returns>
    public static Task<PackageExtractionOutcome> ExtractPackageAsync(
        HttpClient client,
        string packageSource,
        Action<string>? log = null,
        string tempDirPrefix = "inspect-pkg",
        NuGetSourceOptions? sourceOptions = null,
        string? version = null,
        bool forceLatest = false,
        bool includePrerelease = false) =>
        ExtractPackageCoreAsync(
            client, packageSource, log, tempDirPrefix, sourceOptions,
            version, forceLatest, includePrerelease, authoritySession: null);

    /// <summary>Extracts an online caller-pinned package through configured authorities.</summary>
    public static async Task<PackageExtractionOutcome> ExtractPinnedPackageAsync(
        HttpClient client,
        string packageId,
        string version,
        Action<string>? log = null,
        string tempDirPrefix = "inspect-pkg",
        NuGetSourceOptions? sourceOptions = null,
        Func<DesktopPackageSourceComposition>? createComposition = null)
    {
        if (HttpClientFactory.IsOffline
            || !IsValidPackageId(packageId)
            || !TryNormalizePackageVersion(version, out string normalizedVersion))
        {
            return PackageExtractionOutcome.Error(
                "Configured-authority extraction requires online mode, a valid package ID, and an exact version.");
        }
        await using var session = new ConfiguredPackageExtractionSession(
            client.Timeout, tempDirPrefix, createComposition);
        return await ExtractPackageCoreAsync(
            client, packageId, log, tempDirPrefix, sourceOptions,
            normalizedVersion, forceLatest: false, includePrerelease: false, session)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Selects and extracts an online package using current configured-authority
    /// evidence. Range selectors require an explicit address.
    /// </summary>
    public static async Task<PackageExtractionOutcome> ExtractSelectedPackageAsync(
        HttpClient client,
        string packageId,
        string? versionSelector = null,
        Action<string>? log = null,
        string tempDirPrefix = "inspect-pkg",
        NuGetSourceOptions? sourceOptions = null,
        bool includePrerelease = false,
        string? rangeAddress = null,
        Func<DesktopPackageSourceComposition>? createComposition = null)
    {
        if (HttpClientFactory.IsOffline || !IsValidPackageId(packageId))
        {
            return PackageExtractionOutcome.Error(
                "Configured-authority selection requires online mode and a valid package ID.");
        }

        await using var session = new ConfiguredPackageExtractionSession(
            client.Timeout, tempDirPrefix, createComposition);
        PackageExtractionOutcome selected;
        using (FeedFailureTelemetry.Scope())
        {
            selected = await session.AcquireSelectedAsync(
                packageId, versionSelector, sourceOptions, log,
                includePrerelease, rangeAddress).ConfigureAwait(false);
        }
        if (!selected.IsSuccess)
            return selected;

        return await ExtractPackageCoreAsync(
            client, packageId, log, tempDirPrefix, sourceOptions,
            selected.Result!.Version, forceLatest: false, includePrerelease: false,
            session, selected).ConfigureAwait(false);
    }

    private static async Task<PackageExtractionOutcome> ExtractPackageCoreAsync(
        HttpClient client,
        string packageSource,
        Action<string>? log,
        string tempDirPrefix,
        NuGetSourceOptions? sourceOptions,
        string? version,
        bool forceLatest,
        bool includePrerelease,
        ConfiguredPackageExtractionSession? authoritySession,
        PackageExtractionOutcome? initialOutcome = null)
    {
        bool isLocalFile = authoritySession is null
            && packageSource.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase);

        if (isLocalFile)
        {
            return ExtractLocalPackage(packageSource, log, tempDirPrefix);
        }

        // Keep redirect traversal outside exact-coordinate acquisition so one
        // package flight never waits on another package key.
        var visitedPackageIds = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        List<string> redirectChain = [];
        List<ToolWrapperPackage> wrapperPackages = [];
        string currentPackageSource = packageSource;
        string? currentVersion = version;
        bool currentForceLatest = forceLatest;
        bool currentIncludePrerelease = includePrerelease;
        NuGetSourceOptions? currentSourceOptions = sourceOptions;

        while (true)
        {
            // Scoped per hop, not per acquisition: each hop resolves a different package id,
            // and a source failure recorded while resolving one package must not be offered
            // as the explanation for the next one going missing.
            PackageExtractionOutcome outcome;
            using (FeedFailureTelemetry.Scope())
            {
                outcome = initialOutcome ?? await DownloadAndExtractPackageAsync(
                    client,
                    currentPackageSource,
                    log,
                    tempDirPrefix,
                    currentSourceOptions,
                    currentVersion,
                    currentForceLatest,
                    currentIncludePrerelease,
                    authoritySession).ConfigureAwait(false);
                initialOutcome = null;
            }

            if (!outcome.IsSuccess)
                return outcome;

            PackageExtractionResult result = outcome.Result!;
            string? redirectId =
                NuGetFetch.PackageExtractor.TryGetToolWrapperRedirect(
                    result.ExtractPath);
            if (redirectId is null)
            {
                PackageExtractionResult completed = wrapperPackages.Count == 0
                    ? result
                    : result with
                    {
                        ToolWrapperChain = wrapperPackages.ToArray()
                    };
                return authoritySession?.Complete(completed) ?? completed;
            }

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
            wrapperPackages.Add(new ToolWrapperPackage(
                result.ExtractPath,
                result.PackageName,
                result.Version,
                result.ProducerKey)
            {
                Authority = result.Authority,
            });
            if (!IsValidPackageId(redirectId))
            {
                return PackageExtractionOutcome.Error(
                    $"Tool wrapper package '{result.PackageName}' declares an invalid redirect package id.");
            }

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
            currentSourceOptions =
                NuGetSourceResolver.WithoutSourceRestriction(sourceOptions);
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
        return new PackageExtractionResult(
            extractPath,
            tempDir,
            pkgName,
            pkgVersion,
            packageSource,
            ProducerKey: "explicit-local-input");
    }

    private static async Task<PackageExtractionOutcome> DownloadAndExtractPackageAsync(
        HttpClient client,
        string packageSource,
        Action<string>? log,
        string tempDirPrefix,
        NuGetSourceOptions? sourceOptions,
        string? explicitVersion = null,
        bool forceLatest = false,
        bool includePrerelease = false,
        ConfiguredPackageExtractionSession? authoritySession = null)
    {
        (string packageName, string? parsedVersion) = authoritySession is null
            ? ParsePackageReference(packageSource)
            : (packageSource, null);
        var version = explicitVersion ?? parsedVersion;

        // A legacy selector's producer restriction is not an authority receipt.
        // Those discovered-coordinate paths migrate with their resolver.
        if (!HttpClientFactory.IsOffline
            && authoritySession is not null
            && sourceOptions?.AuthorizedSourceKeys is null
            && sourceOptions?.ResolvedSources is null
            && version is not null
            && TryNormalizePackageVersion(version, out string pinnedVersion))
        {
            return await authoritySession.AcquireAsync(
                packageName, pinnedVersion, sourceOptions, log).ConfigureAwait(false);
        }

        // @latest is a special tag: resolve to newest version via network
        if (string.Equals(version, "latest", StringComparison.OrdinalIgnoreCase))
        {
            version = null;
            forceLatest = true;
        }

        // Resolve NuGet sources
        var sources = NuGetSourceResolver.ResolveSourcesForPackage(
            sourceOptions,
            packageName);
        IReadOnlyList<NuGetSource> authorizedSources =
            NuGetSourceResolver.ResolveAuthorizedSources(
                sourceOptions,
                sources);
        IReadOnlyList<string> originalAuthorizedSourceKeys =
            NuGetSourceResolver.SourceKeys(authorizedSources);
        IReadOnlyList<string>? selectedVersionSourceUrls = null;
        bool selectedVersionUsesOriginalSources = false;
        IReadOnlyList<string> cachedVersions = version == null
            ? NuGetCache.GetCachedVersions(
                packageName,
                NuGetSourceResolver.SourceKeys(authorizedSources),
                includePrerelease)
            : [];

        // Resolve wildcard version patterns (e.g., 11.0.0-preview*)
        if (version != null && version.Contains('*'))
        {
            PackageVersionResolution? resolution =
                await ResolveVersionPatternWithSourcesAsync(
                    client,
                    packageName,
                    version,
                    sources,
                    log).ConfigureAwait(false);
            if (resolution is null)
            {
                return PackageExtractionOutcome.Error($"No version matching pattern found for '{packageName}'.");
            }

            version = resolution.Version;
            authorizedSources = resolution.ReportingSources;
            selectedVersionSourceUrls =
                [.. resolution.ReportingSources.Select(source => source.Url)];
            selectedVersionUsesOriginalSources =
                originalAuthorizedSourceKeys.SequenceEqual(
                    NuGetSourceResolver.SourceKeys(resolution.ReportingSources));
        }

        // Get version if not specified
        if (version == null)
        {
            CancellationTokenSource? latestTimeout = null;
            if (!forceLatest && !HttpClientFactory.IsOffline && cachedVersions.Count > 0)
            {
                latestTimeout = new CancellationTokenSource(
                    CachedVersionResolutionTimeout);
            }

            PackageCoordinateResolution resolution;
            try
            {
                resolution = await PackageCoordinateResolver.ResolveAsync(
                    client,
                    new PackageCoordinate(packageName),
                    authorizedSources,
                    log,
                    includePrerelease: includePrerelease,
                    useVersionCache: !forceLatest,
                    cancellationToken: latestTimeout?.Token ?? default)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (latestTimeout?.IsCancellationRequested == true)
            {
                return PackageExtractionOutcome.Error(
                    DescribeCachedVersionFallback(
                        packageName,
                        cachedVersions,
                        offline: false));
            }
            finally
            {
                latestTimeout?.Dispose();
            }

            if (resolution
                is not PackageCoordinateResolution.Resolved resolved)
            {
                if (HttpClientFactory.IsOffline)
                {
                    if (cachedVersions.Count > 0)
                    {
                        return PackageExtractionOutcome.Error(
                            DescribeCachedVersionFallback(
                                packageName,
                                cachedVersions,
                                offline: true));
                    }

                    return PackageExtractionOutcome.Error($"Package '{packageName}' is not available offline; no cached version was found.");
                }

                return PackageExtractionOutcome.Error(
                    (FeedFailureTelemetry.Current?.DescribeFailure(packageName)
                        ?? InertString.Format(TextPolicy.Field, $"Package '{packageName}' not found."))
                        .ToString());
            }

            version = resolved.Coordinate.Version;
            authorizedSources = resolved.Coordinate.Sources;
            selectedVersionSourceUrls =
                [.. resolved.Coordinate.Sources.Select(source => source.Url)];
            selectedVersionUsesOriginalSources =
                originalAuthorizedSourceKeys.SequenceEqual(
                    NuGetSourceResolver.SourceKeys(resolved.Coordinate.Sources));
        }

        // Normalize to lowercase for NuGet API
        string normalizedName = packageName.ToLowerInvariant();
        string normalizedVersion = version.ToLowerInvariant();

        IReadOnlyList<string> authorizedProducerKeys =
            NuGetSourceResolver.SourceKeys(authorizedSources);
        PackageAcquisitionRequest request = CreatePackageAcquisitionRequest(
            normalizedName,
            normalizedVersion,
            authorizedProducerKeys,
            authorizedSources,
            client);
        PackageExtractionOutcome outcome =
            await s_packageRequests.GetOrAddAsync(
            request,
            _ => AcquireResolvedPackageAsync(
                client,
                packageName,
                version,
                normalizedName,
                normalizedVersion,
                authorizedSources,
                sourceOptions,
                log,
                tempDirPrefix),
            // This is an in-flight registry. The committed filesystem entry is
            // authoritative and is revalidated by every later request.
            static _ => false).ConfigureAwait(false);

        if (outcome.Result is { } result
            && (result.ProducerKey is null
                || !authorizedProducerKeys.Contains(result.ProducerKey)))
        {
            return PackageExtractionOutcome.Error(
                $"Package '{packageName}' version '{version}' resolved from an unauthorized producer.");
        }

        return outcome.Result is { } selectedResult
            ? selectedResult with
            {
                SelectedVersionSourceUrls = selectedVersionSourceUrls,
                SelectedVersionUsesOriginalSources =
                    selectedVersionUsesOriginalSources,
            }
            : outcome;
    }

    private static string DescribeCachedVersionFallback(
        string packageName,
        IReadOnlyList<string> cachedVersions,
        bool offline)
    {
        const int DisplayLimit = 5;
        string displayed = string.Join(", ", cachedVersions.Take(DisplayLimit));
        string remainder = cachedVersions.Count > DisplayLimit
            ? $" (+{cachedVersions.Count - DisplayLimit} more)"
            : "";
        string reason = offline
            ? $"Package '{packageName}' cannot resolve its latest version while offline."
            : $"Package '{packageName}' could not resolve its latest version before the online lookup timed out.";

        return $"{reason}{Environment.NewLine}" +
            $"Locally cached versions: {displayed}{remainder}{Environment.NewLine}" +
            $"Use an exact version to skip version discovery, for example: " +
            $"dotnet-inspect package {packageName}@{cachedVersions[0]}";
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
        // Full producer list in one EnumerateCached so app-cache slots for
        // every authorized source precede any global-packages tier.
        IReadOnlyList<string> producerKeys =
            NuGetSourceResolver.SourceKeys(sources);
        PackageContentAdmission.Outcome? lastCacheRejection = null;
        foreach (IPackageContent cached in s_packageStore.EnumerateCached(
                     normalizedName,
                     normalizedVersion,
                     producerKeys,
                     log))
        {
            PackageContentAdmission.Outcome admission =
                await PackageContentAdmission.EvaluateAsync(
                    cached,
                    PackagePayloadLimits.Default,
                    CancellationToken.None).ConfigureAwait(false);
            if (admission != PackageContentAdmission.Outcome.Admissible)
            {
                lastCacheRejection = admission;
                log?.Invoke(
                    admission == PackageContentAdmission.Outcome.MissingArchive
                        ? $"Cached content for package '{packageName}' version "
                            + $"'{version}' from one authorized producer has no "
                            + "retained archive and no usable extracted tree."
                        : $"Cached content for package '{packageName}' version "
                            + $"'{version}' from one authorized producer does not "
                            + "satisfy the current payload limits.");
                continue;
            }

            return new PackageExtractionResult(
                cached.RootPath!,
                null,
                packageName,
                version,
                cached.NupkgPath,
                FromCache: true,
                cached.ProducerKey);
        }

        if (HttpClientFactory.IsOffline)
        {
            string offlineReason = lastCacheRejection switch
            {
                PackageContentAdmission.Outcome.LimitsExceeded =>
                    "a cached package was found but does not satisfy the current payload limits",
                PackageContentAdmission.Outcome.MissingArchive =>
                    "a cached package was found but has no retained archive and no usable extracted tree",
                _ =>
                    "no cached package was found",
            };
            return PackageExtractionOutcome.Error(
                $"Package '{packageName}' version '{version}' is not available offline; {offlineReason}.");
        }

        string tempDir = Directory.CreateTempSubdirectory(tempDirPrefix).FullName;

        try
        {
            // Try each source in order. The bounded download lands in this
            // temporary file; its bytes are admitted before publication.
            string nupkgPath = Path.Combine(
                tempDir,
                $"{packageName}.{version}.nupkg");
            bool sourceSuppliedUnusablePayload = false;
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
                    $"Downloading: {packageName} {version} from {PackageSourceDisplay.ForDiagnostics(source)}");

                try
                {
                    HttpRetryHelper.DownloadToFileResult download =
                        await HttpRetryHelper.DownloadToFileWithRetryAsync(
                            client,
                            nupkgUrl,
                            nupkgPath,
                            log: log,
                            auth: NuGetCredentialScope.AuthFor(source, nupkgUrl, log),
                            trafficKind: NetworkTrafficKind.PackageDownload)
                            .ConfigureAwait(false);
                    if (download
                        is HttpRetryHelper.DownloadToFileResult.RejectedPayload)
                    {
                        sourceSuppliedUnusablePayload = true;
                        log?.Invoke(
                            $"Source {PackageSourceDisplay.ForDiagnostics(source)} advertised a package payload above the configured archive limit.");
                        continue;
                    }

                    if (download is HttpRetryHelper.DownloadToFileResult.Succeeded)
                    {
                        // Re-admit through the same bounded reader used on the
                        // host-neutral path so a raced or swapped on-disk file
                        // cannot bypass MaxArchiveBytes via ReadAllBytesAsync.
                        byte[]? archive;
                        await using (FileStream onDisk = new(
                            nupkgPath,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.Read,
                            bufferSize: 81920,
                            FileOptions.Asynchronous | FileOptions.SequentialScan))
                        {
                            archive = await PackageContentAdmission.ReadBoundedAsync(
                                    onDisk,
                                    PackagePayloadLimits.Default.MaxArchiveBytes,
                                    CancellationToken.None)
                                .ConfigureAwait(false);
                        }

                        if (archive is null)
                        {
                            sourceSuppliedUnusablePayload = true;
                            log?.Invoke(
                                $"Source {PackageSourceDisplay.ForDiagnostics(source)} advertised a package payload above the configured archive limit.");
                            continue;
                        }

                        if (PackageArchiveValidator.Validate(archive)
                            is PackageArchiveValidation.Rejected rejection)
                        {
                            sourceSuppliedUnusablePayload = true;
                            log?.Invoke(
                                $"Source {PackageSourceDisplay.ForDiagnostics(source)} did not deliver a usable package payload: "
                                + rejection.Reason);
                            continue;
                        }

                        using var archiveStream = new MemoryStream(
                            archive,
                            writable: false);
                        IPackageContent content = await s_packageStore.CommitAsync(
                                packageName,
                                version,
                                NuGetCache.GetSourceKey(source.Url),
                                archiveStream)
                            .ConfigureAwait(false);
                        if (!await PackageContentAdmission.IsAdmissibleAsync(
                                content,
                                PackagePayloadLimits.Default,
                                CancellationToken.None).ConfigureAwait(false))
                        {
                            sourceSuppliedUnusablePayload = true;
                            log?.Invoke(
                                $"Source {PackageSourceDisplay.ForDiagnostics(source)} did not publish content satisfying the current payload limits.");
                            continue;
                        }

                        log?.Invoke(
                            $"Package downloaded successfully from {PackageSourceDisplay.ForDiagnostics(source)}.");
                        log?.Invoke($"Cached to: {content.RootPath}");
                        return new PackageExtractionResult(
                            content.RootPath!,
                            TempDir: null,
                            packageName,
                            version,
                            content.NupkgPath,
                            FromCache: true,
                            content.ProducerKey);
                    }
                }
                catch (HttpRequestException ex)
                {
                    // The transport's message embeds the request URI, and that
                    // URI came from a feed-declared flat-container base.
                    log?.Invoke(
                        $"Source {PackageSourceDisplay.ForDiagnostics(source)} failed: "
                        + UrlRedaction.DescribeRequestFailure(nupkgUrl, ex));
                }
                catch (Exception ex) when (
                    ex is IOException
                        or InvalidDataException
                        or NotSupportedException
                        or UnauthorizedAccessException
                        or OperationCanceledException)
                {
                    // Body timeout, mid-body stall, unreadable archive, or
                    // persistence failure is this source failing — not a
                    // reason to stop trying every other authorized source.
                    // This path has no caller CancellationToken; any OCE here
                    // is the transport body timer (see DownloadToFileWithRetryAsync).
                    sourceSuppliedUnusablePayload = true;
                    log?.Invoke(
                        $"Source {PackageSourceDisplay.ForDiagnostics(source)} did not deliver a usable package payload.");
                }
            }

            if (sourceSuppliedUnusablePayload)
            {
                return PackageExtractionOutcome.Error(
                    $"Package '{packageName}@{version}' was rejected before caching because no authorized source supplied usable content.");
            }

            // Differentiate "package doesn't exist" from "version doesn't exist"
            // from "some authorized source never answered". A nonempty listing
            // from one feed must not suppress failures on another — the missing
            // pin may live only on the unreadable source.
            var knownVersions = await GetVersionsAsync(
                client,
                packageName,
                includePrerelease: true,
                limit: null,
                log: null,
                sourceOptions: sourceOptions).ConfigureAwait(false);
            if (FeedFailureTelemetry.Current is { HasFailures: true } hopFailures)
            {
                return PackageExtractionOutcome.Error(
                    (hopFailures.DescribeFailure(packageName)
                        ?? InertString.Format(
                            TextPolicy.Field,
                            $"Package '{packageName}' could not be fully resolved from every authorized source."))
                        .ToString());
            }

            if (knownVersions == null || knownVersions.Count == 0)
            {
                return PackageExtractionOutcome.Error(
                    InertString.Format(
                        TextPolicy.Field,
                        $"Package '{packageName}' not found.")
                        .ToString());
            }

            return PackageExtractionOutcome.Error(
                $"Version '{version}' of package '{packageName}' not found. Use --versions to see available versions.");
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

    /// <summary>
    /// Identifies one in-flight acquisition. The source scope is part of the
    /// identity, not just the coordinate: callers configured for different
    /// sources must not share a download, or one would receive bytes the other
    /// was entitled to. Source order is also part of the identity because cache
    /// slots and feeds are consulted in that order.
    /// </summary>
    internal static PackageAcquisitionRequest CreatePackageAcquisitionRequest(
        string normalizedName,
        string normalizedVersion,
        IReadOnlyList<string> authorizedProducerKeys,
        IReadOnlyList<NuGetSource>? transportSources,
        HttpClient? client)
        => new(
            $"{normalizedName}@{normalizedVersion}",
            string.Join(
                '|',
                authorizedProducerKeys
                    .Distinct(StringComparer.Ordinal)),
            CreateTransportScope(transportSources, client),
            Path.GetFullPath(NuGetCache.GetPackageContentCachePath()),
            NuGetCache.UsesGlobalPackages,
            HttpClientFactory.IsOffline);

    private static string CreateTransportScope(
        IReadOnlyList<NuGetSource>? sources,
        HttpClient? client)
    {
        if (sources is null && client is null)
            return string.Empty;

        var scope = new StringBuilder();
        if (client is not null)
        {
            long clientIdentity = s_httpClientIdentities.GetValue(
                client,
                static _ => new HttpClientIdentity(
                    Interlocked.Increment(ref s_nextHttpClientIdentity))).Value;
            Append(clientIdentity.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (sources is not null)
        {
            foreach (NuGetSource source in sources)
            {
                Append(source.Url);
                Append(source.Credential?.Username);
                Append(source.Credential?.Password);
            }
        }

        return Convert.ToHexString(
            HMACSHA256.HashData(
                s_transportScopeKey,
                Encoding.UTF8.GetBytes(scope.ToString())));

        void Append(string? value)
        {
            scope.Append(value?.Length ?? -1);
            scope.Append(':');
            scope.Append(value);
            scope.Append(';');
        }
    }

    internal readonly record struct PackageAcquisitionRequest(
        string Coordinate,
        string AuthorizedProducerScope,
        string TransportScope,
        string CacheRoot,
        bool UseGlobalPackages,
        bool Offline);

    private sealed record HttpClientIdentity(long Value);

    /// <summary>
    /// Builds the flat-container version-index URL
    /// (<c>{base}/{id}/index.json</c>), or null when the base address is not a
    /// usable absolute HTTP(S) resource URL.
    /// </summary>
    /// <remarks>
    /// The base address is feed-declared metadata, so it goes through
    /// <see cref="PackageResourceUrl.Combine"/> like every other package
    /// resource: appending to it as text would put the package path inside a
    /// signed base's query, and would hand a relative or non-HTTP <c>@id</c> to
    /// the request layer instead of ending that one source.
    /// </remarks>
    private static string? GetVersionIndexUrl(
        string? baseAddress,
        string packageName)
        => PackageResourceUrl.Combine(baseAddress, packageName, "index.json");

    /// <summary>
    /// Gets the download URL for a package from a specific source.
    /// </summary>
    /// <remarks>
    /// The base address is feed-declared metadata and the id and version are
    /// product-validated coordinate components, so the URL is composed by
    /// <see cref="PackageResourceUrl.Combine"/> rather than by concatenation:
    /// it appends escaped path segments to the base's path and preserves any
    /// query the base carries. A base address that is not a usable absolute
    /// HTTP(S) URL yields null, which the caller treats as this source failing
    /// to serve the coordinate.
    /// </remarks>
    public static async Task<string?> GetPackageDownloadUrlAsync(
        HttpClient client,
        NuGetSource source,
        string packageName,
        string version,
        Action<string>? log,
        CancellationToken cancellationToken = default)
    {
        string fileName = $"{packageName}.{version}.nupkg";

        // Check for well-known flat-container URL (nuget.org optimization)
        var flatContainerUrl = source.GetFlatContainerUrl();
        if (flatContainerUrl != null)
        {
            return PackageResourceUrl.Combine(
                flatContainerUrl,
                packageName,
                version,
                fileName);
        }

        // Query V3 service index to discover PackageBaseAddress (flat-container) endpoint
        var baseAddress = await GetPackageBaseAddressAsync(
            client,
            source,
            log,
            cancellationToken).ConfigureAwait(false);
        return PackageResourceUrl.Combine(
            baseAddress,
            packageName,
            version,
            fileName);
    }

    /// <summary>
    /// Builds the flat-container URL for a package's .nuspec
    /// ({base}/{id}/{version}/{id}.nuspec) while preserving malformed critical
    /// resource evidence from service-index discovery.
    /// </summary>
    private static async Task<PackageResourceUrlResult> GetNuspecUrlAsync(
        HttpClient client,
        NuGetSource source,
        string packageName,
        string version,
        Action<string>? log)
    {
        string fileName = $"{packageName}.nuspec";

        var flatContainerUrl = source.GetFlatContainerUrl();
        if (flatContainerUrl != null)
        {
            return new PackageResourceUrlResult(
                PackageResourceUrl.Combine(
                    flatContainerUrl,
                    packageName,
                    version,
                    fileName),
                HasMalformedCriticalResource: false);
        }

        ServiceIndexResourceResult baseAddress =
            await GetPackageBaseAddressResultAsync(
                client,
                source,
                log).ConfigureAwait(false);
        return new PackageResourceUrlResult(
            PackageResourceUrl.Combine(
                baseAddress.Id,
                packageName,
                version,
                fileName),
            baseAddress.HasMalformedCriticalResource);
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
        => (await ProbeNuspecXmlCoreAsync(
            client,
            packageId,
            version,
            log,
            sourceOptions,
            validateCoordinate: false).ConfigureAwait(false)).Xml;

    /// <summary>
    /// Probes a package's nuspec while preserving whether a missing document was
    /// authoritatively absent or could not be checked.
    /// </summary>
    public static async Task<NuspecProbeResult> ProbeNuspecXmlAsync(
        HttpClient client,
        string packageId,
        string version,
        Action<string>? log = null,
        NuGetSourceOptions? sourceOptions = null)
        => await ProbeNuspecXmlCoreAsync(
            client,
            packageId,
            version,
            log,
            sourceOptions,
            validateCoordinate: true).ConfigureAwait(false);

    /// <summary>
    /// Probes an extracted package for bounded, coordinate-matching nuspec
    /// evidence.
    /// </summary>
    public static async Task<NuspecProbeResult> ProbeExtractedPackageNuspecAsync(
        string extractPath,
        string packageId,
        string version,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
        => await ProbeExtractedPackageNuspecCoreAsync(
            extractPath,
            packageId,
            version,
            validateCoordinate: true,
            log,
            cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Probes a local package archive for bounded, coordinate-matching nuspec
    /// evidence.
    /// </summary>
    /// <remarks>
    /// Gated end to end by
    /// <c>RidPackageVerifierTests.VerifyAsync_UnusableLocalSiblingLeavesAvailabilityUnknown</c>.
    /// </remarks>
    public static async Task<NuspecProbeResult> ProbeLocalPackageArchiveAsync(
        string packagePath,
        string packageId,
        string version,
        Action<string>? log = null,
        CancellationToken cancellationToken = default,
        PackageArchiveReadBudget? archiveReadBudget = null)
    {
        if (!IsValidPackageId(packageId)
            || !TryNormalizePackageVersion(version, out _))
        {
            return new NuspecProbeResult(
                null,
                NuspecProbeStatus.Indeterminate);
        }

        try
        {
            FileAttributes attributes = File.GetAttributes(packagePath);
            if ((attributes
                    & (FileAttributes.Directory
                        | FileAttributes.ReparsePoint))
                != 0)
            {
                return IndeterminateLocalProbe(
                    log,
                    "Local RID package path was not a regular file.");
            }

            var info = new FileInfo(packagePath);
            if (info.Length <= 0
                || info.Length
                    > PackagePayloadLimits.Default.MaxArchiveBytes)
            {
                return IndeterminateLocalProbe(
                    log,
                    "Local RID package was empty or exceeded the package archive limit.");
            }
        }
        catch (FileNotFoundException)
        {
            return new NuspecProbeResult(
                null,
                NuspecProbeStatus.Absent);
        }
        catch (DirectoryNotFoundException ex)
        {
            return IndeterminateLocalProbe(
                log,
                $"Local RID package directory could not be read: {ex.GetType().Name}");
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or NotSupportedException)
        {
            return IndeterminateLocalProbe(
                log,
                $"Local RID package metadata could not be read: {ex.GetType().Name}");
        }

        FileStream stream;
        try
        {
            stream = new FileStream(
                packagePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (FileNotFoundException)
        {
            return new NuspecProbeResult(
                null,
                NuspecProbeStatus.Absent);
        }
        catch (DirectoryNotFoundException ex)
        {
            return IndeterminateLocalProbe(
                log,
                $"Local RID package directory could not be read: {ex.GetType().Name}");
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or NotSupportedException)
        {
            return IndeterminateLocalProbe(
                log,
                $"Local RID package could not be opened: {ex.GetType().Name}");
        }

        await using (stream)
        {
            try
            {
                long archiveLength = stream.Length;
                if (archiveLength <= 0
                    || archiveLength
                        > PackagePayloadLimits.Default.MaxArchiveBytes)
                {
                    return IndeterminateLocalProbe(
                        log,
                        "Local RID package was empty or exceeded the package archive limit.");
                }

                // Reserve from the opened handle, then read that same handle.
                if (archiveReadBudget is not null
                    && !archiveReadBudget.TryReserve(archiveLength))
                {
                    return IndeterminateLocalProbe(
                        log,
                        "Local RID package archive read budget was exhausted; "
                        + "remaining evidence is unknown.");
                }

                byte[]? archive =
                    await PackageContentAdmission.ReadBoundedAsync(
                            stream,
                            archiveLength,
                            cancellationToken)
                        .ConfigureAwait(false);
                if (archive is null || archive.Length == 0)
                {
                    return IndeterminateLocalProbe(
                        log,
                        "Local RID package changed while it was being read.");
                }

                if (PackageArchiveValidator.Validate(
                        archive,
                        cancellationToken: cancellationToken)
                    is PackageArchiveValidation.Rejected rejection)
                {
                    return IndeterminateLocalProbe(
                        log,
                        "Local RID package was not a usable package archive: "
                        + rejection.Reason);
                }

                using var archiveStream = new MemoryStream(
                    archive,
                    writable: false);
                using var package = new ZipArchive(
                    archiveStream,
                    ZipArchiveMode.Read);
                ZipArchiveEntry? nuspec = null;
                foreach (ZipArchiveEntry entry in package.Entries)
                {
                    if (!IsRootNuspec(entry))
                        continue;

                    if (nuspec is not null)
                    {
                        return IndeterminateLocalProbe(
                            log,
                            "Local RID package contained multiple root nuspec files.");
                    }

                    nuspec = entry;
                }

                if (nuspec is null)
                {
                    return IndeterminateLocalProbe(
                        log,
                        "Local RID package contained no root nuspec file.");
                }

                await using Stream nuspecStream = nuspec.Open();
                byte[]? nuspecBytes =
                    await PackageContentAdmission.ReadBoundedAsync(
                            nuspecStream,
                            MaxNuspecBytes,
                            cancellationToken)
                        .ConfigureAwait(false);
                if (nuspecBytes is null || nuspecBytes.Length == 0)
                {
                    return IndeterminateLocalProbe(
                        log,
                        "Local RID package nuspec was empty or exceeded the nuspec limit.");
                }

                string xml;
                try
                {
                    xml = StrictUtf8.GetString(nuspecBytes);
                }
                catch (DecoderFallbackException)
                {
                    return IndeterminateLocalProbe(
                        log,
                        "Local RID package nuspec was not valid UTF-8.");
                }

                if (!IsExpectedNuspec(xml, packageId, version))
                {
                    return IndeterminateLocalProbe(
                        log,
                        "Local RID package nuspec was malformed or did not match "
                        + "the requested package.");
                }

                return new NuspecProbeResult(
                    xml,
                    NuspecProbeStatus.Present);
            }
            catch (Exception ex) when (
                ex is IOException
                    or InvalidDataException
                    or NotSupportedException)
            {
                return IndeterminateLocalProbe(
                    log,
                    $"Local RID package could not be validated: {ex.GetType().Name}");
            }
        }
    }

    private static NuspecProbeResult IndeterminateLocalProbe(
        Action<string>? log,
        string message)
    {
        log?.Invoke(message);
        return new NuspecProbeResult(
            null,
            NuspecProbeStatus.Indeterminate);
    }

    private static bool IsRootNuspec(ZipArchiveEntry entry)
    {
        string path = entry.FullName;
        return path.EndsWith(
                   ".nuspec",
                   StringComparison.OrdinalIgnoreCase)
               && path.IndexOf('/') < 0
               && path.IndexOf('\\') < 0;
    }

    private static async Task<NuspecProbeResult> ProbeNuspecXmlCoreAsync(
        HttpClient client,
        string packageId,
        string version,
        Action<string>? log,
        NuGetSourceOptions? sourceOptions,
        bool validateCoordinate)
    {
        if (!IsValidPackageId(packageId)
            || !TryNormalizePackageVersion(
                version,
                out string normalizedVersion))
        {
            return new NuspecProbeResult(
                null,
                NuspecProbeStatus.Indeterminate);
        }

        normalizedVersion = normalizedVersion.ToLowerInvariant();
        string normalizedName = packageId.ToLowerInvariant();
        bool sawAuthoritativeAbsence = false;
        bool sawIndeterminateSource = false;

        // Cache hit: read the nuspec straight from the already-extracted package,
        // under the same byte ceiling as the remote path. Marker presence alone
        // is not a size admission gate.
        IReadOnlyList<string> sourceKeys =
            NuGetSourceResolver.ResolveSourceKeysForPackage(
                sourceOptions,
                packageId);
        foreach (CachedPackage cached in
                 NuGetCache.EnumerateCachedPackageContent(
                     normalizedName,
                     normalizedVersion,
                     sourceKeys))
        {
            string cachedPath = cached.ExtractPath;
            try
            {
                if (!NuGetCache.IsCachedPackageValid(cachedPath))
                {
                    sawIndeterminateSource = true;
                    continue;
                }

                NuspecProbeResult cachedProbe =
                    await ProbeExtractedPackageNuspecCoreAsync(
                        cachedPath,
                        packageId,
                        version,
                        validateCoordinate,
                        log,
                        CancellationToken.None).ConfigureAwait(false);
                if (cachedProbe.Status == NuspecProbeStatus.Present)
                {
                    return cachedProbe;
                }
            }
            catch (IOException ex)
            {
                log?.Invoke(
                    $"Cached nuspec read failed: {ex.GetType().Name}");
            }
            catch (UnauthorizedAccessException ex)
            {
                log?.Invoke(
                    $"Cached nuspec read failed: {ex.GetType().Name}");
            }

            sawIndeterminateSource = true;
        }

        foreach (var source in NuGetSourceResolver.ResolveSourcesForPackage(
            sourceOptions,
            packageId))
        {
            if (!IsHttpSource(source))
                continue;

            PackageResourceUrlResult resource =
                await GetNuspecUrlAsync(
                    client,
                    source,
                    normalizedName,
                    normalizedVersion,
                    log).ConfigureAwait(false);
            if (resource.HasMalformedCriticalResource)
                sawIndeterminateSource = true;
            if (resource.Url == null)
            {
                sawIndeterminateSource = true;
                continue;
            }

            try
            {
                HttpRetryHelper.HttpBodyFetchResult body =
                    await HttpRetryHelper.GetBytesAfterHeadersWithRetryAsync(
                        client,
                        resource.Url,
                        static _ => true,
                        log: log,
                        auth: NuGetCredentialScope.AuthFor(
                            source,
                            resource.Url,
                            log),
                        trafficKind: NetworkTrafficKind.PackageManifest,
                        maxDownloadSize: MaxNuspecBytes).ConfigureAwait(false);
                if (body.Status == HttpRetryHelper.HttpBodyFetchStatus.Success
                    && body.Bytes is { Length: > 0 })
                {
                    string xml;
                    try
                    {
                        xml = (validateCoordinate ? StrictUtf8 : Encoding.UTF8)
                            .GetString(body.Bytes);
                    }
                    catch (DecoderFallbackException)
                    {
                        log?.Invoke(
                            $"Nuspec from {PackageSourceDisplay.ForDiagnostics(source)} "
                            + "was not valid UTF-8.");
                        sawIndeterminateSource = true;
                        continue;
                    }

                    if (!validateCoordinate
                        || IsExpectedNuspec(xml, packageId, version))
                    {
                        return new NuspecProbeResult(
                            xml,
                            NuspecProbeStatus.Present);
                    }

                    log?.Invoke(
                        $"Nuspec from {PackageSourceDisplay.ForDiagnostics(source)} "
                        + "was malformed or did not match the requested package.");
                    sawIndeterminateSource = true;
                    continue;
                }

                if (body.StatusCode == HttpStatusCode.NotFound)
                {
                    sawAuthoritativeAbsence = true;
                    continue;
                }

                sawIndeterminateSource = true;
                if (body.Status == HttpRetryHelper.HttpBodyFetchStatus.TooLarge)
                {
                    log?.Invoke(
                        $"Nuspec from {PackageSourceDisplay.ForDiagnostics(source)} "
                        + $"exceeded {MaxNuspecBytes} byte cap.");
                }
            }
            catch (HttpRequestException ex)
            {
                log?.Invoke(
                    $"Nuspec fetch from {PackageSourceDisplay.ForDiagnostics(source)} failed: "
                    + $"{ex.GetType().Name}");
                sawIndeterminateSource = true;
            }
        }

        return new NuspecProbeResult(
            null,
            sawAuthoritativeAbsence && !sawIndeterminateSource
                ? NuspecProbeStatus.Absent
                : NuspecProbeStatus.Indeterminate);
    }

    private static async Task<NuspecProbeResult>
        ProbeExtractedPackageNuspecCoreAsync(
            string extractPath,
            string packageId,
            string version,
            bool validateCoordinate,
            Action<string>? log,
            CancellationToken cancellationToken)
    {
        if (!IsValidPackageId(packageId)
            || !TryNormalizePackageVersion(version, out _))
        {
            return new NuspecProbeResult(
                null,
                NuspecProbeStatus.Indeterminate);
        }

        try
        {
            string[] nuspecs = Directory
                .EnumerateFiles(
                    extractPath,
                    "*",
                    SearchOption.TopDirectoryOnly)
                .Where(path => path.EndsWith(
                    ".nuspec",
                    StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToArray();
            if (nuspecs.Length == 0
                || (validateCoordinate && nuspecs.Length > 1))
            {
                log?.Invoke(
                    nuspecs.Length == 0
                        ? "Extracted package contained no root nuspec file."
                        : "Extracted package contained multiple root nuspec files.");
                return new NuspecProbeResult(
                    null,
                    NuspecProbeStatus.Indeterminate);
            }

            string? xml = await TryReadNuspecFileAsync(
                    nuspecs[0],
                    cancellationToken,
                    strictUtf8: validateCoordinate)
                .ConfigureAwait(false);
            if (xml is null
                || (validateCoordinate
                    && !IsExpectedNuspec(
                        xml,
                        packageId,
                        version)))
            {
                log?.Invoke(
                    "Extracted package nuspec was unreadable, malformed, or "
                    + "did not match the requested package.");
                return new NuspecProbeResult(
                    null,
                    NuspecProbeStatus.Indeterminate);
            }

            return new NuspecProbeResult(
                xml,
                NuspecProbeStatus.Present);
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException)
        {
            log?.Invoke(
                $"Extracted package nuspec could not be inspected: {ex.GetType().Name}");
            return new NuspecProbeResult(
                null,
                NuspecProbeStatus.Indeterminate);
        }
    }

    private static bool IsExpectedNuspec(
        string xml,
        string packageId,
        string version)
    {
        try
        {
            string parseableXml = xml.Length > 0 && xml[0] == '\uFEFF'
                ? xml[1..]
                : xml;
            using var textReader = new StringReader(parseableXml);
            using XmlReader reader = XmlReader.Create(
                textReader,
                new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument = MaxNuspecBytes,
                });
            XDocument document = XDocument.Load(reader, LoadOptions.None);
            XElement? root = document.Root;
            if (root is null
                || root.Name.LocalName != "package")
            {
                return false;
            }

            XNamespace nuspecNamespace = root.Name.Namespace;
            XElement[] metadataElements = root.Elements()
                .Where(element =>
                    element.Name.LocalName == "metadata")
                .Take(2)
                .ToArray();
            if (metadataElements.Length != 1
                || metadataElements[0].Name.Namespace
                    != nuspecNamespace)
            {
                return false;
            }

            XElement metadata = metadataElements[0];
            XElement[] idElements = metadata.Elements()
                .Where(element => element.Name.LocalName == "id")
                .Take(2)
                .ToArray();
            XElement[] versionElements = metadata.Elements()
                .Where(element =>
                    element.Name.LocalName == "version")
                .Take(2)
                .ToArray();
            if (idElements.Length != 1
                || idElements[0].Name.Namespace != nuspecNamespace
                || versionElements.Length != 1
                || versionElements[0].Name.Namespace != nuspecNamespace)
            {
                return false;
            }

            string actualId = idElements[0].Value;
            string actualVersion = versionElements[0].Value;

            return string.Equals(
                       actualId.Trim(),
                       packageId,
                       StringComparison.OrdinalIgnoreCase)
                   && TryNormalizePackageVersion(
                       version,
                       out string expected)
                   && TryNormalizePackageVersion(
                       actualVersion.Trim(),
                       out string actual)
                   && string.Equals(
                       expected,
                       actual,
                       StringComparison.OrdinalIgnoreCase);
        }
        catch (XmlException)
        {
            return false;
        }
    }

    /// <summary>
    /// Reads a local <c>.nuspec</c> under <see cref="MaxNuspecBytes"/>. Returns
    /// null when missing, unreadable, empty, or oversize.
    /// </summary>
    internal static async Task<string?> TryReadNuspecFileAsync(
        string nuspecPath,
        CancellationToken cancellationToken = default,
        bool strictUtf8 = false)
    {
        try
        {
            var info = new FileInfo(nuspecPath);
            if (!info.Exists || info.Length <= 0 || info.Length > MaxNuspecBytes)
                return null;

            await using FileStream stream = new(
                nuspecPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            byte[]? bytes = await PackageContentAdmission.ReadBoundedAsync(
                stream,
                MaxNuspecBytes,
                cancellationToken).ConfigureAwait(false);
            if (bytes is null || bytes.Length == 0)
                return null;

            return (strictUtf8 ? StrictUtf8 : Encoding.UTF8)
                .GetString(bytes);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
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
        Action<string>? log,
        CancellationToken cancellationToken = default)
        => (await GetPackageBaseAddressResultAsync(
                client,
                source,
                log,
                cancellationToken).ConfigureAwait(false))
            .Id;

    private static Task<ServiceIndexResourceResult>
        GetPackageBaseAddressResultAsync(
            HttpClient client,
            NuGetSource source,
            Action<string>? log,
            CancellationToken cancellationToken = default)
        => GetServiceIndexResourceResultAsync(
            client,
            source,
            "PackageBaseAddress",
            log,
            cancellationToken);

    /// <summary>
    /// Discovers the first compatible SearchQueryService endpoint from a V3 service index.
    /// Returns null when the
    /// source is not an HTTP feed, its service index cannot be read, or it advertises no search
    /// resource (a valid state — flat-container-only feeds exist and simply cannot be searched).
    /// </summary>
    public static async Task<string?> GetSearchQueryServiceAsync(
        HttpClient client,
        NuGetSource source,
        Action<string>? log = null,
        CancellationToken cancellationToken = default,
        TimeSpan? responseBodyTimeout = null)
    {
        IReadOnlyList<string>? services = await GetSearchQueryServicesAsync(
            client,
            source,
            log,
            cancellationToken,
            responseBodyTimeout).ConfigureAwait(false);
        return services?.FirstOrDefault();
    }

    /// <summary>
    /// Discovers every SearchQueryService endpoint at the highest advertised capability version,
    /// preserving service-index order for failover.
    /// </summary>
    public static async Task<IReadOnlyList<string>?> GetSearchQueryServicesAsync(
        HttpClient client,
        NuGetSource source,
        Action<string>? log = null,
        CancellationToken cancellationToken = default,
        TimeSpan? responseBodyTimeout = null)
    {
        IReadOnlyList<ServiceResource>? resources =
            await GetServiceIndexResourcesAsync(
                client,
                source,
                log,
                cancellationToken,
                responseBodyTimeout).ConfigureAwait(false);
        if (resources is null)
            return null;

        return
        [
            .. GetCompatibleSearchServiceResources(resources)
                .Select(resource => resource.Id)
                .Distinct(StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// Reads all resources advertised by a V3 service index.
    /// </summary>
    /// <returns>
    /// The advertised resources, an empty list for a valid index with no resources, or
    /// <see langword="null"/> when the source is not HTTP or its index cannot be read or parsed.
    /// </returns>
    public static async Task<IReadOnlyList<ServiceResource>?> GetServiceIndexResourcesAsync(
        HttpClient client,
        NuGetSource source,
        Action<string>? log = null,
        CancellationToken cancellationToken = default,
        TimeSpan? responseBodyTimeout = null)
        => (await GetServiceIndexResourcesResultAsync(
                client,
                source,
                log,
                cancellationToken,
                responseBodyTimeout).ConfigureAwait(false))
            .Resources;

    private readonly record struct ServiceIndexResourcesResult(
        IReadOnlyList<ServiceResource>? Resources,
        bool HasMalformedCriticalResource);

    private readonly record struct ServiceIndexResourceResult(
        string? Id,
        bool HasMalformedCriticalResource);

    private readonly record struct PackageResourceUrlResult(
        string? Url,
        bool HasMalformedCriticalResource);

    private static async Task<ServiceIndexResourcesResult>
        GetServiceIndexResourcesResultAsync(
            HttpClient client,
            NuGetSource source,
            Action<string>? log = null,
            CancellationToken cancellationToken = default,
            TimeSpan? responseBodyTimeout = null)
    {
        if (!IsHttpSource(source))
        {
            log?.Invoke(
                $"Skipping non-HTTP NuGet source '{PackageSourceDisplay.ForDiagnostics(source)}': {UrlRedaction.ForDiagnostics(source.Url)}");
            return new(null, HasMalformedCriticalResource: false);
        }

        string indexUrl = source.Url;
        var indexUri = new Uri(source.Url, UriKind.Absolute);
        if (!indexUri.AbsolutePath.TrimEnd('/').EndsWith(
                "index.json",
                StringComparison.OrdinalIgnoreCase))
        {
            var builder = new UriBuilder(indexUri)
            {
                Path =
                    $"{indexUri.AbsolutePath.TrimEnd('/')}/v3/index.json",
            };
            indexUrl = builder.Uri.AbsoluteUri;
        }

        log?.Invoke(
            $"Querying service index: {UrlRedaction.ForDiagnostics(indexUrl)}");

        string? json = await HttpRetryHelper.GetStringWithRetryAsync(
            client,
            indexUrl,
            auth: NuGetCredentialScope.AuthFor(source, indexUrl, log),
            cancellationToken: cancellationToken,
            trafficKind: NetworkTrafficKind.PackageSourceDiscovery,
            responseBodyTimeout: responseBodyTimeout).ConfigureAwait(false);
        if (json == null)
            return new(null, HasMalformedCriticalResource: false);

        try
        {
            using var doc = HardenedJson.Parse(json);
            if (!doc.RootElement.TryGetProperty(
                    "resources",
                    out JsonElement resources)
                || resources.ValueKind != JsonValueKind.Array)
            {
                log?.Invoke(
                    $"Invalid service index from '{PackageSourceDisplay.ForDiagnostics(source)}': missing resources array.");
                FeedFailureTelemetry.Record(
                    indexUrl,
                    HttpStatusCode.OK);
                return new(null, HasMalformedCriticalResource: false);
            }
            var result = new List<ServiceResource>();
            bool hasMalformedCriticalResource = false;

            foreach (JsonElement resource in resources.EnumerateArray())
            {
                if (resource.ValueKind != JsonValueKind.Object)
                    continue;

                if (!resource.TryGetProperty(
                        "@type",
                        out JsonElement typeElement))
                {
                    continue;
                }

                string? id =
                    resource.TryGetProperty(
                        "@id",
                        out JsonElement idElement)
                    && idElement.ValueKind == JsonValueKind.String
                        ? idElement.GetString()
                        : null;
                IEnumerable<string> types = typeElement.ValueKind switch
                {
                    JsonValueKind.String when !string.IsNullOrWhiteSpace(
                        typeElement.GetString()) =>
                        [typeElement.GetString()!],
                    JsonValueKind.Array =>
                        typeElement
                            .EnumerateArray()
                            .Where(item => item.ValueKind == JsonValueKind.String)
                            .Select(item => item.GetString())
                            .Where(type => !string.IsNullOrWhiteSpace(type))
                            .Cast<string>(),
                    _ => [],
                };
                foreach (string type in types)
                {
                    // PackageBaseAddress is the only service-index resource
                    // whose malformation must fail complete-source / latest
                    // version resolution. RegistrationsBaseUrl, SearchQueryService,
                    // and VulnerabilityInfo are parsed when present, but a
                    // cosmetic bad @id must not convert an authoritative flat-
                    // container 404 into "source did not answer" after nested
                    // telemetry merges into the parent hop.
                    bool isCriticalHttpEndpoint =
                        IsServiceType(type, "PackageBaseAddress");
                    bool isOptionalHttpEndpoint =
                        IsServiceType(type, "RegistrationsBaseUrl")
                        || IsServiceType(type, "SearchQueryService")
                        || IsServiceType(type, "VulnerabilityInfo");
                    if (!isCriticalHttpEndpoint && !isOptionalHttpEndpoint)
                    {
                        if (!string.IsNullOrWhiteSpace(id))
                            result.Add(new ServiceResource(id, type));
                    }
                    else if (!string.IsNullOrWhiteSpace(id)
                        && Uri.TryCreate(
                            id,
                            UriKind.Absolute,
                            out Uri? resourceUri)
                        && resourceUri.Scheme is "http" or "https"
                        && (!isCriticalHttpEndpoint
                            || PackageResourceUrl.IsUsableBaseAddress(id)))
                    {
                        result.Add(new ServiceResource(
                            IsServiceType(type, "SearchQueryService")
                                ? id
                                : resourceUri.AbsoluteUri,
                            type));
                    }
                    else
                    {
                        log?.Invoke(
                            $"Ignoring invalid {new InertString(TextPolicy.Field, type)} resource URL from "
                            + $"'{PackageSourceDisplay.ForDiagnostics(source)}'.");
                        if (isCriticalHttpEndpoint)
                        {
                            // A malformed PackageBaseAddress is a failed source
                            // answer, not a quiet absence. Complete-source
                            // floating resolution depends on that distinction.
                            FeedFailureTelemetry.Record(
                                indexUrl,
                                HttpStatusCode.OK);
                            hasMalformedCriticalResource = true;
                        }
                    }
                }
            }

            return new(result, hasMalformedCriticalResource);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            log?.Invoke(
                $"Invalid service index from '{PackageSourceDisplay.ForDiagnostics(source)}': "
                + "the document could not be read.");
            FeedFailureTelemetry.Record(
                indexUrl,
                HttpStatusCode.OK);
            return new(null, HasMalformedCriticalResource: false);
        }
    }

    private static bool IsServiceType(string type, string prefix) =>
        type.Equals(prefix, StringComparison.OrdinalIgnoreCase)
        || type.StartsWith($"{prefix}/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Selects every endpoint at the highest advertised capability version, preserving
    /// service-index order.
    /// </summary>
    public static List<ServiceResource> GetCompatibleServiceResources(
        IReadOnlyList<ServiceResource> resources,
        string typePrefix)
    {
        List<ServiceResource> matching =
        [
            .. resources.Where(resource =>
                IsServiceType(resource.Type, typePrefix)),
        ];
        if (matching.Count == 0)
            return [];

        System.Version bestVersion = ServiceResourceVersion(matching[0].Type);
        for (int i = 1; i < matching.Count; i++)
        {
            System.Version version = ServiceResourceVersion(matching[i].Type);
            if (version > bestVersion)
                bestVersion = version;
        }

        return
        [
            .. matching.Where(resource =>
                ServiceResourceVersion(resource.Type) == bestVersion),
        ];
    }

    /// <summary>
    /// Selects at most four distinct endpoints at the highest supported search capability
    /// version, preserving service-index order. Unknown future versions are not assumed to
    /// preserve the current search protocol.
    /// </summary>
    public static List<ServiceResource> GetCompatibleSearchServiceResources(
        IReadOnlyList<ServiceResource> resources)
    {
        List<(ServiceResource Resource, int Rank)> matching = [];
        foreach (ServiceResource resource in resources)
        {
            if (TryGetSearchServiceRank(resource.Type, out int rank))
                matching.Add((resource, rank));
        }

        if (matching.Count == 0)
            return [];

        int bestRank = matching.Max(item => item.Rank);
        var selected = new List<ServiceResource>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach ((ServiceResource resource, int rank) in matching)
        {
            if (rank == bestRank
                && seen.Add(resource.Id))
            {
                selected.Add(resource);
                if (selected.Count == MaxEquivalentSearchEndpoints)
                    break;
            }
        }

        return selected;
    }

    private static bool TryGetSearchServiceRank(string type, out int rank)
    {
        if (type.Equals(
                "SearchQueryService/3.5.0",
                StringComparison.OrdinalIgnoreCase))
        {
            rank = 4;
            return true;
        }

        if (type.Equals(
                "SearchQueryService/3.0.0",
                StringComparison.OrdinalIgnoreCase))
        {
            rank = 3;
            return true;
        }

        if (type.Equals(
                "SearchQueryService/3.0.0-rc",
                StringComparison.OrdinalIgnoreCase))
        {
            rank = 2;
            return true;
        }

        if (type.Equals(
                "SearchQueryService/3.0.0-beta",
                StringComparison.OrdinalIgnoreCase))
        {
            rank = 1;
            return true;
        }

        if (type.Equals("SearchQueryService", StringComparison.OrdinalIgnoreCase))
        {
            rank = 0;
            return true;
        }

        rank = -1;
        return false;
    }

    private static System.Version ServiceResourceVersion(string resourceType)
    {
        int separator = resourceType.IndexOf('/');
        return separator >= 0
            && System.Version.TryParse(
                resourceType[(separator + 1)..],
                out System.Version? version)
                ? version
                : new System.Version();
    }

    /// <summary>
    /// Reads a V3 service index and returns the <c>@id</c> of the first resource whose
    /// <c>@type</c> starts with <paramref name="resourceTypePrefix"/>. Service-index types are
    /// versioned by suffix (<c>SearchQueryService/3.5.0</c>), so matching is by prefix.
    /// </summary>
    private static async Task<string?> GetServiceIndexResourceAsync(
        HttpClient client,
        NuGetSource source,
        string resourceTypePrefix,
        Action<string>? log,
        CancellationToken cancellationToken,
        TimeSpan? responseBodyTimeout = null)
        => (await GetServiceIndexResourceResultAsync(
                client,
                source,
                resourceTypePrefix,
                log,
                cancellationToken,
                responseBodyTimeout).ConfigureAwait(false))
            .Id;

    private static async Task<ServiceIndexResourceResult>
        GetServiceIndexResourceResultAsync(
            HttpClient client,
            NuGetSource source,
            string resourceTypePrefix,
            Action<string>? log,
            CancellationToken cancellationToken,
            TimeSpan? responseBodyTimeout = null)
    {
        ServiceIndexResourcesResult result =
            await GetServiceIndexResourcesResultAsync(
                client,
                source,
                log,
                cancellationToken,
                responseBodyTimeout).ConfigureAwait(false);
        if (result.Resources is null)
        {
            return new(
                Id: null,
                HasMalformedCriticalResource:
                    result.HasMalformedCriticalResource);
        }

        string? id = result.Resources
            .Where(resource =>
                IsServiceType(resource.Type, resourceTypePrefix))
            .Select(resource => resource.Id)
            .FirstOrDefault();
        return new(id, result.HasMalformedCriticalResource);
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

    // v5 fences candidate metadata that could have been attributed to a
    // noncanonical api.nuget.org URL by the former host-only shortcut.
    private const string VersionCacheCategory = "versions-v5";
    private const string VersionCacheCategoryPrefix = "versions-v";

    private sealed record SourceVersionListings(
        NuGetSource Source,
        List<PackageVersionInfo> Listings,
        bool Authoritative,
        bool SourceMissing = false);

    readonly record struct SourceVersionList(
        List<string>? Versions,
        bool Failed,
        bool SourceMissing)
    {
        internal static SourceVersionList Found(List<string> versions) =>
            new(versions, Failed: false, SourceMissing: false);

        internal static SourceVersionList Absent { get; } =
            new(null, Failed: false, SourceMissing: false);

        internal static SourceVersionList MissingSource { get; } =
            new(null, Failed: false, SourceMissing: true);

        internal static SourceVersionList Failure { get; } =
            new(null, Failed: true, SourceMissing: false);
    }

    static PackageExtractor()
    {
        CoreCache.RegisterVersionedCategory(
            VersionCacheCategoryPrefix,
            VersionCacheCategory);
    }

    internal static string GetLatestVersionCacheKey(
        string packageName,
        NuGetSource source,
        bool includePrerelease = false)
        => LatestVersionCacheKey(
            NuGetCache.GetSourceKey(source.Url),
            packageName.ToLowerInvariant(),
            includePrerelease);

    internal static string GetListingsVersionCacheKey(
        string packageName,
        NuGetSource source)
        => ListingsVersionCacheKey(
            NuGetCache.GetSourceKey(source.Url),
            packageName.ToLowerInvariant());

    /// <summary>
    /// Returns the highest fresh version candidate recorded for the supplied
    /// source identities, without consulting package-content directories.
    /// </summary>
    public static string? TryGetLatestCachedCandidateVersion(
        string packageName,
        IReadOnlyList<string> sourceKeys,
        bool includePrerelease = false)
    {
        string normalizedName = packageName.ToLowerInvariant();
        NuGet.Versioning.NuGetVersion? best = null;
        string? bestOriginal = null;

        foreach (string sourceKey in sourceKeys)
        {
            string? version = TryGetCachedLatestForSource(
                normalizedName,
                sourceKey,
                includePrerelease);
            if (version is null
                || !NuGet.Versioning.NuGetVersion.TryParse(
                    version,
                    out var parsed))
            {
                continue;
            }

            if (best is null || parsed > best)
            {
                best = parsed;
                bestOriginal = version;
            }
        }

        return bestOriginal;
    }

    /// <summary>
    /// Returns whether any fresh source-scoped candidate metadata exists for a
    /// package, regardless of stable or prerelease selection flavor.
    /// </summary>
    public static bool HasCachedCandidateVersion(
        string packageName,
        IReadOnlyList<string> sourceKeys)
        => TryGetLatestCachedCandidateVersion(
                packageName,
                sourceKeys,
                includePrerelease: false) is not null
            || TryGetLatestCachedCandidateVersion(
                packageName,
                sourceKeys,
                includePrerelease: true) is not null;

    private static string? TryGetCachedLatestForSource(
        string normalizedName,
        string sourceKey,
        bool includePrerelease)
    {
        string? latest = TryGetCachedLatestEntryForSource(
            normalizedName,
            sourceKey,
            includePrerelease);
        if (latest is not null)
            return latest;

        string? serializedListings = CoreCache.TryGet(
            VersionCacheCategory,
            ListingsVersionCacheKey(sourceKey, normalizedName),
            VersionCacheTtl,
            extension: "txt");
        List<PackageVersionInfo>? listings = serializedListings is null
            ? null
            : DeserializeListings(serializedListings);
        return listings is null
            ? null
            : PickLatest(
                listings
                    .Where(listing => listing.Listed)
                    .Select(listing => listing.Version),
                includePrerelease);
    }

    private static string? TryGetCachedLatestEntryForSource(
        string normalizedName,
        string sourceKey,
        bool includePrerelease)
    {
        string? latest = NormalizeCandidateVersion(
            CoreCache.TryGet(
                VersionCacheCategory,
                LatestVersionCacheKey(
                    sourceKey,
                    normalizedName,
                    includePrerelease),
                VersionCacheTtl,
                extension: "txt"));
        if (includePrerelease && latest is not null)
        {
            string? stable = NormalizeCandidateVersion(
                CoreCache.TryGet(
                    VersionCacheCategory,
                    LatestVersionCacheKey(
                        sourceKey,
                        normalizedName,
                        includePrerelease: false),
                    VersionCacheTtl,
                    extension: "txt"));
            latest = PickLatest([latest, stable], includePrerelease: true);
        }

        return latest;
    }

    private static string LatestVersionCacheKey(
        string sourceKey,
        string normalizedName,
        bool includePrerelease)
        => $"{sourceKey}:latest:{(includePrerelease ? "prerelease" : "stable")}:{normalizedName}";

    private static string ListingsVersionCacheKey(
        string sourceKey,
        string normalizedName)
        => $"{sourceKey}:listings:{normalizedName}";

    private static void AddReporter(
        List<NuGetSource> reporters,
        NuGetSource source)
    {
        if (!reporters.Contains(source))
        {
            reporters.Add(source);
        }
    }

    public static async Task<string?> GetLatestVersionAsync(
        HttpClient client,
        string packageName,
        List<NuGetSource> sources,
        Action<string>? log,
        bool skipCache = false,
        bool includePrerelease = false)
        => (await ResolveLatestVersionAsync(
            client,
            packageName,
            sources,
            log,
            skipCache,
            includePrerelease,
            cancellationToken: default).ConfigureAwait(false))?.Version;

    internal static async Task<PackageVersionResolution?> ResolveLatestVersionAsync(
        HttpClient client,
        string packageName,
        List<NuGetSource> sources,
        Action<string>? log,
        bool skipCache = false,
        bool includePrerelease = false,
        bool requireCompleteSources = false,
        CancellationToken cancellationToken = default)
    {
        string normalizedName = packageName.ToLowerInvariant();
        NuGet.Versioning.NuGetVersion? bestStable = null;
        string? bestStableOriginal = null;
        List<NuGetSource> stableReporters = [];
        NuGet.Versioning.NuGetVersion? bestAny = null;
        string? bestAnyOriginal = null;
        List<NuGetSource> anyReporters = [];
        bool complete = true;

        foreach (var source in sources)
        {
            if (!IsHttpSource(source))
            {
                log?.Invoke(
                    $"Skipping non-HTTP NuGet source '{PackageSourceDisplay.ForDiagnostics(source)}': {UrlRedaction.ForDiagnostics(source.Url)}");
                continue;
            }

            string? version = null;
            if (!skipCache)
            {
                version = TryGetCachedLatestForSource(
                    normalizedName,
                    NuGetCache.GetSourceKey(source.Url),
                    includePrerelease);
                if (version is not null)
                    log?.Invoke(
                        $"Using cached version from {PackageSourceDisplay.ForDiagnostics(source)}: {version}");
            }

            if (version is null)
            {
                SourceLatestVersion lookup =
                    await GetLatestVersionFromSourceAsync(
                        client,
                        normalizedName,
                        source,
                        log,
                        includePrerelease,
                        cancellationToken).ConfigureAwait(false);
                string? fetchedVersion =
                    NormalizeCandidateVersion(lookup.Version);

                // Complete-source mode needs a typed source outcome. Ambient
                // FeedFailureTelemetry is not enough: a superseded same-source
                // failure can linger while a later path proves absence, and a
                // malformed critical resource can fail quietly without a
                // telemetry mark. The lookup itself reports Failed vs Absent.
                if (requireCompleteSources
                    && fetchedVersion is null
                    && lookup.Failed)
                {
                    complete = false;
                }

                if (fetchedVersion is not null)
                {
                    version = fetchedVersion;
                    if (!skipCache)
                    {
                        using var cacheScope = NetworkTelemetry.Scope(NetworkTrafficKind.PackageVersionList);
                        CoreCache.Set(
                            VersionCacheCategory,
                            LatestVersionCacheKey(
                                NuGetCache.GetSourceKey(source.Url),
                                normalizedName,
                                includePrerelease),
                            version,
                            extension: "txt");
                    }
                }
            }

            if (version == null)
                continue;

            if (NuGet.Versioning.NuGetVersion.TryParse(version, out var parsed))
            {
                if (bestAny == null || parsed > bestAny)
                {
                    bestAny = parsed;
                    bestAnyOriginal = version;
                    anyReporters.Clear();
                    anyReporters.Add(source);
                }
                else if (parsed == bestAny)
                {
                    AddReporter(anyReporters, source);
                }

                if (!parsed.IsPrerelease
                    && (bestStable == null || parsed > bestStable))
                {
                    bestStable = parsed;
                    bestStableOriginal = version;
                    stableReporters.Clear();
                    stableReporters.Add(source);
                }
                else if (!parsed.IsPrerelease && parsed == bestStable)
                {
                    AddReporter(stableReporters, source);
                }
            }
        }

        string? selected = includePrerelease
            ? bestAnyOriginal
            : bestStableOriginal ?? bestAnyOriginal;
        IReadOnlyList<NuGetSource> selectedReporters =
            includePrerelease || bestStableOriginal is null
                ? anyReporters
                : stableReporters;
        return selected is null
            ? null
            : new PackageVersionResolution(
                selected,
                selectedReporters,
                complete);
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
        => (await ResolveVersionPatternWithSourcesAsync(
            client,
            packageName,
            pattern,
            sources,
            log).ConfigureAwait(false))?.Version;

    internal static async Task<PackageVersionResolution?> ResolveVersionPatternWithSourcesAsync(
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

        var perSource = await FetchListingsPerSourceAsync(
            client,
            normalizedName,
            sources,
            log).ConfigureAwait(false);
        if (perSource is null || perSource.Any(candidate => !candidate.Authoritative))
            return null;

        foreach (var candidate in perSource)
        {
            foreach (var listing in candidate.Listings)
            {
                string ver = listing.Version;
                string? normalizedVersion = NormalizeCandidateVersion(ver);
                if (listing.Listed
                    && normalizedVersion is not null
                    && normalizedVersion.StartsWith(
                        prefix,
                        StringComparison.OrdinalIgnoreCase)
                    && NuGet.Versioning.NuGetVersion.TryParse(
                        normalizedVersion,
                        out var parsed)
                    && (best == null || parsed > best))
                {
                    best = parsed;
                    bestOriginal = normalizedVersion;
                }
            }
        }

        if (bestOriginal is null || best is null)
            return null;

        List<NuGetSource> reporters = [];
        foreach (var candidate in perSource)
        {
            if (candidate.Listings.Any(listing =>
                    listing.Listed
                    && NormalizeCandidateVersion(
                        listing.Version) is string normalizedVersion
                    && normalizedVersion.StartsWith(
                        prefix,
                        StringComparison.OrdinalIgnoreCase)
                    && NuGet.Versioning.NuGetVersion.TryParse(
                        normalizedVersion,
                        out var parsed)
                    && parsed == best))
            {
                AddReporter(reporters, candidate.Source);
            }
        }

        log?.Invoke($"Resolved pattern '{pattern}' to version: {bestOriginal}");
        return new PackageVersionResolution(bestOriginal, reporters);
    }

    /// <summary>
    /// Fetches all version strings for a package from a single source.
    /// </summary>
    private static async Task<SourceVersionList> FetchAllVersionsFromSourceAsync(
        HttpClient client,
        string packageName,
        NuGetSource source,
        Action<string>? log,
        CancellationToken cancellationToken = default)
    {
        using var failureScope = FeedFailureTelemetry.Scope();
        bool attemptedAuthoritativeLookup = false;

        // Try flat-container index first
        if (GetVersionIndexUrl(source.GetFlatContainerUrl(), packageName)
            is { } wellKnownIndexUrl)
        {
            attemptedAuthoritativeLookup = true;
            // nuget.org's well-known flat-container is authoritative for
            // presence. Do not consult the service index after a clean 404, or
            // a later service-index outage would convert absence into failure.
            SourceVersionList versions = await FetchVersionListAsync(
                client,
                wellKnownIndexUrl,
                log,
                NuGetCredentialScope.AuthFor(source, wellKnownIndexUrl, log),
                cancellationToken).ConfigureAwait(false);
            if (versions.Versions is not null || versions.Failed)
                return versions;

            if (source.IsNuGetOrg)
                return SourceVersionList.Absent;
        }

        // Fall back to V3 service index discovery
        int discoveryFailuresBefore =
            FeedFailureTelemetry.Current?.Failures.Count ?? 0;
        ServiceIndexResourceResult baseAddress =
            await GetPackageBaseAddressResultAsync(
            client,
            source,
            log,
            cancellationToken).ConfigureAwait(false);

        if (GetVersionIndexUrl(baseAddress.Id, packageName) is { } indexUrl)
        {
            attemptedAuthoritativeLookup = true;
            SourceVersionList versions = await FetchVersionListAsync(
                client,
                indexUrl,
                log,
                NuGetCredentialScope.AuthFor(source, indexUrl, log),
                cancellationToken).ConfigureAwait(false);
            if (!baseAddress.HasMalformedCriticalResource)
                return versions;

            return versions.Versions is not null
                ? versions with { Failed = true }
                : SourceVersionList.Failure;
        }

        if (baseAddress.HasMalformedCriticalResource)
            return SourceVersionList.Failure;

        if (attemptedAuthoritativeLookup)
            return SourceVersionList.Absent;

        int discoveryFailuresAfter =
            FeedFailureTelemetry.Current?.Failures.Count ?? 0;
        return discoveryFailuresAfter > discoveryFailuresBefore
            ? SourceVersionList.Failure
            : SourceVersionList.MissingSource;
    }

    private static async Task<SourceVersionList> FetchVersionListAsync(
        HttpClient client, string indexUrl, Action<string>? log,
        AuthenticationHeaderValue? auth = null,
        CancellationToken cancellationToken = default)
    {
        log?.Invoke(
            $"Fetching versions from: {UrlRedaction.ForDiagnostics(indexUrl)}");
        int failuresBefore =
            FeedFailureTelemetry.Current?.Failures.Count ?? 0;
        string? json = await HttpRetryHelper.GetStringWithRetryAsync(
            client, indexUrl, auth: auth,
            cancellationToken: cancellationToken,
            trafficKind: NetworkTrafficKind.PackageVersionList).ConfigureAwait(false);
        if (json == null)
        {
            int failuresAfter =
                FeedFailureTelemetry.Current?.Failures.Count ?? 0;
            return failuresAfter > failuresBefore
                ? SourceVersionList.Failure
                : SourceVersionList.Absent;
        }

        try
        {
            using var doc = HardenedJson.Parse(json);
            if (!doc.RootElement.TryGetProperty(
                    "versions",
                    out var versions)
                || versions.ValueKind
                    != System.Text.Json.JsonValueKind.Array)
            {
                FeedFailureTelemetry.Record(
                    indexUrl,
                    HttpStatusCode.OK);
                return SourceVersionList.Failure;
            }

            List<string> result = [];
            foreach (var element in versions.EnumerateArray())
            {
                if (element.ValueKind
                        != System.Text.Json.JsonValueKind.String
                    || NormalizeCandidateVersion(
                        element.GetString()) is not string candidate)
                {
                    FeedFailureTelemetry.Record(
                        indexUrl,
                        HttpStatusCode.OK);
                    return SourceVersionList.Failure;
                }

                result.Add(candidate);
            }

            return result.Count == 0
                ? SourceVersionList.Absent
                : SourceVersionList.Found(result);
        }
        catch (Exception ex) when (ex is
            System.Text.Json.JsonException
            or InvalidOperationException)
        {
            // Ignore parse errors
            FeedFailureTelemetry.Record(
                indexUrl,
                HttpStatusCode.OK);
        }

        return SourceVersionList.Failure;
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
    private static async Task<(
        List<string>? Versions,
        bool Authoritative,
        bool Failed,
        bool SourceMissing)> FetchListedVersionsFromSourceAsync(
        HttpClient client,
        string packageName,
        NuGetSource source,
        Action<string>? log,
        CancellationToken cancellationToken = default)
    {
        SourceVersionList lookup = await FetchAllVersionsFromSourceAsync(
            client,
            packageName,
            source,
            log,
            cancellationToken).ConfigureAwait(false);
        if (lookup.Versions is not { } versions || !source.IsNuGetOrg)
        {
            return (
                lookup.Versions,
                Authoritative: !lookup.Failed,
                lookup.Failed,
                lookup.SourceMissing);
        }

        var registration = await FetchRegistrationVersionsFromNuGetOrgAsync(
            client,
            packageName,
            log,
            cancellationToken).ConfigureAwait(false);
        if (registration == null)
            return (
                versions,
                Authoritative: false,
                Failed: true,
                SourceMissing: false);
        if (!RegistrationCovers(versions, registration.AllVersions))
        {
            FeedFailureTelemetry.Record(
                $"{NuGetOrgRegistrationBase}/{packageName}/index.json",
                HttpStatusCode.OK);
            return (
                versions,
                Authoritative: false,
                Failed: true,
                SourceMissing: false);
        }
        if (registration.UnlistedVersions.Count == 0)
        {
            return (
                versions,
                Authoritative: true,
                Failed: false,
                SourceMissing: false);
        }

        var filtered = versions
            .Where(v => !IsUnlisted(v, registration.UnlistedVersions))
            .ToList();
        int removed = versions.Count - filtered.Count;
        if (removed > 0)
            log?.Invoke($"Excluded {removed} unlisted version(s) from enumeration");
        return (
            filtered,
            Authoritative: true,
            Failed: false,
            SourceMissing: false);
    }

    private static bool IsUnlisted(string version, HashSet<NuGet.Versioning.NuGetVersion> unlisted) =>
        NuGet.Versioning.NuGetVersion.TryParse(version, out var parsed) && unlisted.Contains(parsed);

    private static bool RegistrationCovers(
        IEnumerable<string> versions,
        HashSet<NuGet.Versioning.NuGetVersion> registeredVersions) =>
        versions.All(version =>
            NuGet.Versioning.NuGetVersion.TryParse(version, out var parsed)
            && registeredVersions.Contains(parsed));

    private sealed record NuGetOrgRegistrationVersions(
        HashSet<NuGet.Versioning.NuGetVersion> AllVersions,
        HashSet<NuGet.Versioning.NuGetVersion> UnlistedVersions);

    /// <summary>
    /// Reads the nuget.org registration index and returns every version and the subset whose
    /// <c>catalogEntry.listed</c> is explicitly <c>false</c>. A missing <c>listed</c> property is
    /// treated as listed (older catalog entries omit it). Returns <c>null</c> when the index cannot
    /// be fetched or parsed, so callers fail open (no filtering) rather than dropping real versions.
    /// </summary>
    private static async Task<NuGetOrgRegistrationVersions?> FetchRegistrationVersionsFromNuGetOrgAsync(
        HttpClient client,
        string packageName,
        Action<string>? log,
        CancellationToken cancellationToken = default)
    {
        string indexUrl = $"{NuGetOrgRegistrationBase}/{packageName}/index.json";
        log?.Invoke(
            $"Fetching listing status from: {UrlRedaction.ForDiagnostics(indexUrl)}");

        string? json = await HttpRetryHelper.GetStringWithRetryAsync(
            client, indexUrl,
            cancellationToken: cancellationToken,
            trafficKind: NetworkTrafficKind.PackageMetadata).ConfigureAwait(false);
        if (json == null)
            return null;

        var allVersions = new HashSet<NuGet.Versioning.NuGetVersion>();
        var unlistedVersions = new HashSet<NuGet.Versioning.NuGetVersion>();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("items", out var pages))
            {
                FeedFailureTelemetry.Record(
                    indexUrl,
                    HttpStatusCode.OK);
                return null;
            }

            foreach (var page in pages.EnumerateArray())
            {
                // A page either inlines its entries under "items" or points at a URL that must be
                // fetched separately (registration pages are split for large version histories).
                if (page.TryGetProperty("items", out var inlineItems))
                {
                    CollectRegistrationVersions(
                        inlineItems,
                        allVersions,
                        unlistedVersions);
                }
                else if (page.TryGetProperty("@id", out var pageIdElement)
                    && pageIdElement.GetString() is string pageUrl)
                {
                    string? pageJson = await HttpRetryHelper.GetStringWithRetryAsync(
                        client, pageUrl,
                        cancellationToken: cancellationToken,
                        trafficKind: NetworkTrafficKind.PackageMetadata).ConfigureAwait(false);
                    if (pageJson == null)
                        return null;
                    using var pageDoc = System.Text.Json.JsonDocument.Parse(pageJson);
                    if (!pageDoc.RootElement.TryGetProperty("items", out var pageItems))
                        throw new InvalidOperationException(
                            "Registration page does not contain items.");
                    CollectRegistrationVersions(
                        pageItems,
                        allVersions,
                        unlistedVersions);
                }
                else
                    throw new InvalidOperationException(
                        "Registration page contains neither inline items nor a page URL.");
            }
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
        {
            // Fail open on any malformed or unexpectedly shaped registration document
            // (JsonException = invalid JSON; InvalidOperationException = valid JSON whose
            // shape defies the accessors, e.g. `items` not an array or `version` not a string).
            log?.Invoke($"Could not parse listing status: {ex.Message}");
            FeedFailureTelemetry.Record(
                indexUrl,
                HttpStatusCode.OK);
            return null;
        }

        return new NuGetOrgRegistrationVersions(allVersions, unlistedVersions);
    }

    private static void CollectRegistrationVersions(
        System.Text.Json.JsonElement items,
        HashSet<NuGet.Versioning.NuGetVersion> allVersions,
        HashSet<NuGet.Versioning.NuGetVersion> unlistedVersions)
    {
        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("catalogEntry", out var entry))
                throw new InvalidOperationException(
                    "Registration item does not contain a catalog entry.");
            if (!entry.TryGetProperty("version", out var versionElement)
                || versionElement.GetString() is not string version
                || !NuGet.Versioning.NuGetVersion.TryParse(version, out var parsed))
                throw new InvalidOperationException(
                    "Registration entry does not contain a valid version.");

            allVersions.Add(parsed);

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
            unlistedVersions.Add(parsed);
        }
    }

    /// <summary>
    /// Picks the newest acceptable version, or null when none is acceptable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// With prereleases enabled the answer is the newest version of any kind.
    /// With them disabled it is the newest stable version, falling back to the
    /// newest prerelease when the feed carries no stable release at all.
    /// </para>
    /// <para>
    /// That fallback is deliberate and long-standing CLI behavior: a package
    /// published only as previews — <c>Aspire.OpenAI</c> is the standing
    /// example — is still the package the user named, and refusing to resolve
    /// it would turn <c>dotnet inspect package Aspire.OpenAI</c> into an error
    /// for every preview-only library. The stricter "stable or nothing" rule is
    /// a workspace acquisition policy, not a property of this shared helper;
    /// <see cref="PackageCoordinateResolver"/> owns it and applies it to its own
    /// answers, so tightening it here would change every legacy caller instead.
    /// </para>
    /// </remarks>
    private static string? PickLatest(IEnumerable<string?> versions, bool includePrerelease)
    {
        NuGet.Versioning.NuGetVersion? latestStable = null;
        NuGet.Versioning.NuGetVersion? latestAny = null;
        foreach (var ver in versions)
        {
            string? normalizedVersion = NormalizeCandidateVersion(ver);
            if (normalizedVersion is not null
                && NuGet.Versioning.NuGetVersion.TryParse(
                    normalizedVersion,
                    out var parsed))
            {
                if (latestAny == null || parsed > latestAny)
                    latestAny = parsed;
                if (!parsed.IsPrerelease && (latestStable == null || parsed > latestStable))
                    latestStable = parsed;
            }
        }
        return (includePrerelease
            ? latestAny
            : latestStable ?? latestAny)?.ToNormalizedString();
    }

    private static string? NormalizeCandidateVersion(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)
            || !string.Equals(
                candidate,
                candidate.Trim(),
                StringComparison.Ordinal)
            || !NuGet.Versioning.NuGetVersion.TryParse(
                candidate,
                out var parsed))
        {
            return null;
        }

        return parsed.ToNormalizedString();
    }

    /// <summary>
    /// One source's answer to "what is the latest version?", distinguishing a
    /// proven absence from a source that never produced an authoritative answer.
    /// </summary>
    readonly record struct SourceLatestVersion(string? Version, bool Failed)
    {
        internal static SourceLatestVersion Found(string version) => new(version, Failed: false);

        internal static SourceLatestVersion Absent { get; } = new(null, Failed: false);

        internal static SourceLatestVersion Failure { get; } = new(null, Failed: true);
    }

    private static async Task<SourceLatestVersion> GetLatestVersionFromSourceAsync(
        HttpClient client,
        string packageName,
        NuGetSource source,
        Action<string>? log,
        bool includePrerelease,
        CancellationToken cancellationToken)
    {
        using var failureScope = FeedFailureTelemetry.Scope();

        // For nuget.org, use the search API — returns latest version directly without listing all versions.
        // Throwaway scope: search is best-effort before the authoritative flat-container
        // / registration path. A 500 here must not merge into the parent and convert a
        // later authoritative 404 into "source did not answer".
        if (source.IsNuGetOrg && !includePrerelease)
        {
            using (FeedFailureTelemetry.Scope(mergeIntoParent: false))
            {
                var version = await GetLatestVersionFromSearchAsync(
                    client,
                    packageName,
                    log,
                    cancellationToken).ConfigureAwait(false);
                if (version != null)
                    return SourceLatestVersion.Found(version);
            }
        }

        // For nuget.org, the flat-container/prerelease path must exclude unlisted versions.
        // FetchListedVersionsFromSourceAsync consults the registration index; only an
        // authoritative result (registration index read successfully) is trustworthy. If the
        // registration index is unavailable the method fails open to an UNFILTERED list flagged
        // Authoritative=false; picking a "latest" from that could surface an unlisted version, so
        // return failure (couldn't determine a latest) instead of falling through to the unfiltered
        // index below.
        if (source.IsNuGetOrg)
        {
            var (listed, authoritative, failed, _) =
                await FetchListedVersionsFromSourceAsync(
                    client,
                    packageName,
                    source,
                    log,
                    cancellationToken).ConfigureAwait(false);
            if (listed != null && authoritative)
            {
                return PickLatest(listed, includePrerelease) is { } picked
                    ? SourceLatestVersion.Found(picked)
                    : SourceLatestVersion.Absent;
            }

            if (listed is null && authoritative)
            {
                return failed
                    ? SourceLatestVersion.Failure
                    : SourceLatestVersion.Absent;
            }

            return SourceLatestVersion.Failure;
        }

        bool attemptedAuthoritativeLookup = false;

        // Fall back to flat-container index (enumerates all versions)
        if (GetVersionIndexUrl(source.GetFlatContainerUrl(), packageName)
            is { } wellKnownIndexUrl)
        {
            log?.Invoke(
                $"Fetching versions from: {UrlRedaction.ForDiagnostics(wellKnownIndexUrl)}");

            attemptedAuthoritativeLookup = true;
            var wellKnown = await ParseVersionIndexResultAsync(
                client,
                wellKnownIndexUrl,
                NuGetCredentialScope.AuthFor(source, wellKnownIndexUrl, log),
                includePrerelease,
                cancellationToken).ConfigureAwait(false);
            if (wellKnown.Version is not null || wellKnown.Failed)
                return wellKnown;
        }

        // Fall back to V3 service index discovery
        ServiceIndexResourceResult baseAddress =
            await GetPackageBaseAddressResultAsync(
                client,
                source,
                log,
                cancellationToken).ConfigureAwait(false);
        if (baseAddress.HasMalformedCriticalResource)
            return SourceLatestVersion.Failure;

        if (GetVersionIndexUrl(baseAddress.Id, packageName) is { } indexUrl)
        {
            log?.Invoke(
                $"Fetching versions from: {UrlRedaction.ForDiagnostics(indexUrl)}");

            attemptedAuthoritativeLookup = true;
            var discovered = await ParseVersionIndexResultAsync(
                client,
                indexUrl,
                NuGetCredentialScope.AuthFor(source, indexUrl, log),
                includePrerelease,
                cancellationToken).ConfigureAwait(false);
            if (discovered.Version is not null || discovered.Failed)
                return discovered;
        }

        // Never found a version index URL to query (missing/malformed critical
        // PackageBaseAddress, etc.). Attempted lookups already returned their
        // own typed Absent/Failed above without consulting ambient leftovers.
        if (!attemptedAuthoritativeLookup)
            return SourceLatestVersion.Failure;

        return SourceLatestVersion.Absent;
    }

    private static async Task<string?> GetLatestVersionFromSearchAsync(
        HttpClient client,
        string packageName,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        string searchUrl = $"https://azuresearch-usnc.nuget.org/query?q=packageid:{packageName}&take=1&prerelease=false";
        log?.Invoke(
            $"Fetching latest version from: {UrlRedaction.ForDiagnostics(searchUrl)}");

        try
        {
            string? json = await HttpRetryHelper.GetStringWithRetryAsync(
                client, searchUrl,
                cancellationToken: cancellationToken,
                trafficKind: NetworkTrafficKind.PackageSearch).ConfigureAwait(false);
            if (json == null)
                return null;

            using var doc = HardenedJson.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Array)
            {
                FeedFailureTelemetry.Record(
                    searchUrl,
                    HttpStatusCode.OK);
                return null;
            }

            if (data.GetArrayLength() == 0)
                return null;

            var package = data[0];
            if (package.TryGetProperty("version", out var version)
                && NormalizeCandidateVersion(
                    version.GetString()) is { } candidate)
            {
                return candidate;
            }

            FeedFailureTelemetry.Record(
                searchUrl,
                HttpStatusCode.OK);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            log?.Invoke($"Search API failed: {ex.Message}");
            FeedFailureTelemetry.Record(
                searchUrl,
                HttpStatusCode.OK);
        }

        return null;
    }

    private static async Task<string?> ParseVersionIndexAsync(
        HttpClient client, string indexUrl,
        AuthenticationHeaderValue? auth = null,
        bool includePrerelease = false,
        CancellationToken cancellationToken = default)
    {
        SourceLatestVersion result = await ParseVersionIndexResultAsync(
            client,
            indexUrl,
            auth,
            includePrerelease,
            cancellationToken).ConfigureAwait(false);
        return result.Version;
    }

    private static async Task<SourceLatestVersion> ParseVersionIndexResultAsync(
        HttpClient client,
        string indexUrl,
        AuthenticationHeaderValue? auth = null,
        bool includePrerelease = false,
        CancellationToken cancellationToken = default)
    {
        // Attribute transport failure to *this* request only. Ambient
        // HasFailures can include an earlier optional resource warning or a
        // superseded sibling path; those must not turn a plain 404 into Failed.
        int failuresBefore =
            FeedFailureTelemetry.Current?.Failures.Count ?? 0;
        string? json = await HttpRetryHelper.GetStringWithRetryAsync(
            client, indexUrl, auth: auth,
            cancellationToken: cancellationToken,
            trafficKind: NetworkTrafficKind.PackageVersionList).ConfigureAwait(false);
        if (json == null)
        {
            int failuresAfter =
                FeedFailureTelemetry.Current?.Failures.Count ?? 0;
            return failuresAfter > failuresBefore
                ? SourceLatestVersion.Failure
                : SourceLatestVersion.Absent;
        }

        try
        {
            using var doc = HardenedJson.Parse(json);
            if (!doc.RootElement.TryGetProperty(
                    "versions",
                    out var versions)
                || versions.ValueKind
                    != System.Text.Json.JsonValueKind.Array)
            {
                FeedFailureTelemetry.Record(
                    indexUrl,
                    HttpStatusCode.OK);
                return SourceLatestVersion.Failure;
            }

            if (versions.GetArrayLength() == 0)
                return SourceLatestVersion.Absent;

            var candidates = new List<string>();
            foreach (var element in versions.EnumerateArray())
            {
                if (element.ValueKind
                        != System.Text.Json.JsonValueKind.String
                    || NormalizeCandidateVersion(
                        element.GetString()) is not string candidate)
                {
                    FeedFailureTelemetry.Record(
                        indexUrl,
                        HttpStatusCode.OK);
                    return SourceLatestVersion.Failure;
                }

                candidates.Add(candidate);
            }

            // Use NuGetVersion for proper comparison — feeds may return
            // versions in any order (nuget.org ascending, Azure DevOps descending).
            return PickLatest(candidates, includePrerelease) is { } picked
                ? SourceLatestVersion.Found(picked)
                : SourceLatestVersion.Absent;
        }
        catch (Exception ex) when (ex is
            System.Text.Json.JsonException
            or InvalidOperationException)
        {
            FeedFailureTelemetry.Record(
                indexUrl,
                HttpStatusCode.OK);
            return SourceLatestVersion.Failure;
        }
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
        var sources = NuGetSourceResolver.ResolveSourcesForPackage(
            sourceOptions,
            packageName);

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
    /// Checks whether an exact package version appears in an authoritative
    /// version index. Returns <see langword="null"/> when an eligible HTTP
    /// source fails and no source reports the version.
    /// </summary>
    public static async Task<bool?> PackageVersionExistsAsync(
        HttpClient client,
        string packageName,
        string version,
        Action<string>? log,
        NuGetSourceOptions? sourceOptions = null)
    {
        string normalizedName = packageName.ToLowerInvariant();
        List<NuGetSource> sources =
            NuGetSourceResolver.ResolveSourcesForPackage(
                sourceOptions,
                packageName);
        if (sources.Count == 0)
            return null;

        string? normalizedVersion = NormalizeCandidateVersion(version);
        if (normalizedVersion is null)
            return null;
        if (HttpClientFactory.IsOffline)
            return null;

        List<SourceVersionListings>? perSource =
            await FetchListingsPerSourceAsync(
                client,
                normalizedName,
                sources,
                log,
                requireCompleteSources: true).ConfigureAwait(false);
        if (perSource is null)
            return false;

        bool incomplete = false;
        foreach (SourceVersionListings source in perSource)
        {
            if (source.Listings.Any(candidate =>
                    string.Equals(
                        candidate.Version,
                        normalizedVersion,
                        StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            if (!source.Authoritative)
                incomplete = true;
        }

        return incomplete ? null : false;
    }

    /// <summary>
    /// Resolves the newest listed version using matching-flavor latest entries
    /// where available and strict listing semantics for uncached sources.
    /// Returns an empty list when source metadata exists but has no matching
    /// listed version, and <see langword="null"/> when no source can answer.
    /// </summary>
    public static async Task<List<string>?> GetSingleVersionListingAsync(
        HttpClient client,
        string packageName,
        bool includePrerelease,
        Action<string>? log,
        NuGetSourceOptions? sourceOptions = null)
    {
        string normalizedName = packageName.ToLowerInvariant();
        List<NuGetSource> sources =
            NuGetSourceResolver.ResolveSourcesForPackage(
                sourceOptions,
                packageName);
        NuGet.Versioning.NuGetVersion? best = null;
        string? bestVersion = null;
        bool sawMetadata = false;

        foreach (NuGetSource source in sources)
        {
            string? candidate = TryGetCachedLatestEntryForSource(
                normalizedName,
                NuGetCache.GetSourceKey(source.Url),
                includePrerelease);
            if (candidate is not null)
            {
                sawMetadata = true;
                Consider(candidate);
                continue;
            }

            List<SourceVersionListings>? perSource =
                await FetchListingsPerSourceAsync(
                    client,
                    normalizedName,
                    [source],
                    log).ConfigureAwait(false);
            if (perSource is null)
                continue;
            if (!perSource[0].Authoritative)
                return null;

            sawMetadata = true;
            foreach (PackageVersionInfo listing in perSource[0].Listings)
            {
                if (listing.Listed)
                    Consider(listing.Version);
            }
        }

        return bestVersion is not null
            ? [bestVersion]
            : sawMetadata ? [] : null;

        void Consider(string version)
        {
            string? normalized = NormalizeCandidateVersion(version);
            if (normalized is null
                || !NuGet.Versioning.NuGetVersion.TryParse(
                    normalized,
                    out var parsed)
                || (!includePrerelease && parsed.IsPrerelease)
                || (best is not null && parsed <= best))
            {
                return;
            }

            best = parsed;
            bestVersion = normalized;
        }
    }

    internal static async Task<(
        List<PackageVersionResolution>? Candidates,
        bool HasIncompleteMetadata)> GetVersionCandidatesAsync(
        HttpClient client,
        string packageName,
        bool includePrerelease,
        Action<string>? log,
        NuGetSourceOptions? sourceOptions = null)
    {
        List<NuGetSource> sources =
            NuGetSourceResolver.ResolveSourcesForPackage(
                sourceOptions,
                packageName);
        return await GetVersionCandidatesAsync(
            client,
            packageName,
            sources,
            includePrerelease,
            log,
            useCache: true,
            requireCompleteSources: false,
            cancellationToken: default).ConfigureAwait(false);
    }

    internal static async Task<(
        List<PackageVersionResolution>? Candidates,
        bool HasIncompleteMetadata)> GetVersionCandidatesAsync(
        HttpClient client,
        string packageName,
        IReadOnlyList<NuGetSource> sources,
        bool includePrerelease,
        Action<string>? log,
        bool useCache,
        bool requireCompleteSources,
        CancellationToken cancellationToken)
    {
        string normalizedName = packageName.ToLowerInvariant();
        var perSource = await FetchListingsPerSourceAsync(
            client,
            normalizedName,
            [.. sources],
            log,
            useCache,
            cancellationToken,
            requireCompleteSources).ConfigureAwait(false);
        if (perSource is null)
            return (null, HasIncompleteMetadata: false);
        if (perSource.Any(candidate => !candidate.Authoritative))
        {
            bool hasAuthoritativeEvidence =
                perSource.Any(candidate => candidate.Authoritative);
            bool hasHardFailure = perSource.Any(candidate =>
                !candidate.Authoritative
                && !candidate.SourceMissing);
            return (
                null,
                HasIncompleteMetadata:
                    hasAuthoritativeEvidence || hasHardFailure);
        }

        var candidates = new Dictionary<
            string,
            (NuGet.Versioning.NuGetVersion Parsed, string Original, List<NuGetSource> Reporters)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in perSource)
        {
            foreach (var listing in candidate.Listings)
            {
                if (!listing.Listed
                    || (!includePrerelease
                        && listing.Version.Contains(
                            '-',
                            StringComparison.Ordinal))
                    || NormalizeCandidateVersion(
                        listing.Version) is not string identity
                    || !NuGet.Versioning.NuGetVersion.TryParse(
                        identity,
                        out var parsed))
                {
                    continue;
                }

                if (!candidates.TryGetValue(identity, out var existing))
                {
                    existing = (parsed, identity, []);
                }

                AddReporter(existing.Reporters, candidate.Source);
                candidates[identity] = existing;
            }
        }

        return (
            [
                .. candidates.Values
                    .OrderBy(candidate => candidate.Parsed)
                    .Select(candidate => new PackageVersionResolution(
                        candidate.Original,
                        candidate.Reporters)),
            ],
            HasIncompleteMetadata: false);
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
        NuGetSourceOptions? sourceOptions = null,
        bool useVersionCache = true)
    {
        string normalizedName = packageName.ToLowerInvariant();
        var sources = NuGetSourceResolver.ResolveSourcesForPackage(
            sourceOptions,
            packageName);

        var allListings = await GetAllVersionListingsWithCacheAsync(
            client,
            normalizedName,
            sources,
            log,
            useVersionCache).ConfigureAwait(false);
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
        var merged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool authoritative = true;

        var perSource = await FetchListingsPerSourceAsync(
            client,
            normalizedName,
            sources,
            log).ConfigureAwait(false);
        if (perSource is null)
            return (null, authoritative);

        foreach (var candidate in perSource)
        {
            if (!candidate.Authoritative)
                authoritative = false;

            merged.UnionWith(
                candidate.Listings
                    .Where(listing => listing.Listed)
                    .Select(listing => listing.Version));
        }

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

    /// <summary>
    /// Lists versions together with the feed each one came from, newest version first.
    /// </summary>
    /// <remarks>
    /// A version carried by more than one feed produces one row per feed, in source order, so that
    /// duplication across feeds is visible rather than silently collapsed. This is the only way to
    /// see that two feeds both publish a given version, which matters because the rest of the tool
    /// identifies a package by name and version alone.
    /// <para>
    /// Listing status is applied per feed rather than merged, so a version unlisted on nuget.org
    /// but also carried by a private feed is hidden for the nuget.org row and shown for the private
    /// one. The merged views cannot express that split and report such a version as listed.
    /// </para>
    /// </remarks>
    /// <param name="limit">Maximum number of distinct versions, not rows. A limit of 3 returns the
    /// newest three versions along with every feed that carries them.</param>
    public static async Task<List<PackageVersionSourceInfo>?> GetVersionListingsWithSourceAsync(
        HttpClient client, string packageName, bool includePrerelease, bool includeUnlisted,
        int? limit, Action<string>? log,
        NuGetSourceOptions? sourceOptions = null,
        bool useCache = true)
    {
        string normalizedName = packageName.ToLowerInvariant();
        var sources = NuGetSourceResolver.ResolveSourcesForPackage(
            sourceOptions,
            packageName);

        var perSource = await FetchListingsPerSourceAsync(
            client, normalizedName, sources, log, useCache).ConfigureAwait(false);
        if (perSource == null)
            return null;

        // Feeds that carry each version, keeping source order and dropping duplicates within a
        // single feed. Identity is the source URL, because a source requested on the command line
        // that matches nothing in configuration is named "explicit", and every such source shares
        // that name — two of them would otherwise collapse into one row.
        var labels = BuildFeedLabels(perSource.Select(p => p.Source).ToList());
        var rowsByVersion = new Dictionary<string, List<PackageVersionSourceInfo>>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in perSource)
        {
            NuGetSource source = candidate.Source;
            string label = labels[source.Url];
            foreach (var listing in candidate.Listings)
            {
                if (!includePrerelease && listing.Version.Contains('-', StringComparison.Ordinal))
                    continue;
                if (!includeUnlisted && !listing.Listed)
                    continue;

                if (!rowsByVersion.TryGetValue(listing.Version, out var rows))
                {
                    rows = [];
                    rowsByVersion[listing.Version] = rows;
                }

                if (!rows.Any(r => string.Equals(r.Feed, label, StringComparison.Ordinal)))
                    rows.Add(new PackageVersionSourceInfo(listing.Version, label, listing.Listed));
            }
        }

        var parseable = new List<(NuGet.Versioning.NuGetVersion Parsed, string Original)>();
        var unparseable = new List<string>();
        foreach (string version in rowsByVersion.Keys)
        {
            if (NuGet.Versioning.NuGetVersion.TryParse(version, out var parsed))
                parseable.Add((parsed, version));
            else
                unparseable.Add(version);
        }

        parseable.Sort((a, b) => b.Parsed.CompareTo(a.Parsed));

        List<PackageVersionSourceInfo> result = [];
        int versionCount = 0;
        foreach (string version in parseable.Select(p => p.Original).Concat(unparseable))
        {
            if (limit.HasValue && versionCount >= limit.Value)
                break;

            versionCount++;
            result.AddRange(rowsByVersion[version]);
        }

        return result;
    }

    /// <summary>
    /// Chooses a short, unambiguous label for each source, keyed by source URL.
    /// </summary>
    /// <remarks>
    /// Configured sources have useful names, while a source named on the command line that matches
    /// nothing in configuration uses its URL as the mapping alias. A label must stay short and
    /// distinguish feeds, so a configured name is used when present and a
    /// redacted URL-shaped alias is used otherwise. Collisions gain a short
    /// credential-free producer-key suffix rather than exposing the raw URL.
    /// </remarks>
    private static Dictionary<string, string> BuildFeedLabels(List<NuGetSource> sources)
    {
        static string Candidate(NuGetSource source)
        {
            if (!string.IsNullOrEmpty(source.Name)
                && !string.Equals(source.Name, source.Url, StringComparison.Ordinal))
            {
                return PackageSourceDisplay.ForDiagnostics(source).ToString();
            }

            if (source.IsNuGetOrg)
                return "nuget.org";

            return Uri.TryCreate(
                    source.Url,
                    UriKind.Absolute,
                    out Uri? uri)
                ? new InertString(TextPolicy.Field, uri.Host).ToString()
                : PackageSourceDisplay.ForDiagnostics(source).ToString();
        }

        var byUrl = new Dictionary<string, string>(StringComparer.Ordinal);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var source in sources)
        {
            if (byUrl.ContainsKey(source.Url))
                continue;

            string candidate = Candidate(source);
            byUrl[source.Url] = candidate;
            counts[candidate] = counts.GetValueOrDefault(candidate) + 1;
        }

        foreach (var source in sources)
        {
            if (byUrl.TryGetValue(source.Url, out var candidate) && counts[candidate] > 1)
            {
                string sourceKey = NuGetCache.GetSourceKey(source.Url);
                byUrl[source.Url] = $"{candidate} [{sourceKey[..8]}]";
            }
        }

        return byUrl;
    }

    /// <summary>
    /// Runs the per-source listing fetch once, preserving which source produced
    /// each list. Every authoritative list is cached under its canonical source
    /// identity and package id. Callers that only need the union merge the
    /// result; callers that authorize payloads retain its provenance.
    /// </summary>
    /// <returns>
    /// One entry per source that produced a list, in source order, or
    /// <see langword="null"/> when no source carried the package at all.
    /// Complete-source callers also retain a failed source as a
    /// non-authoritative empty entry.
    /// </returns>
    private static async Task<List<SourceVersionListings>?> FetchListingsPerSourceAsync(
        HttpClient client,
        string normalizedName,
        List<NuGetSource> sources,
        Action<string>? log,
        bool useCache = true,
        CancellationToken cancellationToken = default,
        bool requireCompleteSources = false)
    {
        var perSource = new List<SourceVersionListings>();

        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsHttpSource(source))
            {
                log?.Invoke(
                    $"Skipping non-HTTP NuGet source '{PackageSourceDisplay.ForDiagnostics(source)}': {UrlRedaction.ForDiagnostics(source.Url)}");
                continue;
            }

            List<PackageVersionInfo>? listings = null;
            bool fetchedAuthoritative = false;
            bool fetchedFailed = false;
            bool fetchedSourceMissing = false;
            bool fromCache = false;
            string cacheKey = ListingsVersionCacheKey(
                NuGetCache.GetSourceKey(source.Url),
                normalizedName);

            string? cached = useCache
                ? CoreCache.TryGet(
                    VersionCacheCategory,
                    cacheKey,
                    VersionCacheTtl,
                    extension: "txt")
                : null;
            if (cached is not null)
            {
                listings = DeserializeListings(cached);
                if (listings is not null)
                {
                    log?.Invoke(
                        $"Using cached version listings from {PackageSourceDisplay.ForDiagnostics(source)}");
                    fromCache = true;
                }
            }

            if (listings == null)
            {
                (
                    listings,
                    fetchedAuthoritative,
                    fetchedFailed,
                    fetchedSourceMissing) =
                    await FetchVersionListingsFromSourceAsync(
                        client,
                        normalizedName,
                        source,
                        log,
                        cancellationToken).ConfigureAwait(false);
            }
            if (listings == null)
            {
                if (requireCompleteSources
                    && (fetchedFailed || fetchedSourceMissing))
                {
                    perSource.Add(new SourceVersionListings(
                        source,
                        [],
                        Authoritative: false,
                        fetchedSourceMissing));
                }
                continue;
            }

            bool authoritative = fromCache || fetchedAuthoritative;
            perSource.Add(new SourceVersionListings(
                source,
                listings,
                authoritative));

            if (useCache && !fromCache && fetchedAuthoritative)
            {
                CoreCache.Set(
                    VersionCacheCategory,
                    cacheKey,
                    SerializeListings(listings),
                    extension: "txt");
            }
        }

        return perSource.Count == 0 ? null : perSource;
    }

    /// <summary>
    /// Produces the full annotated version list (listed and unlisted) across
    /// source-scoped candidate caches, ascending by SemVer. Mirrors
    /// <see cref="GetAllVersionsWithCacheAsync"/> but carries the listing bit. A
    /// version listed on any source is reported as listed.
    /// </summary>
    private static async Task<List<PackageVersionInfo>?> GetAllVersionListingsWithCacheAsync(
        HttpClient client,
        string normalizedName,
        List<NuGetSource> sources,
        Action<string>? log,
        bool useVersionCache)
    {
        var perSource = await FetchListingsPerSourceAsync(
            client,
            normalizedName,
            sources,
            log,
            useVersionCache).ConfigureAwait(false);
        if (perSource == null)
            return null;

        var merged = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in perSource)
        {
            foreach (var listing in candidate.Listings)
            {
                // A version listed on any source counts as listed.
                merged[listing.Version] = merged.TryGetValue(listing.Version, out var existing)
                    ? existing || listing.Listed
                    : listing.Listed;
            }
        }

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
    private static async Task<(
        List<PackageVersionInfo>? Listings,
        bool Authoritative,
        bool Failed,
        bool SourceMissing)> FetchVersionListingsFromSourceAsync(
        HttpClient client,
        string packageName,
        NuGetSource source,
        Action<string>? log,
        CancellationToken cancellationToken = default)
    {
        SourceVersionList lookup = await FetchAllVersionsFromSourceAsync(
            client,
            packageName,
            source,
            log,
            cancellationToken).ConfigureAwait(false);
        if (lookup.Versions is not { } versions)
        {
            return (
                null,
                Authoritative: true,
                lookup.Failed,
                lookup.SourceMissing);
        }

        NuGetOrgRegistrationVersions? registration = source.IsNuGetOrg
            ? await FetchRegistrationVersionsFromNuGetOrgAsync(
                client,
                packageName,
                log,
                cancellationToken).ConfigureAwait(false)
            : null;

        bool authoritative = !lookup.Failed
            && (!source.IsNuGetOrg
            || registration is not null
                && RegistrationCovers(versions, registration.AllVersions));
        var listings = versions
            .Select(v => new PackageVersionInfo(
                v,
                registration == null
                    || !IsUnlisted(v, registration.UnlistedVersions)))
            .ToList();
        return (
            listings,
            authoritative,
            Failed: lookup.Failed || !authoritative,
            SourceMissing: false);
    }

    // Every line carries an explicit two-character tab suffix. The versioned
    // cache rejects incomplete or malformed snapshots rather than treating a
    // partially published file as authoritative candidate metadata.
    private static string SerializeListings(IEnumerable<PackageVersionInfo> listings) =>
        string.Join('\n', listings.Select(l => l.Listed ? $"{l.Version}\tL" : $"{l.Version}\tU"));

    private static List<PackageVersionInfo>? DeserializeListings(string cached)
    {
        if (string.IsNullOrWhiteSpace(cached))
            return null;

        List<PackageVersionInfo> result = [];
        foreach (var line in cached.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            bool listed;
            if (line.EndsWith("\tU", StringComparison.Ordinal))
                listed = false;
            else if (line.EndsWith("\tL", StringComparison.Ordinal))
                listed = true;
            else
                return null;

            string? version = NormalizeCandidateVersion(line[..^2]);
            if (version is null)
                return null;

            result.Add(new PackageVersionInfo(version, listed));
        }

        return result.Count == 0 ? null : result;
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
