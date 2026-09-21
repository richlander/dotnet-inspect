using System.Diagnostics.CodeAnalysis;
using QuerySpace.Operations;
using QuerySpace.Rows;

namespace QuerySpace.Composition;

public enum QuerySpaceTerminalRequirement
{
    Rows,
    Count
}

public sealed class QuerySpaceOperationTermDescriptor
{
    internal QuerySpaceOperationTermDescriptor(
        QueryOperationTermCapability capability)
    {
        QueryOperationTermBinding binding = capability.Binding;
        Identity = binding.Identity;
        Key = binding.Key;
        Role = binding.Role;
        Operators = QuerySpaceCompositionContract.CopyEnums(
            capability.Operators,
            nameof(capability));
        ValueKind = binding.Description.ValueKind;
        ValueVocabulary = null;
        Label = binding.Description.Label;
        Values = QuerySpaceCompositionContract.CopyValues(
            binding.Description.Values,
            nameof(capability));
        Summary = binding.Description.Summary;
        Effects = QuerySpaceCompositionContract.Copy(
            binding.Effects,
            nameof(capability));
    }

    public string Identity { get; }

    public string Key { get; }

    public QueryOperationTermRole Role { get; }

    public IReadOnlyList<PortableQueryOperator> Operators { get; }

    public string ValueKind { get; }

    public string? ValueVocabulary { get; }

    public string Label { get; }

    public IReadOnlyList<string> Values { get; }

    public string Summary { get; }

    public IReadOnlyList<QueryOperationEffect> Effects { get; }
}

public sealed class QuerySpaceOperationScopeDescriptor
{
    private QuerySpaceOperationScopeDescriptor(
        string identity,
        string operation,
        string queryVocabulary,
        string subjectRole,
        string resultGrain,
        IReadOnlyList<string> rowSets,
        string profile,
        IReadOnlyList<QuerySpaceOperationTermDescriptor> terms,
        IReadOnlyList<string> dimensions)
    {
        Identity = identity;
        Operation = operation;
        QueryVocabulary = queryVocabulary;
        SubjectRole = subjectRole;
        ResultGrain = resultGrain;
        RowSets = rowSets;
        Profile = profile;
        Terms = terms;
        Dimensions = dimensions;
    }

    public string Identity { get; }

    public string Operation { get; }

    public string QueryVocabulary { get; }

    public string SubjectRole { get; }

    public string ResultGrain { get; }

    public IReadOnlyList<string> RowSets { get; }

    public string Profile { get; }

    public IReadOnlyList<QuerySpaceOperationTermDescriptor> Terms { get; }

    public IReadOnlyList<string> Dimensions { get; }

    public static QuerySpaceOperationScopeDescriptor Create(
        IQueryOperationRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);

        QuerySpaceOperationTermDescriptor[] terms =
        [
            .. route.Capabilities.Terms.Select(
                static capability =>
                    new QuerySpaceOperationTermDescriptor(capability)),
        ];
        return new(
            route.Identity,
            route.OperationIdentity,
            route.Capabilities.Vocabulary,
            route.SubjectRole,
            route.ResultGrain,
            QuerySpaceCompositionContract.CopyIdentities(
                route.RowSets,
                nameof(route)),
            route.ProfileIdentity,
            Array.AsReadOnly(terms),
            QuerySpaceCompositionContract.CopyIdentities(
                route.Capabilities.Dimensions,
                nameof(route)));
    }
}

public sealed class QuerySpaceRowFacetDescriptor
{
    public QuerySpaceRowFacetDescriptor(
        string identity,
        string key,
        IReadOnlyList<PortableQueryOperator> operators,
        string valueKind,
        string? valueVocabulary,
        string label,
        IReadOnlyList<string> values,
        string summary,
        bool supportsOrdering)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(valueKind);
        if (valueVocabulary is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(valueVocabulary);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);

