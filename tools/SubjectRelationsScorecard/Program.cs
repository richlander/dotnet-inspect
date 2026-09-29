using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using DotnetInspector.Fixtures;
using DotnetInspector.PerformanceOracles;
using ILInspector.Metadata;
using NLinq;

const string usage =
    "usage: check|time [--rounds N] [--budget-ms N] [--tsv <path>] "
    + "'<assembly>|<base|interface>|<target>'...";

if (!ScorecardCommandLine.TryParse(
        args,
        out ScorecardOptions? options,
        out string? error))
{
    Console.Error.WriteLine(error);
    return 2;
}

var shape = new ScorecardShape();
var assets = new List<ScorecardAsset<HierarchyAsset>>();
try
{
    foreach (string specification in options!.Assets)
    {
        if (!HierarchyAsset.TryLoad(
                specification,
                out HierarchyAsset? asset,
                out error))
        {
            Console.Error.WriteLine($"{error} {usage}");
            return 2;
        }
        assets.Add(new(asset!.Name, asset));
    }

    ScorecardColumn<HierarchyAsset, HierarchyAnswerRow> oracle =
        HierarchyPopulation.NLinqColumn(shape);
    ScorecardColumn<HierarchyAsset, HierarchyAnswerRow>[] columns =
    [
        HierarchyPopulation.OldColumn(shape),
        HierarchyPopulation.LinqColumn(shape),
        oracle,
        HierarchyPopulation.PlannerColumn(shape),
    ];
    ScorecardCheck check =
        Scorecard.Check(
            assets,
            oracle,
            columns,
            static row =>
                $"0x{row.SourceToken:X8}|{row.SourceType}|"
                + string.Join(
                    ',',
                    row.OccurrenceTokens.Select(
                        static token => $"0x{token:X8}")),
            HierarchyAnswerRowComparer.Instance);
    foreach (ScorecardMismatch mismatch in check.Mismatches)
    {
        Console.WriteLine(
            $"mismatch\t{mismatch.Asset}\t"
            + $"{shape.Label(mismatch.Closing)}\t{mismatch.Column}\t"
            + $"{mismatch.Answer}\toracle={mismatch.OracleAnswer}");
    }
    Console.WriteLine(
        $"# answers: {check.Compared} compared, "
        + $"{check.Mismatches.Count} mismatches, "
        + $"{check.WindowFailures.Count} strict-window failures");
    if (!check.Agrees)
        return 1;
    if (!HierarchySafetyCheck.Agrees())
        return 1;

    foreach (string asset in check.WindowFailures)
    {
        Console.WriteLine(
            $"window-failed\t{asset}\t"
            + $"{shape.Label(ScorecardClosing.Window)}\t"
            + "every column fails: the window does not exist");
    }
    if (options.Command == ScorecardCommand.Check)
        return 0;

    IReadOnlyList<ScorecardCell> cells =
        Scorecard.Measure(
            assets,
            columns,
            options.Timing,
            progress => Console.Error.WriteLine(progress));
    if (options.TsvPath is { } tsvPath)
    {
        using StreamWriter tsv = File.CreateText(tsvPath);
        Scorecard.WriteTsv(cells, tsv);
    }

    Console.Write(Scorecard.Report(cells, oracle.Name, shape));
    return 0;
}
finally
{
    foreach (ScorecardAsset<HierarchyAsset> asset in assets)
        asset.Asset.Dispose();
}

sealed class HierarchyAsset : IDisposable
{
    HierarchyAsset(
        string path,
        MetadataHierarchyRelationKind kind,
        MetadataTypeDefinitionName target,
        byte[] content)
    {
        Path = path;
        Kind = kind;
        Target = target;
        Reader = new PEReader(ImmutableArray.Create(content));
        Session = AssemblyInspectionSession.OpenPrefetched(
            new MemoryStream(content, writable: false));
        Name =
            $"{System.IO.Path.GetFileNameWithoutExtension(path)}:"
            + $"{kind}:{target.ToEscapedFullName()}";
        OldRequest = new(
            [MetadataRelationFamily.Hierarchy],
            MetadataOperationPolicy.Unbounded)
        {
            IncludeHidden = false,
        };
        PlannerRequest = new(
            [MetadataRelationFamily.Hierarchy],
            MetadataOperationPolicy.Unbounded,
            hierarchyTarget: new(target, kind))
        {
            IncludeHidden = false,
        };
    }

