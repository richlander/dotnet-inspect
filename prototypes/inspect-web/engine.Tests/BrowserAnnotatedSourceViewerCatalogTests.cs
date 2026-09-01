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
    public void CatalogCollectionsRemainImmutableAcrossInputAndOutputMutation()
    {
        int[] defaultFindingIds = [1];
        BrowserAnnotatedSourceMedium[] supportedMedia =
            [BrowserAnnotatedSourceMedium.CSharp];
        string[] invocationLikeNodeKinds = ["InvocationExpression"];
        var unavailable = new BrowserAnnotatedSourceCapabilityAvailability(
            Available: false,
            BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected);
        var catalog = new BrowserAnnotatedSourceViewerCatalog(
            defaultFindingIds,
            supportedMedia,
            invocationLikeNodeKinds,
            unavailable,
            unavailable,
            []);

        defaultFindingIds[0] = 99;
        supportedMedia[0] = BrowserAnnotatedSourceMedium.Il;
        invocationLikeNodeKinds[0] = "MemberAccessExpression";
        catalog.DefaultFindingIds[0] = 98;
        catalog.SupportedMedia[0] = BrowserAnnotatedSourceMedium.Il;
        catalog.InvocationLikeNodeKinds[0] = "MemberAccessExpression";

        Assert.Equal([1], catalog.DefaultFindingIds);
        Assert.Equal(
            [BrowserAnnotatedSourceMedium.CSharp],
            catalog.SupportedMedia);
        Assert.Equal(
            ["InvocationExpression"],
            catalog.InvocationLikeNodeKinds);
        Assert.Empty(catalog.InvocationDestinations);
    }

    [Fact]
    public void Create_ProjectedEmptyDestinationsAreAvailable()
    {
        BrowserAnnotatedSourceViewerCatalog catalog =
            BrowserAnnotatedSourceViewerCatalogFactory.Create(
                new AnnotatedSourceDocument("", [], [], [], []),
                []);

        Assert.True(catalog.Destinations.Available);
        Assert.Null(catalog.Destinations.UnavailableReason);
        Assert.Empty(catalog.InvocationDestinations);
    }

    [Fact]
    public void Create_ContextFailureKeepsDestinationUnavailabilityVisible()
    {
        BrowserAnnotatedSourceViewerCatalog catalog =
            BrowserAnnotatedSourceViewerCatalogFactory.Create(
                new AnnotatedSourceDocument("", [], [], [], []),
                invocationDestinations: null,
                destinationUnavailableReason:
                    BrowserAnnotatedSourceCapabilityUnavailableReason.ContextUnavailable);

        Assert.False(catalog.Destinations.Available);
        Assert.Equal(
            BrowserAnnotatedSourceCapabilityUnavailableReason.ContextUnavailable,
            catalog.Destinations.UnavailableReason);
        Assert.Empty(catalog.InvocationDestinations);
    }

    [Fact]
    public void Create_ProjectsAndCopiesTypedInvocationDestinations()
    {
        AnnotatedSourceDocument document = CreateMixedDocument();
        BrowserAnnotatedSourceInvocationDestination[] destinations =
        [
            new(1, Target("n1", "Call")),
        ];

        BrowserAnnotatedSourceViewerCatalog catalog =
            BrowserAnnotatedSourceViewerCatalogFactory.Create(
                document,
                destinations);

        destinations[0] = new(1, Target("n2", "Other"));
        BrowserAnnotatedSourceInvocationDestination projected =
            Assert.Single(catalog.InvocationDestinations);
        Assert.True(catalog.Destinations.Available);
        Assert.Equal(1, projected.NodeId);
        Assert.Equal("n1", projected.Target.Id);
        Assert.Equal("Call", projected.Target.MemberName);
    }

    [Fact]
    public void Create_RejectsInvalidInvocationDestinationNodes()
    {
        AnnotatedSourceDocument document = CreateMixedDocument();

        Assert.Throws<ArgumentException>(() =>
            BrowserAnnotatedSourceViewerCatalogFactory.Create(
                document,
                [new(99, Target("n1", "Call"))]));
        Assert.Throws<ArgumentException>(() =>
            BrowserAnnotatedSourceViewerCatalogFactory.Create(
                document,
                [new(2, Target("n1", "Call"))]));
        Assert.Throws<ArgumentException>(() =>
            BrowserAnnotatedSourceViewerCatalogFactory.Create(
                document,
                [
                    new(1, Target("n1", "Call")),
                    new(1, Target("n2", "Other")),
                ]));
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
        Assert.Empty(
            catalog.GetProperty("invocationDestinations")
                .EnumerateArray());
    }

    [Fact]
    public void EnvelopeSerializationCarriesTypedInvocationDestinationRows()
    {
        AnnotatedSourceDocument document = CreateMixedDocument();
        string envelopeJson = JsonSerializer.Serialize(
            BrowserAnnotatedSource.Create(
                document,
                "test provenance",
                contextLimitation: null,
                [new(1, Target("n1", "Call"))]),
            BrowserJsonContext.Default.BrowserAnnotatedSource);
        using JsonDocument envelope = JsonDocument.Parse(envelopeJson);
        JsonElement catalog =
            envelope.RootElement.GetProperty("viewerCatalog");

        Assert.True(
            catalog.GetProperty("destinations")
                .GetProperty("available")
                .GetBoolean());
        JsonElement destination = Assert.Single(
            catalog.GetProperty("invocationDestinations")
                .EnumerateArray());
        Assert.Equal(1, destination.GetProperty("nodeId").GetInt32());
        Assert.Equal(
            "method:Call",
            destination.GetProperty("target")
                .GetProperty("selectorKey")
                .GetString());
    }

    private static BrowserCallGraphTarget Target(string id, string memberName) =>
        new(
            id,
            "Example",
            "1.0.0.0",
            AssemblyCulture: null,
            AssemblyPublicKeyToken: null,
            "Example.Type",
            TypeMetadataId: "Example.Type",
            TypeDefinitionId: "Example.Type",
            memberName,
            ParameterTypes: [],
            ReturnType: "System.Void",
            GenericArity: 0,
            MetadataToken: null,
            SelectorKey: $"method:{memberName}",
            "definition",
            PlatformPack: null);

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
