using System.Reflection;
using System.Text.Json;
using DotnetInspector.Presentation;
using DotnetInspector.Queries;
using DotnetInspector.ResearchSections;
using ILInspector.Metadata;
using ILInspector.Research;
using Inspector.Findings;

namespace DotnetInspector.Sections.Tests;

public sealed class AnalysisParticipationRegistrationTests
{
    static InspectionCapabilityCatalog ProductCatalog()
        => InspectionCapabilityCatalog.Create([DiffAnalysisCatalog.ProductModule]);

    [Fact]
    public void AnalysisIdentity_ConformsToGrammarAndIsUniquePerBuild()
    {
        InspectionCapabilityCatalog catalog = ProductCatalog();
        AnalysisDescriptor[] participating =
        [
            .. catalog.Analyses
                .Select(registration => registration.Analysis)
                .Where(analysis => !analysis.Participations.IsEmpty),
        ];

        Assert.Equal(
            ["api", "api-attribute", "allocation", "call-site", "unsafety", "csharp", "il"],
            participating.Select(analysis => analysis.Id.Value));
        Assert.All(
            participating,
            analysis => Assert.True(AnalysisIdentity.IsValid(analysis.Id.Value), analysis.Id.Value));
        Assert.Equal(
            participating.Length,
            participating.Select(analysis => analysis.Id.Value).Distinct(StringComparer.Ordinal).Count());

        // One Finding descriptor maps to exactly one analysis per surface.
        foreach (var group in participating
            .SelectMany(analysis => analysis.Participations.SelectMany(participation =>
                participation.Surfaces.SelectMany(surface => surface.Descriptors.Select(
                    descriptor => (surface.Surface, descriptor.Id, Analysis: analysis.Id.Value)))))
            .GroupBy(entry => (entry.Surface, entry.Id)))
        {
            Assert.Single(group.Select(entry => entry.Analysis).Distinct());
        }

        // The legacy Integration descriptor enters the grammar with Graph.
        Assert.False(AnalysisIdentity.IsValid(IntegrationAnalysisCatalog.Analysis.Id.Value));
        Assert.True(IntegrationAnalysisCatalog.Analysis.Participations.IsEmpty);
    }

    [Fact]
    public void AnalysisParticipation_CompareBindsKeyedFindingComparisonProducer()
    {
        InspectionCapabilityCatalog catalog = ProductCatalog();

        foreach (InspectionAnalysisRegistration registration in catalog.Analyses)
        {
            AnalysisOperationParticipation participation = Assert.IsType<AnalysisOperationParticipation>(
                registration.Analysis.ParticipationFor(AnalysisOperationKind.Compare));
            foreach (AnalysisSurfaceParticipation surface in participation.Surfaces)
            {
                InspectionAnalysisProducerBinding producer = Assert.IsType<InspectionAnalysisProducerBinding>(
                    registration.ProducerFor(AnalysisOperationKind.Compare, surface.Surface),
                    exactMatch: false);
                Assert.Same(surface, producer.Participation);
                Assert.Equal(typeof(DiffAnalysisProducerContext), producer.InputType);
                Assert.Equal(typeof(DiffAnalysisProduction), producer.ResultType);
            }
        }

        // Structurally, a Compare production carries only the closed keyed
        // comparison family: Metadata's API comparison or Research's
        // descriptor-keyed retained set. Neither union is openable.
        Assert.Equal(
            [typeof(KeyedFindingComparison)],
            typeof(DiffAnalysisProduction.ComparedProduction)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.PropertyType));
        Assert.Equal(
            [typeof(KeyedFindingComparison.Api), typeof(KeyedFindingComparison.Retained)],
            typeof(KeyedFindingComparison)
                .GetNestedTypes()
                .Where(type => type.IsSubclassOf(typeof(KeyedFindingComparison)))
                .OrderBy(type => type.Name, StringComparer.Ordinal));
        Assert.Equal(
            typeof(ApiFindingComparison),
            typeof(KeyedFindingComparison.Api).GetProperty("Comparison")!.PropertyType);
        Assert.Equal(
            typeof(RetainedFindingComparisonSet),
            typeof(KeyedFindingComparison.Retained).GetProperty("Comparisons")!.PropertyType);
        Assert.All(
            typeof(KeyedFindingComparison).GetConstructors(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
            constructor => Assert.True(constructor.IsPrivate));
    }

