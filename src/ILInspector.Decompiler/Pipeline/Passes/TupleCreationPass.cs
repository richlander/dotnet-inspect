namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises direct <c>System.ValueTuple&lt;...&gt;</c> constructor calls into C#
/// tuple literals. This covers the compiler's ordinary tuple-creation lowering:
/// <c>(a, b)</c> becomes <c>new ValueTuple&lt;T1, T2&gt;(a, b)</c>. Scoped to
/// arities 2-7; eight-or-more tuples use the nested <c>TRest</c> representation
/// and stay as constructors for a later slice.
/// </summary>
public sealed class TupleCreationPass : IIrPass
{
    public string Name => "tuple-creation";

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var newObject in function.Descendants.OfType<NewObject>().ToList())
        {
            if (!MemberIdentity.IsValueTupleConstructor(newObject, out _))
                continue;
            if (newObject.Parent is ExpressionStatement)
                continue;

            var elements = newObject.DetachChildren().Cast<IrExpression>().ToList();
            var tuple = new TupleExpression(newObject.Constructor.DeclaringType, elements);
            context.Stepper.StepOver("raise ValueTuple constructor to tuple literal", newObject);
            newObject.ReplaceWith(tuple);
        }
    }

}
