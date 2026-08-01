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
/// (recursion / nesting), which keeps the import non-recursive. Every call declines when the
/// seam is absent, and each is stamped as such.</para>
/// </summary>
public sealed class LocalFunctionRaisingPass : IIrPass
{
    public string Name => "local-function-raising";

    public void Run(IrFunction function, PassContext context)
    {
        // Deliberately NOT gated on the seam being present. Without it no body can be
        // imported, so every local-function reference is declined — and three shipped
        // output paths print with no seam (CSharpBodyDiff and two ResearchViews lenses),
        // where staying silent reproduces #3631 verbatim: a decoded name declared
        // nowhere, reported Full.
        //
        // The raised set is keyed on the FULL synthesized identity, never on the decoded
        // source name. `<M>g__F|0_0` and `<M>g__F|0_1` are distinct local functions that
        // share the source name `F`, so a name-keyed set would let a declined reference
        // borrow a raised sibling's declaration and bind to the WRONG function — output
        // that compiles and silently means something else.
        var raised = context.ImportMethodBody is null
            ? []
            : RaiseCalls(function, context);

        // Record what was decided while it is known. Neither consumer may re-derive it
        // from the name: before this pass runs, a local function that WILL be raised
        // carries an identical mangled name, so the name shape answers nothing (#3631).
        //
        // Calls and method groups reach opposite states by different routes:
        //
        //  - RaiseCalls rewrites every raised CALL site to a LocalFunctionInvocation, so
        //    a surviving Call is always one that was not raised.
        //  - It rewrites nothing else, so a method group (`Func<int, int> d = F;`) or a
        //    function-pointer address (`&F`) survives even when the method WAS raised.
        //    Those are Raised, not Declined: the declaration exists and the reference is
        //    spellable — but only unqualified (see LocalFunctionRaiseState.Raised).
        foreach (var node in function.Descendants)
        {
            if (LocalFunctionReference(node) is not { } method)
                continue;

            var state = State(method);
            switch (node)
            {
                case Call call:
                    call.MarkLocalFunctionRaise(state);
                    break;
                case DelegateCreation delegateCreation:
                    delegateCreation.MarkLocalFunctionRaise(state);
                    break;
                // `ldftn` imports as LoadFunctionPointer and only becomes AddressOfMethod
                // in MethodAddressPass, which runs AFTER this one — so this is the node
                // actually present here. MethodAddressPass forwards `pointer.Method`
                // into the new node, carrying the stamp with it. AddressOfMethod is
                // stamped too so the sweep does not silently depend on that ordering.
                case LoadFunctionPointer pointer:
                    pointer.MarkLocalFunctionRaise(state);
                    break;
                case AddressOfMethod addressOf:
                    addressOf.MarkLocalFunctionRaise(state);
                    break;
            }
        }

        LocalFunctionRaiseState State(MethodRef method)
            => raised.Contains(Identity(method))
                ? LocalFunctionRaiseState.Raised
                : LocalFunctionRaiseState.Declined;
    }

    /// <summary>
    /// The local function a node references, if it references one. Every reference to a
    /// local function reaches the output through one of these four nodes, which is the
    /// set <c>EveryMethodRefBearingNodeIsEitherSweptOrJustifiablyUnreachable</c> pins.
    /// </summary>
    /// <remarks>
    /// Keyed on the name SHAPE rather than the CompilerGenerated fact that gates raising,
    /// because the question here is about this pass's own output: a mangled reference left
    /// undeclared must be spelled honestly even when that metadata fact was unavailable
    /// (hand-written or obfuscated IL).
    /// </remarks>
    static MethodRef? LocalFunctionReference(IrNode node)
    {
        var method = node switch
        {
            Call call => call.Callee,
            DelegateCreation delegateCreation => delegateCreation.Method,
            LoadFunctionPointer pointer => pointer.Method,
            AddressOfMethod addressOf => addressOf.Method,
            _ => null,
        };

        return method is not null && GeneratedCodeIdentity.IsSynthesizedLocalFunctionName(method.Name)
            ? method
            : null;
    }

