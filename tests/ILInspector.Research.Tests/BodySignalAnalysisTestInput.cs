using ILInspector.Analysis;

namespace ILInspector.Research.Tests;

internal static class BodySignalAnalysisTestInput
{
    internal static BodySignalAnalysisInput FromIndex(
        LibraryBodyIndex index)
    {
        var receipt = new LibraryBodyAnalysisReceipt(
            index.Path,
            index.ModuleIdentity,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.Allocations
                | LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities,
            HasFullMethodEvidenceScope: true,
            index.Diagnostics);
        return new(
            receipt,
            index.CallGraphAnalysis,
            index.Methods,
            index.GeneratedFrameworkTypes,
            index.GetMethodSignals(),
            index.GetAllocationOccurrences(),
            index.GetDirectCallsByEvidenceMethod(),
            index.GetUnsafetyOccurrences(),
            index.GetUnsafeEvidenceByMember(),
            index.OptimizationOpportunities);
    }
}
