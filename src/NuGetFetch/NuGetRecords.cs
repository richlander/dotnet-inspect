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

// The wire format carries a "totalHits" field that is deliberately not modelled
// here. nuget.org serialises it as a JSON number and Azure DevOps serialises it
// as a string, so a single typed property rejects one of the two feeds outright:
// binding it to int made every Azure DevOps search throw and report zero
// results. Azure DevOps also returns "0" alongside a populated data array, so
// the value is not trustworthy even when it does parse. Nothing consumes it —
// Data.Count is the real result count. See issue #3417.
public record SearchResponse(
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
