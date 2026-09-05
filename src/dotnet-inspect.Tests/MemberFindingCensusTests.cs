using DotnetInspector.Output;
using ILInspector.Decompiler.Annotations;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Findings;
using ILInspector.Research;

namespace DotnetInspector.Tests;

public class MemberFindingCensusTests
{
    [Fact]
    public void Create_PreservesDisplayIdenticalResearchInstances()
    {
        var descriptor = new AnnotationDescriptor(
            "test.duplicate",
            AnnotationCategory.Cost,
            "duplicate");
        var producer = new TestProducer(
        [
            Finding(new Annotation(descriptor, SourceOffset: 0, Detail: "same")),
            Finding(new Annotation(descriptor, SourceOffset: 0, Detail: "same")),
        ]);
        ResearchViews.MemberProjectionResult projection = Project(
            new ResearchFactRegistry(producer));

        MemberFindingCensusEnvelope envelope = MemberFindingCensus.Create(
            projection.FactCensusReceipt,
            projection.Facts,
            Assert.IsType<ILInspector.Decompiler.AnnotatedSourceDocument>(
                projection.SourceDocument),
            projection.SourceDocumentFactIdentities);

        int[] factKeys = envelope.Facts
            .Select(static fact => fact.InstanceKey)
            .OfType<int>()
            .Order()
            .ToArray();
        int[] sourceKeys = envelope.SourceFactInstances
            .Select(static identity => identity.InstanceKey)
            .Order()
            .ToArray();
        Assert.Equal([1, 2], factKeys);
        Assert.Equal(factKeys, sourceKeys);
        Assert.Equal(2, envelope.Facts.Count(static fact =>
            fact.Id == "test.duplicate"
            && fact.Detail == "same"));
    }

    [Fact]
    public void Create_PreservesSuccessfulEmptyBodyCensusReceipt()
    {
        ResearchViews.MemberProjectionResult projection = Project(
            new ResearchFactRegistry());

        MemberFindingCensusEnvelope envelope = MemberFindingCensus.Create(
            projection.FactCensusReceipt,
            projection.Facts,
            Assert.IsType<ILInspector.Decompiler.AnnotatedSourceDocument>(
                projection.SourceDocument),
            projection.SourceDocumentFactIdentities);

        Assert.True(Guid.TryParse(envelope.FactCensusReceipt, out Guid receipt));
        Assert.NotEqual(Guid.Empty, receipt);
        Assert.DoesNotContain(
            envelope.Facts,
            static fact => fact.InstanceKey is not null);
        Assert.Empty(envelope.SourceFactInstances);
    }

    [Fact]
    public void Create_RejectsReceiptFromAnotherResearchOperation()
    {
        ResearchViews.MemberProjectionResult first = Project(
            new ResearchFactRegistry(new TestProducer(
            [
                Finding(new Annotation(
                    new AnnotationDescriptor(
                        "test.first",
                        AnnotationCategory.Semantics,
                        "first"),
                    SourceOffset: 0)),
            ])));
        ResearchViews.MemberProjectionResult second = Project(
            new ResearchFactRegistry());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            MemberFindingCensus.Create(
                second.FactCensusReceipt,
                first.Facts,
                Assert.IsType<ILInspector.Decompiler.AnnotatedSourceDocument>(
                    first.SourceDocument),
                first.SourceDocumentFactIdentities));

        Assert.Contains("different receipt", error.Message);
    }

    static ResearchViews.MemberProjectionResult Project(
        ResearchFactRegistry registry)
    {
        using MetadataSource source = MetadataSource.Open(
            typeof(FactsTableFixture).Assembly.Location);
        return ResearchViews.ProjectMember(
            new ResearchViews.MemberProjectionRequest(
                source,
                typeof(FactsTableFixture).FullName!,
                nameof(FactsTableFixture.BoxInt),
                Registry: registry,
                FactRows: true,
                SourceDocument: true));
    }

    static Finding<IAnnotation> Finding(IAnnotation annotation)
        => new(
            new FindingSubject("test-member", "test member"),
            new FindingDescriptor(
                annotation.Descriptor.Id,
                annotation.Descriptor.Title),
            new FindingKey("same-correspondence"),
            annotation,
            Detail: annotation.Detail);

    sealed class TestProducer(IReadOnlyList<Finding<IAnnotation>> findings)
        : IResearchFactProducer
    {
        public string Name => "cli-finding-census-test";
        public IReadOnlyList<string> Produces =>
            [.. findings.Select(static finding => finding.Descriptor.Id)];
        public IReadOnlyList<string> DependsOn => [];

        public IReadOnlyList<Finding<IAnnotation>> Produce(
            ResearchFactContext context)
            => findings;
    }
}
