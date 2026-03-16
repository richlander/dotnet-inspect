// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using NuGetFetch;
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

        string? explicitSource = options.Sources.Length == 1 ? options.Sources[0] : null;

        IEnumerable<string>? additional = options.AdditionalSources.Length > 0
            ? options.AdditionalSources
            : null;

        if (options.Sources.Length > 1)
        {
            explicitSource = null;
            additional = options.Sources.Concat(options.AdditionalSources);
        }

        IReadOnlyList<PackageSource> sources = SourceResolver.ResolveSources(
            explicitSource: explicitSource,
            configPath: options.ConfigFile,
            additionalSources: additional);

        return sources.Select(s => s).ToList();
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
/// Searches NuGet by delegating to NuGetFetch.SearchService.
/// </summary>
public static class NuGetSearchService
{
    public static async Task<List<NuGetSearchResult>> SearchAsync(
        HttpClient client, string query, int take = 20, bool prerelease = false, Action<string>? log = null)
    {
        log?.Invoke($"Searching NuGet: {query}");
        SearchService service = new(client);
        IReadOnlyList<SearchResult> results = await service.SearchAsync(query, take, prerelease);
        return results.Select(NuGetSearchResult.From).ToList();
    }

    public static async Task<List<NuGetSearchResult>> SearchByPrefixAsync(
        HttpClient client, string prefix, int take = 100, bool prerelease = false, Action<string>? log = null)
    {
        log?.Invoke($"Searching NuGet by prefix: {prefix}");
        SearchService service = new(client);
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
}
