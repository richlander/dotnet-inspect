using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using ILInspector.Analysis;
using QuerySpace;
using QuerySpace.Composition;
using QuerySpace.Operations;
using QuerySpace.Rows;

namespace DotnetInspector.Sections;

public sealed record MemberMetricsQueryPlan(
    MemberMetricKind ProjectedMetrics,
    MemberMetricKind QueryMetrics,
    MemberMetricKind RequestedMetrics,
    QuerySpaceTerminalRequirement Terminal,
    ResolvedRowQueryPlan<MemberMetricsRow> Rows);

public enum MemberMetricsQueryRequestRejection
{
    QuerySpaceMismatch,
    ParticipatingRowSetsMismatch,
    RowIntentMismatch,
    TerminalMismatch,
    ResultContractMismatch,
    UnauthorizedMetric,
    NoEffectiveMetric,
}

public abstract record MemberMetricsQueryRequestResult
{
    private MemberMetricsQueryRequestResult()
    {
    }

    public sealed record Accepted(MemberMetricsQueryPlan Plan)
        : MemberMetricsQueryRequestResult;

    public sealed record Rejected(
        MemberMetricsQueryRequestRejection Reason)
        : MemberMetricsQueryRequestResult;

    public sealed record IntentRejected(PortableQueryFailure Failure)
        : MemberMetricsQueryRequestResult;

    public sealed record RowIntentRejected(RowQueryFailure Failure)
        : MemberMetricsQueryRequestResult;
}

public static class MemberMetricsQuery
{
    public const string OperationIdentity = "member-metrics";
    public const string OperationRouteIdentity =
        "member-metrics/default";
    public const string SubjectRole = "member-group-population";
    public const string ResultGrain = "exact-member-metric";
    public const string RowSet = "member-metrics";
    public const string OperationProfileIdentity = "default";
    public const string OperationVocabularyIdentity =
        "member-metrics/operation/v1";
    public const string RowScopeIdentity = "member-metrics/rows";
    public const string RowVocabularyIdentity =
        "member-metrics/rows/v1";
    public const string QuerySpaceIdentity =
        "member-metrics/query-space/v1";
    public const string RowsResultContract =
        "member-metrics/rows-result/v1";
    public const string CountResultContract =
        "member-metrics/count-result/v1";
    public const string LargestPhysicalIlBytesKey =
        "largest-physical-il-bytes";
    public const string IncomingSiblingCallersKey =
        "incoming-sibling-callers";
    public const string OutgoingSiblingTargetsKey =
        "outgoing-sibling-targets";
    public const string BaselineOrder = "baseline";
    public const string LargestBodyOrder = "largest-body";

    private static readonly RowQueryKey<MemberMetricsRow>
        s_largestPhysicalIlBytes =
            NumericKey(
                LargestPhysicalIlBytesKey,
                static row => row.LargestPhysicalIlBytes);
    private static readonly RowQueryKey<MemberMetricsRow>
        s_incomingSiblingCallers =
            NumericKey(
                IncomingSiblingCallersKey,
                static row => row.IncomingSiblingCallers);
    private static readonly RowQueryKey<MemberMetricsRow>
        s_outgoingSiblingTargets =
            NumericKey(
                OutgoingSiblingTargetsKey,
                static row => row.OutgoingSiblingTargets);
    private static readonly RowQueryNamedOrder<MemberMetricsRow>
        s_baselineOrder =
            new(
                RowQueryNamedOrderIdentity.Create(),
                BaselineOrder,
                RowQueryOrderPurpose.Sequence,
                direction => Directional(
                    Comparer<MemberMetricsRow>.Create(
                        static (left, right) =>
                            left.Member.BaselineOrdinal.CompareTo(
                                right.Member.BaselineOrdinal)),
                    direction));
    private static readonly RowQueryNamedOrder<MemberMetricsRow>
        s_largestBodyOrder =
            new(
                RowQueryNamedOrderIdentity.Create(),
                LargestBodyOrder,
                RowQueryOrderPurpose.Ranking,
                direction =>
                    Comparer<MemberMetricsRow>.Create(
                        (left, right) =>
                            CompareMetric(
                                left.LargestPhysicalIlBytes,
                                right.LargestPhysicalIlBytes,
                                direction)));
    private static readonly RowQueryVocabulary<MemberMetricsRow>
        s_rowVocabulary =
            RowQueryVocabulary<MemberMetricsRow>.Create(
                RowQueryVocabularyIdentity.Create(),
                [
                    s_largestPhysicalIlBytes,
                    s_incomingSiblingCallers,
                    s_outgoingSiblingTargets,
                ],
                [s_baselineOrder, s_largestBodyOrder],
                defaultTopRanking:
                    new(
                        s_largestBodyOrder,
                        RowQueryOrderDirection.Descending));
    private static readonly Lazy<QuerySpaceBinding> s_querySpace =
        new(CreateQuerySpace);

