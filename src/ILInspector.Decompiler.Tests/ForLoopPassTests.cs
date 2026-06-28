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

    [Fact]
    public void ContinueInNestedLoop_DoesNotBlockOuterForLoop()
    {
        var function = FunctionWithNestedContinueLoop();

        new ForLoopPass().Run(function, PassContext.None);

        var outer = Assert.Single(function.Descendants.OfType<ForLoop>());
        Assert.Single(outer.Body.Descendants.OfType<WhileLoop>());
        Assert.Single(outer.Body.Descendants.OfType<Continue>());
    }

    [Fact]
    public void CheckedUserIncrement_StaysWhileLoop()
    {
        var function = FunctionWithCheckedUserIncrement();

        new ForLoopPass().Run(function, PassContext.None);

        // A checked user-defined increment has no for-header spelling, so the loop
        // must stay a while loop (#1712).
        Assert.Single(function.Descendants.OfType<WhileLoop>());
        Assert.Empty(function.Descendants.OfType<ForLoop>());
    }

    static IrFunction FunctionWithCheckedUserIncrement()
    {
        var userType = TypeRef.Definition("Test", "Synthetic", "Stepper", ValueTypeHint.ValueType);
        var op = new MethodRef(userType, "op_CheckedIncrement", userType, [userType], HasThis: false)
        {
            IsSpecialName = true,
            IsOperator = MetadataFactState.Yes,
        };

        var container = new BlockContainer();
        var block = new Block();
        block.Add(new StoreLocal(0, userType, new Constant(0, Int32)));

        var loopBody = new Block();
        loopBody.Add(new StoreLocal(0, userType, new Call(op, isVirtual: false, [new LoadLocal(0, userType)])));
        block.Add(new WhileLoop(
            new Comparison(ComparisonKind.LessThan, isUnsigned: false, new LoadLocal(0, Int32), new Constant(10, Int32)),
            loopBody));
        block.Add(new Return(new LoadLocal(0, Int32)));
        container.Add(block);

        var signature = new MethodSignature(Int32, [], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", Owner, signature, [userType], container);
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

    static IrFunction FunctionWithNestedContinueLoop()
    {
        var container = new BlockContainer();
        var block = new Block();
        block.Add(new StoreLocal(0, Int32, new Constant(0, Int32)));

        var outerBody = new Block();
        var innerBody = new Block();
        var then = new Block();
        then.Add(new Continue());
        innerBody.Add(new IfStatement(new LoadArgument(0, "skip", Bool), then, elseArm: null));
        outerBody.Add(new WhileLoop(new LoadArgument(0, "skip", Bool), innerBody));
        outerBody.Add(Increment());

        block.Add(new WhileLoop(
            new Comparison(ComparisonKind.LessThan, isUnsigned: false, new LoadLocal(0, Int32), new Constant(10, Int32)),
            outerBody));
        block.Add(new Return(new LoadLocal(0, Int32)));
        container.Add(block);

        var signature = new MethodSignature(
            Int32,
            [new Parameter("skip", Bool)],
            HasThis: false,
            GenericParameterCount: 0);

        return new IrFunction("M", Owner, signature, [Int32], container);
    }
}
