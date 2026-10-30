using System.Runtime.Versioning;
using CoreHttpClientFactory = DotnetInspector.Networking.HttpClientFactory;
using System.Text.Json;
using DotnetInspect.Web.Interop.Package;
using DotnetInspect.Web.Interop.Metadata;
using DotnetInspector.Diff;
using DotnetInspector.Queries;
using DotnetInspector.ResearchSections;
using DotnetInspector.Sections;

namespace DotnetInspect.Web.Tests;

[Collection("Package query operations")]
[SupportedOSPlatform("browser")]
public sealed class BrowserCapabilityAdmissionTests : IDisposable
{
    private int _requests;

    public BrowserCapabilityAdmissionTests()
    {
        InspectionEngine.InitializeCapabilityComposition();
        CoreHttpClientFactory.SetAuthenticationDecorator(
            inner => new RejectNetworkHandler(inner, () => _requests++));
    }

    public void Dispose() => CoreHttpClientFactory.SetAuthenticationDecorator(null);

    private sealed class RejectNetworkHandler(HttpMessageHandler inner, Action record) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            record();
            throw new InvalidOperationException("Capability discovery attempted network acquisition.");
        }
    }

    [Fact]
    public void HostConfigurationDefersContributionProvidersUntilSearch()
    {
        int evaluations = 0;
        try
        {
            BrowserCapabilityComposition.Configure([() =>
            {
                evaluations++;
                return BrowserCapabilityComposition.CreatePackageQueryContribution();
            }]);
            Assert.Equal(0, evaluations);
            PackageExports.SearchCapabilities("literal", 1);
            Assert.Equal(1, evaluations);
            var first = BrowserCapabilityComposition.CreateSearchComposition();
            PackageExports.SearchCapabilities("depends", 1);
            var second = BrowserCapabilityComposition.CreateSearchComposition();
            Assert.Equal(1, evaluations);
            Assert.Same(first.Catalog, second.Catalog);
            Assert.Same(first.Explanation, second.Explanation);
            Assert.Equal(0, _requests);
        }
        finally
        {
            InspectionEngine.InitializeCapabilityComposition();
        }
    }

    [Fact]
    public void HostReconfigurationReplacesTheMetadataSnapshot()
    {
        try
        {
            BrowserCapabilityComposition.Configure(
                [BrowserCapabilityComposition.CreatePackageQueryContribution]);
            var first = BrowserCapabilityComposition.CreateSearchComposition();
            Assert.Single(first.Catalog.Routes);
            InspectionEngine.InitializeCapabilityComposition();
            var second = BrowserCapabilityComposition.CreateSearchComposition();
            Assert.Equal(3, second.Catalog.Routes.Length);
            Assert.NotSame(first.Catalog, second.Catalog);
            Assert.NotSame(first.Explanation, second.Explanation);
        }
        finally
        {
            InspectionEngine.InitializeCapabilityComposition();
        }
    }

    [Fact]
    public void BrowserCompareBindingExposesOnlyInstalledExecutableProducers()
    {
        InspectionCapabilityCatalog catalog = BrowserCapabilityComposition.CreateSearchComposition().Catalog;
        Assert.Same(DiffAnalysisCatalog.Route, BrowserCompareCapabilityBinding.Binding.Route);
        Assert.Contains(BrowserCompareCapabilityBinding.Binding, catalog.Bindings);
        Assert.Contains(PackageQueryCapabilityBinding.Binding, catalog.Bindings);
        Assert.Equal(["api", "api-attribute", "string-literals"],
            BrowserCompareCapabilityBinding.Binding.ExposedAnalyses);
        Assert.Empty(catalog.StructuralBindings);
        Assert.Empty(catalog.StructuralCatalogs);
    }

    [Fact]
    public void BrowserFindingDiffAnyBindingConsumesTheLibraryRoute()
    {
        // The queryLibraryFindingDiffAny Worker operation is the Browser
        // consumer of Finding Diff Any's Library route, so the composed
        // catalog has no Browser adoption gap for it.
        InspectionCapabilityCatalog catalog = BrowserCapabilityComposition.CreateSearchComposition().Catalog;
        Assert.Same(FindingDiffAnyLibraryCapability.Route, BrowserFindingDiffAnyCapabilityBinding.Binding.Route);
        Assert.Equal(InspectionConsumerKind.Browser, BrowserFindingDiffAnyCapabilityBinding.Binding.Descriptor.Kind);
        Assert.Equal("queryLibraryFindingDiffAny", BrowserFindingDiffAnyCapabilityBinding.Binding.Descriptor.Gesture);
        Assert.Contains(BrowserFindingDiffAnyCapabilityBinding.Binding, catalog.Bindings);
        Assert.Contains(FindingDiffAnyLibraryCapability.Route, catalog.Routes);
        Assert.DoesNotContain(catalog.AdoptionGaps, static gap =>
            ReferenceEquals(gap.Route, FindingDiffAnyLibraryCapability.Route)
            && gap.ConsumerKind == InspectionConsumerKind.Browser);
        var own = BrowserFindingDiffAnyCapabilityBinding.Catalog;
        Assert.Same(FindingDiffAnyLibraryCapability.Document, Assert.Single(own.Documents));
        Assert.Same(BrowserFindingDiffAnyCapabilityBinding.Binding, Assert.Single(own.Bindings));
    }

    [Fact]
    public void SearchExportFindsTheFindingDiffAnyDocumentWithItsBrowserBinding()
    {
        using JsonDocument json = JsonDocument.Parse(PackageExports.SearchCapabilities("finding diff any", 100));
        JsonElement result = Assert.Single(json.RootElement.GetProperty("content").GetProperty("results")
            .EnumerateArray(), static candidate =>
                candidate.GetProperty("resourcePath").GetString() == "finding-diff-any");
        JsonElement binding = Assert.Single(result.GetProperty("productionBindings").EnumerateArray());
        Assert.Equal(BrowserFindingDiffAnyCapabilityBinding.BindingIdentity,
            binding.GetProperty("identity").GetString());
        Assert.Equal("Browser", binding.GetProperty("consumerKind").GetString());
        Assert.Equal(0, _requests);
    }

    [Theory]
    [InlineData("api-attribute", "api-attribute")]
    [InlineData("string-literals", "string-literals")]
    public void SearchExportPreservesSharedAnalysisIdentityAndBrowserBinding(string query, string identity)
    {
        using JsonDocument json = JsonDocument.Parse(PackageExports.SearchCapabilities(query, 100));
        JsonElement result = Assert.Single(json.RootElement.GetProperty("content").GetProperty("results")
            .EnumerateArray(), candidate =>
                candidate.GetProperty("resourceIdentity").GetProperty("kind").GetString() == "Analysis"
                && candidate.GetProperty("resourceIdentity").GetProperty("identity").GetString() == identity);
        Assert.Equal(0, _requests);
        Assert.Equal("Analysis", result.GetProperty("resourceKind").GetString());
        Assert.Equal("analyses/" + identity, result.GetProperty("resourcePath").GetString());
        JsonElement binding = Assert.Single(result.GetProperty("productionBindings").EnumerateArray());
        Assert.Equal(BrowserCompareCapabilityBinding.BindingIdentity,
            binding.GetProperty("identity").GetString());
        Assert.Equal("Browser", binding.GetProperty("consumerKind").GetString());
        Assert.Equal("diff-analysis/routes/compare", Assert.Single(
            result.GetProperty("owningRoutes").EnumerateArray()).GetProperty("resourcePath").GetString());
    }

    [Fact]
    public void SearchExportPreservesIssuedFindingProvenance()
    {
        using JsonDocument json = JsonDocument.Parse(PackageExports.SearchCapabilities("member", 100));
        JsonElement result = Assert.Single(json.RootElement.GetProperty("content").GetProperty("results")
            .EnumerateArray(), candidate =>
                candidate.GetProperty("resourceIdentity").GetProperty("kind").GetString() == "Analysis"
                && candidate.GetProperty("resourceIdentity").GetProperty("identity").GetString() == "api");
        Assert.Equal("IssuedFinding", result.GetProperty("matchSource").GetString());
        Assert.Equal(0, _requests);
    }

    [Fact]
    public void SearchAdmitsNoUnavailableBodyAnalysesOrUnboundStructuralViews()
    {
        var (catalog, explanation) = BrowserCapabilityComposition.CreateSearchComposition();
        foreach (string text in new[] { "allocation", "unsafety", "retained", "library sections" })
        {
            var result = CapabilityCatalogSearch.Search(catalog, explanation, new(text, 100));
            Assert.DoesNotContain(result.Content.Results, candidate =>
                candidate.ResourceKind is CapabilityCatalogSearchResourceKind.Section
                    or CapabilityCatalogSearchResourceKind.Category);
            Assert.All(result.Content.Results.Where(candidate =>
                candidate.ResourceKind == CapabilityCatalogSearchResourceKind.Analysis), candidate =>
                    Assert.Contains(candidate.ResourceIdentity!.Identity,
                        BrowserCompareCapabilityBinding.Binding.ExposedAnalyses));
        }
    }

    [Fact]
    public void CompareCatalogDoesNotInitializePackageQueryResources()
    {
        var catalog = BrowserCompareCapabilityBinding.Catalog;
        Assert.Same(DiffAnalysisCatalog.Document, Assert.Single(catalog.Documents));
        Assert.Same(BrowserCompareCapabilityBinding.Binding, Assert.Single(catalog.Bindings));
        Assert.Empty(catalog.Routes.Where(route => route.QuerySpace is not null));
    }
}
