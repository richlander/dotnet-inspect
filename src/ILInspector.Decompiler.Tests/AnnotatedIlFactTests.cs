using ILInspector.Decompiler.Pipeline;
using ILInspector.Research;

namespace ILInspector.Decompiler.Tests;

public class AnnotatedIlFactTests
{
    static string Render(string methodName)
    {
        var source = MetadataSource.Open(typeof(AllocSampleClass).Assembly.Location);
        var result = ResearchViews.ProjectAnnotatedIl(
            source, typeof(AllocSampleClass).FullName!, methodName);
        Assert.NotNull(result.Output);
        return result.Output!;
    }

    [Fact]
    public void Box_FactLandsOnTheBoxOpcode()
    {
        // The IL-view dual of the C# anchoring: the fact attaches to the exact
        // opcode, not a statement.
        var output = Render(nameof(AllocSampleClass.BoxInt));
        var line = output.Split('\n').Single(l => l.Contains(": box"));
        Assert.Contains("alloc.box(int; alloc=boxed System.Int32; path=straight-line; path-confidence=dominates-return; escape=escapes)", line);
    }

    [Fact]
    public void CachedDelegate_FactLandsOnTheNewobjAndShowsConditionality()
    {
        var output = Render(nameof(AllocSampleClass.Cached));
        var line = output.Split('\n').Single(l => l.Contains(": newobj"));
        Assert.Contains("alloc.delegate", line);
        Assert.Contains("cached-once", line);
    }

    [Fact]
    public void StateMachine_FactLandsOnTheKickoffNewobj()
    {
        var output = Render(nameof(AllocSampleClass.Range));
        var line = output.Split('\n').Single(l => l.Contains(": newobj"));
        Assert.Contains("alloc.statemachine", line);
    }

    [Fact]
    public void FactPrecedesStructuralAnnotationsOnTheSameLine()
    {
        // Facts lead; the structural stack-type annotation follows — the
        // high-value fact is read first.
        var output = Render(nameof(AllocSampleClass.BoxInt));
        var line = output.Split('\n').Single(l => l.Contains(": box"));
        int fact = line.IndexOf("alloc.box", StringComparison.Ordinal);
        int stack = line.IndexOf("stack: [object]", StringComparison.Ordinal);
        Assert.True(fact >= 0 && stack >= 0 && fact < stack);
    }

    [Fact]
    public void NoAllocation_AddsNoFactComment()
    {
        // SumList's struct enumerator allocates nothing on the heap.
        var output = Render(nameof(AllocSampleClass.SumList));
        Assert.DoesNotContain("alloc.", output);
    }
}
