using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MetadataNameRangePilot;

internal enum SlotLatencyStrategy
{
    RetainedRange,
    CheckedSlot,
    TrustedSlot,
}

internal readonly record struct SlotLatencySample(
    int Round,
    int Order,
    SlotLatencyStrategy Strategy,
    double ElapsedMicroseconds,
    long AllocatedBytes,
    int Checksum);

internal sealed record SlotLatencyResult(
    string Asset,
    int Copies,
    int Names,
    long Utf8Bytes,
    int QueryMatches,
    int RangeBytes,
    int SlotBytes,
    IReadOnlyList<SlotLatencySample> Samples);

internal readonly struct PilotRetainedRange(
    byte[] bytes,
    int offset,
    int length)
{
    readonly byte[] _bytes = bytes;
    readonly int _offset = offset;
    readonly int _length = length;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool ValueTextEquals(ReadOnlySpan<byte> query) =>
        _bytes.AsSpan(_offset, _length).SequenceEqual(query);
}

internal readonly record struct PilotSlot(
    int Offset,
    int Length);

internal sealed class PilotSlotPopulation(byte[] bytes)
{
    readonly byte[] _bytes = bytes;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool ValueTextEquals(
        PilotSlot slot,
        ReadOnlySpan<byte> query) =>
        _bytes.AsSpan(slot.Offset, slot.Length)
            .SequenceEqual(query);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool ValueTextEqualsTrusted(
        PilotSlot slot,
        ReadOnlySpan<byte> query)
    {
        ref byte start = ref Unsafe.Add(
            ref MemoryMarshal.GetArrayDataReference(_bytes),
            (nuint)(uint)slot.Offset);
        return MemoryMarshal.CreateReadOnlySpan(
                ref start,
                slot.Length)
            .SequenceEqual(query);
    }
}

internal sealed class SlotLatencyStore
{
    readonly PilotRetainedRange[] _ranges;
    readonly PilotSlotPopulation _population;
    readonly PilotSlot[] _slots;

    SlotLatencyStore(
        PilotRetainedRange[] ranges,
        PilotSlotPopulation population,
        PilotSlot[] slots)
    {
        _ranges = ranges;
        _population = population;
        _slots = slots;
    }

