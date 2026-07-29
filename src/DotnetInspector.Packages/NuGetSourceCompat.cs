// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using NuGetFetch;
using DotnetInspector.Core;
using NuGetSource = NuGetFetch.PackageSource;

namespace DotnetInspector.Packages;

/// <summary>
/// Options for configuring NuGet package sources.
/// </summary>
public record NuGetSourceOptions
{
    public string[] Sources { get; init; } = [];
    public string[] AdditionalSources { get; init; } = [];
    public string? ConfigFile { get; init; }
    public static NuGetSourceOptions Default { get; } = new();
    public bool HasCustomConfiguration =>
        Sources.Length > 0 || AdditionalSources.Length > 0 || ConfigFile != null;
}

/// <summary>
/// Resolves NuGet sources by delegating to NuGetFetch.SourceResolver.
/// </summary>
public static class NuGetSourceResolver
{
    public static List<NuGetSource> ResolveSources(NuGetSourceOptions? options, string? workingDirectory = null)
    {
        options ??= NuGetSourceOptions.Default;

        if (options.ConfigFile is not null)
        {
            ValidateExplicitConfig(options.ConfigFile);
        }

        if (options.Sources.Length > 0)
        {
            return SelectExplicitSources(options);
        }

        IReadOnlyList<NuGetSource> sources = SourceResolver.ResolveSources(
            explicitSource: null,
            configPath: options.ConfigFile,
            additionalSources: options.AdditionalSources.Length > 0 ? options.AdditionalSources : null);

        return [.. sources];
    }

