using System.Diagnostics.CodeAnalysis;
using QuerySpace.Operations;
using QuerySpace.Rows;

namespace QuerySpace.Composition;

public abstract class QuerySpaceRowScopeBinding
{
    private protected QuerySpaceRowScopeBinding(
        QuerySpaceRowScopeDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        Descriptor = descriptor;
    }

    public QuerySpaceRowScopeDescriptor Descriptor { get; }
}

public sealed class QuerySpaceRowScopeBinding<TRow> :
    QuerySpaceRowScopeBinding
{
    public QuerySpaceRowScopeBinding(
        QuerySpaceRowScopeDescriptor descriptor,
        RowQueryVocabulary<TRow> vocabulary)
        : base(descriptor)
    {
        ArgumentNullException.ThrowIfNull(vocabulary);
        ValidateDescriptor(descriptor, vocabulary);
        Vocabulary = vocabulary;
    }

    public RowQueryVocabulary<TRow> Vocabulary { get; }

    public RowQueryResolutionResult<TRow> Resolve(
        PortableQueryIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (intent.Bounds.Count != 0)
        {
            throw new ArgumentException(
                "A row-query intent cannot authorize execution bounds.",
                nameof(intent));
        }

        return RowQueryResolver.Resolve(
            Vocabulary,
            Lower(intent));
    }

    private static RowQueryIntent Lower(
        PortableQueryIntent intent)
    {
        var predicates =
            new RowQueryPredicateIntent[intent.Terms.Count];
        for (int index = 0; index < intent.Terms.Count; index++)
        {
            PortableQueryTerm term = intent.Terms[index];
            predicates[index] =
                new RowQueryPredicateIntent(
                    term.Key,
                    ToRowOperator(term.Operator),
                    new RowQueryValueToken(term.Value));
        }

        PortableQueryOrderOperation? baselineOperation =
            intent.Order.SingleOrDefault(
                static operation => operation.Role.IsBaseline);
        RowQueryOrderIntent? baseline =
            baselineOperation is null
                ? null
                : ToRowOrder(baselineOperation);

        var selection =
            new RowSelectionIntentOperation<
                RowQueryOrderIntent>[intent.Stages.Count];
        for (int index = 0; index < intent.Stages.Count; index++)
        {
            PortableQueryStage stage = intent.Stages[index];
            PortableQueryOrderOperation? ranking =
                intent.Order.SingleOrDefault(
                    operation =>
                        !operation.Role.IsBaseline
                        && operation.Role.StageIndex == index);
            selection[index] =
                stage.Kind switch
                {
                    RowSelectionStageKind.Head =>
                        RowSelectionIntentOperation<
                            RowQueryOrderIntent>.Head(
                                stage.Count),
                    RowSelectionStageKind.Tail =>
                        RowSelectionIntentOperation<
                            RowQueryOrderIntent>.Tail(
                                stage.Count),
                    RowSelectionStageKind.Window =>
                        RowSelectionIntentOperation<
                            RowQueryOrderIntent>.Window(
                                stage.Start,
                                stage.End),
                    RowSelectionStageKind.Top
                        when ranking is not null =>
                            RowSelectionIntentOperation<
                                RowQueryOrderIntent>.Top(
                                    stage.Count,
                                    ToRowOrder(ranking)),
                    RowSelectionStageKind.Top =>
                        RowSelectionIntentOperation<
                            RowQueryOrderIntent>.Top(
                                stage.Count),
                    _ => throw new InvalidOperationException(
                        $"Unsupported row-selection stage {stage.Kind}."),
                };
        }

        return RowQueryIntent.Create(
            predicates,
            baseline,
            RowSelectionIntent<RowQueryOrderIntent>.Create(
                selection));
    }

    private static RowQueryOrderIntent ToRowOrder(
        PortableQueryOrderOperation operation) =>
        operation.Kind switch
        {
            PortableQueryOrderKind.Named =>
                RowQueryOrderIntent.Named(
                    operation.Reference,
                    ToRowDirection(operation.Direction)),
            PortableQueryOrderKind.Fields =>
                RowQueryOrderIntent.Keys(
                    [
                        .. operation.Terms.Select(
                            static term =>
                                new RowQueryOrderTermIntent(
                                    term.Key,
                                    ToRowDirection(term.Direction))),
                    ]),
            _ => throw new InvalidOperationException(
                $"Unsupported portable row order {operation.Kind}."),
        };

    private static RowQueryOperator ToRowOperator(
        PortableQueryOperator @operator) =>
        @operator switch
        {
            PortableQueryOperator.Equal =>
                RowQueryOperator.Equals,
            PortableQueryOperator.NotEqual =>
                RowQueryOperator.NotEquals,
            PortableQueryOperator.AtLeast =>
                RowQueryOperator.GreaterOrEqual,
            PortableQueryOperator.AtMost =>
                RowQueryOperator.LessOrEqual,
            _ => throw new InvalidOperationException(
                $"Portable operator '{@operator}' has no row-query binding."),
        };

    private static PortableQueryOperator ToPortableOperator(
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
            _ => throw new InvalidOperationException(
                $"Row-query operator '{@operator}' has no portable binding."),
        };

    private static RowQueryOrderDirection ToRowDirection(
        PortableQueryDirection direction) =>
        direction switch
        {
            PortableQueryDirection.Ascending =>
                RowQueryOrderDirection.Ascending,
            PortableQueryDirection.Descending =>
                RowQueryOrderDirection.Descending,
            _ => throw new InvalidOperationException(
                $"Unsupported portable row direction {direction}."),
        };

    private static void ValidateDescriptor(
        QuerySpaceRowScopeDescriptor descriptor,
        RowQueryVocabulary<TRow> vocabulary)
    {
        if (descriptor.Facets.Count != vocabulary.Keys.Count)
        {
            throw Mismatch(
                descriptor,
                "facet and executable key counts differ");
        }

        foreach (RowQueryKey<TRow> key in vocabulary.Keys)
        {
            if (!descriptor.TryGetFacet(
                    key.Key,
                    out QuerySpaceRowFacetDescriptor? facet))
            {
                throw Mismatch(
                    descriptor,
                    $"executable key '{key.Key}' has no facet");
            }

            var operators = key.Operators
                .Select(ToPortableOperator)
                .ToHashSet();
            if (!operators.SetEquals(facet.Operators))
            {
                throw Mismatch(
                    descriptor,
                    $"facet '{facet.Identity}' operator surface differs");
            }

            if (facet.SupportsOrdering != key.SupportsOrdering)
            {
                throw Mismatch(
                    descriptor,
                    $"facet '{facet.Identity}' order capability differs");
            }
        }

        if (descriptor.Orders.Count != vocabulary.NamedOrders.Count)
        {
            throw Mismatch(
                descriptor,
                "named-order counts differ");
        }

        foreach (RowQueryNamedOrder<TRow> order
            in vocabulary.NamedOrders)
        {
            if (!descriptor.TryGetOrder(
                    order.Key,
                    out QuerySpaceRowOrderDescriptor? declaration))
            {
                throw Mismatch(
                    descriptor,
                    $"executable named order '{order.Key}' is undeclared");
            }

            bool ranking =
                order.Purpose is RowQueryOrderPurpose.Ranking;
            if (declaration.Ranking != ranking)
            {
                throw Mismatch(
                    descriptor,
                    $"named order '{order.Key}' purpose differs");
            }
        }

        if (descriptor.SupportsStage(RowSelectionStageKind.Top)
            && vocabulary.DefaultTopRanking is null
            && !vocabulary.Keys.Any(static key => key.SupportsOrdering)
            && !vocabulary.NamedOrders.Any(
                static order =>
                    order.Purpose is RowQueryOrderPurpose.Ranking))
        {
            throw Mismatch(
                descriptor,
                "Top is advertised without an executable ranking order");
        }
    }

    private static ArgumentException Mismatch(
        QuerySpaceRowScopeDescriptor descriptor,
        string detail) =>
        new(
            $"Row-query scope '{descriptor.Identity}' descriptor and "
                + $"executable vocabulary do not match: {detail}.",
            nameof(descriptor));
}

