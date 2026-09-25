using System.Text.Json;
using DotnetInspector.Queries;
using ILInspector.Analysis;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Annotations;
using ILInspector.Decompiler.Pipeline;
using InertText;
using Inspector.Findings;
using ILInspector.Research;

using DotnetInspect.Web.Interop.Source;

namespace DotnetInspect.Web.Tests;

public sealed class BrowserMemberFindingCensusTests
{
    [Fact]
    public void CalleeDocumentProjection_DeduplicatesOnlyCompleteMethodIdentity()
    {
        AnnotatedSourceDocument document = CalleeDocument();
        MethodIdentity first = Method(Guid.Parse(
            "11111111-1111-1111-1111-111111111111"));
        MethodIdentity secondPhysicalBody = Method(Guid.Parse(
            "22222222-2222-2222-2222-222222222222"));
        AssemblyMemberFindingEvidence[] evidence =
        [
            Evidence(0, first, document),
            Evidence(1, first, document),
            Evidence(2, secondPhysicalBody, document),
        ];

        BrowserCalleeEvidenceDocumentProjectionResult projected =
            BrowserCalleeEvidenceDocumentProjection.Project(evidence);

        Assert.Equal(2, projected.Documents.Length);
        Assert.Equal(0, projected.Admissions[first].DocumentId);
        Assert.Equal(
            1,
            projected.Admissions[secondPhysicalBody].DocumentId);
        BrowserCalleeEvidenceDocumentReference unavailable =
            BrowserCalleeEvidenceDocumentProjection.Reference(
                Evidence(3, first, document: null) with
                {
                    UnavailableReason = "Instruction coordinates unavailable.",
                },
                projected);
        Assert.Null(unavailable.DocumentId);
        Assert.Empty(unavailable.NodeIds);
    }

    [Fact]
    public void CalleeDocumentProjection_ReportsAggregateBudgetOmission()
    {
        AnnotatedSourceDocument document = CalleeDocument();
        int documentCharacters =
            BrowserAnnotatedSource.SerializeDocument(document)!.Value
                .GetRawText()
                .Length;
        MethodIdentity first = Method(Guid.Parse(
            "11111111-1111-1111-1111-111111111111"));
        MethodIdentity second = Method(Guid.Parse(
            "22222222-2222-2222-2222-222222222222"));

        BrowserCalleeEvidenceDocumentProjectionResult projected =
            BrowserCalleeEvidenceDocumentProjection.Project(
                [
                    Evidence(0, first, document),
                    Evidence(1, second, document),
                ],
                documentCharacters);

        Assert.Single(projected.Documents);
        Assert.NotNull(projected.Admissions[first].DocumentId);
        Assert.Null(projected.Admissions[second].DocumentId);
        Assert.Contains(
            "aggregate Browser/Wasm document limit",
            projected.Admissions[second].UnavailableReason);
        BrowserCalleeEvidenceDocumentReference reference =
            BrowserCalleeEvidenceDocumentProjection.Reference(
                Evidence(1, second, document) with
                {
                    NodeIds = [0],
                    UnavailableReason = "Original correspondence failure.",
                },
                projected);
        Assert.Null(reference.DocumentId);
        Assert.Empty(reference.NodeIds);
        Assert.Contains(
            "aggregate Browser/Wasm document limit",
            reference.UnavailableReason);
        Assert.Contains(
            "Original correspondence failure.",
            reference.UnavailableReason);
    }

