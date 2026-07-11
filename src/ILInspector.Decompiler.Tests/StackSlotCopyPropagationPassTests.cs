using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class StackSlotCopyPropagationPassTests
{
    static readonly TypeRef Holder = TypeRef.Definition("Synthetic", "Samples", "Holder", ValueTypeHint.ReferenceType);
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Bool = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef Object = TypeRef.CoreLib("System", "Object");

    [Fact]
    public void PropagatesSingleUseCopyAcrossFallthroughBlock()
    {
        var body = MultiBlockSourceBeforeCopy();
        var copy = new Block(12);
        copy.Add(new StoreStackSlot(1, new LoadStackSlot(0, Int32)));
        body.Add(copy);
        var use = new Block(16);
        use.Add(UseSlot(1));
        use.Add(new Return(null));
        body.Add(use);
        var function = Function(body);

        new StackSlotCopyPropagationPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.DoesNotContain(function.Descendants.OfType<StoreStackSlot>(), store => store.Slot == 1);
        Assert.DoesNotContain(function.Descendants.OfType<LoadStackSlot>(), load => load.Slot == 1);
        Assert.Single(function.Descendants.OfType<LoadStackSlot>(), load => load.Slot == 0);
    }

    [Fact]
    public void PropagatesOnlyWhenEveryBranchPathKeepsSourceStable()
    {
        var body = MultiBlockSourceBeforeCopy();
        var copy = new Block(12);
        copy.Add(new StoreStackSlot(1, new LoadStackSlot(0, Int32)));
        copy.Add(new ConditionalBranch(new LoadArgument(1, "flag", Bool), 20));
        body.Add(copy);
        var fallthrough = new Block(16);
        fallthrough.Add(new Branch(24));
        body.Add(fallthrough);
        var target = new Block(20);
        target.Add(new Branch(24));
        body.Add(target);
        var join = new Block(24);
        join.Add(UseSlot(1));
        join.Add(new Return(null));
        body.Add(join);
        var function = Function(body);

        new StackSlotCopyPropagationPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.DoesNotContain(function.Descendants.OfType<StoreStackSlot>(), store => store.Slot == 1);
        Assert.DoesNotContain(function.Descendants.OfType<LoadStackSlot>(), load => load.Slot == 1);
        Assert.Single(function.Descendants.OfType<LoadStackSlot>(), load => load.Slot == 0);
    }

    [Fact]
    public void DoesNotPropagateWhenSourceIsRewrittenBeforeUse()
    {
        var function = RewriteSourceBeforeUse();

        new StackSlotCopyPropagationPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Contains(function.Descendants.OfType<StoreStackSlot>(), store => store.Slot == 1);
        Assert.Contains(function.Descendants.OfType<LoadStackSlot>(), load => load.Slot == 1);
    }

    [Fact]
    public void DoesNotPropagateWhenDestinationHasMultipleUses()
    {
        var body = MultiBlockSourceBeforeCopy();
        var copy = new Block(12);
        copy.Add(new StoreStackSlot(1, new LoadStackSlot(0, Int32)));
        body.Add(copy);
        var firstUse = new Block(16);
        firstUse.Add(UseSlot(1));
        body.Add(firstUse);
        var secondUse = new Block(20);
        secondUse.Add(UseSlot(1));
        secondUse.Add(new Return(null));
        body.Add(secondUse);
        var function = Function(body);

        new StackSlotCopyPropagationPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Contains(function.Descendants.OfType<StoreStackSlot>(), store => store.Slot == 1);
        Assert.Equal(2, function.Descendants.OfType<LoadStackSlot>().Count(load => load.Slot == 1));
    }

    [Fact]
    public void DoesNotPropagateWhenDestinationHasMultipleStores()
    {
        var body = MultiBlockSourceBeforeCopy();
        var firstCopy = new Block(12);
        firstCopy.Add(new StoreStackSlot(1, new LoadStackSlot(0, Int32)));
        firstCopy.Add(new Branch(16));
        body.Add(firstCopy);
        var secondCopy = new Block(16);
        secondCopy.Add(new StoreStackSlot(1, new LoadStackSlot(0, Int32)));
        secondCopy.Add(UseSlot(1));
        secondCopy.Add(new Return(null));
        body.Add(secondCopy);
        var function = Function(body);

        new StackSlotCopyPropagationPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Equal(2, function.Descendants.OfType<StoreStackSlot>().Count(store => store.Slot == 1));
        Assert.Contains(function.Descendants.OfType<LoadStackSlot>(), load => load.Slot == 1);
    }

    [Fact]
    public void DoesNotPropagateSelfCopy()
    {
        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new StoreStackSlot(1, new LoadStackSlot(1, Int32)));
        block.Add(UseSlot(1));
        block.Add(new Return(null));
        body.Add(block);
        var function = Function(body);

        new StackSlotCopyPropagationPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Contains(function.Descendants.OfType<StoreStackSlot>(), store => store.Slot == 1);
        Assert.Equal(2, function.Descendants.OfType<LoadStackSlot>().Count(load => load.Slot == 1));
    }

    [Fact]
    public void DoesNotPropagateWhenSourceHasSingleStore()
    {
        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new StoreStackSlot(0, new LoadArgument(0, "x", Int32)));
        block.Add(new StoreStackSlot(1, new LoadStackSlot(0, Int32)));
        block.Add(UseSlot(1));
        block.Add(new Return(null));
        body.Add(block);
        var function = Function(body);

        new StackSlotCopyPropagationPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Contains(function.Descendants.OfType<StoreStackSlot>(), store => store.Slot == 1);
        Assert.Contains(function.Descendants.OfType<LoadStackSlot>(), load => load.Slot == 1);
    }

    [Fact]
    public void DoesNotPropagateWhenSourceStoresAreInOneBlock()
    {
        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new StoreStackSlot(0, new LoadArgument(0, "x", Int32)));
        block.Add(new StoreStackSlot(0, new LoadArgument(2, "y", Int32)));
        block.Add(new StoreStackSlot(1, new LoadStackSlot(0, Int32)));
        block.Add(UseSlot(1));
        block.Add(new Return(null));
        body.Add(block);
        var function = Function(body);

        new StackSlotCopyPropagationPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Contains(function.Descendants.OfType<StoreStackSlot>(), store => store.Slot == 1);
        Assert.Contains(function.Descendants.OfType<LoadStackSlot>(), load => load.Slot == 1);
    }

    [Fact]
    public void DoesNotPropagateIntoIncrementDecrementTarget()
    {
        var body = MultiBlockSourceBeforeCopy();
        var copy = new Block(12);
        copy.Add(new StoreStackSlot(1, new LoadStackSlot(0, Int32)));
        body.Add(copy);
        var use = new Block(16);
        use.Add(new ExpressionStatement(new IncrementDecrement(new LoadStackSlot(1, Int32), isIncrement: true, isPrefix: false)));
        use.Add(new Return(null));
        body.Add(use);
        var function = Function(body);

        new StackSlotCopyPropagationPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Contains(function.Descendants.OfType<StoreStackSlot>(), store => store.Slot == 1);
        Assert.Contains(function.Descendants.OfType<LoadStackSlot>(), load => load.Slot == 1);
    }

    [Fact]
    public void DoesNotPropagateAcrossCyclicCopiedValueRegion()
    {
        var body = MultiBlockSourceBeforeCopy();
        var copy = new Block(12);
        copy.Add(new StoreStackSlot(1, new LoadStackSlot(0, Int32)));
        body.Add(copy);
        var loop = new Block(16);
        loop.Add(UseSlot(1));
        loop.Add(new StoreStackSlot(0, new LoadArgument(2, "y", Int32)));
        loop.Add(new ConditionalBranch(new LoadArgument(1, "flag", Bool), 16));
        body.Add(loop);
        var exit = new Block(20);
        exit.Add(new Return(null));
        body.Add(exit);
        var function = Function(body);

        new StackSlotCopyPropagationPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Contains(function.Descendants.OfType<StoreStackSlot>(), store => store.Slot == 1);
        Assert.Contains(function.Descendants.OfType<LoadStackSlot>(), load => load.Slot == 1);
    }

    [Fact]
    public void DoesNotPropagateWhenOutsideEdgeCanEnterUseRegion()
    {
        var body = new BlockContainer();
        var entry = new Block(0);
        entry.Add(new ConditionalBranch(new LoadArgument(1, "flag", Bool), 8));
        body.Add(entry);
        var firstSourceStore = new Block(4);
        firstSourceStore.Add(new StoreStackSlot(0, new LoadArgument(0, "x", Int32)));
        firstSourceStore.Add(new Branch(12));
        body.Add(firstSourceStore);
        var secondSourceStore = new Block(8);
        secondSourceStore.Add(new StoreStackSlot(0, new LoadArgument(2, "y", Int32)));
        secondSourceStore.Add(new ConditionalBranch(new LoadArgument(1, "flag", Bool), 16));
        body.Add(secondSourceStore);
        var copyJoin = new Block(12);
        copyJoin.Add(new StoreStackSlot(1, new LoadStackSlot(0, Int32)));
        copyJoin.Add(new Branch(20));
        body.Add(copyJoin);
        var outside = new Block(16);
        outside.Add(new Branch(20));
        body.Add(outside);
        var join = new Block(20);
        join.Add(UseSlot(1));
        join.Add(new Return(null));
        body.Add(join);
        var function = Function(body);

        new StackSlotCopyPropagationPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Contains(function.Descendants.OfType<StoreStackSlot>(), store => store.Slot == 1);
        Assert.Contains(function.Descendants.OfType<LoadStackSlot>(), load => load.Slot == 1);
    }

    [Fact]
    public void DoesNotPropagateWhenSwitchEdgeCanEnterUseRegion()
    {
        var body = OutsideEntryBody();
        var outside = new Block(16);
        outside.Add(new SwitchBranch(new LoadArgument(0, "x", Int32), [20]));
        body.Add(outside);
        var join = new Block(20);
        join.Add(UseSlot(1));
        join.Add(new Return(null));
        body.Add(join);
        var function = Function(body);

        new StackSlotCopyPropagationPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Contains(function.Descendants.OfType<StoreStackSlot>(), store => store.Slot == 1);
        Assert.Contains(function.Descendants.OfType<LoadStackSlot>(), load => load.Slot == 1);
    }

    [Fact]
    public void DoesNotPropagateWhenSwitchTargetRewritesSourceBeforeUse()
    {
        var body = MultiBlockSourceBeforeCopy();
        var copy = new Block(12);
        copy.Add(new StoreStackSlot(1, new LoadStackSlot(0, Int32)));
        copy.Add(new SwitchBranch(new LoadArgument(0, "x", Int32), [20]));
        body.Add(copy);
        var fallthrough = new Block(16);
        fallthrough.Add(new Branch(24));
        body.Add(fallthrough);
        var switchTarget = new Block(20);
        switchTarget.Add(new StoreStackSlot(0, new LoadArgument(2, "y", Int32)));
        switchTarget.Add(new Branch(24));
        body.Add(switchTarget);
        var join = new Block(24);
        join.Add(UseSlot(1));
        join.Add(new Return(null));
        body.Add(join);
        var function = Function(body);

        new StackSlotCopyPropagationPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Contains(function.Descendants.OfType<StoreStackSlot>(), store => store.Slot == 1);
        Assert.Contains(function.Descendants.OfType<LoadStackSlot>(), load => load.Slot == 1);
    }

    [Fact]
    public void DoesNotPropagateWhenLeaveEdgeCanEnterUseRegion()
    {
        var body = OutsideEntryBody();
        var outside = new Block(16);
        outside.Add(new Leave(20));
        body.Add(outside);
        var join = new Block(20);
        join.Add(UseSlot(1));
        join.Add(new Return(null));
        body.Add(join);
        var function = Function(body);

        new StackSlotCopyPropagationPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Contains(function.Descendants.OfType<StoreStackSlot>(), store => store.Slot == 1);
        Assert.Contains(function.Descendants.OfType<LoadStackSlot>(), load => load.Slot == 1);
    }

    [Fact]
    public void DoesNotPropagateWhenLeaveTargetRewritesSourceBeforeUse()
    {
        var body = MultiBlockSourceBeforeCopy();
        var copy = new Block(12);
        copy.Add(new StoreStackSlot(1, new LoadStackSlot(0, Int32)));
        copy.Add(new ConditionalBranch(new LoadArgument(1, "flag", Bool), 20));
        body.Add(copy);
        var fallthrough = new Block(16);
        fallthrough.Add(new Leave(24));
        body.Add(fallthrough);
        var branchTarget = new Block(20);
        branchTarget.Add(new Branch(28));
        body.Add(branchTarget);
        var leaveTarget = new Block(24);
        leaveTarget.Add(new StoreStackSlot(0, new LoadArgument(2, "y", Int32)));
        leaveTarget.Add(new Branch(28));
        body.Add(leaveTarget);
        var join = new Block(28);
        join.Add(UseSlot(1));
        join.Add(new Return(null));
        body.Add(join);
        var function = Function(body);

        new StackSlotCopyPropagationPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Contains(function.Descendants.OfType<StoreStackSlot>(), store => store.Slot == 1);
        Assert.Contains(function.Descendants.OfType<LoadStackSlot>(), load => load.Slot == 1);
    }

    [Fact]
    public void DoesNotPropagateWhenContainerHasUnmodeledLoopTerminator()
    {
        var body = MultiBlockSourceBeforeCopy();
        var copy = new Block(12);
        copy.Add(new StoreStackSlot(1, new LoadStackSlot(0, Int32)));
        body.Add(copy);
        var use = new Block(16);
        use.Add(UseSlot(1));
        use.Add(new Return(null));
        body.Add(use);
        var loopExit = new Block(20);
        loopExit.Add(new Break());
        body.Add(loopExit);
        var function = Function(body);

        new StackSlotCopyPropagationPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Contains(function.Descendants.OfType<StoreStackSlot>(), store => store.Slot == 1);
        Assert.Contains(function.Descendants.OfType<LoadStackSlot>(), load => load.Slot == 1);
    }

    [Fact]
    public void DoesNotPropagateWhenNestedBranchCanEnterUseRegion()
    {
        var body = new BlockContainer();
        var entry = new Block(0);
        entry.Add(new ConditionalBranch(new LoadArgument(1, "flag", Bool), 8));
        body.Add(entry);
        var firstSourceStore = new Block(4);
        firstSourceStore.Add(new StoreStackSlot(0, new LoadArgument(0, "x", Int32)));
        firstSourceStore.Add(new Branch(12));
        body.Add(firstSourceStore);
        var secondSourceStore = new Block(8);
        secondSourceStore.Add(new StoreStackSlot(0, new LoadArgument(2, "y", Int32)));
        secondSourceStore.Add(new ConditionalBranch(new LoadArgument(1, "flag", Bool), 16));
        body.Add(secondSourceStore);
        var copyJoin = new Block(12);
        copyJoin.Add(new StoreStackSlot(1, new LoadStackSlot(0, Int32)));
        copyJoin.Add(new Branch(24));
        body.Add(copyJoin);
        var nestedBranch = new Block(16);
        var thenArm = new Block(17);
        thenArm.Add(new Branch(24));
        nestedBranch.Add(new IfStatement(new LoadArgument(1, "flag", Bool), thenArm, null));
        nestedBranch.Add(new Return(null));
        body.Add(nestedBranch);
        var use = new Block(24);
        use.Add(UseSlot(1));
        use.Add(new Return(null));
        body.Add(use);
        var function = Function(body);

        new StackSlotCopyPropagationPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Contains(function.Descendants.OfType<StoreStackSlot>(), store => store.Slot == 1);
        Assert.Contains(function.Descendants.OfType<LoadStackSlot>(), load => load.Slot == 1);
    }

    [Fact]
    public void DoesNotPropagateWhenNestedLoopTerminatorIsPresent()
    {
        var body = MultiBlockSourceBeforeCopy();
        var copy = new Block(12);
        copy.Add(new StoreStackSlot(1, new LoadStackSlot(0, Int32)));
        body.Add(copy);
        var use = new Block(16);
        use.Add(UseSlot(1));
        var thenArm = new Block(17);
        thenArm.Add(new Continue());
        use.Add(new IfStatement(new LoadArgument(1, "flag", Bool), thenArm, null));
        use.Add(new Return(null));
        body.Add(use);
        var function = Function(body);

        new StackSlotCopyPropagationPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Contains(function.Descendants.OfType<StoreStackSlot>(), store => store.Slot == 1);
        Assert.Contains(function.Descendants.OfType<LoadStackSlot>(), load => load.Slot == 1);
    }

    [Fact]
    public void DoesNotPropagateInsideNestedStructuredContainer()
    {
        var lockBody = MultiBlockSourceBeforeCopy();
        var copy = new Block(12);
        copy.Add(new StoreStackSlot(1, new LoadStackSlot(0, Int32)));
        lockBody.Add(copy);
        var use = new Block(16);
        use.Add(UseSlot(1));
        use.Add(new Return(null));
        lockBody.Add(use);

        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new Pipeline.Lock(new LoadArgument(3, "gate", Object), lockBody));
        block.Add(new Return(null));
        body.Add(block);
        var function = Function(body);

        new StackSlotCopyPropagationPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Contains(function.Descendants.OfType<StoreStackSlot>(), store => store.Slot == 1);
        Assert.Contains(function.Descendants.OfType<LoadStackSlot>(), load => load.Slot == 1);
    }

    [Fact]
    public void DoesNotCrossNestedBodyScope()
    {
        var lambdaBody = new BlockContainer();
        var lambdaBlock = new Block(0);
        lambdaBlock.Add(UseSlot(1));
        lambdaBlock.Add(new Return(null));
        lambdaBody.Add(lambdaBlock);
        var lambda = new Lambda(
            TypeRef.CoreLib("System", "Action"),
            [], [], [],
            usesUpdatedMemorySafetyRules: false,
            skipLocalsInit: false,
            lambdaBody);

        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new StoreStackSlot(0, new LoadArgument(0, "x", Int32)));
        block.Add(new StoreStackSlot(1, new LoadStackSlot(0, Int32)));
        block.Add(new StoreLocal(0, lambda.ResultType!, lambda));
        block.Add(new Return(null));
        body.Add(block);
        var function = Function(body);

        new StackSlotCopyPropagationPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Contains(function.Descendants.OfType<StoreStackSlot>(), store => store.Slot == 1);
        Assert.Contains(function.Descendants.OfType<LoadStackSlot>(), load => load.Slot == 1);
    }

    [Fact]
    public void DoesNotPropagateIntoLockReceiverCarrier()
    {
        var body = new BlockContainer();
        var entry = new Block(0);
        entry.Add(new ConditionalBranch(new LoadArgument(1, "flag", Bool), 8));
        body.Add(entry);
        var firstSourceStore = new Block(4);
        firstSourceStore.Add(new StoreStackSlot(0, new LoadArgument(3, "gate", Object)));
        firstSourceStore.Add(new Branch(12));
        body.Add(firstSourceStore);
        var secondSourceStore = new Block(8);
        secondSourceStore.Add(new StoreStackSlot(0, new LoadArgument(3, "gate", Object)));
        secondSourceStore.Add(new Branch(12));
        body.Add(secondSourceStore);
        var block = new Block(12);
        block.Add(new StoreStackSlot(1, new LoadStackSlot(0, Object)));
        block.Add(new StoreLocal(0, Object, new LoadStackSlot(1, Object)));
        var lockBody = new BlockContainer();
        var lockBlock = new Block(16);
        lockBlock.Add(new Return(null));
        lockBody.Add(lockBlock);
        block.Add(new Pipeline.Lock(new LoadLocal(0, Object), lockBody));
        body.Add(block);
        var function = Function(body);

        new StackSlotCopyPropagationPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Contains(function.Descendants.OfType<StoreStackSlot>(), store => store.Slot == 1);
        Assert.Contains(function.Descendants.OfType<LoadStackSlot>(), load => load.Slot == 1);
    }

    [Fact]
    public void DoesNotPropagateNonSlotCopyValue()
    {
        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new StoreStackSlot(1, new LoadArgument(0, "x", Int32)));
        block.Add(UseSlot(1));
        block.Add(new Return(null));
        body.Add(block);
        var function = Function(body);

        new StackSlotCopyPropagationPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Contains(function.Descendants.OfType<StoreStackSlot>(), store => store.Slot == 1);
        Assert.Contains(function.Descendants.OfType<LoadStackSlot>(), load => load.Slot == 1);
    }

    static IrFunction RewriteSourceBeforeUse()
    {
        var body = MultiBlockSourceBeforeCopy();
        var copy = new Block(12);
        copy.Add(new StoreStackSlot(1, new LoadStackSlot(0, Int32)));
        body.Add(copy);
        var rewrite = new Block(16);
        rewrite.Add(new StoreStackSlot(0, new LoadArgument(2, "y", Int32)));
        body.Add(rewrite);
        var use = new Block(20);
        use.Add(UseSlot(1));
        use.Add(new Return(null));
        body.Add(use);
        return Function(body);
    }

    static BlockContainer MultiBlockSourceBeforeCopy()
    {
        var body = new BlockContainer();
        var entry = new Block(0);
        entry.Add(new ConditionalBranch(new LoadArgument(1, "flag", Bool), 8));
        body.Add(entry);
        var firstSourceStore = new Block(4);
        firstSourceStore.Add(new StoreStackSlot(0, new LoadArgument(0, "x", Int32)));
        firstSourceStore.Add(new Branch(12));
        body.Add(firstSourceStore);
        var secondSourceStore = new Block(8);
        secondSourceStore.Add(new StoreStackSlot(0, new LoadArgument(2, "y", Int32)));
        secondSourceStore.Add(new Branch(12));
        body.Add(secondSourceStore);
        return body;
    }

    static BlockContainer OutsideEntryBody()
    {
        var body = new BlockContainer();
        var entry = new Block(0);
        entry.Add(new ConditionalBranch(new LoadArgument(1, "flag", Bool), 8));
        body.Add(entry);
        var firstSourceStore = new Block(4);
        firstSourceStore.Add(new StoreStackSlot(0, new LoadArgument(0, "x", Int32)));
        firstSourceStore.Add(new Branch(12));
        body.Add(firstSourceStore);
        var secondSourceStore = new Block(8);
        secondSourceStore.Add(new StoreStackSlot(0, new LoadArgument(2, "y", Int32)));
        secondSourceStore.Add(new ConditionalBranch(new LoadArgument(1, "flag", Bool), 16));
        body.Add(secondSourceStore);
        var copyJoin = new Block(12);
        copyJoin.Add(new StoreStackSlot(1, new LoadStackSlot(0, Int32)));
        copyJoin.Add(new Branch(20));
        body.Add(copyJoin);
        return body;
    }

    static ExpressionStatement UseSlot(int slot)
        => new(new Call(new MethodRef(Holder, "Use", Void, [Int32], HasThis: false), isVirtual: false, [new LoadStackSlot(slot, Int32)]));

    static IrFunction Function(BlockContainer body)
        => new(
            "M",
            Holder,
            new MethodSignature(Void, [
                new Parameter("x", Int32),
                new Parameter("flag", Bool),
                new Parameter("y", Int32),
                new Parameter("gate", Object),
            ], HasThis: false, GenericParameterCount: 0),
            [TypeRef.CoreLib("System", "Action")],
            body);
}
