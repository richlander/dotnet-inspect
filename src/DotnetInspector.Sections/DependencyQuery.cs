using System.Diagnostics.CodeAnalysis;

using QuerySpace;
using DotnetInspector.QueryOperations;
using QuerySpace.Rows;
using ILInspector.Metadata;

namespace DotnetInspector.Sections;

public enum DependencyQueryRouteKind
{
    TypeRelationships,
    AssetHierarchy,
    PackageHierarchy
}

public sealed record DependencyQueryPlan(
    PortableQueryIntent Intent,
    RowQueryIntent RelationshipRows,
    RowSelectionIntent<string> HierarchyRows,
    int? MaximumDepth);

public abstract record DependencyQueryPlanResult
{
    private DependencyQueryPlanResult()
    {
    }

    public sealed record Accepted(DependencyQueryPlan Plan)
        : DependencyQueryPlanResult;

    public sealed record Rejected(PortableQueryFailure Failure)
        : DependencyQueryPlanResult;
}

/// <summary>
/// Executable query registration for Dependency relationship and hierarchy
/// routes.
/// </summary>
public static class DependencyQuery
{
    public const string OperationIdentity = "dependency";
    public const string TypeRouteIdentity = "dependency/type-relationships";
    public const string AssetRouteIdentity = "dependency/asset-hierarchy";
    public const string PackageRouteIdentity = "dependency/package-hierarchy";
    public const string TypeSubjectRole = "selected-type";
    public const string AssetSubjectRole = "explicit-dependency-root-set";
    public const string PackageSubjectRole = "resolved-package-subject";
    public const string TypeResultGrain = "type-dependency-relationship";
    public const string HierarchyResultGrain =
        "rooted-dependency-hierarchy-occurrence";
    public const string TypeRowSet = "dependency-graph";
    public const string HierarchyRowSet = "dependency-hierarchy";
    public const string TypeProfileIdentity = "type-relationships";
    public const string HierarchyProfileIdentity = "hierarchy";
    public const string DepthDimension = "depth";

    public static IQueryOperationRoute Route(
        DependencyQueryRouteKind kind) =>
        RouteCore(kind);

    public static DependencyQueryPlanResult ResolveIntent(
        DependencyQueryRouteKind kind,
        PortableQueryIntent intent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        PortableQueryResolution<DependencyQueryPlan> resolution =
            RouteCore(kind).Resolve(intent, cancellationToken);
        if (!resolution.IsResolved)
            return new DependencyQueryPlanResult.Rejected(resolution.Failure);

        DependencyQueryPlan plan = resolution.Plan;
        if (kind is DependencyQueryRouteKind.AssetHierarchy
            or DependencyQueryRouteKind.PackageHierarchy)
        {
            plan = plan with
            {
                RelationshipRows = RowQueryIntent.Empty,
                HierarchyRows = HierarchyRowSelection(plan.Intent.Stages),
            };
        }

        return new DependencyQueryPlanResult.Accepted(plan);
    }

