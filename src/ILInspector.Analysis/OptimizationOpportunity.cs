namespace ILInspector.Analysis;

public enum PerformanceTriageProvenance
{
    Unknown,
    Exact,
    Aggregate,
    Unmatched,
}

public sealed record OptimizationOpportunity(
    MethodIdentity Method,
    string Shape,
    string Evidence,
    string SafeFixDirection,
    string Confidence,
    bool InLoop,
    int? ILOffset,
    string? Caveat,
    int RootReach = 0,
    bool ColdPath = false,
    string? RuntimeAllocationType = null,
    string? PathContext = null,
    string? PathConfidence = null)
{
    public bool Amortized { get; init; }
    public string? PostDominance { get; init; }

    /// <summary>Objective per-invocation multiplicity of the underlying allocation
    /// (from <see cref="AllocationOccurrence.Multiplicity"/>), used as a weight input.</summary>
    public string? Multiplicity { get; init; }

    /// <summary>Objective estimated size of the underlying allocation, used as a weight input.</summary>
    public int? EstimatedSizeBytes { get; init; }

    /// <summary>Curated coarse priority (size x multiplicity x reach) for allocation
    /// opportunities — a Performance Triage judgment derived from objective inputs.
    /// Null for non-allocation opportunities. Additive: does not reorder rows.</summary>
    public string? Weight { get; init; }

    /// <summary>
    /// Machine-readable identifier for this triage row. Exact allocation and call-site rows
    /// retain their producer descriptor and identity fingerprint; aggregate rows retain their
    /// method coordinate and shape.
    /// </summary>
    public string? CandidateId { get; init; }

    /// <summary>The native Finding descriptor that supplies this row's exact occurrence.</summary>
    public string? SourceFinding { get; init; }

    /// <summary>The IL operation associated with the source Finding, when known.</summary>
    public string? Operation { get; init; }

    /// <summary>The source occurrence's version-local metadata operand token, when present.</summary>
    public int? OperandToken { get; init; }

    /// <summary>Whether this row has exact, aggregate, or unresolved producer provenance.</summary>
    public PerformanceTriageProvenance Provenance { get; init; }

    /// <summary>
    /// Objective invocation path showing that an upstream loop can repeat this method.
    /// Separate from the allocation's local multiplicity, loop membership, and confidence.
    /// </summary>
    public CallerLoopEvidence? CallerLoop { get; init; }

    /// <summary>Direct IL-visible heap-allocation sites in the declaring method.</summary>
    public int? DirectAllocationSites { get; init; }

    /// <summary>
    /// IL-visible allocation paths classified once on normally returning control flow, including
    /// exact intra-assembly callees reached only through call sites with the same classification.
    /// </summary>
    public long? OnceAllocationPaths { get; init; }

    public long? ConditionalAllocationPaths { get; init; }
    public long? RepeatedAllocationPaths { get; init; }
    public long? UnknownAllocationPaths { get; init; }
    public long? CachedAllocationSites { get; init; }
    public long? OpaqueCallPaths { get; init; }
    public bool AllocationCountSaturated { get; init; }
}
