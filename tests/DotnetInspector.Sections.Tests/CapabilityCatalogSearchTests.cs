using DotnetInspector.Queries;
using DotnetInspector.Sections;
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
}
