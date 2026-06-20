namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises the compiler's index-from-end lowering for arrays and strings:
/// <c>receiver[receiver.Length - n]</c> becomes <c>receiver[^n]</c>. The pass
/// rewrites only the index operand; the second expression-inlining pass then
/// collapses the compiler's duplicated receiver stack slot back into the
/// element receiver when it is single-use.
/// </summary>
public sealed class IndexFromEndPass : IIrPass
{
    public string Name => "index-from-end";

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var node in function.Descendants.ToList())
        {
            switch (node)
            {
                case LoadElement element:
                    TryRewrite(element.Array, element.Index, context.Stepper);
                    break;
                case LoadElementAddress address:
                    TryRewrite(address.Array, address.Index, context.Stepper);
                    break;
                case StoreElement store:
                    TryRewrite(store.Array, store.Index, context.Stepper);
                    break;
                case LoadProperty property when property is { HasInstance: true, PropertyName: "Chars", IndexArguments.Count: 1 }:
                    TryRewrite(property.Instance!, property.IndexArguments[0], context.Stepper);
                    break;
            }
        }
    }

    static bool TryRewrite(IrExpression receiver, IrExpression index, Stepper stepper)
    {
        if (index is not Binary { Kind: BinaryKind.Subtract, Right: Constant { Value: int offset } } subtract
            || offset < 0
            || LengthReceiver(subtract.Left) is not { } lengthReceiver
            || !SamePlace(receiver, lengthReceiver))
        {
            return false;
        }

        var offsetNode = (IrExpression)subtract.DetachChildren()[1];
        var raised = new IndexFromEnd(offsetNode);
        stepper.StepOver("raise receiver.Length - n to ^n index", subtract);
        subtract.ReplaceWith(raised);
        return true;
    }

    static IrExpression? LengthReceiver(IrExpression expression) => expression switch
    {
        ArrayLength length => length.Array,
        LoadProperty { HasInstance: true, PropertyName: "Length" } property => property.Instance,
        _ => null,
    };

    static bool SamePlace(IrExpression left, IrExpression right) => (left, right) switch
    {
        (LoadStackSlot a, LoadStackSlot b) => a.Slot == b.Slot,
        (LoadArgument a, LoadArgument b) => a.Index == b.Index,
        (LoadLocal a, LoadLocal b) => a.Index == b.Index,
        _ => false,
    };
}
