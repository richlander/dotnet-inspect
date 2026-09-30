using System.Collections.Immutable;
using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

using DotnetInspector.PerformanceOracles;
using DotnetInspector.Sections;
using ILInspector.Analysis;
using ILInspector.Metadata;
using ILInspector.Research;
using ILInspector.SourceLink;
using NLinq;
using QuerySpace.Composition;
using QuerySpace.Rows;

if (args.Length < 3
    || args[0] is not ("check" or "time")
    || !int.TryParse(args[1], out int population)
    || population <= 0)
{
    Console.Error.WriteLine(
        "Usage: address-terminal-scorecard <check|time> "
            + "<population> <assembly>...");
    return 2;
}

var assets = new List<ScorecardAsset<AddressAsset>>();
try
{
    foreach (string path in args[2..])
    {
        AddressAsset asset = AddressAsset.Open(path, population);
        assets.Add(
            new(
                $"{Path.GetFileNameWithoutExtension(path)}-{population}",
                asset));
    }

    var shape = new ScorecardShape(
        N: Math.Min(6, population),
        WindowFirst: 1,
        WindowLast: Math.Min(6, population));
    ScorecardColumn<AddressAsset, AddressRow>[] composed =
    [
        new("LINQ", (closing, asset) =>
            AddressColumns.Linq(closing, asset, shape)),
        new("NLinq query", (closing, asset) =>
            AddressColumns.NLinq(closing, asset, shape)),
        new("NLinq source", (closing, asset) =>
            AddressColumns.NLinqSource(closing, asset, shape)),
        new("Planner/QuerySpace", (closing, asset) =>
            AddressColumns.Planner(closing, asset, shape)),
    ];
    ScorecardColumn<AddressAsset, AddressRow>[] prepared =
    [
        new("LINQ", (closing, asset) =>
            AddressColumns.LinqPrepared(closing, asset, shape)),
        new("NLinq query", (closing, asset) =>
            AddressColumns.NLinqPrepared(closing, asset, shape)),
        new("NLinq source", (closing, asset) =>
            AddressColumns.NLinqSourcePrepared(
                closing,
                asset,
                shape)),
        new("Planner/QuerySpace", (closing, asset) =>
            AddressColumns.PlannerPrepared(
                closing,
                asset,
                shape)),
    ];

    ScorecardCheck composedCheck = Scorecard.Check(
        assets,
        composed[1],
        composed,
        static row => row.ToString());
    ScorecardCheck preparedCheck = Scorecard.Check(
        assets,
        prepared[1],
        prepared,
        static row => row.ToString());
    Console.WriteLine(
        $"# composed answers: {composedCheck.Compared} compared, "
            + $"{composedCheck.Mismatches.Count} mismatches, "
            + $"{composedCheck.WindowFailures.Count} window failures");
    Console.WriteLine(
        $"# prepared answers: {preparedCheck.Compared} compared, "
            + $"{preparedCheck.Mismatches.Count} mismatches, "
            + $"{preparedCheck.WindowFailures.Count} window failures");
    foreach (ScorecardMismatch mismatch in composedCheck.Mismatches
        .Concat(preparedCheck.Mismatches))
    {
        Console.WriteLine(
            $"mismatch\t{mismatch.Asset}\t{mismatch.Closing}\t"
                + $"{mismatch.Column}\t{mismatch.Answer}\t"
                + $"oracle={mismatch.OracleAnswer}");
    }
    if (!composedCheck.Agrees || !preparedCheck.Agrees)
        return 1;
    if (args[0] == "check")
        return 0;

    var timing = new ScorecardTiming(
        Rounds: 6,
        Warmup: 5,
        BudgetMilliseconds: 100,
        MinSamples: 10,
        MaxSamples: 2_000);
    IReadOnlyList<ScorecardCell> preparedCells =
        Scorecard.Measure(
            assets,
            prepared,
            timing,
            progress => Console.Error.WriteLine(
                $"prepared {progress}"));
    IReadOnlyList<ScorecardCell> composedCells =
        Scorecard.Measure(
            assets,
            composed,
            timing,
            progress => Console.Error.WriteLine(
                $"composed {progress}"));
    WriteReport("Prepared terminal kernel", preparedCells);
    WriteReport("Composed Address projection", composedCells);
    return 0;
}
finally
{
    foreach (ScorecardAsset<AddressAsset> asset in assets)
        asset.Asset.Dispose();
}

