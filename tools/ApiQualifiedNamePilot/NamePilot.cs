using System.Diagnostics;
using System.Globalization;

using InertText;

namespace ApiQualifiedNamePilot;

internal enum PilotScenario
{
    Match,
    DisplayTransient,
    DisplayRetained,
}

internal sealed class PilotPayload(
    NameStore left,
    NameStore? right,
    InertString[]? display,
    PilotScenario scenario)
{
    readonly NameStore _left = left;
    readonly NameStore? _right = right;
    readonly InertString[]? _display = display;
    readonly PilotScenario _scenario = scenario;

    internal int Consume() =>
        _scenario switch
        {
            PilotScenario.Match =>
                _left.Matches(_right!)
                    ? _left.MatchHash() ^ _right!.MatchHash()
                    : throw new InvalidOperationException(
                        "Equal pilot inputs did not match."),
            PilotScenario.DisplayTransient =>
                _left.ProjectAndHash(),
            PilotScenario.DisplayRetained =>
                _left.DisplayHash(_display),
            _ => throw new ArgumentOutOfRangeException(),
        };
}

internal readonly record struct PilotCell(
    PilotScenario Scenario,
    NameStrategy Strategy);

internal readonly record struct PilotSample(
    int Round,
    int Order,
    PilotScenario Scenario,
    NameStrategy Strategy,
    double ElapsedMicroseconds,
    long AllocatedBytes,
    long RetainedBytes,
    int Checksum);

internal sealed record PilotResult(
    string Asset,
    int Copies,
    int Names,
    int Utf8Bytes,
    IReadOnlyList<PilotSample> Samples);

internal static class NamePilot
{
    static readonly PilotCell[] s_cells =
    [
        new(PilotScenario.Match, NameStrategy.EagerInert),
        new(PilotScenario.Match, NameStrategy.Utf8PerName),
        new(PilotScenario.Match, NameStrategy.Utf8Arena),
        new(PilotScenario.DisplayTransient, NameStrategy.EagerInert),
        new(PilotScenario.DisplayTransient, NameStrategy.Utf8PerName),
        new(PilotScenario.DisplayTransient, NameStrategy.Utf8Arena),
        new(PilotScenario.DisplayRetained, NameStrategy.EagerInert),
        new(PilotScenario.DisplayRetained, NameStrategy.Utf8PerName),
        new(PilotScenario.DisplayRetained, NameStrategy.Utf8Arena),
    ];

    static int s_checksum;

    internal static PilotResult Measure(
        NameAsset asset,
        int rounds,
        int copies,
        TextWriter samples)
    {
        foreach (PilotCell cell in s_cells)
            Warm(asset, cell, copies);

        var collected = new List<PilotSample>(
            checked(rounds * s_cells.Length));
        for (int round = 0; round < rounds; round++)
        {
            var partial = new PilotSample[s_cells.Length];
            for (int order = 0; order < s_cells.Length; order++)
            {
                PilotCell cell =
                    s_cells[(round + order) % s_cells.Length];
                partial[order] = MeasureTime(
                    asset,
                    cell,
                    copies,
                    round + 1,
                    order + 1);
            }
            for (int order = 0; order < s_cells.Length; order++)
            {
                PilotCell cell =
                    s_cells[(round + order) % s_cells.Length];
                long retained = MeasureRetained(
                    asset,
                    cell,
                    copies);
                partial[order] = partial[order] with
                {
                    RetainedBytes = retained,
                };
                PilotSample sample = partial[order];
                collected.Add(sample);
                if (samples != TextWriter.Null)
                {
                    samples.WriteLine(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"{asset.Name}\t{sample.Round}\t"
                            + $"{sample.Order}\t{sample.Scenario}\t"
                            + $"{sample.Strategy}\t{copies}\t"
                            + $"{asset.NameCount * copies}\t"
                            + $"{asset.Utf8Bytes * copies}\t"
                            + $"{sample.ElapsedMicroseconds:F3}\t"
                            + $"{sample.AllocatedBytes}\t"
                            + $"{sample.RetainedBytes}\t"
                            + $"{sample.Checksum}"));
                }
            }
            samples.Flush();
        }

