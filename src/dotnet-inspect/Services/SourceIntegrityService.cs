using System.Collections.Concurrent;
using DotnetInspector.Core;
using ILInspector.Findings;
using ILInspector.Metadata;
using DotnetInspector.Models;
using DotnetInspector.Output;
using DotnetInspector.Packages;

namespace DotnetInspector.Services;

/// <summary>
/// Slow, opt-in source verification: downloads each SourceLink source body and compares its
/// content hash against the checksum recorded in the PDB. Unlike <see cref="SourceAuditService"/>
/// (HEAD reachability only), this proves the bytes served match what was compiled.
/// </summary>
internal static class SourceIntegrityService
{
    private const string CacheCategory = "source-integrity";

    public static async Task PopulateAsync(
        FindingInspection<SourceDocumentObservation> documentInspection,
        LibraryInspection inspection,
        VerboseLogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documentInspection);
        ArgumentNullException.ThrowIfNull(inspection);
        ArgumentNullException.ThrowIfNull(logger);

        if (documentInspection.Value is not FindingInspection<SourceDocumentObservation>.Complete complete)
            return;

        using var trafficScope = NetworkTelemetry.Scope(NetworkTrafficKind.SourceIntegrity);
        var documents = complete.Findings
            .Select(static finding => finding.Payload)
            .Where(static document => document.IsCompilerLanguageSource)
            .ToArray();

        // Only network-fetchable documents that carry a usable checksum can be verified.
        List<SourceDocumentObservation> verifiable = [];
        int unverifiable = 0;
        foreach (var doc in documents)
        {
            if (doc.Storage == SourceDocumentStorage.Embedded)
                continue; // present in the artifact; nothing to fetch
            if (doc.ResolvedUrl == null || string.IsNullOrEmpty(doc.Checksum) || doc.ChecksumAlgorithm == null
                || !Core.HttpClientFactory.IsAllowedFetchScheme(doc.ResolvedUrl))
            {
                unverifiable++;
                continue;
            }
            verifiable.Add(doc);
        }

        int verified = 0;
        int lineEndingNormalized = 0;
        int mismatched = 0;
        var mismatches = new ConcurrentBag<string>();

        if (verifiable.Count > 0)
        {
            logger.Log($"Verifying integrity of {verifiable.Count} source files...");
            foreach (var host in verifiable
                .Select(d => SafeHost(d.ResolvedUrl!))
                .Where(h => h != null)
                .Distinct())
            {
                logger.Log($"Source integrity fetch host: {host}");
            }

            // Untrusted URLs come from the inspected artifact's PDB: use an SSRF-hardened client
            // that validates every connection (including redirect hops) resolves to a public IP.
            using var fetchClient = Core.HttpClientFactory.CreateUntrustedFetchClient();

            await Parallel.ForEachAsync(verifiable,
                new ParallelOptions { MaxDegreeOfParallelism = 16, CancellationToken = cancellationToken },
                async (doc, ct) =>
                {
                    string cacheKey = $"{doc.ResolvedUrl}|{doc.ChecksumAlgorithm}|{doc.Checksum}";
                    bool immutable = SourceLinkUrls.IsImmutable(doc.ResolvedUrl!);

                    if (immutable && CoreCache.TryGet(CacheCategory, cacheKey, extension: "verified") != null)
                    {
                        Interlocked.Increment(ref verified);
                        return;
                    }
                    if (immutable && CoreCache.TryGet(CacheCategory, cacheKey, extension: "normalized") != null)
                    {
                        Interlocked.Increment(ref verified);
                        Interlocked.Increment(ref lineEndingNormalized);
                        return;
                    }

                    byte[]? body;
                    try
                    {
                        body = await HttpRetryHelper.GetBytesWithRetryAsync(
                            fetchClient, doc.ResolvedUrl!, log: logger.Log, cancellationToken: ct,
                            trafficKind: NetworkTrafficKind.SourceIntegrity);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.Log($"Integrity fetch failed: {doc.ResolvedUrl} ({ex.Message})");
                        body = null;
                    }

                    if (body == null)
                    {
                        Interlocked.Increment(ref unverifiable);
                        return;
                    }

                    var verification = AuthoredSourceAcquisition.VerifyChecksum(
                        doc,
                        body);
                    if (verification == SourceChecksumVerification.Exact)
                    {
                        Interlocked.Increment(ref verified);
                        if (immutable)
                            CoreCache.Set(CacheCategory, cacheKey, "1", extension: "verified");
                    }
                    else if (verification
                        == SourceChecksumVerification.LineEndingNormalized)
                    {
                        Interlocked.Increment(ref verified);
                        Interlocked.Increment(ref lineEndingNormalized);
                        if (immutable)
                            CoreCache.Set(CacheCategory, cacheKey, "1", extension: "normalized");
                    }
                    else if (verification == SourceChecksumVerification.Mismatch)
                    {
                        Interlocked.Increment(ref mismatched);
                        mismatches.Add(doc.OriginalPath);
                        logger.Log($"Integrity MISMATCH: {doc.OriginalPath} ({doc.ResolvedUrl})");
                    }
                    else
                    {
                        Interlocked.Increment(ref unverifiable);
                    }
                });
        }

        inspection.SourceIntegrityChecked = true;
        inspection.SourceIntegrityVerified = verified;
        inspection.SourceIntegrityMismatched = mismatched;
        inspection.SourceIntegrityLineEndingNormalized = lineEndingNormalized;
        inspection.SourceIntegrityUnverifiable = unverifiable;
        inspection.SourceIntegrityMismatches = mismatches.IsEmpty ? null : [.. mismatches.OrderBy(f => f)];
    }

    private static string? SafeHost(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : null;

}
