// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Text.Json;
using NuGetFetch;
using DotnetInspector.Core;
using InertText;
using NuGet.Versioning;
using NuGetSource = NuGetFetch.PackageSource;

namespace DotnetInspector.Packages;

/// <summary>
/// Package-layer projection of a NuGet search result.
/// </summary>
public record NuGetSearchResult(
    string PackageId,
    string Version,
    string? Description,
    long TotalDownloads,
    bool Verified)
{
    /// <summary>
    /// Creates from a NuGetFetch.SearchResult.
    /// </summary>
    public static NuGetSearchResult From(SearchResult r) =>
        new(r.Id, r.Version, r.Description, r.TotalDownloads, r.Verified);
}

/// <summary>
/// The outcome of a package search: the results that were obtained, plus a description of every
/// configured source that could not be searched. Failures are carried rather than discarded so a
/// partially-searched feed set never renders as a confident empty result.
/// </summary>
public record NuGetSearchOutcome(
    IReadOnlyList<NuGetSearchResult> Results,
    IReadOnlyList<string> Failures);

/// <summary>
/// Searches NuGet by delegating to NuGetFetch.SearchService.
/// </summary>
public static class NuGetSearchService
{
    private const int MaxEquivalentSearchEndpoints = 4;

    /// <summary>
    /// Searches the resolved NuGet sources. Resolution always runs, so a NuGet.config discovered
    /// from the working directory is honored even when no source option was passed. Each resolved
    /// HTTP source has its SearchQueryService endpoint discovered from its V3 service index and is
    /// searched with its own credentials.
    /// </summary>
    public static async Task<NuGetSearchOutcome> SearchAsync(
        HttpClient client,
        string query,
        int take = 20,
        bool prerelease = false,
        Action<string>? log = null,
        NuGetSourceOptions? sourceOptions = null,
        NuGetFetchOptions? fetchOptions = null)
    {
        List<NuGetSource> sources = NuGetSourceResolver.ResolveSources(sourceOptions);
        PackageSourceMapping mapping =
            NuGetSourceResolver.ResolvePackageSourceMapping(sourceOptions);
        fetchOptions ??= new NuGetFetchOptions();
        return await SearchResolvedAsync(
            client,
            fetchOptions,
            sources,
            mapping,
            query,
            take,
            prerelease,
            log,
            resultFilter: null).ConfigureAwait(false);
    }

    private static async Task<NuGetSearchOutcome> SearchResolvedAsync(
        HttpClient client,
        NuGetFetchOptions fetchOptions,
        List<NuGetSource> sources,
        PackageSourceMapping mapping,
        string query,
        int take,
        bool prerelease,
        Action<string>? log,
        Func<SearchResult, bool>? resultFilter)
    {
        using var trafficScope = NetworkTelemetry.Scope(NetworkTrafficKind.PackageSearch);

        return await SearchSourcesAsync(
            client,
            fetchOptions,
            sources,
            mapping,
            query,
            take,
            prerelease,
            log,
            resultFilter).ConfigureAwait(false);
    }

