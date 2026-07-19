using System.Text.Json.Serialization;
using Markout;

namespace DotnetInspector.Views;

/// <summary>
/// View model for cache info output.
/// </summary>
[MarkoutSerializable(FieldLayout = FieldLayout.Table)]
public class CacheInfoView
{
    public string Location { get; set; } = "";

    [MarkoutSection(Name = "Categories")]
    public List<CacheCategoryRow>? Categories { get; set; }

    public string? Total { get; set; }
}

[MarkoutSerializable]
public record CacheCategoryRow(string Name, string Size, string Items);

[MarkoutContext(typeof(CacheInfoView))]
public partial class CacheInfoContext : MarkoutSerializerContext
{
}

/// <summary>
/// JSON projection for cache info output (used by <c>cache --json</c>).
/// </summary>
public record CacheInfoJson(
    [property: JsonPropertyName("location")] string Location,
    [property: JsonPropertyName("total")] string Total,
    [property: JsonPropertyName("categories")] List<CacheCategoryJson> Categories);

public record CacheCategoryJson(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("size")] string Size,
    [property: JsonPropertyName("items")] string Items);

[JsonSerializable(typeof(CacheInfoJson))]
internal partial class CacheInfoJsonContext : JsonSerializerContext
{
}
