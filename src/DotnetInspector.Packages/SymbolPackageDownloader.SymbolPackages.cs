using System.Net;

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
        PortablePdbAcquisitionEvidenceCollector? evidence,
        CancellationToken cancellationToken)
    {
        using var trafficScope = NetworkTelemetry.Scope(NetworkTrafficKind.SymbolDownload);
        var normalizedName = packageName.ToLowerInvariant();
        var normalizedVersion = packageVersion.ToLowerInvariant();
        bool windowsPdbDetected = false;
        PortablePdbAcquisitionFailureKind? acquisitionFailure = null;

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
        PortablePdbStoreFailureKind? storeFailure = cached.StoreFailure;
        if (cached.Rejected)
            storeFailure ??= PortablePdbStoreFailureKind.InvalidCachedContent;
        evidence?.RecordObservations(
            windowsPdbDetected,
            storeFailure);
        if (cached.StoreFailure is not null)
            log?.Invoke($"The PDB store could not read the cached {assemblyName} entry");
        else if (cached.Rejected)
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
                    await FetchPdbBytesWithEvidenceAsync(
                        PortablePdbAcquisitionNetworkRoute
                            .SymbolPackage,
                        snupkgUrl,
                        log,
                        _limits?.MaxSymbolPackageBytes
                            ?? DefaultMaximumSymbolBytes,
                        evidence,
                        cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                acquisitionFailure ??=
                    PortablePdbAcquisitionFailureKind
                        .ExternalProviderFailed;
                FeedFailureRecorder.Record(snupkgUrl, status: null);
                log?.Invoke(
                    "Error downloading symbol package: "
                    + UrlRedaction.DescribeRequestFailure(snupkgUrl, ex));
                continue;
            }

            if (httpResult.Bytes is not { } symbolPackageBytes)
            {
                acquisitionFailure ??=
                    ClassifyProviderFailure(httpResult);
                CacheMissIfDefinitive(
                    snupkgUrl,
                    new HttpRetryHelper.HttpRetryResult(
                        null,
                        httpResult.StatusCode));
                if (httpResult.Status
                    == HttpRetryHelper.HttpBodyFetchStatus.TooLarge)
                {
                    FeedFailureRecorder.Record(
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
                acquisitionFailure ??=
                    PortablePdbAcquisitionFailureKind
                        .ExternalProviderFailed;
                FeedFailureRecorder.Record(
                    snupkgUrl,
                    HttpStatusCode.OK);
                log?.Invoke(
                    "Error reading symbol package: "
                    + UrlRedaction.DescribeRequestFailure(snupkgUrl, ex));
                continue;
            }

            if (extracted.WindowsPdbDetected)
            {
                windowsPdbDetected = true;
                evidence?.RecordObservations(
                    windowsPdbDetected,
                    storeFailure);
            }

            if (extracted.PdbBytes == null)
            {
                PortablePdbAcquisitionFailureKind?
                    inventoryFailure = null;
                if (extracted.InvalidPdbDetected)
                {
                    inventoryFailure =
                        PortablePdbAcquisitionFailureKind
                            .ExternalProviderFailed;
                    FeedFailureRecorder.Record(
                        snupkgUrl,
                        HttpStatusCode.OK);
                }
                log?.Invoke(
                    "No matching Portable PDB identity found in symbol package");
                return new PdbProbeResult(
                    null,
                    windowsPdbDetected,
                    storeFailure,
                    inventoryFailure);
            }

            using (var pdbStream =
                   new MemoryStream(
                       extracted.PdbBytes,
                       writable: false))
            {
                PortablePdbStoreFailureKind? publicationFailure =
                    await PublishPdbAsync(
                        cacheKey,
                        pdbStream,
                        cancellationToken).ConfigureAwait(false);
                if (publicationFailure is not null)
                {
                    evidence?.RecordObservations(
                        windowsPdbDetected,
                        publicationFailure);
                    log?.Invoke(
                        "The PDB store could not publish the verified symbol-package response.");
                    return new PdbProbeResult(
                        null,
                        windowsPdbDetected,
                        publicationFailure);
                }
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
                PortablePdbStoreFailureKind finalStoreFailure =
                    stored.StoreFailure
                    ?? PortablePdbStoreFailureKind.PublicationNotRetained;
                evidence?.RecordObservations(
                    windowsPdbDetected,
                    finalStoreFailure);
                log?.Invoke(
                    "The matching Portable PDB could not be read back from the configured store.");
                return new PdbProbeResult(
                    null,
                    windowsPdbDetected,
                    finalStoreFailure);
            }

            log?.Invoke(
                "Successfully located PDB from symbol package");
            return Acquired(
                cacheKey,
                "nuget.org",
                fromCache: false,
                windowsPdbDetected);
        }

        log?.Invoke(acquisitionFailure is null
            ? "Symbol package not found on NuGet"
            : "Symbol package providers did not produce a usable response");
        return new PdbProbeResult(
            null,
            windowsPdbDetected,
            storeFailure,
            acquisitionFailure);
    }

    private static string GetCachedPdbKey(
        string packageName,
        string packageVersion,
        string assemblyName,
        string symbolKey)
        => $"{packageName}/{packageVersion}/{symbolKey}/{assemblyName}.pdb";
}
