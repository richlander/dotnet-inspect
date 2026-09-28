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

/// <summary>A pinned real asset the postcard runs over.</summary>
public sealed record PostcardAsset<TAsset>(string Name, TAsset Asset);

/// <summary>A column whose answer differs from the oracle's.</summary>
public sealed record PostcardMismatch(string Asset, PostcardClosing Closing, string Column, string Answer, string OracleAnswer);

/// <summary>How a postcard times each cell.</summary>
public sealed record PostcardTiming(
    int Rounds = 6,
    int Warmup = 5,
    int BudgetMilliseconds = 2000,
    int MinSamples = 20,
    int MaxSamples = 5000);

/// <summary>One timed cell: the median of each round's median, in microseconds.</summary>
public sealed record PostcardCell(string Asset, PostcardClosing Closing, string Column, IReadOnlyList<double> RoundMedians)
{
    public double Median => Postcard.MedianOf(RoundMedians);
}

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
    /// Describes an answer for comparison: the Boolean, the count, the row
    /// count with an FNV-1a hash of the row text, or <c>fail</c>.
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

    /// <summary>Every place a column's answer differs from the oracle's.</summary>
    public static IReadOnlyList<PostcardMismatch> Check<TAsset, TRow>(
        IReadOnlyList<PostcardAsset<TAsset>> assets,
        PostcardColumn<TAsset, TRow> oracle,
        IReadOnlyList<PostcardColumn<TAsset, TRow>> columns,
        Func<TRow, string> rowText)
    {
        var mismatches = new List<PostcardMismatch>();
        foreach (PostcardAsset<TAsset> asset in assets)
        {
            foreach (PostcardClosing closing in Closings)
            {
                string expected = Describe(oracle.Answer(closing, asset.Asset), rowText);
                foreach (PostcardColumn<TAsset, TRow> column in columns)
                {
                    if (ReferenceEquals(column, oracle))
                        continue;
                    string actual = Describe(column.Answer(closing, asset.Asset), rowText);
                    if (actual != expected)
                        mismatches.Add(new(asset.Name, closing, column.Name, actual, expected));
                }
            }
        }

        return mismatches;
    }

    /// <summary>
    /// Times every column on every closing and asset. Each round visits the
    /// columns in a rotated order; a cell is the median of its round medians.
    /// </summary>
    public static IReadOnlyList<PostcardCell> Measure<TAsset, TRow>(
        IReadOnlyList<PostcardAsset<TAsset>> assets,
        IReadOnlyList<PostcardColumn<TAsset, TRow>> columns,
        PostcardTiming timing,
        Action<string>? progress = null)
    {
        var rounds = new Dictionary<(string, PostcardClosing, string), List<double>>();
        for (int round = 0; round < timing.Rounds; round++)
        {
            foreach (PostcardAsset<TAsset> asset in assets)
            {
                foreach (PostcardClosing closing in Closings)
                {
                    for (int i = 0; i < columns.Count; i++)
                    {
                        PostcardColumn<TAsset, TRow> column = columns[(i + round) % columns.Count];
                        double median = MedianMicroseconds(column, closing, asset.Asset, timing);
                        var key = (asset.Name, closing, column.Name);
                        if (!rounds.TryGetValue(key, out List<double>? medians))
                            rounds[key] = medians = [];
                        medians.Add(median);
                    }
                }

                progress?.Invoke(string.Create(CultureInfo.InvariantCulture, $"round {round + 1}/{timing.Rounds}: {asset.Name}"));
            }
        }

        var cells = new List<PostcardCell>();
        foreach (PostcardAsset<TAsset> asset in assets)
        {
            foreach (PostcardClosing closing in Closings)
            {
                foreach (PostcardColumn<TAsset, TRow> column in columns)
                    cells.Add(new(asset.Name, closing, column.Name, rounds[(asset.Name, closing, column.Name)]));
            }
        }

        return cells;
    }

    /// <summary>
    /// Per closing and column, the geometric mean across assets of the ratio
    /// to the oracle, with its minimum and maximum.
    /// </summary>
    public static IReadOnlyList<(PostcardClosing Closing, string Column, double GeometricMean, double Min, double Max)> Summarize(
        IReadOnlyList<PostcardCell> cells,
        string oracle)
    {
        var summary = new List<(PostcardClosing, string, double, double, double)>();
        var byKey = cells.ToDictionary(c => (c.Asset, c.Closing, c.Column));
        string[] columns = [.. cells.Select(c => c.Column).Distinct()];
        string[] assets = [.. cells.Select(c => c.Asset).Distinct()];
        foreach (PostcardClosing closing in Closings)
        {
            foreach (string column in columns)
            {
                double[] ratios = [.. assets.Select(a => byKey[(a, closing, column)].Median / byKey[(a, closing, oracle)].Median)];
                double geometricMean = Math.Exp(ratios.Average(Math.Log));
                summary.Add((closing, column, geometricMean, ratios.Min(), ratios.Max()));
            }
        }

        return summary;
    }

    /// <summary>The summary as a Markdown table: one row per closing, one column per postcard column.</summary>
    public static string SummaryTable(IReadOnlyList<PostcardCell> cells, string oracle)
    {
        var summary = Summarize(cells, oracle);
        string[] columns = [.. cells.Select(c => c.Column).Distinct()];
        var text = new StringBuilder();
        text.Append("| Closing |");
        foreach (string column in columns)
            text.Append(' ').Append(column).Append(" |");
        text.AppendLine().Append("| --- |");
        foreach (string _ in columns)
            text.Append(" ---: |");
        text.AppendLine();
        foreach (PostcardClosing closing in Closings)
        {
            text.Append("| ").Append(closing).Append(" |");
            foreach (string column in columns)
            {
                var entry = summary.First(s => s.Closing == closing && s.Column == column);
                text.Append(column == oracle
                    ? " 1.00× |"
                    : string.Create(CultureInfo.InvariantCulture, $" {entry.GeometricMean:0.00}× ({entry.Min:0.00}–{entry.Max:0.00}) |"));
            }

            text.AppendLine();
        }

        return text.ToString();
    }

    /// <summary>Every cell with its round medians, as tab-separated values.</summary>
    public static void WriteTsv(IReadOnlyList<PostcardCell> cells, TextWriter writer)
    {
        writer.WriteLine("asset\tclosing\tcolumn\tmedian_us\tround_medians_us");
        foreach (PostcardCell cell in cells)
        {
            writer.WriteLine(string.Create(
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
