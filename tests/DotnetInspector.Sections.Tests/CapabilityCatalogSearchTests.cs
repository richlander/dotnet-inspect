using DotnetInspector.Queries;
using DotnetInspector.Sections;
using QuerySpace;
using QuerySpace.Composition;
using QuerySpace.Operations;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace DotnetInspector.Sections.Tests;

public sealed class CapabilityCatalogSearchTests
{
    [Fact]
    public void Literal_FindsTheExposedLibraryLiteralFacet()
    {
        (InspectionCapabilityCatalog catalog,
            ResourceExplanationCatalog explanation) = CreateCatalog();

        InspectionEnvelope<CapabilityCatalogSearchDocument> envelope =
            CapabilityCatalogSearch.Search(
                catalog,
                explanation,
                new("literal"));

        CapabilityCatalogSearchResult result = envelope.Content.Results[0];
        Assert.Equal(1.0, result.Similarity);
        Assert.Equal("literal", result.MatchedTerm);
        Assert.Equal(
            CapabilityCatalogSearchMatchSource.CanonicalKey,
            result.MatchSource);
        Assert.True(result.IsSegment);
        Assert.Equal(
            InspectionCapabilityResourceKind.QueryFacet,
            result.ResourceIdentity.Kind);
        Assert.Equal(["library-literal"], result.CanonicalKeys);
        Assert.Equal(
            "package-query/query/facets/library-literal",
            result.ResourcePath);
        Assert.Equal(
            PackageQueryCapability.Route.Descriptor.Identity,
            Assert.Single(result.OwningRoutes).Identity);
        Assert.Equal(
            "test/cli/package-query",
            Assert.Single(result.ProductionBindings).Identity);
        Assert.IsType<InspectionShare.NonProjectable>(envelope.Share);
        Assert.Empty(envelope.Diagnostics);
    }

    [Fact]
    public void Misspelling_UsesTheExistingSimilarityModel()
    {
        (InspectionCapabilityCatalog catalog,
            ResourceExplanationCatalog explanation) = CreateCatalog();

        CapabilityCatalogSearchResult result =
            CapabilityCatalogSearch.Search(
                    catalog,
                    explanation,
                    new("litteral"))
                .Content
                .Results[0];

        Assert.Equal("literal", result.MatchedTerm);
        Assert.Equal(0.875, result.Similarity);
        Assert.Equal(
            "package-query/query/facets/library-literal",
            result.ResourcePath);
    }

    [Fact]
    public void Search_IsIndependentOfModuleRegistrationOrder()
    {
        InspectionConsumerBinding browser = CreateBinding(
            "test/browser/package-query",
            InspectionConsumerKind.Browser,
            "Browser package query");
        InspectionConsumerBinding cli = CreateBinding(
            "test/cli/package-query",
            InspectionConsumerKind.Cli,
            "package query");
        InspectionCapabilityModule cliModule =
            new("test/cli", bindings: [cli]);
        InspectionCapabilityModule browserModule =
            new("test/browser", bindings: [browser]);

        InspectionEnvelope<CapabilityCatalogSearchDocument> first =
            Search(
                [
                    PackageQueryCapability.ProductModule,
                    cliModule,
                    browserModule,
                ],
                "literal");
        InspectionEnvelope<CapabilityCatalogSearchDocument> second =
            Search(
                [
                    browserModule,
                    PackageQueryCapability.ProductModule,
                    cliModule,
                ],
                "literal");

        Assert.Equal(
            JsonSerializer.Serialize(
                first,
                CapabilityCatalogSearchJsonContext.Default
                    .InspectionEnvelopeCapabilityCatalogSearchDocument),
            JsonSerializer.Serialize(
                second,
                CapabilityCatalogSearchJsonContext.Default
                    .InspectionEnvelopeCapabilityCatalogSearchDocument));
        Assert.Equal(
            [
                "test/browser/package-query",
                "test/cli/package-query",
            ],
            first.Content.Results[0].ProductionBindings
                .Select(static binding => binding.Identity));
    }

    [Fact]
    public void EqualScores_PreferStrongerProvenance()
    {
        (InspectionCapabilityCatalog catalog,
            ResourceExplanationCatalog explanation) = CreateCatalog();

        CapabilityCatalogSearchResult[] results =
        [
            .. CapabilityCatalogSearch.Search(
                    catalog,
                    explanation,
                    new("literal"))
                .Content
                .Results
                .Where(static result => result.Similarity == 1.0),
        ];

        Assert.Equal(2, results.Length);
        Assert.Equal(
            CapabilityCatalogSearchMatchSource.CanonicalKey,
            results[0].MatchSource);
        Assert.Equal(
            CapabilityCatalogSearchMatchSource.Summary,
            results[1].MatchSource);
    }

