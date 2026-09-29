using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using ILInspector.Metadata;
using NLinq;

namespace DotnetInspector.PerformanceOracles;

public sealed record MemberGroupScorecardScenario(
    string Name,
    string TypeName,
    string MethodName,
    MetadataMethodAccessibilityFilter Accessibility,
    MetadataMethodReceiverFilter Receiver,
    int ExpectedCount);

public sealed record MemberGroupProjectionRow(
    int MetadataToken,
    string DisplaySignature,
    string CanonicalSignature,
    string Fingerprint,
    string Accessibility,
    MetadataMethodReceiver Receiver);

public sealed record MemberGroupProjectionAnswer(
    int Count,
    IReadOnlyList<MemberGroupProjectionRow> Rows,
    int? NextOrdinal,
    bool ContinuationOutOfRange,
    long? IncompleteRetainedTextCharacters,
    bool RowsFailed);

public sealed record MemberGroupScorecardMismatch(
    string Scenario,
    string Terminal,
    string Column);

public sealed record MemberGroupScorecardAnswerHash(
    string Scenario,
    string Terminal,
    string Hash);

public sealed record MemberGroupScorecardCheck(
    int Compared,
    IReadOnlyList<MemberGroupScorecardMismatch> Mismatches,
    IReadOnlyList<MemberGroupScorecardAnswerHash> AnswerHashes)
{
    public bool Agrees => Mismatches.Count == 0;
}

public sealed record MemberGroupScorecardCell(
    string Scenario,
    string Terminal,
    string Phase,
    string Column,
    double Microseconds,
    long AllocatedBytes);

public sealed record MemberGroupScorecardResult(
    MemberGroupScorecardCheck Check,
    IReadOnlyList<MemberGroupScorecardCell> Cells);

public static class MemberGroupPopulation
{
    public static IReadOnlyList<MemberGroupScorecardScenario>
        Scenarios { get; } =
        [
            new(
                "Deserialize public/all",
                "JsonSerializer",
                "Deserialize",
                MetadataMethodAccessibilityFilter.Public,
                MetadataMethodReceiverFilter.All,
                40),
            new(
                "Deserialize public/static",
                "JsonSerializer",
                "Deserialize",
                MetadataMethodAccessibilityFilter.Public,
                MetadataMethodReceiverFilter.Static,
                25),
            new(
                "Deserialize public/extension",
                "JsonSerializer",
                "Deserialize",
                MetadataMethodAccessibilityFilter.Public,
                MetadataMethodReceiverFilter.Extension,
                15),
            new(
                "Deserialize public/this",
                "JsonSerializer",
                "Deserialize",
                MetadataMethodAccessibilityFilter.Public,
                MetadataMethodReceiverFilter.This,
                0),
            new(
                "Parse public/all",
                "JsonDocument",
                "Parse",
                MetadataMethodAccessibilityFilter.Public,
                MetadataMethodReceiverFilter.All,
                5),
            new(
                "Parse private/all",
                "JsonDocument",
                "Parse",
                MetadataMethodAccessibilityFilter.Private,
                MetadataMethodReceiverFilter.All,
                2),
            new(
                "Parse all/all",
                "JsonDocument",
                "Parse",
                MetadataMethodAccessibilityFilter.All,
                MetadataMethodReceiverFilter.All,
                7),
        ];

    private static IReadOnlyList<Column> Columns { get; } =
        [
            new("LINQ", Linq),
            new("NLinq", NLinq),
            new("Planner", Planner),
        ];

    public static MemberGroupScorecardCheck Check(
        string path)
    {
        using var asset = Asset.Open(path);
        return Check(asset);
    }

