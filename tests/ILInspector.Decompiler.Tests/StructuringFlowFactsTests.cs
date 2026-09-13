using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class StructuringFlowFactsTests
{
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Boolean = TypeRef.CoreLib("System", "Boolean");

    [Fact]
    public void Collect_DistinguishesJumpPredecessorsFromNestedLeaveCloneOwners()
    {
        var branch = new Block(0x00);
        branch.Add(new Branch(0x50));

        var firstConditional = new Block(0x08);
        firstConditional.Add(new ConditionalBranch(new Constant(true, Boolean), 0x60));

        var switchBlock = new Block(0x10);
        switchBlock.Add(new SwitchBranch(new Constant(0, Int32), [0x70, 0x80]));

        var nestedLeaveBody = new BlockContainer();
        var nestedLeave = new Block(0x18);
        nestedLeave.Add(new Leave(0x90));
        nestedLeaveBody.Add(nestedLeave);
        var nestedFinallyBody = new BlockContainer();
        nestedFinallyBody.Add(new Block(0x1C));
        var region = new Block(0x18);
        region.Add(new TryFinally(nestedLeaveBody, nestedFinallyBody));

        var secondConditional = new Block(0x20);
        secondConditional.Add(new ConditionalBranch(new Constant(false, Boolean), 0x60));

        var facts = StructuringFlowFacts.Collect(
            [branch, firstConditional, switchBlock, region, secondConditional]);

        Assert.Equal(0, facts.OffsetToIndex[0x00]);
        Assert.Equal(4, facts.OffsetToIndex[0x20]);
        Assert.Equal([0x50, 0x70, 0x80], facts.UnconditionalTargets.Order());
        Assert.Equal(2, facts.ConditionalTargetCounts[0x60]);
        Assert.Equal([1, 4], facts.ConditionalPredecessorIndices[0x60]);
        Assert.Equal([0], facts.BranchPredecessorIndices[0x50]);
        Assert.Equal([0], facts.JumpPredecessorIndices[0x50]);
        Assert.Equal([1, 4], facts.JumpPredecessorIndices[0x60]);
        Assert.Equal([2], facts.JumpPredecessorIndices[0x70]);
        Assert.False(facts.JumpPredecessorIndices.ContainsKey(0x90));
        Assert.Equal([0], facts.ClonePredecessorIndices[0x50]);
        Assert.Equal([1, 4], facts.ClonePredecessorIndices[0x60]);
        Assert.Equal([2], facts.ClonePredecessorIndices[0x70]);
        Assert.Equal([3], facts.ClonePredecessorIndices[0x90]);
        Assert.Equal([0x70, 0x80], facts.SwitchTargets.Order());
        Assert.Equal([0x50, 0x60, 0x70, 0x80, 0x90], facts.BranchTargets.Order());
    }

    [Fact]
    public void Collect_WithoutDispatchFacts_PreservesInlineFacts()
    {
        var branch = new Block(0x00);
        branch.Add(new Branch(0x50));

        var conditional = new Block(0x08);
        conditional.Add(new ConditionalBranch(new Constant(true, Boolean), 0x60));

        var switchBlock = new Block(0x10);
        switchBlock.Add(new SwitchBranch(new Constant(0, Int32), [0x70, 0x80]));

        var nestedLeaveBody = new BlockContainer();
        var nestedLeave = new Block(0x18);
        nestedLeave.Add(new Leave(0x90));
        nestedLeaveBody.Add(nestedLeave);
        var nestedFinallyBody = new BlockContainer();
        nestedFinallyBody.Add(new Block(0x1C));
        var region = new Block(0x18);
        region.Add(new TryFinally(nestedLeaveBody, nestedFinallyBody));

        var facts = StructuringFlowFacts.Collect(
            [branch, conditional, switchBlock, region],
            includeDispatchFacts: false);

        Assert.Equal(0, facts.OffsetToIndex[0x00]);
        Assert.Equal(3, facts.OffsetToIndex[0x18]);
        Assert.Equal([0x50, 0x70, 0x80], facts.UnconditionalTargets.Order());
        Assert.Equal(1, facts.ConditionalTargetCounts[0x60]);
        Assert.Empty(facts.ConditionalPredecessorIndices);
        Assert.Empty(facts.BranchPredecessorIndices);
        Assert.Empty(facts.JumpPredecessorIndices);
        Assert.Empty(facts.SwitchTargets);
        Assert.Equal([0], facts.ClonePredecessorIndices[0x50]);
        Assert.Equal([1], facts.ClonePredecessorIndices[0x60]);
        Assert.Equal([2], facts.ClonePredecessorIndices[0x70]);
        Assert.Equal([3], facts.ClonePredecessorIndices[0x90]);
        Assert.Equal([0x50, 0x60, 0x70, 0x80, 0x90], facts.BranchTargets.Order());
    }

    [Fact]
    public void Collect_NestedTransfersReserveCanonicalTargetLabels()
    {
        var nested = new Block(0x00);
        var arm = new Block();
        arm.Add(new Branch(0x40));
        arm.Add(new ConditionalBranch(new Constant(true, Boolean), 0x50));
        arm.Add(new SwitchBranch(new Constant(0, Int32), [0x60, 0x70]));
        nested.Add(new IfStatement(new Constant(true, Boolean), arm, elseArm: null));

        var facts = StructuringFlowFacts.Collect([nested]);

        Assert.Equal([0x40, 0x50, 0x60, 0x70], facts.PreservedTargets.Order());
        Assert.Equal([0], facts.ClonePredecessorIndices[0x40]);
        Assert.Equal([0], facts.ClonePredecessorIndices[0x50]);
        Assert.Equal([0], facts.ClonePredecessorIndices[0x60]);
        Assert.Equal([0], facts.ClonePredecessorIndices[0x70]);
        Assert.Empty(facts.UnconditionalTargets);
        Assert.Empty(facts.ConditionalTargetCounts);
        Assert.Empty(facts.JumpPredecessorIndices);
    }
}
