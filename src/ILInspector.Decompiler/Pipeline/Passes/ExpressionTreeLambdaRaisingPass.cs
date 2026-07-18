using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises the canonical C# expression-tree lambda construction —
/// <c>ExpressionLambdaRewriter</c>'s lowering of
/// <c>Expression&lt;Func&lt;…&gt;&gt; f = p =&gt; e</c> to a run of
/// <c>System.Linq.Expressions</c> factory calls — back into the source lambda
/// <c>p =&gt; e</c> (issue #2864, first slice: homogeneous <c>int</c> arithmetic).
///
/// <para>Unlike the delegate-lambda raise (<see cref="LambdaRaisingPass"/>) there
/// is no synthesized closure method and no <c>[CompilerGenerated]</c> marker: the
/// lowering emits the same public factory calls a source author can hand-write.
/// So this is <b>not</b> a "detect the compiler" raise — it is a
/// <b>semantics-preserving rewrite</b>. The recovered C# lambda lowers (by the C#
/// compiler) to <em>exactly</em> the matched factory graph, so replacing the graph
/// with the lambda leaves the returned <see cref="System.Linq.Expressions.Expression{TDelegate}"/>
/// identical regardless of whether the source was a lambda or the equivalent
/// hand-written calls. Soundness therefore rests on matching only the exact
/// canonical, fully-owned graph and on preserving parameter identity, not on
/// provenance.</para>
///
/// <para>The owned canonical graph is the whole method body as one straight-line
/// block:</para>
/// <list type="number">
/// <item>one <c>StoreLocal p_i = Expression.Parameter(typeof(int), "name")</c> per
/// parameter — the parameter's <b>single</b> owning definition;</item>
/// <item><c>StoreStackSlot arr = new ParameterExpression[N]</c>;</item>
/// <item>the body <c>Expression.Add/Subtract/Multiply/Divide/Modulo</c> tree over
/// parameter loads and <c>Expression.Constant(c, typeof(int))</c> leaves, spilled
/// to a single-use slot or inlined into the <c>Lambda</c> call;</item>
/// <item><c>arr[i] = p_i</c> element stores registering each parameter;</item>
/// <item><c>return Expression.Lambda&lt;Func&lt;int,…,int&gt;&gt;(body, arr);</c></item>
/// </list>
///
/// <para>Every statement of the block must be one of those and nothing else, and
/// each parameter must be referenced only through loads of its single owning local
/// (in the body and in the array). This is the identity guard that rejects the
/// hand-composed near miss where the parameter is aliased through both a stack slot
/// and a local (the body reads the slot, the array reads the local): two
/// independent value sources whose sameness cannot be proven, so the lambda's
/// declared parameter and its body reference might not be the same node. Captured,
/// member-token (<c>ldtoken</c>), method-call, comparison, conversion, non-<c>int</c>,
/// shared, mutated, and multi-block graphs all fall outside the subset and stay in
/// their honest factory-call / <c>Partial</c> form for later slices (#2864).</para>
/// </summary>
public sealed class ExpressionTreeLambdaRaisingPass : IIrPass
{
    public string Name => "expression-tree-lambda-raising";

    static bool IsExpressionFactory(IrExpression node, string name, out Call call)
    {
        if (node is Call { Callee: { HasThis: false } callee } c
            && callee.Name == name
            && callee.DeclaringType is { Kind: TypeRefKind.Definition, Namespace: "System.Linq.Expressions", Name: "Expression" })
        {
            call = c;
            return true;
        }
        call = null!;
        return false;
    }

    static bool IsInt(TypeRef? type) => type is not null && type.Equals(TypeRef.CoreLib("System", "Int32"));

