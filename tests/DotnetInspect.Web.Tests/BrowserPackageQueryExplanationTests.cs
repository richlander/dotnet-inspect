using System.Net;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspect.Web.Interop.Package;
using DotnetInspector.Sections;
using CoreHttpClientFactory = DotnetInspector.Networking.HttpClientFactory;

namespace DotnetInspect.Web.Tests;

// PR-fast: installed metadata only, with fail-fast package-source requests.
[Collection("Package query operations")]
[SupportedOSPlatform("browser")]
public sealed class BrowserPackageQueryExplanationTests : IDisposable
{
    private int requests;

    public BrowserPackageQueryExplanationTests() => CoreHttpClientFactory.SetAuthenticationDecorator(
        handler => new RejectNetworkHandler(handler, () => requests++));

    [Theory]
    [InlineData("depends", new[] { "depends" })]
    [InlineData("dependency-depth", new[] { "dependency-target", "dependency-depth" })]
    [InlineData("library-literal", new[] { "library-literal", "library-target" })]
    public void SelectedRecordsEqualTheFullModelAndKeepContext(string key, string[] expected)
    {
        var full = Read("package-query/query", null);
        var selected = Read("package-query/query", JsonSerializer.Serialize(new[] { key, key }));
        JsonElement embedded = selected.GetProperty("_embedded");
        JsonElement[] facets = embedded.GetProperty("facets").EnumerateArray().ToArray();
        Assert.Equal(expected, facets.Select(f => f.GetProperty("key").GetString()));
        foreach (JsonElement facet in facets)
            Assert.True(JsonElement.DeepEquals(facet, full.GetProperty("_embedded").GetProperty("facets")
                .EnumerateArray().Single(f => f.GetProperty("key").GetString() == facet.GetProperty("key").GetString())));
        JsonElement binding = Assert.Single(embedded.GetProperty("bindings").EnumerateArray());
        Assert.Equal("Browser", binding.GetProperty("consumer_kind").GetString());
        Assert.NotEmpty(binding.GetProperty("input_rules").EnumerateArray());
        Assert.True(JsonElement.DeepEquals(binding.GetProperty("input_rules"),
            full.GetProperty("_embedded").GetProperty("bindings")[0].GetProperty("input_rules")));
        Assert.All(binding.GetProperty("exposed_facets").EnumerateArray(), f => Assert.Contains(f.GetString()!, expected));
        Assert.DoesNotContain(binding.GetProperty("exposed_facets").EnumerateArray(), f => f.GetString() == "library-target");
        Assert.Equal("query-preparation/v2", selected.GetProperty("scope").GetProperty("profile").GetString());
        Assert.All(facets, facet => Assert.False(facet.TryGetProperty("options", out _)));
        Assert.Equal("Complete", selected.GetProperty("scope").GetProperty("completeness").GetString());
        Assert.False(selected.GetProperty("_links").TryGetProperty("self", out _));
        string href = selected.GetProperty("_links").GetProperty("collection").GetProperty("href").GetString()!;
        Assert.True(JsonElement.DeepEquals(full, Read(href, null)));
        Assert.Equal(0, requests);
    }

    [Fact]
    public void NarrowedDirectAndHalResponsesEqualTheSharedSelection()
    {
        string[] keys = ["depends", "dependency-target"];
        var installed = BrowserCapabilityCatalogSearch.CreateCatalog();
        var catalog = ResourceExplanationCatalog.CreateCapabilities(installed,
            PackageQueryCapabilityResourcePaths.Create(installed));
        var resolved = Assert.IsType<ResourcePathResolution.Resolved>(catalog.Resolve("package-query/query"));
        var dataset = ResourceExplanationDataset.Create(catalog, resolved, keys);
        foreach (string projection in new[] { "data", "hal" })
            Assert.True(JsonElement.DeepEquals(dataset.ToJson(
                static p => "inspect-resource:/" + p.Value, hal: projection == "hal",
                bindHalAddress: static p => "inspect-resource:/" + p.Value + "?projection=hal"),
                Read("inspect-resource:/package-query/query?projection=" + projection,
                    JsonSerializer.Serialize(keys))));
        Assert.Equal(0, requests);
    }

