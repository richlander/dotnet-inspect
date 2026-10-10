using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotnetInspect.ProductVocabularyTesting;
using DotnetInspect.Web.Interop.Catalog;
using DotnetInspector.Sections;

namespace DotnetInspect.Web.Tests;

/// <summary>
/// Inspect Web composes its own product vocabulary list. Its snapshot identity
/// is asserted against <see cref="ProductVocabularyPin"/>, the one pin the CLI
/// suite also asserts, so the two hosts' lists cannot drift apart without one
/// suite failing. These are PR-fast catalog and projection gates with no acquisition.
/// </summary>
[SupportedOSPlatform("browser")]
public sealed class BrowserVocabularyCompositionTests
{
    [Fact]
    public void BrowserCompositionMatchesTheProductVocabularyPin()
    {
        Assert.Equal(
            ProductVocabularyPin.FormatVersion,
            BrowserVocabularyComposition.Snapshot.FormatVersion);
        Assert.Equal(
            ProductVocabularyPin.SnapshotIdentity,
            BrowserVocabularyComposition.Snapshot.Identity.Value);
    }

    [Fact]
    public void InspectVocabularyExportsTheBrowserComposedSnapshot()
    {
        BrowserVocabularyInspection? inspection = JsonSerializer.Deserialize(
            CatalogExports.InspectVocabulary(),
            BrowserCatalogJsonContext.Default.BrowserVocabularyInspection);

        Assert.NotNull(inspection);
        Assert.Equal(ProductVocabularyPin.SnapshotIdentity, inspection.Content.Identity.Value);
        Assert.Equal(
            [
                "vocabulary.sections",
                "api.accessibility",
                "csharp.style-tiers",
                "csharp.style-choices",
                "csharp.body-kinds",
            ],
            inspection.Content.Vocabularies.Select(vocabulary => vocabulary.Identity.Value));
        var share = Assert.IsType<BrowserVocabularyNonProjectableShare>(inspection.Share);
        Assert.Equal("vocabulary/share", share.Path);
        Assert.Empty(inspection.Diagnostics);
    }

    [Fact]
    public void ExplainVocabulariesMatchesThePinnedCrossHostContent()
    {
        foreach (ExplanationContentPin pin in ProductVocabularyPin.ExplanationContent)
        {
            BrowserVocabularyExplanationResult result =
                CatalogExports.ExplainVocabulariesCore(
                    "inspect-resource:/" + pin.Path + "?projection=contract", pin.Depth);

            Assert.Equal(BrowserVocabularyExplanationOutcome.Explained, result.Outcome);
            Assert.Null(result.Rejection);
            BrowserVocabularyExplanation explanation = Assert.IsType<BrowserVocabularyExplanation>(
                result.Explanation);
            Assert.Equal(
                pin.Digest,
                "sha256:" + Convert.ToHexStringLower(
                    SHA256.HashData(Encoding.UTF8.GetBytes(
                        explanation.Content.GetRawText()
                            .Replace("\r\n", "\n", StringComparison.Ordinal)))));
            Assert.Empty(explanation.Diagnostics);
        }
    }

    [Fact]
    public void CompactAndSelectedContentMatchesThePinnedCrossHostContent()
    {
        foreach (ExplanationContentPin pin in ProductVocabularyPin.CompactContent)
        {
            string path = pin.Selection is null ? pin.Path
                : "inspect-resource:/" + pin.Path + "?projection=" + pin.Selection;
            var result = CatalogExports.ExplainVocabulariesCore(path, pin.Depth);
            Assert.Equal(BrowserVocabularyExplanationOutcome.Explained, result.Outcome);
            Assert.Equal(pin.Digest, "sha256:" + Convert.ToHexStringLower(
                SHA256.HashData(Encoding.UTF8.GetBytes(result.Explanation!.Content.GetRawText()))));
        }
    }

