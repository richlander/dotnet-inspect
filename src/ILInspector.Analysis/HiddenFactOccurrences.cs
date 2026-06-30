namespace ILInspector.Analysis;

public enum UnsafetyKind
{
    Deref,
    StackAlloc,
    CallIndirect,
}

public sealed record UnsafetyOccurrence(
    MethodIdentity Method,
    int ILOffset,
    UnsafetyKind Kind,
    string? Detail);

public enum LifetimeKind
{
    RefReturn,
    StackBound,
    RefStructReturn,
    PointerReturn,
    StackEscape,
}

public sealed record LifetimeOccurrence(
    MethodIdentity Method,
    int ILOffset,
    LifetimeKind Kind,
    string? Detail);
