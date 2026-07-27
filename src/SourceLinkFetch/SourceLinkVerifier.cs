namespace SourceLinkFetch;

/// <summary>
/// Result of verifying a single source document's URL accessibility.
/// </summary>
public record VerificationResult(string FilePath, string? Url, bool IsAccessible, string? Error);

/// <summary>
/// Verifies that SourceLink URLs are accessible via HTTP HEAD requests.
/// </summary>
public static class SourceLinkVerifier
{
    /// <summary>
    /// Verifies all source documents by sending HTTP HEAD requests to their resolved URLs.
    /// </summary>
    /// <param name="documents">Source documents with resolved URLs.</param>
    /// <param name="client">HttpClient to use for requests.</param>
    /// <param name="maxConcurrency">Maximum parallel requests (default 16).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<IReadOnlyList<VerificationResult>> VerifyAsync(
        IEnumerable<SourceDocument> documents,
        HttpClient client,
        int maxConcurrency = 16,
        CancellationToken cancellationToken = default)
    {
        var docsWithUrls = documents.Where(d => d.ResolvedUrl is not null).ToList();

        if (docsWithUrls.Count == 0)
            return [];

        using var semaphore = new SemaphoreSlim(maxConcurrency);
        var tasks = docsWithUrls.Select(doc => VerifyOneAsync(doc, client, semaphore, cancellationToken));
        var results = await Task.WhenAll(tasks);
        return results;
    }

    private static async Task<VerificationResult> VerifyOneAsync(
        SourceDocument doc, HttpClient client, SemaphoreSlim semaphore, CancellationToken ct)
    {
        await semaphore.WaitAsync(ct);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, doc.ResolvedUrl);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            return new VerificationResult(doc.FilePath, doc.ResolvedUrl,
                response.IsSuccessStatusCode, null);
        }
        catch (Exception ex)
        {
            return new VerificationResult(doc.FilePath, doc.ResolvedUrl,
                false, ex.Message);
        }
        finally
        {
            semaphore.Release();
        }
    }
}
