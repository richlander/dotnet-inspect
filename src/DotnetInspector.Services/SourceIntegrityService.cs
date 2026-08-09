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
    private const string CacheCategory = "source-integrity";

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
            foreach (string? host in verifiable
                .Select(document => SafeHost(document.ResolvedUrl!))
                .Where(static host => host != null)
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                log?.Invoke($"Source integrity fetch host: {host}");
            }

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
                        body = await HttpRetryHelper.GetBytesWithRetryAsync(
                            httpClient,
                            document.ResolvedUrl!,
                            log: log,
                            cancellationToken: ct,
                            trafficKind: NetworkTrafficKind.SourceIntegrity).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        log?.Invoke(
                            $"Integrity fetch failed: {document.ResolvedUrl} ({ex.Message})");
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
                        log?.Invoke(
                            $"Integrity MISMATCH: {document.OriginalPath} ({document.ResolvedUrl})");
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

    private static string? SafeHost(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ? uri.Host : null;
}
