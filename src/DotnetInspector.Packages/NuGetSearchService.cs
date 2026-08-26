// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Net;
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
        bool operationTimedOut = false;
        using var operationCancellation = new CancellationTokenSource(
            fetchOptions.OperationTimeout);
        long operationStarted = Stopwatch.GetTimestamp();

        for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
        {
            NuGetSource source = sources[sourceIndex];
            if (HasOperationExpired(
                    operationStarted,
                    fetchOptions.OperationTimeout))
            {
                operationTimedOut = true;
                AddOperationTimeoutFailures(
                    sources,
                    sourceIndex,
                    failures,
                    fetchOptions.OperationTimeout);
                break;
            }

            using var failureScope = FeedFailureTelemetry.Scope();
            PackageSourceOperationResult<PackageSearchResult> search;
            try
            {
                using IPackageSourceClient sourceClient =
                    PackageSourceClientProvider.Create(
                        source,
                        client,
                        fetchOptions);
                log?.Invoke(
                    $"Searching {PackageSourceDisplay.ForDiagnostics(source)}");
                search = resultFilter is null
                    ? await sourceClient.SearchAsync(
                        query,
                        take,
                        prerelease,
                        operationCancellation.Token).ConfigureAwait(false)
                    : await sourceClient.SearchByPrefixAsync(
                        query,
                        take,
                        prerelease,
                        operationCancellation.Token).ConfigureAwait(false);
                ThrowIfOperationExpired(
                    operationStarted,
                    fetchOptions.OperationTimeout,
                    operationCancellation.Token);
            }
            catch (NuGetOperationTimeoutException)
            {
                operationTimedOut = true;
                failures.Add(OperationTimeoutFailure(
                    source,
                    fetchOptions.OperationTimeout,
                    attempted: true));
                AddOperationTimeoutFailures(
                    sources,
                    sourceIndex + 1,
                    failures,
                    fetchOptions.OperationTimeout);
                break;
            }
            catch (PackageSourceClientUnavailableException)
            {
                failures.Add(
                    $"{PackageSourceDisplay.ForDiagnostics(source)}: no searchable endpoint for "
                    + $"'{UrlRedaction.ForDiagnostics(source.Url)}' "
                    + "(the package source transport does not support search)");
                continue;
            }
            catch (OperationCanceledException)
                when (operationCancellation.IsCancellationRequested
                    || HasOperationExpired(
                        operationStarted,
                        fetchOptions.OperationTimeout))
            {
                operationTimedOut = true;
                failures.Add(OperationTimeoutFailure(
                    source,
                    fetchOptions.OperationTimeout,
                    attempted: true));
                AddOperationTimeoutFailures(
                    sources,
                    sourceIndex + 1,
                    failures,
                    fetchOptions.OperationTimeout);
                break;
            }

            if (search
                is PackageSourceOperationResult<PackageSearchResult>.Failed
                    failed)
            {
                PackageSourceClientProvider.RecordFailure(
                    source,
                    failed.Failure,
                    NetworkTrafficKind.PackageSearch);
                failures.Add(DescribeSearchFailure(source, failed.Failure));
                if (failed.Failure is
                    {
                        Kind: PackageSourceFailureKind.Timeout,
                        Timeout.Kind: PackageSourceTimeoutKind.Operation,
                    })
                {
                    operationTimedOut = true;
                    AddOperationTimeoutFailures(
                        sources,
                        sourceIndex + 1,
                        failures,
                        fetchOptions.OperationTimeout);
                    break;
                }
                continue;
            }

            PackageSearchResult found =
                ((PackageSourceOperationResult<PackageSearchResult>.Succeeded)
                    search).Value;
            if (resultFilter is not null
                && found.TruncationReason is
                    PackageSearchTruncationReason.SourcePageLimit
                    or PackageSearchTruncationReason.ClientPageLimit)
            {
                failures.Add(
                    $"{PackageSourceDisplay.ForDiagnostics(source)}: search failed "
                    + "(the package source prefix search ended before a complete result could be established)");
                continue;
            }

            var sourceResults = new List<NuGetSearchResult>();
            var sourceKeys = new HashSet<(string Id, NuGetVersion Version)>(
                SearchResultKeyComparer.Instance);
            bool aggregationTimedOut = false;
            foreach (PackageSearchMatch match in found.Matches)
            {
                if (HasOperationExpired(
                        operationStarted,
                        fetchOptions.OperationTimeout))
                {
                    aggregationTimedOut = true;
                    break;
                }

                SearchResult result = match.Metadata;
                var key = (
                    result.Id,
                    NuGetVersion.Parse(result.Version));
                if ((resultFilter?.Invoke(result) ?? true)
                    && NuGetSourceResolver.IsAliasEligibleForPackage(
                        source,
                        sources,
                        mapping,
                        result.Id)
                    && !seen.Contains(key)
                    && sourceKeys.Add(key))
                {
                    sourceResults.Add(NuGetSearchResult.From(result));
                }
            }

            if (aggregationTimedOut)
            {
                operationTimedOut = true;
                failures.Add(OperationTimeoutFailure(
                    source,
                    fetchOptions.OperationTimeout,
                    attempted: true));
                AddOperationTimeoutFailures(
                    sources,
                    sourceIndex + 1,
                    failures,
                    fetchOptions.OperationTimeout);
                break;
            }

            searched++;
            seen.UnionWith(sourceKeys);
            results.AddRange(sourceResults);
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

        if (!operationTimedOut)
        {
            ThrowIfOperationExpired(
                operationStarted,
                fetchOptions.OperationTimeout,
                operationCancellation.Token);
        }
        List<NuGetSearchResult> finalResults = limited.Take(take).ToList();
        if (!operationTimedOut)
        {
            ThrowIfOperationExpired(
                operationStarted,
                fetchOptions.OperationTimeout,
                operationCancellation.Token);
        }
        return new NuGetSearchOutcome(finalResults, failures);
    }

    private static void AddOperationTimeoutFailures(
        IReadOnlyList<NuGetSource> sources,
        int startIndex,
        List<string> failures,
        TimeSpan timeout)
    {
        for (int index = startIndex; index < sources.Count; index++)
        {
            failures.Add(OperationTimeoutFailure(
                sources[index],
                timeout,
                attempted: false));
        }
    }

    private static string OperationTimeoutFailure(
        NuGetSource source,
        TimeSpan timeout,
        bool attempted) =>
        $"{PackageSourceDisplay.ForDiagnostics(source)}: "
        + (attempted ? "search failed " : "search not attempted ")
        + $"({PackageSourceFailureKind.Timeout}; "
        + $"{PackageSourceTimeoutKind.Operation}: "
        + $"NuGet operation did not complete within {timeout}.)";

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

    private static string DescribeSearchFailure(
        NuGetSource source,
        PackageSourceFailure failure)
    {
        string display =
            PackageSourceDisplay.ForDiagnostics(source).ToString();
        if (failure.Kind == PackageSourceFailureKind.Unsupported)
        {
            return $"{display}: no searchable endpoint for "
                + $"'{UrlRedaction.ForDiagnostics(source.Url)}' "
                + "(service index unavailable, or advertises no SearchQueryService)";
        }

        string status = failure.StatusCode is { } statusCode
            ? $"HTTP {(int)statusCode} {statusCode}; "
            : string.Empty;
        string reason = failure.Kind switch
        {
            PackageSourceFailureKind.AuthenticationRequired
                when failure.StatusCode == HttpStatusCode.Unauthorized =>
                "source requires credentials",
            PackageSourceFailureKind.AuthenticationRequired =>
                "source denied access",
            PackageSourceFailureKind.Transport
                when failure.StatusCode is not null =>
                "service index unavailable",
            _ => "search failed",
        };
        return $"{display}: {reason} "
            + $"({status}{failure.Kind}: {failure.Message})";
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
