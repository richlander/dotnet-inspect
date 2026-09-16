using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Markout;
using Markout.Formatting;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// Renders a Markout progress card over the committed EVIL authored-corpus trend
/// store (<c>tools/DecompilerHarness/corpus/evil-runs/history.jsonl</c>, one
/// summarized run per line in append order). The card shows every recorded run as a
/// trend table plus a movement table that pivots the most recent runs onto
/// per-metric rows with goal (↑/↓) and per-step (✓/✗) glyphs.
///
/// The headline metrics are the product-body-defect counts within invalid rows and
/// the valid-different IL frontier, not their raw populations: per #3079/#3096 the
/// raw buckets are dominated by harness shell-reconstruction noise, so the card
/// surfaces the attributed product sub-counts that track decompiler progress.
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

        // A malformed or internally inconsistent run identity is a schema error, not
        // a measurement defect, so it is refused at the boundary where the file is
        // read rather than left for a consumer to walk past.
        if (AuthoredCorpusRatchet.RefuseMalformedIdentities(runs) is { } malformed)
            throw new JsonException($"History row records a malformed run identity: {malformed}");
        if (AuthoredCorpusRatchet.RefuseUnknownMethodologies(runs) is { } unknown)
            throw new JsonException($"History row records an unknown methodology: {unknown}");
        if (AuthoredCorpusRatchet.RefuseFrontierAttributionMethodologyMismatch(runs) is { } mismatch)
            throw new JsonException($"History row does not match its methodology schema: {mismatch}");

        return runs;
    }

    internal static string Render(IReadOnlyList<HistoryRun> runs, int window)
    {
        ArgumentNullException.ThrowIfNull(runs);

        int movementCount = window <= 0 ? runs.Count : Math.Min(window, runs.Count);
        var movementWindow = runs.Skip(runs.Count - movementCount).ToArray();
        var movement = BuildMovement(movementWindow);

        const string productSignalNote =
            "Track attributed product defects (target-body decompiler bugs), not "
            + "raw invalid or the unpartitioned IL frontier. Frontier attribution "
            + "is an informational census, not a raw-count ratchet.";
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

        var output = new StringWriter { NewLine = "\n" };
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
        if (window.Any(run => run.ValidDifferent?.FrontierIlDiffAttribution is not null))
        {
            rows.Add(InformationalNullableScalarRow(
                "Frontier product defects (attributed)",
                window,
                cols,
                run => run.ValidDifferent?.FrontierIlDiffAttribution?.ProductBodyDefect));
        }
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

    static MultiSourceRow InformationalNullableScalarRow(
        string label,
        IReadOnlyList<HistoryRun> window,
        string[] cols,
        Func<HistoryRun, int?> value)
    {
        var sources = new Source[window.Count];
        for (int i = 0; i < window.Count; i++)
        {
            sources[i] = value(window[i]) is { } measured
                ? new Source(cols[i], measured)
                : new Source(cols[i], (IMarkoutCell?)null);
        }

        return new MultiSourceRow(label, sources);
    }

    // Runs predating #3096 carry no invalid breakdown; render those columns as an absent cell so the
    // product-defect signal stays honest (no fabricated zero) and Markout's pairwise chain skips them
    // rather than charting a bogus step.
    //
    // productBodyDefect is computed under an explicit invalid-attribution lineage.
    // When the window straddles a lineage boundary the metric is split into one row
    // per lineage, so Markout never charts an incomparable step.
    static IEnumerable<MultiSourceRow> ProductDefectRows(IReadOnlyList<HistoryRun> window, string[] cols)
    {
        int[] lineages = window
            .Where(run => run.InvalidBreakdown is not null)
            .Select(run => AuthoredCorpusMethodology.InvalidAttributionLineage(run.Methodology))
            .OfType<int>()
            .Distinct()
            .Order()
            .ToArray();
        if (lineages.Length > 1)
        {
            foreach (int lineage in lineages)
                yield return ProductDefectRow(ProductDefectLabel(lineage), window, cols, lineage);
        }
        else
        {
            yield return ProductDefectRow("Product defects", window, cols, lineage: null);
        }
    }

    static MultiSourceRow ProductDefectRow(string label, IReadOnlyList<HistoryRun> window, string[] cols, int? lineage)
    {
        var sources = new Source[window.Count];
        for (int i = 0; i < window.Count; i++)
        {
            bool inLineage = lineage is null
                || AuthoredCorpusMethodology.InvalidAttributionLineage(window[i].Methodology) == lineage;
            sources[i] = inLineage && window[i].InvalidBreakdown is { } breakdown
                ? new Source(cols[i], breakdown.ProductBodyDefect)
                : new Source(cols[i], (IMarkoutCell?)null);
        }

        return new MultiSourceRow(label, sources) { Goal = Goal.Lower };
    }

    static string ProductDefectLabel(int lineage)
        => lineage switch
        {
            1 => "Product defects (v1 substitution lower bound)",
            2 => "Product defects (v2 span-measured lower bound)",
            3 => "Product defects (v3 final-shell lower bound)",
            _ => $"Product defects (lineage {lineage})",
        };

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
    [property: JsonRequired] int Total,
    [property: JsonRequired] int FrontierIlExact,
    [property: JsonRequired] int FrontierIlDiff,
    int? Lowering = null,
    int? KnownTaste = null,
    int? FrontierIlNoVerdict = null,
    HistoryRunFrontierIlDiffAttribution? FrontierIlDiffAttribution = null)
{
    /// <summary>True when every sub-bucket was recorded, so the partition is checkable.</summary>
    [JsonIgnore]
    public bool IsComplete => Lowering is not null && KnownTaste is not null && FrontierIlNoVerdict is not null;

    /// <summary>
    /// Sum of the recorded sub-buckets, or null when the partition is incomplete.
    ///
    /// <para>Widened to <see cref="long"/> so the addition cannot wrap. A recorded row
    /// can be caller-supplied via <c>--ratchet-baseline</c>, and sub-buckets of
    /// <c>int.MaxValue, int.MaxValue, 51</c> summed as <see cref="int"/> to exactly 49 —
    /// a partition that "closed" only because it overflowed.</para>
    /// </summary>
    [JsonIgnore]
    public long? SubBucketSum => IsComplete
        ? (long)Lowering!.Value + KnownTaste!.Value + FrontierIlExact + FrontierIlDiff + FrontierIlNoVerdict!.Value
        : null;

    /// <summary>True when no recorded sub-bucket is a negative count.</summary>
    [JsonIgnore]
    public bool CountsAreNonNegative
        => Total >= 0
            && FrontierIlExact >= 0
            && FrontierIlDiff >= 0
            && Lowering is not < 0
            && KnownTaste is not < 0
            && FrontierIlNoVerdict is not < 0
            && FrontierIlDiffAttribution is not { CountsAreNonNegative: false };
}

