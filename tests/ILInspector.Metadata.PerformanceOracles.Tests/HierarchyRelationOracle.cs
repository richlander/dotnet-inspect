using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using DotnetInspector.Fixtures;
using ILInspector.Metadata;
using NLinq;

namespace DotnetInspector.PerformanceOracles;

public readonly record struct HierarchyRelationOracleRow(
    int SourceToken,
    MetadataTypeDefinitionName SourceType,
    ImmutableArray<int> OccurrenceTokens);

public sealed class HierarchyRelationOracleRowComparer :
    IEqualityComparer<HierarchyRelationOracleRow>
{
    public static HierarchyRelationOracleRowComparer Instance { get; } = new();

    public bool Equals(
        HierarchyRelationOracleRow x,
        HierarchyRelationOracleRow y) =>
        x.SourceToken == y.SourceToken
        && x.SourceType == y.SourceType
        && x.OccurrenceTokens.AsSpan()
            .SequenceEqual(y.OccurrenceTokens.AsSpan());

    public int GetHashCode(HierarchyRelationOracleRow row)
    {
        var hash = new HashCode();
        hash.Add(row.SourceToken);
        hash.Add(row.SourceType);
        foreach (int token in row.OccurrenceTokens)
            hash.Add(token);
        return hash.ToHashCode();
    }
}

public readonly record struct HierarchyRelationSafetyObservation(
    int CandidateCount,
    string Diagnostics,
    MetadataOperationCounters Counters);

public sealed record HierarchyRelationSafetyMismatch(
    string Asset,
    string Column,
    HierarchyRelationSafetyObservation Actual,
    HierarchyRelationSafetyObservation Expected);

public sealed record HierarchyRelationSafetyCheck(
    int Compared,
    IReadOnlyList<HierarchyRelationSafetyMismatch> Mismatches)
{
    public bool Agrees => Mismatches.Count == 0;
}

public static class HierarchyRelationOracle
{
    public static string RowText(HierarchyRelationOracleRow row) =>
        $"0x{row.SourceToken:X8}|{row.SourceType}|"
        + string.Join(
            ',',
            row.OccurrenceTokens.Select(
                static token => $"0x{token:X8}"));

    public static ScorecardAnswer<HierarchyRelationOracleRow> LinqAnswer(
        AssemblyInspectionSession session,
        MetadataHierarchyRelationKind kind,
        MetadataTypeDefinitionName target,
        ScorecardClosing closing,
        ScorecardShape shape)
    {
        bool materializeRows =
            closing is not ScorecardClosing.Count
                and not ScorecardClosing.Exists;
        using var context =
            new HierarchyRelationOracleContext(
                session,
                kind,
                target,
                materializeRows);
        IEnumerable<HierarchyRelationOracleRow> selected =
            context.TypeDefinitions
                .Select(handle => AnalyzeLinq(context, handle))
                .Where(static candidate => candidate.Matched)
                .Select(static candidate => candidate.Row);
        return closing switch
        {
            ScorecardClosing.Exists =>
                ScorecardAnswer<HierarchyRelationOracleRow>.OfExists(
                    selected.Any()),
            ScorecardClosing.Count =>
                ScorecardAnswer<HierarchyRelationOracleRow>.OfCount(
                    selected.Count()),
            ScorecardClosing.Head =>
                ScorecardAnswer<HierarchyRelationOracleRow>.OfRows(
                    selected.Take(shape.N).ToList()),
            ScorecardClosing.Tail =>
                ScorecardAnswer<HierarchyRelationOracleRow>.OfRows(
                    selected.TakeLast(shape.N).ToList()),
            ScorecardClosing.Rows =>
                ScorecardAnswer<HierarchyRelationOracleRow>.OfRows(
                    selected.ToList()),
            ScorecardClosing.Window =>
                selected.Skip(shape.WindowSkip)
                    .Take(shape.WindowTake)
                    .ToList() is { } window
                && window.Count == shape.WindowTake
                    ? ScorecardAnswer<HierarchyRelationOracleRow>
                        .OfRows(window)
                    : ScorecardAnswer<HierarchyRelationOracleRow>
                        .OfWindowFailure(),
            _ => throw new ArgumentOutOfRangeException(nameof(closing)),
        };
    }

