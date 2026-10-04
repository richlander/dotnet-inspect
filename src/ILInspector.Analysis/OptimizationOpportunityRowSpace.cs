using System.Collections.Immutable;

using QuerySpace;
using QuerySpace.Composition;
using QuerySpace.Rows;

namespace ILInspector.Analysis;

public enum OptimizationOpportunityKind
{
    Boxing,
    Arrays,
    ClosuresAndDelegates,
    Enumerators,
    Strings,
    LoopHotPaths,
    AllocationHotspots,
    Async,
    Other,
}

public sealed class OptimizationOpportunityCuratedQuery
{
    internal OptimizationOpportunityCuratedQuery(
        string identity,
        PortableQueryIntent intent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        ArgumentNullException.ThrowIfNull(intent);
        Identity = identity;
        Intent = intent;
    }

    public string Identity { get; }

    public PortableQueryIntent Intent { get; }
}

/// <summary>
/// Analysis-owned row vocabulary and curated queries for optimization
/// opportunities.
/// </summary>
public static class OptimizationOpportunityRowSpace
{
    public const string RowSet = "optimization-opportunities";
    public const string RowScopeIdentity =
        "analysis/optimization-opportunities";
    public const string RowVocabularyIdentity =
        "analysis/optimization-opportunities/v1";
    public const string TriageOrderKey = "Triage";
    public const string AllocationFanoutOrderKey = "AllocationFanout";
    public const string KindKey = "Kind";

    private static readonly ImmutableArray<string> s_rankedValues =
        ["low", "medium", "high"];

    private static readonly ImmutableArray<string> s_knownShapes =
    [
        "allocation-hotspot",
        "allocation-fanout",
        "async-state-machine",
        "box-value-type",
        "cache-lookup-factory-delegate",
        "capturing-delegate",
        "enumerator-allocation",
        "generic-parameter-object-box",
        "instance-method-group-delegate",
        "linq-scan-in-loop",
        "materialize-in-loop",
        "scan-method-in-loop-call",
        "scan-method-in-recursive-traversal",
        "small-array",
        "span-to-array-copy",
        "stackalloc-candidate",
        "string-build-in-loop",
        "string-materialization",
        "sync-call-in-async",
        "temporary-byte-array-copy",
    ];

    private static readonly RowQueryNamedOrder<OptimizationOpportunity>
        TriageOrder =
        new(
            RowQueryNamedOrderIdentity.Create(),
            TriageOrderKey,
            RowQueryOrderPurpose.Ranking,
            direction => Directional(
                OptimizationOpportunityRanking.OpportunityComparer,
                direction));

    private static readonly RowQueryNamedOrder<OptimizationOpportunity>
        AllocationFanoutOrder =
        new(
            RowQueryNamedOrderIdentity.Create(),
            AllocationFanoutOrderKey,
            RowQueryOrderPurpose.Ranking,
            direction => Directional(
                Comparer<OptimizationOpportunity>.Create(
                    CompareAllocationFanout),
                direction));

    private static readonly RowQueryVocabulary<OptimizationOpportunity>
        Vocabulary =
        RowQueryVocabulary<OptimizationOpportunity>.Create(
            RowQueryVocabularyIdentity.Create(),
            CreateKeys(),
            [TriageOrder, AllocationFanoutOrder],
            defaultTopRanking:
                new(
                    TriageOrder,
                    RowQueryOrderDirection.Descending));

    private static readonly QuerySpaceRowScopeBinding<
        OptimizationOpportunity> Scope =
            new(
                new QuerySpaceRowScopeDescriptor(
                    RowScopeIdentity,
                    RowVocabularyIdentity,
                    [RowSet],
                    [
                        .. Vocabulary.Keys.Select(CreateFacet),
                    ],
                    [
                        new(TriageOrderKey, ranking: true),
                        new(AllocationFanoutOrderKey, ranking: true),
                    ],
                    [RowSelectionStageKind.Top]),
                Vocabulary);

    public static OptimizationOpportunityCuratedQuery PerformanceTriage
    { get; } =
        new(
            "performance-triage",
            PortableQueryIntent.Empty);

