using System.Collections.Immutable;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>Typed result of reading extension methods declared by an assembly.</summary>
public abstract record ExtensionMethodsResult
{
    private ExtensionMethodsResult()
    {
    }

    /// <summary>The declared extension methods, which may be empty.</summary>
    public sealed record Available(
        ImmutableArray<ExtensionMethodInfo> Methods) : ExtensionMethodsResult;

    /// <summary>The query failed while reading extension methods.</summary>
    public sealed record Failed(Exception Error) : ExtensionMethodsResult;
}

/// <summary>Reads declared extension methods from an already-open assembly session.</summary>
public static class ExtensionMethodsQuery
{
    public static InspectionQuery<ExtensionMethodsResult> Definition { get; } =
        new("Extension methods", InspectionCost.NetworkFree);

    public static ExtensionMethodsResult Execute(AssemblyInspectionSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        try
        {
            return new ExtensionMethodsResult.Available(
                session.ExtensionMethods().ToImmutableArray());
        }
        catch (Exception ex)
        {
            return new ExtensionMethodsResult.Failed(ex);
        }
    }
}
