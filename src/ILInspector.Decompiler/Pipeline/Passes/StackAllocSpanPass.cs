namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises the compiler's lowering of <c>Span&lt;T&gt; s = stackalloc T[n]</c> back
/// into a source-level <c>stackalloc T[n]</c>. The source form lowers to a
/// <c>localloc</c> of <c>n * sizeof(T)</c> bytes fed to the <c>Span&lt;T&gt;(void*,
/// int)</c> constructor, which left flat renders as
/// <c>new Span&lt;T&gt;(stackalloc byte[...], n)</c> — and that does not compile:
/// a <c>stackalloc</c> in argument position types as <c>Span&lt;byte&gt;</c>, not
/// <c>void*</c>, and the byte-count expression carries a <c>nuint</c> that will not
/// implicitly convert to <c>int</c>. Rewriting to <c>stackalloc T[n]</c> (target-typed
/// to the span) round-trips to the same lowering.
///
/// <para>The element type is the span's type argument; the element count is the
/// constructor's <c>length</c> argument (the byte size on the inner
/// <see cref="StackAllocate"/> is the redundant <c>n * sizeof(T)</c> and is dropped).
/// This raise is independent of the memory-safety rules — the lowered shape never
/// compiled — so the pass runs unconditionally. Whether the result needs an
/// <c>unsafe</c> context (only under <c>[SkipLocalsInit]</c>, per the stackalloc
/// rule) is decided later by the printer.</para>
/// </summary>
public sealed class StackAllocSpanPass : IIrPass
{
    public string Name => "stackalloc-span";

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var newObject in function.Descendants.OfType<NewObject>().ToList())
        {
            if (!MemberIdentity.IsStackAllocSpanConstructor(newObject, out var element))
                continue;

            // Span<T>(void* pointer, int length): the pointer is the stackalloc,
            // the length is the logical element count.
            if (newObject.Arguments is not [{ } pointer, var count]
                || pointer is not StackAllocate)
                continue;

            count.Detach();
            var raised = new StackAllocArray(element, count, newObject.ResultType);
            raised.InheritSourceOffset(newObject);
            context.Stepper.StepOver("raise Span-over-stackalloc to stackalloc T[n]", newObject);
            newObject.ReplaceWith(raised);
        }
    }
}
