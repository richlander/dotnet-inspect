using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;

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

    ScorecardColumn<HierarchyAsset, int> oracle =
        HierarchyPopulation.NLinqColumn(shape);
    ScorecardColumn<HierarchyAsset, int>[] columns =
    [
        HierarchyPopulation.OldColumn(shape),
        HierarchyPopulation.LinqColumn(shape),
        oracle,
        HierarchyPopulation.PlannerColumn(shape),
    ];
    ScorecardCheck check =
        Scorecard.Check(assets, oracle, columns, static token => $"0x{token:X8}");
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

readonly struct HierarchyRelationRow(
    MetadataReader reader,
    TypeDefinitionHandle source,
    EntityHandle target,
    MetadataHierarchyRelationKind kind)
{
    public MetadataReader Reader { get; } = reader;

    public TypeDefinitionHandle Source { get; } = source;

    public EntityHandle Target { get; } = target;

    public MetadataHierarchyRelationKind Kind { get; } = kind;

    public int SourceToken => MetadataTokens.GetToken(Source);
}

struct HierarchyRelationRows :
    NLinq.IEnumerator<HierarchyRelationRows, HierarchyRelationRow>
{
    readonly MetadataReader _reader;
    TypeDefinitionHandleCollection.Enumerator _types;
    InterfaceImplementationHandleCollection.Enumerator _interfaces;
    TypeDefinitionHandle _source;
    TypeDefinition _definition;
    bool _basePending;
    bool _inType;

    public HierarchyRelationRows(MetadataReader reader)
    {
        _reader = reader;
        _types = reader.TypeDefinitions.GetEnumerator();
    }

    public HierarchyRelationRow TryGetNext(out bool hasMore)
    {
        while (true)
        {
            if (_inType)
            {
                if (_basePending)
                {
                    _basePending = false;
                    hasMore = true;
                    return new(
                        _reader,
                        _source,
                        _definition.BaseType,
                        MetadataHierarchyRelationKind.BaseType);
                }
                if (_interfaces.MoveNext())
                {
                    InterfaceImplementation implementation =
                        _reader.GetInterfaceImplementation(
                            _interfaces.Current);
                    hasMore = true;
                    return new(
                        _reader,
                        _source,
                        implementation.Interface,
                        MetadataHierarchyRelationKind.Interface);
                }
                _inType = false;
            }

            if (!_types.MoveNext())
            {
                hasMore = false;
                return default;
            }

            _source = _types.Current;
            _definition = _reader.GetTypeDefinition(_source);
            if (!IsExternallyVisible(_reader, _source)
                || AttributeReader.HasHiddenAttribute(
                    _reader,
                    _definition.GetCustomAttributes()))
            {
                continue;
            }
            _basePending = !_definition.BaseType.IsNil;
            _interfaces =
                _definition.GetInterfaceImplementations().GetEnumerator();
            _inType = true;
        }
    }

    static TAccumulator
        NLinq.IEnumerator<HierarchyRelationRows, HierarchyRelationRow>
            .Fold<TAccumulator, TFunction>(
                scoped ref HierarchyRelationRows source,
                TAccumulator accumulator,
                TFunction function)
    {
        while (true)
        {
            HierarchyRelationRow row =
                source.TryGetNext(out bool hasMore);
            if (!hasMore)
                return accumulator;
            accumulator = function.Invoke(accumulator, row);
        }
    }

    static bool IsExternallyVisible(
        MetadataReader reader,
        TypeDefinitionHandle handle)
    {
        while (!handle.IsNil)
        {
            TypeDefinition definition = reader.GetTypeDefinition(handle);
            TypeAttributes visibility =
                definition.Attributes & TypeAttributes.VisibilityMask;
            TypeDefinitionHandle declaring =
                definition.GetDeclaringType();
            if (declaring.IsNil
                    ? visibility != TypeAttributes.Public
                    : visibility != TypeAttributes.NestedPublic)
            {
                return false;
            }
            handle = declaring;
        }
        return true;
    }
}

static class HierarchyPopulation
{
    public static ScorecardColumn<HierarchyAsset, int> OldColumn(
        ScorecardShape shape) =>
        new("Old", (closing, asset) => OldAnswer(closing, asset, shape));

    public static ScorecardColumn<HierarchyAsset, int> LinqColumn(
        ScorecardShape shape) =>
        new("LINQ", (closing, asset) => LinqAnswer(closing, asset, shape));

    public static ScorecardColumn<HierarchyAsset, int> NLinqColumn(
        ScorecardShape shape) =>
        new("NLinq", (closing, asset) => NLinqAnswer(closing, asset, shape));

