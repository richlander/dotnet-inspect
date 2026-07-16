using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.ControlFlow;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;
using ILInspector.Instructions;

namespace ILInspector.Decompiler.Tests;

public class CfgDumpTests
{
    [Theory]
    [InlineData("raised", 0)]
    [InlineData("RAISED", 0)]
    [InlineData("il", 1)]
    public void StageParser_AcceptsAdvertisedStages(
        string value,
        int expected)
    {
        Assert.Equal((CfgDumpStage)expected, CfgDumpStageParser.Parse(value));
    }

    [Fact]
    public void StageParser_RejectsUnknownStage()
    {
        var error = Assert.Throws<ArgumentException>(
            () => CfgDumpStageParser.Parse("imported"));

        Assert.Contains("Expected raised or il", error.Message);
    }

    [Fact]
    public void RaisedAdapter_PreservesExistingMermaidOutput()
    {
        var blocks = new List<Block>
        {
            Term(0x00, new Branch(0x08)),
            Term(0x08, new Return(null)),
        };

        Assert.Equal(
            CfgMermaid.Render(blocks),
            CfgDumpRenderer.RenderMermaid(CfgDumpGraph.FromRaised(blocks)));
    }

    [Fact]
    public void TextRenderer_UsesOffsetsForPredecessorsAndSuccessors()
    {
        var graph = new CfgDumpGraph(
            [0x00, 0x08, 0x10],
            [
                new BlockEdges([1, 2], [], ExitsMethod: false, LeavesRegion: false),
                new BlockEdges([2], [], ExitsMethod: false, LeavesRegion: false),
                new BlockEdges([], [], ExitsMethod: true, LeavesRegion: false),
            ]);

        var output = CfgDumpRenderer.RenderText(graph);

        Assert.Contains("IL_0000  preds: -", output);
        Assert.Contains("succs: IL_0008, IL_0010", output);
        Assert.Contains("IL_0010  preds: IL_0000, IL_0008", output);
        Assert.Contains("succs: (return)", output);
    }

    [Fact]
    public void IlAdapter_RendersCompiledEhAwareBlockGraph()
    {
        var instructions = DecodeFixture(nameof(TryFinallyFixture));
        var graph = CfgDumpGraph.FromIl(instructions.Blocks);
        var region = Assert.Single(
            instructions.Blocks.Regions,
            candidate => candidate.Kind == ILInspector.Instructions.HandlerKind.Finally);

        Assert.True(instructions.IsComplete);
        Assert.Equal(
            instructions.Blocks.Blocks.Select(block => block.Start),
            graph.BlockOffsets);
        Assert.Contains(
            graph.Edges,
            edge => edge.Successors.Any(
                successor => graph.BlockOffsets[successor] == region.HandlerStart));
        Assert.Contains(
            $"b{region.HandlerStart:X4}",
            CfgDumpRenderer.RenderMermaid(graph));
    }

    static Block Term(int offset, IrNode terminator)
    {
        var block = new Block(offset);
        block.Add(terminator);
        return block;
    }

    static MethodInstructions DecodeFixture(string methodName)
    {
        using var stream = File.OpenRead(typeof(CfgDumpTests).Assembly.Location);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var methodInfo = typeof(CfgDumpTests).GetMethod(
            methodName,
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var handle = (MethodDefinitionHandle)MetadataTokens.EntityHandle(
            methodInfo.MetadataToken);
        var method = reader.GetMethodDefinition(handle);
        return MethodInstructions.Decode(
            peReader.GetMethodBody(method.RelativeVirtualAddress));
    }

    static void TryFinallyFixture(Action action)
    {
        try
        {
            action();
        }
        finally
        {
            action();
        }
    }
}