        Identity = identity;
        Key = key;
        Operators = QuerySpaceCompositionContract.CopyEnums(
            operators,
            nameof(operators));
        if (Operators.Count == 0)
        {
            throw new ArgumentException(
                "A row facet must admit at least one operator.",
                nameof(operators));
        }
        ValueKind = valueKind;
        ValueVocabulary = valueVocabulary;
        Label = label;
        Values = QuerySpaceCompositionContract.CopyValues(
            values,
            nameof(values));
        Summary = summary;
        SupportsOrdering = supportsOrdering;
    }

    public string Identity { get; }

    public string Key { get; }

    public IReadOnlyList<PortableQueryOperator> Operators { get; }

    public string ValueKind { get; }

    public string? ValueVocabulary { get; }

    public string Label { get; }

    public IReadOnlyList<string> Values { get; }

    public string Summary { get; }

    public bool SupportsOrdering { get; }
}

public sealed class QuerySpaceRowOrderDescriptor
{
    public QuerySpaceRowOrderDescriptor(
        string identity,
        bool ranking)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        Identity = identity;
        Ranking = ranking;
    }

    public string Identity { get; }

    public bool Ranking { get; }
}

public sealed class QuerySpaceRowScopeDescriptor
{
    private readonly IReadOnlyDictionary<
        string,
        QuerySpaceRowFacetDescriptor> _facetsByKey;
    private readonly IReadOnlyDictionary<
        string,
        QuerySpaceRowOrderDescriptor> _ordersByIdentity;
    private readonly IReadOnlySet<string> _rowSetSet;
    private readonly IReadOnlySet<RowSelectionStageKind> _stageSet;

    public QuerySpaceRowScopeDescriptor(
        string identity,
        string queryVocabulary,
        IReadOnlyList<string> rowSets,
        IReadOnlyList<QuerySpaceRowFacetDescriptor> facets,
        IReadOnlyList<QuerySpaceRowOrderDescriptor> orders,
        IReadOnlyList<RowSelectionStageKind> stages)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(queryVocabulary);
        Identity = identity;
        QueryVocabulary = queryVocabulary;
        RowSets = QuerySpaceCompositionContract.CopyIdentities(
            rowSets,
            nameof(rowSets),
            requireAny: true);
        Facets = QuerySpaceCompositionContract.Copy(
            facets,
            nameof(facets));
        Orders = QuerySpaceCompositionContract.Copy(
            orders,
            nameof(orders));
        Stages = QuerySpaceCompositionContract.CopyEnums(
            stages,
            nameof(stages));

        _rowSetSet = RowSets.ToHashSet(StringComparer.Ordinal);
        _stageSet = Stages.ToHashSet();
        _facetsByKey = QuerySpaceCompositionContract.Index(
            Facets,
            static facet => facet.Key,
            "row facet key",
            nameof(facets));
        _ = QuerySpaceCompositionContract.Index(
            Facets,
            static facet => facet.Identity,
            "row facet identity",
            nameof(facets));
        _ordersByIdentity = QuerySpaceCompositionContract.Index(
            Orders,
            static order => order.Identity,
            "row order identity",
            nameof(orders));
    }

    public string Identity { get; }

    public string QueryVocabulary { get; }

    public IReadOnlyList<string> RowSets { get; }

    public IReadOnlyList<QuerySpaceRowFacetDescriptor> Facets { get; }

    public IReadOnlyList<QuerySpaceRowOrderDescriptor> Orders { get; }

    public IReadOnlyList<RowSelectionStageKind> Stages { get; }

    internal bool SupportsRowSet(string identity) =>
        _rowSetSet.Contains(identity);

    internal bool TryGetFacet(
        string key,
        [NotNullWhen(true)]
        out QuerySpaceRowFacetDescriptor? facet) =>
        _facetsByKey.TryGetValue(key, out facet);

    internal bool TryGetOrder(
        string identity,
        [NotNullWhen(true)]
        out QuerySpaceRowOrderDescriptor? order) =>
        _ordersByIdentity.TryGetValue(identity, out order);

    internal bool SupportsStage(RowSelectionStageKind kind) =>
        _stageSet.Contains(kind);
}

