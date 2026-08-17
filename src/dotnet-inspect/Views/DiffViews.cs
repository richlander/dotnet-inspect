using System.Text.Json.Serialization;
using InertText;
using Markout;

namespace DotnetInspector.Views;

internal static class DiffViewText
{
    public static InertString Field(string value) => new(TextPolicy.Field, value);
    public static InertString Prose(string value) => new(TextPolicy.Prose, value);
}

[MarkoutSerializable(
    TitleProperty = nameof(Title),
    FieldLayout = FieldLayout.Table)]
public class DiffTableView(
    InertString titleText,
    InertString versionsText,
    InertString summaryText)
{
    [MarkoutIgnore, JsonIgnore] public InertString TitleText { get; } = titleText;
    [MarkoutIgnore, JsonIgnore] public InertString VersionsText { get; } = versionsText;
    [MarkoutIgnore, JsonIgnore] public InertString SummaryText { get; } = summaryText;
    [MarkoutIgnore] public string Title => TitleText.ToString();
    public string Versions => VersionsText.ToString();
    public string Summary => SummaryText.ToString();

    [MarkoutSection(Name = "Changes")]
    public List<DiffTableRow>? Rows { get; set; }
}

[MarkoutSerializable]
public record DiffTableRow(
    [property: MarkoutIgnore, JsonIgnore] InertString ChangeText,
    [property: MarkoutIgnore, JsonIgnore] InertString TypeText,
    [property: MarkoutIgnore, JsonIgnore] InertString DetailText)
{
    public DiffTableRow(string change, string type, string detail)
        : this(DiffViewText.Field(change), DiffViewText.Field(type), DiffViewText.Field(detail))
    {
    }

    public string Change => ChangeText.ToString();
    public string Type => TypeText.ToString();
    public string Detail => DetailText.ToString();
}

[MarkoutSerializable(
    TitleProperty = nameof(Title),
    FieldLayout = FieldLayout.Table)]
public class DiffDetailedChangesView(
    InertString titleText,
    InertString versionsText,
    InertString summaryText)
{
    [MarkoutIgnore, JsonIgnore] public InertString TitleText { get; } = titleText;
    [MarkoutIgnore, JsonIgnore] public InertString VersionsText { get; } = versionsText;
    [MarkoutIgnore, JsonIgnore] public InertString SummaryText { get; } = summaryText;
    [MarkoutIgnore] public string Title => TitleText.ToString();
    public string Versions => VersionsText.ToString();
    public string Summary => SummaryText.ToString();

    [MarkoutSection(Name = "Changes")]
    public List<DiffDetailedChangeRow>? Rows { get; set; }
}

[MarkoutSerializable]
public record DiffDetailedChangeRow(
    [property: MarkoutIgnore, JsonIgnore] InertString ChangeText,
    [property: MarkoutIgnore, JsonIgnore] InertString ClassificationText,
    [property: MarkoutIgnore, JsonIgnore] InertString TypeText,
    [property: MarkoutIgnore, JsonIgnore] InertString MemberText,
    [property: MarkoutIgnore, JsonIgnore] InertString KindText,
    [property: MarkoutIgnore, JsonIgnore] InertString DetailText,
    [property: MarkoutIgnore, JsonIgnore] InertString OldText,
    [property: MarkoutIgnore, JsonIgnore] InertString NewText)
{
    public string Change => ChangeText.ToString();
    public string Classification => ClassificationText.ToString();
    public string Type => TypeText.ToString();
    public string Member => MemberText.ToString();
    public string Kind => KindText.ToString();
    public string Detail => DetailText.ToString();
    public string Old => OldText.ToString();
    public string New => NewText.ToString();
}

[MarkoutSerializable(
    TitleProperty = nameof(Title),
    FieldLayout = FieldLayout.Table)]
