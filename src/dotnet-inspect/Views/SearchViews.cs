using Markout;

namespace DotnetInspector.Views;

/// <summary>
/// View model for NuGet package search results.
/// </summary>
[MarkoutSerializable(
    TitleProperty = nameof(Title),
    DescriptionProperty = nameof(Description))]
public class PackageSearchResultView
{
    [MarkoutIgnore] public string Title { get; set; } = "";
    [MarkoutIgnore] public string? Description { get; set; }

    [MarkoutIgnoreInTable]
    public List<PackageSearchRow>? Results { get; set; }
}

[MarkoutSerializable]
public record PackageSearchRow(string Package, string Version, string Downloads, string Description);

[MarkoutContext(typeof(PackageSearchResultView))]
public partial class PackageSearchResultContext : MarkoutSerializerContext
{
}
