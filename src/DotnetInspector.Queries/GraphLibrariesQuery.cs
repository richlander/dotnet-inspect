using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using QuerySpace;
using QuerySpace.Operations;
using QuerySpace.Rows;

namespace DotnetInspector.Queries;

/// <summary>One Graph Libraries term owned by the executable vocabulary.</summary>
public sealed record GraphLibrariesQueryTermDescriptor(
    string Key,
    string Label,
    string Summary,
    string ValueKind,
    string ExampleValue);

/// <summary>
/// One Graph Libraries term projected from an effective operation route.
/// </summary>
public sealed record GraphLibrariesQueryRegisteredTerm(
    GraphLibrariesQueryTermDescriptor Descriptor,
    ImmutableArray<PortableQueryOperator> Operators);

/// <summary>One validated Graph Libraries query plan.</summary>
public sealed record GraphLibrariesQueryPlan(
    PortableQueryIntent Intent,
    int? Cluster);

/// <summary>The result of resolving one Graph Libraries intent.</summary>
public abstract record GraphLibrariesQueryPlanResult
{
    private GraphLibrariesQueryPlanResult()
    {
    }

    public sealed record Accepted(GraphLibrariesQueryPlan Plan)
        : GraphLibrariesQueryPlanResult;

    public sealed record Rejected(PortableQueryFailure Failure)
        : GraphLibrariesQueryPlanResult;
}

/// <summary>
/// Portable query registration for the pair-wide Graph Libraries selector.
/// </summary>
public static class GraphLibrariesQuery
{
    public const string OperationIdentity = "graph-libraries";
    public const string OperationRouteIdentity = "graph-libraries/default";
    public const string OperationSubjectRole = "explicit-library-pair";
    public const string OperationResultGrain = "library-pair-direct-use";
    public const string OperationProfileIdentity = "cluster-selection";
    public const string ClusterTermKey = "Cluster";
    public const string ConsumerUseSitesRowSet = "consumer-use-sites";
    public const string ProviderApiTypesRowSet = "provider-api-types";
    public const string DirectUseClustersRowSet = "direct-use-clusters";
    public const string CallSitesRowSet = "call-sites";
    public const string PublicRootPathsRowSet = "public-root-paths";

    private static readonly GraphLibrariesQueryTermDescriptor ClusterTerm =
        new(
            ClusterTermKey,
            "direct-use cluster",
            "Selects one observed pair-wide direct-use cluster ordinal.",
            "positive pair-wide direct-use cluster ordinal (exactly one)",
            "3");

    private static readonly IReadOnlyDictionary<
        string,
        GraphLibrariesQueryTermDescriptor> TermsByKey =
            new Dictionary<string, GraphLibrariesQueryTermDescriptor>(
                StringComparer.Ordinal)
            {
                [ClusterTerm.Key] = ClusterTerm,
            };

    /// <summary>The command-wide Graph Libraries operation route.</summary>
    public static IQueryOperationRoute OperationRoute =>
        OperationRegistration.Route;

    /// <summary>All row sets declared by Graph Libraries.</summary>
    public static ImmutableArray<string> RowSets { get; } =
    [
        ConsumerUseSitesRowSet,
        ProviderApiTypesRowSet,
        DirectUseClustersRowSet,
        CallSitesRowSet,
        PublicRootPathsRowSet,
    ];

    /// <summary>
    /// The terms admitted by the command-wide effective operation route.
    /// </summary>
    public static ImmutableArray<GraphLibrariesQueryRegisteredTerm>
        RegisteredTerms => OperationRegistration.RegisteredTerms;

