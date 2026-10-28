using System.Runtime.Versioning;
using CoreHttpClientFactory = DotnetInspector.Networking.HttpClientFactory;
using System.Text.Json;
using DotnetInspect.Web.Interop.Package;
using DotnetInspector.Queries;
using DotnetInspector.ResearchSections;
using DotnetInspector.Sections;

namespace DotnetInspect.Web.Tests;

[Collection("Package query operations")]
[SupportedOSPlatform("browser")]
public sealed class BrowserCapabilityAdmissionTests : IDisposable
{
    private int _requests;

    public BrowserCapabilityAdmissionTests() => CoreHttpClientFactory.SetAuthenticationDecorator(
        inner => new RejectNetworkHandler(inner, () => _requests++));

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
    public void BrowserCompareBindingExposesOnlyInstalledExecutableProducers()
    {
        InspectionCapabilityCatalog catalog = BrowserCapabilityComposition.CreateCatalog();
        Assert.Same(DiffAnalysisCatalog.Route, BrowserCompareCapabilityBinding.Binding.Route);
        Assert.Contains(BrowserCompareCapabilityBinding.Binding, catalog.Bindings);
        Assert.Contains(PackageQueryCapabilityBinding.Binding, catalog.Bindings);
        Assert.Equal(["api", "api-attribute", "string-literals"],
            BrowserCompareCapabilityBinding.Binding.ExposedAnalyses);
        Assert.Empty(catalog.StructuralBindings);
        Assert.Empty(catalog.StructuralCatalogs);
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
    public void SearchAdmitsNoUnavailableBodyAnalysesOrUnboundStructuralViews()
    {
        InspectionCapabilityCatalog catalog = BrowserCapabilityComposition.CreateCatalog();
        var explanation = BrowserCapabilityComposition.CreateExplanation(catalog);
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
        var catalog = BrowserCapabilityComposition.CreateCompareCatalog();
        Assert.Same(DiffAnalysisCatalog.Document, Assert.Single(catalog.Documents));
        Assert.Same(BrowserCompareCapabilityBinding.Binding, Assert.Single(catalog.Bindings));
        Assert.Empty(catalog.Routes.Where(route => route.QuerySpace is not null));
    }
}