    public string Name { get; }

    public string Path { get; }

    public MetadataHierarchyRelationKind Kind { get; }

    public MetadataTypeDefinitionName Target { get; }

    public PEReader Reader { get; }

    public AssemblyInspectionSession Session { get; }

    public MetadataRelationInspectionRequest OldRequest { get; }

    public MetadataRelationInspectionRequest PlannerRequest { get; }

    public MetadataReader Metadata => Reader.GetMetadataReader();

    public void Dispose()
    {
        Session.Dispose();
        Reader.Dispose();
    }

    public static bool TryLoad(
        string specification,
        out HierarchyAsset? asset,
        out string? error)
    {
        asset = null;
        string[] parts = specification.Split('|');
        if (parts is not [string path, string form, string targetText]
            || !File.Exists(path))
        {
            error = $"Invalid asset '{specification}'.";
            return false;
        }
        MetadataHierarchyRelationKind? kind =
            form.ToLowerInvariant() switch
            {
                "base" => MetadataHierarchyRelationKind.BaseType,
                "interface" => MetadataHierarchyRelationKind.Interface,
                _ => null,
            };
        int separator = targetText.LastIndexOf('.');
        string targetNamespace =
            separator < 0 ? string.Empty : targetText[..separator];
        string targetName =
            separator < 0 ? targetText : targetText[(separator + 1)..];
        if (kind is null
            || MetadataTypeDefinitionName.Create(
                    targetNamespace,
                    [targetName])
                is not MetadataTypeDefinitionNameResult.Valid valid)
        {
            error = $"Invalid asset '{specification}'.";
            return false;
        }

        asset = new(
            System.IO.Path.GetFullPath(path),
            kind.Value,
            valid.Name,
            File.ReadAllBytes(path));
        error = null;
        return true;
    }
}

readonly record struct HierarchyAnswerRow(
    int SourceToken,
    MetadataTypeDefinitionName SourceType,
    ImmutableArray<int> OccurrenceTokens);

sealed class HierarchyAnswerRowComparer :
    IEqualityComparer<HierarchyAnswerRow>
{
    public static HierarchyAnswerRowComparer Instance { get; } = new();

    public bool Equals(
        HierarchyAnswerRow x,
        HierarchyAnswerRow y) =>
        x.SourceToken == y.SourceToken
        && x.SourceType == y.SourceType
        && x.OccurrenceTokens.AsSpan()
            .SequenceEqual(y.OccurrenceTokens.AsSpan());

    public int GetHashCode(HierarchyAnswerRow row)
    {
        var hash = new HashCode();
        hash.Add(row.SourceToken);
        hash.Add(row.SourceType);
        foreach (int token in row.OccurrenceTokens)
            hash.Add(token);
        return hash.ToHashCode();
    }
}

readonly record struct HierarchySafetyObservation(
    int CandidateCount,
    string Diagnostics,
    MetadataOperationCounters Counters);

struct HierarchyAnalysisUnits :
    NLinq.IEnumerator<
        HierarchyAnalysisUnits,
        MetadataHierarchyRelationAnalysisUnit>
{
    readonly MetadataReader _reader;
    TypeDefinitionHandleCollection.Enumerator _types;
    readonly MetadataHierarchyRelationAnalysisPass _pass;

    public HierarchyAnalysisUnits(
        MetadataReader reader,
        MetadataHierarchyRelationAnalysisPass pass)
    {
        _reader = reader;
        _types = reader.TypeDefinitions.GetEnumerator();
        _pass = pass;
    }

    public MetadataHierarchyRelationAnalysisUnit TryGetNext(
        out bool hasMore)
    {
        if (!_types.MoveNext())
        {
            hasMore = false;
            return default;
        }

        hasMore = true;
        return _pass.Analyze(_reader, _types.Current);
    }

    static TAccumulator
        NLinq.IEnumerator<
            HierarchyAnalysisUnits,
            MetadataHierarchyRelationAnalysisUnit>
            .Fold<TAccumulator, TFunction>(
                scoped ref HierarchyAnalysisUnits source,
                TAccumulator accumulator,
                TFunction function)
    {
        while (true)
        {
            MetadataHierarchyRelationAnalysisUnit unit =
                source.TryGetNext(out bool hasMore);
            if (!hasMore)
                return accumulator;
            accumulator = function.Invoke(accumulator, unit);
        }
    }
}

