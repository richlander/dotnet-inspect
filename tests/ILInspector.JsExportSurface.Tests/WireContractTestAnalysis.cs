using System.Collections.Immutable;
using ILInspector.Analysis;

namespace ILInspector.JsExportSurface.Tests;

internal static class WireContractTestAnalysis
{
    public static LibraryJsonWireContractAnalysisResult Open(
        string path,
        LibraryBodyAnalysisFeatures features) =>
        LibraryBodyAnalysisService.ExecutePath(
            path,
            LibraryBodyAnalysisRequest.Create(
                features
                    | LibraryBodyAnalysisFeatures.JsonWireContractFlow))
            .JsonWireContracts;

    public static LibraryJsonWireContractAnalysisResult FromEvidence(
        ImmutableArray<MethodIdentity> methods,
        ImmutableArray<UnsafeEvidence> unsafeEvidence,
        IReadOnlyDictionary<int, ImmutableArray<AllocationOccurrence>>?
            allocationOccurrences = null,
        IReadOnlyDictionary<int, ImmutableArray<UnsafetyOccurrence>>?
            unsafetyOccurrences = null,
        ImmutableArray<AnalysisDiagnostic> diagnostics = default,
        ImmutableArray<DirectCall> directCalls = default,
        ImmutableArray<MethodResultSink> resultSinks = default,
        ImmutableArray<FieldStoreFact> fieldStores = default,
        ImmutableArray<FieldLoadFact> fieldLoads = default,
        ImmutableArray<MethodReturnFlow> returnFlows = default,
        LibraryBodyModuleIdentity? moduleIdentity = null) =>
        LibraryBodyIndex.FromEvidence(
            methods,
            unsafeEvidence,
            allocationOccurrences,
            unsafetyOccurrences,
            diagnostics,
            directCalls,
            resultSinks,
            fieldStores,
            fieldLoads,
            returnFlows,
            moduleIdentity,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.JsonWireContractFlow)
            .JsonWireContracts;
}
