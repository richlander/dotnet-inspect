using DotnetInspector.HarnessReportDiff;
using DotnetInspector.HarnessReports;
using Markout;
using Markout.Formatting;
using System.Globalization;
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
    Console.Write(ComparisonRenderer.Render(comparison, format));
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

/// <summary>
/// Renders a <see cref="HarnessComparison"/> to text in the requested format. The Markdown card flows
/// through Markout's native change rendering (arrow, polarity glyph, goal label); the tsv/jsonl paths
/// emit one flat, string-valued row shape with a <c>section</c> discriminator.
/// </summary>
public static class ComparisonRenderer
{
    public static string Render(HarnessComparison comparison, string format)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        var output = new StringWriter();
        if (format == "markdown")
        {
            MarkoutSerializer.Serialize(
                ComparisonView.Create(comparison),
                output,
                new MarkdownFormatter(),
                ComparisonViewContext.Default,
                new MarkoutWriterOptions());
            return output.ToString();
        }

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
        string rendered = output.ToString();
        if (format == "jsonl")
        {
            rendered = string.Join(
                Environment.NewLine,
                rendered.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries))
                + Environment.NewLine;
        }
        return rendered;
    }
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
    [MarkoutLabelHeader("Metric")]
    public List<MultiSourceRow>? Metrics { get; init; }

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
        Metrics = comparison.Metrics.Count == 0 ? null : [.. comparison.Metrics.Select(MetricMovementRow)],
        Warnings = comparison.Warnings.Count == 0 ? null : [.. comparison.Warnings.Select(warning => new WarningRow(warning))],
    };

    // Render each metric as a Markout change row: the arrow, the ✓/✗ polarity glyph, and the goal
    // (↑/↓) label glyph are all derived natively from the two MetricValue cells plus the mapped Goal —
    // no hand-built change string, goal symbol, or verdict word. The typed MetricVerdict still drives
    // the exit-code gate (HarnessComparison.HasRegressions); it is not inferred from this display.
    static MultiSourceRow MetricMovementRow(MetricComparison metric)
    {
        // An incomparable population must not imply a good/bad change: neutralize the value-cell goal to
        // Context so no ✓/✗ is derived, while the label still shows the metric's inherent direction.
        Goal cellGoal = metric.Verdict == MetricVerdict.Incomparable
            ? Goal.Context
            : MapGoal(metric.Goal);
        return new MultiSourceRow(
            metric.Label,
            new Source(
                "Change",
                new Change<MetricValueCell>(new MetricValueCell(metric.Before), new MetricValueCell(metric.After)),
                new MarkoutCellFormat { Goal = cellGoal }),
            new Source("Delta", new MetricText(metric.Delta)))
        {
            Goal = MapGoal(metric.Goal),
        };
    }

    // Hold and Context carry no display polarity (their any-change semantics live in the verdict/gate,
    // not the glyph), so both map to Markout's neutral Context.
    static Goal MapGoal(MetricGoal goal) => goal switch
    {
        MetricGoal.Higher => Goal.Higher,
        MetricGoal.Lower => Goal.Lower,
        _ => Goal.Context,
    };
}

[MarkoutSerializable]
sealed record ReportRow(string Side, string Kind, string Description);

[MarkoutSerializable]
sealed record FullyRaisedRow(string Before, string After, string Basis, string Verdict);

// A MetricValue rendered as a Markout cell: mirrors MetricValue.Display ("count" or "count (pct%)") and
// exposes the goal magnitude (rate when a total is present, else the raw count) so Change<T> can derive
// the polarity glyph. Mirrors the QualityRate pattern in CorpusSensor.cs.
readonly record struct MetricValueCell(long Count, long? Total) : IMarkoutCell, IGoalMagnitude
{
    public MetricValueCell(MetricValue value)
        : this(value.Count, value.Total)
    {
    }

    double IGoalMagnitude.GoalMagnitude => Total is > 0 ? (double)Count / Total.Value : Count;

    public void FormatInline(TextWriter writer, in MarkoutCellFormat format)
        => writer.Write(Total is > 0
            ? $"{Count.ToString("N0", CultureInfo.InvariantCulture)} ({100.0 * Count / Total.Value:0.00}%)"
            : Count.ToString("N0", CultureInfo.InvariantCulture));

    public void Decompose(ICollection<MarkoutField> fields, string? side, in MarkoutCellFormat format)
    {
        fields.Add(new MarkoutField(SideKey(side, "count"), Count.ToString(CultureInfo.InvariantCulture)));
        if (Total is > 0)
            fields.Add(new MarkoutField(SideKey(side, "total"), Total.Value.ToString(CultureInfo.InvariantCulture)));
    }

    static string SideKey(string? side, string key) => side is null ? key : side + "_" + key;
}

// A precomputed delta string (count, optionally with a percentage-point rate) rendered verbatim. The
// rate delta is information Markout's numeric delta cannot express, so it is carried here.
readonly record struct MetricText(string Text) : IMarkoutCell
{
    public void FormatInline(TextWriter writer, in MarkoutCellFormat format) => writer.Write(Text);

    public void Decompose(ICollection<MarkoutField> fields, string? side, in MarkoutCellFormat format)
        => fields.Add(new MarkoutField(side ?? "value", Text));
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
            new("Report", "Before", "", "", "", "", "", $"{comparison.Before.Kind}: {comparison.Before.Description}"),
            new("Report", "After", "", "", "", "", "", $"{comparison.After.Kind}: {comparison.After.Description}"),
        };
        if (comparison.FullyRaised is { } endpoint)
        {
            rows.Add(new(
                "Endpoint",
                "Fully raised",
                "",
                endpoint.Before,
                endpoint.After,
                "",
                endpoint.Verdict.ToString(),
                endpoint.Basis));
        }
        rows.AddRange(comparison.Metrics.Select(metric => new ComparisonTableRow(
            "Metric",
            metric.Label,
            metric.Goal.ToString(),
            metric.Before.Display,
            metric.After.Display,
            metric.Delta,
            metric.Verdict.ToString(),
            "")));
        rows.AddRange(comparison.Warnings.Select(warning =>
            new ComparisonTableRow("Warning", "Warning", "", "", "", "", "", warning)));
        return new ComparisonTableView { Rows = rows };
    }
}

[MarkoutSerializable]
sealed record ComparisonTableRow(
    string Section,
    string Item,
    string Goal,
    string Before,
    string After,
    string Delta,
    string Verdict,
    string Detail);

[MarkoutContextOptions(SuppressTableWarnings = true)]
[MarkoutContext(typeof(ComparisonView))]
[MarkoutContext(typeof(ReportRow))]
[MarkoutContext(typeof(FullyRaisedRow))]
[MarkoutContext(typeof(WarningRow))]
[MarkoutContext(typeof(ComparisonTableView))]
[MarkoutContext(typeof(ComparisonTableRow))]
partial class ComparisonViewContext : MarkoutSerializerContext
{
}
