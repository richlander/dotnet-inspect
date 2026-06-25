using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class ForLoopPassTests
{
    static readonly TypeRef Bool = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Owner = TypeRef.CoreLib("Synthetic", "ForLoopPassTests");

    [Fact]
    public void TrailingIncrementWithoutContinue_RaisesToForLoop()
    {
        var function = FunctionWithLoop(hasContinueBeforeIncrement: false);

        new ForLoopPass().Run(function, PassContext.None);

        var loop = Assert.Single(function.Descendants.OfType<ForLoop>());
        Assert.IsType<StoreLocal>(loop.Initializer);
        Assert.IsType<StoreLocal>(loop.Increment);
        Assert.Empty(function.Descendants.OfType<WhileLoop>());
    }

    [Fact]
    public void ContinueBeforeTrailingIncrement_StaysWhileLoop()
    {
        var function = FunctionWithLoop(hasContinueBeforeIncrement: true);

        new ForLoopPass().Run(function, PassContext.None);

        var loop = Assert.Single(function.Descendants.OfType<WhileLoop>());
        Assert.Single(loop.Body.Descendants.OfType<Continue>());
        Assert.Empty(function.Descendants.OfType<ForLoop>());
    }

    static IrFunction FunctionWithLoop(bool hasContinueBeforeIncrement)
    {
        var container = new BlockContainer();
        var block = new Block();
        block.Add(new StoreLocal(0, Int32, new Constant(0, Int32)));

        var loopBody = new Block();
        if (hasContinueBeforeIncrement)
        {
            var then = new Block();
            then.Add(new Continue());
            loopBody.Add(new IfStatement(new LoadArgument(0, "skip", Bool), then, elseArm: null));
        }

        loopBody.Add(Increment());
        block.Add(new WhileLoop(
            new Comparison(ComparisonKind.LessThan, isUnsigned: false, new LoadLocal(0, Int32), new Constant(10, Int32)),
            loopBody));
        block.Add(new Return(new LoadLocal(0, Int32)));
        container.Add(block);

        var signature = new MethodSignature(
            Int32,
            [new Parameter("skip", Bool)],
            HasThis: false,
            GenericParameterCount: 0);

        return new IrFunction("M", Owner, signature, [Int32], container);
    }

    static StoreLocal Increment()
        => new(
            0,
            Int32,
            new Binary(
                BinaryKind.Add,
                isChecked: false,
                isUnsigned: false,
                new LoadLocal(0, Int32),
                new Constant(1, Int32)));
}
