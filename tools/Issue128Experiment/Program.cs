using Markout;

// Experiment (issue #128): consume the local Markout 0.17-dev via projectref and prove the new
// declarative MetricChange<T> card renders as Metric | Change | Target | Status in Markdown and
// decomposes to flat typed fields in JSONL — replacing the IL Diff harness's pre-stringified
// BaselineFindingView(Kind, Metric, Baseline, Current, Detail).

var card = new BaselineCard
{
    Metrics =
    [
        new("Failures", 0, 7, 0, "allowed failures", GateStatus.Bad, "regression"),
        new("Self-diff empty", 78, 77, 0, "no drops allowed", GateStatus.Bad, "regression"),
        new("Changed bodies", 45, 46, Status: GateStatus.Warning, StatusLabel: "drift"),
        new("Compared bodies", 78, 78, Status: GateStatus.Neutral, StatusLabel: "unchanged"),
    ],
};

Console.WriteLine("# Markdown\n");
Console.WriteLine(MarkoutSerializer.Serialize(card, BaselineCardContext.Default));

Console.WriteLine("\n# JSONL (typed)\n");
MarkoutSerializer.Serialize(card, Console.Out, new TableFormatter(), BaselineCardContext.Default,
    new MarkoutWriterOptions { TableMode = MarkoutTableMode.Jsonl, JsonTypedValues = true, OmitEmptyJsonFields = true });

[MarkoutSerializable(TitleProperty = nameof(Title))]
public sealed class BaselineCard
{
    [MarkoutIgnore] public string Title => "IL Diff baseline";

    [MarkoutSection(Name = "Baseline comparison", EmptyText = "No baseline regressions or drift.")]
    public List<MetricChange<int>> Metrics { get; init; } = new();
}

[MarkoutContext(typeof(BaselineCard))]
public partial class BaselineCardContext : MarkoutSerializerContext { }