static void WriteReport(
    string phase,
    IReadOnlyList<ScorecardCell> cells)
{
    ScorecardClosing[] closings =
    [
        ScorecardClosing.Exists,
        ScorecardClosing.Count,
        ScorecardClosing.Rows,
    ];
    string[] columns =
        ["LINQ", "NLinq query", "NLinq source", "Planner/QuerySpace"];
    var byKey = cells.ToDictionary(
        cell => (cell.AssetIndex, cell.Closing, cell.Column));
    Console.WriteLine();
    Console.WriteLine($"## {phase}");
    Console.WriteLine();
    Console.WriteLine(
        "| Asset | Terminal | LINQ | NLinq query | NLinq source | "
            + "Planner/QuerySpace |");
    Console.WriteLine(
        "| --- | --- | ---: | ---: | ---: | ---: |");
    foreach ((int index, string asset) in cells
        .Select(cell => (cell.AssetIndex, cell.Asset))
        .Distinct())
    {
        foreach (ScorecardClosing closing in closings)
        {
            Console.Write($"| {asset} | {closing} |");
            foreach (string column in columns)
            {
                ScorecardCell cell = byKey[
                    (index, closing, column)];
                Console.Write(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $" {cell.Median:0.###} us |"));
            }
            Console.WriteLine();
        }
    }
}

readonly record struct AddressRow(
    int LineNumber,
    int MethodToken,
    string Kind,
    string? Method,
    string? Opcode,
    int ExceptionContexts,
    int AllocationContexts,
    int SafetyContexts,
    int CostContexts)
{
    public static AddressRow From(
        LibraryAddressPopulationRow row) =>
        row switch
        {
            LibraryAddressPopulationRow.Resolved resolved =>
                new(
                    resolved.LineNumber,
                    resolved.MethodToken,
                    "resolved",
                    resolved.Projection.Method,
                    resolved.Projection
                        .InstructionContext?.Opcode,
                    resolved.Projection
                        .ExceptionContext?.Count ?? 0,
                    resolved.Projection
                        .AllocationContext?.Count ?? 0,
                    resolved.Projection
                        .SafetyContext?.Count ?? 0,
                    resolved.Projection
                        .CostContext?.Count ?? 0),
            LibraryAddressPopulationRow.Unresolved unresolved =>
                new(
                    unresolved.LineNumber,
                    unresolved.MethodToken,
                    unresolved.Failure.Kind.ToString(),
                    null,
                    null,
                    0,
                    0,
                    0,
                    0),
            LibraryAddressPopulationRow.Malformed malformed =>
                new(
                    malformed.LineNumber,
                    0,
                    "malformed",
                    null,
                    null,
                    0,
                    0,
                    0,
                    0),
            _ => throw new InvalidOperationException(
                "Unknown Library Address population row."),
        };

    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{LineNumber}|{MethodToken:X8}|{Kind}|{Method}|{Opcode}|"
                + $"{ExceptionContexts}|{AllocationContexts}|"
                + $"{SafetyContexts}|{CostContexts}");
}

sealed class AddressAsset : IDisposable
{
    private const ILOffsetProjectionCapabilities Capabilities =
        ILOffsetProjectionCapabilities.InstructionContext
        | ILOffsetProjectionCapabilities.ExceptionContext
        | ILOffsetProjectionCapabilities.CallsiteContext
        | ILOffsetProjectionCapabilities.ReturnAddressContext
        | ILOffsetProjectionCapabilities.AllocationContext
        | ILOffsetProjectionCapabilities.SafetyContext
        | ILOffsetProjectionCapabilities.CostContext;

    private readonly SourceLinkService _source;
    private readonly ILOffsetAnalysisInput _analysis;

