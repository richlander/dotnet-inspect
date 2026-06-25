namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises accessor calls to property and indexer nodes — the inverse of the
/// compiler's property lowering. The get_/set_ naming plus matching arity is
/// the same evidence the current emitter uses; the result is typed nodes,
/// not name surgery at print time. Event add_/remove_ accessor calls are
/// raised the same way to <c>e += h</c> / <c>e -= h</c> subscriptions.
/// </summary>
public sealed class PropertySugarPass : IIrPass
{
    public string Name => "property-sugar";

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var node in function.Descendants.ToList())
        {
            if (node.Parent is null)
                continue;  // detached by an earlier rewrite in this walk
            switch (node)
            {
                case Call call when IsGetter(call.Callee):
                {
                    var children = call.DetachChildren().Cast<IrExpression>().ToList();
                    var instance = call.Callee.HasThis ? children[0] : null;
                    var indexArguments = children.Skip(call.Callee.HasThis ? 1 : 0).ToList();
                    context.Stepper.StepOver($"raise {call.Callee.Name} call to property load", call);
                    call.ReplaceWith(new LoadProperty(call.Callee, instance, indexArguments) { IsVirtual = call.IsVirtual });
                    break;
                }
                case ExpressionStatement { Expression: Call call } statement when IsSetter(call.Callee):
                {
                    var children = call.DetachChildren().Cast<IrExpression>().ToList();
                    var instance = call.Callee.HasThis ? children[0] : null;
                    int skip = call.Callee.HasThis ? 1 : 0;
                    var value = children[^1];
                    var indexArguments = children.Skip(skip).Take(children.Count - skip - 1).ToList();
                    context.Stepper.StepOver($"raise {call.Callee.Name} call to property store", statement);
                    statement.ReplaceWith(new StoreProperty(call.Callee, instance, indexArguments, value) { IsVirtual = call.IsVirtual });
                    break;
                }
                case ExpressionStatement { Expression: Call call } statement when IsEventAccessor(call.Callee, out bool isAdd):
                {
                    var children = call.DetachChildren().Cast<IrExpression>().ToList();
                    var instance = call.Callee.HasThis ? children[0] : null;
                    var value = children[^1];
                    context.Stepper.StepOver($"raise {call.Callee.Name} call to event {(isAdd ? "+=" : "-=")}", statement);
                    statement.ReplaceWith(new EventSubscription(call.Callee, isAdd, instance, value) { IsVirtual = call.IsVirtual });
                    break;
                }
            }
        }
    }

    static bool IsGetter(MethodRef callee)
        => callee.IsSpecialName
            && callee.Name.StartsWith("get_", StringComparison.Ordinal)
            && callee.Name.Length > "get_".Length
            && callee.ReturnType is not { Namespace: "System", Name: "Void" }
            && callee.TypeArguments.IsEmpty
            && (callee.HasThis || callee.ParameterTypes.Length == 0);

    static bool IsSetter(MethodRef callee)
        => callee.IsSpecialName
            && callee.Name.StartsWith("set_", StringComparison.Ordinal)
            && callee.Name.Length > "set_".Length
            && callee.ParameterTypes.Length >= 1
            && callee.ReturnType is { Namespace: "System", Name: "Void" }
            && callee.TypeArguments.IsEmpty
            && (callee.HasThis || callee.ParameterTypes.Length == 1);

    // An event accessor is a void add_X/remove_X special-name method taking the
    // handler delegate — exactly one explicit parameter (the receiver, if any,
    // is the this-pointer). Calling it directly is CS0571; C# spells it += / -=.
    static bool IsEventAccessor(MethodRef callee, out bool isAdd)
    {
        isAdd = callee.Name.StartsWith("add_", StringComparison.Ordinal);
        bool isRemove = callee.Name.StartsWith("remove_", StringComparison.Ordinal);
        if (!callee.IsSpecialName || (!isAdd && !isRemove))
            return false;
        int prefix = isAdd ? "add_".Length : "remove_".Length;
        return callee.Name.Length > prefix
            && callee.TypeArguments.IsEmpty
            && callee.ParameterTypes.Length == 1
            && callee.ReturnType is { Namespace: "System", Name: "Void" };
    }
}
