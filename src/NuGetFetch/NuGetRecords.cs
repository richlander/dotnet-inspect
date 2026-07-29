using System.Text.Json.Serialization;

namespace NuGetFetch;

// NuGet V3 Service Index

public record ServiceIndex(
    string Version,
    IReadOnlyList<ServiceResource> Resources);

public record ServiceResource(
    [property: JsonPropertyName("@id")] string Id,
    [property: JsonPropertyName("@type")] string Type,
    string? Comment = null);

// NuGet V3 Flat-Container Version Index

public record VersionIndex(
    IReadOnlyList<string> Versions);

// NuGet Search API

public record SearchResponse(
    int TotalHits,
    IReadOnlyList<SearchResult> Data);

public record SearchResult(
    string Id,
    string Version,
    string? Description = null,
    long TotalDownloads = 0,
    bool Verified = false,
    IReadOnlyList<SearchVersion>? Versions = null);

public record SearchVersion(
    string Version,
    long Downloads);
