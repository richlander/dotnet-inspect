using ILInspector.Decompiler.Annotations;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Research.Tests;

public class ResearchFactRegistryTests
{
    [Fact]
    public void Registry_OrdersProducersAfterTheirDependencies()
    {
        var registry = new ResearchFactRegistry(
            new TestProducer("consumer", ["source"]),
            new TestProducer("source"));

        Assert.Equal(["source", "consumer"], registry.ProducerNames);
    }

    [Fact]
    public void EmptyRegistry_ProducesNoOverlayFacts()
    {
        using var source = MetadataSource.Open(typeof(ResearchFixture).Assembly.Location);
        var registry = new ResearchFactRegistry();

        var facts = ResearchViews.CollectFacts(
            source, typeof(ResearchFixture).FullName!, nameof(ResearchFixture.BoxInt), registry: registry);
        var annotated = ResearchViews.RenderAnnotatedSource(
            source, typeof(ResearchFixture).FullName!, nameof(ResearchFixture.BoxInt), registry: registry).Output;
        var il = ResearchViews.ProjectAnnotatedIl(
            source, typeof(ResearchFixture).FullName!, nameof(ResearchFixture.BoxInt), registry: registry).Output;
        var imported = IrImporter.Import(source, typeof(ResearchFixture).FullName!, nameof(ResearchFixture.BoxInt))
            ?? throw new InvalidOperationException("fixture method has no IL body");
        var headerFacts = registry.CollectHeaderFacts(new ResearchFactContext(source, imported));

        Assert.Empty(facts);
        Assert.Empty(headerFacts);
        Assert.DoesNotContain("alloc.", annotated);
        Assert.DoesNotContain("alloc.", il);
    }

    [Fact]
    public void RenderedOverlayFacts_ComeFromRegisteredProducers()
    {
        using var source = MetadataSource.Open(typeof(ResearchFixture).Assembly.Location);
        var registry = new ResearchFactRegistry(new TestProducer(
            "registered-allocation",
            produces: ["alloc.box"],
            facts: [new Annotation(
                new AnnotationDescriptor("alloc.box", AnnotationCategory.Allocation, "test allocation"),
                SourceOffset: 0,
                Detail: "registered")]));

        var facts = ResearchViews.CollectFacts(
            source, typeof(ResearchFixture).FullName!, nameof(ResearchFixture.BoxInt), registry: registry);
        var annotated = ResearchViews.RenderAnnotatedSource(
            source, typeof(ResearchFixture).FullName!, nameof(ResearchFixture.BoxInt), registry: registry).Output;
        var il = ResearchViews.ProjectAnnotatedIl(
            source, typeof(ResearchFixture).FullName!, nameof(ResearchFixture.BoxInt), registry: registry).Output;

        var fact = Assert.Single(facts);
        Assert.Equal("alloc.box", fact.Descriptor.Id);
        Assert.Contains("alloc.box(registered)", annotated);
        Assert.Contains("alloc.box(registered)", il);
    }

    [Fact]
    public void CostOverlay_AnnotatesHighValueCalleeAtCallSite()
    {
        using var source = MetadataSource.Open(typeof(ResearchFixture).Assembly.Location);

        var overlay = ResearchViews.RenderCostOverlay(
            source, typeof(ResearchFixture).FullName!, nameof(ResearchFixture.CallsAllocInLoopCallee)).Output;

        Assert.Contains("AllocInLoopCallee", overlay);
        Assert.Contains("cost.callee", overlay);
        Assert.Contains("alloc-loop", overlay);
    }

    [Fact]
    public void CostOverlay_DoesNotAnnotateLowSignalCallee()
    {
        using var source = MetadataSource.Open(typeof(ResearchFixture).Assembly.Location);

        var overlay = ResearchViews.RenderCostOverlay(
            source, typeof(ResearchFixture).FullName!, nameof(ResearchFixture.CallsLowSignalCallee)).Output;

        Assert.Contains("LowSignalCallee", overlay);
        Assert.DoesNotContain("cost.callee", overlay);
    }

    [Fact]
    public void CostOverlay_DoesNotAnnotateExceptionOnlyCallee()
    {
        using var source = MetadataSource.Open(typeof(ResearchFixture).Assembly.Location);

        var overlay = ResearchViews.RenderCostOverlay(
            source, typeof(ResearchFixture).FullName!, nameof(ResearchFixture.CallsExceptionOnlyCallee)).Output;

        Assert.Contains("ExceptionOnlyCallee", overlay);
        Assert.DoesNotContain("cost.callee", overlay);
        Assert.DoesNotContain("FormatException", overlay);
    }

