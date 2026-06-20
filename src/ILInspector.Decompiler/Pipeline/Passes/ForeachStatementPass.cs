namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises the narrow enumerator-lowering slice of <c>foreach</c>:
/// <code>
/// using (var e = collection.GetEnumerator())
/// {
///     while (e.MoveNext())
///     {
///         T item = e.Current;
///         BODY
///     }
/// }
/// </code>
/// into <c>foreach (T item in collection) { BODY }</c>. The enumerator local must
/// be compiler-hidden (no source local name) so hand-written using/while loops stay
/// at their source altitude.
/// </summary>
public sealed class ForeachStatementPass : IIrPass
{
    public string Name => "foreach-statement";

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var usingStatement in function.Descendants.OfType<UsingStatement>().ToList())
        {
            if (TryMatch(function, usingStatement) is not { } match)
                continue;

            var collection = match.Collection;
            collection.Detach();
            match.CurrentStore.Detach();
            var body = match.Loop.Body;
            body.Detach();
            var foreachStatement = new ForeachStatement(match.CurrentStore.Index, match.CurrentStore.Type, collection, body);
            context.Stepper.StepOver("raise enumerator loop to foreach", usingStatement);
            usingStatement.ReplaceWith(foreachStatement);
            return;
        }
    }

    sealed record Match(IrExpression Collection, WhileLoop Loop, StoreLocal CurrentStore);

    static Match? TryMatch(IrFunction function, UsingStatement usingStatement)
    {
        int enumeratorIndex = usingStatement.LocalIndex;
        if (HasSourceLocalName(function, enumeratorIndex))
            return null;

        if (usingStatement.Resource is not Call getEnumerator
            || !IsGetEnumerator(getEnumerator)
            || getEnumerator.Arguments is not [_])
        {
            return null;
        }

        if (usingStatement.Body.Blocks is not [{ Children: [WhileLoop loop] }]
            || !IsMoveNextOn(loop.Condition, enumeratorIndex)
            || loop.Body.Children is not [StoreLocal { Value: LoadProperty current } currentStore, ..]
            || !IsCurrentOn(current, enumeratorIndex))
        {
            return null;
        }

        if (loop.Body.Children.Skip(1).Any(child => ReferencesEnumerator(child, enumeratorIndex)))
            return null;

        return new Match(getEnumerator.Arguments[0], loop, currentStore);
    }

    static bool HasSourceLocalName(IrFunction function, int index)
        => index >= 0
            && index < function.LocalNames.Length
            && !string.IsNullOrWhiteSpace(function.LocalNames[index]);

    static bool IsGetEnumerator(Call call)
        => call.Callee is { Name: "GetEnumerator", HasThis: true } && call.Arguments.Count == 1;

    static bool IsMoveNextOn(IrExpression condition, int enumeratorIndex)
        => condition is Call
        {
            Callee: { Name: "MoveNext", HasThis: true, ReturnType: { Namespace: "System", Name: "Boolean" } },
            Arguments: [var receiver],
        } && IsEnumeratorReceiver(receiver, enumeratorIndex);

    static bool IsCurrentOn(LoadProperty property, int enumeratorIndex)
        => property is { HasInstance: true, PropertyName: "Current", Instance: { } receiver }
            && IsEnumeratorReceiver(receiver, enumeratorIndex);

    static bool IsEnumeratorReceiver(IrExpression receiver, int enumeratorIndex) => receiver switch
    {
        LoadLocal load => load.Index == enumeratorIndex,
        LoadLocalAddress address => address.Index == enumeratorIndex,
        _ => false,
    };

    static bool ReferencesEnumerator(IrNode node, int enumeratorIndex)
        => node.Descendants.Prepend(node).Any(candidate => candidate switch
        {
            LoadLocal load => load.Index == enumeratorIndex,
            LoadLocalAddress address => address.Index == enumeratorIndex,
            StoreLocal store => store.Index == enumeratorIndex,
            _ => false,
        });
}