public sealed class QuerySpaceResultContractDescriptor
{
    public QuerySpaceResultContractDescriptor(
        QuerySpaceTerminalRequirement terminal,
        string identity)
    {
        QuerySpaceCompositionContract.ValidateDefined(
            terminal,
            nameof(terminal));
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        Terminal = terminal;
        Identity = identity;
    }

    public QuerySpaceTerminalRequirement Terminal { get; }

    public string Identity { get; }
}

public sealed class QuerySpaceDescriptor
{
    private readonly IReadOnlyDictionary<
        string,
        QuerySpaceRowScopeDescriptor> _rowScopesByIdentity;
    private readonly IReadOnlySet<string> _rowSetSet;
    private readonly IReadOnlySet<QuerySpaceTerminalRequirement> _terminalSet;
    private readonly IReadOnlyDictionary<
        QuerySpaceTerminalRequirement,
        QuerySpaceResultContractDescriptor> _resultContractsByTerminal;

    private QuerySpaceDescriptor(
        string identity,
        QuerySpaceOperationScopeDescriptor operation,
        IReadOnlyList<QuerySpaceRowScopeDescriptor> rowScopes,
        IReadOnlyList<QuerySpaceTerminalRequirement> terminals,
        bool acceptsContinuation,
        IReadOnlyList<QuerySpaceResultContractDescriptor> resultContracts,
        IReadOnlyDictionary<
            string,
            QuerySpaceRowScopeDescriptor> rowScopesByIdentity,
        IReadOnlyDictionary<
            QuerySpaceTerminalRequirement,
            QuerySpaceResultContractDescriptor> resultContractsByTerminal)
    {
        Identity = identity;
        Operation = operation;
        RowScopes = rowScopes;
        Terminals = terminals;
        AcceptsContinuation = acceptsContinuation;
        ResultContracts = resultContracts;
        _rowScopesByIdentity = rowScopesByIdentity;
        _rowSetSet = operation.RowSets.ToHashSet(StringComparer.Ordinal);
        _terminalSet = terminals.ToHashSet();
        _resultContractsByTerminal = resultContractsByTerminal;
    }

    public string Identity { get; }

    public QuerySpaceOperationScopeDescriptor Operation { get; }

    public IReadOnlyList<QuerySpaceRowScopeDescriptor> RowScopes { get; }

    public IReadOnlyList<QuerySpaceTerminalRequirement> Terminals { get; }

    public bool AcceptsContinuation { get; }

    public IReadOnlyList<QuerySpaceResultContractDescriptor> ResultContracts
    {
        get;
    }

