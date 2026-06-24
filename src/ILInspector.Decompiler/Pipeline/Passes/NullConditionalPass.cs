namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises the C# null-conditional lowering <c>target?.Member</c> back to a
/// single <see cref="NullConditional"/> access. The compiler lowers <c>a?.M</c>
/// to "test the receiver for null; on the non-null path perform the member
/// access; otherwise yield null", and <see cref="BooleanFoldingPass"/> folds
/// that diamond into a ternary. Two lowered shapes survive, by whether the
/// receiver is cheap to re-evaluate:
/// <list type="number">
/// <item>
/// <b>Spilled receiver</b> (a field load, an <c>as</c> result — anything the
/// compiler will not re-evaluate). The receiver is spilled into a stack slot
/// and reloaded for the access. Older imports can reuse one slot for both the
/// receiver and result; newer position/type slots can keep them split. The shape
/// is two adjacent stores:
/// <code>
///   StoreStackSlot(r, LoadStackSlot(s))                      // spill the receiver
///   StoreStackSlot(j, Conditional(LoadStackSlot(s),          // s is not null ?
///                                 member(receiver: LoadStackSlot(r)),  //   recv.Member :
///                                 Constant null))                       //   null
/// </code>
/// Raising drops the spill, moves the real receiver into the access, and leaves
/// slot j carrying only the member result type — the soundness fix.
/// </item>
/// <item>
/// <b>Re-evaluable receiver</b> (an argument or local). The compiler re-emits
/// the load instead of spilling, so the receiver appears in both the null test
/// and the access:
/// <code>
///   Conditional(LoadArgument n, member(receiver: LoadArgument n), Constant null)
/// </code>
/// No slot is conflated here — the win is idiom only: the ternary becomes
/// <c>n?.Member</c>, dropping the duplicate load.
/// </item>
/// </list>
/// Both arms require a literal-null false arm, which proves the member result is
/// a reference type (a value-type <c>?.</c> lowers through <c>Nullable&lt;T&gt;</c>,
/// never a bare <c>ldnull</c>). Runs to fixpoint so chained accesses raise.
/// </summary>
public sealed class NullConditionalPass : IIrPass
{
    public string Name => "null-conditional";

    public void Run(IrFunction function, PassContext context)
    {
        while (RaiseSpilled(function, context.Stepper) || RaiseReevaluable(function, context.Stepper))
        {
        }
    }

    /// <summary>Arm 1: the spilled-receiver shape (adjacent receiver/result stores).</summary>
    static bool RaiseSpilled(IrFunction function, Stepper stepper)
    {
        foreach (var block in function.Descendants.OfType<Block>())
        {
            var children = block.Children;
            for (int i = 0; i + 1 < children.Count; i++)
            {
                if (children[i] is not StoreStackSlot spill || children[i + 1] is not StoreStackSlot result)
                    continue;
                // The spill holds the receiver, loaded from slot s.
                if (spill.Value is not LoadStackSlot receiverLoad)
                    continue;
                if (result.Value is not Conditional conditional || !IsNullConditionalShape(conditional, out var member))
                    continue;
                // The null test reads the SAME source slot s, and the member's
                // receiver is the spill slot r — the two stores tie them together.
                if (conditional.Condition is not LoadStackSlot conditionLoad || conditionLoad.Slot != receiverLoad.Slot)
                    continue;
                if (MemberReceiver(member) is not LoadStackSlot memberReceiver || memberReceiver.Slot != spill.Slot)
                    continue;
                if (TypeFamilies.IsKnownNonNullableValueType(memberReceiver.Type, function.TypeShapes))
                    continue;

                member.Detach();
                var receiver = (IrExpression)spill.DetachChildren()[0];
                member.SetChild(0, receiver);
                stepper.StepOver("raise spilled null-check diamond to ?.", result);
                result.SetChild(0, new NullConditional(member));
                spill.Detach();
                return true;
            }
        }
        return false;
    }

    /// <summary>Arm 2: the re-evaluable-receiver shape (argument/local loaded in both test and access).</summary>
    static bool RaiseReevaluable(IrFunction function, Stepper stepper)
    {
        foreach (var conditional in function.Descendants.OfType<Conditional>())
        {
            if (!IsNullConditionalShape(conditional, out var member))
                continue;
            if (MemberReceiver(member) is not { } receiver || !PlaceIdentity.SameVariable(conditional.Condition, receiver))
                continue;
            if (TypeFamilies.IsKnownNonNullableValueType(receiver.ResultType, function.TypeShapes))
                continue;

            member.Detach();
            stepper.StepOver("raise re-evaluable null-check diamond to ?.", conditional);
            conditional.ReplaceWith(new NullConditional(member));
            return true;
        }
        return false;
    }

    /// <summary>
    /// A <c>recv is not null ? member : null</c> ternary whose true arm is an
    /// instance member access with a reference-typed result (guaranteed by the
    /// literal-null false arm; a known value family means hand-rolled IL — leave
    /// it a ternary).
    /// </summary>
    static bool IsNullConditionalShape(Conditional conditional, out IrExpression member)
    {
        member = conditional.WhenTrue;
        if (conditional.WhenFalse is not Constant { Value: null })
            return false;
        if (MemberReceiver(member) is null)
            return false;
        return TypeFamilies.Of(member.ResultType) is not { } family || family == StackFamily.O;
    }

    /// <summary>The receiver child of an instance member access — always its first child.</summary>
    static IrExpression? MemberReceiver(IrExpression member) => member switch
    {
        Call { Callee.HasThis: true } call when call.Children.Count > 0 => (IrExpression)call.Children[0],
        LoadProperty { HasInstance: true } property => property.Instance,
        LoadField { Instance: not null } field => field.Instance,
        _ => null,
    };
}
