using System.Text.Json;
using System.Text.Json.Serialization;

namespace NuGetFetch;

public static class NuGetApi
{
    private static readonly NuGetFetchOptions DefaultOptions = new();

    public static ValueTask<ServiceIndex?> GetServiceIndexAsync(
        Stream json,
        CancellationToken cancellationToken = default) =>
        NuGetMetadataReader.ReadStreamAsync(
            json,
            DeserializeServiceIndexAsync,
            DefaultOptions,
            cancellationToken);

    internal static ValueTask<ServiceIndex?> DeserializeServiceIndexAsync(
        Stream json,
        CancellationToken cancellationToken) =>
        JsonSerializer.DeserializeAsync(
            json,
            NuGetJsonContext.Default.ServiceIndex,
            cancellationToken);

    public static ValueTask<VersionIndex?> GetVersionIndexAsync(
        Stream json,
        CancellationToken cancellationToken = default) =>
        NuGetMetadataReader.ReadStreamAsync(
            json,
            DeserializeVersionIndexAsync,
            DefaultOptions,
            cancellationToken);

    internal static ValueTask<VersionIndex?> DeserializeVersionIndexAsync(
        Stream json,
        CancellationToken cancellationToken) =>
        JsonSerializer.DeserializeAsync(
            json,
            NuGetJsonContext.Default.VersionIndex,
            cancellationToken);

    /// <summary>
    /// Deserializes a NuGet V3 search response.
    /// </summary>
    /// <remarks>
    /// Like the service-index and version-index readers above, a malformed
    /// document propagates as <see cref="JsonException"/> instead of being
    /// reported as an absent response. A swallowed parse failure here is
    /// indistinguishable from a genuine zero-result search, which turns a hard
    /// failure into success-shaped empty output. See issue #3417.
    /// </remarks>
    public static ValueTask<SearchResponse?> GetSearchResponseAsync(
        Stream json,
        CancellationToken cancellationToken = default) =>
        NuGetMetadataReader.ReadStreamAsync(
            json,
            DeserializeSearchResponseAsync,
            DefaultOptions,
            cancellationToken);

    internal static async ValueTask<SearchResponse?> DeserializeSearchResponseAsync(
        Stream json,
        CancellationToken cancellationToken) =>
        await JsonSerializer.DeserializeAsync(
            json,
            NuGetJsonContext.Default.SearchResponse,
            cancellationToken).ConfigureAwait(false);
}

// Feeds disagree about whether a JSON number is a number. Azure DevOps Artifacts
// serialises counts as strings ("0"), which is a common way to keep 64-bit values
// out of a JavaScript double; nuget.org sends them as numbers. Reading a count
// strictly therefore rejects an otherwise valid document from a conforming feed,
// and takes every result in it down with it -- that is how the Azure DevOps search
// defect in issue #3417 presented. Modelling totalHits away fixed that field; the
// remaining counts are the two members most likely to be spelled the same way,
// because both are Int64. Accept either spelling for all of them, once, so the
// next stringified count is not a second outage.
//
// This only affects reading. Nothing serialises through this context.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    NumberHandling = JsonNumberHandling.AllowReadingFromString)]
[JsonSerializable(typeof(ServiceIndex))]
[JsonSerializable(typeof(VersionIndex))]
[JsonSerializable(typeof(SearchResponse))]
public partial class NuGetJsonContext : JsonSerializerContext
{
}
