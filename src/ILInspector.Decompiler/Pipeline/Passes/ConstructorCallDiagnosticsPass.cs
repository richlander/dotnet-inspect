namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Marks direct constructor calls that survived all constructor-raising passes as
/// explicit unsupported residuals. C# can spell only a constructor chain
/// (<c>: base(...)</c>/<c>: this(...)</c>) or an object creation expression; a
/// leftover <c>call instance .ctor</c> body statement has no faithful statement
/// spelling and must not leak as <c>base(...)</c> in arbitrary methods.
/// </summary>
public sealed class ConstructorCallDiagnosticsPass : IIrPass
{
    public string Name => "constructor-call-diagnostics";

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var statement in function.Descendants.OfType<ExpressionStatement>().ToList())
        {
            if (statement.Expression is not Call { Callee: { Name: ".ctor", HasThis: true } } call)
                continue;
            if (IsLiftableConstructorChain(function, statement, call))
                continue;

            var marker = new UnsupportedNode(
                call.SourceOffset >= 0 ? call.SourceOffset : statement.SourceOffset,
                "call .ctor",
                $"direct constructor call to {call.Callee.DeclaringType.ToDisplayString()} is not representable as a C# statement");
            marker.InheritSourceOffset(call);
            var replacement = new ExpressionStatement(marker);
            replacement.InheritSourceOffset(statement);
            context.Stepper.StepOver($"mark unraised {call.Callee.DeclaringType.Name}..ctor call unsupported", statement);
            statement.ReplaceWith(replacement);
        }
    }

    static bool IsLiftableConstructorChain(IrFunction function, ExpressionStatement statement, Call call)
    {
        if (function.Name != ".ctor"
            || call.Arguments is not [LoadArgument { Index: 0 }, ..]
            || !IsConstructorChainTarget(function, call.Callee.DeclaringType)
            || function.Body.Blocks is not [{ } entry, ..])
        {
            return false;
        }

        int chainIndex = ChildIndexOf(entry, statement);
        if (chainIndex < 0)
            return false;

        var prologue = entry.Children.Take(chainIndex);
        // Standard prologue: constant field initializers (no place loads) precede
        // the chain call, so they re-express as field declarations + : base().
        if (prologue.All(IsFieldInitializerStore))
            return true;

        // Record / primary-constructor prologue: the synthesized constructor
        // stores its parameters into the backing fields (this.<X>k__BackingField
        // = X) and THEN calls the implicit parameterless object..ctor. The printer
        // does NOT lift these param-dependent stores into pre-base position — it
        // emits them as body statements and suppresses the parameterless base call
        // — so the stores effectively run AFTER the implicit base ctor in the
        // rendered C#. That reordering is only observably safe when the base ctor
        // does nothing: System.Object's parameterless ctor is a guaranteed no-op,
        // so eliding it keeps the constructor valid Full instead of leaking an
        // owned base-ctor residual. A non-trivial base ctor (e.g. one calling a
        // virtual the derived type overrides to read the field) must stay a
        // residual. See #1639.
        return IsElidableObjectBaseCall(function, call)
            && prologue.All(IsInstanceFieldStore);
    }

    /// <summary>A no-argument <c>call instance .ctor</c> on <c>this</c> to corelib <see cref="object"/> — the one base ctor whose no-op body makes eliding it (and thus reordering preceding field stores after it) observably safe.</summary>
    static bool IsElidableObjectBaseCall(IrFunction function, Call call)
        => call.Arguments is [LoadArgument { Index: 0 }]
            && call.Callee.DeclaringType is { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System", Name: "Object" }
            && function.BaseType is { } baseType
            && SameDefinition(call.Callee.DeclaringType, baseType);

    /// <summary>A <c>this.field = value</c> store, regardless of whether the value reads constructor parameters (the record primary-constructor shape).</summary>
    static bool IsInstanceFieldStore(IrNode node)
        => node is StoreField { HasInstance: true, Instance: LoadArgument { Index: 0 } };

    static bool IsConstructorChainTarget(IrFunction function, TypeRef declaringType)
        => SameDefinition(declaringType, function.DeclaringType)
            || function.BaseType is { } baseType && SameDefinition(declaringType, baseType);

    static bool SameDefinition(TypeRef left, TypeRef right)
        => Definition(left).Equals(Definition(right));

    static TypeRef Definition(TypeRef type)
        => type.Kind == TypeRefKind.GenericInstance && type.ElementType is { } definition ? definition : type;

    static int ChildIndexOf(Block block, IrNode node)
    {
        for (int i = 0; i < block.Children.Count; i++)
            if (ReferenceEquals(block.Children[i], node))
                return i;
        return -1;
    }

    static bool IsFieldInitializerStore(IrNode node)
        => node is StoreField { HasInstance: true, Instance: LoadArgument { Index: 0 } } store
            && !ReferencesPlace(store.Value);

    static bool ReferencesPlace(IrExpression value)
    {
        foreach (var node in (IEnumerable<IrNode>)[value, .. value.Descendants])
        {
            if (node is LoadArgument or LoadLocal or LoadStackSlot or LoadLocalAddress or LoadArgumentAddress)
                return true;
        }
        return false;
    }
}
