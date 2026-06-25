using Markout;

namespace DotnetInspector.Views;

[MarkoutSerializable(
    TitleProperty = nameof(Title),
    FieldLayout = FieldLayout.Table)]
public class DiffOneLineView
{
    [MarkoutIgnore] public string Title { get; set; } = "";
    public string Versions { get; set; } = "";
    public string Summary { get; set; } = "";

    [MarkoutSection(Name = "Changes")]
    public List<DiffOneLineRow>? Rows { get; set; }
}

[MarkoutSerializable]
public record DiffOneLineRow(string Change, string Type, string Detail);

/// <summary>
/// View model for full diff rendering. Uses GroupBy to partition changes
/// by type name, rendering each type as a subheading with its changes as list items.
/// </summary>
[MarkoutSerializable(TitleProperty = nameof(Title))]
public class DiffFullView
{
    [MarkoutIgnore] public string Title { get; set; } = "";
    public string Versions { get; set; } = "";
    public string Summary { get; set; } = "";
    public Callout Status { get; set; }

    [MarkoutSection(Name = "Breaking Changes", GroupBy = nameof(DiffChangeRow.TypeName))]
    public List<DiffChangeRow>? BreakingChanges { get; set; }

    [MarkoutSection(Name = "Potentially Breaking Changes", GroupBy = nameof(DiffChangeRow.TypeName))]
    public List<DiffChangeRow>? PotentiallyBreakingChanges { get; set; }

    [MarkoutSection(Name = "Additive Changes", GroupBy = nameof(DiffChangeRow.TypeName))]
    public List<DiffChangeRow>? AdditiveChanges { get; set; }
}

[MarkoutSerializable(
    TitleProperty = nameof(Title),
    FieldLayout = FieldLayout.Table)]
public class AnalysisDiffView
{
    [MarkoutIgnore] public string Title { get; set; } = "";
    public string Versions { get; set; } = "";
    public string Summary { get; set; } = "";
    public Callout Status { get; set; }

    [MarkoutSection(Name = "Analysis Diff")]
    public List<AnalysisDiffRow>? Rows { get; set; }
}

[MarkoutSerializable]
public record AnalysisDiffRow(
    string Member,
    string Signal,
    string Old,
    string New,
    string Delta,
    string? Shape,
    string? Evidence);

[MarkoutSerializable]
public record DiffChangeRow(
    [property: MarkoutIgnore] string TypeName,
    string Message);

[MarkoutContextOptions(SuppressTableWarnings = true)]
[MarkoutContext(typeof(DiffOneLineView))]
[MarkoutContext(typeof(DiffOneLineRow))]
[MarkoutContext(typeof(DiffFullView))]
[MarkoutContext(typeof(DiffChangeRow))]
[MarkoutContext(typeof(AnalysisDiffView))]
[MarkoutContext(typeof(AnalysisDiffRow))]
public partial class DiffViewContext : MarkoutSerializerContext
{
}