    /// <summary>The effective route for one operation-backed row set.</summary>
    public static IQueryOperationRoute RouteForRowSet(string rowSet)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rowSet);
        return OperationRegistration.SectionRoutes.TryGetValue(
            rowSet,
            out QueryOperationRoute<
                GraphLibrariesQueryPredicate,
                GraphLibrariesQueryPlan>? route)
            ? route
            : throw new ArgumentOutOfRangeException(
                nameof(rowSet),
                rowSet,
                "Graph Libraries declares no such row set.");
    }

    /// <summary>
    /// The terms admitted by one operation-backed row-set route.
    /// </summary>
    public static ImmutableArray<GraphLibrariesQueryRegisteredTerm>
        RegisteredTermsForRowSet(string rowSet) =>
        ProjectTerms(RouteForRowSet(rowSet));

    /// <summary>Creates the canonical intent for one optional cluster.</summary>
    public static PortableQueryIntent CreateIntent(int? cluster)
    {
        if (cluster is null)
            return PortableQueryIntent.Empty;

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cluster.Value);
        return PortableQueryIntent.Create(
            [
                new(
                    ClusterTermKey,
                    PortableQueryOperator.Equal,
                    cluster.Value.ToString(CultureInfo.InvariantCulture)),
            ],
            [],
            [],
            []);
    }

    /// <summary>Resolves one complete Graph Libraries query intent.</summary>
    public static GraphLibrariesQueryPlanResult ResolveIntent(
        PortableQueryIntent intent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        PortableQueryResolution<GraphLibrariesQueryPlan> resolution =
            OperationRegistration.Route.Resolve(
                intent,
                cancellationToken);
        return resolution.IsResolved
            ? new GraphLibrariesQueryPlanResult.Accepted(resolution.Plan)
            : new GraphLibrariesQueryPlanResult.Rejected(resolution.Failure);
    }

    /// <summary>
    /// Applies one resolved selector to the observed direct-use clusters.
    /// </summary>
    public static AssemblyPairDirectUseClusterProjection? Apply(
        GraphLibrariesQueryPlan plan,
        AssemblyPairDirectUseClusterProjection projection)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(projection);
        return plan.Cluster is int cluster
            ? projection.ScopeToObservedCluster(cluster)
            : projection;
    }

    private static ImmutableArray<GraphLibrariesQueryRegisteredTerm>
        ProjectTerms(IQueryOperationRoute route) =>
    [
        .. route.Capabilities.Terms.Select(capability =>
            new GraphLibrariesQueryRegisteredTerm(
                TermsByKey[capability.Binding.Key],
                [.. capability.Operators])),
    ];

    private sealed record GraphLibrariesQueryPredicate(int Cluster);

    private sealed class GraphLibrariesQueryKeyDeclaration
        : PortableQueryKeyDeclaration<GraphLibrariesQueryPredicate>
    {
        public override string Key => ClusterTermKey;

        public override string? Family => "cluster";

        public override PortableQueryFamilyKind FamilyKind =>
            PortableQueryFamilyKind.Exclusive;

        public override bool AdmitsOperator(
            PortableQueryOperator @operator) =>
            @operator == PortableQueryOperator.Equal;

        public override PortableQueryBinding<GraphLibrariesQueryPredicate>
            Bind(
                PortableQueryOperator @operator,
                string value)
        {
            if (@operator != PortableQueryOperator.Equal
                || !int.TryParse(
                    value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int cluster)
                || cluster <= 0)
            {
                return PortableQueryBinding<
                    GraphLibrariesQueryPredicate>.Rejected;
            }

            return PortableQueryBinding<
                GraphLibrariesQueryPredicate>.Bound(
                    $"cluster:{cluster}",
                    new(cluster));
        }
    }

    private sealed class GraphLibrariesQueryVocabulary
        : PortableQueryVocabulary<
            GraphLibrariesQueryPredicate,
            GraphLibrariesQueryPlan>
    {
        internal const string VocabularyIdentity = "graph-libraries/v1";

        private static readonly GraphLibrariesQueryKeyDeclaration
            ClusterDeclaration = new();

        public override string Identity => VocabularyIdentity;

        public override bool TryGetKey(
            string key,
            [NotNullWhen(true)]
            out PortableQueryKeyDeclaration<
                GraphLibrariesQueryPredicate>? declaration)
        {
            bool found = string.Equals(
                key,
                ClusterTermKey,
                StringComparison.Ordinal);
            declaration = found ? ClusterDeclaration : null;
            return found;
        }

        public override bool TryGetDimension(
            string dimension,
            [NotNullWhen(true)]
            out PortableQueryDimensionDeclaration<
                GraphLibrariesQueryPredicate>? declaration)
        {
            declaration = null;
            return false;
        }

        public override bool AdmitsStageKind(RowSelectionStageKind kind) =>
            false;

        public override bool TryGetNamedOrder(
            string reference,
            out PortableQueryOrderPurpose purpose)
        {
            purpose = default;
            return false;
        }

        public override bool IsOrderable(string key) => false;

        public override GraphLibrariesQueryPlan CreatePlan(
            PortableQueryResolvedIntent<
                GraphLibrariesQueryPredicate> resolved)
        {
            PortableQueryResolvedTerm<GraphLibrariesQueryPredicate>?
                cluster = resolved.Terms.SingleOrDefault();
            return new(
                CreateResolvedIntent(resolved),
                cluster?.Predicate.Cluster);
        }

        private static PortableQueryIntent CreateResolvedIntent(
            PortableQueryResolvedIntent<
                GraphLibrariesQueryPredicate> resolved) =>
            PortableQueryIntent.Create(
                [.. resolved.Terms.Select(term => term.Term)],
                [],
                [],
                []);
    }

    private static class OperationRegistration
    {
        private static readonly GraphLibrariesQueryVocabulary Vocabulary =
            new();

        internal static readonly QueryOperationDefinition<
            GraphLibrariesQueryPredicate,
            GraphLibrariesQueryPlan> Definition =
                CreateDefinition();

        internal static readonly QueryOperationRoute<
            GraphLibrariesQueryPredicate,
            GraphLibrariesQueryPlan> Route =
                CreateRoute(OperationRouteIdentity, RowSets);

        internal static readonly IReadOnlyDictionary<
            string,
            QueryOperationRoute<
                GraphLibrariesQueryPredicate,
                GraphLibrariesQueryPlan>> SectionRoutes =
                    RowSets.ToDictionary(
                        rowSet => rowSet,
                        rowSet => CreateRoute(
                            $"graph-libraries/section/{rowSet}",
                            [rowSet]),
                        StringComparer.Ordinal);

        internal static readonly ImmutableArray<
            GraphLibrariesQueryRegisteredTerm> RegisteredTerms =
                ProjectTerms(Route);

        private static QueryOperationDefinition<
            GraphLibrariesQueryPredicate,
            GraphLibrariesQueryPlan> CreateDefinition()
        {
            var applicability = new QueryOperationApplicability(
                [OperationSubjectRole],
                [OperationResultGrain],
                []);
            var term = new QueryOperationTermBinding(
                "graph-libraries.term.cluster",
                ClusterTermKey,
                QueryOperationTermRole.OperationSelector,
                applicability,
                new QueryOperationTermDescription(
                    ClusterTerm.Label,
                    ClusterTerm.ValueKind,
                    [],
                    ClusterTerm.Summary),
                []);

            return QueryOperationDefinition<
                GraphLibrariesQueryPredicate,
                GraphLibrariesQueryPlan>.Create(
                    OperationIdentity,
                    Vocabulary,
                    [OperationSubjectRole],
                    [OperationResultGrain],
                    RowSets,
                    [term],
                    [],
                    [
                        new(
                            OperationProfileIdentity,
                            [term.Identity],
                            []),
                    ]);
        }

        private static QueryOperationRoute<
            GraphLibrariesQueryPredicate,
            GraphLibrariesQueryPlan> CreateRoute(
                string identity,
                IReadOnlyList<string> rowSets) =>
            QueryOperationRoute<
                GraphLibrariesQueryPredicate,
                GraphLibrariesQueryPlan>.Create(
                    identity,
                    Definition,
                    OperationSubjectRole,
                    OperationResultGrain,
                    rowSets,
                    OperationProfileIdentity,
                    [],
                    []);
    }
}
