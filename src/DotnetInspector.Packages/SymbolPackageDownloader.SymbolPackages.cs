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

    private static string GetCachedPdbKey(
        string packageName,
        string packageVersion,
        string assemblyName,
        string symbolKey)
        => $"{packageName}/{packageVersion}/{symbolKey}/{assemblyName}.pdb";
}
