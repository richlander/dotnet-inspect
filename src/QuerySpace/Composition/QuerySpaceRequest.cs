namespace QuerySpace.Composition;

public sealed class QuerySpaceRowIntentAssociation
{
    public QuerySpaceRowIntentAssociation(
        string scope,
        PortableQueryIntent intent,
        IReadOnlyList<string> rowSets)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentNullException.ThrowIfNull(intent);
        if (intent.Bounds.Count != 0)
        {
            throw new ArgumentException(
                "A row-query intent cannot authorize execution bounds.",
                nameof(intent));
        }

        Scope = scope;
        Intent = intent;
        RowSets = QuerySpaceCompositionContract.CopyIdentities(
            rowSets,
            nameof(rowSets),
            requireAny: true);
    }

    public string Scope { get; }

    public PortableQueryIntent Intent { get; }

    public IReadOnlyList<string> RowSets { get; }
}

public sealed class QuerySpaceRequest
{
    private QuerySpaceRequest(
        string querySpace,
        PortableQueryIntent operation,
        IReadOnlyList<string> participatingRowSets,
        IReadOnlyList<QuerySpaceRowIntentAssociation> rowIntents,
        QuerySpaceTerminalRequirement terminal,
        string? resultContract)
    {
        QuerySpace = querySpace;
        Operation = operation;
        ParticipatingRowSets = participatingRowSets;
        RowIntents = rowIntents;
        Terminal = terminal;
        ResultContract = resultContract;
    }

    public string QuerySpace { get; }

    public PortableQueryIntent Operation { get; }

    public IReadOnlyList<string> ParticipatingRowSets { get; }

    public IReadOnlyList<QuerySpaceRowIntentAssociation> RowIntents { get; }

    public QuerySpaceTerminalRequirement Terminal { get; }

    public string? ResultContract { get; }

    public static QuerySpaceRequest Create(
        QuerySpaceDescriptor descriptor,
        PortableQueryIntent operation,
        IReadOnlyList<string> participatingRowSets,
        IReadOnlyList<QuerySpaceRowIntentAssociation> rowIntents,
        QuerySpaceTerminalRequirement terminal)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(operation);
        if (operation.Order.Count != 0 || operation.Stages.Count != 0)
        {
            throw new ArgumentException(
                "An operation intent cannot carry row order or semantic "
                + "selection stages.",
                nameof(operation));
        }

        ValidateOperationIntent(descriptor.Operation, operation);

        IReadOnlyList<string> participatingCopy =
            QuerySpaceCompositionContract.CopyIdentities(
                participatingRowSets,
                nameof(participatingRowSets),
                requireAny: true);
        foreach (string rowSet in participatingCopy)
        {
            if (!descriptor.SupportsRowSet(rowSet))
            {
                throw new ArgumentException(
                    $"Query space '{descriptor.Identity}' declares no row set "
                    + $"'{rowSet}'.",
                    nameof(participatingRowSets));
            }
        }

        QuerySpaceCompositionContract.ValidateDefined(
            terminal,
            nameof(terminal));
        if (!descriptor.SupportsTerminal(terminal))
        {
            throw new ArgumentException(
                $"Query space '{descriptor.Identity}' does not support "
                + $"terminal '{terminal}'.",
                nameof(terminal));
        }

        IReadOnlyList<QuerySpaceRowIntentAssociation> rowIntentCopy =
            QuerySpaceCompositionContract.Copy(
                rowIntents,
                nameof(rowIntents));
        ValidateRowIntents(
            descriptor,
            participatingCopy,
            rowIntentCopy,
            nameof(rowIntents));

        string? resultContract = descriptor.TryGetResultContract(
            terminal,
            out QuerySpaceResultContractDescriptor? contract)
                ? contract.Identity
                : null;

