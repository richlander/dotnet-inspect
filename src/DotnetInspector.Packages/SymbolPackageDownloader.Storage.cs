using System.Net;
using DotnetInspector.Core;

namespace DotnetInspector.Packages;

public partial class SymbolPackageDownloader
{
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
            FeedFailureTelemetry.Record(key, HttpStatusCode.Forbidden);
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
}
