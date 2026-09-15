using DotnetInspector.RowSelection;

namespace DotnetInspector.Sections;

public enum RowQueryOperationKind
{
    Predicate,
    BaselineOrder,
    TopRanking
}

public enum RowQueryFailureReason
{
    UnknownField,
    UnsupportedPredicateOperator,
    InvalidValue,
    UnsupportedFieldOrder,
    UnknownNamedOrder,
    NamedOrderIsNotRanking,
    MissingTopRanking
}

public sealed class RowQueryFailure
{
    internal RowQueryFailure(
        RowQueryOperationKind operationKind,
        int operationPosition,
        int? termPosition,
        int? semanticStageNumber,
        RowQuerySchemaIdentity schemaIdentity,
        RowQueryFieldIdentity? fieldIdentity,
        RowQueryNamedOrderIdentity? namedOrderIdentity,
        RowQueryFailureReason reason)
    {
        OperationKind = operationKind;
        OperationPosition = operationPosition;
        TermPosition = termPosition;
        SemanticStageNumber = semanticStageNumber;
        SchemaIdentity = schemaIdentity;
        FieldIdentity = fieldIdentity;
        NamedOrderIdentity = namedOrderIdentity;
        Reason = reason;
    }

    public RowQueryOperationKind OperationKind { get; }

    public int OperationPosition { get; }

    public int? TermPosition { get; }

    public int? SemanticStageNumber { get; }

    public RowQuerySchemaIdentity SchemaIdentity { get; }

    public RowQueryFieldIdentity? FieldIdentity { get; }

    public RowQueryNamedOrderIdentity? NamedOrderIdentity { get; }

    public RowQueryFailureReason Reason { get; }
}

public sealed class ResolvedRowQueryPlan<TRow>
{
    private readonly IReadOnlyList<Predicate<TRow>> _predicates;
    private readonly Func<IComparer<TRow>>? _baselineComparerFactory;
    private readonly IReadOnlyDictionary<
        ResolvedRowQueryOrderIdentity,
        Func<IComparer<TRow>>> _orderCatalog;

    internal ResolvedRowQueryPlan(
        RowQuerySchemaIdentity schemaIdentity,
        IReadOnlyList<RowQueryFieldIdentity> predicateFieldIdentities,
        IReadOnlyList<Predicate<TRow>> predicates,
        ResolvedRowQueryOrderBinding? baselineOrder,
        IReadOnlyList<ResolvedRowQueryOrderBinding> resolvedOrders,
        Func<IComparer<TRow>>? baselineComparerFactory,
        RowSelectionPlan<ResolvedRowQueryOrderIdentity> selectionPlan,
        IReadOnlyDictionary<
            ResolvedRowQueryOrderIdentity,
            Func<IComparer<TRow>>> orderCatalog)
    {
        SchemaIdentity = schemaIdentity;
        PredicateFieldIdentities = predicateFieldIdentities;
        _predicates = predicates;
        BaselineOrder = baselineOrder;
        ResolvedOrders = resolvedOrders;
        _baselineComparerFactory = baselineComparerFactory;
        SelectionPlan = selectionPlan;
        _orderCatalog = orderCatalog;
    }

    public RowQuerySchemaIdentity SchemaIdentity { get; }

    public IReadOnlyList<RowQueryFieldIdentity>
        PredicateFieldIdentities
    { get; }

    public ResolvedRowQueryOrderBinding? BaselineOrder { get; }

    public IReadOnlyList<ResolvedRowQueryOrderBinding>
        ResolvedOrders
    { get; }

    public RowSelectionPlan<ResolvedRowQueryOrderIdentity>
        SelectionPlan
    { get; }

    internal IReadOnlyList<Predicate<TRow>> Predicates =>
        _predicates;

    internal IComparer<TRow>? CreateBaselineComparer() =>
        _baselineComparerFactory?.Invoke();

    internal IComparer<TRow>? ResolveOrder(
        ResolvedRowQueryOrderIdentity identity) =>
        _orderCatalog.TryGetValue(
            identity,
            out Func<IComparer<TRow>>? comparerFactory)
            ? comparerFactory()
            : null;
}

public sealed class ResolvedRowQueryOrderBinding
{
    internal ResolvedRowQueryOrderBinding(
        ResolvedRowQueryOrderIdentity identity,
        RowQueryNamedOrderIdentity? namedOrderIdentity,
        IReadOnlyList<RowQueryFieldIdentity> fieldIdentities,
        IReadOnlyList<RowQueryOrderDirection> fieldDirections,
        RowQueryOrderDirection? namedOrderDirection)
    {
        Identity = identity;
        NamedOrderIdentity = namedOrderIdentity;
        FieldIdentities = fieldIdentities;
        FieldDirections = fieldDirections;
        NamedOrderDirection = namedOrderDirection;
    }