    public void Run(IrFunction function, PassContext context)
    {
        if (function.Body.Blocks is not [{ } block]
            || block.Children.Count == 0
            || block.Children[^1] is not Return { Value: { } returnValue } ret)
            return;

        // The returned value is Expression.Lambda<TDelegate>(body, parameters[]).
        if (!IsExpressionFactory(returnValue, "Lambda", out var lambdaCall)
            || lambdaCall.Callee.TypeArguments is not [var delegateType]
            || lambdaCall.Arguments.Count != 2
            || !IsHomogeneousIntFunc(delegateType, out int arity))
            return;

        // Resolve the body and parameter-array arguments through their single-use /
        // twice-used spill slots. A slot referenced any other way is not owned.
        if (ResolveConstruction(function, block, lambdaCall, arity) is not { } plan)
            return;

        // Rebuild the parameter list and the body over LoadArgument references, in
        // parameter (array) order. RaiseBody rejects anything outside the int
        // arithmetic subset, so a member/comparison/method-call/convert body bails.
        var parameters = plan.Parameters;
        var indexByLocal = new Dictionary<int, int>();
        for (int i = 0; i < plan.ParameterLocals.Length; i++)
            indexByLocal[plan.ParameterLocals[i]] = i;

        if (RaiseBody(plan.Body, parameters, indexByLocal) is not { } body)
            return;

        var lambdaBody = new BlockContainer();
        var lambdaBlock = new Block();
        lambdaBlock.Add(new Return(body));
        lambdaBody.Add(lambdaBlock);

        var lambda = new Lambda(
            delegateType,
            parameters,
            locals: [],
            localNames: [],
            usesUpdatedMemorySafetyRules: function.UsesUpdatedMemorySafetyRules,
            skipLocalsInit: function.SkipLocalsInit,
            lambdaBody);
        lambda.InheritSourceOffset(ret);

        context.Stepper.StepOver("raise expression-tree lambda", ret);
        ret.Value.ReplaceWith(lambda);

        // The construction's setup statements are subsumed by the lambda; drop them
        // so the block is exactly `return p => e;`.
        foreach (var statement in plan.SetupStatements)
            statement.Detach();
    }

    sealed record ConstructionPlan(
        IrExpression Body,
        ImmutableArray<int> ParameterLocals,
        ImmutableArray<Parameter> Parameters,
        IReadOnlyList<IrNode> SetupStatements);

