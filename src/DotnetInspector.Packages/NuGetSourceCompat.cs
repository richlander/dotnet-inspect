// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;

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
/// Resolves NuGet sources using NuGet.Configuration.
/// </summary>
public static class NuGetSourceResolver
{
    public static List<PackageSource> ResolveSources(NuGetSourceOptions? options, string? workingDirectory = null)
    {
        options ??= NuGetSourceOptions.Default;

        List<PackageSource> sources = options.Sources.Length > 0
            ? options.Sources.Select(CreateCommandLineSource).ToList()
            : LoadConfiguredSources(options.ConfigFile, workingDirectory);

        sources.AddRange(options.AdditionalSources.Select(CreateCommandLineSource));

        return sources
            .GroupBy(s => s.Url, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    private static List<PackageSource> LoadConfiguredSources(string? configFile, string? workingDirectory)
    {
        string root = workingDirectory ?? Directory.GetCurrentDirectory();
        NuGet.Configuration.ISettings settings;

        if (!string.IsNullOrWhiteSpace(configFile))
        {
            string fullPath = Path.GetFullPath(configFile);
            settings = NuGet.Configuration.Settings.LoadSpecificSettings(
                Path.GetDirectoryName(fullPath),
                Path.GetFileName(fullPath));
        }
        else
        {
            settings = NuGet.Configuration.Settings.LoadDefaultSettings(root);
        }

        var provider = new NuGet.Configuration.PackageSourceProvider(settings);
        var configuredSources = provider.LoadPackageSources()
            .Where(s => s.IsEnabled)
            .Select(PackageSource.FromNuGetPackageSource)
            .ToList();

        return configuredSources.Count > 0 ? configuredSources : [PackageSource.NuGetOrg];
    }

    private static PackageSource CreateCommandLineSource(string source)
    {
        if (source.Equals("nuget.org", StringComparison.OrdinalIgnoreCase)
            || source.Equals(PackageSource.NuGetOrg.Url, StringComparison.OrdinalIgnoreCase))
            return PackageSource.NuGetOrg;

        string name = Uri.TryCreate(source, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host)
            ? uri.Host
            : source;

        return new PackageSource(name, source);
    }
}

/// <summary>
/// Search result shape used by dotnet-inspect.
/// </summary>
public record NuGetSearchResult(
    string PackageId,
    string Version,
    string? Description,
    long TotalDownloads,
    bool Verified)
;

/// <summary>
/// Searches NuGet using the V3 search endpoint.
/// </summary>
public static class NuGetSearchService
{
    public static async Task<List<NuGetSearchResult>> SearchAsync(
        HttpClient client, string query, int take = 20, bool prerelease = false, Action<string>? log = null)
    {
        log?.Invoke($"Searching NuGet: {query}");
        string searchUrl = $"https://azuresearch-usnc.nuget.org/query?q={Uri.EscapeDataString(query)}&take={take}&prerelease={prerelease.ToString().ToLowerInvariant()}";
        string? json = await HttpRetryHelper.GetStringWithRetryAsync(client, searchUrl, log: log).ConfigureAwait(false);
        return json == null ? [] : ParseSearchResponse(json);
    }

    public static async Task<List<NuGetSearchResult>> SearchByPrefixAsync(
        HttpClient client, string prefix, int take = 100, bool prerelease = false, Action<string>? log = null)
    {
        log?.Invoke($"Searching NuGet by prefix: {prefix}");
        string query = $"packageid:{prefix}";
        List<NuGetSearchResult> results = await SearchAsync(client, query, take, prerelease, log).ConfigureAwait(false);
        return results
            .Where(r => r.PackageId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public static List<NuGetSearchResult> ParseSearchResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return [];

        List<NuGetSearchResult> results = [];
        foreach (var item in data.EnumerateArray())
        {
            string? id = item.TryGetProperty("id", out var idProperty) ? idProperty.GetString() : null;
            string? version = item.TryGetProperty("version", out var versionProperty) ? versionProperty.GetString() : null;

            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(version))
                continue;

            string? description = item.TryGetProperty("description", out var descriptionProperty)
                ? descriptionProperty.GetString()
                : null;
            long totalDownloads = item.TryGetProperty("totalDownloads", out var downloadsProperty)
                && downloadsProperty.TryGetInt64(out long downloads)
                    ? downloads
                    : 0;
            bool verified = item.TryGetProperty("verified", out var verifiedProperty)
                && verifiedProperty.ValueKind == JsonValueKind.True;

            results.Add(new NuGetSearchResult(id, version, description, totalDownloads, verified));
        }

        return results;
    }
}
