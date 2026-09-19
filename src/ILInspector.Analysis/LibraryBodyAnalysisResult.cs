using System.Collections.Immutable;
using ILInspector.Metadata;

namespace ILInspector.Analysis;

internal sealed record LibraryBodyAnalysisResult(
    MethodBodyAnalysisResult Methods,
    SafetyAnalysisResult Safety,
    AllocationAnalysisResult Allocations,
    OptimizationAnalysisResult Optimizations,
    OwnershipFlowAnalysisResult OwnershipFlow,
    LegacyResourceLifecycleAnalysisResult Resources,
    ImmutableArray<AnalysisDiagnostic> Diagnostics)
{
    internal ResourceOccurrenceLibraryAnalysisResult? ResourceOccurrences
    { get; init; }

    internal ResourceLifecycleLibraryAnalysisResult? ResourceLifecycles
    { get; init; }
}

internal sealed record MethodBodyAnalysisResult(
    ImmutableArray<MethodIdentity> DeclaredMethods,
    ImmutableArray<MethodIdentity> Methods,
    ImmutableArray<DirectCall> DirectCalls,
    ImmutableArray<MethodResultSink> ResultSinks,
    ImmutableArray<FieldStoreFact> FieldStores,
    ImmutableArray<FieldLoadFact> FieldLoads,
    ImmutableArray<MethodReturnFlow> ReturnFlows,
    IReadOnlyDictionary<int, BodySignals> BodySignals,
    ImmutableArray<MethodBodyImplementationMetrics> ImplementationProfiles,
    IReadOnlyDictionary<(string Namespace, string Name), bool> InAssemblyTypeIsException,
    IReadOnlySet<int> NonHeapNewObjOperandTokens,
    IReadOnlyDictionary<int, MethodIdentity> DeclaredSources,
    ImmutableArray<MethodLocalThrowEvidence> LocalThrows);

internal sealed record SafetyAnalysisResult(
    ImmutableArray<UnsafeEvidence> Evidence,
    ImmutableArray<MethodIdentity> LeverageMethods,
    MemorySafetyRulesResult Rules,
    UnsafeModeBreakdown Modes,
    IReadOnlyDictionary<int, ImmutableArray<UnsafetyOccurrence>> Occurrences);

internal sealed record AllocationAnalysisResult(
    IReadOnlyDictionary<int, ImmutableArray<AllocationOccurrence>> Occurrences);

internal sealed record OptimizationAnalysisResult(
    ImmutableArray<OptimizationOpportunity> Opportunities,
    IReadOnlySet<int> SuppressedMethodTokens,
    IReadOnlySet<int> ScopeExcludedMethodTokens,
    IReadOnlySet<string> ExceptionTypeNames);

internal sealed record LegacyResourceLifecycleAnalysisResult(
    LeakTriageResult? LeakTriage);

internal sealed record OwnershipFlowAnalysisResult(
    ImmutableArray<ArrayPoolOwnershipMethodEvidence> Methods);

internal sealed record ResourceOccurrenceLibraryAnalysisResult(
    ImmutableArray<ResourceOccurrenceAnalysisResult> Methods,
    ImmutableArray<ResourceOccurrenceLimitation> Limitations);

internal sealed record ResourceLifecycleLibraryAnalysisResult(
    ImmutableArray<ResourceLifecycleMethodAnalysisResult> Methods,
    ImmutableArray<ResourceLifecycleLimitation> Limitations);
