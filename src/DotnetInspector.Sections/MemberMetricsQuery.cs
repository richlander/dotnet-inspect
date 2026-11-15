using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using QuerySpace;
using QuerySpace.Composition;
using QuerySpace.Operations;
using QuerySpace.Rows;

namespace DotnetInspector.Sections;

internal sealed record MemberMetricsEvaluationRow(
    MemberOverloadShape Member,
    MemberMetricEvidenceState BodySizeState,
    MemberMetricEvidenceState SiblingCallsState,
    int? LargestPhysicalIlBytes,
    int? IncomingSiblingCallers,
    int? OutgoingSiblingTargets,
    MemberBodySizeMetrics? BodySize,
    MemberSiblingCallMetrics? SiblingCalls,
    MemberSiblingRelationships? Relationships);

public enum MemberMetricsQueryRejection
{
    QuerySpaceMismatch,
    ParticipatingRowSetsMismatch,
    RowIntentMismatch,
    TerminalMismatch,
    ResultContractMismatch,
    OperationIntentRejected,
    RowIntentRejected,
    ProjectionNotSupportedForCount,
}

public abstract record MemberMetricsQueryRequestResult
{
    private protected MemberMetricsQueryRequestResult()
    {
    }

    public sealed record Accepted : MemberMetricsQueryRequestResult
    {
        internal Accepted(
            QuerySpaceTerminalRequirement terminal,
            ImmutableArray<MemberMetricField> projectedFields,
            MemberMetricEvidenceKind requestedEvidence,
            ResolvedRowQueryPlan<MemberMetricsEvaluationRow> rows,
            ResolvedRowQueryPlan<MemberMetricsEvaluationRow>
                sourcePredicates)
        {
            Terminal = terminal;
            ProjectedFields = projectedFields;
            RequestedEvidence = requestedEvidence;
            Rows = rows;
            SourcePredicates = sourcePredicates;
        }

        public QuerySpaceTerminalRequirement Terminal { get; }

        public ImmutableArray<MemberMetricField> ProjectedFields { get; }

        public MemberMetricEvidenceKind RequestedEvidence { get; }

        internal ResolvedRowQueryPlan<MemberMetricsEvaluationRow> Rows
        { get; }

        internal ResolvedRowQueryPlan<MemberMetricsEvaluationRow>
            SourcePredicates
        { get; }
    }

    public sealed record Rejected(
        MemberMetricsQueryRejection Reason,
        string Detail)
        : MemberMetricsQueryRequestResult;
}

public static class MemberMetricsQuery
{
    public const string OperationIdentity = "member-metrics";
    public const string OperationRouteIdentity = "member-metrics/default";
    public const string SubjectRole = "member-overload-population";
    public const string ResultGrain = "member-metric";
    public const string RowSet = "member-metrics";
    public const string OperationProfileIdentity = "default";
    public const string VocabularyIdentity = "member-metrics/v1";
    public const string QuerySpaceIdentity = "member-metrics/query-space/v1";
    public const string RowScopeIdentity = "member-metrics/rows";
    public const string RowVocabularyIdentity = "member-metrics/rows/v1";
    public const string RowsResultContract = "member-metrics/rows-result/v1";
    public const string CountResultContract = "member-metrics/count-result/v1";

    public const string FieldTermKey = "field";
    public const string BaselineOrdinalKey = "baseline-ordinal";
    public const string ReceiverKey = "receiver";
    public const string BodySizeStateKey = "body-size-state";
    public const string SiblingCallsStateKey = "sibling-calls-state";
    public const string LargestPhysicalIlBytesKey =
        "largest-physical-il-bytes";
    public const string IncomingSiblingCallersKey =
        "incoming-sibling-callers";
    public const string OutgoingSiblingTargetsKey =
        "outgoing-sibling-targets";

    public const string BaselineOrder = "baseline";
    public const string LargestPhysicalIlBytesOrder =
        "largest-physical-il-bytes";
    public const string IncomingSiblingCallersOrder =
        "incoming-sibling-callers";
    public const string OutgoingSiblingTargetsOrder =
        "outgoing-sibling-targets";

    private const string FieldTermIdentity = "member-metrics.term.field";