    private AddressAsset(
        SourceLinkService source,
        ILOffsetAnalysisInput analysis,
        int[] tokens)
    {
        _source = source;
        _analysis = analysis;
        Tokens = tokens;
        Prepared =
        [
            .. Enumerable.Range(0, tokens.Length)
                .Select(Project),
        ];
    }

    public int[] Tokens { get; }

    public LibraryAddressPopulationRow[] Prepared { get; }

    public static AddressAsset Open(
        string path,
        int population)
    {
        path = Path.GetFullPath(path);
        byte[] image = File.ReadAllBytes(path);
        int[] available;
        AssemblyReferenceIdentity identity;
        using (var pe = new PEReader(
            new MemoryStream(image, writable: false)))
        {
            MetadataReader reader = pe.GetMetadataReader();
            identity =
                AssemblyReferenceIdentity.FromAssemblyDefinition(
                    reader);
            available =
            [
                .. reader.MethodDefinitions
                    .Where(handle =>
                    {
                        MethodDefinition method =
                            reader.GetMethodDefinition(handle);
                        return method.RelativeVirtualAddress != 0
                            && pe.GetMethodBody(
                                    method.RelativeVirtualAddress)
                                .GetILBytes()
                                is { Length: > 0 };
                    })
                    .Select(static handle =>
                        MetadataTokens.GetToken(handle)),
            ];
        }
        if (available.Length == 0)
        {
            throw new InvalidOperationException(
                $"{path} has no managed method bodies.");
        }

        int[] tokens = new int[population];
        for (int index = 0; index < tokens.Length; index++)
            tokens[index] = available[index % available.Length];

        ResolvedAssemblyReference descriptor =
            ResolvedAssemblyReference.Create(
                identity,
                path: null,
                () => new MemoryStream(image, writable: false),
                AssemblyResolutionProvenance.Local(
                    "Address terminal scorecard"));
        SourceLinkService source =
            SourceLinkService.OpenMetadataOnly(
                descriptor,
                log: null,
                cache: null,
                SourceLinkReadLimits.Unlimited);
        try
        {
            LibraryBodyAnalysisExecution execution =
                LibraryBodyAnalysisService.ExecuteImage(
                    Path.GetFileName(path),
                    ImmutableCollectionsMarshal
                        .AsImmutableArray(image),
                    LibraryBodyAnalysisRequest.Create(
                        LibraryBodyAnalysisFeatures.Allocations
                        | LibraryBodyAnalysisFeatures.MethodEvidence,
                        tokens.ToHashSet()));
            return new(
                source,
                new(
                    execution.Allocations,
                    execution.Safety,
                    execution.CallGraph),
                tokens);
        }
        catch
        {
            source.Dispose();
            throw;
        }
    }

    public LibraryAddressPopulationRow Project(int index)
    {
        int methodToken = Tokens[index];
        ILOffsetProjectionOutcome outcome =
            ResearchViews.ProjectILOffset(
                new(
                    _source,
                    methodToken,
                    ILOffset: 0,
                    Capabilities,
                    Analysis: _analysis));
        return outcome.Succeeded
            ? new LibraryAddressPopulationRow.Resolved(
                index + 1,
                $"0x{methodToken:X8}+0x0",
                label: null,
                methodToken,
                ilOffset: 0,
                outcome.Projection!)
            : new LibraryAddressPopulationRow.Unresolved(
                index + 1,
                $"0x{methodToken:X8}+0x0",
                label: null,
                methodToken,
                ilOffset: 0,
                outcome.Failure!);
    }

    public void Dispose() => _source.Dispose();
}

static class AddressColumns
{
    public static ScorecardAnswer<AddressRow> Linq(
        ScorecardClosing closing,
        AddressAsset asset,
        ScorecardShape shape) =>
        LinqCore(
            closing,
            Enumerable.Range(0, asset.Tokens.Length)
                .Select(asset.Project),
            shape);

    public static ScorecardAnswer<AddressRow> LinqPrepared(
        ScorecardClosing closing,
        AddressAsset asset,
        ScorecardShape shape) =>
        LinqCore(closing, asset.Prepared, shape);

