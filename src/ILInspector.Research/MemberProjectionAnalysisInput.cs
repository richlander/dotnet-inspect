using System.Collections.Immutable;

using ILInspector.Analysis;
using Inspector.Findings;

namespace ILInspector.Research;

/// <summary>
/// Exact focused Analysis results consumed by member Research fact producers.
/// </summary>
public sealed class MemberProjectionAnalysisInput
{
    readonly Lazy<IReadOnlyDictionary<int, MethodLeverage>>
        _leverageByToken;
    readonly Lazy<IReadOnlyDictionary<int, IReadOnlyList<DirectCall>>>
        _callsByEvidenceMethod;
    readonly Lazy<IReadOnlyDictionary<int, IReadOnlyList<UnsafeEvidence>>>
        _unsafeEvidenceByToken;

    public MemberProjectionAnalysisInput(
        LibraryAllocationAnalysisResult allocations,
        LibrarySafetyAnalysisResult safety,
        LibraryCallGraphAnalysisResult callGraph,
        LibraryLeverageAnalysisResult leverage)
    {
        ArgumentNullException.ThrowIfNull(allocations);
        ArgumentNullException.ThrowIfNull(safety);
        ArgumentNullException.ThrowIfNull(callGraph);
        ArgumentNullException.ThrowIfNull(leverage);
        if (!ReferenceEquals(allocations.Receipt, safety.Receipt)
            || !ReferenceEquals(allocations.Receipt, callGraph.Receipt)
            || !ReferenceEquals(allocations.Receipt, leverage.Receipt))
        {
            throw new ArgumentException(
                "Member projection Analysis results must come from one "
                    + "execution receipt.");
        }

        Allocations = allocations;
        Safety = safety;
        CallGraph = callGraph;
        Leverage = leverage;
        _leverageByToken = new(() => leverage.Top(int.MaxValue)
            .ToDictionary(
                entry => entry.Method.MetadataToken,
                entry => entry));
        _callsByEvidenceMethod = new(() => callGraph.DirectCalls
            .GroupBy(call => call.EvidenceMethod.MetadataToken)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<DirectCall>)
                    group.ToImmutableArray()));
        _unsafeEvidenceByToken = new(() => safety.Evidence
            .GroupBy(evidence => evidence.Member.MetadataToken)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<UnsafeEvidence>)
                    group.ToArray()));
    }

    public LibraryAllocationAnalysisResult Allocations { get; }
    public LibrarySafetyAnalysisResult Safety { get; }
    public LibraryCallGraphAnalysisResult CallGraph { get; }
    public LibraryLeverageAnalysisResult Leverage { get; }

    internal IReadOnlyDictionary<int, MethodSignals> Signals =>
        CallGraph.MethodSignals;

    internal IReadOnlyDictionary<int, MethodLeverage> LeverageByToken =>
        _leverageByToken.Value;

    internal IReadOnlyDictionary<int, IReadOnlyList<DirectCall>>
        CallsByEvidenceMethod => _callsByEvidenceMethod.Value;

    internal IReadOnlyDictionary<int, IReadOnlyList<UnsafeEvidence>>
        UnsafeEvidenceByToken => _unsafeEvidenceByToken.Value;

    internal ImmutableArray<Finding<DirectCall>> InspectCallSites(
        int methodToken)
    {
        if (!CallsByEvidenceMethod.TryGetValue(
                methodToken,
                out IReadOnlyList<DirectCall>? calls)
            || calls.Count == 0)
        {
            return [];
        }

        var subject =
            ResearchMemberIdentity.SubjectFromMethod(calls[0].Caller);
        return AnalysisFindings.InspectCallSites(
            calls,
            new FindingSubject(subject.Id, subject.Display));
    }
}