    [Fact]
    public void AnalysisParticipation_DiscoveryAndDispatchShareOneRegistration()
    {
        InspectionCapabilityCatalog catalog = ProductCatalog();
        ResourceExplanationCatalog explanation =
            ResourceExplanationCatalog.CreateAnalyses(catalog);

        // Explanation lists exactly the registered analyses, by identity.
        Assert.Equal(
            catalog.Analyses.Select(registration => $"analyses/{registration.Analysis.Id.Value}"),
            explanation.Resources
                .Where(resource => resource.ResourceKind == ResourceExplanationResourceKind.Analysis)
                .Select(resource => resource.Path.Value));
        var resolved = Assert.IsType<ResourcePathResolution.Resolved>(
            explanation.Resolve("analyses/call-site"));
        ResourceExplanationDocument document = explanation.Explain(
            resolved,
            new ResourceExplanationRequest(0, 16, 16)).Content;
        Assert.Contains(
            document.Relationships,
            relationship => relationship.RelationshipKind == ResourceExplanationRelationshipKind.Issues
                && relationship.Target is ResourceExplanationIdentity.IssuedFinding
                {
                    Operation: "Compare",
                    Surface: "Member",
                    Descriptor: "analysis.call-site",
                });

        // Validation and dispatch use the same descriptor instances.
        var accepted = Assert.IsType<AnalysisSetValidationResult.Accepted>(
            catalog.AnalysisCapabilities.ValidateSet(
                DiffAnalysisCatalog.Operation,
                AnalysisReportSurfaceKind.Library,
                targetCount: 1,
                requested: null));
        AnalysisDescriptor api = Assert.Single(accepted.Analyses);
        Assert.Same(
            catalog.Analyses.Single(registration => registration.Analysis.Id.Value == "api").Analysis,
            api);
        DiffAnalysisResult result = DiffAnalysisOperation.Execute(
            catalog,
            accepted,
            Input(prepareBodySignals: null));
        DiffAnalysisOutcome outcome = Assert.Single(result.Outcomes);
        Assert.Same(api, outcome.Analysis);
        var compared = Assert.IsType<DiffAnalysisOutcome.Compared>(outcome);
        Assert.IsType<KeyedFindingComparison.Api>(compared.Comparison);
        Assert.Equal(
            ["api.type", "api.member"],
            outcome.Participation.Descriptors.Select(descriptor => descriptor.Id));
    }

    [Fact]
    public void AnalysisSet_RejectedSetExecutesNoProducer()
    {
        InspectionCapabilityCatalog catalog = ProductCatalog();
        int prepared = 0;

        AnalysisSetValidationResult validation =
            catalog.AnalysisCapabilities.ValidateSet(
                DiffAnalysisCatalog.Operation,
                AnalysisReportSurfaceKind.Member,
                targetCount: 1,
                ["call-site", "nope", "call-site"]);

        var rejected = Assert.IsType<AnalysisSetValidationResult.Rejected>(validation);
        Assert.Equal(2, rejected.Rejections.Length);
        Assert.Equal(0, prepared);

        var accepted = Assert.IsType<AnalysisSetValidationResult.Accepted>(
            catalog.AnalysisCapabilities.ValidateSet(
                DiffAnalysisCatalog.Operation,
                AnalysisReportSurfaceKind.Member,
                targetCount: 1,
                ["allocation", "call-site"]));
        DiffAnalysisResult result = DiffAnalysisOperation.Execute(
            catalog,
            accepted,
            Input(descriptors =>
            {
                prepared++;
                Assert.Equal(
                    ["analysis.allocation", "analysis.call-site"],
                    descriptors.Select(descriptor => descriptor.Id));
                return new ResearchComparison([]);
            }));

        // One shared body-signal preparation serves every selected body analysis.
        Assert.Equal(1, prepared);
        Assert.Equal(
            ["allocation", "call-site"],
            result.Outcomes.Select(outcome => outcome.Identity));
        Assert.All(result.Outcomes, outcome => Assert.IsType<DiffAnalysisOutcome.Compared>(outcome));
    }