public class DiffDocumentView(
    InertString titleText,
    InertString versionsText,
    InertString? changesSummaryText,
    InertString? analysisDiffSummaryText,
    InertString? analysisDiffNoteText,
    InertString? implementationDiffSummaryText,
    InertString? implementationDiffNoteText,
    InertString? findingTransitionsSummaryText,
    InertString? inspectionFailuresSummaryText)
{
    [MarkoutIgnore, JsonIgnore] public InertString TitleText { get; } = titleText;
    [MarkoutIgnore, JsonIgnore] public InertString VersionsText { get; } = versionsText;
    [MarkoutIgnore, JsonIgnore] public InertString? ChangesSummaryText { get; } = changesSummaryText;
    [MarkoutIgnore, JsonIgnore] public InertString? AnalysisDiffSummaryText { get; } = analysisDiffSummaryText;
    [MarkoutIgnore, JsonIgnore] public InertString? AnalysisDiffNoteText { get; } = analysisDiffNoteText;
    [MarkoutIgnore, JsonIgnore] public InertString? ImplementationDiffSummaryText { get; } = implementationDiffSummaryText;
    [MarkoutIgnore, JsonIgnore] public InertString? ImplementationDiffNoteText { get; } = implementationDiffNoteText;
    [MarkoutIgnore, JsonIgnore] public InertString? FindingTransitionsSummaryText { get; } = findingTransitionsSummaryText;
    [MarkoutIgnore, JsonIgnore] public InertString? InspectionFailuresSummaryText { get; } = inspectionFailuresSummaryText;

    [MarkoutIgnore] public string Title => TitleText.ToString();
    public string Versions => VersionsText.ToString();
    [MarkoutSkipNull] public string? ChangesSummary => ChangesSummaryText?.ToString();
    [MarkoutSkipNull] public string? AnalysisDiffSummary => AnalysisDiffSummaryText?.ToString();
    [MarkoutSkipNull] public string? AnalysisDiffNote => AnalysisDiffNoteText?.ToString();
    [MarkoutSkipNull] public string? ImplementationDiffSummary => ImplementationDiffSummaryText?.ToString();
    [MarkoutSkipNull] public string? ImplementationDiffNote => ImplementationDiffNoteText?.ToString();
    [MarkoutSkipNull] public string? FindingTransitionsSummary => FindingTransitionsSummaryText?.ToString();
    [MarkoutSkipNull] public string? InspectionFailuresSummary => InspectionFailuresSummaryText?.ToString();

    [MarkoutSection(Name = "Changes")]
    public List<DiffDetailedChangeRow>? Changes { get; set; }

    [MarkoutSection(Name = "Analysis Diff")]
    public List<AnalysisDiffRow>? AnalysisDiff { get; set; }

    [MarkoutSection(Name = "Implementation Diff")]
    public List<ImplementationDiffRow>? ImplementationDiff { get; set; }

    [MarkoutSection(Name = "Finding Transitions")]
    public List<FindingTransitionRow>? FindingTransitions { get; set; }

    [MarkoutSection(Name = "Inspection Failures")]
    public List<DiffInspectionFailureRow>? InspectionFailures { get; set; }
}

[MarkoutSerializable(
    TitleProperty = nameof(Title),
    FieldLayout = FieldLayout.Table)]
public class FindingTransitionsView(
    InertString titleText,
    InertString versionsText)
{
    [MarkoutIgnore, JsonIgnore] public InertString TitleText { get; } = titleText;
    [MarkoutIgnore, JsonIgnore] public InertString VersionsText { get; } = versionsText;
    [MarkoutIgnore] public string Title => TitleText.ToString();
    public string Versions => VersionsText.ToString();
    public Callout Status { get; set; }

    [MarkoutSection(Name = "Finding Transitions")]
    public List<FindingTransitionRow>? Rows { get; set; }
}

[MarkoutSerializable]
public record FindingTransitionRow(
    [property: MarkoutIgnore, JsonIgnore] InertString TransitionText,
    [property: MarkoutIgnore, JsonIgnore] InertString FindingText,
    [property: MarkoutIgnore, JsonIgnore] InertString TargetText,
    [property: MarkoutIgnore, JsonIgnore] InertString FromText,
    [property: MarkoutIgnore, JsonIgnore] InertString ToText,
    [property: MarkoutIgnore, JsonIgnore] InertString OldText,
    [property: MarkoutIgnore, JsonIgnore] InertString NewText,
    [property: MarkoutIgnore, JsonIgnore] InertString? DetailText)
{
    public FindingTransitionRow(
        string transition,
        string finding,
        string target,
        string from,
        string to,
        string old,
        string @new,
        string? detail)
        : this(
            DiffViewText.Field(transition),
            DiffViewText.Field(finding),
            DiffViewText.Field(target),
            DiffViewText.Field(from),
            DiffViewText.Field(to),
            DiffViewText.Field(old),
            DiffViewText.Field(@new),
            detail is null ? null : DiffViewText.Field(detail))
    {
    }

    public string Transition => TransitionText.ToString();
    public string Finding => FindingText.ToString();
    public string Target => TargetText.ToString();
    public string From => FromText.ToString();
    public string To => ToText.ToString();
    public string Old => OldText.ToString();
    public string New => NewText.ToString();
    [MarkoutSkipNull] public string? Detail => DetailText?.ToString();
}

