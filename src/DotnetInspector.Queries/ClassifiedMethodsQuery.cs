using System.Collections.Immutable;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>Typed result of classifying an assembly's public methods.</summary>
public abstract record ClassifiedMethodsResult
{
    private ClassifiedMethodsResult()
    {
    }

    /// <summary>The classified methods, in metadata order, which may be empty.</summary>
    public sealed record Available(
        ImmutableArray<ClassifiedMethodInfo> Methods) : ClassifiedMethodsResult;

    /// <summary>The query failed while classifying methods.</summary>
    public sealed record Failed(Exception Error) : ClassifiedMethodsResult;
}

/// <summary>Classifies public methods from an already-open assembly session.</summary>
public static class ClassifiedMethodsQuery
{
    public static InspectionQuery<ClassifiedMethodsResult> Definition { get; } =
        new("Classified methods", InspectionCost.NetworkFree);

    public static ClassifiedMethodsResult Execute(AssemblyInspectionSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        try
        {
            return new ClassifiedMethodsResult.Available(
                session.ClassifiedMethods().ToImmutableArray());
        }
        catch (Exception ex)
        {
            return new ClassifiedMethodsResult.Failed(ex);
        }
    }
}
