using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Metadata;
using NLinq;

namespace DotnetInspector.PerformanceOracles;

public sealed record DeclaredMethodScorecardCheck(
    int Count,
    string AnswerHash,
    MetadataDeclaredMethodPopulationReceipt CountReceipt,
    MetadataDeclaredMethodPopulationReceipt RowsReceipt);

public sealed record DeclaredMethodScorecardCell(
    string Implementation,
    string Terminal,
    double Microseconds,
    long AllocatedBytes);

public sealed record DeclaredMethodScorecardResult(
    string TypeName,
    DeclaredMethodScorecardCheck Check,
    IReadOnlyList<DeclaredMethodScorecardCell> Cells);

public static class DeclaredMethodPopulation
{
    private const int OperationsPerSample = 256;

    public static DeclaredMethodScorecardCheck Check(
        string path,
        MetadataTypeDefinitionName type)
    {
        using var asset = Asset.Open(path, type);
        return Check(asset);
    }

    public static DeclaredMethodScorecardResult Measure(
        string path,
        MetadataTypeDefinitionName type)
    {
        using var asset = Asset.Open(path, type);
        DeclaredMethodScorecardCheck check = Check(asset);
        var cells = new List<DeclaredMethodScorecardCell>();
        DeclaredMethodScorecardCase[] cases =
            Enum.GetValues<DeclaredMethodScorecardCase>();
        foreach (DeclaredMethodScorecardCase measurementCase
            in cases)
        {
            for (int warmup = 0; warmup < 10; warmup++)
                Warmup(asset, measurementCase);
        }

        var times = cases.ToDictionary(
            static measurementCase => measurementCase,
            static _ => new List<double>());
        var allocations = cases.ToDictionary(
            static measurementCase => measurementCase,
            static _ => new List<long>());
        for (int round = 0; round < 8; round++)
        {
            for (int offset = 0; offset < cases.Length; offset++)
            {
                DeclaredMethodScorecardCase measurementCase =
                    cases[(round + offset) % cases.Length];
                Measurement measurement =
                    Measure(asset, measurementCase);
                times[measurementCase].Add(measurement.Microseconds);
                allocations[measurementCase].Add(
                    measurement.AllocatedBytes);
            }
        }
        foreach (DeclaredMethodScorecardCase measurementCase
            in cases)
        {
            cells.Add(
                new(
                    Implementation(measurementCase),
                    Terminal(measurementCase),
                    Median(times[measurementCase]),
                    Median(allocations[measurementCase])));
        }
        return new(DisplayName(type), check, cells);
    }