    private static async Task<NuGetSearchOutcome> SearchSourcesAsync(
        HttpClient client,
        NuGetFetchOptions fetchOptions,
        List<NuGetSource> sources,
        PackageSourceMapping mapping,
        string query,
        int take,
        bool prerelease,
        Action<string>? log,
        Func<SearchResult, bool>? resultFilter)
    {
        List<NuGetSearchResult> results = [];
        List<string> failures = [];
        HashSet<(string Id, NuGetVersion Version)> seen =
            new(SearchResultKeyComparer.Instance);
        int searched = 0;
        bool useFactoryClients =
            ReferenceEquals(client, HttpClientFactory.Shared);
        _ = NuGetFetchOptions.RequestTimeoutForClient(
            fetchOptions,
            client.Timeout);
        using var operationCancellation = new CancellationTokenSource(
            fetchOptions.OperationTimeout);
        long operationStarted = Stopwatch.GetTimestamp();

        foreach (NuGetSource source in sources)
        {
            if (HasOperationExpired(
                    operationStarted,
                    fetchOptions.OperationTimeout))
            {
                failures.Add(
                    $"{PackageSourceDisplay.ForDiagnostics(source)}: search failed "
                    + $"({nameof(NuGetOperationTimeoutException)}: "
                    + $"NuGet operation did not complete within {fetchOptions.OperationTimeout}.)");
                break;
            }

            using var failureScope = FeedFailureTelemetry.Scope();
            HttpClient sourceClient = useFactoryClients
                && Uri.TryCreate(source.Url, UriKind.Absolute, out Uri? sourceUri)
                && sourceUri.Scheme is "http" or "https"
                    ? HttpClientFactory.GetPackageSourceClient(source.Url)
                    : client;
            TimeSpan requestTimeout = NuGetFetchOptions.RequestTimeoutForClient(
                fetchOptions,
                sourceClient.Timeout);
            IReadOnlyList<string>? searchUrls;
            try
            {
                using CancellationTokenSource requestCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        operationCancellation.Token);
                requestCancellation.CancelAfter(requestTimeout);
                try
                {
                    searchUrls = await PackageExtractor.GetSearchQueryServicesAsync(
                        sourceClient,
                        source,
                        log,
                        requestCancellation.Token,
                        Timeout.InfiniteTimeSpan);
                    ThrowIfOperationExpired(
                        operationStarted,
                        fetchOptions.OperationTimeout,
                        operationCancellation.Token);
                }
                catch (OperationCanceledException ex)
                    when (operationCancellation.IsCancellationRequested
                        || HasOperationExpired(
                            operationStarted,
                            fetchOptions.OperationTimeout))
                {
                    throw new NuGetOperationTimeoutException(
                        fetchOptions.OperationTimeout,
                        ex);
                }
                catch (OperationCanceledException ex)
                    when (requestCancellation.IsCancellationRequested)
                {
                    throw new NuGetRequestTimeoutException(
                        requestTimeout,
                        ex);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException
                or TaskCanceledException
                or TimeoutException)
            {
                failures.Add(
                    $"{PackageSourceDisplay.ForDiagnostics(source)}: service index unavailable "
                    + $"at {UrlRedaction.ForDiagnostics(source.Url)} "
                    + $"({DescribeTransportFailure(ex)})");
                if (ex is NuGetOperationTimeoutException)
                    break;
                continue;
            }

            if (searchUrls is null || searchUrls.Count == 0)
            {
                IReadOnlyList<FeedFailure> sourceFailures =
                    FeedFailureTelemetry.Current!.Failures;
                failures.Add(sourceFailures.Count > 0
                    ? DescribeServiceIndexFailure(source, sourceFailures)
                    : $"{PackageSourceDisplay.ForDiagnostics(source)}: no searchable endpoint for '{UrlRedaction.ForDiagnostics(source.Url)}' "
                        + "(service index unavailable, or advertises no SearchQueryService)");
                continue;
            }

            IReadOnlyList<SearchResult>? found = null;
            Exception? lastFailure = null;
            string? lastSearchUrl = null;
            foreach (string searchUrl in searchUrls.Take(MaxEquivalentSearchEndpoints))
            {
                if (HasOperationExpired(
                        operationStarted,
                        fetchOptions.OperationTimeout))
                {
                    lastFailure = CreateOperationTimeoutException(
                        fetchOptions.OperationTimeout,
                        operationCancellation.Token);
                    break;
                }

                lastSearchUrl = searchUrl;
                var auth = NuGetCredentialScope.AuthFor(source, searchUrl, log);
                log?.Invoke(
                    $"Searching {PackageSourceDisplay.ForDiagnostics(source)}: {UrlRedaction.ForDiagnostics(searchUrl)}");
                HttpClient endpointClient = NuGetCredentialScope.IsSameOrigin(
                    source.Url,
                    searchUrl)
                        ? sourceClient
                        : useFactoryClients
                            ? HttpClientFactory.SharedUntrustedFetch
                            : client;

                try
                {
                    SearchService service = new(
                        endpointClient,
                        searchUrl,
                        fetchOptions);
                    found = resultFilter is null
                        ? await service.SearchAsync(
                            query,
                            take,
                            prerelease,
                            auth,
                            operationCancellation.Token)
                        : await service.SearchByPrefixAsync(
                            query,
                            take,
                            prerelease,
                            auth,
                            operationCancellation.Token);
                    ThrowIfOperationExpired(
                        operationStarted,
                        fetchOptions.OperationTimeout,
                        operationCancellation.Token);
                    break;
                }
                catch (Exception ex) when (ex is HttpRequestException or JsonException
                    or InvalidOperationException or OperationCanceledException
                    or IOException or TimeoutException)
                {
                    lastFailure = ex;
                    if (operationCancellation.IsCancellationRequested
                        || HasOperationExpired(
                            operationStarted,
                            fetchOptions.OperationTimeout))
                    {
                        lastFailure = CreateOperationTimeoutException(
                            fetchOptions.OperationTimeout,
                            operationCancellation.Token,
                            ex);
                        break;
                    }
                }
            }

            if (found is null)
            {
                // The remote controls both the response that produced this
                // exception and the endpoint URL its message embeds, so the
                // failure names the endpoint this product resolved and the
                // category of what went wrong, not the message.
                failures.Add(
                    $"{PackageSourceDisplay.ForDiagnostics(source)}: search failed "
                    + $"at {UrlRedaction.ForDiagnostics(lastSearchUrl)} "
                    + $"({DescribeTransportFailure(lastFailure!)})");
                if (lastFailure is NuGetOperationTimeoutException)
                    break;
                continue;
            }

            searched++;
            foreach (SearchResult result in found)
            {
                if ((resultFilter?.Invoke(result) ?? true)
                    && NuGetSourceResolver.IsAliasEligibleForPackage(
                        source,
                        sources,
                        mapping,
                        result.Id)
                    && seen.Add((
                        result.Id,
                        NuGetVersion.Parse(result.Version))))
                {
                    results.Add(NuGetSearchResult.From(result));
                }
            }
        }

        // Every configured source failed. Returning an empty list here would render as
        // "no packages found", which is the opposite of what happened.
        if (searched == 0)
        {
            string detail = failures.Count > 0
                ? Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", failures)
                : string.Empty;
            throw new InvalidOperationException($"No configured NuGet source could be searched.{detail}");
        }

        IEnumerable<NuGetSearchResult> limited = results;
        if (resultFilter is not null)
        {
            limited = limited.DistinctBy(
                result => result.PackageId,
                StringComparer.OrdinalIgnoreCase);
        }

        return new NuGetSearchOutcome(limited.Take(take).ToList(), failures);
    }