    private static ScorecardAnswer<AddressRow> LinqCore(
        ScorecardClosing closing,
        IEnumerable<LibraryAddressPopulationRow> source,
        ScorecardShape shape)
    {
        IEnumerable<AddressRow> rows =
            source.Select(AddressRow.From);
        return closing switch
        {
            ScorecardClosing.Exists =>
                ScorecardAnswer<AddressRow>.OfExists(
                    source.Any()),
            ScorecardClosing.Count =>
                ScorecardAnswer<AddressRow>.OfCount(
                    source.Count()),
            ScorecardClosing.Head =>
                ScorecardAnswer<AddressRow>.OfRows(
                    rows.Take(shape.N).ToList()),
            ScorecardClosing.Tail =>
                ScorecardAnswer<AddressRow>.OfRows(
                    rows.TakeLast(shape.N).ToList()),
            ScorecardClosing.Rows =>
                ScorecardAnswer<AddressRow>.OfRows(
                    rows.ToList()),
            ScorecardClosing.Window =>
                Window(
                    rows.Skip(shape.WindowSkip)
                        .Take(shape.WindowTake)
                        .ToList(),
                    shape),
            _ => throw new ArgumentOutOfRangeException(
                nameof(closing)),
        };
    }

    public static ScorecardAnswer<AddressRow> NLinq(
        ScorecardClosing closing,
        AddressAsset asset,
        ScorecardShape shape) =>
        NLinqCore(
            closing,
            new AddressPopulationRows(asset),
            shape);

    public static ScorecardAnswer<AddressRow> NLinqPrepared(
        ScorecardClosing closing,
        AddressAsset asset,
        ScorecardShape shape) =>
        NLinqCore(
            closing,
            new PreparedAddressPopulationRows(asset),
            shape);

    public static ScorecardAnswer<AddressRow> NLinqSource(
        ScorecardClosing closing,
        AddressAsset asset,
        ScorecardShape shape) =>
        NLinqSourceCore(
            closing,
            new AddressPopulationRows(asset),
            shape);

    public static ScorecardAnswer<AddressRow> NLinqSourcePrepared(
        ScorecardClosing closing,
        AddressAsset asset,
        ScorecardShape shape) =>
        NLinqSourceCore(
            closing,
            new PreparedAddressPopulationRows(asset),
            shape);

    private static ScorecardAnswer<AddressRow> NLinqSourceCore<TSource>(
        ScorecardClosing closing,
        TSource source,
        ScorecardShape shape)
        where TSource :
            NLinq.IEnumerator<
                TSource,
                LibraryAddressPopulationRow>,
            allows ref struct
    {
        if (closing is ScorecardClosing.Count
            or ScorecardClosing.Exists)
        {
            int count =
                source.Count<
                    TSource,
                    LibraryAddressPopulationRow>();
            return closing == ScorecardClosing.Count
                ? ScorecardAnswer<AddressRow>.OfCount(count)
                : ScorecardAnswer<AddressRow>.OfExists(
                    count != 0);
        }
        return NLinqCore(closing, source, shape);
    }