    public static ScorecardAnswer<HierarchyRelationOracleRow> NLinqAnswer(
        AssemblyInspectionSession session,
        MetadataHierarchyRelationKind kind,
        MetadataTypeDefinitionName target,
        ScorecardClosing closing,
        ScorecardShape shape)
    {
        var selected =
            new HierarchyRelationNLinqRows(
                session,
                kind,
                target,
                materializeRows:
                    closing is not ScorecardClosing.Count
                        and not ScorecardClosing.Exists);
        try
        {
            switch (closing)
            {
                case ScorecardClosing.Exists:
                    _ = selected.TryGetNext(out bool exists);
                    return ScorecardAnswer<HierarchyRelationOracleRow>.OfExists(
                        exists);
                case ScorecardClosing.Count:
                    return ScorecardAnswer<HierarchyRelationOracleRow>.OfCount(
                        selected.CountFold<
                            HierarchyRelationNLinqRows,
                            HierarchyRelationOracleRow>());
                case ScorecardClosing.Rows:
                    return ScorecardAnswer<HierarchyRelationOracleRow>.OfRows(
                        selected.ToList<
                            HierarchyRelationNLinqRows,
                            HierarchyRelationOracleRow>());
                case ScorecardClosing.Head:
                {
                    var rows =
                        selected.Take<
                            HierarchyRelationNLinqRows,
                            HierarchyRelationOracleRow>(shape.N);
                    var result =
                        new List<HierarchyRelationOracleRow>(shape.N);
                    while (true)
                    {
                        HierarchyRelationOracleRow row =
                            rows.TryGetNext(out bool hasMore);
                        if (!hasMore)
                        {
                            return ScorecardAnswer<
                                HierarchyRelationOracleRow>.OfRows(result);
                        }
                        result.Add(row);
                    }
                }
                case ScorecardClosing.Tail:
                    return ScorecardAnswer<HierarchyRelationOracleRow>.OfRows(
                        selected.TakeLast<
                            HierarchyRelationNLinqRows,
                            HierarchyRelationOracleRow>(shape.N));
                case ScorecardClosing.Window:
                    return selected
                            .Skip<
                                HierarchyRelationNLinqRows,
                                HierarchyRelationOracleRow>(
                                    shape.WindowSkip)
                            .TryTakeExactly<
                                SkipEnumerator<
                                    HierarchyRelationNLinqRows,
                                    HierarchyRelationOracleRow>,
                                HierarchyRelationOracleRow>(
                                    shape.WindowTake,
                                    out List<HierarchyRelationOracleRow> window)
                        ? ScorecardAnswer<HierarchyRelationOracleRow>
                            .OfRows(window)
                        : ScorecardAnswer<HierarchyRelationOracleRow>
                            .OfWindowFailure();
                default:
                    throw new ArgumentOutOfRangeException(nameof(closing));
            }
        }
        finally
        {
            selected.Dispose();
        }
    }

    public static ScorecardAnswer<HierarchyRelationOracleRow> PlannerAnswer(
        AssemblyInspectionSession session,
        MetadataHierarchyRelationKind kind,
        MetadataTypeDefinitionName target,
        ScorecardClosing closing,
        ScorecardShape shape)
    {
        MetadataHierarchyRelationAnalysisResult result =
            RequireAvailable(
                session.AnalyzeHierarchyRelations(
                    new(
                        new(target, kind),
                        MetadataOperationPolicy.Unbounded,
                        materializeRows:
                            closing is not ScorecardClosing.Count
                                and not ScorecardClosing.Exists,
                        forwardPlan:
                            ForwardPlan(closing, shape))));
        if (closing == ScorecardClosing.Exists)
        {
            return ScorecardAnswer<HierarchyRelationOracleRow>.OfExists(
                result.CandidateCount != 0);
        }
        if (closing == ScorecardClosing.Count)
        {
            return ScorecardAnswer<HierarchyRelationOracleRow>.OfCount(
                result.CandidateCount);
        }

        IEnumerable<HierarchyRelationOracleRow> rows =
            result.Relations.Evidence.Select(static candidate =>
                new HierarchyRelationOracleRow(
                    candidate.Source.Definition.Value,
                    candidate.SourceType,
                    candidate.MetadataTokens));
        return closing switch
        {
            ScorecardClosing.Head =>
                ScorecardAnswer<HierarchyRelationOracleRow>.OfRows(
                    rows.Take(shape.N).ToList()),
            ScorecardClosing.Tail =>
                ScorecardAnswer<HierarchyRelationOracleRow>.OfRows(
                    rows.TakeLast(shape.N).ToList()),
            ScorecardClosing.Rows =>
                ScorecardAnswer<HierarchyRelationOracleRow>.OfRows(
                    rows.ToList()),
            ScorecardClosing.Window =>
                rows.Skip(shape.WindowSkip)
                    .Take(shape.WindowTake)
                    .ToList() is { } window
                && window.Count == shape.WindowTake
                    ? ScorecardAnswer<HierarchyRelationOracleRow>
                        .OfRows(window)
                    : ScorecardAnswer<HierarchyRelationOracleRow>
                        .OfWindowFailure(),
            _ => throw new ArgumentOutOfRangeException(nameof(closing)),
        };
    }

