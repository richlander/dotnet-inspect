using ILInspector.Decompiler.Analysis;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class MixedSourceRendererTests
{
    static string Render(string methodName)
    {
        var source = MetadataSource.Open(typeof(AllocSampleClass).Assembly.Location);
        var result = MixedSourceRenderer.Render(
            source, typeof(AllocSampleClass).FullName!, methodName);
        Assert.NotNull(result.Output);
        return result.Output!;
    }

    [Fact]
    public void CSharpStatement_PrecedesItsInterleavedIl()
    {
        // The mixed view is C#-primary: a C# statement renders, then the IL that
        // implements it follows beneath as comment lines.
        var output = Render(nameof(AllocSampleClass.MakeArray));
        var lines = output.Split('\n');
        int cs = Array.FindIndex(lines, l => l.Contains("new int[n]"));
        int il = Array.FindIndex(lines, l => l.Contains("newarr"));
        Assert.True(cs >= 0 && il > cs, "C# statement should precede its interleaved IL");
        Assert.StartsWith("//", lines[il].TrimStart());
    }

    [Fact]
    public void Fact_ShowsOnBothTheCSharpLineAndTheIlLine()
    {
        // One fact, projected co-equally: a trailing comment on the C# statement
        // and at the opcode beneath it.
        var output = Render(nameof(AllocSampleClass.MakeArray));
        var lines = output.Split('\n');
        var cs = lines.Single(l => l.Contains("new int[n]"));
        var il = lines.Single(l => l.Contains("newarr"));
        Assert.Contains("alloc.array(int[])", cs);
        Assert.Contains("alloc.array(int[])", il);
    }

    [Fact]
    public void ClosureAndDelegateAllocations_BothInterleave()
    {
        var output = Render(nameof(AllocSampleClass.Capture));
        Assert.Contains("alloc.closure", output);
        Assert.Contains("alloc.delegate", output);
        // IL is interleaved, identified by IL offsets in comment lines.
        Assert.Matches(@"// IL_[0-9A-Fa-f]{4}: ", output);
    }

    [Fact]
    public void EveryInterleavedIlLine_IsACommentBeneathCSharp()
    {
        // No bare IL leaks into the C#: every IL_ line is a comment.
        var output = Render(nameof(AllocSampleClass.SumList));
        foreach (var line in output.Split('\n'))
            if (line.Contains("IL_") && line.Contains(": "))
                Assert.Contains("//", line);
    }
}
