using System.Runtime.Versioning;
using System.Text.Json;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Annotations;

namespace InspectWeb.Engine.Tests;

[SupportedOSPlatform("browser")]
public sealed class BrowserAnnotatedSourceViewerCatalogTests
{
    [Fact]
    public void Create_EmptyDocumentProjectsOnlyCSharpAndUnavailableCapabilities()
    {
        BrowserAnnotatedSourceViewerCatalog catalog =
            BrowserAnnotatedSourceViewerCatalogFactory.Create(
                new AnnotatedSourceDocument("", [], [], [], []));

        Assert.Empty(catalog.DefaultFindingIds);
        Assert.Equal(
            [BrowserAnnotatedSourceMedium.CSharp],
            catalog.SupportedMedia);
        Assert.Empty(catalog.InvocationLikeNodeKinds);
        Assert.False(catalog.FindingEvidence.Available);
        Assert.Equal(
            BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected,
            catalog.FindingEvidence.UnavailableReason);
        Assert.False(catalog.Destinations.Available);
        Assert.Equal(
            BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected,
            catalog.Destinations.UnavailableReason);
    }

    [Theory]
    [InlineData(nameof(AnnotationCategory.Allocation))]
    [InlineData(nameof(AnnotationCategory.Unsafety))]
    [InlineData(nameof(AnnotationCategory.Cost))]
    [InlineData(nameof(AnnotationCategory.Semantics))]
    [InlineData(nameof(AnnotationCategory.Lifetime))]
    public void Create_IncludesEveryCurrentDefaultFindingCategory(string category)
    {
        var document = new AnnotatedSourceDocument(
            "body",
            [
                new AnnotatedSourceNode(
                    0,
                    "MemberBody",
                    SourceLineKind.CSharp,
                    [new AnnotatedSourceSpan(0, 4)]),
            ],
            [],
            [
                new AnnotatedSourceFact(
                    0,
                    "example.fact",
                    category,
                    AnnotationConditionality.Always,
                    Detail: null,
                    SourceOffset: 0,
                    AnnotatedSourceFactOrigin.Body),
            ],
            [new AnnotatedSourceTarget(0, 0)]);

        BrowserAnnotatedSourceViewerCatalog catalog =
            BrowserAnnotatedSourceViewerCatalogFactory.Create(document);

        Assert.Equal([0], catalog.DefaultFindingIds);
        Assert.Equal(
            [BrowserAnnotatedSourceMedium.CSharp],
            catalog.SupportedMedia);
    }

    [Fact]
    public void DefaultFindingCategoriesRequireAnExplicitDecisionForEveryCategory()
    {
        Assert.Equal(
            [
                AnnotationCategory.Allocation,
                AnnotationCategory.Unsafety,
                AnnotationCategory.Lifetime,
                AnnotationCategory.Cost,
                AnnotationCategory.Semantics,
                AnnotationCategory.Relationship,
            ],
            Enum.GetValues<AnnotationCategory>());
        Assert.Equal(
            [
                AnnotationCategory.Allocation,
                AnnotationCategory.Unsafety,
                AnnotationCategory.Lifetime,
                AnnotationCategory.Cost,
                AnnotationCategory.Semantics,
            ],
            BrowserAnnotatedSourceViewerCatalogFactory.DefaultFindingCategories
                .Order());
    }

    [Fact]
    public void CapabilityAvailabilityRequiresExactlyOneOutcome()
    {
        Assert.Throws<ArgumentException>(() =>
            new BrowserAnnotatedSourceCapabilityAvailability(
                Available: true,
                BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected));
        Assert.Throws<ArgumentException>(() =>
            new BrowserAnnotatedSourceCapabilityAvailability(
                Available: false,
                UnavailableReason: null));
    }

    [Fact]
    public void Create_ProjectsDocumentRelativeDefaultsAndInvocationKinds()
    {
        AnnotatedSourceDocument document = CreateMixedDocument();

        BrowserAnnotatedSourceViewerCatalog catalog =
            BrowserAnnotatedSourceViewerCatalogFactory.Create(document);

        Assert.Equal([0, 2], catalog.DefaultFindingIds);
        Assert.Equal(
            [
                BrowserAnnotatedSourceMedium.CSharp,
                BrowserAnnotatedSourceMedium.Il,
            ],
            catalog.SupportedMedia);
        Assert.Equal(
            [
                "InvocationExpression",
                "IndirectInvocationExpression",
            ],
            catalog.InvocationLikeNodeKinds);
    }

    [Fact]
    public void Create_ExcludesRelationshipAndUnanchoredDefaultFacts()
    {
        BrowserAnnotatedSourceViewerCatalog catalog =
            BrowserAnnotatedSourceViewerCatalogFactory.Create(
                CreateMixedDocument());

        Assert.DoesNotContain(1, catalog.DefaultFindingIds);
        Assert.DoesNotContain(3, catalog.DefaultFindingIds);
    }

