using System.Collections.Immutable;

namespace ILInspector.Analysis;

internal sealed record LibraryBodyAnalysisResult(
    MethodBodyAnalysisResult Methods,
    SafetyAnalysisResult Safety,
    AllocationAnalysisResult Allocations,
    OptimizationAnalysisResult Optimizations,
    OwnershipFlowAnalysisResult OwnershipFlow,
    ResourceLifecycleAnalysisResult Resources,
    ImmutableArray<AnalysisDiagnostic> Diagnostics);

internal sealed record MethodBodyAnalysisResult(
    ImmutableArray<MethodIdentity> DeclaredMethods,
    ImmutableArray<MethodIdentity> Methods,
    ImmutableArray<DirectCall> DirectCalls,
    IReadOnlyDictionary<int, BodySignals> BodySignals,
    IReadOnlyDictionary<(string Namespace, string Name), bool> InAssemblyTypeIsException,
    IReadOnlySet<int> NonHeapNewObjOperandTokens,
    IReadOnlyDictionary<int, MethodIdentity> DeclaredSources);

internal sealed record SafetyAnalysisResult(
    ImmutableArray<UnsafeEvidence> Evidence,
    ImmutableArray<MethodIdentity> LeverageMethods,
    bool UpdatedRulesEnabled,
    UnsafeModeBreakdown Modes,
    IReadOnlyDictionary<int, ImmutableArray<UnsafetyOccurrence>> Occurrences);

internal sealed record AllocationAnalysisResult(
    IReadOnlyDictionary<int, ImmutableArray<AllocationOccurrence>> Occurrences);

internal sealed record OptimizationAnalysisResult(
    ImmutableArray<OptimizationOpportunity> Opportunities,
    IReadOnlySet<int> SuppressedMethodTokens,
    IReadOnlySet<string> ExceptionTypeNames);

internal sealed record ResourceLifecycleAnalysisResult(
    LeakTriageResult? LeakTriage);

internal sealed record OwnershipFlowAnalysisResult(
    ImmutableArray<ArrayPoolOwnershipMethodEvidence> Methods);
