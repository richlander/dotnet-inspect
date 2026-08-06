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
}

/// <summary>
/// Resolves NuGet sources by delegating to NuGetFetch.SourceResolver.
/// </summary>
public static class NuGetSourceResolver
{
    /// <summary>
    /// Resolves sources and reduces them to the identities the package content
    /// cache records, so a caller can ask the cache for content this
    /// configuration is actually entitled to. Configured order is preserved.
    /// </summary>
    public static IReadOnlyList<string> ResolveSourceKeys(
        NuGetSourceOptions? options,
        string? workingDirectory = null)
        => SourceKeys(ResolveSources(options, workingDirectory));

    /// <summary>
    /// Reduces already-resolved sources to their cache identities, preserving
    /// configured order.
    /// </summary>
    /// <remarks>
    /// Order is part of the contract. Sources are consulted in configured order
    /// on a miss, so a cache read that consults slots in some other order could
    /// answer from a lower-precedence feed than the one a cold run would have
    /// used. Returning a set rather than an ordered list would leave that
    /// precedence undefined.
    /// </remarks>
    public static IReadOnlyList<string> SourceKeys(IEnumerable<NuGetSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var keys = new List<string>();
        foreach (var source in sources)
        {
            var key = NuGetCache.GetSourceKey(source.Url);
            if (seen.Add(key))
                keys.Add(key);
        }

        return keys;
    }

    public static List<NuGetSource> ResolveSources(NuGetSourceOptions? options, string? workingDirectory = null)
    {
        options ??= NuGetSourceOptions.Default;

        if (options.ConfigFile is not null)
        {
            ValidateExplicitConfig(options.ConfigFile);
        }

        if (options.Sources.Length > 0)
        {
            return SelectExplicitSources(options, workingDirectory);
        }

        IReadOnlyList<NuGetSource> sources = SourceResolver.ResolveSources(
            explicitSource: null,
            configPath: options.ConfigFile,
            additionalSources: options.AdditionalSources.Length > 0 ? options.AdditionalSources : null,
            workingDirectory: workingDirectory);

        return [.. sources];
    }