    public static QuerySpaceDescriptor Create(
        string identity,
        IQueryOperationRoute operation,
        IReadOnlyList<QuerySpaceRowScopeDescriptor> rowScopes,
        IReadOnlyList<QuerySpaceTerminalRequirement> terminals,
        bool acceptsContinuation,
        IReadOnlyList<QuerySpaceResultContractDescriptor> resultContracts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        ArgumentNullException.ThrowIfNull(operation);
        QuerySpaceOperationScopeDescriptor operationDescriptor =
            QuerySpaceOperationScopeDescriptor.Create(operation);
        if (operationDescriptor.RowSets.Count == 0)
        {
            throw new ArgumentException(
                "A terminal query space requires at least one declared row set.",
                nameof(operation));
        }

        IReadOnlyList<QuerySpaceRowScopeDescriptor> rowScopeCopy =
            QuerySpaceCompositionContract.Copy(
                rowScopes,
                nameof(rowScopes));
        if (rowScopeCopy.Count == 0)
        {
            throw new ArgumentException(
                "A terminal query space requires at least one row-query scope.",
                nameof(rowScopes));
        }

        IReadOnlyDictionary<string, QuerySpaceRowScopeDescriptor>
            rowScopesByIdentity =
                QuerySpaceCompositionContract.Index(
                    rowScopeCopy,
                    static scope => scope.Identity,
                    "row-query scope identity",
                    nameof(rowScopes));
        ValidateRowScopes(
            operationDescriptor,
            rowScopeCopy,
            nameof(rowScopes));
        ValidateCanonicalKeys(
            operationDescriptor,
            rowScopeCopy,
            nameof(rowScopes));

        IReadOnlyList<QuerySpaceTerminalRequirement> terminalCopy =
            QuerySpaceCompositionContract.CopyEnums(
                terminals,
                nameof(terminals));
        if (terminalCopy.Count == 0)
        {
            throw new ArgumentException(
                "A query space requires at least one terminal.",
                nameof(terminals));
        }

        IReadOnlyList<QuerySpaceResultContractDescriptor> resultContractCopy =
            QuerySpaceCompositionContract.Copy(
                resultContracts,
                nameof(resultContracts));
        IReadOnlyDictionary<
            QuerySpaceTerminalRequirement,
            QuerySpaceResultContractDescriptor> resultContractsByTerminal =
                QuerySpaceCompositionContract.Index(
                    resultContractCopy,
                    static contract => contract.Terminal,
                    "result-contract terminal",
                    nameof(resultContracts));
        var terminalSet = terminalCopy.ToHashSet();
        foreach (QuerySpaceResultContractDescriptor contract
            in resultContractCopy)
        {
            if (!terminalSet.Contains(contract.Terminal))
            {
                throw new ArgumentException(
                    $"Result contract '{contract.Identity}' names unsupported "
                    + $"terminal '{contract.Terminal}'.",
                    nameof(resultContracts));
            }
        }

        return new(
            identity,
            operationDescriptor,
            rowScopeCopy,
            terminalCopy,
            acceptsContinuation,
            resultContractCopy,
            rowScopesByIdentity,
            resultContractsByTerminal);
    }

    public bool TryGetResultContract(
        QuerySpaceTerminalRequirement terminal,
        [NotNullWhen(true)]
        out QuerySpaceResultContractDescriptor? resultContract)
    {
        QuerySpaceCompositionContract.ValidateDefined(
            terminal,
            nameof(terminal));
        return _resultContractsByTerminal.TryGetValue(
            terminal,
            out resultContract);
    }

    internal bool SupportsRowSet(string identity) =>
        _rowSetSet.Contains(identity);

    internal bool SupportsTerminal(
        QuerySpaceTerminalRequirement terminal) =>
        _terminalSet.Contains(terminal);

    internal QuerySpaceRowScopeDescriptor GetRowScope(string identity) =>
        _rowScopesByIdentity.TryGetValue(
            identity,
            out QuerySpaceRowScopeDescriptor? scope)
            ? scope
            : throw new ArgumentException(
                $"Query space '{Identity}' declares no row-query scope "
                + $"'{identity}'.",
                nameof(identity));

    private static void ValidateRowScopes(
        QuerySpaceOperationScopeDescriptor operation,
        IReadOnlyList<QuerySpaceRowScopeDescriptor> rowScopes,
        string parameterName)
    {
        var operationRowSets =
            operation.RowSets.ToHashSet(StringComparer.Ordinal);
        var coveredRowSets = new HashSet<string>(StringComparer.Ordinal);
        foreach (QuerySpaceRowScopeDescriptor scope in rowScopes)
        {
            foreach (string rowSet in scope.RowSets)
            {
                if (!operationRowSets.Contains(rowSet))
                {
                    throw new ArgumentException(
                        $"Row-query scope '{scope.Identity}' names unknown "
                        + $"row set '{rowSet}'.",
                        parameterName);
                }
                coveredRowSets.Add(rowSet);
            }
        }

        foreach (string rowSet in operation.RowSets)
        {
            if (!coveredRowSets.Contains(rowSet))
            {
                throw new ArgumentException(
                    $"Declared row set '{rowSet}' has no row-query scope.",
                    parameterName);
            }
        }
    }

