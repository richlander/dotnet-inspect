using ILInspector.ControlFlow;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class DominatorsTests
{
    static readonly TypeRef I32 = TypeRef.CoreLib("System", "Int32");

    static Block Term(int offset, IrNode terminator)
    {
        var block = new Block(offset);
        block.Add(terminator);
        return block;
    }

    static ConditionalBranch Cond(int targetOffset) =>
        new(new Comparison(ComparisonKind.Equal, isUnsigned: false,
            new Constant(1, I32), new Constant(0, I32)), targetOffset);

    [Fact]
    public void Diamond_SplitDominatesBothArms_MergeDoesNotDominateEitherArm()
    {
        // B0: if (c) goto B2;   B1: goto B3;   B2: <fallthrough>;   B3: return
        var blocks = new List<Block>
        {
            Term(0, Cond(2)),
            Term(1, new Branch(3)),
            Term(2, new StoreLocal(0, I32, new Constant(7, I32))),  // falls through to B3
            Term(3, new Return(null)),
        };

        var dom = Dominators.Of(Cfg.Build(blocks));

        Assert.True(dom.Dominates(0, 1));
        Assert.True(dom.Dominates(0, 2));
        Assert.True(dom.Dominates(0, 3));
        Assert.False(dom.Dominates(1, 2));
        Assert.False(dom.Dominates(2, 1));
    }

    [Fact]
    public void NestedDiamond_OuterSplitDominatesEveryDescendant()
    {
        // B0: if (c0) goto B4;   // outer split
        // B1: if (c1) goto B3;   // nested split
        // B2: goto B5;
        // B3: goto B5;
        // B4: goto B5;
        // B5: return
        var blocks = new List<Block>
        {
            Term(0, Cond(4)),
            Term(1, Cond(3)),
            Term(2, new Branch(5)),
            Term(3, new Branch(5)),
            Term(4, new Branch(5)),
            Term(5, new Return(null)),
        };

        var dom = Dominators.Of(Cfg.Build(blocks));

        for (int block = 1; block <= 5; block++)
            Assert.True(dom.Dominates(0, block));
        Assert.True(dom.Dominates(1, 2));
        Assert.True(dom.Dominates(1, 3));
        Assert.False(dom.Dominates(1, 4));  // the nested split does not dominate the outer arm
        Assert.False(dom.Dominates(5, 0));  // the merge does not dominate the split that reaches it
    }

    [Fact]
    public void LoopWithExit_HeaderDominatesBody_BodyDoesNotDominateHeader()
    {
        // B0: if (c) goto B2;   // header: exit to B2, else fall into body B1
        // B1: goto B0;          // body back-edge to the header
        // B2: return            // loop exit
        var blocks = new List<Block>
        {
            Term(0, Cond(2)),
            Term(1, new Branch(0)),
            Term(2, new Return(null)),
        };

        var dom = Dominators.Of(Cfg.Build(blocks));

        Assert.True(dom.Dominates(0, 1));
        Assert.True(dom.Dominates(0, 2));
        Assert.False(dom.Dominates(1, 0));  // the back-edge does not make the body dominate the header
        Assert.False(dom.Dominates(2, 0));
    }

    [Fact]
    public void UnreachableBlock_NeverDominatesAndIsNeverDominated()
    {
        // B0: return;   B1: goto B1 (unreachable self-loop, never reached from the entry)
        var blocks = new List<Block>
        {
            Term(0, new Return(null)),
            Term(1, new Branch(1)),
        };

        var dom = Dominators.Of(Cfg.Build(blocks));

        Assert.False(dom.Dominates(1, 0));
        Assert.False(dom.Dominates(0, 1));
        Assert.True(dom.Dominates(1, 1));  // trivial self-dominance holds even for an unreached block
    }

    [Fact]
    public void GuardChain_EachBlockDominatesItselfAndEverySuccessor()
    {
        var blocks = new List<Block>
        {
            Term(0, Cond(2)),
            Term(1, new Branch(2)),
            Term(2, new Return(null)),
        };

        var dom = Dominators.Of(Cfg.Build(blocks));

        Assert.True(dom.Dominates(0, 0));
        Assert.True(dom.Dominates(1, 1));
        Assert.True(dom.Dominates(2, 2));
        Assert.True(dom.Dominates(0, 1));
        Assert.True(dom.Dominates(0, 2));
    }
}
