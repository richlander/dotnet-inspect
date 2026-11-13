using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class ArithmeticBoolOperandPassTests
{
    static readonly TypeRef Boolean = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");

    [Fact]
    public void ArithmeticSlotStoresPreserveIntegerLoadIdentity()
    {
        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new StoreStackSlot(0, BoolAdd()));
        block.Add(new Return(new Binary(
            BinaryKind.Add,
            isChecked: false,
            isUnsigned: false,
            new LoadStackSlot(0, Boolean),
            new Constant(1, Int32))));
        body.Add(block);
        var function = Function(body);

        new ArithmeticBoolOperandPass().Run(function, PassContext.None);

        var store = Assert.IsType<StoreStackSlot>(block.Children[0]);
        var storedAdd = Assert.IsType<Binary>(store.Value);
        Assert.IsType<Conditional>(storedAdd.Left);
        Assert.IsType<Conditional>(storedAdd.Right);
        var returnedAdd = Assert.IsType<Binary>(Assert.IsType<Return>(block.Children[1]).Value);
        Assert.Equal(Int32, Assert.IsType<LoadStackSlot>(returnedAdd.Left).Type);
        function.CheckInvariant();
    }

    [Fact]
    public void ArbitraryBooleanStoreDeclinesIntegerLoadIdentity()
    {
        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new StoreStackSlot(0, BoolAdd()));
        block.Add(new StoreStackSlot(0, new LoadArgument(0, "left", Boolean)));
        block.Add(new Return(new Binary(
            BinaryKind.Add,
            isChecked: false,
            isUnsigned: false,
            new LoadStackSlot(0, Boolean),
            new Constant(1, Int32))));
        body.Add(block);
        var function = Function(body);

        new ArithmeticBoolOperandPass().Run(function, PassContext.None);

        var returnedAdd = Assert.IsType<Binary>(Assert.IsType<Return>(block.Children[2]).Value);
        var normalized = Assert.IsType<Conditional>(returnedAdd.Left);
        Assert.Equal(Boolean, Assert.IsType<LoadStackSlot>(normalized.Condition).Type);
        function.CheckInvariant();
    }

    [Fact]
    public void NonInt32ArithmeticDeclinesInt32SlotIdentity()
    {
        var uint32 = TypeRef.CoreLib("System", "UInt32");
        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new StoreStackSlot(0, new Binary(
            BinaryKind.Add,
            isChecked: false,
            isUnsigned: false,
            new LoadArgument(0, "left", Boolean),
            new Constant(1u, uint32))));
        block.Add(new Return(new Binary(
            BinaryKind.Add,
            isChecked: false,
            isUnsigned: false,
            new LoadStackSlot(0, Boolean),
            new Constant(1u, uint32))));
        body.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            new MethodSignature(
                uint32,
                [new Parameter("left", Boolean)],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            body);

        new ArithmeticBoolOperandPass().Run(function, PassContext.None);

        var returnedAdd = Assert.IsType<Binary>(Assert.IsType<Return>(block.Children[1]).Value);
        var normalized = Assert.IsType<Conditional>(returnedAdd.Left);
        Assert.Equal(Boolean, Assert.IsType<LoadStackSlot>(normalized.Condition).Type);
        function.CheckInvariant();
    }

    static Binary BoolAdd()
        => new(
            BinaryKind.Add,
            isChecked: false,
            isUnsigned: false,
            new LoadArgument(0, "left", Boolean),
            new LoadArgument(1, "right", Boolean));

    static IrFunction Function(BlockContainer body)
        => new(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            new MethodSignature(
                Int32,
                [new Parameter("left", Boolean), new Parameter("right", Boolean)],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            body);
}
