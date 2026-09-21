using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using ILInspector.CSharp;
using Markout;

namespace DotnetInspect.Cli.Views;

internal static class DiffHistoryViewText
{
    [return: NotNullIfNotNull(nameof(value))]
    internal static string? Contain(string? value) =>
        value is null
            ? null
            : CSharpIdentifier.ContainRenderedText(value);
}

[MarkoutSerializable(
    TitleProperty = nameof(Title),
    FieldLayout = FieldLayout.Table)]
public sealed class DiffHistoryDocumentView
{
    [MarkoutIgnore]
    public string Title
    {
        get => field;
        init => field = DiffHistoryViewText.Contain(value);
    } = "";

    public string Range
    {
        get => field;
        init => field = DiffHistoryViewText.Contain(value);
    } = "";

    public string Type
    {
        get => field;
        init => field = DiffHistoryViewText.Contain(value);
    } = "";

    public string? Member
    {
        get => field;
        init => field = DiffHistoryViewText.Contain(value);
    }

    public string Finding
    {
        get => field;
        init => field = DiffHistoryViewText.Contain(value);
    } = "";

    public string Policy
    {
        get => field;
        init => field = DiffHistoryViewText.Contain(value);
    } = "";

    [MarkoutSection(Name = "Outcome")]
    public List<DiffHistoryOutcomeRowView>? Outcome { get; init; }

    [MarkoutSection(Name = "Probe Trace")]
    public List<DiffHistoryProbeRowView>? ProbeTrace { get; init; }

    [MarkoutSection(Name = "Evaluations")]
    public List<DiffHistoryEvaluationRowView>? Evaluations { get; init; }

    [MarkoutSection(Name = "Transitions")]
    public List<DiffHistoryTransitionRowView>? Transitions { get; init; }

    [MarkoutSection(Name = "Changed Versions")]
    public List<DiffHistoryChangedVersionRowView>? ChangedVersions { get; init; }
}

[MarkoutSerializable]
public sealed class DiffHistoryOutcomeRowView(
    string result,
    string probes,
    string? resolved,
    string? unresolved,
    string? blocked,
    string? nextActions)
{
    public string Result { get; } =
        DiffHistoryViewText.Contain(result) ?? "";
    public string Probes { get; } =
        DiffHistoryViewText.Contain(probes) ?? "";
    public string? Resolved { get; } =
        DiffHistoryViewText.Contain(resolved);
    public string? Unresolved { get; } =
        DiffHistoryViewText.Contain(unresolved);
    public string? Blocked { get; } =
        DiffHistoryViewText.Contain(blocked);

    [MarkoutPropertyName("Next actions")]
    public string? NextActions { get; } =
        DiffHistoryViewText.Contain(nextActions);
}

[MarkoutSerializable]
public sealed class DiffHistoryProbeRowView(
    int step,
    string purpose,
    string address,
    string version,
    string? interval,
    string state,
    string learning)
{
    public int Step { get; } = step;
    public string Purpose { get; } =
        DiffHistoryViewText.Contain(purpose) ?? "";
    public string Address { get; } =
        DiffHistoryViewText.Contain(address) ?? "";
    public string Version { get; } =
        DiffHistoryViewText.Contain(version) ?? "";
    public string? Interval { get; } =
        DiffHistoryViewText.Contain(interval);
    public string State { get; } =
        DiffHistoryViewText.Contain(state) ?? "";
    public string Learning { get; } =
        DiffHistoryViewText.Contain(learning) ?? "";
}

[MarkoutSerializable]
public sealed class DiffHistoryEvaluationRowView(
    string address,
    string version,
    string state,
    int? findings,
    string? detail)
{
    public string Address { get; } =
        DiffHistoryViewText.Contain(address) ?? "";
    public string Version { get; } =
        DiffHistoryViewText.Contain(version) ?? "";
    public string State { get; } =
        DiffHistoryViewText.Contain(state) ?? "";
    public int? Findings { get; } = findings;
    public string? Detail { get; } =
        DiffHistoryViewText.Contain(detail);
}

