using System.Reflection.Metadata;

using ILInspector.Metadata;
using NLinq;

namespace DotnetInspector.PerformanceOracles;

public enum MemberExactSelectorKind
{
    Ordinal,
    Fingerprint,
}

public sealed record MemberExactScorecardScenario(
    string Name,
    string TypeName,
    string MethodName,
    MetadataMethodAccessibilityFilter Accessibility,
    MetadataMethodReceiverFilter Receiver,
    int ExpectedCount,
    int TargetOrdinal,
    MemberExactSelectorKind Selector);

public sealed record MemberExactProjectionAnswer(
    int PopulationCount,
    int BaselineOrdinal,
    MemberGroupProjectionRow Row);

public sealed record MemberExactScorecardCheck(
    int Compared,
    IReadOnlyList<MemberGroupScorecardMismatch> Mismatches,
    IReadOnlyList<MemberGroupScorecardAnswerHash> AnswerHashes)
{
    public bool Agrees => Mismatches.Count == 0;
}

public sealed record MemberExactScorecardResult(
    MemberExactScorecardCheck Check,
    IReadOnlyList<MemberGroupScorecardCell> Cells);

public static partial class MemberGroupPopulation
{
    public static IReadOnlyList<MemberExactScorecardScenario>
        ExactScenarios
    { get; } =
        [
            ExactScenario(
                "Serialize first",
                "JsonSerializer",
                "Serialize",
                expectedCount: 15,
                targetOrdinal: 1,
                MemberExactSelectorKind.Ordinal),
            ExactScenario(
                "Serialize middle",
                "JsonSerializer",
                "Serialize",
                expectedCount: 15,
                targetOrdinal: 6,
                MemberExactSelectorKind.Ordinal),
            ExactScenario(
                "Serialize last",
                "JsonSerializer",
                "Serialize",
                expectedCount: 15,
                targetOrdinal: 15,
                MemberExactSelectorKind.Ordinal),
            ExactScenario(
                "Serialize fingerprint",
                "JsonSerializer",
                "Serialize",
                expectedCount: 15,
                targetOrdinal: 6,
                MemberExactSelectorKind.Fingerprint),
            ExactScenario(
                "Deserialize first",
                "JsonSerializer",
                "Deserialize",
                expectedCount: 40,
                targetOrdinal: 1,
                MemberExactSelectorKind.Ordinal),
            ExactScenario(
                "Deserialize middle",
                "JsonSerializer",
                "Deserialize",
                expectedCount: 40,
                targetOrdinal: 20,
                MemberExactSelectorKind.Ordinal),
            ExactScenario(
                "Deserialize last",
                "JsonSerializer",
                "Deserialize",
                expectedCount: 40,
                targetOrdinal: 40,
                MemberExactSelectorKind.Ordinal),
            ExactScenario(
                "Deserialize fingerprint",
                "JsonSerializer",
                "Deserialize",
                expectedCount: 40,
                targetOrdinal: 20,
                MemberExactSelectorKind.Fingerprint),
        ];

    private static IReadOnlyList<ExactColumn> ExactColumns { get; } =
        [
            new("LINQ", ExactLinq),
            new("NLinq", ExactNLinq),
            new("Planner", ExactPlanner),
        ];

    public static MemberExactScorecardCheck CheckExact(string path)
    {
        using var asset = Asset.Open(path);
        return CheckExact(asset);
    }

    public static MemberExactScorecardResult MeasureExact(string path)
    {
        using var asset = Asset.Open(path);
        MemberExactScorecardCheck check = CheckExact(asset);
        if (!check.Agrees)
            return new(check, []);

        var cells = new List<MemberGroupScorecardCell>();
        for (int scenarioIndex = 0;
            scenarioIndex < ExactScenarios.Count;
            scenarioIndex++)
        {
            MemberExactScorecardScenario scenario =
                ExactScenarios[scenarioIndex];
            MetadataTypeDefinitionName declaringType =
                TypeName(scenario.TypeName);
            MetadataMethodGroupInspection.Analysis model =
                asset.Prepare(declaringType, scenario.MethodName);
            string fingerprint = TargetFingerprint(model, scenario);
            bool composedFirst = (scenarioIndex & 1) != 0;
            if (composedFirst)
                MeasureComposed();
            MeasureKernel();
            if (!composedFirst)
                MeasureComposed();

            void MeasureKernel() =>
                MeasureExactPhase(
                    cells,
                    scenario,
                    "Kernel",
                    column => column.Execute(
                        model,
                        scenario,
                        fingerprint));

            void MeasureComposed() =>
                MeasureExactPhase(
                    cells,
                    scenario,
                    "Composed",
                    column =>
                    {
                        MetadataMethodGroupInspection.Analysis fresh =
                            asset.Prepare(
                                declaringType,
                                scenario.MethodName);
                        return column.Execute(
                            fresh,
                            scenario,
                            fingerprint);
                    });
        }

        return new(check, cells);
    }

