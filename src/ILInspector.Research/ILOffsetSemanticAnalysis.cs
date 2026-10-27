using ILInspector.Metadata;

namespace ILInspector.Research;

public sealed record ILOffsetSemanticAnalysisInput(
    ILOffsetAllocationAnalysisInput? Allocation,
    ILOffsetSafetyAnalysisInput? Safety,
    ILOffsetCostAnalysisInput? Cost);

/// <summary>
/// Composes focused coordinate evidence over one exact MethodDef population.
/// </summary>
public static class ILOffsetSemanticAnalysis
{
    public static ILOffsetSemanticAnalysisInput Execute(
        AssemblyInspectionSession session,
        string subjectName,
        Guid moduleVersionId,
        IEnumerable<int> methodTokens,
        ILOffsetProjectionCapabilities capabilities,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectName);
        ArgumentNullException.ThrowIfNull(methodTokens);
        cancellationToken.ThrowIfCancellationRequested();

        int[] methods = [.. methodTokens.Distinct()];
        if (methods.Length == 0)
        {
            throw new ArgumentException(
                "IL-offset semantic analysis requires at least one MethodDef.",
                nameof(methodTokens));
        }

        return new(
            capabilities.HasFlag(
                ILOffsetProjectionCapabilities.AllocationContext)
                ? ILOffsetAllocationAnalysis.Execute(
                    session,
                    subjectName,
                    moduleVersionId,
                    methods,
                    cancellationToken)
                : null,
            capabilities.HasFlag(
                ILOffsetProjectionCapabilities.SafetyContext)
                ? ILOffsetSafetyAnalysis.Execute(
                    session,
                    subjectName,
                    moduleVersionId,
                    methods,
                    cancellationToken)
                : null,
            capabilities.HasFlag(
                ILOffsetProjectionCapabilities.CostContext)
                ? ILOffsetCostAnalysis.Execute(
                    session,
                    subjectName,
                    moduleVersionId,
                    methods,
                    cancellationToken)
                : null);
    }
}