    [Fact]
    public void EqualScores_PreferCompleteTermsBeforeCanonicalPath()
    {
        (InspectionCapabilityCatalog catalog,
            ResourceExplanationCatalog explanation) =
                CreateRankingCatalog(
                    ("alpha", "ranking/z-complete"),
                    ("group/alpha", "ranking/a-segment"));

        CapabilityCatalogSearchResult[] documents =
        [
            .. CapabilityCatalogSearch.Search(
                    catalog,
                    explanation,
                    new("alpha"))
                .Content
                .Results
                .Where(static result =>
                    result.ResourceKind
                        == ResourceExplanationResourceKind
                            .InspectionDocument),
        ];

        Assert.Equal(2, documents.Length);
        Assert.Equal("ranking/z-complete", documents[0].ResourcePath);
        Assert.False(documents[0].IsSegment);
        Assert.Equal("ranking/a-segment", documents[1].ResourcePath);
        Assert.True(documents[1].IsSegment);
    }

    [Fact]
    public void EqualStrengthDuplicateTerms_PreferCompleteTerm()
    {
        (InspectionCapabilityCatalog catalog,
            ResourceExplanationCatalog explanation) =
                CreateTermCollisionCatalog();

        CapabilityCatalogSearchResult result =
            CapabilityCatalogSearch.Search(
                    catalog,
                    explanation,
                    new("alpha", maximumResults: 1))
                .Content
                .Results
                .Single();

        Assert.Equal(
            InspectionCapabilityResourceKind.QueryFacet,
            result.ResourceIdentity.Kind);
        Assert.Equal("alpha", result.ResourceIdentity.Identity);
        Assert.Equal(
            CapabilityCatalogSearchMatchSource.OwnerIdentity,
            result.MatchSource);
        Assert.False(result.IsSegment);
    }

    [Fact]
    public void EqualScores_UseCanonicalPathAsTheFinalTieBreaker()
    {
        (InspectionCapabilityCatalog catalog,
            ResourceExplanationCatalog explanation) =
                CreateRankingCatalog(
                    ("right/alpha", "ranking/z"),
                    ("left/alpha", "ranking/a"));

        CapabilityCatalogSearchResult[] documents =
        [
            .. CapabilityCatalogSearch.Search(
                    catalog,
                    explanation,
                    new("alpha"))
                .Content
                .Results
                .Where(static result =>
                    result.ResourceKind
                        == ResourceExplanationResourceKind
                            .InspectionDocument),
        ];

        Assert.Equal(
            ["ranking/a", "ranking/z"],
            documents.Select(static result => result.ResourcePath));
    }

    [Fact]
    public void OnlyFacetsExposedByAProductionBindingAreSearchable()
    {
        var binding =
            new InspectionConsumerBinding<
                PackageQueryInspectionRequest,
                PackageQueryDocument>(
                    new(
                        "test/cli/literal-only",
                        InspectionConsumerKind.Cli,
                        "test CLI",
                        "package query"),
                    PackageQueryCapability.Route,
                    [
                        PackageQuery.TermBindingIdentity(
                            PackageQuery.LibraryLiteralTermKey),
                    ]);
        InspectionCapabilityCatalog catalog =
            InspectionCapabilityCatalog.Create(
                [
                    PackageQueryCapability.ProductModule,
                    new("test/cli", bindings: [binding]),
                ]);
        ResourceExplanationCatalog explanation =
            ResourceExplanationCatalog.CreateCapabilities(
                catalog,
                PackageQueryCapabilityResourcePaths.Create(catalog));

        CapabilityCatalogSearchDocument document =
            CapabilityCatalogSearch.Search(
                    catalog,
                    explanation,
                    new("license"))
                .Content;

        Assert.DoesNotContain(
            document.Results,
            result => result.ResourcePath
                == "package-query/query/facets/license");
        Assert.Equal(
            catalog.Documents.Length
            + catalog.Routes.Length
            + 1
            + binding.ExposedQueryTerms.Length
            + catalog.Bindings.Length,
            document.CandidateResourceCount);
    }