    public static OptimizationOpportunityCuratedQuery Boxing { get; } =
        Curated("performance-boxing", OptimizationOpportunityKind.Boxing);

    public static OptimizationOpportunityCuratedQuery Arrays { get; } =
        Curated("performance-arrays", OptimizationOpportunityKind.Arrays);

    public static OptimizationOpportunityCuratedQuery ClosuresAndDelegates
    { get; } =
        Curated(
            "performance-closures-and-delegates",
            OptimizationOpportunityKind.ClosuresAndDelegates);

    public static OptimizationOpportunityCuratedQuery Enumerators { get; } =
        Curated(
            "performance-enumerators",
            OptimizationOpportunityKind.Enumerators);

    public static OptimizationOpportunityCuratedQuery Strings { get; } =
        Curated("performance-strings", OptimizationOpportunityKind.Strings);

    public static OptimizationOpportunityCuratedQuery LoopHotPaths { get; } =
        Curated(
            "performance-loop-hot-paths",
            OptimizationOpportunityKind.LoopHotPaths);

    public static OptimizationOpportunityCuratedQuery AllocationHotspots
    { get; } =
        Curated(
            "performance-allocation-hotspots",
            OptimizationOpportunityKind.AllocationHotspots);

    public static OptimizationOpportunityCuratedQuery Async { get; } =
        Curated("performance-async", OptimizationOpportunityKind.Async);

    public static OptimizationOpportunityCuratedQuery Other { get; } =
        Curated("performance-other", OptimizationOpportunityKind.Other);

    public static ImmutableArray<OptimizationOpportunityCuratedQuery>
        CuratedQueries { get; } =
        [
            PerformanceTriage,
            Boxing,
            Arrays,
            ClosuresAndDelegates,
            Enumerators,
            Strings,
            LoopHotPaths,
            AllocationHotspots,
            Async,
            Other,
        ];

    public static IReadOnlyList<string> KnownShapes => s_knownShapes;

    public static IReadOnlyList<string> RankedValues => s_rankedValues;

    public static QuerySpaceRowScopeBinding<OptimizationOpportunity>
        RowScope => Scope;

    public static RowQueryKey<OptimizationOpportunity> Key(string key) =>
        Vocabulary.Keys.Single(
            candidate => string.Equals(
                candidate.Key,
                key,
                StringComparison.Ordinal));

