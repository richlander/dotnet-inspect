using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class CfgTests
{
    static Block Term(int offset, IrNode terminator)
    {
        var block = new Block(offset);
        block.Add(terminator);
        return block;
    }

    [Fact]
    public void Build_ConditionalBranch_AddsTargetThenFallthrough()
    {
        var i32 = TypeRef.CoreLib("System", "Int32");
        var blocks = new List<Block>
        {
            Term(0x00, new ConditionalBranch(
                new Comparison(ComparisonKind.Equal, isUnsigned: false, new Constant(1, i32), new Constant(0, i32)),
                targetOffset: 0x10)),
            Term(0x08, new Branch(0x10)),
            Term(0x10, new Return(null)),
        };

        var edges = Cfg.Build(blocks);

        // index 0: conditional → target (block index 2) then fall-through (1).
        Assert.Equal([2, 1], edges[0].Successors);
        Assert.Empty(edges[0].ExternalTargets);
        // index 1: unconditional branch to 0x10 (block index 2).
        Assert.Equal([2], edges[1].Successors);
        // index 2: return — a method exit, no successor.
        Assert.Empty(edges[2].Successors);
        Assert.True(edges[2].ExitsMethod);
    }

    [Fact]
    public void Build_FlagsExternalTargetsAndRegionExits()
    {
        var blocks = new List<Block>
        {
            Term(0x00, new Branch(0xFF)),     // target not in this container
            Term(0x08, new Leave(0x20)),      // EH survivor
            Term(0x10, new Return(null)),
        };

        var edges = Cfg.Build(blocks);

        Assert.Empty(edges[0].Successors);
        Assert.Equal([0xFF], edges[0].ExternalTargets);
        Assert.True(edges[1].LeavesRegion);
        Assert.True(edges[2].ExitsMethod);
    }

    [Fact]
    public void Build_NoTerminator_FallsThroughToNext()
    {
        var i32 = TypeRef.CoreLib("System", "Int32");
        var blocks = new List<Block>
        {
            Term(0x00, new StoreLocal(0, i32, new Constant(1, i32))),  // no branch: falls through
            Term(0x08, new Return(null)),
        };

        var edges = Cfg.Build(blocks);

        Assert.Equal([1], edges[0].Successors);
        Assert.True(edges[1].ExitsMethod);
    }
}
