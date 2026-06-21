namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises the compiler's index-from-end lowering for arrays, strings, and spans:
/// <c>receiver[receiver.Length - n]</c> becomes <c>receiver[^n]</c>, where the
/// offset <c>n</c> is any integer expression (a constant such as <c>^1</c> or a
/// variable/computed value such as <c>^n</c> or <c>^(n + 1)</c>). The pass
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
                case LoadProperty property when MemberIdentity.IsStringCharsGetter(property):
                    TryRewrite(property.Instance!, property.IndexArguments[0], context.Stepper);
                    break;
                case LoadProperty property when MemberIdentity.IsSpanIndexerGetter(property):
                    TryRewrite(property.Instance!, property.IndexArguments[0], context.Stepper);
                    break;
            }
        }
    }

    static bool TryRewrite(IrExpression receiver, IrExpression index, Stepper stepper)
    {
        // The offset is any integer expression: a constant (`^1`) or a
        // variable/computed value (`^n`, `^(n + 1)`). A negative constant is the
        // one shape that cannot come from `^` (it would be `receiver.Length + k`),
        // so it is excluded; every other offset is admitted because the
        // receiver-spill discriminator below already proves the `^` lowering.
        if (index is not Binary { Kind: BinaryKind.Subtract } subtract
            || (subtract.Right is Constant { Value: int offset } && offset < 0)
            || LengthReceiver(subtract.Left) is not { } lengthReceiver
            || !PlaceIdentity.SameStackSlot(receiver, lengthReceiver))
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
        LoadProperty property when MemberIdentity.IsStringLengthGetter(property)
            || MemberIdentity.IsSpanLengthGetter(property) => property.Instance,
        _ => null,
    };

    // The compiler's index-from-end lowering spills the receiver into a single
    // stack slot and reads it twice (for .Length and for the index), so at this
    // point in the pipeline a genuine `^n` always presents as two reads of the
    // same stack slot. Hand-written `a[a.Length - n]` re-loads the receiver
    // directly (two ldarg/ldloc, no spill); matching those would rewrite faithful
    // source into `^n` whose recompile produces a different opcode stream. That
    // narrowing — stack-slot only, never a variable — is the discriminator this
    // pass keeps; PlaceIdentity.SameStackSlot supplies the equality.
}
