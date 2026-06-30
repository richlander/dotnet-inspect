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
            source, typeof(ResearchFixture).FullName!, nameof(ResearchFixture.SharedLeverageCallee));

        var fact = Assert.Single(overlay.HeaderFacts);
        Assert.Equal("cost.method", fact.Descriptor.Id);
        Assert.Contains("direct-callers 2", fact.Detail);
        Assert.DoesNotContain("cost.method", overlay.Body.Output);
    }

    [Fact]
    public void CostOverlay_KeepsMethodHeaderLeverageOutOfBodyText()
    {
        using var source = MetadataSource.Open(typeof(ResearchFixture).Assembly.Location);

        var overlay = ResearchViews.RenderCostOverlayWithHeaderFacts(
            source, typeof(ResearchFixture).FullName!, nameof(ResearchFixture.AllocInLoopCallee));

        Assert.NotEmpty(overlay.HeaderFacts);
        Assert.DoesNotContain("cost.method", overlay.Body.Output);
        Assert.StartsWith("int total = 0;", overlay.Body.Output);
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

    public static int SharedLeverageCallee(int value) => value + 1;

    public static int LeverageCallerA(int value) => SharedLeverageCallee(value);

    public static int LeverageCallerB(int value) => SharedLeverageCallee(value + 1);
}