static class HierarchySafetyCheck
{
    public static bool Agrees()
    {
        byte[] content =
            HierarchyRelationSafetyFixtures
                .BuildMalformedGenericTypeSpecification();
        MetadataTypeDefinitionName target = Target();
        HierarchySafetyObservation oracle =
            ObserveNLinq(content, target);
        (string Name, HierarchySafetyObservation Observation)[] columns =
        [
            ("LINQ", ObserveLinq(content, target)),
            ("Planner analysis (shipping)",
                ObservePlanner(content, target)),
        ];
        int mismatches = 0;
        foreach ((string name, HierarchySafetyObservation observation)
            in columns)
        {
            if (observation == oracle)
                continue;
            mismatches++;
            Console.WriteLine(
                "hostile-mismatch\tmalformed-generic-typespec\t"
                + $"{name}\t{observation}\toracle={oracle}");
        }

        Console.WriteLine(
            $"# hostile answers: {columns.Length} compared, "
            + $"{mismatches} mismatches");
        return mismatches == 0;
    }

    static HierarchySafetyObservation ObserveLinq(
        byte[] content,
        MetadataTypeDefinitionName target)
    {
        using var image =
            new PEReader(ImmutableArray.Create(content));
        MetadataReader reader = image.GetMetadataReader();
        using var pass =
            new MetadataHierarchyRelationAnalysisPass(
                reader,
                Request(target));
        MetadataHierarchyRelationAnalysisUnit[] units =
            reader.TypeDefinitions
                .Select(handle => pass.Analyze(reader, handle))
                .ToArray();
        return Observe(units, pass.Counters);
    }

    static HierarchySafetyObservation ObserveNLinq(
        byte[] content,
        MetadataTypeDefinitionName target)
    {
        using var image =
            new PEReader(ImmutableArray.Create(content));
        MetadataReader reader = image.GetMetadataReader();
        using var pass =
            new MetadataHierarchyRelationAnalysisPass(
                reader,
                Request(target));
        var source = new HierarchyAnalysisUnits(reader, pass);
        List<MetadataHierarchyRelationAnalysisUnit> units =
            source.ToList<
                HierarchyAnalysisUnits,
                MetadataHierarchyRelationAnalysisUnit>();
        return Observe(units, pass.Counters);
    }

    static HierarchySafetyObservation ObservePlanner(
        byte[] content,
        MetadataTypeDefinitionName target)
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.OpenPrefetched(
                new MemoryStream(content, writable: false));
        var available =
            session.AnalyzeHierarchyRelations(Request(target))
                as MetadataHierarchyRelationAnalysisOutcome.Available
            ?? throw new InvalidOperationException(
                "The malformed hierarchy safety fixture must remain "
                    + "an admitted ECMA-335 image.");
        return new(
            available.Result.CandidateCount,
            Diagnostics(available.Result.Relations.Diagnostics),
            available.Result.Receipt.Counters);
    }

    static HierarchySafetyObservation Observe(
        IEnumerable<MetadataHierarchyRelationAnalysisUnit> units,
        MetadataOperationCounters counters)
    {
        int candidates = 0;
        var diagnostics =
            ImmutableArray.CreateBuilder<MetadataRelationDiagnostic>();
        foreach (MetadataHierarchyRelationAnalysisUnit unit in units)
        {
            candidates = checked(candidates + unit.CandidateCount);
            if (unit.Diagnostic is { } diagnostic)
                diagnostics.Add(diagnostic);
        }
        return new(candidates, Diagnostics(diagnostics), counters);
    }

    static string Diagnostics(
        IEnumerable<MetadataRelationDiagnostic> diagnostics) =>
        string.Join(
            ',',
            diagnostics.Select(static diagnostic =>
                $"{diagnostic.Kind}:{diagnostic.BudgetDimension}"));

    static MetadataHierarchyRelationAnalysisRequest Request(
        MetadataTypeDefinitionName target) =>
        new(
            new(
                target,
                MetadataHierarchyRelationKind.Interface),
            MetadataOperationPolicy.Unbounded);

    static MetadataTypeDefinitionName Target() =>
        MetadataTypeDefinitionName.Create(
            "Sample",
            ["ITarget`1"])
        is MetadataTypeDefinitionNameResult.Valid valid
            ? valid.Name
            : throw new InvalidOperationException(
                "The hierarchy safety target is invalid.");
}

