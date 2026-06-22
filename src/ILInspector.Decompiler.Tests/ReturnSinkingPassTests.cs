using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class ReturnSinkingPassTests
{
    static readonly TypeRef Bool = TypeRef.CoreLib("System", "Boolean");

    [Fact]
    public void ExplicitElseBoolAccumulator_StaysAccumulatorForFidelity()
    {
        var condition = new LoadArgument(0, "condition", Bool);
        var thenBlock = new Block(1);
        thenBlock.Add(new StoreLocal(0, Bool, new Constant(true, Bool)));
        var elseBlock = new Block(2);
        elseBlock.Add(new StoreLocal(0, Bool, new Constant(false, Bool)));
        var entry = new Block(0);
        entry.Add(new IfStatement(condition, thenBlock, elseBlock));
        entry.Add(new Return(new LoadLocal(0, Bool)));
        var container = new BlockContainer();
        container.Add(entry);
        var function = new IrFunction(
            "M",
            TypeRef.CoreLib("System", "Object"),
            new MethodSignature(Bool, [new Parameter("condition", Bool)], HasThis: false, GenericParameterCount: 0),
            [Bool],
            container);

        new ReturnSinkingPass().Run(function, PassContext.None);

        Assert.Contains(function.Descendants.OfType<IfStatement>(), _ => true);
        Assert.Equal(2, function.Descendants.OfType<StoreLocal>().Count());
        var ret = Assert.Single(function.Body.Blocks[0].Children.OfType<Return>());
        Assert.IsType<LoadLocal>(ret.Value);
        function.CheckInvariant();
    }

    [Fact]
    public void ExplicitElseInvertedBoolAccumulator_StaysAccumulatorForFidelity()
    {
        var condition = new LoadArgument(0, "condition", Bool);
        var thenBlock = new Block(1);
        thenBlock.Add(new StoreLocal(0, Bool, new Constant(false, Bool)));
        var elseBlock = new Block(2);
        elseBlock.Add(new StoreLocal(0, Bool, new Constant(true, Bool)));
        var entry = new Block(0);
        entry.Add(new IfStatement(condition, thenBlock, elseBlock));
        entry.Add(new Return(new LoadLocal(0, Bool)));
        var container = new BlockContainer();
        container.Add(entry);
        var function = new IrFunction(
            "M",
            TypeRef.CoreLib("System", "Object"),
            new MethodSignature(Bool, [new Parameter("condition", Bool)], HasThis: false, GenericParameterCount: 0),
            [Bool],
            container);

        new ReturnSinkingPass().Run(function, PassContext.None);

        Assert.Contains(function.Descendants.OfType<IfStatement>(), _ => true);
        Assert.Equal(2, function.Descendants.OfType<StoreLocal>().Count());
        var ret = Assert.Single(function.Body.Blocks[0].Children.OfType<Return>());
        Assert.IsType<LoadLocal>(ret.Value);
        function.CheckInvariant();
    }

    [Fact]
    public void ExplicitElseWithPriorArmStatement_StaysStructured()
    {
        var thenBlock = new Block(1);
        thenBlock.Add(new ExpressionStatement(new Call(
            new MethodRef(TypeRef.CoreLib("System", "GC"), "KeepAlive", TypeRef.CoreLib("System", "Void"), [TypeRef.CoreLib("System", "Object")], HasThis: false),
            isVirtual: false,
            [new Constant(null, TypeRef.CoreLib("System", "Object"))])));
        thenBlock.Add(new StoreLocal(0, Bool, new Constant(true, Bool)));
        var elseBlock = new Block(2);
        elseBlock.Add(new StoreLocal(0, Bool, new Constant(false, Bool)));
        var entry = new Block(0);
        entry.Add(new IfStatement(new LoadArgument(0, "condition", Bool), thenBlock, elseBlock));
        entry.Add(new Return(new LoadLocal(0, Bool)));
        var container = new BlockContainer();
        container.Add(entry);
        var function = new IrFunction(
            "M",
            TypeRef.CoreLib("System", "Object"),
            new MethodSignature(Bool, [new Parameter("condition", Bool)], HasThis: false, GenericParameterCount: 0),
            [Bool],
            container);

        new ReturnSinkingPass().Run(function, PassContext.None);

        Assert.Contains(function.Descendants.OfType<IfStatement>(), _ => true);
        Assert.Contains(function.Descendants.OfType<Return>(), r => r.Value is Constant { Value: true });
        function.CheckInvariant();
    }
}