/// <summary>
/// View model for full diff rendering. Uses GroupBy to partition changes
/// by type name, rendering each type as a subheading with its changes as list items.
/// </summary>
[MarkoutSerializable(TitleProperty = nameof(Title))]
public class DiffFullView(
    InertString titleText,
    InertString versionsText,
    InertString summaryText)
{
    [MarkoutIgnore, JsonIgnore] public InertString TitleText { get; } = titleText;
    [MarkoutIgnore, JsonIgnore] public InertString VersionsText { get; } = versionsText;
    [MarkoutIgnore, JsonIgnore] public InertString SummaryText { get; set; } = summaryText;
    [MarkoutIgnore] public string Title => TitleText.ToString();
    public string Versions => VersionsText.ToString();
    public string Summary => SummaryText.ToString();
    public Callout Status { get; set; }

    [MarkoutSection(Name = "Breaking Changes", GroupBy = nameof(DiffChangeRow.TypeName))]
    public List<DiffChangeRow>? BreakingChanges { get; set; }

    [MarkoutSection(Name = "Potentially Breaking Changes", GroupBy = nameof(DiffChangeRow.TypeName))]
    public List<DiffChangeRow>? PotentiallyBreakingChanges { get; set; }

    [MarkoutSection(Name = "Additive Changes", GroupBy = nameof(DiffChangeRow.TypeName))]
    public List<DiffChangeRow>? AdditiveChanges { get; set; }

    [MarkoutSection(Name = "Inspection Failures")]
    public List<DiffInspectionFailureRow>? InspectionFailures { get; set; }
}

[MarkoutSerializable]
public record DiffInspectionFailureRow(
    string Side,
    string Assembly,
    string Operation,
    string Subject,
    string Mechanism,
    string Kind,
    string Detail,
    string? DependencyAssembly = null)
{
    public string Side { get; init; } =
        CSharpIdentifier.ContainRenderedText(Side);
    public string Assembly { get; init; } =
        CSharpIdentifier.ContainRenderedText(Assembly);
    public string Operation { get; init; } =
        CSharpIdentifier.ContainRenderedText(Operation);
    public string Subject { get; init; } =
        CSharpIdentifier.ContainRenderedText(Subject);
    public string Mechanism { get; init; } =
        CSharpIdentifier.ContainRenderedText(Mechanism);
    public string Kind { get; init; } =
        CSharpIdentifier.ContainRenderedText(Kind);
    public string Detail { get; init; } =
        CSharpIdentifier.ContainRenderedText(Detail);
    [MarkoutSkipNull]
    public string? DependencyAssembly { get; init; } =
        DependencyAssembly is null
            ? null
            : CSharpIdentifier.ContainRenderedText(
                DependencyAssembly);
}

[MarkoutSerializable(
    TitleProperty = nameof(Title),
    FieldLayout = FieldLayout.Table)]
public class AnalysisDiffView(
    InertString titleText,
    InertString versionsText,
    InertString summaryText)
{
    [MarkoutIgnore, JsonIgnore] public InertString TitleText { get; } = titleText;
    [MarkoutIgnore, JsonIgnore] public InertString VersionsText { get; } = versionsText;
    [MarkoutIgnore, JsonIgnore] public InertString SummaryText { get; } = summaryText;
    [MarkoutIgnore] public string Title => TitleText.ToString();
    public string Versions => VersionsText.ToString();
    public string Summary => SummaryText.ToString();
    public Callout Status { get; set; }

    [MarkoutSection(Name = "Analysis Diff")]
    public List<AnalysisDiffRow>? Rows { get; set; }
}

