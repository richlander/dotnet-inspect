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

/// <summary>Why a keyword-search page stopped.</summary>
public enum SearchPageCompletion
{
    /// <summary>The source returned fewer rows than requested.</summary>
    ShortPage,

    /// <summary>The requested page maximum was reached.</summary>
    RequestedLimit,
}

/// <summary>A keyword-search page with explicit completion evidence.</summary>
public sealed record SearchPageResult(
    IReadOnlyList<SearchResult> Results,
    SearchPageCompletion Completion)
{
    public bool Truncated =>
        Completion == SearchPageCompletion.RequestedLimit;
}

public record SearchResult(
    string Id,
    string Version,
    string? Description = null,
    long TotalDownloads = 0,
    bool Verified = false,
    IReadOnlyList<SearchVersion>? Versions = null,
    [property: JsonConverter(typeof(StringOrArrayJsonConverter))]
    IReadOnlyList<string>? Owners = null);

public record SearchVersion(
    string Version,
    long Downloads);

internal sealed record PrefixSearchWireResponse(
    IReadOnlyList<PrefixSearchWireResult> Data);

internal sealed record PrefixSearchWireResult(
    string Id,
    string Version,
    string? Description = null,
    long TotalDownloads = 0,
    bool Verified = false,
    [property: JsonConverter(typeof(StringOrArrayJsonConverter))]
    IReadOnlyList<string>? Owners = null);