    [Fact]
    public void CostOverlay_RendersMethodHeaderLeverage()
    {
        using var source = MetadataSource.Open(typeof(ResearchFixture).Assembly.Location);

        var overlay = ResearchViews.RenderCostOverlayWithHeaderFacts(
            source, typeof(ResearchFixture).FullName!, nameof(ResearchFixture.HighLoopLeverageCallee));

        var fact = Assert.Single(overlay.HeaderFacts);
        Assert.Equal("cost.method", fact.Descriptor.Id);
        Assert.Contains("direct-callers 20", fact.Detail);
        Assert.DoesNotContain("cost.method", overlay.Body.Output);
    }

    [Fact]
    public void CostOverlay_KeepsMethodHeaderLeverageOutOfBodyText()
    {
        using var source = MetadataSource.Open(typeof(ResearchFixture).Assembly.Location);

        var overlay = ResearchViews.RenderCostOverlayWithHeaderFacts(
            source, typeof(ResearchFixture).FullName!, nameof(ResearchFixture.HighLoopLeverageCallee));

        Assert.NotEmpty(overlay.HeaderFacts);
        Assert.DoesNotContain("cost.method", overlay.Body.Output);
        Assert.StartsWith("return value + 1;", overlay.Body.Output);
    }

    [Fact]
    public void CostOverlay_DoesNotAnnotateDirectCallerOnlyCallee()
    {
        using var source = MetadataSource.Open(typeof(ResearchFixture).Assembly.Location);

        var overlay = ResearchViews.RenderCostOverlay(
            source, typeof(ResearchFixture).FullName!, nameof(ResearchFixture.LoopCaller01)).Output;

        Assert.Contains("HighLoopLeverageCallee", overlay);
        Assert.DoesNotContain("cost.callee", overlay);
        Assert.DoesNotContain("root-reach 1", overlay);
        Assert.DoesNotContain("direct-callers 1", overlay);
    }

    [Fact]
    public void AnnotatedSource_DoesNotIncludeCostOverlayByDefault()
    {
        using var source = MetadataSource.Open(typeof(ResearchFixture).Assembly.Location);

        var annotated = ResearchViews.RenderAnnotatedSource(
            source, typeof(ResearchFixture).FullName!, nameof(ResearchFixture.CallsAllocInLoopCallee)).Output;

        Assert.DoesNotContain("cost.callee", annotated);
        Assert.DoesNotContain("cost.method", annotated);
    }

    [Fact]
    public void SemanticsOverlay_AnnotatesExceptionCalleeAtCallSite()
    {
        using var source = MetadataSource.Open(typeof(ResearchFixture).Assembly.Location);

        var overlay = ResearchViews.RenderSemanticsOverlay(
            source, typeof(ResearchFixture).FullName!, nameof(ResearchFixture.CallsExceptionOnlyCallee)).Output;

        Assert.Contains("ExceptionOnlyCallee", overlay);
        Assert.Contains("semantics.callee", overlay);
        Assert.Contains("may-throw FormatException", overlay);
        Assert.DoesNotContain("cost.callee", overlay);
    }

    [Fact]
    public void SemanticsOverlay_AnnotatesUnsafeCalleeAtCallSite()
    {
        using var source = MetadataSource.Open(typeof(ResearchFixture).Assembly.Location);

        var overlay = ResearchViews.RenderSemanticsOverlay(
            source, typeof(ResearchFixture).FullName!, nameof(ResearchFixture.CallsStackallocCallee)).Output;

        Assert.Contains("StackallocCallee", overlay);
        Assert.Contains("safety.callee", overlay);
        Assert.Contains("unsafe", overlay);
        Assert.Contains("stackalloc", overlay);
    }

    [Fact]
    public void AnnotatedSource_DoesNotIncludeSemanticsOverlayByDefault()
    {
        using var source = MetadataSource.Open(typeof(ResearchFixture).Assembly.Location);

        var annotated = ResearchViews.RenderAnnotatedSource(
            source, typeof(ResearchFixture).FullName!, nameof(ResearchFixture.CallsExceptionOnlyCallee)).Output;

        Assert.DoesNotContain("semantics.callee", annotated);
        Assert.DoesNotContain("safety.callee", annotated);
    }