    [Fact]
    public void AnalysisSet_TargetFailureIsRequestFailureBeforeAnyProducerOutcome()
    {
        InspectionCapabilityCatalog catalog = ProductCatalog();
        var accepted = Assert.IsType<AnalysisSetValidationResult.Accepted>(
            catalog.AnalysisCapabilities.ValidateSet(
                DiffAnalysisCatalog.Operation,
                AnalysisReportSurfaceKind.Member,
                targetCount: 1,
                ["api", "allocation"]));

        var error = Assert.Throws<DiffAnalysisTargetException>(() =>
            DiffAnalysisOperation.Execute(
                catalog,
                accepted,
                Input(_ => throw new DiffAnalysisTargetException("Member target 'Run' did not resolve."))));

        Assert.Contains("did not resolve", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalysisSet_SharedBodyPreparationFailureFailsOnlyBodyAnalyses()
    {
        InspectionCapabilityCatalog catalog = ProductCatalog();
        var accepted = Assert.IsType<AnalysisSetValidationResult.Accepted>(
            catalog.AnalysisCapabilities.ValidateSet(
                DiffAnalysisCatalog.Operation,
                AnalysisReportSurfaceKind.Member,
                targetCount: 1,
                ["api", "call-site", "allocation"]));

        // A non-target shared preparation failure, such as admission
        // rejecting the body-signal inputs, is each body analysis's own
        // producer failure. It does not erase the api outcome.
        DiffAnalysisResult result = DiffAnalysisOperation.Execute(
            catalog,
            accepted,
            Input(_ => throw new InvalidOperationException(
                "body-signal inputs were not admitted (Rejected).")));

        Assert.Equal(
            ["api", "call-site", "allocation"],
            result.Outcomes.Select(outcome => outcome.Analysis.Id.Value));
        Assert.IsType<DiffAnalysisOutcome.Compared>(result.Outcomes[0]);
        Assert.All(
            result.Outcomes.Skip(1),
            outcome => Assert.Contains(
                "were not admitted",
                Assert.IsType<DiffAnalysisOutcome.Failed>(outcome).Diagnostic,
                StringComparison.Ordinal));
    }

    [Fact]
    public void DiffAnalysisInspection_ProjectsSelectedViewsFromOneExecution()
    {
        InspectionCapabilityCatalog catalog = ProductCatalog();
        var accepted = Assert.IsType<AnalysisSetValidationResult.Accepted>(
            catalog.AnalysisCapabilities.ValidateSet(
                DiffAnalysisCatalog.Operation,
                AnalysisReportSurfaceKind.Member,
                targetCount: 1,
                ["allocation", "call-site"]));
        int prepared = 0;
        var input = new DiffAnalysisInput(
            new ApiSurface(),
            new ApiSurface(),
            [],
            [],
            new HashSet<string>(),
            ["Sample.Widget"],
            new HashSet<string>(),
            descriptors =>
            {
                prepared++;
                Assert.Equal(
                    ["analysis.allocation", "analysis.call-site"],
                    descriptors.Select(descriptor => descriptor.Id));
                return new ResearchComparison([]);
            });

        InspectionEnvelope<DiffAnalysisDocument> inspection =
            DiffAnalysisInspection.Execute(
                new DiffAnalysisInspectionRequest(
                    "Sample",
                    "1.0.0",
                    "2.0.0",
                    catalog,
                    accepted,
                    input,
                    DiffAnalysisDocumentViews.Summary));

        Assert.Equal(1, prepared);
        Assert.Equal(
            ["allocation", "call-site"],
            inspection.Content.Comparison.Analyses);
        Assert.Equal(
            DiffAnalysisDocumentViews.Summary,
            inspection.Content.Comparison.Views);
        Assert.Equal(2, inspection.Content.Outcomes.Length);
        Assert.Equal(2, inspection.Content.Summary?.Length);
        Assert.Null(inspection.Content.Changes);
        Assert.Null(inspection.Content.Transitions);
        string json = JsonSerializer.Serialize(
            inspection.Content,
            DiffAnalysisInspectionJsonContext.Default.DiffAnalysisDocument);
        using var parsed = JsonDocument.Parse(json);
        Assert.True(parsed.RootElement.TryGetProperty("summary", out _));
        Assert.False(parsed.RootElement.TryGetProperty("changes", out _));
        Assert.False(parsed.RootElement.TryGetProperty("transitions", out _));
        Assert.False(parsed.RootElement.TryGetProperty("libraryApi", out _));
    }

    [Fact]
    public void DiffAnalysisOperation_HostUnavailableBodyAnalysesSkipPreparation()
    {
        InspectionCapabilityCatalog catalog = ProductCatalog();
        var accepted = Assert.IsType<AnalysisSetValidationResult.Accepted>(
            catalog.AnalysisCapabilities.ValidateSet(
                DiffAnalysisCatalog.Operation,
                AnalysisReportSurfaceKind.Member,
                targetCount: 1,
                ["allocation", "call-site", "unsafety"]));
        int prepared = 0;
        var input = new DiffAnalysisInput(
            new ApiSurface(),
            new ApiSurface(),
            [],
            [],
            new HashSet<string>(),
            ["Sample.Widget"],
            new HashSet<string>(),
            _ =>
            {
                prepared++;
                throw new InvalidOperationException(
                    "Unavailable analyses must not prepare body signals.");
            },
            hostUnavailability:
            [
                new(new AnalysisDeclarationId("allocation"),
                    "Browser/Wasm cannot construct method bodies."),
                new(new AnalysisDeclarationId("call-site"),
                    "Browser/Wasm cannot construct method bodies."),
                new(new AnalysisDeclarationId("unsafety"),
                    "Browser/Wasm cannot construct method bodies."),
            ]);

        DiffAnalysisResult result =
            DiffAnalysisOperation.Execute(catalog, accepted, input);

        Assert.Equal(0, prepared);
        Assert.Equal(
            ["allocation", "call-site", "unsafety"],
            result.Outcomes.Select(outcome => outcome.Identity));
        Assert.All(
            result.Outcomes,
            outcome => Assert.Equal(
                "Browser/Wasm cannot construct method bodies.",
                Assert.IsType<DiffAnalysisOutcome.Unavailable>(outcome).Reason));
    }

    [Fact]
    public void DiffAnalysisInspection_SerializesOwnerIssuedLibraryApiOutcome()
    {
        InspectionCapabilityCatalog catalog = ProductCatalog();
        var accepted = Assert.IsType<AnalysisSetValidationResult.Accepted>(
            catalog.AnalysisCapabilities.ValidateSet(
                DiffAnalysisCatalog.Operation,
                AnalysisReportSurfaceKind.Library,
                targetCount: 1,
                ["api"]));
        var identity = new AssemblyReferenceIdentity(
            "Sample",
            new Version(1, 0, 0, 0),
            null,
            null);
        var endpoint = new LibraryApiDiffEndpointSummary(
            identity,
            ApiSurfaceScope.Public,
            IsComplete: true,
            []);
        var libraryApi = new LibraryApiDiffOutcome.Rejected(
            LibraryApiDiffRejectionKind.LogicalLibraryMismatch,
            endpoint,
            endpoint);
        var input = new DiffAnalysisInput(
            new ApiSurface(),
            new ApiSurface(),
            [],
            [],
            new HashSet<string>(),
            [],
            memberTargetIdentities: null,
            prepareBodySignals: null);

        InspectionEnvelope<DiffAnalysisDocument> inspection =
            DiffAnalysisInspection.Execute(
                new DiffAnalysisInspectionRequest(
                    "Sample",
                    "1.0.0",
                    "2.0.0",
                    catalog,
                    accepted,
                    input,
                    DiffAnalysisDocumentViews.Changes,
                    libraryApi));

        string json = JsonSerializer.Serialize(
            inspection.Content,
            DiffAnalysisInspectionJsonContext.Default.DiffAnalysisDocument);
        using var parsed = JsonDocument.Parse(json);
        JsonElement embedded = parsed.RootElement.GetProperty("libraryApi");
        Assert.Equal("rejected", embedded.GetProperty("outcome").GetString());
        Assert.Equal(
            (int)LibraryApiDiffRejectionKind.LogicalLibraryMismatch,
            embedded.GetProperty("kind").GetInt32());
        Assert.Equal(
            "Sample",
            embedded
                .GetProperty("before")
                .GetProperty("identity")
                .GetProperty("name")
                .GetString());
    }

    [Fact]
    public void DiffAnalysisInspection_RetainsApiFailuresWithoutChangesView()
    {
        InspectionCapabilityCatalog catalog = ProductCatalog();
        var accepted = Assert.IsType<AnalysisSetValidationResult.Accepted>(
            catalog.AnalysisCapabilities.ValidateSet(
                DiffAnalysisCatalog.Operation,
                AnalysisReportSurfaceKind.Type,
                targetCount: 1,
                ["api"]));
        var before = new ApiSurface();
        before.InspectionFailures.Add(new ApiSurfaceInspectionFailure(
            "resolve malformed AssemblyRef",
            0x23000001,
            MetadataTypeNameFailureMechanism.Metadata,
            "InvalidAssemblyReference",
            "invalid AssemblyRef row"));
        var after = new ApiSurface();
        after.InspectionFailures.Add(new ApiSurfaceInspectionFailure(
            "resolve malformed AssemblyRef",
            0x23000001,
            MetadataTypeNameFailureMechanism.Metadata,
            "InvalidAssemblyReference",
            "invalid AssemblyRef row"));
        var input = new DiffAnalysisInput(
            before,
            after,
            [],
            [],
            new HashSet<string>(["N.Healthy"]),
            ["N.Healthy"],
            memberTargetIdentities: null,
            prepareBodySignals: null);

        InspectionEnvelope<DiffAnalysisDocument> inspection =
            DiffAnalysisInspection.Execute(
                new DiffAnalysisInspectionRequest(
                    "Sample",
                    "1.0.0",
                    "2.0.0",
                    catalog,
                    accepted,
                    input,
                    DiffAnalysisDocumentViews.Transitions));

        Assert.Null(inspection.Content.Changes);
        Assert.NotEmpty(inspection.Content.ApiInspectionFailures);
        Assert.All(
            inspection.Content.ApiInspectionFailures,
            failure => Assert.Contains(
                "invalid AssemblyRef row",
                failure.Detail,
                StringComparison.Ordinal));
        string json = JsonSerializer.Serialize(
            inspection.Content,
            DiffAnalysisInspectionJsonContext.Default.DiffAnalysisDocument);
        using var parsed = JsonDocument.Parse(json);
        Assert.True(
            parsed.RootElement.TryGetProperty(
                "apiInspectionFailures",
                out JsonElement failures));
        Assert.NotEmpty(failures.EnumerateArray());
        Assert.False(parsed.RootElement.TryGetProperty("changes", out _));
    }

    [Fact]
    public void DiffAnalysisInspection_RetainsUnclassifiedApiChanges()
    {
        InspectionCapabilityCatalog catalog = ProductCatalog();
        var accepted = Assert.IsType<AnalysisSetValidationResult.Accepted>(
            catalog.AnalysisCapabilities.ValidateSet(
                DiffAnalysisCatalog.Operation,
                AnalysisReportSurfaceKind.Type,
                targetCount: 1,
                ["api"]));
        var before = new ApiSurface
        {
            Types =
            [
                new ApiType
                {
                    Namespace = "N",
                    Name = "Widget",
                    Kind = "struct",
                },
            ],
        };
        var after = new ApiSurface
        {
            Types =
            [
                new ApiType
                {
                    Namespace = "N",
                    Name = "Widget",
                    Kind = "struct",
                    IsByRefLike = true,
                },
            ],
        };
        var input = new DiffAnalysisInput(
            before,
            after,
            [],
            [],
            new HashSet<string>(["N.Widget"]),
            ["N.Widget"],
            memberTargetIdentities: null,
            prepareBodySignals: null);

        InspectionEnvelope<DiffAnalysisDocument> inspection =
            DiffAnalysisInspection.Execute(
                new DiffAnalysisInspectionRequest(
                    "Sample",
                    "1.0.0",
                    "2.0.0",
                    catalog,
                    accepted,
                    input,
                    DiffAnalysisDocumentViews.Changes));

        DiffAnalysisChangedType type = Assert.Single(
            inspection.Content.Changes!.Types);
        Assert.Empty(type.Changes);
        DiffAnalysisUnclassifiedApiChange change = Assert.Single(
            type.UnclassifiedChanges);
        Assert.Equal(
            DiffAnalysisUnclassifiedApiChangeKind.TypeDefinitionChanged,
            change.Kind);
    }

    [Fact]
    public void DiffAnalysisInspection_MemberScopeExcludesTypeDefinitionChanges()
    {
        InspectionCapabilityCatalog catalog = ProductCatalog();
        var accepted = Assert.IsType<AnalysisSetValidationResult.Accepted>(
            catalog.AnalysisCapabilities.ValidateSet(
                DiffAnalysisCatalog.Operation,
                AnalysisReportSurfaceKind.Member,
                targetCount: 1,
                ["api"]));
        var beforeMember = new ApiMember
        {
            Name = "Value",
            Kind = "field",
            ReturnType = "System.Int32",
        };
        var afterMember = new ApiMember
        {
            Name = "Value",
            Kind = "field",
            ReturnType = "System.Int32",
        };
        var beforeType = new ApiType
        {
            Namespace = "N",
            Name = "Widget",
            Kind = "struct",
            Members = [beforeMember],
        };
        var afterType = new ApiType
        {
            Namespace = "N",
            Name = "Widget",
            Kind = "struct",
            IsByRefLike = true,
            Members = [afterMember],
        };
        string memberIdentity =
            ApiMemberIdentity.CreateHandle(beforeType, beforeMember).Identity;
        var input = new DiffAnalysisInput(
            new ApiSurface { Types = [beforeType] },
            new ApiSurface { Types = [afterType] },
            [],
            [],
            new HashSet<string>(["N.Widget"]),
            ["N.Widget"],
            new HashSet<string>([memberIdentity]),
            prepareBodySignals: null);

        InspectionEnvelope<DiffAnalysisDocument> inspection =
            DiffAnalysisInspection.Execute(
                new DiffAnalysisInspectionRequest(
                    "Sample",
                    "1.0.0",
                    "2.0.0",
                    catalog,
                    accepted,
                    input,
                    DiffAnalysisDocumentViews.Changes));

        Assert.Empty(inspection.Content.Changes!.Types);
    }

    [Fact]
    public void DiffAnalysisInspection_WholeTypeChangesSubsumeMemberChanges()
    {
        InspectionCapabilityCatalog catalog = ProductCatalog();
        var accepted = Assert.IsType<AnalysisSetValidationResult.Accepted>(
            catalog.AnalysisCapabilities.ValidateSet(
                DiffAnalysisCatalog.Operation,
                AnalysisReportSurfaceKind.Library,
                targetCount: 1,
                ["api"]));
        var before = new ApiSurface
        {
            Types =
            [
                new ApiType
                {
                    Namespace = "N",
                    Name = "Removed",
                    Kind = "class",
                    Members =
                    [
                        new ApiMember
                        {
                            Name = "Run",
                            Kind = "method",
                            Signature = "void Run()",
                        },
                    ],
                },
            ],
        };
        var after = new ApiSurface
        {
            Types =
            [
                new ApiType
                {
                    Namespace = "N",
                    Name = "Added",
                    Kind = "class",
                    Members =
                    [
                        new ApiMember
                        {
                            Name = "Run",
                            Kind = "method",
                            Signature = "void Run()",
                        },
                    ],
                },
            ],
        };
        var input = new DiffAnalysisInput(
            before,
            after,
            [],
            [],
            new HashSet<string>(),
            [],
            memberTargetIdentities: null,
            prepareBodySignals: null);

        InspectionEnvelope<DiffAnalysisDocument> inspection =
            DiffAnalysisInspection.Execute(
                new DiffAnalysisInspectionRequest(
                    "Sample",
                    "1.0.0",
                    "2.0.0",
                    catalog,
                    accepted,
                    input,
                    DiffAnalysisDocumentViews.Changes));

        Assert.Collection(
            inspection.Content.Changes!.Types,
            type =>
            {
                Assert.Equal("N.Added", type.Type);
                Assert.Equal(
                    ChangeKind.TypeAdded,
                    Assert.Single(type.Changes).Kind);
                Assert.Empty(type.UnclassifiedChanges);
            },
            type =>
            {
                Assert.Equal("N.Removed", type.Type);
                Assert.Equal(
                    ChangeKind.TypeRemoved,
                    Assert.Single(type.Changes).Kind);
                Assert.Empty(type.UnclassifiedChanges);
            });
    }

    static DiffAnalysisInput Input(
        Func<IReadOnlyList<FindingDescriptor>, ResearchComparison>? prepareBodySignals)
        => new(
            new ApiSurface(),
            new ApiSurface(),
            [],
            [],
            new HashSet<string>(),
            [],
            memberTargetIdentities: null,
            prepareBodySignals);
}
