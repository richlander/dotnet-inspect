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

        Assert.Empty(facts);
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
}
