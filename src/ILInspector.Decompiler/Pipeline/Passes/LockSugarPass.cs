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

        // The synthetic locals must not appear in the body (everything in the
        // try body except the Monitor.Enter we are about to remove).
        if (LeaksLocal(tryFinally.TryBody, storeObject.Index, firstStatement)
            || LeaksLocal(tryFinally.TryBody, storeTaken.Index, firstStatement))
        {
            return null;
        }

        return new Match(storeObject, storeTaken, tryFinally, (ExpressionStatement)firstStatement, storeObject.Value);
    }

    static bool IsMonitorCall(Call call, string method)
        => !call.IsVirtual
            && call.Callee.Name == method
            && call.Callee.DeclaringType is { Namespace: "System.Threading", Name: "Monitor" };

    /// <summary>True when a local index is referenced anywhere in the subtree outside the excluded node.</summary>
    static bool LeaksLocal(IrNode subtree, int index, IrNode excluded)
    {
        foreach (var node in subtree.Descendants)
        {
            if (IsInside(node, excluded))
                continue;
            bool references = node switch
            {
                LoadLocal load => load.Index == index,
                StoreLocal store => store.Index == index,
                LoadLocalAddress address => address.Index == index,
                _ => false,
            };
            if (references)
                return true;
        }
        return false;
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
