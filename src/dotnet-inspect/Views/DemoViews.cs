using Markout;

namespace DotnetInspector.Views;

/// <summary>Catalog of product home demos for <c>demo list</c>.</summary>
[MarkoutSerializable(TitleProperty = nameof(Title), DescriptionProperty = nameof(Description))]
public sealed class DemoListView
{
    [MarkoutIgnore]
    public string Title => "Home demos";

    [MarkoutIgnore]
    public string Description =>
        "Product-resident inspection scenarios. Run one with "
        + "`dotnet-inspect demo <id>` (returns real section output).";

    [MarkoutSection(Headless = true)]
    public List<DemoListRow> Demos { get; set; } = [];
}

public sealed class DemoListRow
{
    [MarkoutPropertyName("Id")]
    public string Id { get => field; set => field = LibraryViewText.Contain(value) ?? ""; } = "";

    [MarkoutPropertyName("Title")]
    public string Title { get => field; set => field = LibraryViewText.Contain(value) ?? ""; } = "";

    [MarkoutPropertyName("Summary")]
    public string Summary { get => field; set => field = LibraryViewText.Contain(value) ?? ""; } = "";
}

[MarkoutContext(typeof(DemoListView))]
public partial class DemoViewContext : MarkoutSerializerContext;
