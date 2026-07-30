using System.Text.Json;
using System.Text.Json.Serialization;

namespace NuGetFetch;

public static class NuGetApi
{
    public static async ValueTask<ServiceIndex?> GetServiceIndexAsync(Stream json, CancellationToken cancellationToken = default)
    {
        try
        {
            return await JsonSerializer.DeserializeAsync(json, NuGetJsonContext.Default.ServiceIndex, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static async ValueTask<VersionIndex?> GetVersionIndexAsync(Stream json, CancellationToken cancellationToken = default)
    {
        try
        {
            return await JsonSerializer.DeserializeAsync(json, NuGetJsonContext.Default.VersionIndex, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Deserializes a NuGet V3 search response.
    /// </summary>
    /// <remarks>
    /// Unlike the service-index and version-index readers above, a malformed
    /// document propagates as <see cref="JsonException"/> instead of being
    /// reported as an absent response. A swallowed parse failure here is
    /// indistinguishable from a genuine zero-result search, which turns a hard
    /// failure into success-shaped empty output. See issue #3417.
    /// </remarks>
    public static async ValueTask<SearchResponse?> GetSearchResponseAsync(Stream json, CancellationToken cancellationToken = default)
        => await JsonSerializer.DeserializeAsync(json, NuGetJsonContext.Default.SearchResponse, cancellationToken).ConfigureAwait(false);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ServiceIndex))]
[JsonSerializable(typeof(VersionIndex))]
[JsonSerializable(typeof(SearchResponse))]
public partial class NuGetJsonContext : JsonSerializerContext
{
}
