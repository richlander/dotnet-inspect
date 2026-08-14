using System.Collections.Immutable;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>Typed result of reading an assembly's discriminated-union types.</summary>
public abstract record UnionTypesResult
{
    private UnionTypesResult()
    {
    }

    /// <summary>The union types, in metadata order, which may be empty.</summary>
    public sealed record Available(ImmutableArray<UnionTypeInfo> Unions) : UnionTypesResult;

    /// <summary>The query failed while reading union types.</summary>
    public sealed record Failed(Exception Error) : UnionTypesResult;
}

/// <summary>Reads discriminated-union types from an already-open assembly session.</summary>
public static class UnionTypesQuery
{
    public static InspectionQuery<UnionTypesResult> Definition { get; } =
        new("Union types", InspectionCost.NetworkFree);

    public static UnionTypesResult Execute(AssemblyInspectionSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        try
        {
            return new UnionTypesResult.Available(session.UnionTypes().ToImmutableArray());
        }
        catch (Exception ex)
        {
            return new UnionTypesResult.Failed(ex);
        }
    }
}
