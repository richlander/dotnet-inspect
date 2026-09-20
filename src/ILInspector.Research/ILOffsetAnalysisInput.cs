using System.Collections.Immutable;
using ILInspector.Analysis;

namespace ILInspector.Research;

/// <summary>
/// Exact focused Analysis results consumed by IL-offset projection.
/// </summary>
public sealed class ILOffsetAnalysisInput
{
    readonly Lazy<IReadOnlyDictionary<
        int,
        ImmutableArray<DirectCall>>> _callsByEvidenceMethod;
    readonly Lazy<IReadOnlyDictionary<
        int,
        ImmutableArray<UnsafeEvidence>>> _unsafeEvidenceByToken;

    public ILOffsetAnalysisInput(
        LibraryAllocationAnalysisResult allocations,
        LibrarySafetyAnalysisResult safety,
        LibraryCallGraphAnalysisResult callGraph)
    {
        ArgumentNullException.ThrowIfNull(allocations);
        ArgumentNullException.ThrowIfNull(safety);
        ArgumentNullException.ThrowIfNull(callGraph);
        if (!ReferenceEquals(allocations.Receipt, safety.Receipt)
            || !ReferenceEquals(
                allocations.Receipt,
                callGraph.Receipt))
        {
            throw new ArgumentException(
                "IL-offset Analysis results must come from one "
                    + "execution receipt.");
        }

        Allocations = allocations;
        Safety = safety;
        CallGraph = callGraph;
        _callsByEvidenceMethod = new(() => callGraph.DirectCalls
            .GroupBy(call => call.EvidenceMethod.MetadataToken)
            .ToDictionary(
                group => group.Key,
                group => group.ToImmutableArray()));
        _unsafeEvidenceByToken = new(() => safety.Evidence
            .GroupBy(evidence => evidence.Member.MetadataToken)
            .ToDictionary(
                group => group.Key,
                group => group.ToImmutableArray()));
    }

    public LibraryAllocationAnalysisResult Allocations { get; }
    public LibrarySafetyAnalysisResult Safety { get; }
    public LibraryCallGraphAnalysisResult CallGraph { get; }

    public LibraryBodyAnalysisReceipt Receipt =>
        Allocations.Receipt;

    internal IReadOnlyDictionary<
        int,
        ImmutableArray<DirectCall>> CallsByEvidenceMethod =>
        _callsByEvidenceMethod.Value;

    internal IReadOnlyDictionary<
        int,
        ImmutableArray<UnsafeEvidence>> UnsafeEvidenceByToken =>
        _unsafeEvidenceByToken.Value;
}
