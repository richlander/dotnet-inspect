using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Markout;
using Markout.Formatting;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// Renders a Markout progress card over the committed EVIL authored-corpus trend
/// store (<c>tools/DecompilerHarness/corpus/evil-runs/history.jsonl</c>, one
/// summarized run per line, newest-last). The card shows every recorded run as a
/// trend table plus a movement table that pivots the most recent runs onto
/// per-metric rows with goal (↑/↓) and per-step (✓/✗) glyphs.
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
            var run = JsonSerializer.Deserialize<HistoryRun>(line, options)
                ?? throw new JsonException($"History row is null (not a run object): {line.Trim()}");
            runs.Add(run);
        }

        return runs;
    }

    internal static string Render(IReadOnlyList<HistoryRun> runs, int window)
    {
        ArgumentNullException.ThrowIfNull(runs);

        int movementCount = window <= 0 ? runs.Count : Math.Min(window, runs.Count);
        var movementWindow = runs.Skip(runs.Count - movementCount).ToArray();
        var movement = BuildMovement(movementWindow);

        const string productSignalNote =
            "Track product defects (target-body decompiler bugs), not raw invalid "
            + "(~92% harness shell-reconstruction noise per #3079).";
        string note;
        if (movement is not null)
        {
            note = $"Runs lists every recorded run; the last {movementWindow.Length} are pivoted per metric "
                + "below with goal (\u2191/\u2193) and per-step (\u2713/\u2717) glyphs. " + productSignalNote;
        }
        else if (runs.Count < 2)
        {
            note = "Only one recorded run so far; a trend needs at least two. " + productSignalNote;
        }
        else
        {
            note = "Movement window is 1 run; a trend needs at least two. " + productSignalNote;
        }

        var view = new HistoryCardView
        {
            Runs = [.. runs.Select(ToRunRow)],
            Movement = movement,
            WindowNote = note,
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
            run.InvalidBreakdown is { } noise ? noise.HarnessShellReconstruction.ToString(CultureInfo.InvariantCulture) : "—",
            $"v{run.Methodology.ToString(CultureInfo.InvariantCulture)}");

    // Movement pivots the trend metrics: each metric is a row and each recent run a column, so a
    // MultiSourceRow carries the row's Goal and lets Markout derive the goal glyph (↑/↓) on the label
    // and a per-step polarity glyph (✓/✗) on each column vs the previous populated one — no hand-computed
    // delta or trend word. It is the transpose (pivot) of the Runs table, bounded to the recent window.
    static List<MultiSourceRow>? BuildMovement(IReadOnlyList<HistoryRun> window)
    {
        if (window.Count < 2)
            return null;

        string[] cols = ColumnKeys(window);
        var rows = new List<MultiSourceRow>
        {
            ScalarRow("Valid %", Goal.Higher, window, cols, r => r.ValidPct),
            ScalarRow("Correct", Goal.Higher, window, cols, r => r.Correct),
            ScalarRow("Invalid (raw)", Goal.Lower, window, cols, r => r.Invalid),
        };
        rows.AddRange(ProductDefectRows(window, cols));
        return rows;
    }

    static MultiSourceRow ScalarRow(
        string label, Goal goal, IReadOnlyList<HistoryRun> window, string[] cols, Func<HistoryRun, double> value)
    {
        var sources = new Source[window.Count];
        for (int i = 0; i < window.Count; i++)
            sources[i] = new Source(cols[i], value(window[i]));
        return new MultiSourceRow(label, sources) { Goal = goal };
    }

    // Runs predating #3096 carry no invalid breakdown; render those columns as an absent cell so the
    // product-defect signal stays honest (no fabricated zero) and Markout's pairwise chain skips them
    // rather than charting a bogus step.
    //
    // productBodyDefect is also computed differently across methodology versions (v1 = substitution
    // control only; v2 = substitution control plus span attribution). Both are lower bounds, but a
    // tighter rule counts strictly more rows, so the two are not comparable. When the window straddles
    // a version boundary the metric is split into one row per version — each populated only for its own
    // columns — so Markout never charts a step across the boundary. When every populated run shares a
    // version, a single "Product defects" row is emitted (unchanged output for uniform history).
    static IEnumerable<MultiSourceRow> ProductDefectRows(IReadOnlyList<HistoryRun> window, string[] cols)
    {
        bool hasV1 = window.Any(run => run.InvalidBreakdown is not null && run.Methodology <= 1);
        bool hasV2 = window.Any(run => run.InvalidBreakdown is not null && run.Methodology >= 2);
        if (hasV1 && hasV2)
        {
            yield return ProductDefectRow("Product defects (v1 substitution lower bound)", window, cols, version: 1);
            yield return ProductDefectRow("Product defects (v2 span-measured lower bound)", window, cols, version: 2);
        }
        else
        {
            yield return ProductDefectRow("Product defects", window, cols, version: null);
        }
    }

    static MultiSourceRow ProductDefectRow(string label, IReadOnlyList<HistoryRun> window, string[] cols, int? version)
    {
        var sources = new Source[window.Count];
        for (int i = 0; i < window.Count; i++)
        {
            bool inVersion = version is null
                || (version == 1 ? window[i].Methodology <= 1 : window[i].Methodology >= 2);
            sources[i] = inVersion && window[i].InvalidBreakdown is { } breakdown
                ? new Source(cols[i], breakdown.ProductBodyDefect)
                : new Source(cols[i], (IMarkoutCell?)null);
        }

        return new MultiSourceRow(label, sources) { Goal = Goal.Lower };
    }

    // Column keys are the run dates (the pivoted table's headers). Disambiguate a repeated date with its
    // commit so each run stays a distinct column even when two runs share a day.
    static string[] ColumnKeys(IReadOnlyList<HistoryRun> window)
    {
        var keys = new string[window.Count];
        var seen = new Dictionary<string, int>();
        for (int i = 0; i < window.Count; i++)
        {
            string key = window[i].Date ?? window[i].Commit ?? $"run{i + 1}";
            if (seen.TryGetValue(key, out int count))
            {
                seen[key] = count + 1;
                key = $"{key} #{window[i].Commit ?? (count + 1).ToString(CultureInfo.InvariantCulture)}";
            }
            else
            {
                seen[key] = 1;
            }

            keys[i] = key;
        }

        return keys;
    }

    static string FormatPct(double value) => value.ToString("F1", CultureInfo.InvariantCulture) + "%";
}