internal sealed record HistoryRunFrontierIlDiffAttribution(
    [property: JsonRequired] int Total,
    [property: JsonRequired] int ProductBodyDefect,
    [property: JsonRequired] int HarnessShellReconstruction,
    [property: JsonRequired] int CompileBackFloor,
    [property: JsonRequired] int Unclassified)
{
    [JsonIgnore]
    public long Sum
        => (long)ProductBodyDefect + HarnessShellReconstruction + CompileBackFloor + Unclassified;

    [JsonIgnore]
    public bool CountsAreNonNegative
        => Total >= 0
            && ProductBodyDefect >= 0
            && HarnessShellReconstruction >= 0
            && CompileBackFloor >= 0
            && Unclassified >= 0;
}

internal sealed record HistoryRunInvalidBreakdown(
    [property: JsonRequired] int ProductBodyDefect,
    [property: JsonRequired] int HarnessShellReconstruction,
    [property: JsonRequired] int Unclassified)
{
    /// <summary>Sum of the recorded reason buckets, to compare against <c>invalid</c>.</summary>
    [JsonIgnore]
    public long Sum => (long)ProductBodyDefect + HarnessShellReconstruction + Unclassified;

    /// <summary>True when no recorded reason bucket is a negative count.</summary>
    [JsonIgnore]
    public bool CountsAreNonNegative
        => ProductBodyDefect >= 0 && HarnessShellReconstruction >= 0 && Unclassified >= 0;
}

