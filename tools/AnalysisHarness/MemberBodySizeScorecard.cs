using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using DotnetInspector.PerformanceOracles;
using ILInspector.Analysis;
using NLinq;

namespace ILInspector.AnalysisHarness;

public enum MemberBodySizeScope
{
    FirstLogicalMethod,
    PublicFamily,
}

public sealed record MemberBodySizeScorecardScenario(
    string Name,
    string AssemblyPath,
    string Namespace,
    string TypeName,
    string MethodName,
    MemberBodySizeScope Scope,
    int ExpectedRows);

public readonly record struct MemberBodySizeProjectionRow(
    int MethodToken,
    int LargestIlBytes,
    int PhysicalBodyCount);

public sealed record MemberBodySizeScorecardMismatch(
    string Scenario,
    string Closing,
    string Column,
    string Answer,
    string OracleAnswer);

public sealed record MemberBodySizeScorecardAnswerHash(
    string Scenario,
    string Closing,
    string Hash);

public sealed record MemberBodySizeScorecardCheck(
    int Compared,
    IReadOnlyList<MemberBodySizeScorecardMismatch> Mismatches,
    IReadOnlyList<MemberBodySizeScorecardAnswerHash> AnswerHashes)
{
    public bool Agrees => Mismatches.Count == 0;
}

public sealed record MemberBodySizeScorecardTiming(
    int Rounds = 5,
    int SamplesPerRound = 3,
    int Warmup = 2);

public sealed record MemberBodySizeScorecardCell(
    int ScenarioIndex,
    string Scenario,
    ScorecardClosing Closing,
    string Column,
    double Microseconds,
    long AllocatedBytes,
    bool WindowFailed);

public sealed record MemberBodySizePreparationCell(
    string Scenario,
    double Microseconds,
    long AllocatedBytes,
    int AssemblyMethodCount,
    int LogicalMethodCount,
    int PhysicalBodyCount,
    int AttributionProbeBodies,
    long AttributionProbeIlBytes);

public sealed record MemberBodySizeScorecardResult(
    MemberBodySizeScorecardCheck Check,
    IReadOnlyList<MemberBodySizeScorecardCell> Cells,
    IReadOnlyList<MemberBodySizePreparationCell> Preparation);

public sealed record MemberBodySizePhysicalBody(
    int LogicalMethodToken,
    int PhysicalMethodToken);

public sealed record MemberBodySizeSafetyResult(
    string Column,
    bool Incomplete,
    string? Failure);

public static class MemberBodySizeScorecard
{
    const int MinimumIlBytes = 1;

    static readonly ScorecardShape s_shape =
        new(N: 3, WindowFirst: 2, WindowLast: 4);

    static readonly IReadOnlyList<Column> s_columns =
    [
        new("Old", Old),
        new("LINQ", Linq),
        new("NLinq", NLinqColumn),
        new("Planner (experimental)", Planner),
    ];

    public static IReadOnlyList<MemberBodySizeScorecardScenario>
        DefaultScenarios(
            IReadOnlyList<string> assemblyPaths)
    {
        var scenarios =
            new List<MemberBodySizeScorecardScenario>();
        IReadOnlyList<string> assetNames =
            ScorecardAssetNames.FromPaths(assemblyPaths);
        for (int index = 0;
            index < assemblyPaths.Count;
            index++)
        {
            string path = assemblyPaths[index];
            string fullPath = Path.GetFullPath(path);
            string assemblyName = AssemblyName(fullPath);
            switch (assemblyName)
            {
                case "System.Private.CoreLib":
                    Add(
                        assetNames[index],
                        fullPath,
                        "System.Text",
                        "StringBuilder",
                        "AppendFormat",
                        familyRows: 15);
                    break;
                case "System.Text.Json":
                    Add(
                        assetNames[index],
                        fullPath,
                        "System.Text.Json",
                        "JsonDocument",
                        "Parse",
                        familyRows: 5);
                    break;
                case "ILInspector.Analysis.Fixtures":
                    Add(
                        assetNames[index],
                        fullPath,
                        "ILInspector.Analysis.ImplementationProfileFixtures",
                        "ImplementationHeatLambdaSample",
                        "Scale",
                        familyRows: 2);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"No member body-size scenario is registered "
                        + $"for assembly '{assemblyName}'.");
            }
        }
        return scenarios;

