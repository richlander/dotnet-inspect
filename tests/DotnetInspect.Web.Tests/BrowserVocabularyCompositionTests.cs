using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotnetInspect.ProductVocabularyTesting;
using DotnetInspect.Web.Interop.Catalog;

namespace DotnetInspect.Web.Tests;

/// <summary>
/// Inspect Web composes its own product vocabulary list. Its snapshot identity
/// is asserted against <see cref="ProductVocabularyPin"/>, the one pin the CLI
/// suite also asserts, so the two hosts' lists cannot drift apart without one
/// suite failing.
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
                CatalogExports.ExplainVocabulariesCore(pin.Path, pin.Depth);

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
    public void ExplainVocabulariesExportRoundTripsTheDocument()
    {
        BrowserVocabularyExplanationResult? result = JsonSerializer.Deserialize(
            CatalogExports.ExplainVocabularies(
                "vocabularies/csharp.style-tiers/values/spelling",
                0),
            BrowserCatalogJsonContext.Default.BrowserVocabularyExplanationResult);

        Assert.NotNull(result);
        Assert.Equal(BrowserVocabularyExplanationOutcome.Explained, result.Outcome);
        JsonElement resource = result.Explanation!.Content
            .GetProperty("resources")[0];
        Assert.Equal(
            "vocabularies/csharp.style-tiers/values/spelling",
            resource.GetProperty("path").GetString());
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