    private static ScorecardAnswer<AddressRow> NLinqCore<TSource>(
        ScorecardClosing closing,
        TSource source,
        ScorecardShape shape)
        where TSource :
            NLinq.IEnumerator<
                TSource,
                LibraryAddressPopulationRow>,
            allows ref struct
    {
        switch (closing)
        {
            case ScorecardClosing.Exists:
                return ScorecardAnswer<AddressRow>.OfExists(
                    source.Any<
                        TSource,
                        LibraryAddressPopulationRow>());
            case ScorecardClosing.Count:
                return ScorecardAnswer<AddressRow>.OfCount(
                    source.CountFold<
                        TSource,
                        LibraryAddressPopulationRow>());
            case ScorecardClosing.Head:
            {
                var rows = source
                    .Select<
                        TSource,
                        LibraryAddressPopulationRow,
                        AddressRow,
                        ToRow>(default)
                    .Take<
                        Map<
                            LibraryAddressPopulationRow,
                            AddressRow,
                            TSource,
                            ToRow>,
                        AddressRow>(shape.N);
                var answer = new List<AddressRow>(shape.N);
                while (true)
                {
                    AddressRow row =
                        rows.TryGetNext(out bool hasMore);
                    if (!hasMore)
                    {
                        return ScorecardAnswer<AddressRow>
                            .OfRows(answer);
                    }
                    answer.Add(row);
                }
            }
            case ScorecardClosing.Tail:
            {
                List<LibraryAddressPopulationRow> tail =
                    source.TakeLast<
                        TSource,
                        LibraryAddressPopulationRow>(
                            shape.N);
                return ScorecardAnswer<AddressRow>.OfRows(
                    [.. tail.Select(AddressRow.From)]);
            }
            case ScorecardClosing.Rows:
                return ScorecardAnswer<AddressRow>.OfRows(
                    source
                        .Select<
                            TSource,
                            LibraryAddressPopulationRow,
                            AddressRow,
                            ToRow>(default)
                        .ToList<
                            Map<
                                LibraryAddressPopulationRow,
                                AddressRow,
                                TSource,
                                ToRow>,
                            AddressRow>());
            case ScorecardClosing.Window:
                return source
                        .Skip<
                            TSource,
                            LibraryAddressPopulationRow>(
                                shape.WindowSkip)
                        .Select<
                            SkipEnumerator<
                                TSource,
                                LibraryAddressPopulationRow>,
                            LibraryAddressPopulationRow,
                            AddressRow,
                            ToRow>(default)
                        .TryTakeExactly<
                            Map<
                                LibraryAddressPopulationRow,
                                AddressRow,
                                SkipEnumerator<
                                    TSource,
                                    LibraryAddressPopulationRow>,
                                ToRow>,
                            AddressRow>(
                                shape.WindowTake,
                                out List<AddressRow> window)
                    ? ScorecardAnswer<AddressRow>.OfRows(window)
                    : ScorecardAnswer<AddressRow>
                        .OfWindowFailure();
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(closing));
        }
    }

    public static ScorecardAnswer<AddressRow> Planner(
        ScorecardClosing closing,
        AddressAsset asset,
        ScorecardShape shape) =>
        PlannerCore(
            closing,
            asset.Tokens.Length,
            () =>
            [
                .. Enumerable.Range(0, asset.Tokens.Length)
                    .Select(asset.Project),
            ],
            shape);

    public static ScorecardAnswer<AddressRow> PlannerPrepared(
        ScorecardClosing closing,
        AddressAsset asset,
        ScorecardShape shape) =>
        PlannerCore(
            closing,
            asset.Prepared.Length,
            () => asset.Prepared,
            shape);

    private static ScorecardAnswer<AddressRow> PlannerCore(
        ScorecardClosing closing,
        int sourceCount,
        Func<IReadOnlyList<LibraryAddressPopulationRow>> createRows,
        ScorecardShape shape)
    {
        QuerySpaceTerminalRequirement terminal =
            closing switch
            {
                ScorecardClosing.Exists =>
                    QuerySpaceTerminalRequirement.Exists,
                ScorecardClosing.Count =>
                    QuerySpaceTerminalRequirement.Count,
                _ => QuerySpaceTerminalRequirement.Rows,
            };
        QuerySpaceRequest request =
            LibraryAddressPopulationQuery.CreateRequest(
                Selection(closing, shape),
                terminal);
        var resolved =
            LibraryAddressPopulationQuery.ResolveRequest(request)
                as LibraryAddressPopulationQueryResolution.Accepted
            ?? throw new InvalidOperationException(
                "The owner-issued Address query did not resolve.");

        if (terminal is QuerySpaceTerminalRequirement.Count
            or QuerySpaceTerminalRequirement.Exists)
        {
            if (!RowQueryExecutor.TryApplyCount(
                    sourceCount,
                    resolved.Plan.Rows,
                    out RowSelectionCountResult count)
                || !count.IsSuccess)
            {
                return ScorecardAnswer<AddressRow>
                    .OfWindowFailure();
            }
            return terminal is QuerySpaceTerminalRequirement.Count
                ? ScorecardAnswer<AddressRow>.OfCount(count.Count)
                : ScorecardAnswer<AddressRow>.OfExists(
                    count.Count != 0);
        }

        RowSelectionResult<LibraryAddressPopulationRow> selection =
            RowQueryExecutor.Apply(
                createRows(),
                resolved.Plan.Rows);
        return selection.IsSuccess
            ? ScorecardAnswer<AddressRow>.OfRows(
                [.. selection.Values.Select(AddressRow.From)])
            : ScorecardAnswer<AddressRow>.OfWindowFailure();
    }

    private static RowSelectionIntent<string> Selection(
        ScorecardClosing closing,
        ScorecardShape shape) =>
        closing switch
        {
            ScorecardClosing.Head =>
                RowSelectionIntent<string>.Create(
                    [
                        RowSelectionIntentOperation<string>.Head(
                            shape.N),
                    ]),
            ScorecardClosing.Tail =>
                RowSelectionIntent<string>.Create(
                    [
                        RowSelectionIntentOperation<string>.Tail(
                            shape.N),
                    ]),
            ScorecardClosing.Window =>
                RowSelectionIntent<string>.Create(
                    [
                        RowSelectionIntentOperation<string>.Window(
                            shape.WindowFirst,
                            shape.WindowLast),
                    ]),
            _ => RowSelectionIntent<string>.Empty,
        };

    private static ScorecardAnswer<AddressRow> Window(
        List<AddressRow> rows,
        ScorecardShape shape) =>
        rows.Count == shape.WindowTake
            ? ScorecardAnswer<AddressRow>.OfRows(rows)
            : ScorecardAnswer<AddressRow>.OfWindowFailure();

    private readonly struct ToRow :
        IFunc<LibraryAddressPopulationRow, AddressRow>
    {
        public AddressRow Invoke(
            LibraryAddressPopulationRow value) =>
            AddressRow.From(value);
    }
}

