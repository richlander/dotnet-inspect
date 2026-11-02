using System.Reflection.Metadata;

using ILInspector.Analysis;
using ILInspector.Analysis.Planning;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>Typed result of running the unsafe member census for a host.</summary>
public abstract record UnsafeMemberCensusQueryResult
{
    private UnsafeMemberCensusQueryResult()
    {
    }

    /// <summary>The census operation ran: an available census or its inspection failure.</summary>
    public sealed record Inspected(UnsafeMemberCensusResult Census)
        : UnsafeMemberCensusQueryResult;

    /// <summary>The image contains no managed metadata and therefore has no members.</summary>
    public sealed record NoMetadata : UnsafeMemberCensusQueryResult;

    /// <summary>The census operation could not be bound or run.</summary>
    public sealed record Failed(Exception Error) : UnsafeMemberCensusQueryResult;
}

/// <summary>
/// Runs the unsafe member census over one opened image at Library, Type, or
/// Member scope, as <c>docs/design/unsafe-members-section.md#execution</c> defines.
/// </summary>
public static class UnsafeMemberCensusQuery
{
    public static InspectionQuery<UnsafeMemberCensusQueryResult> Definition { get; } =
        new("Unsafe member census", InspectionCost.Unbounded);

    /// <summary>The census over every method definition of the library.</summary>
    public static UnsafeMemberCensusQueryResult Execute(
        string path,
        PdbContext context)
        => Execute(path, context, UnsafeMemberCensusAnalysis.ForLibrary());

    /// <summary>The census over the selected types and their generated bodies.</summary>
    public static UnsafeMemberCensusQueryResult ExecuteExactTypes(
        string path,
        PdbContext context,
        params TypeDefinitionHandle[] types)
    {
        ArgumentNullException.ThrowIfNull(types);
        return Execute(
            path,
            context,
            UnsafeMemberCensusAnalysis.ForTypes(types));
    }

    /// <summary>
    /// The census over every method definition of an image the caller holds
    /// open, named <paramref name="sourceName"/> in diagnostics.
    /// </summary>
    public static UnsafeMemberCensusQueryResult Execute(
        AssemblyInspectionSession session,
        string sourceName)
        => Execute(session, sourceName, UnsafeMemberCensusAnalysis.ForLibrary());

    /// <summary>
    /// The census over the selected types of an image the caller holds open.
    /// </summary>
    public static UnsafeMemberCensusQueryResult ExecuteExactTypes(
        AssemblyInspectionSession session,
        string sourceName,
        params TypeDefinitionHandle[] types)
    {
        ArgumentNullException.ThrowIfNull(types);
        return Execute(
            session,
            sourceName,
            UnsafeMemberCensusAnalysis.ForTypes(types));
    }

    /// <summary>
    /// The census over one declared member's method definitions of an image
    /// the caller holds open.
    /// </summary>
    public static UnsafeMemberCensusQueryResult ExecuteExactMethods(
        AssemblyInspectionSession session,
        string sourceName,
        params MethodDefinitionHandle[] methods)
    {
        ArgumentNullException.ThrowIfNull(methods);
        return Execute(
            session,
            sourceName,
            UnsafeMemberCensusAnalysis.ForMethods(methods));
    }

    static UnsafeMemberCensusQueryResult Execute(
        string path,
        PdbContext context,
        MethodDefinitionSourceRequest<UnsafeMemberCensus> methodSource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(context);
        if (!context.HasMetadata)
            return new UnsafeMemberCensusQueryResult.NoMetadata();

        try
        {
            using AssemblyInspectionSession session =
                AssemblyInspectionSession.Borrow(context);
            return new UnsafeMemberCensusQueryResult.Inspected(
                Run(session, path, methodSource));
        }
        catch (Exception ex)
        {
            return new UnsafeMemberCensusQueryResult.Failed(ex);
        }
    }

    static UnsafeMemberCensusQueryResult Execute(
        AssemblyInspectionSession session,
        string sourceName,
        MethodDefinitionSourceRequest<UnsafeMemberCensus> methodSource)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        if (!session.HasMetadata)
            return new UnsafeMemberCensusQueryResult.NoMetadata();

        try
        {
            return new UnsafeMemberCensusQueryResult.Inspected(
                Run(session, sourceName, methodSource));
        }
        catch (Exception ex)
        {
            return new UnsafeMemberCensusQueryResult.Failed(ex);
        }
    }

    static UnsafeMemberCensusResult Run(
        AssemblyInspectionSession session,
        string sourceName,
        MethodDefinitionSourceRequest<UnsafeMemberCensus> methodSource)
    {
        var operation = AssemblyAnalysisOperation<UnsafeMemberCensus>.Create(
            sourceName,
            methodSource.AsAssemblyAnalysisSource());
        return session.SnapshotOperation<
            AssemblyAnalysisOperation<UnsafeMemberCensus>,
            UnsafeMemberCensusResult>(
            operation,
            access =>
            {
                AssemblyAnalysisServiceResult<UnsafeMemberCensus> serviceResult =
                    AssemblyAnalysisService.Instance.Execute(
                        operation,
                        access);
                return serviceResult switch
                {
                    AssemblyAnalysisServiceResult<UnsafeMemberCensus>.Completed completed =>
                        UnsafeMemberCensusAnalysis.Project(completed.Execution),
                    AssemblyAnalysisServiceResult<UnsafeMemberCensus>.Rejected rejected =>
                        throw new InvalidOperationException(
                            "The unsafe member census could not bind its "
                            + $"assembly analysis operation: {rejected.Kind}."),
                    _ => throw new InvalidOperationException(
                        "Unknown assembly analysis service result."),
                };
            });
    }
}
