using ILInspector.Metadata;

namespace ILInspector.Research;

/// <summary>
/// Runs cost-only IL-coordinate preparation through one exact Method-source
/// operation over an already-open assembly image.
/// </summary>
public static class ILOffsetCostAnalysis
{
    public static ILOffsetCostAnalysisInput Execute(
        AssemblyInspectionSession session,
        string subjectName,
        Guid moduleVersionId,
        IEnumerable<int> methodTokens,
        CancellationToken cancellationToken = default)
    {
        return ILOffsetSemanticAnalysis.Execute(
                session,
                subjectName,
                moduleVersionId,
                methodTokens,
                ILOffsetProjectionCapabilities.CostContext,
                cancellationToken)
            .Cost!;
    }
}