    private static QueryOperationRoute<
        RowQueryPredicateIntent,
        DependencyQueryPlan> RouteCore(
        DependencyQueryRouteKind kind) =>
        kind switch
        {
            DependencyQueryRouteKind.TypeRelationships =>
                OperationRegistration.TypeRoute,
            DependencyQueryRouteKind.AssetHierarchy =>
                OperationRegistration.AssetRoute,
            DependencyQueryRouteKind.PackageHierarchy =>
                OperationRegistration.PackageRoute,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private sealed class PredicateDeclaration(
        string key,
        IReadOnlyList<RowQueryOperator> operators)
        : PortableQueryKeyDeclaration<RowQueryPredicateIntent>
    {
        public override string Key { get; } = key;

        public override bool AdmitsOperator(
            PortableQueryOperator @operator) =>
            TryRowOperator(@operator, out RowQueryOperator rowOperator)
            && operators.Contains(rowOperator);

        public override PortableQueryBinding<RowQueryPredicateIntent> Bind(
            PortableQueryOperator @operator,
            string value)
        {
            if (!TryRowOperator(@operator, out RowQueryOperator rowOperator))
            {
                return PortableQueryBinding<
                    RowQueryPredicateIntent>.Rejected;
            }

            var predicate = new RowQueryPredicateIntent(
                Key,
                rowOperator,
                new RowQueryValueToken(value));
            RowQueryResolutionResult<TypeDependencyRelationship> resolution =
                TypeDependencyVocabulary.Resolve(
                    RowQueryIntent.Create(
                        [predicate],
                        baselineOrder: null,
                        RowSelectionIntent<RowQueryOrderIntent>.Empty));
            return resolution.Plan is null
                ? PortableQueryBinding<
                    RowQueryPredicateIntent>.Rejected
                : PortableQueryBinding<
                    RowQueryPredicateIntent>.Bound(
                        $"{Key}:{rowOperator}:{value}",
                        predicate);
        }

    }

    private sealed class DepthDeclaration
        : PortableQueryDimensionDeclaration<RowQueryPredicateIntent>
    {
        public override string Dimension => DepthDimension;

        public override bool Admits(
            int requestedMaximum,
            IReadOnlyList<
                PortableQueryResolvedTerm<RowQueryPredicateIntent>>
                boundTerms) =>
            requestedMaximum > 0;
    }

    private sealed class DependencyQueryVocabulary
        : PortableQueryVocabulary<
            RowQueryPredicateIntent,
            DependencyQueryPlan>
    {
        private static readonly IReadOnlyDictionary<
            string,
            PortableQueryKeyDeclaration<RowQueryPredicateIntent>>
            Keys = TypeDependencyVocabulary.Query.Keys.ToDictionary(
                key => key.Key,
                key => (PortableQueryKeyDeclaration<
                    RowQueryPredicateIntent>)new PredicateDeclaration(
                        key.Key,
                        key.Operators),
                StringComparer.Ordinal);

        private static readonly DepthDeclaration Depth = new();

        public override string Identity => "dependency/v1";

        public override bool TryGetKey(
            string key,
            [NotNullWhen(true)]
            out PortableQueryKeyDeclaration<
                RowQueryPredicateIntent>? declaration) =>
            Keys.TryGetValue(key, out declaration);

        public override bool TryGetDimension(
            string dimension,
            [NotNullWhen(true)]
            out PortableQueryDimensionDeclaration<
                RowQueryPredicateIntent>? declaration)
        {
            bool found = dimension.Equals(
                DepthDimension,
                StringComparison.Ordinal);
            declaration = found ? Depth : null;
            return found;
        }

        public override bool AdmitsStageKind(
            RowSelectionStageKind kind) =>
            kind is RowSelectionStageKind.Head
                or RowSelectionStageKind.Tail
                or RowSelectionStageKind.Window
                or RowSelectionStageKind.Top;

        public override bool TryGetNamedOrder(
            string reference,
            out PortableQueryOrderPurpose purpose)
        {
            bool found = reference.Equals(
                TypeDependencyVocabulary.TraversalOrderKey,
                StringComparison.Ordinal);
            purpose = PortableQueryOrderPurpose.Sequence;
            return found;
        }

        public override bool IsOrderable(string key) =>
            TypeDependencyVocabulary.Query.Keys.Any(candidate =>
                candidate.Key.Equals(key, StringComparison.Ordinal)
                && candidate.SupportsOrdering);

        public override DependencyQueryPlan CreatePlan(
            PortableQueryResolvedIntent<RowQueryPredicateIntent> resolved)
        {
            RowSelectionIntent<RowQueryOrderIntent> selection =
                RowSelectionIntent<RowQueryOrderIntent>.Create(
                [
                    .. resolved.Stages.Select((stage, index) =>
                        ToRowSelectionOperation(
                            stage,
                            resolved.Rankings.SingleOrDefault(
                                ranking =>
                                    ranking.StageIndex == index))),
                ]);
            RowQueryOrderIntent? baseline =
                resolved.Baseline is null
                    ? null
                    : ToRowOrder(resolved.Baseline);
            var relationshipRows = RowQueryIntent.Create(
                [.. resolved.Terms.Select(term => term.Predicate)],
                baseline,
                selection);
            int? maximumDepth = resolved.Bounds.SingleOrDefault(bound =>
                bound.Dimension.Equals(
                    DepthDimension,
                    StringComparison.Ordinal))?.RequestedMaximum;
            var orders = new List<PortableQueryOrderOperation>();
            if (resolved.Baseline is not null)
                orders.Add(resolved.Baseline);
            orders.AddRange(
                resolved.Rankings
                    .Where(ranking => ranking.Operation is not null)
                    .Select(ranking => ranking.Operation!));
            return new(
                PortableQueryIntent.Create(
                    [.. resolved.Terms.Select(term => term.Term)],
                    [.. resolved.Bounds],
                    [.. resolved.Stages],
                    orders),
                relationshipRows,
                RowSelectionIntent<string>.Empty,
                maximumDepth);
        }
    }

    private static RowSelectionIntent<string> HierarchyRowSelection(
        IReadOnlyList<PortableQueryStage> stages) =>
        RowSelectionIntent<string>.Create(
            [
                .. stages.Select(static stage =>
                    stage.Kind switch
                    {
                        RowSelectionStageKind.Head =>
                            RowSelectionIntentOperation<string>.Head(
                                stage.Count),
                        RowSelectionStageKind.Tail =>
                            RowSelectionIntentOperation<string>.Tail(
                                stage.Count),
                        RowSelectionStageKind.Window =>
                            RowSelectionIntentOperation<string>.Window(
                                stage.Start,
                                stage.End),
                        _ => throw new InvalidOperationException(
                            "A Dependency hierarchy route resolved an "
                                + $"unsupported {stage.Kind} row stage."),
                    }),
            ]);

    private static RowSelectionIntentOperation<RowQueryOrderIntent>
        ToRowSelectionOperation(
        PortableQueryStage stage,
        PortableQueryResolvedRanking? ranking) =>
        stage.Kind switch
        {
            RowSelectionStageKind.Head =>
                RowSelectionIntentOperation<RowQueryOrderIntent>.Head(
                    stage.Count),
            RowSelectionStageKind.Tail =>
                RowSelectionIntentOperation<RowQueryOrderIntent>.Tail(
                    stage.Count),
            RowSelectionStageKind.Window =>
                RowSelectionIntentOperation<RowQueryOrderIntent>.Window(
                    stage.Start,
                    stage.End),
            RowSelectionStageKind.Top
                when ranking?.Operation is { } operation =>
                RowSelectionIntentOperation<RowQueryOrderIntent>.Top(
                    stage.Count,
                    ToRowOrder(operation)),
            RowSelectionStageKind.Top =>
                RowSelectionIntentOperation<RowQueryOrderIntent>.Top(
                    stage.Count),
            _ => throw new InvalidOperationException(
                "Dependency Query resolved an unsupported row stage."),
        };

    private static RowQueryOrderIntent ToRowOrder(
        PortableQueryOrderOperation operation) =>
        operation.Kind switch
        {
            PortableQueryOrderKind.Named =>
                RowQueryOrderIntent.Named(
                    operation.Reference,
                    RowDirection(operation.Direction)),
            PortableQueryOrderKind.Fields =>
                RowQueryOrderIntent.Keys(
                    [
                        .. operation.Terms.Select(term =>
                            new RowQueryOrderTermIntent(
                                term.Key,
                                RowDirection(term.Direction))),
                    ]),
            _ => throw new InvalidOperationException(
                "Dependency Query resolved an unsupported order."),
        };

    private static RowQueryOrderDirection RowDirection(
        PortableQueryDirection direction) =>
        direction switch
        {
            PortableQueryDirection.Ascending =>
                RowQueryOrderDirection.Ascending,
            PortableQueryDirection.Descending =>
                RowQueryOrderDirection.Descending,
            _ => throw new ArgumentOutOfRangeException(nameof(direction)),
        };

    private static bool TryRowOperator(
        PortableQueryOperator @operator,
        out RowQueryOperator rowOperator)
    {
        switch (@operator)
        {
            case PortableQueryOperator.Equal:
                rowOperator = RowQueryOperator.Equals;
                return true;
            case PortableQueryOperator.NotEqual:
                rowOperator = RowQueryOperator.NotEquals;
                return true;
            case PortableQueryOperator.AtLeast:
                rowOperator = RowQueryOperator.GreaterOrEqual;
                return true;
            case PortableQueryOperator.AtMost:
                rowOperator = RowQueryOperator.LessOrEqual;
                return true;
            default:
                rowOperator = default;
                return false;
        }
    }

    private static class OperationRegistration
    {
        private static readonly DependencyQueryVocabulary Vocabulary = new();

        internal static readonly QueryOperationDefinition<
            RowQueryPredicateIntent,
            DependencyQueryPlan> Definition =
            CreateDefinition();

        internal static readonly QueryOperationRoute<
            RowQueryPredicateIntent,
            DependencyQueryPlan> TypeRoute =
            QueryOperationRoute<
                RowQueryPredicateIntent,
                DependencyQueryPlan>.Create(
                TypeRouteIdentity,
                Definition,
                TypeSubjectRole,
                TypeResultGrain,
                [TypeRowSet],
                TypeProfileIdentity,
                [DepthDimension],
                [
                    RowSelectionStageKind.Head,
                    RowSelectionStageKind.Tail,
                    RowSelectionStageKind.Window,
                    RowSelectionStageKind.Top,
                ]);

        internal static readonly QueryOperationRoute<
            RowQueryPredicateIntent,
            DependencyQueryPlan> AssetRoute =
            CreateHierarchyRoute(
                AssetRouteIdentity,
                AssetSubjectRole);

        internal static readonly QueryOperationRoute<
            RowQueryPredicateIntent,
            DependencyQueryPlan> PackageRoute =
            CreateHierarchyRoute(
                PackageRouteIdentity,
                PackageSubjectRole);

        private static QueryOperationDefinition<
            RowQueryPredicateIntent,
            DependencyQueryPlan> CreateDefinition()
        {
            var applicability = new QueryOperationApplicability(
                [TypeSubjectRole],
                [TypeResultGrain],
                [TypeRowSet]);
            QueryOperationTermBinding[] terms =
            [
                .. TypeDependencyVocabulary.Query.Keys.Select(key =>
                    new QueryOperationTermBinding(
                        $"dependency.term.{key.Key}",
                        key.Key,
                        QueryOperationTermRole.ResultPredicate,
                        applicability,
                        new QueryOperationTermDescription(
                            key.Key,
                            ValueKind(key.Key),
                            Values(key.Key),
                            $"Filters type dependency relationships by {key.Key}."),
                        [])),
            ];
            QueryOperationOrderBinding[] orders =
            [
                .. TypeDependencyVocabulary.Query.Keys
                    .Where(key => key.SupportsOrdering)
                    .Select(key =>
                        new QueryOperationOrderBinding(
                            $"dependency.order.field.{key.Key}",
                            QueryOperationOrderKind.Field,
                            key.Key,
                            applicability,
                            new QueryOperationOrderDescription(
                                key.Key,
                                $"Orders type dependency relationships by {key.Key}."),
                            [])),
                new(
                    "dependency.order.traversal",
                    QueryOperationOrderKind.Named,
                    TypeDependencyVocabulary.TraversalOrderKey,
                    applicability,
                    new QueryOperationOrderDescription(
                        "Traversal",
                        "Preserves owner-issued dependency traversal order."),
                    []),
            ];

            return QueryOperationDefinition<
                RowQueryPredicateIntent,
                DependencyQueryPlan>.Create(
                OperationIdentity,
                Vocabulary,
                [
                    TypeSubjectRole,
                    AssetSubjectRole,
                    PackageSubjectRole,
                ],
                [TypeResultGrain, HierarchyResultGrain],
                [TypeRowSet, HierarchyRowSet],
                terms,
                orders,
                [
                    new(
                        TypeProfileIdentity,
                        [.. terms.Select(term => term.Identity)],
                        [.. orders.Select(order => order.Identity)]),
                    new(HierarchyProfileIdentity, [], []),
                ]);
        }

        private static QueryOperationRoute<
            RowQueryPredicateIntent,
            DependencyQueryPlan> CreateHierarchyRoute(
            string identity,
            string subjectRole) =>
            QueryOperationRoute<
                RowQueryPredicateIntent,
                DependencyQueryPlan>.Create(
                identity,
                Definition,
                subjectRole,
                HierarchyResultGrain,
                [HierarchyRowSet],
                HierarchyProfileIdentity,
                [DepthDimension],
                [
                    RowSelectionStageKind.Head,
                    RowSelectionStageKind.Tail,
                    RowSelectionStageKind.Window,
                ]);

        private static string ValueKind(string key) =>
            key.Equals(
                TypeDependencyVocabulary.KindKey,
                StringComparison.Ordinal)
                ? "dependency relationship kind"
                : "case-insensitive type-name glob";

        private static string[] Values(string key) =>
            key.Equals(
                TypeDependencyVocabulary.KindKey,
                StringComparison.Ordinal)
                ? Enum.GetNames<TypeDependencyRelationshipKind>()
                : [];
    }
}
