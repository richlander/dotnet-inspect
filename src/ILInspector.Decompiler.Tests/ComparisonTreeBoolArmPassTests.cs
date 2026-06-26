using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class ComparisonTreeBoolArmPassTests
{
    static readonly TypeRef s_int = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef s_bool = TypeRef.CoreLib("System", "Boolean");

    [Fact]
    public void SharedFalseTail_WithoutLeaveTarget_FoldsAndDropsTail()
    {
        var function = BuildFunction(includeLeaveToTail: false);

        new ComparisonTreeBoolArmPass().Run(function, PassContext.None);

        var arm = Assert.Single(function.Body.Blocks, b => b.StartOffset == 20);
        var ret = Assert.IsType<Return>(Assert.Single(arm.Children));
        Assert.IsType<LogicalBinary>(ret.Value);
        Assert.DoesNotContain(function.Body.Blocks, b => b.StartOffset is 24 or 28 or 60);
        function.CheckInvariant();
    }

    [Fact]
    public void SharedFalseTail_TargetedByNestedLeave_IsRetained()
    {
        var function = BuildFunction(includeLeaveToTail: true);

        new ComparisonTreeBoolArmPass().Run(function, PassContext.None);

        var arm = Assert.Single(function.Body.Blocks, b => b.StartOffset == 20);
        var ret = Assert.IsType<Return>(Assert.Single(arm.Children));
        Assert.IsType<LogicalBinary>(ret.Value);
        Assert.DoesNotContain(function.Body.Blocks, b => b.StartOffset is 24 or 28);
        Assert.Contains(function.Body.Blocks, b => b.StartOffset == 60);
        Assert.Contains(function.Descendants.OfType<Leave>(), leave => leave.TargetOffset == 60);
        function.CheckInvariant();
    }

    static IrFunction BuildFunction(bool includeLeaveToTail)
    {
        LoadArgument X() => new(0, "x", s_int);
        LoadArgument Y() => new(1, "y", s_int);
        Constant C(int value) => new(value, s_int);

        var container = new BlockContainer();
        var b0 = new Block(0);
        b0.Add(new ConditionalBranch(new Comparison(ComparisonKind.Equal, false, X(), C(0)), 20));
        var b4 = new Block(4);
        b4.Add(new ConditionalBranch(new Comparison(ComparisonKind.Equal, false, X(), C(1)), 44));
        var b8 = new Block(8);
        b8.Add(new ConditionalBranch(new Comparison(ComparisonKind.Equal, false, X(), C(2)), 48));
        var b12 = new Block(12);
        b12.Add(new ConditionalBranch(new Comparison(ComparisonKind.Equal, false, X(), C(3)), 52));
        var rangeLow = new Block(20);
        rangeLow.Add(new ConditionalBranch(new Comparison(ComparisonKind.LessThan, false, Y(), C(64)), 60));
        var rangeHigh = new Block(24);
        rangeHigh.Add(new ConditionalBranch(new Comparison(ComparisonKind.GreaterThan, false, Y(), C(127)), 60));

        foreach (var block in (Block[])[
            b0, b4, b8, b12, BoolReturn(16, false), rangeLow, rangeHigh, BoolReturn(28, true),
            BoolReturn(44, true), BoolReturn(48, false), BoolReturn(52, true), BoolReturn(60, false)])
        {
            container.Add(block);
        }
        if (includeLeaveToTail)
            container.Add(LeaveTargetingTail());

        var signature = new MethodSignature(s_bool,
            [new Parameter("x", s_int), new Parameter("y", s_int)], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [s_bool], container);
    }

    static Block BoolReturn(int offset, bool value)
    {
        var block = new Block(offset);
        block.Add(new StoreLocal(0, s_bool, new Constant(value, s_bool)));
        block.Add(new Return(new LoadLocal(0, s_bool)));
        return block;
    }

    static Block LeaveTargetingTail()
    {
        var tryBody = new BlockContainer();
        var leaveBlock = new Block(100);
        leaveBlock.Add(new Leave(60));
        tryBody.Add(leaveBlock);
        var finallyBody = new BlockContainer();
        var finallyBlock = new Block(104);
        finallyBlock.Add(new EndFinally());
        finallyBody.Add(finallyBlock);
        var block = new Block(96);
        block.Add(new TryFinally(tryBody, finallyBody));
        return block;
    }
}
