using System.Net;
using DotnetInspector.Core;

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
/// Downloads and manages symbol packages (.snupkg) from NuGet for SourceLink resolution.
/// Only supports Portable PDBs (embedded or standalone) and snupkg files.
/// </summary>
/// <remarks>
/// <para>
/// Transport is injectable via <see cref="HttpClient"/> and persistence via
/// <see cref="IPdbStore"/>. The host-neutral snupkg parsing lives in
/// <see cref="SnupkgPdbReader"/>; this class is the desktop orchestrator that
/// returns on-disk PDB paths (a browser/WASM host reuses <see cref="SnupkgPdbReader"/>
/// directly for bytes and does not depend on filesystem paths).
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

    /// <summary>
    /// Creates a downloader backed by the default filesystem PDB cache
    /// (<c>{app-cache}/packages/symbols</c>).
    /// </summary>
    public SymbolPackageDownloader(HttpClient client)
        : this(client, FileSystemPdbStore.CreateDefault())
    {
    }

    /// <summary>
    /// Creates a downloader with an explicit <see cref="IPdbStore"/> for PDB
    /// persistence (in-memory for browser/WASM hosts and tests).
    /// </summary>
    /// <remarks>
    /// <see cref="DownloadPdbAsync"/> reports success as an on-disk
    /// <see cref="PdbDownloadResult.PdbFilePath"/>, so it is meaningful only with a
    /// filesystem-backed store. A store whose <c>TryGetLocalPath</c> always returns
    /// null (for example <see cref="InMemoryPdbStore"/>) is for host-neutral
    /// persistence; such a host reads PDB bytes through <see cref="SnupkgPdbReader"/>
    /// and the store directly rather than through this on-disk path orchestration.
    /// </remarks>
    public SymbolPackageDownloader(HttpClient client, IPdbStore pdbStore)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(pdbStore);
        _client = client;
        _pdbStore = pdbStore;
    }

    /// <summary>
    /// Downloads a PDB file and returns its path on disk. No SRM types in signature.
    /// The caller (PdbContext) is responsible for opening and reading the PDB.
    /// </summary>
    public async Task<PdbDownloadResult> DownloadPdbAsync(
        Guid pdbGuid, int pdbAge, string pdbFileName, bool isPortable,
        string assemblyPath,
        string? packageName = null,
        string? packageVersion = null,
        Action<string>? log = null,
        bool isPlatformAssembly = false)
    {
        bool windowsPdbDetected = false;

        pdbFileName = GetSymbolFileName(pdbFileName);
        // The PDB file name comes from untrusted PE debug metadata. If it is not
        // a usable single path segment (empty, traversal, volume-qualified, or
        // separator-bearing) it cannot form a valid cache key or symbol-server
        // path, so treat it as "no symbols available" rather than letting an
        // invalid store key throw later.
        if (!StorePath.IsSafeSegment(pdbFileName))
        {
            log?.Invoke("No usable PDB file name; treating as no symbols available");
            return new PdbDownloadResult(null, windowsPdbDetected);
        }

        var guid = pdbGuid.ToString("N").ToUpperInvariant();
        var symbolKey = isPortable
            ? $"{guid}FFFFFFFF"
            : $"{guid}{pdbAge:x}";

        // For Microsoft packages or platform assemblies, try MSDL first
        bool isMicrosoftPackage = isPlatformAssembly || IsMicrosoftPackage(packageName);
        if (isMicrosoftPackage)
        {
            log?.Invoke(isPlatformAssembly ? "Platform library, trying MSDL symbol server" : "Microsoft package detected, trying MSDL symbol server first");
            var msdlResult = await TryLocateFromMsdlAsync(pdbFileName, symbolKey, log).ConfigureAwait(false);
            if (msdlResult.PdbFilePath != null)
                return msdlResult;
            if (msdlResult.WindowsPdbDetected)
                windowsPdbDetected = true;
        }

        // Try downloading symbol package (.snupkg)
        if (!string.IsNullOrEmpty(packageName) && !string.IsNullOrEmpty(packageVersion))
        {
            var snupkgResult = await TryLocateFromSymbolPackageAsync(
                packageName, packageVersion, assemblyPath, symbolKey, pdbGuid, log).ConfigureAwait(false);
            if (snupkgResult.PdbFilePath != null)
                return snupkgResult;
            if (snupkgResult.WindowsPdbDetected)
                windowsPdbDetected = true;
        }

        // Try NuGet symbol server, then MSDL as fallback (for non-Microsoft packages)
        if (!isMicrosoftPackage)
        {
            var symbolResult = await TryLocateFromSymbolServerAsync(pdbFileName, symbolKey, log).ConfigureAwait(false);
            if (symbolResult.PdbFilePath != null)
                return symbolResult;
            if (symbolResult.WindowsPdbDetected)
                windowsPdbDetected = true;
        }

        log?.Invoke("No Portable PDB available");
        return new PdbDownloadResult(null, windowsPdbDetected);
    }

    private async Task<PdbDownloadResult> TryLocateFromMsdlAsync(
        string pdbFileName, string symbolKey, Action<string>? log)
    {
        using var trafficScope = NetworkTelemetry.Scope(NetworkTrafficKind.SymbolDownload);
        bool windowsPdbDetected = false;

        var cacheKey = GetSymbolServerCacheKey(pdbFileName, symbolKey);
        var cached = await ClassifyStoredPdbAsync(cacheKey).ConfigureAwait(false);
        if (cached.Portable)
        {
            log?.Invoke("Using cached PDB from MSDL");
            return new PdbDownloadResult(_pdbStore.TryGetLocalPath(cacheKey), SymbolServer: "msdl.microsoft.com");
        }
        if (cached.Windows)
            windowsPdbDetected = true;

        var url = $"https://msdl.microsoft.com/download/symbols/{pdbFileName}/{symbolKey}/{pdbFileName}";
        if (IsCachedMiss(url, log, "MSDL symbol server"))
            return new PdbDownloadResult(null, windowsPdbDetected);

        log?.Invoke("Trying MSDL symbol server");

        try
        {
            var httpResult = await HttpRetryHelper.GetWithRetryResultAsync(
                _client, url, log: log,
                trafficKind: NetworkTrafficKind.SymbolDownload).ConfigureAwait(false);
            using var response = httpResult.Response;
            if (response == null || !response.IsSuccessStatusCode)
            {
                CacheMissIfDefinitive(url, httpResult);
                log?.Invoke("MSDL: symbol not found");
                return new PdbDownloadResult(null, windowsPdbDetected);
            }

            using (var content = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            {
                await _pdbStore.PutAsync(cacheKey, content).ConfigureAwait(false);
            }

            var headerCheck = await ClassifyStoredPdbAsync(cacheKey).ConfigureAwait(false);
            if (headerCheck.Portable)
            {
                log?.Invoke("Successfully downloaded PDB from MSDL");
                return new PdbDownloadResult(_pdbStore.TryGetLocalPath(cacheKey), SymbolServer: "msdl.microsoft.com");
            }
            if (headerCheck.Windows)
            {
                windowsPdbDetected = true;
                log?.Invoke("MSDL returned a Windows PDB (not supported)");
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"MSDL error: {ex.Message}");
        }

        return new PdbDownloadResult(null, windowsPdbDetected);
    }

    private async Task<PdbDownloadResult> TryLocateFromSymbolPackageAsync(
        string packageName, string packageVersion, string assemblyPath, string symbolKey, Guid pdbGuid, Action<string>? log)
    {
        using var trafficScope = NetworkTelemetry.Scope(NetworkTrafficKind.SymbolDownload);
        var normalizedName = packageName.ToLowerInvariant();
        var normalizedVersion = packageVersion.ToLowerInvariant();
        bool windowsPdbDetected = false;

        // Check cache first
        var cacheKey = GetCachedPdbKey(normalizedName, normalizedVersion, assemblyPath, symbolKey);
        var cached = await ClassifyStoredPdbAsync(cacheKey).ConfigureAwait(false);
        if (cached.Portable)
        {
            log?.Invoke($"Using cached PDB: {Path.GetFileNameWithoutExtension(assemblyPath)}.pdb");
            return new PdbDownloadResult(_pdbStore.TryGetLocalPath(cacheKey), SymbolServer: "nuget.org");
        }
        if (cached.Windows)
            windowsPdbDetected = true;

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

            try
            {
                var httpResult = await HttpRetryHelper.GetWithRetryResultAsync(
                    _client, snupkgUrl, log: log,
                    trafficKind: NetworkTrafficKind.SymbolDownload).ConfigureAwait(false);
                using var response = httpResult.Response;
                if (response is not { IsSuccessStatusCode: true })
                {
                    CacheMissIfDefinitive(snupkgUrl, httpResult);
                    continue;
                }

                log?.Invoke($"Found symbol package at: {snupkgUrl}");
                var result = await ExtractPdbFromSymbolPackage(
                    response, cacheKey, assemblyPath, pdbGuid, windowsPdbDetected, log).ConfigureAwait(false);
                if (result.WindowsPdbDetected)
                    windowsPdbDetected = true;
                return result;
            }
            catch (Exception ex)
            {
                log?.Invoke($"Error downloading symbol package: {ex.Message}");
            }
        }

        log?.Invoke("Symbol package not found on NuGet");
        return new PdbDownloadResult(null, windowsPdbDetected);
    }

    private async Task<PdbDownloadResult> ExtractPdbFromSymbolPackage(
        HttpResponseMessage response,
        string cacheKey, string assemblyPath,
        Guid pdbGuid, bool windowsPdbDetected, Action<string>? log)
    {
        var assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);

        SnupkgPdbResult extracted;
        using (var content = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
        {
            extracted = SnupkgPdbReader.ExtractPortablePdb(content, assemblyName, pdbGuid, log);
        }

        if (extracted.WindowsPdbDetected)
            windowsPdbDetected = true;

        if (extracted.PdbBytes == null)
        {
            log?.Invoke("No matching Portable PDB identity found in symbol package");
            return new PdbDownloadResult(null, windowsPdbDetected);
        }

        using (var pdbStream = new MemoryStream(extracted.PdbBytes, writable: false))
        {
            await _pdbStore.PutAsync(cacheKey, pdbStream).ConfigureAwait(false);
        }

        log?.Invoke("Successfully located PDB from symbol package");
        return new PdbDownloadResult(_pdbStore.TryGetLocalPath(cacheKey), windowsPdbDetected, SymbolServer: "nuget.org");
    }

    private async Task<PdbDownloadResult> TryLocateFromSymbolServerAsync(
        string pdbFileName, string symbolKey, Action<string>? log)
    {
        using var trafficScope = NetworkTelemetry.Scope(NetworkTrafficKind.SymbolDownload);
        bool windowsPdbDetected = false;

        // Check cache before hitting the network
        var cacheKey = GetSymbolServerCacheKey(pdbFileName, symbolKey);
        var cached = await ClassifyStoredPdbAsync(cacheKey).ConfigureAwait(false);
        if (cached.Portable)
        {
            log?.Invoke("Using cached PDB from symbol server");
            return new PdbDownloadResult(_pdbStore.TryGetLocalPath(cacheKey), SymbolServer: "cached");
        }
        if (cached.Windows)
            windowsPdbDetected = true;

        var symbolServers = new[]
        {
            "https://symbols.nuget.org/download/symbols",
            "https://msdl.microsoft.com/download/symbols"
        };

        foreach (var server in symbolServers)
        {
            var url = $"{server}/{pdbFileName}/{symbolKey}/{pdbFileName}";
            if (IsCachedMiss(url, log, "symbol server"))
                continue;

            log?.Invoke($"Trying symbol server: {server}");

            try
            {
                var httpResult = await HttpRetryHelper.GetWithRetryResultAsync(
                    _client, url, log: log,
                    trafficKind: NetworkTrafficKind.SymbolDownload).ConfigureAwait(false);
                using var response = httpResult.Response;
                if (response == null || !response.IsSuccessStatusCode)
                {
                    CacheMissIfDefinitive(url, httpResult);
                    continue;
                }

                using (var content = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                {
                    await _pdbStore.PutAsync(cacheKey, content).ConfigureAwait(false);
                }

                var headerCheck = await ClassifyStoredPdbAsync(cacheKey).ConfigureAwait(false);
                if (headerCheck.Portable)
                {
                    var serverHost = new Uri(server).Host;
                    log?.Invoke("Successfully downloaded PDB from symbol server");
                    return new PdbDownloadResult(_pdbStore.TryGetLocalPath(cacheKey), SymbolServer: serverHost);
                }
                if (headerCheck.Windows)
                {
                    windowsPdbDetected = true;
                    log?.Invoke("Symbol server returned a Windows PDB (not supported)");
                }
            }
            catch (Exception ex)
            {
                log?.Invoke($"Symbol server error: {ex.Message}");
            }
        }

        return new PdbDownloadResult(null, windowsPdbDetected);
    }

    private async Task<(bool Portable, bool Windows)> ClassifyStoredPdbAsync(string cacheKey)
    {
        Stream? stream;
        try
        {
            stream = await _pdbStore.TryOpenAsync(cacheKey).ConfigureAwait(false);
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
            return SnupkgPdbReader.ClassifyHeader(stream);
        }
    }

    private static bool IsCachedMiss(string key, Action<string>? log, string source)
    {
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

    private static void CacheMissIfDefinitive(string key, HttpRetryHelper.HttpRetryResult result)
    {
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

    private static string GetCachedPdbKey(string packageName, string packageVersion, string assemblyPath, string symbolKey)
    {
        var assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);
        return $"{packageName}/{packageVersion}/{symbolKey}/{assemblyName}.pdb";
    }

    private static string GetSymbolServerCacheKey(string pdbName, string symbolKey)
        => $"servers/{pdbName}/{symbolKey}/{pdbName}";

    private static string GetSymbolFileName(string pdbPath)
    {
        var normalized = pdbPath.Replace('\\', '/');
        var slash = normalized.LastIndexOf('/');
        return slash >= 0 ? normalized[(slash + 1)..] : normalized;
    }
}
