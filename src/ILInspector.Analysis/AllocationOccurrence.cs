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
    AllocationPathConfidence PathConfidence = AllocationPathConfidence.Unknown)
{
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