    // Match the whole-block canonical construction and prove full local ownership:
    // every statement is one of the construction's parts, each parameter has a
    // single owning Expression.Parameter local, and the body/array slots are used
    // only within the construction. Returns null on any deviation.
    static ConstructionPlan? ResolveConstruction(IrFunction function, Block block, Call lambdaCall, int arity)
    {
        var statements = block.Children;

        // The parameter-array argument: a slot holding new ParameterExpression[N],
        // possibly reached through a chain of `dup` copies (Sₖ = Sₖ₋₁) the importer
        // materializes between element stores. Resolve the whole alias set back to
        // the single rooting NewArray.
        if (lambdaCall.Arguments[1] is not LoadStackSlot { Slot: var lambdaArrSlot }
            || !TryResolveArraySlots(function, lambdaArrSlot, arity, out var arraySlots, out var arrStore, out var arrayCopies))
            return null;

        // Every alias slot must be used only within the construction: each copy
        // reads one alias, each element store reads one alias, and the lambda arg
        // reads one — nothing else touches the array.
        int arrayLoads = arraySlots.Sum(slot => CountSlotLoads(function, slot));
        if (arrayLoads != arrayCopies.Count + arity + 1)
            return null;

        // The body argument: either a single-use spill slot or an inline factory call.
        IrExpression bodyGraph;
        StoreStackSlot? bodyStore = null;
        switch (lambdaCall.Arguments[0])
        {
            case LoadStackSlot { Slot: var bodySlot } when CountSlotLoads(function, bodySlot) == 1:
                if (SingleStore(function, bodySlot) is not { } store)
                    return null;
                bodyStore = store;
                bodyGraph = store.Value;
                break;
            case Call inlineBody:
                bodyGraph = inlineBody;
                break;
            default:
                return null;
        }

        // The element stores arr[i] = p_i register each parameter into the array.
        var elementStores = new StoreElement?[arity];
        var parameterLocals = new int[arity];
        foreach (var element in statements.OfType<StoreElement>())
        {
            if (element.Array is not LoadStackSlot { Slot: var slot } || !arraySlots.Contains(slot)
                || element.Index is not Constant { Value: int index }
                || index < 0 || index >= arity
                || element.Value is not LoadLocal { Index: var paramLocal })
                return null;
            if (elementStores[index] is not null)
                return null;  // duplicate index
            elementStores[index] = element;
            parameterLocals[index] = paramLocal;
        }
        if (elementStores.Any(e => e is null))
            return null;

        // Each parameter local's single owning definition is Expression.Parameter,
        // typed int, with a literal name. This is the identity guard: the aliased
        // near miss stores the parameter object into a stack slot and copies it into
        // the local, so the local's value is a LoadStackSlot, not Expression.Parameter.
        var parameters = new Parameter[arity];
        var paramStores = new StoreLocal[arity];
        for (int i = 0; i < arity; i++)
        {
            if (SingleLocalStore(function, parameterLocals[i]) is not { Value: { } value } paramStore
                || !IsExpressionFactory(value, "Parameter", out var parameterCall)
                || parameterCall.Arguments is not [TypeOf { Type: { } paramType }, Constant { Value: string name }]
                || !IsInt(paramType))
                return null;
            parameters[i] = new Parameter(name, paramType);
            paramStores[i] = paramStore;
        }

        // Full-ownership ledger: the block is exactly the parameter stores, the
        // array store, the array-copy (dup) stores, the (optional) body store, the
        // element stores, and the return.
        var setup = new List<IrNode>(paramStores);
        setup.Add(arrStore);
        setup.AddRange(arrayCopies);
        if (bodyStore is not null)
            setup.Add(bodyStore);
        setup.AddRange(elementStores.Cast<StoreElement>());

        var accounted = new HashSet<IrNode>(setup, ReferenceEqualityComparer.Instance) { block.Children[^1] };
        if (accounted.Count != statements.Count || statements.Any(s => !accounted.Contains(s)))
            return null;

        return new ConstructionPlan(bodyGraph, [.. parameterLocals], [.. parameters], setup);
    }

    // Resolve the parameter-array slot back to its single rooting NewArray,
    // collecting every alias slot reached through `dup` copies (Sₖ = load Sₖ₋₁)
    // and the copy stores themselves. Each alias slot must be defined exactly once
    // (SingleStore); a shared, mutated, or cyclic slot is not owned and fails.
    static bool TryResolveArraySlots(
        IrFunction function, int startSlot, int arity,
        out HashSet<int> aliasSlots, out StoreStackSlot rootStore, out List<StoreStackSlot> copyStores)
    {
        aliasSlots = new HashSet<int>();
        copyStores = new List<StoreStackSlot>();
        rootStore = null!;
        int current = startSlot;
        while (true)
        {
            if (!aliasSlots.Add(current))
                return false;  // cycle
            if (SingleStore(function, current) is not { } store)
                return false;
            switch (store.Value)
            {
                case NewArray { ElementType: { } elem, Length: Constant { Value: int len } }:
                    if (len != arity
                        || elem is not { Kind: TypeRefKind.Definition, Namespace: "System.Linq.Expressions", Name: "ParameterExpression" })
                        return false;
                    rootStore = store;
                    return true;
                case LoadStackSlot { Slot: var prev }:
                    copyStores.Add(store);
                    current = prev;
                    continue;
                default:
                    return false;
            }
        }
    }

    // The one StoreStackSlot writing the slot in the whole function, or null when it
    // is defined zero or more than once (a shared/mutated slot is not owned).
    static StoreStackSlot? SingleStore(IrFunction function, int slot)
    {
        StoreStackSlot? found = null;
        foreach (var store in function.Descendants.OfType<StoreStackSlot>())
        {
            if (store.Slot != slot)
                continue;
            if (found is not null)
                return null;
            found = store;
        }
        return found;
    }