    [Fact]
    public void ExplainVocabulariesExportRoundTripsCompactContent()
    {
        BrowserVocabularyExplanationResult? result = JsonSerializer.Deserialize(
            CatalogExports.ExplainVocabularies(
                "vocabularies/csharp.style-tiers/values/spelling",
                0),
            BrowserCatalogJsonContext.Default.BrowserVocabularyExplanationResult);

        Assert.NotNull(result);
        Assert.Equal(BrowserVocabularyExplanationOutcome.Explained, result.Outcome);
        Assert.True(JsonElement.DeepEquals(
            CatalogExports.ExplainVocabulariesCore(
                    "vocabularies/csharp.style-tiers/values/spelling",
                    0)
                .Explanation!.Content,
            result.Explanation!.Content));
        JsonElement resource = result.Explanation.Content;
        Assert.Equal(
            "vocabularies/csharp.style-tiers/values/spelling",
            resource.GetProperty("path").GetString());
        Assert.False(resource.TryGetProperty("schemas", out _));
        Assert.Equal("Spelling", resource.GetProperty("facts").GetProperty("name").GetString());
        Assert.Equal(
            "inspect-resource:/vocabularies/csharp.style-tiers/values/spelling",
            resource.GetProperty("_links").GetProperty("self").GetProperty("href").GetString());
    }

    [Fact]
    public void CompactContentMatchesTheSharedProjectionAndKeepsEnvelope()
    {
        var owner = new VocabularyExplanation(BrowserVocabularyComposition.Snapshot);
        var explained = Assert.IsType<VocabularyExplanationResult.Explained>(
            owner.Explain("vocabularies/csharp.style-choices", 1));
        var result = CatalogExports.ExplainVocabulariesCore("vocabularies/csharp.style-choices", 1);
        Assert.True(JsonElement.DeepEquals(
            ResourceExplanationDataProjection.Create(explained.Inspection.Content,
                static path => "inspect-resource:/" + path.Value), result.Explanation!.Content));
        Assert.NotEmpty(result.Explanation.Content.GetProperty("_embedded").GetProperty("resources").EnumerateArray());
        Assert.IsType<BrowserVocabularyNonProjectableShare>(result.Explanation.Share);
        Assert.Empty(result.Explanation.Diagnostics);
    }

    [Theory]
    [InlineData("data")]
    [InlineData("hal")]
    public void SelectedVocabularyUsesSharedDataset(string projection)
    {
        const string path = "vocabularies/csharp.style-choices";
        var result = CatalogExports.ExplainVocabulariesCore(
            "inspect-resource:/" + path + "?projection=" + projection, 0);
        Assert.Equal(BrowserVocabularyExplanationOutcome.Explained, result.Outcome);
        var dataset = new VocabularyExplanation(BrowserVocabularyComposition.Snapshot)
            .SelectData(new ResourcePath(path));
        Assert.True(JsonElement.DeepEquals(dataset.ToJson(
            static path => "inspect-resource:/" + path.Value, hal: projection == "hal",
            bindHalAddress: static path => "inspect-resource:/" + path.Value + "?projection=hal",
            bindContractAddress: static path => "inspect-resource:/" + path.Value + "?projection=contract"),
            result.Explanation!.Content));
        if (projection == "hal")
        {
            Assert.Equal("Complete", result.Explanation.Content.GetProperty("data_scope")
                .GetProperty("completeness").GetString());
            Assert.NotEmpty(result.Explanation.Content.GetProperty("_embedded").GetProperty("values").EnumerateArray());
        }
        else
        {
            JsonElement choices = result.Explanation.Content.GetProperty("vocabularies").GetProperty("csharp.style-choices");
            Assert.NotEmpty(choices.GetProperty("values").EnumerateArray());
            Assert.True(choices.TryGetProperty("property_sets", out _));
        }
    }

