using System.Globalization;
using System.Net.Http.Headers;

namespace NuGetFetch;

/// <summary>
/// Searches the NuGet Search API for packages by keyword or prefix.
/// </summary>
public class SearchService
{
    private const int PrefixSearchPageSize = 100;
    private const int MaxPrefixSearchPages = 32;
    private static readonly TimeSpan PrefixSearchTimeout = TimeSpan.FromSeconds(30);
    private readonly HttpClient _client;
    private readonly NuGetFetchOptions _options;
    private readonly string _searchUrl;

    /// <summary>
    /// Creates a NuGet search client with default metadata response limits.
    /// </summary>
    public SearchService(HttpClient client, string? searchUrl = null)
        : this(client, searchUrl, new NuGetFetchOptions())
    {
    }

    /// <summary>
    /// Creates a NuGet search client with configured metadata response limits.
    /// </summary>
    public SearchService(
        HttpClient client,
        string? searchUrl,
        NuGetFetchOptions options)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _searchUrl = searchUrl ?? NuGetClient.NuGetOrgSearchUrl;
        _options = NuGetFetchOptions.ForClient(options, client.Timeout);
    }

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
        => await SearchPageAsync(
            query,
            skip: 0,
            take,
            prerelease,
            auth,
            cancellationToken).ConfigureAwait(false);

    private async Task<IReadOnlyList<SearchResult>> SearchPageAsync(
        string query,
        int skip,
        int take,
        bool prerelease,
        AuthenticationHeaderValue? auth,
        CancellationToken cancellationToken)
    {
        string pre = prerelease ? "true" : "false";
        if (!SearchRequestUri.TryCompose(
                _searchUrl,
                [
                    ("q", query),
                    ("skip", skip.ToString(CultureInfo.InvariantCulture)),
                    ("take", take.ToString(CultureInfo.InvariantCulture)),
                    ("prerelease", pre),
                ],
                out string url))
        {
            // The endpoint is feed-declared and may carry a signature, so it is
            // not named here; the caller's adapter reports the source and the
            // redacted endpoint.
            throw new InvalidOperationException(
                "The search endpoint is not a usable absolute HTTP or HTTPS URL.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (auth is not null)
        {
            request.Headers.Authorization = auth;
        }

        using HttpResponseMessage response = await _client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        SearchResponse? parsed = await NuGetMetadataReader.ReadResponseAsync(
            response,
            NuGetApi.DeserializeSearchResponseAsync,
            _options,
            cancellationToken).ConfigureAwait(false);

        // A null document is not an empty result set. Reporting it as one would
        // hide the failure behind a successful-looking zero-result search. The
        // endpoint is not named: it is feed-declared metadata that can carry a
        // signature, and this message reaches a caller's failure list.
        return parsed?.Data
            ?? throw new InvalidOperationException(
                "The search response was not a valid NuGet search document.");
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
        List<SearchResult> matches = [];
        var matchedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var observedResults = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int skip = 0;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(PrefixSearchTimeout);

        for (int pageNumber = 0;
            pageNumber < MaxPrefixSearchPages && matches.Count < take;
            pageNumber++)
        {
            IReadOnlyList<SearchResult> page = await SearchPageAsync(
                prefix,
                skip,
                PrefixSearchPageSize,
                prerelease,
                auth,
                timeout.Token).ConfigureAwait(false);
            if (page.Count == 0)
                return matches;

            bool madeProgress = false;
            foreach (SearchResult result in page)
            {
                madeProgress |= observedResults.Add(
                    $"{result.Id.Length}:{result.Id}{result.Version}");
                if (result.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && matchedIds.Add(result.Id))
                {
                    matches.Add(result);
                    if (matches.Count == take)
                        break;
                }
            }

            if (!madeProgress)
                throw new InvalidOperationException(
                    "NuGet search pagination repeated a page without making progress.");

            skip += page.Count;
        }

        if (matches.Count < take)
            throw new InvalidOperationException(
                $"NuGet search pagination exceeded {MaxPrefixSearchPages} pages.");

        return matches;
    }
}
