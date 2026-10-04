using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using DotnetInspector.PerformanceOracles;
using DotnetInspector.Queries;
using ILInspector.Analysis.Classification;
using ILInspector.Analysis.Planning;
using ILInspector.Metadata;
using ILInspector.Metadata.LegacyOracles;
using InertText;
using NLinq;

// method-classification-scorecard async|pinvoke check <assembly>...
// method-classification-scorecard async|pinvoke time [--rounds N] [--budget-ms N] [--tsv <path>] <assembly>...
//
// Old is the retired scanner (dated oracle) filtered as the CLI filtered it.
// The gate, tests, and call state live in DotnetInspector.PerformanceOracles
// (MethodClassificationOracle.cs) so other scorecards apply the same analysis.
// LINQ, NLinq, Direct, and Planner apply the identical analysis and produce the
// identical row: the analyzers' gate and tests, with the gate's per-execution
// attribute-match memos, and the Planner's projection (identity through
// MethodRowProjection, every field inert). Direct uses the PEReader
// reference path; Planner uses the production prepared session-backed request
// set, with cold preparation reported separately. Only the read machinery
// differs.
// The equivalent columns follow #8870's audit harness (tools/AnalyzerAudit on
// its scratch branch). Rows compare by method name in traversal order.
if (args.Length == 0 || args[0] is not ("async" or "pinvoke"))
{
    Console.Error.WriteLine("usage: async|pinvoke " + ScorecardCommandLine.Usage);
    return 2;
}

ClassificationPopulation population = args[0] == "async" ? ClassificationPopulation.Async : ClassificationPopulation.PInvoke;
if (!ScorecardCommandLine.TryParse(args[1..], out ScorecardOptions? options, out string? error))
{
    Console.Error.WriteLine(error);
    return 2;
}

var shape = new ScorecardShape();
ScorecardColumn<MethodClassificationAsset, string>[] columns =
    Columns.For(population, shape);
ScorecardColumn<MethodClassificationAsset, string> oracle =
    columns.Single(static column => column.Name == "NLinq");

IReadOnlyList<ScorecardAsset<MethodClassificationAsset>> assets =
    LoadAssets(options!.Assets);
try
{
    Metrics.WritePreparation(
        population,
        assets);
    ScorecardCheck check = Scorecard.Check(assets, oracle, columns, static name => name);
    foreach (ScorecardMismatch mismatch in check.Mismatches)
        Console.WriteLine($"mismatch\t{mismatch.Asset}\t{shape.Label(mismatch.Closing)}\t{mismatch.Column}\t{mismatch.Answer}\toracle={mismatch.OracleAnswer}");
    Console.WriteLine($"# {args[0]}: {check.Compared} compared, {check.Mismatches.Count} mismatches, {check.WindowFailures.Count} strict-window failures");
    if (!check.Agrees)
        return 1;
    foreach (string asset in check.WindowFailures)
        Console.WriteLine($"window-failed\t{asset}\t{shape.Label(ScorecardClosing.Window)}");

    ScorecardColumn<MethodClassificationAsset, string>[] bundleColumns =
        Columns.Bundle(population, shape);
    ScorecardCheck bundleCheck = Scorecard.Check(
        assets,
        bundleColumns[0],
        bundleColumns,
        static name => name,
        closings: [ScorecardClosing.Rows]);
    foreach (ScorecardMismatch mismatch in bundleCheck.Mismatches)
    {
        Console.WriteLine(
            $"bundle-mismatch\t{mismatch.Asset}\t{mismatch.Column}"
            + $"\t{mismatch.Answer}\toracle={mismatch.OracleAnswer}");
    }
    Console.WriteLine(
        $"# {args[0]} request-set bundle: "
        + $"{bundleCheck.Compared} compared, "
        + $"{bundleCheck.Mismatches.Count} mismatches");
    if (!bundleCheck.Agrees)
        return 1;

    Metrics.WriteWork(
        population,
        assets);
    Metrics.WriteAllocations(
        assets,
        columns,
        shape);
    Metrics.WriteAllocations(
        assets,
        bundleColumns,
        shape,
        [ScorecardClosing.Rows]);

    if (options.Command == ScorecardCommand.Check)
        return 0;

    IReadOnlyList<ScorecardCell> cells = Scorecard.Measure(assets, columns, options.Timing, progress => Console.Error.WriteLine(progress));
    IReadOnlyList<ScorecardCell> bundleCells =
        Scorecard.Measure(
            assets,
            bundleColumns,
            options.Timing,
            progress => Console.Error.WriteLine(
                $"bundle {progress}"),
            [ScorecardClosing.Rows]);
    if (options.TsvPath is { } tsvPath)
    {
        using StreamWriter tsv = File.CreateText(tsvPath);
        Scorecard.WriteTsv(
            [.. cells, .. bundleCells],
            tsv);
    }

    Console.Write(Scorecard.Report(cells, "Planner", shape));
    Console.WriteLine("# request-set Rows+Count+Exists bundle");
    Console.Write(
        Scorecard.Report(
            bundleCells,
            "Collapsed",
            shape));
    return 0;
}
finally
{
    foreach (ScorecardAsset<MethodClassificationAsset> asset in assets)
        asset.Asset.Dispose();
}