    public static MemberGroupScorecardResult Measure(
        string path)
    {
        using var asset = Asset.Open(path);
        MemberGroupScorecardCheck check = Check(asset);
        if (!check.Agrees)
            return new(check, []);

        var cells = new List<MemberGroupScorecardCell>();
        for (int scenarioIndex = 0;
            scenarioIndex < Scenarios.Count;
            scenarioIndex++)
        {
            MemberGroupScorecardScenario scenario =
                Scenarios[scenarioIndex];
            MetadataTypeDefinitionName declaringType =
                TypeName(scenario.TypeName);
            MetadataMethodGroupInspection.Analysis model =
                asset.Prepare(
                    declaringType,
                    scenario.MethodName);
            cells.Add(
                MeasurePreparation(
                    asset,
                    scenario.Name,
                    declaringType,
                    scenario.MethodName));
            foreach (bool rows in new[] { false, true })
            {
                bool composedFirst =
                    ((scenarioIndex + (rows ? 1 : 0)) & 1) != 0;
                if (composedFirst)
                    MeasureComposed();
                MeasureKernel();
                if (!composedFirst)
                    MeasureComposed();

                void MeasureKernel() =>
                    MeasurePhase(
                        cells,
                        scenario,
                        rows,
                        "Kernel",
                        column =>
                            column.Execute(
                                model,
                                scenario.Accessibility,
                                scenario.Receiver,
                                rows));

                void MeasureComposed() =>
                    MeasurePhase(
                        cells,
                        scenario,
                        rows,
                        "Composed",
                        column =>
                        {
                            MetadataMethodGroupInspection.Analysis fresh =
                                asset.Prepare(
                                    declaringType,
                                    scenario.MethodName);
                            return column.Execute(
                                fresh,
                                scenario.Accessibility,
                                scenario.Receiver,
                                rows);
                        });
            }
        }

        return new(check, cells);
    }

    public static string Report(
        MemberGroupScorecardResult result)
    {
        using var writer = new StringWriter(
            System.Globalization.CultureInfo.InvariantCulture);
        writer.WriteLine(
            $"# answers: {result.Check.Compared} compared, "
                + $"{result.Check.Mismatches.Count} mismatches");
        foreach (MemberGroupScorecardAnswerHash answer
            in result.Check.AnswerHashes)
        {
            writer.WriteLine(
                $"# answer: {answer.Scenario} / "
                    + $"{answer.Terminal} = {answer.Hash}");
        }
        foreach (MemberGroupScorecardMismatch mismatch
            in result.Check.Mismatches)
        {
            writer.WriteLine(
                $"mismatch\t{mismatch.Scenario}\t"
                    + $"{mismatch.Terminal}\t{mismatch.Column}");
        }
        if (!result.Check.Agrees)
            return writer.ToString();

        writer.WriteLine(
            "| Scenario | Terminal | Phase | LINQ | NLinq | Planner | Planner alloc |");
        writer.WriteLine(
            "| --- | --- | --- | ---: | ---: | ---: | ---: |");
        foreach (MemberGroupScorecardScenario scenario
            in Scenarios)
        {
            foreach (string terminal in new[] { "Count", "Rows" })
            {
                foreach (string phase in new[] { "Kernel", "Composed" })
                {
                    MemberGroupScorecardCell[] selected =
                        [.. result.Cells.Where(cell =>
                            cell.Scenario == scenario.Name
                            && cell.Terminal == terminal
                            && cell.Phase == phase)];
                    double oracle =
                        selected.Single(cell =>
                                cell.Column == "NLinq")
                            .Microseconds;
                    MemberGroupScorecardCell planner =
                        selected.Single(cell =>
                            cell.Column == "Planner");
                    writer.WriteLine(
                        $"| {scenario.Name} | {terminal} | {phase} | "
                            + $"{Ratio(selected, "LINQ", oracle):F2}x | "
                            + $"1.00x ({oracle:F3} us) | "
                            + $"{planner.Microseconds / oracle:F2}x "
                            + $"({planner.Microseconds:F3} us) | "
                            + $"{planner.AllocatedBytes:N0} B |");
                }
            }
        }
        writer.WriteLine();
        writer.WriteLine(
            "| Scenario | Preparation | Preparation alloc |");
        writer.WriteLine("| --- | ---: | ---: |");
        foreach (MemberGroupScorecardCell cell
            in result.Cells.Where(cell =>
                cell.Phase == "Preparation"))
        {
            writer.WriteLine(
                $"| {cell.Scenario} | {cell.Microseconds:F3} us | "
                    + $"{cell.AllocatedBytes:N0} B |");
        }
        writer.WriteLine();
        writer.WriteLine(
            "| Terminal | Phase | LINQ geo mean | Planner geo mean |");
        writer.WriteLine("| --- | --- | ---: | ---: |");
        foreach (string terminal in new[] { "Count", "Rows" })
        {
            foreach (string phase in new[] { "Kernel", "Composed" })
            {
                MemberGroupScorecardCell[] selected =
                    [.. result.Cells.Where(cell =>
                        cell.Terminal == terminal
                        && cell.Phase == phase)];
                writer.WriteLine(
                    $"| {terminal} | {phase} | "
                        + $"{GeometricRatio(selected, "LINQ"):F2}x | "
                        + $"{GeometricRatio(selected, "Planner"):F2}x |");
            }
        }
        return writer.ToString();
    }

