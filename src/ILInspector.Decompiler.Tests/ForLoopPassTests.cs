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
    public void ProtectedLeaveToTrailingIncrement_RaisesAtomically()
    {
        var function = FunctionWithProtectedIncrementTargets(protectedLeaveCount: 1);

        new ForLoopPass().Run(function, PassContext.None);

        var loop = Assert.Single(function.Descendants.OfType<ForLoop>());
        var next = Assert.Single(loop.Body.Descendants.OfType<Continue>());
        Assert.Equal(ContinueOrigin.ProtectedRegionLeaveToForIncrement, next.Origin);
        Assert.Equal(0x20, next.SourceOffset);
        Assert.Empty(function.Descendants.OfType<Leave>());
        Assert.Empty(function.Descendants.OfType<WhileLoop>());
    }

    [Fact]
    public void MultipleProtectedLeavesToTrailingIncrement_RaiseTogether()
    {
        var function = FunctionWithProtectedIncrementTargets(protectedLeaveCount: 2);

        new ForLoopPass().Run(function, PassContext.None);

        var loop = Assert.Single(function.Descendants.OfType<ForLoop>());
        var continues = loop.Body.Descendants.OfType<Continue>().ToList();
        Assert.Equal(2, continues.Count);
        Assert.All(continues, next =>
            Assert.Equal(ContinueOrigin.ProtectedRegionLeaveToForIncrement, next.Origin));
        Assert.Empty(function.Descendants.OfType<Leave>());
        Assert.Empty(function.Descendants.OfType<WhileLoop>());
    }

    [Fact]
    public void ProtectedLeaveAndBranchToTrailingIncrement_StayWhileLoop()
    {
        var function = FunctionWithProtectedIncrementTargets(
            protectedLeaveCount: 1,
            includeBranchTarget: true);

        new ForLoopPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<WhileLoop>());
        Assert.Single(function.Descendants.OfType<Leave>());
        Assert.Single(function.Descendants.OfType<Branch>());
        Assert.Empty(function.Descendants.OfType<Continue>());
        Assert.Empty(function.Descendants.OfType<ForLoop>());
    }

    [Fact]
    public void LeaveInsideFinallyBody_StaysWhileLoop()
    {
        var function = FunctionWithProtectedIncrementTargets(
            protectedLeaveCount: 1,
            leaveInsideFinally: true);

        new ForLoopPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<WhileLoop>());
        Assert.Single(function.Descendants.OfType<Leave>());
        Assert.Empty(function.Descendants.OfType<Continue>());
        Assert.Empty(function.Descendants.OfType<ForLoop>());
    }

    [Fact]
    public void LeaveWithOnlyOuterProtectedRegion_StaysWhileLoop()
    {
        var function = FunctionWithOuterProtectedIncrementTarget();

        new ForLoopPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<WhileLoop>());
        Assert.Single(function.Descendants.OfType<Leave>());
        Assert.Empty(function.Descendants.OfType<Continue>());
        Assert.Empty(function.Descendants.OfType<ForLoop>());
    }

    [Fact]
    public void ProtectedLeaveOwnedByNestedLoop_DoesNotRaiseOuterLoop()
    {
        var function = FunctionWithProtectedIncrementTargets(
            protectedLeaveCount: 1,
            leaveInsideNestedLoop: true);

        new ForLoopPass().Run(function, PassContext.None);

        Assert.Equal(2, function.Descendants.OfType<WhileLoop>().Count());
        Assert.Single(function.Descendants.OfType<Leave>());
        Assert.Empty(function.Descendants.OfType<Continue>());
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
    public void NestedFunctionJumpTarget_EqualOuterIncrementOffset_DoesNotBlockOuterForLoop()
    {
        var container = new BlockContainer();
        var block = new Block();
        block.Add(new StoreLocal(0, Int32, new Constant(0, Int32)));

        var loopBody = new Block();

        // Nested lambda with a Leave whose target offset deliberately equals the outer increment's SourceOffset
        var lambdaBody = new BlockContainer();
        var lambdaBlock = new Block();
        lambdaBlock.Add(new Leave(targetOffset: 0x42));
        lambdaBody.Add(lambdaBlock);

        var lambdaFunc = new IrFunction("<>b__0", Owner, new MethodSignature(Int32, [], false, 0), [], lambdaBody);
        var lambda = new Lambda(Bool, [], [], [], false, false, (BlockContainer)lambdaBody.Clone());
        loopBody.Add(new ExpressionStatement(lambda));

        var increment = Increment();
        increment.SetSourceOffset(0x42);
        loopBody.Add(increment);

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
        var function = new IrFunction("Test", Owner, signature, [Int32], container);

        new ForLoopPass().Run(function, PassContext.None);

        var loop = Assert.Single(function.Descendants.OfType<ForLoop>());
        Assert.IsType<StoreLocal>(loop.Initializer);
        Assert.IsType<StoreLocal>(loop.Increment);
        Assert.Empty(function.Descendants.OfType<WhileLoop>());
    }

    [Fact]
    public void NestedFunctionLoop_UsesNestedLiveTargets()
    {
        var function = FunctionWithNestedLambdaLoop(hasLiveIncrementTarget: true);

        new ForLoopPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<WhileLoop>());
        Assert.Empty(function.Descendants.OfType<ForLoop>());
    }

    [Fact]
    public void NestedFunctionLoop_WithoutLiveIncrementTarget_Raises()
    {
        var function = FunctionWithNestedLambdaLoop(hasLiveIncrementTarget: false);

        new ForLoopPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<WhileLoop>());
        Assert.Single(function.Descendants.OfType<ForLoop>());
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

    static IrFunction FunctionWithProtectedIncrementTargets(
        int protectedLeaveCount,
        bool includeBranchTarget = false,
        bool leaveInsideFinally = false,
        bool leaveInsideNestedLoop = false)
    {
        const int incrementOffset = 0x42;
        var protectedBlock = new Block();
        for (int i = 0; i < protectedLeaveCount; i++)
        {
            var then = new Block();
            var leave = new Leave(incrementOffset);
            leave.SetSourceOffset(0x20 + i);
            then.Add(leave);
            protectedBlock.Add(new IfStatement(new LoadArgument(0, "skip", Bool), then, elseArm: null));
        }

        var protectedBody = new BlockContainer();
        protectedBody.Add(protectedBlock);
        var emptyBody = new BlockContainer();
        emptyBody.Add(new Block());

        IrNode protectedRegion = leaveInsideFinally
            ? new TryFinally(emptyBody, protectedBody)
            : new TryCatch(protectedBody, [new CatchClause(TypeRef.CoreLib("System", "Exception"), emptyBody)]);

        var loopBody = new Block();
        if (leaveInsideNestedLoop)
        {
            var nestedBody = new Block();
            nestedBody.Add(protectedRegion);
            loopBody.Add(new WhileLoop(new LoadArgument(0, "skip", Bool), nestedBody));
        }
        else
        {
            loopBody.Add(protectedRegion);
        }

        if (includeBranchTarget)
            loopBody.Add(new Branch(incrementOffset));

        var increment = Increment();
        increment.SetSourceOffset(incrementOffset);
        loopBody.Add(increment);

        var container = new BlockContainer();
        var block = new Block();
        block.Add(new StoreLocal(0, Int32, new Constant(0, Int32)));
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

    static IrFunction FunctionWithOuterProtectedIncrementTarget()
    {
        const int incrementOffset = 0x42;
        var loopBody = new Block();
        loopBody.Add(new Leave(incrementOffset));
        var increment = Increment();
        increment.SetSourceOffset(incrementOffset);
        loopBody.Add(increment);

        var tryBlock = new Block();
        tryBlock.Add(new StoreLocal(0, Int32, new Constant(0, Int32)));
        tryBlock.Add(new WhileLoop(
            new Comparison(ComparisonKind.LessThan, isUnsigned: false, new LoadLocal(0, Int32), new Constant(10, Int32)),
            loopBody));
        var tryBody = new BlockContainer();
        tryBody.Add(tryBlock);
        var catchBody = new BlockContainer();
        catchBody.Add(new Block());

        var rootBlock = new Block();
        rootBlock.Add(new TryCatch(
            tryBody,
            [new CatchClause(TypeRef.CoreLib("System", "Exception"), catchBody)]));
        var container = new BlockContainer();
        container.Add(rootBlock);
        return new IrFunction(
            "M",
            Owner,
            new MethodSignature(Int32, [], HasThis: false, GenericParameterCount: 0),
            [Int32],
            container);
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

    static IrFunction FunctionWithNestedLambdaLoop(bool hasLiveIncrementTarget)
    {
        var lambdaBody = new BlockContainer();
        var lambdaBlock = new Block();
        lambdaBlock.Add(new StoreLocal(0, Int32, new Constant(0, Int32)));

        var loopBody = new Block();
        if (hasLiveIncrementTarget)
            loopBody.Add(new Leave(targetOffset: 0x42));
        var increment = Increment();
        increment.SetSourceOffset(0x42);
        loopBody.Add(increment);
        lambdaBlock.Add(new WhileLoop(
            new Comparison(
                ComparisonKind.LessThan,
                isUnsigned: false,
                new LoadLocal(0, Int32),
                new Constant(10, Int32)),
            loopBody));
        lambdaBody.Add(lambdaBlock);

        var container = new BlockContainer();
        var block = new Block();
        block.Add(new ExpressionStatement(
            new Lambda(Bool, [], [Int32], [], false, false, lambdaBody)));
        container.Add(block);

        return new IrFunction(
            "M",
            Owner,
            new MethodSignature(Int32, [], HasThis: false, GenericParameterCount: 0),
            [],
            container);
    }

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
