using DotnetInspector.ResearchSections;

namespace DotnetInspector.Sections.Tests;

public sealed class CapabilityCatalogAnalysisSearchTests
{
    [Fact]
    public void AnalysisIdentity_ReachesTheAnalysisThroughItsRoute()
    {
        CapabilityCatalogSearchResult result =
            Search(ExposeAll(), "string literals").Results[0];

        Assert.Equal(1.0, result.Similarity);
        Assert.Equal(
            CapabilityCatalogSearchMatchSource.OwnerIdentity,
            result.MatchSource);
        Assert.Equal(
            new InspectionCapabilityResourceIdentity(
                InspectionCapabilityResourceKind.Analysis,
                "string-literals"),
            result.ResourceIdentity);
        Assert.Equal(
            CapabilityCatalogSearchResourceKind.Analysis,
            result.ResourceKind);
        Assert.Equal("analyses/string-literals", result.ResourcePath);
        CapabilityCatalogSearchRoute route = Assert.Single(result.OwningRoutes);
        Assert.Equal(DiffAnalysisCatalog.RouteIdentity, route.Identity);
        Assert.Equal(
            DiffAnalysisCapabilityResourcePaths.Route.Value,
            route.ResourcePath);
        CapabilityCatalogSearchBinding binding =
            Assert.Single(result.ProductionBindings);
        Assert.Equal("diff --analysis", binding.Gesture);
        Assert.Equal("diff-analysis/bindings/cli", binding.ResourcePath);
    }

    [Fact]
    public void IssuedFindingTitle_Participates()
    {
        CapabilityCatalogSearchResult result = Assert.Single(
            Search(ExposeAll(), "occurrence").Results,
            static result => result.ResourcePath == "analyses/allocation");

        Assert.Equal("occurrence", result.MatchedTerm);
        Assert.Equal(
            CapabilityCatalogSearchMatchSource.IssuedFinding,
            result.MatchSource);
    }

    [Fact]
    public void PhraseWord_InAnIssuedFindingIsContext()
    {
        // "call" is in the analysis identity; "direct" appears only in the
        // title of the Finding it issues, "Direct call site".
        CapabilityCatalogSearchResult result =
            Search(ExposeAll(), "direct call").Results[0];

        Assert.Equal("analyses/call-site", result.ResourcePath);
        Assert.Equal(0.833, result.Similarity);
    }

    [Fact]
    public void UnexposedAnalysis_IsNotACandidate()
    {
        CapabilityCatalogSearchDocument all =
            Search(ExposeAll(), "string literals");
        CapabilityCatalogSearchDocument apiOnly =
            Search(["api"], "string literals");

        Assert.DoesNotContain(
            apiOnly.Results,
            static result => result.ResourceKind
                == CapabilityCatalogSearchResourceKind.Analysis
                && result.ResourcePath == "analyses/string-literals");
        Assert.Equal(
            all.CandidateResourceCount - (ExposeAll().Length - 1),
            apiOnly.CandidateResourceCount);
    }

    private static string[] ExposeAll() =>
    [
        .. DiffAnalysisCatalog.Registrations.Select(static registration =>
            registration.Analysis.Id.Value),
    ];

    private static CapabilityCatalogSearchDocument Search(
        IEnumerable<string> exposedAnalyses,
        string text)
    {
        InspectionCapabilityCatalog catalog =
            InspectionCapabilityCatalog.Create(
            [
                DiffAnalysisCatalog.ProductModule,
                new InspectionCapabilityModule(
                    "test/diff-compare-capability",
                    bindings:
                    [
                        new InspectionConsumerBinding<
                            DiffAnalysisInspectionRequest,
                            DiffAnalysisDocument>(
                            new(
                                "test/diff-compare",
                                InspectionConsumerKind.Cli,
                                "test CLI",
                                "diff --analysis"),
                            DiffAnalysisCatalog.Route,
                            exposedQueryTerms: [],
                            exposedAnalyses: exposedAnalyses),
                    ]),
            ]);
        return CapabilityCatalogSearch.Search(
                catalog,
                ResourceExplanationCatalog.CreateCapabilities(
                    catalog,
                    DiffAnalysisCapabilityResourcePaths.Create(catalog)),
                new(text, 100))
            .Content;
    }
}