    [Fact]
    public void Bound_IsAppliedAfterTheCompletePopulationIsScored()
    {
        (InspectionCapabilityCatalog catalog,
            ResourceExplanationCatalog explanation) = CreateCatalog();

        CapabilityCatalogSearchDocument document =
            CapabilityCatalogSearch.Search(
                    catalog,
                    explanation,
                    new("package", maximumResults: 1))
                .Content;

        Assert.True(document.MatchCount > 1);
        Assert.Equal(1, document.ReturnedCount);
        Assert.True(document.IsTruncated);
        Assert.Single(document.Results);
    }

    [Fact]
    public void NoMatches_ReturnsACompleteEmptyDocument()
    {
        (InspectionCapabilityCatalog catalog,
            ResourceExplanationCatalog explanation) = CreateCatalog();

        CapabilityCatalogSearchDocument document =
            CapabilityCatalogSearch.Search(
                    catalog,
                    explanation,
                    new("zzzzzzzzzzzzzzzz"))
                .Content;

        Assert.Equal(0, document.MatchCount);
        Assert.Equal(0, document.ReturnedCount);
        Assert.False(document.IsTruncated);
        Assert.Empty(document.Results);
    }

    [Fact]
    public void ResultPath_ResolvesToTheSameCapabilityIdentity()
    {
        (InspectionCapabilityCatalog catalog,
            ResourceExplanationCatalog explanation) = CreateCatalog();
        CapabilityCatalogSearchResult result =
            CapabilityCatalogSearch.Search(
                    catalog,
                    explanation,
                    new("literal"))
                .Content
                .Results[0];

        var resolved = Assert.IsType<ResourcePathResolution.Resolved>(
            explanation.Resolve(result.ResourcePath));
        var identity =
            Assert.IsType<ResourceExplanationIdentity.Capability>(
                resolved.Identity);
        Assert.Equal(result.ResourceIdentity, identity.Resource);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptySearchText_IsRejected(string text)
    {
        Assert.Throws<ArgumentException>(
            () => new CapabilityCatalogSearchRequest(text));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void InvalidResultBound_IsRejected(int maximumResults)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CapabilityCatalogSearchRequest(
                "literal",
                maximumResults));
    }

    [Fact]
    public void OversizedSearchText_IsRejected()
    {
        Assert.Throws<ArgumentException>(
            () => new CapabilityCatalogSearchRequest(
                new string('x', 129)));
    }

    private static (
        InspectionCapabilityCatalog Catalog,
        ResourceExplanationCatalog Explanation) CreateCatalog()
    {
        InspectionCapabilityCatalog catalog =
            InspectionCapabilityCatalog.Create(
                [
                    PackageQueryCapability.ProductModule,
                    new(
                        "test/cli",
                        bindings:
                        [
                            CreateBinding(
                                "test/cli/package-query",
                                InspectionConsumerKind.Cli,
                                "package query"),
                        ]),
                ]);
        return (
            catalog,
            ResourceExplanationCatalog.CreateCapabilities(
                catalog,
                PackageQueryCapabilityResourcePaths.Create(catalog)));
    }

    private static InspectionEnvelope<CapabilityCatalogSearchDocument> Search(
        IEnumerable<InspectionCapabilityModule> modules,
        string text)
    {
        InspectionCapabilityCatalog catalog =
            InspectionCapabilityCatalog.Create(modules);
        ResourceExplanationCatalog explanation =
            ResourceExplanationCatalog.CreateCapabilities(
                catalog,
                PackageQueryCapabilityResourcePaths.Create(catalog));
        return CapabilityCatalogSearch.Search(
            catalog,
            explanation,
            new(text));
    }

