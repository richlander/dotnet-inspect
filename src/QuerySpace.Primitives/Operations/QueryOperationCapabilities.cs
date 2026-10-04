using QuerySpace.Rows;

namespace QuerySpace.Operations;

public sealed class QueryOperationTermCapability
{
    internal QueryOperationTermCapability(
        QueryOperationTermBinding binding,
        IReadOnlyList<PortableQueryOperator> operators)
    {
        Binding = binding;
        Operators = operators;
    }

    public QueryOperationTermBinding Binding { get; }

    public IReadOnlyList<PortableQueryOperator> Operators { get; }
}

public sealed class QueryOperationOrderCapability
{
    internal QueryOperationOrderCapability(
        QueryOperationOrderBinding binding,
        IReadOnlyList<QueryOperationOrderRole> roles)
    {
        Binding = binding;
        Roles = roles;
        Directions = Array.AsReadOnly(
            new[]
            {
                PortableQueryDirection.Ascending,
                PortableQueryDirection.Descending,
            });
    }

    public QueryOperationOrderBinding Binding { get; }

    public IReadOnlyList<QueryOperationOrderRole> Roles { get; }

    public IReadOnlyList<PortableQueryDirection> Directions { get; }
}

public sealed class QueryOperationRouteCapabilities
{
    internal QueryOperationRouteCapabilities(
        string vocabulary,
        IReadOnlyList<QueryOperationTermCapability> terms,
        IReadOnlyList<QueryOperationOrderCapability> orders,
        IReadOnlyList<string> dimensions,
        IReadOnlyList<RowSelectionStageKind> stages)
    {
        Vocabulary = vocabulary;
        Terms = terms;
        Orders = orders;
        Dimensions = dimensions;
        Stages = stages;
    }

    public string Vocabulary { get; }

    public IReadOnlyList<QueryOperationTermCapability> Terms { get; }

    public IReadOnlyList<QueryOperationOrderCapability> Orders { get; }

    public IReadOnlyList<string> Dimensions { get; }

    public IReadOnlyList<RowSelectionStageKind> Stages { get; }
}

public interface IQueryOperationRoute
{
    string Identity { get; }

    string OperationIdentity { get; }

    string SubjectRole { get; }

    string ResultGrain { get; }

    IReadOnlyList<string> RowSets { get; }

    string ProfileIdentity { get; }

    QueryOperationRouteCapabilities Capabilities { get; }
}