    // Keyed on the declaring TypeRef itself, never on its rendered display text.
    // ToDisplayString omits namespace and assembly, so `NsA.Owner` and `NsB.Owner`
    // — or same-named types from two assemblies — render identically and would let
    // a reference to one borrow the other's declaration and bind to the WRONG
    // method. TypeRef implements hand-written structural equality (assembly,
    // namespace, name, and recursively type arguments), so it is safe as a key and
    // does not hit the ImmutableArray reference-equality trap that rules out using
    // the MethodRef record itself.
    internal static (TypeRef Type, string Name) Identity(MethodRef method)
        => (method.DeclaringType, method.Name);

    /// <summary>
    /// Whether a raised body's type parameters are exactly the host's, so that a
    /// declaration with no type-parameter list and call sites with no type arguments
    /// mean what the original meant.
    /// </summary>
    /// <remarks>
    /// Sound only when the substitution at EVERY reference is the identity: argument
    /// <c>i</c> must be a method generic parameter carrying the same name the body
    /// declares at position <c>i</c>. A reference lies inside the host, so its method
    /// generic parameters ARE the host's — matching positionally against them therefore
    /// establishes host membership too, and a separate membership test was measured to
    /// gate nothing. A body whose parameter count is non-zero but whose names metadata
    /// does not supply, or supplies empty, is declined rather than guessed at; that arm
    /// is defensive and no fixture reaches it.
    /// <para>
    /// References, not just calls. A method group or <c>&amp;F</c> is not rewritten by
    /// <see cref="RaiseCalls"/> and so is not in the call group, but it still spells the
    /// raised declaration's name. Judging on calls alone let a local function that is
    /// called as <c>Own&lt;T&gt;(value)</c> and taken as <c>&amp;Own&lt;int&gt;</c> raise
    /// to <c>static int Own(T x)</c> and then emit <c>delegate*&lt;int, int&gt; f = &amp;Own</c>
    /// — CS8757, at Full.
    /// </para>
    /// </remarks>
    static bool TypeParametersAreTheHostsOwn(MethodSignature body, List<MethodRef> references)
    {
        if (body.GenericParameterCount == 0)
            return true;
        if (body.GenericParameterNames.Length != body.GenericParameterCount)
            return false;

        var names = body.GenericParameterNames;
        if (names.Any(string.IsNullOrEmpty))
            return false;

        foreach (var reference in references)
        {
            var arguments = reference.TypeArguments;
            if (arguments.Length != names.Length)
                return false;
            for (int i = 0; i < arguments.Length; i++)
            {
                if (arguments[i] is not
                    { Kind: TypeRefKind.MethodGenericParameter, GenericParameterName: var argumentName }
                    || !string.Equals(argumentName, names[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Every reference the body makes to itself, of any node kind — not just the calls
    /// <see cref="RewriteSelfCalls"/> rewrites.
    /// </summary>
    static List<MethodRef> SelfReferences(IrFunction body, (TypeRef Type, string Name) identity)
        => body.Descendants
            .Select(LocalFunctionReference)
            // .Equals, not ==. TypeRef implements IEquatable but declares no operator ==,
            // so ValueTuple's == compares the declaring types by REFERENCE and every
            // comparison here is false — which silently emptied this list. ValueTuple's
            // Equals goes through EqualityComparer<T>.Default, which is structural, and
            // is the same route the Identity-keyed dictionary and GroupBy already take.
            .Where(m => m is not null && Identity(m).Equals(identity))
            .Select(m => m!)
            .ToList();

    /// <summary>Raises what it can, and reports the identities it actually declared.</summary>
    static HashSet<(TypeRef Type, string Name)> RaiseCalls(IrFunction function, PassContext context)
    {
        var raised = new HashSet<(TypeRef Type, string Name)>();

        // Every reference, not just the calls. A method group or `&F` is not rewritten
        // here and so never appears in a call group, but it still spells whatever
        // declaration this pass produces, so it gets a vote on whether raising is sound.
        var referencesByIdentity = function.Descendants
            .Select(LocalFunctionReference)
            .Where(m => m is not null)
            .Select(m => m!)
            .GroupBy(Identity)
            .ToDictionary(g => g.Key, g => g.ToList());

        var groups = function.Descendants.OfType<Call>()
            .Where(c => c.Parent is not null && GeneratedCodeIdentity.IsLocalFunctionMethod(c.Callee))
            // Group by the callee's stable identity, not the MethodRef record:
            // ParameterTypes is an ImmutableArray, whose Equals is reference (not
            // structural), so two call sites to the same local function carry
            // non-equal MethodRef instances and would split into separate groups.
            // Local-function mangled names are unique within a type. This is the
            // same key the raised set uses, so what is grouped and what is recorded
            // as raised can never disagree.
            .GroupBy(c => Identity(c.Callee))
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

                // LocalFunctionStatement has no type-parameter list, so a type parameter
                // the raised body declares itself cannot be written down: the declaration
                // comes out as `static int Both(U u)` with `U` bound to nothing (CS0246)
                // and the call sites lose their type arguments (CS0411) — #3631's failure
                // mode exactly, reported as Full.
                //
                // Dropping the list is only sound when it changes nothing: when every call
                // site instantiates the body's type parameters with the HOST's parameters
                // of the same name, so the substitution is the identity and the body's
                // spelling already reads correctly at the declaration site. That is the
                // ordinary case of a non-generic local function inside a generic METHOD,
                // which inherits that method's type parameters — real framework code
                // depends on it raising (VectorMath.HypotSingle's CoreImpl).
                //
                // Neither half of that test can be dropped. Judging from the call site
                // alone misses `Own<U>(U u)` called as `Own<T>(value)` inside `M<T>`, whose
                // arguments are all method generic parameters. Judging from the body's
                // names alone misses shadowing, which C# permits with only a warning
                // (CS8387): `Own<T>(T x)` inside `M<T>` declares its OWN `T`, so the name
                // is a host name while the parameter is not the host's — raising it spelled
                // `static int Own(T x)` and called it as `Own(1)`/`Own("x")` (CS1503), or
                // as `Own(u)` for a `U` in `M<T, U>` (CS1503). Matching NAMES POSITIONALLY
                // against each call site's arguments is what rejects both.
                // The body's own self-references vote too. RewriteSelfCalls drops their
                // type arguments exactly as the host call sites' are dropped, so a
                // recursive `Own<int>(1, false)` inside `Own<T>(T x)` raised to
                // `static int Own(T x)` calling itself as `Own(1, false)` — CS1503, at
                // Full. They are not in the host's descendants, so they must be gathered
                // from the body.
                if (!referencesByIdentity.TryGetValue(group.Key, out var references)
                    || !TypeParametersAreTheHostsOwn(body.Signature, references)
                    || !TypeParametersAreTheHostsOwn(body.Signature, SelfReferences(body, group.Key)))
                {
                    continue;
                }
                // Mutual or nested local-function calls are still out of this slice.
                // A self-call is recoverable: rewrite it to the same local-function
                // invocation used by the host call sites after the nested pipeline
                // has run without re-entering this method's import.
                if (HasOtherLocalFunctionCall(body, method))
                    continue;

                IrPasses.Run(body, IrPasses.Default, context);

                if (HasOtherLocalFunctionCall(body, method))
                    continue;
                // And vote again on the body's self-references, for the same reason the
                // foreign check above runs twice: IrPasses.Run can ADD reference nodes.
                // LambdaRaisingPass imports a lambda's body and attaches it here, so a
                // self-reference written inside a lambda is not a node at all when the
                // vote above happens — and RewriteSelfCalls, which runs after this point,
                // rewrites calls anywhere in the body, dropping their type arguments.
                // A non-identity `Own<int>(1)` inside a lambda in `Own<T>(T x)` would
                // become `Own(1)` against `static string Own(T x)`: CS1503, at Full.
                //
                // Defensive: no fixture reaches this arm, because a lambda in a generic
                // context is never raised (#3665), and a local function only has type
                // parameters to judge when its host is generic. That makes today's safety
                // a coincidence of an unrelated open bug rather than a property of this
                // pass. LambdasInGenericContextsAreNotRaised pins the coincidence, so
                // fixing #3665 fails there and points here instead of silently reopening
                // #3631.
                if (!TypeParametersAreTheHostsOwn(body.Signature, SelfReferences(body, group.Key)))
                    continue;
                if (environment is not null && !SubstituteEnvironment(body, environment))
                    continue;
                bool allowLocals = environment is null;
                if (!allowLocals && !body.Locals.IsEmpty
                    || body.Descendants.OfType<UnsupportedNode>().Any()
                    || !IsPrintableBody(body, allowLocals))
                    continue;

                string name = CSharpNaming.MethodName(method.Name);
                RewriteSelfCalls(body, method, name);
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
                raised.Add(Identity(method));
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
                // Merge the raised body's resolved type info into the enclosing
                // function. The body was imported from a separate method, so the
                // host never materialized shapes/enum members/underlying types/
                // union types for definitions only this local function references.
                // The metadata-free printer reads these off the enclosing function
                // for every local-function print path — inline (expression-bodied
                // or block) and the reconstructed nested scope — so without the
                // merge an enum-typed constant used only inside the local function
                // renders as a bare int (CS1503/CS0266) even though the host body
                // spells the same value correctly (issue #2983). Outer entries win
                // on collision: a definition the host already resolved keeps its
                // authoritative shape.
                function.TypeShapes = MergeMap(function.TypeShapes, body.TypeShapes);
                function.EnumMembers = MergeMap(function.EnumMembers, body.EnumMembers);
                function.EnumUnderlyingTypes = MergeMap(function.EnumUnderlyingTypes, body.EnumUnderlyingTypes);
                function.UnionTypes = MergeSet(function.UnionTypes, body.UnionTypes);
                function.ByRefLikeTypes = MergeSet(function.ByRefLikeTypes, body.ByRefLikeTypes);
                environment?.Elide();
            }
        }

        if (declarations.Count == 0)
            return raised;

        // Local functions are declarations, valid even after a return, so they
        // append to the last top-level block — the idiomatic trailing placement.
        var block = function.Body.Blocks[^1];
        foreach (var declaration in declarations)
            block.Add(declaration);
        return raised;
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

    /// <summary>
    /// Whether the body reaches a DIFFERENT local function. Mutual and nested local
    /// functions are out of this slice, and a body that touches one is declined.
    /// </summary>
    /// <remarks>
    /// Every reference kind, not just calls. A sibling body holding <c>&amp;A&lt;int&gt;</c>
    /// was invisible here, so the sibling raised and its printed body bound <c>&amp;A</c> to
    /// the RAISED <c>A</c> — which carries the host's type arguments, not the <c>int</c>
    /// the IL names. That compiles and returns the wrong type at run time. A foreign
    /// reference also never reaches the gate that judges the referee, because
    /// <see cref="RaiseCalls"/> gathers references from the host and from the referee's
    /// own body — never from a sibling's.
    /// </remarks>
    static bool HasOtherLocalFunctionCall(IrFunction body, MethodRef method)
        => body.Descendants
            .Select(LocalFunctionReference)
            .Any(m => m is not null && !SameLocalFunctionMethod(m, method));

    static void RewriteSelfCalls(IrFunction body, MethodRef method, string name)
    {
        foreach (var call in body.Descendants.OfType<Call>().ToList())
        {
            if (!SameLocalFunctionMethod(call.Callee, method))
                continue;

            var arguments = call.DetachChildren().Cast<IrExpression>().ToList();
            call.ReplaceWith(new LocalFunctionInvocation(name, method.ReturnType, arguments));
        }
    }

    static bool SameLocalFunctionMethod(MethodRef left, MethodRef right)
        => left.Name == right.Name
            && Equals(left.DeclaringType, right.DeclaringType);

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
            if (statement is IfStatement)
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

    static IReadOnlyDictionary<TKey, TValue> MergeMap<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue> outer, IReadOnlyDictionary<TKey, TValue> inner)
        where TKey : notnull
    {
        if (inner.Count == 0)
            return outer;
        var result = outer as ImmutableDictionary<TKey, TValue> ?? ImmutableDictionary.CreateRange(outer);
        foreach (var (key, value) in inner)
            if (!result.ContainsKey(key))
                result = result.SetItem(key, value);
        return result;
    }

    static IReadOnlySet<TypeRef> MergeSet(IReadOnlySet<TypeRef> outer, IReadOnlySet<TypeRef> inner)
    {
        if (inner.Count == 0)
            return outer;
        var result = outer as ImmutableHashSet<TypeRef> ?? ImmutableHashSet.CreateRange(outer);
        return result.Union(inner);
    }
}
