namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Canonicalizes a value read of an unboxed managed pointer —
/// <c>unbox T; ldobj T</c> — into the equivalent <see cref="UnboxAny"/> node
/// (<c>unbox.any T</c>). Both spell "read the boxed <c>T</c> value": <c>unbox T</c>
/// yields a <c>ByRef T</c> into the box payload and the trailing <c>ldobj T</c>
/// dereferences it, which is exactly what a single <c>unbox.any T</c> does.
///
/// <para>The importer keeps the two-op form as
/// <see cref="LoadIndirect"/>(<see cref="Unbox"/>). Left standing, the printer
/// spells that managed-pointer address as the <c>Unsafe.Unbox&lt;T&gt;(o)</c>
/// intrinsic — the only assignable-place spelling, correct where the pointer is
/// genuinely consumed as a <c>ref</c>/<c>out</c>/write target, but wrong for a
/// pure value read: the intrinsic requires <c>where T : struct</c>, so a boxed
/// <c>Nullable&lt;T&gt;</c> or an open type parameter is CS0453 even though the
/// value read has a perfectly valid cast spelling (<c>(T)o</c>, which itself
/// compiles to <c>unbox.any</c>). Normalizing here keeps the value/place decision
/// out of the printer: the printer spells <see cref="Unbox"/> only where it is a
/// genuine place, and the universally valid cast falls out of the existing
/// <see cref="UnboxAny"/> rendering.</para>
///
/// <para>Only a same-type, non-volatile read qualifies. A reinterpreting read
/// (<c>ldobj U</c> over <c>unbox T</c> with <c>U != T</c>) is not <c>unbox.any</c>
/// and stays explicit; a <c>volatile.</c> read carries acquire semantics a plain
/// cast would drop, so it is left for the honest managed-pointer spelling.</para>
/// </summary>
public sealed class UnboxValueReadPass : IIrPass
{
    public string Name => "unbox-value-read";

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var load in function.Descendants.OfType<LoadIndirect>().ToList())
        {
            if (load.Parent is null)
                continue;  // already detached by an outer rewrite this pass
            if (load.IsVolatile)
                continue;  // a volatile acquire read is not a plain unbox.any
            if (load.Address is not Unbox unbox)
                continue;
            if (load.Type is not { } readType || !readType.Equals(unbox.Type))
                continue;  // ldobj U over unbox T (U != T) is a reinterpret, not unbox.any
            var boxed = (IrExpression)unbox.DetachChildren()[0];
            context.Stepper.StepOver($"normalize ldobj(unbox {unbox.Type.Name}) to unbox.any", load);
            load.ReplaceWith(new UnboxAny(unbox.Type, boxed));
        }
    }
}