static IReadOnlyList<ScorecardAsset<MethodClassificationAsset>> LoadAssets(
    IReadOnlyList<string> paths)
{
    IReadOnlyList<ScorecardAsset<PEReader>> readers =
        MethodPopulation.LoadAssets(paths);
    var assets =
        new List<ScorecardAsset<MethodClassificationAsset>>(
            readers.Count);
    try
    {
        for (int i = 0; i < readers.Count; i++)
        {
            ScorecardAsset<PEReader> reader = readers[i];
            assets.Add(new(
                reader.Name,
                new(
                    reader.Asset,
                    AssemblyInspectionSession.Open(paths[i]))));
        }

        return assets;
    }
    catch
    {
        foreach (ScorecardAsset<MethodClassificationAsset> asset
            in assets)
        {
            asset.Asset.Dispose();
        }

        for (int i = assets.Count; i < readers.Count; i++)
            readers[i].Asset.Dispose();
        throw;
    }
}

sealed class MethodClassificationAsset(
    PEReader reader,
    AssemblyInspectionSession session) : IDisposable
{
    readonly Dictionary<
        (
            ClassificationPopulation Population,
            ScorecardClosing Closing,
            int HeadCount),
        PreparedMethodClassificationQuery> _prepared = [];
    readonly Dictionary<
        ClassificationPopulation,
        PreparedMethodClassificationQuery> _preparedBundles = [];

    public PEReader Reader { get; } = reader;

    public AssemblyInspectionSession Session { get; } = session;

    public PreparedMethodClassificationQuery Prepared(
        ClassificationPopulation population,
        ScorecardClosing closing,
        ScorecardShape shape)
    {
        var key = (
            population,
            closing,
            closing == ScorecardClosing.Head
                ? shape.N
                : 0);
        if (!_prepared.TryGetValue(
                key,
                out PreparedMethodClassificationQuery? prepared))
        {
            prepared = MethodClassificationQuery.Prepare(
                Session,
                [Columns.PlannerQuestion(
                    population,
                    closing,
                    shape)]);
            _prepared.Add(key, prepared);
        }

        return prepared;
    }

    public PreparedMethodClassificationQuery PreparedBundle(
        ClassificationPopulation population)
    {
        if (!_preparedBundles.TryGetValue(
                population,
                out PreparedMethodClassificationQuery? prepared))
        {
            (
                ClassificationQuestion rows,
                ClassificationQuestion count,
                ClassificationQuestion exists) =
                    Columns.BundleQuestions(population);
            prepared = MethodClassificationQuery.Prepare(
                Session,
                [rows, count, exists]);
            _preparedBundles.Add(population, prepared);
        }

        return prepared;
    }

    public void Dispose()
    {
        Session.Dispose();
        Reader.Dispose();
    }
}