    public static HierarchyRelationSafetyCheck CheckSafety()
    {
        (string Name, byte[] Content, MetadataTypeDefinitionName Target,
            MetadataHierarchyRelationKind Kind)[] assets =
        [
            (
                "malformed-generic-typespec",
                HierarchyRelationSafetyFixtures
                    .BuildMalformedGenericTypeSpecification(),
                TypeName("Sample", "ITarget`1"),
                MetadataHierarchyRelationKind.Interface),
            (
                "cyclic-nested-visibility",
                HierarchyRelationSafetyFixtures
                    .BuildCyclicNestedTypeVisibility(128),
                TypeName("Sample", "ITarget"),
                MetadataHierarchyRelationKind.BaseType),
        ];
        var mismatches =
            new List<HierarchyRelationSafetyMismatch>();
        int compared = 0;
        foreach (var asset in assets)
        {
            HierarchyRelationSafetyObservation expected =
                ObserveNLinq(
                    asset.Content,
                    asset.Target,
                    asset.Kind);
            (string Name, HierarchyRelationSafetyObservation Observation)[]
                columns =
            [
                (
                    "LINQ",
                    ObserveLinq(
                        asset.Content,
                        asset.Target,
                        asset.Kind)),
                (
                    "Planner",
                    ObservePlanner(
                        asset.Content,
                        asset.Target,
                        asset.Kind)),
            ];
            foreach ((string name,
                HierarchyRelationSafetyObservation observation)
                in columns)
            {
                compared++;
                if (observation != expected)
                {
                    mismatches.Add(
                        new(
                            asset.Name,
                            name,
                            observation,
                            expected));
                }
            }
        }
        return new(compared, mismatches);
    }

    static HierarchyRelationOracleCandidate AnalyzeLinq(
        HierarchyRelationOracleContext context,
        TypeDefinitionHandle handle)
    {
        context.Charge(
            MetadataOperationDimension.DeclarationCandidates);
        try
        {
            TypeDefinition definition =
                context.Reader.GetTypeDefinition(handle);
            if (!context.IsExternallyVisible(handle)
                || context.IsHidden(definition))
            {
                return default;
            }

            if (context.Kind == MetadataHierarchyRelationKind.BaseType)
            {
                if (definition.BaseType.IsNil)
                    return default;
                int token = MetadataTokens.GetToken(handle);
                if (context.Match(definition.BaseType, token)
                    is not MetadataTypeDefinitionNameMatchResult.Match)
                {
                    return default;
                }
                if (!context.MaterializeRows)
                    return new(true, default);

                context.Charge(
                    MetadataOperationDimension.StructuredNodes);
                MetadataTypeDefinitionName? sourceName =
                    context.ReadSourceName(handle);
                return sourceName is null
                    ? default
                    : new(
                        true,
                        new(
                            MetadataTokens.GetToken(handle),
                            sourceName,
                            [token]));
            }

            bool matched = false;
            ImmutableArray<int>.Builder? tokens =
                context.MaterializeRows
                    ? ImmutableArray.CreateBuilder<int>()
                    : null;
            foreach (InterfaceImplementationHandle implementationHandle
                in definition.GetInterfaceImplementations())
            {
                context.Charge(
                    MetadataOperationDimension
                        .InterfaceImplementationRows);
                InterfaceImplementation implementation =
                    context.Reader.GetInterfaceImplementation(
                        implementationHandle);
                int token =
                    MetadataTokens.GetToken(implementationHandle);
                MetadataTypeDefinitionNameMatchResult match =
                    context.Match(
                        implementation.Interface,
                        token);
                if (match
                    == MetadataTypeDefinitionNameMatchResult.Rejected)
                {
                    return default;
                }
                if (match
                    != MetadataTypeDefinitionNameMatchResult.Match)
                {
                    continue;
                }

                matched = true;
                if (tokens is not null)
                {
                    context.Charge(
                        MetadataOperationDimension.StructuredNodes);
                    tokens.Add(token);
                }
            }
            if (!matched)
                return default;
            if (!context.MaterializeRows)
                return new(true, default);

            MetadataTypeDefinitionName? interfaceSourceName =
                context.ReadSourceName(handle);
            return interfaceSourceName is null
                ? default
                : new(
                    true,
                    new(
                        MetadataTokens.GetToken(handle),
                        interfaceSourceName,
                        tokens!.ToImmutable()));
        }
        catch (Exception exception)
            when (exception
                    is not MetadataVisibilityGraphException
                && exception is
                    (BadImageFormatException
                        or ArgumentException
                        or InvalidOperationException
                        or OverflowException))
        {
            context.RecordMalformed(
                MetadataTokens.GetToken(handle),
                exception.Message);
            return default;
        }
    }

