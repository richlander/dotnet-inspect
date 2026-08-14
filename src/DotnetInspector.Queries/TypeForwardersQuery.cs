using System.Collections.Immutable;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>Typed result of reading an assembly's type forwarders.</summary>
public abstract record TypeForwardersResult
{
    private TypeForwardersResult()
    {
    }

    /// <summary>The type forwarders, in metadata order, which may be empty.</summary>
    public sealed record Available(
        ImmutableArray<TypeForwarderInfo> Forwarders) : TypeForwardersResult;

    /// <summary>The query failed while reading type forwarders.</summary>
    public sealed record Failed(Exception Error) : TypeForwardersResult;
}

/// <summary>Reads type forwarders from an already-open assembly session.</summary>
public static class TypeForwardersQuery
{
    public static InspectionQuery<TypeForwardersResult> Definition { get; } =
        new("Type forwarders", InspectionCost.NetworkFree);

    public static TypeForwardersResult Execute(AssemblyInspectionSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        try
        {
            return new TypeForwardersResult.Available(
                session.TypeForwarders().ToImmutableArray());
        }
        catch (Exception ex)
        {
            return new TypeForwardersResult.Failed(ex);
        }
    }
}
