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

    /// <summary>
    /// For a <see cref="UnsafetyKind.StackAlloc"/> lowered into
    /// <c>Span&lt;T&gt;</c>, the IL offset of the compiler-emitted
    /// <c>Span&lt;T&gt;(void*, int)</c> constructor call that wraps it. That call is
    /// lowering, not a source call to the constructor's caller-unsafe contract.
    /// </summary>
    internal int? SpanConstructorOffset { get; init; }
}
