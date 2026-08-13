// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using NuGetFetch;
using DotnetInspector.Core;
using InertText;
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
    internal string[]? AuthorizedSourceKeys { get; init; }
    public static NuGetSourceOptions Default { get; } = new();
}

/// <summary>
/// Identifies why package source mapping could not authorize a producer.
/// </summary>
public enum PackageSourceMappingFailure
{
    /// <summary>The package id matched no configured pattern.</summary>
    NoPattern,

    /// <summary>No active source carries a configured name selected by mapping.</summary>
    InactiveSource,

    /// <summary>Eligible aliases for one producer use different credentials.</summary>
    ConflictingCredentials,
}

/// <summary>
/// Thrown when package source mapping cannot authorize a producer for a package id.
/// </summary>
public sealed class PackageSourceMappingException(
    PackageSourceMappingFailure failure,
    string message) : InvalidOperationException(message)
{
    /// <summary>
    /// Gets the mapping failure category.
    /// </summary>
    public PackageSourceMappingFailure Failure { get; } = failure;
}

/// <summary>
/// Resolves NuGet sources by delegating to NuGetFetch.SourceResolver.
/// </summary>
public static class NuGetSourceResolver
{
    /// <summary>
    /// Restricts payload fulfillment for one discovered coordinate to its
    /// reporting producers while retaining the ambient source set and config.
    /// Follow-on coordinates, such as tool-wrapper redirects, independently
    /// recalculate their authorization.
    /// </summary>
    public static NuGetSourceOptions? RestrictToSources(
        NuGetSourceOptions? original,
        IReadOnlyList<string> sourceUrls)
    {
        ArgumentNullException.ThrowIfNull(sourceUrls);
        return RestrictToSourceKeys(
            original,
            [.. sourceUrls.Select(NuGetCache.GetSourceKey)]);
    }

    /// <summary>
    /// Restricts payload or metadata fulfillment to canonical producer identities established
    /// by an earlier package acquisition.
    /// </summary>
    public static NuGetSourceOptions? RestrictToSourceKeys(
        NuGetSourceOptions? original,
        IReadOnlyList<string> sourceKeys)
    {
        ArgumentNullException.ThrowIfNull(sourceKeys);
        return (original ?? NuGetSourceOptions.Default) with
        {
            AuthorizedSourceKeys = [.. sourceKeys],
        };
    }

    /// <summary>
    /// Applies a producer restriction established by an earlier coordinate resolution to an
    /// already package-mapped source set.
    /// </summary>
    public static IReadOnlyList<NuGetSource> ResolveAuthorizedSources(
        NuGetSourceOptions? options,
        IReadOnlyList<NuGetSource> activeSources)
    {
        if (options?.AuthorizedSourceKeys is not { } authorizedKeys)
            return activeSources;

        HashSet<string> authorizedKeySet = [.. authorizedKeys];
        return
        [
            .. activeSources.Where(source =>
                authorizedKeySet.Contains(NuGetCache.GetSourceKey(source.Url))),
        ];
    }

    internal static NuGetSourceOptions? WithoutSourceRestriction(
        NuGetSourceOptions? options)
        => options?.AuthorizedSourceKeys is null
            ? options
            : options with { AuthorizedSourceKeys = null };

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
    /// Resolves the producers eligible to serve <paramref name="packageId"/> and reduces them to
    /// the identities recorded by the package-content cache.
    /// </summary>
    public static IReadOnlyList<string> ResolveSourceKeysForPackage(
        NuGetSourceOptions? options,
        string packageId,
        string? workingDirectory = null)
        => SourceKeys(ResolveSourcesForPackage(options, packageId, workingDirectory));

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

