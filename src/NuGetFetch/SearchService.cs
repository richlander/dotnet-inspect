using System.Globalization;
using System.Net.Http.Headers;

namespace NuGetFetch;

/// <summary>
/// Searches the NuGet Search API for packages by keyword or prefix.
/// </summary>
public partial class SearchService
{
    private const int PrefixSearchPageSize = 100;
    private const int MaxPrefixSearchPages = 100;
    private readonly HttpClient _client;
    private readonly NuGetFetchOptions _options;
    private readonly bool _retryTransientRequests;
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
        : this(
            client,
            searchUrl,
            options,
            retryTransientRequests: false)
    {
    }

    internal SearchService(
        HttpClient client,
        string? searchUrl,
        NuGetFetchOptions options,
        bool retryTransientRequests)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _searchUrl = searchUrl ?? NuGetClient.NuGetOrgSearchUrl;
        _options = NuGetFetchOptions.Validate(options);
        _retryTransientRequests = retryTransientRequests;
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
            operation,
            pluginAuthenticationSourceUrl: null).ConfigureAwait(false);
    }

    internal async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int take,
        bool prerelease,
        AuthenticationHeaderValue? auth,
        NuGetOperationDeadline operation,
        string? pluginAuthenticationSourceUrl = null) =>
        await SearchPageAsync(
            query,
            skip: 0,
            take,
            prerelease,
            auth,
            operation,
            pluginAuthenticationSourceUrl).ConfigureAwait(false);

    private async Task<IReadOnlyList<SearchResult>> SearchPageAsync(
        string query,
        int skip,
        int take,
        bool prerelease,
        AuthenticationHeaderValue? auth,
        NuGetOperationDeadline operation,
        string? pluginAuthenticationSourceUrl)
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

        async Task<SearchResponse?> SendAsync(
            CancellationToken requestToken)
        {
            using HttpRequestMessage request =
                NuGetHttpRequest.CreateGetPreservingPathAndQuery(url);
            if (auth is not null)
            {
                request.Headers.Authorization = auth;
            }
            NuGetSourceRequest.SuppressPluginAuthenticationForCrossOrigin(
                request,
                pluginAuthenticationSourceUrl,
                url);

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
        }

        SearchResponse? parsed = _retryTransientRequests
            ? await NuGetHttpRetry.RunRequestAsync(
                operation,
                SendAsync,
                retryDeadlineExpirations: false).ConfigureAwait(false)
            : await operation.RunRequestAsync(
                SendAsync).ConfigureAwait(false);

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
        using var operation = new NuGetOperationDeadline(
            _options,
            _client.Timeout,
            cancellationToken);
        return await SearchByPrefixAsync(
            prefix,
            take,
            prerelease,
            auth,
            operation,
            pluginAuthenticationSourceUrl: null).ConfigureAwait(false);
    }

    internal async Task<IReadOnlyList<SearchResult>> SearchByPrefixAsync(
        string prefix,
        int take,
        bool prerelease,
        AuthenticationHeaderValue? auth,
        NuGetOperationDeadline operation,
        string? pluginAuthenticationSourceUrl = null)
    {
        PrefixSearchResult result = await SearchByPrefixWithStateAsync(
            prefix,
            take,
            prerelease,
            auth,
            maximumSkip: null,
            operation,
            pluginAuthenticationSourceUrl).ConfigureAwait(false);
        if (result.Completion
            is PrefixSearchCompletion.SourcePageLimitReached
                or PrefixSearchCompletion.ClientPageLimitReached
            && result.Matches.Count < take)
        {
            throw new InvalidOperationException(
                "NuGet prefix-search pagination ended before the requested result count could be established.");
        }

        return result.Matches;
    }

    /// <summary>
    /// Searches by package-ID prefix while retaining whether a source or client
    /// pagination boundary prevented an exhaustive answer.
    /// </summary>
    public async Task<PrefixSearchResult> SearchByPrefixWithStateAsync(
        string prefix,
        int take = 100,
        bool prerelease = false,
        AuthenticationHeaderValue? auth = null,
        int? maximumSkip = null,
        CancellationToken cancellationToken = default)
    {
        using var operation = new NuGetOperationDeadline(
            _options,
            _client.Timeout,
            cancellationToken);
        return await SearchByPrefixWithStateAsync(
            prefix,
            take,
            prerelease,
            auth,
            maximumSkip,
            operation,
            pluginAuthenticationSourceUrl: null).ConfigureAwait(false);
    }

    internal async Task<PrefixSearchResult> SearchByPrefixWithStateAsync(
        string prefix,
        int take,
        bool prerelease,
        AuthenticationHeaderValue? auth,
        int? maximumSkip,
        NuGetOperationDeadline operation,
        string? pluginAuthenticationSourceUrl = null)
    {
        if (maximumSkip < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumSkip));

        List<SearchResult> matches = [];
        var matchedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var observedResults = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int skip = 0;
        for (int pageNumber = 0;
            pageNumber < MaxPrefixSearchPages && matches.Count < take;
            pageNumber++)
        {
            if (maximumSkip is int skipLimit && skip > skipLimit)
            {
                operation.ThrowIfExpired();
                return new PrefixSearchResult(
                    matches,
                    PrefixSearchCompletion.SourcePageLimitReached);
            }

            IReadOnlyList<SearchResult> page = await SearchPageAsync(
                prefix,
                skip,
                PrefixSearchPageSize,
                prerelease,
                auth,
                operation,
                pluginAuthenticationSourceUrl).ConfigureAwait(false);
            if (page.Count == 0)
            {
                return new PrefixSearchResult(
                    matches,
                    PrefixSearchCompletion.Complete);
            }

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
                    {
                        operation.ThrowIfExpired();
                        return new PrefixSearchResult(
                            matches,
                            PrefixSearchCompletion.TakeReached);
                    }
                }
            }

            if (!madeProgress)
                throw new InvalidOperationException(
                    "NuGet search pagination repeated a page without making progress.");

            skip += page.Count;
        }

        operation.ThrowIfExpired();
        return new PrefixSearchResult(
            matches,
            PrefixSearchCompletion.ClientPageLimitReached);
    }
}

/// <summary>Why a package-prefix search stopped.</summary>
public enum PrefixSearchCompletion
{
    /// <summary>The source returned an empty page.</summary>
    Complete,

    /// <summary>The caller's requested match count was reached.</summary>
    TakeReached,

    /// <summary>The source's documented skip boundary was reached.</summary>
    SourcePageLimitReached,

    /// <summary>The client's bounded page ceiling was reached.</summary>
    ClientPageLimitReached,
}

/// <summary>A bounded prefix-search result with explicit completion state.</summary>
public sealed record PrefixSearchResult(
    IReadOnlyList<SearchResult> Matches,
    PrefixSearchCompletion Completion)
{
    public bool Truncated => Completion != PrefixSearchCompletion.Complete;
}