    private static readonly RowQueryKeyIdentity s_baselineOrdinalIdentity =
        RowQueryKeyIdentity.Create();
    private static readonly RowQueryKeyIdentity s_receiverIdentity =
        RowQueryKeyIdentity.Create();
    private static readonly RowQueryKeyIdentity s_bodySizeStateIdentity =
        RowQueryKeyIdentity.Create();
    private static readonly RowQueryKeyIdentity s_siblingCallsStateIdentity =
        RowQueryKeyIdentity.Create();
    private static readonly RowQueryKeyIdentity s_largestBodyIdentity =
        RowQueryKeyIdentity.Create();
    private static readonly RowQueryKeyIdentity s_incomingIdentity =
        RowQueryKeyIdentity.Create();
    private static readonly RowQueryKeyIdentity s_outgoingIdentity =
        RowQueryKeyIdentity.Create();

    private static readonly RowQueryNamedOrderIdentity
        s_baselineOrderIdentity =
            RowQueryNamedOrderIdentity.Create();
    private static readonly RowQueryNamedOrderIdentity
        s_largestBodyOrderIdentity =
            RowQueryNamedOrderIdentity.Create();
    private static readonly RowQueryNamedOrderIdentity
        s_incomingOrderIdentity =
            RowQueryNamedOrderIdentity.Create();
    private static readonly RowQueryNamedOrderIdentity
        s_outgoingOrderIdentity =
            RowQueryNamedOrderIdentity.Create();

    private static readonly Lazy<QuerySpaceBinding> s_querySpace =
        new(CreateQuerySpace);

    public static IQueryOperationRoute OperationRoute =>
        OperationRegistration.Route;

    public static QuerySpaceBinding QuerySpace => s_querySpace.Value;

