using System.Collections.Immutable;
using DotnetInspector.Packages;
using ILInspector.SourceLink;

namespace DotnetInspector.Services;

/// <summary>The completed reachability audit for one assembly's source documents.</summary>
public sealed record SourceAvailabilitySummary(
    int TotalSourceFiles,
    int AccessibleSourceFiles,
    int EmbeddedSourceFiles,
    ImmutableArray<string> MissingSourceFiles)
{
    public bool AllSourcesAccessible =>
        TotalSourceFiles > 0 && AccessibleSourceFiles >= TotalSourceFiles;
}

/// <summary>
/// Probes SourceLink document URLs without carrying CLI models or presentation concerns.
/// </summary>
public static class SourceAvailabilityService
{
    private const string CacheCategory = "source-audit-v2";
    private const int MaximumConcurrentHeads = 16;
    private static readonly TimeSpan MutablePositiveCacheTtl = TimeSpan.FromDays(1);

    public static async Task<SourceAvailabilitySummary> InspectAsync(
        IEnumerable<SourceDocumentObservation> sourceDocuments,
        HttpClient httpClient,
        ISourceLinkQueryCache? cache = null,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceDocuments);
        ArgumentNullException.ThrowIfNull(httpClient);

        using var trafficScope = NetworkTelemetry.Scope(NetworkTrafficKind.SourceAudit);
        var documents = sourceDocuments
            .Where(static document => document.IsCompilerLanguageSource)
            .ToArray();
        int embeddedFiles = 0;
        int accessibleCount = 0;
        List<string> missingFiles = [];
        List<SourceDocumentObservation> urlDocuments = [];

        foreach (SourceDocumentObservation document in documents)
        {
            if (document.Storage == SourceDocumentStorage.Embedded)
            {
                embeddedFiles++;
                continue;
            }

            if (document.ResolvedUrl == null
                || IsBuildArtifact(document.OriginalPath)
                || !HttpClientFactory.IsAllowedFetchScheme(document.ResolvedUrl))
            {
                missingFiles.Add(document.OriginalPath);
                continue;
            }

            urlDocuments.Add(document);
        }

        List<SourceDocumentObservation> uncachedDocuments = [];
        foreach (SourceDocumentObservation document in urlDocuments)
        {
            bool immutable = SourceLinkUrls.IsImmutable(document.ResolvedUrl!);
            string? positiveHit = cache?.TryGet(
                CacheCategory,
                document.ResolvedUrl!,
                immutable ? null : MutablePositiveCacheTtl,
                "ok");

            if (positiveHit != null)
            {
                accessibleCount++;
            }
            else
            {
                uncachedDocuments.Add(document);
            }
        }

        if (uncachedDocuments.Count > 0)
        {
            log?.Invoke(
                $"Verifying {uncachedDocuments.Count} source URLs ({urlDocuments.Count - uncachedDocuments.Count} cached)...");

            SourceProbeSummary probes = await ProbeSourcesAsync(
                uncachedDocuments,
                httpClient,
                cache,
                log,
                cancellationToken).ConfigureAwait(false);
            accessibleCount += probes.AccessibleCount;
            missingFiles.AddRange(probes.MissingFiles);
        }

        int totalAccessible = accessibleCount + embeddedFiles;
        return new SourceAvailabilitySummary(
            documents.Length,
            totalAccessible,
            embeddedFiles,
            [.. missingFiles.Order(StringComparer.Ordinal)]);
    }

    private static async Task<SourceProbeSummary> ProbeSourcesAsync(
        IReadOnlyList<SourceDocumentObservation> documents,
        HttpClient httpClient,
        ISourceLinkQueryCache? cache,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        int accessibleCount = 0;
        List<string> missingFiles = [];
        var pending = new List<Task<SourceProbeResult>>();
        int next = 0;

        try
        {
            while (next < documents.Count || pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                while (next < documents.Count
                    && pending.Count < MaximumConcurrentHeads)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    pending.Add(ProbeSourceAsync(
                        documents[next],
                        httpClient,
                        cancellationToken));
                    next++;
                }

                Task<SourceProbeResult> completed =
                    await Task.WhenAny(pending).ConfigureAwait(false);
                pending.Remove(completed);
                SourceProbeResult probe = await completed.ConfigureAwait(false);
                if (probe.OriginPreserved)
                {
                    accessibleCount++;
                    cache?.Set(
                        CacheCategory,
                        probe.Document.ResolvedUrl!,
                        "1",
                        "ok");
                }
                else
                {
                    if (probe.ReceivedResponse)
                    {
                        log?.Invoke(
                            "Could not verify the final SourceLink response origin.");
                    }
                    else
                    {
                        log?.Invoke("Source not accessible.");
                    }

                    missingFiles.Add(probe.Document.OriginalPath);
                }
            }
        }
        finally
        {
            Task settlement = Task.WhenAll(pending);
            await settlement.ConfigureAwait(
                ConfigureAwaitOptions.SuppressThrowing);
        }

        return new SourceProbeSummary(accessibleCount, missingFiles);
    }

    private static async Task<SourceProbeResult> ProbeSourceAsync(
        SourceDocumentObservation document,
        HttpClient httpClient,
        CancellationToken cancellationToken)
    {
        var result = await HttpRetryHelper.HeadWithRetryResultAsync(
            httpClient,
            document.ResolvedUrl!,
            log: null,
            cancellationToken: cancellationToken,
            trafficKind: NetworkTrafficKind.SourceAudit).ConfigureAwait(false);
        using var response = result.Response;
        cancellationToken.ThrowIfCancellationRequested();
        string? finalUrl = response?.RequestMessage?.RequestUri?.AbsoluteUri;
        bool originPreserved = response is not null
            && SourceFetchOriginValidator.Validate(
                document.ResolvedUrl!,
                finalUrl).IsAllowed;
        return new SourceProbeResult(
            document,
            originPreserved,
            response is not null);
    }

    private static bool IsBuildArtifact(string filePath) =>
        filePath.Contains("/artifacts/obj/", StringComparison.Ordinal)
        || filePath.Contains("\\artifacts\\obj\\", StringComparison.Ordinal);

    private readonly record struct SourceProbeResult(
        SourceDocumentObservation Document,
        bool OriginPreserved,
        bool ReceivedResponse);

    private sealed record SourceProbeSummary(
        int AccessibleCount,
        IReadOnlyList<string> MissingFiles);
}
