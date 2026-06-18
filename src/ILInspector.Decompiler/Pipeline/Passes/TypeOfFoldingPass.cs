namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Folds Type.GetTypeFromHandle(ldtoken T) to typeof(T) — the inverse of
/// the compiler's typeof lowering, which always emits exactly this pair.
/// </summary>
public sealed class TypeOfFoldingPass : IIrPass
{
    public string Name => "typeof-folding";

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var call in function.Descendants.OfType<Call>().ToList())
        {
            if (call.Parent is null)
                continue;
            if (call.Callee is { Name: "GetTypeFromHandle", HasThis: false, ParameterTypes.Length: 1 }
                && call.Callee.DeclaringType is { Namespace: "System", Name: "Type" }
                && call.Children.Count == 1
                && call.Children[0] is LoadToken { Kind: RuntimeTokenKind.Type, Type: { } type })
            {
                call.ReplaceWith(new TypeOf(type));
            }
        }
    }
}