    public static string Report(DeclaredMethodScorecardResult result)
    {
        using var writer = new StringWriter(
            System.Globalization.CultureInfo.InvariantCulture);
        writer.WriteLine(
            $"# Metadata and NLinq kernels — {result.TypeName}");
        writer.WriteLine(
            $"# answer: {result.Check.Count:N0} declared MethodDefs, "
                + result.Check.AnswerHash);
        writer.WriteLine(
            "| Implementation | Terminal | Median | Allocated | "
                + "Time vs implementation Count | "
                + "Allocation vs implementation Count | "
                + "Time vs Metadata | Allocation vs Metadata |");
        writer.WriteLine(
            "| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (DeclaredMethodScorecardCell cell in result.Cells)
        {
            DeclaredMethodScorecardCell implementationCount =
                result.Cells.Single(candidate =>
                    candidate.Implementation == cell.Implementation
                    && candidate.Terminal == "Count");
            DeclaredMethodScorecardCell metadata =
                result.Cells.Single(candidate =>
                    candidate.Implementation == "Metadata"
                    && candidate.Terminal == cell.Terminal);
            writer.WriteLine(
                $"| {cell.Implementation} | {cell.Terminal} | "
                    + $"{cell.Microseconds:F3} us | "
                    + $"{cell.AllocatedBytes:N0} B | "
                    + $"{cell.Microseconds / implementationCount.Microseconds:F2}x | "
                    + $"{FormatRatio(cell.AllocatedBytes, implementationCount.AllocatedBytes)} | "
                    + $"{cell.Microseconds / metadata.Microseconds:F2}x | "
                    + $"{FormatRatio(cell.AllocatedBytes, metadata.AllocatedBytes)} |");
        }
        writer.WriteLine();
        writer.WriteLine(
            "| Terminal | Method handles | Method rows | Names | "
                + "Signatures | Attributes | Projected rows |");
        writer.WriteLine(
            "| --- | ---: | ---: | ---: | ---: | ---: | ---: |");
        WriteReceipt("Count", result.Check.CountReceipt);
        WriteReceipt("Rows", result.Check.RowsReceipt);
        return writer.ToString();

        void WriteReceipt(
            string terminal,
            MetadataDeclaredMethodPopulationReceipt receipt)
        {
            writer.WriteLine(
                $"| {terminal} | "
                    + $"{receipt.MethodDefinitionHandlesVisited} | "
                    + $"{receipt.MethodDefinitionRowsRead} | "
                    + $"{receipt.MethodNamesDecoded} | "
                    + $"{receipt.MethodSignaturesDecoded} | "
                    + $"{receipt.MethodAttributesDecoded} | "
                    + $"{receipt.ProjectedRows} |");
        }
    }

    private static DeclaredMethodScorecardCheck Check(Asset asset)
    {
        var rows = AssertRead(
            asset.ExecuteMetadata(
                MetadataDeclaredMethodPopulationTerminal.Rows));
        var count = AssertCounted(
            asset.ExecuteMetadata(
                MetadataDeclaredMethodPopulationTerminal.Count));
        int nlinqCount = asset.ExecuteNLinqCount();
        ImmutableArray<int> nlinqRows =
            asset.ExecuteNLinqRows();
        if (count.Count != rows.Count
            || count.Count != rows.Rows.Length)
        {
            throw new InvalidOperationException(
                "Declared MethodDef Count and Rows disagree.");
        }
        if (count.Receipt.MethodDefinitionHandlesVisited != 0
            || count.Receipt.MethodDefinitionRowsRead != 0
            || count.Receipt.MethodNamesDecoded != 0
            || count.Receipt.MethodSignaturesDecoded != 0
            || count.Receipt.MethodAttributesDecoded != 0
            || count.Receipt.ProjectedRows != 0)
        {
            throw new InvalidOperationException(
                "Declared MethodDef Count performed per-method work.");
        }
        if (nlinqCount != count.Count
            || !nlinqRows.AsSpan().SequenceEqual(rows.Rows.AsSpan()))
        {
            throw new InvalidOperationException(
                "NLinq declared MethodDef Count and Rows disagree.");
        }

        return new(
            count.Count,
            Hash(rows.Rows),
            count.Receipt,
            rows.Receipt);
    }

    private static MetadataDeclaredMethodPopulationOutcome.Counted
        AssertCounted(MetadataDeclaredMethodPopulationOutcome outcome) =>
            outcome
                as MetadataDeclaredMethodPopulationOutcome.Counted
            ?? throw new InvalidOperationException(
                $"Expected Counted, got {outcome}.");

    private static MetadataDeclaredMethodPopulationOutcome.Read
        AssertRead(MetadataDeclaredMethodPopulationOutcome outcome) =>
            outcome
                as MetadataDeclaredMethodPopulationOutcome.Read
            ?? throw new InvalidOperationException(
                $"Expected Read, got {outcome}.");

    private static void Warmup(
        Asset asset,
        DeclaredMethodScorecardCase measurementCase)
    {
        switch (measurementCase)
        {
            case DeclaredMethodScorecardCase.MetadataCount:
                GC.KeepAlive(
                    asset.ExecuteMetadata(
                        MetadataDeclaredMethodPopulationTerminal.Count));
                break;
            case DeclaredMethodScorecardCase.MetadataRows:
                GC.KeepAlive(
                    asset.ExecuteMetadata(
                        MetadataDeclaredMethodPopulationTerminal.Rows));
                break;
            case DeclaredMethodScorecardCase.NLinqCount:
                GC.KeepAlive(asset.ExecuteNLinqCount());
                break;
            case DeclaredMethodScorecardCase.NLinqRows:
                GC.KeepAlive(asset.ExecuteNLinqRows());
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(measurementCase));
        }
    }

