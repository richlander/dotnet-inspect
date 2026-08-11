using System.Collections.Concurrent;
using System.Collections.Immutable;
using DotnetInspector.Core;
using DotnetInspector.Packages;
using ILInspector.SourceLink;
using SLF = SourceLinkFetch;

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
    private static readonly TimeSpan NegativeCacheTtl = TimeSpan.FromDays(1);
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
        var missingFiles = new ConcurrentBag<string>();
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
            else if (cache?.TryGet(
                CacheCategory,
                document.ResolvedUrl!,
                NegativeCacheTtl,
                "miss") != null)
            {
                missingFiles.Add(document.OriginalPath);
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

            await Parallel.ForEachAsync(
                uncachedDocuments,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = 16,
                    CancellationToken = cancellationToken,
                },
                async (document, ct) =>
                {
                    var result = await HttpRetryHelper.HeadWithRetryResultAsync(
                        httpClient,
                        document.ResolvedUrl!,
                        log: null,
                        cancellationToken: ct,
                        trafficKind: NetworkTrafficKind.SourceAudit).ConfigureAwait(false);
                    using var response = result.Response;
                    string? finalUrl = response?.RequestMessage?.RequestUri?.AbsoluteUri;
                    bool originPreserved = response is not null
                        && finalUrl is not null
                        && SLF.SourceLinkProvenance.ValidateFetchOrigin(
                            document.ResolvedUrl!,
                            finalUrl).IsAllowed;
                    if (originPreserved)
                    {
                        Interlocked.Increment(ref accessibleCount);
                        cache?.Set(
                            CacheCategory,
                            document.ResolvedUrl!,
                            "1",
                            "ok");
                    }
                    else
                    {
                        if (response is not null)
                        {
                            log?.Invoke(
                                "Source fetch left the attributed source origin.");
                        }
                        else if (result.IsNotFound)
                        {
                            cache?.Set(
                                CacheCategory,
                                document.ResolvedUrl!,
                                "1",
                                "miss");
                        }

                        if (response is null)
                            log?.Invoke("Source not accessible.");
                        missingFiles.Add(document.OriginalPath);
                    }
                }).ConfigureAwait(false);
        }

        int totalAccessible = accessibleCount + embeddedFiles;
        return new SourceAvailabilitySummary(
            documents.Length,
            totalAccessible,
            embeddedFiles,
            [.. missingFiles.Order(StringComparer.Ordinal)]);
    }

    private static bool IsBuildArtifact(string filePath) =>
        filePath.Contains("/artifacts/obj/", StringComparison.Ordinal)
        || filePath.Contains("\\artifacts\\obj\\", StringComparison.Ordinal);
}