    static HierarchyRelationOracleCandidate AnalyzeNLinq(
        HierarchyRelationOracleContext context,
        TypeDefinitionHandle handle)
    {
        context.Charge(
            MetadataOperationDimension.DeclarationCandidates);
        try
        {
            TypeDefinition definition =
                context.Reader.GetTypeDefinition(handle);
            if (!context.IsExternallyVisible(handle)
                || context.IsHidden(definition))
            {
                return default;
            }

            if (context.Kind == MetadataHierarchyRelationKind.BaseType)
            {
                if (definition.BaseType.IsNil)
                    return default;
                int token = MetadataTokens.GetToken(handle);
                MetadataTypeDefinitionNameMatchResult match =
                    context.Match(definition.BaseType, token);
                if (match
                    != MetadataTypeDefinitionNameMatchResult.Match)
                {
                    return default;
                }
                if (!context.MaterializeRows)
                    return new(true, default);

                context.Charge(
                    MetadataOperationDimension.StructuredNodes);
                MetadataTypeDefinitionName? sourceName =
                    context.ReadSourceName(handle);
                if (sourceName is null)
                    return default;
                return new(
                    true,
                    new(
                        MetadataTokens.GetToken(handle),
                        sourceName,
                        [token]));
            }

            bool matched = false;
            ImmutableArray<int>.Builder? tokens =
                context.MaterializeRows
                    ? ImmutableArray.CreateBuilder<int>()
                    : null;
            foreach (InterfaceImplementationHandle implementationHandle
                in definition.GetInterfaceImplementations())
            {
                context.Charge(
                    MetadataOperationDimension
                        .InterfaceImplementationRows);
                InterfaceImplementation implementation =
                    context.Reader.GetInterfaceImplementation(
                        implementationHandle);
                int token =
                    MetadataTokens.GetToken(implementationHandle);
                MetadataTypeDefinitionNameMatchResult match =
                    context.Match(
                        implementation.Interface,
                        token);
                if (match
                    == MetadataTypeDefinitionNameMatchResult.Rejected)
                {
                    return default;
                }
                if (match
                    != MetadataTypeDefinitionNameMatchResult.Match)
                {
                    continue;
                }

                matched = true;
                if (tokens is not null)
                {
                    context.Charge(
                        MetadataOperationDimension.StructuredNodes);
                    tokens.Add(token);
                }
            }
            if (!matched)
                return default;
            if (!context.MaterializeRows)
                return new(true, default);

            MetadataTypeDefinitionName? interfaceSourceName =
                context.ReadSourceName(handle);
            if (interfaceSourceName is null)
                return default;
            return new(
                true,
                new(
                    MetadataTokens.GetToken(handle),
                    interfaceSourceName,
                    tokens!.ToImmutable()));
        }
        catch (Exception exception)
            when (exception
                    is not MetadataVisibilityGraphException
                && exception is
                    (BadImageFormatException
                        or ArgumentException
                        or InvalidOperationException
                        or OverflowException))
        {
            context.RecordMalformed(
                MetadataTokens.GetToken(handle),
                exception.Message);
            return default;
        }
    }