        return new(
            asset.Name,
            copies,
            asset.NameCount * copies,
            asset.Utf8Bytes * copies,
            collected);
    }

    internal static string Report(PilotResult result)
    {
        var writer = new StringWriter(CultureInfo.InvariantCulture);
        writer.WriteLine(
            $"# {result.Asset}: copies={result.Copies}, "
            + $"names={result.Names}, utf8-bytes={result.Utf8Bytes}");
        writer.WriteLine(
            "ABS\tasset\tscenario\tstrategy\tmetric\tmedian\tp95");
        foreach (PilotScenario scenario
            in Enum.GetValues<PilotScenario>())
        {
            foreach (NameStrategy strategy
                in Enum.GetValues<NameStrategy>())
            {
                PilotSample[] samples =
                [.. result.Samples.Where(
                    sample => sample.Scenario == scenario
                        && sample.Strategy == strategy)];
                WriteAbsolute(
                    writer,
                    result.Asset,
                    scenario,
                    strategy,
                    "elapsed_us",
                    samples.Select(
                        sample => sample.ElapsedMicroseconds));
                WriteAbsolute(
                    writer,
                    result.Asset,
                    scenario,
                    strategy,
                    "allocated_b",
                    samples.Select(
                        sample => (double)sample.AllocatedBytes));
                WriteAbsolute(
                    writer,
                    result.Asset,
                    scenario,
                    strategy,
                    "retained_b",
                    samples.Select(
                        sample => (double)sample.RetainedBytes));
            }
        }

        writer.WriteLine(
            "SPC\tasset\tscenario\tstrategy\tmetric\t"
            + "median_ratio\tcenter_ratio\tlcl_ratio\tucl_ratio\t"
            + "outside\tassessment");
        foreach (PilotScenario scenario
            in Enum.GetValues<PilotScenario>())
        {
            foreach (NameStrategy strategy in new[]
            {
                NameStrategy.Utf8PerName,
                NameStrategy.Utf8Arena,
            })
            {
                WriteSpc(
                    writer,
                    result,
                    scenario,
                    strategy,
                    "elapsed_us",
                    sample => sample.ElapsedMicroseconds);
                WriteSpc(
                    writer,
                    result,
                    scenario,
                    strategy,
                    "allocated_b",
                    sample => sample.AllocatedBytes);
                WriteSpc(
                    writer,
                    result,
                    scenario,
                    strategy,
                    "retained_b",
                    sample => sample.RetainedBytes);
            }
        }
        return writer.ToString();
    }

    static void Warm(
        NameAsset asset,
        PilotCell cell,
        int copies)
    {
        PilotPayload payload = BuildPayload(asset, cell, copies);
        s_checksum ^= payload.Consume();
        GC.KeepAlive(payload);
    }

    static PilotSample MeasureTime(
        NameAsset asset,
        PilotCell cell,
        int copies,
        int round,
        int order)
    {
        long allocatedBefore =
            GC.GetAllocatedBytesForCurrentThread();
        long started = Stopwatch.GetTimestamp();
        PilotPayload payload = BuildPayload(asset, cell, copies);
        int checksum = payload.Consume();
        double elapsed =
            Stopwatch.GetElapsedTime(started).TotalMicroseconds;
        long allocated =
            GC.GetAllocatedBytesForCurrentThread()
            - allocatedBefore;
        s_checksum ^= checksum;
        GC.KeepAlive(payload);
        return new(
            round,
            order,
            cell.Scenario,
            cell.Strategy,
            elapsed,
            allocated,
            RetainedBytes: 0,
            checksum);
    }

    static long MeasureRetained(
        NameAsset asset,
        PilotCell cell,
        int copies)
    {
        long before = CollectManagedBytes();
        PilotPayload payload = BuildPayload(asset, cell, copies);
        s_checksum ^= payload.Consume();
        long after = CollectManagedBytes();
        GC.KeepAlive(payload);
        return after - before;
    }

    static PilotPayload BuildPayload(
        NameAsset asset,
        PilotCell cell,
        int copies)
    {
        NameStore left = NameStore.Build(
            asset,
            cell.Strategy,
            copies);
        if (cell.Scenario == PilotScenario.Match)
        {
            return new(
                left,
                NameStore.Build(
                    asset,
                    cell.Strategy,
                    copies),
                display: null,
                cell.Scenario);
        }
        InertString[]? display =
            cell.Scenario != PilotScenario.DisplayRetained
                || cell.Strategy == NameStrategy.EagerInert
                ? null
                : left.ProjectAll();
        return new(
            left,
            right: null,
            display,
            cell.Scenario);
    }

    static long CollectManagedBytes()
    {
        GC.Collect(
            GC.MaxGeneration,
            GCCollectionMode.Forced,
            blocking: true,
            compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(
            GC.MaxGeneration,
            GCCollectionMode.Forced,
            blocking: true,
            compacting: true);
        return GC.GetTotalMemory(forceFullCollection: false);
    }

    static void WriteAbsolute(
        TextWriter writer,
        string asset,
        PilotScenario scenario,
        NameStrategy strategy,
        string metric,
        IEnumerable<double> source)
    {
        double[] values = [.. source.Order()];
        writer.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"ABS\t{asset}\t{scenario}\t{strategy}\t{metric}\t"
                + $"{Percentile(values, 0.5):F3}\t"
                + $"{Percentile(values, 0.95):F3}"));
    }

    static void WriteSpc(
        TextWriter writer,
        PilotResult result,
        PilotScenario scenario,
        NameStrategy strategy,
        string metric,
        Func<PilotSample, double> selector)
    {
        PilotSample[] baseline =
            [.. result.Samples
                .Where(sample =>
                    sample.Scenario == scenario
                    && sample.Strategy
                        == NameStrategy.EagerInert)
                .OrderBy(sample => sample.Round)];
        PilotSample[] candidate =
            [.. result.Samples
                .Where(sample =>
                    sample.Scenario == scenario
                    && sample.Strategy == strategy)
                .OrderBy(sample => sample.Round)];
        var ratios = new double[baseline.Length];
        for (int index = 0; index < ratios.Length; index++)
        {
            double denominator = selector(baseline[index]);
            double numerator = selector(candidate[index]);
            if (denominator <= 0 || numerator <= 0)
            {
                writer.WriteLine(
                    $"SPC\t{result.Asset}\t{scenario}\t{strategy}\t"
                    + $"{metric}\tNA\tNA\tNA\tNA\tNA\t"
                    + "non-positive-sample");
                return;
            }
            ratios[index] = numerator / denominator;
        }

        SpcSummary summary = SpcSummary.Calculate(ratios);
        writer.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"SPC\t{result.Asset}\t{scenario}\t{strategy}\t"
                + $"{metric}\t{summary.MedianRatio:F4}\t"
                + $"{summary.CenterRatio:F4}\t"
                + $"{summary.LowerControlRatio:F4}\t"
                + $"{summary.UpperControlRatio:F4}\t"
                + $"{summary.OutsideControlLimits}\t"
                + $"{summary.Assessment}"));
    }

    static double Percentile(double[] ordered, double percentile)
    {
        if (ordered.Length == 0)
            throw new InvalidOperationException("No samples.");
        int index = Math.Clamp(
            (int)Math.Ceiling(ordered.Length * percentile) - 1,
            0,
            ordered.Length - 1);
        return ordered[index];
    }
}

