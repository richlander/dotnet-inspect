using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class ReturnDispatchPassTests
{
    static readonly TypeRef Holder = TypeRef.CoreLib("Synthetic", "Holder");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef String = TypeRef.CoreLib("System", "String");

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
    public void SameReturnArms_FoldToPositiveFallthroughGuard()
    {
        var function = BuildSameReturnDispatchCandidate(includeLaterPrefix: false);

        new ReturnDispatchPass().Run(function, PassContext.None);
        function.CheckInvariant();

        var block = Assert.Single(function.Body.Blocks);
        Assert.IsType<StoreLocal>(block.Children[0]);
        var ifStatement = Assert.IsType<IfStatement>(block.Children[1]);
        Assert.IsType<LogicalBinary>(ifStatement.Condition);
        Assert.IsType<Return>(block.Children[2]);

        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n").TrimEnd();
        Assert.Contains("if (V_0 > 1 && V_0 < 5 && number != 12 && number != 13)", output);
        Assert.Contains("return \"paucal\";", output);
        Assert.EndsWith("return resourceKey;", output);
    }

    [Fact]
    public void SameReturnArms_WithLaterPrefix_KeepOrderedGuardReturns()
    {
        var function = BuildSameReturnDispatchCandidate(includeLaterPrefix: true);

        new ReturnDispatchPass().Run(function, PassContext.None);
        function.CheckInvariant();

        var block = Assert.Single(function.Body.Blocks);
        Assert.Equal(4, block.Children.OfType<IfStatement>().Count());
        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n").TrimEnd();
        Assert.DoesNotContain("V_0 > 1 &&", output);
        Assert.Contains("V_1 = number;", output);
        Assert.Contains("if (V_1 == 12)", output);
    }

    [Fact]
    public void SameReturnArms_BrfalseGuard_UnwrapsDoubleNegation()
    {
        var body = new BlockContainer();
        AddGuard(body, 0, new LogicalNot(new LoadArgument(1, "number", Int32)), targetOffset: 5);
        AddGuard(body, 1, new LogicalNot(new LoadArgument(2, "other", Int32)), targetOffset: 5);
        AddGuard(body, 2, ComparisonKind.Equal, new LoadArgument(1, "number", Int32), new Constant(12, Int32), targetOffset: 5);
        AddGuard(body, 3, ComparisonKind.Equal, new LoadArgument(2, "other", Int32), new Constant(13, Int32), targetOffset: 5);
        var fallback = new Block(4);
        fallback.Add(new Return(new Constant("special", String)));
        body.Add(fallback);
        var common = new Block(5);
        common.Add(new Return(new LoadArgument(0, "resourceKey", String)));
        body.Add(common);
        var function = new IrFunction(
            "M",
            Holder,
            new MethodSignature(String, [
                new Parameter("resourceKey", String),
                new Parameter("number", Int32),
                new Parameter("other", Int32),
            ], HasThis: false, GenericParameterCount: 0),
            [],
            body);

        new ReturnDispatchPass().Run(function, PassContext.None);
        function.CheckInvariant();

        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n").TrimEnd();
        Assert.Contains("if (number != 0 && other != 0 && number != 12 && other != 13)", output);
        Assert.DoesNotContain("!(", output);
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

    static IrFunction BuildSameReturnDispatchCandidate(bool includeLaterPrefix)
    {
        var body = new BlockContainer();
        var first = new Block(0);
        first.Add(new StoreLocal(0, Int32, new Binary(
            BinaryKind.Remainder,
            isChecked: false,
            isUnsigned: false,
            new LoadArgument(1, "number", Int32),
            new Constant(10, Int32))));
        first.Add(new ConditionalBranch(
            new Comparison(ComparisonKind.LessThanOrEqual, isUnsigned: false, new LoadLocal(0, Int32), new Constant(1, Int32)),
            5));
        body.Add(first);

        AddGuard(body, 1, ComparisonKind.GreaterThanOrEqual, new LoadLocal(0, Int32), new Constant(5, Int32), targetOffset: 5);
        var third = new Block(2);
        if (includeLaterPrefix)
            third.Add(new StoreLocal(1, Int32, new LoadArgument(1, "number", Int32)));
        third.Add(new ConditionalBranch(
            new Comparison(ComparisonKind.Equal, isUnsigned: false, includeLaterPrefix ? new LoadLocal(1, Int32) : new LoadArgument(1, "number", Int32), new Constant(12, Int32)),
            5));
        body.Add(third);
        AddGuard(body, 3, ComparisonKind.Equal, new LoadArgument(1, "number", Int32), new Constant(13, Int32), targetOffset: 5);
        var fallback = new Block(4);
        fallback.Add(new Return(new Constant("paucal", String)));
        body.Add(fallback);
        var common = new Block(5);
        common.Add(new Return(new LoadArgument(0, "resourceKey", String)));
        body.Add(common);

        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(String, [new Parameter("resourceKey", String), new Parameter("number", Int32)], HasThis: false, GenericParameterCount: 0),
            includeLaterPrefix ? [Int32, Int32] : [Int32],
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
        => AddGuard(body, offset, kind, new LoadArgument(argIndex, argIndex == 0 ? "x" : "y", Int32), new Constant(0, Int32), targetOffset);

    static void AddGuard(BlockContainer body, int offset, ComparisonKind kind, IrExpression left, IrExpression right, int targetOffset)
        => AddGuard(body, offset, new Comparison(kind, isUnsigned: false, left, right), targetOffset);

    static void AddGuard(BlockContainer body, int offset, IrExpression condition, int targetOffset)
    {
        var block = new Block(offset);
        block.Add(new ConditionalBranch(condition, targetOffset));
        body.Add(block);
    }

    static void AddReturn(BlockContainer body, int offset, int value)
    {
        var block = new Block(offset);
        block.Add(new Return(new Constant(value, Int32)));
        body.Add(block);
    }
}
