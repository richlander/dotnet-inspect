using System.Runtime.Versioning;
using System.Text.Json;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Annotations;
using InertText;

using DotnetInspect.Web.Interop.Source;

namespace DotnetInspect.Web.Tests;

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
        Assert.False(catalog.CallCycles.Available);
        Assert.Equal(
            BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected,
            catalog.CallCycles.UnavailableReason);
        Assert.False(catalog.SynchronousCompletions.Available);
        Assert.Equal(
            BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected,
            catalog.SynchronousCompletions.UnavailableReason);
        Assert.False(catalog.AllocationExceptionPaths.Available);
        Assert.Equal(
            BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected,
            catalog.AllocationExceptionPaths.UnavailableReason);
        Assert.False(catalog.LocalThrowPaths.Available);
        Assert.Equal(
            BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected,
            catalog.LocalThrowPaths.UnavailableReason);
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
        var unavailableCycles =
            new BrowserAnnotatedSourceCallCycleInspection(
                Available: false,
                BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected,
                IsComplete: false,
                Limits: [],
                Findings: []);
        var unavailableSynchronousCompletions =
            new BrowserAnnotatedSourceSynchronousCompletionInspection(
                Available: false,
                BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected,
                Observations: []);
        var unavailableAwaitCompletionPaths =
            new BrowserAnnotatedSourceAwaitCompletionPathInspection(
                Available: false,
                BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected,
                Observations: []);
        var unavailableAllocationExceptionPaths =
            new BrowserAnnotatedSourceAllocationExceptionPathInspection(
                Available: false,
                BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected,
                Observations: []);
        var unavailableLocalThrowPaths =
            new BrowserAnnotatedSourceLocalThrowPathInspection(
                Available: false,
                BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected,
                IsComplete: false,
                Boundaries: [],
                Limits: null,
                Receipt: null,
                Paths: []);
        var catalog = new BrowserAnnotatedSourceViewerCatalog(
            defaultFindingIds,
            supportedMedia,
            invocationLikeNodeKinds,
            unavailable,
            unavailable,
            unavailable,
            unavailableCycles,
            unavailableSynchronousCompletions,
            unavailableAwaitCompletionPaths,
            unavailableAllocationExceptionPaths,
            unavailableLocalThrowPaths,
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
    public void Create_ProjectsAndValidatesCallRelationshipAvailability()
    {
        AnnotatedSourceDocument document = CreateMixedDocument();
        BrowserAnnotatedSourceCallRelationship[] relationships =
        [
            new(
                EdgeRow: 1,
                FactId: 1,
                ModuleVersionId:
                    Guid.Parse("11111111-1111-1111-1111-111111111111"),
                CallerToken: 0x06000001,
                IlOffset: 0,
                OperandToken: 0x0A000001,
                BrowserAnnotatedSourceCallKind.Call,
                InLoop: false,
                Target("n1", "Call")),
        ];

        BrowserAnnotatedSourceViewerCatalog catalog =
            BrowserAnnotatedSourceViewerCatalogFactory.Create(
                document,
                callRelationships: relationships);

        Assert.True(catalog.CallRelationships.Available);
        Assert.Null(catalog.CallRelationships.UnavailableReason);
        Assert.Throws<ArgumentException>(() =>
            BrowserAnnotatedSourceViewerCatalogFactory.Create(
                document,
                callRelationships:
                [
                    relationships[0] with { FactId = 0 },
                ]));
        Assert.Throws<ArgumentException>(() =>
            BrowserAnnotatedSourceViewerCatalogFactory.Create(
                document,
                callRelationships:
                [
                    relationships[0] with
                    {
                        Kind = (BrowserAnnotatedSourceCallKind)99,
                    },
                ]));
    }

    [Fact]
    public void Create_ProjectsAndValidatesCallCycleEvidence()
    {
        AnnotatedSourceDocument document = CreateMixedDocument();
        BrowserAnnotatedSourceCallRelationship[] relationships =
        [
            new(
                EdgeRow: 1,
                FactId: 1,
                ModuleVersionId:
                    Guid.Parse("11111111-1111-1111-1111-111111111111"),
                CallerToken: 0x06000001,
                IlOffset: 0,
                OperandToken: 0x0A000001,
                BrowserAnnotatedSourceCallKind.Call,
                InLoop: false,
                Target("n1", "Call")),
        ];
        var cycle = new BrowserAnnotatedSourceCallCycle(
            "cycle:key",
            Ordinal: 0,
            EdgeRows: [1],
            FactIds: [1],
            Targets: [Target("n0", "Caller")]);
        var cycles = new BrowserAnnotatedSourceCallCycleInspection(
            Available: true,
            UnavailableReason: null,
            IsComplete: true,
            Limits: [],
            Findings: [cycle]);

        BrowserAnnotatedSourceViewerCatalog catalog =
            BrowserAnnotatedSourceViewerCatalogFactory.Create(
                document,
                callRelationships: relationships,
                callCycles: cycles);

        Assert.True(catalog.CallCycles.Available);
        Assert.True(catalog.CallCycles.IsComplete);
        Assert.Single(catalog.CallCycles.Findings);
        Assert.Throws<ArgumentException>(() =>
            BrowserAnnotatedSourceViewerCatalogFactory.Create(
                document,
                callRelationships: relationships,
                callCycles:
                    new BrowserAnnotatedSourceCallCycleInspection(
                        Available: true,
                        UnavailableReason: null,
                        IsComplete: true,
                        Limits: [],
                        Findings:
                        [
                            new BrowserAnnotatedSourceCallCycle(
                                "cycle:key",
                                Ordinal: 0,
                                EdgeRows: [1],
                                FactIds: [0],
                                Targets: [Target("n0", "Caller")]),
                        ])));
        Assert.Throws<ArgumentException>(() =>
            new BrowserAnnotatedSourceCallCycleInspection(
                Available: true,
                UnavailableReason: null,
                IsComplete: true,
                Limits:
                [
                    BrowserAnnotatedSourceCallCycleLimit
                        .TraversalBoundary,
                ],
                Findings: []));
    }

    [Fact]
    public void Create_ProjectsAndValidatesSynchronousCompletionEvidence()
    {
        AnnotatedSourceDocument document = CreateMixedDocument();
        BrowserAnnotatedSourceCallRelationship[] relationships =
        [
            new(
                EdgeRow: 1,
                FactId: 1,
                ModuleVersionId:
                    Guid.Parse("11111111-1111-1111-1111-111111111111"),
                CallerToken: 0x06000001,
                IlOffset: 0,
                OperandToken: 0x0A000001,
                BrowserAnnotatedSourceCallKind.CallVirtual,
                InLoop: false,
                Target("n1", "get_Result")),
        ];
        BrowserAnnotatedSourceSynchronousCompletion[] observations =
        [
            new(
                FactId: 1,
                BrowserSynchronousCompletionKind.TaskResult),
        ];

        BrowserAnnotatedSourceViewerCatalog catalog =
            BrowserAnnotatedSourceViewerCatalogFactory.Create(
                document,
                callRelationships: relationships,
                synchronousCompletions: observations);

        Assert.True(catalog.SynchronousCompletions.Available);
        Assert.Single(
            catalog.SynchronousCompletions.Observations);
        Assert.Throws<ArgumentException>(() =>
            BrowserAnnotatedSourceViewerCatalogFactory.Create(
                document,
                callRelationships: relationships,
                synchronousCompletions:
                [
                    observations[0] with { FactId = 0 },
                ]));
        Assert.Throws<ArgumentException>(() =>
            BrowserAnnotatedSourceViewerCatalogFactory.Create(
                document,
                callRelationships: relationships,
                synchronousCompletions:
                [
                    observations[0],
                    observations[0],
                ]));
    }

    [Fact]
    public void Create_ProjectsAndValidatesLocalThrowPathEvidence()
    {
        AnnotatedSourceDocument document = CreateMixedDocument();
        BrowserAnnotatedSourceCallRelationship[] relationships =
        [
            new(
                EdgeRow: 1,
                FactId: 1,
                ModuleVersionId:
                    Guid.Parse("11111111-1111-1111-1111-111111111111"),
                CallerToken: 0x06000001,
                IlOffset: 0,
                OperandToken: 0x0A000001,
                BrowserAnnotatedSourceCallKind.Call,
                InLoop: false,
                Target("n1", "Forward")),
        ];
        var path = new BrowserAnnotatedSourceLocalThrowPath(
            FactIds: [1],
            Targets:
            [
                Target("n1", "Forward"),
                Target("n2", "Throw"),
            ],
            TerminalThrows:
            [
                new BrowserAnnotatedSourceLocalThrowSite(
                    "System.ArgumentNullException",
                    Guid.Parse(
                        "11111111-1111-1111-1111-111111111111"),
                    DefinitionToken: 0x02000002,
                    ConstructionOffset: 2,
                    ConstructorToken: 0x0A000002,
                    ThrowOffset: 7),
            ]);
        var inspection =
            new BrowserAnnotatedSourceLocalThrowPathInspection(
                Available: true,
                UnavailableReason: null,
                IsComplete: true,
                Boundaries: [],
                Limits: new BrowserAnnotatedSourceLocalThrowPathLimits(
                    MaximumDepth: 3,
                    MaximumNodes: 25,
                    MaximumEdges: 100,
                    MaximumPaths: 25),
                Receipt: new BrowserAnnotatedSourceLocalThrowPathReceipt(
                    DestinationSearches: 1,
                    SearchNodes: 3,
                    SearchedEdges: 2,
                    ObservedReachablePairs: 1,
                    ReturnedPaths: 1),
                Paths: [path]);

        BrowserAnnotatedSourceViewerCatalog catalog =
            BrowserAnnotatedSourceViewerCatalogFactory.Create(
                document,
                callRelationships: relationships,
                localThrowPaths: inspection);

        Assert.True(catalog.LocalThrowPaths.Available);
        Assert.True(catalog.LocalThrowPaths.IsComplete);
        Assert.Single(catalog.LocalThrowPaths.Paths);
        Assert.Throws<ArgumentException>(() =>
            BrowserAnnotatedSourceViewerCatalogFactory.Create(
                document,
                callRelationships: relationships,
                localThrowPaths:
                    new BrowserAnnotatedSourceLocalThrowPathInspection(
                        Available: true,
                        UnavailableReason: null,
                        IsComplete: true,
                        Boundaries: [],
                        Limits: inspection.Limits,
                        Receipt: inspection.Receipt,
                        Paths:
                        [
                            new BrowserAnnotatedSourceLocalThrowPath(
                                FactIds: [0],
                                Targets: path.Targets,
                                TerminalThrows: path.TerminalThrows),
                        ])));
        Assert.Throws<ArgumentException>(() =>
            new BrowserAnnotatedSourceLocalThrowPathInspection(
                Available: true,
                UnavailableReason: null,
                IsComplete: true,
                Boundaries:
                [
                    new(
                        BrowserAnnotatedSourceLocalThrowPathBoundaryKind
                            .DepthLimit,
                        3),
                ],
                Limits: inspection.Limits,
                Receipt: inspection.Receipt,
                Paths: []));
    }

    [Fact]
    public void Create_ProjectsAndValidatesAwaitCompletionPathEvidence()
    {
        var document = new AnnotatedSourceDocument(
            "await work",
            [
                new AnnotatedSourceNode(
                    0,
                    AnnotatedSourceNodeKinds.AwaitExpression,
                    SourceLineKind.CSharp,
                    [new AnnotatedSourceSpan(0, 10)]),
            ],
            [],
            [],
            []);
        BrowserAnnotatedSourceAwaitCompletionPath[] observations =
        [
            new(NodeId: 0),
        ];

        BrowserAnnotatedSourceViewerCatalog catalog =
            BrowserAnnotatedSourceViewerCatalogFactory.Create(
                document,
                awaitCompletionPaths: observations);

        Assert.True(catalog.AwaitCompletionPaths.Available);
        Assert.Single(catalog.AwaitCompletionPaths.Observations);
        Assert.Throws<ArgumentException>(() =>
            BrowserAnnotatedSourceViewerCatalogFactory.Create(
                document,
                awaitCompletionPaths:
                [
                    observations[0],
                    observations[0],
                ]));
        Assert.Throws<ArgumentException>(() =>
            BrowserAnnotatedSourceViewerCatalogFactory.Create(
                new AnnotatedSourceDocument(
                    "work()",
                    [
                        new AnnotatedSourceNode(
                            0,
                            "InvocationExpression",
                            SourceLineKind.CSharp,
                            [new AnnotatedSourceSpan(0, 6)]),
                    ],
                    [],
                    [],
                    []),
                awaitCompletionPaths: observations));
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
    public void Create_ProjectsAndValidatesAllocationExceptionPathEvidence()
    {
        var document = new AnnotatedSourceDocument(
            "new object()",
            [
                new AnnotatedSourceNode(
                    0,
                    "ObjectCreationExpression",
                    SourceLineKind.CSharp,
                    [new AnnotatedSourceSpan(0, 12)]),
            ],
            [],
            [
                new AnnotatedSourceFact(
                    0,
                    "alloc.new",
                    nameof(AnnotationCategory.Allocation),
                    AnnotationConditionality.Always,
                    Detail: null,
                    SourceOffset: 0,
                    AnnotatedSourceFactOrigin.Body),
            ],
            [new AnnotatedSourceTarget(0, 0)]);
        BrowserAnnotatedSourceAllocationExceptionPath[] observations =
        [
            new(
                FactId: 0,
                BrowserAllocationExceptionPathKind.ThrownValue),
        ];

        BrowserAnnotatedSourceViewerCatalog catalog =
            BrowserAnnotatedSourceViewerCatalogFactory.Create(
                document,
                allocationExceptionPaths: observations);

        Assert.True(catalog.AllocationExceptionPaths.Available);
        Assert.Single(
            catalog.AllocationExceptionPaths.Observations);
        Assert.Throws<ArgumentException>(() =>
            BrowserAnnotatedSourceViewerCatalogFactory.Create(
                document,
                allocationExceptionPaths:
                [
                    observations[0],
                    observations[0],
                ]));
        Assert.Throws<ArgumentException>(() =>
            BrowserAnnotatedSourceViewerCatalogFactory.Create(
                new AnnotatedSourceDocument(
                    document.Text,
                    document.Nodes,
                    document.Regions,
                    [
                        document.Facts[0] with
                        {
                            Descriptor = "semantics.throw",
                        },
                    ],
                    document.Targets),
                allocationExceptionPaths: observations));
        Assert.Throws<ArgumentException>(() =>
            BrowserAnnotatedSourceViewerCatalogFactory.Create(
                new AnnotatedSourceDocument(
                    document.Text,
                    document.Nodes,
                    document.Regions,
                    [
                        document.Facts[0] with
                        {
                            Origin =
                                AnnotatedSourceFactOrigin.MemberHeader,
                        },
                    ],
                    document.Targets),
                allocationExceptionPaths: observations));
        Assert.Throws<ArgumentException>(() =>
            BrowserAnnotatedSourceViewerCatalogFactory.Create(
                document,
                allocationExceptionPaths:
                [
                    observations[0] with
                    {
                        Kind =
                            (BrowserAllocationExceptionPathKind)(-1),
                    },
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
                new InertString(TextPolicy.Field, "public void M()"),
                new InertString(TextPolicy.Field, "test provenance"),
                contextLimitation: null),
            BrowserSourceJsonContext.Default.BrowserAnnotatedSource);
        using JsonDocument envelope = JsonDocument.Parse(envelopeJson);
        string documentJson = JsonSerializer.Serialize(
            document,
            AnnotatedSourceDocumentCompactJsonContext.Default.AnnotatedSourceDocument);

        Assert.Equal(
            documentJson,
            envelope.RootElement.GetProperty("document").GetRawText());
        JsonElement signature = envelope.RootElement.GetProperty("signature");
        Assert.Equal(JsonValueKind.String, signature.ValueKind);
        Assert.Equal("public void M()", signature.GetString());
        JsonElement provenance = envelope.RootElement.GetProperty("provenance");
        Assert.Equal(JsonValueKind.String, provenance.ValueKind);
        Assert.Equal(
            "test provenance",
            provenance.GetString());
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
                new InertString(TextPolicy.Field, "public void M()"),
                new InertString(TextPolicy.Field, "test provenance"),
                contextLimitation: null,
                [new(1, Target("n1", "Call"))]),
            BrowserSourceJsonContext.Default.BrowserAnnotatedSource);
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
            PlatformPack: null,
            SurfaceAssemblyId: null);

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
