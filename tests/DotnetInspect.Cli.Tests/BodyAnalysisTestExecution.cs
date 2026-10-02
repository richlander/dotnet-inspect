using Analysis = ILInspector.Analysis;

namespace DotnetInspect.Cli.Tests;

internal static class BodyAnalysisTestExecution
{
    public static Analysis.LibraryBodyAnalysisExecution Open(
        string path,
        ILInspector.Metadata.IAssemblyReferenceResolver? resolver = null,
        bool includeAllocations = true,
        bool includeOpportunities = true,
        IReadOnlySet<int>? bodyScope = null,
        Func<Analysis.TypeRef, bool>? bodyTypeScope = null)
    {
        Analysis.LibraryBodyAnalysisFeatures features =
            Analysis.LibraryBodyAnalysisFeatures.MethodEvidence;
        if (includeAllocations)
            features |= Analysis.LibraryBodyAnalysisFeatures.Allocations;
        if (includeOpportunities)
        {
            features |=
                Analysis.LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities;
        }

        return Analysis.LibraryBodyAnalysisService.ExecutePath(
            path,
            Analysis.LibraryBodyAnalysisRequest.Create(
                features,
                bodyScope,
                bodyTypeScope),
            resolver);
    }
}
