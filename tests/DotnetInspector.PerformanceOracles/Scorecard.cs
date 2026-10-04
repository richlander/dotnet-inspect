using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace DotnetInspector.PerformanceOracles;

/// <summary>The six closings every scorecard scores.</summary>
public enum ScorecardClosing
{
    Exists,
    Count,
    Head,
    Tail,
    Rows,
    Window,
}

/// <summary>
/// The sizes a scorecard asks for: Head(N), Tail(N), and the strict window
/// Rows(WindowFirst..WindowLast), positions counted from 1.
/// </summary>
public sealed record ScorecardShape(int N = 6, int WindowFirst = 100, int WindowLast = 110)
{
    public int WindowSkip => WindowFirst - 1;

    public int WindowTake => WindowLast - WindowFirst + 1;

    /// <summary>The closing's name with its parameters, such as <c>Head(6)</c> or <c>Rows(100..110)</c>.</summary>
    public string Label(ScorecardClosing closing) => closing switch
    {
        ScorecardClosing.Exists => "Exists",
        ScorecardClosing.Count => "Count",
        ScorecardClosing.Head => string.Create(CultureInfo.InvariantCulture, $"Head({N})"),
        ScorecardClosing.Tail => string.Create(CultureInfo.InvariantCulture, $"Tail({N})"),
        ScorecardClosing.Rows => "Rows",
        ScorecardClosing.Window => string.Create(CultureInfo.InvariantCulture, $"Rows({WindowFirst}..{WindowLast})"),
        _ => throw new ArgumentOutOfRangeException(nameof(closing)),
    };
}

/// <summary>
/// One column's answer to one closing: a Boolean, a count, rows, or a strict
/// window failure.
/// </summary>
public readonly record struct ScorecardAnswer<TRow>(bool? Exists, int? Count, IReadOnlyList<TRow>? Rows, bool WindowFailed)
{
    public static ScorecardAnswer<TRow> OfExists(bool exists) => new(exists, null, null, false);

    public static ScorecardAnswer<TRow> OfCount(int count) => new(null, count, null, false);

    public static ScorecardAnswer<TRow> OfRows(IReadOnlyList<TRow> rows) => new(null, null, rows, false);

    public static ScorecardAnswer<TRow> OfWindowFailure() => new(null, null, null, true);
}

/// <summary>
/// A column: a name and how it answers each closing over an asset. The oracle
/// column is an NLinq query; others are Before, After, or labeled extras.
/// </summary>
public sealed record ScorecardColumn<TAsset, TRow>(
    string Name,
    Func<ScorecardClosing, TAsset, ScorecardAnswer<TRow>> Answer);

/// <summary>
/// A pinned real asset the scorecard runs over. <see cref="Name"/> is for
/// display; an asset's identity is its position in the asset list, so two
/// assets may share a name without their cells colliding.
/// </summary>
public sealed record ScorecardAsset<TAsset>(string Name, TAsset Asset);

/// <summary>Display names for assets loaded from paths.</summary>
public static class ScorecardAssetNames
{
    /// <summary>
    /// Each path's file name without its extension, qualified with as many
    /// parent directories as it takes to make every name distinct.
    /// </summary>
    public static IReadOnlyList<string> FromPaths(IReadOnlyList<string> paths)
    {
        string[][] parts = [.. paths.Select(p => Path.GetFullPath(p)
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))];
        string[] names = new string[paths.Count];
        for (int i = 0; i < paths.Count; i++)
        {
            for (int depth = 1; ; depth++)
            {
                names[i] = NameAt(parts[i], depth);
                bool unique = true;
                for (int j = 0; j < paths.Count && unique; j++)
                    unique = j == i || NameAt(parts[j], depth) != names[i];

                // Distinct paths that still read alike, such as the same file
                // twice, keep their position as the tiebreaker.
                if (unique)
                    break;
                if (depth >= parts[i].Length)
                {
                    names[i] = string.Create(CultureInfo.InvariantCulture, $"{NameAt(parts[i], 1)} #{i + 1}");
                    break;
                }
            }
        }

        return names;

        static string NameAt(string[] path, int depth)
        {
            int take = Math.Min(depth, path.Length);
            string file = Path.GetFileNameWithoutExtension(path[^1]);
            return take == 1 ? file : string.Join('/', path[^take..^1]) + "/" + file;
        }
    }
}

