using ILInspector.Analysis;
using ILInspector.Analysis.Planning;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>Typed result of running Finding Unsafe Any for a host.</summary>
public abstract record FindingUnsafeAnyQueryResult
{
    private FindingUnsafeAnyQueryResult()
    {
    }

    /// <summary>The operation ran: an available answer or its inspection failure.</summary>
    public sealed record Inspected(FindingUnsafeAnyResult Answer)
        : FindingUnsafeAnyQueryResult;

    /// <summary>The image contains no managed metadata and therefore has no Types.</summary>
    public sealed record NoMetadata : FindingUnsafeAnyQueryResult;

    /// <summary>The operation could not be bound or run.</summary>
    public sealed record Failed(Exception Error) : FindingUnsafeAnyQueryResult;
}

/// <summary>
/// Runs Finding Unsafe Any over one opened image: the Library scope of the
/// <c>Unsafe Code</c> section, as
/// <c>docs/design/unsafe-code-section.md#execution</c> defines.
/// </summary>
public static class FindingUnsafeAnyQuery
{
    public static InspectionQuery<FindingUnsafeAnyQueryResult> Definition { get; } =
        new("Finding Unsafe Any", InspectionCost.Unbounded);

    /// <summary>The answer for every Type of the library.</summary>
    public static FindingUnsafeAnyQueryResult Execute(
        string path,
        PdbContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(context);
        if (!context.HasMetadata)
            return new FindingUnsafeAnyQueryResult.NoMetadata();

        try
        {
            using AssemblyInspectionSession session =
                AssemblyInspectionSession.Borrow(context);
            return new FindingUnsafeAnyQueryResult.Inspected(
                Run(session, path, FindingUnsafeAny.ForLibrary()));
        }
        catch (Exception ex)
        {
            return new FindingUnsafeAnyQueryResult.Failed(ex);
        }
    }

    static FindingUnsafeAnyResult Run(
        AssemblyInspectionSession session,
        string sourceName,
        MethodDefinitionSourceRequest<UnsafeCodeTypes> methodSource)
    {
        var operation = AssemblyAnalysisOperation<UnsafeCodeTypes>.Create(
            sourceName,
            methodSource.AsAssemblyAnalysisSource());
        return session.SnapshotOperation<
            AssemblyAnalysisOperation<UnsafeCodeTypes>,
            FindingUnsafeAnyResult>(
            operation,
            access =>
            {
                AssemblyAnalysisServiceResult<UnsafeCodeTypes> serviceResult =
                    AssemblyAnalysisService.Instance.Execute(
                        operation,
                        access);
                return serviceResult switch
                {
                    AssemblyAnalysisServiceResult<UnsafeCodeTypes>.Completed completed =>
                        FindingUnsafeAny.Project(completed.Execution),
                    AssemblyAnalysisServiceResult<UnsafeCodeTypes>.Rejected rejected =>
                        throw new InvalidOperationException(
                            "Finding Unsafe Any could not bind its "
                            + $"assembly analysis operation: {rejected.Kind}."),
                    _ => throw new InvalidOperationException(
                        "Unknown assembly analysis service result."),
                };
            });
    }
}