struct AddressPopulationRows :
    NLinq.IEnumerator<
        AddressPopulationRows,
        LibraryAddressPopulationRow>
{
    private readonly AddressAsset _asset;
    private int _index;

    public AddressPopulationRows(AddressAsset asset)
    {
        _asset = asset;
    }

    public LibraryAddressPopulationRow TryGetNext(
        out bool hasMore)
    {
        if (_index == _asset.Tokens.Length)
        {
            hasMore = false;
            return null!;
        }
        hasMore = true;
        return _asset.Project(_index++);
    }

    public static int Count(
        scoped ref AddressPopulationRows source) =>
        source._asset.Tokens.Length - source._index;

    static TAccumulator
        NLinq.IEnumerator<
            AddressPopulationRows,
            LibraryAddressPopulationRow>
        .Fold<TAccumulator, TFunc>(
            scoped ref AddressPopulationRows source,
            TAccumulator accumulator,
            TFunc function)
    {
        while (source._index < source._asset.Tokens.Length)
        {
            accumulator = function.Invoke(
                accumulator,
                source._asset.Project(source._index++));
        }
        return accumulator;
    }
}

struct PreparedAddressPopulationRows :
    NLinq.IEnumerator<
        PreparedAddressPopulationRows,
        LibraryAddressPopulationRow>
{
    private readonly AddressAsset _asset;
    private int _index;

    public PreparedAddressPopulationRows(AddressAsset asset)
    {
        _asset = asset;
    }

    public LibraryAddressPopulationRow TryGetNext(
        out bool hasMore)
    {
        if (_index == _asset.Prepared.Length)
        {
            hasMore = false;
            return null!;
        }
        hasMore = true;
        return _asset.Prepared[_index++];
    }

    public static int Count(
        scoped ref PreparedAddressPopulationRows source) =>
        source._asset.Prepared.Length - source._index;

    static TAccumulator
        NLinq.IEnumerator<
            PreparedAddressPopulationRows,
            LibraryAddressPopulationRow>
        .Fold<TAccumulator, TFunc>(
            scoped ref PreparedAddressPopulationRows source,
            TAccumulator accumulator,
            TFunc function)
    {
        while (source._index < source._asset.Prepared.Length)
        {
            accumulator = function.Invoke(
                accumulator,
                source._asset.Prepared[source._index++]);
        }
        return accumulator;
    }
}
