using System.Net;
using DotnetInspector.Core;
using InertText;

namespace DotnetInspector.Packages;

/// <summary>
/// Result of downloading a PDB file (no SRM types).
/// </summary>
public record PdbDownloadResult(
    string? PdbFilePath,
    bool WindowsPdbDetected = false,
    string? SymbolServer = null
);

/// <summary>
/// Repeatable access to one acquired Portable PDB payload.
/// </summary>
/// <remarks>
/// The backing store owns persistence. <see cref="OpenReadAsync"/> returns a
/// fresh readable, seekable stream positioned at zero; the caller owns that
/// stream. <see cref="LocalPath"/> is available only when the store is
/// filesystem-backed.
/// </remarks>
public sealed class AcquiredPortablePdb
{
    private readonly IPdbStore _store;
    private readonly string _storeKey;

    internal AcquiredPortablePdb(
        IPdbStore store,
        string storeKey,
        string symbolServer,
        bool fromCache)
    {
        _store = store;
        _storeKey = storeKey;
        SymbolServer = symbolServer;
        FromCache = fromCache;
    }

    public string SymbolServer { get; }
    public bool FromCache { get; }
    public string? LocalPath => _store.TryGetLocalPath(_storeKey);

    public async ValueTask<Stream> OpenReadAsync(
        CancellationToken cancellationToken = default)
    {
        Stream? stream =
            await _store.TryOpenAsync(
                _storeKey,
                cancellationToken).ConfigureAwait(false);
        if (stream is null)
        {
            throw new IOException(
                "The acquired Portable PDB content is no longer available.");
        }

        if (!stream.CanRead || !stream.CanSeek)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw new IOException(
                "The acquired Portable PDB store returned an unreadable or non-seekable stream.");
        }

        stream.Position = 0;
        return stream;
    }
}

/// <summary>The typed result of content-shaped Portable PDB acquisition.</summary>
public abstract record PortablePdbAcquisitionResult
{
    private protected PortablePdbAcquisitionResult(
        bool windowsPdbDetected)
        => WindowsPdbDetected = windowsPdbDetected;

    public bool WindowsPdbDetected { get; }

    public sealed record Acquired : PortablePdbAcquisitionResult
    {
        internal Acquired(
            AcquiredPortablePdb pdb,
            bool windowsPdbDetected)
            : base(windowsPdbDetected)
            => Pdb = pdb;

        public AcquiredPortablePdb Pdb { get; }
    }

    public sealed record Unavailable : PortablePdbAcquisitionResult
    {
        internal Unavailable(bool windowsPdbDetected)
            : base(windowsPdbDetected)
        {
        }
    }
}

/// <summary>
/// Downloads and manages symbol packages (.snupkg) from NuGet for SourceLink resolution.
/// Only supports Portable PDBs (embedded or standalone) and snupkg files.
/// </summary>
/// <remarks>
/// <para>
/// Transport is injectable via <see cref="HttpClient"/> and persistence via
/// <see cref="IPdbStore"/>. The host-neutral snupkg parsing lives in
/// <see cref="SnupkgPdbReader"/>. <see cref="AcquirePdbAsync"/> returns
/// repeatable content for either filesystem or in-memory stores;
/// <see cref="DownloadPdbAsync"/> is the desktop compatibility projection that
/// returns only an on-disk path.
/// </para>
/// <para>
/// Symbol server key generation follows the conventions from dotnet/symstore:
/// https://github.com/dotnet/symstore/blob/d66992e7c2f32288fbf1acf08cdea43098025c7c/src/Microsoft.SymbolStore/KeyGenerators/PortablePDBFileKeyGenerator.cs
/// Portable PDBs use {GUID}FFFFFFFF, Windows PDBs use {GUID}{age:x}.
/// </para>
/// </remarks>
public class SymbolPackageDownloader
{
    private const string SymbolMissCacheCategory = "symbol-misses";
    private static readonly TimeSpan SymbolMissCacheTtl = TimeSpan.FromDays(1);
    private static readonly TimeSpan SymbolForbiddenCacheTtl = TimeSpan.FromDays(7);
    private readonly HttpClient _client;
    private readonly IPdbStore _pdbStore;
    private readonly IPackageSourceAuthorization? _sourceAuthorization;
    private readonly bool _usePersistentMissCache;