    private static void ValidateCanonicalKeys(
        QuerySpaceOperationScopeDescriptor operation,
        IReadOnlyList<QuerySpaceRowScopeDescriptor> rowScopes,
        string parameterName)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (QuerySpaceOperationTermDescriptor term in operation.Terms)
            Add(term.Key, $"operation term '{term.Identity}'");
        foreach (QuerySpaceRowScopeDescriptor scope in rowScopes)
        {
            foreach (QuerySpaceRowFacetDescriptor facet in scope.Facets)
            {
                Add(
                    facet.Key,
                    $"row facet '{scope.Identity}/{facet.Identity}'");
            }
        }
        return;

        void Add(string key, string source)
        {
            if (!keys.Add(key))
            {
                throw new ArgumentException(
                    $"Canonical query key '{key}' is duplicated at {source}.",
                    parameterName);
            }
        }
    }
}

internal static class QuerySpaceCompositionContract
{
    public static IReadOnlyList<T> Copy<T>(
        IReadOnlyList<T> values,
        string parameterName)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        var copy = new T[values.Count];
        for (int index = 0; index < values.Count; index++)
        {
            copy[index] = values[index]
                ?? throw new ArgumentNullException(
                    parameterName,
                    $"Value {index + 1} is null.");
        }
        return Array.AsReadOnly(copy);
    }

    public static IReadOnlyList<TEnum> CopyEnums<TEnum>(
        IReadOnlyList<TEnum> values,
        string parameterName)
        where TEnum : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        var seen = new HashSet<TEnum>();
        var copy = new TEnum[values.Count];
        for (int index = 0; index < values.Count; index++)
        {
            TEnum value = values[index];
            ValidateDefined(value, parameterName);
            if (!seen.Add(value))
            {
                throw new ArgumentException(
                    $"Value '{value}' is duplicated.",
                    parameterName);
            }
            copy[index] = value;
        }
        return Array.AsReadOnly(copy);
    }

    public static IReadOnlyList<string> CopyIdentities(
        IReadOnlyList<string> values,
        string parameterName,
        bool requireAny = false) =>
        CopyStrings(
            values,
            parameterName,
            requireAny,
            "Identity");

    public static IReadOnlyList<string> CopyValues(
        IReadOnlyList<string> values,
        string parameterName) =>
        CopyStrings(
            values,
            parameterName,
            requireAny: false,
            "Value");

    public static IReadOnlyDictionary<TKey, TValue> Index<TKey, TValue>(
        IReadOnlyList<TValue> values,
        Func<TValue, TKey> key,
        string kind,
        string parameterName)
        where TKey : notnull
        where TValue : class
    {
        var index = new Dictionary<TKey, TValue>();
        foreach (TValue value in values)
        {
            TKey identity = key(value);
            if (!index.TryAdd(identity, value))
            {
                throw new ArgumentException(
                    $"{kind} '{identity}' is duplicated.",
                    parameterName);
            }
        }
        return index;
    }

    public static void ValidateDefined<TEnum>(
        TEnum value,
        string parameterName)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"Unsupported {typeof(TEnum).Name} value.");
        }
    }

    private static IReadOnlyList<string> CopyStrings(
        IReadOnlyList<string> values,
        string parameterName,
        bool requireAny,
        string kind)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        if (requireAny && values.Count == 0)
        {
            throw new ArgumentException(
                $"At least one {kind.ToLowerInvariant()} is required.",
                parameterName);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var copy = new string[values.Count];
        for (int index = 0; index < values.Count; index++)
        {
            string value = values[index]
                ?? throw new ArgumentNullException(
                    parameterName,
                    $"{kind} {index + 1} is null.");
            ArgumentException.ThrowIfNullOrWhiteSpace(
                value,
                parameterName);
            if (!seen.Add(value))
            {
                throw new ArgumentException(
                    $"{kind} '{value}' is duplicated.",
                    parameterName);
            }
            copy[index] = value;
        }
        return Array.AsReadOnly(copy);
    }
}
