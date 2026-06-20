namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises csc's reference-type <c>using</c> lowering into a
/// <see cref="UsingStatement"/>. The proven shape, after EH and guard
/// structuring plus return sinking:
/// <code>
///   T V_0 = resource;
///   try { BODY }
///   finally { if (V_0) ((IDisposable)V_0).Dispose(); }
/// </code>
/// becomes <c>using (T V_0 = resource) { BODY }</c>. Scoped to the
/// interface-dispose/null-guard shape csc emits for reference-type resources;
/// value-type/constrained dispose remains flat for a later slice.
/// </summary>
public sealed class UsingStatementPass : IIrPass
{
    public string Name => "using-statement";

    sealed record Match(StoreLocal StoreResource, TryFinally TryFinally);

    static readonly TypeRef s_void = TypeRef.CoreLib("System", "Void");

    public void Run(IrFunction function, PassContext context)
    {
        while (TransformOne(function, context.Stepper))
        {
        }
    }

    static bool TransformOne(IrFunction function, Stepper stepper)
    {
        foreach (var block in function.Descendants.OfType<Block>().ToList())
        {
            var children = block.Children;
            for (int i = 0; i + 1 < children.Count; i++)
            {
                if (TryMatch(children[i], children[i + 1]) is not { } match)
                    continue;

                if (ReferencesLocal(match.StoreResource.Value, match.StoreResource.Index)
                    || !ReferencedOnlyWithin(function, match.StoreResource.Index, [match.StoreResource, match.TryFinally]))
                {
                    continue;
                }

                var resource = (IrExpression)match.StoreResource.DetachChildren()[0];
                var body = match.TryFinally.TryBody;
                body.Detach();
                var usingStatement = new UsingStatement(match.StoreResource.Index, match.StoreResource.Type, resource, body);
                stepper.StepOver("raise dispose try/finally to using", match.TryFinally);
                match.TryFinally.ReplaceWith(usingStatement);
                match.StoreResource.Detach();
                return true;
            }
        }
        return false;
    }

    static Match? TryMatch(IrNode first, IrNode second)
    {
        if (first is not StoreLocal storeResource || second is not TryFinally tryFinally)
            return null;

        if (tryFinally.FinallyBody.Blocks is not [{ Children: [IfStatement guard] }]
            || guard.Else is not null
            || guard.Condition is not LoadLocal guardLoad
            || guardLoad.Index != storeResource.Index
            || guard.Then.Children is not [ExpressionStatement { Expression: Call dispose }]
            || !IsIDisposableDispose(dispose)
            || dispose.Arguments is not [LoadLocal disposed]
            || disposed.Index != storeResource.Index)
        {
            return null;
        }

        return new Match(storeResource, tryFinally);
    }

    static bool IsIDisposableDispose(Call call)
        => call.IsVirtual
            && call.Callee.Name == "Dispose"
            && call.Callee.HasThis
            && call.Callee.TypeArguments.IsEmpty
            && call.Callee.ParameterTypes.IsEmpty
            && call.Callee.ReturnType.Equals(s_void)
            && call.Callee.DeclaringType is
                { Namespace: "System", Name: "IDisposable", Assembly: TypeRef.CoreLibrary or "System.Runtime" };

    static bool ReferencedOnlyWithin(IrFunction function, int index, IrNode[] allowed)
    {
        foreach (var node in function.Descendants)
        {
            bool references = node switch
            {
                LoadLocal load => load.Index == index,
                StoreLocal store => store.Index == index,
                LoadLocalAddress address => address.Index == index,
                _ => false,
            };
            if (references && !allowed.Any(root => IsInside(node, root)))
                return false;
        }
        return true;
    }

    static bool ReferencesLocal(IrNode root, int index)
        => root.Descendants.Prepend(root).Any(node => node switch
        {
            LoadLocal load => load.Index == index,
            StoreLocal store => store.Index == index,
            LoadLocalAddress address => address.Index == index,
            _ => false,
        });

    static bool IsInside(IrNode node, IrNode root)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, root))
                return true;
        }
        return false;
    }
}