    [Fact]
    public void EverySelectedHalLinkResolvesThroughTheFacade()
    {
        var result = CatalogExports.ExplainVocabulariesCore(
            "inspect-resource:/vocabularies/csharp.style-choices?projection=hal", 0);
        string[] links = Hrefs(result.Explanation!.Content).Distinct().ToArray();
        Assert.True(links.Length > 10);
        foreach (string href in links)
        {
            var followed = CatalogExports.ExplainVocabulariesCore(href, 0);
            Assert.Equal(BrowserVocabularyExplanationOutcome.Explained, followed.Outcome);
            if (href.EndsWith("?projection=hal", StringComparison.Ordinal))
                Assert.True(followed.Explanation!.Content.TryGetProperty("data_scope", out _));
            if (href.EndsWith("?projection=contract", StringComparison.Ordinal))
                Assert.True(followed.Explanation!.Content.TryGetProperty("schemas", out _));
        }
    }

    [Theory]
    [InlineData("vocabularies?projection=hal", 0)]
    [InlineData("vocabularies/csharp.style-choices/values/slot-local-names?projection=data", 0)]
    [InlineData("vocabularies/csharp.style-choices?projection=hal", 1)]
    [InlineData("vocabularies/csharp.style-choices?projection=bogus", 0)]
    [InlineData("vocabularies/csharp.style-choices?projection=hal&depth=1", 0)]
    public void UnsupportedSelectionsFailVisibly(string operand, int depth)
    {
        var result = CatalogExports.ExplainVocabulariesCore("inspect-resource:/" + operand, depth);
        Assert.Equal(BrowserVocabularyExplanationOutcome.InvalidSelection, result.Outcome);
        Assert.Null(result.Explanation);
        Assert.NotEmpty(result.Rejection!.Message);
    }

    private static IEnumerable<string> Hrefs(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (property.Name == "href")
                    yield return property.Value.GetString()!;
                else
                    foreach (string href in Hrefs(property.Value))
                        yield return href;
            }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (JsonElement child in element.EnumerateArray())
                foreach (string href in Hrefs(child))
                    yield return href;
    }

    [Theory]
    [InlineData("Vocabularies", 0, BrowserVocabularyExplanationOutcome.InvalidPath)]
    [InlineData("vocabularies/csharp.body-kinds/values/BreakStatement", 0,
        BrowserVocabularyExplanationOutcome.InvalidPath)]
    [InlineData("library", 0, BrowserVocabularyExplanationOutcome.OutsideVocabularies)]
    [InlineData("vocabularies/csharp.body-kind", 0, BrowserVocabularyExplanationOutcome.Unknown)]
    [InlineData("vocabularies/csharp.body-kinds/values", 0,
        BrowserVocabularyExplanationOutcome.Unknown)]
    [InlineData("vocabularies", -1, BrowserVocabularyExplanationOutcome.InvalidDepth)]
    public void ExplainVocabulariesRejectsWithATypedOutcome(
        string path,
        int depth,
        BrowserVocabularyExplanationOutcome outcome)
    {
        BrowserVocabularyExplanationResult result =
            CatalogExports.ExplainVocabulariesCore(path, depth);

        Assert.Equal(outcome, result.Outcome);
        Assert.Null(result.Explanation);
        BrowserVocabularyExplanationRejection rejection =
            Assert.IsType<BrowserVocabularyExplanationRejection>(result.Rejection);
        Assert.Equal(path, rejection.RequestedPath);
        Assert.NotEmpty(rejection.Message);
    }

    [Fact]
    public void UnknownVocabularyPathCarriesCanonicalSuggestions()
    {
        BrowserVocabularyExplanationRejection rejection = Assert.IsType<
            BrowserVocabularyExplanationRejection>(
            CatalogExports.ExplainVocabulariesCore("vocabularies/csharp.body-kind", 0)
                .Rejection);

        Assert.Contains("vocabularies/csharp.body-kinds", rejection.Suggestions);
    }
}