    public static IQueryOperationRoute OperationRoute =>
        OperationRegistration.Route;

    public static QuerySpaceBinding QuerySpace => s_querySpace.Value;

    public static QuerySpaceRequest CreateRequest(
        RowQueryIntent rows,
        QuerySpaceTerminalRequirement terminal)
    {
        ArgumentNullException.ThrowIfNull(rows);
        PortableQueryIntent rowIntent = Portable(rows);
        return QuerySpaceRequest.Create(
            QuerySpace.Descriptor,
            PortableQueryIntent.Empty,
            [RowSet],
            [
                new(
                    RowScopeIdentity,
                    rowIntent,
                    [RowSet]),
            ],
            terminal);
    }

    public static MemberMetricsQueryRequestResult ResolveRequest(
        QuerySpaceRequest request,
        MemberMetricKind authorizedMetrics,
        MemberMetricKind projectedMetrics,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMetrics(authorizedMetrics, nameof(authorizedMetrics));
        ValidateMetrics(projectedMetrics, nameof(projectedMetrics));
        cancellationToken.ThrowIfCancellationRequested();

        if (!request.QuerySpace.Equals(
                QuerySpaceIdentity,
                StringComparison.Ordinal))
        {
            return new MemberMetricsQueryRequestResult.Rejected(
                MemberMetricsQueryRequestRejection.QuerySpaceMismatch);
        }
        if (request.ParticipatingRowSets.Count != 1
            || !request.ParticipatingRowSets[0].Equals(
                RowSet,
                StringComparison.Ordinal))
        {
            return new MemberMetricsQueryRequestResult.Rejected(
                MemberMetricsQueryRequestRejection
                    .ParticipatingRowSetsMismatch);
        }
        if (request.RowIntents.Count != 1
            || !request.RowIntents[0].Scope.Equals(
                RowScopeIdentity,
                StringComparison.Ordinal)
            || request.RowIntents[0].RowSets.Count != 1
            || !request.RowIntents[0].RowSets[0].Equals(
                RowSet,
                StringComparison.Ordinal))
        {
            return new MemberMetricsQueryRequestResult.Rejected(
                MemberMetricsQueryRequestRejection.RowIntentMismatch);
        }
        if (request.Terminal
            is not QuerySpaceTerminalRequirement.Rows
                and not QuerySpaceTerminalRequirement.Count)
        {
            return new MemberMetricsQueryRequestResult.Rejected(
                MemberMetricsQueryRequestRejection.TerminalMismatch);
        }
        string expectedContract =
            request.Terminal is QuerySpaceTerminalRequirement.Rows
                ? RowsResultContract
                : CountResultContract;
        if (!string.Equals(
                request.ResultContract,
                expectedContract,
                StringComparison.Ordinal))
        {
            return new MemberMetricsQueryRequestResult.Rejected(
                MemberMetricsQueryRequestRejection
                    .ResultContractMismatch);
        }

        PortableQueryResolution<OperationPlan> operation =
            OperationRegistration.Route.Resolve(
                request.Operation,
                cancellationToken);
        if (!operation.IsResolved)
        {
            return new MemberMetricsQueryRequestResult.IntentRejected(
                operation.Failure);
        }

        QuerySpaceRowIntentAssociation association =
            request.RowIntents[0];
        RowQueryResolutionResult<MemberMetricsRow> rows =
            RowScope.Resolve(association.Intent);
        if (!rows.IsSuccess)
        {
            return new MemberMetricsQueryRequestResult
                .RowIntentRejected(rows.Failure!);
        }

        MemberMetricKind queryMetrics =
            Requirements(
                association.Intent,
                request.Terminal);
        MemberMetricKind requested =
            queryMetrics
            | (request.Terminal
                is QuerySpaceTerminalRequirement.Rows
                    ? projectedMetrics
                    : MemberMetricKind.None);
        if ((projectedMetrics & ~authorizedMetrics) != 0
            || (queryMetrics & ~authorizedMetrics) != 0)
        {
            return new MemberMetricsQueryRequestResult.Rejected(
                MemberMetricsQueryRequestRejection
                    .UnauthorizedMetric);
        }
        if (requested == MemberMetricKind.None)
        {
            return new MemberMetricsQueryRequestResult.Rejected(
                MemberMetricsQueryRequestRejection
                    .NoEffectiveMetric);
        }

        return new MemberMetricsQueryRequestResult.Accepted(
            new(
                request.Terminal
                    is QuerySpaceTerminalRequirement.Rows
                        ? projectedMetrics
                        : MemberMetricKind.None,
                queryMetrics,
                requested,
                request.Terminal,
                rows.Plan!));
    }

