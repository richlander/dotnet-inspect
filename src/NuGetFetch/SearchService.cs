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

        async Task<SearchResponse?> SendAsync(
            CancellationToken requestToken)
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
                operation.RequestTimeout,
                requestToken).ConfigureAwait(false);
        }

        SearchResponse? parsed = _retryTransientRequests
            ? await NuGetHttpRetry.RunRequestAsync(
                operation,
                SendAsync).ConfigureAwait(false)
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
        PrefixSearchResult result = await SearchByPrefixWithStateAsync(
            prefix,
            take,
            prerelease,
            auth,
            maximumSkip: null,
            cancellationToken).ConfigureAwait(false);
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
            operation).ConfigureAwait(false);
    }

    internal async Task<PrefixSearchResult> SearchByPrefixWithStateAsync(
        string prefix,
        int take,
        bool prerelease,
        AuthenticationHeaderValue? auth,
        int? maximumSkip,
        NuGetOperationDeadline operation)
    {
        PrefixSearchCursor cursor = CreatePrefixSearchCursor(
            prefix, take, prerelease, auth, maximumSkip);
        List<SearchResult> matches = [];
        while (!cursor.IsCompleted)
        {
            PrefixSearchPage page =
                await cursor.ReadNextAsync(operation).ConfigureAwait(false);
            matches.AddRange(page.Matches);
        }

        operation.ThrowIfExpired();
        return new PrefixSearchResult(matches, cursor.Completion!.Value);
    }

    internal PrefixSearchCursor CreatePrefixSearchCursor(
        string prefix,
        int take,
        bool prerelease,
        AuthenticationHeaderValue? auth,
        int? maximumSkip)
    {
        if (maximumSkip < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumSkip));

        return new PrefixSearchCursor(
            this, prefix, take, prerelease, auth, maximumSkip);
    }

    internal sealed record PrefixSearchPage(
        IReadOnlyList<SearchResult> Matches,
        PrefixSearchCompletion? Completion);

    internal sealed class PrefixSearchCursor(
        SearchService service,
        string prefix,
        int take,
        bool prerelease,
        AuthenticationHeaderValue? auth,
        int? maximumSkip)
    {
        private readonly HashSet<string> _matchedIds =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _observedResults =
            new(StringComparer.OrdinalIgnoreCase);
        private int _skip;
        private int _pageNumber;

        internal PrefixSearchCompletion? Completion { get; private set; }
        internal bool IsCompleted => Completion.HasValue;

        internal async Task<PrefixSearchPage> ReadNextAsync(
            NuGetOperationDeadline operation)
        {
            operation.ThrowIfExpired();
            if (_matchedIds.Count >= take)
            {
                Completion = PrefixSearchCompletion.ClientPageLimitReached;
                return new([], Completion);
            }

            IReadOnlyList<SearchResult> page =
                await service.SearchPageAsync(
                    prefix,
                    _skip,
                    PrefixSearchPageSize,
                    prerelease,
                    auth,
                    operation).ConfigureAwait(false);
            _pageNumber++;
            if (page.Count == 0)
            {
                Completion = PrefixSearchCompletion.Complete;
                return new([], Completion);
            }

            List<SearchResult> matches = [];
            bool madeProgress = false;
            foreach (SearchResult result in page)
            {
                operation.ThrowIfExpired();
                madeProgress |= _observedResults.Add(
                    $"{result.Id.Length}:{result.Id}{result.Version}");
                if (result.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && _matchedIds.Add(result.Id))
                {
                    matches.Add(result);
                    if (_matchedIds.Count == take)
                    {
                        Completion = PrefixSearchCompletion.TakeReached;
                        break;
                    }
                }
            }

            if (!madeProgress)
                throw new InvalidOperationException(
                    "NuGet search pagination repeated a page without making progress.");

            _skip += page.Count;
            if (!IsCompleted)
            {
                Completion = _pageNumber >= MaxPrefixSearchPages
                    ? PrefixSearchCompletion.ClientPageLimitReached
                    : maximumSkip is int skipLimit && _skip > skipLimit
                        ? PrefixSearchCompletion.SourcePageLimitReached
                        : null;
            }

            operation.ThrowIfExpired();
            return new(matches, Completion);
        }
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