    public static QuerySpaceRequest CreateRequest(
        IEnumerable<MemberMetricField> projectedFields,
        PortableQueryIntent rowIntent,
        QuerySpaceTerminalRequirement terminal)
    {
        ArgumentNullException.ThrowIfNull(projectedFields);
        ArgumentNullException.ThrowIfNull(rowIntent);
        ImmutableArray<MemberMetricField> fields =
            [.. projectedFields.Distinct()];
        foreach (MemberMetricField field in fields)
        {
            if (!Enum.IsDefined(field))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(projectedFields),
                    field,
                    "Unknown Member metric projection field.");
            }
        }

        PortableQueryIntent operationIntent =
            fields.IsEmpty
                ? PortableQueryIntent.Empty
                : PortableQueryIntent.Create(
                    [
                        .. fields.Select(
                            field => new PortableQueryTerm(
                                FieldTermKey,
                                PortableQueryOperator.Equal,
                                FieldText(field))),
                    ],
                    [],
                    [],
                    []);
        return QuerySpaceRequest.Create(
            QuerySpace.Descriptor,
            operationIntent,
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
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!request.QuerySpace.Equals(
                QuerySpaceIdentity,
                StringComparison.Ordinal))
        {
            return Rejected(
                MemberMetricsQueryRejection.QuerySpaceMismatch,
                "The request names another QuerySpace.");
        }
        if (request.ParticipatingRowSets.Count != 1
            || !request.ParticipatingRowSets[0].Equals(
                RowSet,
                StringComparison.Ordinal))
        {
            return Rejected(
                MemberMetricsQueryRejection
                    .ParticipatingRowSetsMismatch,
                "Member Metrics requires its one logical row set.");
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
            return Rejected(
                MemberMetricsQueryRejection.RowIntentMismatch,
                "Member Metrics requires one row intent over its row set.");
        }
        if (request.Terminal
            is not QuerySpaceTerminalRequirement.Rows
                and not QuerySpaceTerminalRequirement.Count)
        {
            return Rejected(
                MemberMetricsQueryRejection.TerminalMismatch,
                "Member Metrics supports Rows and Count.");
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
            return Rejected(
                MemberMetricsQueryRejection.ResultContractMismatch,
                "The request result contract does not match its terminal.");
        }

        PortableQueryResolution<OperationPlan> operation =
            OperationRegistration.Route.Resolve(
                request.Operation,
                cancellationToken);
        if (!operation.IsResolved)
        {
            return Rejected(
                MemberMetricsQueryRejection.OperationIntentRejected,
                $"The metric field selection was rejected: "
                    + $"{operation.Failure.Reason}.");
        }
        OperationPlan operationPlan = operation.Plan!;
        if (request.Terminal is QuerySpaceTerminalRequirement.Count
            && !operationPlan.ProjectedFields.IsEmpty)
        {
            return Rejected(
                MemberMetricsQueryRejection
                    .ProjectionNotSupportedForCount,
                "Count validates metric fields but does not execute cell "
                    + "projection.");
        }

        var rowScope =
            (QuerySpaceRowScopeBinding<MemberMetricsEvaluationRow>)
                QuerySpace.RowScopes.Single();
        RowQueryResolutionResult<MemberMetricsEvaluationRow> rows =
            rowScope.Resolve(request.RowIntents[0].Intent);
        if (!rows.IsSuccess)
        {
            return Rejected(
                MemberMetricsQueryRejection.RowIntentRejected,
                $"The metric row intent was rejected: "
                    + $"{rows.Failure!.Reason}.");
        }
        RowQueryResolutionResult<MemberMetricsEvaluationRow>
            sourcePredicates =
                rowScope.Resolve(
                    SourcePredicateIntent(
                        request.RowIntents[0].Intent));
        if (!sourcePredicates.IsSuccess)
        {
            return Rejected(
                MemberMetricsQueryRejection.RowIntentRejected,
                "The metric source predicates were rejected: "
                    + $"{sourcePredicates.Failure!.Reason}.");
        }

        ResolvedRowQueryPlan<MemberMetricsEvaluationRow> rowPlan =
            rows.Plan!;
        MemberMetricEvidenceKind requested =
            request.Terminal is QuerySpaceTerminalRequirement.Rows
                ? Evidence(operationPlan.ProjectedFields)
                : MemberMetricEvidenceKind.None;
        requested |= PredicateEvidence(rowPlan);
        if (request.Terminal is QuerySpaceTerminalRequirement.Rows)
        {
            requested |= OrderEvidence(rowPlan);
        }

        return new MemberMetricsQueryRequestResult.Accepted(
            request.Terminal,
            operationPlan.ProjectedFields,
            requested,
            rowPlan,
            sourcePredicates.Plan!);
    }

    internal static MemberMetricEvidenceKind Evidence(
        MemberMetricField field) =>
        field switch
        {
            MemberMetricField.LargestPhysicalIlBytes =>
                MemberMetricEvidenceKind.BodySize,
            MemberMetricField.IncomingSiblingCallers
                or MemberMetricField.OutgoingSiblingTargets
                or MemberMetricField.SiblingRelationships =>
                    MemberMetricEvidenceKind.SiblingCalls,
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };

    private static MemberMetricEvidenceKind Evidence(
        IEnumerable<MemberMetricField> fields)
    {
        MemberMetricEvidenceKind evidence =
            MemberMetricEvidenceKind.None;
        foreach (MemberMetricField field in fields)
            evidence |= Evidence(field);
        return evidence;
    }

    private static MemberMetricEvidenceKind PredicateEvidence(
        ResolvedRowQueryPlan<MemberMetricsEvaluationRow> plan)
    {
        MemberMetricEvidenceKind evidence =
            MemberMetricEvidenceKind.None;
        foreach (RowQueryKeyIdentity identity
            in plan.PredicateKeyIdentities)
        {
            evidence |= Evidence(identity);
        }
        return evidence;
    }

    private static MemberMetricEvidenceKind OrderEvidence(
        ResolvedRowQueryPlan<MemberMetricsEvaluationRow> plan)
    {
        MemberMetricEvidenceKind evidence =
            MemberMetricEvidenceKind.None;
        foreach (ResolvedRowQueryOrderBinding order
            in plan.ResolvedOrders)
        {
            foreach (RowQueryKeyIdentity identity
                in order.KeyIdentities)
            {
                evidence |= Evidence(identity);
            }
            if (order.NamedOrderIdentity is { } named)
                evidence |= Evidence(named);
        }
        return evidence;
    }

    private static MemberMetricEvidenceKind Evidence(
        RowQueryKeyIdentity identity)
    {
        if (ReferenceEquals(identity, s_largestBodyIdentity)
            || ReferenceEquals(identity, s_bodySizeStateIdentity))
        {
            return MemberMetricEvidenceKind.BodySize;
        }
        if (ReferenceEquals(identity, s_incomingIdentity)
            || ReferenceEquals(identity, s_outgoingIdentity)
            || ReferenceEquals(
                identity,
                s_siblingCallsStateIdentity))
        {
            return MemberMetricEvidenceKind.SiblingCalls;
        }
        return MemberMetricEvidenceKind.None;
    }

    private static MemberMetricEvidenceKind Evidence(
        RowQueryNamedOrderIdentity identity)
    {
        if (ReferenceEquals(identity, s_largestBodyOrderIdentity))
            return MemberMetricEvidenceKind.BodySize;
        if (ReferenceEquals(identity, s_incomingOrderIdentity)
            || ReferenceEquals(identity, s_outgoingOrderIdentity))
        {
            return MemberMetricEvidenceKind.SiblingCalls;
        }
        return MemberMetricEvidenceKind.None;
    }

    private static PortableQueryIntent SourcePredicateIntent(
        PortableQueryIntent intent)
    {
        PortableQueryTerm[] terms =
        [
            .. intent.Terms.Where(
                static term =>
                    term.Key.Equals(
                        BaselineOrdinalKey,
                        StringComparison.Ordinal)
                    || term.Key.Equals(
                        ReceiverKey,
                        StringComparison.Ordinal)),
        ];
        return terms.Length == 0
            ? PortableQueryIntent.Empty
            : PortableQueryIntent.Create(terms, [], [], []);
    }

    private static MemberMetricsQueryRequestResult.Rejected Rejected(
        MemberMetricsQueryRejection reason,
        string detail) =>
        new(reason, detail);

    private static QuerySpaceBinding CreateQuerySpace()
    {
        RowQueryNamedOrder<MemberMetricsEvaluationRow> baseline =
            NamedOrder(
                s_baselineOrderIdentity,
                BaselineOrder,
                RowQueryOrderPurpose.Sequence,
                static row => RowQueryValue<int>.Present(
                    row.Member.BaselineOrdinal),
                missingLast: false);
        RowQueryNamedOrder<MemberMetricsEvaluationRow> largestBody =
            NamedOrder(
                s_largestBodyOrderIdentity,
                LargestPhysicalIlBytesOrder,
                RowQueryOrderPurpose.Ranking,
                BodySizeValue,
                missingLast: true);
        RowQueryNamedOrder<MemberMetricsEvaluationRow> incoming =
            NamedOrder(
                s_incomingOrderIdentity,
                IncomingSiblingCallersOrder,
                RowQueryOrderPurpose.Ranking,
                IncomingValue,
                missingLast: true);
        RowQueryNamedOrder<MemberMetricsEvaluationRow> outgoing =
            NamedOrder(
                s_outgoingOrderIdentity,
                OutgoingSiblingTargetsOrder,
                RowQueryOrderPurpose.Ranking,
                OutgoingValue,
                missingLast: true);

        var vocabulary =
            RowQueryVocabulary<MemberMetricsEvaluationRow>.Create(
                RowQueryVocabularyIdentity.Create(),
                [
                    IntegerKey(
                        s_baselineOrdinalIdentity,
                        BaselineOrdinalKey,
                        static row => RowQueryValue<int>.Present(
                            row.Member.BaselineOrdinal),
                        missingLast: false),
                    EnumKey(
                        s_receiverIdentity,
                        ReceiverKey,
                        static row =>
                            RowQueryValue<MemberReceiver>.Present(
                                row.Member.Receiver)),
                    EnumKey(
                        s_bodySizeStateIdentity,
                        BodySizeStateKey,
                        static row =>
                            RowQueryValue<MemberMetricEvidenceState>
                                .Present(row.BodySizeState)),
                    EnumKey(
                        s_siblingCallsStateIdentity,
                        SiblingCallsStateKey,
                        static row =>
                            RowQueryValue<MemberMetricEvidenceState>
                                .Present(row.SiblingCallsState)),
                    IntegerKey(
                        s_largestBodyIdentity,
                        LargestPhysicalIlBytesKey,
                        BodySizeValue,
                        missingLast: true),
                    IntegerKey(
                        s_incomingIdentity,
                        IncomingSiblingCallersKey,
                        IncomingValue,
                        missingLast: true),
                    IntegerKey(
                        s_outgoingIdentity,
                        OutgoingSiblingTargetsKey,
                        OutgoingValue,
                        missingLast: true),
                ],
                [baseline, largestBody, incoming, outgoing],
                defaultBaselineOrder:
                    new(
                        baseline,
                        RowQueryOrderDirection.Ascending),
                defaultTopRanking:
                    new(
                        largestBody,
                        RowQueryOrderDirection.Descending));
        var rowScope =
            new QuerySpaceRowScopeBinding<
                MemberMetricsEvaluationRow>(
                new(
                    RowScopeIdentity,
                    RowVocabularyIdentity,
                    [RowSet],
                    [
                        IntegerFacet(
                            "baseline-ordinal",
                            BaselineOrdinalKey,
                            "Baseline ordinal",
                            "The exact Member's stable ordinal.",
                            supportsOrdering: true),
                        EnumFacet(
                            "receiver",
                            ReceiverKey,
                            "receiver",
                            "Receiver",
                            "The inherited exact Member receiver form.",
                            supportsOrdering: true),
                        EnumFacet(
                            "body-size-state",
                            BodySizeStateKey,
                            "evidence state",
                            "Body-size state",
                            "The body-size evidence outcome.",
                            supportsOrdering: true),
                        EnumFacet(
                            "sibling-calls-state",
                            SiblingCallsStateKey,
                            "evidence state",
                            "Sibling-call state",
                            "The sibling-call evidence outcome.",
                            supportsOrdering: true),
                        IntegerFacet(
                            "largest-physical-il-bytes",
                            LargestPhysicalIlBytesKey,
                            "Largest physical IL bytes",
                            "The largest encoded IL body attributed to the "
                                + "logical Member.",
                            supportsOrdering: true),
                        IntegerFacet(
                            "incoming-sibling-callers",
                            IncomingSiblingCallersKey,
                            "Incoming sibling callers",
                            "Distinct bound sibling Members that call this "
                                + "Member.",
                            supportsOrdering: true),
                        IntegerFacet(
                            "outgoing-sibling-targets",
                            OutgoingSiblingTargetsKey,
                            "Outgoing sibling targets",
                            "Distinct bound sibling Members called by this "
                                + "Member.",
                            supportsOrdering: true),
                    ],
                    [
                        new(BaselineOrder, ranking: false),
                        new(
                            LargestPhysicalIlBytesOrder,
                            ranking: true),
                        new(
                            IncomingSiblingCallersOrder,
                            ranking: true),
                        new(
                            OutgoingSiblingTargetsOrder,
                            ranking: true),
                    ],
                    [
                        RowSelectionStageKind.Head,
                        RowSelectionStageKind.Tail,
                        RowSelectionStageKind.Window,
                        RowSelectionStageKind.Top,
                    ]),
                vocabulary);
        return QuerySpaceBinding.Create(
            QuerySpaceIdentity,
            OperationRegistration.Route,
            [rowScope],
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
    }

    private static QuerySpaceRowFacetDescriptor IntegerFacet(
        string identity,
        string key,
        string label,
        string summary,
        bool supportsOrdering) =>
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
            supportsOrdering);

    private static QuerySpaceRowFacetDescriptor EnumFacet(
        string identity,
        string key,
        string valueKind,
        string label,
        string summary,
        bool supportsOrdering) =>
        new(
            $"member-metrics.facet.{identity}",
            key,
            [
                PortableQueryOperator.Equal,
                PortableQueryOperator.NotEqual,
            ],
            valueKind,
            valueVocabulary: null,
            label,
            [],
            summary,
            supportsOrdering);

    private static RowQueryValue<int> BodySizeValue(
        MemberMetricsEvaluationRow row) =>
        row.BodySizeState is MemberMetricEvidenceState.Available
            && row.LargestPhysicalIlBytes is { } value
                ? RowQueryValue<int>.Present(value)
                : RowQueryValue<int>.Missing;

    private static RowQueryValue<int> IncomingValue(
        MemberMetricsEvaluationRow row) =>
        row.SiblingCallsState is MemberMetricEvidenceState.Available
            && row.IncomingSiblingCallers is { } value
                ? RowQueryValue<int>.Present(value)
                : RowQueryValue<int>.Missing;

    private static RowQueryValue<int> OutgoingValue(
        MemberMetricsEvaluationRow row) =>
        row.SiblingCallsState is MemberMetricEvidenceState.Available
            && row.OutgoingSiblingTargets is { } value
                ? RowQueryValue<int>.Present(value)
                : RowQueryValue<int>.Missing;

    private static RowQueryKey<MemberMetricsEvaluationRow> IntegerKey(
        RowQueryKeyIdentity identity,
        string key,
        Func<MemberMetricsEvaluationRow, RowQueryValue<int>> accessor,
        bool missingLast) =>
        RowQueryKey<MemberMetricsEvaluationRow>.Create(
            identity,
            key,
            [
                RowQueryOperator.Equals,
                RowQueryOperator.NotEquals,
                RowQueryOperator.GreaterOrEqual,
                RowQueryOperator.LessOrEqual,
            ],
            accessor,
            BindInteger,
            direction => RowQueryValueOrder.Create(
                Comparer<int>.Default,
                direction,
                missingLast));

    private static RowQueryKey<MemberMetricsEvaluationRow> EnumKey<TEnum>(
        RowQueryKeyIdentity identity,
        string key,
        Func<MemberMetricsEvaluationRow, RowQueryValue<TEnum>> accessor)
        where TEnum : struct, Enum =>
        RowQueryKey<MemberMetricsEvaluationRow>.Create(
            identity,
            key,
            [RowQueryOperator.Equals, RowQueryOperator.NotEquals],
            accessor,
            BindEnum<TEnum>,
            direction => RowQueryValueOrder.Create(
                Comparer<TEnum>.Default,
                direction,
                missingLast: false));

    private static RowQueryNamedOrder<MemberMetricsEvaluationRow>
        NamedOrder(
            RowQueryNamedOrderIdentity identity,
            string key,
            RowQueryOrderPurpose purpose,
            Func<MemberMetricsEvaluationRow, RowQueryValue<int>> accessor,
            bool missingLast) =>
        new(
            identity,
            key,
            purpose,
            direction =>
            {
                IComparer<RowQueryValue<int>> comparer =
                    RowQueryValueOrder.Create(
                        Comparer<int>.Default,
                        direction,
                        missingLast);
                return Comparer<MemberMetricsEvaluationRow>.Create(
                    (left, right) =>
                        comparer.Compare(
                            accessor(left),
                            accessor(right)));
            });

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
            RowQueryOperator.Equals => value => value == expected,
            RowQueryOperator.NotEquals => value => value != expected,
            RowQueryOperator.GreaterOrEqual =>
                value => value >= expected,
            RowQueryOperator.LessOrEqual =>
                value => value <= expected,
            _ => null,
        };
    }

    private static Predicate<TEnum>? BindEnum<TEnum>(
        RowQueryOperator operation,
        RowQueryValueToken token)
        where TEnum : struct, Enum
    {
        string normalized = NormalizeEnumText(token.Text);
        TEnum? expected = null;
        foreach (TEnum candidate in Enum.GetValues<TEnum>())
        {
            if (string.Equals(
                    NormalizeEnumText(candidate.ToString()),
                    normalized,
                    StringComparison.OrdinalIgnoreCase))
            {
                expected = candidate;
                break;
            }
        }
        if (expected is null)
        {
            return null;
        }
        return operation switch
        {
            RowQueryOperator.Equals => value =>
                EqualityComparer<TEnum>.Default.Equals(
                    value,
                    expected.Value),
            RowQueryOperator.NotEquals => value =>
                !EqualityComparer<TEnum>.Default.Equals(
                    value,
                    expected.Value),
            _ => null,
        };
    }

    private static string NormalizeEnumText(string value) =>
        value.Replace("-", "", StringComparison.Ordinal)
            .Replace("_", "", StringComparison.Ordinal);

    private static string FieldText(MemberMetricField field) =>
        field switch
        {
            MemberMetricField.LargestPhysicalIlBytes =>
                LargestPhysicalIlBytesKey,
            MemberMetricField.IncomingSiblingCallers =>
                IncomingSiblingCallersKey,
            MemberMetricField.OutgoingSiblingTargets =>
                OutgoingSiblingTargetsKey,
            MemberMetricField.SiblingRelationships =>
                "sibling-relationships",
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };

    private static MemberMetricField? ParseField(string value) =>
        value switch
        {
            LargestPhysicalIlBytesKey =>
                MemberMetricField.LargestPhysicalIlBytes,
            IncomingSiblingCallersKey =>
                MemberMetricField.IncomingSiblingCallers,
            OutgoingSiblingTargetsKey =>
                MemberMetricField.OutgoingSiblingTargets,
            "sibling-relationships" =>
                MemberMetricField.SiblingRelationships,
            _ => null,
        };

    private sealed record OperationPredicate(MemberMetricField Field);

    private sealed class FieldDeclaration
        : PortableQueryKeyDeclaration<OperationPredicate>
    {
        public override string Key => FieldTermKey;

        public override string? Family => FieldTermKey;

        public override PortableQueryFamilyKind FamilyKind =>
            PortableQueryFamilyKind.Combining;

        public override bool AdmitsOperator(
            PortableQueryOperator @operator) =>
            @operator is PortableQueryOperator.Equal;

        public override PortableQueryBinding<OperationPredicate> Bind(
            PortableQueryOperator @operator,
            string value)
        {
            MemberMetricField? field = ParseField(value);
            return @operator is PortableQueryOperator.Equal
                && field is not null
                    ? PortableQueryBinding<OperationPredicate>.Bound(
                        $"{FieldTermKey}:{value}",
                        new(field.Value))
                    : PortableQueryBinding<OperationPredicate>.Rejected;
        }
    }

    private sealed record OperationPlan(
        PortableQueryIntent Intent,
        ImmutableArray<MemberMetricField> ProjectedFields);

    private sealed class OperationVocabulary
        : PortableQueryVocabulary<OperationPredicate, OperationPlan>
    {
        private static readonly FieldDeclaration s_field = new();

        public override string Identity => VocabularyIdentity;

        public override IReadOnlyList<string> RequiredDimensions => [];

        public override bool TryGetKey(
            string key,
            [NotNullWhen(true)]
            out PortableQueryKeyDeclaration<OperationPredicate>?
                declaration)
        {
            declaration = key.Equals(
                FieldTermKey,
                StringComparison.Ordinal)
                    ? s_field
                    : null;
            return declaration is not null;
        }

        public override bool TryGetDimension(
            string dimension,
            [NotNullWhen(true)]
            out PortableQueryDimensionDeclaration<OperationPredicate>?
                declaration)
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
            new(
                PortableQueryIntent.Create(
                    [.. resolved.Terms.Select(term => term.Term)],
                    [.. resolved.Bounds],
                    [.. resolved.Stages],
                    []),
                [
                    .. resolved.Terms
                        .Select(term => term.Predicate.Field)
                        .Distinct(),
                ]);
    }

    private static class OperationRegistration
    {
        private static readonly OperationVocabulary s_vocabulary = new();

        internal static readonly QueryOperationRoute<
            OperationPredicate,
            OperationPlan> Route =
                QueryOperationRoute<
                    OperationPredicate,
                    OperationPlan>.Create(
                    OperationRouteIdentity,
                    CreateDefinition(),
                    SubjectRole,
                    ResultGrain,
                    [RowSet],
                    OperationProfileIdentity,
                    [],
                    []);

        private static QueryOperationDefinition<
            OperationPredicate,
            OperationPlan> CreateDefinition()
        {
            var applicability = new QueryOperationApplicability(
                [SubjectRole],
                [ResultGrain],
                [RowSet]);
            return QueryOperationDefinition<
                OperationPredicate,
                OperationPlan>.Create(
                    OperationIdentity,
                    s_vocabulary,
                    [SubjectRole],
                    [ResultGrain],
                    [RowSet],
                    [
                        new(
                            FieldTermIdentity,
                            FieldTermKey,
                            QueryOperationTermRole.ResultProjection,
                            applicability,
                            new(
                                "Metric field",
                                "metric field",
                                [
                                    LargestPhysicalIlBytesKey,
                                    IncomingSiblingCallersKey,
                                    OutgoingSiblingTargetsKey,
                                    "sibling-relationships",
                                ],
                                "Selects one Member metric result field."),
                            []),
                    ],
                    [],
                    [
                        new(
                            OperationProfileIdentity,
                            [FieldTermIdentity],
                            []),
                    ]);
        }
    }
}
