using Markout;

namespace DotnetInspector.Views;

[MarkoutSerializable(
    TitleProperty = nameof(Title),
    FieldLayout = FieldLayout.Table)]
public sealed class TimelineDocumentView
{
    [MarkoutIgnore] public string Title { get; init; } = "";
    public string Range { get; init; } = "";
    public string Type { get; init; } = "";
    public string? Member { get; init; }
    public string Finding { get; init; } = "";
    public string? Recommendation { get; init; }

    [MarkoutSection(Name = "Evaluations")]
    public List<TimelineEvaluationRow>? Evaluations { get; init; }

    [MarkoutSection(Name = "Transitions")]
    public List<TimelineTransitionRow>? Transitions { get; init; }
}

[MarkoutSerializable]
public sealed record TimelineEvaluationRow(
    string Address,
    string Version,
    string State,
    int? Findings,
    string? Detail);

[MarkoutSerializable]
public sealed record TimelineTransitionRow(
    string From,
    string To,
    string Span,
    string Transition,
    string Finding,
    string Target,
    string? Detail);

[MarkoutSerializable(FieldLayout = FieldLayout.Table)]
public sealed class TimelineEvaluationsView
{
    [MarkoutSection(Name = "Evaluations")]
    public List<TimelineEvaluationRow>? Rows { get; init; }
}

[MarkoutSerializable(FieldLayout = FieldLayout.Table)]
public sealed class TimelineTransitionsView
{
    [MarkoutSection(Name = "Transitions")]
    public List<TimelineTransitionRow>? Rows { get; init; }
}

[MarkoutContextOptions(SuppressTableWarnings = true)]
[MarkoutContext(typeof(TimelineDocumentView))]
[MarkoutContext(typeof(TimelineEvaluationRow))]
[MarkoutContext(typeof(TimelineTransitionRow))]
[MarkoutContext(typeof(TimelineEvaluationsView))]
[MarkoutContext(typeof(TimelineTransitionsView))]
public partial class TimelineViewContext : MarkoutSerializerContext
{
}