    public static ScorecardColumn<HierarchyAsset, int> PlannerColumn(
        ScorecardShape shape) =>
        new(
            "Planner (shipping)",
            (closing, asset) => PlannerAnswer(closing, asset, shape));

    static ScorecardAnswer<int> NLinqAnswer(
        ScorecardClosing closing,
        HierarchyAsset asset,
        ScorecardShape shape)
    {
        var selected =
            new HierarchyRelationRows(asset.Metadata)
                .Where<
                    HierarchyRelationRows,
                    HierarchyRelationRow,
                    Selected>(
                        new(asset.Kind, asset.Target));
        switch (closing)
        {
            case ScorecardClosing.Exists:
                return ScorecardAnswer<int>.OfExists(
                    new HierarchyRelationRows(asset.Metadata)
                        .Any<
                            HierarchyRelationRows,
                            HierarchyRelationRow,
                            Selected>(
                                new(asset.Kind, asset.Target)));
            case ScorecardClosing.Count:
                return ScorecardAnswer<int>.OfCount(
                    selected.CountFold<
                        Filter<
                            HierarchyRelationRow,
                            HierarchyRelationRows,
                            Selected>,
                        HierarchyRelationRow>());
            case ScorecardClosing.Rows:
                return ScorecardAnswer<int>.OfRows(
                    selected
                        .Select<
                            Filter<
                                HierarchyRelationRow,
                                HierarchyRelationRows,
                                Selected>,
                            HierarchyRelationRow,
                            int,
                            ToToken>(default)
                        .ToList<
                            Map<
                                HierarchyRelationRow,
                                int,
                                Filter<
                                    HierarchyRelationRow,
                                    HierarchyRelationRows,
                                    Selected>,
                                ToToken>,
                            int>());
            case ScorecardClosing.Head:
            {
                var rows =
                    selected
                        .Select<
                            Filter<
                                HierarchyRelationRow,
                                HierarchyRelationRows,
                                Selected>,
                            HierarchyRelationRow,
                            int,
                            ToToken>(default)
                        .Take<
                            Map<
                                HierarchyRelationRow,
                                int,
                                Filter<
                                    HierarchyRelationRow,
                                    HierarchyRelationRows,
                                    Selected>,
                                ToToken>,
                            int>(shape.N);
                var result = new List<int>(shape.N);
                while (true)
                {
                    int token = rows.TryGetNext(out bool hasMore);
                    if (!hasMore)
                        return ScorecardAnswer<int>.OfRows(result);
                    result.Add(token);
                }
            }
            case ScorecardClosing.Tail:
            {
                List<HierarchyRelationRow> last =
                    selected.TakeLast<
                        Filter<
                            HierarchyRelationRow,
                            HierarchyRelationRows,
                            Selected>,
                        HierarchyRelationRow>(shape.N);
                return ScorecardAnswer<int>.OfRows(
                    [.. last.Select(static row => row.SourceToken)]);
            }
            case ScorecardClosing.Window:
                return selected
                        .Skip<
                            Filter<
                                HierarchyRelationRow,
                                HierarchyRelationRows,
                                Selected>,
                            HierarchyRelationRow>(shape.WindowSkip)
                        .Select<
                            SkipEnumerator<
                                Filter<
                                    HierarchyRelationRow,
                                    HierarchyRelationRows,
                                    Selected>,
                                HierarchyRelationRow>,
                            HierarchyRelationRow,
                            int,
                            ToToken>(default)
                        .TryTakeExactly<
                            Map<
                                HierarchyRelationRow,
                                int,
                                SkipEnumerator<
                                    Filter<
                                        HierarchyRelationRow,
                                        HierarchyRelationRows,
                                        Selected>,
                                    HierarchyRelationRow>,
                                ToToken>,
                            int>(
                                shape.WindowTake,
                                out List<int> window)
                    ? ScorecardAnswer<int>.OfRows(window)
                    : ScorecardAnswer<int>.OfWindowFailure();
            default:
                throw new ArgumentOutOfRangeException(nameof(closing));
        }
    }

    static ScorecardAnswer<int> LinqAnswer(
        ScorecardClosing closing,
        HierarchyAsset asset,
        ScorecardShape shape)
    {
        IEnumerable<int> rows =
            Enumerate(asset.Metadata)
                .Where(row => Matches(row, asset.Kind, asset.Target))
                .Select(static row => row.SourceToken);
        return Answer(closing, rows, shape);
    }