    private static (
        InspectionCapabilityCatalog Catalog,
        ResourceExplanationCatalog Explanation) CreateRankingCatalog(
            params (string Identity, string Path)[] documents)
    {
        var documentRegistrations =
            new List<InspectionDocumentRegistration<PackageQueryDocument>>();
        var routeRegistrations =
            new List<InspectionRouteRegistration<
                PackageQueryInspectionRequest,
                PackageQueryDocument>>();
        var bindings =
            new List<InspectionConsumerBinding<
                PackageQueryInspectionRequest,
                PackageQueryDocument>>();
        var paths =
            new List<InspectionCapabilityResourcePathRegistration>();

        for (int index = 0; index < documents.Length; index++)
        {
            (string identity, string path) = documents[index];
            var document =
                new InspectionDocumentRegistration<PackageQueryDocument>(
                    new(
                        identity,
                        $"Document {index}",
                        "Synthetic search-order resource.",
                        PackageQuery.ResultContractIdentity));
            var route =
                new InspectionRouteRegistration<
                    PackageQueryInspectionRequest,
                    PackageQueryDocument>(
                        new(
                            $"ranking/route/{index}",
                            $"Route {index}",
                            "Synthetic search-order route."),
                        document,
                        PackageQuery.QuerySpace,
                        static (_, _) =>
                            throw new InvalidOperationException(
                                "Capability search must not execute routes."));
            var binding =
                new InspectionConsumerBinding<
                    PackageQueryInspectionRequest,
                    PackageQueryDocument>(
                        new(
                            $"ranking/binding/{index}",
                            InspectionConsumerKind.Cli,
                            $"Binding {index}",
                            $"binding {index}"),
                        route,
                        []);

            documentRegistrations.Add(document);
            routeRegistrations.Add(route);
            bindings.Add(binding);
            paths.Add(
                new(
                    new(
                        InspectionCapabilityResourceKind.Document,
                        document.Descriptor.Identity),
                    new(path)));
            paths.Add(
                new(
                    new(
                        InspectionCapabilityResourceKind.Route,
                        route.Descriptor.Identity),
                    new($"ranking/routes/{index}")));
            paths.Add(
                new(
                    new(
                        InspectionCapabilityResourceKind.ConsumerBinding,
                        binding.Descriptor.Identity),
                    new($"ranking/bindings/{index}")));
        }

        paths.Add(
            new(
                new(
                    InspectionCapabilityResourceKind.QuerySpace,
                    PackageQuery.QuerySpace.Descriptor.Identity),
                new("ranking/query")));
        foreach (var term in PackageQuery.QuerySpace.Descriptor.Operation.Terms)
        {
            paths.Add(
                new(
                    new(
                        InspectionCapabilityResourceKind.QueryFacet,
                        term.Identity,
                        PackageQuery.QuerySpace.Descriptor.Identity),
                    new($"ranking/query/facets/{term.Key}")));
        }

        InspectionCapabilityCatalog catalog =
            InspectionCapabilityCatalog.Create(
                [
                    new(
                        "ranking",
                        documentRegistrations,
                        routeRegistrations,
                        bindings),
                ]);
        return (
            catalog,
            ResourceExplanationCatalog.CreateCapabilities(
                catalog,
                paths));
    }

    private static (
        InspectionCapabilityCatalog Catalog,
        ResourceExplanationCatalog Explanation) CreateTermCollisionCatalog()
    {
        const string querySpaceIdentity = "collision/query";
        const string routeIdentity = "collision/route";
        const string bindingIdentity = "collision/binding";
        QuerySpaceBinding querySpace =
            CreateTermCollisionQuerySpace(querySpaceIdentity);
        var document =
            new InspectionDocumentRegistration<PackageQueryDocument>(
                new(
                    "other/alpha",
                    "Collision document",
                    "Synthetic term-precedence resource.",
                    PackageQuery.ResultContractIdentity));
        var route =
            new InspectionRouteRegistration<
                PackageQueryInspectionRequest,
                PackageQueryDocument>(
                    new(
                        routeIdentity,
                        "Collision route",
                        "Synthetic term-precedence route."),
                    document,
                    querySpace,
                    static (_, _) =>
                        throw new InvalidOperationException(
                            "Capability search must not execute routes."));
        var binding =
            new InspectionConsumerBinding<
                PackageQueryInspectionRequest,
                PackageQueryDocument>(
                    new(
                        bindingIdentity,
                        InspectionConsumerKind.Cli,
                        "Collision binding",
                        "collision query"),
                    route,
                    ["alpha"]);
        InspectionCapabilityCatalog catalog =
            InspectionCapabilityCatalog.Create(
                [
                    new(
                        "collision",
                        [document],
                        [route],
                        [binding]),
                ]);
        var paths = new InspectionCapabilityResourcePathRegistration[]
        {
            new(
                new(
                    InspectionCapabilityResourceKind.Document,
                    document.Descriptor.Identity),
                new("collision/a-segment")),
            new(
                new(
                    InspectionCapabilityResourceKind.Route,
                    routeIdentity),
                new("collision/route")),
            new(
                new(
                    InspectionCapabilityResourceKind.ConsumerBinding,
                    bindingIdentity),
                new("collision/binding")),
            new(
                new(
                    InspectionCapabilityResourceKind.QuerySpace,
                    querySpaceIdentity),
                new("collision/query")),
            new(
                new(
                    InspectionCapabilityResourceKind.QueryFacet,
                    "alpha",
                    querySpaceIdentity),
                new("collision/query/facets/group-alpha")),
        };

        return (
            catalog,
            ResourceExplanationCatalog.CreateCapabilities(
                catalog,
                paths));
    }

