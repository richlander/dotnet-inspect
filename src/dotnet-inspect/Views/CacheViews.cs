using Markout;

namespace DotnetInspector.Views;

/// <summary>
/// View model for cache info output.
/// </summary>
[MarkoutSerializable(FieldLayout = FieldLayout.Plain)]
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
