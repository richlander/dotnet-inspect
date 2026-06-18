using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class CfgMermaidTests
{
    static Block Term(int offset, IrNode terminator)
    {
        var block = new Block(offset);
        block.Add(terminator);
        return block;
    }

    [Fact]
    public void Render_EmitsFlowchartNodesAndSuccessorEdges()
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

        var mermaid = CfgMermaid.Render(blocks);

        Assert.StartsWith("flowchart TD", mermaid);
        // every block becomes a labelled node
        Assert.Contains("b0000[\"IL_0000\"]", mermaid);
        Assert.Contains("b0010[\"IL_0010\"]", mermaid);
        // conditional emits both target and fall-through edges
        Assert.Contains("b0000 --> b0010", mermaid);
        Assert.Contains("b0000 --> b0008", mermaid);
        // the return block flows to the shared exit terminal
        Assert.Contains("b0010 --> _ret", mermaid);
        Assert.Contains("_ret([\"return\"])", mermaid);
    }

    [Fact]
    public void Render_MarksExternalTargetsAndRegionExits()
    {
        var blocks = new List<Block>
        {
            Term(0x00, new Branch(0xFF)),   // target not in this container
            Term(0x08, new Leave(0x20)),    // EH survivor
            Term(0x10, new Return(null)),
        };

        var mermaid = CfgMermaid.Render(blocks);

        // external target: dashed, labelled edge plus a declared node
        Assert.Contains("b0000 -.->|external| ext_00FF", mermaid);
        Assert.Contains("ext_00FF[\"IL_00FF (external)\"]", mermaid);
        // region exit flows to the shared leave terminal
        Assert.Contains("b0008 --> _leave", mermaid);
        Assert.Contains("_leave([\"leave region\"])", mermaid);
    }
}