struct HierarchyCandidateRows :
    NLinq.IEnumerator<HierarchyCandidateRows, HierarchyAnswerRow>
{
    readonly MetadataReader _reader;
    TypeDefinitionHandleCollection.Enumerator _types;
    readonly MetadataHierarchyRelationKind _kind;
    readonly bool _materializeRows;
    readonly MetadataHierarchyRelationAnalysisPass _pass;

    public HierarchyCandidateRows(
        MetadataReader reader,
        MetadataHierarchyRelationKind kind,
        MetadataTypeDefinitionName target,
        bool materializeRows = true)
    {
        _reader = reader;
        _types = reader.TypeDefinitions.GetEnumerator();
        _kind = kind;
        _materializeRows = materializeRows;
        _pass =
            new(
                reader,
                new(
                    new(target, kind),
                    MetadataOperationPolicy.Unbounded,
                    materializeRows: materializeRows));
    }

    public HierarchyAnswerRow TryGetNext(out bool hasMore)
    {
        while (_types.MoveNext())
        {
            TypeDefinitionHandle source = _types.Current;
            MetadataHierarchyRelationAnalysisUnit unit =
                _pass.Analyze(_reader, source);
            bool matched =
                _kind == MetadataHierarchyRelationKind.BaseType
                    ? unit.BaseMatched
                    : unit.InterfaceMatched;
            if (!matched || unit.IsUnavailable)
                continue;

            hasMore = true;
            if (!_materializeRows)
                return default;
            MetadataHierarchyRelationAnalysisRow relation =
                (_kind == MetadataHierarchyRelationKind.BaseType
                    ? unit.BaseRelation
                    : unit.InterfaceRelation)
                ?? throw new InvalidOperationException(
                    "Materialized hierarchy analysis requires a row.");
            return new(
                relation.Source.Definition.Value,
                relation.SourceType,
                relation.MetadataTokens);
        }

        hasMore = false;
        return default;
    }

    static TAccumulator
        NLinq.IEnumerator<HierarchyCandidateRows, HierarchyAnswerRow>
            .Fold<TAccumulator, TFunction>(
                scoped ref HierarchyCandidateRows source,
                TAccumulator accumulator,
                TFunction function)
    {
        while (true)
        {
            HierarchyAnswerRow row =
                source.TryGetNext(out bool hasMore);
            if (!hasMore)
                return accumulator;
            accumulator = function.Invoke(accumulator, row);
        }
    }
}

static class HierarchyPopulation
{
    public static ScorecardColumn<HierarchyAsset, HierarchyAnswerRow> OldColumn(
        ScorecardShape shape) =>
        new("Old", (closing, asset) => OldAnswer(closing, asset, shape));

    public static ScorecardColumn<HierarchyAsset, HierarchyAnswerRow> LinqColumn(
        ScorecardShape shape) =>
        new("LINQ", (closing, asset) => LinqAnswer(closing, asset, shape));

    public static ScorecardColumn<HierarchyAsset, HierarchyAnswerRow> NLinqColumn(
        ScorecardShape shape) =>
        new("NLinq", (closing, asset) => NLinqAnswer(closing, asset, shape));

    public static ScorecardColumn<HierarchyAsset, HierarchyAnswerRow>
        PlannerColumn(
        ScorecardShape shape) =>
        new(
            "Planner analysis (shipping)",
            (closing, asset) => PlannerAnswer(closing, asset, shape));

