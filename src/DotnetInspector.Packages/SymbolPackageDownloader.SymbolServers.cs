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

    private static string GetSymbolServerCacheKey(
        string serverHost,
        string pdbName,
        string symbolKey)
        => $"servers/{serverHost}/{pdbName}/{symbolKey}/{pdbName}";
}
