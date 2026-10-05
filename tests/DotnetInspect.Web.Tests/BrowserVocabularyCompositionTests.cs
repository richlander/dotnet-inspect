using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspect.Web.Interop.Catalog;

namespace DotnetInspect.Web.Tests;

/// <summary>
/// Inspect Web composes its own product vocabulary list. Its snapshot identity
/// is pinned to the same digest the CLI suite pins for the CLI composition, so
/// the two hosts' lists cannot drift apart without one suite failing.
/// </summary>
[SupportedOSPlatform("browser")]
public sealed class BrowserVocabularyCompositionTests
{
    private const string ProductSnapshotIdentity =
        "sha256:79f5a1cddcbabc41e23e85a96ecefbe582416382ec00fb79794f444e794c308e";

    [Fact]
    public void BrowserCompositionMatchesTheProductSnapshotIdentity()
    {
        Assert.Equal(1, BrowserVocabularyComposition.Snapshot.FormatVersion);
        Assert.Equal(
            ProductSnapshotIdentity,
            BrowserVocabularyComposition.Snapshot.Identity.Value);
    }

    [Fact]
    public void InspectVocabularyExportsTheBrowserComposedSnapshot()
    {
        BrowserVocabularyInspection? inspection = JsonSerializer.Deserialize(
            CatalogExports.InspectVocabulary(),
            BrowserCatalogJsonContext.Default.BrowserVocabularyInspection);

        Assert.NotNull(inspection);
        Assert.Equal(ProductSnapshotIdentity, inspection.Content.Identity.Value);
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
