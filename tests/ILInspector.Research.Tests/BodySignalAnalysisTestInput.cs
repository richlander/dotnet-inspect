using ILInspector.Analysis;

namespace ILInspector.Research.Tests;

internal static class BodySignalAnalysisTestInput
{
    internal static BodySignalAnalysisInput FromIndex(
        LibraryBodyAnalysisExecution index)
    {
        var receipt = new LibraryBodyAnalysisReceipt(
            index.Receipt.SourceName,
            index.Receipt.ModuleIdentity,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.Allocations
                | LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities,
            HasFullMethodEvidenceScope: true,
            index.Receipt.Diagnostics);
        return new(
            receipt,
            index.CallGraph,
            index.CallGraph.Methods,
            index.CompatibilityIndex().GeneratedFrameworkTypes,
            index.CallGraph.MethodSignals,
            index.Allocations.Occurrences,
            index.CallGraph.DirectCallsByEvidenceMethod,
            index.Safety.Occurrences,
            index.Safety.GetEvidenceByMember(),
            index.Optimization.Opportunities);
    }
}
