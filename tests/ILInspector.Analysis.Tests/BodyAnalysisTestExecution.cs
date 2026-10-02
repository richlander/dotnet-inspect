using System.Collections.Immutable;

using ILInspector.Analysis;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

internal static class BodyAnalysisTestExecution
{
    public static LibraryBodyAnalysisExecution Open(string path) =>
        LibraryBodyAnalysisService.ExecutePath(
            path,
            LibraryBodyAnalysisRequest.Create(
                LibraryBodyAnalysisFeatures.Default));

    public static LibraryBodyAnalysisExecution Open(
        string path,
        IAssemblyReferenceResolver? resolver = null,
        bool includeAllocations = true,
        bool includeOpportunities = true,
        IReadOnlySet<int>? bodyScope = null,
        Func<TypeRef, bool>? bodyTypeScope = null)
    {
        LibraryBodyAnalysisFeatures features =
            LibraryBodyAnalysisFeatures.MethodEvidence;
        if (includeAllocations)
            features |= LibraryBodyAnalysisFeatures.Allocations;
        if (includeOpportunities)
        {
            features |=
                LibraryBodyAnalysisFeatures.OptimizationOpportunities;
        }

        return LibraryBodyAnalysisService.ExecutePath(
            path,
            LibraryBodyAnalysisRequest.Create(
                features,
                bodyScope,
                bodyTypeScope),
            resolver);
    }

    public static LibraryBodyAnalysisExecution Open(
        string path,
        LibraryBodyAnalysisFeatures features,
        IAssemblyReferenceResolver? resolver = null,
        IReadOnlySet<int>? bodyScope = null,
        Func<TypeRef, bool>? bodyTypeScope = null) =>
        LibraryBodyAnalysisService.ExecutePath(
            path,
            LibraryBodyAnalysisRequest.Create(
                features,
                bodyScope,
                bodyTypeScope),
            resolver);

    public static LibraryBodyAnalysisExecution OpenFromPrefetchedImage(
        string path,
        ImmutableArray<byte> image,
        LibraryBodyAnalysisFeatures features,
        IAssemblyReferenceResolver? resolver = null,
        IReadOnlySet<int>? bodyScope = null,
        Func<TypeRef, bool>? bodyTypeScope = null) =>
        LibraryBodyAnalysisService.ExecuteImage(
            path,
            image,
            LibraryBodyAnalysisRequest.Create(
                features,
                bodyScope,
                bodyTypeScope),
            resolver);

    public static LibraryBodyAnalysisExecution FromEvidence(
        ImmutableArray<MethodIdentity> methods,
        ImmutableArray<UnsafeEvidence> unsafeEvidence,
        IReadOnlyDictionary<
            int,
            ImmutableArray<AllocationOccurrence>>?
            allocationOccurrences = null,
        IReadOnlyDictionary<
            int,
            ImmutableArray<UnsafetyOccurrence>>?
            unsafetyOccurrences = null,
        ImmutableArray<AnalysisDiagnostic> diagnostics = default,
        ImmutableArray<DirectCall> directCalls = default,
        ImmutableArray<MethodResultSink> resultSinks = default,
        ImmutableArray<FieldStoreFact> fieldStores = default,
        ImmutableArray<FieldLoadFact> fieldLoads = default,
        ImmutableArray<MethodReturnFlow> returnFlows = default,
        LibraryBodyModuleIdentity? moduleIdentity = null,
        LibraryBodyAnalysisFeatures features =
            LibraryBodyAnalysisFeatures.MethodEvidence) =>
        LibraryBodyAnalysisExecution.FromEvidence(
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
            features);
}
