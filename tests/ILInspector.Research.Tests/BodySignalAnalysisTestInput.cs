using ILInspector.Analysis;

namespace ILInspector.Research.Tests;

internal static class BodySignalAnalysisTestInput
{
    internal static BodySignalAnalysisInput FromExecution(
        LibraryBodyAnalysisExecution execution)
    {
        var receipt = new LibraryBodyAnalysisReceipt(
            execution.Receipt.SourceName,
            execution.Receipt.ModuleIdentity,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.Allocations
                | LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities,
            HasFullMethodEvidenceScope: true,
            execution.Receipt.Diagnostics);
        return new(
            receipt,
            execution.CallGraph,
            execution.CallGraph.Methods,
            execution.Optimization.GeneratedFrameworkTypes,
            execution.CallGraph.MethodSignals,
            execution.Allocations.Occurrences,
            execution.CallGraph.DirectCallsByEvidenceMethod,
            execution.Safety.Occurrences,
            execution.Safety.GetEvidenceByMember(),
            execution.Optimization.Opportunities);
    }
}