    public static MemberGroupProjectionAnswer Execute(
        string path,
        MemberGroupScorecardScenario scenario,
        string column,
        bool rows)
    {
        using var asset = Asset.Open(path);
        MetadataMethodGroupInspection.Analysis model =
            asset.Prepare(
                TypeName(scenario.TypeName),
                scenario.MethodName);
        Column selected = Columns.Single(item =>
            item.Name == column);
        return Normalize(
            selected.Execute(
                model,
                scenario.Accessibility,
                scenario.Receiver,
                rows));
    }

    private static MemberGroupScorecardCheck Check(
        Asset asset)
    {
        var mismatches =
            new List<MemberGroupScorecardMismatch>();
        var answerHashes =
            new List<MemberGroupScorecardAnswerHash>();
        int compared = 0;
        foreach (MemberGroupScorecardScenario scenario
            in Scenarios)
        {
            MetadataMethodGroupInspection.Analysis model =
                asset.Prepare(
                    TypeName(scenario.TypeName),
                    scenario.MethodName);
            foreach (bool rows in new[] { false, true })
            {
                MemberGroupProjectionAnswer expected =
                    Normalize(
                        NLinq(
                            model,
                            scenario.Accessibility,
                            scenario.Receiver,
                            rows));
                if (expected.Count != scenario.ExpectedCount)
                {
                    throw new InvalidOperationException(
                        $"{scenario.Name} returned "
                            + $"{expected.Count}, expected "
                            + $"{scenario.ExpectedCount}.");
                }
                answerHashes.Add(
                    new(
                        scenario.Name,
                        rows ? "Rows" : "Count",
                        AnswerHash(expected)));
                foreach (Column column in Columns)
                {
                    MemberGroupProjectionAnswer actual =
                        Normalize(
                            column.Execute(
                                model,
                                scenario.Accessibility,
                                scenario.Receiver,
                                rows));
                    compared++;
                    if (!Same(expected, actual))
                    {
                        mismatches.Add(
                            new(
                                scenario.Name,
                                rows ? "Rows" : "Count",
                                column.Name));
                    }
                }
            }
        }

        return new(compared, mismatches, answerHashes);
    }

    private static MetadataMethodGroupInspectionOutcome Planner(
        MetadataMethodGroupInspection.Analysis model,
        MetadataMethodAccessibilityFilter accessibility,
        MetadataMethodReceiverFilter receiver,
        bool rows) =>
        MetadataMethodGroupInspection.Execute(
            model,
            Selection(model, accessibility, receiver),
            startOrdinal: 0,
            maximumRows: 100,
            materializeRows: rows,
            maximumMembers: 100_000,
            maximumRetainedTextCharacters: 20_000_000);

