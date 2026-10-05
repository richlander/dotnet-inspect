using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Vocabulary;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
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
            "sha256:f0527bd80f85c7683fcf3797da980ef38b3266116f375cc44323796775d884df",
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
    public void ProductSnapshotComposesEveryOwnerDeclaration()
    {
        VocabularySnapshot snapshot = VocabularyCatalog.Snapshot;
        var catalog = snapshot.Catalog;

        Assert.Equal(
            [
                "vocabulary.sections",
                ApiAccessibilityVocabulary.AccessibilityId,
                StyleOptionVocabularies.StyleTiersId,
                StyleOptionVocabularies.StyleChoicesId,
                BodyShapeVocabulary.BodyKindsId,
                PackageQueryDurableRowContract.Vocabulary,
            ],
            snapshot.Vocabularies.Select(vocabulary => vocabulary.Identity.Value));
        AssertComposed(snapshot, ApiAccessibilityVocabulary.Declare(catalog));
        AssertComposed(snapshot, StyleOptionVocabularies.DeclareStyleTiers(catalog));
        AssertComposed(snapshot, StyleOptionVocabularies.DeclareStyleChoices(catalog));
        AssertComposed(snapshot, BodyShapeVocabulary.Declare(catalog));
        AssertComposed(
            snapshot,
            PackageQueryDurableRowContract.DeclareVocabulary(catalog));
    }

    private static void AssertComposed(
        VocabularySnapshot snapshot,
        VocabularyDefinition declared)
    {
        VocabularyDefinition composed = snapshot.GetVocabulary(declared.Identity);
        Assert.Equal(declared.DisplayLabel, composed.DisplayLabel);
        Assert.Equal(declared.Summary, composed.Summary);
        Assert.Equal(
            declared.Maps.Select(map => map.Identity),
            composed.Maps.Select(map => map.Identity));
        Assert.Equal(
            declared.Terms.Select(term => term.Identity),
            composed.Terms.Select(term => term.Identity));
        Assert.Equal(
            declared.Terms.Select(term => term.DisplayLabel),
            composed.Terms.Select(term => term.DisplayLabel));
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
