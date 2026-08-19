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
        "Product-resident inspection scenarios. Print one with "
        + "`dotnet-inspect demo <id>` (resolve-only plan; no package download).";

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

/// <summary>Resolved home-demo activation plan for <c>demo &lt;id&gt;</c>.</summary>
[MarkoutSerializable(TitleProperty = nameof(Title), DescriptionProperty = nameof(Description))]
public sealed class DemoPlanView
{
    [MarkoutIgnore]
    public string Title
    {
        get => field;
        set => field = LibraryViewText.Contain(value) ?? "";
    } = "Demo plan";

    [MarkoutIgnore]
    public string? Description
    {
        get => field;
        set => field = LibraryViewText.Contain(value);
    }

    [MarkoutSection(Headless = true)]
    public List<DemoPlanFieldRow> Plan { get; set; } = [];

    [MarkoutSection(Name = "Members")]
    public List<DemoMemberRow> Members { get; set; } = [];

    [MarkoutSection(Name = "Navigation")]
    public List<DemoNavigationRow> Navigation { get; set; } = [];
}

public sealed class DemoPlanFieldRow
{
    [MarkoutPropertyName("Field")]
    public string Field { get => field; set => field = LibraryViewText.Contain(value) ?? ""; } = "";

    [MarkoutPropertyName("Value")]
    public string Value { get => field; set => field = LibraryViewText.Contain(value) ?? ""; } = "";
}

public sealed class DemoMemberRow
{
    [MarkoutPropertyName("Kind")]
    public string Kind { get => field; set => field = LibraryViewText.Contain(value) ?? ""; } = "";

    [MarkoutPropertyName("Identity")]
    public string Identity { get => field; set => field = LibraryViewText.Contain(value) ?? ""; } = "";

    [MarkoutPropertyName("Version")]
    public string? Version { get => field; set => field = LibraryViewText.Contain(value); }

    [MarkoutPropertyName("Framework")]
    public string? Framework { get => field; set => field = LibraryViewText.Contain(value); }
}

public sealed class DemoNavigationRow
{
    [MarkoutPropertyName("Tab")]
    public string Tab { get => field; set => field = LibraryViewText.Contain(value) ?? ""; } = "";

    [MarkoutPropertyName("Focus")]
    public string Focus { get => field; set => field = LibraryViewText.Contain(value) ?? ""; } = "";

    [MarkoutPropertyName("Coordinate")]
    public string Coordinate { get => field; set => field = LibraryViewText.Contain(value) ?? ""; } = "";
}

[MarkoutContext(typeof(DemoListView))]
[MarkoutContext(typeof(DemoPlanView))]
public partial class DemoViewContext : MarkoutSerializerContext;
