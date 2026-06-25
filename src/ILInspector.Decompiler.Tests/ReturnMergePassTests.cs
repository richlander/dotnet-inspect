using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class ReturnMergePassTests
{
    static readonly TypeRef Holder = TypeRef.CoreLib("Synthetic", "Holder");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");

    [Fact]
    public void LeaveTargetedReturnTail_RemainsAfterMergeFold()
    {
        var function = BuildReturnTailCandidate(includeLeaveTarget: true);

        new ReturnMergePass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Contains(function.Body.Blocks, block => block.StartOffset == 0x0003);
        Assert.Single(function.Descendants.OfType<Leave>());
    }

    [Fact]
    public void UntargetedReturnTail_IsRemovedAfterMergeFold()
    {
        var function = BuildReturnTailCandidate(includeLeaveTarget: false);

        new ReturnMergePass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.DoesNotContain(function.Body.Blocks, block => block.StartOffset == 0x0003);
        Assert.All(function.Body.Blocks, block => Assert.IsType<Return>(Assert.Single(block.Children)));
    }

    static IrFunction BuildReturnTailCandidate(bool includeLeaveTarget)
    {
        var body = new BlockContainer();

        var first = new Block(0x0000);
        first.Add(new Branch(0x0003));
        body.Add(first);

        var second = new Block(0x0001);
        second.Add(new Branch(0x0003));
        body.Add(second);

        var merge = new Block(0x0003);
        merge.Add(new Return(new Constant(1, Int32)));
        body.Add(merge);

        if (includeLeaveTarget)
        {
            var residue = new Block(0x0004);
            residue.Add(new Leave(0x0003));
            body.Add(residue);
        }

        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(Int32, [new Parameter("x", Int32)], HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }
}