    internal static SlotLatencyStore Build(
        NameAsset asset,
        int copies)
    {
        int count = checked(asset.NameCount * copies);
        int byteCount = checked(asset.Utf8Bytes * copies);
        byte[] bytes = GC.AllocateUninitializedArray<byte>(
            byteCount);
        var ranges = new PilotRetainedRange[count];
        var slots = new PilotSlot[count];
        int index = 0;
        int offset = 0;
        for (int copy = 0; copy < copies; copy++)
        {
            foreach (NameEntry entry in asset.Entries)
            {
                int length = asset.GetUtf8Length(entry.Handle);
                asset.CopyUtf8(
                    entry.Handle,
                    bytes.AsSpan(offset, length));
                ranges[index] = new(bytes, offset, length);
                slots[index] = new(offset, length);
                offset += length;
                index++;
            }
        }
        if (offset != bytes.Length)
        {
            throw new InvalidOperationException(
                "The slot pilot did not fill its byte population.");
        }
        return new(
            ranges,
            new PilotSlotPopulation(bytes),
            slots);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal int CountRetainedRangeMatches(
        ReadOnlySpan<byte> query)
    {
        int matches = 0;
        foreach (PilotRetainedRange range in _ranges)
        {
            if (range.ValueTextEquals(query))
                matches++;
        }
        return matches;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal int CountCheckedSlotMatches(
        ReadOnlySpan<byte> query)
    {
        int matches = 0;
        foreach (PilotSlot slot in _slots)
        {
            if (_population.ValueTextEquals(slot, query))
                matches++;
        }
        return matches;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal int CountTrustedSlotMatches(
        ReadOnlySpan<byte> query)
    {
        int matches = 0;
        foreach (PilotSlot slot in _slots)
        {
            if (_population.ValueTextEqualsTrusted(
                    slot,
                    query))
            {
                matches++;
            }
        }
        return matches;
    }

    internal int CountMatches(
        SlotLatencyStrategy strategy,
        ReadOnlySpan<byte> query) =>
        strategy switch
        {
            SlotLatencyStrategy.RetainedRange =>
                CountRetainedRangeMatches(query),
            SlotLatencyStrategy.CheckedSlot =>
                CountCheckedSlotMatches(query),
            SlotLatencyStrategy.TrustedSlot =>
                CountTrustedSlotMatches(query),
            _ => throw new ArgumentOutOfRangeException(
                nameof(strategy)),
        };
}

internal static class SlotLatencyPilot
{
    static int s_checksum;

    internal static SlotLatencyResult Measure(
        NameAsset asset,
        int rounds,
        int copies,
        TextWriter samples)
    {
        int rangeBytes = Unsafe.SizeOf<PilotRetainedRange>();
        int slotBytes = Unsafe.SizeOf<PilotSlot>();
        if (rangeBytes != 16 || slotBytes != 8)
        {
            throw new InvalidOperationException(
                $"Expected 16-byte ranges and 8-byte slots; "
                + $"observed {rangeBytes} and {slotBytes}.");
        }

        SlotLatencyStore store =
            SlotLatencyStore.Build(asset, copies);
        foreach (SlotLatencyStrategy strategy
            in Enum.GetValues<SlotLatencyStrategy>())
        {
            int checksum = store.CountMatches(
                strategy,
                asset.Query);
            if (checksum != asset.QueryMatches * copies)
            {
                throw new InvalidOperationException(
                    $"{strategy} produced {checksum} matches; "
                    + $"expected {asset.QueryMatches * copies}.");
            }
            s_checksum ^= checksum;
        }

        var collected = new List<SlotLatencySample>(
            checked(
                rounds
                * Enum.GetValues<SlotLatencyStrategy>().Length));
        for (int round = 0; round < rounds; round++)
        {
            SlotLatencyStrategy[] strategies =
                Enum.GetValues<SlotLatencyStrategy>();
            for (int order = 0; order < strategies.Length; order++)
            {
                SlotLatencyStrategy strategy =
                    strategies[(round + order) % strategies.Length];
                long allocatedBefore =
                    GC.GetAllocatedBytesForCurrentThread();
                long started = Stopwatch.GetTimestamp();
                int checksum = store.CountMatches(
                    strategy,
                    asset.Query);
                double elapsed =
                    Stopwatch.GetElapsedTime(started)
                        .TotalMicroseconds;
                long allocated =
                    GC.GetAllocatedBytesForCurrentThread()
                    - allocatedBefore;
                s_checksum ^= checksum;
                var sample = new SlotLatencySample(
                    round + 1,
                    order + 1,
                    strategy,
                    elapsed,
                    allocated,
                    checksum);
                collected.Add(sample);
                if (samples != TextWriter.Null)
                {
                    samples.WriteLine(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"{asset.Name}\t{sample.Round}\t"
                            + $"{sample.Order}\t{sample.Strategy}\t"
                            + $"{copies}\t{asset.NameCount * copies}\t"
                            + $"{(long)asset.Utf8Bytes * copies}\t"
                            + $"{asset.QueryMatches * copies}\t"
                            + $"{sample.ElapsedMicroseconds:F3}\t"
                            + $"{sample.AllocatedBytes}\t"
                            + $"{sample.Checksum}"));
                }
            }
            samples.Flush();
        }
        GC.KeepAlive(store);

        return new(
            asset.Name,
            copies,
            asset.NameCount * copies,
            (long)asset.Utf8Bytes * copies,
            asset.QueryMatches * copies,
            rangeBytes,
            slotBytes,
            collected);
    }

    internal static string Report(
        SlotLatencyResult result)
    {
        var writer = new StringWriter(
            CultureInfo.InvariantCulture);
        writer.WriteLine(
            $"# {result.Asset}: copies={result.Copies}, "
            + $"names={result.Names}, utf8-bytes={result.Utf8Bytes}, "
            + $"query-matches={result.QueryMatches}, "
            + $"range-bytes={result.RangeBytes}, "
            + $"slot-bytes={result.SlotBytes}");
        writer.WriteLine(
            "ABS\tasset\tstrategy\tmetric\tmedian\tp95");
        foreach (SlotLatencyStrategy strategy
            in Enum.GetValues<SlotLatencyStrategy>())
        {
            SlotLatencySample[] strategySamples =
                [.. result.Samples.Where(
                    sample => sample.Strategy == strategy)];
            WriteAbsolute(
                writer,
                result.Asset,
                strategy,
                "elapsed_us",
                strategySamples.Select(
                    sample => sample.ElapsedMicroseconds));
            WriteAbsolute(
                writer,
                result.Asset,
                strategy,
                "allocated_b",
                strategySamples.Select(
                    sample => (double)sample.AllocatedBytes));
        }

        writer.WriteLine(
            "SPC\tasset\tstrategy\tmetric\tmedian_ratio\t"
            + "center_ratio\tlcl_ratio\tucl_ratio\toutside\tassessment");
        foreach (SlotLatencyStrategy strategy
            in Enum.GetValues<SlotLatencyStrategy>())
        {
            if (strategy == SlotLatencyStrategy.RetainedRange)
                continue;
            WriteSpc(
                writer,
                result,
                strategy);
        }
        return writer.ToString();
    }

    static void WriteAbsolute(
        TextWriter writer,
        string asset,
        SlotLatencyStrategy strategy,
        string metric,
        IEnumerable<double> source)
    {
        double[] values = [.. source.Order()];
        writer.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"ABS\t{asset}\t{strategy}\t{metric}\t"
                + $"{Percentile(values, 0.5):F3}\t"
                + $"{Percentile(values, 0.95):F3}"));
    }

    static void WriteSpc(
        TextWriter writer,
        SlotLatencyResult result,
        SlotLatencyStrategy strategy)
    {
        SlotLatencySample[] baseline =
            [.. result.Samples
                .Where(sample =>
                    sample.Strategy
                        == SlotLatencyStrategy.RetainedRange)
                .OrderBy(sample => sample.Round)];
        SlotLatencySample[] candidate =
            [.. result.Samples
                .Where(sample =>
                    sample.Strategy == strategy)
                .OrderBy(sample => sample.Round)];
        var ratios = new double[baseline.Length];
        for (int index = 0; index < ratios.Length; index++)
        {
            ratios[index] =
                candidate[index].ElapsedMicroseconds
                / baseline[index].ElapsedMicroseconds;
        }

        SpcSummary summary = SpcSummary.Calculate(ratios);
        writer.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"SPC\t{result.Asset}\t{strategy}\telapsed_us\t"
                + $"{summary.MedianRatio:F4}\t"
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