    private static Measurement Measure(
        Asset asset,
        DeclaredMethodScorecardCase measurementCase) =>
        measurementCase switch
        {
            DeclaredMethodScorecardCase.MetadataCount =>
                Measure(
                    () => asset.ExecuteMetadata(
                        MetadataDeclaredMethodPopulationTerminal.Count)),
            DeclaredMethodScorecardCase.MetadataRows =>
                Measure(
                    () => asset.ExecuteMetadata(
                        MetadataDeclaredMethodPopulationTerminal.Rows)),
            DeclaredMethodScorecardCase.NLinqCount =>
                Measure(asset.ExecuteNLinqCount),
            DeclaredMethodScorecardCase.NLinqRows =>
                Measure(asset.ExecuteNLinqRows),
            _ => throw new ArgumentOutOfRangeException(
                nameof(measurementCase)),
        };

    private static Measurement Measure<T>(Func<T> execute)
    {
        var times = new double[31];
        var allocations = new long[31];
        for (int sample = 0; sample < times.Length; sample++)
        {
            long before =
                GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            T? result = default;
            for (int operation = 0;
                operation < OperationsPerSample;
                operation++)
            {
                result = execute();
            }
            long elapsed = Stopwatch.GetTimestamp() - started;
            allocations[sample] =
                (GC.GetAllocatedBytesForCurrentThread() - before)
                    / OperationsPerSample;
            times[sample] =
                elapsed * 1_000_000.0
                    / Stopwatch.Frequency
                    / OperationsPerSample;
            GC.KeepAlive(result);
        }
        Array.Sort(times);
        Array.Sort(allocations);
        return new(
            times[times.Length / 2],
            allocations[allocations.Length / 2]);
    }

    private static double Median(List<double> values)
    {
        values.Sort();
        return values[values.Count / 2];
    }

    private static long Median(List<long> values)
    {
        values.Sort();
        return values[values.Count / 2];
    }

    private static double Ratio(long value, long baseline) =>
        baseline == 0
            ? value == 0 ? 1 : double.PositiveInfinity
            : (double)value / baseline;

    private static string FormatRatio(
        long value,
        long baseline) =>
        baseline == 0 && value != 0
            ? "unbounded"
            : string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{Ratio(value, baseline):F2}x");

    private static string Hash(
        ImmutableArray<int> rows)
    {
        ulong hash = 14695981039346656037;
        foreach (int token in rows)
        {
            unchecked
            {
                for (int shift = 0; shift < 32; shift += 8)
                {
                    hash ^= (byte)(token >> shift);
                    hash *= 1099511628211;
                }
            }
        }
        return hash.ToString("x16");
    }

    private static string DisplayName(
        MetadataTypeDefinitionName type) =>
        string.IsNullOrEmpty(type.Namespace)
            ? string.Join('+', type.Segments)
            : $"{type.Namespace}.{string.Join('+', type.Segments)}";

    private static string Implementation(
        DeclaredMethodScorecardCase measurementCase) =>
        measurementCase switch
        {
            DeclaredMethodScorecardCase.MetadataCount
                or DeclaredMethodScorecardCase.MetadataRows =>
                "Metadata",
            DeclaredMethodScorecardCase.NLinqCount
                or DeclaredMethodScorecardCase.NLinqRows =>
                "NLinq",
            _ => throw new ArgumentOutOfRangeException(
                nameof(measurementCase)),
        };

    private static string Terminal(
        DeclaredMethodScorecardCase measurementCase) =>
        measurementCase switch
        {
            DeclaredMethodScorecardCase.MetadataCount
                or DeclaredMethodScorecardCase.NLinqCount =>
                "Count",
            DeclaredMethodScorecardCase.MetadataRows
                or DeclaredMethodScorecardCase.NLinqRows =>
                "Rows",
            _ => throw new ArgumentOutOfRangeException(
                nameof(measurementCase)),
        };

    private enum DeclaredMethodScorecardCase
    {
        MetadataCount,
        MetadataRows,
        NLinqCount,
        NLinqRows,
    }

    private readonly record struct Measurement(
        double Microseconds,
        long AllocatedBytes);

