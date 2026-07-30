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
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int take = 20,
        bool prerelease = false,
        CancellationToken cancellationToken = default)
    {
        string pre = prerelease ? "true" : "false";
        string url = $"{_searchUrl}?q={Uri.EscapeDataString(query)}&take={take}&prerelease={pre}";

        using Stream stream = await client.GetStreamAsync(url, cancellationToken).ConfigureAwait(false);
        SearchResponse? response = await NuGetApi.GetSearchResponseAsync(stream, cancellationToken).ConfigureAwait(false);
        return (IReadOnlyList<SearchResult>?)response?.Data ?? [];
    }

    /// <summary>
    /// Searches NuGet for packages whose ID starts with the given prefix.
    /// Filters client-side since the search API doesn't support true prefix matching.
    /// </summary>
    public async Task<IReadOnlyList<SearchResult>> SearchByPrefixAsync(
        string prefix,
        int take = 100,
        bool prerelease = false,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SearchResult> results = await SearchAsync(prefix, take, prerelease, cancellationToken).ConfigureAwait(false);

        return results
            .Where(r => r.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