/// <summary>A column whose answer differs from the oracle's. The answer texts are for display.</summary>
public sealed record ScorecardMismatch(string Asset, ScorecardClosing Closing, string Column, string Answer, string OracleAnswer);

/// <summary>
/// The answer check: every mismatch, and every asset whose strict window
/// failed in every column. A failed window is an outcome, not a result: its
/// timing is never scored.
/// </summary>
public sealed record ScorecardCheck(
    IReadOnlyList<ScorecardMismatch> Mismatches,
    IReadOnlyList<string> WindowFailures,
    int Compared)
{
    public bool Agrees => Mismatches.Count == 0;
}

/// <summary>How a scorecard times each cell.</summary>
public sealed record ScorecardTiming(
    int Rounds = 6,
    int Warmup = 5,
    int BudgetMilliseconds = 2000,
    int MinSamples = 20,
    int MaxSamples = 5000);

/// <summary>
/// One cell: the median of each round's median, in microseconds, or a failed
/// strict window, which carries no timing.
/// </summary>
public sealed record ScorecardCell(int AssetIndex, string Asset, ScorecardClosing Closing, string Column, IReadOnlyList<double> RoundMedians, bool WindowFailed = false)
{
    public double Median => WindowFailed
        ? throw new InvalidOperationException($"{Asset} {Closing} {Column} is a failed strict window and has no timing.")
        : Scorecard.MedianOf(RoundMedians);
}

/// <summary>One closing's summary for one column across the assets whose cells were timed.</summary>
public sealed record ScorecardSummary(
    ScorecardClosing Closing,
    string Column,
    double GeometricMean,
    double Min,
    double Max,
    int Assets,
    int FailedExcluded);

/// <summary>
/// One closing's cost relative to Count for one implementation column.
/// </summary>
public sealed record ScorecardTerminalSummary(
    string Column,
    ScorecardClosing Closing,
    double GeometricMean,
    double Min,
    double Max,
    int Assets,
    int FailedExcluded);

/// <summary>
/// The scorecard harness: checks that every column answers every closing the
/// way the oracle does, and times them with rotated rounds so drift affects
/// every column equally.
/// </summary>
public static class Scorecard
{
    public static IReadOnlyList<ScorecardClosing> Closings { get; } =
        [ScorecardClosing.Exists, ScorecardClosing.Count, ScorecardClosing.Head, ScorecardClosing.Tail, ScorecardClosing.Rows, ScorecardClosing.Window];

