using System.Collections.Immutable;
using ILInspector.Analysis;

namespace ILInspector.Research;

/// <summary>
/// Focused Analysis evidence consumed by one side of a body-signal comparison.
/// </summary>
public sealed class BodySignalAnalysisInput
{
    readonly Func<IReadOnlyDictionary<
        int,
        ImmutableArray<DirectCall>>> _callsByEvidenceMethod;
    readonly Func<IReadOnlyDictionary<
        int,
        ImmutableArray<UnsafeEvidence>>> _unsafeEvidenceByMember;

    public BodySignalAnalysisInput(
        LibraryAllocationAnalysisResult allocations,
        LibrarySafetyAnalysisResult safety,
        LibraryCallGraphAnalysisResult callGraph,
        LibraryOptimizationAnalysisResult optimization)
    {
        ArgumentNullException.ThrowIfNull(allocations);
        ArgumentNullException.ThrowIfNull(safety);
        ArgumentNullException.ThrowIfNull(callGraph);
        ArgumentNullException.ThrowIfNull(optimization);
        if (!ReferenceEquals(allocations.Receipt, safety.Receipt)
            || !ReferenceEquals(allocations.Receipt, callGraph.Receipt)
            || !ReferenceEquals(
                allocations.Receipt,
                optimization.Receipt))
        {
            throw new ArgumentException(
                "Body-signal Analysis results must come from one "
                    + "execution receipt.");
        }
        if (!callGraph.HasFullMethodEvidenceScope)
        {
            throw new ArgumentException(
                "Body-signal comparison requires full method-evidence "
                    + "scope.");
        }
        if (!allocations.WasRequested)
        {
            throw new ArgumentException(
                "Body-signal comparison requires allocation evidence.");
        }
        if (!optimization.WasRequested)
        {
            throw new ArgumentException(
                "Body-signal comparison requires optimization evidence.");
        }

        Receipt = allocations.Receipt;
        MethodPopulation = callGraph;
        Methods = callGraph.Methods;
        GeneratedFrameworkTypes =
            optimization.GeneratedFrameworkTypes;
        MethodSignals = callGraph.MethodSignals;
        AllocationOccurrences = allocations.Occurrences;
        UnsafetyOccurrences = safety.Occurrences;
        Opportunities = optimization.Opportunities;
        _callsByEvidenceMethod =
            () => callGraph.DirectCallsByEvidenceMethod;
        _unsafeEvidenceByMember = safety.GetEvidenceByMember;
    }

    internal BodySignalAnalysisInput(
        LibraryBodyAnalysisReceipt receipt,
        LibraryCallGraphAnalysisResult methodPopulation,
        ImmutableArray<MethodIdentity> methods,
        IReadOnlySet<TypeRef> generatedFrameworkTypes,
        IReadOnlyDictionary<int, MethodSignals> methodSignals,
        IReadOnlyDictionary<
            int,
            ImmutableArray<AllocationOccurrence>>
            allocationOccurrences,
        IReadOnlyDictionary<
            int,
            ImmutableArray<DirectCall>>
            callsByEvidenceMethod,
        IReadOnlyDictionary<
            int,
            ImmutableArray<UnsafetyOccurrence>>
            unsafetyOccurrences,
        IReadOnlyDictionary<
            int,
            ImmutableArray<UnsafeEvidence>>
            unsafeEvidenceByMember,
        ImmutableArray<OptimizationOpportunity> opportunities)
    {
        Receipt = receipt;
        MethodPopulation = methodPopulation;
        Methods = methods;
        GeneratedFrameworkTypes = generatedFrameworkTypes;
        MethodSignals = methodSignals;
        AllocationOccurrences = allocationOccurrences;
        UnsafetyOccurrences = unsafetyOccurrences;
        Opportunities = opportunities;
        _callsByEvidenceMethod = () => callsByEvidenceMethod;
        _unsafeEvidenceByMember = () => unsafeEvidenceByMember;
    }

    public LibraryBodyAnalysisReceipt Receipt { get; }

    /// <summary>
    /// The Analysis method population these focused results were built from,
    /// carried for now by the call-graph result.
    /// </summary>
    /// <remarks>
    /// This is an interim carrier. Research reads only its declared methods,
    /// module identity, diagnostics, and source name.
    /// </remarks>
    public LibraryCallGraphAnalysisResult MethodPopulation { get; }

    internal ImmutableArray<MethodIdentity> Methods { get; }

    internal IReadOnlySet<TypeRef> GeneratedFrameworkTypes { get; }

    internal IReadOnlyDictionary<int, MethodSignals>
        MethodSignals { get; }

    internal IReadOnlyDictionary<
        int,
        ImmutableArray<AllocationOccurrence>>
        AllocationOccurrences { get; }

    internal IReadOnlyDictionary<
        int,
        ImmutableArray<DirectCall>>
        CallsByEvidenceMethod => _callsByEvidenceMethod();

    internal IReadOnlyDictionary<
        int,
        ImmutableArray<UnsafetyOccurrence>>
        UnsafetyOccurrences { get; }

    internal IReadOnlyDictionary<
        int,
        ImmutableArray<UnsafeEvidence>>
        UnsafeEvidenceByMember => _unsafeEvidenceByMember();

    internal ImmutableArray<OptimizationOpportunity>
        Opportunities { get; }
}
