using System.Text.Json;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Annotations;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Findings;
using ILInspector.Research;

using InspectWeb.Engine.SourceFacade;

namespace InspectWeb.Engine.Tests;

public sealed class BrowserMemberFindingCensusTests
{
    [Fact]
    public void Create_PreservesDisplayIdenticalResearchInstancesAndDocumentShape()
    {
        var descriptor = new AnnotationDescriptor(
            "test.duplicate",
            AnnotationCategory.Cost,
            "duplicate");
        ResearchViews.MemberProjectionResult projection = Project(
            new ResearchFactRegistry(
                new TestProducer(
                [
                    Finding(new Annotation(
                        descriptor,
                        SourceOffset: 0,
                        Detail: "same")),
                    Finding(new Annotation(
                        descriptor,
                        SourceOffset: 0,
                        Detail: "same")),
                ])));

        BrowserMemberFindingCensus envelope = Create(projection);

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
        Assert.Equal(
            2,
            envelope.Facts.Count(static fact =>
                fact.Id == "test.duplicate"
                && fact.Detail == "same"));

        string json = JsonSerializer.Serialize(
            envelope,
            BrowserSourceJsonContext.Default.BrowserMemberFindingCensus);
        using JsonDocument serialized = JsonDocument.Parse(json);
        JsonElement root = serialized.RootElement;
        Assert.Equal(envelope.FactCensusReceipt, root
            .GetProperty("factCensusReceipt")
            .GetString());
        JsonElement annotatedSource = root.GetProperty("annotatedSource");
        JsonElement[] documentFacts =
        [
            .. annotatedSource
                .GetProperty("document")
                .GetProperty("facts")
                .EnumerateArray(),
        ];
        JsonElement[] duplicateFacts =
        [
            .. documentFacts.Where(fact =>
                fact.GetProperty("descriptor").GetString()
                    == "test.duplicate"),
        ];
        Assert.Equal(2, duplicateFacts.Length);
        Assert.All(
            duplicateFacts,
            fact =>
            {
                Assert.True(fact.TryGetProperty("source_offset", out _));
                Assert.False(fact.TryGetProperty("sourceOffset", out _));
            });
    }

    [Fact]
    public void Create_PreservesSuccessfulEmptyBodyCensusReceipt()
    {
        ResearchViews.MemberProjectionResult projection = Project(
            new ResearchFactRegistry());

        BrowserMemberFindingCensus envelope = Create(projection);

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
            new ResearchFactRegistry(
                new TestProducer(
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
            BrowserMemberFindingCensus.Create(
                second.FactCensusReceipt,
                first.Facts,
                Assert.IsType<AnnotatedSourceDocument>(first.SourceDocument),
                first.SourceDocumentFactIdentities,
                "test provenance",
                contextLimitation: null));

        Assert.Contains("different receipt", error.Message);
    }

    [Fact]
    public void Create_RejectsDuplicateSourceIdentityMapping()
    {
        ResearchViews.MemberProjectionResult projection = Project(
            new ResearchFactRegistry(
                new TestProducer(
                [
                    Finding(new Annotation(
                        new AnnotationDescriptor(
                            "test.first",
                            AnnotationCategory.Semantics,
                            "first"),
                        SourceOffset: 0)),
                ])));
        ResearchViews.AnnotatedSourceFactIdentity identity = Assert.Single(
            Assert.IsAssignableFrom<
                IReadOnlyList<ResearchViews.AnnotatedSourceFactIdentity>>(
                    projection.SourceDocumentFactIdentities));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            BrowserMemberFindingCensus.Create(
                projection.FactCensusReceipt,
                projection.Facts,
                Assert.IsType<AnnotatedSourceDocument>(projection.SourceDocument),
                [identity, identity],
                "test provenance",
                contextLimitation: null));

        Assert.Contains("invalid or duplicate instance key", error.Message);
    }

    static BrowserMemberFindingCensus Create(
        ResearchViews.MemberProjectionResult projection)
        => BrowserMemberFindingCensus.Create(
            projection.FactCensusReceipt,
            projection.Facts,
            Assert.IsType<AnnotatedSourceDocument>(projection.SourceDocument),
            projection.SourceDocumentFactIdentities,
            "test provenance",
            contextLimitation: null);

    static ResearchViews.MemberProjectionResult Project(
        ResearchFactRegistry registry)
    {
        using MetadataSource source = MetadataSource.Open(
            typeof(BrowserMemberFindingCensusTests).Assembly.Location);
        return ResearchViews.ProjectMember(
            new ResearchViews.MemberProjectionRequest(
                source,
                typeof(BrowserMemberFindingCensusTests).FullName!,
                nameof(BoxInt),
                Registry: registry,
                FactRows: true,
                SourceDocument: true));
    }

    public static object BoxInt(int value) => value;

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
        public string Name => "browser-finding-census-test";
        public IReadOnlyList<string> Produces =>
            [.. findings.Select(static finding => finding.Descriptor.Id)];
        public IReadOnlyList<string> DependsOn => [];

        public IReadOnlyList<Finding<IAnnotation>> Produce(
            ResearchFactContext context)
            => findings;
    }
}