[MarkoutSerializable]
public record AnalysisDiffRow(
    [property: MarkoutIgnore, JsonIgnore] InertString MemberText,
    [property: MarkoutIgnore, JsonIgnore] InertString SignalText,
    [property: MarkoutIgnore, JsonIgnore] InertString OldText,
    [property: MarkoutIgnore, JsonIgnore] InertString NewText,
    [property: MarkoutIgnore, JsonIgnore] InertString DeltaText,
    [property: MarkoutIgnore, JsonIgnore] InertString? ShapeText,
    [property: MarkoutIgnore, JsonIgnore] InertString? EvidenceText)
{
    public AnalysisDiffRow(
        string member,
        string signal,
        string old,
        string @new,
        string delta,
        string? shape,
        string? evidence)
        : this(
            DiffViewText.Field(member),
            DiffViewText.Field(signal),
            DiffViewText.Field(old),
            DiffViewText.Field(@new),
            DiffViewText.Field(delta),
            shape is null ? null : DiffViewText.Field(shape),
            evidence is null ? null : DiffViewText.Field(evidence))
    {
    }

    public string Member => MemberText.ToString();
    public string Signal => SignalText.ToString();
    public string Old => OldText.ToString();
    public string New => NewText.ToString();
    public string Delta => DeltaText.ToString();
    [MarkoutSkipNull] public string? Shape => ShapeText?.ToString();
    [MarkoutSkipNull] public string? Evidence => EvidenceText?.ToString();
}

[MarkoutSerializable(
    TitleProperty = nameof(Title),
    FieldLayout = FieldLayout.Table)]
public class ImplementationDiffView(
    InertString titleText,
    InertString versionsText,
    InertString summaryText)
{
    [MarkoutIgnore, JsonIgnore] public InertString TitleText { get; } = titleText;
    [MarkoutIgnore, JsonIgnore] public InertString VersionsText { get; } = versionsText;
    [MarkoutIgnore, JsonIgnore] public InertString SummaryText { get; } = summaryText;
    [MarkoutIgnore] public string Title => TitleText.ToString();
    public string Versions => VersionsText.ToString();
    public string Summary => SummaryText.ToString();
    public Callout Status { get; set; }

    [MarkoutSection(Name = "Implementation Diff")]
    public List<ImplementationDiffRow>? Rows { get; set; }
}

[MarkoutSerializable]
public record ImplementationDiffRow(
    [property: MarkoutIgnore, JsonIgnore] InertString MemberText,
    [property: MarkoutIgnore, JsonIgnore] InertString MechanismText,
    [property: MarkoutIgnore, JsonIgnore] InertString DifferenceText,
    [property: MarkoutIgnore, JsonIgnore] InertString ChangeText,
    [property: MarkoutIgnore, JsonIgnore] InertString EvidenceText)
{
    public ImplementationDiffRow(
        string member,
        string mechanism,
        string difference,
        string change,
        string evidence)
        : this(
            DiffViewText.Field(member),
            DiffViewText.Field(mechanism),
            DiffViewText.Field(difference),
            DiffViewText.Field(change),
            DiffViewText.Field(evidence))
    {
    }

    public string Member => MemberText.ToString();
    public string Mechanism => MechanismText.ToString();
    public string Difference => DifferenceText.ToString();
    public string Change => ChangeText.ToString();
    public string Evidence => EvidenceText.ToString();
}

[MarkoutSerializable]
public record DiffChangeRow(
    [property: MarkoutIgnore, JsonIgnore] InertString TypeNameText,
    [property: MarkoutIgnore, JsonIgnore] InertString MessageText)
{
    [MarkoutIgnore] public string TypeName => TypeNameText.ToString();
    public string Message => MessageText.ToString();
}

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
[MarkoutContext(typeof(DiffInspectionFailureRow))]
[MarkoutContext(typeof(AnalysisDiffView))]
[MarkoutContext(typeof(AnalysisDiffRow))]
[MarkoutContext(typeof(ImplementationDiffView))]
[MarkoutContext(typeof(ImplementationDiffRow))]
public partial class DiffViewContext : MarkoutSerializerContext
{
}