static class Metrics
{
    public static void WritePreparation(
        ClassificationPopulation population,
        IReadOnlyList<ScorecardAsset<MethodClassificationAsset>>
            assets)
    {
        foreach (ScorecardAsset<MethodClassificationAsset> asset
            in assets)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long retainedBefore = GC.GetTotalMemory(
                forceFullCollection: true);
            long allocatedBefore =
                GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            PreparedMethodClassificationQuery prepared =
                asset.Asset.PreparedBundle(population);
            double elapsedMicroseconds =
                Stopwatch.GetElapsedTime(started)
                    .TotalMicroseconds;
            long allocated =
                GC.GetAllocatedBytesForCurrentThread()
                - allocatedBefore;
            long retainedAfter = GC.GetTotalMemory(
                forceFullCollection: true);

            Console.WriteLine(
                $"preparation\t{asset.Name}"
                + "\tRows+Count+Exists"
                + $"\tcold-us={elapsedMicroseconds:F3}"
                + $"\tcold-allocated={allocated}"
                + "\tretained-heap-delta="
                + Math.Max(0, retainedAfter - retainedBefore)
                + "\treuse=explicit-prepared-query");
            GC.KeepAlive(prepared);
        }
    }

    public static void WriteWork(
        ClassificationPopulation population,
        IReadOnlyList<ScorecardAsset<MethodClassificationAsset>>
            assets)
    {
        MethodClassificationAnalyzer analyzer =
            population == ClassificationPopulation.Async
                ? MethodClassificationAnalyzer.Async
                : MethodClassificationAnalyzer.PInvoke;
        ClassificationQuestion rows =
            new(analyzer, ClassificationClosing.Rows);
        ClassificationQuestion count =
            new(analyzer, ClassificationClosing.Count);
        ClassificationQuestion exists =
            new(analyzer, ClassificationClosing.Exists);
        ClassificationQuestion[] questions = [rows, count, exists];
        foreach (ScorecardAsset<MethodClassificationAsset> asset
            in assets)
        {
            foreach (ClassificationQuestion question in questions)
            {
                MethodClassificationResult direct =
                    MethodClassificationQuery.Execute(
                        asset.Asset.Reader,
                        [question]);
                MethodClassificationResult singleton =
                    MethodClassificationQuery.Execute(
                        asset.Asset.Session,
                        [question]);
                var execution = new ClassificationExecution(
                    question.Closing,
                    question.HeadCount);
                WorkReceipt directReceipt =
                    direct.ReceiptOf(execution);
                WorkReceipt singletonReceipt =
                    singleton.ReceiptOf(execution);
                MethodDefinitionSourceGroupReceipt source =
                    singleton.SourceGroups.Single();
                Console.WriteLine(
                    $"work\t{asset.Name}\t{question.Closing}"
                    + $"\tdirect-units={directReceipt.UnitsVisited}"
                    + $"\tsingleton-units={singletonReceipt.UnitsVisited}"
                    + "\tsingleton-physical="
                    + source.PhysicalCoverage.MethodsSelected.Count
                    + "\tdirect-identity="
                    + directReceipt.IdentityWorkCharged
                    + "\tsingleton-identity="
                    + singletonReceipt.IdentityWorkCharged
                    + $"\tmaterialized-rows={MaterializedRows(singleton, question)}"
                    + $"\tcardinality={Cardinality(singleton, question)}");
            }

            MethodClassificationResult collapsed =
                MethodClassificationQuery.Execute(
                    asset.Asset.Session,
                    questions);
            MethodDefinitionSourceGroupReceipt collapsedSource =
                collapsed.SourceGroups.Single();
            Console.WriteLine(
                $"work\t{asset.Name}\tRows+Count+Exists"
                + "\tindependent-physical="
                + questions.Sum(
                    question =>
                        MethodClassificationQuery.Execute(
                            asset.Asset.Session,
                            [question])
                        .SourceGroups.Single()
                        .PhysicalCoverage.MethodsSelected.Count)
                + "\tcollapsed-physical="
                + collapsedSource.PhysicalCoverage.MethodsSelected.Count
                + "\tlanes="
                + collapsedSource.LaneReceipts.Length
                + "\tmaterialized-rows="
                + MaterializedRows(collapsed, rows));
        }
    }

    public static void WriteAllocations(
        IReadOnlyList<ScorecardAsset<MethodClassificationAsset>>
            assets,
        IReadOnlyList<
            ScorecardColumn<MethodClassificationAsset, string>>
                columns,
        ScorecardShape shape,
        IReadOnlyList<ScorecardClosing>? closings = null)
    {
        closings ??= Scorecard.Closings;
        foreach (ScorecardAsset<MethodClassificationAsset> asset
            in assets)
        {
            foreach (ScorecardClosing closing in closings)
            {
                foreach (ScorecardColumn<
                    MethodClassificationAsset,
                    string> column in columns)
                {
                    ScorecardAnswer<string> first =
                        column.Answer(closing, asset.Asset);
                    if (closing == ScorecardClosing.Window
                        && first.WindowFailed)
                    {
                        continue;
                    }

                    for (int i = 0; i < 3; i++)
                        _ = column.Answer(closing, asset.Asset);

                    var samples = new long[21];
                    for (int i = 0; i < samples.Length; i++)
                    {
                        long before =
                            GC.GetAllocatedBytesForCurrentThread();
                        _ = column.Answer(closing, asset.Asset);
                        samples[i] =
                            GC.GetAllocatedBytesForCurrentThread()
                            - before;
                    }

                    Array.Sort(samples);
                    Console.WriteLine(
                        $"allocation\t{asset.Name}"
                        + $"\t{shape.Label(closing)}"
                        + $"\t{column.Name}"
                        + $"\t{samples[samples.Length / 2]}");
                }
            }
        }
    }

    static int MaterializedRows(
        MethodClassificationResult result,
        ClassificationQuestion question) =>
        result.AnswerTo(question)
            is ClassificationAnswer.Rows rows
                ? rows.Methods.Length
                : 0;

    static string Cardinality(
        MethodClassificationResult result,
        ClassificationQuestion question) =>
        result.AnswerTo(question) switch
        {
            ClassificationAnswer.Rows rows =>
                rows.Methods.Length.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
            ClassificationAnswer.Count count =>
                count.Value.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
            ClassificationAnswer.Exists exists =>
                exists.Value ? "true" : "false",
            _ => "incomplete",
        };
}

