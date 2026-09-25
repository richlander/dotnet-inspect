namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises csc's effectful-receiver lowering for a void null-conditional call:
/// <c>receiver()?.M()</c>. The importer materializes the duplicated receiver in
/// two stack slots, branches to the call on non-null, and leaves a no-op return
/// or branch on the null path.
/// </summary>
public sealed class VoidNullConditionalPass : IIrPass
{
    public string Name => "void-null-conditional";

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var container in function.Descendants.OfType<BlockContainer>().ToList())
        {
            while (RaiseOne(function, container, context.Stepper))
            {
            }
        }
    }

    static bool RaiseOne(IrFunction function, BlockContainer container, Stepper stepper)
    {
        var blocks = container.Blocks;
        if (blocks.Count < 4)
            return false;

        var targetCounts = CollectTargetCounts(container);
        for (int headIndex = 0; headIndex + 3 < blocks.Count; headIndex++)
        {
            var head = blocks[headIndex];
            if (head.Children.Count < 3
                || head.Children[^3] is not StoreStackSlot receiverValue
                || head.Children[^2] is not StoreStackSlot callReceiver
                || callReceiver.Value is not LoadStackSlot copiedReceiver
                || copiedReceiver.Slot != receiverValue.Slot
                || head.Children[^1] is not ConditionalBranch guard
                || guard.Condition is not LoadStackSlot testedReceiver
                || testedReceiver.Slot != receiverValue.Slot)
            {
                continue;
            }

            var nullArm = blocks[headIndex + 1];
            var callArmIndex = container.IndexOfOffset(guard.TargetOffset);
            if (callArmIndex != headIndex + 2
                || targetCounts.GetValueOrDefault(nullArm.StartOffset) != 0
                || targetCounts.GetValueOrDefault(blocks[callArmIndex].StartOffset) != 1)
            {
                continue;
            }

            var callArm = blocks[callArmIndex];
            if (callArm.Children is not [ExpressionStatement { Expression: Call call }]
                || !call.Callee.HasThis
                || !IsVoid(call.Callee.ReturnType)
                || call.Arguments.FirstOrDefault() is not LoadStackSlot memberReceiver
                || memberReceiver.Slot != callReceiver.Slot
                || !CanUseNullConditional(memberReceiver.Type, function.TypeShapes))
            {
                continue;
            }

            int continuationIndex = callArmIndex + 1;
            if (continuationIndex >= blocks.Count
                || !FallsThrough(callArm)
                || !NullArmReachesSameContinuation(
                    nullArm,
                    blocks[continuationIndex]))
            {
                continue;
            }

            if (receiverValue.Slot == callReceiver.Slot
                || function.Descendants.OfType<StoreStackSlot>()
                    .Count(store => store.Slot == receiverValue.Slot) != 1
                || function.Descendants.OfType<StoreStackSlot>()
                    .Count(store => store.Slot == callReceiver.Slot) != 1
                || function.Descendants.OfType<LoadStackSlot>()
                    .Count(load => load.Slot == receiverValue.Slot) != 2
                || function.Descendants.OfType<LoadStackSlot>()
                    .Count(load => load.Slot == callReceiver.Slot) != 1)
            {
                continue;
            }

            var receiver = (IrExpression)receiverValue.DetachChildren()[0];
            call.Detach();
            call.SetChild(0, receiver);
            var raised = new ExpressionStatement(new NullConditional(call));
            raised.InheritSourceOffset(callArm.Children[0]);

            receiverValue.Detach();
            callReceiver.Detach();
            guard.Detach();
            head.Add(raised);
            nullArm.Detach();
            callArm.Detach();

            stepper.StepOver(
                $"raise void null-conditional call at IL_{head.StartOffset:X4}",
                container);
            return true;
        }

        return false;
    }

    static bool NullArmReachesSameContinuation(Block nullArm, Block continuation)
    {
        if (nullArm.Children is [Branch branch])
            return branch.TargetOffset == continuation.StartOffset;

        return nullArm.Children is [Return { Value: null }]
            && continuation.Children is [Return { Value: null }];
    }

    static Dictionary<int, int> CollectTargetCounts(BlockContainer container)
    {
        var counts = new Dictionary<int, int>();
        foreach (var node in container.Descendants)
        {
            switch (node)
            {
                case Branch branch:
                    Add(branch.TargetOffset);
                    break;
                case ConditionalBranch conditional:
                    Add(conditional.TargetOffset);
                    break;
                case Leave leave:
                    Add(leave.TargetOffset);
                    break;
                case SwitchBranch switchBranch:
                    foreach (int target in switchBranch.TargetOffsets)
                        Add(target);
                    break;
            }
        }
        return counts;

        void Add(int target) => counts[target] = counts.GetValueOrDefault(target) + 1;
    }

    static bool FallsThrough(Block block)
        => block.Children.Count == 0
            || block.Children[^1] is not (Branch or ConditionalBranch or SwitchBranch
                or Leave or Return or Throw or EndFinally or EndFilter);

    static bool CanUseNullConditional(
        TypeRef? receiverType,
        IReadOnlyDictionary<TypeRef, TypeShape> shapes)
        => receiverType is not
            { Kind: TypeRefKind.Pointer or TypeRefKind.FunctionPointer or TypeRefKind.ByRef }
            && !TypeFamilies.IsKnownNonNullableValueType(receiverType, shapes);

    static bool IsVoid(TypeRef type)
        => type is { Namespace: "System", Name: "Void" };
}