    private static MetadataMethodGroupInspectionOutcome Linq(
        MetadataMethodGroupInspection.Analysis model,
        MetadataMethodAccessibilityFilter accessibility,
        MetadataMethodReceiverFilter receiver,
        bool rows)
    {
        var fold =
            Fold(model, accessibility, receiver, rows);
        MetadataMethodGroupInspection.Fold completed =
            model.Methods
                .Select(static handle => handle)
                .Aggregate(
                    fold,
                    static (current, handle) =>
                    {
                        _ = current.Accept(handle);
                        return current;
                    });
        return completed.Complete();
    }

    private static MetadataMethodGroupInspectionOutcome NLinq(
        MetadataMethodGroupInspection.Analysis model,
        MetadataMethodAccessibilityFilter accessibility,
        MetadataMethodReceiverFilter receiver,
        bool rows)
    {
        var source = new MethodHandles(model.Methods);
        MetadataMethodGroupInspection.Fold completed =
            source.Fold<
                MethodHandles,
                MethodDefinitionHandle,
                MetadataMethodGroupInspection.Fold,
                Accept>(
                    Fold(model, accessibility, receiver, rows),
                    default);
        return completed.Complete();
    }

    private static MetadataMethodGroupInspection.Selection Selection(
        MetadataMethodGroupInspection.Analysis model,
        MetadataMethodAccessibilityFilter accessibility,
        MetadataMethodReceiverFilter receiver) =>
        new(
            model,
            accessibility,
            receiver,
            includeHidden: false);

    private static MetadataMethodGroupInspection.Fold Fold(
        MetadataMethodGroupInspection.Analysis model,
        MetadataMethodAccessibilityFilter accessibility,
        MetadataMethodReceiverFilter receiver,
        bool rows) =>
        new(
            model,
            Selection(model, accessibility, receiver),
            startOrdinal: 0,
            maximumRows: 100,
            materializeRows: rows,
            maximumMembers: 100_000,
            maximumRetainedTextCharacters: 20_000_000);

    private static MemberGroupProjectionAnswer Normalize(
        MetadataMethodGroupInspectionOutcome outcome)
    {
        if (outcome
            is not MetadataMethodGroupInspectionOutcome.Read read)
        {
            throw new InvalidOperationException(
                $"Expected a read outcome, got {outcome}.");
        }
        return new(
            read.Count,
            [.. read.Rows.Select(static row =>
                new MemberGroupProjectionRow(
                    row.MetadataToken,
                    row.DisplaySignature,
                    row.CanonicalSignature,
                    row.Fingerprint,
                    row.Accessibility,
                    row.Receiver))],
            read.NextOrdinal,
            read.ContinuationOutOfRange,
            read.IncompleteRetainedTextCharacters,
            read.RowsFailed);
    }

    private static bool Same(
        MemberGroupProjectionAnswer first,
        MemberGroupProjectionAnswer second) =>
        first.Count == second.Count
        && first.Rows.SequenceEqual(second.Rows)
        && first.NextOrdinal == second.NextOrdinal
        && first.ContinuationOutOfRange
            == second.ContinuationOutOfRange
        && first.IncompleteRetainedTextCharacters
            == second.IncompleteRetainedTextCharacters
        && first.RowsFailed == second.RowsFailed;

    private static string AnswerHash(
        MemberGroupProjectionAnswer answer)
    {
        ulong hash = 14695981039346656037;
        AddLong(answer.Count);
        AddLong(answer.NextOrdinal ?? -1);
        AddLong(answer.ContinuationOutOfRange ? 1 : 0);
        AddLong(
            answer.IncompleteRetainedTextCharacters
                ?? -1);
        AddLong(answer.RowsFailed ? 1 : 0);
        foreach (MemberGroupProjectionRow row in answer.Rows)
        {
            AddLong(row.MetadataToken);
            AddString(row.DisplaySignature);
            AddString(row.CanonicalSignature);
            AddString(row.Fingerprint);
            AddString(row.Accessibility);
            AddLong((int)row.Receiver);
        }
        return hash.ToString("x16");

        void AddLong(long value)
        {
            unchecked
            {
                for (int shift = 0; shift < 64; shift += 8)
                {
                    hash ^= (byte)(value >> shift);
                    hash *= 1099511628211;
                }
            }
        }

        void AddString(string value)
        {
            foreach (char character in value)
                AddLong(character);
            AddLong(-2);
        }
    }