    public ResolvedRowQueryOrderIdentity Identity { get; }

    public RowQueryNamedOrderIdentity? NamedOrderIdentity { get; }

    public IReadOnlyList<RowQueryFieldIdentity> FieldIdentities { get; }

    public IReadOnlyList<RowQueryOrderDirection> FieldDirections { get; }

    public RowQueryOrderDirection? NamedOrderDirection { get; }
}

public sealed class RowQueryResolutionResult<TRow>
{
    private RowQueryResolutionResult(
        ResolvedRowQueryPlan<TRow>? plan,
        RowQueryFailure? failure)
    {
        Plan = plan;
        Failure = failure;
    }

    public bool IsSuccess => Plan is not null;

    public ResolvedRowQueryPlan<TRow>? Plan { get; }

    public RowQueryFailure? Failure { get; }

    internal static RowQueryResolutionResult<TRow> Success(
        ResolvedRowQueryPlan<TRow> plan) =>
        new(plan, null);

    internal static RowQueryResolutionResult<TRow> Failed(
        RowQueryFailure failure) =>
        new(null, failure);
}

public static class RowQueryResolver
{
    public static RowQueryResolutionResult<TRow> Resolve<TRow>(
        RowQuerySchema<TRow> schema,
        RowQueryIntent intent)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(intent);

        var predicates =
            new Predicate<TRow>[intent.Predicates.Count];
        var predicateIdentities =
            new RowQueryFieldIdentity[intent.Predicates.Count];
        for (int index = 0; index < intent.Predicates.Count; index++)
        {
            RowQueryPredicateIntent predicateIntent =
                intent.Predicates[index];
            if (!schema.TryGetField(
                    predicateIntent.FieldKey,
                    out RowQueryField<TRow>? field))
            {
                return Failed<TRow>(
                    schema,
                    RowQueryOperationKind.Predicate,
                    index + 1,
                    null,
                    null,
                    RowQueryFailureReason.UnknownField);
            }

            if (!field.Operators.Contains(
                    predicateIntent.Operator))
            {
                return Failed<TRow>(
                    schema,
                    RowQueryOperationKind.Predicate,
                    index + 1,
                    null,
                    null,
                    RowQueryFailureReason.UnsupportedPredicateOperator,
                    fieldIdentity: field.Identity);
            }

            Predicate<TRow>? predicate =
                field.BindPredicate(
                    predicateIntent.Operator,
                    predicateIntent.Value);
            if (predicate is null)
            {
                return Failed<TRow>(
                    schema,
                    RowQueryOperationKind.Predicate,
                    index + 1,
                    null,
                    null,
                    RowQueryFailureReason.InvalidValue,
                    fieldIdentity: field.Identity);
            }

            predicates[index] = predicate;
            predicateIdentities[index] = field.Identity;
        }

        var orderCatalog =
            new Dictionary<
                ResolvedRowQueryOrderIdentity,
                Func<IComparer<TRow>>>();
        var resolvedOrders =
            new List<ResolvedRowQueryOrderBinding>();
        ResolvedRowQueryOrderBinding? baselineBinding = null;
        Func<IComparer<TRow>>? baselineComparerFactory = null;
        if (intent.BaselineOrder is not null)
        {
            OrderResolution<TRow> baseline =
                ResolveOrder(
                    schema,
                    intent.BaselineOrder,
                    requireRanking: false,
                    RowQueryOperationKind.BaselineOrder,
                    operationPosition: 1,
                    semanticStageNumber: null);
            if (baseline.Failure is not null)
                return RowQueryResolutionResult<TRow>.Failed(
                    baseline.Failure);

            baselineBinding =
                CreateBinding(baseline);
            baselineComparerFactory =
                baseline.ComparerFactory!;
            resolvedOrders.Add(baselineBinding);
        }
        else if (schema.DefaultBaselineOrder is not null)
        {
            OrderResolution<TRow> baseline =
                ResolveDefaultOrder(
                    schema.DefaultBaselineOrder);
            if (baseline.Failure is not null)
                return RowQueryResolutionResult<TRow>.Failed(
                    baseline.Failure);

            baselineBinding =
                CreateBinding(baseline);
            baselineComparerFactory =
                baseline.ComparerFactory!;
            resolvedOrders.Add(baselineBinding);
        }

