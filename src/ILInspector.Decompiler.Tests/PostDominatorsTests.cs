using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class PostDominatorsTests
{
    static readonly TypeRef I32 = TypeRef.CoreLib("System", "Int32");

    static Block Term(int offset, IrNode terminator)
    {
        var block = new Block(offset);
        block.Add(terminator);
        return block;
    }

    // A conditional whose taken edge is `targetOffset` and whose fall-through is
    // the next block. The condition content is irrelevant to post-dominance.
    static ConditionalBranch Cond(int targetOffset) =>
        new(new Comparison(ComparisonKind.Equal, isUnsigned: false,
            new Constant(1, I32), new Constant(0, I32)), targetOffset);

    [Fact]
    public void Diamond_BothArmsPostDominatedByMerge()
    {
        // B0: if (c) goto B2;   B1: goto B3;   B2: <fallthrough>;   B3: return
        var blocks = new List<Block>
        {
            Term(0, Cond(2)),
            Term(1, new Branch(3)),
            Term(2, new StoreLocal(0, I32, new Constant(7, I32))),  // falls through to B3
            Term(3, new Return(null)),
        };

        var pdom = PostDominators.Of(blocks);

        Assert.Equal(3, pdom.ImmediatePostDominator(0));  // the merge post-dominates the split
        Assert.Equal(3, pdom.ImmediatePostDominator(1));
        Assert.Equal(3, pdom.ImmediatePostDominator(2));
        Assert.Equal(PostDominators.VirtualExit, pdom.ImmediatePostDominator(3));
    }

    [Fact]
    public void NestedDiamond_SingleGlobalMerge_IsPostDominatorOfOuterSplit()
    {
        // The InternalSetValue shape: nested diamonds whose every arm branches to
        // one common exit past every enclosing region. ipostdom of the outer split
        // is that exit — the join the index-range model cannot name.
        //
        // B0: if (c0) goto B4;   // outer split
        // B1: if (c1) goto B3;   // nested split (value-is-null arm)
        // B2: goto B5;           // arm → common exit
        // B3: goto B5;           // arm → common exit
        // B4: goto B5;           // outer arm → common exit
        // B5: return             // the common exit / post-dominator
        var blocks = new List<Block>
        {
            Term(0, Cond(4)),
            Term(1, Cond(3)),
            Term(2, new Branch(5)),
            Term(3, new Branch(5)),
            Term(4, new Branch(5)),
            Term(5, new Return(null)),
        };

        var pdom = PostDominators.Of(blocks);

        Assert.Equal(5, pdom.ImmediatePostDominator(0));  // the acceptance bar
        Assert.Equal(5, pdom.ImmediatePostDominator(1));
        Assert.Equal(5, pdom.ImmediatePostDominator(2));
        Assert.Equal(5, pdom.ImmediatePostDominator(3));
        Assert.Equal(5, pdom.ImmediatePostDominator(4));
        Assert.True(pdom.PostDominates(postDominator: 5, block: 0));
        Assert.False(pdom.PostDominates(postDominator: 1, block: 0));  // nested split does not post-dominate the outer
    }

    [Fact]
    public void EarlyReturnArm_HasNoCommonRealMerge_FallsToVirtualExit()
    {
        // Both arms exit the method directly, so the only shared post-dominator is
        // the method exit, not any real block.
        // B0: if (c) goto B2;   B1: return;   B2: return
        var blocks = new List<Block>
        {
            Term(0, Cond(2)),
            Term(1, new Return(null)),
            Term(2, new Return(null)),
        };

        var pdom = PostDominators.Of(blocks);

        Assert.Equal(PostDominators.VirtualExit, pdom.ImmediatePostDominator(0));
        Assert.Equal(PostDominators.VirtualExit, pdom.ImmediatePostDominator(1));
        Assert.Equal(PostDominators.VirtualExit, pdom.ImmediatePostDominator(2));
    }

    [Fact]
    public void BlockThatCannotReachExit_HasNoPostDominator_NeverThrows()
    {
        // B1 self-loops with no exit, so it cannot reach the method exit and has no
        // defined post-dominator. The analysis must report None, not throw, and
        // still resolve the reachable arm.
        // B0: if (c) goto B2;   B1: goto B1 (endless);   B2: return
        var blocks = new List<Block>
        {
            Term(0, Cond(2)),
            Term(1, new Branch(1)),
            Term(2, new Return(null)),
        };

        var pdom = PostDominators.Of(blocks);

        Assert.Equal(PostDominators.None, pdom.ImmediatePostDominator(1));
        Assert.Equal(2, pdom.ImmediatePostDominator(0));   // the only exit path is through B2
        Assert.Equal(PostDominators.VirtualExit, pdom.ImmediatePostDominator(2));
        Assert.False(pdom.PostDominates(postDominator: 0, block: 1));  // unreachable-to-exit: defined as false
    }

    [Fact]
    public void GuardChain_EachBlockPostDominatesItself()
    {
        // A straight guard chain: every block falls through to the next. Each
        // block trivially post-dominates itself.
        var blocks = new List<Block>
        {
            Term(0, Cond(2)),
            Term(1, new Branch(2)),
            Term(2, new Return(null)),
        };

        var pdom = PostDominators.Of(blocks);

        Assert.True(pdom.PostDominates(postDominator: 0, block: 0));
        Assert.True(pdom.PostDominates(postDominator: 2, block: 1));
        Assert.True(pdom.PostDominates(postDominator: 2, block: 0));
    }
}
