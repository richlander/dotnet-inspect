using System;
using System.Linq;

using ILInspector.Decompiler.Pipeline;

using QuerySpace.Vocabulary;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The Decompiler declares its style tiers, style choices, and body kinds as value
/// vocabularies over a host-supplied catalog identity. These tests pin that each
/// declaration is a complete, ordered projection of the owning catalog and that
/// the choice-to-tier map resolves inside the same catalog, so a host composing
/// the declarations restates nothing.
/// </summary>
public class StyleOptionVocabulariesTests
{
    private static readonly VocabularyCatalogIdentity Catalog = new("test.catalog");

    [Fact]
    public void StyleTiersDeclareEveryTierInCatalogOrder()
    {
        VocabularyDefinition tiers = StyleOptionVocabularies.DeclareStyleTiers(Catalog);

        Assert.Equal(new VocabularyIdentity(Catalog, "csharp.style-tiers"), tiers.Identity);
        Assert.Equal("C# Style Tiers", tiers.DisplayLabel);
        Assert.Equal(
            StyleOptionCatalog.Tiers.Select(tier => tier.Id.ToString()),
            tiers.Terms.Select(term => term.Identity.Value));
        Assert.Equal(["order", "byte_divergent"], tiers.Maps.Select(map => map.Identity.Value));
        foreach ((StyleOptionTierDescriptor tier, VocabularyTerm term) in
            StyleOptionCatalog.Tiers.Zip(tiers.Terms))
        {
            Assert.Equal(tier.Title, term.DisplayLabel);
            Assert.Equal(tier.Summary, term.Summary);
            var order = Assert.IsType<VocabularyMapValue.Scalar>(
                Assert.Single(term.GetRequiredValues(tiers.GetMap("order").Identity)));
            Assert.Equal(tier.Order, order.Value.Integer);
        }
    }

    [Fact]
    public void StyleChoicesDeclareEveryChoiceAndTargetTheTierVocabulary()
    {
        VocabularyDefinition tiers = StyleOptionVocabularies.DeclareStyleTiers(Catalog);
        VocabularyDefinition choices = StyleOptionVocabularies.DeclareStyleChoices(Catalog);

        Assert.Equal(new VocabularyIdentity(Catalog, "csharp.style-choices"), choices.Identity);
        Assert.Equal(
            StyleOptionCatalog.Choices.Select(choice => choice.Id),
            choices.Terms.Select(term => term.Identity.Value));

        VocabularyMapDefinition tier = choices.GetMap("tier");
        var target = Assert.IsType<VocabularyMapTarget.Terms>(tier.Target);
        var local = Assert.IsType<VocabularyTermSetReference.Local>(target.Reference);
        Assert.Equal(tiers.Identity, local.Vocabulary);

        VocabularySnapshot snapshot = VocabularySnapshot.Create(1, Catalog, [tiers, choices]);
        foreach ((StyleOptionChoice choice, VocabularyTerm term) in
            StyleOptionCatalog.Choices.Zip(choices.Terms))
        {
            var value = Assert.IsType<VocabularyMapValue.Term>(
                Assert.Single(term.GetRequiredValues(tier.Identity)));
            Assert.Equal(choice.Tier.ToString(), snapshot.GetTerm(value.Identity).Identity.Value);
            Assert.Equal(
                choice.ConflictGroup is null ? 0 : 1,
                term.GetRequiredValues(choices.GetMap("conflict_group").Identity).Length);
        }
    }

    [Fact]
    public void BodyKindsDeclareEverySupportedKindWithItsDisplayLabel()
    {
        VocabularyDefinition kinds = BodyShapeVocabulary.Declare(Catalog);

        Assert.Equal(new VocabularyIdentity(Catalog, "csharp.body-kinds"), kinds.Identity);
        Assert.Equal("C# Body Kinds", kinds.DisplayLabel);
        Assert.Empty(kinds.Maps);
        Assert.Equal(BodyShapeSearch.SupportedKinds, kinds.Terms.Select(term => term.Identity.Value));
        Assert.All(
            kinds.Terms,
            term => Assert.Equal(
                AnnotatedSourceNodeKinds.GetDisplayLabel(term.Identity.Value),
                term.DisplayLabel));
    }

    [Fact]
    public void DeclarationsAreDeterministicAcrossCalls()
    {
        VocabularySnapshot first = VocabularySnapshot.Create(
            1,
            Catalog,
            [
                StyleOptionVocabularies.DeclareStyleTiers(Catalog),
                StyleOptionVocabularies.DeclareStyleChoices(Catalog),
                BodyShapeVocabulary.Declare(Catalog),
            ]);
        VocabularySnapshot second = VocabularySnapshot.Create(
            1,
            Catalog,
            [
                StyleOptionVocabularies.DeclareStyleTiers(Catalog),
                StyleOptionVocabularies.DeclareStyleChoices(Catalog),
                BodyShapeVocabulary.Declare(Catalog),
            ]);

        Assert.Equal(first.Identity, second.Identity);
    }
}