[MarkoutSerializable]
public sealed class DiffHistoryTransitionRowView(
    string from,
    string to,
    string span,
    string transition,
    string finding,
    string target,
    string? detail)
{
    public string From { get; } =
        DiffHistoryViewText.Contain(from) ?? "";
    public string To { get; } =
        DiffHistoryViewText.Contain(to) ?? "";
    public string Span { get; } =
        DiffHistoryViewText.Contain(span) ?? "";
    public string Transition { get; } =
        DiffHistoryViewText.Contain(transition) ?? "";
    public string Finding { get; } =
        DiffHistoryViewText.Contain(finding) ?? "";
    public string Target { get; } =
        DiffHistoryViewText.Contain(target) ?? "";
    public string? Detail { get; } =
        DiffHistoryViewText.Contain(detail);
}

[MarkoutSerializable]
public sealed class DiffHistoryChangedVersionRowView(
    string address,
    string version,
    string predecessor,
    string state,
    string? detail)
{
    public string Address { get; } =
        DiffHistoryViewText.Contain(address) ?? "";
    public string Version { get; } =
        DiffHistoryViewText.Contain(version) ?? "";
    public string Predecessor { get; } =
        DiffHistoryViewText.Contain(predecessor) ?? "";
    public string State { get; } =
        DiffHistoryViewText.Contain(state) ?? "";
    public string? Detail { get; } =
        DiffHistoryViewText.Contain(detail);
}

[MarkoutSerializable(FieldLayout = FieldLayout.Table)]
public sealed class DiffHistoryOutcomeView
{
    [MarkoutSection(Name = "Outcome")]
    public List<DiffHistoryOutcomeRowView>? Rows { get; init; }
}

[MarkoutSerializable(FieldLayout = FieldLayout.Table)]
public sealed class DiffHistoryProbeTraceView
{
    [MarkoutSection(Name = "Probe Trace")]
    public List<DiffHistoryProbeRowView>? Rows { get; init; }
}

[MarkoutSerializable(FieldLayout = FieldLayout.Table)]
public sealed class DiffHistoryEvaluationsView
{
    [MarkoutSection(Name = "Evaluations")]
    public List<DiffHistoryEvaluationRowView>? Rows { get; init; }
}

[MarkoutSerializable(FieldLayout = FieldLayout.Table)]
public sealed class DiffHistoryTransitionsView
{
    [MarkoutSection(Name = "Transitions")]
    public List<DiffHistoryTransitionRowView>? Rows { get; init; }
}

[MarkoutSerializable(FieldLayout = FieldLayout.Table)]
public sealed class DiffHistoryChangedVersionsView
{
    [MarkoutSection(Name = "Changed Versions")]
    public List<DiffHistoryChangedVersionRowView>? Rows { get; init; }
}

[MarkoutContextOptions(SuppressTableWarnings = true)]
[MarkoutContext(typeof(DiffHistoryDocumentView))]
[MarkoutContext(typeof(DiffHistoryOutcomeRowView))]
[MarkoutContext(typeof(DiffHistoryProbeRowView))]
[MarkoutContext(typeof(DiffHistoryEvaluationRowView))]
[MarkoutContext(typeof(DiffHistoryTransitionRowView))]
[MarkoutContext(typeof(DiffHistoryChangedVersionRowView))]
[MarkoutContext(typeof(DiffHistoryOutcomeView))]
[MarkoutContext(typeof(DiffHistoryProbeTraceView))]
[MarkoutContext(typeof(DiffHistoryEvaluationsView))]
[MarkoutContext(typeof(DiffHistoryTransitionsView))]
[MarkoutContext(typeof(DiffHistoryChangedVersionsView))]
public partial class DiffHistoryViewContext : MarkoutSerializerContext;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(DiffHistoryDocumentView))]
public sealed partial class DiffHistoryViewJsonContext : JsonSerializerContext;