    /// <summary>
    /// Validates a user-supplied <c>--nugetconfig</c> path before it is used.
    /// </summary>
    /// <remarks>
    /// SourceResolver parses config files best-effort and falls back to nuget.org when it ends up
    /// with no sources. That is right for the machine, user, and project configs it discovers on
    /// its own — a broken machine config should not break every command. It is wrong for a config
    /// the user named explicitly: a mistyped path or malformed file would otherwise search
    /// unrelated feeds and exit 0, reporting someone else's packages as the answer. An explicit
    /// config that cannot be used is a failure, not a reason to pick a default.
    /// </remarks>
    private static void ValidateExplicitConfig(string configFile)
    {
        if (!File.Exists(configFile))
        {
            throw new FileNotFoundException($"NuGet config file not found: '{configFile}'.", configFile);
        }

        try
        {
            XDocument.Load(configFile);
        }
        catch (XmlException ex)
        {
            throw new InvalidOperationException(
                $"NuGet config file '{configFile}' is not valid XML: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Builds the source list for an explicit <c>--source</c> selection.
    /// </summary>
    /// <remarks>
    /// <c>--source</c> replaces the configured defaults. SourceResolver's explicit-source fast
    /// path takes a single value, so more than one had been forwarded as *additional* sources,
    /// which re-entered config resolution and silently searched feeds the user never named — and
    /// a single <c>--source</c> combined with <c>--add-source</c> dropped the added source
    /// entirely. Selection is resolved here instead.
    ///
    /// Credentials still come from configuration: a user who names an authenticated feed on the
    /// command line has already declared that feed's credentials in nuget.config, keyed by the
    /// same URL, and NuGet's own client matches them the same way.
    /// </remarks>
    private static List<NuGetSource> SelectExplicitSources(NuGetSourceOptions options)
    {
        IReadOnlyList<NuGetSource> configured = SourceResolver.ResolveSources(configPath: options.ConfigFile);

        List<NuGetSource> selected = [.. options.Sources.Select(url => Match(url, configured))];
        selected.AddRange(options.AdditionalSources.Select(url => Match(url, configured)));
        return selected;
    }

    private static NuGetSource Match(string url, IReadOnlyList<NuGetSource> configured) =>
        configured.FirstOrDefault(s => string.Equals(s.Url, url, StringComparison.OrdinalIgnoreCase))
        ?? new NuGetSource("explicit", url);
}

/// <summary>
/// Compatibility re-export: NuGetSearchResult maps to NuGetFetch.SearchResult.
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
    /// Searches the configured NuGet sources. With no custom source configuration this searches
    /// nuget.org, as before. Otherwise each resolved HTTP source has its SearchQueryService
    /// endpoint discovered from its V3 service index and is searched with its own credentials.
    /// </summary>
    public static async Task<NuGetSearchOutcome> SearchAsync(
        HttpClient client,
        string query,
        int take = 20,
        bool prerelease = false,
        Action<string>? log = null,
        NuGetSourceOptions? sourceOptions = null)
    {
        using var trafficScope = NetworkTelemetry.Scope(NetworkTrafficKind.PackageSearch);

        if (sourceOptions is null || !sourceOptions.HasCustomConfiguration)
        {
            log?.Invoke($"Searching NuGet: {query}");
            SearchService service = new(client);
            IReadOnlyList<SearchResult> results = await service.SearchAsync(query, take, prerelease);
            return new NuGetSearchOutcome(results.Select(NuGetSearchResult.From).ToList(), []);
        }

        return await SearchSourcesAsync(client, sourceOptions, query, take, prerelease, log);
    }

    private static async Task<NuGetSearchOutcome> SearchSourcesAsync(
        HttpClient client,
        NuGetSourceOptions sourceOptions,
        string query,
        int take,
        bool prerelease,
        Action<string>? log)
    {
        List<NuGetSource> sources = NuGetSourceResolver.ResolveSources(sourceOptions);
        List<NuGetSearchResult> results = [];
        List<string> failures = [];
        HashSet<(string Id, string Version)> seen = new(SearchResultKeyComparer.Instance);
        int searched = 0;

        foreach (NuGetSource source in sources)
        {
            string? searchUrl;
            try
            {
                searchUrl = await PackageExtractor.GetSearchQueryServiceAsync(client, source, log);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                failures.Add($"{source.Name}: service index unavailable ({ex.Message})");
                continue;
            }

            if (searchUrl is null)
            {
                // Distinguishing "index unreachable" from "index has no search resource" needs
                // typed HTTP failures, which HttpRetryHelper does not yet surface (issue #3417,
                // bug 1). Until then the message stays honest about covering both.
                failures.Add(
                    $"{source.Name}: no searchable endpoint for '{source.Url}' "
                    + "(service index unavailable, or advertises no SearchQueryService)");
                continue;
            }

            var auth = NuGetCredentialScope.AuthFor(source, searchUrl, log);
            log?.Invoke($"Searching {source.Name}: {searchUrl}");

            IReadOnlyList<SearchResult> found;
            try
            {
                SearchService service = new(client, searchUrl);
                found = await service.SearchAsync(query, take, prerelease, auth);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or TaskCanceledException)
            {
                failures.Add($"{source.Name}: {ex.Message}");
                continue;
            }

            searched++;
            foreach (SearchResult result in found)
            {
                if (seen.Add((result.Id, result.Version)))
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

        return new NuGetSearchOutcome(results.Take(take).ToList(), failures);
    }

    public static async Task<List<NuGetSearchResult>> SearchByPrefixAsync(
        HttpClient client, string prefix, int take = 100, bool prerelease = false, Action<string>? log = null)
    {
        log?.Invoke($"Searching NuGet by prefix: {prefix}");
        SearchService service = new(client);
        using var trafficScope = NetworkTelemetry.Scope(NetworkTrafficKind.PackageSearch);
        IReadOnlyList<SearchResult> results = await service.SearchByPrefixAsync(prefix, take, prerelease);
        return results.Select(NuGetSearchResult.From).ToList();
    }

    public static List<NuGetSearchResult> ParseSearchResponse(string json)
    {
        // Legacy compat — callers that parse raw JSON can use this
        using var stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        var response = NuGetApi.GetSearchResponseAsync(stream).AsTask().GetAwaiter().GetResult();
        return response?.Data.Select(NuGetSearchResult.From).ToList() ?? [];
    }

    private sealed class SearchResultKeyComparer : IEqualityComparer<(string Id, string Version)>
    {
        public static readonly SearchResultKeyComparer Instance = new();

        public bool Equals((string Id, string Version) x, (string Id, string Version) y) =>
            string.Equals(x.Id, y.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Version, y.Version, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Id, string Version) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Id),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Version));
    }
}