        var stages =
            new RowSelectionStage<ResolvedRowQueryOrderIdentity>[
                intent.Selection.Operations.Count];
        int topPosition = 0;
        for (int stageIndex = 0;
             stageIndex < intent.Selection.Operations.Count;
             stageIndex++)
        {
            RowSelectionIntentOperation<RowQueryOrderIntent> operation =
                intent.Selection.Operations[stageIndex];
            switch (operation.Kind)
            {
                case RowSelectionStageKind.Head:
                    stages[stageIndex] =
                        RowSelectionStage<
                            ResolvedRowQueryOrderIdentity>.Head(
                                operation.Count);
                    break;
                case RowSelectionStageKind.Tail:
                    stages[stageIndex] =
                        RowSelectionStage<
                            ResolvedRowQueryOrderIdentity>.Tail(
                                operation.Count);
                    break;
                case RowSelectionStageKind.Window:
                    stages[stageIndex] =
                        RowSelectionStage<
                            ResolvedRowQueryOrderIdentity>.Window(
                                operation.Start,
                                operation.End);
                    break;
                case RowSelectionStageKind.Top:
                    {
                        topPosition++;
                        OrderResolution<TRow> ranking;
                        if (operation.HasRankingOrderOperand)
                        {
                            ranking =
                                ResolveOrder(
                                    schema,
                                    operation.RankingOrderOperand,
                                    requireRanking: true,
                                    RowQueryOperationKind.TopRanking,
                                    topPosition,
                                    stageIndex + 1);
                        }
                        else if (schema.DefaultTopRanking is not null)
                        {
                            ranking =
                                ResolveDefaultOrder(
                                    schema.DefaultTopRanking);
                        }
                        else
                        {
                            return Failed<TRow>(
                                schema,
                                RowQueryOperationKind.TopRanking,
                                topPosition,
                                null,
                                stageIndex + 1,
                                RowQueryFailureReason.MissingTopRanking);
                        }

                        if (ranking.Failure is not null)
                        {
                            return RowQueryResolutionResult<TRow>.Failed(
                                ranking.Failure);
                        }

                        ResolvedRowQueryOrderBinding rankingBinding =
                            CreateBinding(ranking);
                        orderCatalog.Add(
                            rankingBinding.Identity,
                            ranking.ComparerFactory!);
                        resolvedOrders.Add(rankingBinding);
                        stages[stageIndex] =
                            RowSelectionStage<
                                ResolvedRowQueryOrderIdentity>.Top(
                                    operation.Count,
                                    rankingBinding.Identity);
                        break;
                    }
                default:
                    throw new InvalidOperationException(
                        $"Unsupported row-selection operation {operation.Kind}.");
            }
        }

