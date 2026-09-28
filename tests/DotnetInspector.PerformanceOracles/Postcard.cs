using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace DotnetInspector.PerformanceOracles;

/// <summary>The six closings every postcard scores.</summary>
public enum PostcardClosing
{
    Exists,
    Count,
    Head,
    Tail,
    Rows,
    Window,
}

/// <summary>
/// The sizes a postcard asks for: Head(N), Tail(N), and the strict window
/// Rows(WindowFirst..WindowLast), positions counted from 1.
/// </summary>
public sealed record PostcardShape(int N = 6, int WindowFirst = 100, int WindowLast = 110)
{
    public int WindowSkip => WindowFirst - 1;

    public int WindowTake => WindowLast - WindowFirst + 1;

    /// <summary>The closing's name with its parameters, such as <c>Head(6)</c> or <c>Rows(100..110)</c>.</summary>
    public string Label(PostcardClosing closing) => closing switch
    {
        PostcardClosing.Exists => "Exists",
        PostcardClosing.Count => "Count",
        PostcardClosing.Head => string.Create(CultureInfo.InvariantCulture, $"Head({N})"),
        PostcardClosing.Tail => string.Create(CultureInfo.InvariantCulture, $"Tail({N})"),
        PostcardClosing.Rows => "Rows",
        PostcardClosing.Window => string.Create(CultureInfo.InvariantCulture, $"Rows({WindowFirst}..{WindowLast})"),
        _ => throw new ArgumentOutOfRangeException(nameof(closing)),
    };
}

/// <summary>
/// One column's answer to one closing: a Boolean, a count, rows, or a strict
/// window failure.
/// </summary>
public readonly record struct PostcardAnswer<TRow>(bool? Exists, int? Count, IReadOnlyList<TRow>? Rows, bool WindowFailed)
{
    public static PostcardAnswer<TRow> OfExists(bool exists) => new(exists, null, null, false);

    public static PostcardAnswer<TRow> OfCount(int count) => new(null, count, null, false);

    public static PostcardAnswer<TRow> OfRows(IReadOnlyList<TRow> rows) => new(null, null, rows, false);

    public static PostcardAnswer<TRow> OfWindowFailure() => new(null, null, null, true);
}

/// <summary>
/// A column: a name and how it answers each closing over an asset. The oracle
/// column is an NLinq query; others are Before, After, or labeled extras.
/// </summary>
public sealed record PostcardColumn<TAsset, TRow>(
    string Name,
    Func<PostcardClosing, TAsset, PostcardAnswer<TRow>> Answer);

/// <summary>
/// A pinned real asset the postcard runs over. <see cref="Name"/> is for
/// display; an asset's identity is its position in the asset list, so two
/// assets may share a name without their cells colliding.
/// </summary>
public sealed record PostcardAsset<TAsset>(string Name, TAsset Asset);

/// <summary>Display names for assets loaded from paths.</summary>
public static class PostcardAssetNames
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
public sealed record PostcardMismatch(string Asset, PostcardClosing Closing, string Column, string Answer, string OracleAnswer);

/// <summary>
/// The answer check: every mismatch, and every asset whose strict window
/// failed in every column. A failed window is an outcome, not a result: its
/// timing is never scored.
/// </summary>
public sealed record PostcardCheck(
    IReadOnlyList<PostcardMismatch> Mismatches,
    IReadOnlyList<string> WindowFailures,
    int Compared)
{
    public bool Agrees => Mismatches.Count == 0;
}

/// <summary>How a postcard times each cell.</summary>
public sealed record PostcardTiming(
    int Rounds = 6,
    int Warmup = 5,
    int BudgetMilliseconds = 2000,
    int MinSamples = 20,
    int MaxSamples = 5000);

/// <summary>
/// One cell: the median of each round's median, in microseconds, or a failed
/// strict window, which carries no timing.
/// </summary>
public sealed record PostcardCell(int AssetIndex, string Asset, PostcardClosing Closing, string Column, IReadOnlyList<double> RoundMedians, bool WindowFailed = false)
{
    public double Median => WindowFailed
        ? throw new InvalidOperationException($"{Asset} {Closing} {Column} is a failed strict window and has no timing.")
        : Postcard.MedianOf(RoundMedians);
}

/// <summary>One closing's summary for one column across the assets whose cells were timed.</summary>
public sealed record PostcardSummary(
    PostcardClosing Closing,
    string Column,
    double GeometricMean,
    double Min,
    double Max,
    int Assets,
    int FailedExcluded);

/// <summary>
/// The postcard harness: checks that every column answers every closing the
/// way the oracle does, and times them with rotated rounds so drift affects
/// every column equally.
/// </summary>
public static class Postcard
{
    public static IReadOnlyList<PostcardClosing> Closings { get; } =
        [PostcardClosing.Exists, PostcardClosing.Count, PostcardClosing.Head, PostcardClosing.Tail, PostcardClosing.Rows, PostcardClosing.Window];

