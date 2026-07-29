using System.Net.Http.Headers;

namespace NuGetFetch;

/// <summary>
/// Searches the NuGet Search API for packages by keyword or prefix.
/// </summary>
public class SearchService(HttpClient client, string? searchUrl = null)
{
    private readonly string _searchUrl = searchUrl ?? NuGetClient.NuGetOrgSearchUrl;

    /// <summary>
    /// Searches NuGet for packages matching the given query.
    /// </summary>
    /// <remarks>
    /// Retry, telemetry, and credential acquisition belong to the caller: this
    /// type stays a leaf and reaches for nothing beyond the supplied
    /// <see cref="HttpClient"/> and the optional header it is handed.
    /// </remarks>
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int take = 20,
        bool prerelease = false,
        AuthenticationHeaderValue? auth = null,
        CancellationToken cancellationToken = default)
    {
        string pre = prerelease ? "true" : "false";
        string url = $"{_searchUrl}?q={Uri.EscapeDataString(query)}&take={take}&prerelease={pre}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (auth is not null)
        {
            request.Headers.Authorization = auth;
        }

        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        SearchResponse? parsed = await NuGetApi.GetSearchResponseAsync(stream, cancellationToken).ConfigureAwait(false);

        // A null document is not an empty result set. Reporting it as one would
        // hide the failure behind a successful-looking zero-result search.
        return parsed?.Data
            ?? throw new InvalidOperationException(
                $"Search response from '{_searchUrl}' was not a valid NuGet search document.");
    }

    /// <summary>
    /// Searches NuGet for packages whose ID starts with the given prefix.
    /// Filters client-side since the search API doesn't support true prefix matching.
    /// </summary>
    public async Task<IReadOnlyList<SearchResult>> SearchByPrefixAsync(
        string prefix,
        int take = 100,
        bool prerelease = false,
        AuthenticationHeaderValue? auth = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SearchResult> results = await SearchAsync(
            prefix, take, prerelease, auth, cancellationToken).ConfigureAwait(false);

        return results
            .Where(r => r.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