/// <summary>The Planner's row, built as its projection builds it.</summary>
static class Rows
{
    public static ClassifiedMethodRow Project(MethodClassificationCallState state, MethodDefinitionRow row, MethodClassification classification)
    {
        MetadataReader reader = state.Reader;
        MethodAnchorInfo? anchor = MethodRowProjection.TryCreateMethodIdentity(
            reader, row.TypeHandle, row.Method, ref state.IdentityDecodeFailures, ref state.IdentityWork);
        string name = reader.GetString(row.Method.Name);
        string signature = MethodRowProjection.FormatSignatureOrFallback(reader, row.Type, row.Method, name, anchor);
        string declaring = MethodRowProjection.FormatDeclaringTypeName(reader, row.TypeHandle);
        string ns = reader.GetString(row.Type.Namespace);
        string? module = classification == MethodClassification.PInvoke
            ? MethodRowProjection.GetPInvokeModuleName(reader, row.MethodHandle)
            : null;
        return new ClassifiedMethodRow(
            MetadataTokens.GetToken(row.MethodHandle),
            MetadataTokens.GetRowNumber(row.MethodHandle) - 1,
            Inert(name),
            Inert(declaring),
            Inert(ns),
            Inert(signature),
            classification,
            module is null ? null : Inert(module),
            anchor is null
                ? null
                : new MethodRowAnchor(
                    anchor.Anchor,
                    Inert(anchor.Anchor.StableSelector),
                    Inert(anchor.Anchor.CanonicalSignature),
                    Inert(anchor.Anchor.TypeFullName),
                    Inert(anchor.Anchor.MemberName)),
            anchor is null ? null : Inert(anchor.ReturnType));
    }

    static InertString Inert(string text) => new(TextPolicy.Field, text);
}

/// <summary>A lazy view: rows become names only when compared, never inside a timed call.</summary>
sealed class View<T>(IReadOnlyList<T> rows, Func<T, string> name) : IReadOnlyList<string>
{
    public string this[int index] => name(rows[index]);

    public int Count => rows.Count;