        return RowQueryResolutionResult<TRow>.Success(
            new ResolvedRowQueryPlan<TRow>(
                schema.Identity,
                SectionContractSnapshot.Own(
                    predicateIdentities),
                SectionContractSnapshot.Own(predicates),
                baselineBinding,
                SectionContractSnapshot.Copy(
                    resolvedOrders),
                baselineComparerFactory,
                RowSelectionPlan<
                    ResolvedRowQueryOrderIdentity>.Create(
                        stages),
                orderCatalog));
    }

    private static ResolvedRowQueryOrderBinding CreateBinding<TRow>(
        OrderResolution<TRow> resolution) =>
        new(
            new ResolvedRowQueryOrderIdentity(),
            resolution.NamedOrderIdentity,
            resolution.FieldIdentities,
            resolution.FieldDirections,
            resolution.NamedOrderDirection);

    private static OrderResolution<TRow> ResolveOrder<TRow>(
        RowQuerySchema<TRow> schema,
        RowQueryOrderIntent intent,
        bool requireRanking,
        RowQueryOperationKind operationKind,
        int operationPosition,
        int? semanticStageNumber)
    {
        if (intent.Kind is RowQueryOrderIntentKind.Named)
        {
            if (!schema.TryGetNamedOrder(
                    intent.NamedOrderKey,
                    out RowQueryNamedOrder<TRow>? order))
            {
                return OrderResolution<TRow>.Failed(
                    Failure(
                        schema,
                        operationKind,
                        operationPosition,
                        null,
                        semanticStageNumber,
                        RowQueryFailureReason.UnknownNamedOrder));
            }

            if (requireRanking
                && order.Purpose
                    is not RowQueryOrderPurpose.Ranking)
            {
                return OrderResolution<TRow>.Failed(
                    Failure(
                        schema,
                        operationKind,
                        operationPosition,
                        null,
                        semanticStageNumber,
                        RowQueryFailureReason.NamedOrderIsNotRanking,
                        namedOrderIdentity: order.Identity));
            }

            return OrderResolution<TRow>.SuccessNamed(
                order.CreateComparerFactory(
                    intent.NamedOrderDirection),
                order.Identity,
                intent.NamedOrderDirection);
        }

        var comparerFactories =
            new Func<IComparer<TRow>>[intent.Terms.Count];
        var fieldIdentities =
            new RowQueryFieldIdentity[intent.Terms.Count];
        var fieldDirections =
            new RowQueryOrderDirection[intent.Terms.Count];
        for (int index = 0; index < intent.Terms.Count; index++)
        {
            RowQueryOrderTermIntent term =
                intent.Terms[index];
            if (!schema.TryGetField(
                    term.FieldKey,
                    out RowQueryField<TRow>? field))
            {
                return OrderResolution<TRow>.Failed(
                    Failure(
                        schema,
                        operationKind,
                        operationPosition,
                        index + 1,
                        semanticStageNumber,
                        RowQueryFailureReason.UnknownField));
            }

            if (!field.SupportsOrdering)
            {
                return OrderResolution<TRow>.Failed(
                    Failure(
                        schema,
                        operationKind,
                        operationPosition,
                        index + 1,
                        semanticStageNumber,
                        RowQueryFailureReason.UnsupportedFieldOrder,
                        fieldIdentity: field.Identity));
            }

            comparerFactories[index] =
                field.CreateComparerFactory(term.Direction)!;
            fieldIdentities[index] = field.Identity;
            fieldDirections[index] = term.Direction;
        }

        return OrderResolution<TRow>.SuccessFields(
            () =>
            {
                var comparers =
                    new IComparer<TRow>[comparerFactories.Length];
                for (int index = 0;
                     index < comparerFactories.Length;
                     index++)
                {
                    comparers[index] =
                        comparerFactories[index]();
                }

                return Comparer<TRow>.Create(
                    (left, right) =>
                    {
                        for (int index = 0;
                             index < comparers.Length;
                             index++)
                        {
                            int comparison =
                                comparers[index].Compare(left, right);
                            if (comparison != 0)
                                return comparison;
                        }

                        return 0;
                    });
            },
            fieldIdentities,
            fieldDirections);
    }

    private static OrderResolution<TRow> ResolveDefaultOrder<TRow>(
        RowQueryNamedOrderDefault<TRow> defaultOrder)
    {
        return OrderResolution<TRow>.SuccessNamed(
            defaultOrder.Order.CreateComparerFactory(
                defaultOrder.Direction),
            defaultOrder.Order.Identity,
            defaultOrder.Direction);
    }

    private static RowQueryResolutionResult<TRow> Failed<TRow>(
        RowQuerySchema<TRow> schema,
        RowQueryOperationKind operationKind,
        int operationPosition,
        int? termPosition,
        int? semanticStageNumber,
        RowQueryFailureReason reason,
        RowQueryFieldIdentity? fieldIdentity = null,
        RowQueryNamedOrderIdentity? namedOrderIdentity = null) =>
        RowQueryResolutionResult<TRow>.Failed(
            Failure(
                schema,
                operationKind,
                operationPosition,
                termPosition,
                semanticStageNumber,
                reason,
                fieldIdentity,
                namedOrderIdentity));

    private static RowQueryFailure Failure<TRow>(
        RowQuerySchema<TRow> schema,
        RowQueryOperationKind operationKind,
        int operationPosition,
        int? termPosition,
        int? semanticStageNumber,
        RowQueryFailureReason reason,
        RowQueryFieldIdentity? fieldIdentity = null,
        RowQueryNamedOrderIdentity? namedOrderIdentity = null) =>
        new(
            operationKind,
            operationPosition,
            termPosition,
            semanticStageNumber,
            schema.Identity,
            fieldIdentity,
            namedOrderIdentity,
            reason);

    private readonly record struct OrderResolution<TRow>(
        Func<IComparer<TRow>>? ComparerFactory,
        RowQueryFailure? Failure,
        RowQueryNamedOrderIdentity? NamedOrderIdentity,
        IReadOnlyList<RowQueryFieldIdentity> FieldIdentities,
        IReadOnlyList<RowQueryOrderDirection> FieldDirections,
        RowQueryOrderDirection? NamedOrderDirection)
    {
        public static OrderResolution<TRow> SuccessNamed(
            Func<IComparer<TRow>> comparerFactory,
            RowQueryNamedOrderIdentity identity,
            RowQueryOrderDirection direction) =>
            new(
                comparerFactory,
                null,
                identity,
                SectionContractSnapshot.Empty<
                    RowQueryFieldIdentity>(),
                SectionContractSnapshot.Empty<
                    RowQueryOrderDirection>(),
                direction);

        public static OrderResolution<TRow> SuccessFields(
            Func<IComparer<TRow>> comparerFactory,
            RowQueryFieldIdentity[] fieldIdentities,
            RowQueryOrderDirection[] fieldDirections) =>
            new(
                comparerFactory,
                null,
                null,
                SectionContractSnapshot.Own(fieldIdentities),
                SectionContractSnapshot.Own(fieldDirections),
                null);

        public static OrderResolution<TRow> Failed(
            RowQueryFailure failure) =>
            new(
                null,
                failure,
                null,
                SectionContractSnapshot.Empty<
                    RowQueryFieldIdentity>(),
                SectionContractSnapshot.Empty<
                    RowQueryOrderDirection>(),
                null);
    }
}
