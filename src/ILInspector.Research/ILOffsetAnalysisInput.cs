using System.Collections.Immutable;
using ILInspector.Analysis;
using ILInspector.Analysis.Planning;

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

/// <summary>
/// Exact Method-source allocation evidence consumed by IL-offset projection.
/// </summary>
public sealed class ILOffsetAllocationAnalysisInput
{
    internal ILOffsetAllocationAnalysisInput(
        Guid moduleVersionId,
        IReadOnlyDictionary<
            int,
            ImmutableArray<AllocationOccurrence>> occurrences,
        ImmutableArray<AnalysisDiagnostic> diagnostics,
        MethodDefinitionSourceReceipt sourceReceipt,
        WorkReceipt workReceipt)
    {
        ModuleVersionId = moduleVersionId;
        Occurrences = occurrences;
        Diagnostics = diagnostics;
        SourceReceipt = sourceReceipt;
        WorkReceipt = workReceipt;
    }

    /// <summary>The exact module generation that supplied the evidence.</summary>
    public Guid ModuleVersionId { get; }

    /// <summary>Visible producer diagnostics retained with partial evidence.</summary>
    public ImmutableArray<AnalysisDiagnostic> Diagnostics { get; }

    /// <summary>The exact Method-source work performed for this input.</summary>
    public MethodDefinitionSourceReceipt SourceReceipt { get; }

    /// <summary>The focused producer participation receipt.</summary>
    public WorkReceipt WorkReceipt { get; }

    internal IReadOnlyDictionary<
        int,
        ImmutableArray<AllocationOccurrence>> Occurrences { get; }
}

/// <summary>
/// Exact Method-source direct-call evidence consumed by IL-offset projection.
/// </summary>
public sealed class ILOffsetCostAnalysisInput
{
    internal ILOffsetCostAnalysisInput(
        Guid moduleVersionId,
        IReadOnlyDictionary<
            int,
            ImmutableArray<DirectCall>> calls,
        ImmutableArray<AnalysisDiagnostic> diagnostics,
        MethodDefinitionSourceReceipt sourceReceipt,
        WorkReceipt workReceipt)
    {
        ModuleVersionId = moduleVersionId;
        Calls = calls;
        Diagnostics = diagnostics;
        SourceReceipt = sourceReceipt;
        WorkReceipt = workReceipt;
    }

    /// <summary>The exact module generation that supplied the evidence.</summary>
    public Guid ModuleVersionId { get; }

    /// <summary>Visible producer diagnostics retained with partial evidence.</summary>
    public ImmutableArray<AnalysisDiagnostic> Diagnostics { get; }

    /// <summary>The exact Method-source work performed for this input.</summary>
    public MethodDefinitionSourceReceipt SourceReceipt { get; }

    /// <summary>The focused producer participation receipt.</summary>
    public WorkReceipt WorkReceipt { get; }

    internal IReadOnlyDictionary<
        int,
        ImmutableArray<DirectCall>> Calls { get; }
}

/// <summary>
/// Exact Method-source safety evidence consumed by IL-offset projection.
/// </summary>
public sealed class ILOffsetSafetyAnalysisInput
{
    internal ILOffsetSafetyAnalysisInput(
        Guid moduleVersionId,
        IReadOnlyDictionary<
            int,
            ImmutableArray<UnsafetyOccurrence>> occurrences,
        IReadOnlyDictionary<
            int,
            ImmutableArray<UnsafeEvidence>> evidence,
        ImmutableArray<AnalysisDiagnostic> diagnostics,
        MethodDefinitionSourceReceipt sourceReceipt,
        WorkReceipt workReceipt)
    {
        ModuleVersionId = moduleVersionId;
        Occurrences = occurrences;
        Evidence = evidence;
        Diagnostics = diagnostics;
        SourceReceipt = sourceReceipt;
        WorkReceipt = workReceipt;
    }

    /// <summary>The exact module generation that supplied the evidence.</summary>
    public Guid ModuleVersionId { get; }

    /// <summary>Visible producer diagnostics retained with partial evidence.</summary>
    public ImmutableArray<AnalysisDiagnostic> Diagnostics { get; }

    /// <summary>The exact Method-source work performed for this input.</summary>
    public MethodDefinitionSourceReceipt SourceReceipt { get; }

    /// <summary>The focused producer participation receipt.</summary>
    public WorkReceipt WorkReceipt { get; }

    internal IReadOnlyDictionary<
        int,
        ImmutableArray<UnsafetyOccurrence>> Occurrences { get; }

    internal IReadOnlyDictionary<
        int,
        ImmutableArray<UnsafeEvidence>> Evidence { get; }
}
