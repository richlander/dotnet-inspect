
namespace QuerySpace.Rows;

public enum RowQueryOperationKind
{
    Predicate,
    BaselineOrder,
    TopRanking
}

public enum RowQueryFailureReason
{
    UnknownKey,
    UnsupportedPredicateOperator,
    InvalidValue,
    UnsupportedKeyOrder,
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
        RowQueryVocabularyIdentity vocabularyIdentity,
        RowQueryKeyIdentity? keyIdentity,
        RowQueryNamedOrderIdentity? namedOrderIdentity,
        RowQueryFailureReason reason)
    {
        OperationKind = operationKind;
        OperationPosition = operationPosition;
        TermPosition = termPosition;
        SemanticStageNumber = semanticStageNumber;
        VocabularyIdentity = vocabularyIdentity;
        KeyIdentity = keyIdentity;
        NamedOrderIdentity = namedOrderIdentity;
        Reason = reason;
    }

    public RowQueryOperationKind OperationKind { get; }

    public int OperationPosition { get; }

    public int? TermPosition { get; }

    public int? SemanticStageNumber { get; }

    public RowQueryVocabularyIdentity VocabularyIdentity { get; }

    public RowQueryKeyIdentity? KeyIdentity { get; }

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
        RowQueryVocabularyIdentity vocabularyIdentity,
        IReadOnlyList<RowQueryKeyIdentity> predicateKeyIdentities,
        IReadOnlyList<Predicate<TRow>> predicates,
        ResolvedRowQueryOrderBinding? baselineOrder,
        IReadOnlyList<ResolvedRowQueryOrderBinding> resolvedOrders,
        Func<IComparer<TRow>>? baselineComparerFactory,
        RowSelectionPlan<ResolvedRowQueryOrderIdentity> selectionPlan,
        IReadOnlyDictionary<
            ResolvedRowQueryOrderIdentity,
            Func<IComparer<TRow>>> orderCatalog)
    {
        VocabularyIdentity = vocabularyIdentity;
        PredicateKeyIdentities = predicateKeyIdentities;
        _predicates = predicates;
        BaselineOrder = baselineOrder;
        ResolvedOrders = resolvedOrders;
        _baselineComparerFactory = baselineComparerFactory;
        SelectionPlan = selectionPlan;
        _orderCatalog = orderCatalog;
    }

    public RowQueryVocabularyIdentity VocabularyIdentity { get; }

    public IReadOnlyList<RowQueryKeyIdentity>
        PredicateKeyIdentities
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
        IReadOnlyList<RowQueryKeyIdentity> keyIdentities,
        IReadOnlyList<RowQueryOrderDirection> keyDirections,
        RowQueryOrderDirection? namedOrderDirection)
    {
        Identity = identity;
        NamedOrderIdentity = namedOrderIdentity;
        KeyIdentities = keyIdentities;
        KeyDirections = keyDirections;
        NamedOrderDirection = namedOrderDirection;
    }

    public ResolvedRowQueryOrderIdentity Identity { get; }

    public RowQueryNamedOrderIdentity? NamedOrderIdentity { get; }

    public IReadOnlyList<RowQueryKeyIdentity> KeyIdentities { get; }

    public IReadOnlyList<RowQueryOrderDirection> KeyDirections { get; }

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
        RowQueryVocabulary<TRow> vocabulary,
        RowQueryIntent intent)
    {
        ArgumentNullException.ThrowIfNull(vocabulary);
        ArgumentNullException.ThrowIfNull(intent);

        var predicates =
            new Predicate<TRow>[intent.Predicates.Count];
        var predicateKeyIdentities =
            new RowQueryKeyIdentity[intent.Predicates.Count];
        for (int index = 0; index < intent.Predicates.Count; index++)
        {
            RowQueryPredicateIntent predicateIntent =
                intent.Predicates[index];
            if (!vocabulary.TryGetKey(
                    predicateIntent.Key,
                    out RowQueryKey<TRow>? key))
            {
                return Failed<TRow>(
                    vocabulary,
                    RowQueryOperationKind.Predicate,
                    index + 1,
                    null,
                    null,
                    RowQueryFailureReason.UnknownKey);
            }

            if (!key.Operators.Contains(
                    predicateIntent.Operator))
            {
                return Failed<TRow>(
                    vocabulary,
                    RowQueryOperationKind.Predicate,
                    index + 1,
                    null,
                    null,
                    RowQueryFailureReason.UnsupportedPredicateOperator,
                    keyIdentity: key.Identity);
            }

            Predicate<TRow>? predicate =
                key.BindPredicate(
                    predicateIntent.Operator,
                    predicateIntent.Value);
            if (predicate is null)
            {
                return Failed<TRow>(
                    vocabulary,
                    RowQueryOperationKind.Predicate,
                    index + 1,
                    null,
                    null,
                    RowQueryFailureReason.InvalidValue,
                    keyIdentity: key.Identity);
            }

            predicates[index] = predicate;
            predicateKeyIdentities[index] = key.Identity;
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
                    vocabulary,
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
        else if (vocabulary.DefaultBaselineOrder is not null)
        {
            OrderResolution<TRow> baseline =
                ResolveDefaultOrder(
                    vocabulary.DefaultBaselineOrder);
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
                                    vocabulary,
                                    operation.RankingOrderOperand,
                                    requireRanking: true,
                                    RowQueryOperationKind.TopRanking,
                                    topPosition,
                                    stageIndex + 1);
                        }
                        else if (vocabulary.DefaultTopRanking is not null)
                        {
                            ranking =
                                ResolveDefaultOrder(
                                    vocabulary.DefaultTopRanking);
                        }
                        else
                        {
                            return Failed<TRow>(
                                vocabulary,
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
                vocabulary.Identity,
                QuerySpaceSnapshot.Own(
                    predicateKeyIdentities),
                QuerySpaceSnapshot.Own(predicates),
                baselineBinding,
                QuerySpaceSnapshot.Copy(
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
            resolution.KeyIdentities,
            resolution.KeyDirections,
            resolution.NamedOrderDirection);

    private static OrderResolution<TRow> ResolveOrder<TRow>(
        RowQueryVocabulary<TRow> vocabulary,
        RowQueryOrderIntent intent,
        bool requireRanking,
        RowQueryOperationKind operationKind,
        int operationPosition,
        int? semanticStageNumber)
    {
        if (intent.Kind is RowQueryOrderIntentKind.Named)
        {
            if (!vocabulary.TryGetNamedOrder(
                    intent.NamedOrderKey,
                    out RowQueryNamedOrder<TRow>? order))
            {
                return OrderResolution<TRow>.Failed(
                    Failure(
                        vocabulary,
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
                        vocabulary,
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
        var keyIdentities =
            new RowQueryKeyIdentity[intent.Terms.Count];
        var keyDirections =
            new RowQueryOrderDirection[intent.Terms.Count];
        for (int index = 0; index < intent.Terms.Count; index++)
        {
            RowQueryOrderTermIntent term =
                intent.Terms[index];
            if (!vocabulary.TryGetKey(
                    term.Key,
                    out RowQueryKey<TRow>? key))
            {
                return OrderResolution<TRow>.Failed(
                    Failure(
                        vocabulary,
                        operationKind,
                        operationPosition,
                        index + 1,
                        semanticStageNumber,
                        RowQueryFailureReason.UnknownKey));
            }

            if (!key.SupportsOrdering)
            {
                return OrderResolution<TRow>.Failed(
                    Failure(
                        vocabulary,
                        operationKind,
                        operationPosition,
                        index + 1,
                        semanticStageNumber,
                        RowQueryFailureReason.UnsupportedKeyOrder,
                        keyIdentity: key.Identity));
            }

            comparerFactories[index] =
                key.CreateComparerFactory(term.Direction)!;
            keyIdentities[index] = key.Identity;
            keyDirections[index] = term.Direction;
        }

        return OrderResolution<TRow>.SuccessKeys(
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
            keyIdentities,
            keyDirections);
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
        RowQueryVocabulary<TRow> vocabulary,
        RowQueryOperationKind operationKind,
        int operationPosition,
        int? termPosition,
        int? semanticStageNumber,
        RowQueryFailureReason reason,
        RowQueryKeyIdentity? keyIdentity = null,
        RowQueryNamedOrderIdentity? namedOrderIdentity = null) =>
        RowQueryResolutionResult<TRow>.Failed(
            Failure(
                vocabulary,
                operationKind,
                operationPosition,
                termPosition,
                semanticStageNumber,
                reason,
                keyIdentity,
                namedOrderIdentity));

    private static RowQueryFailure Failure<TRow>(
        RowQueryVocabulary<TRow> vocabulary,
        RowQueryOperationKind operationKind,
        int operationPosition,
        int? termPosition,
        int? semanticStageNumber,
        RowQueryFailureReason reason,
        RowQueryKeyIdentity? keyIdentity = null,
        RowQueryNamedOrderIdentity? namedOrderIdentity = null) =>
        new(
            operationKind,
            operationPosition,
            termPosition,
            semanticStageNumber,
            vocabulary.Identity,
            keyIdentity,
            namedOrderIdentity,
            reason);

    private readonly record struct OrderResolution<TRow>(
        Func<IComparer<TRow>>? ComparerFactory,
        RowQueryFailure? Failure,
        RowQueryNamedOrderIdentity? NamedOrderIdentity,
        IReadOnlyList<RowQueryKeyIdentity> KeyIdentities,
        IReadOnlyList<RowQueryOrderDirection> KeyDirections,
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
                QuerySpaceSnapshot.Empty<
                    RowQueryKeyIdentity>(),
                QuerySpaceSnapshot.Empty<
                    RowQueryOrderDirection>(),
                direction);

        public static OrderResolution<TRow> SuccessKeys(
            Func<IComparer<TRow>> comparerFactory,
            RowQueryKeyIdentity[] keyIdentities,
            RowQueryOrderDirection[] keyDirections) =>
            new(
                comparerFactory,
                null,
                null,
                QuerySpaceSnapshot.Own(keyIdentities),
                QuerySpaceSnapshot.Own(keyDirections),
                null);

        public static OrderResolution<TRow> Failed(
            RowQueryFailure failure) =>
            new(
                null,
                failure,
                null,
                QuerySpaceSnapshot.Empty<
                    RowQueryKeyIdentity>(),
                QuerySpaceSnapshot.Empty<
                    RowQueryOrderDirection>(),
                null);
    }
}
