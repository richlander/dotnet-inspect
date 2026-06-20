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
/// <item><b>Capturing</b> — captured variables are hoisted into a
/// <c>&lt;&gt;c__DisplayClass</c> environment; the body's <c>this.f</c> reads are
/// substituted with the captured values, which then print in the outer scope.
/// Two environment shapes are taken: the <b>folded</b>
/// <c>new &lt;&gt;c__DisplayClass { f = v, ... }</c> on the delegate target, and the
/// <b>local</b> form where the environment is a local allocated and field-set
/// across statements (its allocation and capture stores are then elided). A
/// display class read in any way other than its capture stores and lambda
/// delegate targets is left as-is.</item>
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

        // A local display-class environment is a multi-statement shape, so it is
        // raised (and its setup elided) block-first, before the per-creation walk
        // picks up the folded and non-capturing forms left standing.
        RaiseLocalDisplayClasses(function, context);

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

        return Finish(creation, body, creation);
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

        var outer = RootFunction(creation);
        var captures = new Dictionary<string, IrExpression>(StringComparer.Ordinal);
        var values = env.Values;
        for (int i = 0; i < values.Count; i++)
        {
            if (env.Members[i] is not { } field || !IsCaptureValue(values[i], outer))
                return null;
            captures[field] = values[i];
        }

        // Anchor to the folded environment's allocation (new <>c__DisplayClass)
        // rather than the delegate creation: it is the earliest outer offset the
        // lambda subsumes, so the closure allocation fact and its setup IL anchor
        // to this statement in the mixed view (see Finish).
        return RaiseWithCaptures(creation, captures, env.Creation, context);
    }

    // Recover lambdas whose captured environment is a local <>c__DisplayClass set
    // up across statements: `dc = new DC(); dc.f = v; ... use(new Func(dc.b__N))`.
    // Each delegate creation over the local is raised by substituting the captured
    // values, then the allocation and capture stores are elided so the environment
    // disappears (the now-unreferenced local prints nothing).
    static void RaiseLocalDisplayClasses(IrFunction function, PassContext context)
    {
        foreach (var alloc in function.Descendants.OfType<StoreLocal>().ToList())
        {
            if (alloc.Parent is null
                || alloc.Value is not NewObject { Arguments.Count: 0, Constructor.DeclaringType: { } dcType }
                || !GeneratedCodeIdentity.IsDisplayClassName(dcType))
                continue;
            int slot = alloc.Index;

            // The allocation must be the only store to the slot — otherwise the
            // environment outlives this setup and elision is unsound.
            if (function.Descendants.OfType<StoreLocal>().Count(s => s.Index == slot) != 1)
                continue;

            // Classify every read of the local: a capture store's receiver, or a
            // lambda delegate target. Any other use means we cannot elide it.
            var captures = new Dictionary<string, IrExpression>(StringComparer.Ordinal);
            var captureStores = new List<StoreField>();
            var creations = new List<DelegateCreation>();
            bool elidable = true;
            foreach (var load in function.Descendants.OfType<LoadLocal>().Where(l => l.Index == slot))
            {
                if (load.Parent is StoreField store
                    && ReferenceEquals(store.Instance, load)
                    && Equals(store.Field.DeclaringType, dcType)
                    && IsCaptureValue(store.Value, function))
                {
                    captures[store.Field.Name] = store.Value;
                    captureStores.Add(store);
                }
                else if (load.Parent is DelegateCreation creation
                    && ReferenceEquals(creation.Target, load)
                    && GeneratedCodeIdentity.IsCapturingLambdaMethod(creation.Method)
                    && Equals(creation.Method.DeclaringType, dcType))
                {
                    creations.Add(creation);
                }
                else
                {
                    elidable = false;
                    break;
                }
            }
            if (!elidable || creations.Count == 0)
                continue;

            // Raise every lambda first; commit nothing unless all succeed. The
            // environment allocation is each lambda's outer provenance anchor.
            var raised = new List<(DelegateCreation Creation, Lambda Lambda)>(creations.Count);
            foreach (var creation in creations)
            {
                if (RaiseWithCaptures(creation, captures, alloc.Value, context) is not { } lambda)
                {
                    raised = null!;
                    break;
                }
                raised.Add((creation, lambda));
            }
            if (raised is null)
                continue;

            foreach (var (creation, lambda) in raised)
            {
                context.Stepper.StepOver($"raise captured lambda {creation.Method.Name}", creation);
                creation.ReplaceWith(lambda);
            }
            foreach (var store in captureStores)
                store.Detach();
            alloc.Detach();
        }
    }

    // Import and raise the lambda body, then substitute each read of a captured
    // field (through the display-class `this`, arg 0) with the captured value.
    // Bails if `this` is used any way other than reading a known captured field.
    static Lambda? RaiseWithCaptures(
        DelegateCreation creation, Dictionary<string, IrExpression> captures, IrNode provenance, PassContext context)
    {
        var body = RaisedBody(creation, context);
        if (body is null)
            return null;

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

        return Finish(creation, body, provenance);
    }

    // A hoisted capture binds a variable, not an expression: a parameter/this load
    // or a non-display-class local (a display-class local would be a nested
    // environment we do not yet flatten).
    static bool IsCaptureValue(IrExpression value, IrFunction function) => value switch
    {
        LoadArgument => true,
        LoadLocal local => !GeneratedCodeIdentity.IsDisplayClassName(function.Locals[local.Index]),
        _ => false,
    };

    static IrFunction RootFunction(IrNode node)
    {
        while (node.Parent is not null)
            node = node.Parent;
        return (IrFunction)node;
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
    static Lambda? Finish(DelegateCreation creation, IrFunction body, IrNode provenance)
    {
        if (!body.Locals.IsEmpty)
            return null;
        if (body.Descendants.OfType<UnsupportedNode>().Any())
            return null;
        if (!IsPrintableBody(body))
            return null;

        var container = body.Body;
        container.Detach();

        // The body comes from a different method, so its IL offsets live in that
        // method's offset space and would collide with the host method's — a stray
        // inner offset can form a tighter span that steals the host's offset-keyed
        // annotations and interleaved IL (the lambda is rendered inline; its own IL
        // is never projected into the host). Clear them, then anchor the whole
        // lambda to its outer provenance so the host's surrounding facts/IL (the
        // delegate creation, and for a capturing lambda its <>c__DisplayClass
        // allocation) anchor to this statement.
        foreach (var node in Self(container))
            node.SetSourceOffset(-1);
        var lambda = new Lambda(creation.DelegateType, body.Signature.Parameters, container);
        lambda.InheritSourceOffset(provenance);
        return lambda;
    }

    static IEnumerable<IrNode> Self(IrNode node)
    {
        yield return node;
        foreach (var descendant in node.Descendants)
            yield return descendant;
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