    /// <summary>
    /// The part of a transport failure that may be printed.
    /// </summary>
    /// <remarks>
    /// A timeout's wording is generated from this product's configured request
    /// deadline or operation ceiling and names no endpoint. It is kept so an
    /// operator learns which timeout fired. Every other message here is written
    /// by a layer that saw the remote's response or the feed-declared URL, so
    /// only the exception's category survives.
    /// </remarks>
    private static string DescribeTransportFailure(Exception error) =>
        error is TaskCanceledException or TimeoutException
            ? $"{error.GetType().Name}: {error.Message}"
            : error.GetType().Name;

    private static bool HasOperationExpired(
        long started,
        TimeSpan timeout) =>
        Stopwatch.GetElapsedTime(started) >= timeout;

    private static void ThrowIfOperationExpired(
        long started,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (HasOperationExpired(started, timeout))
            throw CreateOperationTimeoutException(timeout, cancellationToken);
    }

    private static NuGetOperationTimeoutException CreateOperationTimeoutException(
        TimeSpan timeout,
        CancellationToken cancellationToken,
        Exception? innerException = null) =>
        new(
            timeout,
            innerException as OperationCanceledException
                ?? new OperationCanceledException(
                    "NuGet search operation deadline expired.",
                    innerException,
                    cancellationToken));

    private static string DescribeServiceIndexFailure(
        NuGetSource source,
        IReadOnlyList<FeedFailure> failures)
    {
        string reason = failures.Any(f => f.Kind == FeedFailureKind.Authentication)
            ? "source requires credentials"
            : failures.Any(f => f.Kind == FeedFailureKind.Authorization)
                ? "source denied access"
                : "service index unavailable";
        string observations = string.Join(
            "; ",
            failures.Select(f => $"{f.Url} — {f.StatusText} while {f.PhaseText}"));

        return $"{PackageSourceDisplay.ForDiagnostics(source)}: {reason} ({observations})";
    }

    public static async Task<List<NuGetSearchResult>> SearchByPrefixAsync(
        HttpClient client,
        string prefix,
        int take = 100,
        bool prerelease = false,
        Action<string>? log = null,
        NuGetSourceOptions? sourceOptions = null,
        NuGetFetchOptions? fetchOptions = null)
    {
        log?.Invoke($"Searching packages by prefix: {prefix}");
        List<NuGetSource> sources = NuGetSourceResolver.ResolveSources(sourceOptions);
        PackageSourceMapping mapping =
            NuGetSourceResolver.ResolvePackageSourceMapping(sourceOptions);
        fetchOptions ??= new NuGetFetchOptions();
        NuGetSearchOutcome outcome = await SearchResolvedAsync(
            client,
            fetchOptions,
            sources,
            mapping,
            prefix,
            take,
            prerelease,
            log,
            result => result.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ConfigureAwait(false);
        if (outcome.Failures.Count > 0)
        {
            throw new InvalidOperationException(
                "Could not search every configured NuGet source."
                + Environment.NewLine
                + "  "
                + string.Join(Environment.NewLine + "  ", outcome.Failures));
        }

        return [.. outcome.Results];
    }

    private sealed class SearchResultKeyComparer
        : IEqualityComparer<(string Id, NuGetVersion Version)>
    {
        public static readonly SearchResultKeyComparer Instance = new();

        public bool Equals(
            (string Id, NuGetVersion Version) x,
            (string Id, NuGetVersion Version) y) =>
            string.Equals(x.Id, y.Id, StringComparison.OrdinalIgnoreCase)
            && x.Version.Equals(y.Version);

        public int GetHashCode((string Id, NuGetVersion Version) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Id),
                obj.Version.GetHashCode());
    }
}
