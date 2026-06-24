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
            if (MemberIdentity.IsTypeGetTypeFromHandle(call)
                && call.Children.Count == 1
                && call.Children[0] is LoadToken { Kind: RuntimeTokenKind.Type, Type: { } type })
            {
                context.Stepper.StepOver($"fold GetTypeFromHandle to typeof({type.Name})", call);
                call.ReplaceWith(new TypeOf(type));
            }
        }
    }
}
