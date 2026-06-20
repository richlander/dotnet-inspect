namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises a call to a compiler-synthesized local-function method back to the
/// local function itself — the inverse of ClosureConversion lowering a
/// <c>Name(...) { ... }</c> local function to a <c>&lt;Enclosing&gt;g__Name|N_M</c>
/// method invoked directly. The synthesized body is imported through the pass
/// context's cross-method seam, emitted as a nested local-function declaration
/// at the end of the host body, and every call site is rewritten to the
/// unqualified <c>Name(args)</c> (the source spelling, replacing the
/// otherwise-unspeakable <c>Enclosing.&lt;Outer&gt;g__Name|N_M(args)</c>).
///
/// <para>Current slice — <b>static, non-capturing</b> local functions with a
/// zero-local body that prints in the host scope (arguments are self-naming).
/// Excluded, and left as-is: a capturing local function (it takes its
/// <c>&lt;&gt;c__DisplayClass</c> environment by <c>ref</c>), and a body that
/// itself calls a local function (recursion or nesting), which keeps the import
/// non-recursive. A no-op when the seam is absent.</para>
/// </summary>
public sealed class LocalFunctionRaisingPass : IIrPass
{
    public string Name => "local-function-raising";

    public void Run(IrFunction function, PassContext context)
    {
        if (context.ImportMethodBody is null)
            return;

        var byMethod = function.Descendants.OfType<Call>()
            .Where(c => c.Parent is not null && GeneratedCodeIdentity.IsLocalFunctionMethod(c.Callee))
            .GroupBy(c => c.Callee)
            .ToList();

        var declarations = new List<LocalFunctionStatement>();
        foreach (var group in byMethod)
        {
            var method = group.Key;
            // Static value-parameter signature only: an instance receiver or a
            // display-class (by-ref) parameter means a capturing local function.
            if (method.HasThis || method.ParameterTypes.Any(IsDisplayClassParameter))
                continue;

            var body = context.ImportMethodBody(method);
            if (body is null)
                continue;
            // Keep the import non-recursive: a body that calls a local function
            // (itself, mutually, or nested) is out of this slice.
            if (body.Descendants.OfType<Call>().Any(c => GeneratedCodeIdentity.IsLocalFunctionMethod(c.Callee)))
                continue;

            IrPasses.Run(body, IrPasses.Default, context);
            if (!body.Locals.IsEmpty
                || body.Descendants.OfType<UnsupportedNode>().Any()
                || !IsPrintableBody(body))
                continue;

            string name = CSharpNaming.MethodName(method.Name);
            var container = body.Body;
            container.Detach();
            // The body is another method's; clear its offsets so they cannot
            // collide with the host's offset-keyed annotations and interleaved IL
            // (the local function is rendered inline; its own IL is not projected).
            foreach (var node in Self(container))
                node.SetSourceOffset(-1);

            foreach (var call in group.ToList())
            {
                context.Stepper.StepOver($"raise local function {name}", call);
                var arguments = call.DetachChildren().Cast<IrExpression>();
                call.ReplaceWith(new LocalFunctionInvocation(name, method.ReturnType, arguments));
            }
            declarations.Add(new LocalFunctionStatement(
                name, method.ReturnType, body.Signature.Parameters, isStatic: true, container));
        }

        if (declarations.Count == 0)
            return;

        // Local functions are declarations, valid even after a return, so they
        // append to the last top-level block — the idiomatic trailing placement.
        var block = function.Body.Blocks[^1];
        foreach (var declaration in declarations)
            block.Add(declaration);
    }

    static bool IsDisplayClassParameter(TypeRef type)
        => GeneratedCodeIdentity.IsDisplayClassName(type.Kind == TypeRefKind.ByRef ? type.ElementType! : type);

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

    static IEnumerable<IrNode> Self(IrNode node)
    {
        yield return node;
        foreach (var descendant in node.Descendants)
            yield return descendant;
    }
}