        IReadOnlyList<NuGetSource> configured = SourceResolver.ResolveSources(
            explicitSource: null,
            configPath: options.ConfigFile,
            additionalSources: null,
            workingDirectory: workingDirectory);
        IReadOnlyList<NuGetSource> configuredAliases =
            options.Sources.Length > 0 || options.AdditionalSources.Length > 0
                ? SourceResolver.ResolveConfiguredSourceAliases(
                    options.ConfigFile,
                    workingDirectory)
                : configured;

        List<NuGetSource> selected = options.Sources.Length > 0
            ? SelectExplicitSources(options.Sources, configuredAliases)
            : [.. configured];
        AddExplicitSources(selected, options.AdditionalSources, configuredAliases);
        return selected;
    }

    /// <summary>
    /// Resolves active source aliases, applies package source mapping for
    /// <paramref name="packageId"/>, and collapses eligible aliases to canonical producers.
    /// </summary>
    /// <remarks>
    /// Mapping names configured aliases, while package payloads and caches name canonical
    /// producer endpoints. Aliases therefore remain distinct until mapping has selected the
    /// package-specific set. Eligible aliases for one producer must agree on credentials.
    /// </remarks>
    /// <exception cref="PackageSourceMappingException">
    /// Mapping is enabled but the package id matches no pattern, none of the mapped names is
    /// active, or eligible aliases for one producer disagree on credentials.
    /// </exception>
    public static List<NuGetSource> ResolveSourcesForPackage(
        NuGetSourceOptions? options,
        string packageId,
        string? workingDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        List<NuGetSource> activeAliases = ResolveSources(options, workingDirectory);
        PackageSourceMapping mapping = ResolvePackageSourceMapping(options, workingDirectory);

        IReadOnlyList<NuGetSource> eligibleAliases = activeAliases;
        if (mapping.IsEnabled)
        {
            IReadOnlyList<string> mappedNames =
                mapping.GetConfiguredPackageSources(packageId);
            if (mappedNames.Count == 0)
            {
                throw new PackageSourceMappingException(
                    PackageSourceMappingFailure.NoPattern,
                    $"Package source mapping has no pattern for package '{packageId}'.");
            }

            var allowedNames = new HashSet<string>(
                mappedNames,
                StringComparer.OrdinalIgnoreCase);
            eligibleAliases =
            [
                .. activeAliases.Where(source => allowedNames.Contains(source.Name)),
            ];
            if (eligibleAliases.Count == 0)
            {
                throw new PackageSourceMappingException(
                    PackageSourceMappingFailure.InactiveSource,
                    $"Package '{packageId}' maps to source"
                    + $"{(mappedNames.Count == 1 ? "" : "s")} "
                    + $"'{string.Join("', '", mappedNames)}', but "
                    + $"{(mappedNames.Count == 1 ? "it is not" : "none are")} active.");
            }
        }

        return CollapseAliases(eligibleAliases, packageId);
    }

    internal static PackageSourceMapping ResolvePackageSourceMapping(
        NuGetSourceOptions? options,
        string? workingDirectory = null)
        => SourceResolver.ResolvePackageSourceMapping(
            options?.ConfigFile,
            workingDirectory);

    internal static bool IsAliasEligibleForPackage(
        NuGetSource source,
        IReadOnlyList<NuGetSource> activeAliases,
        PackageSourceMapping mapping,
        string packageId)
    {
        if (!mapping.IsEnabled)
        {
            return true;
        }

        IReadOnlyList<string> mappedNames =
            mapping.GetConfiguredPackageSources(packageId);
        if (mappedNames.Count == 0)
        {
            return false;
        }

        var allowedNames = new HashSet<string>(
            mappedNames,
            StringComparer.OrdinalIgnoreCase);
        List<NuGetSource> eligibleAliases =
        [
            .. activeAliases.Where(alias => allowedNames.Contains(alias.Name)),
        ];
        if (eligibleAliases.Count == 0)
        {
            return false;
        }

        _ = CollapseAliases(eligibleAliases, packageId);
        return allowedNames.Contains(source.Name);
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
        // reaches this point — and an explicitly selected config starts from an empty source
        // layer rather than inheriting the ambient NuGet.org default.
        try
        {
            if (SourceResolver.ResolveConfiguredSourceAliases(configFile).Count == 0)
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
    /// Ambient resolution starts with the default NuGet.org source layer before merging discovered
    /// configuration. An explicitly selected config starts empty instead: a mistyped path or
    /// malformed file must not search unrelated feeds and exit 0, reporting someone else's
    /// packages as the answer. An explicit config that cannot be used is a failure, not a reason
    /// to pick a default.
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
        IEnumerable<string> urls,
        IReadOnlyList<NuGetSource> configured)
    {
        List<NuGetSource> selected = [];
        AddExplicitSources(selected, urls, configured);
        return selected;
    }

    private static void AddExplicitSources(
        List<NuGetSource> selected,
        IEnumerable<string> urls,
        IReadOnlyList<NuGetSource> configured)
    {
        foreach (string url in urls)
        {
            foreach (NuGetSource match in Match(url, configured))
            {
                if (!selected.Contains(match))
                {
                    selected.Add(match);
                }
            }
        }
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
    /// Every configured alias for the endpoint is retained. Package source mapping names those
    /// aliases, so selecting one before the package id is known would either bypass mapping or
    /// discard the credential attached to the alias mapping later selects.
    ///
    /// On a match only the credentials are adopted. The URL stays exactly as the user spelled it,
    /// so a request never silently goes somewhere other than where it was pointed.
    /// </remarks>
    private static IReadOnlyList<NuGetSource> Match(
        string url,
        IReadOnlyList<NuGetSource> configured)
    {
        List<NuGetSource> matches =
        [
            .. configured
                .Where(source => NuGetCredentialScope.IsSameEndpoint(source.Url, url))
                .Select(source => source with { Url = url }),
        ];
        return matches.Count == 0
            ? [new NuGetSource(url, url)]
            : matches;
    }

    private static List<NuGetSource> CollapseAliases(
        IReadOnlyList<NuGetSource> eligibleAliases,
        string packageId)
    {
        List<NuGetSource> producers = [];
        foreach (IGrouping<string, NuGetSource> aliases in eligibleAliases.GroupBy(
            source => NuGetCache.GetSourceKey(source.Url),
            StringComparer.Ordinal))
        {
            NuGetSource first = aliases.First();
            if (aliases.Any(alias => alias.Credential != first.Credential))
            {
                throw new PackageSourceMappingException(
                    PackageSourceMappingFailure.ConflictingCredentials,
                    $"Package '{packageId}' is eligible from multiple configured names for "
                    + $"'{UrlRedaction.ForDiagnostics(first.Url)}', but those names use conflicting credentials.");
            }

            producers.Add(first);
        }

        return producers;
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
        List<NuGetSource> sources = NuGetSourceResolver.ResolveSources(sourceOptions);
        PackageSourceMapping mapping =
            NuGetSourceResolver.ResolvePackageSourceMapping(sourceOptions);
        return await SearchResolvedAsync(
            client,
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
        List<NuGetSource> sources,
        PackageSourceMapping mapping,
        string query,
        int take,
        bool prerelease,
        Action<string>? log,
        Func<SearchResult, bool>? resultFilter)
    {
        using var trafficScope = NetworkTelemetry.Scope(NetworkTrafficKind.PackageSearch);

        // nuget.org's search endpoint is well known, so searching it needs no service-index
        // request. That shortcut is keyed on where resolution actually landed, not on whether a
        // source option was passed: a discovered NuGet.config can name an entirely different feed,
        // and gating on the flags alone sent those users to nuget.org anyway (issue #3417, bug 2).
        //
        // The match is against the canonical service index, not merely a nuget.org host. Any other
        // path on that host is a different endpoint the user named deliberately, and answering it
        // from the well-known search endpoint would report results the requested URL never served.
        if (sources is [{ Credential: null } only]
            && only.IsNuGetOrg)
        {
            log?.Invoke($"Searching NuGet: {query}");
            SearchService service = new(client);
            IReadOnlyList<SearchResult> results = resultFilter is null
                ? await service.SearchAsync(query, take, prerelease)
                : await service.SearchByPrefixAsync(query, take, prerelease);
            IEnumerable<NuGetSearchResult> projected = results
                .Where(result =>
                    (resultFilter?.Invoke(result) ?? true)
                    && NuGetSourceResolver.IsAliasEligibleForPackage(
                        only,
                        sources,
                        mapping,
                        result.Id))
                .Select(NuGetSearchResult.From);
            if (resultFilter is not null)
            {
                projected = projected.DistinctBy(
                    result => result.PackageId,
                    StringComparer.OrdinalIgnoreCase);
            }

            return new NuGetSearchOutcome(
                projected.Take(take).ToList(),
                []);
        }

        return await SearchSourcesAsync(
            client,
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
                failures.Add(
                    $"{PackageSourceDisplay.ForDiagnostics(source)}: service index unavailable "
                    + $"at {UrlRedaction.ForDiagnostics(source.Url)} "
                    + $"({DescribeTransportFailure(ex)})");
                continue;
            }

            if (searchUrl is null)
            {
                IReadOnlyList<FeedFailure> sourceFailures =
                    FeedFailureTelemetry.Current!.Failures;
                failures.Add(sourceFailures.Count > 0
                    ? DescribeServiceIndexFailure(source, sourceFailures)
                    : $"{PackageSourceDisplay.ForDiagnostics(source)}: no searchable endpoint for '{UrlRedaction.ForDiagnostics(source.Url)}' "
                        + "(service index unavailable, or advertises no SearchQueryService)");
                continue;
            }

            var auth = NuGetCredentialScope.AuthFor(source, searchUrl, log);
            log?.Invoke(
                $"Searching {PackageSourceDisplay.ForDiagnostics(source)}: {UrlRedaction.ForDiagnostics(searchUrl)}");

            IReadOnlyList<SearchResult> found;
            try
            {
                SearchService service = new(client, searchUrl);
                found = resultFilter is null
                    ? await service.SearchAsync(query, take, prerelease, auth)
                    : await service.SearchByPrefixAsync(
                        query,
                        take,
                        prerelease,
                        auth);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or TaskCanceledException)
            {
                // The remote controls both the response that produced this
                // exception and the endpoint URL its message embeds, so the
                // failure names the endpoint this product resolved and the
                // category of what went wrong, not the message.
                failures.Add(
                    $"{PackageSourceDisplay.ForDiagnostics(source)}: search failed "
                    + $"at {UrlRedaction.ForDiagnostics(searchUrl)} "
                    + $"({DescribeTransportFailure(ex)})");
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
                    && seen.Add((result.Id, result.Version)))
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
    /// A timeout's wording is generated by the client from this product's own
    /// configured <c>HttpClient.Timeout</c> and names no endpoint, so it is
    /// kept: it is how an operator learns which timeout fired. Every other
    /// message here is written by a layer that saw the remote's response or the
    /// feed-declared URL, so only the exception's category survives.
    /// </remarks>
    private static string DescribeTransportFailure(Exception error) =>
        error is TaskCanceledException or TimeoutException
            ? $"{error.GetType().Name}: {error.Message}"
            : error.GetType().Name;

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
        NuGetSourceOptions? sourceOptions = null)
    {
        log?.Invoke($"Searching packages by prefix: {prefix}");
        List<NuGetSource> sources = NuGetSourceResolver.ResolveSources(sourceOptions);
        PackageSourceMapping mapping =
            NuGetSourceResolver.ResolvePackageSourceMapping(sourceOptions);
        NuGetSearchOutcome outcome = await SearchResolvedAsync(
            client,
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
