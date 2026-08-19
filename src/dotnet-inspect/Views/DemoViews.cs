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
    public string Id { get; set; } = "";

    [MarkoutPropertyName("Title")]
    public string Title { get; set; } = "";

    [MarkoutPropertyName("Summary")]
    public string Summary { get; set; } = "";
}

/// <summary>Resolved home-demo activation plan for <c>demo &lt;id&gt;</c>.</summary>
[MarkoutSerializable(TitleProperty = nameof(Title), DescriptionProperty = nameof(Description))]
public sealed class DemoPlanView
{
    [MarkoutIgnore]
    public string Title { get; set; } = "Demo plan";

    [MarkoutIgnore]
    public string? Description { get; set; }

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
    public string Field { get; set; } = "";

    [MarkoutPropertyName("Value")]
    public string Value { get; set; } = "";
}

public sealed class DemoMemberRow
{
    [MarkoutPropertyName("Kind")]
    public string Kind { get; set; } = "";

    [MarkoutPropertyName("Identity")]
    public string Identity { get; set; } = "";

    [MarkoutPropertyName("Version")]
    public string? Version { get; set; }

    [MarkoutPropertyName("Framework")]
    public string? Framework { get; set; }
}

public sealed class DemoNavigationRow
{
    [MarkoutPropertyName("Tab")]
    public string Tab { get; set; } = "";

    [MarkoutPropertyName("Focus")]
    public string Focus { get; set; } = "";

    [MarkoutPropertyName("Coordinate")]
    public string Coordinate { get; set; } = "";
}

[MarkoutContext(typeof(DemoListView))]
[MarkoutContext(typeof(DemoPlanView))]
public partial class DemoViewContext : MarkoutSerializerContext;