    static ScorecardAnswer<HierarchyAnswerRow> NLinqAnswer(
        ScorecardClosing closing,
        HierarchyAsset asset,
        ScorecardShape shape)
    {
        var selected =
            new HierarchyCandidateRows(
                asset.Metadata,
                asset.Kind,
                asset.Target,
                materializeRows:
                    closing is not ScorecardClosing.Count
                        and not ScorecardClosing.Exists);
        switch (closing)
        {
            case ScorecardClosing.Exists:
                _ = selected.TryGetNext(out bool exists);
                return ScorecardAnswer<HierarchyAnswerRow>.OfExists(
                    exists);
            case ScorecardClosing.Count:
                return ScorecardAnswer<HierarchyAnswerRow>.OfCount(
                    selected.CountFold<
                        HierarchyCandidateRows,
                        HierarchyAnswerRow>());
            case ScorecardClosing.Rows:
                return ScorecardAnswer<HierarchyAnswerRow>.OfRows(
                    selected.ToList<
                        HierarchyCandidateRows,
                        HierarchyAnswerRow>());
            case ScorecardClosing.Head:
            {
                var rows =
                    selected.Take<
                        HierarchyCandidateRows,
                        HierarchyAnswerRow>(shape.N);
                var result = new List<HierarchyAnswerRow>(shape.N);
                while (true)
                {
                    HierarchyAnswerRow row =
                        rows.TryGetNext(out bool hasMore);
                    if (!hasMore)
                    {
                        return ScorecardAnswer<HierarchyAnswerRow>
                            .OfRows(result);
                    }
                    result.Add(row);
                }
            }
            case ScorecardClosing.Tail:
            {
                List<HierarchyAnswerRow> last =
                    selected.TakeLast<
                        HierarchyCandidateRows,
                        HierarchyAnswerRow>(shape.N);
                return ScorecardAnswer<HierarchyAnswerRow>.OfRows(
                    last);
            }
            case ScorecardClosing.Window:
                return selected
                        .Skip<
                            HierarchyCandidateRows,
                            HierarchyAnswerRow>(shape.WindowSkip)
                        .TryTakeExactly<
                            SkipEnumerator<
                                HierarchyCandidateRows,
                                HierarchyAnswerRow>,
                            HierarchyAnswerRow>(
                                shape.WindowTake,
                                out List<HierarchyAnswerRow> window)
                    ? ScorecardAnswer<HierarchyAnswerRow>.OfRows(window)
                    : ScorecardAnswer<HierarchyAnswerRow>.OfWindowFailure();
            default:
                throw new ArgumentOutOfRangeException(nameof(closing));
        }
    }

    static ScorecardAnswer<HierarchyAnswerRow> LinqAnswer(
        ScorecardClosing closing,
        HierarchyAsset asset,
        ScorecardShape shape)
    {
        IEnumerable<HierarchyAnswerRow> rows =
            EnumerateCandidates(
                asset.Metadata,
                asset.Kind,
                asset.Target);
        return Answer(closing, rows, shape);
    }

    static ScorecardAnswer<HierarchyAnswerRow> OldAnswer(
        ScorecardClosing closing,
        HierarchyAsset asset,
        ScorecardShape shape)
    {
        MetadataRelationInspectionResult result =
            RequireAvailable(
                asset.Session.Relations(asset.OldRequest));
        IEnumerable<HierarchyAnswerRow> rows =
            result.Hierarchy.Evidence
                .Where(evidence =>
                    evidence.Kind == asset.Kind
                    && Matches(evidence.Target, asset.Target))
                .GroupBy(static evidence => evidence.Source)
                .Select(static group =>
                    new HierarchyAnswerRow(
                        group.Key.Definition.Value,
                        group.First().SourceType,
                        [.. group.Select(static row =>
                            row.MetadataToken)]));
        return Answer(closing, rows, shape);
    }

    static ScorecardAnswer<HierarchyAnswerRow> PlannerAnswer(
        ScorecardClosing closing,
        HierarchyAsset asset,
        ScorecardShape shape)
    {
        MetadataHierarchyRelationAnalysisResult result =
            RequireAvailable(
                asset.Session.AnalyzeHierarchyRelations(
                    new(
                        new(asset.Target, asset.Kind),
                        MetadataOperationPolicy.Unbounded,
                        materializeRows:
                            closing is not ScorecardClosing.Count
                                and not ScorecardClosing.Exists,
                        forwardPlan:
                            ForwardPlan(closing, shape))));
        if (closing == ScorecardClosing.Exists)
        {
            return ScorecardAnswer<HierarchyAnswerRow>.OfExists(
                result.CandidateCount != 0);
        }
        if (closing == ScorecardClosing.Count)
        {
            return ScorecardAnswer<HierarchyAnswerRow>.OfCount(
                result.CandidateCount);
        }
        IEnumerable<HierarchyAnswerRow> rows =
            result.Relations.Evidence.Select(static candidate =>
                new HierarchyAnswerRow(
                    candidate.Source.Definition.Value,
                    candidate.SourceType,
                    candidate.MetadataTokens));
        return Answer(closing, rows, shape);
    }

