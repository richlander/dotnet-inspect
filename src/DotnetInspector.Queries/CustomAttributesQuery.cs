using System.Collections.Immutable;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>Typed result of reading assembly-level and module-level custom attributes.</summary>
public abstract record CustomAttributesResult
{
    private CustomAttributesResult()
    {
    }

    /// <summary>The custom attributes, in metadata order, which may be empty.</summary>
    public sealed record Available(
        ImmutableArray<AssemblyAttributeInfo> Attributes) : CustomAttributesResult;

    /// <summary>The query failed while reading custom attributes.</summary>
    public sealed record Failed(Exception Error) : CustomAttributesResult;
}

/// <summary>Reads custom attributes from an already-open assembly session.</summary>
public static class CustomAttributesQuery
{
    public static InspectionQuery<CustomAttributesResult> Definition { get; } =
        new("Custom attributes", InspectionCost.NetworkFree);

    public static CustomAttributesResult Execute(AssemblyInspectionSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        try
        {
            return new CustomAttributesResult.Available(
                session.CustomAttributes().ToImmutableArray());
        }
        catch (Exception ex)
        {
            return new CustomAttributesResult.Failed(ex);
        }
    }
}
