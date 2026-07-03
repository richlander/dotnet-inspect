namespace ILInspector.Analysis;

public enum AllocationKind
{
    Box,
    Array,
    Object,
    Closure,
    StateMachine,
    Delegate,
    Enumerator,
}

public enum AllocationFrequency
{
    Always,
    CachedOnce,
    PerIteration,
}

public enum AllocationEscape
{
    Unknown,
    LocalOnly,
    Escapes,
    ThrowPath,
}

/// <summary>
/// Objective refinement of an <see cref="AllocationEscape.Escapes"/> verdict: WHERE
/// the produced value escapes. Additive on top of the binary verdict — a plain
/// <see cref="AllocationEscape.Escapes"/> with an undetermined sink stays
/// <see cref="None"/> (fail-honest). Feeds the static→dynamic promotion prior.
/// </summary>
public enum AllocationEscapeKind
{
    None,
    Return,
    Field,
    Static,
    Collection,
    Capture,
}

public enum AllocationFactSource
{
    Newobj,
    Newarr,
    Box,
    GetEnumeratorCall,
}

public enum AllocationPathContext
{
    StraightLine,
    Branch,
    SwitchArm,
    LoopBody,
    ErrorPath,
}

public enum AllocationPathConfidence
{
    Unknown,
    DominatesReturn,
    BehindBranch,
}

public enum AllocationPostDominance
{
    Unknown,
    ReturnPostDominates,
}

public enum AllocationSizeTier
{
    Unknown,
    Exact,
    Approx,
}

/// <summary>
/// Objective per-invocation multiplicity tier: how many times a call to the
/// declaring method executes this allocation. A consolidation of the existing
/// path axes (loop membership, dominates-return, branch context) into one
/// "how often per call" signal. Fail-honest <see cref="Unknown"/> when it cannot
/// be proven. <see cref="Loop"/> is unbounded (the trip count N is not statically
/// resolved). Feeds the static pre-profile prioritization / dynamic validation.
/// </summary>
public enum AllocationMultiplicity
{
    Unknown,
    Once,
    Conditional,
    Loop,
}

/// <summary>
/// One static heap-allocation occurrence, keyed by IL offset. Presentation layers
/// project this into hidden-fact annotations, method signals, and triage rows.
/// </summary>
public sealed record AllocationOccurrence(
    MethodIdentity Method,
    int ILOffset,
    int? OperandToken,
    AllocationKind Kind,
    TypeRef? AllocatedType,
    string? Detail,
    bool CountsAsHeapAllocation,
    AllocationFrequency Frequency,
    bool InLoop,
    AllocationEscape Escape,
    AllocationFactSource Source,
    string? RuntimeAllocationType = null,
    AllocationPathContext PathContext = AllocationPathContext.StraightLine,
    AllocationPathConfidence PathConfidence = AllocationPathConfidence.Unknown,
    int? EstimatedSizeBytes = null,
    AllocationSizeTier SizeTier = AllocationSizeTier.Unknown)
{
    public AllocationPostDominance PostDominance { get; init; }

    public AllocationEscapeKind EscapeKind { get; init; }

    public AllocationMultiplicity Multiplicity { get; init; }

    public string AnnotationId => Kind switch
    {
        AllocationKind.Box => "alloc.box",
        AllocationKind.Array => "alloc.array",
        AllocationKind.Closure => "alloc.closure",
        AllocationKind.StateMachine => "alloc.statemachine",
        AllocationKind.Delegate => "alloc.delegate",
        AllocationKind.Enumerator => "alloc.enumerator",
        _ => "alloc.new",
    };
}
