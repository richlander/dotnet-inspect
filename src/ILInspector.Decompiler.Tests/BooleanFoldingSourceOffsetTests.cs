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

    static IrFunction Function(
        Block block,
        TypeRef returnType,
        ImmutableArray<Parameter> parameters)
    {
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(returnType, parameters, HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }
}
