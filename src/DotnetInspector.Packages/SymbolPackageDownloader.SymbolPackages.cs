using System.Net;

using DotnetInspector.Core;
using InertText;

namespace DotnetInspector.Packages;

public partial class SymbolPackageDownloader
{
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

    private async Task<PdbProbeResult> TryLocateFromSymbolPackageAsync(
        string packageName,
        string packageVersion,
        string assemblyName,
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
        var normalizedName = packageName.ToLowerInvariant();
        var normalizedVersion = packageVersion.ToLowerInvariant();
        bool windowsPdbDetected = false;

        // Check cache first
        var cacheKey =
            GetCachedPdbKey(
                normalizedName,
                normalizedVersion,
                assemblyName,
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
            log?.Invoke($"Using cached PDB: {assemblyName}.pdb");
            return Acquired(
                cacheKey,
                "nuget.org",
                fromCache: true);
        }
        if (cached.Windows)
            windowsPdbDetected = true;
        PortablePdbStoreFailureKind? storeFailure = cached.Rejected
            ? PortablePdbStoreFailureKind.InvalidCachedContent
            : null;
        if (storeFailure is not null)
            log?.Invoke($"Cached PDB for {assemblyName} is invalid or mismatched");

        if (cacheOnly)
            return new PdbProbeResult(
                null,
                windowsPdbDetected,
                storeFailure);

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

            HttpRetryHelper.HttpBodyFetchResult httpResult;
            try
            {
                httpResult =
                    await HttpRetryHelper.GetBytesAfterHeadersWithRetryAsync(
                        _client,
                        snupkgUrl,
                        static _ => true,
                        log: log,
                        cancellationToken: cancellationToken,
                        trafficKind: NetworkTrafficKind.SymbolDownload,
                        maxDownloadSize:
                            _limits?.MaxSymbolPackageBytes
                            ?? DefaultMaximumSymbolBytes).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                FeedFailureTelemetry.Record(snupkgUrl, status: null);
                log?.Invoke(
                    "Error downloading symbol package: "
                    + UrlRedaction.DescribeRequestFailure(snupkgUrl, ex));
                continue;
            }

            if (httpResult.Bytes is not { } symbolPackageBytes)
            {
                CacheMissIfDefinitive(
                    snupkgUrl,
                    new HttpRetryHelper.HttpRetryResult(
                        null,
                        httpResult.StatusCode));
                if (httpResult.Status
                    == HttpRetryHelper.HttpBodyFetchStatus.TooLarge)
                {
                    FeedFailureTelemetry.Record(
                        snupkgUrl,
                        HttpStatusCode.OK);
                    log?.Invoke(
                        "Symbol package exceeds the configured download limit.");
                }
                continue;
            }

            SnupkgPdbResult extracted;
            try
            {
                log?.Invoke(
                    $"Found symbol package at: {UrlRedaction.ForDiagnostics(snupkgUrl)}");
                using var content =
                    new MemoryStream(
                        symbolPackageBytes,
                        writable: false);
                cancellationToken.ThrowIfCancellationRequested();
                extracted = SnupkgPdbReader.ExtractPortablePdbCancelable(
                    content,
                    assemblyName,
                    pdbGuid,
                    log,
                    portablePdbStamp,
                    _limits,
                    cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                FeedFailureTelemetry.Record(
                    snupkgUrl,
                    HttpStatusCode.OK);
                log?.Invoke(
                    "Error reading symbol package: "
                    + UrlRedaction.DescribeRequestFailure(snupkgUrl, ex));
                continue;
            }

            if (extracted.WindowsPdbDetected)
                windowsPdbDetected = true;

            if (extracted.PdbBytes == null)
            {
                if (extracted.InvalidPdbDetected)
                {
                    FeedFailureTelemetry.Record(
                        snupkgUrl,
                        HttpStatusCode.OK);
                }
                log?.Invoke(
                    "No matching Portable PDB identity found in symbol package");
                return new PdbProbeResult(
                    null,
                    windowsPdbDetected,
                    storeFailure);
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
                    windowsPdbDetected,
                    PortablePdbStoreFailureKind.PublicationNotRetained);
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
        return new PdbProbeResult(
            null,
            windowsPdbDetected,
            storeFailure);
    }

    private static string GetCachedPdbKey(
        string packageName,
        string packageVersion,
        string assemblyName,
        string symbolKey)
        => $"{packageName}/{packageVersion}/{symbolKey}/{assemblyName}.pdb";
}
