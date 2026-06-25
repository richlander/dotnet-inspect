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
}
