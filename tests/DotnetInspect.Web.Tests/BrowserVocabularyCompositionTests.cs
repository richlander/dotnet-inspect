using System.Runtime.Versioning;
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
}