    sealed class TestProducer(
        string name,
        IReadOnlyList<string>? dependsOn = null,
        IReadOnlyList<string>? produces = null,
        IReadOnlyList<Annotation>? facts = null) : IResearchFactProducer
    {
        public string Name => name;
        public IReadOnlyList<string> Produces { get; } = produces ?? [];
        public IReadOnlyList<string> DependsOn { get; } = dependsOn ?? [];
        public IReadOnlyList<Annotation> Produce(ResearchFactContext context) => facts ?? [];
    }
}

public static class ResearchFixture
{
    public static object BoxInt(int value) => value;

    public static int CallsAllocInLoopCallee(int count) => AllocInLoopCallee(count);

    public static int AllocInLoopCallee(int count)
    {
        int total = 0;
        for (int i = 0; i < count; i++)
            total += new object().GetHashCode();
        return total;
    }

    public static int CallsLowSignalCallee(int value) => LowSignalCallee(value);

    public static int LowSignalCallee(int value) => value + 1;

    public static int CallsExceptionOnlyCallee(string value) => ExceptionOnlyCallee(value);

    public static int ExceptionOnlyCallee(string value)
    {
        if (value.Length == 0)
            throw new FormatException();
        return value.Length;
    }

    public static int CallsStackallocCallee(int value) => StackallocCallee(value);

    public static int StackallocCallee(int value)
    {
        Span<int> values = stackalloc int[1];
        values[0] = value;
        return values[0];
    }

    public static int SharedLeverageCallee(int value) => value + 1;

    public static int LeverageCallerA(int value) => SharedLeverageCallee(value);

    public static int LeverageCallerB(int value) => SharedLeverageCallee(value + 1);

    public static int HighLoopLeverageCallee(int value) => value + 1;

    public static int LoopCaller01(int count) { int total = 1; for (int i = 0; i < count; i++) total += HighLoopLeverageCallee(i); return total; }
    public static int LoopCaller02(int count) { int total = 2; for (int i = 0; i < count; i++) total += HighLoopLeverageCallee(i); return total; }
    public static int LoopCaller03(int count) { int total = 3; for (int i = 0; i < count; i++) total += HighLoopLeverageCallee(i); return total; }
    public static int LoopCaller04(int count) { int total = 4; for (int i = 0; i < count; i++) total += HighLoopLeverageCallee(i); return total; }
    public static int LoopCaller05(int count) { int total = 5; for (int i = 0; i < count; i++) total += HighLoopLeverageCallee(i); return total; }
    public static int LoopCaller06(int count) { int total = 6; for (int i = 0; i < count; i++) total += HighLoopLeverageCallee(i); return total; }
    public static int LoopCaller07(int count) { int total = 7; for (int i = 0; i < count; i++) total += HighLoopLeverageCallee(i); return total; }
    public static int LoopCaller08(int count) { int total = 8; for (int i = 0; i < count; i++) total += HighLoopLeverageCallee(i); return total; }
    public static int LoopCaller09(int count) { int total = 9; for (int i = 0; i < count; i++) total += HighLoopLeverageCallee(i); return total; }
    public static int LoopCaller10(int count) { int total = 10; for (int i = 0; i < count; i++) total += HighLoopLeverageCallee(i); return total; }
    public static int LoopCaller11(int count) { int total = 11; for (int i = 0; i < count; i++) total += HighLoopLeverageCallee(i); return total; }
    public static int LoopCaller12(int count) { int total = 12; for (int i = 0; i < count; i++) total += HighLoopLeverageCallee(i); return total; }
    public static int LoopCaller13(int count) { int total = 13; for (int i = 0; i < count; i++) total += HighLoopLeverageCallee(i); return total; }
    public static int LoopCaller14(int count) { int total = 14; for (int i = 0; i < count; i++) total += HighLoopLeverageCallee(i); return total; }
    public static int LoopCaller15(int count) { int total = 15; for (int i = 0; i < count; i++) total += HighLoopLeverageCallee(i); return total; }
    public static int LoopCaller16(int count) { int total = 16; for (int i = 0; i < count; i++) total += HighLoopLeverageCallee(i); return total; }
    public static int LoopCaller17(int count) { int total = 17; for (int i = 0; i < count; i++) total += HighLoopLeverageCallee(i); return total; }
    public static int LoopCaller18(int count) { int total = 18; for (int i = 0; i < count; i++) total += HighLoopLeverageCallee(i); return total; }
    public static int LoopCaller19(int count) { int total = 19; for (int i = 0; i < count; i++) total += HighLoopLeverageCallee(i); return total; }
    public static int LoopCaller20(int count) { int total = 20; for (int i = 0; i < count; i++) total += HighLoopLeverageCallee(i); return total; }
}
