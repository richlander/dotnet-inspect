using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class SwitchExpressionRaisingTests
{
    static readonly TypeRef Holder = TypeRef.CoreLib("Synthetic", "Holder");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");

    [Fact]
    public void ValueBlocksWithLeaveTargetedArm_DeclinesSwitchExpressionRaise()
    {
        var function = BuildLeaveTargetedCandidate();

        new SwitchRaisingPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Empty(function.Descendants.OfType<SwitchExpression>());
        Assert.Contains(function.Body.Blocks, block => block.StartOffset == 0x0002);
        Assert.Single(function.Descendants.OfType<Leave>());
    }

    [Fact]
    public void ValueBlocksWithoutLeaveTarget_RaisesToSwitchExpression()
    {
        var function = BuildLeaveTargetedCandidate(includeLeaveTarget: false);

        new SwitchRaisingPass().Run(function, PassContext.None);
        function.CheckInvariant();

        var expression = Assert.Single(function.Descendants.OfType<SwitchExpression>());
        Assert.Equal(2, expression.Arms.Count);
        Assert.DoesNotContain(function.Body.Blocks, block => block.StartOffset == 0x0002);
    }

    static IrFunction BuildLeaveTargetedCandidate(bool includeLeaveTarget = true)
    {
        var body = new BlockContainer();

        var entry = new Block(0x0000);
        entry.Add(new SwitchBranch(new LoadArgument(0, "x", Int32), [0x0002]));
        body.Add(entry);

        var defaultBlock = new Block(0x0001);
        defaultBlock.Add(new StoreLocal(0, Int32, new Constant(99, Int32)));
        defaultBlock.Add(new Branch(0x0003));
        body.Add(defaultBlock);

        var caseBlock = new Block(0x0002);
        caseBlock.Add(new StoreLocal(0, Int32, new Constant(42, Int32)));
        caseBlock.Add(new Branch(0x0003));
        body.Add(caseBlock);

        var join = new Block(0x0003);
        join.Add(new Return(new LoadLocal(0, Int32)));
        body.Add(join);

        if (includeLeaveTarget)
        {
            var residue = new Block(0x0004);
            residue.Add(new Leave(0x0002));
            body.Add(residue);
        }

        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(Int32, [new Parameter("x", Int32)], HasThis: false, GenericParameterCount: 0),
            [Int32],
            body);
    }
}
