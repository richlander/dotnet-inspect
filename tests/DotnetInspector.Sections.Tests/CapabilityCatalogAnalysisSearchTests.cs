using DotnetInspector.ResearchSections;
using DotnetInspector.Sections;

namespace DotnetInspector.Sections.Tests;

public sealed class CapabilityCatalogAnalysisSearchTests
{
    private static readonly InspectionAnalysisConsumerBinding DiffBinding =
        new(
            new(
                "test/diff-analysis",
                InspectionConsumerKind.Cli,
                "test CLI",
                "diff --analysis"),
            DiffAnalysisCatalog.Operation);

    [Fact]
    public void AnalysisIdentity_ReachesTheAnalysis()
    {
        CapabilityCatalogSearchDocument document = Search("string literals");

        Assert.Equal(
            CapabilityCatalogSearch.PhraseThreshold,
            document.SimilarityThreshold);
        CapabilityCatalogSearchResult result = document.Results[0];
        Assert.Equal(1.0, result.Similarity);
        Assert.Equal(
            CapabilityCatalogSearchMatchSource.OwnerIdentity,
            result.MatchSource);
        Assert.Equal(
            new InspectionCapabilityResourceIdentity(
                InspectionCapabilityResourceKind.Analysis,
                "string-literals"),
            result.ResourceIdentity);
        Assert.Null(result.StructuralIdentity);
        Assert.Equal(
            CapabilityCatalogSearchResourceKind.Analysis,
            result.ResourceKind);
        Assert.Equal("string-literals", result.ResourceName);
        Assert.Empty(result.CanonicalKeys);
        Assert.Equal("analyses/string-literals", result.ResourcePath);
        Assert.Equal(
            new CapabilityCatalogSearchRoute(
                "diff-analysis",
                "Diff analyses",
                "analyses"),
            Assert.Single(result.OwningRoutes));
        CapabilityCatalogSearchBinding binding =
            Assert.Single(result.ProductionBindings);
        Assert.Equal("test/diff-analysis", binding.Identity);
        Assert.Equal("diff --analysis", binding.Gesture);
        Assert.Equal(InspectionConsumerKind.Cli, binding.ConsumerKind);
        Assert.Equal("analyses", binding.ResourcePath);
    }

    [Fact]
    public void IssuedFindingTitle_Participates()
    {
        CapabilityCatalogSearchResult result = Assert.Single(
            Search("occurrence").Results,
            static result => result.ResourcePath == "analyses/allocation");

        Assert.Equal(1.0, result.Similarity);
        Assert.Equal("occurrence", result.MatchedTerm);
        Assert.Equal(
            CapabilityCatalogSearchMatchSource.IssuedFinding,
            result.MatchSource);
        Assert.True(result.IsSegment);
    }

    [Fact]
    public void PhraseWord_InAnIssuedFindingIsContext()
    {
        // "call" is in the analysis identity; "direct" appears only in the
        // title of the finding it issues, "Direct call site".
        CapabilityCatalogSearchResult result =
            Search("direct call").Results[0];

        Assert.Equal("analyses/call-site", result.ResourcePath);
        Assert.Equal(0.833, result.Similarity);
        Assert.Equal(
            CapabilityCatalogSearchMatchSource.OwnerIdentity,
            result.MatchSource);
    }

    [Fact]
    public void EveryParticipatingAnalysis_IsACandidate()
    {
        InspectionCapabilityCatalog catalog = Catalog();

        CapabilityCatalogSearchDocument document = Search("string literals");

        Assert.Equal(catalog.Analyses.Length, document.CandidateResourceCount);
    }

    [Fact]
    public void AnalysisPath_ResolvesInTheAnalysisExplanation()
    {
        InspectionCapabilityCatalog catalog = Catalog();
        ResourceExplanationCatalog explanation =
            ResourceExplanationCatalog.CreateAnalyses(catalog);

        CapabilityCatalogSearchDocument document = CapabilityCatalogSearch.Search(
                InspectionCapabilityCatalog.Create([]),
                explanation,
                [],
                [Admit()],
                new("diff analyses", 100))
            .Content;

        Assert.Equal(catalog.Analyses.Length, document.ReturnedCount);
        Assert.All(
            document.Results,
            result => Assert.IsType<ResourcePathResolution.Resolved>(
                explanation.Resolve(result.ResourcePath)));
    }

    [Fact]
    public void AnalysisRoute_IsTheAnalysesCollection()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new CapabilityCatalogSearchAnalysisCatalog(
                Catalog(),
                new("diff-analysis", "Diff analyses", "operations")));

        Assert.Equal("route", exception.ParamName);
    }

    [Fact]
    public void AnalysisCatalog_RequiresAnAnalysisBinding()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new CapabilityCatalogSearchAnalysisCatalog(
                InspectionCapabilityCatalog.Create(
                    [DiffAnalysisCatalog.ProductModule]),
                Route()));

        Assert.Equal("catalog", exception.ParamName);
    }

    [Fact]
    public void AnalysesCollection_IsAdmittedOnce()
    {
        CapabilityCatalogSearchAnalysisCatalog admitted = Admit();

        var exception = Assert.Throws<ArgumentException>(() =>
            CapabilityCatalogSearch.Search(
                InspectionCapabilityCatalog.Create([]),
                EmptyExplanation(),
                [],
                [admitted, admitted],
                new("allocation")));

        Assert.Equal("catalogs", exception.ParamName);
    }

    private static CapabilityCatalogSearchDocument Search(string text) =>
        CapabilityCatalogSearch.Search(
                InspectionCapabilityCatalog.Create([]),
                EmptyExplanation(),
                [],
                [Admit()],
                new(text))
            .Content;

    private static CapabilityCatalogSearchAnalysisCatalog Admit() =>
        new(Catalog(), Route());

    private static CapabilityCatalogSearchRoute Route() =>
        new("diff-analysis", "Diff analyses", "analyses");

    private static InspectionCapabilityCatalog Catalog() =>
        InspectionCapabilityCatalog.Create(
        [
            DiffAnalysisCatalog.ProductModule,
            new InspectionCapabilityModule(
                "test/diff-analysis-capability",
                analysisBindings: [DiffBinding]),
        ]);

    // Search resolves only capability paths in its explanation catalog;
    // with no capabilities, any catalog serves.
    private static ResourceExplanationCatalog EmptyExplanation() =>
        ResourceExplanationCatalog.CreateAnalyses(Catalog());
}