    internal static void ValidateMetrics(
        MemberMetricKind metrics,
        string parameterName)
    {
        if ((metrics & ~MemberMetricKind.All) != 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                metrics,
                "Unknown Member metric selection.");
        }
    }

    internal static ImplementationMetricKind AnalysisMetrics(
        MemberMetricKind metrics)
    {
        ImplementationMetricKind result =
            ImplementationMetricKind.None;
        if (metrics.HasFlag(MemberMetricKind.BodySize))
            result |= ImplementationMetricKind.BodySize;
        if (metrics.HasFlag(
                MemberMetricKind.SiblingRelationships))
        {
            result |= ImplementationMetricKind
                .SiblingOverloadRelationships;
        }
        return result;
    }

    private static QuerySpaceRowScopeBinding<MemberMetricsRow>
        RowScope { get; } =
        new(
            new(
                RowScopeIdentity,
                RowVocabularyIdentity,
                [RowSet],
                [
                    Facet(
                        "body-size",
                        LargestPhysicalIlBytesKey,
                        "Largest physical IL bytes",
                        "The largest encoded IL body attributed to the exact Member."),
                    Facet(
                        "incoming-sibling-callers",
                        IncomingSiblingCallersKey,
                        "Incoming sibling callers",
                        "The number of distinct sibling overloads that call the exact Member."),
                    Facet(
                        "outgoing-sibling-targets",
                        OutgoingSiblingTargetsKey,
                        "Outgoing sibling targets",
                        "The number of distinct sibling overloads called by the exact Member."),
                ],
                [
                    new(BaselineOrder, ranking: false),
                    new(LargestBodyOrder, ranking: true),
                ],
                [
                    RowSelectionStageKind.Head,
                    RowSelectionStageKind.Tail,
                    RowSelectionStageKind.Window,
                    RowSelectionStageKind.Top,
                ]),
            s_rowVocabulary);

    private static MemberMetricKind Requirements(
        PortableQueryIntent intent,
        QuerySpaceTerminalRequirement terminal)
    {
        MemberMetricKind metrics = MemberMetricKind.None;
        foreach (PortableQueryTerm term in intent.Terms)
            metrics |= Requirement(term.Key);
        foreach (PortableQueryOrderOperation order in intent.Order)
        {
            if (terminal is QuerySpaceTerminalRequirement.Count
                && order.Role.IsBaseline)
            {
                continue;
            }
            if (order.Kind is PortableQueryOrderKind.Named)
            {
                if (order.Reference.Equals(
                        LargestBodyOrder,
                        StringComparison.Ordinal))
                {
                    metrics |= MemberMetricKind.BodySize;
                }
            }
            else
            {
                foreach (PortableQueryOrderTerm term in order.Terms)
                    metrics |= Requirement(term.Key);
            }
        }
        for (int index = 0; index < intent.Stages.Count; index++)
        {
            if (intent.Stages[index].Kind
                    is RowSelectionStageKind.Top
                && !intent.Order.Any(order =>
                    !order.Role.IsBaseline
                    && order.Role.StageIndex == index))
            {
                metrics |= MemberMetricKind.BodySize;
            }
        }
        return metrics;
    }

    private static MemberMetricKind Requirement(string key) =>
        key switch
        {
            LargestPhysicalIlBytesKey =>
                MemberMetricKind.BodySize,
            IncomingSiblingCallersKey
                or OutgoingSiblingTargetsKey =>
                MemberMetricKind.SiblingRelationships,
            _ => MemberMetricKind.None,
        };

    private static QuerySpaceBinding CreateQuerySpace() =>
        QuerySpaceBinding.Create(
            QuerySpaceIdentity,
            OperationRegistration.Route,
            [RowScope],
            [
                QuerySpaceTerminalRequirement.Rows,
                QuerySpaceTerminalRequirement.Count,
            ],
            acceptsContinuation: false,
            [
                new(
                    QuerySpaceTerminalRequirement.Rows,
                    RowsResultContract),
                new(
                    QuerySpaceTerminalRequirement.Count,
                    CountResultContract),
            ]);

    private static QuerySpaceRowFacetDescriptor Facet(
        string identity,
        string key,
        string label,
        string summary) =>
        new(
            $"member-metrics.facet.{identity}",
            key,
            [
                PortableQueryOperator.Equal,
                PortableQueryOperator.NotEqual,
                PortableQueryOperator.AtLeast,
                PortableQueryOperator.AtMost,
            ],
            "integer",
            valueVocabulary: null,
            label,
            [],
            summary,
            supportsOrdering: true);

    private static RowQueryKey<MemberMetricsRow> NumericKey(
        string key,
        Func<MemberMetricsRow, int?> accessor) =>
        RowQueryKey<MemberMetricsRow>.Create(
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
            BindInteger,
            direction => RowQueryValueOrder.Create(
                Comparer<int>.Default,
                direction,
                missingLast: true));

    private static Predicate<int>? BindInteger(
        RowQueryOperator operation,
        RowQueryValueToken token)
    {
        if (!int.TryParse(
                token.Text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int expected))
        {
            return null;
        }

        return operation switch
        {
            RowQueryOperator.Equals =>
                value => value == expected,
            RowQueryOperator.NotEquals =>
                value => value != expected,
            RowQueryOperator.GreaterOrEqual =>
                value => value >= expected,
            RowQueryOperator.LessOrEqual =>
                value => value <= expected,
            _ => null,
        };
    }

    private static IComparer<T> Directional<T>(
        IComparer<T> ascending,
        RowQueryOrderDirection direction) =>
        direction is RowQueryOrderDirection.Ascending
            ? ascending
            : Comparer<T>.Create(
                (left, right) =>
                    ascending.Compare(right, left));

    private static int CompareMetric(
        int? left,
        int? right,
        RowQueryOrderDirection direction)
    {
        if (left is null)
            return right is null ? 0 : 1;
        if (right is null)
            return -1;
        return direction is RowQueryOrderDirection.Ascending
            ? left.Value.CompareTo(right.Value)
            : right.Value.CompareTo(left.Value);
    }

    private static PortableQueryIntent Portable(
        RowQueryIntent rows)
    {
        var terms = rows.Predicates.Select(predicate =>
            new PortableQueryTerm(
                predicate.Key,
                PortableOperator(predicate.Operator),
                predicate.Value.Text));
        var stages =
            rows.Selection.Operations.Select(operation =>
                operation.Kind switch
                {
                    RowSelectionStageKind.Head =>
                        PortableQueryStage.Head(operation.Count),
                    RowSelectionStageKind.Tail =>
                        PortableQueryStage.Tail(operation.Count),
                    RowSelectionStageKind.Window =>
                        PortableQueryStage.Window(
                            operation.Start,
                            operation.End),
                    RowSelectionStageKind.Top =>
                        PortableQueryStage.Top(operation.Count),
                    _ => throw new InvalidOperationException(
                        "Unknown Member metrics row-selection stage."),
                }).ToArray();
        var orders = new List<PortableQueryOrderOperation>();
        if (rows.BaselineOrder is not null)
        {
            orders.Add(
                PortableOrder(
                    PortableQueryOrderRole.Baseline,
                    rows.BaselineOrder));
        }
        for (int index = 0;
             index < rows.Selection.Operations.Count;
             index++)
        {
            RowSelectionIntentOperation<RowQueryOrderIntent>
                operation =
                    rows.Selection.Operations[index];
            if (operation.Kind is RowSelectionStageKind.Top
                && operation.HasRankingOrderOperand)
            {
                orders.Add(
                    PortableOrder(
                        PortableQueryOrderRole.ForStage(index),
                        operation.RankingOrderOperand));
            }
        }

        return PortableQueryIntent.Create(
            [.. terms],
            [],
            stages,
            orders);
    }

    private static PortableQueryOrderOperation PortableOrder(
        PortableQueryOrderRole role,
        RowQueryOrderIntent order) =>
        order.Kind switch
        {
            RowQueryOrderIntentKind.Named =>
                PortableQueryOrderOperation.Named(
                    role,
                    order.NamedOrderKey,
                    PortableDirection(
                        order.NamedOrderDirection)),
            RowQueryOrderIntentKind.Keys =>
                PortableQueryOrderOperation.Fields(
                    role,
                    [
                        .. order.Terms.Select(term =>
                            new PortableQueryOrderTerm(
                                term.Key,
                                PortableDirection(
                                    term.Direction))),
                    ]),
            _ => throw new InvalidOperationException(
                "Unknown Member metrics row order."),
        };

    private static PortableQueryOperator PortableOperator(
        RowQueryOperator @operator) =>
        @operator switch
        {
            RowQueryOperator.Equals =>
                PortableQueryOperator.Equal,
            RowQueryOperator.NotEquals =>
                PortableQueryOperator.NotEqual,
            RowQueryOperator.GreaterOrEqual =>
                PortableQueryOperator.AtLeast,
            RowQueryOperator.LessOrEqual =>
                PortableQueryOperator.AtMost,
            _ => throw new ArgumentOutOfRangeException(
                nameof(@operator)),
        };

    private static PortableQueryDirection PortableDirection(
        RowQueryOrderDirection direction) =>
        direction switch
        {
            RowQueryOrderDirection.Ascending =>
                PortableQueryDirection.Ascending,
            RowQueryOrderDirection.Descending =>
                PortableQueryDirection.Descending,
            _ => throw new ArgumentOutOfRangeException(
                nameof(direction)),
        };

    private sealed record OperationPlan(PortableQueryIntent Intent);

    private readonly record struct OperationPredicate;

    private sealed class OperationVocabulary
        : PortableQueryVocabulary<
            OperationPredicate,
            OperationPlan>
    {
        public override string Identity =>
            OperationVocabularyIdentity;

        public override IReadOnlyList<string> RequiredDimensions => [];

        public override bool TryGetKey(
            string key,
            [NotNullWhen(true)]
            out PortableQueryKeyDeclaration<
                OperationPredicate>? declaration)
        {
            declaration = null;
            return false;
        }

        public override bool TryGetDimension(
            string dimension,
            [NotNullWhen(true)]
            out PortableQueryDimensionDeclaration<
                OperationPredicate>? declaration)
        {
            declaration = null;
            return false;
        }

        public override bool AdmitsStageKind(
            RowSelectionStageKind kind) => false;

        public override bool TryGetNamedOrder(
            string reference,
            out PortableQueryOrderPurpose purpose)
        {
            purpose = default;
            return false;
        }

        public override bool IsOrderable(string key) => false;

        public override bool CollapsesDuplicateBindings => true;

        public override bool AreTermsCompatible(
            PortableQueryResolvedTerm<OperationPredicate> first,
            PortableQueryResolvedTerm<OperationPredicate> second) =>
            true;

        public override OperationPlan CreatePlan(
            PortableQueryResolvedIntent<OperationPredicate> resolved) =>
            new(PortableQueryIntent.Empty);
    }

    private static class OperationRegistration
    {
        private static readonly OperationVocabulary s_vocabulary =
            new();

        internal static readonly QueryOperationDefinition<
            OperationPredicate,
            OperationPlan> Definition =
                QueryOperationDefinition<
                    OperationPredicate,
                    OperationPlan>.Create(
                        OperationIdentity,
                        s_vocabulary,
                        [SubjectRole],
                        [ResultGrain],
                        [RowSet],
                        [],
                        [],
                        [
                            new(
                                OperationProfileIdentity,
                                [],
                                []),
                        ]);

        internal static readonly QueryOperationRoute<
            OperationPredicate,
            OperationPlan> Route =
                QueryOperationRoute<
                    OperationPredicate,
                    OperationPlan>.Create(
                        OperationRouteIdentity,
                        Definition,
                        SubjectRole,
                        ResultGrain,
                        [RowSet],
                        OperationProfileIdentity,
                        [],
                        []);
    }
}
