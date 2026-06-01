using System.Collections.Concurrent;
using DotnetInspector.Core;
using DotnetInspector.Metadata;
using DotnetInspector.Models;
using DotnetInspector.Output;
using DotnetInspector.Packages;

namespace DotnetInspector.Services;

internal static class SourceAuditService
{
    private static readonly TimeSpan NegativeCacheTtl = TimeSpan.FromDays(1);

    public static async Task PopulateAsync(
        SourceLinkService service,
        LibraryInspection inspection,
        HttpClient httpClient,
        VerboseLogger logger)
    {
        var documents = service.GetTrackedFiles();
        int embeddedFiles = 0;
        int accessibleCount = 0;
        var missingFiles = new ConcurrentBag<string>();
        List<SourceDocument> urlDocs = [];

        foreach (var doc in documents)
        {
            if (doc.IsEmbedded)
            {
                embeddedFiles++;
                continue;
            }

            if (doc.ResolvedUrl == null || IsBuildArtifact(doc.FilePath))
            {
                missingFiles.Add(doc.FilePath);
                continue;
            }

            urlDocs.Add(doc);
        }

        List<SourceDocument> uncachedDocs = [];
        foreach (var doc in urlDocs)
        {
            if (CoreCache.TryGet("source-audit", doc.ResolvedUrl!, extension: "ok") != null)
            {
                Interlocked.Increment(ref accessibleCount);
            }
            else if (CoreCache.TryGet("source-audit", doc.ResolvedUrl!, NegativeCacheTtl, extension: "miss") != null)
            {
                missingFiles.Add(doc.FilePath);
            }
            else
            {
                uncachedDocs.Add(doc);
            }
        }

        if (uncachedDocs.Count > 0)
        {
            logger.Log($"Verifying {uncachedDocs.Count} source URLs ({urlDocs.Count - uncachedDocs.Count} cached)...");

            await Parallel.ForEachAsync(uncachedDocs,
                new ParallelOptions { MaxDegreeOfParallelism = 16 },
                async (doc, ct) =>
                {
                    var result = await HttpRetryHelper.HeadWithRetryResultAsync(
                        httpClient, doc.ResolvedUrl!, log: logger.Log, cancellationToken: ct);
                    using var response = result.Response;
                    if (response != null)
                    {
                        Interlocked.Increment(ref accessibleCount);
                        CoreCache.Set("source-audit", doc.ResolvedUrl!, "1", extension: "ok");
                    }
                    else
                    {
                        if (result.IsNotFound)
                            CoreCache.Set("source-audit", doc.ResolvedUrl!, "1", extension: "miss");
                        logger.Log($"Source not accessible: {doc.ResolvedUrl}");
                        missingFiles.Add(doc.FilePath);
                    }
                });
        }

        int totalAccessible = accessibleCount + embeddedFiles;
        inspection.TotalSourceFiles = documents.Count;
        inspection.AccessibleSourceFiles = totalAccessible;
        inspection.EmbeddedSourceFiles = embeddedFiles;
        inspection.MissingSourceFiles = missingFiles.IsEmpty ? null : [.. missingFiles.OrderBy(f => f)];
        inspection.AllSourcesAccessible = documents.Count > 0 && totalAccessible >= documents.Count;
    }

    private static bool IsBuildArtifact(string filePath) =>
        filePath.Contains("/artifacts/obj/") || filePath.Contains("\\artifacts\\obj\\");
}