    private static QuerySpaceBinding CreateTermCollisionQuerySpace(
        string identity)
    {
        const string subjectRole = "collision-subject";
        const string resultGrain = "collision-result";
        const string profileIdentity = "collision-profile";
        string rowSet =
            PackageQuery.QuerySpace.Descriptor.Operation.RowSets.Single();
        var term =
            new QueryOperationTermBinding(
                "alpha",
                "group-alpha",
                QueryOperationTermRole.OperationSelector,
                new(
                    [subjectRole],
                    [resultGrain],
                    []),
                new(
                    "alpha",
                    "text",
                    [],
                    "Synthetic colliding query facet."),
                []);
        QueryOperationDefinition<
            CollisionPredicate,
            CollisionPlan> operation =
                QueryOperationDefinition<
                    CollisionPredicate,
                    CollisionPlan>.Create(
                        "collision-operation",
                        new CollisionVocabulary(),
                        [subjectRole],
                        [resultGrain],
                        [rowSet],
                        [term],
                        [],
                        [new(profileIdentity, [term.Identity], [])]);
        QueryOperationRoute<
            CollisionPredicate,
            CollisionPlan> route =
                QueryOperationRoute<
                    CollisionPredicate,
                    CollisionPlan>.Create(
                        "collision-operation-route",
                        operation,
                        subjectRole,
                        resultGrain,
                        [rowSet],
                        profileIdentity,
                        [],
                        []);

        return QuerySpaceBinding.Create(
            identity,
            route,
            PackageQuery.QuerySpace.RowScopes,
            PackageQuery.QuerySpace.Descriptor.Terminals,
            PackageQuery.QuerySpace.Descriptor.AcceptsContinuation,
            PackageQuery.QuerySpace.Descriptor.ResultContracts);
    }

    private static InspectionConsumerBinding<
        PackageQueryInspectionRequest,
        PackageQueryDocument> CreateBinding(
            string identity,
            InspectionConsumerKind kind,
            string gesture) =>
        new(
            new(identity, kind, identity, gesture),
            PackageQueryCapability.Route,
            PackageQuery.InspectionTermBindingIdentities);

    private sealed record CollisionPredicate(string Value);

    private sealed record CollisionPlan(string Value);

    private sealed class CollisionDeclaration
        : PortableQueryKeyDeclaration<CollisionPredicate>
    {
        public override string Key => "group-alpha";

        public override bool AdmitsOperator(
            PortableQueryOperator @operator) =>
            @operator == PortableQueryOperator.Equal;

        public override PortableQueryBinding<CollisionPredicate> Bind(
            PortableQueryOperator @operator,
            string value) =>
            @operator == PortableQueryOperator.Equal
                ? PortableQueryBinding<CollisionPredicate>.Bound(
                    value,
                    new(value))
                : PortableQueryBinding<CollisionPredicate>.Rejected;
    }

    private sealed class CollisionVocabulary
        : PortableQueryVocabulary<CollisionPredicate, CollisionPlan>
    {
        private static readonly CollisionDeclaration Declaration = new();

        public override string Identity => "collision-vocabulary";

        public override bool TryGetKey(
            string key,
            [NotNullWhen(true)]
            out PortableQueryKeyDeclaration<CollisionPredicate>?
                declaration)
        {
            bool found = string.Equals(
                key,
                Declaration.Key,
                StringComparison.Ordinal);
            declaration = found ? Declaration : null;
            return found;
        }

        public override bool TryGetDimension(
            string dimension,
            [NotNullWhen(true)]
            out PortableQueryDimensionDeclaration<CollisionPredicate>?
                declaration)
        {
            declaration = null;
            return false;
        }

        public override bool AdmitsStageKind(
            RowSelectionStageKind kind) =>
            false;

        public override bool TryGetNamedOrder(
            string reference,
            out PortableQueryOrderPurpose purpose)
        {
            purpose = default;
            return false;
        }

        public override bool IsOrderable(string key) => false;

        public override CollisionPlan CreatePlan(
            PortableQueryResolvedIntent<CollisionPredicate> resolved) =>
            new(resolved.Terms.Single().Predicate.Value);
    }
}