    static StoreLocal? SingleLocalStore(IrFunction function, int index)
    {
        StoreLocal? found = null;
        foreach (var store in function.Descendants.OfType<StoreLocal>())
        {
            if (store.Index != index)
                continue;
            if (found is not null)
                return null;
            found = store;
        }
        return found;
    }

    static int CountSlotLoads(IrFunction function, int slot)
        => function.Descendants.OfType<LoadStackSlot>().Count(load => load.Slot == slot);

    // Rebuild the body expression over LoadArgument parameter references. Accepts
    // only the int arithmetic subset (binary +,-,*,/,% over parameters and int
    // constants); any other node — a member access, comparison, method call,
    // conversion, non-int constant, or a load of a non-parameter local — bails,
    // keeping that graph in its honest factory-call form.
    static IrExpression? RaiseBody(IrExpression node, ImmutableArray<Parameter> parameters, IReadOnlyDictionary<int, int> indexByLocal)
    {
        switch (node)
        {
            case LoadLocal { Index: var local } when indexByLocal.TryGetValue(local, out int index):
                var parameter = parameters[index];
                return new LoadArgument(index, parameter.Name, parameter.Type);

            case Call when TryArithmeticKind(node, out var kind, out var left, out var right):
                if (RaiseBody(left, parameters, indexByLocal) is not { } raisedLeft
                    || RaiseBody(right, parameters, indexByLocal) is not { } raisedRight
                    || !IsInt(raisedLeft.ResultType) || !IsInt(raisedRight.ResultType))
                    return null;
                return new Binary(kind, isChecked: false, isUnsigned: false, raisedLeft, raisedRight);

            case Call when TryIntConstant(node, out var value):
                return new Constant(value, TypeRef.CoreLib("System", "Int32"));

            default:
                return null;
        }
    }

    static bool TryArithmeticKind(IrExpression node, out BinaryKind kind, out IrExpression left, out IrExpression right)
    {
        left = right = null!;
        kind = default;
        if (!IsExpressionFactory(node, MethodNameFor(node), out var call) || call.Arguments.Count != 2)
            return false;
        var name = call.Callee.Name;
        kind = name switch
        {
            "Add" => BinaryKind.Add,
            "Subtract" => BinaryKind.Subtract,
            "Multiply" => BinaryKind.Multiply,
            "Divide" => BinaryKind.Divide,
            "Modulo" => BinaryKind.Remainder,
            _ => (BinaryKind)(-1),
        };
        if ((int)kind < 0)
            return false;
        left = call.Arguments[0];
        right = call.Arguments[1];
        return true;
    }

    static string MethodNameFor(IrExpression node) => node is Call c ? c.Callee.Name : "";

    // Expression.Constant(box <int-literal>, typeof(int)) — the exact int constant
    // shape ExpressionLambdaRewriter emits. A user-type constant (a reference,
    // enum, or a Constant with no boxed int) bails.
    static bool TryIntConstant(IrExpression node, out int value)
    {
        value = 0;
        if (!IsExpressionFactory(node, "Constant", out var call)
            || call.Arguments is not [Box { Type: { } boxType, Operand: Constant { Value: int literal, Type: { } literalType } }, TypeOf { Type: { } declaredType }]
            || !IsInt(boxType) || !IsInt(literalType) || !IsInt(declaredType))
            return false;
        value = literal;
        return true;
    }

    // The delegate type is System.Func<T1,…,Tn,TResult> with every argument int.
    // Homogeneous int keeps the recovered lambda's literals and operators binding
    // to built-in int arithmetic with no promotion/Convert, so the round-trip tree
    // is identical. arity is the parameter count (n).
    static bool IsHomogeneousIntFunc(TypeRef delegateType, out int arity)
    {
        arity = 0;
        if (delegateType is not { Kind: TypeRefKind.GenericInstance, ElementType: { Namespace: "System", Name: var name }, TypeArguments: { Length: >= 2 } args }
            || !name.StartsWith("Func`", StringComparison.Ordinal)
            || !args.All(IsInt))
            return false;
        arity = args.Length - 1;
        return true;
    }
}
