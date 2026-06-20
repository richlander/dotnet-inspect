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
/// <para>Current slice — <b>zero-local</b> lambdas, capturing or not:</para>
/// <list type="bullet">
/// <item><b>Non-capturing</b> — the target runs on the static <c>&lt;&gt;c</c>
/// singleton and reads no <c>this</c>.</item>
/// <item><b>Capturing</b> — the delegate target is a folded
/// <c>new &lt;&gt;c__DisplayClass { f = v, ... }</c> environment; each member binds
/// a hoisted field to a value captured from the outer scope. The body's
/// <c>this.f</c> reads are substituted with those captured values, which then
/// print in the outer scope. Only the inlined single-expression environment is
/// taken; a display class spread across statements is left for a later
/// increment.</item>
/// </list>
/// <para>In both cases the body must carry compiler-generated metadata evidence,
/// declare no locals of its own, and be a single <c>return expr;</c> or a simple
/// block ending in a return — bodies that print correctly inside the outer
/// function's scope (arguments are self-naming). A no-op when the seam is absent
/// (stage dumps, the lowered/annotated views).</para>
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
            Lambda? lambda =
                GeneratedCodeIdentity.IsNonCapturingLambdaMethod(creation.Method) ? RaiseNonCapturing(creation, context)
                : GeneratedCodeIdentity.IsCapturingLambdaMethod(creation.Method) ? RaiseCapturing(creation, context)
                : null;
            if (lambda is null)
                continue;
            context.Stepper.StepOver($"raise lambda {creation.Method.Name}", creation);
            creation.ReplaceWith(lambda);
        }
    }

    static Lambda? RaiseNonCapturing(DelegateCreation creation, PassContext context)
    {
        var body = RaisedBody(creation, context);
        if (body is null)
            return null;

        // A non-capturing body holds no state, so it reads its receiver (arg 0,
        // the <>c singleton) never; any such read is a shape we cannot present.
        if (body.Descendants.OfType<LoadArgument>().Any(a => a.Index == 0))
            return null;

        return Finish(creation, body);
    }

    static Lambda? RaiseCapturing(DelegateCreation creation, PassContext context)
    {
        // The delegate target is the folded environment: new <>c__DisplayClass
        // { field = capturedValue, ... }. Anything else (a display class kept in a
        // local across statements) is out of this slice.
        if (creation.Target is not ObjectInitializerExpression env
            || env.IsCollection
            || !Equals(env.Creation.Constructor.DeclaringType, creation.Method.DeclaringType))
            return null;

        var captures = new Dictionary<string, IrExpression>(StringComparer.Ordinal);
        var values = env.Values;
        for (int i = 0; i < values.Count; i++)
        {
            // The compiler hoists variables, not expressions, so a capture value
            // is a bare parameter/local/this load that re-prints in the outer scope.
            if (env.Members[i] is not { } field || values[i] is not (LoadArgument or LoadLocal))
                return null;
            captures[field] = values[i];
        }

        var body = RaisedBody(creation, context);
        if (body is null)
            return null;

        // Every read of the display-class `this` (arg 0) must be a captured-field
        // load we can substitute; any other use of `this` we cannot represent.
        var thisReads = body.Descendants.OfType<LoadArgument>().Where(a => a.Index == 0).ToList();
        if (!thisReads.All(a => a.Parent is LoadField field
                && Equals(field.Field.DeclaringType, creation.Method.DeclaringType)
                && captures.ContainsKey(field.Field.Name)))
            return null;

        foreach (var load in body.Descendants.OfType<LoadField>().ToList())
        {
            if (load.Instance is LoadArgument { Index: 0 }
                && Equals(load.Field.DeclaringType, creation.Method.DeclaringType)
                && captures.TryGetValue(load.Field.Name, out var value))
                load.ReplaceWith(value.Clone());
        }

        return Finish(creation, body);
    }

    // Import the synthesized method and raise it with the same pipeline so it
    // lands at the shipped altitude (and any nested lambda resolves through the
    // seam). Null when the body is absent.
    static IrFunction? RaisedBody(DelegateCreation creation, PassContext context)
    {
        var body = context.ImportMethodBody!(creation.Method);
        if (body is null)
            return null;
        IrPasses.Run(body, IrPasses.Default, context);
        return body;
    }

    // Shared finisher: admit only a body that prints soundly in the outer scope —
    // no locals of its own, nothing unsupported, and a single printable block.
    static Lambda? Finish(DelegateCreation creation, IrFunction body)
    {
        if (!body.Locals.IsEmpty)
            return null;
        if (body.Descendants.OfType<UnsupportedNode>().Any())
            return null;
        if (!IsPrintableBody(body))
            return null;

        var container = body.Body;
        container.Detach();
        return new Lambda(creation.DelegateType, body.Signature.Parameters, container);
    }

    static bool IsPrintableBody(IrFunction body)
    {
        if (body.Body.Blocks is not [{ Children: var statements }] || statements.Count == 0)
            return false;

        for (int i = 0; i < statements.Count; i++)
        {
            var statement = statements[i];
            if (statement is Return { Value: not null })
                return i == statements.Count - 1;
            if (statement is not ExpressionStatement)
                return false;
        }

        return false;
    }
}