    private static MetadataTypeDefinitionName TypeName(
        string typeName) =>
        MetadataTypeDefinitionName.Create(
            "System.Text.Json",
            [typeName])
        is MetadataTypeDefinitionNameResult.Valid valid
            ? valid.Name
            : throw new InvalidOperationException(
                "The scorecard Type name was invalid.");

    private static void MeasurePhase(
        List<MemberGroupScorecardCell> cells,
        MemberGroupScorecardScenario scenario,
        bool rows,
        string phase,
        Func<Column, MetadataMethodGroupInspectionOutcome>
            execute)
    {
        var times = Columns.ToDictionary(
            static column => column.Name,
            static _ => new List<double>());
        var allocations = Columns.ToDictionary(
            static column => column.Name,
            static _ => new List<long>());
        foreach (Column column in Columns)
        {
            for (int warmup = 0; warmup < 5; warmup++)
                GC.KeepAlive(execute(column));
        }

        for (int round = 0; round < 6; round++)
        {
            for (int offset = 0;
                offset < Columns.Count;
                offset++)
            {
                Column column =
                    Columns[(round + offset) % Columns.Count];
                Measurement measurement =
                    Measure(() => execute(column));
                times[column.Name].Add(
                    measurement.Microseconds);
                allocations[column.Name].Add(
                    measurement.AllocatedBytes);
            }
        }

        foreach (Column column in Columns)
        {
            cells.Add(
                new(
                    scenario.Name,
                    rows ? "Rows" : "Count",
                    phase,
                    column.Name,
                    Median(times[column.Name]),
                    Median(allocations[column.Name])));
        }
    }

    private static MemberGroupScorecardCell MeasurePreparation(
        Asset asset,
        string scenario,
        MetadataTypeDefinitionName declaringType,
        string methodName)
    {
        for (int warmup = 0; warmup < 5; warmup++)
            GC.KeepAlive(asset.Prepare(declaringType, methodName));
        var times = new List<double>(6);
        var allocations = new List<long>(6);
        for (int round = 0; round < 6; round++)
        {
            Measurement measurement =
                Measure(
                    () => asset.Prepare(
                        declaringType,
                        methodName));
            times.Add(measurement.Microseconds);
            allocations.Add(measurement.AllocatedBytes);
        }
        return new(
            scenario,
            "",
            "Preparation",
            "Shared",
            Median(times),
            Median(allocations));
    }