    public IEnumerator<string> GetEnumerator()
    {
        foreach (T row in rows)
            yield return name(row);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

static class Columns
{
    public static ScorecardColumn<MethodClassificationAsset, string>[] For(
        ClassificationPopulation population,
        ScorecardShape shape) =>
    [
        new(
            "Old",
            (closing, asset) =>
                Old(
                    population,
                    closing,
                    asset.Reader,
                    shape)),
        new(
            "LINQ",
            (closing, asset) =>
                Linq(
                    population,
                    closing,
                    asset.Reader,
                    shape)),
        new(
            "NLinq",
            (closing, asset) =>
                NLinqColumn(
                    population,
                    closing,
                    asset.Reader,
                    shape)),
        new(
            "Direct",
            (closing, asset) =>
                Planner(
                    population,
                    closing,
                    asset.Reader,
                    shape)),
        new(
            "Planner",
            (closing, asset) =>
                Planner(
                    population,
                    closing,
                    asset,
                    shape)),
    ];

    public static ScorecardColumn<
        MethodClassificationAsset,
        string>[] Bundle(
            ClassificationPopulation population,
            ScorecardShape shape) =>
    [
        new(
            "Independent",
            (_, asset) =>
                IndependentBundle(
                    population,
                    asset,
                    shape)),
        new(
            "Collapsed",
            (_, asset) =>
                CollapsedBundle(
                    population,
                    asset,
                    shape)),
    ];

    static ScorecardAnswer<string> Answer<T>(ScorecardClosing closing, List<T> rows, Func<T, string> name, ScorecardShape shape) =>
        closing switch
        {
            ScorecardClosing.Exists => ScorecardAnswer<string>.OfExists(rows.Count > 0),
            ScorecardClosing.Count => ScorecardAnswer<string>.OfCount(rows.Count),
            ScorecardClosing.Head => ScorecardAnswer<string>.OfRows(new View<T>(rows.GetRange(0, Math.Min(shape.N, rows.Count)), name)),
            ScorecardClosing.Tail => ScorecardAnswer<string>.OfRows(new View<T>(rows.GetRange(Math.Max(0, rows.Count - shape.N), Math.Min(shape.N, rows.Count)), name)),
            ScorecardClosing.Rows => ScorecardAnswer<string>.OfRows(new View<T>(rows, name)),
            ScorecardClosing.Window => rows.Count >= shape.WindowSkip + shape.WindowTake
                ? ScorecardAnswer<string>.OfRows(new View<T>(rows.GetRange(shape.WindowSkip, shape.WindowTake), name))
                : ScorecardAnswer<string>.OfWindowFailure(),
            _ => throw new ArgumentOutOfRangeException(nameof(closing)),
        };

    /// <summary>The retired path: classify every method into rows, then filter and close.</summary>
    static ScorecardAnswer<string> Old(ClassificationPopulation population, ScorecardClosing closing, PEReader pe, ScorecardShape shape)
    {
        List<ClassifiedMethodInfo> rows = [.. LegacyMethodClassificationScanner.Scan(pe)
            .Where(m => population == ClassificationPopulation.Async
                ? m.Classification is MethodClassification.RuntimeAsync or MethodClassification.StateMachineAsync
                : m.Classification == MethodClassification.PInvoke)];
        return Answer(closing, rows, static m => m.MethodName, shape);
    }

    /// <summary>
    /// An idiomatic streaming System.Linq pipeline over the same gate and
    /// tests. Row-returning closings project only the rows they consume;
    /// Exists and Count never construct the projection.
    /// </summary>
    static ScorecardAnswer<string> Linq(ClassificationPopulation population, ScorecardClosing closing, PEReader pe, ScorecardShape shape)
    {
        MetadataReader reader = pe.GetMetadataReader();
        var state = new MethodClassificationCallState(reader);
        IEnumerable<MethodDefinitionRow> selected = reader.TypeDefinitions
            .Select(handle => (Handle: handle, Type: reader.GetTypeDefinition(handle)))
            .Where(type => MethodClassificationGate.TypeInScope(reader, type.Type))
            .SelectMany(type => type.Type.GetMethods().Select(method => new MethodDefinitionRow(reader, type.Handle, type.Type, method, reader.GetMethodDefinition(method))))
            .Where(row => MethodClassificationGate.MethodInScope(reader, row.Method))
            .Where(row => MethodClassificationTests.Classify(population, state, row.Method) is not null);
        return closing switch
        {
            ScorecardClosing.Exists => ScorecardAnswer<string>.OfExists(selected.Any()),
            ScorecardClosing.Count => ScorecardAnswer<string>.OfCount(selected.Count()),
            ScorecardClosing.Head => ScorecardAnswer<string>.OfRows(new View<ClassifiedMethodRow>(
                selected
                    .Take(shape.N)
                    .Select(row => Rows.Project(state, row, ClassOf(population, row.Method)))
                    .ToList(),
                Name)),
            ScorecardClosing.Tail => ScorecardAnswer<string>.OfRows(new View<ClassifiedMethodRow>(
                selected
                    .TakeLast(shape.N)
                    .Select(row => Rows.Project(state, row, ClassOf(population, row.Method)))
                    .ToList(),
                Name)),
            ScorecardClosing.Rows => ScorecardAnswer<string>.OfRows(new View<ClassifiedMethodRow>(
                selected
                    .Select(row => Rows.Project(state, row, ClassOf(population, row.Method)))
                    .ToList(),
                Name)),
            ScorecardClosing.Window => selected
                    .Skip(shape.WindowSkip)
                    .Take(shape.WindowTake)
                    .Select(row => Rows.Project(state, row, ClassOf(population, row.Method)))
                    .ToList() is { } window
                && window.Count == shape.WindowTake
                    ? ScorecardAnswer<string>.OfRows(new View<ClassifiedMethodRow>(window, Name))
                    : ScorecardAnswer<string>.OfWindowFailure(),
            _ => throw new ArgumentOutOfRangeException(nameof(closing)),
        };
    }

    /// <summary>The NLinq oracle over the method-definition source: the gate, the test, the projection, then the closing.</summary>
    static ScorecardAnswer<string> NLinqColumn(ClassificationPopulation population, ScorecardClosing closing, PEReader pe, ScorecardShape shape)
    {
        MetadataReader reader = pe.GetMetadataReader();
        var state = new MethodClassificationCallState(reader);
        var select = new Select(population, state);
        if (closing == ScorecardClosing.Exists)
        {
            return ScorecardAnswer<string>.OfExists(
                new MethodDefinitionRows(reader).Any<MethodDefinitionRows, MethodDefinitionRow, Select>(select));
        }

        var selected =
            new MethodDefinitionRows(reader)
                .Where<MethodDefinitionRows, MethodDefinitionRow, Select>(
                    select);
        if (closing == ScorecardClosing.Count)
        {
            return ScorecardAnswer<string>.OfCount(
                selected.CountFold<
                    Filter<MethodDefinitionRow, MethodDefinitionRows, Select>,
                    MethodDefinitionRow>());
        }

        var project = new Project(population, state);
        switch (closing)
        {
            case ScorecardClosing.Head:
                {
                    var rows = selected
                        .Select<Filter<MethodDefinitionRow, MethodDefinitionRows, Select>, MethodDefinitionRow, ClassifiedMethodRow, Project>(project)
                        .Take<Map<MethodDefinitionRow, ClassifiedMethodRow, Filter<MethodDefinitionRow, MethodDefinitionRows, Select>, Project>, ClassifiedMethodRow>(shape.N);
                    var list = new List<ClassifiedMethodRow>(shape.N);
                    while (true)
                    {
                        ClassifiedMethodRow row = rows.TryGetNext(out bool hasMore);
                        if (!hasMore)
                            return ScorecardAnswer<string>.OfRows(new View<ClassifiedMethodRow>(list, Name));
                        list.Add(row);
                    }
                }

            case ScorecardClosing.Tail:
                {
                    List<MethodDefinitionRow> last = selected
                        .TakeLast<Filter<MethodDefinitionRow, MethodDefinitionRows, Select>, MethodDefinitionRow>(shape.N);
                    var list = new List<ClassifiedMethodRow>(last.Count);
                    foreach (MethodDefinitionRow row in last)
                        list.Add(project.Invoke(row));
                    return ScorecardAnswer<string>.OfRows(new View<ClassifiedMethodRow>(list, Name));
                }

            case ScorecardClosing.Rows:
                return ScorecardAnswer<string>.OfRows(new View<ClassifiedMethodRow>(selected
                    .Select<Filter<MethodDefinitionRow, MethodDefinitionRows, Select>, MethodDefinitionRow, ClassifiedMethodRow, Project>(project)
                    .ToList<Map<MethodDefinitionRow, ClassifiedMethodRow, Filter<MethodDefinitionRow, MethodDefinitionRows, Select>, Project>, ClassifiedMethodRow>(), Name));

            case ScorecardClosing.Window:
                // Skip before Select, so skipped rows are never projected.
                return selected
                        .Skip<Filter<MethodDefinitionRow, MethodDefinitionRows, Select>, MethodDefinitionRow>(shape.WindowSkip)
                        .Select<SkipEnumerator<Filter<MethodDefinitionRow, MethodDefinitionRows, Select>, MethodDefinitionRow>, MethodDefinitionRow, ClassifiedMethodRow, Project>(project)
                        .TryTakeExactly<Map<MethodDefinitionRow, ClassifiedMethodRow, SkipEnumerator<Filter<MethodDefinitionRow, MethodDefinitionRows, Select>, MethodDefinitionRow>, Project>, ClassifiedMethodRow>(shape.WindowTake, out List<ClassifiedMethodRow> window)
                    ? ScorecardAnswer<string>.OfRows(new View<ClassifiedMethodRow>(window, Name))
                    : ScorecardAnswer<string>.OfWindowFailure();

            default:
                throw new ArgumentOutOfRangeException(nameof(closing));
        }
    }

    /// <summary>
    /// The enablement: Exists, Count, and Head are source closings. Tail and
    /// the strict window still read the Rows closing.
    /// </summary>
    static ScorecardAnswer<string> IndependentBundle(
        ClassificationPopulation population,
        MethodClassificationAsset asset,
        ScorecardShape shape)
    {
        (
            ClassificationQuestion rows,
            ClassificationQuestion count,
            ClassificationQuestion exists) =
                BundleQuestions(population);
        return BundleAnswer(
            MethodClassificationQuery.Execute(
                asset.Prepared(
                    population,
                    ScorecardClosing.Rows,
                    shape)),
            MethodClassificationQuery.Execute(
                asset.Prepared(
                    population,
                    ScorecardClosing.Count,
                    shape)),
            MethodClassificationQuery.Execute(
                asset.Prepared(
                    population,
                    ScorecardClosing.Exists,
                    shape)),
            rows,
            count,
            exists,
            shape);
    }

    static ScorecardAnswer<string> CollapsedBundle(
        ClassificationPopulation population,
        MethodClassificationAsset asset,
        ScorecardShape shape)
    {
        (
            ClassificationQuestion rows,
            ClassificationQuestion count,
            ClassificationQuestion exists) =
                BundleQuestions(population);
        MethodClassificationResult result =
            MethodClassificationQuery.Execute(
                asset.PreparedBundle(population));
        return BundleAnswer(
            result,
            result,
            result,
            rows,
            count,
            exists,
            shape);
    }

    internal static (
        ClassificationQuestion Rows,
        ClassificationQuestion Count,
        ClassificationQuestion Exists) BundleQuestions(
            ClassificationPopulation population)
    {
        MethodClassificationAnalyzer analyzer =
            population == ClassificationPopulation.Async
                ? MethodClassificationAnalyzer.Async
                : MethodClassificationAnalyzer.PInvoke;
        return (
            new(
                analyzer,
                ClassificationClosing.Rows),
            new(
                analyzer,
                ClassificationClosing.Count),
            new(
                analyzer,
                ClassificationClosing.Exists));
    }

    static ScorecardAnswer<string> BundleAnswer(
        MethodClassificationResult rowsResult,
        MethodClassificationResult countResult,
        MethodClassificationResult existsResult,
        ClassificationQuestion rows,
        ClassificationQuestion count,
        ClassificationQuestion exists,
        ScorecardShape shape)
    {
        ClassificationAnswer.Rows listed =
            (ClassificationAnswer.Rows)rowsResult.AnswerTo(rows);
        int counted =
            ((ClassificationAnswer.Count)countResult.AnswerTo(count)).Value;
        bool found =
            ((ClassificationAnswer.Exists)existsResult.AnswerTo(exists))
                .Value;
        if (counted != listed.Methods.Length
            || found != (listed.Methods.Length > 0))
        {
            throw new InvalidOperationException(
                "The Method Classification bundle's independent terminals "
                + "disagree.");
        }

        return Answer(
            ScorecardClosing.Rows,
            [.. listed.Methods],
            Name,
            shape);
    }

    static ScorecardAnswer<string> Planner(
        ClassificationPopulation population,
        ScorecardClosing closing,
        PEReader pe,
        ScorecardShape shape)
    {
        ClassificationQuestion question =
            PlannerQuestion(population, closing, shape);
        return PlannerAnswer(
            MethodClassificationQuery.Execute(pe, [question]),
            question,
            closing,
            shape);
    }

    static ScorecardAnswer<string> Planner(
        ClassificationPopulation population,
        ScorecardClosing closing,
        MethodClassificationAsset asset,
        ScorecardShape shape)
    {
        ClassificationQuestion question =
            PlannerQuestion(population, closing, shape);
        return PlannerAnswer(
            MethodClassificationQuery.Execute(
                asset.Prepared(
                    population,
                    closing,
                    shape)),
            question,
            closing,
            shape);
    }

    internal static ClassificationQuestion PlannerQuestion(
        ClassificationPopulation population,
        ScorecardClosing closing,
        ScorecardShape shape)
    {
        MethodClassificationAnalyzer analyzer =
            population == ClassificationPopulation.Async
                ? MethodClassificationAnalyzer.Async
                : MethodClassificationAnalyzer.PInvoke;
        return closing == ScorecardClosing.Head
            ? ClassificationQuestion.Head(analyzer, shape.N)
            : new(
                analyzer,
                closing switch
                {
                    ScorecardClosing.Exists =>
                        ClassificationClosing.Exists,
                    ScorecardClosing.Count =>
                        ClassificationClosing.Count,
                    _ => ClassificationClosing.Rows,
                });
    }

    static ScorecardAnswer<string> PlannerAnswer(
        MethodClassificationResult result,
        ClassificationQuestion question,
        ScorecardClosing closing,
        ScorecardShape shape) =>
        result.AnswerTo(question) switch
        {
            ClassificationAnswer.Exists exists => ScorecardAnswer<string>.OfExists(exists.Value),
            ClassificationAnswer.Count count => ScorecardAnswer<string>.OfCount(count.Value),
            ClassificationAnswer.Rows rows => Answer(closing, [.. rows.Methods], Name, shape),
            ClassificationAnswer answer => throw new InvalidOperationException($"The planner did not answer: {answer}"),
        };

    static string Name(ClassifiedMethodRow row) => row.MethodName.ToString();

    static MethodClassification ClassOf(ClassificationPopulation population, MethodDefinition method) =>
        population == ClassificationPopulation.PInvoke ? MethodClassification.PInvoke
        : MethodClassificationTests.IsRuntimeAsync(method) ? MethodClassification.RuntimeAsync
        : MethodClassification.StateMachineAsync;

    struct Select(ClassificationPopulation population, MethodClassificationCallState state) : IFunc<MethodDefinitionRow, bool>
    {
        public readonly bool Invoke(MethodDefinitionRow row) =>
            MethodClassificationGate.TypeInScope(row.Reader, row.Type)
            && MethodClassificationGate.MethodInScope(row.Reader, row.Method)
            && MethodClassificationTests.Classify(population, state, row.Method) is not null;
    }

    struct Project(ClassificationPopulation population, MethodClassificationCallState state) : IFunc<MethodDefinitionRow, ClassifiedMethodRow>
    {
        // As the Planner's projections: a selected row's class follows from
        // the population and the runtime flag.
        public readonly ClassifiedMethodRow Invoke(MethodDefinitionRow row) =>
            Rows.Project(state, row, ClassOf(population, row.Method));
    }
}
