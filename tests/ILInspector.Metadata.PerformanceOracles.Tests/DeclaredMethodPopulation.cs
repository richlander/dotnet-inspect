using System.Diagnostics;

using ILInspector.Metadata;

namespace DotnetInspector.PerformanceOracles;

public sealed record DeclaredMethodScorecardCheck(
    int Count,
    string AnswerHash,
    MetadataDeclaredMethodPopulationReceipt CountReceipt,
    MetadataDeclaredMethodPopulationReceipt RowsReceipt);

public sealed record DeclaredMethodScorecardCell(
    string Terminal,
    double Microseconds,
    long AllocatedBytes);

public sealed record DeclaredMethodScorecardResult(
    DeclaredMethodScorecardCheck Check,
    IReadOnlyList<DeclaredMethodScorecardCell> Cells);

public static class DeclaredMethodPopulation
{
    private const int OperationsPerSample = 256;

    public static DeclaredMethodScorecardCheck Check(string path)
    {
        using var asset = Asset.Open(path);
        return Check(asset);
    }

    public static DeclaredMethodScorecardResult Measure(string path)
    {
        using var asset = Asset.Open(path);
        DeclaredMethodScorecardCheck check = Check(asset);
        var cells = new List<DeclaredMethodScorecardCell>();
        var terminals = new[]
        {
            MetadataDeclaredMethodPopulationTerminal.Count,
            MetadataDeclaredMethodPopulationTerminal.Rows,
        };
        foreach (MetadataDeclaredMethodPopulationTerminal terminal
            in terminals)
        {
            for (int warmup = 0; warmup < 10; warmup++)
                GC.KeepAlive(asset.Execute(terminal));
        }

        var times = terminals.ToDictionary(
            static terminal => terminal,
            static _ => new List<double>());
        var allocations = terminals.ToDictionary(
            static terminal => terminal,
            static _ => new List<long>());
        for (int round = 0; round < 8; round++)
        {
            for (int offset = 0; offset < terminals.Length; offset++)
            {
                MetadataDeclaredMethodPopulationTerminal terminal =
                    terminals[(round + offset) % terminals.Length];
                Measurement measurement =
                    Measure(() => asset.Execute(terminal));
                times[terminal].Add(measurement.Microseconds);
                allocations[terminal].Add(measurement.AllocatedBytes);
            }
        }
        foreach (MetadataDeclaredMethodPopulationTerminal terminal
            in terminals)
        {
            cells.Add(
                new(
                    terminal.ToString(),
                    Median(times[terminal]),
                    Median(allocations[terminal])));
        }
        return new(check, cells);
    }

    public static string Report(DeclaredMethodScorecardResult result)
    {
        using var writer = new StringWriter(
            System.Globalization.CultureInfo.InvariantCulture);
        writer.WriteLine(
            $"# answer: {result.Check.Count} declared MethodDefs, "
                + result.Check.AnswerHash);
        writer.WriteLine(
            "| Terminal | Median | Allocated | Time vs Count | "
                + "Allocation vs Count |");
        writer.WriteLine("| --- | ---: | ---: | ---: | ---: |");
        DeclaredMethodScorecardCell count =
            result.Cells.Single(cell => cell.Terminal == "Count");
        DeclaredMethodScorecardCell rows =
            result.Cells.Single(cell => cell.Terminal == "Rows");
        writer.WriteLine(
            $"| Count | {count.Microseconds:F3} us | "
                + $"{count.AllocatedBytes:N0} B | 1.00x | 1.00x |");
        writer.WriteLine(
            $"| Rows | {rows.Microseconds:F3} us | "
                + $"{rows.AllocatedBytes:N0} B | "
                + $"{rows.Microseconds / count.Microseconds:F2}x | "
                + $"{Ratio(rows.AllocatedBytes, count.AllocatedBytes):F2}x |");
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
        var count = AssertCounted(
            asset.Execute(
                MetadataDeclaredMethodPopulationTerminal.Count));
        var rows = AssertRead(
            asset.Execute(
                MetadataDeclaredMethodPopulationTerminal.Rows));
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

        ulong hash = 14695981039346656037;
        foreach (int token in rows.Rows)
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
        return new(
            count.Count,
            hash.ToString("x16"),
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

    private static Measurement Measure(Func<object> execute)
    {
        var times = new double[31];
        var allocations = new long[31];
        for (int sample = 0; sample < times.Length; sample++)
        {
            long before =
                GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            object? result = null;
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

    private readonly record struct Measurement(
        double Microseconds,
        long AllocatedBytes);

    private sealed class Asset : IDisposable
    {
        private readonly AssemblyInspectionSession _session;
        private readonly MetadataTypeDefinitionBinding _type;

        private Asset(
            AssemblyInspectionSession session,
            MetadataTypeDefinitionBinding type)
        {
            _session = session;
            _type = type;
        }

        internal static Asset Open(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            AssemblyInspectionSession session =
                AssemblyInspectionSession.Open(path);
            try
            {
                MetadataTypeDefinitionName type =
                    MetadataTypeDefinitionName.Create(
                        "System.Text.Json",
                        ["JsonSerializer"])
                    is MetadataTypeDefinitionNameResult.Valid valid
                        ? valid.Name
                        : throw new InvalidOperationException(
                            "The scorecard Type name was invalid.");
                TypeDeclarationResult.Defined defined =
                    session.ProbeDeclaration(type)
                        as TypeDeclarationResult.Defined
                    ?? throw new InvalidOperationException(
                        "System.Text.Json.JsonSerializer was not defined.");
                return new(
                    session,
                    new(
                        session.ModuleVersionId(),
                        defined.Definition));
            }
            catch
            {
                session.Dispose();
                throw;
            }
        }

        internal MetadataDeclaredMethodPopulationOutcome Execute(
            MetadataDeclaredMethodPopulationTerminal terminal) =>
            _session.DeclaredMethods(
                new(_type, terminal));

        public void Dispose() => _session.Dispose();
    }
}
