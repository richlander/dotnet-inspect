using DotnetInspector.HarnessReportDiff;
using Markout;
using Markout.Formatting;
using System.Text.Json;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: harness-report-diff <before.json> <after.json> [--format markdown|tsv|jsonl] [--fail-on-regression]");
    return 2;
}

string beforePath = args[0];
string afterPath = args[1];
string format = "markdown";
bool failOnRegression = false;
for (int i = 2; i < args.Length; i++)
{
    if (args[i] == "--fail-on-regression")
    {
        failOnRegression = true;
        continue;
    }
    if (args[i] == "--format" && i + 1 < args.Length)
    {
        format = args[++i];
        continue;
    }
    Console.Error.WriteLine($"Unknown option: {args[i]}");
    return 2;
}

try
{
    var comparison = HarnessReportComparer.Compare(HarnessReportReader.Read(beforePath), HarnessReportReader.Read(afterPath));
    var view = ComparisonView.Create(comparison);
    var formatter = format == "markdown" ? (IMarkoutFormatter)new MarkdownFormatter() : new TableFormatter(showHeader: true);
    var options = format switch
    {
        "markdown" => new MarkoutWriterOptions(),
        "tsv" => new MarkoutWriterOptions { TableMode = MarkoutTableMode.Tsv, JsonTypedValues = true, OmitEmptyJsonFields = true },
        "jsonl" => new MarkoutWriterOptions { TableMode = MarkoutTableMode.Jsonl, JsonTypedValues = true, OmitEmptyJsonFields = true },
        _ => throw new InvalidOperationException($"Unsupported format '{format}'."),
    };
    var output = new StringWriter();
    MarkoutSerializer.Serialize(view, output, formatter, ComparisonViewContext.Default, options);
    string rendered = output.ToString();
    if (format == "jsonl")
    {
        rendered = string.Join(
            Environment.NewLine,
            rendered.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries))
            + Environment.NewLine;
    }
    Console.Write(rendered);
    return failOnRegression && comparison.HasRegressions ? 1 : 0;
}
catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or UnauthorizedAccessException)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

[MarkoutSerializable(TitleProperty = nameof(Title), AutoFields = false)]
sealed class ComparisonView
{
    [MarkoutIgnore]
    public string Title => "Harness report diff";

    [MarkoutSection(Name = "Reports")]
    public List<ReportRow>? Reports { get; init; }

    [MarkoutSection(Name = "Fully raised")]
    public List<FullyRaisedRow>? FullyRaised { get; init; }

    [MarkoutSection(Name = "Metrics")]
    public List<MetricRow>? Metrics { get; init; }

    [MarkoutSection(Name = "Warnings", EmptyText = "None")]
    public List<WarningRow>? Warnings { get; init; }

    public static ComparisonView Create(HarnessComparison comparison) => new()
    {
        Reports =
        [
            new("Before", comparison.Before.Kind, comparison.Before.Description),
            new("After", comparison.After.Kind, comparison.After.Description),
        ],
        FullyRaised = [new(comparison.FullyRaised.Before, comparison.FullyRaised.After, comparison.FullyRaised.Basis)],
        Metrics = [.. comparison.Metrics.Select(MetricRow.Create)],
        Warnings = comparison.Warnings.Count == 0 ? null : [.. comparison.Warnings.Select(warning => new WarningRow(warning))],
    };
}

[MarkoutSerializable]
sealed record ReportRow(string Side, string Kind, string Description);

[MarkoutSerializable]
sealed record FullyRaisedRow(string Before, string After, string Basis);

[MarkoutSerializable]
sealed record MetricRow(
    [property: MarkoutPropertyName("Metric (goal)")] string Metric,
    string Change,
    string Delta,
    string Verdict)
{
    public static MetricRow Create(MetricComparison metric)
    {
        string goal = metric.Goal switch
        {
            MetricGoal.Higher => "+",
            MetricGoal.Lower => "−",
            MetricGoal.Hold => "=",
            _ => "context",
        };
        return new($"{metric.Label} ({goal})", $"{metric.Before.Display} → {metric.After.Display}", metric.Delta, metric.Verdict.ToString());
    }
}

[MarkoutSerializable]
sealed record WarningRow(string Warning);

[MarkoutContextOptions(SuppressTableWarnings = true)]
[MarkoutContext(typeof(ComparisonView))]
[MarkoutContext(typeof(ReportRow))]
[MarkoutContext(typeof(FullyRaisedRow))]
[MarkoutContext(typeof(MetricRow))]
[MarkoutContext(typeof(WarningRow))]
partial class ComparisonViewContext : MarkoutSerializerContext
{
}