    [Fact]
    public void CalleeDocumentProjection_OrdersAdmissionByFirstFindingOccurrence()
    {
        AnnotatedSourceDocument document = CalleeDocument();
        int documentCharacters =
            BrowserAnnotatedSource.SerializeDocument(document)!.Value
                .GetRawText()
                .Length;
        MethodIdentity first = Method(Guid.Parse(
            "11111111-1111-1111-1111-111111111111"));
        MethodIdentity second = Method(Guid.Parse(
            "22222222-2222-2222-2222-222222222222"));
        AssemblyMemberFindingEvidence firstUnavailable =
            Evidence(0, first, document: null) with
            {
                UnavailableReason = "Instruction coordinates unavailable.",
            };

        BrowserCalleeEvidenceDocumentProjectionResult projected =
            BrowserCalleeEvidenceDocumentProjection.Project(
                [
                    firstUnavailable,
                    Evidence(1, second, document),
                    Evidence(2, first, document),
                ],
                documentCharacters);

        Assert.Single(projected.Documents);
        Assert.Equal(0, projected.Admissions[first].DocumentId);
        Assert.Null(projected.Admissions[second].DocumentId);
        Assert.Null(
            BrowserCalleeEvidenceDocumentProjection.Reference(
                firstUnavailable,
                projected).DocumentId);
    }

    [Fact]
    public void CalleeDocumentProjection_RejectsOneIdentityWithDifferentDocuments()
    {
        MethodIdentity member = Method(Guid.Parse(
            "11111111-1111-1111-1111-111111111111"));
        AnnotatedSourceDocument first = CalleeDocument();
        var second = new AnnotatedSourceDocument(
            "stackalloc int[2]",
            first.Nodes,
            first.Regions,
            first.Facts,
            first.Targets);

        InvalidOperationException error =
            Assert.Throws<InvalidOperationException>(() =>
                BrowserCalleeEvidenceDocumentProjection.Project(
                    [
                        Evidence(0, member, first),
                        Evidence(1, member, second),
                    ]));

        Assert.Contains("inconsistent source documents", error.Message);
    }

    [Fact]
    public void SharedDocumentTable_GrowthTracksUniqueDocumentsNotFindingCount()
    {
        const int FindingCount = 64;
        var descriptor = new AnnotationDescriptor(
            "safety.callee",
            AnnotationCategory.Unsafety,
            "callee safety");
        MemberProjectionResult projection = Project(
            new ResearchFactRegistry(
                new TestProducer(
                [
                    .. Enumerable.Range(0, FindingCount).Select(_ =>
                        Finding(new Annotation(
                            descriptor,
                            SourceOffset: 0))),
                ])));
        AnnotatedSourceFactIdentity[] identities =
        [
            .. Assert.IsAssignableFrom<
                IReadOnlyList<AnnotatedSourceFactIdentity>>(
                    projection.SourceDocumentFactIdentities),
        ];
        JsonElement document = BrowserAnnotatedSource
            .SerializeDocument(CalleeDocument())!.Value;
        BrowserAnnotatedSourceFindingEvidence[] sharedRows =
        [
            .. identities.Select(identity =>
                new BrowserAnnotatedSourceFindingEvidence(
                    identity.FactId,
                    identity.InstanceKey.Value,
                    "Example.Targets.Target()",
                    Target(),
                    BrowserCalleeEvidenceState.Instruction,
                    AggregateInputs: [],
                    [new(2, BrowserCalleeEvidenceKind.Localloc)],
                    DocumentId: 0,
                    [0],
                    UnavailableReason: null)),
        ];
        BrowserAnnotatedSourceFindingEvidence[] uniqueRows =
        [
            .. sharedRows.Select((row, index) => row with
            {
                DocumentId = index,
            }),
        ];

        BrowserMemberFindingCensus shared = Create(
            projection,
            sharedRows,
            [new(0, document)]);
        BrowserMemberFindingCensus unique = Create(
            projection,
            uniqueRows,
            [
                .. Enumerable.Range(0, FindingCount).Select(
                    id =>
                        new BrowserAnnotatedSourceFindingEvidenceDocument(
                            id,
                            document)),
            ]);
        string sharedJson = JsonSerializer.Serialize(
            shared,
            BrowserSourceJsonContext.Default.BrowserMemberFindingCensus);
        string uniqueJson = JsonSerializer.Serialize(
            unique,
            BrowserSourceJsonContext.Default.BrowserMemberFindingCensus);

        Assert.Single(shared.AnnotatedSource.FindingEvidenceDocuments);
        Assert.Equal(
            FindingCount,
            shared.AnnotatedSource.FindingEvidence.Length);
        Assert.Equal(
            FindingCount,
            unique.AnnotatedSource.FindingEvidenceDocuments.Length);
        Assert.True(
            uniqueJson.Length - sharedJson.Length
                > (FindingCount - 1) * document.GetRawText().Length);
    }