    /// <summary>
    /// Whether two answers are the same: the same kind, and the same Boolean,
    /// count, or row sequence, element for element in order.
    /// </summary>
    public static bool SameAnswer<TRow>(PostcardAnswer<TRow> left, PostcardAnswer<TRow> right, IEqualityComparer<TRow>? rows = null)
    {
        if (left.WindowFailed || right.WindowFailed)
            return left.WindowFailed && right.WindowFailed;
        if (left.Exists is not null || right.Exists is not null)
            return left.Exists == right.Exists;
        if (left.Count is not null || right.Count is not null)
            return left.Count == right.Count;
        if (left.Rows is not { } leftRows || right.Rows is not { } rightRows)
            throw new InvalidOperationException("A postcard answer must carry a Boolean, a count, rows, or a window failure.");
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
    public static string Describe<TRow>(PostcardAnswer<TRow> answer, Func<TRow, string> rowText)
    {
        if (answer.WindowFailed)
            return "fail";
        if (answer.Exists is bool exists)
            return exists ? "true" : "false";
        if (answer.Count is int count)
            return string.Create(CultureInfo.InvariantCulture, $"c={count}");
        if (answer.Rows is not { } rows)
            throw new InvalidOperationException("A postcard answer must carry a Boolean, a count, rows, or a window failure.");

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
    /// Compares every column's answer with the oracle's, and records each asset
    /// whose strict window failed in the oracle. A column that succeeds where
    /// the oracle failed, or fails where it succeeded, is a mismatch.
    /// </summary>
    public static PostcardCheck Check<TAsset, TRow>(
        IReadOnlyList<PostcardAsset<TAsset>> assets,
        PostcardColumn<TAsset, TRow> oracle,
        IReadOnlyList<PostcardColumn<TAsset, TRow>> columns,
        Func<TRow, string> rowText,
        IEqualityComparer<TRow>? rowComparer = null)
    {
        var mismatches = new List<PostcardMismatch>();
        var windowFailures = new List<string>();
        int compared = 0;
        foreach (PostcardAsset<TAsset> asset in assets)
        {
            foreach (PostcardClosing closing in Closings)
            {
                PostcardAnswer<TRow> expected = oracle.Answer(closing, asset.Asset);
                if (closing == PostcardClosing.Window && expected.WindowFailed)
                    windowFailures.Add(asset.Name);
                foreach (PostcardColumn<TAsset, TRow> column in columns)
                {
                    if (ReferenceEquals(column, oracle))
                        continue;
                    PostcardAnswer<TRow> actual = column.Answer(closing, asset.Asset);
                    compared++;
                    if (!SameAnswer(actual, expected, rowComparer))
                        mismatches.Add(new(asset.Name, closing, column.Name, Describe(actual, rowText), Describe(expected, rowText)));
                }
            }
        }

        return new(mismatches, windowFailures, compared);
    }

    /// <summary>
    /// Times every column on every closing and asset. Each round visits the
    /// columns in a rotated order; a cell is the median of its round medians.
    /// A strict window that fails for an asset is recorded as a failed cell
    /// with no timing.
    /// </summary>
    public static IReadOnlyList<PostcardCell> Measure<TAsset, TRow>(
        IReadOnlyList<PostcardAsset<TAsset>> assets,
        IReadOnlyList<PostcardColumn<TAsset, TRow>> columns,
        PostcardTiming timing,
        Action<string>? progress = null)
    {
        var rounds = new Dictionary<(int, PostcardClosing, string), List<double>>();
        var failed = new HashSet<(int, PostcardClosing, string)>();
        for (int round = 0; round < timing.Rounds; round++)
        {
            for (int a = 0; a < assets.Count; a++)
            {
                PostcardAsset<TAsset> asset = assets[a];
                foreach (PostcardClosing closing in Closings)
                {
                    for (int i = 0; i < columns.Count; i++)
                    {
                        PostcardColumn<TAsset, TRow> column = columns[(i + round) % columns.Count];
                        var key = (a, closing, column.Name);
                        if (failed.Contains(key))
                            continue;
                        if (closing == PostcardClosing.Window && column.Answer(closing, asset.Asset).WindowFailed)
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

        var cells = new List<PostcardCell>();
        for (int a = 0; a < assets.Count; a++)
        {
            PostcardAsset<TAsset> asset = assets[a];
            foreach (PostcardClosing closing in Closings)
            {
                foreach (PostcardColumn<TAsset, TRow> column in columns)
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
    /// to the oracle, with its minimum and maximum. An asset is scored only
    /// when both its cell and the oracle's were timed; failed strict windows
    /// are excluded and counted.
    /// </summary>
    public static IReadOnlyList<PostcardSummary> Summarize(IReadOnlyList<PostcardCell> cells, string oracle)
    {
        var summary = new List<PostcardSummary>();
        var byKey = cells.ToDictionary(c => (c.AssetIndex, c.Closing, c.Column));
        string[] columns = [.. cells.Select(c => c.Column).Distinct()];
        int[] assets = [.. cells.Select(c => c.AssetIndex).Distinct()];
        foreach (PostcardClosing closing in Closings)
        {
            foreach (string column in columns)
            {
                var ratios = new List<double>();
                int excluded = 0;
                foreach (int asset in assets)
                {
                    PostcardCell cell = byKey[(asset, closing, column)];
                    PostcardCell baseline = byKey[(asset, closing, oracle)];
                    if (cell.WindowFailed || baseline.WindowFailed)
                    {
                        excluded++;
                        continue;
                    }

                    ratios.Add(cell.Median / baseline.Median);
                }

                summary.Add(ratios.Count == 0
                    ? new(closing, column, double.NaN, double.NaN, double.NaN, 0, excluded)
                    : new(closing, column, Math.Exp(ratios.Average(Math.Log)), ratios.Min(), ratios.Max(), ratios.Count, excluded));
            }
        }

        return summary;
    }

    /// <summary>
    /// The time report as Markdown: ratios to the oracle per closing, with the
    /// number of assets scored and any failed strict windows excluded, then
    /// every asset's absolute medians.
    /// </summary>
    public static string Report(IReadOnlyList<PostcardCell> cells, string oracle, PostcardShape shape)
    {
        IReadOnlyList<PostcardSummary> summary = Summarize(cells, oracle);
        string[] columns = [.. cells.Select(c => c.Column).Distinct()];
        (int Index, string Name)[] assets = [.. cells.Select(c => (c.AssetIndex, c.Asset)).Distinct()];
        var text = new StringBuilder();

        text.Append("Ratios to ").Append(oracle).AppendLine(": geometric mean across assets (min–max); lower is faster.").AppendLine();
        text.Append("| Closing | Assets |");
        foreach (string column in columns)
            text.Append(' ').Append(column).Append(" |");
        text.AppendLine().Append("| --- | --- |");
        foreach (string _ in columns)
            text.Append(" ---: |");
        text.AppendLine();
        foreach (PostcardClosing closing in Closings)
        {
            PostcardSummary scored = summary.First(s => s.Closing == closing && s.Column == oracle);
            text.Append("| ").Append(shape.Label(closing)).Append(" | ")
                .Append(string.Create(CultureInfo.InvariantCulture, $"{scored.Assets}"))
                .Append(scored.FailedExcluded == 0 ? "" : string.Create(CultureInfo.InvariantCulture, $" ({scored.FailedExcluded} failed, excluded)"))
                .Append(" |");
            foreach (string column in columns)
            {
                PostcardSummary entry = summary.First(s => s.Closing == closing && s.Column == column);
                text.Append(entry.Assets == 0 ? " — |"
                    : column == oracle ? " 1.00× |"
                    : string.Create(CultureInfo.InvariantCulture, $" {entry.GeometricMean:0.00}× ({entry.Min:0.00}–{entry.Max:0.00}) |"));
            }

            text.AppendLine();
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
            foreach (PostcardClosing closing in Closings)
            {
                text.Append("| ").Append(name).Append(" | ").Append(shape.Label(closing)).Append(" |");
                foreach (string column in columns)
                {
                    PostcardCell cell = byKey[(index, closing, column)];
                    text.Append(cell.WindowFailed ? " fail |" : string.Create(CultureInfo.InvariantCulture, $" {cell.Median:0.0} |"));
                }

                text.AppendLine();
            }
        }

        return text.ToString();
    }

    /// <summary>Every cell with its round medians, as tab-separated values; a failed strict window reads <c>fail</c>.</summary>
    public static void WriteTsv(IReadOnlyList<PostcardCell> cells, TextWriter writer)
    {
        writer.WriteLine("asset\tclosing\tcolumn\tmedian_us\tround_medians_us");
        foreach (PostcardCell cell in cells)
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
        PostcardColumn<TAsset, TRow> column,
        PostcardClosing closing,
        TAsset asset,
        PostcardTiming timing)
    {
        for (int i = 0; i < timing.Warmup; i++)
            Keep(column.Answer(closing, asset));

        var samples = new List<double>();
        long budget = Stopwatch.Frequency * timing.BudgetMilliseconds / 1000;
        long started = Stopwatch.GetTimestamp();
        while (samples.Count < timing.MaxSamples
            && (samples.Count < timing.MinSamples || Stopwatch.GetTimestamp() - started < budget))
        {
            long start = Stopwatch.GetTimestamp();
            Keep(column.Answer(closing, asset));
            samples.Add(Stopwatch.GetElapsedTime(start).TotalMicroseconds);
        }

        return MedianOf(samples);
    }

    static long s_sink;

    // Consumes every answer without allocating, so no column's work is dead.
    static void Keep<TRow>(PostcardAnswer<TRow> answer) =>
        Volatile.Write(
            ref s_sink,
            s_sink + (answer.Count ?? 0) + (answer.Exists == true ? 1 : 0) + (answer.Rows?.Count ?? 0));
}