    /// <summary>
    /// Creates a downloader backed by the default filesystem PDB cache
    /// (<c>{app-cache}/packages/symbols</c>).
    /// </summary>
    public SymbolPackageDownloader(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _pdbStore = FileSystemPdbStore.CreateDefault();
        _sourceAuthorization = null;
        _usePersistentMissCache = true;
    }

    /// <summary>
    /// Creates a downloader with an explicit <see cref="IPdbStore"/> for PDB
    /// persistence while retaining the desktop's ambient package-source policy.
    /// </summary>
    /// <remarks>
    /// Persistent negative-result caching is disabled by default for an
    /// explicit store. Set
    /// <paramref name="usePersistentMissCache"/> only when the host has
    /// initialized that disk cache.
    /// </remarks>
    public SymbolPackageDownloader(
        HttpClient client,
        IPdbStore pdbStore,
        bool usePersistentMissCache = false)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(pdbStore);
        _client = client;
        _pdbStore = pdbStore;
        _sourceAuthorization = null;
        _usePersistentMissCache = usePersistentMissCache;
    }

    /// <summary>
    /// Creates a host-neutral downloader with explicit PDB persistence and
    /// package-source authorization.
    /// </summary>
    /// <remarks>
    /// This overload performs no ambient NuGet configuration discovery.
    /// Persistent negative-result caching remains disabled unless explicitly
    /// enabled. <c>SymbolPackageDownloaderTests.AcquirePdbAsync_ExplicitStore_DoesNotUseAmbientCaches</c>
    /// gates both filesystem-free defaults.
    /// </remarks>
    public SymbolPackageDownloader(
        HttpClient client,
        IPdbStore pdbStore,
        IPackageSourceAuthorization sourceAuthorization,
        bool usePersistentMissCache = false)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(pdbStore);
        ArgumentNullException.ThrowIfNull(sourceAuthorization);
        _client = client;
        _pdbStore = pdbStore;
        _sourceAuthorization = sourceAuthorization;
        _usePersistentMissCache = usePersistentMissCache;
    }

    /// <summary>
    /// Acquires a Portable PDB through the configured store without requiring a
    /// filesystem path.
    /// </summary>
    public async Task<PortablePdbAcquisitionResult> AcquirePdbAsync(
        Guid pdbGuid, int pdbAge, string pdbFileName, bool isPortable,
        string? assemblyName = null,
        string? packageName = null,
        string? packageVersion = null,
        Action<string>? log = null,
        bool isPlatformAssembly = false,
        bool cacheOnly = false,
        NuGetSourceOptions? sourceOptions = null,
        CancellationToken cancellationToken = default,
        uint? portablePdbStamp = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool windowsPdbDetected = false;

        pdbFileName = GetSymbolFileName(pdbFileName);
        // The PDB file name comes from untrusted PE debug metadata. Only the
        // symbol-server paths (MSDL and the NuGet symbol server) use it to build
        // a cache key and request URL; snupkg acquisition is keyed off the
        // assembly name and debug GUID instead. If the name is not a usable
        // single path segment, skip only those symbol-server paths rather than
        // abandoning snupkg recovery, and never let an invalid store key throw.
        bool pdbFileNameUsable = StorePath.IsSafeSegment(pdbFileName);
        if (!pdbFileNameUsable)
            log?.Invoke("Unusable PDB file name; skipping symbol-server paths");
        string? snupkgAssemblyName =
            GetSnupkgAssemblyName(
                pdbFileName,
                pdbFileNameUsable,
                assemblyName,
                log);

        var guid = pdbGuid.ToString("N").ToUpperInvariant();
        var symbolKey = isPortable
            ? $"{guid}FFFFFFFF"
            : $"{guid}{pdbAge:x}";

        // For Microsoft packages or platform assemblies, try MSDL first
        bool isMicrosoftPackage = isPlatformAssembly || IsMicrosoftPackage(packageName);
        if (isMicrosoftPackage && pdbFileNameUsable)
        {
            log?.Invoke(isPlatformAssembly ? "Platform library, trying MSDL symbol server" : "Microsoft package detected, trying MSDL symbol server first");
            var msdlResult = await TryLocateFromMsdlAsync(
                pdbFileName, symbolKey, pdbGuid, portablePdbStamp,
                isPortable, log, cacheOnly, cancellationToken).ConfigureAwait(false);
            if (msdlResult.Pdb is not null)
            {
                return new PortablePdbAcquisitionResult.Acquired(
                    msdlResult.Pdb,
                    windowsPdbDetected || msdlResult.WindowsPdbDetected);
            }
            if (msdlResult.WindowsPdbDetected)
                windowsPdbDetected = true;
        }

        // Try downloading symbol package (.snupkg)
        if (!string.IsNullOrEmpty(packageName)
            && !string.IsNullOrEmpty(packageVersion)
            && snupkgAssemblyName is not null
            && IsNuGetOrgEligibleForPackage(sourceOptions, packageName))
        {
            var snupkgResult = await TryLocateFromSymbolPackageAsync(
                packageName, packageVersion, snupkgAssemblyName, symbolKey,
                pdbGuid, portablePdbStamp, isPortable, log, cacheOnly,
                cancellationToken).ConfigureAwait(false);
            if (snupkgResult.Pdb is not null)
            {
                return new PortablePdbAcquisitionResult.Acquired(
                    snupkgResult.Pdb,
                    windowsPdbDetected || snupkgResult.WindowsPdbDetected);
            }
            if (snupkgResult.WindowsPdbDetected)
                windowsPdbDetected = true;
        }

        // Try NuGet symbol server, then MSDL as fallback (for non-Microsoft packages)
        if (!isMicrosoftPackage && pdbFileNameUsable)
        {
            var symbolResult = await TryLocateFromSymbolServerAsync(
                pdbFileName, symbolKey, pdbGuid, portablePdbStamp,
                isPortable, log, cacheOnly, cancellationToken).ConfigureAwait(false);
            if (symbolResult.Pdb is not null)
            {
                return new PortablePdbAcquisitionResult.Acquired(
                    symbolResult.Pdb,
                    windowsPdbDetected || symbolResult.WindowsPdbDetected);
            }
            if (symbolResult.WindowsPdbDetected)
                windowsPdbDetected = true;
        }

        log?.Invoke(cacheOnly ? "No cached Portable PDB available" : "No Portable PDB available");
        return new PortablePdbAcquisitionResult.Unavailable(
            windowsPdbDetected);
    }

    /// <summary>
    /// Downloads a PDB file and returns its path on disk. This compatibility
    /// wrapper is meaningful only with a filesystem-backed store.
    /// </summary>
    public async Task<PdbDownloadResult> DownloadPdbAsync(
        Guid pdbGuid, int pdbAge, string pdbFileName, bool isPortable,
        string assemblyPath,
        string? packageName = null,
        string? packageVersion = null,
        Action<string>? log = null,
        bool isPlatformAssembly = false,
        bool cacheOnly = false,
        NuGetSourceOptions? sourceOptions = null,
        CancellationToken cancellationToken = default,
        uint? portablePdbStamp = null)
    {
        PortablePdbAcquisitionResult result =
            await AcquirePdbAsync(
                pdbGuid,
                pdbAge,
                pdbFileName,
                isPortable,
                Path.GetFileNameWithoutExtension(assemblyPath),
                packageName,
                packageVersion,
                log,
                isPlatformAssembly,
                cacheOnly,
                sourceOptions,
                cancellationToken,
                portablePdbStamp).ConfigureAwait(false);

        if (result is not PortablePdbAcquisitionResult.Acquired acquired)
        {
            return new PdbDownloadResult(
                null,
                result.WindowsPdbDetected);
        }

        string? localPath = acquired.Pdb.LocalPath;
        if (localPath is null)
        {
            log?.Invoke(
                "Portable PDB content was acquired, but the configured store exposes no filesystem path.");
        }

        return new PdbDownloadResult(
            localPath,
            result.WindowsPdbDetected,
            acquired.Pdb.SymbolServer);
    }

    private bool IsNuGetOrgEligibleForPackage(
        NuGetSourceOptions? sourceOptions,
        string packageName)
    {
        if (_sourceAuthorization is not null)
        {
            PackageSourceAuthorization authorization =
                _sourceAuthorization.AuthorizeSourcesFor(
                    packageName.ToLowerInvariant());
            return authorization.Sources.Any(
                source => source.IsNuGetOrg);
        }

        try
        {
            return NuGetSourceResolver.ResolveSourcesForPackage(
                    sourceOptions,
                    packageName)
                .Any(source => source.IsNuGetOrg);
        }
        catch (PackageSourceMappingException ex)
            when (ex.Failure is
                PackageSourceMappingFailure.NoPattern
                or PackageSourceMappingFailure.InactiveSource)
        {
            return false;
        }
    }

    private sealed record PdbProbeResult(
        AcquiredPortablePdb? Pdb,
        bool WindowsPdbDetected);

    private PdbProbeResult Acquired(
        string cacheKey,
        string symbolServer,
        bool fromCache,
        bool windowsPdbDetected = false)
        => new(
            new AcquiredPortablePdb(
                _pdbStore,
                cacheKey,
                symbolServer,
                fromCache),
            windowsPdbDetected);

    private async Task<PdbProbeResult> TryLocateFromMsdlAsync(
        string pdbFileName,
        string symbolKey,
        Guid pdbGuid,
        uint? portablePdbStamp,
        bool isPortable,
        Action<string>? log,
        bool cacheOnly,
        CancellationToken cancellationToken)
    {
        using var trafficScope = NetworkTelemetry.Scope(NetworkTrafficKind.SymbolDownload);
        const string ServerHost = "msdl.microsoft.com";
        bool windowsPdbDetected = false;

        var cacheKey =
            GetSymbolServerCacheKey(
                ServerHost,
                pdbFileName,
                symbolKey);
        var cached = await ClassifyStoredPdbAsync(
            cacheKey,
            pdbGuid,
            portablePdbStamp,
            isPortable,
            log,
            cancellationToken).ConfigureAwait(false);
        if (cached.Portable)
        {
            log?.Invoke("Using cached PDB from MSDL");
            return Acquired(
                cacheKey,
                ServerHost,
                fromCache: true);
        }
        if (cached.Windows)
            windowsPdbDetected = true;

        if (cacheOnly)
            return new PdbProbeResult(null, windowsPdbDetected);

        var url = $"https://msdl.microsoft.com/download/symbols/{pdbFileName}/{symbolKey}/{pdbFileName}";
        if (IsCachedMiss(url, log, "MSDL symbol server"))
            return new PdbProbeResult(null, windowsPdbDetected);

        log?.Invoke("Trying MSDL symbol server");

        bool storeOperation = false;
        try
        {
            var httpResult = await HttpRetryHelper.GetWithRetryResultAsync(
                _client, url, log: log,
                cancellationToken: cancellationToken,
                trafficKind: NetworkTrafficKind.SymbolDownload).ConfigureAwait(false);
            using var response = httpResult.Response;
            if (response == null || !response.IsSuccessStatusCode)
            {
                CacheMissIfDefinitive(url, httpResult);
                log?.Invoke("MSDL: symbol not found");
                return new PdbProbeResult(null, windowsPdbDetected);
            }

            using (var content = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            {
                storeOperation = true;
                await _pdbStore.PutAsync(cacheKey, content, cancellationToken).ConfigureAwait(false);
            }

            var headerCheck = await ClassifyStoredPdbAsync(
                cacheKey,
                pdbGuid,
                portablePdbStamp,
                isPortable,
                log,
                cancellationToken).ConfigureAwait(false);
            storeOperation = false;
            if (headerCheck.Portable)
            {
                log?.Invoke("Successfully downloaded PDB from MSDL");
                return Acquired(
                    cacheKey,
                    ServerHost,
                    fromCache: false,
                    windowsPdbDetected);
            }
            if (headerCheck.Windows)
            {
                windowsPdbDetected = true;
                log?.Invoke("MSDL returned a Windows PDB (not supported)");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (!storeOperation)
        {
            log?.Invoke($"MSDL error: {ex.Message}");
        }

        return new PdbProbeResult(null, windowsPdbDetected);
    }

    private async Task<PdbProbeResult> TryLocateFromSymbolPackageAsync(
        string packageName,
        string packageVersion,
        string assemblyName,
        string symbolKey,
        Guid pdbGuid,
        uint? portablePdbStamp,
        bool isPortable,
        Action<string>? log,
        bool cacheOnly,
        CancellationToken cancellationToken)
    {
        using var trafficScope = NetworkTelemetry.Scope(NetworkTrafficKind.SymbolDownload);
        var normalizedName = packageName.ToLowerInvariant();
        var normalizedVersion = packageVersion.ToLowerInvariant();
        bool windowsPdbDetected = false;

        // Check cache first
        var cacheKey =
            GetCachedPdbKey(
                normalizedName,
                normalizedVersion,
                assemblyName,
                symbolKey);
        var cached = await ClassifyStoredPdbAsync(
            cacheKey,
            pdbGuid,
            portablePdbStamp,
            isPortable,
            log,
            cancellationToken).ConfigureAwait(false);
        if (cached.Portable)
        {
            log?.Invoke($"Using cached PDB: {assemblyName}.pdb");
            return Acquired(
                cacheKey,
                "nuget.org",
                fromCache: true);
        }
        if (cached.Windows)
            windowsPdbDetected = true;

        if (cacheOnly)
            return new PdbProbeResult(null, windowsPdbDetected);

        // Try NuGet global CDN first
        var snupkgUrls = new[]
        {
            $"https://globalcdn.nuget.org/symbol-packages/{normalizedName}.{normalizedVersion}.snupkg",
            $"https://api.nuget.org/v3-flatcontainer/{normalizedName}/{normalizedVersion}/{normalizedName}.{normalizedVersion}.snupkg"
        };

        log?.Invoke($"Trying symbol package: {normalizedName}.{normalizedVersion}.snupkg");

        foreach (var snupkgUrl in snupkgUrls)
        {
            if (IsCachedMiss(snupkgUrl, log, "symbol package"))
                continue;

            HttpRetryHelper.HttpRetryResult httpResult;
            try
            {
                httpResult = await HttpRetryHelper.GetWithRetryResultAsync(
                    _client, snupkgUrl, log: log,
                    cancellationToken: cancellationToken,
                    trafficKind: NetworkTrafficKind.SymbolDownload).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                log?.Invoke(
                    "Error downloading symbol package: "
                    + UrlRedaction.DescribeRequestFailure(snupkgUrl, ex));
                continue;
            }

            using var response = httpResult.Response;
            if (response is not { IsSuccessStatusCode: true })
            {
                CacheMissIfDefinitive(snupkgUrl, httpResult);
                continue;
            }

            SnupkgPdbResult extracted;
            try
            {
                log?.Invoke(
                    $"Found symbol package at: {UrlRedaction.ForDiagnostics(snupkgUrl)}");
                extracted = await ExtractPdbFromSymbolPackage(
                    response,
                    assemblyName,
                    pdbGuid,
                    portablePdbStamp,
                    log,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                log?.Invoke(
                    "Error reading symbol package: "
                    + UrlRedaction.DescribeRequestFailure(snupkgUrl, ex));
                continue;
            }

            if (extracted.WindowsPdbDetected)
                windowsPdbDetected = true;

            if (extracted.PdbBytes == null)
            {
                log?.Invoke(
                    "No matching Portable PDB identity found in symbol package");
                return new PdbProbeResult(
                    null,
                    windowsPdbDetected);
            }

            using (var pdbStream =
                   new MemoryStream(
                       extracted.PdbBytes,
                       writable: false))
            {
                await _pdbStore.PutAsync(
                    cacheKey,
                    pdbStream,
                    cancellationToken).ConfigureAwait(false);
            }

            var stored =
                await ClassifyStoredPdbAsync(
                    cacheKey,
                    pdbGuid,
                    portablePdbStamp,
                    isPortable,
                    log,
                    cancellationToken).ConfigureAwait(false);
            if (!stored.Portable)
            {
                if (stored.Windows)
                    windowsPdbDetected = true;
                log?.Invoke(
                    "The matching Portable PDB could not be read back from the configured store.");
                return new PdbProbeResult(
                    null,
                    windowsPdbDetected);
            }

            log?.Invoke(
                "Successfully located PDB from symbol package");
            return Acquired(
                cacheKey,
                "nuget.org",
                fromCache: false,
                windowsPdbDetected);
        }

        log?.Invoke("Symbol package not found on NuGet");
        return new PdbProbeResult(null, windowsPdbDetected);
    }

    private static async Task<SnupkgPdbResult> ExtractPdbFromSymbolPackage(
        HttpResponseMessage response,
        string assemblyName,
        Guid pdbGuid,
        uint? portablePdbStamp,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        SnupkgPdbResult extracted;
        using (var content = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            extracted = SnupkgPdbReader.ExtractPortablePdb(
                content,
                assemblyName,
                pdbGuid,
                log,
                portablePdbStamp);
            cancellationToken.ThrowIfCancellationRequested();
        }

        return extracted;
    }

    private async Task<PdbProbeResult> TryLocateFromSymbolServerAsync(
        string pdbFileName,
        string symbolKey,
        Guid pdbGuid,
        uint? portablePdbStamp,
        bool isPortable,
        Action<string>? log,
        bool cacheOnly,
        CancellationToken cancellationToken)
    {
        using var trafficScope = NetworkTelemetry.Scope(NetworkTrafficKind.SymbolDownload);
        bool windowsPdbDetected = false;

        var symbolServers = new[]
        {
            "https://symbols.nuget.org/download/symbols",
            "https://msdl.microsoft.com/download/symbols"
        };

        foreach (var server in symbolServers)
        {
            var serverHost = new Uri(server).Host;
            var cacheKey =
                GetSymbolServerCacheKey(
                    serverHost,
                    pdbFileName,
                    symbolKey);
            var cached =
                await ClassifyStoredPdbAsync(
                    cacheKey,
                    pdbGuid,
                    portablePdbStamp,
                    isPortable,
                    log,
                    cancellationToken).ConfigureAwait(false);
            if (cached.Portable)
            {
                log?.Invoke(
                    $"Using cached PDB from {serverHost}");
                return Acquired(
                    cacheKey,
                    serverHost,
                    fromCache: true,
                    windowsPdbDetected);
            }
            if (cached.Windows)
                windowsPdbDetected = true;

            if (cacheOnly)
                continue;

            var url = $"{server}/{pdbFileName}/{symbolKey}/{pdbFileName}";
            if (IsCachedMiss(url, log, "symbol server"))
                continue;

            log?.Invoke($"Trying symbol server: {server}");

            bool storeOperation = false;
            try
            {
                var httpResult = await HttpRetryHelper.GetWithRetryResultAsync(
                    _client, url, log: log,
                    cancellationToken: cancellationToken,
                    trafficKind: NetworkTrafficKind.SymbolDownload).ConfigureAwait(false);
                using var response = httpResult.Response;
                if (response == null || !response.IsSuccessStatusCode)
                {
                    CacheMissIfDefinitive(url, httpResult);
                    continue;
                }

                using (var content = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                {
                    storeOperation = true;
                    await _pdbStore.PutAsync(cacheKey, content, cancellationToken).ConfigureAwait(false);
                }

                var headerCheck = await ClassifyStoredPdbAsync(
                    cacheKey,
                    pdbGuid,
                    portablePdbStamp,
                    isPortable,
                    log,
                    cancellationToken).ConfigureAwait(false);
                storeOperation = false;
                if (headerCheck.Portable)
                {
                    log?.Invoke("Successfully downloaded PDB from symbol server");
                    return Acquired(
                        cacheKey,
                        serverHost,
                        fromCache: false,
                        windowsPdbDetected);
                }
                if (headerCheck.Windows)
                {
                    windowsPdbDetected = true;
                    log?.Invoke("Symbol server returned a Windows PDB (not supported)");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (!storeOperation)
            {
                log?.Invoke($"Symbol server error: {ex.Message}");
            }
        }

        return new PdbProbeResult(null, windowsPdbDetected);
    }

    private async Task<(bool Portable, bool Windows)> ClassifyStoredPdbAsync(
        string cacheKey,
        Guid expectedGuid,
        uint? expectedStamp,
        bool expectedPortable,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        Stream? stream;
        try
        {
            stream = await _pdbStore.TryOpenAsync(cacheKey, cancellationToken).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            // An untrusted assembly/PDB name can produce a key the store rejects;
            // that simply means nothing is (or can be) cached under it.
            return (false, false);
        }

        if (stream == null)
            return (false, false);

        await using (stream.ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var header = SnupkgPdbReader.ClassifyHeader(stream);
            if (!header.Portable || !expectedPortable)
                return (false, header.Windows);

            stream.Position = 0;
            bool matches =
                SnupkgPdbReader.PortablePdbMatchesIdentity(
                    stream,
                    expectedGuid,
                    expectedStamp,
                    log);
            return (matches, false);
        }
    }

    private bool IsCachedMiss(
        string key,
        Action<string>? log,
        string source)
    {
        if (!_usePersistentMissCache)
            return false;

        if (CoreCache.TryGet(SymbolMissCacheCategory, key, SymbolForbiddenCacheTtl, extension: "forbidden") != null)
        {
            log?.Invoke($"Using cached symbol miss: {source}");
            return true;
        }

        if (CoreCache.TryGet(SymbolMissCacheCategory, key, SymbolMissCacheTtl, extension: "miss") == null)
            return false;

        log?.Invoke($"Using cached symbol miss: {source}");
        return true;
    }

    private void CacheMissIfDefinitive(
        string key,
        HttpRetryHelper.HttpRetryResult result)
    {
        if (!_usePersistentMissCache)
            return;

        if (result.StatusCode is not { } statusCode)
            return;

        if (HttpRetryHelper.IsRetryableStatus(statusCode))
            return;

        var extension = statusCode == HttpStatusCode.Forbidden ? "forbidden" : "miss";
        CoreCache.Set(SymbolMissCacheCategory, key, ((int)statusCode).ToString(), extension);
    }

    /// <summary>
    /// Checks if a package name indicates it's a Microsoft package.
    /// </summary>
    private static bool IsMicrosoftPackage(string? packageName)
    {
        if (string.IsNullOrEmpty(packageName))
            return false;

        return packageName.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase) ||
               packageName.StartsWith("System.", StringComparison.OrdinalIgnoreCase) ||
               packageName.StartsWith("Azure.", StringComparison.OrdinalIgnoreCase) ||
               packageName.Equals("WindowsAzure.Storage", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetCachedPdbKey(
        string packageName,
        string packageVersion,
        string assemblyName,
        string symbolKey)
        => $"{packageName}/{packageVersion}/{symbolKey}/{assemblyName}.pdb";

    private static string GetSymbolServerCacheKey(
        string serverHost,
        string pdbName,
        string symbolKey)
        => $"servers/{serverHost}/{pdbName}/{symbolKey}/{pdbName}";

    private static string? GetSnupkgAssemblyName(
        string pdbFileName,
        bool pdbFileNameUsable,
        string? fallbackAssemblyName,
        Action<string>? log)
    {
        if (pdbFileNameUsable)
        {
            string fromCodeView =
                Path.GetFileNameWithoutExtension(pdbFileName);
            if (StorePath.IsSafeSegment(fromCodeView))
                return fromCodeView;
        }

        if (!string.IsNullOrWhiteSpace(fallbackAssemblyName)
            && StorePath.IsSafeSegment(fallbackAssemblyName))
        {
            return fallbackAssemblyName;
        }

        log?.Invoke(
            "No usable assembly name is available; skipping the symbol-package path.");
        return null;
    }

    private static string GetSymbolFileName(string pdbPath)
    {
        var normalized = pdbPath.Replace('\\', '/');
        var slash = normalized.LastIndexOf('/');
        return slash >= 0 ? normalized[(slash + 1)..] : normalized;
    }
}
