using System.Buffers;
using System.Diagnostics;
using System.Globalization;

using InertText;

namespace MetadataNameRangePilot;

internal enum PilotScenario
{
    Build,
    MatchOnly,
    SparseDisplay,
    DisplayAll,
    Utf8OutputAll,
    BuildAndDisplayAll,
    BuildAndUtf8OutputAll,
}

internal sealed class PilotPayload(
    NameStore store,
    byte[] query,
    InertString containedQuery,
    InertUtf8String containedUtf8Query,
    int expectedMatches,
    PilotScenario scenario)
{
    readonly NameStore _store = store;
    readonly byte[] _query = query;
    readonly InertString _containedQuery = containedQuery;
    readonly InertUtf8String _containedUtf8Query =
        containedUtf8Query;
    readonly int _expectedMatches = expectedMatches;
    readonly PilotScenario _scenario = scenario;
    DisplayedName[]? _display;
    ArrayBufferWriter<byte>? _utf8Output;

    internal int Consume() =>
        _scenario switch
        {
            PilotScenario.Build =>
                HashCode.Combine(
                    _store.Count,
                    _store.SourceBytes),
            PilotScenario.MatchOnly =>
                _store.CountMatches(
                    _query,
                    _containedQuery,
                    _containedUtf8Query),
            PilotScenario.SparseDisplay =>
                HashDisplay(
                    _display = _store.SelectAndProject(
                        _query,
                        _containedQuery,
                        _containedUtf8Query,
                        _expectedMatches)),
            PilotScenario.DisplayAll =>
                HashDisplay(
                    _display = _store.ProjectAll()),
            PilotScenario.Utf8OutputAll =>
                HashUtf8(
                    _utf8Output = _store.WriteAllUtf8()),
            PilotScenario.BuildAndDisplayAll =>
                HashDisplay(
                    _display = _store.ProjectAll()),
            PilotScenario.BuildAndUtf8OutputAll =>
                HashUtf8(
                    _utf8Output = _store.WriteAllUtf8()),
            _ => throw new ArgumentOutOfRangeException(),
        };

    static int HashDisplay(
        ReadOnlySpan<DisplayedName> values)
    {
        var hash = new HashCode();
        foreach (DisplayedName value in values)
        {
            hash.Add(value.Role);
            hash.Add(value.Value);
        }
        return hash.ToHashCode();
    }

    static int HashUtf8(ArrayBufferWriter<byte> output)
    {
        var hash = new HashCode();
        hash.AddBytes(output.WrittenSpan);
        return hash.ToHashCode();
    }
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
    long Utf8Bytes,
    int QueryMatches,
    IReadOnlyList<PilotSample> Samples);

internal static class NamePilot
{
    static readonly PilotCell[] s_cells =
    [
        .. Enum.GetValues<PilotScenario>()
            .SelectMany(scenario =>
                Enum.GetValues<NameStrategy>()
                    .Select(strategy =>
                        new PilotCell(
                            scenario,
                            strategy))),
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
                            + $"{(long)asset.Utf8Bytes * copies}\t"
                            + $"{asset.QueryMatches * copies}\t"
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
            asset.NameCount * copies,
            (long)asset.Utf8Bytes * copies,
            asset.QueryMatches * copies,
            collected);
    }

    internal static string Report(PilotResult result)
    {
        var writer = new StringWriter(
            CultureInfo.InvariantCulture);
        writer.WriteLine(
            $"# {result.Asset}: copies={result.Copies}, "
            + $"names={result.Names}, utf8-bytes={result.Utf8Bytes}, "
            + $"query-matches={result.QueryMatches}");
        writer.WriteLine(
            "ABS\tasset\tscenario\tstrategy\tmetric\tmedian\tp95");
        foreach (PilotScenario scenario
            in Enum.GetValues<PilotScenario>())
        {
            foreach (NameStrategy strategy
                in Enum.GetValues<NameStrategy>())
            {
                PilotSample[] scenarioSamples =
                    [.. result.Samples.Where(
                        sample => sample.Scenario == scenario
                            && sample.Strategy == strategy)];
                WriteAbsolute(
                    writer,
                    result.Asset,
                    scenario,
                    strategy,
                    "elapsed_us",
                    scenarioSamples.Select(
                        sample => sample.ElapsedMicroseconds));
                WriteAbsolute(
                    writer,
                    result.Asset,
                    scenario,
                    strategy,
                    "allocated_b",
                    scenarioSamples.Select(
                        sample => (double)sample.AllocatedBytes));
                WriteAbsolute(
                    writer,
                    result.Asset,
                    scenario,
                    strategy,
                    "retained_b",
                    scenarioSamples.Select(
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
            foreach (NameStrategy strategy
                in Enum.GetValues<NameStrategy>())
            {
                if (strategy == NameStrategy.EagerInert)
                    continue;
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
            IncludesBuild(cell.Scenario)
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

    static bool IncludesBuild(PilotScenario scenario) =>
        scenario is
            PilotScenario.Build
            or PilotScenario.BuildAndDisplayAll
            or PilotScenario.BuildAndUtf8OutputAll;

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
        int copies) =>
        new(
            NameStore.Build(
                asset,
                cell.Strategy,
                copies),
            asset.QueryBytes,
            asset.ContainedQuery,
            asset.ContainedUtf8Query,
            checked(asset.QueryMatches * copies),
            cell.Scenario);

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
