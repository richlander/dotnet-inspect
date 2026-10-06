using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;

using DotnetInspector.PerformanceOracles;
using ILInspector.Analysis;
using ILInspector.Analysis.Planning;
using ILInspector.Metadata;
using NLinq;

namespace ILInspector.AnalysisHarness;

public static class BodyUseScorecard
{
    static IReadOnlyList<BodyUseScorecardColumn> Columns =>
        BodyUseScorecardKernel.Columns;

    static IReadOnlyList<BodyUseScorecardClosing> Closings =>
        BodyUseScorecardKernel.Closings;

    public static IReadOnlyList<BodyUseScorecardAsset> LoadAssets(
        IReadOnlyList<string> paths)
    {
        IReadOnlyList<string> names =
            ScorecardAssetNames.FromPaths(paths);
        var assets = new List<BodyUseScorecardAsset>(paths.Count);
        for (int index = 0; index < paths.Count; index++)
        {
            string path = Path.GetFullPath(paths[index]);
            assets.Add(
                new(
                    names[index],
                    path,
                    ImmutableArray.Create(File.ReadAllBytes(path))));
        }
        return assets;
    }

    public static BodyUseScorecardCheck Check(
        IReadOnlyList<BodyUseScorecardAsset> assets,
        AnalysisLibraryBodyUseLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        var mismatches = new List<BodyUseScorecardMismatch>();
        var hashes = new List<BodyUseScorecardAnswerHash>();
        int compared = 0;
        foreach (BodyUseScorecardAsset asset in assets)
        {
            foreach (BodyUseScorecardClosing closing in Closings)
            {
                BodyUseScorecardExecution oracle = Execute(
                    BodyUseScorecardColumn.NLinq,
                    closing,
                    asset,
                    limits,
                    cancellationToken);
                hashes.Add(
                    new(
                        asset.Name,
                        closing,
                        AnswerHash(oracle)));
                foreach (BodyUseScorecardColumn column in Columns)
                {
                    if (column == BodyUseScorecardColumn.NLinq)
                        continue;
                    BodyUseScorecardExecution actual = Execute(
                        column,
                        closing,
                        asset,
                        limits,
                        cancellationToken);
                    compared++;
                    if (!Same(actual, oracle))
                    {
                        mismatches.Add(
                            new(
                                asset.Name,
                                closing,
                                column,
                                Describe(actual),
                                Describe(oracle)));
                    }
                }
            }
        }
        return new(compared, mismatches, hashes);
    }

    public static BodyUseScorecardExecution Execute(
        BodyUseScorecardColumn column,
        BodyUseScorecardAsset asset,
        AnalysisLibraryBodyUseLimits? limits = null,
        CancellationToken cancellationToken = default) =>
        BodyUseScorecardKernel.Execute(
            column,
            asset,
            limits,
            cancellationToken);

    public static BodyUseScorecardExecution Execute(
        BodyUseScorecardColumn column,
        BodyUseScorecardClosing closing,
        BodyUseScorecardAsset asset,
        AnalysisLibraryBodyUseLimits? limits = null,
        CancellationToken cancellationToken = default) =>
        BodyUseScorecardKernel.Execute(
            column,
            closing,
            asset,
            limits,
            cancellationToken);

    public static IReadOnlyList<BodyUseScorecardCell> Measure(
        IReadOnlyList<BodyUseScorecardAsset> assets,
        ScorecardTiming timing,
        Action<string>? progress = null,
        AnalysisLibraryBodyUseLimits? limits = null,
        CancellationToken cancellationToken = default) =>
        BodyUseScorecardKernel.Measure(
            assets,
            timing,
            progress,
            limits,
            cancellationToken);

