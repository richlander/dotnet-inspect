using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MetadataNameRangePilot;

internal enum IndexPatternStrategy
{
    RetainedRange,
    CheckedSlot,
    DenseOrdinal,
    ValidatedDenseOrdinal,
    ExplicitIndex,
    ValidatedExplicitIndex,
    InternedIndex,
    ValidatedInternedIndex,
    SourceStringHandle,
}

internal readonly record struct IndexPatternSample(
    int Round,
    int Order,
    IndexPatternStrategy Strategy,
    double ElapsedMicroseconds,
    long AllocatedBytes,
    int Checksum);

internal readonly record struct IndexPatternStorage(
    IndexPatternStrategy Strategy,
    long DescriptorBytes,
    long PopulationBytes,
    bool OwnsPopulation)
{
    internal long TotalBytes => checked(DescriptorBytes + PopulationBytes);
}

internal sealed record IndexPatternResult(
    string Asset,
    int Copies,
    int Names,
    long Utf8Bytes,
    int UniqueNames,
    long UniqueUtf8Bytes,
    int QueryMatches,
    int RangeBytes,
    int SlotBytes,
    int IndexBytes,
    IReadOnlyList<IndexPatternStorage> Storage,
    IReadOnlyList<IndexPatternSample> Samples);

internal readonly record struct PilotNameIndex(int Value);

internal sealed class PilotOffsetPopulation
{
    readonly byte[] _bytes;
    readonly int[] _offsets;

    internal PilotOffsetPopulation(
        byte[] bytes,
        int[] offsets)
    {
        if (offsets.Length == 0
            || offsets[0] != 0
            || offsets[^1] != bytes.Length)
        {
            throw new ArgumentException(
                "Offsets must exactly bound the byte population.",
                nameof(offsets));
        }
        for (int index = 1; index < offsets.Length; index++)
        {
            if (offsets[index] < offsets[index - 1])
            {
                throw new ArgumentException(
                    "Offsets must be monotonic.",
                    nameof(offsets));
            }
        }

        _bytes = bytes;
        _offsets = offsets;
    }