        return new(
            descriptor.Identity,
            operation,
            participatingCopy,
            rowIntentCopy,
            terminal,
            resultContract);
    }

    private static void ValidateOperationIntent(
        QuerySpaceOperationScopeDescriptor operation,
        PortableQueryIntent intent)
    {
        foreach (PortableQueryTerm term in intent.Terms)
        {
            QuerySpaceOperationTermDescriptor? declaration =
                operation.Terms.FirstOrDefault(
                    candidate => string.Equals(
                        candidate.Key,
                        term.Key,
                        StringComparison.Ordinal));
            if (declaration is null)
            {
                throw new ArgumentException(
                    $"Operation scope '{operation.Identity}' declares no "
                    + $"canonical query key '{term.Key}'.",
                    nameof(intent));
            }
            if (!declaration.Operators.Contains(term.Operator))
            {
                throw new ArgumentException(
                    $"Operation term '{declaration.Identity}' does not admit "
                    + $"operator '{term.Operator}'.",
                    nameof(intent));
            }
        }

        var dimensions =
            operation.Dimensions.ToHashSet(StringComparer.Ordinal);
        foreach (PortableQueryBound bound in intent.Bounds)
        {
            if (!dimensions.Contains(bound.Dimension))
            {
                throw new ArgumentException(
                    $"Operation scope '{operation.Identity}' declares no "
                    + $"execution-bound dimension '{bound.Dimension}'.",
                    nameof(intent));
            }
        }
    }

    private static void ValidateRowIntents(
        QuerySpaceDescriptor descriptor,
        IReadOnlyList<string> participatingRowSets,
        IReadOnlyList<QuerySpaceRowIntentAssociation> rowIntents,
        string parameterName)
    {
        if (rowIntents.Count == 0)
            return;

        var participating =
            participatingRowSets.ToHashSet(StringComparer.Ordinal);
        var assigned = new HashSet<string>(StringComparer.Ordinal);
        foreach (QuerySpaceRowIntentAssociation association in rowIntents)
        {
            QuerySpaceRowScopeDescriptor scope =
                descriptor.GetRowScope(association.Scope);
            ValidateRowIntent(scope, association.Intent, parameterName);
            foreach (string rowSet in association.RowSets)
            {
                if (!participating.Contains(rowSet))
                {
                    throw new ArgumentException(
                        $"Row-intent association '{association.Scope}' names "
                        + $"nonparticipating row set '{rowSet}'.",
                        parameterName);
                }
                if (!scope.SupportsRowSet(rowSet))
                {
                    throw new ArgumentException(
                        $"Row-query scope '{scope.Identity}' is incompatible "
                        + $"with row set '{rowSet}'.",
                        parameterName);
                }
                if (!assigned.Add(rowSet))
                {
                    throw new ArgumentException(
                        $"Participating row set '{rowSet}' is assigned more "
                        + "than once.",
                        parameterName);
                }
            }
        }

        foreach (string rowSet in participatingRowSets)
        {
            if (!assigned.Contains(rowSet))
            {
                throw new ArgumentException(
                    $"Participating row set '{rowSet}' has no explicit "
                    + "row-intent association.",
                    parameterName);
            }
        }
    }

    private static void ValidateRowIntent(
        QuerySpaceRowScopeDescriptor scope,
        PortableQueryIntent intent,
        string parameterName)
    {
        foreach (PortableQueryTerm term in intent.Terms)
        {
            if (!scope.TryGetFacet(
                    term.Key,
                    out QuerySpaceRowFacetDescriptor? facet))
            {
                throw new ArgumentException(
                    $"Row-query scope '{scope.Identity}' declares no "
                    + $"canonical query key '{term.Key}'.",
                    parameterName);
            }
            if (!facet.Operators.Contains(term.Operator))
            {
                throw new ArgumentException(
                    $"Row facet '{facet.Identity}' does not admit operator "
                    + $"'{term.Operator}'.",
                    parameterName);
            }
        }

        foreach (PortableQueryStage stage in intent.Stages)
        {
            if (!scope.SupportsStage(stage.Kind))
            {
                throw new ArgumentException(
                    $"Row-query scope '{scope.Identity}' does not admit stage "
                    + $"'{stage.Kind}'.",
                    parameterName);
            }
        }

        foreach (PortableQueryOrderOperation operation in intent.Order)
        {
            if (operation.Kind is PortableQueryOrderKind.Named)
            {
                if (!scope.TryGetOrder(
                        operation.Reference,
                        out QuerySpaceRowOrderDescriptor? order))
                {
                    throw new ArgumentException(
                        $"Row-query scope '{scope.Identity}' declares no "
                        + $"named order '{operation.Reference}'.",
                        parameterName);
                }
                if (!operation.Role.IsBaseline && !order.Ranking)
                {
                    throw new ArgumentException(
                        $"Named order '{order.Identity}' is not a ranking "
                        + "order.",
                        parameterName);
                }
                continue;
            }

            foreach (PortableQueryOrderTerm term in operation.Terms)
            {
                if (!scope.TryGetFacet(
                        term.Key,
                        out QuerySpaceRowFacetDescriptor? facet)
                    || !facet.SupportsOrdering)
                {
                    throw new ArgumentException(
                        $"Row-query scope '{scope.Identity}' declares no "
                        + $"orderable key '{term.Key}'.",
                        parameterName);
                }
            }
        }
    }
}