    static ScorecardAnswer<int> OldAnswer(
        ScorecardClosing closing,
        HierarchyAsset asset,
        ScorecardShape shape)
    {
        MetadataRelationInspectionResult result =
            RequireAvailable(
                asset.Session.Relations(asset.OldRequest));
        IEnumerable<int> rows =
            result.Hierarchy.Evidence
                .Where(evidence =>
                    evidence.Kind == asset.Kind
                    && Matches(evidence.Target, asset.Target))
                .Select(static evidence =>
                    evidence.Source.Definition.Value);
        return Answer(closing, rows, shape);
    }

    static ScorecardAnswer<int> PlannerAnswer(
        ScorecardClosing closing,
        HierarchyAsset asset,
        ScorecardShape shape)
    {
        MetadataRelationInspectionResult result =
            RequireAvailable(
                asset.Session.Relations(asset.PlannerRequest));
        IEnumerable<int> rows =
            result.Hierarchy.Evidence.Select(static evidence =>
                evidence.Source.Definition.Value);
        return Answer(closing, rows, shape);
    }

    static ScorecardAnswer<int> Answer(
        ScorecardClosing closing,
        IEnumerable<int> rows,
        ScorecardShape shape) =>
        closing switch
        {
            ScorecardClosing.Exists =>
                ScorecardAnswer<int>.OfExists(rows.Any()),
            ScorecardClosing.Count =>
                ScorecardAnswer<int>.OfCount(rows.Count()),
            ScorecardClosing.Head =>
                ScorecardAnswer<int>.OfRows(
                    rows.Take(shape.N).ToList()),
            ScorecardClosing.Tail =>
                ScorecardAnswer<int>.OfRows(
                    rows.TakeLast(shape.N).ToList()),
            ScorecardClosing.Rows =>
                ScorecardAnswer<int>.OfRows(rows.ToList()),
            ScorecardClosing.Window =>
                rows.Skip(shape.WindowSkip)
                    .Take(shape.WindowTake)
                    .ToList() is { } window
                && window.Count == shape.WindowTake
                    ? ScorecardAnswer<int>.OfRows(window)
                    : ScorecardAnswer<int>.OfWindowFailure(),
            _ => throw new ArgumentOutOfRangeException(nameof(closing)),
        };

    static IEnumerable<HierarchyRelationRow> Enumerate(
        MetadataReader reader)
    {
        var rows = new HierarchyRelationRows(reader);
        while (true)
        {
            HierarchyRelationRow row =
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

    static bool Matches(
        HierarchyRelationRow row,
        MetadataHierarchyRelationKind kind,
        MetadataTypeDefinitionName target) =>
        row.Kind == kind
        && Matches(row.Reader, row.Target, target);

    static bool Matches(
        MetadataReader reader,
        EntityHandle handle,
        MetadataTypeDefinitionName target)
    {
        if (target.Segments.Length != 1)
            return false;
        string targetNamespace = target.Namespace.ToString();
        string targetName = target.Segments[0].ToString();
        return handle.Kind switch
        {
            HandleKind.TypeDefinition =>
                Matches(
                    reader,
                    reader.GetTypeDefinition(
                        (TypeDefinitionHandle)handle),
                    targetNamespace,
                    targetName),
            HandleKind.TypeReference =>
                Matches(
                    reader,
                    reader.GetTypeReference(
                        (TypeReferenceHandle)handle),
                    targetNamespace,
                    targetName),
            _ => false,
        };
    }

    static bool Matches(
        MetadataReader reader,
        TypeDefinition definition,
        string targetNamespace,
        string targetName) =>
        definition.GetDeclaringType().IsNil
        && reader.StringComparer.Equals(
            definition.Namespace,
            targetNamespace)
        && reader.StringComparer.Equals(
            definition.Name,
            targetName);

    static bool Matches(
        MetadataReader reader,
        TypeReference reference,
        string targetNamespace,
        string targetName) =>
        reference.ResolutionScope.Kind
            != HandleKind.TypeReference
        && reader.StringComparer.Equals(
            reference.Namespace,
            targetNamespace)
        && reader.StringComparer.Equals(
            reference.Name,
            targetName);

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

    readonly struct Selected(
        MetadataHierarchyRelationKind kind,
        MetadataTypeDefinitionName target) :
        IFunc<HierarchyRelationRow, bool>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Invoke(HierarchyRelationRow row) =>
            Matches(row, kind, target);
    }

    readonly struct ToToken :
        IFunc<HierarchyRelationRow, int>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Invoke(HierarchyRelationRow row) =>
            row.SourceToken;
    }
}
