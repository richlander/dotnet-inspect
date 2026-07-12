using System.Collections.Immutable;

using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class BooleanFoldingSourceOffsetTests
{
    static readonly TypeRef Boolean = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef Byte = TypeRef.CoreLib("System", "Byte");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Object = TypeRef.CoreLib("System", "Object");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef Holder = TypeRef.CoreLib("Synthetic", "Holder");

    [Fact]
    public void NestedGuardFold_PreservesSourceOffset()
    {
        var innerThen = new Block(0);
        innerThen.Add(new Return(null));
        var inner = new IfStatement(new LoadArgument(1, "b", Boolean), innerThen, null);
        var outerThen = new Block(0);
        outerThen.Add(inner);
        var outer = new IfStatement(new LoadArgument(0, "a", Boolean), outerThen, null);
        outer.SetSourceOffset(0x08);

        var block = new Block(0);
        block.Add(outer);
        var function = Function(
            block,
            Void,
            [new Parameter("a", Boolean), new Parameter("b", Boolean)]);

        new BooleanFoldingPass().Run(function, PassContext.None);

        var folded = Assert.IsType<IfStatement>(Assert.Single(block.Children));
        Assert.IsType<LogicalBinary>(folded.Condition);
        Assert.Equal(0x08, folded.SourceOffset);
    }

    [Fact]
    public void NestedGuardFold_DeclinesWhenInnerGuardIsBranchTarget()
    {
        var innerThen = new Block(0);
        innerThen.Add(new Return(null));
        var inner = new IfStatement(new LoadArgument(1, "b", Boolean), innerThen, null);
        inner.SetSourceOffset(0x09);
        var outerThen = new Block(0);
        outerThen.Add(inner);
        var outer = new IfStatement(new LoadArgument(0, "a", Boolean), outerThen, null);
        outer.SetSourceOffset(0x08);

        var block = new Block(0);
        block.Add(outer);
        var function = Function(
            block,
            Void,
            [new Parameter("a", Boolean), new Parameter("b", Boolean)],
            liveTarget: 0x09);

        new BooleanFoldingPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Same(outer, Assert.Single(block.Children));
        Assert.Equal(0x09, inner.SourceOffset);
    }

    [Theory]
    [InlineData(0x11)]
    [InlineData(0x12)]
    public void GuardReturnFold_DeclinesWhenConsumedReturnIsBranchTarget(int liveTarget)
    {
        var thenReturn = new Return(new Constant(true, Boolean));
        thenReturn.SetSourceOffset(0x11);
        var then = new Block(0);
        then.Add(thenReturn);
        var guard = new IfStatement(new LoadArgument(0, "c", Boolean), then, null);
        guard.SetSourceOffset(0x10);
        var tailReturn = new Return(new Constant(false, Boolean));
        tailReturn.SetSourceOffset(0x12);

        var block = new Block(0);
        block.Add(guard);
        block.Add(tailReturn);
        var function = Function(
            block,
            Boolean,
            [new Parameter("c", Boolean)],
            liveTarget);

        new BooleanFoldingPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Equal(2, block.Children.Count);
        Assert.Same(guard, block.Children[0]);
        Assert.Contains(block.Descendants, node => node.SourceOffset == liveTarget);
    }

    [Fact]
    public void GuardReturnFold_IgnoresCollidingTargetInNestedLocalFunction()
    {
        var then = new Block(0);
        then.Add(new Return(new Constant(true, Boolean)));
        var guard = new IfStatement(new LoadArgument(0, "c", Boolean), then, null);
        guard.SetSourceOffset(0x10);
        var tailReturn = new Return(new Constant(false, Boolean));
        tailReturn.SetSourceOffset(0x12);

        var localBlock = new Block(0);
        localBlock.Add(new Branch(0x12));
        var localBody = new BlockContainer();
        localBody.Add(localBlock);
        var localFunction = new LocalFunctionStatement(
            "Inner",
            Void,
            [],
            isStatic: false,
            [],
            [],
            usesUpdatedMemorySafetyRules: false,
            skipLocalsInit: false,
            localBody);

        var block = new Block(0);
        block.Add(guard);
        block.Add(tailReturn);
        block.Add(localFunction);
        var function = Function(block, Boolean, [new Parameter("c", Boolean)]);

        new BooleanFoldingPass().Run(function, PassContext.None);
        function.CheckInvariant();

        var folded = Assert.IsType<Return>(block.Children[0]);
        Assert.Equal(0x10, folded.SourceOffset);
        Assert.Same(localFunction, block.Children[1]);
    }

    [Fact]
    public void GuardReturnFold_PreservesSourceOffset()
    {
        var then = new Block(0);
        then.Add(new Return(new Constant(true, Boolean)));
        var guard = new IfStatement(new LoadArgument(0, "c", Boolean), then, null);
        guard.SetSourceOffset(0x10);

        var block = new Block(0);
        block.Add(guard);
        block.Add(new Return(new Constant(false, Boolean)));
        var function = Function(block, Boolean, [new Parameter("c", Boolean)]);

        new BooleanFoldingPass().Run(function, PassContext.None);

        var folded = Assert.IsType<Return>(Assert.Single(block.Children));
        Assert.Equal(0x10, folded.SourceOffset);
    }

    [Fact]
    public void TernaryReturnFold_PreservesSourceOffset()
    {
        var then = new Block(0);
        then.Add(new Return(new Constant(1, Int32)));
        var guard = new IfStatement(
            new Comparison(
                ComparisonKind.GreaterThan,
                isUnsigned: false,
                new LoadArgument(0, "x", Int32),
                new Constant(0, Int32)),
            then,
            null);
        guard.SetSourceOffset(0x20);

        var block = new Block(0);
        block.Add(guard);
        block.Add(new Return(new Constant(2, Int32)));
        var function = Function(block, Int32, [new Parameter("x", Int32)]);

        new BooleanFoldingPass().Run(function, PassContext.None);

        var folded = Assert.IsType<Return>(Assert.Single(block.Children));
        Assert.Equal(0x20, folded.SourceOffset);
    }

    [Fact]
    public void TernaryReturnFold_DeclinesWhenTailReturnIsBranchTarget()
    {
        var then = new Block(0);
        then.Add(new Return(new Constant(1, Int32)));
        var guard = new IfStatement(
            new Comparison(
                ComparisonKind.GreaterThan,
                isUnsigned: false,
                new LoadArgument(0, "x", Int32),
                new Constant(0, Int32)),
            then,
            null);
        guard.SetSourceOffset(0x20);
        var tailReturn = new Return(new Constant(2, Int32));
        tailReturn.SetSourceOffset(0x21);

        var block = new Block(0);
        block.Add(guard);
        block.Add(tailReturn);
        var function = Function(
            block,
            Int32,
            [new Parameter("x", Int32)],
            liveTarget: 0x21);

        new BooleanFoldingPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Equal(2, block.Children.Count);
        Assert.Same(guard, block.Children[0]);
        Assert.Equal(0x21, tailReturn.SourceOffset);
    }

    [Fact]
    public void BooleanElementStoreRetype_PreservesSourceOffset()
    {
        var store = new StoreElement(
            Byte,
            new LoadArgument(0, "values", TypeRef.SzArray(Boolean)),
            new Constant(0, Int32),
            new Constant(1, Int32));
        store.SetSourceOffset(0x30);

        var block = new Block(0);
        block.Add(store);
        var function = Function(
            block,
            Void,
            [new Parameter("values", TypeRef.SzArray(Boolean))]);

        new BooleanFoldingPass().Run(function, PassContext.None);

        var folded = Assert.IsType<StoreElement>(Assert.Single(block.Children));
        Assert.Equal(0x30, folded.SourceOffset);
    }

    [Fact]
    public void CoalesceStoreFold_PreservesSourceOffset()
    {
        var initial = new StoreLocal(0, Object, new LoadArgument(0, "value", Object));
        initial.SetSourceOffset(0x40);
        var then = new Block(0);
        then.Add(new StoreLocal(0, Object, new LoadArgument(1, "fallback", Object)));
        var guard = new IfStatement(
            new Comparison(
                ComparisonKind.Equal,
                isUnsigned: false,
                new LoadArgument(0, "value", Object),
                new Constant(null, Object)),
            then,
            null);

        var block = new Block(0);
        block.Add(initial);
        block.Add(guard);
        var function = Function(
            block,
            Void,
            [new Parameter("value", Object), new Parameter("fallback", Object)]);

        new BooleanFoldingPass().Run(function, PassContext.None);

        var folded = Assert.IsType<StoreLocal>(Assert.Single(block.Children));
        Assert.Equal(0x40, folded.SourceOffset);
    }

    [Fact]
    public void CoalesceStackSlotFold_PreservesSourceOffset()
    {
        var initial = new StoreStackSlot(0, new LoadArgument(0, "value", Object));
        initial.SetSourceOffset(0x48);
        var then = new Block(0);
        then.Add(new StoreStackSlot(0, new LoadArgument(1, "fallback", Object)));
        var guard = new IfStatement(
            new Comparison(
                ComparisonKind.Equal,
                isUnsigned: false,
                new LoadArgument(0, "value", Object),
                new Constant(null, Object)),
            then,
            null);

        var block = new Block(0);
        block.Add(initial);
        block.Add(guard);
        var function = Function(
            block,
            Void,
            [new Parameter("value", Object), new Parameter("fallback", Object)]);

        new BooleanFoldingPass().Run(function, PassContext.None);
        function.CheckInvariant();

        var folded = Assert.IsType<StoreStackSlot>(Assert.Single(block.Children));
        Assert.IsType<Coalesce>(folded.Value);
        Assert.Equal(0x48, folded.SourceOffset);
    }

    [Fact]
    public void CoalesceFold_DeclinesWhenConsumedFallbackStoreIsBranchTarget()
    {
        var initial = new StoreLocal(0, Object, new LoadArgument(0, "value", Object));
        initial.SetSourceOffset(0x40);
        var fallbackStore = new StoreLocal(0, Object, new LoadArgument(1, "fallback", Object));
        fallbackStore.SetSourceOffset(0x41);
        var then = new Block(0);
        then.Add(fallbackStore);
        var guard = new IfStatement(
            new Comparison(
                ComparisonKind.Equal,
                isUnsigned: false,
                new LoadArgument(0, "value", Object),
                new Constant(null, Object)),
            then,
            null);

        var block = new Block(0);
        block.Add(initial);
        block.Add(guard);
        var function = Function(
            block,
            Void,
            [new Parameter("value", Object), new Parameter("fallback", Object)],
            liveTarget: 0x41);

        new BooleanFoldingPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Equal(2, block.Children.Count);
        Assert.Same(guard, block.Children[1]);
        Assert.Equal(0x41, fallbackStore.SourceOffset);
    }

    [Fact]
    public void SlotDiamondFold_PreservesSourceOffset()
    {
        var then = new Block(0);
        then.Add(new StoreStackSlot(0, new Constant(1, Int32)));
        var @else = new Block(0);
        @else.Add(new StoreStackSlot(0, new Constant(2, Int32)));
        var diamond = new IfStatement(new LoadArgument(0, "c", Boolean), then, @else);
        diamond.SetSourceOffset(0x50);

        var block = new Block(0);
        block.Add(diamond);
        block.Add(new Return(new LoadStackSlot(0, Int32)));
        var function = Function(block, Int32, [new Parameter("c", Boolean)]);

        new BooleanFoldingPass().Run(function, PassContext.None);

        var folded = Assert.IsType<StoreStackSlot>(block.Children[0]);
        Assert.Equal(0x50, folded.SourceOffset);
    }

    [Fact]
    public void SlotDiamondFold_DeclinesWhenConsumedStoreIsBranchTarget()
    {
        var thenStore = new StoreStackSlot(0, new Constant(1, Int32));
        thenStore.SetSourceOffset(0x51);
        var then = new Block(0);
        then.Add(thenStore);
        var @else = new Block(0);
        @else.Add(new StoreStackSlot(0, new Constant(2, Int32)));
        var diamond = new IfStatement(new LoadArgument(0, "c", Boolean), then, @else);
        diamond.SetSourceOffset(0x50);

        var block = new Block(0);
        block.Add(diamond);
        block.Add(new Return(new LoadStackSlot(0, Int32)));
        var function = Function(
            block,
            Int32,
            [new Parameter("c", Boolean)],
            liveTarget: 0x51);

        new BooleanFoldingPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Same(diamond, block.Children[0]);
        Assert.Equal(0x51, thenStore.SourceOffset);
    }

    static IrFunction Function(
        Block block,
        TypeRef returnType,
        ImmutableArray<Parameter> parameters,
        int? liveTarget = null)
    {
        var body = new BlockContainer();
        body.Add(block);
        if (liveTarget is { } target)
        {
            var branch = new Block(0x100);
            branch.Add(new Branch(target));
            body.Add(branch);
        }
        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(returnType, parameters, HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }
}