    public static string Report(
        IReadOnlyList<BodyUseScorecardCell> cells,
        IReadOnlyList<BodyUseScorecardWorkShape> workShapes)
    {
        var text = new StringBuilder();
        BodyUseScorecardRendering.AppendWorkShapes(
            text,
            workShapes);
        text.AppendLine(
            "Implementation ratios to Planner: geometric mean across assets "
                + "(min-max); lower is faster.");
        text.AppendLine();
        text.AppendLine(
            "| Terminal | Metric | Assets | Direct | LINQ | NLinq | Planner |");
        text.AppendLine(
            "| --- | --- | ---: | ---: | ---: | ---: | ---: |");
        foreach (BodyUseScorecardClosing closing in Closings)
        {
            AppendRatioRow(
                text,
                cells,
                closing,
                "Time",
                static cell => cell.MedianMicroseconds);
            AppendRatioRow(
                text,
                cells,
                closing,
                "Allocation",
                static cell => cell.MedianAllocatedBytes);
        }
        text.AppendLine();
        text.AppendLine(
            "Terminal ratios to Count: geometric mean across assets "
                + "(min-max); lower is faster.");
        text.AppendLine();
        text.AppendLine(
            "| Implementation | Metric | Assets | Exists | Count | Rows |");
        text.AppendLine(
            "| --- | --- | ---: | ---: | ---: | ---: |");
        foreach (BodyUseScorecardColumn column in Columns)
        {
            AppendTerminalRatioRow(
                text,
                cells,
                column,
                "Time",
                static cell => cell.MedianMicroseconds);
            AppendTerminalRatioRow(
                text,
                cells,
                column,
                "Allocation",
                static cell => cell.MedianAllocatedBytes);
        }
        BodyUseScorecardRendering.AppendDeltas(
            text,
            cells);
        text.AppendLine("Absolute end-to-end medians.");
        text.AppendLine();
        text.AppendLine(
            "| Asset | Terminal | Column | Time (us) | Allocated bytes |");
        text.AppendLine("| --- | --- | --- | ---: | ---: |");
        foreach (BodyUseScorecardCell cell in cells)
        {
            text.Append("| ")
                .Append(cell.Asset)
                .Append(" | ")
                .Append(cell.Closing)
                .Append(" | ")
                .Append(BodyUseScorecardKernel.Name(cell.Column))
                .Append(" | ")
                .Append(
                    cell.MedianMicroseconds.ToString(
                        "0.0",
                        CultureInfo.InvariantCulture))
                .Append(" | ")
                .Append(
                    cell.MedianAllocatedBytes.ToString(
                        "N0",
                        CultureInfo.InvariantCulture))
                .AppendLine(" |");
        }
        return text.ToString();
    }

