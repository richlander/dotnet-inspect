using System.Net;
using DotnetInspector.Core;

namespace DotnetInspector.Packages;

public partial class SymbolPackageDownloader
{
    private sealed record PdbProbeResult(
        AcquiredPortablePdb? Pdb,
        bool WindowsPdbDetected,
        PortablePdbStoreFailureKind? StoreFailure = null);

    private readonly record struct StoredPdbProbe(
        bool Portable,
        bool Windows,
        bool Rejected,
        PortablePdbStoreFailureKind? StoreFailure = null);

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

    private static (bool Portable, bool Windows, bool Rejected) ClassifyPdb(
        Stream stream,
        Guid expectedGuid,
        uint? expectedStamp,
        bool expectedPortable,
        Action<string>? log)
    {
        var header = SnupkgPdbReader.ClassifyHeader(stream);
        if (!header.Portable || !expectedPortable)
            return (false, header.Windows, !header.Windows);

        stream.Position = 0;
        SnupkgPdbReader.PortablePdbIdentityResult identity =
            SnupkgPdbReader.ClassifyPortablePdbIdentity(
                stream,
                expectedGuid,
                expectedStamp,
                log);
        bool matches =
            identity == SnupkgPdbReader.PortablePdbIdentityResult.Match;
        return (matches, false, !matches);
    }

    private async Task<StoredPdbProbe> ClassifyStoredPdbAsync(
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
            stream =
                await _pdbStore.TryOpenAsync(
                    cacheKey,
                    cancellationToken).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            // An untrusted assembly/PDB name can produce a key the store rejects;
            // that simply means nothing is (or can be) cached under it.
            return new StoredPdbProbe(false, false, false);
        }
        catch (PathTooLongException)
        {
            return new StoredPdbProbe(false, false, false);
        }
        catch (IOException)
        {
            return new StoredPdbProbe(
                false,
                false,
                false,
                PortablePdbStoreFailureKind.ReadFailed);
        }
        catch (UnauthorizedAccessException)
        {
            return new StoredPdbProbe(
                false,
                false,
                false,
                PortablePdbStoreFailureKind.ReadFailed);
        }

        if (stream is null)
            return new StoredPdbProbe(false, false, false);

        try
        {
            await using (stream.ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var classified = ClassifyPdb(
                    stream,
                    expectedGuid,
                    expectedStamp,
                    expectedPortable,
                    log);
                return new StoredPdbProbe(
                    classified.Portable,
                    classified.Windows,
                    classified.Rejected);
            }
        }
        catch (IOException)
        {
            return new StoredPdbProbe(
                false,
                false,
                false,
                PortablePdbStoreFailureKind.ReadFailed);
        }
        catch (UnauthorizedAccessException)
        {
            return new StoredPdbProbe(
                false,
                false,
                false,
                PortablePdbStoreFailureKind.ReadFailed);
        }
    }

    private async Task<PortablePdbStoreFailureKind?> PublishPdbAsync(
        string cacheKey,
        Stream content,
        CancellationToken cancellationToken)
    {
        try
        {
            await _pdbStore.PutAsync(
                cacheKey,
                content,
                cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (IOException)
        {
            return PortablePdbStoreFailureKind.PublicationNotRetained;
        }
        catch (UnauthorizedAccessException)
        {
            return PortablePdbStoreFailureKind.PublicationNotRetained;
        }
        catch (ArgumentException)
        {
            return PortablePdbStoreFailureKind.PublicationNotRetained;
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

        string? cachedStatus =
            CoreCache.TryGet(
                SymbolMissCacheCategory,
                key,
                SymbolMissCacheTtl,
                extension: "miss");
        if (!string.Equals(
            cachedStatus,
            ((int)HttpStatusCode.NotFound).ToString(),
            StringComparison.Ordinal))
        {
            return false;
        }

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

        string? extension = statusCode switch
        {
            HttpStatusCode.NotFound => "miss",
            HttpStatusCode.Forbidden => "forbidden",
            _ => null,
        };
        if (extension is null)
            return;

        CoreCache.Set(SymbolMissCacheCategory, key, ((int)statusCode).ToString(), extension);
    }
}