    public static RowQueryResolutionResult<OptimizationOpportunity> Resolve(
        OptimizationOpportunityCuratedQuery query,
        PortableQueryIntent intent)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(intent);
        return Scope.Resolve(Compose(query, intent));
    }

    public static RowSelectionResult<OptimizationOpportunity> Apply(
        IEnumerable<OptimizationOpportunity> rows,
        IReadOnlyCollection<string> admittedShapes,
        ResolvedRowQueryPlan<OptimizationOpportunity> plan)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(admittedShapes);
        ArgumentNullException.ThrowIfNull(plan);

        IEnumerable<OptimizationOpportunity> admitted = rows;
        if (admittedShapes.Count > 0)
        {
            var shapes = admittedShapes.ToHashSet(
                StringComparer.OrdinalIgnoreCase);
            admitted = admitted.Where(
                opportunity => shapes.Contains(opportunity.Shape));
        }

        return RowQueryExecutor.Apply(admitted.ToArray(), plan);
    }

    public static ImmutableArray<OptimizationOpportunity> Select(
        OptimizationOpportunityCuratedQuery query,
        IEnumerable<OptimizationOpportunity> rows)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(rows);

        RowQueryResolutionResult<OptimizationOpportunity> resolution =
            Resolve(query, PortableQueryIntent.Empty);
        ResolvedRowQueryPlan<OptimizationOpportunity> plan =
            resolution.Plan
                ?? throw new InvalidOperationException(
                    $"Curated optimization-opportunity query "
                    + $"'{query.Identity}' failed to resolve: "
                    + $"{resolution.Failure!.OperationKind}/"
                    + $"{resolution.Failure.Reason}.");
        RowSelectionResult<OptimizationOpportunity> result =
            Apply(rows, [], plan);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Curated optimization-opportunity query "
                + $"'{query.Identity}' failed to execute.");
        }

        return [.. result.Values];
    }

    public static ImmutableArray<OptimizationOpportunity>
        PerformanceCandidates(
            IEnumerable<OptimizationOpportunity> opportunities,
            IEnumerable<OptimizationOpportunity> allocationFanoutOpportunities,
            IReadOnlySet<TypeRef> generatedFrameworkTypes,
            bool includeAllocationFanout)
    {
        ArgumentNullException.ThrowIfNull(opportunities);
        ArgumentNullException.ThrowIfNull(
            allocationFanoutOpportunities);
        ArgumentNullException.ThrowIfNull(generatedFrameworkTypes);

        IEnumerable<OptimizationOpportunity> candidates =
            includeAllocationFanout
                ? opportunities.Concat(allocationFanoutOpportunities)
                : opportunities;
        return
        [
            .. candidates.Where(
                opportunity =>
                    opportunity.Shape
                        == AnalysisFindings.StringMaterializationShape
                    || OptimizationOpportunityRanking
                        .IncludePerformanceOpportunity(
                            opportunity,
                            generatedFrameworkTypes)),
        ];
    }

    public static OptimizationOpportunityCuratedQuery QueryForKind(
        OptimizationOpportunityKind kind) =>
        kind switch
        {
            OptimizationOpportunityKind.Boxing => Boxing,
            OptimizationOpportunityKind.Arrays => Arrays,
            OptimizationOpportunityKind.ClosuresAndDelegates =>
                ClosuresAndDelegates,
            OptimizationOpportunityKind.Enumerators => Enumerators,
            OptimizationOpportunityKind.Strings => Strings,
            OptimizationOpportunityKind.LoopHotPaths => LoopHotPaths,
            OptimizationOpportunityKind.AllocationHotspots =>
                AllocationHotspots,
            OptimizationOpportunityKind.Async => Async,
            OptimizationOpportunityKind.Other => Other,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    public static OptimizationOpportunityKind KindForShape(string? shape) =>
        shape?.ToLowerInvariant() switch
        {
            "box-value-type"
                or "generic-parameter-object-box" =>
                    OptimizationOpportunityKind.Boxing,
            "small-array"
                or "temporary-byte-array-copy"
                or "span-to-array-copy"
                or "stackalloc-candidate" =>
                    OptimizationOpportunityKind.Arrays,
            "cache-lookup-factory-delegate"
                or "capturing-delegate"
                or "instance-method-group-delegate" =>
                    OptimizationOpportunityKind.ClosuresAndDelegates,
            "enumerator-allocation" =>
                OptimizationOpportunityKind.Enumerators,
            "string-materialization" =>
                OptimizationOpportunityKind.Strings,
            "linq-scan-in-loop"
                or "materialize-in-loop"
                or "scan-method-in-loop-call"
                or "scan-method-in-recursive-traversal"
                or "string-build-in-loop" =>
                    OptimizationOpportunityKind.LoopHotPaths,
            "allocation-hotspot"
                or "allocation-fanout" =>
                    OptimizationOpportunityKind.AllocationHotspots,
            "async-state-machine"
                or "sync-call-in-async" =>
                    OptimizationOpportunityKind.Async,
            _ => OptimizationOpportunityKind.Other,
        };

    public static string? ProvenanceText(
        PerformanceTriageProvenance provenance) =>
        provenance switch
        {
            PerformanceTriageProvenance.Exact => "exact",
            PerformanceTriageProvenance.Aggregate => "aggregate",
            PerformanceTriageProvenance.Unmatched => "unmatched",
            _ => null,
        };

    public static string? CallerLoopText(CallerLoopEvidence? evidence) =>
        evidence is null
            ? null
            : evidence.Depth == 1
                ? "direct"
                : "transitive";

    public static string? CallerLoopWitnessText(
        CallerLoopEvidence? evidence)
    {
        if (evidence is null || evidence.Witness.IsDefaultOrEmpty)
            return null;

        IEnumerable<string> calls = evidence.Witness.Select(
            step => $"{MethodText(step.Caller)} @ IL_{step.ILOffset:X4}");
        return $"{string.Join(" -> ", calls)} -> "
            + MethodText(evidence.Witness[^1].Callee);
    }

    private static OptimizationOpportunityCuratedQuery Curated(
        string identity,
        OptimizationOpportunityKind kind) =>
        new(
            identity,
            PortableQueryIntent.Create(
                [
                    new(
                        KindKey,
                        PortableQueryOperator.Equal,
                        KindToken(kind)),
                ],
                [],
                [],
                []));

    private static PortableQueryIntent Compose(
        OptimizationOpportunityCuratedQuery query,
        PortableQueryIntent intent)
    {
        if (query.Intent.Terms.Count == 0)
            return intent;

        return PortableQueryIntent.Create(
            [
                .. query.Intent.Terms,
                .. intent.Terms,
            ],
            intent.Bounds,
            intent.Stages,
            intent.Order);
    }

    private static RowQueryKey<OptimizationOpportunity>[] CreateKeys() =>
    [
        MemberKey(),
        TextKey("Candidate", row => row.CandidateId, ordered: true),
        TextKey("Finding", row => row.SourceFinding, ordered: true),
        TextKey(
            "Provenance",
            row => ProvenanceText(row.Provenance),
            ordered: true),
        NumericKey(
            "RootReach",
            row => row.RootReach,
            missingLast: false),
        TextKey("Shape", row => row.Shape, ordered: true),
        KindQueryKey(),
        TextKey("Operation", row => row.Operation, ordered: true),
        TokenKey(),
        TextKey(
            "EvidenceMethod",
            row => FormatToken(row.EvidenceMethodToken),
            ordered: true),
        TextKey("Evidence", row => row.Evidence, ordered: false),
        TextKey("Fix", row => row.SafeFixDirection, ordered: false),
        RankedKey(
            "Priority",
            row => (int)OptimizationOpportunityRanking.Priority(row),
            missing: false),
        RankedKey(
            "Confidence",
            row => Rank(row.Confidence),
            missing: false),
        TextKey(
            "Loop",
            row => OptimizationOpportunityRanking.IteratesInLoop(row)
                ? "loop"
                : "",
            ordered: true),
        TextKey(
            "CallerLoop",
            row => CallerLoopText(row.CallerLoop),
            ordered: true),
        NumericKey(
            "CallerLoopDepth",
            row => row.CallerLoop?.Depth,
            missingLast: true),
        TextKey(
            "CallerLoopWitness",
            row => CallerLoopWitnessText(row.CallerLoop),
            ordered: true),
        TextKey(
            "Allocation",
            row => row.RuntimeAllocationType,
            ordered: true),
        TextKey("Path", row => row.PathContext, ordered: true),
        TextKey(
            "PathConfidence",
            row => row.PathConfidence,
            ordered: true),
        TextKey(
            "PostDominance",
            row => row.PostDominance,
            ordered: true),
        IlKey(),
        RankedKey(
            "Weight",
            row => row.Weight is null ? null : Rank(row.Weight),
            missing: true),
        NumericKey(
            "DirectSites",
            row => row.DirectAllocationSites,
            missingLast: false),
        NumericKey(
            "OncePaths",
            row => row.OnceAllocationPaths,
            missingLast: false),
        NumericKey(
            "ConditionalPaths",
            row => row.ConditionalAllocationPaths,
            missingLast: false),
        NumericKey(
            "RepeatedPaths",
            row => row.RepeatedAllocationPaths,
            missingLast: false),
        NumericKey(
            "UnknownPaths",
            row => row.UnknownAllocationPaths,
            missingLast: false),
        NumericKey(
            "CachedSites",
            row => row.CachedAllocationSites,
            missingLast: false),
        NumericKey(
            "OpaquePaths",
            row => row.OpaqueCallPaths,
            missingLast: false),
        TextKey(
            "Saturated",
            row => row.AllocationCountSaturated ? "yes" : null,
            ordered: false),
    ];

    private static RowQueryKey<OptimizationOpportunity> TextKey(
        string key,
        Func<OptimizationOpportunity, string?> accessor,
        bool ordered) =>
        RowQueryText.Key(key, accessor, ordered);

    private static RowQueryKey<OptimizationOpportunity> NumericKey(
        string key,
        Func<OptimizationOpportunity, long?> accessor,
        bool missingLast) =>
        RowQueryKey<OptimizationOpportunity>.Create(
            RowQueryKeyIdentity.Create(),
            key,
            [
                RowQueryOperator.Equals,
                RowQueryOperator.NotEquals,
                RowQueryOperator.GreaterOrEqual,
                RowQueryOperator.LessOrEqual,
            ],
            row => accessor(row) is { } value
                ? RowQueryValue<long>.Present(value)
                : RowQueryValue<long>.Missing,
            BindLong,
            direction => RowQueryValueOrder.Create(
                Comparer<long>.Default,
                direction,
                missingLast
                    || direction is RowQueryOrderDirection.Descending));

    private static RowQueryKey<OptimizationOpportunity> RankedKey(
        string key,
        Func<OptimizationOpportunity, int?> accessor,
        bool missing) =>
        RowQueryKey<OptimizationOpportunity>.Create(
            RowQueryKeyIdentity.Create(),
            key,
            [
                RowQueryOperator.Equals,
                RowQueryOperator.NotEquals,
                RowQueryOperator.GreaterOrEqual,
                RowQueryOperator.LessOrEqual,
            ],
            row => accessor(row) is { } value
                ? RowQueryValue<int>.Present(value)
                : RowQueryValue<int>.Missing,
            BindRank,
            direction => RowQueryValueOrder.Create(
                Comparer<int>.Default,
                direction,
                missing
                    && direction is RowQueryOrderDirection.Descending));

    private static RowQueryKey<OptimizationOpportunity> MemberKey() =>
        RowQueryKey<OptimizationOpportunity>.Create(
            RowQueryKeyIdentity.Create(),
            "Member",
            [RowQueryOperator.Equals, RowQueryOperator.NotEquals],
            row => RowQueryValue<MemberValue>.Present(
                new(
                    MethodText(row.Method),
                    ShortMethodText(row.Method))),
            (operation, token) =>
            {
                bool Match(MemberValue value) =>
                    RowQueryText.Matches(value.Full, token.Text)
                    || RowQueryText.Matches(value.Short, token.Text);
                return operation switch
                {
                    RowQueryOperator.Equals => Match,
                    RowQueryOperator.NotEquals => value => !Match(value),
                    _ => null,
                };
            },
            direction => RowQueryValueOrder.Create(
                MemberValueComparer.Instance,
                direction,
                missingLast: false));

    private static RowQueryKey<OptimizationOpportunity> KindQueryKey() =>
        RowQueryKey<OptimizationOpportunity>.Create(
            RowQueryKeyIdentity.Create(),
            KindKey,
            [RowQueryOperator.Equals, RowQueryOperator.NotEquals],
            row => RowQueryValue<OptimizationOpportunityKind>.Present(
                KindForShape(row.Shape)),
            BindKind,
            orderComparerFactory: null);

    private static RowQueryKey<OptimizationOpportunity> IlKey() =>
        RowQueryKey<OptimizationOpportunity>.Create(
            RowQueryKeyIdentity.Create(),
            "IL",
            [RowQueryOperator.Equals, RowQueryOperator.NotEquals],
            row => RowQueryValue<IlValue>.Present(
                new(
                    row.ILOffset is { } offset
                        ? $"IL_{offset:X4}"
                        : "",
                    row.ILOffset ?? -1)),
            (operation, token) =>
            {
                bool Match(IlValue value) =>
                    RowQueryText.Matches(value.Text, token.Text);
                return operation switch
                {
                    RowQueryOperator.Equals => Match,
                    RowQueryOperator.NotEquals => value => !Match(value),
                    _ => null,
                };
            },
            direction => RowQueryValueOrder.Create(
                IlValueComparer.Instance,
                direction,
                missingLast: false));

    private static RowQueryKey<OptimizationOpportunity> TokenKey() =>
        RowQueryKey<OptimizationOpportunity>.Create(
            RowQueryKeyIdentity.Create(),
            "Token",
            [RowQueryOperator.Equals, RowQueryOperator.NotEquals],
            row => RowQueryValue<TokenValue>.Present(
                new(
                    FormatToken(row.OperandToken) ?? "",
                    row.OperandToken)),
            (operation, token) =>
            {
                bool Match(TokenValue value)
                {
                    if (!token.Text.Contains('*')
                        && !token.Text.Contains('?')
                        && TryParseMetadataToken(
                            token.Text,
                            out int expected))
                    {
                        return value.Token == expected;
                    }

                    return RowQueryText.Matches(value.Text, token.Text);
                }

                return operation switch
                {
                    RowQueryOperator.Equals => Match,
                    RowQueryOperator.NotEquals => value => !Match(value),
                    _ => null,
                };
            },
            direction => RowQueryValueOrder.Create(
                TokenValueComparer.Instance,
                direction,
                missingLast: false));

    private static Predicate<long>? BindLong(
        RowQueryOperator operation,
        RowQueryValueToken token)
    {
        if (!long.TryParse(
                token.Text,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out long expected))
        {
            return null;
        }

        return operation switch
        {
            RowQueryOperator.Equals => value => value == expected,
            RowQueryOperator.NotEquals => value => value != expected,
            RowQueryOperator.GreaterOrEqual => value => value >= expected,
            RowQueryOperator.LessOrEqual => value => value <= expected,
            _ => null,
        };
    }

    private static Predicate<int>? BindRank(
        RowQueryOperator operation,
        RowQueryValueToken token)
    {
        int expected = Rank(token.Text);
        if (expected < 0)
            return null;
        return operation switch
        {
            RowQueryOperator.Equals => value => value == expected,
            RowQueryOperator.NotEquals => value => value != expected,
            RowQueryOperator.GreaterOrEqual => value => value >= expected,
            RowQueryOperator.LessOrEqual => value => value <= expected,
            _ => null,
        };
    }

    private static Predicate<OptimizationOpportunityKind>? BindKind(
        RowQueryOperator operation,
        RowQueryValueToken token)
    {
        if (!TryParseKind(token.Text, out OptimizationOpportunityKind kind))
            return null;
        return operation switch
        {
            RowQueryOperator.Equals => value => value == kind,
            RowQueryOperator.NotEquals => value => value != kind,
            _ => null,
        };
    }

    private static int Rank(string value)
    {
        for (int index = 0; index < s_rankedValues.Length; index++)
        {
            if (value.Equals(
                    s_rankedValues[index],
                    StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static int CompareAllocationFanout(
        OptimizationOpportunity left,
        OptimizationOpportunity right)
    {
        int comparison = (right.OnceAllocationPaths ?? -1)
            .CompareTo(left.OnceAllocationPaths ?? -1);
        if (comparison != 0)
            return comparison;
        comparison = (right.RepeatedAllocationPaths ?? -1)
            .CompareTo(left.RepeatedAllocationPaths ?? -1);
        if (comparison != 0)
            return comparison;
        return (right.ConditionalAllocationPaths ?? -1)
            .CompareTo(left.ConditionalAllocationPaths ?? -1);
    }

    private static IComparer<T> Directional<T>(
        IComparer<T> descendingComparer,
        RowQueryOrderDirection direction) =>
        direction is RowQueryOrderDirection.Descending
            ? descendingComparer
            : Comparer<T>.Create(
                (left, right) =>
                    descendingComparer.Compare(right, left));

    private static QuerySpaceRowFacetDescriptor CreateFacet(
        RowQueryKey<OptimizationOpportunity> key) =>
        new(
            $"analysis.optimization-opportunities.{Kebab(key.Key)}",
            key.Key,
            [.. key.Operators.Select(ToPortableOperator)],
            ValueKind(key.Key),
            valueVocabulary: null,
            key.Key,
            Values(key.Key),
            $"Optimization opportunity {key.Key} facet.",
            key.SupportsOrdering);

    private static PortableQueryOperator ToPortableOperator(
        RowQueryOperator operation) =>
        operation switch
        {
            RowQueryOperator.Equals => PortableQueryOperator.Equal,
            RowQueryOperator.NotEquals => PortableQueryOperator.NotEqual,
            RowQueryOperator.GreaterOrEqual => PortableQueryOperator.AtLeast,
            RowQueryOperator.LessOrEqual => PortableQueryOperator.AtMost,
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

    private static string ValueKind(string key) =>
        key switch
        {
            "RootReach"
                or "CallerLoopDepth"
                or "DirectSites"
                or "OncePaths"
                or "ConditionalPaths"
                or "RepeatedPaths"
                or "UnknownPaths"
                or "CachedSites"
                or "OpaquePaths" => "integer",
            "Priority"
                or "Confidence"
                or "Weight" => "rank",
            KindKey => "kind",
            _ => "text",
        };

    private static IReadOnlyList<string> Values(string key) =>
        key switch
        {
            "Priority"
                or "Confidence"
                or "Weight" => s_rankedValues,
            KindKey =>
            [
                .. Enum.GetValues<OptimizationOpportunityKind>()
                    .Select(KindToken),
            ],
            _ => [],
        };

    private static string KindToken(OptimizationOpportunityKind kind) =>
        kind switch
        {
            OptimizationOpportunityKind.Boxing => "boxing",
            OptimizationOpportunityKind.Arrays => "arrays",
            OptimizationOpportunityKind.ClosuresAndDelegates =>
                "closures-and-delegates",
            OptimizationOpportunityKind.Enumerators => "enumerators",
            OptimizationOpportunityKind.Strings => "strings",
            OptimizationOpportunityKind.LoopHotPaths => "loop-hot-paths",
            OptimizationOpportunityKind.AllocationHotspots =>
                "allocation-hotspots",
            OptimizationOpportunityKind.Async => "async",
            OptimizationOpportunityKind.Other => "other",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static bool TryParseKind(
        string value,
        out OptimizationOpportunityKind kind)
    {
        foreach (OptimizationOpportunityKind candidate
            in Enum.GetValues<OptimizationOpportunityKind>())
        {
            if (value.Equals(
                    KindToken(candidate),
                    StringComparison.OrdinalIgnoreCase))
            {
                kind = candidate;
                return true;
            }
        }

        kind = default;
        return false;
    }

    private static string Kebab(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length + 4);
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (char.IsUpper(character)
                && index > 0
                && (char.IsLower(value[index - 1])
                    || char.IsDigit(value[index - 1])))
            {
                builder.Append('-');
            }
            builder.Append(char.ToLowerInvariant(character));
        }
        return builder.ToString();
    }

    private static string? FormatToken(int? token) =>
        token is { } value ? $"0x{value:X8}" : null;

    private static bool TryParseMetadataToken(
        string value,
        out int token)
    {
        token = default;
        value = value.Trim();
        return value.StartsWith(
                "0x",
                StringComparison.OrdinalIgnoreCase)
            && int.TryParse(
                value.AsSpan(2),
                System.Globalization.NumberStyles.AllowHexSpecifier,
                System.Globalization.CultureInfo.InvariantCulture,
                out token);
    }

    private static string MethodText(MethodIdentity method) =>
        $"{method.DeclaringType.ToQualifiedDisplayString()}.{method.Name}"
        + $"({string.Join(", ", method.ParameterTypes.Select(
            parameter => parameter.ToQualifiedDisplayString()))})";

    private static string ShortMethodText(MethodIdentity method) =>
        $"{method.Name}({string.Join(", ", method.ParameterTypes.Select(
            parameter => parameter.ToQualifiedDisplayString()))})";

    private readonly record struct MemberValue(
        string Full,
        string Short);

    private sealed class MemberValueComparer : IComparer<MemberValue>
    {
        internal static MemberValueComparer Instance { get; } = new();

        public int Compare(MemberValue left, MemberValue right) =>
            StringComparer.OrdinalIgnoreCase.Compare(
                left.Full,
                right.Full);
    }

    private readonly record struct IlValue(string Text, int Offset);

    private sealed class IlValueComparer : IComparer<IlValue>
    {
        internal static IlValueComparer Instance { get; } = new();

        public int Compare(IlValue left, IlValue right) =>
            left.Offset.CompareTo(right.Offset);
    }

    private readonly record struct TokenValue(
        string Text,
        int? Token);

    private sealed class TokenValueComparer : IComparer<TokenValue>
    {
        internal static TokenValueComparer Instance { get; } = new();

        public int Compare(TokenValue left, TokenValue right) =>
            (left.Token ?? -1).CompareTo(right.Token ?? -1);
    }
}
