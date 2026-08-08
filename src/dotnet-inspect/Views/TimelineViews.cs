using System.Diagnostics.CodeAnalysis;
using ILInspector.CSharp;
using Markout;

namespace DotnetInspector.Views;

/// <summary>
/// Containment for text rendered by the timeline views.
/// </summary>
/// <remarks>
/// Timeline rows carry type and member spellings and finding detail drawn from
/// the inspected packages, which are untrusted input. Text carrying a line
/// terminator, ANSI escape, or bidi override breaks out of its Markdown table
/// cell and injects text that reads as genuine tool output (issue #3319). These
/// records are presentation-only, never identity; containment is a no-op on
/// clean text.
/// </remarks>
internal static class TimelineViewText
{
    [return: NotNullIfNotNull(nameof(value))]
    public static string? Contain(string? value)
        => value is null ? null : CSharpIdentifier.ContainRenderedText(value);
}

[MarkoutSerializable(
    TitleProperty = nameof(Title),
    FieldLayout = FieldLayout.Table)]
public sealed class TimelineDocumentView
{
    [MarkoutIgnore] public string Title { get => field; init => field = TimelineViewText.Contain(value); } = "";

    /// <inheritdoc cref="TimelineViewText"/>
    public string Range { get => field; init => field = TimelineViewText.Contain(value); } = "";

    /// <inheritdoc cref="TimelineViewText"/>
    public string Type { get => field; init => field = TimelineViewText.Contain(value); } = "";

    /// <inheritdoc cref="TimelineViewText"/>
    public string? Member { get => field; init => field = TimelineViewText.Contain(value); }

    /// <inheritdoc cref="TimelineViewText"/>
    public string Finding { get => field; init => field = TimelineViewText.Contain(value); } = "";

    /// <inheritdoc cref="TimelineViewText"/>
    public string? Recommendation { get => field; init => field = TimelineViewText.Contain(value); }

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
    string? Detail)
{
    /// <inheritdoc cref="TimelineViewText"/>
    public string Address { get; init; } = TimelineViewText.Contain(Address);

    /// <inheritdoc cref="TimelineViewText"/>
    public string Version { get; init; } = TimelineViewText.Contain(Version);

    /// <inheritdoc cref="TimelineViewText"/>
    public string State { get; init; } = TimelineViewText.Contain(State);

    public int? Findings { get; init; } = Findings;

    /// <inheritdoc cref="TimelineViewText"/>
    public string? Detail { get; init; } = TimelineViewText.Contain(Detail);
}

[MarkoutSerializable]
public sealed record TimelineTransitionRow(
    string From,
    string To,
    string Span,
    string Transition,
    string Finding,
    string Target,
    string? Detail)
{
    /// <inheritdoc cref="TimelineViewText"/>
    public string From { get; init; } = TimelineViewText.Contain(From);

    /// <inheritdoc cref="TimelineViewText"/>
    public string To { get; init; } = TimelineViewText.Contain(To);

    /// <inheritdoc cref="TimelineViewText"/>
    public string Span { get; init; } = TimelineViewText.Contain(Span);

    /// <inheritdoc cref="TimelineViewText"/>
    public string Transition { get; init; } = TimelineViewText.Contain(Transition);

    /// <inheritdoc cref="TimelineViewText"/>
    public string Finding { get; init; } = TimelineViewText.Contain(Finding);

    /// <inheritdoc cref="TimelineViewText"/>
    public string Target { get; init; } = TimelineViewText.Contain(Target);

    /// <inheritdoc cref="TimelineViewText"/>
    public string? Detail { get; init; } = TimelineViewText.Contain(Detail);
}

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
