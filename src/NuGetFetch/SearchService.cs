using System.Globalization;
using System.Net.Http.Headers;

namespace NuGetFetch;

/// <summary>
/// Searches the NuGet Search API for packages by keyword or prefix.
/// </summary>
public partial class SearchService
{
    private const int PrefixSearchPageSize = 100;
    private const int MaxPrefixSearchPages = 32;
    private readonly HttpClient _client;
    private readonly NuGetFetchOptions _options;
    private readonly string _searchUrl;

    /// <summary>
    /// Creates a NuGet search client with default resource limits and deadlines.
    /// </summary>
    public SearchService(HttpClient client, string? searchUrl = null)
        : this(client, searchUrl, new NuGetFetchOptions())
    {
    }

    /// <summary>
    /// Creates a NuGet search client with configured resource limits and deadlines.
    /// </summary>
    public SearchService(
        HttpClient client,
        string? searchUrl,
        NuGetFetchOptions options)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _searchUrl = searchUrl ?? NuGetClient.NuGetOrgSearchUrl;
        _options = NuGetFetchOptions.Validate(options);
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
    {
        using var operation = new NuGetOperationDeadline(
            _options,
            _client.Timeout,
            cancellationToken);
        return await SearchAsync(
            query,
            take,
            prerelease,
            auth,
            operation).ConfigureAwait(false);
    }

    internal async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int take,
        bool prerelease,
        AuthenticationHeaderValue? auth,
        NuGetOperationDeadline operation) =>
        await SearchPageAsync(
            query,
            skip: 0,
            take,
            prerelease,
            auth,
            operation).ConfigureAwait(false);

    private async Task<IReadOnlyList<SearchResult>> SearchPageAsync(
        string query,
        int skip,
        int take,
        bool prerelease,
        AuthenticationHeaderValue? auth,
        NuGetOperationDeadline operation)
    {
        string pre = prerelease ? "true" : "false";
        if (!SearchRequestUri.TryCompose(
                _searchUrl,
                [
                    ("q", query),
                    ("skip", skip.ToString(CultureInfo.InvariantCulture)),
                    ("take", take.ToString(CultureInfo.InvariantCulture)),
                    ("prerelease", pre),
                    ("semVerLevel", "2.0.0"),
                ],
                out string url))
        {
            // The endpoint is feed-declared and may carry a signature, so it is
            // not named here; the caller's adapter reports the source and the
            // redacted endpoint.
            throw new InvalidOperationException(
                "The search endpoint is not a usable absolute HTTP or HTTPS URL.");
        }

        SearchResponse? parsed = await operation.RunRequestAsync(
            async requestToken =>
            {
                using HttpRequestMessage request =
                    NuGetHttpRequest.CreateGetPreservingPathAndQuery(url);
                if (auth is not null)
                {
                    request.Headers.Authorization = auth;
                }

                using HttpResponseMessage response = await _client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    requestToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                return await NuGetMetadataReader.ReadResponseAsync(
                    response,
                    NuGetApi.DeserializeSearchResponseAsync,
                    _options,
                    _client.Timeout,
                    requestToken).ConfigureAwait(false);
            }).ConfigureAwait(false);

        // A null document is not an empty result set. Reporting it as one would
        // hide the failure behind a successful-looking zero-result search. The
        // endpoint is not named: it is feed-declared metadata that can carry a
        // signature, and this message reaches a caller's failure list.
        IReadOnlyList<SearchResult> results = parsed?.Data
            ?? throw new InvalidOperationException(
                "The search response was not a valid NuGet search document.");
        foreach (SearchResult result in results)
        {
            operation.ThrowIfExpired();
            if (!IsValidResult(result, operation))
            {
                throw new InvalidOperationException(
                    "The search response contained an invalid result identity.");
            }
        }

        operation.ThrowIfExpired();
        return results;
    }

    private static bool IsValidResult(
        SearchResult? result,
        NuGetOperationDeadline operation)
    {
        if (result is null
            || !PackageCoordinateValidation.IsValidPackageId(result.Id)
            || !PackageCoordinateValidation.IsValidPackageVersion(
                result.Version))
        {
            return false;
        }

        if (result.Versions is null)
            return true;

        foreach (SearchVersion version in result.Versions)
        {
            operation.ThrowIfExpired();
            if (version is null
                || !PackageCoordinateValidation.IsValidPackageVersion(
                    version.Version))
            {
                return false;
            }
        }

        return true;
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
        using var operation = new NuGetOperationDeadline(
            _options,
            _client.Timeout,
            cancellationToken);

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
                operation).ConfigureAwait(false);
            if (page.Count == 0)
                return matches;

            bool madeProgress = false;
            foreach (SearchResult result in page)
            {
                operation.ThrowIfExpired();
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

        operation.ThrowIfExpired();
        return matches;
    }
}
