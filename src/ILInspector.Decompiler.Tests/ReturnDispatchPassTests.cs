using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class ReturnDispatchPassTests
{
    static readonly TypeRef Holder = TypeRef.CoreLib("Synthetic", "Holder");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");

    [Fact]
    public void LeaveTargetedArm_DeclinesReturnDispatchFold()
    {
        var function = BuildReturnDispatchCandidate(includeLeaveTarget: true);

        new ReturnDispatchPass().Run(function, PassContext.None);
        function.CheckInvariant();

        var tryFinally = Assert.Single(function.Descendants.OfType<TryFinally>());
        Assert.Equal(9, tryFinally.TryBody.Blocks.Count);
        Assert.Contains(tryFinally.TryBody.Blocks, block => block.StartOffset == 0x0006);
        Assert.Single(function.Descendants.OfType<Leave>());
    }

    [Fact]
    public void UntargetedArms_StillFoldToOrderedGuardReturns()
    {
        var function = BuildReturnDispatchCandidate(includeLeaveTarget: false);

        new ReturnDispatchPass().Run(function, PassContext.None);
        function.CheckInvariant();

        var tryFinally = Assert.Single(function.Descendants.OfType<TryFinally>());
        var block = Assert.Single(tryFinally.TryBody.Blocks);
        Assert.Equal(0x0000, block.StartOffset);
        Assert.Equal(4, block.Children.OfType<IfStatement>().Count());
        Assert.IsType<Return>(block.Children[^1]);
    }

    [Fact]
    public void ComparisonTree_StillFoldsToNestedGuardReturns()
    {
        var function = BuildComparisonTreeCandidate();

        new ReturnDispatchPass().Run(function, PassContext.None);
        function.CheckInvariant();

        var block = Assert.Single(function.Body.Blocks);
        var root = Assert.Single(block.Children.OfType<IfStatement>());
        Assert.NotNull(root.Else);
        Assert.Equal(7, block.Descendants.OfType<Return>().Count());
        Assert.Empty(block.Descendants.OfType<ConditionalBranch>());
    }

    [Fact]
    public void SmallSelection_DeclinesTreeFold()
    {
        var function = BuildSmallSelectionCandidate();

        new ReturnDispatchPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Equal(5, function.Body.Blocks.Count);
        Assert.Equal(2, function.Descendants.OfType<ConditionalBranch>().Count());
        Assert.Empty(function.Descendants.OfType<IfStatement>());
    }

    static IrFunction BuildReturnDispatchCandidate(bool includeLeaveTarget)
    {
        var tryBody = new BlockContainer();
        for (int i = 0; i < 4; i++)
        {
            var guard = new Block(i);
            guard.Add(new ConditionalBranch(new LoadArgument(0, "x", Int32), i + 5));
            tryBody.Add(guard);
        }

        var fallback = new Block(0x0004);
        fallback.Add(new Return(new Constant(0, Int32)));
        tryBody.Add(fallback);

        for (int i = 5; i < 9; i++)
        {
            var arm = new Block(i);
            arm.Add(new Return(new Constant(i, Int32)));
            tryBody.Add(arm);
        }

        var finallyBody = new BlockContainer();
        var finallyBlock = new Block(0x0100);
        if (includeLeaveTarget)
            finallyBlock.Add(new Leave(0x0006));
        else
            finallyBlock.Add(new EndFinally());
        finallyBody.Add(finallyBlock);

        var body = new BlockContainer();
        var outer = new Block(0x0200);
        outer.Add(new TryFinally(tryBody, finallyBody));
        outer.Add(new Return(new Constant(-1, Int32)));
        body.Add(outer);

        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(Int32, [new Parameter("x", Int32)], HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }

    static IrFunction BuildComparisonTreeCandidate()
    {
        var body = new BlockContainer();
        AddGuard(body, 0, ComparisonKind.GreaterThan, argIndex: 0, targetOffset: 3);
        AddGuard(body, 1, ComparisonKind.LessThan, argIndex: 0, targetOffset: 6);
        AddReturn(body, 2, 0);
        AddGuard(body, 3, ComparisonKind.GreaterThan, argIndex: 1, targetOffset: 9);
        AddGuard(body, 4, ComparisonKind.LessThan, argIndex: 1, targetOffset: 12);
        AddReturn(body, 5, 0);
        AddGuard(body, 6, ComparisonKind.GreaterThan, argIndex: 1, targetOffset: 10);
        AddGuard(body, 7, ComparisonKind.LessThan, argIndex: 1, targetOffset: 11);
        AddReturn(body, 8, 0);
        AddReturn(body, 9, 1);
        AddReturn(body, 10, 2);
        AddReturn(body, 11, 3);
        AddReturn(body, 12, 4);

        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(Int32, [new Parameter("x", Int32), new Parameter("y", Int32)], HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }

    static IrFunction BuildSmallSelectionCandidate()
    {
        var body = new BlockContainer();
        AddGuard(body, 0, ComparisonKind.GreaterThan, argIndex: 0, targetOffset: 3);
        AddGuard(body, 1, ComparisonKind.LessThan, argIndex: 0, targetOffset: 4);
        AddReturn(body, 2, 0);
        AddReturn(body, 3, 1);
        AddReturn(body, 4, 2);

        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(Int32, [new Parameter("x", Int32)], HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }

    static void AddGuard(BlockContainer body, int offset, ComparisonKind kind, int argIndex, int targetOffset)
    {
        var block = new Block(offset);
        block.Add(new ConditionalBranch(
            new Comparison(kind, isUnsigned: false, new LoadArgument(argIndex, argIndex == 0 ? "x" : "y", Int32), new Constant(0, Int32)),
            targetOffset));
        body.Add(block);
    }

    static void AddReturn(BlockContainer body, int offset, int value)
    {
        var block = new Block(offset);
        block.Add(new Return(new Constant(value, Int32)));
        body.Add(block);
    }
}
