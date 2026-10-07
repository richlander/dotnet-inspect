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
    string? Detail)
{
    /// <summary>
    /// Whether the reconstructed operation requires an unsafe context under
    /// updated language semantics.
    /// </summary>
    public bool RequiresUnsafeContext { get; init; } = true;
}
