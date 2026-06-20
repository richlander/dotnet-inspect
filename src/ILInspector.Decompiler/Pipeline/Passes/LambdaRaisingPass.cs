namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises a delegate creation over a compiler-synthesized lambda method back to
/// the lambda itself — the inverse of ClosureConversion's "lower <c>x =&gt; e</c>
/// to a delegate over a method on the <c>&lt;&gt;c</c> closure holder" step. By
/// the time this runs, <see cref="LambdaCachePass"/> and the second inlining
/// have stripped the lazy cache, leaving a bare
/// <c>new Func&lt;…&gt;(&lt;&gt;c.&lt;Outer&gt;b__N_M)</c>; this imports that
/// synthesized method through the pass context's cross-method seam, re-presents
/// its body as <c>(params) =&gt; body</c>, and replaces the delegate creation.
///
/// <para>First slice — <b>non-capturing, expression-bodied</b> only: the target
/// is a method on the static singleton <c>&lt;&gt;c</c> (no display class), and
/// its body must declare no locals, read no captured <c>this</c>, and be a
/// single <c>return expr;</c>. Those are exactly the bodies that print correctly
/// inside the outer function's scope without a local/parameter context of their
/// own (arguments are self-naming). A capturing lambda, a body with locals, or a
/// statement body is left as a delegate creation for a later increment. A no-op
/// when the seam is absent (stage dumps, the lowered/annotated views).</para>
/// </summary>
public sealed class LambdaRaisingPass : IIrPass
{
    public string Name => "lambda-raising";

    public void Run(IrFunction function, PassContext context)
    {
        if (context.ImportMethodBody is null)
            return;

        foreach (var creation in function.Descendants.OfType<DelegateCreation>().ToList())
        {
            if (creation.Parent is null)
                continue;  // detached by an earlier rewrite in this walk
            if (!IsNonCapturingLambdaMethod(creation.Method))
                continue;
            if (Raise(creation, context) is not { } lambda)
                continue;
            context.Stepper.StepOver($"raise lambda {creation.Method.Name}", creation);
            creation.ReplaceWith(lambda);
        }
    }

    static Lambda? Raise(DelegateCreation creation, PassContext context)
    {
        var body = context.ImportMethodBody!(creation.Method);
        if (body is null)
            return null;

        // Raise the imported body with the same pipeline so it lands at the
        // shipped altitude (and any nested lambda resolves through the seam).
        IrPasses.Run(body, IrPasses.Default, context);

        // Admit only what prints soundly in the outer scope: no locals of its
        // own, no read of the captured-this slot (arg 0), and a single returned
        // expression. Anything else needs printer scope-switching — not yet.
        if (!body.Locals.IsEmpty)
            return null;
        if (body.Descendants.OfType<UnsupportedNode>().Any())
            return null;
        if (body.Descendants.OfType<LoadArgument>().Any(a => a.Index == 0))
            return null;
        if (body.Body.Blocks is not [{ Children: [Return { Value: not null }] }])
            return null;

        var container = body.Body;
        container.Detach();
        return new Lambda(creation.DelegateType, body.Signature.Parameters, container);
    }

    // A non-capturing lambda's target is a method named <Outer>b__N_M on the
    // static singleton closure holder <>c (capturing lambdas live on a
    // <>c__DisplayClass instance instead). The leaf type name pins it.
    static bool IsNonCapturingLambdaMethod(MethodRef method)
        => LeafTypeName(method.DeclaringType.Name) == "<>c"
            && method.Name.Contains(">b__", StringComparison.Ordinal);

    // TypeRef.Name spells nesting with '+'; the closure holder is the leaf.
    static string LeafTypeName(string name)
    {
        int plus = name.LastIndexOf('+');
        return plus < 0 ? name : name[(plus + 1)..];
    }
}