    public static MemberExactProjectionAnswer ExecuteExact(
        string path,
        MemberExactScorecardScenario scenario,
        string column)
    {
        using var asset = Asset.Open(path);
        MetadataMethodGroupInspection.Analysis model =
            asset.Prepare(
                TypeName(scenario.TypeName),
                scenario.MethodName);
        string fingerprint = TargetFingerprint(model, scenario);
        ExactColumn selected = ExactColumns.Single(item =>
            item.Name == column);
        return selected.Execute(model, scenario, fingerprint);
    }

    public static string ReportExact(MemberExactScorecardResult result)
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
            "| Scenario | Selector | Phase | LINQ | NLinq | Planner | "
                + "LINQ alloc | NLinq alloc | Planner alloc |");
        writer.WriteLine(
            "| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (MemberExactScorecardScenario scenario
            in ExactScenarios)
        {
            foreach (string phase in new[] { "Kernel", "Composed" })
            {
                string terminal = scenario.Selector.ToString();
                MemberGroupScorecardCell[] selected =
                    [.. result.Cells.Where(cell =>
                        cell.Scenario == scenario.Name
                        && cell.Terminal == terminal
                        && cell.Phase == phase)];
                MemberGroupScorecardCell linq =
                    selected.Single(cell => cell.Column == "LINQ");
                MemberGroupScorecardCell nlinq =
                    selected.Single(cell => cell.Column == "NLinq");
                MemberGroupScorecardCell planner =
                    selected.Single(cell => cell.Column == "Planner");
                writer.WriteLine(
                    $"| {scenario.Name} | {terminal} | {phase} | "
                        + $"{linq.Microseconds / planner.Microseconds:F2}x "
                        + $"({linq.Microseconds:F3} us) | "
                        + $"{nlinq.Microseconds / planner.Microseconds:F2}x "
                        + $"({nlinq.Microseconds:F3} us) | "
                        + $"1.00x "
                        + $"({planner.Microseconds:F3} us) | "
                        + $"{linq.AllocatedBytes:N0} B | "
                        + $"{nlinq.AllocatedBytes:N0} B | "
                        + $"{planner.AllocatedBytes:N0} B |");
            }
        }

        writer.WriteLine();
        writer.WriteLine(
            "| Selector | Phase | LINQ/Planner geo mean | "
                + "NLinq/Planner geo mean | Planner |");
        writer.WriteLine("| --- | --- | ---: | ---: | ---: |");
        foreach (MemberExactSelectorKind selector
            in Enum.GetValues<MemberExactSelectorKind>())
        {
            foreach (string phase in new[] { "Kernel", "Composed" })
            {
                MemberGroupScorecardCell[] selected =
                    [.. result.Cells.Where(cell =>
                        cell.Terminal == selector.ToString()
                        && cell.Phase == phase)];
                writer.WriteLine(
                    $"| {selector} | {phase} | "
                        + $"{GeometricRatio(
                            selected,
                            "LINQ",
                            "Planner"):F2}x | "
                        + $"{GeometricRatio(
                            selected,
                            "NLinq",
                            "Planner"):F2}x | "
                        + "1.00x |");
            }
        }
        return writer.ToString();
    }

    private static MemberExactScorecardCheck CheckExact(Asset asset)
    {
        var mismatches =
            new List<MemberGroupScorecardMismatch>();
        var answerHashes =
            new List<MemberGroupScorecardAnswerHash>();
        int compared = 0;
        foreach (MemberExactScorecardScenario scenario
            in ExactScenarios)
        {
            MetadataMethodGroupInspection.Analysis model =
                asset.Prepare(
                    TypeName(scenario.TypeName),
                    scenario.MethodName);
            string fingerprint = TargetFingerprint(model, scenario);
            MemberExactProjectionAnswer expected =
                ExactNLinq(model, scenario, fingerprint);
            if (expected.PopulationCount != scenario.ExpectedCount)
            {
                throw new InvalidOperationException(
                    $"{scenario.Name} returned "
                        + $"{expected.PopulationCount}, expected "
                        + $"{scenario.ExpectedCount}.");
            }
            answerHashes.Add(
                new(
                    scenario.Name,
                    scenario.Selector.ToString(),
                    ExactAnswerHash(expected)));
            foreach (ExactColumn column in ExactColumns)
            {
                MemberExactProjectionAnswer actual =
                    column.Execute(model, scenario, fingerprint);
                compared++;
                if (actual != expected)
                {
                    mismatches.Add(
                        new(
                            scenario.Name,
                            scenario.Selector.ToString(),
                            column.Name));
                }
            }
        }
        return new(compared, mismatches, answerHashes);
    }

    private static MemberExactProjectionAnswer ExactLinq(
        MetadataMethodGroupInspection.Analysis model,
        MemberExactScorecardScenario scenario,
        string fingerprint)
    {
        MetadataMethodGroupInspection.Fold completed =
            model.Methods
                .Select(static handle => handle)
                .Aggregate(
                    ExactFold(model, scenario),
                    static (current, handle) =>
                    {
                        _ = current.Accept(handle);
                        return current;
                    });
        return ProjectExact(
            completed.Complete(),
            scenario,
            fingerprint);
    }

    private static MemberExactProjectionAnswer ExactNLinq(
        MetadataMethodGroupInspection.Analysis model,
        MemberExactScorecardScenario scenario,
        string fingerprint)
    {
        var source = new MethodHandles(model.Methods);
        MetadataMethodGroupInspection.Fold completed =
            source.Fold<
                MethodHandles,
                MethodDefinitionHandle,
                MetadataMethodGroupInspection.Fold,
                Accept>(
                    ExactFold(model, scenario),
                    default);
        return ProjectExact(
            completed.Complete(),
            scenario,
            fingerprint);
    }

    private static MemberExactProjectionAnswer ExactPlanner(
        MetadataMethodGroupInspection.Analysis model,
        MemberExactScorecardScenario scenario,
        string fingerprint) =>
        ProjectExact(
            MetadataMethodGroupInspection.Execute(
                model,
                Selection(
                    model,
                    scenario.Accessibility,
                    scenario.Receiver),
                startOrdinal: 0,
                maximumRows: MaximumRows(scenario),
                materializeRows: true,
                maximumMembers: 100_000,
                maximumRetainedTextCharacters: 20_000_000),
            scenario,
            fingerprint);

    private static MetadataMethodGroupInspection.Fold ExactFold(
        MetadataMethodGroupInspection.Analysis model,
        MemberExactScorecardScenario scenario) =>
        new(
            model,
            Selection(
                model,
                scenario.Accessibility,
                scenario.Receiver),
            startOrdinal: 0,
            maximumRows: MaximumRows(scenario),
            materializeRows: true,
            maximumMembers: 100_000,
            maximumRetainedTextCharacters: 20_000_000);

    private static MemberExactProjectionAnswer ProjectExact(
        MetadataMethodGroupInspectionOutcome outcome,
        MemberExactScorecardScenario scenario,
        string fingerprint)
    {
        if (outcome
            is not MetadataMethodGroupInspectionOutcome.Read read)
        {
            throw new InvalidOperationException(
                $"Expected a read outcome, got {outcome}.");
        }
        if (read.Count != scenario.ExpectedCount)
        {
            throw new InvalidOperationException(
                $"{scenario.Name} returned {read.Count} members, "
                    + $"expected {scenario.ExpectedCount}.");
        }

        (MetadataMethodGroupRow Row, int Ordinal) selected =
            scenario.Selector is MemberExactSelectorKind.Ordinal
                ? (
                    read.Rows[scenario.TargetOrdinal - 1],
                    scenario.TargetOrdinal)
                : read.Rows
                    .Select((row, index) => (Row: row, Ordinal: index + 1))
                    .Single(item => item.Row.Fingerprint.StartsWith(
                        fingerprint,
                        StringComparison.OrdinalIgnoreCase));
        return new(
            read.Count,
            selected.Ordinal,
            ProjectionRow(selected.Row));
    }

    private static string TargetFingerprint(
        MetadataMethodGroupInspection.Analysis model,
        MemberExactScorecardScenario scenario)
    {
        MetadataMethodGroupInspection.Fold completed =
            new MethodHandles(model.Methods).Fold<
                MethodHandles,
                MethodDefinitionHandle,
                MetadataMethodGroupInspection.Fold,
                Accept>(
                    new(
                        model,
                        Selection(
                            model,
                            scenario.Accessibility,
                            scenario.Receiver),
                        startOrdinal: 0,
                        maximumRows: 100_000,
                        materializeRows: true,
                        maximumMembers: 100_000,
                        maximumRetainedTextCharacters: 20_000_000),
                    default);
        if (completed.Complete()
            is not MetadataMethodGroupInspectionOutcome.Read read
            || read.Count != scenario.ExpectedCount)
        {
            throw new InvalidOperationException(
                $"{scenario.Name} could not resolve its target fingerprint.");
        }
        return read.Rows[scenario.TargetOrdinal - 1].Fingerprint;
    }

    private static void MeasureExactPhase(
        List<MemberGroupScorecardCell> cells,
        MemberExactScorecardScenario scenario,
        string phase,
        Func<ExactColumn, MemberExactProjectionAnswer> execute)
    {
        var times = ExactColumns.ToDictionary(
            static column => column.Name,
            static _ => new List<double>());
        var allocations = ExactColumns.ToDictionary(
            static column => column.Name,
            static _ => new List<long>());
        foreach (ExactColumn column in ExactColumns)
        {
            for (int warmup = 0; warmup < 5; warmup++)
                GC.KeepAlive(execute(column));
        }

        for (int round = 0; round < 6; round++)
        {
            for (int offset = 0;
                offset < ExactColumns.Count;
                offset++)
            {
                ExactColumn column =
                    ExactColumns[(round + offset) % ExactColumns.Count];
                Measurement measurement =
                    Measure(() => execute(column));
                times[column.Name].Add(measurement.Microseconds);
                allocations[column.Name].Add(
                    measurement.AllocatedBytes);
            }
        }

        foreach (ExactColumn column in ExactColumns)
        {
            cells.Add(
                new(
                    scenario.Name,
                    scenario.Selector.ToString(),
                    phase,
                    column.Name,
                    Median(times[column.Name]),
                    Median(allocations[column.Name])));
        }
    }

    private static int MaximumRows(
        MemberExactScorecardScenario scenario) =>
        scenario.Selector is MemberExactSelectorKind.Ordinal
            ? scenario.TargetOrdinal
            : 100_000;

    private static MemberGroupProjectionRow ProjectionRow(
        MetadataMethodGroupRow row) =>
        new(
            row.MetadataToken,
            row.DisplaySignature,
            row.CanonicalSignature,
            row.Fingerprint,
            row.Accessibility,
            row.Receiver);

    private static string ExactAnswerHash(
        MemberExactProjectionAnswer answer)
    {
        ulong hash = 14695981039346656037;
        AddLong(answer.PopulationCount);
        AddLong(answer.BaselineOrdinal);
        AddLong(answer.Row.MetadataToken);
        AddString(answer.Row.DisplaySignature);
        AddString(answer.Row.CanonicalSignature);
        AddString(answer.Row.Fingerprint);
        AddString(answer.Row.Accessibility);
        AddLong((int)answer.Row.Receiver);
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

    private static MemberExactScorecardScenario ExactScenario(
        string name,
        string typeName,
        string methodName,
        int expectedCount,
        int targetOrdinal,
        MemberExactSelectorKind selector) =>
        new(
            name,
            typeName,
            methodName,
            MetadataMethodAccessibilityFilter.Public,
            MetadataMethodReceiverFilter.All,
            expectedCount,
            targetOrdinal,
            selector);

    private sealed record ExactColumn(
        string Name,
        ExactExecutor Execute);

    private delegate MemberExactProjectionAnswer ExactExecutor(
        MetadataMethodGroupInspection.Analysis model,
        MemberExactScorecardScenario scenario,
        string fingerprint);
}
