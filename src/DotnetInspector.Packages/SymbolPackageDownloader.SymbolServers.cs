using System.Net;

using DotnetInspector.Core;

namespace DotnetInspector.Packages;

public partial class SymbolPackageDownloader
{
    private async Task<PdbProbeResult> TryLocateFromMsdlAsync(
        string pdbFileName,
        string symbolKey,
        string storeIdentity,
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
                storeIdentity);
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
        PortablePdbStoreFailureKind? storeFailure = cached.StoreFailure;
        if (cached.Rejected)
            storeFailure ??= PortablePdbStoreFailureKind.InvalidCachedContent;
        if (cached.StoreFailure is not null)
            log?.Invoke("The PDB store could not read the cached MSDL entry");
        else if (cached.Rejected)
            log?.Invoke("Cached PDB from MSDL is invalid or mismatched");

        if (cacheOnly)
            return new PdbProbeResult(
                null,
                windowsPdbDetected,
                storeFailure);

        var url = $"https://msdl.microsoft.com/download/symbols/{pdbFileName}/{symbolKey}/{pdbFileName}";
        if (IsCachedMiss(url, log, "MSDL symbol server"))
            return new PdbProbeResult(
                null,
                windowsPdbDetected,
                storeFailure);

        log?.Invoke("Trying MSDL symbol server");

