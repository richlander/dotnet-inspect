using DotnetInspector.HarnessReportDiff;
using DotnetInspector.HarnessReports;
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
    var output = new StringWriter();
    if (format == "markdown")
    {
        MarkoutSerializer.Serialize(
            ComparisonView.Create(comparison),
            output,
            new MarkdownFormatter(),
            ComparisonViewContext.Default,
            new MarkoutWriterOptions());
    }
    else
    {
        var options = format switch
        {
            "tsv" => new MarkoutWriterOptions { TableMode = MarkoutTableMode.Tsv },
            "jsonl" => new MarkoutWriterOptions { TableMode = MarkoutTableMode.Jsonl, OmitEmptyJsonFields = true },
            _ => throw new InvalidOperationException($"Unsupported format '{format}'."),
        };
        MarkoutSerializer.Serialize(
            ComparisonTableView.Create(comparison),
            output,
            new TableFormatter(showHeader: true),
            ComparisonViewContext.Default,
            options);
    }
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
catch (Exception ex) when (ex is IOException
    or JsonException
    or InvalidOperationException
    or UnauthorizedAccessException
    or ArgumentException)
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
        FullyRaised = comparison.FullyRaised is { } fullyRaised
            ? [new(fullyRaised.Before, fullyRaised.After, fullyRaised.Basis, fullyRaised.Verdict.ToString())]
            : null,
        Metrics = [.. comparison.Metrics.Select(MetricRow.Create)],
        Warnings = comparison.Warnings.Count == 0 ? null : [.. comparison.Warnings.Select(warning => new WarningRow(warning))],
    };
}

[MarkoutSerializable]
sealed record ReportRow(string Side, string Kind, string Description);

[MarkoutSerializable]
sealed record FullyRaisedRow(string Before, string After, string Basis, string Verdict);

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

[MarkoutSerializable(TitleProperty = nameof(Title), AutoFields = false)]
sealed class ComparisonTableView
{
    [MarkoutIgnore]
    public string Title => "Harness report diff";

    [MarkoutSection(Name = "Rows")]
    public required List<ComparisonTableRow> Rows { get; init; }

    public static ComparisonTableView Create(HarnessComparison comparison)
    {
        var rows = new List<ComparisonTableRow>
        {
            new("Report", "Before", "", "", "", "", $"{comparison.Before.Kind}: {comparison.Before.Description}"),
            new("Report", "After", "", "", "", "", $"{comparison.After.Kind}: {comparison.After.Description}"),
        };
        if (comparison.FullyRaised is { } endpoint)
        {
            rows.Add(new(
                "Endpoint",
                "Fully raised",
                endpoint.Before,
                endpoint.After,
                "",
                endpoint.Verdict.ToString(),
                endpoint.Basis));
        }
        rows.AddRange(comparison.Metrics.Select(metric =>
        {
            string goal = metric.Goal switch
            {
                MetricGoal.Higher => "+",
                MetricGoal.Lower => "−",
                MetricGoal.Hold => "=",
                _ => "context",
            };
            return new ComparisonTableRow(
                "Metric",
                $"{metric.Label} ({goal})",
                metric.Before.Display,
                metric.After.Display,
                metric.Delta,
                metric.Verdict.ToString(),
                "");
        }));
        rows.AddRange(comparison.Warnings.Select(warning =>
            new ComparisonTableRow("Warning", "Warning", "", "", "", "", warning)));
        return new ComparisonTableView { Rows = rows };
    }
}

[MarkoutSerializable]
sealed record ComparisonTableRow(
    string Section,
    string Item,
    string Before,
    string After,
    string Delta,
    string Verdict,
    string Detail);

[MarkoutContextOptions(SuppressTableWarnings = true)]
[MarkoutContext(typeof(ComparisonView))]
[MarkoutContext(typeof(ReportRow))]
[MarkoutContext(typeof(FullyRaisedRow))]
[MarkoutContext(typeof(MetricRow))]
[MarkoutContext(typeof(WarningRow))]
[MarkoutContext(typeof(ComparisonTableView))]
[MarkoutContext(typeof(ComparisonTableRow))]
partial class ComparisonViewContext : MarkoutSerializerContext
{
}