    static HierarchyRelationSafetyObservation ObserveLinq(
        byte[] content,
        MetadataTypeDefinitionName target,
        MetadataHierarchyRelationKind kind)
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.OpenPrefetched(
                new MemoryStream(content, writable: false));
        using var context =
            new HierarchyRelationOracleContext(
                session,
                kind,
                target,
                materializeRows: true);
        int candidates = 0;
        try
        {
            candidates =
                context.TypeDefinitions
                    .Select(handle => AnalyzeLinq(context, handle))
                    .Count(static candidate => candidate.Matched);
        }
        catch (MetadataVisibilityGraphException exception)
        {
            context.RecordMalformed(null, exception.Message);
        }
        return context.Observe(candidates);
    }

    static HierarchyRelationSafetyObservation ObserveNLinq(
        byte[] content,
        MetadataTypeDefinitionName target,
        MetadataHierarchyRelationKind kind)
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.OpenPrefetched(
                new MemoryStream(content, writable: false));
        var selected =
            new HierarchyRelationNLinqRows(
                session,
                kind,
                target,
                materializeRows: true);
        int candidates = 0;
        try
        {
            candidates =
                selected.CountFold<
                    HierarchyRelationNLinqRows,
                    HierarchyRelationOracleRow>();
        }
        catch (MetadataVisibilityGraphException exception)
        {
            selected.Context.RecordMalformed(
                null,
                exception.Message);
        }
        HierarchyRelationSafetyObservation observation =
            selected.Context.Observe(candidates);
        selected.Dispose();
        return observation;
    }

    static HierarchyRelationSafetyObservation ObservePlanner(
        byte[] content,
        MetadataTypeDefinitionName target,
        MetadataHierarchyRelationKind kind)
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.OpenPrefetched(
                new MemoryStream(content, writable: false));
        MetadataHierarchyRelationAnalysisResult result =
            RequireAvailable(
                session.AnalyzeHierarchyRelations(
                    new(
                        new(target, kind),
                        MetadataOperationPolicy.Unbounded)));
        return new(
            result.CandidateCount,
            Diagnostics(result.Relations.Diagnostics),
            result.Receipt.Counters);
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

    static string Diagnostics(
        IEnumerable<MetadataRelationDiagnostic> diagnostics) =>
        string.Join(
            ',',
            diagnostics.Select(static diagnostic =>
                $"{diagnostic.Kind}:{diagnostic.BudgetDimension}"));

    static MetadataTypeDefinitionName TypeName(
        string @namespace,
        string name) =>
        MetadataTypeDefinitionName.Create(
            @namespace,
            [name])
        is MetadataTypeDefinitionNameResult.Valid valid
            ? valid.Name
            : throw new InvalidOperationException(
                "The hierarchy safety target is invalid.");

    readonly record struct HierarchyRelationOracleCandidate(
        bool Matched,
        HierarchyRelationOracleRow Row);

    struct HierarchyRelationNLinqRows :
        NLinq.IEnumerator<
            HierarchyRelationNLinqRows,
            HierarchyRelationOracleRow>,
        IDisposable
    {
        TypeDefinitionHandleCollection.Enumerator _types;

        internal HierarchyRelationNLinqRows(
            AssemblyInspectionSession session,
            MetadataHierarchyRelationKind kind,
            MetadataTypeDefinitionName target,
            bool materializeRows)
        {
            Context =
                new(
                    session,
                    kind,
                    target,
                    materializeRows);
            _types = Context.TypeDefinitions.GetEnumerator();
        }

        internal HierarchyRelationOracleContext Context { get; }

        public HierarchyRelationOracleRow TryGetNext(out bool hasMore)
        {
            while (_types.MoveNext())
            {
                HierarchyRelationOracleCandidate candidate =
                    AnalyzeNLinq(Context, _types.Current);
                if (!candidate.Matched)
                    continue;
                hasMore = true;
                return candidate.Row;
            }

            hasMore = false;
            return default;
        }

        public void Dispose() => Context.Dispose();

        static TAccumulator
            NLinq.IEnumerator<
                HierarchyRelationNLinqRows,
                HierarchyRelationOracleRow>
                .Fold<TAccumulator, TFunction>(
                    scoped ref HierarchyRelationNLinqRows source,
                    TAccumulator accumulator,
                    TFunction function)
        {
            while (true)
            {
                HierarchyRelationOracleRow row =
                    source.TryGetNext(out bool hasMore);
                if (!hasMore)
                    return accumulator;
                accumulator = function.Invoke(accumulator, row);
            }
        }
    }

    sealed class HierarchyRelationOracleContext : IDisposable
    {
        readonly MetadataOperationContext _operation;
        readonly MetadataVisibilityResolver _visibility;
        readonly ImmutableArray<MetadataRelationDiagnostic>.Builder
            _diagnostics =
                ImmutableArray.CreateBuilder<
                    MetadataRelationDiagnostic>();

        internal HierarchyRelationOracleContext(
            AssemblyInspectionSession session,
            MetadataHierarchyRelationKind kind,
            MetadataTypeDefinitionName target,
            bool materializeRows)
        {
            ArgumentNullException.ThrowIfNull(session);
            Target = target
                ?? throw new ArgumentNullException(nameof(target));
            Kind = kind;
            MaterializeRows = materializeRows;
            Reader = session.GetAdmittedMetadataReader();
            _operation =
                new MetadataOperationContext(
                    MetadataOperationPolicy.Unbounded);
            if (_operation.AdmitImage(Reader)
                is MetadataImageAdmissionResult.Rejected rejected)
            {
                throw new InvalidOperationException(
                    "The admitted hierarchy image exceeded the oracle "
                    + $"row budget ({rejected.Failure.ImageMetadataRows} > "
                    + $"{rejected.Failure.MaxMetadataRows}).");
            }
            _visibility = new(Reader);
        }

        internal MetadataReader Reader { get; }

        internal TypeDefinitionHandleCollection TypeDefinitions =>
            Reader.TypeDefinitions;

        internal MetadataHierarchyRelationKind Kind { get; }

        internal MetadataTypeDefinitionName Target { get; }

        internal bool MaterializeRows { get; }

        internal bool IsExternallyVisible(
            TypeDefinitionHandle handle) =>
            _visibility.IsExternallyVisible(
                Reader,
                handle,
                _operation);

        internal bool IsHidden(TypeDefinition definition) =>
            AttributeReader.HasHiddenAttribute(
                Reader,
                definition.GetCustomAttributes());

        internal MetadataTypeDefinitionNameMatchResult Match(
            EntityHandle candidateTarget,
            int occurrenceToken)
        {
            Charge(MetadataOperationDimension.RelationshipEdges);
            MetadataTypeDefinitionNameMatchResult match =
                MetadataHierarchyRelationAnalysis.MatchTarget(
                    Reader,
                    candidateTarget,
                    Target,
                    out string? failure);
            if (match == MetadataTypeDefinitionNameMatchResult.Rejected)
            {
                _diagnostics.Add(
                    new(
                        MetadataRelationFamily.Hierarchy,
                        MetadataRelationDiagnosticKind.UnsupportedShape,
                        occurrenceToken,
                        failure
                            ?? "The hierarchy target definition could not "
                                + "be analyzed safely."));
            }
            return match;
        }

        internal MetadataTypeDefinitionName? ReadSourceName(
            TypeDefinitionHandle handle)
        {
            MetadataTypeDefinitionNameReadResult read =
                MetadataTypeDefinitionNameReader.Read(
                    Reader,
                    handle,
                    beforeMaterialize: amount =>
                        Charge(
                            MetadataOperationDimension.StructuredNodes,
                            amount),
                    chargeChain: amount =>
                        Charge(
                            MetadataOperationDimension.RelationshipEdges,
                            amount),
                    chargeCharacters: amount =>
                        Charge(
                            MetadataOperationDimension.RetainedText,
                            amount));
            if (read
                is MetadataTypeDefinitionNameReadResult.Read source)
            {
                return source.Name;
            }

            var rejected =
                (MetadataTypeDefinitionNameReadResult.Rejected)read;
            RecordMalformed(
                MetadataTokens.GetToken(handle),
                rejected.Failure.Detail);
            return null;
        }

        internal void Charge(
            MetadataOperationDimension dimension,
            long amount = 1) =>
            _operation.Charge(dimension, amount);

        internal void RecordMalformed(
            int? metadataToken,
            string detail) =>
            _diagnostics.Add(
                new(
                    MetadataRelationFamily.Hierarchy,
                    MetadataRelationDiagnosticKind.MalformedMetadata,
                    metadataToken,
                    detail));

        internal HierarchyRelationSafetyObservation Observe(
            int candidateCount) =>
            new(
                candidateCount,
                Diagnostics(_diagnostics),
                _operation.Counters);

        public void Dispose() => _operation.Dispose();
    }
}