    [Fact]
    public void Create_ProjectsPresentInvocationLikeKindsInCapabilityOrder()
    {
        var document = new AnnotatedSourceDocument(
            "x",
            [
                new AnnotatedSourceNode(
                    0,
                    "DelegateCreationExpression",
                    SourceLineKind.CSharp,
                    [new AnnotatedSourceSpan(0, 1)]),
                new AnnotatedSourceNode(
                    1,
                    "ObjectCreationExpression",
                    SourceLineKind.CSharp,
                    [new AnnotatedSourceSpan(0, 1)]),
                new AnnotatedSourceNode(
                    2,
                    "IndirectInvocationExpression",
                    SourceLineKind.CSharp,
                    [new AnnotatedSourceSpan(0, 1)]),
                new AnnotatedSourceNode(
                    3,
                    "InvocationExpression",
                    SourceLineKind.CSharp,
                    [new AnnotatedSourceSpan(0, 1)]),
                new AnnotatedSourceNode(
                    4,
                    "MemberAccessExpression",
                    SourceLineKind.CSharp,
                    [new AnnotatedSourceSpan(0, 1)]),
            ],
            [],
            [],
            []);

        BrowserAnnotatedSourceViewerCatalog catalog =
            BrowserAnnotatedSourceViewerCatalogFactory.Create(document);

        Assert.Equal(
            [
                "InvocationExpression",
                "IndirectInvocationExpression",
                "ObjectCreationExpression",
                "DelegateCreationExpression",
            ],
            catalog.InvocationLikeNodeKinds);
    }

    [Fact]
    public void EnvelopeSerializationPreservesPortableDocumentBytes()
    {
        AnnotatedSourceDocument document = CreateMixedDocument();
        string envelopeJson = JsonSerializer.Serialize(
            BrowserAnnotatedSource.Create(
                document,
                "test provenance",
                contextLimitation: null),
            BrowserJsonContext.Default.BrowserAnnotatedSource);
        using JsonDocument envelope = JsonDocument.Parse(envelopeJson);
        string documentJson = JsonSerializer.Serialize(
            document,
            AnnotatedSourceDocumentCompactJsonContext.Default.AnnotatedSourceDocument);

        Assert.Equal(
            documentJson,
            envelope.RootElement.GetProperty("document").GetRawText());
        JsonElement catalog = envelope.RootElement.GetProperty("viewerCatalog");
        Assert.Equal(
            ["CSharp", "Il"],
            catalog.GetProperty("supportedMedia")
                .EnumerateArray()
                .Select(item => item.GetString()));
        Assert.False(
            catalog.GetProperty("findingEvidence")
                .GetProperty("available")
                .GetBoolean());
        Assert.Equal(
            "NotProjected",
            catalog.GetProperty("findingEvidence")
                .GetProperty("unavailableReason")
                .GetString());
        Assert.False(
            catalog.GetProperty("destinations")
                .GetProperty("available")
                .GetBoolean());
        Assert.Equal(
            "NotProjected",
            catalog.GetProperty("destinations")
                .GetProperty("unavailableReason")
                .GetString());
    }

    private static AnnotatedSourceDocument CreateMixedDocument()
    {
        const string text = "Call();\nIndirect();\nIL_0000: ret";
        AnnotatedSourceNode[] nodes =
        [
            new(
                0,
                "MemberBody",
                SourceLineKind.CSharp,
                [
                    new AnnotatedSourceSpan(0, 7),
                    new AnnotatedSourceSpan(8, 11),
                ]),
            new(
                1,
                "InvocationExpression",
                SourceLineKind.CSharp,
                [new AnnotatedSourceSpan(0, 6)]),
            new(
                2,
                "IndirectInvocationExpression",
                SourceLineKind.CSharp,
                [new AnnotatedSourceSpan(8, 10)]),
            new(
                3,
                AnnotatedSourceNode.InstructionKind,
                SourceLineKind.Il,
                [new AnnotatedSourceSpan(20, 12)],
                IlOffset: 0),
        ];
        AnnotatedSourceFact[] facts =
        [
            new(
                0,
                "alloc.call",
                nameof(AnnotationCategory.Allocation),
                AnnotationConditionality.Always,
                Detail: null,
                SourceOffset: 0,
                AnnotatedSourceFactOrigin.Body),
            new(
                1,
                "call.edge",
                nameof(AnnotationCategory.Relationship),
                AnnotationConditionality.Always,
                Detail: null,
                SourceOffset: 0,
                AnnotatedSourceFactOrigin.Body),
            new(
                2,
                "cost.call",
                nameof(AnnotationCategory.Cost),
                AnnotationConditionality.Always,
                Detail: null,
                SourceOffset: 0,
                AnnotatedSourceFactOrigin.Body),
            new(
                3,
                "semantics.header",
                nameof(AnnotationCategory.Semantics),
                AnnotationConditionality.Always,
                Detail: null,
                SourceOffset: -1,
                AnnotatedSourceFactOrigin.MemberHeader),
        ];
        AnnotatedSourceTarget[] targets =
        [
            new(0, 1),
            new(1, 2),
            new(2, 3),
        ];

        return new AnnotatedSourceDocument(text, nodes, [], facts, targets);
    }
}