internal sealed record HistoryRun(
    [property: JsonRequired] string? Date,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Commit,
    int PoolMatched,
    int PoolTotal,
    int Evaluated,
    [property: JsonRequired] double ValidPct,
    [property: JsonRequired] int Correct,
    HistoryRunValidDifferent? ValidDifferent,
    [property: JsonRequired] int Invalid,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] HistoryRunInvalidBreakdown? InvalidBreakdown,
    int Unsupported,
    int Drift,
    [property: JsonPropertyName("inputsComplete")] bool InputsComplete,
    [property: JsonPropertyName("sweepManifestSha256")] string? SweepManifestSha256,
    int? MethodologyVersion = null,
    /// <summary>
    /// Identity of the corpus this run measured. Absent on every row recorded before
    /// the ratchet, which is why absence is compared as a distinct value rather than
    /// waved through: an unidentified corpus is not the same as a matching one.
    /// </summary>
    [property: JsonPropertyName("corpusSha256")] string? CorpusSha256 = null,
    int? NotFull = null,
    int? UnknownOutcome = null,
    /// <summary>
    /// Identity of the assembly pool this run measured: the named, content-hashed
    /// assemblies themselves. Distinct from the older <c>sweepManifestSha256</c>, which
    /// hashed the sweep manifest and so described only half the pool; rows carrying
    /// only that field record no pool identity under the current scheme, and absence is
    /// compared as a distinct value rather than waved through.
    /// </summary>
    [property: JsonPropertyName("poolSha256")] string? PoolSha256 = null)
{
    // Unidentified rows predating the span-attribution change carry no
    // methodologyVersion; treat them as v1 (substitution lower bound).
    [JsonIgnore]
    public int Methodology => MethodologyVersion ?? 1;

    /// <summary>
    /// True when every top-level bucket was recorded, so
    /// <see cref="TopLevelSum"/> can be compared against <see cref="Evaluated"/>.
    /// </summary>
    [JsonIgnore]
    public bool TopLevelIsComplete => ValidDifferent is not null && NotFull is not null && UnknownOutcome is not null;

    /// <summary>
    /// Sum of the recorded top-level buckets, or null when any is unrecorded. Widened
    /// to <see cref="long"/> for the same reason as <see cref="HistoryRunValidDifferent.SubBucketSum"/>:
    /// a sum that can wrap is not a partition check.
    /// </summary>
    [JsonIgnore]
    public long? TopLevelSum => TopLevelIsComplete
        ? (long)Correct + ValidDifferent!.Total + Invalid + NotFull!.Value + Drift + Unsupported + UnknownOutcome!.Value
        : null;

    /// <summary>
    /// True when no recorded count on the row, at any level, is negative.
    ///
    /// <para>A sum that closes is not by itself evidence that the row is arithmetically
    /// real: a negative bucket lets any other bucket be arbitrarily large while the
    /// total still lands on <c>evaluated</c>. That is not hypothetical — it is how a
    /// reviewer forged a row reporting <c>invalid: 0</c> alongside
    /// <c>productBodyDefect: 100</c>. Non-negativity is what makes closure mean every
    /// bucket is bounded by the run's own size.</para>
    /// </summary>
    [JsonIgnore]
    public bool CountsAreNonNegative
        => Evaluated >= 0
            && PoolMatched >= 0
            && PoolTotal >= 0
            && Correct >= 0
            && Invalid >= 0
            && Unsupported >= 0
            && Drift >= 0
            && NotFull is not < 0
            && UnknownOutcome is not < 0
            && ValidDifferent is not { CountsAreNonNegative: false }
            && InvalidBreakdown is not { CountsAreNonNegative: false };
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
