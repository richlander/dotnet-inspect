using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspect.Web.Interop.Catalog;
using DotnetInspector.Sections;

namespace DotnetInspect.Web.Tests;

[SupportedOSPlatform("browser")]
public sealed partial class BrowserVocabularyCompositionTests
{
    [Theory]
    [InlineData("data")]
    [InlineData("hal")]
    public void ValueSelectionUsesSharedKnowledgeAndOnlyRequiredContext(string projection)
    {
        const string root = "vocabularies/csharp.style-choices";
        string address = "inspect-resource:/" + root + "?projection=" + projection;
        var result = CatalogExports.ExplainVocabulariesCore(address, 0, ["slot-local-names"]);
        Assert.Equal(BrowserVocabularyExplanationOutcome.Explained, result.Outcome);
        Assert.Null(result.Rejection);
        var dataset = new VocabularyExplanation(BrowserVocabularyComposition.Snapshot)
            .SelectData(new(root), ["slot-local-names"]);
        Assert.True(JsonElement.DeepEquals(dataset.ToJson(
            static path => "inspect-resource:/" + path.Value, hal: projection == "hal",
            bindHalAddress: static path => "inspect-resource:/" + path.Value + "?projection=hal"),
            result.Explanation!.Content));
        Assert.Equal(4, dataset.Resources.Length);
        Assert.IsType<BrowserVocabularyNonProjectableShare>(result.Explanation.Share);
        Assert.Empty(result.Explanation.Diagnostics);
        var exported = JsonSerializer.Deserialize(CatalogExports.ExplainVocabularies(address, 0, ["slot-local-names"]),
            BrowserCatalogJsonContext.Default.BrowserVocabularyExplanationResult);
        Assert.NotNull(exported);
        Assert.True(JsonElement.DeepEquals(result.Explanation.Content, exported.Explanation!.Content));
    }

    [Fact]
    public void NativeArraySelectionDeduplicatesInOwnerOrder()
    {
        var result = CatalogExports.ExplainVocabulariesCore(
            "inspect-resource:/vocabularies/csharp.body-kinds?projection=data", 0,
            ["ObjectCreationExpression", "AddressExpression", "ObjectCreationExpression"]);
        Assert.Equal(BrowserVocabularyExplanationOutcome.Explained, result.Outcome);
        var vocabulary = result.Explanation!.Content.GetProperty("vocabularies").GetProperty("csharp.body-kinds");
        Assert.Equal(["AddressExpression", "ObjectCreationExpression"], vocabulary.GetProperty("value_order")
            .EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(72, vocabulary.GetProperty("catalog_values").GetInt32());
        Assert.False(vocabulary.GetProperty("values").TryGetProperty("AwaitExpression", out _));
    }

    [Fact]
    public void SelectedNonDefaultKeepsSparseDefaultMeaning()
    {
        var result = CatalogExports.ExplainVocabulariesCore(
            "inspect-resource:/vocabularies/api.accessibility?projection=data", 0, ["private"]);
        Assert.Equal(BrowserVocabularyExplanationOutcome.Explained, result.Outcome);
        var vocabulary = result.Explanation!.Content.GetProperty("vocabularies").GetProperty("api.accessibility");
        Assert.Equal("private", Assert.Single(vocabulary.GetProperty("values").EnumerateObject()).Name);
        Assert.Empty(vocabulary.GetProperty("defaults").EnumerateArray());
        Assert.Empty(vocabulary.GetProperty("property_sets").GetProperty("default").EnumerateArray());
    }

    [Fact]
    public void SelectedHalCollectionReadsFullOrNewlyNeededValue()
    {
        const string address = "inspect-resource:/vocabularies/csharp.body-kinds?projection=hal";
        var selected = CatalogExports.ExplainVocabulariesCore(address, 0, ["ObjectCreationExpression"]);
        Assert.Equal(BrowserVocabularyExplanationOutcome.Explained, selected.Outcome);
        var links = selected.Explanation!.Content.GetProperty("_links");
        string href = links.GetProperty("collection").GetProperty("href").GetString()!;
        Assert.Equal(address, href);
        Assert.Single(links.EnumerateObject());
        var full = CatalogExports.ExplainVocabulariesCore(href, 0, null);
        Assert.Equal(72, full.Explanation!.Content.GetProperty("_embedded").GetProperty("vocabularies")[0]
            .GetProperty("_embedded").GetProperty("values").GetArrayLength());
        var changed = CatalogExports.ExplainVocabulariesCore(href, 0, ["AddressExpression"]);
        Assert.Equal("AddressExpression", Assert.Single(changed.Explanation!.Content.GetProperty("_embedded")
            .GetProperty("vocabularies")[0].GetProperty("_embedded").GetProperty("values").EnumerateArray())
            .GetProperty("id").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("objectcreationexpression")]
    [InlineData("Object*")]
    [InlineData("unknown")]
    [InlineData("ObjectCreationExpression,AddressExpression")]
    [InlineData(null)]
    public void InvalidNativeIdentitiesReturnTypedRejection(string? id)
    {
        var result = CatalogExports.ExplainVocabulariesCore(
            "inspect-resource:/vocabularies/csharp.body-kinds?projection=hal", 0, [id!]);
        Assert.Equal(BrowserVocabularyExplanationOutcome.InvalidSelection, result.Outcome);
        Assert.Null(result.Explanation);
        Assert.Contains("Unknown vocabulary value identity", result.Rejection!.Message);
    }

    [Fact]
    public void EmptyArrayIsNotAFullRead()
    {
        var result = CatalogExports.ExplainVocabulariesCore(
            "inspect-resource:/vocabularies/csharp.body-kinds?projection=hal", 0, []);
        Assert.Equal(BrowserVocabularyExplanationOutcome.InvalidSelection, result.Outcome);
        Assert.Null(result.Explanation);
        Assert.Contains("at least one", result.Rejection!.Message);
    }

    [Theory]
    [InlineData("vocabularies/csharp.body-kinds", 0)]
    [InlineData("inspect-resource:/vocabularies/csharp.body-kinds?projection=contract", 0)]
    [InlineData("inspect-resource:/vocabularies/csharp.body-kinds?projection=hal", 1)]
    [InlineData("inspect-resource:/vocabularies?projection=hal", 0)]
    [InlineData("inspect-resource:/vocabularies/csharp.body-kinds/values/objectcreationexpression?projection=data", 0)]
    [InlineData("inspect-resource:/vocabularies/csharp.body-kinds?projection=hal&depth=1", 0)]
    public void ValueSelectionRejectsUnsupportedEntrances(string path, int depth)
    {
        var result = CatalogExports.ExplainVocabulariesCore(path, depth, ["ObjectCreationExpression"]);
        Assert.Equal(BrowserVocabularyExplanationOutcome.InvalidSelection, result.Outcome);
        Assert.Null(result.Explanation);
        Assert.Equal(path, result.Rejection!.RequestedPath);
        Assert.NotEmpty(result.Rejection.Message);
    }
}