        void Add(
            string assetName,
            string path,
            string @namespace,
            string type,
            string method,
            int familyRows)
        {
            string prefix =
                $"{assetName} {type}.{method}";
            scenarios.Add(
                new(
                    $"{prefix} / one logical method",
                    path,
                    @namespace,
                    type,
                    method,
                    MemberBodySizeScope.FirstLogicalMethod,
                    ExpectedRows: 1));
            scenarios.Add(
                new(
                    $"{prefix} / public family",
                    path,
                    @namespace,
                    type,
                    method,
                    MemberBodySizeScope.PublicFamily,
                    ExpectedRows: familyRows));
        }
    }

    public static MemberBodySizeScorecardCheck Check(
        IReadOnlyList<MemberBodySizeScorecardScenario> scenarios)
    {
        using LoadedAssets loaded = LoadedAssets.Open(scenarios);
        return Check(loaded.Assets);
    }

    public static MemberBodySizeScorecardResult Measure(
        IReadOnlyList<MemberBodySizeScorecardScenario> scenarios,
        MemberBodySizeScorecardTiming? timing = null)
    {
        timing ??= new();
        ValidateTiming(timing);
        using LoadedAssets loaded = LoadedAssets.Open(scenarios);
        MemberBodySizeScorecardCheck check =
            Check(loaded.Assets);
        if (!check.Agrees)
            return new(check, [], []);

        var cells = new List<MemberBodySizeScorecardCell>();
        foreach (Asset asset in loaded.Assets)
        {
            foreach (ScorecardClosing closing
                in Scorecard.Closings)
            {
                MeasureClosing(
                    cells,
                    asset,
                    closing,
                    timing);
            }
        }

        var preparation =
            new List<MemberBodySizePreparationCell>();
        foreach (Asset asset in loaded.Assets)
        {
            preparation.Add(
                MeasurePreparation(asset, timing));
        }
        return new(check, cells, preparation);
    }

    public static string Report(
        MemberBodySizeScorecardResult result)
    {
        using var writer =
            new StringWriter(CultureInfo.InvariantCulture);
        writer.WriteLine(
            $"# answers: {result.Check.Compared} compared, "
            + $"{result.Check.Mismatches.Count} mismatches");
        writer.WriteLine(
            "# Planner cells are experimental #8577 method-body "
            + "breadth/depth kernels, not shipping product measurements.");
        foreach (MemberBodySizeScorecardAnswerHash answer
            in result.Check.AnswerHashes)
        {
            writer.WriteLine(
                $"# answer: {answer.Scenario} / "
                + $"{answer.Closing} = {answer.Hash}");
        }
        foreach (MemberBodySizeScorecardMismatch mismatch
            in result.Check.Mismatches)
        {
            writer.WriteLine(
                $"mismatch\t{mismatch.Scenario}\t"
                + $"{mismatch.Closing}\t{mismatch.Column}\t"
                + $"{mismatch.Answer}\toracle={mismatch.OracleAnswer}");
        }
        if (!result.Check.Agrees)
            return writer.ToString();

        writer.WriteLine();
        writer.WriteLine(
            "| Scenario | Closing | Old | Old alloc | LINQ | "
            + "LINQ alloc | NLinq | NLinq alloc | "
            + "Planner (experimental) | Planner alloc |");
        writer.WriteLine(
            "| --- | --- | ---: | ---: | ---: | ---: | "
            + "---: | ---: | ---: | ---: |");
        foreach ((int index, string scenario)
            in result.Cells
                .Select(static cell =>
                    (cell.ScenarioIndex, cell.Scenario))
                .Distinct())
        {
            foreach (ScorecardClosing closing
                in Scorecard.Closings)
            {
                MemberBodySizeScorecardCell[] selected =
                [
                    .. result.Cells.Where(cell =>
                        cell.ScenarioIndex == index
                        && cell.Closing == closing),
                ];
                MemberBodySizeScorecardCell baseline =
                    selected.Single(static cell =>
                        cell.Column == "Planner (experimental)");
                writer.Write(
                    $"| {scenario} | {s_shape.Label(closing)} |");
                foreach (string column
                    in new[]
                    {
                        "Old",
                        "LINQ",
                        "NLinq",
                        "Planner (experimental)",
                    })
                {
                    MemberBodySizeScorecardCell cell =
                        selected.Single(item =>
                            item.Column == column);
                    if (cell.WindowFailed)
                    {
                        writer.Write(" fail | fail |");
                    }
                    else if (column == "Planner (experimental)")
                    {
                        writer.Write(
                            $" 1.00x ({cell.Microseconds:F3} us) | "
                            + $"{cell.AllocatedBytes:N0} B |");
                    }
                    else
                    {
                        writer.Write(
                            $" {cell.Microseconds / baseline.Microseconds:F2}x "
                            + $"({cell.Microseconds:F3} us) | "
                            + $"{cell.AllocatedBytes:N0} B |");
                    }
                }
                writer.WriteLine();
            }
        }

        writer.WriteLine();
        writer.WriteLine(
            "| Scenario | Current preparation | Allocation | "
            + "Assembly methods | Logical methods | Physical bodies | "
            + "Attribution probes | Attribution IL |");
        writer.WriteLine(
            "| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (MemberBodySizePreparationCell cell
            in result.Preparation)
        {
            writer.WriteLine(
                $"| {cell.Scenario} | {cell.Microseconds:F3} us | "
                + $"{cell.AllocatedBytes:N0} B | "
                + $"{cell.AssemblyMethodCount:N0} | "
                + $"{cell.LogicalMethodCount:N0} | "
                + $"{cell.PhysicalBodyCount:N0} | "
                + $"{cell.AttributionProbeBodies:N0} | "
                + $"{cell.AttributionProbeIlBytes:N0} B |");
        }

        writer.WriteLine();
        writer.WriteLine(
            "| Closing | Old/Planner geo mean (range) | "
            + "LINQ/Planner geo mean (range) | "
            + "NLinq/Planner geo mean (range) | Planner |");
        writer.WriteLine("| --- | ---: | ---: | ---: | ---: |");
        foreach (ScorecardClosing closing
            in Scorecard.Closings)
        {
            writer.WriteLine(
                $"| {s_shape.Label(closing)} | "
                + $"{FormatRatio(RatioSummary(
                    result.Cells,
                    closing,
                    "Old"))} | "
                + $"{FormatRatio(RatioSummary(
                    result.Cells,
                    closing,
                    "LINQ"))} | "
                + $"{FormatRatio(RatioSummary(
                    result.Cells,
                    closing,
                    "NLinq"))} | "
                + "1.00x |");
        }
        writer.WriteLine();
        writer.WriteLine(
            "| Implementation | Metric | Exists | Count | Rows |");
        writer.WriteLine("| --- | --- | ---: | ---: | ---: |");
        foreach (string column
            in new[]
            {
                "Old",
                "LINQ",
                "NLinq",
                "Planner (experimental)",
            })
        {
            writer.WriteLine(
                $"| {column} | Time | "
                    + $"{FormatRatio(TerminalRatioSummary(
                        result.Cells,
                        column,
                        ScorecardClosing.Exists,
                        static cell => cell.Microseconds))} | "
                    + $"{FormatTerminalBaseline(
                        result.Cells,
                        column,
                        static cell => cell.Microseconds)} | "
                    + $"{FormatRatio(TerminalRatioSummary(
                        result.Cells,
                        column,
                        ScorecardClosing.Rows,
                        static cell => cell.Microseconds))} |");
            writer.WriteLine(
                $"| {column} | Allocation | "
                    + $"{FormatRatio(TerminalRatioSummary(
                        result.Cells,
                        column,
                        ScorecardClosing.Exists,
                        static cell => cell.AllocatedBytes))} | "
                    + $"{FormatTerminalBaseline(
                        result.Cells,
                        column,
                        static cell => cell.AllocatedBytes)} | "
                    + $"{FormatRatio(TerminalRatioSummary(
                        result.Cells,
                        column,
                        ScorecardClosing.Rows,
                        static cell => cell.AllocatedBytes))} |");
        }
        return writer.ToString();
    }

    public static void WriteTsv(
        MemberBodySizeScorecardResult result,
        TextWriter writer)
    {
        writer.WriteLine(
            "scenario_index\tscenario\tclosing\tcolumn\tmedian_us"
            + "\tallocated_bytes\twindow_failed");
        foreach (MemberBodySizeScorecardCell cell
            in result.Cells)
        {
            writer.WriteLine(
                string.Join(
                    '\t',
                    cell.ScenarioIndex.ToString(
                        CultureInfo.InvariantCulture),
                    cell.Scenario,
                    s_shape.Label(cell.Closing),
                    cell.Column,
                    cell.WindowFailed
                        ? "fail"
                        : cell.Microseconds.ToString(
                            "0.###",
                            CultureInfo.InvariantCulture),
                    cell.WindowFailed
                        ? ""
                        : cell.AllocatedBytes.ToString(
                            CultureInfo.InvariantCulture),
                    cell.WindowFailed ? "true" : "false"));
        }
    }

    public static ScorecardAnswer<MemberBodySizeProjectionRow>
        Execute(
            MemberBodySizeScorecardScenario scenario,
            string column,
            ScorecardClosing closing)
    {
        using Asset asset = Asset.Open(scenario);
        return s_columns
            .Single(item => item.Name == column)
            .Execute(closing, asset.Model);
    }

    public static IReadOnlyList<MemberBodySizePhysicalBody>
        PhysicalBodies(
            MemberBodySizeScorecardScenario scenario)
    {
        using Asset asset = Asset.Open(scenario);
        return
        [
            .. asset.Model.Groups.SelectMany(group =>
                group.PhysicalTokens.Select(token =>
                    new MemberBodySizePhysicalBody(
                        group.LogicalToken,
                        token))),
        ];
    }

    public static IReadOnlyList<MemberBodySizeSafetyResult>
        CheckDirectSafety(
            MemberBodySizeScorecardScenario scenario,
            byte[] replacementImage)
    {
        ArgumentNullException.ThrowIfNull(replacementImage);
        using Asset clean = Asset.Open(scenario);
        using var replacementReader =
            new PEReader(ImmutableArray.Create(replacementImage));
        var model = clean.Model with
        {
            Image = ImmutableArray.Create(replacementImage),
            Reader = replacementReader.GetMetadataReader(),
            PeReader = replacementReader,
        };
        return
        [
            Try("LINQ", Linq),
            Try("NLinq", NLinqColumn),
            Try("Planner (experimental)", Planner),
        ];

        MemberBodySizeSafetyResult Try(
            string column,
            Executor execute)
        {
            try
            {
                GC.KeepAlive(
                    execute(
                        ScorecardClosing.Rows,
                        model));
                return new(column, Incomplete: false, Failure: null);
            }
            catch (Exception ex)
                when (LibraryMethodAnalysisRunner
                    .IsRecoverableMethodFailure(ex))
            {
                return new(
                    column,
                    Incomplete: true,
                    Failure: ex.GetType().Name);
            }
        }
    }

    static MemberBodySizeScorecardCheck Check(
        IReadOnlyList<Asset> assets)
    {
        var mismatches =
            new List<MemberBodySizeScorecardMismatch>();
        var answerHashes =
            new List<MemberBodySizeScorecardAnswerHash>();
        int compared = 0;
        Column oracle = s_columns.Single(static column =>
            column.Name == "NLinq");
        foreach (Asset asset in assets)
        {
            foreach (ScorecardClosing closing
                in Scorecard.Closings)
            {
                ScorecardAnswer<MemberBodySizeProjectionRow>
                    expected =
                        oracle.Execute(closing, asset.Model);
                if (closing == ScorecardClosing.Count
                    && expected.Count
                        != asset.Scenario.ExpectedRows)
                {
                    throw new InvalidOperationException(
                        $"{asset.Scenario.Name} returned "
                        + $"{expected.Count} rows, expected "
                        + $"{asset.Scenario.ExpectedRows}.");
                }
                answerHashes.Add(
                    new(
                        asset.Scenario.Name,
                        s_shape.Label(closing),
                        AnswerHash(expected)));
                foreach (Column column in s_columns)
                {
                    if (ReferenceEquals(column, oracle))
                        continue;
                    ScorecardAnswer<MemberBodySizeProjectionRow>
                        actual =
                            column.Execute(
                                closing,
                                asset.Model);
                    compared++;
                    if (!Scorecard.SameAnswer(
                            actual,
                            expected))
                    {
                        mismatches.Add(
                            new(
                                asset.Scenario.Name,
                                s_shape.Label(closing),
                                column.Name,
                                Scorecard.Describe(
                                    actual,
                                    RowText),
                                Scorecard.Describe(
                                    expected,
                                    RowText)));
                    }
                }
            }
        }
        return new(compared, mismatches, answerHashes);
    }

    static ScorecardAnswer<MemberBodySizeProjectionRow> Old(
        ScorecardClosing closing,
        Model model)
    {
        LibraryBodyAnalysisExecution execution =
            ExecuteCurrent(
                model.Name,
                model.Image,
                model.Scope,
                model.AssemblyMethodCount);
        return Answer(
            closing,
            NormalizeCurrent(
                execution,
                model.Scope),
            s_shape);
    }

    static ScorecardAnswer<MemberBodySizeProjectionRow> Linq(
        ScorecardClosing closing,
        Model model)
    {
        IEnumerable<MemberBodySizeProjectionRow> selected =
            model.Groups
                .Select(group => Project(model, group))
                .Where(static row =>
                    row.LargestIlBytes >= MinimumIlBytes);
        return closing switch
        {
            ScorecardClosing.Exists =>
                ScorecardAnswer<MemberBodySizeProjectionRow>
                    .OfExists(selected.Any()),
            ScorecardClosing.Count =>
                ScorecardAnswer<MemberBodySizeProjectionRow>
                    .OfCount(selected.Count()),
            ScorecardClosing.Head =>
                ScorecardAnswer<MemberBodySizeProjectionRow>
                    .OfRows(
                        Ordered(selected)
                            .Take(s_shape.N)
                            .ToArray()),
            ScorecardClosing.Tail =>
                ScorecardAnswer<MemberBodySizeProjectionRow>
                    .OfRows(
                        Ordered(selected)
                            .TakeLast(s_shape.N)
                            .ToArray()),
            ScorecardClosing.Rows =>
                ScorecardAnswer<MemberBodySizeProjectionRow>
                    .OfRows(
                        Ordered(selected).ToArray()),
            ScorecardClosing.Window =>
                StrictWindow(Ordered(selected)),
            _ => throw new ArgumentOutOfRangeException(
                nameof(closing)),
        };

        static IOrderedEnumerable<MemberBodySizeProjectionRow>
            Ordered(
                IEnumerable<MemberBodySizeProjectionRow> rows) =>
            rows.OrderBy(
                static row => row,
                RowComparer.Instance);
    }

    static ScorecardAnswer<MemberBodySizeProjectionRow>
        NLinqColumn(
            ScorecardClosing closing,
            Model model)
    {
        var project = new ProjectRow(model);
        var eligible = new Eligible();
        switch (closing)
        {
            case ScorecardClosing.Exists:
                return ScorecardAnswer<
                    MemberBodySizeProjectionRow>.OfExists(
                        model.Groups
                            .AsNLinq()
                            .Select<
                                ImmutableArrayEnumerator<BodyGroup>,
                                BodyGroup,
                                MemberBodySizeProjectionRow,
                                ProjectRow>(project)
                            .Any<
                                Map<
                                    BodyGroup,
                                    MemberBodySizeProjectionRow,
                                    ImmutableArrayEnumerator<BodyGroup>,
                                    ProjectRow>,
                                MemberBodySizeProjectionRow,
                                Eligible>(eligible));
            case ScorecardClosing.Count:
                {
                    var selected = model.Groups
                        .AsNLinq()
                        .Select<
                            ImmutableArrayEnumerator<BodyGroup>,
                            BodyGroup,
                            MemberBodySizeProjectionRow,
                            ProjectRow>(project)
                        .Where<
                            Map<
                                BodyGroup,
                                MemberBodySizeProjectionRow,
                                ImmutableArrayEnumerator<BodyGroup>,
                                ProjectRow>,
                            MemberBodySizeProjectionRow,
                            Eligible>(eligible);
                    return ScorecardAnswer<
                        MemberBodySizeProjectionRow>.OfCount(
                            selected.CountFold<
                                Filter<
                                    MemberBodySizeProjectionRow,
                                    Map<
                                        BodyGroup,
                                        MemberBodySizeProjectionRow,
                                        ImmutableArrayEnumerator<BodyGroup>,
                                        ProjectRow>,
                                    Eligible>,
                                MemberBodySizeProjectionRow>());
                }
            default:
                {
                    var selected = model.Groups
                        .AsNLinq()
                        .Select<
                            ImmutableArrayEnumerator<BodyGroup>,
                            BodyGroup,
                            MemberBodySizeProjectionRow,
                            ProjectRow>(project)
                        .Where<
                            Map<
                                BodyGroup,
                                MemberBodySizeProjectionRow,
                                ImmutableArrayEnumerator<BodyGroup>,
                                ProjectRow>,
                            MemberBodySizeProjectionRow,
                            Eligible>(eligible);
                    List<MemberBodySizeProjectionRow> rows =
                        NLinqExtensions.OrderBy<
                            Filter<
                                MemberBodySizeProjectionRow,
                                Map<
                                    BodyGroup,
                                    MemberBodySizeProjectionRow,
                                    ImmutableArrayEnumerator<BodyGroup>,
                                    ProjectRow>,
                                Eligible>,
                            MemberBodySizeProjectionRow>(
                                selected,
                                RowComparer.Instance);
                    return Answer(closing, rows, s_shape);
                }
        }
    }

    static ScorecardAnswer<MemberBodySizeProjectionRow> Planner(
        ScorecardClosing closing,
        Model model)
    {
        switch (closing)
        {
            case ScorecardClosing.Exists:
                foreach (BodyGroup group in model.Groups)
                {
                    if (Project(model, group).LargestIlBytes
                        >= MinimumIlBytes)
                    {
                        return ScorecardAnswer<
                            MemberBodySizeProjectionRow>
                            .OfExists(true);
                    }
                }
                return ScorecardAnswer<
                    MemberBodySizeProjectionRow>
                    .OfExists(false);
            case ScorecardClosing.Count:
                {
                    int count = 0;
                    foreach (BodyGroup group in model.Groups)
                    {
                        if (Project(model, group).LargestIlBytes
                            >= MinimumIlBytes)
                        {
                            count++;
                        }
                    }
                    return ScorecardAnswer<
                        MemberBodySizeProjectionRow>
                        .OfCount(count);
                }
            case ScorecardClosing.Head:
                return ScorecardAnswer<
                    MemberBodySizeProjectionRow>.OfRows(
                        SelectBounded(
                            model,
                            s_shape.N,
                            keepLargest: true));
            case ScorecardClosing.Tail:
                return ScorecardAnswer<
                    MemberBodySizeProjectionRow>.OfRows(
                        SelectBounded(
                            model,
                            s_shape.N,
                            keepLargest: false));
            case ScorecardClosing.Rows:
                return ScorecardAnswer<
                    MemberBodySizeProjectionRow>.OfRows(
                        AllRows(model));
            case ScorecardClosing.Window:
                {
                    List<MemberBodySizeProjectionRow> head =
                        SelectBounded(
                            model,
                            s_shape.WindowLast,
                            keepLargest: true);
                    if (head.Count < s_shape.WindowLast)
                    {
                        return ScorecardAnswer<
                            MemberBodySizeProjectionRow>
                            .OfWindowFailure();
                    }
                    return ScorecardAnswer<
                        MemberBodySizeProjectionRow>.OfRows(
                            head.GetRange(
                                s_shape.WindowSkip,
                                s_shape.WindowTake));
                }
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(closing));
        }
    }

    static List<MemberBodySizeProjectionRow> AllRows(
        Model model)
    {
        var rows =
            new List<MemberBodySizeProjectionRow>(
                model.Groups.Length);
        foreach (BodyGroup group in model.Groups)
        {
            MemberBodySizeProjectionRow row =
                Project(model, group);
            if (row.LargestIlBytes >= MinimumIlBytes)
                rows.Add(row);
        }
        rows.Sort(RowComparer.Instance);
        return rows;
    }

    static List<MemberBodySizeProjectionRow> SelectBounded(
        Model model,
        int maximumRows,
        bool keepLargest)
    {
        var rows =
            new List<MemberBodySizeProjectionRow>(
                Math.Min(maximumRows, model.Groups.Length));
        foreach (BodyGroup group in model.Groups)
        {
            MemberBodySizeProjectionRow row =
                Project(model, group);
            if (row.LargestIlBytes < MinimumIlBytes)
                continue;
            int index = rows.BinarySearch(
                row,
                RowComparer.Instance);
            if (index < 0)
                index = ~index;
            rows.Insert(index, row);
            if (rows.Count > maximumRows)
            {
                rows.RemoveAt(
                    keepLargest
                        ? rows.Count - 1
                        : 0);
            }
        }
        return rows;
    }

    static MemberBodySizeProjectionRow Project(
        Model model,
        BodyGroup group)
    {
        int largest = 0;
        int bodies = 0;
        foreach (int token in group.PhysicalTokens)
        {
            EntityHandle entity =
                MetadataTokens.EntityHandle(token);
            if (entity.Kind
                != HandleKind.MethodDefinition)
            {
                throw new BadImageFormatException(
                    $"0x{token:X8} is not a MethodDef token.");
            }
            MethodDefinition method =
                model.Reader.GetMethodDefinition(
                    (MethodDefinitionHandle)entity);
            if (method.RelativeVirtualAddress == 0
                || !LibraryMethodAnalysisRunner
                    .HasManagedIlBody(method.ImplAttributes))
            {
                continue;
            }
            MethodBodyBlock body =
                model.PeReader.GetMethodBody(
                    method.RelativeVirtualAddress);
            int length =
                body.GetILReader().Length;
            largest = Math.Max(largest, length);
            bodies++;
        }
        return new(
            group.LogicalToken,
            largest,
            bodies);
    }

    static List<MemberBodySizeProjectionRow> NormalizeCurrent(
        LibraryBodyAnalysisExecution execution,
        ImmutableHashSet<int> scope)
    {
        EnsureComplete(execution);
        return
        [
            .. execution.ImplementationMetrics.Bodies
                .Where(body =>
                    scope.Contains(
                        body.Method.MetadataToken)
                    && body.ILBytes is not null)
                .GroupBy(static body =>
                    body.Method.MetadataToken)
                .Select(static bodies =>
                    new MemberBodySizeProjectionRow(
                        bodies.Key,
                        bodies.Max(static body =>
                            body.ILBytes!.Value),
                        bodies.Select(static body =>
                                body.EvidenceMethod
                                    .MetadataToken)
                            .Distinct()
                            .Count()))
                .Where(static row =>
                    row.LargestIlBytes
                        >= MinimumIlBytes)
                .OrderBy(
                    static row => row,
                    RowComparer.Instance),
        ];
    }

    static ScorecardAnswer<MemberBodySizeProjectionRow> Answer(
        ScorecardClosing closing,
        IReadOnlyList<MemberBodySizeProjectionRow> rows,
        ScorecardShape shape) =>
        closing switch
        {
            ScorecardClosing.Exists =>
                ScorecardAnswer<
                    MemberBodySizeProjectionRow>
                    .OfExists(rows.Count > 0),
            ScorecardClosing.Count =>
                ScorecardAnswer<
                    MemberBodySizeProjectionRow>
                    .OfCount(rows.Count),
            ScorecardClosing.Head =>
                ScorecardAnswer<
                    MemberBodySizeProjectionRow>.OfRows(
                        rows.Take(shape.N).ToArray()),
            ScorecardClosing.Tail =>
                ScorecardAnswer<
                    MemberBodySizeProjectionRow>.OfRows(
                        rows.TakeLast(shape.N).ToArray()),
            ScorecardClosing.Rows =>
                ScorecardAnswer<
                    MemberBodySizeProjectionRow>
                    .OfRows(rows),
            ScorecardClosing.Window =>
                rows.Count
                    >= shape.WindowSkip
                        + shape.WindowTake
                    ? ScorecardAnswer<
                        MemberBodySizeProjectionRow>.OfRows(
                            rows.Skip(shape.WindowSkip)
                                .Take(shape.WindowTake)
                                .ToArray())
                    : ScorecardAnswer<
                        MemberBodySizeProjectionRow>
                        .OfWindowFailure(),
            _ => throw new ArgumentOutOfRangeException(
                nameof(closing)),
        };

    static ScorecardAnswer<MemberBodySizeProjectionRow>
        StrictWindow(
            IEnumerable<MemberBodySizeProjectionRow> rows)
    {
        MemberBodySizeProjectionRow[] window =
        [
            .. rows.Skip(s_shape.WindowSkip)
                .Take(s_shape.WindowTake),
        ];
        return window.Length == s_shape.WindowTake
            ? ScorecardAnswer<
                MemberBodySizeProjectionRow>.OfRows(window)
            : ScorecardAnswer<
                MemberBodySizeProjectionRow>
                .OfWindowFailure();
    }

    static LibraryBodyAnalysisExecution ExecuteCurrent(
        string name,
        ImmutableArray<byte> image,
        ImmutableHashSet<int> scope,
        int assemblyMethodCount)
    {
        long imageBytes = Math.Max(1, image.Length);
        var limits = new ImplementationMetricWorkLimits(
            maximumPhysicalBodies:
                Math.Max(1, assemblyMethodCount),
            maximumEncodedIlBytes: imageBytes,
            maximumAttributionProbeBodies:
                Math.Max(1, assemblyMethodCount),
            maximumAttributionProbeIlBytes:
                imageBytes);
        return LibraryBodyAnalysisService.ExecuteImage(
            name,
            image,
            LibraryBodyAnalysisRequest
                .CreateImplementationMetrics(
                    ImplementationMetricKind.BodySize,
                    limits,
                    scope));
    }

    static void EnsureComplete(
        LibraryBodyAnalysisExecution execution)
    {
        LibraryImplementationMetricAnalysisResult result =
            execution.ImplementationMetrics;
        ImplementationMetricParticipationReceipt participation =
            result.Participation
            ?? throw new InvalidOperationException(
                "Body-size participation was not published.");
        ImplementationMetricWorkBudgetSnapshot work =
            participation.Work
            ?? throw new InvalidOperationException(
                "Body-size work was not published.");
        if (!participation.HasCompleteStageParticipation
            || work.AttributionExhaustedLimit is not null
            || work.MetricExhaustedLimit is not null
            || !result.Diagnostics.IsEmpty)
        {
            throw new InvalidOperationException(
                "The body-size scorecard requires complete "
                + "current-path evidence.");
        }
    }

    static string AnswerHash(
        ScorecardAnswer<MemberBodySizeProjectionRow> answer)
    {
        ulong hash = 14695981039346656037;
        Add(answer.WindowFailed ? 1 : 0);
        Add(answer.Exists switch
        {
            true => 1,
            false => 0,
            null => -1,
        });
        Add(answer.Count ?? -1);
        if (answer.Rows is { } rows)
        {
            Add(rows.Count);
            foreach (MemberBodySizeProjectionRow row in rows)
            {
                Add(row.MethodToken);
                Add(row.LargestIlBytes);
                Add(row.PhysicalBodyCount);
            }
        }
        return hash.ToString("x16", CultureInfo.InvariantCulture);

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

    static string RowText(
        MemberBodySizeProjectionRow row) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{row.MethodToken:X8}:{row.LargestIlBytes}:"
            + $"{row.PhysicalBodyCount}");

    static void MeasureClosing(
        List<MemberBodySizeScorecardCell> cells,
        Asset asset,
        ScorecardClosing closing,
        MemberBodySizeScorecardTiming timing)
    {
        var failed = closing == ScorecardClosing.Window
            ? new HashSet<string>(
                s_columns
                    .Where(column =>
                        column.Execute(
                                closing,
                                asset.Model)
                            .WindowFailed)
                    .Select(static column =>
                        column.Name))
            : [];
        var times = s_columns.ToDictionary(
            static column => column.Name,
            static _ => new List<double>());
        var allocations = s_columns.ToDictionary(
            static column => column.Name,
            static _ => new List<long>());
        foreach (Column column in s_columns)
        {
            if (failed.Contains(column.Name))
                continue;
            for (int warmup = 0;
                warmup < timing.Warmup;
                warmup++)
            {
                GC.KeepAlive(
                    column.Execute(
                        closing,
                        asset.Model));
            }
        }

        for (int round = 0;
            round < timing.Rounds;
            round++)
        {
            for (int offset = 0;
                offset < s_columns.Count;
                offset++)
            {
                Column column =
                    s_columns[
                        (round + offset)
                        % s_columns.Count];
                if (failed.Contains(column.Name))
                    continue;
                Measurement measurement =
                    Measure(
                        () => column.Execute(
                            closing,
                            asset.Model),
                        timing.SamplesPerRound);
                times[column.Name].Add(
                    measurement.Microseconds);
                allocations[column.Name].Add(
                    measurement.AllocatedBytes);
            }
        }

        foreach (Column column in s_columns)
        {
            bool windowFailed =
                failed.Contains(column.Name);
            cells.Add(
                new(
                    asset.Index,
                    asset.Scenario.Name,
                    closing,
                    column.Name,
                    windowFailed
                        ? 0
                        : Median(times[column.Name]),
                    windowFailed
                        ? 0
                        : Median(allocations[column.Name]),
                    windowFailed));
        }
    }

    static MemberBodySizePreparationCell MeasurePreparation(
        Asset asset,
        MemberBodySizeScorecardTiming timing)
    {
        for (int warmup = 0;
            warmup < timing.Warmup;
            warmup++)
        {
            GC.KeepAlive(asset.Prepare());
        }
        var times = new List<double>();
        var allocations = new List<long>();
        PreparedPopulation? population = null;
        for (int round = 0;
            round < timing.Rounds;
            round++)
        {
            Measurement measurement = Measure(
                () =>
                {
                    population = asset.Prepare();
                    return population;
                },
                timing.SamplesPerRound);
            times.Add(measurement.Microseconds);
            allocations.Add(measurement.AllocatedBytes);
        }
        population ??= asset.Prepare();
        return new(
            asset.Scenario.Name,
            Median(times),
            Median(allocations),
            asset.Model.AssemblyMethodCount,
            asset.Model.Scope.Count,
            population.Groups.Sum(static group =>
                group.PhysicalTokens.Length),
            population.AttributionProbeBodies,
            population.AttributionProbeIlBytes);
    }

    static Measurement Measure(
        Func<object> execute,
        int samples)
    {
        var times = new double[samples];
        var allocations = new long[samples];
        for (int sample = 0;
            sample < samples;
            sample++)
        {
            long allocatedBefore =
                GC.GetTotalAllocatedBytes(precise: true);
            long started = Stopwatch.GetTimestamp();
            object result = execute();
            long elapsed =
                Stopwatch.GetTimestamp() - started;
            allocations[sample] =
                GC.GetTotalAllocatedBytes(precise: true)
                - allocatedBefore;
            times[sample] =
                elapsed * 1_000_000.0
                / Stopwatch.Frequency;
            GC.KeepAlive(result);
        }
        Array.Sort(times);
        Array.Sort(allocations);
        return new(
            times[times.Length / 2],
            allocations[allocations.Length / 2]);
    }

    static double Median(List<double> values)
    {
        values.Sort();
        return values[values.Count / 2];
    }

    static long Median(List<long> values)
    {
        values.Sort();
        return values[values.Count / 2];
    }

    static RatioStatistics RatioSummary(
        IReadOnlyList<MemberBodySizeScorecardCell> cells,
        ScorecardClosing closing,
        string column)
    {
        double logarithms = 0;
        var ratios = new List<double>();
        foreach (IGrouping<int, MemberBodySizeScorecardCell>
            scenario in cells
                .Where(cell =>
                    cell.Closing == closing
                    && !cell.WindowFailed)
                .GroupBy(static cell =>
                    cell.ScenarioIndex))
        {
            double oracle =
                scenario.Single(static cell =>
                        cell.Column
                            == "Planner (experimental)")
                    .Microseconds;
            double ratio =
                scenario.Single(cell =>
                    cell.Column == column)
                .Microseconds
                / oracle;
            logarithms += Math.Log(ratio);
            ratios.Add(ratio);
        }
        return ratios.Count == 0
            ? new(double.NaN, double.NaN, double.NaN)
            : new(
                Math.Exp(logarithms / ratios.Count),
                ratios.Min(),
                ratios.Max());
    }

    static RatioStatistics TerminalRatioSummary(
        IReadOnlyList<MemberBodySizeScorecardCell> cells,
        string column,
        ScorecardClosing closing,
        Func<MemberBodySizeScorecardCell, double> value)
    {
        var ratios = new List<double>();
        foreach (IGrouping<int, MemberBodySizeScorecardCell>
            scenario in cells
                .Where(cell =>
                    cell.Column == column
                    && !cell.WindowFailed
                    && (cell.Closing == closing
                        || cell.Closing == ScorecardClosing.Count))
                .GroupBy(static cell =>
                    cell.ScenarioIndex))
        {
            double baseline =
                value(
                    scenario.Single(cell =>
                        cell.Closing == ScorecardClosing.Count));
            if (baseline <= 0)
                continue;
            ratios.Add(
                value(
                    scenario.Single(cell =>
                        cell.Closing == closing))
                    / baseline);
        }
        return ratios.Count == 0
            ? new(double.NaN, double.NaN, double.NaN)
            : new(
                Math.Exp(ratios.Average(Math.Log)),
                ratios.Min(),
                ratios.Max());
    }

    static string FormatRatio(
        RatioStatistics ratio) =>
        double.IsNaN(ratio.GeometricMean)
            ? "-"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{ratio.GeometricMean:F2}x "
                + $"({ratio.Minimum:F2}-{ratio.Maximum:F2}x)");

    static string FormatTerminalBaseline(
        IReadOnlyList<MemberBodySizeScorecardCell> cells,
        string column,
        Func<MemberBodySizeScorecardCell, double> value) =>
        double.IsNaN(
            TerminalRatioSummary(
                cells,
                column,
                ScorecardClosing.Count,
                value).GeometricMean)
            ? "-"
            : "1.00x";

    static void ValidateTiming(
        MemberBodySizeScorecardTiming timing)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(
            timing.Rounds,
            1);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            timing.SamplesPerRound,
            1);
        ArgumentOutOfRangeException.ThrowIfNegative(
            timing.Warmup);
    }

    static string AssemblyName(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        MetadataReader reader =
            peReader.GetMetadataReader();
        return reader.GetString(
            reader.GetAssemblyDefinition().Name);
    }

    static ImmutableHashSet<int> ResolveScope(
        MetadataReader reader,
        MemberBodySizeScorecardScenario scenario)
    {
        TypeDefinitionHandle typeHandle =
            reader.TypeDefinitions.Single(handle =>
            {
                TypeDefinition type =
                    reader.GetTypeDefinition(handle);
                return reader.StringComparer.Equals(
                        type.Namespace,
                        scenario.Namespace)
                    && reader.StringComparer.Equals(
                        type.Name,
                        scenario.TypeName);
            });
        TypeDefinition type =
            reader.GetTypeDefinition(typeHandle);
        int[] methods =
        [
            .. type.GetMethods()
                .Where(handle =>
                {
                    MethodDefinition method =
                        reader.GetMethodDefinition(handle);
                    return (method.Attributes
                            & MethodAttributes.MemberAccessMask)
                        == MethodAttributes.Public
                        && reader.StringComparer.Equals(
                            method.Name,
                            scenario.MethodName);
                })
                .Select(static handle =>
                    MetadataTokens.GetToken(handle)),
        ];
        if (methods.Length == 0)
        {
            throw new InvalidOperationException(
                $"{scenario.Namespace}.{scenario.TypeName}."
                + $"{scenario.MethodName} was not found.");
        }
        if (scenario.Scope
            == MemberBodySizeScope.FirstLogicalMethod)
        {
            return ImmutableHashSet.Create(methods[0]);
        }
        return methods.ToImmutableHashSet();
    }

    static PreparedPopulation Prepare(
        string name,
        ImmutableArray<byte> image,
        ImmutableHashSet<int> scope,
        int assemblyMethodCount)
    {
        LibraryBodyAnalysisExecution execution =
            ExecuteCurrent(
                name,
                image,
                scope,
                assemblyMethodCount);
        EnsureComplete(execution);
        ImmutableArray<BodyGroup> groups =
        [
            .. scope.Order()
                .Select(token =>
                    new BodyGroup(
                        token,
                        [
                            .. execution
                                .ImplementationMetrics
                                .Bodies
                                .Where(body =>
                                    body.Method.MetadataToken
                                        == token
                                    && body.ILBytes is not null)
                                .Select(static body =>
                                    body.EvidenceMethod
                                        .MetadataToken)
                                .Distinct()
                                .Order(),
                        ])),
        ];
        ImplementationMetricWorkBudgetSnapshot work =
            execution.ImplementationMetrics
                .Participation!.Work!;
        return new(
            groups,
            work.AttributionProbeBodies,
            work.AttributionProbeIlBytes);
    }

    sealed record Column(
        string Name,
        Executor Execute);

    delegate ScorecardAnswer<MemberBodySizeProjectionRow>
        Executor(
            ScorecardClosing closing,
            Model model);

    sealed record Model(
        string Name,
        ImmutableArray<byte> Image,
        PEReader PeReader,
        MetadataReader Reader,
        ImmutableHashSet<int> Scope,
        ImmutableArray<BodyGroup> Groups,
        int AssemblyMethodCount);

    sealed record PreparedPopulation(
        ImmutableArray<BodyGroup> Groups,
        int AttributionProbeBodies,
        long AttributionProbeIlBytes);

    readonly record struct BodyGroup(
        int LogicalToken,
        ImmutableArray<int> PhysicalTokens);

    readonly record struct Measurement(
        double Microseconds,
        long AllocatedBytes);

    readonly record struct RatioStatistics(
        double GeometricMean,
        double Minimum,
        double Maximum);

    readonly struct ProjectRow(Model model)
        : IFunc<
            BodyGroup,
            MemberBodySizeProjectionRow>
    {
        public MemberBodySizeProjectionRow Invoke(
            BodyGroup group) =>
            MemberBodySizeScorecard.Project(
                model,
                group);
    }

    readonly struct Eligible
        : IFunc<
            MemberBodySizeProjectionRow,
            bool>
    {
        public bool Invoke(
            MemberBodySizeProjectionRow row) =>
            row.LargestIlBytes >= MinimumIlBytes;
    }

    sealed class RowComparer
        : IComparer<MemberBodySizeProjectionRow>
    {
        internal static RowComparer Instance { get; } =
            new();

        public int Compare(
            MemberBodySizeProjectionRow x,
            MemberBodySizeProjectionRow y)
        {
            int size = y.LargestIlBytes.CompareTo(
                x.LargestIlBytes);
            return size != 0
                ? size
                : x.MethodToken.CompareTo(y.MethodToken);
        }
    }

    sealed class Asset : IDisposable
    {
        readonly PEReader _peReader;

        Asset(
            int index,
            MemberBodySizeScorecardScenario scenario,
            ImmutableArray<byte> image,
            PEReader peReader,
            MetadataReader reader,
            ImmutableHashSet<int> scope,
            PreparedPopulation population)
        {
            Index = index;
            Scenario = scenario;
            _peReader = peReader;
            Model = new(
                Path.GetFileName(scenario.AssemblyPath),
                image,
                peReader,
                reader,
                scope,
                population.Groups,
                reader.MethodDefinitions.Count);
        }

        internal int Index { get; }

        internal MemberBodySizeScorecardScenario Scenario
        { get; }

        internal Model Model { get; }

        internal static Asset Open(
            MemberBodySizeScorecardScenario scenario,
            int index = 0)
        {
            byte[] bytes =
                File.ReadAllBytes(scenario.AssemblyPath);
            ImmutableArray<byte> image =
                ImmutableArray.Create(bytes);
            var peReader = new PEReader(image);
            try
            {
                MetadataReader reader =
                    peReader.GetMetadataReader();
                ImmutableHashSet<int> scope =
                    ResolveScope(reader, scenario);
                PreparedPopulation population =
                    MemberBodySizeScorecard.Prepare(
                        Path.GetFileName(
                            scenario.AssemblyPath),
                        image,
                        scope,
                        reader.MethodDefinitions.Count);
                return new(
                    index,
                    scenario,
                    image,
                    peReader,
                    reader,
                    scope,
                    population);
            }
            catch
            {
                peReader.Dispose();
                throw;
            }
        }

        internal PreparedPopulation Prepare() =>
            MemberBodySizeScorecard.Prepare(
                Model.Name,
                Model.Image,
                Model.Scope,
                Model.AssemblyMethodCount);

        public void Dispose() =>
            _peReader.Dispose();
    }

    sealed class LoadedAssets(
        IReadOnlyList<Asset> assets)
        : IDisposable
    {
        internal IReadOnlyList<Asset> Assets { get; } =
            assets;

        internal static LoadedAssets Open(
            IReadOnlyList<MemberBodySizeScorecardScenario>
                scenarios)
        {
            if (scenarios.Count == 0)
            {
                throw new ArgumentException(
                    "At least one scorecard scenario is required.",
                    nameof(scenarios));
            }
            var assets = new List<Asset>();
            try
            {
                for (int index = 0;
                    index < scenarios.Count;
                    index++)
                {
                    assets.Add(
                        Asset.Open(
                            scenarios[index],
                            index));
                }
                return new(assets);
            }
            catch
            {
                foreach (Asset asset in assets)
                    asset.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            foreach (Asset asset in Assets)
                asset.Dispose();
        }
    }
}
