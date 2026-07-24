using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Markout;
using Markout.Formatting;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// Renders a Markout progress card over the committed EVIL authored-corpus trend
/// store (<c>tools/DecompilerHarness/corpus/evil-runs/history.jsonl</c>, one
/// summarized run per line, newest-last). The card shows the last N runs as a
/// trend table plus a latest-vs-previous movement table.
///
/// The headline metric is <c>invalidBreakdown.productBodyDefect</c>, not raw
/// <c>invalid</c>: per #3079/#3096 the raw invalid population is ~92% harness
/// shell-reconstruction noise that does not move on decompiler fixes, so the card
/// surfaces the product sub-count as the signal that actually tracks progress.
/// </summary>
static class AuthoredCorpusHistoryCard
{
    internal const string DefaultHistoryRelativePath =
        "tools/DecompilerHarness/corpus/evil-runs/history.jsonl";

    const int DefaultWindow = 5;

    public static int Run(string? historyPath, int window)
    {
        string path = historyPath ?? DefaultHistoryRelativePath;
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"History file not found: {path}");
            return 1;
        }

        IReadOnlyList<HistoryRun> runs;
        try
        {
            runs = ParseHistory(File.ReadLines(path));
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"History file is not valid JSONL: {ex.Message}");
            return 1;
        }

        if (runs.Count == 0)
        {
            Console.Error.WriteLine($"History is empty or unparseable: {path}");
            return 1;
        }

        Console.Write(Render(runs, window));
        return 0;
    }

    internal static IReadOnlyList<HistoryRun> ParseHistory(IEnumerable<string> lines)
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var runs = new List<HistoryRun>();
        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            if (JsonSerializer.Deserialize<HistoryRun>(line, options) is { } run)
                runs.Add(run);
        }

        return runs;
    }

    internal static string Render(IReadOnlyList<HistoryRun> runs, int window)
    {
        ArgumentNullException.ThrowIfNull(runs);

        int effectiveWindow = window <= 0 ? runs.Count : Math.Min(window, runs.Count);
        var recent = runs.Skip(runs.Count - effectiveWindow).ToArray();

        var view = new HistoryCardView
        {
            Runs = [.. recent.Select(ToRunRow)],
            Movement = BuildMovement(recent),
            WindowNote = $"Showing {recent.Length} of {runs.Count} recorded run(s). "
                + "Track product defects (target-body decompiler bugs), not raw invalid "
                + "(~92% harness shell-reconstruction noise per #3079).",
        };

        var output = new StringWriter();
        MarkoutSerializer.Serialize(
            view,
            output,
            new MarkdownFormatter(),
            AuthoredCorpusHistoryCardContext.Default,
            new MarkoutWriterOptions());
        return output.ToString();
    }

    static HistoryRunRow ToRunRow(HistoryRun run)
        => new(
            run.Date ?? "—",
            run.Commit ?? "(baseline)",
            FormatPct(run.ValidPct),
            run.Correct,
            run.Invalid,
            run.InvalidBreakdown is { } breakdown ? breakdown.ProductBodyDefect.ToString(CultureInfo.InvariantCulture) : "—",
            run.InvalidBreakdown is { } noise ? noise.HarnessShellReconstruction.ToString(CultureInfo.InvariantCulture) : "—");

    static List<HistoryMovementRow>? BuildMovement(IReadOnlyList<HistoryRun> recent)
    {
        if (recent.Count < 2)
            return null;

        HistoryRun previous = recent[^2];
        HistoryRun latest = recent[^1];

        return
        [
            PercentMovement("Valid %", previous.ValidPct, latest.ValidPct, higherIsBetter: true),
            CountMovement("Correct", previous.Correct, latest.Correct, higherIsBetter: true),
            CountMovement("Invalid (raw)", previous.Invalid, latest.Invalid, higherIsBetter: false),
            CountMovement(
                "Product defects",
                previous.InvalidBreakdown?.ProductBodyDefect,
                latest.InvalidBreakdown?.ProductBodyDefect,
                higherIsBetter: false),
        ];
    }

    static HistoryMovementRow PercentMovement(string metric, double previous, double latest, bool higherIsBetter)
    {
        double delta = latest - previous;
        return new HistoryMovementRow(
            metric,
            FormatPct(previous),
            FormatPct(latest),
            SignedPct(delta),
            Trend(delta, higherIsBetter));
    }

    static HistoryMovementRow CountMovement(string metric, int? previous, int? latest, bool higherIsBetter)
    {
        if (previous is not { } prev || latest is not { } cur)
        {
            return new HistoryMovementRow(
                metric,
                previous?.ToString(CultureInfo.InvariantCulture) ?? "—",
                latest?.ToString(CultureInfo.InvariantCulture) ?? "—",
                "—",
                "n/a");
        }

        int delta = cur - prev;
        return new HistoryMovementRow(
            metric,
            prev.ToString(CultureInfo.InvariantCulture),
            cur.ToString(CultureInfo.InvariantCulture),
            SignedCount(delta),
            Trend(delta, higherIsBetter));
    }

    static string Trend(double delta, bool higherIsBetter)
    {
        if (delta == 0)
            return "unchanged";
        bool better = higherIsBetter ? delta > 0 : delta < 0;
        return better ? "improved" : "regressed";
    }

    static string FormatPct(double value) => value.ToString("F1", CultureInfo.InvariantCulture) + "%";

    static string SignedPct(double delta)
        => (delta > 0 ? "+" : delta < 0 ? "−" : "±") + Math.Abs(delta).ToString("F1", CultureInfo.InvariantCulture) + "%";

    static string SignedCount(int delta)
        => (delta > 0 ? "+" : delta < 0 ? "−" : "±") + Math.Abs(delta).ToString(CultureInfo.InvariantCulture);
}