internal readonly record struct SpcSummary(
    double MedianRatio,
    double CenterRatio,
    double LowerControlRatio,
    double UpperControlRatio,
    int OutsideControlLimits,
    string Assessment)
{
    internal static SpcSummary Calculate(double[] ratios)
    {
        if (ratios.Length < 2)
        {
            throw new InvalidOperationException(
                "SPC requires at least two paired samples.");
        }
        var logs = new double[ratios.Length];
        for (int index = 0; index < ratios.Length; index++)
            logs[index] = Math.Log(ratios[index]);
        double center = logs.Average();
        double movingRange = 0;
        for (int index = 1; index < logs.Length; index++)
            movingRange += Math.Abs(logs[index] - logs[index - 1]);
        movingRange /= logs.Length - 1;
        double sigma = movingRange / 1.128;
        double lower = center - (3 * sigma);
        double upper = center + (3 * sigma);
        double tolerance = Math.Max(
            1e-12,
            Math.Max(
                Math.Abs(lower),
                Math.Abs(upper)) * 1e-12);
        int outside = logs.Count(
            value => value < lower - tolerance
                || value > upper + tolerance);
        string direction = upper < -tolerance
            ? "win"
            : lower > tolerance
                ? "loss"
                : "inconclusive";
        string stability = outside == 0
            ? "in-control"
            : "special-cause";
        double[] ordered = [.. ratios.Order()];
        return new(
            ordered[(ordered.Length - 1) / 2],
            Math.Exp(center),
            Math.Exp(lower),
            Math.Exp(upper),
            outside,
            $"{stability}-{direction}");
    }
}
