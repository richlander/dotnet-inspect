using System.Diagnostics;
using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using DotnetInspector.Sections;
using ILInspector.Metadata;
using NLinq;

namespace DotnetInspector.PerformanceOracles;

public enum MemberGroupTerminal
{
    Count,
    Rows,
    CountAndRows,
}

public sealed record MemberGroupScorecardScenario(
    string Name,
    string Assembly,
    string Namespace,
    string TypeName,
    string MethodName,
    MetadataMethodAccessibilityFilter Accessibility,
    MetadataMethodReceiverFilter Receiver,
    int? ExpectedCount);

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
    string Hash,
    int Count,
    int Rows,
    int TypeMethods);

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
    public const string SystemTextJson = "System.Text.Json";
    public const string CoreLib = "System.Private.CoreLib";
    private const int MaximumRows = 100;
    private const int MaximumMembers = 100_000;
    private const int MaximumRetainedTextCharacters = 20_000_000;

    public static IReadOnlyList<MemberGroupScorecardScenario>
        Scenarios { get; } =
        [
            Json("Deserialize public/all", "JsonSerializer", "Deserialize", MetadataMethodAccessibilityFilter.Public, MetadataMethodReceiverFilter.All, 40),
            Json("Deserialize public/static", "JsonSerializer", "Deserialize", MetadataMethodAccessibilityFilter.Public, MetadataMethodReceiverFilter.Static, 25),
            Json("Deserialize public/extension", "JsonSerializer", "Deserialize", MetadataMethodAccessibilityFilter.Public, MetadataMethodReceiverFilter.Extension, 15),
            Json("Deserialize public/this", "JsonSerializer", "Deserialize", MetadataMethodAccessibilityFilter.Public, MetadataMethodReceiverFilter.This, 0),
            Json("Parse public/all", "JsonDocument", "Parse", MetadataMethodAccessibilityFilter.Public, MetadataMethodReceiverFilter.All, 5),
            Json("Parse private/all", "JsonDocument", "Parse", MetadataMethodAccessibilityFilter.Private, MetadataMethodReceiverFilter.All, 2),
            Json("Parse all/all", "JsonDocument", "Parse", MetadataMethodAccessibilityFilter.All, MetadataMethodReceiverFilter.All, 7),
            new("CoreLib String.Concat public/all", CoreLib, "System", "String", "Concat", MetadataMethodAccessibilityFilter.Public, MetadataMethodReceiverFilter.All, 15),
            new("CoreLib Convert.ToString public/all", CoreLib, "System", "Convert", "ToString", MetadataMethodAccessibilityFilter.Public, MetadataMethodReceiverFilter.All, 36),
            new("CoreLib MemoryExtensions.IndexOfAny public/extension", CoreLib, "System", "MemoryExtensions", "IndexOfAny", MetadataMethodAccessibilityFilter.Public, MetadataMethodReceiverFilter.Extension, 13),
            new("CoreLib Vector128.Create public/all", CoreLib, "System.Runtime.Intrinsics", "Vector128", "Create", MetadataMethodAccessibilityFilter.Public, MetadataMethodReceiverFilter.All, 40),
            new("CoreLib AdvSimd.Store public/all", CoreLib, "System.Runtime.Intrinsics.Arm", "AdvSimd", "Store", MetadataMethodAccessibilityFilter.Public, MetadataMethodReceiverFilter.All, 41),
        ];

    private static MemberGroupScorecardScenario Json(
        string name, string type, string method,
        MetadataMethodAccessibilityFilter accessibility,
        MetadataMethodReceiverFilter receiver, int expected) =>
        new(name, SystemTextJson, SystemTextJson, type, method, accessibility, receiver, expected);

    private static readonly MemberGroupTerminal[] s_terminals =
        [MemberGroupTerminal.Count, MemberGroupTerminal.Rows, MemberGroupTerminal.CountAndRows];

    // Model columns run over a prepared Analysis (Kernel) or prepare it first
    // on the shared open session (Composed). Host columns always start from
    // the request and the assembly bytes, so they appear in Composed only.
    private static IReadOnlyList<Column> ModelColumns { get; } =
        [
            new("Planner", Planner, Host: false),
            new("LINQ", Linq, Host: false),
            new("NLinq", NLinq, Host: false),
            new("NLinq-idiom", NLinqIdiomatic, Host: false),
        ];

    private static IReadOnlyList<Column> HostColumns { get; } =
        [
            new("Route", Route, Host: true),
            new("Fresh-session", FreshSession, Host: true),
        ];

    private static IEnumerable<Column> AllColumns => ModelColumns.Concat(HostColumns);

    public static MemberGroupScorecardCheck Check(IReadOnlyDictionary<string, string> assemblies)
    {
        using var assets = Assets.Open(assemblies);
        return Check(assets);
    }

    public static MemberGroupScorecardResult Measure(IReadOnlyDictionary<string, string> assemblies)
    {
        using var assets = Assets.Open(assemblies);
        MemberGroupScorecardCheck check = Check(assets);
        if (!check.Agrees)
            return new(check, []);

        var cells = new List<MemberGroupScorecardCell>();
        int index = 0;
        foreach (MemberGroupScorecardScenario scenario in assets.Scenarios)
        {
            Asset asset = assets[scenario.Assembly];
            MetadataTypeDefinitionName declaringType = TypeName(scenario);
            MetadataMethodGroupInspection.Analysis model = asset.Prepare(declaringType, scenario.MethodName);
            cells.Add(MeasurePreparation(asset, scenario.Name, declaringType, scenario.MethodName));
            foreach (MemberGroupTerminal terminal in s_terminals)
            {
                bool composedFirst = ((index++) & 1) != 0;
                if (composedFirst)
                    MeasureComposed();
                MeasureKernel();
                if (!composedFirst)
                    MeasureComposed();
                MeasurePlan();

                void MeasureKernel() =>
                    MeasurePhase(cells, scenario, terminal, "Kernel", ModelColumns,
                        column => column.Execute(asset, scenario, declaringType, model, terminal));

                void MeasureComposed() =>
                    MeasurePhase(cells, scenario, terminal, "Composed", [.. AllColumns],
                        column => column.Execute(
                            asset, scenario, declaringType,
                            column.Host ? null : asset.Prepare(declaringType, scenario.MethodName),
                            terminal));

                void MeasurePlan() =>
                    MeasurePhase(cells, scenario, terminal, "Plan", [new("Route-plan", RoutePlan, Host: true)],
                        column => column.Execute(asset, scenario, declaringType, null, terminal));
            }
        }

        return new(check, cells);
    }

    private static readonly string[] s_reportColumns =
        ["Planner", "LINQ", "NLinq", "NLinq-idiom", "Route", "Fresh-session"];

    public static string Report(MemberGroupScorecardResult result)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        writer.WriteLine($"# answers: {result.Check.Compared} compared, {result.Check.Mismatches.Count} mismatches");
        foreach (MemberGroupScorecardAnswerHash answer in result.Check.AnswerHashes)
            writer.WriteLine($"# answer: {answer.Scenario} / {answer.Terminal} = {answer.Hash} (count {answer.Count}, rows {answer.Rows}, type methods {answer.TypeMethods})");
        foreach (MemberGroupScorecardMismatch mismatch in result.Check.Mismatches)
            writer.WriteLine($"mismatch\t{mismatch.Scenario}\t{mismatch.Terminal}\t{mismatch.Column}");
        if (!result.Check.Agrees)
            return writer.ToString();

        writer.WriteLine("| Scenario | Terminal | Phase | " + string.Join(" | ", s_reportColumns) + " | Route plan |");
        writer.WriteLine("| --- | --- | --- |" + string.Concat(Enumerable.Repeat(" ---: |", s_reportColumns.Length + 1)));
        foreach (IGrouping<string, MemberGroupScorecardCell> scenario
            in result.Cells.Where(c => c.Phase != "Preparation").GroupBy(c => c.Scenario))
        {
            foreach (MemberGroupTerminal terminal in s_terminals)
            {
                foreach (string phase in new[] { "Kernel", "Composed" })
                {
                    MemberGroupScorecardCell[] selected =
                        [.. scenario.Where(c => c.Terminal == terminal.ToString() && c.Phase == phase)];
                    double oracle = selected.Single(c => c.Column == "NLinq").Microseconds;
                    var parts = new List<string>();
                    foreach (string column in s_reportColumns)
                    {
                        MemberGroupScorecardCell? cell = selected.SingleOrDefault(c => c.Column == column);
                        parts.Add(cell is null
                            ? "-"
                            : $"{cell.Microseconds:F2} us ({cell.Microseconds / oracle:F2}x)");
                    }
                    MemberGroupScorecardCell? plan = phase == "Kernel"
                        ? scenario.SingleOrDefault(c => c.Terminal == terminal.ToString() && c.Phase == "Plan")
                        : null;
                    parts.Add(plan is null ? "-" : $"{plan.Microseconds:F2} us");
                    writer.WriteLine($"| {scenario.Key} | {terminal} | {phase} | " + string.Join(" | ", parts) + " |");
                }
            }
        }

        writer.WriteLine();
        writer.WriteLine("| Scenario | Terminal | Phase | " + string.Join(" | ", s_reportColumns.Select(c => c + " B")) + " | Route plan B |");
        writer.WriteLine("| --- | --- | --- |" + string.Concat(Enumerable.Repeat(" ---: |", s_reportColumns.Length + 1)));
        foreach (IGrouping<string, MemberGroupScorecardCell> scenario
            in result.Cells.Where(c => c.Phase != "Preparation").GroupBy(c => c.Scenario))
        {
            foreach (MemberGroupTerminal terminal in s_terminals)
            {
                foreach (string phase in new[] { "Kernel", "Composed" })
                {
                    MemberGroupScorecardCell[] selected =
                        [.. scenario.Where(c => c.Terminal == terminal.ToString() && c.Phase == phase)];
                    var parts = s_reportColumns
                        .Select(column => selected.SingleOrDefault(c => c.Column == column) is { } cell
                            ? cell.AllocatedBytes.ToString("N0", CultureInfo.InvariantCulture)
                            : "-")
                        .ToList();
                    MemberGroupScorecardCell? plan = phase == "Kernel"
                        ? scenario.SingleOrDefault(c => c.Terminal == terminal.ToString() && c.Phase == "Plan")
                        : null;
                    parts.Add(plan is null ? "-" : plan.AllocatedBytes.ToString("N0", CultureInfo.InvariantCulture));
                    writer.WriteLine($"| {scenario.Key} | {terminal} | {phase} | " + string.Join(" | ", parts) + " |");
                }
            }
        }

        writer.WriteLine();
        writer.WriteLine("| Scenario | Preparation | Preparation alloc |");
        writer.WriteLine("| --- | ---: | ---: |");
        foreach (MemberGroupScorecardCell cell in result.Cells.Where(c => c.Phase == "Preparation"))
            writer.WriteLine($"| {cell.Scenario} | {cell.Microseconds:F2} us | {cell.AllocatedBytes:N0} B |");

        writer.WriteLine();
        writer.WriteLine("# raw\tscenario\tterminal\tphase\tcolumn\tus\tbytes");
        foreach (MemberGroupScorecardCell cell in result.Cells)
            writer.WriteLine($"raw\t{cell.Scenario}\t{cell.Terminal}\t{cell.Phase}\t{cell.Column}\t{cell.Microseconds:F3}\t{cell.AllocatedBytes}");
        return writer.ToString();
    }

    private static MemberGroupScorecardCheck Check(Assets assets)
    {
        var mismatches = new List<MemberGroupScorecardMismatch>();
        var answerHashes = new List<MemberGroupScorecardAnswerHash>();
        int compared = 0;
        foreach (MemberGroupScorecardScenario scenario in assets.Scenarios)
        {
            Asset asset = assets[scenario.Assembly];
            MetadataTypeDefinitionName declaringType = TypeName(scenario);
            foreach (MemberGroupTerminal terminal in s_terminals)
            {
                MemberGroupProjectionAnswer expected = Normalize(
                    Planner(asset, scenario, declaringType, asset.Prepare(declaringType, scenario.MethodName), terminal),
                    terminal);
                if (terminal is MemberGroupTerminal.Count
                    && scenario.ExpectedCount is { } expectedCount
                    && expected.Count != expectedCount)
                {
                    throw new InvalidOperationException(
                        $"{scenario.Name} returned {expected.Count}, expected {expectedCount}.");
                }
                answerHashes.Add(new(scenario.Name, terminal.ToString(), AnswerHash(expected), expected.Count, expected.Rows.Count,
                    asset.Prepare(declaringType, scenario.MethodName).Methods.Count));
                foreach (Column column in AllColumns)
                {
                    MemberGroupProjectionAnswer actual = Normalize(
                        column.Execute(
                            asset, scenario, declaringType,
                            column.Host ? null : asset.Prepare(declaringType, scenario.MethodName),
                            terminal),
                        terminal);
                    compared++;
                    if (!Same(expected, actual))
                        mismatches.Add(new(scenario.Name, terminal.ToString(), column.Name));
                }
            }
        }

        return new(compared, mismatches, answerHashes);
    }

    private static object Planner(
        Asset asset, MemberGroupScorecardScenario scenario, MetadataTypeDefinitionName type,
        MetadataMethodGroupInspection.Analysis? model, MemberGroupTerminal terminal) =>
        MetadataMethodGroupInspection.Execute(
            model!,
            Selection(model!, scenario),
            startOrdinal: 0,
            maximumRows: MaximumRows,
            materializeRows: terminal is not MemberGroupTerminal.Count,
            maximumMembers: MaximumMembers,
            maximumRetainedTextCharacters: MaximumRetainedTextCharacters);

    private static object Linq(
        Asset asset, MemberGroupScorecardScenario scenario, MetadataTypeDefinitionName type,
        MetadataMethodGroupInspection.Analysis? model, MemberGroupTerminal terminal)
    {
        MetadataMethodGroupInspection.Fold completed =
            model!.Methods
                .Select(static handle => handle)
                .Aggregate(
                    Fold(model, scenario, terminal),
                    static (current, handle) =>
                    {
                        _ = current.Accept(handle);
                        return current;
                    });
        return completed.Complete();
    }

    private static object NLinq(
        Asset asset, MemberGroupScorecardScenario scenario, MetadataTypeDefinitionName type,
        MetadataMethodGroupInspection.Analysis? model, MemberGroupTerminal terminal)
    {
        var source = new MethodHandles(model!.Methods);
        MetadataMethodGroupInspection.Fold completed =
            source.Fold<MethodHandles, MethodDefinitionHandle, MetadataMethodGroupInspection.Fold, Accept>(
                Fold(model, scenario, terminal),
                default);
        return completed.Complete();
    }

    private static object NLinqIdiomatic(
        Asset asset, MemberGroupScorecardScenario scenario, MetadataTypeDefinitionName type,
        MetadataMethodGroupInspection.Analysis? model, MemberGroupTerminal terminal) =>
        MemberGroupIdiomatic.Execute(
            model!,
            Selection(model!, scenario),
            startOrdinal: 0,
            maximumRows: MaximumRows,
            materializeRows: terminal is not MemberGroupTerminal.Count,
            maximumMembers: MaximumMembers,
            maximumRetainedTextCharacters: MaximumRetainedTextCharacters);

    private static object Route(
        Asset asset, MemberGroupScorecardScenario scenario, MetadataTypeDefinitionName type,
        MetadataMethodGroupInspection.Analysis? model, MemberGroupTerminal terminal) =>
        asset.Route.Execute(scenario, type, terminal, MaximumRows);

    private static object RoutePlan(
        Asset asset, MemberGroupScorecardScenario scenario, MetadataTypeDefinitionName type,
        MetadataMethodGroupInspection.Analysis? model, MemberGroupTerminal terminal) =>
        MemberGroupRoute.Plan(scenario, type, terminal, MaximumRows);

    private static object FreshSession(
        Asset asset, MemberGroupScorecardScenario scenario, MetadataTypeDefinitionName type,
        MetadataMethodGroupInspection.Analysis? model, MemberGroupTerminal terminal) =>
        asset.Route.FreshSession(scenario, type, terminal, MaximumRows);

    private static MetadataMethodGroupInspection.Selection Selection(
        MetadataMethodGroupInspection.Analysis model, MemberGroupScorecardScenario scenario) =>
        new(model, scenario.Accessibility, scenario.Receiver, includeHidden: false);

    private static MetadataMethodGroupInspection.Fold Fold(
        MetadataMethodGroupInspection.Analysis model, MemberGroupScorecardScenario scenario, MemberGroupTerminal terminal) =>
        new(
            model,
            Selection(model, scenario),
            startOrdinal: 0,
            maximumRows: MaximumRows,
            materializeRows: terminal is not MemberGroupTerminal.Count,
            maximumMembers: MaximumMembers,
            maximumRetainedTextCharacters: MaximumRetainedTextCharacters);

    private static MemberGroupProjectionAnswer Normalize(object outcome, MemberGroupTerminal terminal)
    {
        MemberGroupProjectionAnswer answer = outcome switch
        {
            MetadataMethodGroupInspectionOutcome.Read read => new(
                read.Count,
                [.. read.Rows.Select(static row => new MemberGroupProjectionRow(
                    row.MetadataToken, row.DisplaySignature, row.CanonicalSignature,
                    row.Fingerprint, row.Accessibility, row.Receiver))],
                read.NextOrdinal,
                read.ContinuationOutOfRange,
                read.IncompleteRetainedTextCharacters,
                read.RowsFailed),
            MemberOverloadPopulationInspectionOutcome.Available available => Normalize(available.Content.Overloads),
            _ => throw new InvalidOperationException($"Expected a read outcome, got {outcome}."),
        };
        // A Rows-only request makes no Count claim; the fold always counts.
        return terminal is MemberGroupTerminal.Rows ? answer with { Count = -1 } : answer;
    }

    private static MemberGroupProjectionAnswer Normalize(MemberOverloadPopulationResult result)
    {
        int count = result.Count is MemberOverloadCountOutcome.Counted counted ? counted.Value : -1;
        return result.Rows switch
        {
            null => new(count, [], null, false, null, false),
            MemberOverloadRowsOutcome.Read read => new(
                count,
                [.. read.Items.Select(static item => new MemberGroupProjectionRow(
                    item.MetadataToken,
                    item.DisplaySignature.ToString(),
                    item.CanonicalSignature.ToString(),
                    item.Fingerprint.ToString(),
                    item.Accessibility.ToString(),
                    (MetadataMethodReceiver)(int)item.Receiver))],
                read.Continuation?.NextOrdinal,
                false, null, false),
            MemberOverloadRowsOutcome.Rejected { Reason: MemberOverloadRowsRejection.ContinuationOutOfRange } =>
                new(count, [], null, true, null, false),
            MemberOverloadRowsOutcome.Incomplete incomplete => new(count, [], null, false, incomplete.Measured, false),
            MemberOverloadRowsOutcome.Failed => new(count, [], null, false, null, true),
            _ => throw new InvalidOperationException($"Unexpected rows outcome {result.Rows}."),
        };
    }

    private static bool Same(MemberGroupProjectionAnswer first, MemberGroupProjectionAnswer second) =>
        first.Count == second.Count
        && first.Rows.SequenceEqual(second.Rows)
        && first.NextOrdinal == second.NextOrdinal
        && first.ContinuationOutOfRange == second.ContinuationOutOfRange
        && first.IncompleteRetainedTextCharacters == second.IncompleteRetainedTextCharacters
        && first.RowsFailed == second.RowsFailed;

    private static string AnswerHash(MemberGroupProjectionAnswer answer)
    {
        ulong hash = 14695981039346656037;
        AddLong(answer.Count);
        AddLong(answer.NextOrdinal ?? -1);
        AddLong(answer.ContinuationOutOfRange ? 1 : 0);
        AddLong(answer.IncompleteRetainedTextCharacters ?? -1);
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

    private static MetadataTypeDefinitionName TypeName(MemberGroupScorecardScenario scenario) =>
        MetadataTypeDefinitionName.Create(scenario.Namespace, [scenario.TypeName])
            is MetadataTypeDefinitionNameResult.Valid valid
            ? valid.Name
            : throw new InvalidOperationException("The scorecard Type name was invalid.");

    private static void MeasurePhase(
        List<MemberGroupScorecardCell> cells,
        MemberGroupScorecardScenario scenario,
        MemberGroupTerminal terminal,
        string phase,
        IReadOnlyList<Column> columns,
        Func<Column, object> execute)
    {
        var times = columns.ToDictionary(static column => column.Name, static _ => new List<double>());
        var allocations = columns.ToDictionary(static column => column.Name, static _ => new List<long>());
        foreach (Column column in columns)
        {
            for (int warmup = 0; warmup < 5; warmup++)
                GC.KeepAlive(execute(column));
        }

        for (int round = 0; round < 6; round++)
        {
            for (int offset = 0; offset < columns.Count; offset++)
            {
                Column column = columns[(round + offset) % columns.Count];
                Measurement measurement = Measure(() => execute(column));
                times[column.Name].Add(measurement.Microseconds);
                allocations[column.Name].Add(measurement.AllocatedBytes);
            }
        }

        foreach (Column column in columns)
        {
            cells.Add(new(
                scenario.Name,
                terminal.ToString(),
                phase,
                column.Name,
                Median(times[column.Name]),
                Median(allocations[column.Name])));
        }
    }

    private static MemberGroupScorecardCell MeasurePreparation(
        Asset asset, string scenario, MetadataTypeDefinitionName declaringType, string methodName)
    {
        for (int warmup = 0; warmup < 5; warmup++)
            GC.KeepAlive(asset.Prepare(declaringType, methodName));
        var times = new List<double>(6);
        var allocations = new List<long>(6);
        for (int round = 0; round < 6; round++)
        {
            Measurement measurement = Measure(() => asset.Prepare(declaringType, methodName));
            times.Add(measurement.Microseconds);
            allocations.Add(measurement.AllocatedBytes);
        }
        return new(scenario, "", "Preparation", "Shared", Median(times), Median(allocations));
    }

    private static Measurement Measure(Func<object> execute)
    {
        var times = new double[101];
        var allocations = new long[101];
        for (int sample = 0; sample < times.Length; sample++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            object result = execute();
            long elapsed = Stopwatch.GetTimestamp() - started;
            allocations[sample] = GC.GetAllocatedBytesForCurrentThread() - before;
            times[sample] = elapsed * 1_000_000.0 / Stopwatch.Frequency;
            GC.KeepAlive(result);
        }
        Array.Sort(times);
        Array.Sort(allocations);
        return new(times[times.Length / 2], allocations[allocations.Length / 2]);
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

    private sealed record Column(string Name, Executor Execute, bool Host);

    private delegate object Executor(
        Asset asset,
        MemberGroupScorecardScenario scenario,
        MetadataTypeDefinitionName declaringType,
        MetadataMethodGroupInspection.Analysis? model,
        MemberGroupTerminal terminal);

    private readonly record struct Measurement(double Microseconds, long AllocatedBytes);

    private struct MethodHandles : NLinq.IEnumerator<MethodHandles, MethodDefinitionHandle>
    {
        private MethodDefinitionHandleCollection.Enumerator _handles;

        internal MethodHandles(MethodDefinitionHandleCollection handles)
        {
            _handles = handles.GetEnumerator();
        }

        public MethodDefinitionHandle TryGetNext(out bool hasMore)
        {
            hasMore = _handles.MoveNext();
            return hasMore ? _handles.Current : default;
        }
    }

    private readonly struct Accept
        : IFunc<MetadataMethodGroupInspection.Fold, MethodDefinitionHandle, MetadataMethodGroupInspection.Fold>
    {
        public MetadataMethodGroupInspection.Fold Invoke(
            MetadataMethodGroupInspection.Fold fold, MethodDefinitionHandle handle)
        {
            _ = fold.Accept(handle);
            return fold;
        }
    }

    private sealed class Assets : IDisposable
    {
        private readonly Dictionary<string, Asset> _assets;

        private Assets(Dictionary<string, Asset> assets) => _assets = assets;

        internal Asset this[string key] => _assets[key];

        internal IEnumerable<MemberGroupScorecardScenario> Scenarios =>
            MemberGroupPopulation.Scenarios.Where(s => _assets.ContainsKey(s.Assembly));

        internal static Assets Open(IReadOnlyDictionary<string, string> paths) =>
            new(paths.ToDictionary(pair => pair.Key, pair => Asset.Open(pair.Value)));

        public void Dispose()
        {
            foreach (Asset asset in _assets.Values)
                asset.Dispose();
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
            MetadataMethodSemanticsAssociationResult methodSemantics,
            MemberGroupRoute route)
        {
            _stream = stream;
            _peReader = peReader;
            _assembly = assembly;
            _operation = operation;
            _declaration = declaration;
            Reader = reader;
            MethodSemantics = methodSemantics;
            Route = route;
        }

        internal MetadataReader Reader { get; }
        internal MetadataMethodSemanticsAssociationResult MethodSemantics { get; }
        internal MemberGroupRoute Route { get; }

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
                MetadataReader reader = peReader.GetMetadataReader();
                assembly = AssemblyInspectionSession.Open(path);
                operation = new MetadataOperationContext(MetadataOperationPolicy.Unbounded);
                declaration = assembly.CreateDeclarationSession(operation);
                MetadataMethodSemanticsAssociationResult methodSemantics =
                    declaration.MethodSemanticsAssociations.Post();
                return new(stream, peReader, assembly, operation, declaration, reader, methodSemantics,
                    MemberGroupRoute.Create(path));
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
            MetadataTypeDefinitionName declaringType, string methodName) =>
            MetadataMethodGroupInspection.Prepare(Reader, MethodSemantics, declaringType, methodName)
                is MetadataMethodGroupInspection.PreparationResult.Prepared prepared
                    ? prepared.Model
                    : throw new InvalidOperationException("The scorecard model did not prepare.");

        public void Dispose()
        {
            Route.Dispose();
            _declaration.Dispose();
            _operation.Dispose();
            _assembly.Dispose();
            _peReader.Dispose();
            _stream.Dispose();
        }
    }
}
