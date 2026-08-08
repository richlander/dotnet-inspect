using ILInspector.CSharp;
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
    string? Detail)
{
    /// <inheritdoc cref="FindingTransitionRow"/>
    public string Transition { get; init; } = Transition;

    /// <inheritdoc cref="FindingTransitionRow"/>
    public string Finding { get; init; } = Finding;

    /// <summary>
    /// The subject the finding is about, spelled from inspected metadata, so it
    /// carries whatever the assembly author named its type or member. The
    /// versions come from the nuspec and the detail embeds a failure message
    /// that quotes them back, so all four are contained (issue #3319).
    /// <c>Transition</c>, <c>Finding</c>, <c>Old</c>, and <c>New</c> are
    /// tool-owned -- a fixed transition label, a descriptor id, and two
    /// inspection-state enums -- and are left alone. Every positional property
    /// is redeclared so the reflected order stays the constructor's.
    /// </summary>
    public string Target { get; init; } = CSharpIdentifier.ContainRenderedText(Target);

    /// <inheritdoc cref="Target"/>
    public string From { get; init; } = CSharpIdentifier.ContainRenderedText(From);

    /// <inheritdoc cref="Target"/>
    public string To { get; init; } = CSharpIdentifier.ContainRenderedText(To);

    /// <inheritdoc cref="Target"/>
    public string Old { get; init; } = Old;

    /// <inheritdoc cref="Target"/>
    public string New { get; init; } = New;

    /// <inheritdoc cref="Target"/>
    public string? Detail { get; init; } = Detail is null ? null : CSharpIdentifier.ContainRenderedText(Detail);
}

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
    string Evidence)
{
    /// <summary>
    /// <c>Member</c> is the inspected member's display spelling and
    /// <c>Evidence</c> is either a unified IL/source line or a change detail
    /// that quotes metadata, so both are untrusted and contained here (issue
    /// #3319). <c>Mechanism</c>, <c>Difference</c>, and <c>Change</c> are
    /// stringified enums and are left alone. Every positional property is
    /// redeclared so the reflected order stays the constructor's.
    /// </summary>
    public string Member { get; init; } = CSharpIdentifier.ContainRenderedText(Member);

    /// <inheritdoc cref="Member"/>
    public string Mechanism { get; init; } = Mechanism;

    /// <inheritdoc cref="Member"/>
    public string Difference { get; init; } = Difference;

    /// <inheritdoc cref="Member"/>
    public string Change { get; init; } = Change;

    /// <inheritdoc cref="Member"/>
    public string Evidence { get; init; } = CSharpIdentifier.ContainRenderedText(Evidence);
}

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