public sealed class QuerySpaceBinding
{
    private readonly IReadOnlyDictionary<
        string,
        QuerySpaceRowScopeBinding> _rowScopesByIdentity;

    private QuerySpaceBinding(
        QuerySpaceDescriptor descriptor,
        IQueryOperationRoute operation,
        IReadOnlyList<QuerySpaceRowScopeBinding> rowScopes,
        IReadOnlyDictionary<
            string,
            QuerySpaceRowScopeBinding> rowScopesByIdentity)
    {
        Descriptor = descriptor;
        Operation = operation;
        RowScopes = rowScopes;
        _rowScopesByIdentity = rowScopesByIdentity;
    }

    public QuerySpaceDescriptor Descriptor { get; }

    public IQueryOperationRoute Operation { get; }

    public IReadOnlyList<QuerySpaceRowScopeBinding> RowScopes { get; }

    public static QuerySpaceBinding Create(
        string identity,
        IQueryOperationRoute operation,
        IReadOnlyList<QuerySpaceRowScopeBinding> rowScopes,
        IReadOnlyList<QuerySpaceTerminalRequirement> terminals,
        bool acceptsContinuation,
        IReadOnlyList<QuerySpaceResultContractDescriptor> resultContracts)
    {
        IReadOnlyList<QuerySpaceRowScopeBinding> rowScopeCopy =
            QuerySpaceCompositionContract.Copy(
                rowScopes,
                nameof(rowScopes));
        IReadOnlyDictionary<string, QuerySpaceRowScopeBinding>
            rowScopesByIdentity =
                QuerySpaceCompositionContract.Index(
                    rowScopeCopy,
                    static scope => scope.Descriptor.Identity,
                    "row-query scope binding identity",
                    nameof(rowScopes));
        QuerySpaceDescriptor descriptor =
            QuerySpaceDescriptor.Create(
                identity,
                operation,
                [
                    .. rowScopeCopy.Select(
                        static scope => scope.Descriptor),
                ],
                terminals,
                acceptsContinuation,
                resultContracts);
        return new(
            descriptor,
            operation,
            rowScopeCopy,
            rowScopesByIdentity);
    }

    public bool TryGetRowScope(
        string identity,
        [NotNullWhen(true)]
        out QuerySpaceRowScopeBinding? binding)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        return _rowScopesByIdentity.TryGetValue(
            identity,
            out binding);
    }
}