    static MetadataHierarchyRelationForwardPlan? ForwardPlan(
        ScorecardClosing closing,
        ScorecardShape shape) =>
        closing switch
        {
            ScorecardClosing.Exists =>
                new(1),
            ScorecardClosing.Head =>
                new(shape.N),
            ScorecardClosing.Window =>
                new(checked(shape.WindowSkip + shape.WindowTake)),
            _ => null,
        };

    static ScorecardAnswer<HierarchyAnswerRow> Answer(
        ScorecardClosing closing,
        IEnumerable<HierarchyAnswerRow> rows,
        ScorecardShape shape) =>
        closing switch
        {
            ScorecardClosing.Exists =>
                ScorecardAnswer<HierarchyAnswerRow>.OfExists(rows.Any()),
            ScorecardClosing.Count =>
                ScorecardAnswer<HierarchyAnswerRow>.OfCount(rows.Count()),
            ScorecardClosing.Head =>
                ScorecardAnswer<HierarchyAnswerRow>.OfRows(
                    rows.Take(shape.N).ToList()),
            ScorecardClosing.Tail =>
                ScorecardAnswer<HierarchyAnswerRow>.OfRows(
                    rows.TakeLast(shape.N).ToList()),
            ScorecardClosing.Rows =>
                ScorecardAnswer<HierarchyAnswerRow>.OfRows(rows.ToList()),
            ScorecardClosing.Window =>
                rows.Skip(shape.WindowSkip)
                    .Take(shape.WindowTake)
                    .ToList() is { } window
                && window.Count == shape.WindowTake
                    ? ScorecardAnswer<HierarchyAnswerRow>.OfRows(window)
                    : ScorecardAnswer<HierarchyAnswerRow>.OfWindowFailure(),
            _ => throw new ArgumentOutOfRangeException(nameof(closing)),
        };

    static IEnumerable<HierarchyAnswerRow> EnumerateCandidates(
        MetadataReader reader,
        MetadataHierarchyRelationKind kind,
        MetadataTypeDefinitionName target)
    {
        var rows = new HierarchyCandidateRows(
            reader,
            kind,
            target);
        while (true)
        {
            HierarchyAnswerRow row =
                rows.TryGetNext(out bool hasMore);
            if (!hasMore)
                yield break;
            yield return row;
        }
    }

    static MetadataRelationInspectionResult RequireAvailable(
        MetadataRelationInspectionOutcome outcome) =>
        outcome switch
        {
            MetadataRelationInspectionOutcome.Available available =>
                available.Result,
            MetadataRelationInspectionOutcome.Rejected rejected =>
                throw new InvalidOperationException(
                    rejected.Detail),
            _ => throw new InvalidOperationException(
                "Unknown relation inspection outcome."),
        };

    static MetadataHierarchyRelationAnalysisResult RequireAvailable(
        MetadataHierarchyRelationAnalysisOutcome outcome) =>
        outcome switch
        {
            MetadataHierarchyRelationAnalysisOutcome.Available available =>
                available.Result,
            MetadataHierarchyRelationAnalysisOutcome.Rejected rejected =>
                throw new InvalidOperationException(rejected.Detail),
            _ => throw new InvalidOperationException(
                "Unknown hierarchy analysis outcome."),
        };

    static bool Matches(
        MetadataTypeIdentity identity,
        MetadataTypeDefinitionName target)
    {
        MetadataNamedTypeIdentity? named = identity switch
        {
            MetadataTypeIdentity.Named value => value.Definition,
            MetadataTypeIdentity.GenericInstance value =>
                value.Definition,
            _ => null,
        };
        return named is not null
            && MetadataTypeDefinitionName.Create(
                    named.Namespace.ToString(),
                    [.. named.Segments.Select(static segment =>
                        segment.ToString())])
                is MetadataTypeDefinitionNameResult.Valid valid
            && valid.Name == target;
    }
}