/// <summary>
/// The valid-different partition for one run. <see cref="Total"/> must equal the sum
/// of the five sub-buckets; the three added after the store's first rows are nullable
/// so that a row predating them reads as <em>not recorded</em> rather than as zero.
/// <see cref="AuthoredCorpusHistoryCardTests"/> enforces both the sum and the rule
/// that only grandfathered rows may omit them.
/// </summary>
internal sealed record HistoryRunValidDifferent(
    int Total,
    int FrontierIlExact,
    int FrontierIlDiff,
    int? Lowering = null,
    int? KnownTaste = null,
    int? FrontierIlNoVerdict = null)
{
    /// <summary>True when every sub-bucket was recorded, so the partition is checkable.</summary>
    public bool IsComplete => Lowering is not null && KnownTaste is not null && FrontierIlNoVerdict is not null;

    /// <summary>Sum of the recorded sub-buckets, or null when the partition is incomplete.</summary>
    public int? SubBucketSum => IsComplete
        ? Lowering!.Value + KnownTaste!.Value + FrontierIlExact + FrontierIlDiff + FrontierIlNoVerdict!.Value
        : null;
}

internal sealed record HistoryRunInvalidBreakdown(
    [property: JsonRequired] int ProductBodyDefect,
    [property: JsonRequired] int HarnessShellReconstruction,
    [property: JsonRequired] int Unclassified);

internal sealed record HistoryRun(
    [property: JsonRequired] string? Date,
    string? Commit,
    int PoolMatched,
    int PoolTotal,
    int Evaluated,
    [property: JsonRequired] double ValidPct,
    [property: JsonRequired] int Correct,
    HistoryRunValidDifferent? ValidDifferent,
    [property: JsonRequired] int Invalid,
    HistoryRunInvalidBreakdown? InvalidBreakdown,
    int Unsupported,
    int Drift,
    [property: JsonPropertyName("inputsComplete")] bool InputsComplete,
    [property: JsonPropertyName("sweepManifestSha256")] string? SweepManifestSha256,
    int? MethodologyVersion = null,
    int? NotFull = null,
    int? UnknownOutcome = null)
{
    // Rows predating the span-attribution change carry no methodologyVersion;
    // treat them as v1 (substitution lower bound).
    public int Methodology => MethodologyVersion ?? 1;

    /// <summary>
    /// True when every top-level bucket was recorded, so
    /// <see cref="TopLevelSum"/> can be compared against <see cref="Evaluated"/>.
    /// </summary>
    public bool TopLevelIsComplete => ValidDifferent is not null && NotFull is not null && UnknownOutcome is not null;

    /// <summary>Sum of the recorded top-level buckets, or null when any is unrecorded.</summary>
    public int? TopLevelSum => TopLevelIsComplete
        ? Correct + ValidDifferent!.Total + Invalid + NotFull!.Value + Drift + Unsupported + UnknownOutcome!.Value
        : null;
}

[MarkoutSerializable(TitleProperty = nameof(Title), DescriptionProperty = nameof(WindowNote), AutoFields = false)]
internal sealed class HistoryCardView
{
    [MarkoutIgnore]
    public string Title => "EVIL authored-corpus progress";

    [MarkoutIgnore]
    public string? WindowNote { get; init; }

    [MarkoutSection(Name = "Runs")]
    public List<HistoryRunRow>? Runs { get; init; }

    [MarkoutSection(Name = "Movement")]
    [MarkoutLabelHeader("Metric")]
    public List<MultiSourceRow>? Movement { get; init; }
}

[MarkoutSerializable]
internal sealed record HistoryRunRow(
    string Date,
    string Commit,
    [property: MarkoutPropertyName("Valid %")] string Valid,
    int Correct,
    [property: MarkoutPropertyName("Invalid (raw)")] int Invalid,
    [property: MarkoutPropertyName("Product defects")] string Product,
    [property: MarkoutPropertyName("Harness noise")] string Harness,
    [property: MarkoutPropertyName("Method")] string Method);

[MarkoutContextOptions(SuppressTableWarnings = true)]
[MarkoutContext(typeof(HistoryCardView))]
[MarkoutContext(typeof(HistoryRunRow))]
internal sealed partial class AuthoredCorpusHistoryCardContext : MarkoutSerializerContext
{
}
