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
