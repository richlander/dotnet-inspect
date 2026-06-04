using System.Collections.Concurrent;
using System.Security.Cryptography;
using DotnetInspector.Core;
using DotnetInspector.Metadata;
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
        SourceLinkService service,
        LibraryInspection inspection,
        VerboseLogger logger,
        CancellationToken cancellationToken = default)
    {
        var documents = service.GetTrackedFiles();

        // Only network-fetchable documents that carry a usable checksum can be verified.
        List<SourceDocument> verifiable = [];
        int unverifiable = 0;
        foreach (var doc in documents)
        {
            if (doc.IsEmbedded)
                continue; // present in the artifact; nothing to fetch
            if (doc.ResolvedUrl == null || doc.Checksum is not { Length: > 0 } || doc.ChecksumAlgorithm == null
                || !IsAllowedScheme(doc.ResolvedUrl))
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
                    string cacheKey = $"{doc.ResolvedUrl}|{doc.ChecksumAlgorithm}|{Convert.ToHexString(doc.Checksum!)}";
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
                            fetchClient, doc.ResolvedUrl!, log: logger.Log, cancellationToken: ct);
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

                    if (HashMatches(doc.ChecksumAlgorithm!, body, doc.Checksum!))
                    {
                        Interlocked.Increment(ref verified);
                        if (immutable)
                            CoreCache.Set(CacheCategory, cacheKey, "1", extension: "verified");
                    }
                    else if (HashMatchesAfterLineEndingNormalization(doc.ChecksumAlgorithm!, body, doc.Checksum!))
                    {
                        Interlocked.Increment(ref verified);
                        Interlocked.Increment(ref lineEndingNormalized);
                        if (immutable)
                            CoreCache.Set(CacheCategory, cacheKey, "1", extension: "normalized");
                    }
                    else
                    {
                        Interlocked.Increment(ref mismatched);
                        mismatches.Add(doc.FilePath);
                        logger.Log($"Integrity MISMATCH: {doc.FilePath} ({doc.ResolvedUrl})");
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

    private static bool IsAllowedScheme(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    private static string? SafeHost(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : null;

    private static bool HashMatches(string algorithm, byte[] content, byte[] expected)
        => ComputeHash(algorithm, content).AsSpan().SequenceEqual(expected);

    private static bool HashMatchesAfterLineEndingNormalization(string algorithm, byte[] content, byte[] expected)
    {
        if (!content.Contains((byte)'\n') && !content.Contains((byte)'\r'))
            return false;

        return HashMatches(algorithm, NormalizeLineEndings(content, crlf: false), expected)
            || HashMatches(algorithm, NormalizeLineEndings(content, crlf: true), expected);
    }

    private static byte[] NormalizeLineEndings(byte[] content, bool crlf)
    {
        List<byte> lf = new(content.Length);
        for (var i = 0; i < content.Length; i++)
        {
            if (content[i] == '\r')
            {
                if (i + 1 < content.Length && content[i + 1] == '\n')
                    continue;

                lf.Add((byte)'\n');
                continue;
            }

            lf.Add(content[i]);
        }

        if (!crlf)
            return [.. lf];

        List<byte> result = new(lf.Count);
        foreach (var b in lf)
        {
            if (b == '\n')
            {
                result.Add((byte)'\r');
                result.Add((byte)'\n');
            }
            else
            {
                result.Add(b);
            }
        }

        return [.. result];
    }

    private static byte[] ComputeHash(string algorithm, byte[] content) => algorithm switch
    {
        "SHA256" => SHA256.HashData(content),
        "SHA1" => SHA1.HashData(content),
        _ => [],
    };
}