    /// <summary>
    /// Returns a description of why <paramref name="configFile"/> cannot be used as a NuGet
    /// config, or null when it can. Exposed so the CLI can report the same problem at parse
    /// time rather than letting it surface as an exception from whichever service resolves
    /// sources first.
    /// </summary>
    /// <remarks>
    /// This method reports problems; it does not raise them. Its caller is a parse-time option
    /// validator, and an exception thrown there escapes before any command runs — outside every
    /// handler in Program.cs, which wrap invocation rather than parsing — and terminates the
    /// process with a raw stack trace. Every reason a config cannot be read is therefore a
    /// returned string, including the ones that arrive as exceptions.
    /// </remarks>
    public static string? DescribeConfigProblem(string configFile)
    {
        if (!File.Exists(configFile))
        {
            return $"NuGet config file not found: '{configFile}'.";
        }

        try
        {
            XDocument.Load(configFile);
        }
        catch (XmlException ex)
        {
            return $"NuGet config file '{configFile}' is not valid XML: {ex.Message}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Exists but cannot be opened: locked by another process, denied by ACL, or not a
            // regular file. Unusable for the same reason a missing file is unusable.
            return $"NuGet config file '{configFile}' could not be read: {ex.Message}";
        }

        // Well-formed XML is not enough. Any XML file parses — a .csproj passed by mistake
        // reaches this point — and SourceResolver then finds no packageSources and substitutes
        // nuget.org, answering with packages from a feed the user did not choose, at exit 0.
        try
        {
            if (SourceResolver.ResolveConfiguredSources(configFile).Count == 0)
            {
                return $"NuGet config file '{configFile}' declares no usable package sources.";
            }
        }
        catch (UnsupportedSourceException ex)
        {
            // Resolution rejects a source the config declares. That rejection is a throw because
            // it guards every path that reaches a feed, most of which are far past parsing; here,
            // where the config is only being inspected, it converts back to the returned string
            // this method promises so the CLI reports it as an option error.
            return ex.Message;
        }

        return null;
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
        if (DescribeConfigProblem(configFile) is not string problem)
        {
            return;
        }

        throw File.Exists(configFile)
            ? new InvalidOperationException(problem)
            : new FileNotFoundException(problem, configFile);
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
    private static List<NuGetSource> SelectExplicitSources(
        NuGetSourceOptions options,
        string? workingDirectory)
    {
        IReadOnlyList<NuGetSource> configured =
            SourceResolver.ResolveConfiguredSources(options.ConfigFile, workingDirectory);

        List<NuGetSource> selected = [.. options.Sources.Select(url => Match(url, configured))];
        selected.AddRange(options.AdditionalSources.Select(url => Match(url, configured)));
        return selected;
    }

    /// <summary>
    /// Finds the configured source that names the same endpoint as <paramref name="url"/>, so an
    /// explicitly requested feed can use the credentials configured for it.
    /// </summary>
    /// <remarks>
    /// The match is deliberately narrow. Comparing whole URLs case-insensitively would alias
    /// <c>/FeedA</c> and <c>/feeda</c>, which are different feeds on servers with case-sensitive
    /// paths, and would hand one feed's credentials to the other. Origin is compared
    /// case-insensitively because scheme and host are case-insensitive by definition; path and
    /// query are compared ordinally on their escaped form because they are not.
    ///
    /// An exact spelling wins outright. A trailing slash is not a distinction any feed makes, so
    /// it is tolerated only as a fallback, and only when exactly one configured source matches
    /// that way: if two entries differ solely by a trailing slash they are separate entries with
    /// potentially separate credentials, and picking either one would be a guess that could send
    /// one entry's secret to the other's spelling.
    ///
    /// On a match only the credentials are adopted. The URL stays exactly as the user spelled it,
    /// so a request never silently goes somewhere other than where it was pointed.
    /// </remarks>
    private static NuGetSource Match(string url, IReadOnlyList<NuGetSource> configured)
    {
        NuGetSource? match = configured.FirstOrDefault(
            s => string.Equals(s.Url, url, StringComparison.Ordinal));

        if (match is null)
        {
            List<NuGetSource> tolerant =
                [.. configured.Where(s => NuGetCredentialScope.IsSameEndpoint(s.Url, url))];

            match = tolerant.Count == 1 ? tolerant[0] : null;
        }

        return match is null ? new NuGetSource("explicit", url) : match with { Url = url };
    }
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
        NuGetSourceOptions? sourceOptions = null)
    {
        using var trafficScope = NetworkTelemetry.Scope(NetworkTrafficKind.PackageSearch);

        List<NuGetSource> sources = NuGetSourceResolver.ResolveSources(sourceOptions);

        // nuget.org's search endpoint is well known, so searching it needs no service-index
        // request. That shortcut is keyed on where resolution actually landed, not on whether a
        // source option was passed: a discovered NuGet.config can name an entirely different feed,
        // and gating on the flags alone sent those users to nuget.org anyway (issue #3417, bug 2).
        //
        // The match is against the canonical service index, not merely a nuget.org host. Any other
        // path on that host is a different endpoint the user named deliberately, and answering it
        // from the well-known search endpoint would report results the requested URL never served.
        if (sources is [{ Credential: null } only]
            && NuGetCredentialScope.IsSameEndpoint(only.Url, NuGetSource.NuGetOrg.Url))
        {
            log?.Invoke($"Searching NuGet: {query}");
            SearchService service = new(client);
            IReadOnlyList<SearchResult> results = await service.SearchAsync(query, take, prerelease);
            return new NuGetSearchOutcome(results.Select(NuGetSearchResult.From).ToList(), []);
        }

        return await SearchSourcesAsync(client, sources, query, take, prerelease, log);
    }

    private static async Task<NuGetSearchOutcome> SearchSourcesAsync(
        HttpClient client,
        List<NuGetSource> sources,
        string query,
        int take,
        bool prerelease,
        Action<string>? log)
    {
        List<NuGetSearchResult> results = [];
        List<string> failures = [];
        HashSet<(string Id, string Version)> seen = new(SearchResultKeyComparer.Instance);
        int searched = 0;

        foreach (NuGetSource source in sources)
        {
            using var failureScope = FeedFailureTelemetry.Scope();
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
                IReadOnlyList<FeedFailure> sourceFailures =
                    FeedFailureTelemetry.Current!.Failures;
                failures.Add(sourceFailures.Count > 0
                    ? DescribeServiceIndexFailure(source, sourceFailures)
                    : $"{source.Name}: no searchable endpoint for '{source.Url}' "
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

        return $"{source.Name}: {reason} ({observations})";
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
