using System.Collections.Immutable;

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
/// <para>Slice — <b>static</b> local functions may carry body locals/slots and
/// print in a nested scope. Capturing local functions remain zero-local after
/// capture substitution because their substituted captures print in the host
/// scope. A capturing local function takes its <c>&lt;&gt;c__DisplayClass</c>
/// environment (a struct) by <c>ref</c> as its last parameter; the host sets the
/// captured fields directly on a local and passes <c>ref env</c>. This recovers
/// it by substituting each <c>env.f</c> read in the body with the captured value,
/// dropping the environment parameter from the declaration and the <c>ref env</c>
/// argument from each call, and eliding the capture stores. Left as-is: an
/// environment shared with another local function or read any other way, a
/// captured variable stored more than once (reassigned, so no single value is
/// live at every call site), and a body that itself calls a local function
/// (recursion / nesting), which keeps the import non-recursive. A no-op when the
/// seam is absent.</para>
/// </summary>
public sealed class LocalFunctionRaisingPass : IIrPass
{
    public string Name => "local-function-raising";

    public void Run(IrFunction function, PassContext context)
    {
        if (context.ImportMethodBody is null)
            return;

        var groups = function.Descendants.OfType<Call>()
            .Where(c => c.Parent is not null && GeneratedCodeIdentity.IsLocalFunctionMethod(c.Callee))
            // Group by the callee's stable identity, not the MethodRef record:
            // ParameterTypes is an ImmutableArray, whose Equals is reference (not
            // structural), so two call sites to the same local function carry
            // non-equal MethodRef instances and would split into separate groups.
            // Local-function mangled names are unique within a type.
            .GroupBy(c => (c.Callee.DeclaringType.ToDisplayString(), c.Callee.Name))
            .ToList();

        var declarations = new List<LocalFunctionStatement>();
        foreach (var group in groups)
        {
            var calls = group.ToList();
            var method = calls[0].Callee;
            if (method.HasThis)
                continue;  // instance receiver — out of this slice

            var environment = ResolveEnvironment(method, calls, function);
            // A display-class parameter we could not resolve to a clean, single-use
            // environment (shared, or read another way) is out of this slice.
            if (environment is null && method.ParameterTypes.Any(IsDisplayClassParameter))
                continue;

            if (!context.TryEnterCrossMethodPipeline(method, out var importScope))
                continue;
            using (importScope)
            {
                var body = importScope.Import();
                if (body is null)
                    continue;
                // Keep the import non-recursive: a body that calls a local function
                // (itself, mutually, or nested) is out of this slice.
                if (body.Descendants.OfType<Call>().Any(c => GeneratedCodeIdentity.IsLocalFunctionMethod(c.Callee)))
                    continue;

                IrPasses.Run(body, IrPasses.Default, context);

                if (environment is not null && !SubstituteEnvironment(body, environment))
                    continue;
                bool allowLocals = environment is null;
                if (!allowLocals && !body.Locals.IsEmpty
                    || body.Descendants.OfType<UnsupportedNode>().Any()
                    || !IsPrintableBody(body, allowLocals))
                    continue;

                string name = CSharpNaming.MethodName(method.Name);
                // The environment parameter is the last one; drop it from the source signature.
                var parameters = environment is null
                    ? body.Signature.Parameters
                    : body.Signature.Parameters.RemoveAt(body.Signature.Parameters.Length - 1);

                var container = body.Body;
                container.Detach();
                // The body is another method's; clear its offsets so they cannot collide
                // with the host's offset-keyed annotations and interleaved IL.
                foreach (var node in Self(container))
                    node.SetSourceOffset(-1);

                foreach (var call in calls)
                {
                    context.Stepper.StepOver($"raise local function {name}", call);
                    var arguments = call.DetachChildren().Cast<IrExpression>().ToList();
                    if (environment is not null)
                        arguments.RemoveAt(arguments.Count - 1);   // drop the ref-env argument
                    call.ReplaceWith(new LocalFunctionInvocation(name, method.ReturnType, arguments));
                }
                // A capturing local function cannot be `static` (CS8421); the
                // synthesized method is static only because the environment is passed
                // explicitly by ref, which the recovered source form does not show.
                declarations.Add(new LocalFunctionStatement(
                    name,
                    method.ReturnType,
                    parameters,
                    isStatic: environment is null,
                    body.Locals,
                    body.LocalNames,
                    body.UsesUpdatedMemorySafetyRules,
                    body.SkipLocalsInit,
                    container));
                environment?.Elide();
            }
        }

        if (declarations.Count == 0)
            return;

        // Local functions are declarations, valid even after a return, so they
        // append to the last top-level block — the idiomatic trailing placement.
        var block = function.Body.Blocks[^1];
        foreach (var declaration in declarations)
            block.Add(declaration);
    }

    /// <summary>The captured environment of a capturing local function: the host's struct display-class local, its field bindings, and the body argument that names it.</summary>
    sealed record Environment(
        TypeRef Type, int ArgIndex, Dictionary<string, IrExpression> Captures, List<StoreField> Stores)
    {
        public void Elide()
        {
            foreach (var store in Stores)
                store.Detach();
        }
    }