internal sealed record HistoryRunValidDifferent(int Total, int FrontierIlExact, int FrontierIlDiff);

internal sealed record HistoryRunInvalidBreakdown(int ProductBodyDefect, int HarnessShellReconstruction, int Unclassified);

internal sealed record HistoryRun(
    string? Date,
    string? Commit,
    int PoolMatched,
    int PoolTotal,
    int Evaluated,
    double ValidPct,
    int Correct,
    HistoryRunValidDifferent? ValidDifferent,
    int Invalid,
    HistoryRunInvalidBreakdown? InvalidBreakdown,
    int Unsupported,
    int Drift,
    bool Honest,
    [property: JsonPropertyName("sweepManifestSha256")] string? SweepManifestSha256);

[MarkoutSerializable(TitleProperty = nameof(Title), DescriptionProperty = nameof(WindowNote), AutoFields = false)]
internal sealed class HistoryCardView
{
    [MarkoutIgnore]
    public string Title => "EVIL authored-corpus progress";

    [MarkoutIgnore]
    public string? WindowNote { get; init; }

    [MarkoutSection(Name = "Runs")]
    public List<HistoryRunRow>? Runs { get; init; }

    [MarkoutSection(Name = "Movement (latest vs previous)")]
    public List<HistoryMovementRow>? Movement { get; init; }
}

[MarkoutSerializable]
internal sealed record HistoryRunRow(
    string Date,
    string Commit,
    [property: MarkoutPropertyName("Valid %")] string Valid,
    int Correct,
    [property: MarkoutPropertyName("Invalid (raw)")] int Invalid,
    [property: MarkoutPropertyName("Product defects")] string Product,
    [property: MarkoutPropertyName("Harness noise")] string Harness);

[MarkoutSerializable]
internal sealed record HistoryMovementRow(
    string Metric,
    string Previous,
    string Latest,
    [property: MarkoutPropertyName("Δ")] string Change,
    string Trend);

[MarkoutContextOptions(SuppressTableWarnings = true)]
[MarkoutContext(typeof(HistoryCardView))]
[MarkoutContext(typeof(HistoryRunRow))]
[MarkoutContext(typeof(HistoryMovementRow))]
internal sealed partial class AuthoredCorpusHistoryCardContext : MarkoutSerializerContext
{
}