    internal int Count => _offsets.Length - 1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    bool ValueTextEqualsChecked(
        PilotNameIndex index,
        ReadOnlySpan<byte> query)
    {
        int value = index.Value;
        if ((uint)value >= (uint)Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        int start = _offsets[value];
        int end = _offsets[value + 1];
        return _bytes.AsSpan(start, end - start)
            .SequenceEqual(query);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal int CountDenseMatches(ReadOnlySpan<byte> query)
    {
        int matches = 0;
        for (int index = 0; index < Count; index++)
        {
            int start = _offsets[index];
            int end = _offsets[index + 1];
            if (_bytes.AsSpan(start, end - start)
                .SequenceEqual(query))
            {
                matches++;
            }
        }
        return matches;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal int CountValidatedDenseMatches(
        ReadOnlySpan<byte> query)
    {
        int matches = 0;
        for (int index = 0; index < Count; index++)
        {
            int start = _offsets[index];
            int end = _offsets[index + 1];
            if (ValueTextEqualsValidated(start, end, query))
                matches++;
        }
        return matches;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal int CountIndexedMatches(
        ReadOnlySpan<PilotNameIndex> indexes,
        ReadOnlySpan<byte> query)
    {
        int matches = 0;
        foreach (PilotNameIndex index in indexes)
        {
            if (ValueTextEqualsChecked(index, query))
                matches++;
        }
        return matches;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal int CountValidatedIndexedMatches(
        ReadOnlySpan<PilotNameIndex> indexes,
        ReadOnlySpan<byte> query)
    {
        int matches = 0;
        foreach (PilotNameIndex index in indexes)
        {
            int value = index.Value;
            if ((uint)value >= (uint)Count)
                throw new ArgumentOutOfRangeException(nameof(indexes));

            int start = _offsets[value];
            int end = _offsets[value + 1];
            if (ValueTextEqualsValidated(start, end, query))
                matches++;
        }
        return matches;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    bool ValueTextEqualsValidated(
        int start,
        int end,
        ReadOnlySpan<byte> query)
    {
        ref byte first = ref Unsafe.Add(
            ref MemoryMarshal.GetArrayDataReference(_bytes),
            (nuint)(uint)start);
        return MemoryMarshal.CreateReadOnlySpan(
                ref first,
                end - start)
            .SequenceEqual(query);
    }
}

internal sealed class IndexPatternStore
{
    readonly NameAsset _asset;
    readonly PilotRetainedRange[] _ranges;
    readonly PilotSlotPopulation _slotPopulation;
    readonly PilotSlot[] _slots;
    readonly PilotOffsetPopulation _densePopulation;
    readonly PilotNameIndex[] _explicitIndexes;
    readonly PilotOffsetPopulation _internedPopulation;
    readonly PilotNameIndex[] _internedIndexes;
    readonly StringHandle[] _sourceHandles;

    IndexPatternStore(
        NameAsset asset,
        PilotRetainedRange[] ranges,
        PilotSlotPopulation slotPopulation,
        PilotSlot[] slots,
        PilotOffsetPopulation densePopulation,
        PilotNameIndex[] explicitIndexes,
        PilotOffsetPopulation internedPopulation,
        PilotNameIndex[] internedIndexes,
        StringHandle[] sourceHandles,
        IReadOnlyList<IndexPatternStorage> storage,
        int uniqueNames,
        long uniqueUtf8Bytes)
    {
        _asset = asset;
        _ranges = ranges;
        _slotPopulation = slotPopulation;
        _slots = slots;
        _densePopulation = densePopulation;
        _explicitIndexes = explicitIndexes;
        _internedPopulation = internedPopulation;
        _internedIndexes = internedIndexes;
        _sourceHandles = sourceHandles;
        Storage = storage;
        UniqueNames = uniqueNames;
        UniqueUtf8Bytes = uniqueUtf8Bytes;
    }

    internal IReadOnlyList<IndexPatternStorage> Storage { get; }

    internal int UniqueNames { get; }

    internal long UniqueUtf8Bytes { get; }

    internal static IndexPatternStore Build(
        NameAsset asset,
        int copies)
    {
        int count = checked(asset.NameCount * copies);
        int byteCount = checked(asset.Utf8Bytes * copies);
        byte[] bytes = GC.AllocateUninitializedArray<byte>(byteCount);
        var offsets = new int[count + 1];
        var ranges = new PilotRetainedRange[count];
        var slots = new PilotSlot[count];
        var explicitIndexes = new PilotNameIndex[count];
        var sourceHandles = new StringHandle[count];
        int index = 0;
        int offset = 0;
        for (int copy = 0; copy < copies; copy++)
        {
            foreach (NameEntry entry in asset.Entries)
            {
                int length = asset.GetUtf8Length(entry.Handle);
                offsets[index] = offset;
                asset.CopyUtf8(
                    entry.Handle,
                    bytes.AsSpan(offset, length));
                ranges[index] = new(bytes, offset, length);
                slots[index] = new(offset, length);
                explicitIndexes[index] = new(index);
                sourceHandles[index] = entry.Handle;
                offset += length;
                index++;
            }
        }
        offsets[index] = offset;
        if (index != count || offset != byteCount)
        {
            throw new InvalidOperationException(
                "The index pilot did not fill its dense population.");
        }

        var internedBytes = new ArrayBufferWriter<byte>(byteCount);
        var internedOffsets = new List<int> { 0 };
        var internedIndexes = new PilotNameIndex[count];
        index = 0;
        for (int copy = 0; copy < copies; copy++)
        {
            var names = new Dictionary<string, PilotNameIndex>(
                StringComparer.Ordinal);
            foreach (NameEntry entry in asset.Entries)
            {
                string text = asset.ReadString(entry.Handle);
                if (!names.TryGetValue(text, out PilotNameIndex nameIndex))
                {
                    nameIndex = new(internedOffsets.Count - 1);
                    names.Add(text, nameIndex);
                    int length = asset.GetUtf8Length(entry.Handle);
                    Span<byte> destination = internedBytes.GetSpan(length);
                    asset.CopyUtf8(
                        entry.Handle,
                        destination[..length]);
                    internedBytes.Advance(length);
                    internedOffsets.Add(internedBytes.WrittenCount);
                }
                internedIndexes[index++] = nameIndex;
            }
        }
        if (index != count)
        {
            throw new InvalidOperationException(
                "The index pilot did not fill its interned handles.");
        }

        byte[] uniqueBytes = internedBytes.WrittenSpan.ToArray();
        int[] uniqueOffsets = [.. internedOffsets];
        int uniqueNames = uniqueOffsets.Length - 1;
        int rangeBytes = Unsafe.SizeOf<PilotRetainedRange>();
        int slotBytes = Unsafe.SizeOf<PilotSlot>();
        int indexBytes = Unsafe.SizeOf<PilotNameIndex>();
        int stringHandleBytes = Unsafe.SizeOf<StringHandle>();
        if (rangeBytes != 16
            || slotBytes != 8
            || indexBytes != 4
            || stringHandleBytes != 4)
        {
            throw new InvalidOperationException(
                $"Expected 16-byte ranges, 8-byte slots, and 4-byte indexes; "
                + $"observed {rangeBytes}, {slotBytes}, {indexBytes}, "
                + $"and {stringHandleBytes}.");
        }

        IndexPatternStorage[] storage =
        [
            new(
                IndexPatternStrategy.RetainedRange,
                checked((long)count * rangeBytes),
                byteCount,
                OwnsPopulation: true),
            new(
                IndexPatternStrategy.CheckedSlot,
                checked((long)count * slotBytes),
                byteCount,
                OwnsPopulation: true),
            new(
                IndexPatternStrategy.DenseOrdinal,
                checked((long)(count + 1) * sizeof(int)),
                byteCount,
                OwnsPopulation: true),
            new(
                IndexPatternStrategy.ValidatedDenseOrdinal,
                checked((long)(count + 1) * sizeof(int)),
                byteCount,
                OwnsPopulation: true),
            new(
                IndexPatternStrategy.ExplicitIndex,
                checked(
                    ((long)count * indexBytes)
                    + ((long)(count + 1) * sizeof(int))),
                byteCount,
                OwnsPopulation: true),
            new(
                IndexPatternStrategy.ValidatedExplicitIndex,
                checked(
                    ((long)count * indexBytes)
                    + ((long)(count + 1) * sizeof(int))),
                byteCount,
                OwnsPopulation: true),
            new(
                IndexPatternStrategy.InternedIndex,
                checked(
                    ((long)count * indexBytes)
                    + ((long)(uniqueNames + 1) * sizeof(int))),
                uniqueBytes.Length,
                OwnsPopulation: true),
            new(
                IndexPatternStrategy.ValidatedInternedIndex,
                checked(
                    ((long)count * indexBytes)
                    + ((long)(uniqueNames + 1) * sizeof(int))),
                uniqueBytes.Length,
                OwnsPopulation: true),
            new(
                IndexPatternStrategy.SourceStringHandle,
                checked((long)count * stringHandleBytes),
                PopulationBytes: 0,
                OwnsPopulation: false),
        ];

        return new(
            asset,
            ranges,
            new PilotSlotPopulation(bytes),
            slots,
            new PilotOffsetPopulation(bytes, offsets),
            explicitIndexes,
            new PilotOffsetPopulation(uniqueBytes, uniqueOffsets),
            internedIndexes,
            sourceHandles,
            storage,
            uniqueNames,
            uniqueBytes.Length);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    int CountRetainedRangeMatches(ReadOnlySpan<byte> query)
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
    int CountCheckedSlotMatches(ReadOnlySpan<byte> query)
    {
        int matches = 0;
        foreach (PilotSlot slot in _slots)
        {
            if (_slotPopulation.ValueTextEquals(slot, query))
                matches++;
        }
        return matches;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    int CountSourceStringHandleMatches(ReadOnlySpan<byte> query)
    {
        int matches = 0;
        foreach (StringHandle handle in _sourceHandles)
        {
            if (_asset.ValueTextEquals(handle, query))
                matches++;
        }
        return matches;
    }

    internal int CountMatches(
        IndexPatternStrategy strategy,
        ReadOnlySpan<byte> query) =>
        strategy switch
        {
            IndexPatternStrategy.RetainedRange =>
                CountRetainedRangeMatches(query),
            IndexPatternStrategy.CheckedSlot =>
                CountCheckedSlotMatches(query),
            IndexPatternStrategy.DenseOrdinal =>
                _densePopulation.CountDenseMatches(query),
            IndexPatternStrategy.ValidatedDenseOrdinal =>
                _densePopulation.CountValidatedDenseMatches(query),
            IndexPatternStrategy.ExplicitIndex =>
                _densePopulation.CountIndexedMatches(
                    _explicitIndexes,
                    query),
            IndexPatternStrategy.ValidatedExplicitIndex =>
                _densePopulation.CountValidatedIndexedMatches(
                    _explicitIndexes,
                    query),
            IndexPatternStrategy.InternedIndex =>
                _internedPopulation.CountIndexedMatches(
                    _internedIndexes,
                    query),
            IndexPatternStrategy.ValidatedInternedIndex =>
                _internedPopulation.CountValidatedIndexedMatches(
                    _internedIndexes,
                    query),
            IndexPatternStrategy.SourceStringHandle =>
                CountSourceStringHandleMatches(query),
            _ => throw new ArgumentOutOfRangeException(
                nameof(strategy)),
        };
}

internal static class IndexPatternPilot
{
    static int s_checksum;

    internal static IndexPatternResult Measure(
        NameAsset asset,
        int rounds,
        int copies,
        TextWriter samples)
    {
        IndexPatternStore store =
            IndexPatternStore.Build(asset, copies);
        foreach (IndexPatternStrategy strategy
            in Enum.GetValues<IndexPatternStrategy>())
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

        var collected = new List<IndexPatternSample>(
            checked(
                rounds
                * Enum.GetValues<IndexPatternStrategy>().Length));
        for (int round = 0; round < rounds; round++)
        {
            IndexPatternStrategy[] strategies =
                Enum.GetValues<IndexPatternStrategy>();
            for (int order = 0; order < strategies.Length; order++)
            {
                IndexPatternStrategy strategy =
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
                var sample = new IndexPatternSample(
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
            store.UniqueNames,
            store.UniqueUtf8Bytes,
            asset.QueryMatches * copies,
            Unsafe.SizeOf<PilotRetainedRange>(),
            Unsafe.SizeOf<PilotSlot>(),
            Unsafe.SizeOf<PilotNameIndex>(),
            store.Storage,
            collected);
    }

    internal static string Report(IndexPatternResult result)
    {
        var writer = new StringWriter(
            CultureInfo.InvariantCulture);
        writer.WriteLine(
            $"# {result.Asset}: copies={result.Copies}, "
            + $"names={result.Names}, utf8-bytes={result.Utf8Bytes}, "
            + $"unique-names={result.UniqueNames}, "
            + $"unique-utf8-bytes={result.UniqueUtf8Bytes}, "
            + $"query-matches={result.QueryMatches}, "
            + $"range-bytes={result.RangeBytes}, "
            + $"slot-bytes={result.SlotBytes}, "
            + $"index-bytes={result.IndexBytes}");
        writer.WriteLine(
            "PAYLOAD\tasset\tstrategy\tdescriptor_b\tpopulation_b\t"
            + "total_b\tbytes_per_name\trange_ratio\tpopulation");
        long rangeTotal = result.Storage.Single(
            value =>
                value.Strategy == IndexPatternStrategy.RetainedRange)
            .TotalBytes;
        foreach (IndexPatternStorage storage in result.Storage)
        {
            string rangeRatio = storage.OwnsPopulation
                ? ((double)storage.TotalBytes / rangeTotal)
                    .ToString("F4", CultureInfo.InvariantCulture)
                : "n/a";
            writer.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"PAYLOAD\t{result.Asset}\t{storage.Strategy}\t"
                    + $"{storage.DescriptorBytes}\t"
                    + $"{storage.PopulationBytes}\t"
                    + $"{storage.TotalBytes}\t"
                    + $"{(double)storage.TotalBytes / result.Names:F3}\t"
                    + $"{rangeRatio}\t"
                    + $"{(storage.OwnsPopulation ? "owned" : "borrowed")}"));
        }

        writer.WriteLine(
            "ABS\tasset\tstrategy\tmetric\tmedian\tp95");
        foreach (IndexPatternStrategy strategy
            in Enum.GetValues<IndexPatternStrategy>())
        {
            IndexPatternSample[] strategySamples =
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
        foreach (IndexPatternStrategy strategy
            in Enum.GetValues<IndexPatternStrategy>())
        {
            if (strategy == IndexPatternStrategy.RetainedRange)
                continue;
            WriteSpc(writer, result, strategy);
        }
        return writer.ToString();
    }

    static void WriteAbsolute(
        TextWriter writer,
        string asset,
        IndexPatternStrategy strategy,
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
        IndexPatternResult result,
        IndexPatternStrategy strategy)
    {
        IndexPatternSample[] baseline =
            [.. result.Samples
                .Where(sample =>
                    sample.Strategy
                        == IndexPatternStrategy.RetainedRange)
                .OrderBy(sample => sample.Round)];
        IndexPatternSample[] candidate =
            [.. result.Samples
                .Where(sample => sample.Strategy == strategy)
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