    private sealed class Asset : IDisposable
    {
        private readonly AssemblyInspectionSession _session;
        private readonly MetadataTypeDefinitionBinding _type;
        private readonly MetadataReader _reader;

        private Asset(
            AssemblyInspectionSession session,
            MetadataTypeDefinitionBinding type,
            MetadataReader reader)
        {
            _session = session;
            _type = type;
            _reader = reader;
        }

        internal static Asset Open(
            string path,
            MetadataTypeDefinitionName type)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            ArgumentNullException.ThrowIfNull(type);
            AssemblyInspectionSession session =
                AssemblyInspectionSession.Open(path);
            try
            {
                TypeDeclarationResult.Defined defined =
                    session.ProbeDeclaration(type)
                        as TypeDeclarationResult.Defined
                    ?? throw new InvalidOperationException(
                        $"{DisplayName(type)} was not defined.");
                return new(
                    session,
                    new(
                        session.ModuleVersionId(),
                        defined.Definition),
                    session.GetAdmittedMetadataReader());
            }
            catch
            {
                session.Dispose();
                throw;
            }
        }

        internal MetadataDeclaredMethodPopulationOutcome ExecuteMetadata(
            MetadataDeclaredMethodPopulationTerminal terminal) =>
            _session.DeclaredMethods(
                new(_type, terminal));

        internal int ExecuteNLinqCount()
        {
            MethodHandles methods = Source();
            return methods.Count<
                MethodHandles,
                MethodDefinitionHandle>();
        }

        internal ImmutableArray<int> ExecuteNLinqRows()
        {
            MethodHandles methods = Source();
            var rows =
                ImmutableArray.CreateBuilder<int>(
                    methods.Count<
                        MethodHandles,
                        MethodDefinitionHandle>());
            return methods.Fold<
                    MethodHandles,
                    MethodDefinitionHandle,
                    ImmutableArray<int>.Builder,
                    AddToken>(
                    rows,
                    new AddToken())
                .MoveToImmutable();
        }

        private MethodHandles Source()
        {
            Guid moduleVersionId =
                _reader.GetGuid(
                    _reader.GetModuleDefinition().Mvid);
            if (moduleVersionId != _type.ModuleVersionId)
            {
                throw new InvalidOperationException(
                    "The NLinq source binding belongs to another module.");
            }

            EntityHandle entity =
                MetadataTokens.EntityHandle(
                    _type.Definition.Value);
            int rowNumber =
                MetadataTokens.GetRowNumber(entity);
            if (entity.Kind != HandleKind.TypeDefinition
                || rowNumber <= 0
                || rowNumber
                    > _reader.GetTableRowCount(
                        TableIndex.TypeDef))
            {
                throw new InvalidOperationException(
                    "The NLinq source binding is outside the TypeDef table.");
            }

            return new(
                _reader.GetTypeDefinition(
                        (TypeDefinitionHandle)entity)
                    .GetMethods());
        }

        public void Dispose() => _session.Dispose();
    }

    private struct MethodHandles
        : NLinq.IEnumerator<
            MethodHandles,
            MethodDefinitionHandle>
    {
        private MethodDefinitionHandleCollection.Enumerator
            _enumerator;
        private int _remaining;

        internal MethodHandles(
            MethodDefinitionHandleCollection methods)
        {
            _enumerator = methods.GetEnumerator();
            _remaining = methods.Count;
        }

        public static int Count(
            scoped ref MethodHandles source) =>
            source._remaining;

        public MethodDefinitionHandle TryGetNext(
            out bool hasMore)
        {
            hasMore = _enumerator.MoveNext();
            if (!hasMore)
                return default;
            _remaining--;
            return _enumerator.Current;
        }
    }

    private readonly struct AddToken
        : IFunc<
            ImmutableArray<int>.Builder,
            MethodDefinitionHandle,
            ImmutableArray<int>.Builder>
    {
        public ImmutableArray<int>.Builder Invoke(
            ImmutableArray<int>.Builder rows,
            MethodDefinitionHandle method)
        {
            rows.Add(MetadataTokens.GetToken(method));
            return rows;
        }
    }
}
