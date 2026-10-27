using System.Collections.Immutable;
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
    private const int MaximumConcurrentVerifications = 16;

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
        List<string> mismatches = [];

        if (verifiable.Count > 0)
        {
            log?.Invoke($"Verifying integrity of {verifiable.Count} source files...");

            SourceIntegrityProbeSummary probes = await ProbeSourcesAsync(
                verifiable,
                httpClient,
                cache,
                log,
                cancellationToken).ConfigureAwait(false);
            verified += probes.Verified;
            lineEndingNormalized += probes.LineEndingNormalized;
            mismatched += probes.Mismatched;
            unverifiable += probes.Unverifiable;
            mismatches.AddRange(probes.MismatchedFiles);
        }

        return new SourceIntegritySummary(
            verified,
            mismatched,
            lineEndingNormalized,
            unverifiable,
            [.. mismatches.Order(StringComparer.Ordinal)]);
    }

    private static async Task<SourceIntegrityProbeSummary> ProbeSourcesAsync(
        IReadOnlyList<SourceDocumentObservation> documents,
        HttpClient httpClient,
        ISourceLinkQueryCache? cache,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        int verified = 0;
        int lineEndingNormalized = 0;
        int mismatched = 0;
        int unverifiable = 0;
        List<string> mismatchedFiles = [];
        var pending = new List<Task<SourceIntegrityProbeResult>>();
        int next = 0;

        try
        {
            while (next < documents.Count || pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                while (next < documents.Count
                    && pending.Count < MaximumConcurrentVerifications)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    pending.Add(ProbeSourceAsync(
                        documents[next],
                        httpClient,
                        cache,
                        cancellationToken));
                    next++;
                }

                Task<SourceIntegrityProbeResult> completed =
                    await Task.WhenAny(pending).ConfigureAwait(false);
                pending.Remove(completed);
                SourceIntegrityProbeResult probe =
                    await completed.ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();

                switch (probe.Verification)
                {
                    case SourceChecksumVerification.Exact:
                        verified++;
                        if (probe.PublishEvidence)
                        {
                            cache?.Set(
                                CacheCategory,
                                probe.CacheKey,
                                "1",
                                "verified");
                        }
                        break;
                    case SourceChecksumVerification.LineEndingNormalized:
                        verified++;
                        lineEndingNormalized++;
                        if (probe.PublishEvidence)
                        {
                            cache?.Set(
                                CacheCategory,
                                probe.CacheKey,
                                "1",
                                "normalized");
                        }
                        break;
                    case SourceChecksumVerification.Mismatch:
                        mismatched++;
                        mismatchedFiles.Add(probe.Document.OriginalPath);
                        log?.Invoke("Source integrity checksum mismatch.");
                        break;
                    case SourceChecksumVerification.Unavailable:
                    case SourceChecksumVerification.Unsupported:
                        unverifiable++;
                        if (probe.FetchStatus
                            == HttpRetryHelper.HttpBodyFetchStatus.ResponseRejected)
                        {
                            log?.Invoke(
                                "Could not verify the final SourceLink response origin.");
                        }
                        else if (probe.FetchStatus is not null)
                        {
                            log?.Invoke("Source integrity fetch failed.");
                        }
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"Unknown source integrity verdict: {probe.Verification}.");
                }
            }
        }
        finally
        {
            Task settlement = Task.WhenAll(pending);
            await settlement.ConfigureAwait(
                ConfigureAwaitOptions.SuppressThrowing);
        }

        return new SourceIntegrityProbeSummary(
            verified,
            mismatched,
            lineEndingNormalized,
            unverifiable,
            mismatchedFiles);
    }

    private static async Task<SourceIntegrityProbeResult> ProbeSourceAsync(
        SourceDocumentObservation document,
        HttpClient httpClient,
        ISourceLinkQueryCache? cache,
        CancellationToken cancellationToken)
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
            return Result(SourceChecksumVerification.Exact);
        }
        if (immutable
            && cache?.TryGet(
                CacheCategory,
                cacheKey,
                maxAge: null,
                extension: "normalized") != null)
        {
            return Result(SourceChecksumVerification.LineEndingNormalized);
        }

        HttpRetryHelper.HttpBodyFetchResult fetch =
            await HttpRetryHelper.GetBytesAfterHeadersWithRetryAsync(
                httpClient,
                document.ResolvedUrl!,
                response => SourceFetchOriginValidator.Validate(
                    document.ResolvedUrl!,
                    response.RequestMessage?.RequestUri?.AbsoluteUri).IsAllowed,
                log: null,
                cancellationToken: cancellationToken,
                trafficKind: NetworkTrafficKind.SourceIntegrity,
                maxDownloadSize: SourceFetch.MaxSourceDownloadSize)
                .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (fetch.Bytes is not { } body)
        {
            return Result(
                SourceChecksumVerification.Unavailable,
                fetch.Status);
        }

        SourceChecksumVerification verification =
            SourceLinkService.VerifyChecksum(document, body);
        cancellationToken.ThrowIfCancellationRequested();
        return verification switch
        {
            SourceChecksumVerification.Exact => Result(
                SourceChecksumVerification.Exact,
                publishEvidence: immutable),
            SourceChecksumVerification.LineEndingNormalized => Result(
                SourceChecksumVerification.LineEndingNormalized,
                publishEvidence: immutable),
            SourceChecksumVerification.Mismatch => Result(
                SourceChecksumVerification.Mismatch),
            _ => Result(verification),
        };

        SourceIntegrityProbeResult Result(
            SourceChecksumVerification verification,
            HttpRetryHelper.HttpBodyFetchStatus? fetchStatus = null,
            bool publishEvidence = false) =>
            new(
                document,
                cacheKey,
                verification,
                fetchStatus,
                publishEvidence);
    }

    private readonly record struct SourceIntegrityProbeResult(
        SourceDocumentObservation Document,
        string CacheKey,
        SourceChecksumVerification Verification,
        HttpRetryHelper.HttpBodyFetchStatus? FetchStatus,
        bool PublishEvidence);

    private sealed record SourceIntegrityProbeSummary(
        int Verified,
        int Mismatched,
        int LineEndingNormalized,
        int Unverifiable,
        IReadOnlyList<string> MismatchedFiles);
}
