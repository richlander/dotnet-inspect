using System.Collections.Concurrent;
using System.Collections.Immutable;
using DotnetInspector.Core;
using DotnetInspector.Packages;
using ILInspector.SourceLink;

namespace DotnetInspector.Services;

/// <summary>The completed checksum audit for one assembly's source documents.</summary>
public sealed record SourceIntegritySummary(
    int Verified,
    int Mismatched,
    int LineEndingNormalized,
    int Unverifiable,
    ImmutableArray<string> MismatchedFiles);

/// <summary>
/// Downloads SourceLink source bodies and verifies them against portable-PDB checksums.
/// </summary>
public static class SourceIntegrityService
{
    private const string CacheCategory = "source-integrity-v2";

    public static async Task<SourceIntegritySummary> InspectAsync(
        IEnumerable<SourceDocumentObservation> sourceDocuments,
        HttpClient httpClient,
        ISourceLinkQueryCache? cache = null,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceDocuments);
        ArgumentNullException.ThrowIfNull(httpClient);

        using var trafficScope = NetworkTelemetry.Scope(NetworkTrafficKind.SourceIntegrity);
        var documents = sourceDocuments
            .Where(static document => document.IsCompilerLanguageSource)
            .ToArray();

        List<SourceDocumentObservation> verifiable = [];
        int unverifiable = 0;
        foreach (SourceDocumentObservation document in documents)
        {
            if (document.Storage == SourceDocumentStorage.Embedded)
                continue;
            if (document.ResolvedUrl == null
                || string.IsNullOrEmpty(document.Checksum)
                || document.ChecksumAlgorithm == null
                || !HttpClientFactory.IsAllowedFetchScheme(document.ResolvedUrl))
            {
                unverifiable++;
                continue;
            }

            verifiable.Add(document);
        }

        int verified = 0;
        int lineEndingNormalized = 0;
        int mismatched = 0;
        var mismatches = new ConcurrentBag<string>();

        if (verifiable.Count > 0)
        {
            log?.Invoke($"Verifying integrity of {verifiable.Count} source files...");

            await Parallel.ForEachAsync(
                verifiable,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = 16,
                    CancellationToken = cancellationToken,
                },
                async (document, ct) =>
                {
                    string cacheKey =
                        $"{document.ResolvedUrl}|{document.ChecksumAlgorithm}|{document.Checksum}";
                    bool immutable = SourceLinkUrls.IsImmutable(document.ResolvedUrl!);

                    if (immutable
                        && cache?.TryGet(
                            CacheCategory,
                            cacheKey,
                            maxAge: null,
                            extension: "verified") != null)
                    {
                        Interlocked.Increment(ref verified);
                        return;
                    }
                    if (immutable
                        && cache?.TryGet(
                            CacheCategory,
                            cacheKey,
                            maxAge: null,
                            extension: "normalized") != null)
                    {
                        Interlocked.Increment(ref verified);
                        Interlocked.Increment(ref lineEndingNormalized);
                        return;
                    }

                    byte[]? body;
                    try
                    {
                        HttpRetryHelper.HttpBodyFetchResult fetch =
                            await HttpRetryHelper.GetBytesAfterHeadersWithRetryAsync(
                                httpClient,
                                document.ResolvedUrl!,
                                response => SourceFetchOriginValidator.Validate(
                                    document.ResolvedUrl!,
                                    response.RequestMessage?.RequestUri?.AbsoluteUri).IsAllowed,
                                log: null,
                                cancellationToken: ct,
                                trafficKind: NetworkTrafficKind.SourceIntegrity,
                                maxDownloadSize: SourceFetcher.MaxSourceDownloadSize)
                                .ConfigureAwait(false);
                        if (fetch.Status
                            == HttpRetryHelper.HttpBodyFetchStatus.ResponseRejected)
                        {
                            log?.Invoke(
                                "Could not verify the final SourceLink response origin.");
                            body = null;
                        }
                        else if (fetch.Bytes is { } bytes)
                        {
                            body = bytes;
                        }
                        else
                        {
                            log?.Invoke("Source integrity fetch failed.");
                            body = null;
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        log?.Invoke("Source integrity fetch failed.");
                        body = null;
                    }

                    if (body == null)
                    {
                        Interlocked.Increment(ref unverifiable);
                        return;
                    }

                    SourceChecksumVerification verification =
                        AuthoredSourceAcquisition.VerifyChecksum(document, body);
                    if (verification == SourceChecksumVerification.Exact)
                    {
                        Interlocked.Increment(ref verified);
                        if (immutable)
                            cache?.Set(CacheCategory, cacheKey, "1", "verified");
                    }
                    else if (verification == SourceChecksumVerification.LineEndingNormalized)
                    {
                        Interlocked.Increment(ref verified);
                        Interlocked.Increment(ref lineEndingNormalized);
                        if (immutable)
                            cache?.Set(CacheCategory, cacheKey, "1", "normalized");
                    }
                    else if (verification == SourceChecksumVerification.Mismatch)
                    {
                        Interlocked.Increment(ref mismatched);
                        mismatches.Add(document.OriginalPath);
                        log?.Invoke("Source integrity checksum mismatch.");
                    }
                    else
                    {
                        Interlocked.Increment(ref unverifiable);
                    }
                }).ConfigureAwait(false);
        }

        return new SourceIntegritySummary(
            verified,
            mismatched,
            lineEndingNormalized,
            unverifiable,
            [.. mismatches.Order(StringComparer.Ordinal)]);
    }
}
