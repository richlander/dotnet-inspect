namespace DotnetInspector.Decompiler.Pipeline;

/// <summary>
/// Raises accessor calls to property and indexer nodes — the inverse of the
/// compiler's property lowering. The get_/set_ naming plus matching arity is
/// the same evidence the current emitter uses; the result is typed nodes,
/// not name surgery at print time.
/// </summary>
public sealed class PropertySugarPass : IIrPass
{
    public string Name => "property-sugar";

    public void Run(IrFunction function)
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
                    statement.ReplaceWith(new StoreProperty(call.Callee, instance, indexArguments, value) { IsVirtual = call.IsVirtual });
                    break;
                }
            }
        }
    }

    static bool IsGetter(MethodRef callee)
        => callee.IsSpecialName
            && callee.Name.StartsWith("get_", StringComparison.Ordinal)
            && callee.Name.Length > "get_".Length
            && callee.ReturnType is not { Namespace: "System", Name: "Void" };

    static bool IsSetter(MethodRef callee)
        => callee.IsSpecialName
            && callee.Name.StartsWith("set_", StringComparison.Ordinal)
            && callee.Name.Length > "set_".Length
            && callee.ParameterTypes.Length >= 1
            && callee.ReturnType is { Namespace: "System", Name: "Void" };
}
