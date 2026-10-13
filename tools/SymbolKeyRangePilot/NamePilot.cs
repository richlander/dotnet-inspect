using System.Diagnostics;
using System.Globalization;

namespace SymbolKeyRangePilot;

internal enum PilotScenario
{
    Build,
    KeyMatch,
    ValueTextEquals,
}

internal sealed class PilotPayload(
    KeyStore left,
    KeyStore? right,
    byte[] query,
    PilotScenario scenario)
{
    readonly KeyStore _left = left;
    readonly KeyStore? _right = right;
    readonly byte[] _query = query;
    readonly PilotScenario _scenario = scenario;

    internal int Consume() =>
        _scenario switch
        {
            PilotScenario.Build =>
                _left.KeyHash(),
            PilotScenario.KeyMatch =>
                _left.Matches(_right!)
                    ? _left.KeyHash() ^ _right!.KeyHash()
                    : throw new InvalidOperationException(
                        "Equal pilot key populations did not match."),
            PilotScenario.ValueTextEquals =>
                _left.ValueTextEquals(_query),
            _ => throw new ArgumentOutOfRangeException(),
        };
}

internal readonly record struct PilotCell(
    PilotScenario Scenario,
    KeyStrategy Strategy);

internal readonly record struct PilotSample(
    int Round,
    int Order,
    PilotScenario Scenario,
    KeyStrategy Strategy,
    double ElapsedMicroseconds,
    long AllocatedBytes,
    long RetainedBytes,
    int Checksum);

internal sealed record PilotResult(
    string Asset,
    int Copies,
    int Keys,
    int KeyBytes,
    IReadOnlyList<PilotSample> Samples);

internal static class NamePilot
{
    static readonly PilotCell[] s_cells =
    [
        new(
            PilotScenario.Build,
            KeyStrategy.PrivatePerKey),
        new(
            PilotScenario.Build,
            KeyStrategy.OpaqueRangePopulation),
        new(
            PilotScenario.KeyMatch,
            KeyStrategy.PrivatePerKey),
        new(
            PilotScenario.KeyMatch,
            KeyStrategy.OpaqueRangePopulation),
        new(
            PilotScenario.ValueTextEquals,
            KeyStrategy.PrivatePerKey),
        new(
            PilotScenario.ValueTextEquals,
            KeyStrategy.OpaqueRangePopulation),
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
                            + $"{asset.KeyCount * copies}\t"
                            + $"{KeyFormat.Measure(asset, copies)}\t"
                            + $"{sample.ElapsedMicroseconds:F3}\t"
                            + $"{sample.AllocatedBytes}\t"
                            + $"{sample.RetainedBytes}\t"
                            + $"{sample.Checksum}"));
                }
            }
            samples.Flush();
        }

        ValidateChecksums(asset.Name, collected);
        return new(
            asset.Name,
            copies,
            asset.KeyCount * copies,
            KeyFormat.Measure(asset, copies),
            collected);
    }

    internal static string Report(PilotResult result)
    {
        var writer = new StringWriter(
            CultureInfo.InvariantCulture);
        writer.WriteLine(
            $"# {result.Asset}: copies={result.Copies}, "
            + $"keys={result.Keys}, key-bytes={result.KeyBytes}");
        writer.WriteLine(
            "ABS\tasset\tscenario\tstrategy\tmetric\tmedian\tp95");
        foreach (PilotScenario scenario
            in Enum.GetValues<PilotScenario>())
        {
            foreach (KeyStrategy strategy
                in Enum.GetValues<KeyStrategy>())
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
            WriteSpc(
                writer,
                result,
                scenario,
                KeyStrategy.OpaqueRangePopulation,
                "elapsed_us",
                sample => sample.ElapsedMicroseconds);
            WriteSpc(
                writer,
                result,
                scenario,
                KeyStrategy.OpaqueRangePopulation,
                "allocated_b",
                sample => sample.AllocatedBytes);
            WriteSpc(
                writer,
                result,
                scenario,
                KeyStrategy.OpaqueRangePopulation,
                "retained_b",
                sample => sample.RetainedBytes);
        }
        return writer.ToString();
    }

    static void Warm(
        NameAsset asset,
        PilotCell cell,
        int copies)
    {
        PilotPayload payload = BuildPayload(
            asset,
            cell,
            copies);
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
        PilotPayload? prepared =
            cell.Scenario == PilotScenario.Build
                ? null
                : BuildPayload(asset, cell, copies);
        long allocatedBefore =
            GC.GetAllocatedBytesForCurrentThread();
        long started = Stopwatch.GetTimestamp();
        PilotPayload payload =
            prepared
            ?? BuildPayload(asset, cell, copies);
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
        PilotPayload payload = BuildPayload(
            asset,
            cell,
            copies);
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
        KeyStore left = KeyStore.Build(
            asset,
            cell.Strategy,
            copies);
        KeyStore? right =
            cell.Scenario == PilotScenario.KeyMatch
                ? KeyStore.Build(
                    asset,
                    cell.Strategy,
                    copies)
                : null;
        return new(
            left,
            right,
            asset.QueryBytes,
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
        return GC.GetTotalMemory(
            forceFullCollection: false);
    }

    static void ValidateChecksums(
        string asset,
        IReadOnlyList<PilotSample> samples)
    {
        foreach (PilotScenario scenario
            in Enum.GetValues<PilotScenario>())
        {
            foreach (int round in samples
                .Where(sample => sample.Scenario == scenario)
                .Select(sample => sample.Round)
                .Distinct())
            {
                int[] checksums =
                    [.. samples
                        .Where(sample =>
                            sample.Scenario == scenario
                            && sample.Round == round)
                        .Select(sample => sample.Checksum)
                        .Distinct()];
                if (checksums.Length != 1)
                {
                    throw new InvalidOperationException(
                        $"{asset} {scenario} round {round} "
                        + "produced different answers.");
                }
            }
        }
    }

    static void WriteAbsolute(
        TextWriter writer,
        string asset,
        PilotScenario scenario,
        KeyStrategy strategy,
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
        KeyStrategy strategy,
        string metric,
        Func<PilotSample, double> selector)
    {
        PilotSample[] baseline =
            [.. result.Samples
                .Where(sample =>
                    sample.Scenario == scenario
                    && sample.Strategy
                        == KeyStrategy.PrivatePerKey)
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

    static double Percentile(
        double[] ordered,
        double percentile)
    {
        if (ordered.Length == 0)
            throw new InvalidOperationException("No samples.");
        int index = Math.Clamp(
            (int)Math.Ceiling(
                ordered.Length * percentile)
                - 1,
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
    internal static SpcSummary Calculate(
        double[] ratios)
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
        {
            movingRange += Math.Abs(
                logs[index] - logs[index - 1]);
        }
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
