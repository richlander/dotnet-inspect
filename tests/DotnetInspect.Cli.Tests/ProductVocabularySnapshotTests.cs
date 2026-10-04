using DotnetInspector.Sections;
using DotnetInspector.Vocabulary;
using QuerySpace.Vocabulary;

namespace DotnetInspect.Cli.Tests;

public sealed class ProductVocabularySnapshotTests
{
    [Fact]
    public void ProductSnapshotAuthenticatesStyleChoiceTierMap()
    {
        VocabularySnapshot snapshot = VocabularyCatalog.Snapshot;
        VocabularyDefinition choices = snapshot.GetVocabulary(
            new(snapshot.Catalog, "csharp.style-choices"));
        VocabularyMapDefinition tier = choices.GetMap("tier");

        Assert.Equal(1, snapshot.FormatVersion);
        Assert.Equal(
            "sha256:738fb458557d93aa6681947f43cc9ba9315260177f7bbe8af83c02d49b437f36",
            snapshot.Identity.Value);
        Assert.Equal(
            VocabularyMapCardinality.ExactlyOne,
            tier.Cardinality);
        Assert.Equal(VocabularyMapCoverage.Complete, tier.Coverage);
        var target = Assert.IsType<VocabularyMapTarget.Terms>(tier.Target);
        var local = Assert.IsType<VocabularyTermSetReference.Local>(
            target.Reference);
        Assert.Equal("csharp.style-tiers", local.Vocabulary.Value);

        foreach (VocabularyTerm choice in choices.Terms)
        {
            VocabularyMapValue.Term value = Assert.IsType<
                VocabularyMapValue.Term>(
                Assert.Single(choice.GetRequiredValues(tier.Identity)));
            Assert.Same(
                snapshot.GetTerm(value.Identity),
                snapshot.GetVocabulary(local.Vocabulary)
                    .GetTerm(value.Identity.Value));
        }
    }

    [Fact]
    public void ProductInspectionReturnsTheSharedStaticSnapshot()
    {
        InspectionEnvelope<VocabularySnapshot> inspection =
            ProductVocabularyInspection.Execute();

        Assert.Same(VocabularyCatalog.Snapshot, inspection.Content);
        var share = Assert.IsType<InspectionShare.NonProjectable>(
            inspection.Share);
        Assert.Equal("vocabulary/share", share.Path);
        Assert.Empty(inspection.Diagnostics);
    }
}