    [Fact]
    public void FullAndFacetEntrancesUseTheSharedDataset()
    {
        ResourceExplanationCatalog catalog = ResourceExplanationCatalog.CreateCapabilities(
            BrowserCapabilityCatalogSearch.CreateCatalog(),
            PackageQueryCapabilityResourcePaths.Create(BrowserCapabilityCatalogSearch.CreateCatalog()));
        foreach (string path in new[] { "package-query/query", "package-query/query/facets/library-literal" })
        {
            var resolved = Assert.IsType<ResourcePathResolution.Resolved>(catalog.Resolve(path));
            var dataset = ResourceExplanationDataset.Create(catalog, resolved);
            foreach (string projection in new[] { "data", "hal" })
                Assert.True(JsonElement.DeepEquals(dataset.ToJson(
                    static p => "inspect-resource:/" + p.Value,
                    hal: projection == "hal",
                    bindHalAddress: static p => "inspect-resource:/" + p.Value + "?projection=hal"),
                    Read("inspect-resource:/" + path + "?projection=" + projection, null)));
        }
        Assert.Equal(0, requests);
    }

    [Theory]
    [InlineData("package-query/query", "[]")]
    [InlineData("package-query/query", "[\"unknown\"]")]
    [InlineData("package-query/query", "[\"Depends\"]")]
    [InlineData("package-query/query", "[\"depends*\"]")]
    [InlineData("package-query/query", "[null]")]
    [InlineData("package-query/query/facets/depends", "[\"depends\"]")]
    [InlineData("package-query", null)]
    [InlineData("inspect-resource:/package-query/query?projection=contract", null)]
    public void InvalidSelectionsReturnNoPartialContent(string path, string? keys)
    {
        var result = Parse(PackageExports.ExplainPackageQuery(path, keys is null ? null : JsonSerializer.Deserialize<string[]>(keys)));
        Assert.Equal(BrowserPackageQueryExplanationOutcome.InvalidSelection, result.Outcome);
        Assert.Null(result.Content);
        Assert.NotEmpty(result.Message!);
        Assert.Equal(0, requests);
    }

    [Theory]
    [InlineData("PACKAGE-QUERY/query", BrowserPackageQueryExplanationOutcome.InvalidPath)]
    [InlineData("package-query/query/facets/unknown", BrowserPackageQueryExplanationOutcome.Unknown)]
    [InlineData("vocabularies", BrowserPackageQueryExplanationOutcome.Unknown)]
    public void UnsupportedPathsFailVisibly(string path, BrowserPackageQueryExplanationOutcome outcome)
    {
        var result = Parse(PackageExports.ExplainPackageQuery(path, null));
        Assert.Equal(outcome, result.Outcome);
        Assert.Null(result.Content);
        Assert.NotEmpty(result.Message!);
        Assert.Equal(0, requests);
    }

    private static BrowserPackageQueryExplanationResult Parse(string json) => JsonSerializer.Deserialize(
        json, BrowserPackageJsonContext.Default.BrowserPackageQueryExplanationResult)!;

    private static JsonElement Read(string path, string? keys)
    {
        var result = Parse(PackageExports.ExplainPackageQuery(path, keys is null ? null : JsonSerializer.Deserialize<string[]>(keys)));
        Assert.Equal(BrowserPackageQueryExplanationOutcome.Explained, result.Outcome);
        Assert.Null(result.Message);
        return result.Content!.Value;
    }

    public void Dispose() => CoreHttpClientFactory.SetAuthenticationDecorator(null);

    private sealed class RejectNetworkHandler(HttpMessageHandler inner, Action record) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            record();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway));
        }
    }
}
