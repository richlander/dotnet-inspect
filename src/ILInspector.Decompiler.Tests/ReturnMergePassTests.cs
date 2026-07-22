using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
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

    [Fact]
    public void MixedConditionalAndGotoPredecessors_InlineDefaultGotoButKeepSharedTail()
    {
        var (function, defaultArm) = BuildMixedReturnTailCandidate(conditionalPredecessors: 2);

        new ReturnMergePass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.IsType<StoreLocal>(defaultArm.Children[0]);
        Assert.IsType<Return>(defaultArm.Children[1]);
        Assert.Contains(function.Body.Blocks, block => block.StartOffset == 0x0005);
    }

    [Fact]
    public void SingleConditionalAndGotoPredecessors_StayForDiamondStructuring()
    {
        var (function, defaultArm) = BuildMixedReturnTailCandidate(conditionalPredecessors: 1);

        new ReturnMergePass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.IsType<Branch>(Assert.Single(defaultArm.Children));
    }

    [Fact]
    public void SwitchAndGotoPredecessors_StayForSwitchRaising()
    {
        var (function, defaultArm) = BuildMixedReturnTailCandidate(conditionalPredecessors: 2);
        var success = Assert.Single(function.Body.Blocks, block => block.StartOffset == 0x0003);
        success.Children[0].ReplaceWith(
            new SwitchBranch(new Constant(0, Int32), [0x0005, 0x0005]));

        new ReturnMergePass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.IsType<Branch>(Assert.Single(defaultArm.Children));
    }

    [Fact]
    public void MixedPredecessorsWithFallthrough_StayForExistingStructuringRules()
    {
        var (function, defaultArm) = BuildMixedReturnTailCandidate(conditionalPredecessors: 2);
        var success = Assert.Single(function.Body.Blocks, block => block.StartOffset == 0x0003);
        success.Children[0].ReplaceWith(new StoreLocal(0, Int32, new Constant(1, Int32)));

        new ReturnMergePass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.IsType<Branch>(Assert.Single(defaultArm.Children));
    }

    [Fact]
    public void MixedPredecessorsWithDirectReturn_StayForExistingStructuringRules()
    {
        var (function, defaultArm) = BuildMixedReturnTailCandidate(conditionalPredecessors: 2);
        var merge = Assert.Single(function.Body.Blocks, block => block.StartOffset == 0x0005);
        merge.Children[0].Detach();
        var ret = Assert.IsType<Return>(merge.Children[0]);
        Assert.NotNull(ret.Value);
        ret.Value.ReplaceWith(new Constant(0, Int32));

        new ReturnMergePass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.IsType<Branch>(Assert.Single(defaultArm.Children));
    }

    static (IrFunction Function, Block DefaultArm) BuildMixedReturnTailCandidate(int conditionalPredecessors)
    {
        var body = new BlockContainer();
        var firstGuard = new Block(0x0000);
        firstGuard.Add(new ConditionalBranch(new Constant(true, TypeRef.CoreLib("System", "Boolean")), 0x0005));
        body.Add(firstGuard);
        var defaultArm = new Block(0x0001);
        defaultArm.Add(new Branch(0x0005));
        body.Add(defaultArm);
        if (conditionalPredecessors == 2)
        {
            var secondGuard = new Block(0x0002);
            secondGuard.Add(new ConditionalBranch(new Constant(false, TypeRef.CoreLib("System", "Boolean")), 0x0005));
            body.Add(secondGuard);
        }
        var success = new Block(0x0003);
        success.Add(new Return(new Constant(1, Int32)));
        body.Add(success);
        var merge = new Block(0x0005);
        merge.Add(new StoreLocal(0, Int32, new Constant(0, Int32)));
        merge.Add(new Return(new LoadLocal(0, Int32)));
        body.Add(merge);
        return (new IrFunction(
            "M",
            Holder,
            new MethodSignature(Int32, [], HasThis: false, GenericParameterCount: 0),
            [Int32],
            body), defaultArm);
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
