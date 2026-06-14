namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises the csc Monitor lock lowering into a <see cref="Lock"/> statement.
/// The lockTaken shape, after EH structuring and the guard structuring pass:
/// <code>
///   object V_0 = obj;
///   bool V_1 = false;
///   try { Monitor.Enter(V_0, ref V_1); BODY }
///   finally { if (V_1) Monitor.Exit(V_0); }
/// </code>
/// becomes <c>lock (obj) { BODY }</c>. Runs after structuring so the finally
/// guard is an <see cref="IfStatement"/>. Transactional: the whole shape,
/// including that the two synthetic locals appear nowhere in BODY, is proven
/// before anything is detached — otherwise the construct is left flat.
/// </summary>
public sealed class LockSugarPass : IIrPass
{
    public string Name => "lock-sugar";

    public void Run(IrFunction function)
    {
        while (TransformOne(function))
        {
        }
    }

    sealed record Match(
        StoreLocal StoreObject,
        StoreLocal StoreTaken,
        TryFinally TryFinally,
        ExpressionStatement EnterStatement,
        IrExpression LockObject);

    static bool TransformOne(IrFunction function)
    {
        foreach (var block in function.Descendants.OfType<Block>().ToList())
        {
            var children = block.Children;
            for (int i = 0; i + 2 < children.Count; i++)
            {
                if (TryMatch(children[i], children[i + 1], children[i + 2]) is not { } match)
                    continue;

                // Soundness: the two synthetic locals must be referenced ONLY
                // by the nodes this rewrite consumes — the defining stores, the
                // Monitor.Enter we remove, and the whole finally we discard.
                // Any other reference (in the lock body, before the lock, or
                // anywhere else in the function) means these are not throwaway
                // lock temporaries; detaching their stores would strand it.
                var consumed = new IrNode[]
                {
                    match.StoreObject, match.StoreTaken, match.EnterStatement, match.TryFinally.FinallyBody,
                };
                if (!ReferencedOnlyWithin(function, match.StoreObject.Index, consumed)
                    || !ReferencedOnlyWithin(function, match.StoreTaken.Index, consumed))
                {
                    continue;
                }

                var lockObject = (IrExpression)match.StoreObject.DetachChildren()[0];
                match.EnterStatement.Detach();
                var body = match.TryFinally.TryBody;
                body.Detach();                          // reparent the try body into the lock
                var lockNode = new Lock(lockObject, body);
                match.TryFinally.ReplaceWith(lockNode); // slot i+2 → Lock
                match.StoreTaken.Detach();              // drop synthetic locals (high index first)
                match.StoreObject.Detach();
                return true;
            }
        }
        return false;
    }

    /// <summary>Matches the three-statement lock shape; null when it does not fit.</summary>
    static Match? TryMatch(IrNode first, IrNode second, IrNode third)
    {
        if (first is not StoreLocal storeObject
            || second is not StoreLocal storeTaken
            || third is not TryFinally tryFinally)
        {
            return null;
        }
        // bool lockTaken = false
        if (storeTaken.Value is not Constant { Value: 0 or false })
            return null;

        // try body must lead with Monitor.Enter(V_object, ref V_taken)
        if (tryFinally.TryBody.Blocks is not [{ Children: [var firstStatement, ..] }, ..])
            return null;
        if (firstStatement is not ExpressionStatement { Expression: Call enter }
            || !IsMonitorCall(enter, "Enter")
            || enter.Arguments is not [LoadLocal enterObject, LoadLocalAddress enterTaken]
            || enterObject.Index != storeObject.Index
            || enterTaken.Index != storeTaken.Index)
        {
            return null;
        }

        // finally must be exactly: if (V_taken) Monitor.Exit(V_object);
        if (tryFinally.FinallyBody.Blocks is not [{ Children: [IfStatement guard] }])
            return null;
        if (guard.Else is not null
            || guard.Condition is not LoadLocal { } takenLoad
            || takenLoad.Index != storeTaken.Index
            || guard.Then.Children is not [ExpressionStatement { Expression: Call exit }]
            || !IsMonitorCall(exit, "Exit")
            || exit.Arguments is not [LoadLocal exitObject]
            || exitObject.Index != storeObject.Index)
        {
            return null;
        }

        return new Match(storeObject, storeTaken, tryFinally, (ExpressionStatement)firstStatement, storeObject.Value);
    }

    /// <summary>
    /// The real <c>System.Threading.Monitor</c> only — matched on assembly
    /// identity, not just namespace/name, so a same-named type in a user
    /// assembly is never sugared into a C# <c>lock</c>. Monitor lives in
    /// corelib (covered by the canonical facade set) but is exposed through
    /// the <c>System.Threading</c> contract, which is the scope a caller
    /// actually references, so both are accepted.
    /// </summary>
    static bool IsMonitorCall(Call call, string method)
        => !call.IsVirtual
            && call.Callee.Name == method
            && call.Callee.DeclaringType is
                { Namespace: "System.Threading", Name: "Monitor", Assembly: TypeRef.CoreLibrary or "System.Threading" };

    /// <summary>True when every reference to <paramref name="index"/> in the function sits inside one of the allowed subtrees.</summary>
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
