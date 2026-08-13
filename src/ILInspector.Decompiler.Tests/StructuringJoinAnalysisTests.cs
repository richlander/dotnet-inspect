using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class StructuringJoinAnalysisTests
{
    static readonly TypeRef I32 = TypeRef.CoreLib("System", "Int32");

    [Fact]
    public void SingleForwardJoinIdentifiesMerge()
    {
        var blocks = new List<Block>
        {
            Term(0, Cond(2)),
            Term(1, new Branch(3)),
            Term(2, new Branch(3)),
            Term(3, new Return(null)),
        };

        var plan = StructuringJoinAnalysis.Analyze(blocks);

        var region = Assert.Single(plan.Regions);
        Assert.Equal(StructuringJoinRegionKind.Forward, region.Kind);
        Assert.Equal(0, region.Start);
        Assert.Equal(3, region.End);
        Assert.Equal(3, region.Merge);
    }

    [Fact]
    public void NestedForwardJoinsAreDeepestFirst()
    {
        var blocks = new List<Block>
        {
            Term(0, Cond(4)),
            Term(1, Cond(3)),
            Term(2, new Branch(5)),
            Term(3, new Branch(5)),
            Term(4, new Branch(5)),
            Term(5, new Return(null)),
        };

        var plan = StructuringJoinAnalysis.Analyze(blocks);

        Assert.Empty(plan.BackEdgeRegions);
        Assert.Empty(plan.VirtualExitDecisions);
        Assert.Empty(plan.UnrootedDecisions);
        Assert.Collection(
            plan.ForwardRegions,
            region =>
            {
                Assert.Equal(1, region.Start);
                Assert.Equal(5, region.Merge);
            },
            region =>
            {
                Assert.Equal(0, region.Start);
                Assert.Equal(5, region.Merge);
            });
    }

    [Fact]
    public void RotatedLoopAndBodyJoinShareOnePlan()
    {
        var blocks = new List<Block>
        {
            Term(0, new Branch(4)),
            Term(1, Cond(3)),
            Term(2, new Branch(3)),
            Term(3, new StoreLocal(0, I32, new Constant(1, I32))),
            Term(4, Cond(1)),
            Term(5, new Return(null)),
        };

        var plan = StructuringJoinAnalysis.Analyze(blocks);

        var loop = Assert.Single(plan.BackEdgeRegions);
        Assert.Equal(StructuringJoinRegionKind.BackEdge, loop.Kind);
        Assert.Equal(1, loop.Start);
        Assert.Equal(5, loop.End);
        Assert.Equal(5, loop.Merge);
        Assert.Equal([4], loop.BackEdgeSources);
        Assert.True(loop.IsNonCrossing);

        Assert.Contains(plan.ForwardRegions, region =>
            region.Start == 1 && region.Merge == 3);
        Assert.Equal(StructuringJoinRegionKind.Forward, plan.Regions[0].Kind);
        Assert.Equal(StructuringJoinRegionKind.BackEdge, plan.Regions[1].Kind);
    }

    [Fact]
    public void NestedBackEdgeRegionsRemainNonCrossingAndDeepestFirst()
    {
        var blocks = new List<Block>
        {
            Term(0, new Branch(4)),
            Term(1, new StoreLocal(0, I32, new Constant(1, I32))),
            Term(2, new StoreLocal(0, I32, new Constant(2, I32))),
            Term(3, new Branch(2)),
            Term(4, new Branch(1)),
            Term(5, new Return(null)),
        };

        var plan = StructuringJoinAnalysis.Analyze(blocks);

        Assert.Collection(
            plan.BackEdgeRegions,
            inner =>
            {
                Assert.Equal(2, inner.Start);
                Assert.True(inner.IsNonCrossing);
            },
            outer =>
            {
                Assert.Equal(1, outer.Start);
                Assert.True(outer.IsNonCrossing);
            });
    }

    [Fact]
    public void CrossingBackEdgeRegionsAreRejected()
    {
        var blocks = new List<Block>
        {
            Term(0, new Branch(4)),
            Term(1, new StoreLocal(0, I32, new Constant(1, I32))),
            Term(2, new StoreLocal(0, I32, new Constant(2, I32))),
            Term(3, new Branch(1)),
            Term(4, new Branch(2)),
            Term(5, new Return(null)),
        };

        var plan = StructuringJoinAnalysis.Analyze(blocks);

        Assert.Equal(2, plan.BackEdgeRegions.Length);
        Assert.All(plan.BackEdgeRegions, region => Assert.False(region.IsNonCrossing));
    }

    [Fact]
    public void ExitAndUnrootedDecisionsRemainExplicit()
    {
        var splitExitBlocks = new List<Block>
        {
            Term(0, Cond(2)),
            Term(1, new Return(null)),
            Term(2, new Return(null)),
        };
        var unrootedBlocks = new List<Block>
        {
            Term(0, new Branch(1)),
            Term(1, Cond(1)),
        };

        var splitExitPlan = StructuringJoinAnalysis.Analyze(splitExitBlocks);
        var unrootedPlan = StructuringJoinAnalysis.Analyze(unrootedBlocks);

        Assert.Equal([0], splitExitPlan.VirtualExitDecisions);
        Assert.Empty(splitExitPlan.UnrootedDecisions);
        Assert.Equal([1], unrootedPlan.UnrootedDecisions);
        Assert.Empty(unrootedPlan.VirtualExitDecisions);
    }

    static Block Term(int offset, IrNode terminator)
    {
        var block = new Block(offset);
        block.Add(terminator);
        return block;
    }

    static ConditionalBranch Cond(int targetOffset)
        => new(
            new Comparison(
                ComparisonKind.Equal,
                isUnsigned: false,
                new Constant(1, I32),
                new Constant(0, I32)),
            targetOffset);
}
