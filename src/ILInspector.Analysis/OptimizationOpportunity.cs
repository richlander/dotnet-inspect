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
    string? PathConfidence = null,
    string? PostDominance = null)
{
    public bool Amortized { get; init; }
}
