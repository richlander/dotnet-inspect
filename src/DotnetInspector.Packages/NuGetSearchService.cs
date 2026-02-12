// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;

namespace DotnetInspector.Packages;

/// <summary>
/// Result from a NuGet package search query.
/// </summary>
/// <param name="PackageId">NuGet package identifier</param>
/// <param name="Version">Latest version</param>
/// <param name="Description">Package description</param>
/// <param name="TotalDownloads">Total download count</param>
/// <param name="Verified">Whether the package owner is verified</param>
public record NuGetSearchResult(
    string PackageId,
    string Version,
    string? Description,
    long TotalDownloads,
    bool Verified);

/// <summary>
/// Searches the NuGet Search API for packages by keyword or prefix.
/// </summary>
public static class NuGetSearchService
{
    private const string DefaultSearchUrl = "https://azuresearch-usnc.nuget.org/query";

    /// <summary>
    /// Searches NuGet for packages matching the given query.
    /// </summary>
    /// <param name="client">HTTP client</param>
    /// <param name="query">Search query (keyword, package ID prefix, etc.)</param>
    /// <param name="take">Maximum number of results (default 20)</param>
    /// <param name="prerelease">Include prerelease versions</param>
    /// <param name="log">Optional logging callback</param>
    /// <returns>List of matching packages</returns>
    public static async Task<List<NuGetSearchResult>> SearchAsync(
        HttpClient client,
        string query,
        int take = 20,
        bool prerelease = false,
        Action<string>? log = null)
    {
        string url = $"{DefaultSearchUrl}?q={Uri.EscapeDataString(query)}&take={take}&prerelease={prerelease.ToString().ToLowerInvariant()}";
        log?.Invoke($"Searching NuGet: {url}");

        string? json = await HttpRetryHelper.GetStringWithRetryAsync(client, url, log: log);
        if (json == null)
            return [];

        return ParseSearchResponse(json);
    }

    /// <summary>
    /// Searches NuGet for packages whose ID starts with the given prefix.
    /// Uses the packageid: search qualifier for exact prefix matching.
    /// </summary>
    /// <param name="client">HTTP client</param>
    /// <param name="prefix">Package ID prefix (e.g., "Azure.AI", "AWSSDK")</param>
    /// <param name="take">Maximum number of results (default 100)</param>
    /// <param name="prerelease">Include prerelease versions</param>
    /// <param name="log">Optional logging callback</param>
    /// <returns>List of matching package IDs</returns>
    public static async Task<List<NuGetSearchResult>> SearchByPrefixAsync(
        HttpClient client,
        string prefix,
        int take = 100,
        bool prerelease = false,
        Action<string>? log = null)
    {
        // Use plain keyword search — packageid: qualifier does exact match, not prefix.
        // We filter client-side to packages whose ID starts with the prefix.
        string url = $"{DefaultSearchUrl}?q={Uri.EscapeDataString(prefix)}&take={take}&prerelease={prerelease.ToString().ToLowerInvariant()}";
        log?.Invoke($"Searching NuGet by prefix: {url}");

        string? json = await HttpRetryHelper.GetStringWithRetryAsync(client, url, log: log);
        if (json == null)
            return [];

        var results = ParseSearchResponse(json);

        // Filter to only packages whose ID starts with the prefix (case-insensitive)
        return results
            .Where(r => r.PackageId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Parses a NuGet Search API JSON response into search results.
    /// </summary>
    public static List<NuGetSearchResult> ParseSearchResponse(string json)
    {
        List<NuGetSearchResult> results = [];

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data))
                return results;

            foreach (var item in data.EnumerateArray())
            {
                string packageId = item.GetProperty("id").GetString() ?? "";
                string version = item.GetProperty("version").GetString() ?? "";

                string? description = null;
                if (item.TryGetProperty("description", out var desc))
                    description = desc.GetString();

                long totalDownloads = 0;
                if (item.TryGetProperty("totalDownloads", out var dl))
                    totalDownloads = dl.GetInt64();

                bool verified = false;
                if (item.TryGetProperty("verified", out var v))
                    verified = v.GetBoolean();

                results.Add(new NuGetSearchResult(packageId, version, description, totalDownloads, verified));
            }
        }
        catch (JsonException)
        {
            // Malformed response — return what we have
        }

        return results;
    }
}