    [Fact]
    public void Create_PreservesDisplayIdenticalResearchInstancesAndDocumentShape()
    {
        var descriptor = new AnnotationDescriptor(
            "test.duplicate",
            AnnotationCategory.Cost,
            "duplicate");
        MemberProjectionResult projection = Project(
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
        MemberProjectionResult projection = Project(
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
        MemberProjectionResult first = Project(
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
        MemberProjectionResult second = Project(
            new ResearchFactRegistry());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            BrowserMemberFindingCensus.Create(
                second.FactCensusReceipt,
                first.Facts,
                Assert.IsType<AnnotatedSourceDocument>(first.SourceDocument),
                new InertString(TextPolicy.Field, "public void M()"),
                first.SourceDocumentFactIdentities,
                new InertString(TextPolicy.Field, "test provenance"),
                contextLimitation: null));

        Assert.Contains("different receipt", error.Message);
    }

    [Fact]
    public void Create_RejectsDuplicateSourceIdentityMapping()
    {
        MemberProjectionResult projection = Project(
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
        AnnotatedSourceFactIdentity identity = Assert.Single(
            Assert.IsAssignableFrom<
                IReadOnlyList<AnnotatedSourceFactIdentity>>(
                    projection.SourceDocumentFactIdentities));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            BrowserMemberFindingCensus.Create(
                projection.FactCensusReceipt,
                projection.Facts,
                Assert.IsType<AnnotatedSourceDocument>(projection.SourceDocument),
                new InertString(TextPolicy.Field, "public void M()"),
                [identity, identity],
                new InertString(TextPolicy.Field, "test provenance"),
                contextLimitation: null));

        Assert.Contains("invalid or duplicate instance key", error.Message);
    }

    [Fact]
    public void Create_ValidatesCalleeEvidenceIdentityCoverageAndOutcome()
    {
        MemberProjectionResult projection = Project(
            new ResearchFactRegistry(
                new TestProducer(
                [
                    Finding(new Annotation(
                        new AnnotationDescriptor(
                            "safety.callee",
                            AnnotationCategory.Unsafety,
                            "callee safety"),
                        SourceOffset: 0)),
                ])));
        AnnotatedSourceFactIdentity identity = Assert.Single(
            Assert.IsAssignableFrom<
                IReadOnlyList<AnnotatedSourceFactIdentity>>(
                    projection.SourceDocumentFactIdentities));
        JsonElement calleeDocument = JsonSerializer.SerializeToElement(
            new AnnotatedSourceDocument(
                "stackalloc int[1]",
                [
                    new AnnotatedSourceNode(
                        0,
                        "StackAllocationExpression",
                        SourceLineKind.CSharp,
                        [new AnnotatedSourceSpan(0, 17)],
                        Provenance:
                            new AnnotatedSourceNodeProvenance([2])),
                ],
                [],
                [],
                []),
            AnnotatedSourceDocumentCompactJsonContext.Default
                .AnnotatedSourceDocument);
        var available = new BrowserAnnotatedSourceFindingEvidence(
            identity.FactId,
            identity.InstanceKey.Value,
            "Example.Targets.Target()",
            Target(),
            BrowserCalleeEvidenceState.Instruction,
            AggregateInputs: [],
            [new(2, BrowserCalleeEvidenceKind.Localloc)],
            DocumentId: 0,
            [0],
            UnavailableReason: null);

        BrowserMemberFindingCensus envelope = Create(
            projection,
            [available],
            [new(0, calleeDocument)]);
        Assert.Single(envelope.AnnotatedSource.FindingEvidence);
        Assert.Single(envelope.AnnotatedSource.FindingEvidenceDocuments);

        InvalidOperationException identityError =
            Assert.Throws<InvalidOperationException>(() =>
                Create(
                    projection,
                    [available with
                    {
                        InstanceKey = identity.InstanceKey.Value + 1,
                    }],
                    [new(0, calleeDocument)]));
        Assert.Contains("invalid or duplicate fact identity", identityError.Message);

        InvalidOperationException outcomeError =
            Assert.Throws<InvalidOperationException>(() =>
                Create(
                    projection,
                    [available with
                    {
                        DocumentId = null,
                    }],
                    []));
        Assert.Contains("requires a document", outcomeError.Message);

        InvalidOperationException staleUnavailableError =
            Assert.Throws<InvalidOperationException>(() =>
                Create(
                    projection,
                    [available with
                    {
                        NodeIds = [],
                        UnavailableReason =
                            "No unique callee source node.",
                    }],
                    [new(0, calleeDocument)]));
        Assert.Contains(
            "unavailable despite exact serialized correspondence",
            staleUnavailableError.Message);

        BrowserMemberFindingCensus unavailableEnvelope = Create(
            projection,
            [available with
            {
                Coordinates =
                [
                    new(
                        3,
                        BrowserCalleeEvidenceKind.Localloc),
                ],
                NodeIds = [],
                UnavailableReason =
                    "No unique callee source node.",
            }],
            [new(0, calleeDocument)]);
        Assert.Single(unavailableEnvelope.AnnotatedSource.FindingEvidence);

        JsonElement reverseOrderDocument = JsonSerializer.SerializeToElement(
            new AnnotatedSourceDocument(
                "stackalloc int[1]; stackalloc int[2]",
                [
                    new AnnotatedSourceNode(
                        0,
                        "StackAllocationExpression",
                        SourceLineKind.CSharp,
                        [new AnnotatedSourceSpan(0, 17)],
                        Provenance:
                            new AnnotatedSourceNodeProvenance([9])),
                    new AnnotatedSourceNode(
                        1,
                        "StackAllocationExpression",
                        SourceLineKind.CSharp,
                        [new AnnotatedSourceSpan(19, 17)],
                        Provenance:
                            new AnnotatedSourceNodeProvenance([2])),
                ],
                [],
                [],
                []),
            AnnotatedSourceDocumentCompactJsonContext.Default
                .AnnotatedSourceDocument);
        BrowserMemberFindingCensus reverseOrderEnvelope = Create(
            projection,
            [available with
            {
                Coordinates =
                [
                    new(2, BrowserCalleeEvidenceKind.Localloc),
                    new(9, BrowserCalleeEvidenceKind.Localloc),
                ],
                NodeIds = [0, 1],
            }],
            [new(0, reverseOrderDocument)]);
        Assert.Equal(
            [0, 1],
            Assert.Single(
                reverseOrderEnvelope.AnnotatedSource.FindingEvidence)
                .NodeIds);

        JsonElement sharedNodeDocument = JsonSerializer.SerializeToElement(
            new AnnotatedSourceDocument(
                "stackalloc int[1]",
                [
                    new AnnotatedSourceNode(
                        0,
                        "StackAllocationExpression",
                        SourceLineKind.CSharp,
                        [new AnnotatedSourceSpan(0, 17)],
                        Provenance:
                            new AnnotatedSourceNodeProvenance([2, 9])),
                ],
                [],
                [],
                []),
            AnnotatedSourceDocumentCompactJsonContext.Default
                .AnnotatedSourceDocument);
        BrowserMemberFindingCensus sharedNodeEnvelope = Create(
            projection,
            [available with
            {
                Coordinates =
                [
                    new(2, BrowserCalleeEvidenceKind.Localloc),
                    new(9, BrowserCalleeEvidenceKind.Localloc),
                ],
                NodeIds = [0],
            }],
            [new(0, sharedNodeDocument)]);
        Assert.Equal(
            [0],
            Assert.Single(
                sharedNodeEnvelope.AnnotatedSource.FindingEvidence)
                .NodeIds);

        InvalidOperationException offsetError =
            Assert.Throws<InvalidOperationException>(() =>
                Create(
                    projection,
                    [available with
                    {
                        Coordinates =
                        [
                            new(
                                3,
                                BrowserCalleeEvidenceKind.Localloc),
                        ],
                    }],
                    [new(0, calleeDocument)]));
        Assert.Contains("matches 0", offsetError.Message);

        JsonElement secondCalleeDocument = JsonSerializer.SerializeToElement(
            new AnnotatedSourceDocument(
                "stackalloc int[1]; stackalloc int[2]",
                [
                    new AnnotatedSourceNode(
                        0,
                        "StackAllocationExpression",
                        SourceLineKind.CSharp,
                        [new AnnotatedSourceSpan(0, 17)],
                        Provenance:
                            new AnnotatedSourceNodeProvenance([2])),
                    new AnnotatedSourceNode(
                        1,
                        "StackAllocationExpression",
                        SourceLineKind.CSharp,
                        [new AnnotatedSourceSpan(19, 17)],
                        Provenance:
                            new AnnotatedSourceNodeProvenance([9])),
                ],
                [],
                [],
                []),
            AnnotatedSourceDocumentCompactJsonContext.Default
                .AnnotatedSourceDocument);
        InvalidOperationException nodeIdentityError =
            Assert.Throws<InvalidOperationException>(() =>
                Create(
                    projection,
                    [available with
                    {
                        NodeIds = [1],
                    }],
                    [new(0, secondCalleeDocument)]));
        Assert.Contains("do not equal", nodeIdentityError.Message);

        InvalidOperationException coverageError =
            Assert.Throws<InvalidOperationException>(() =>
                Create(projection, [], []));
        Assert.Contains("does not cover every", coverageError.Message);
    }

    [Fact]
    public void Create_ValidatesMethodLevelCostEvidenceWithoutSourceCoordinates()
    {
        MemberProjectionResult projection = Project(
            new ResearchFactRegistry(
                new TestProducer(
                [
                    Finding(new Annotation(
                        new AnnotationDescriptor(
                            "cost.callee",
                            AnnotationCategory.Cost,
                            "callee cost"),
                        SourceOffset: 0)),
                ])));
        AnnotatedSourceFactIdentity identity = Assert.Single(
            Assert.IsAssignableFrom<
                IReadOnlyList<AnnotatedSourceFactIdentity>>(
                    projection.SourceDocumentFactIdentities));
        var methodEvidence = new BrowserAnnotatedSourceFindingEvidence(
            identity.FactId,
            identity.InstanceKey.Value,
            "Example.Targets.Target()",
            Target(),
            BrowserCalleeEvidenceState.Method,
            [
                new(
                    BrowserCostCalleeEvidenceInputKind.AllocationInLoop,
                    Value: null),
                new(
                    BrowserCostCalleeEvidenceInputKind.Reflection,
                    Value: 3),
            ],
            Coordinates: [],
            DocumentId: null,
            NodeIds: [],
            UnavailableReason: null);

        BrowserMemberFindingCensus envelope = Create(
            projection,
            [methodEvidence],
            []);

        BrowserAnnotatedSourceFindingEvidence projected = Assert.Single(
            envelope.AnnotatedSource.FindingEvidence);
        Assert.Equal(BrowserCalleeEvidenceState.Method, projected.State);
        Assert.Equal(2, projected.AggregateInputs.Length);
        Assert.Empty(projected.Coordinates);
        Assert.Null(projected.DocumentId);
        Assert.Empty(projected.NodeIds);

        InvalidOperationException missingInputs =
            Assert.Throws<InvalidOperationException>(() =>
                Create(
                    projection,
                    [methodEvidence with { AggregateInputs = [] }],
                    []));
        Assert.Contains("aggregate inputs", missingInputs.Message);

        InvalidOperationException inventedCoordinate =
            Assert.Throws<InvalidOperationException>(() =>
                Create(
                    projection,
                    [methodEvidence with
                    {
                        Coordinates =
                        [
                            new(
                                2,
                                BrowserCalleeEvidenceKind.Localloc),
                        ],
                    }],
                    []));
        Assert.Contains("instruction projection", inventedCoordinate.Message);
    }

    static BrowserMemberFindingCensus Create(
        MemberProjectionResult projection)
        => BrowserMemberFindingCensus.Create(
            projection.FactCensusReceipt,
            projection.Facts,
            Assert.IsType<AnnotatedSourceDocument>(projection.SourceDocument),
            new InertString(TextPolicy.Field, "public void M()"),
            projection.SourceDocumentFactIdentities,
            new InertString(TextPolicy.Field, "test provenance"),
            contextLimitation: null);

    static BrowserMemberFindingCensus Create(
        MemberProjectionResult projection,
        BrowserAnnotatedSourceFindingEvidence[] findingEvidence,
        BrowserAnnotatedSourceFindingEvidenceDocument[]
            findingEvidenceDocuments)
        => BrowserMemberFindingCensus.Create(
            projection.FactCensusReceipt,
            projection.Facts,
            Assert.IsType<AnnotatedSourceDocument>(projection.SourceDocument),
            new InertString(TextPolicy.Field, "public void M()"),
            projection.SourceDocumentFactIdentities,
            new InertString(TextPolicy.Field, "test provenance"),
            contextLimitation: null,
            findingEvidenceDocuments: findingEvidenceDocuments,
            findingEvidence: findingEvidence);

    static BrowserCallGraphTarget Target() =>
        new(
            "method:target",
            "Example",
            "1.0.0.0",
            AssemblyCulture: null,
            AssemblyPublicKeyToken: null,
            "Example.Targets",
            "Example.Targets",
            "Example.Targets",
            "Target",
            [],
            "System.Void",
            GenericArity: 0,
            MetadataToken: 0x06000001,
            "selector:target",
            "method",
            PlatformPack: null,
            "compile:ref/net11.0/Example.dll");

    static AnnotatedSourceDocument CalleeDocument() =>
        new(
            "stackalloc int[1]",
            [
                new AnnotatedSourceNode(
                    0,
                    "StackAllocationExpression",
                    SourceLineKind.CSharp,
                    [new AnnotatedSourceSpan(0, 17)],
                    Provenance:
                        new AnnotatedSourceNodeProvenance([2])),
            ],
            [],
            [],
            []);

    static MethodIdentity Method(Guid moduleVersionId) =>
        new(
            "Example",
            moduleVersionId,
            ILInspector.Analysis.TypeRef.Definition(
                "Example",
                "Example",
                "Targets"),
            "Target",
            [],
            ILInspector.Analysis.TypeRef.CoreLib("System", "Void"),
            0x06000001,
            IsStatic: true);

    static AssemblyMemberFindingEvidence Evidence(
        int factId,
        MethodIdentity member,
        AnnotatedSourceDocument? document) =>
        new(
            factId,
            default,
            member,
            ResearchFindingEvidenceState.Instruction,
            AggregateInputs: [],
            Coordinates: [],
            document,
            NodeIds: [],
            UnavailableReason: "test");

    static MemberProjectionResult Project(
        ResearchFactRegistry registry)
    {
        using MetadataSource source = MetadataSource.Open(
            typeof(BrowserMemberFindingCensusTests).Assembly.Location);
        return MemberProjectionProducer.Produce(
            new MemberProjectionRequest(
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
