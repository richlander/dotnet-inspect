using System.Text.Json;
using DotnetInspect.Cli.Commands;
using DotnetInspector.InspectionContracts;
using DotnetInspector.Sections;
using QuerySpace.Explanation;

namespace DotnetInspect.Cli.Tests;

// PR-fast: production registrations and metadata-only preparation; no acquisition.
public sealed class QueryPreparationSelectionTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("depends,dependency-target")]
    [InlineData("depends,dependency-depth")]
    [InlineData("library-literal")]
    public void SharedSelection_PreservesOriginalMeaningAndEquivalentLowerings(string? keys)
    {
        ResourceExplanationCatalog catalog = Catalog();
        var resolution = Resolve(catalog, "package-query/query");
        ResourceExplanationDataset selected = ResourceExplanationDataset.Create(catalog, resolution, keys?.Split(','));
        JsonElement direct = selected.ToJson(path => "inspect-resource:/" + path.Value);
        JsonElement hal = selected.ToJson(path => "inspect-resource:/" + path.Value, hal: true,
            bindHalAddress: path => "inspect-resource:/" + path.Value + "?projection=hal");
        Assert.Equal("query-preparation/v2", direct.GetProperty("scope").GetProperty("profile").GetString());
        Assert.Equal(19, direct.GetProperty("scope").GetProperty("catalog_facets").GetInt32());
        Assert.True(JsonElement.DeepEquals(direct.GetProperty("scope"), hal.GetProperty("scope")));
        Dictionary<string, JsonElement> facets = hal.GetProperty("_embedded").GetProperty("facets")
            .EnumerateArray().ToDictionary(facet => facet.GetProperty("key").GetString()!);
        Assert.Equal(direct.GetProperty("facet_order").EnumerateArray().Select(key => key.GetString()), facets.Keys);
        foreach (JsonProperty facet in direct.GetProperty("facets").EnumerateObject())
        {
            JsonElement halFacet = facets[facet.Name];
            Assert.False(halFacet.TryGetProperty("_links", out _));
            var original = ResourceExplanationDataProjection.Create(catalog.Explain(
                Resolve(catalog, "package-query/query/facets/" + facet.Name),
                ResourceExplanationRequest.ForHost(0)).Content, path => path.Value).GetProperty("facts");
            foreach (string field in new[] { "name", "summary", "operators", "value-kind", "examples", "effects", "input-rules", "values" })
            {
                string projected = field.Replace('-', '_');
                JsonElement expected = original.GetProperty(field);
                if (expected.ValueKind == JsonValueKind.Array && expected.GetArrayLength() == 0 && field != "operators")
                {
                    Assert.False(facet.Value.TryGetProperty(projected, out _));
                    Assert.False(halFacet.TryGetProperty(projected, out _));
                }
                else
                {
                    Assert.True(JsonElement.DeepEquals(expected, facet.Value.GetProperty(projected)));
                    Assert.True(JsonElement.DeepEquals(expected, halFacet.GetProperty(projected)));
                }
            }
            Assert.Equal(original.GetProperty("execution-class").GetString(),
                facet.Value.GetProperty("execution_class").GetString());
            Assert.True(JsonElement.DeepEquals(facet.Value.GetProperty("execution_class"),
                halFacet.GetProperty("execution_class")));
            JsonElement options = original.GetProperty("options");
            if (options.GetArrayLength() > 0)
            {
                JsonElement descriptions = facet.Value.GetProperty("value_descriptions");
                Assert.Equal(options.GetArrayLength(), descriptions.EnumerateObject().Count());
                foreach (JsonElement option in options.EnumerateArray())
                    Assert.True(JsonElement.DeepEquals(option.GetProperty("summary"),
                        descriptions.GetProperty(option.GetProperty("value").GetString()!)));
                Assert.True(JsonElement.DeepEquals(descriptions, halFacet.GetProperty("value_descriptions")));
            }
            else
            {
                Assert.False(facet.Value.TryGetProperty("value_descriptions", out _));
                Assert.False(halFacet.TryGetProperty("value_descriptions", out _));
            }
            foreach (string field in new[] { "presentation_order", "control_kind", "presentation_kind",
                "selection_group", "combines_within_selection_group", "replacement_group", "options" })
            {
                Assert.False(facet.Value.TryGetProperty(field, out _));
                Assert.False(halFacet.TryGetProperty(field, out _));
            }
            if (facet.Value.TryGetProperty("requires", out JsonElement requires))
            {
                Assert.True(JsonElement.DeepEquals(requires, halFacet.GetProperty("requires")));
                foreach (JsonElement target in requires.EnumerateArray())
                    Assert.True(facets.ContainsKey(target.GetString()!));
            }
        }
        JsonElement binding = direct.GetProperty("bindings").GetProperty("dotnet-inspect.cli/package-query");
        JsonElement halBinding = hal.GetProperty("_embedded").GetProperty("bindings")[0];
        foreach (JsonProperty field in binding.EnumerateObject())
            Assert.True(JsonElement.DeepEquals(field.Value, halBinding.GetProperty(field.Name)));
        Assert.All(binding.GetProperty("exposed_facets").EnumerateArray(), key => Assert.Contains(key.GetString()!, facets.Keys));
        Assert.False(hal.GetProperty("_links").TryGetProperty("describedby", out _));
        Assert.Equal(keys is null, hal.GetProperty("_links").TryGetProperty("self", out _));
        if (keys is not null)
        {
            Assert.Equal("inspect-resource:/package-query/query?projection=hal",
                hal.GetProperty("_links").GetProperty("collection").GetProperty("href").GetString());
            Assert.False(direct.GetProperty("facets").TryGetProperty("license", out _));
            Assert.Equal("Complete", direct.GetProperty("scope").GetProperty("completeness").GetString());
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("depends,dependency-target")]
    [InlineData("library-literal")]
    public void PresentationSelection_PreservesAllRegisteredControls(string? keys)
    {
        ResourceExplanationCatalog catalog = Catalog();
        ResourceExplanationDataset selected = ResourceExplanationDataset.Create(catalog,
            Resolve(catalog, "package-query/query"), keys?.Split(','));
        JsonElement presentation = selected.ToQueryPresentationJson();
        JsonElement reading = selected.ToJson(path => path.Value);
        Assert.Equal("query-preparation/v1", presentation.GetProperty("scope").GetProperty("profile").GetString());
        Assert.True(JsonElement.DeepEquals(presentation.GetProperty("facet_order"), reading.GetProperty("facet_order")));
        Assert.True(JsonElement.DeepEquals(presentation.GetProperty("bindings"), reading.GetProperty("bindings")));
        foreach (JsonProperty facet in presentation.GetProperty("facets").EnumerateObject())
        {
            JsonElement original = ResourceExplanationDataProjection.Create(catalog.Explain(
                Resolve(catalog, "package-query/query/facets/" + facet.Name),
                ResourceExplanationRequest.ForHost(0)).Content, path => path.Value);
            foreach (string field in new[] { "presentation-order", "control-kind", "presentation-kind",
                "execution-class", "selection-group", "combines-within-selection-group", "replacement-group",
                "option-source", "options" })
            {
                JsonElement actual = facet.Value.GetProperty(field.Replace('-', '_'));
                if (original.GetProperty("facts").TryGetProperty(field, out JsonElement expected))
                    Assert.True(JsonElement.DeepEquals(expected, actual));
                else
                {
                    Assert.Equal("Absent", original.GetProperty("fact_states").GetProperty(field)
                        .GetProperty("state").GetString());
                    Assert.Equal(JsonValueKind.Null, actual.ValueKind);
                }
            }
            foreach (JsonProperty field in reading.GetProperty("facets").GetProperty(facet.Name).EnumerateObject())
                if (field.Name != "value_descriptions")
                    Assert.True(JsonElement.DeepEquals(field.Value, facet.Value.GetProperty(field.Name)));
        }
    }

    [Fact]
    public void KeySelection_ClosesLiteralContextWithoutExposingItAsAnInspectionPredicate()
    {
        ResourceExplanationCatalog catalog = Catalog();
        JsonElement data = ResourceExplanationDataset.Create(catalog, Resolve(catalog, "package-query/query"),
            ["library-literal", "library-literal"]).ToJson(path => path.Value);
        Assert.Equal(["library-literal", "library-target"], data.GetProperty("facet_order")
            .EnumerateArray().Select(key => key.GetString()));
        Assert.Equal(["library-literal"], data.GetProperty("bindings").GetProperty("dotnet-inspect.cli/package-query")
            .GetProperty("exposed_facets").EnumerateArray().Select(key => key.GetString()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("unknown")]
    public void UnknownOrEmptyKeySelection_FailsInsteadOfClaimingEmptyKnowledge(string key)
    {
        ResourceExplanationCatalog catalog = Catalog();
        Assert.Throws<InvalidOperationException>(() => ResourceExplanationDataset.Create(catalog,
            Resolve(catalog, "package-query/query"), key.Length == 0 ? [] : [key]));
        Assert.Throws<InvalidOperationException>(() => ResourceExplanationDataset.Create(catalog,
            Resolve(catalog, "package-query/query/facets/depends"), ["depends"]));
    }

    [Fact]
    public void HostAddressBindings_ChangeNavigationWithoutChangingSelectedKnowledge()
    {
        ResourceExplanationCatalog catalog = Catalog();
        var selected = ResourceExplanationDataset.Create(catalog, Resolve(catalog, "package-query/query"), ["depends"]);
        JsonElement cli = selected.ToJson(path => "inspect-resource:/" + path.Value, hal: true);
        JsonElement browser = selected.ToJson(path => "https://example.test/explain/" + path.Value, hal: true);
        Assert.True(JsonElement.DeepEquals(cli.GetProperty("scope"), browser.GetProperty("scope")));
        Assert.True(JsonElement.DeepEquals(cli.GetProperty("_embedded"), browser.GetProperty("_embedded")));
        Assert.StartsWith("https://example.test/explain/", browser.GetProperty("_links").GetProperty("collection").GetProperty("href").GetString());
        Assert.Throws<InvalidOperationException>(() => selected.ToJson(_ => " ", hal: true));
    }

    private static ResourceExplanationCatalog Catalog()
    {
        InspectionCapabilityCatalog capabilities = InspectionCapabilityCatalog.Create(
            [PackageQueryCapability.ProductModule, PackageQueryCommandCapability.Module]);
        return ResourceExplanationCatalog.CreateCapabilities(capabilities, PackageQueryCapabilityResourcePaths.Create(capabilities));
    }

    private static ResourcePathResolution.Resolved Resolve(ResourceExplanationCatalog catalog, string path) =>
        Assert.IsType<ResourcePathResolution.Resolved>(catalog.Resolve(path));
}