    private static Measurement Measure(Func<object> execute)
    {
        var times = new double[101];
        var allocations = new long[101];
        for (int sample = 0; sample < times.Length; sample++)
        {
            long before =
                GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            object result = execute();
            long elapsed = Stopwatch.GetTimestamp() - started;
            allocations[sample] =
                GC.GetAllocatedBytesForCurrentThread() - before;
            times[sample] =
                elapsed * 1_000_000.0 / Stopwatch.Frequency;
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

    private static double Ratio(
        MemberGroupScorecardCell[] cells,
        string column,
        double oracle) =>
        cells.Single(cell => cell.Column == column)
            .Microseconds
            / oracle;

    private static double GeometricRatio(
        MemberGroupScorecardCell[] cells,
        string column)
    {
        double logarithms = 0;
        int count = 0;
        foreach (IGrouping<string, MemberGroupScorecardCell>
            scenario in cells.GroupBy(static cell =>
                cell.Scenario))
        {
            double oracle =
                scenario.Single(cell =>
                        cell.Column == "NLinq")
                    .Microseconds;
            logarithms += Math.Log(
                scenario.Single(cell =>
                        cell.Column == column)
                    .Microseconds
                    / oracle);
            count++;
        }
        return Math.Exp(logarithms / count);
    }

    private sealed record Column(
        string Name,
        Executor Execute);

    private delegate MetadataMethodGroupInspectionOutcome Executor(
        MetadataMethodGroupInspection.Analysis model,
        MetadataMethodAccessibilityFilter accessibility,
        MetadataMethodReceiverFilter receiver,
        bool rows);

    private readonly record struct Measurement(
        double Microseconds,
        long AllocatedBytes);

    private struct MethodHandles
        : NLinq.IEnumerator<
            MethodHandles,
            MethodDefinitionHandle>
    {
        private MethodDefinitionHandleCollection.Enumerator _handles;

        internal MethodHandles(
            MethodDefinitionHandleCollection handles)
        {
            _handles = handles.GetEnumerator();
        }

        public MethodDefinitionHandle TryGetNext(
            out bool hasMore)
        {
            hasMore = _handles.MoveNext();
            return hasMore
                ? _handles.Current
                : default;
        }
    }

    private readonly struct Accept
        : IFunc<
            MetadataMethodGroupInspection.Fold,
            MethodDefinitionHandle,
            MetadataMethodGroupInspection.Fold>
    {
        public MetadataMethodGroupInspection.Fold Invoke(
            MetadataMethodGroupInspection.Fold fold,
            MethodDefinitionHandle handle)
        {
            _ = fold.Accept(handle);
            return fold;
        }
    }

    private sealed class Asset : IDisposable
    {
        private readonly FileStream _stream;
        private readonly PEReader _peReader;
        private readonly AssemblyInspectionSession _assembly;
        private readonly MetadataOperationContext _operation;
        private readonly MetadataDeclarationSession _declaration;

        private Asset(
            FileStream stream,
            PEReader peReader,
            AssemblyInspectionSession assembly,
            MetadataOperationContext operation,
            MetadataDeclarationSession declaration,
            MetadataReader reader,
            MetadataMethodSemanticsAssociationResult methodSemantics)
        {
            _stream = stream;
            _peReader = peReader;
            _assembly = assembly;
            _operation = operation;
            _declaration = declaration;
            Reader = reader;
            MethodSemantics = methodSemantics;
        }

        internal MetadataReader Reader { get; }
        internal MetadataMethodSemanticsAssociationResult
            MethodSemantics { get; }

        internal static Asset Open(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            FileStream stream = File.OpenRead(path);
            var peReader = new PEReader(stream);
            AssemblyInspectionSession? assembly = null;
            MetadataOperationContext? operation = null;
            MetadataDeclarationSession? declaration = null;
            try
            {
                MetadataReader reader =
                    peReader.GetMetadataReader();
                assembly = AssemblyInspectionSession.Open(path);
                operation = new MetadataOperationContext(
                    MetadataOperationPolicy.Unbounded);
                declaration =
                    assembly.CreateDeclarationSession(operation);
                MetadataMethodSemanticsAssociationResult
                    methodSemantics =
                        declaration.MethodSemanticsAssociations.Post();
                return new(
                    stream,
                    peReader,
                    assembly,
                    operation,
                    declaration,
                    reader,
                    methodSemantics);
            }
            catch
            {
                declaration?.Dispose();
                operation?.Dispose();
                assembly?.Dispose();
                peReader.Dispose();
                stream.Dispose();
                throw;
            }
        }

        internal MetadataMethodGroupInspection.Analysis Prepare(
            MetadataTypeDefinitionName declaringType,
            string methodName) =>
            MetadataMethodGroupInspection.Prepare(
                Reader,
                MethodSemantics,
                declaringType,
                methodName)
            is MetadataMethodGroupInspection.PreparationResult
                .Prepared prepared
                    ? prepared.Model
                    : throw new InvalidOperationException(
                        "The scorecard model did not prepare.");

        public void Dispose()
        {
            _declaration.Dispose();
            _operation.Dispose();
            _assembly.Dispose();
            _peReader.Dispose();
            _stream.Dispose();
        }
    }
}