    // A capturing local function takes its struct <>c__DisplayClass environment by
    // ref as the last parameter; the host fills it via field stores through a
    // local address and passes ref env to each call. Resolve that to a capture map
    // when the environment is used only for those stores and these calls.
    static Environment? ResolveEnvironment(MethodRef method, List<Call> calls, IrFunction function)
    {
        if (method.ParameterTypes is not [.., { Kind: TypeRefKind.ByRef } byRef]
            || !GeneratedCodeIdentity.IsDisplayClassName(byRef.ElementType!))
            return null;
        var envType = byRef.ElementType!;

        // Every call passes ref <sameLocal> as its last argument.
        if (calls[0].Arguments[^1] is not LoadLocalAddress { Index: var slot })
            return null;
        if (calls.Any(c => c.Arguments[^1] is not LoadLocalAddress address || address.Index != slot))
            return null;

        var captures = new Dictionary<string, IrExpression>(StringComparer.Ordinal);
        var stores = new List<StoreField>();
        foreach (var store in function.Descendants.OfType<StoreField>())
        {
            if (store.Instance is LoadLocalAddress { Index: var s } && s == slot && Equals(store.Field.DeclaringType, envType))
            {
                if (!IsCaptureValue(store.Value, function))
                    return null;
                // Exactly one store per captured field: a second store means the
                // captured variable is reassigned, so no single substituted value
                // is live at every call site (the reassignment may even follow the
                // call). Leave those to the honest fallback.
                if (!captures.TryAdd(store.Field.Name, store.Value))
                    return null;
                stores.Add(store);
            }
        }

        // The environment local must be touched only by those capture stores and
        // these calls' ref arguments — any other read means it outlives this setup.
        int addressUses = function.Descendants.OfType<LoadLocalAddress>().Count(a => a.Index == slot);
        if (function.Descendants.OfType<LoadLocal>().Any(l => l.Index == slot)
            || addressUses != stores.Count + calls.Count)
            return null;
        if (!CaptureStoresPrecedeCalls(stores, calls))
            return null;

        return new Environment(envType, method.ParameterTypes.Length - 1, captures, stores);
    }

    static bool CaptureStoresPrecedeCalls(IReadOnlyList<StoreField> stores, IReadOnlyList<Call> calls)
    {
        foreach (var call in calls)
        {
            if (StatementInBlock(call) is not { } callStatement)
                return false;
            foreach (var store in stores)
            {
                if (StatementInBlock(store) is not { } storeStatement
                    || !ReferenceEquals(storeStatement.Parent, callStatement.Parent)
                    || storeStatement.ChildIndex >= callStatement.ChildIndex)
                {
                    return false;
                }
            }
        }
        return true;
    }

    static IrNode? StatementInBlock(IrNode node)
    {
        for (var current = node; current.Parent is not null; current = current.Parent)
            if (current.Parent is Block)
                return current;
        return null;
    }

    static bool SubstituteEnvironment(IrFunction body, Environment environment)
    {
        // Every use of the environment parameter must be the receiver of a
        // LoadField we can substitute. Check that on the original body, before
        // substitution: the captured values cloned in below are themselves
        // host LoadArguments, so a post-substitution index test cannot tell a
        // leftover environment read from a substituted host argument that
        // happens to share the same index.
        foreach (var arg in body.Descendants.OfType<LoadArgument>())
        {
            if (arg.Index != environment.ArgIndex)
                continue;
            if (arg.Parent is not LoadField load
                || !Equals(load.Field.DeclaringType, environment.Type)
                || !environment.Captures.ContainsKey(load.Field.Name))
                return false;
        }

        foreach (var load in body.Descendants.OfType<LoadField>().ToList())
        {
            if (load.Instance is LoadArgument arg && arg.Index == environment.ArgIndex
                && Equals(load.Field.DeclaringType, environment.Type)
                && environment.Captures.TryGetValue(load.Field.Name, out var value))
                load.ReplaceWith(value.Clone());
        }
        return true;
    }

    static bool IsCaptureValue(IrExpression value, IrFunction function) => value switch
    {
        LoadArgument => true,
        LoadLocal local => !GeneratedCodeIdentity.IsDisplayClassName(function.Locals[local.Index]),
        _ => false,
    };

    static bool IsDisplayClassParameter(TypeRef type)
        => GeneratedCodeIdentity.IsDisplayClassName(type.Kind == TypeRefKind.ByRef ? type.ElementType! : type);

    static bool IsPrintableBody(IrFunction body, bool allowLocalStatements = false)
    {
        if (body.Body.Blocks is not [{ Children: var statements }] || statements.Count == 0)
            return false;

        for (int i = 0; i < statements.Count; i++)
        {
            var statement = statements[i];
            if (statement is Return { Value: not null })
                return i == statements.Count - 1;
            if (statement is ExpressionStatement)
                continue;
            if (allowLocalStatements && statement is StoreLocal)
                continue;
            if (allowLocalStatements && statement is StoreStackSlot)
                continue;
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
