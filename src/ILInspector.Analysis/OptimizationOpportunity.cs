namespace ILInspector.Analysis;

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
}