    /// <summary>
    /// Whether two answers are the same: the same kind, and the same Boolean,
    /// count, or row sequence, element for element in order.
    /// </summary>
    public static bool SameAnswer<TRow>(ScorecardAnswer<TRow> left, ScorecardAnswer<TRow> right, IEqualityComparer<TRow>? rows = null)
    {
        if (left.WindowFailed || right.WindowFailed)
            return left.WindowFailed && right.WindowFailed;
        if (left.Exists is not null || right.Exists is not null)
            return left.Exists == right.Exists;
        if (left.Count is not null || right.Count is not null)
            return left.Count == right.Count;
        if (left.Rows is not { } leftRows || right.Rows is not { } rightRows)
            throw new InvalidOperationException("A scorecard answer must carry a Boolean, a count, rows, or a window failure.");
        if (leftRows.Count != rightRows.Count)
            return false;
        rows ??= EqualityComparer<TRow>.Default;
        for (int i = 0; i < leftRows.Count; i++)
        {
            if (!rows.Equals(leftRows[i], rightRows[i]))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Describes an answer for display: the Boolean, the count, the row count
    /// with an FNV-1a hash of the row text, or <c>fail</c>. Comparison uses
    /// <see cref="SameAnswer{TRow}"/>, never this text.
    /// </summary>
    public static string Describe<TRow>(ScorecardAnswer<TRow> answer, Func<TRow, string> rowText)
    {
        if (answer.WindowFailed)
            return "fail";
        if (answer.Exists is bool exists)
            return exists ? "true" : "false";
        if (answer.Count is int count)
            return string.Create(CultureInfo.InvariantCulture, $"c={count}");
        if (answer.Rows is not { } rows)
            throw new InvalidOperationException("A scorecard answer must carry a Boolean, a count, rows, or a window failure.");

        uint hash = 2166136261;
        foreach (TRow row in rows)
        {
            foreach (char c in rowText(row))
                hash = unchecked((hash ^ c) * 16777619);
            hash = unchecked((hash ^ '\n') * 16777619);
        }

        return string.Create(CultureInfo.InvariantCulture, $"n={rows.Count};h={hash:X8}");
    }

    /// <summary>
    /// Compares every column's answer with the oracle's for the selected
    /// closings, and records each asset whose strict window failed in the
    /// oracle. A column that succeeds where the oracle failed, or fails where
    /// it succeeded, is a mismatch.
    /// </summary>
    public static ScorecardCheck Check<TAsset, TRow>(
        IReadOnlyList<ScorecardAsset<TAsset>> assets,
        ScorecardColumn<TAsset, TRow> oracle,
        IReadOnlyList<ScorecardColumn<TAsset, TRow>> columns,
        Func<TRow, string> rowText,
        IEqualityComparer<TRow>? rowComparer = null,
        IReadOnlyList<ScorecardClosing>? closings = null)
    {
        closings = RequireClosings(closings);
        var mismatches = new List<ScorecardMismatch>();
        var windowFailures = new List<string>();
        int compared = 0;
        foreach (ScorecardAsset<TAsset> asset in assets)
        {
            foreach (ScorecardClosing closing in closings)
            {
                ScorecardAnswer<TRow> expected = oracle.Answer(closing, asset.Asset);
                if (closing == ScorecardClosing.Window && expected.WindowFailed)
                    windowFailures.Add(asset.Name);
                foreach (ScorecardColumn<TAsset, TRow> column in columns)
                {
                    if (ReferenceEquals(column, oracle))
                        continue;
                    ScorecardAnswer<TRow> actual = column.Answer(closing, asset.Asset);
                    compared++;
                    if (!SameAnswer(actual, expected, rowComparer))
                        mismatches.Add(new(asset.Name, closing, column.Name, Describe(actual, rowText), Describe(expected, rowText)));
                }
            }
        }

        return new(mismatches, windowFailures, compared);
    }

    /// <summary>
    /// Times every column on every selected closing and asset. Each round
    /// visits the columns in a rotated order; a cell is the median of its
    /// round medians. A strict window that fails for an asset is recorded as
    /// a failed cell with no timing.
    /// </summary>
    public static IReadOnlyList<ScorecardCell> Measure<TAsset, TRow>(
        IReadOnlyList<ScorecardAsset<TAsset>> assets,
        IReadOnlyList<ScorecardColumn<TAsset, TRow>> columns,
        ScorecardTiming timing,
        Action<string>? progress = null,
        IReadOnlyList<ScorecardClosing>? closings = null)
    {
        closings = RequireClosings(closings);
        var rounds = new Dictionary<(int, ScorecardClosing, string), List<double>>();
        var failed = new HashSet<(int, ScorecardClosing, string)>();
        for (int round = 0; round < timing.Rounds; round++)
        {
            for (int a = 0; a < assets.Count; a++)
            {
                ScorecardAsset<TAsset> asset = assets[a];
                foreach (ScorecardClosing closing in closings)
                {
                    for (int i = 0; i < columns.Count; i++)
                    {
                        ScorecardColumn<TAsset, TRow> column = columns[(i + round) % columns.Count];
                        var key = (a, closing, column.Name);
                        if (failed.Contains(key))
                            continue;
                        if (closing == ScorecardClosing.Window && column.Answer(closing, asset.Asset).WindowFailed)
                        {
                            failed.Add(key);
                            continue;
                        }

                        double median = MedianMicroseconds(column, closing, asset.Asset, timing);
                        if (!rounds.TryGetValue(key, out List<double>? medians))
                            rounds[key] = medians = [];
                        medians.Add(median);
                    }
                }

                progress?.Invoke(string.Create(CultureInfo.InvariantCulture, $"round {round + 1}/{timing.Rounds}: {asset.Name}"));
            }
        }

        var cells = new List<ScorecardCell>();
        for (int a = 0; a < assets.Count; a++)
        {
            ScorecardAsset<TAsset> asset = assets[a];
            foreach (ScorecardClosing closing in closings)
            {
                foreach (ScorecardColumn<TAsset, TRow> column in columns)
                {
                    var key = (a, closing, column.Name);
                    cells.Add(failed.Contains(key)
                        ? new(a, asset.Name, closing, column.Name, [], WindowFailed: true)
                        : new(a, asset.Name, closing, column.Name, rounds[key]));
                }
            }
        }

        return cells;
    }

    /// <summary>
    /// Per closing and column, the geometric mean across assets of the ratio
    /// to the implementation baseline, with its minimum and maximum. An asset
    /// is scored only when both cells were timed; failed strict windows are
    /// excluded and counted.
    /// </summary>
    public static IReadOnlyList<ScorecardSummary> Summarize(
        IReadOnlyList<ScorecardCell> cells,
        string baseline)
    {
        var summary = new List<ScorecardSummary>();
        var byKey = cells.ToDictionary(c => (c.AssetIndex, c.Closing, c.Column));
        string[] columns = [.. cells.Select(c => c.Column).Distinct()];
        int[] assets = [.. cells.Select(c => c.AssetIndex).Distinct()];
        ScorecardClosing[] closings =
            [.. cells.Select(c => c.Closing).Distinct()];
        foreach (ScorecardClosing closing in closings)
        {
            foreach (string column in columns)
            {
                var ratios = new List<double>();
                int excluded = 0;
                foreach (int asset in assets)
                {
                    ScorecardCell cell = byKey[(asset, closing, column)];
                    ScorecardCell baselineCell =
                        byKey[(asset, closing, baseline)];
                    if (cell.WindowFailed || baselineCell.WindowFailed)
                    {
                        excluded++;
                        continue;
                    }

                    ratios.Add(cell.Median / baselineCell.Median);
                }

                summary.Add(ratios.Count == 0
                    ? new(closing, column, double.NaN, double.NaN, double.NaN, 0, excluded)
                    : new(closing, column, Math.Exp(ratios.Average(Math.Log)), ratios.Min(), ratios.Max(), ratios.Count, excluded));
            }
        }

        return summary;
    }

    /// <summary>
    /// Per implementation column and closing, the geometric mean across
    /// assets of the ratio to Count for the same column.
    /// </summary>
    public static IReadOnlyList<ScorecardTerminalSummary>
        SummarizeTerminalsToCount(
            IReadOnlyList<ScorecardCell> cells)
    {
        var summary = new List<ScorecardTerminalSummary>();
        var byKey = cells.ToDictionary(
            cell =>
                (cell.AssetIndex, cell.Closing, cell.Column));
        string[] columns =
            [.. cells.Select(cell => cell.Column).Distinct()];
        int[] assets =
            [.. cells.Select(cell => cell.AssetIndex).Distinct()];
        ScorecardClosing[] closings =
        [
            .. new[]
            {
                ScorecardClosing.Exists,
                ScorecardClosing.Count,
                ScorecardClosing.Rows,
            }.Where(closing =>
                cells.Any(cell => cell.Closing == closing)),
        ];
        if (!closings.Contains(ScorecardClosing.Count))
            return summary;

        foreach (string column in columns)
        {
            foreach (ScorecardClosing closing in closings)
            {
                var ratios = new List<double>();
                int excluded = 0;
                foreach (int asset in assets)
                {
                    ScorecardCell cell =
                        byKey[(asset, closing, column)];
                    ScorecardCell count =
                        byKey[
                            (asset, ScorecardClosing.Count, column)];
                    if (cell.WindowFailed
                        || count.WindowFailed
                        || count.Median <= 0)
                    {
                        excluded++;
                        continue;
                    }

                    ratios.Add(cell.Median / count.Median);
                }

                summary.Add(
                    ratios.Count == 0
                        ? new(
                            column,
                            closing,
                            double.NaN,
                            double.NaN,
                            double.NaN,
                            0,
                            excluded)
                        : new(
                            column,
                            closing,
                            Math.Exp(ratios.Average(Math.Log)),
                            ratios.Min(),
                            ratios.Max(),
                            ratios.Count,
                            excluded));
            }
        }

        return summary;
    }

    /// <summary>
    /// The time report as Markdown: implementation ratios to one baseline,
    /// terminal ratios to Count, then every asset's absolute medians.
    /// </summary>
    public static string Report(
        IReadOnlyList<ScorecardCell> cells,
        string baseline,
        ScorecardShape shape)
    {
        IReadOnlyList<ScorecardSummary> summary =
            Summarize(cells, baseline);
        IReadOnlyList<ScorecardTerminalSummary> terminalSummary =
            SummarizeTerminalsToCount(cells);
        string[] columns = [.. cells.Select(c => c.Column).Distinct()];
        (int Index, string Name)[] assets = [.. cells.Select(c => (c.AssetIndex, c.Asset)).Distinct()];
        ScorecardClosing[] closings =
            [.. cells.Select(c => c.Closing).Distinct()];
        var text = new StringBuilder();

        text.Append("Implementation ratios to ")
            .Append(baseline)
            .AppendLine(
                ": geometric mean across assets (min–max); "
                    + "lower is faster.")
            .AppendLine();
        text.Append("| Closing | Assets |");
        foreach (string column in columns)
            text.Append(' ').Append(column).Append(" |");
        text.AppendLine().Append("| --- | --- |");
        foreach (string _ in columns)
            text.Append(" ---: |");
        text.AppendLine();
        foreach (ScorecardClosing closing in closings)
        {
            ScorecardSummary scored =
                summary.First(summary =>
                    summary.Closing == closing
                    && summary.Column == baseline);
            text.Append("| ").Append(shape.Label(closing)).Append(" | ")
                .Append(string.Create(CultureInfo.InvariantCulture, $"{scored.Assets}"))
                .Append(scored.FailedExcluded == 0 ? "" : string.Create(CultureInfo.InvariantCulture, $" ({scored.FailedExcluded} failed, excluded)"))
                .Append(" |");
            foreach (string column in columns)
            {
                ScorecardSummary entry = summary.First(s => s.Closing == closing && s.Column == column);
                text.Append(entry.Assets == 0 ? " — |"
                    : column == baseline ? " 1.00× |"
                    : string.Create(CultureInfo.InvariantCulture, $" {entry.GeometricMean:0.00}× ({entry.Min:0.00}–{entry.Max:0.00}) |"));
            }

            text.AppendLine();
        }

        if (terminalSummary.Count > 0)
        {
            ScorecardClosing[] terminalClosings =
            [
                .. terminalSummary
                    .Select(summary => summary.Closing)
                    .Distinct(),
            ];
            text.AppendLine()
                .AppendLine(
                    "Terminal ratios to Count: geometric mean across "
                        + "assets (min–max); lower is faster.")
                .AppendLine();
            text.Append("| Implementation | Assets |");
            foreach (ScorecardClosing closing in terminalClosings)
            {
                text.Append(' ')
                    .Append(shape.Label(closing))
                    .Append(" |");
            }
            text.AppendLine()
                .Append("| --- | ---: |");
            foreach (ScorecardClosing _ in terminalClosings)
                text.Append(" ---: |");
            text.AppendLine();
            foreach (string column in columns)
            {
                ScorecardTerminalSummary scored =
                    terminalSummary.First(summary =>
                        summary.Column == column
                        && summary.Closing
                            == ScorecardClosing.Count);
                text.Append("| ")
                    .Append(column)
                    .Append(" | ")
                    .Append(
                        scored.Assets.ToString(
                            CultureInfo.InvariantCulture))
                    .Append(" |");
                foreach (ScorecardClosing closing
                    in terminalClosings)
                {
                    ScorecardTerminalSummary entry =
                        terminalSummary.First(summary =>
                            summary.Column == column
                            && summary.Closing == closing);
                    text.Append(
                        entry.Assets == 0
                            ? " — |"
                            : closing == ScorecardClosing.Count
                                ? " 1.00× |"
                                : string.Create(
                                    CultureInfo.InvariantCulture,
                                    $" {entry.GeometricMean:0.00}× "
                                        + $"({entry.Min:0.00}–"
                                        + $"{entry.Max:0.00}) |"));
                }
                text.AppendLine();
            }
        }

        text.AppendLine().AppendLine("Absolute medians, µs; `fail` is a strict window that does not exist for the asset.").AppendLine();
        text.Append("| Asset | Closing |");
        foreach (string column in columns)
            text.Append(' ').Append(column).Append(" |");
        text.AppendLine().Append("| --- | --- |");
        foreach (string _ in columns)
            text.Append(" ---: |");
        text.AppendLine();
        var byKey = cells.ToDictionary(c => (c.AssetIndex, c.Closing, c.Column));
        foreach ((int index, string name) in assets)
        {
            foreach (ScorecardClosing closing in closings)
            {
                text.Append("| ").Append(name).Append(" | ").Append(shape.Label(closing)).Append(" |");
                foreach (string column in columns)
                {
                    ScorecardCell cell = byKey[(index, closing, column)];
                    text.Append(cell.WindowFailed ? " fail |" : string.Create(CultureInfo.InvariantCulture, $" {cell.Median:0.0} |"));
                }

                text.AppendLine();
            }
        }

        return text.ToString();
    }

    /// <summary>Every cell with its round medians, as tab-separated values; a failed strict window reads <c>fail</c>.</summary>
    public static void WriteTsv(IReadOnlyList<ScorecardCell> cells, TextWriter writer)
    {
        writer.WriteLine("asset\tclosing\tcolumn\tmedian_us\tround_medians_us");
        foreach (ScorecardCell cell in cells)
        {
            writer.WriteLine(cell.WindowFailed
                ? $"{cell.Asset}\t{cell.Closing}\t{cell.Column}\tfail\t"
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"{cell.Asset}\t{cell.Closing}\t{cell.Column}\t{cell.Median:0.###}\t{string.Join(",", cell.RoundMedians.Select(m => m.ToString("0.###", CultureInfo.InvariantCulture)))}"));
        }
    }

    internal static double MedianOf(IReadOnlyList<double> values)
    {
        double[] sorted = [.. values];
        Array.Sort(sorted);
        int middle = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    static double MedianMicroseconds<TAsset, TRow>(
        ScorecardColumn<TAsset, TRow> column,
        ScorecardClosing closing,
        TAsset asset,
        ScorecardTiming timing)
    {
        for (int i = 0; i < timing.Warmup; i++)
            Keep(column.Answer(closing, asset));

        int batchSize = 1;
        long minimumBatchTicks = Math.Max(
            1,
            Stopwatch.Frequency / 10_000);
        while (batchSize < 1_048_576)
        {
            long calibrationStart = Stopwatch.GetTimestamp();
            for (int i = 0; i < batchSize; i++)
                Keep(column.Answer(closing, asset));
            if (Stopwatch.GetTimestamp() - calibrationStart
                >= minimumBatchTicks)
            {
                break;
            }
            batchSize *= 2;
        }

        var samples = new List<double>();
        long budget = Stopwatch.Frequency * timing.BudgetMilliseconds / 1000;
        long started = Stopwatch.GetTimestamp();
        while (samples.Count < timing.MaxSamples
            && (samples.Count < timing.MinSamples || Stopwatch.GetTimestamp() - started < budget))
        {
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < batchSize; i++)
                Keep(column.Answer(closing, asset));
            samples.Add(
                Stopwatch.GetElapsedTime(start).TotalMicroseconds
                    / batchSize);
        }

        return MedianOf(samples);
    }

    static long s_sink;

    static IReadOnlyList<ScorecardClosing> RequireClosings(
        IReadOnlyList<ScorecardClosing>? closings)
    {
        if (closings is null)
            return Closings;
        if (closings.Count == 0)
        {
            throw new ArgumentException(
                "A scorecard must select at least one closing.",
                nameof(closings));
        }
        if (closings.Distinct().Count() != closings.Count)
        {
            throw new ArgumentException(
                "A scorecard cannot select the same closing more than once.",
                nameof(closings));
        }
        return closings;
    }

    // Consumes every answer without allocating, so no column's work is dead.
    static void Keep<TRow>(ScorecardAnswer<TRow> answer) =>
        Volatile.Write(
            ref s_sink,
            s_sink + (answer.Count ?? 0) + (answer.Exists == true ? 1 : 0) + (answer.Rows?.Count ?? 0));
}
