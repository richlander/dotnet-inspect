using Markout;

namespace DotnetInspector.Views;

[MarkoutSerializable(
    TitleProperty = nameof(Title),
    FieldLayout = FieldLayout.Table)]
public class DiffTableView
{
    [MarkoutIgnore] public string Title { get; set; } = "";
    public string Versions { get; set; } = "";
    public string Summary { get; set; } = "";

    [MarkoutSection(Name = "Changes")]
    public List<DiffTableRow>? Rows { get; set; }
}

[MarkoutSerializable]
public record DiffTableRow(string Change, string Type, string Detail);

[MarkoutSerializable(
    TitleProperty = nameof(Title),
    FieldLayout = FieldLayout.Table)]
public class DiffDetailedChangesView
{
    [MarkoutIgnore] public string Title { get; set; } = "";
    public string Versions { get; set; } = "";
    public string Summary { get; set; } = "";

    [MarkoutSection(Name = "Changes")]
    public List<DiffDetailedChangeRow>? Rows { get; set; }
}

[MarkoutSerializable]
public record DiffDetailedChangeRow(
    string Change,
    string Classification,
    string Type,
    string Member,
    string Kind,
    string Detail,
    string Old,
    string New);

[MarkoutSerializable(
    TitleProperty = nameof(Title),
    FieldLayout = FieldLayout.Table)]
public class DiffDocumentView
{
    [MarkoutIgnore] public string Title { get; set; } = "";
    public string Versions { get; set; } = "";
    public string? ChangesSummary { get; set; }
    public string? AnalysisDiffSummary { get; set; }
    public string? AnalysisDiffNote { get; set; }
    public string? ImplementationDiffSummary { get; set; }
    public string? ImplementationDiffNote { get; set; }
    public string? FindingTransitionsSummary { get; set; }

    [MarkoutSection(Name = "Changes")]
    public List<DiffDetailedChangeRow>? Changes { get; set; }

    [MarkoutSection(Name = "Analysis Diff")]
    public List<AnalysisDiffRow>? AnalysisDiff { get; set; }

    [MarkoutSection(Name = "Implementation Diff")]
    public List<ImplementationDiffRow>? ImplementationDiff { get; set; }

    [MarkoutSection(Name = "Finding Transitions")]
    public List<FindingTransitionRow>? FindingTransitions { get; set; }
}

[MarkoutSerializable(
    TitleProperty = nameof(Title),
    FieldLayout = FieldLayout.Table)]
public class FindingTransitionsView
{
    [MarkoutIgnore] public string Title { get; set; } = "";
    public string Versions { get; set; } = "";
    public Callout Status { get; set; }

    [MarkoutSection(Name = "Finding Transitions")]
    public List<FindingTransitionRow>? Rows { get; set; }
}

[MarkoutSerializable]
public record FindingTransitionRow(
    string Transition,
    string Finding,
    string Target,
    string From,
    string To,
    string Old,
    string New,
    string? Detail);

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

[MarkoutSerializable(
    TitleProperty = nameof(Title),
    FieldLayout = FieldLayout.Table)]
public class ImplementationDiffView
{
    [MarkoutIgnore] public string Title { get; set; } = "";
    public string Versions { get; set; } = "";
    public string Summary { get; set; } = "";
    public Callout Status { get; set; }

    [MarkoutSection(Name = "Implementation Diff")]
    public List<ImplementationDiffRow>? Rows { get; set; }
}

[MarkoutSerializable]
public record ImplementationDiffRow(
    string Member,
    string Mechanism,
    string Difference,
    string Change,
    string Evidence);

[MarkoutSerializable]
public record DiffChangeRow(
    [property: MarkoutIgnore] string TypeName,
    string Message);

[MarkoutContextOptions(SuppressTableWarnings = true)]
[MarkoutContext(typeof(DiffTableView))]
[MarkoutContext(typeof(DiffTableRow))]
[MarkoutContext(typeof(DiffDetailedChangesView))]
[MarkoutContext(typeof(DiffDetailedChangeRow))]
[MarkoutContext(typeof(DiffDocumentView))]
[MarkoutContext(typeof(FindingTransitionsView))]
[MarkoutContext(typeof(FindingTransitionRow))]
[MarkoutContext(typeof(DiffFullView))]
[MarkoutContext(typeof(DiffChangeRow))]
[MarkoutContext(typeof(AnalysisDiffView))]
[MarkoutContext(typeof(AnalysisDiffRow))]
[MarkoutContext(typeof(ImplementationDiffView))]
[MarkoutContext(typeof(ImplementationDiffRow))]
public partial class DiffViewContext : MarkoutSerializerContext
{
}