        bool storeOperation = false;
        try
        {
            var httpResult =
                await HttpRetryHelper.GetBytesAfterHeadersWithRetryAsync(
                    _client,
                    url,
                    static _ => true,
                    log: log,
                    cancellationToken: cancellationToken,
                    trafficKind: NetworkTrafficKind.SymbolDownload,
                    maxDownloadSize:
                        _limits?.MaxPortablePdbBytes
                        ?? DefaultMaximumSymbolBytes).ConfigureAwait(false);
            if (httpResult.Bytes is not { } pdbBytes)
            {
                CacheMissIfDefinitive(
                    url,
                    new HttpRetryHelper.HttpRetryResult(
                        null,
                        httpResult.StatusCode));
                if (httpResult.Status
                    == HttpRetryHelper.HttpBodyFetchStatus.TooLarge)
                {
                    FeedFailureTelemetry.Record(
                        url,
                        HttpStatusCode.OK);
                    log?.Invoke(
                        "MSDL PDB response exceeds the configured download limit.");
                }
                log?.Invoke("MSDL: symbol not found");
                return new PdbProbeResult(
                    null,
                    windowsPdbDetected,
                    storeFailure);
            }

            using var content =
                new MemoryStream(
                    pdbBytes,
                    writable: false);
            var headerCheck = ClassifyPdb(
                content,
                pdbGuid,
                portablePdbStamp,
                isPortable,
                log);
            if (headerCheck.Portable)
            {
                using var storeContent =
                    new MemoryStream(
                        pdbBytes,
                        writable: false);
                storeOperation = true;
                PortablePdbStoreFailureKind? publicationFailure =
                    await PublishPdbAsync(
                        cacheKey,
                        storeContent,
                        cancellationToken).ConfigureAwait(false);
                if (publicationFailure is not null)
                {
                    storeOperation = false;
                    log?.Invoke(
                        "The PDB store could not publish the verified MSDL response");
                    return new PdbProbeResult(
                        null,
                        windowsPdbDetected,
                        publicationFailure);
                }
                var stored =
                    await ClassifyStoredPdbAsync(
                        cacheKey,
                        pdbGuid,
                        portablePdbStamp,
                        isPortable,
                        log,
                        cancellationToken).ConfigureAwait(false);
                storeOperation = false;
                if (stored.Portable)
                {
                    log?.Invoke("Successfully downloaded PDB from MSDL");
                    return Acquired(
                        cacheKey,
                        ServerHost,
                        fromCache: false,
                        windowsPdbDetected);
                }

                log?.Invoke("The PDB store did not retain the verified MSDL response");
                return new PdbProbeResult(
                    null,
                    windowsPdbDetected,
                    stored.StoreFailure
                        ?? PortablePdbStoreFailureKind.PublicationNotRetained);
            }
            if (headerCheck.Windows)
            {
                windowsPdbDetected = true;
                log?.Invoke("MSDL returned a Windows PDB (not supported)");
            }
            else
            {
                FeedFailureTelemetry.Record(url, HttpStatusCode.OK);
                log?.Invoke("MSDL returned an invalid or mismatched Portable PDB");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (!storeOperation)
        {
            FeedFailureTelemetry.Record(url, status: null);
            log?.Invoke($"MSDL error: {ex.Message}");
        }

        return new PdbProbeResult(
            null,
            windowsPdbDetected,
            storeFailure);
    }

    private async Task<PdbProbeResult> TryLocateFromSymbolServerAsync(
        string pdbFileName,
        string symbolKey,
        string storeIdentity,
        Guid pdbGuid,
        uint? portablePdbStamp,
        bool isPortable,
        Action<string>? log,
        bool cacheOnly,
        CancellationToken cancellationToken)
    {
        using var trafficScope = NetworkTelemetry.Scope(NetworkTrafficKind.SymbolDownload);
        bool windowsPdbDetected = false;
        PortablePdbStoreFailureKind? storeFailure = null;

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
                    storeIdentity);
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
            if (cached.StoreFailure is not null)
            {
                storeFailure ??= cached.StoreFailure;
                log?.Invoke($"The PDB store could not read the cached {serverHost} entry");
            }
            if (cached.Rejected)
            {
                storeFailure ??=
                    PortablePdbStoreFailureKind.InvalidCachedContent;
                log?.Invoke($"Cached PDB from {serverHost} is invalid or mismatched");
            }

            if (cacheOnly)
                continue;

            var url = $"{server}/{pdbFileName}/{symbolKey}/{pdbFileName}";
            if (IsCachedMiss(url, log, "symbol server"))
                continue;

            log?.Invoke($"Trying symbol server: {server}");

            bool storeOperation = false;
            try
            {
                var httpResult =
                    await HttpRetryHelper.GetBytesAfterHeadersWithRetryAsync(
                        _client,
                        url,
                        static _ => true,
                        log: log,
                        cancellationToken: cancellationToken,
                        trafficKind: NetworkTrafficKind.SymbolDownload,
                        maxDownloadSize:
                            _limits?.MaxPortablePdbBytes
                            ?? DefaultMaximumSymbolBytes).ConfigureAwait(false);
                if (httpResult.Bytes is not { } pdbBytes)
                {
                    CacheMissIfDefinitive(
                        url,
                        new HttpRetryHelper.HttpRetryResult(
                            null,
                            httpResult.StatusCode));
                    if (httpResult.Status
                        == HttpRetryHelper.HttpBodyFetchStatus.TooLarge)
                    {
                        FeedFailureTelemetry.Record(
                            url,
                            HttpStatusCode.OK);
                        log?.Invoke(
                            "PDB response exceeds the configured download limit.");
                    }
                    continue;
                }

                using var content =
                    new MemoryStream(
                        pdbBytes,
                        writable: false);
                var headerCheck = ClassifyPdb(
                    content,
                    pdbGuid,
                    portablePdbStamp,
                    isPortable,
                    log);
                if (headerCheck.Portable)
                {
                    using var storeContent =
                        new MemoryStream(
                            pdbBytes,
                            writable: false);
                    storeOperation = true;
                    PortablePdbStoreFailureKind? publicationFailure =
                        await PublishPdbAsync(
                            cacheKey,
                            storeContent,
                            cancellationToken).ConfigureAwait(false);
                    if (publicationFailure is not null)
                    {
                        storeOperation = false;
                        storeFailure ??= publicationFailure;
                        log?.Invoke(
                            "The PDB store could not publish the verified symbol-server response");
                        continue;
                    }
                    var stored =
                        await ClassifyStoredPdbAsync(
                            cacheKey,
                            pdbGuid,
                            portablePdbStamp,
                            isPortable,
                            log,
                            cancellationToken).ConfigureAwait(false);
                    storeOperation = false;
                    if (stored.Portable)
                    {
                        log?.Invoke("Successfully downloaded PDB from symbol server");
                        return Acquired(
                            cacheKey,
                            serverHost,
                            fromCache: false,
                            windowsPdbDetected);
                    }

                    storeFailure ??=
                        stored.StoreFailure
                        ?? PortablePdbStoreFailureKind.PublicationNotRetained;
                    log?.Invoke(
                        "The PDB store did not retain the verified symbol-server response");
                    continue;
                }
                if (headerCheck.Windows)
                {
                    windowsPdbDetected = true;
                    log?.Invoke("Symbol server returned a Windows PDB (not supported)");
                }
                else
                {
                    FeedFailureTelemetry.Record(url, HttpStatusCode.OK);
                    log?.Invoke(
                        "Symbol server returned an invalid or mismatched Portable PDB");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (!storeOperation)
            {
                FeedFailureTelemetry.Record(url, status: null);
                log?.Invoke($"Symbol server error: {ex.Message}");
            }
        }

        return new PdbProbeResult(
            null,
            windowsPdbDetected,
            storeFailure);
    }

    private static string GetSymbolServerCacheKey(
        string serverHost,
        string pdbName,
        string symbolKey)
        => $"servers/{serverHost}/{pdbName}/{symbolKey}/{pdbName}";
}
