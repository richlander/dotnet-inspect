namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises csc's <c>using</c> lowering into a <see cref="UsingStatement"/>. Three
/// shapes are recognized, after EH and guard structuring plus return sinking:
/// <code>
///   // reference type — null-guarded interface dispose
///   T V_0 = resource;
///   try { BODY }
///   finally { if (V_0) ((IDisposable)V_0).Dispose(); }
///
///   // value type — unguarded constrained dispose (no null check)
///   T V_0 = resource;
///   try { BODY }
///   finally { V_0.Dispose(); }
///
///   // pattern dispose — unguarded instance Dispose on a same-assembly value type
///   T V_0 = resource;
///   try { BODY }
///   finally { V_0.Dispose(); }
/// </code>
/// all become <c>using (T V_0 = resource) { BODY }</c>. The dispose receiver is
/// a <c>LoadLocal</c> (reference type) or <c>LoadLocalAddress</c> (value type,
/// constrained callvirt).
/// </summary>
public sealed class UsingStatementPass : IIrPass
{
    public string Name => "using-statement";

    sealed record Match(StoreLocal StoreResource, TryFinally TryFinally);

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
                if (TryMatch(function, children[i], children[i + 1]) is not { } match)
                    continue;

                if (ReferencesLocal(match.StoreResource.Value, match.StoreResource.Index)
                    || StoresLocal(match.TryFinally, match.StoreResource.Index)
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

    static Match? TryMatch(IrFunction function, IrNode first, IrNode second)
    {
        if (first is not StoreLocal storeResource || second is not TryFinally tryFinally)
            return null;

        if (tryFinally.FinallyBody.Blocks is not [{ Children: [var only] }])
            return null;

        bool disposes = only switch
        {
            // Reference type: finally { if (V_0) ((IDisposable)V_0).Dispose(); }
            IfStatement
            {
                Else: null,
                Condition: LoadLocal guardLoad,
                Then.Children: [ExpressionStatement { Expression: Call guardedDispose }],
            } => guardLoad.Index == storeResource.Index && IsDisposeOf(function, guardedDispose, storeResource),
            // Value type: finally { V_0.Dispose(); } — no null guard.
            ExpressionStatement { Expression: Call bareDispose } => IsDisposeOf(function, bareDispose, storeResource),
            _ => false,
        };

        return disposes ? new Match(storeResource, tryFinally) : null;
    }

    /// <summary>An <c>IDisposable.Dispose()</c> or pattern <c>Dispose()</c> call whose receiver is the resource local.</summary>
    static bool IsDisposeOf(IrFunction function, Call dispose, StoreLocal storeResource)
    {
        if (dispose.Arguments is not [var receiver])
            return false;

        if (!IsResourceReceiver(receiver, storeResource.Index))
            return false;

        return MemberIdentity.IsIDisposableDispose(dispose)
            || IsPatternDispose(function, dispose, storeResource);
    }

    static bool IsResourceReceiver(IrExpression receiver, int index) => receiver switch
    {
        LoadLocal load => load.Index == index,
        LoadLocalAddress address => address.Index == index,
        _ => false,
    };

    static bool IsPatternDispose(IrFunction function, Call dispose, StoreLocal storeResource)
    {
        if (dispose.IsVirtual
            || dispose.Callee is not
            {
                HasThis: true,
                Name: "Dispose",
                TypeArguments.IsEmpty: true,
                ParameterTypes.IsEmpty: true,
                ReturnType: var returnType,
            }
            || !returnType.Equals(TypeRef.CoreLib("System", "Void"))
            || !dispose.Callee.DeclaringType.Equals(storeResource.Type))
        {
            return false;
        }

        return function.TypeShapes.GetValueOrDefault(storeResource.Type) is TypeShape.ValueType or TypeShape.Enum;
    }

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

    static bool StoresLocal(IrNode root, int index)
        => root.Descendants.Prepend(root).Any(node => node is StoreLocal store && store.Index == index);

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