    public static void WriteTsv(
        IReadOnlyList<BodyUseScorecardCell> cells,
        IReadOnlyList<BodyUseScorecardWorkShape> workShapes,
        TextWriter writer)
    {
        writer.WriteLine(
            "asset\tterminal\tcolumn\tmedian_us\tallocated_bytes"
                + "\tterminal_value\tdisposition"
                + "\tbodies_considered\toperands_considered"
                + "\tround_medians_us\tround_allocated_bytes");
        foreach (BodyUseScorecardCell cell in cells)
        {
            writer.Write(cell.Asset);
            writer.Write('\t');
            writer.Write(cell.Closing);
            writer.Write('\t');
            writer.Write(BodyUseScorecardKernel.Name(cell.Column));
            writer.Write('\t');
            writer.Write(
                cell.MedianMicroseconds.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture));
            writer.Write('\t');
            writer.Write(
                cell.MedianAllocatedBytes.ToString(
                    CultureInfo.InvariantCulture));
            BodyUseScorecardRendering.WriteWorkShapeFields(
                writer,
                cell,
                workShapes);
            writer.Write('\t');
            writer.Write(
                string.Join(
                    ",",
                    cell.RoundMediansMicroseconds.Select(
                        static value => value.ToString(
                            "0.###",
                            CultureInfo.InvariantCulture))));
            writer.Write('\t');
            writer.WriteLine(
                string.Join(",", cell.RoundMediansAllocatedBytes));
        }
    }

    public static string Describe(
        BodyUseScorecardExecution execution)
    {
        if (execution.Answer is not { } answer)
            return "rejected:" + execution.Rejection;
        return answer switch
        {
            BodyUseScorecardAnswer.Exists exists =>
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"exists={exists.Value};"
                        + $"disposition="
                        + $"{exists.Evidence.Disposition};"
                        + $"hash={AnswerHash(execution)}"),
            BodyUseScorecardAnswer.Count count =>
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"count={count.Value};"
                        + $"disposition="
                        + $"{count.Evidence.Disposition};"
                        + $"hash={AnswerHash(execution)}"),
            BodyUseScorecardAnswer.Rows rows =>
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"types={rows.Types.Length};"
                        + $"bodies={rows.Coverage.BodiesConsidered};"
                        + $"operands={rows.Coverage.OperandsConsidered};"
                        + $"occurrences={rows.Occurrences.Length};"
                        + $"disposition={rows.Disposition};"
                        + $"hash={AnswerHash(execution)}"),
            _ => throw new InvalidOperationException(
                "The scorecard returned an unknown answer."),
        };
    }

    static bool Same(
        BodyUseScorecardExecution left,
        BodyUseScorecardExecution right)
    {
        if (left.Answer is null || right.Answer is null)
        {
            return left.Answer is null
                && right.Answer is null
                && Equals(
                    left.Rejection,
                    right.Rejection);
        }
        return (left.Answer, right.Answer) switch
        {
            (
                BodyUseScorecardAnswer.Exists first,
                BodyUseScorecardAnswer.Exists second) =>
                first.Value == second.Value
                && Same(first.Evidence, second.Evidence),
            (
                BodyUseScorecardAnswer.Count first,
                BodyUseScorecardAnswer.Count second) =>
                first.Value == second.Value
                && Same(first.Evidence, second.Evidence),
            (
                BodyUseScorecardAnswer.Rows first,
                BodyUseScorecardAnswer.Rows second) =>
                first.Disposition == second.Disposition
                && first.Types.AsSpan().SequenceEqual(
                    second.Types.AsSpan())
                && first.Occurrences.AsSpan().SequenceEqual(
                    second.Occurrences.AsSpan())
                && first.PhysicalEvidence.AsSpan().SequenceEqual(
                    second.PhysicalEvidence.AsSpan())
                && first.Coverage == second.Coverage
                && first.Diagnostics.AsSpan().SequenceEqual(
                    second.Diagnostics.AsSpan()),
            _ => false,
        };
    }

    static bool Same(
        BodyUseScorecardTerminalEvidence first,
        BodyUseScorecardTerminalEvidence second) =>
        first.Disposition == second.Disposition
        && first.Coverage == second.Coverage
        && first.Diagnostics.AsSpan().SequenceEqual(
            second.Diagnostics.AsSpan());

    static string AnswerHash(BodyUseScorecardExecution execution)
    {
        if (execution.Answer is not { } answer)
            return "rejected:" + execution.Rejection;

        ulong hash = 14695981039346656037;
        Add((int)execution.Closing);
        switch (answer)
        {
            case BodyUseScorecardAnswer.Exists exists:
                Add(exists.Value ? 1 : 0);
                AddEvidence(exists.Evidence);
                break;
            case BodyUseScorecardAnswer.Count count:
                Add(count.Value);
                AddEvidence(count.Evidence);
                break;
            case BodyUseScorecardAnswer.Rows rows:
                Add((int)rows.Disposition);
                AddCoverage(rows.Coverage);
                foreach (AnalysisLibraryBodyUseType type in rows.Types)
                {
                    AddAddress(type.Type);
                    AddName(type.Name);
                    Add((int)type.DefinitionKind);
                }
                foreach (AnalysisLibraryBodyUseOccurrence occurrence
                    in rows.Occurrences)
                {
                    AddAddress(occurrence.Source);
                    AddName(occurrence.SourceType);
                    AddAddress(occurrence.Target);
                    AddName(occurrence.TargetType);
                    Add(occurrence.PhysicalMethodToken);
                    Add((int)occurrence.OperandKind);
                    Add(occurrence.OperandToken);
                    Add(occurrence.IlOffset);
                    Add(occurrence.OccurrenceOrdinal);
                }
                foreach (AnalysisLibraryBodyUsePhysicalEvidence physical
                    in rows.PhysicalEvidence)
                {
                    AddAddress(physical.PhysicalSource);
                    AddName(physical.PhysicalSourceType);
                    Add(physical.PhysicalMethodToken);
                    Add((int)physical.Fidelity);
                }
                AddDiagnostics(rows.Diagnostics);
                break;
            default:
                throw new InvalidOperationException(
                    "The scorecard returned an unknown answer.");
        }
        return hash.ToString("x16", CultureInfo.InvariantCulture);

        void AddEvidence(BodyUseScorecardTerminalEvidence evidence)
        {
            Add((int)evidence.Disposition);
            AddCoverage(evidence.Coverage);
            AddDiagnostics(evidence.Diagnostics);
        }

        void AddCoverage(AnalysisLibraryBodyUseCoverage coverage)
        {
            Add(coverage.BodiesConsidered);
            Add(coverage.BodiesExamined);
            Add(coverage.BodiesPhysicalOnly);
            Add(coverage.BodiesUnavailable);
            Add(coverage.BodiesLimited);
            Add(coverage.OperandsConsidered);
            Add(coverage.OperandsExamined);
            Add(coverage.OperandsUnavailable);
            Add(coverage.OperandsLimited);
        }

        void AddDiagnostics(
            ImmutableArray<AnalysisLibraryBodyUseDiagnostic> diagnostics)
        {
            foreach (AnalysisLibraryBodyUseDiagnostic diagnostic
                in diagnostics)
            {
                Add((int)diagnostic.Kind);
                Add(diagnostic.MethodToken ?? -1);
                Add(diagnostic.IlOffset ?? -1);
                AddText(diagnostic.Detail);
                Add(diagnostic.Limit ?? -1);
                Add(diagnostic.AttemptedCharge ?? -1);
            }
        }

        void AddAddress(MetadataTypeDefinitionAddress address)
        {
            AddText(address.ModuleVersionId.ToString("N"));
            Add(address.Definition.Value);
        }

        void AddName(MetadataTypeDefinitionName name)
        {
            AddText(name.Namespace);
            foreach (string segment in name.Segments)
                AddText(segment);
        }

        void AddText(string text)
        {
            foreach (char character in text)
                Add(character);
            Add(-2);
        }

        void Add(long value)
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
    }

    static void AppendRatioRow(
        StringBuilder text,
        IReadOnlyList<BodyUseScorecardCell> cells,
        BodyUseScorecardClosing closing,
        string metric,
        Func<BodyUseScorecardCell, double> value)
    {
        BodyUseScorecardCell[] terminalCells =
        [
            .. cells.Where(cell => cell.Closing == closing),
        ];
        Dictionary<int, BodyUseScorecardCell> planners =
            terminalCells
                .Where(cell =>
                    cell.Column == BodyUseScorecardColumn.Planner)
                .ToDictionary(cell => cell.AssetIndex);
        int assets =
            planners.Values.Count(planner => value(planner) > 0);
        text.Append("| ")
            .Append(closing)
            .Append(" | ")
            .Append(metric)
            .Append(" | ")
            .Append(assets.ToString(CultureInfo.InvariantCulture))
            .Append(" |");
        foreach (BodyUseScorecardColumn column in Columns)
        {
            if (column == BodyUseScorecardColumn.Planner)
            {
                text.Append(
                    assets == 0
                        ? " - |"
                        : " 1.00x |");
                continue;
            }
            double[] ratios =
            [
                .. terminalCells
                    .Where(cell =>
                        cell.Column == column
                        && planners.TryGetValue(
                            cell.AssetIndex,
                            out BodyUseScorecardCell? planner)
                        && value(planner) > 0)
                    .Select(cell =>
                        value(cell)
                            / value(planners[cell.AssetIndex])),
            ];
            if (ratios.Length == 0)
            {
                text.Append(" - |");
                continue;
            }
            text.Append(' ')
                .Append(
                    Math.Exp(ratios.Average(Math.Log)).ToString(
                        "0.00",
                        CultureInfo.InvariantCulture))
                .Append("x (")
                .Append(
                    ratios.Min().ToString(
                        "0.00",
                        CultureInfo.InvariantCulture))
                .Append('-')
                .Append(
                    ratios.Max().ToString(
                        "0.00",
                        CultureInfo.InvariantCulture))
                .Append(") |");
        }
        text.AppendLine();
    }

    static void AppendTerminalRatioRow(
        StringBuilder text,
        IReadOnlyList<BodyUseScorecardCell> cells,
        BodyUseScorecardColumn column,
        string metric,
        Func<BodyUseScorecardCell, double> value)
    {
        BodyUseScorecardCell[] implementationCells =
        [
            .. cells.Where(cell => cell.Column == column),
        ];
        Dictionary<int, BodyUseScorecardCell> counts =
            implementationCells
                .Where(cell =>
                    cell.Closing == BodyUseScorecardClosing.Count)
                .ToDictionary(cell => cell.AssetIndex);
        int baselineCount =
            counts.Values.Count(count => value(count) > 0);
        text.Append("| ")
            .Append(BodyUseScorecardKernel.Name(column))
            .Append(" | ")
            .Append(metric)
            .Append(" | ")
            .Append(
                baselineCount.ToString(
                    CultureInfo.InvariantCulture))
            .Append(" |");
        foreach (BodyUseScorecardClosing closing in Closings)
        {
            if (closing == BodyUseScorecardClosing.Count)
            {
                text.Append(
                    baselineCount == 0
                        ? " - |"
                        : " 1.00x |");
                continue;
            }
            double[] ratios =
            [
                .. implementationCells
                    .Where(cell =>
                        cell.Closing == closing
                        && counts.TryGetValue(
                            cell.AssetIndex,
                            out BodyUseScorecardCell? count)
                        && value(count) > 0)
                    .Select(cell =>
                        value(cell)
                            / value(counts[cell.AssetIndex])),
            ];
            if (ratios.Length == 0)
            {
                text.Append(" - |");
                continue;
            }
            text.Append(' ')
                .Append(
                    Math.Exp(ratios.Average(Math.Log)).ToString(
                        "0.00",
                        CultureInfo.InvariantCulture))
                .Append("x (")
                .Append(
                    ratios.Min().ToString(
                        "0.00",
                        CultureInfo.InvariantCulture))
                .Append('-')
                .Append(
                    ratios.Max().ToString(
                        "0.00",
                        CultureInfo.InvariantCulture))
                .Append(") |");
        }
        text.AppendLine();
    }

}
