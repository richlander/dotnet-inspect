using QuerySpace.Explanation;
using QuerySpace.Vocabulary;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Declares the C# style tiers and selectable style choices that
/// <see cref="StyleOptionCatalog"/> owns as value vocabularies, so a host can
/// compose them into its vocabulary snapshot without restating a tier, choice,
/// label, order, or property. The identity of each vocabulary carries the host's
/// catalog identity, so declarations are produced per catalog; their content is
/// a pure projection of the catalog and is deterministic.
/// </summary>
public static class StyleOptionVocabularies
{
    /// <summary>The stable identity of the style-tier vocabulary within a catalog.</summary>
    public const string StyleTiersId = "csharp.style-tiers";

    /// <summary>The stable identity of the style-choice vocabulary within a catalog.</summary>
    public const string StyleChoicesId = "csharp.style-choices";

    /// <summary>The display label of the style-tier vocabulary.</summary>
    public const string StyleTiersLabel = "C# Style Tiers";

    /// <summary>The display label of the style-choice vocabulary.</summary>
    public const string StyleChoicesLabel = "C# Style Choices";

    /// <summary>Declares <see cref="StyleOptionCatalog.Tiers"/> under <paramref name="catalog"/>.</summary>
    public static VocabularyDefinition DeclareStyleTiers(
        VocabularyCatalogIdentity catalog)
    {
        var identity = new VocabularyIdentity(catalog, StyleTiersId);
        VocabularyMapDefinition order = VocabularyMapDefinition.Scalar(
            identity,
            "order",
            "Order",
            "Product-owned presentation order.",
            ExplanationScalarKind.Integer);
        VocabularyMapDefinition byteDivergent = VocabularyMapDefinition.Scalar(
            identity,
            "byte_divergent",
            "Byte Divergent",
            "Whether every choice in the tier may change emitted IL bytes.",
            ExplanationScalarKind.Boolean);

        return new(
            identity,
            StyleTiersLabel,
            "Fidelity and presentation tiers used to group C# style choices.",
            [order, byteDivergent],
            StyleOptionCatalog.Tiers.Select(tier => new VocabularyTerm(
                new(identity, tier.Id.ToString()),
                tier.Title,
                tier.Summary,
                [
                    new(order, ExplanationValue.Integer(tier.Order)),
                    new(byteDivergent, ExplanationValue.Boolean(tier.ByteDivergent)),
                ])));
    }

    /// <summary>
    /// Declares <see cref="StyleOptionCatalog.Choices"/> under <paramref name="catalog"/>.
    /// The <c>tier</c> map targets the style-tier vocabulary declared by
    /// <see cref="DeclareStyleTiers"/> in the same catalog.
    /// </summary>
    public static VocabularyDefinition DeclareStyleChoices(
        VocabularyCatalogIdentity catalog)
    {
        var identity = new VocabularyIdentity(catalog, StyleChoicesId);
        var styleTiers = new VocabularyIdentity(catalog, StyleTiersId);
        VocabularyMapDefinition option = VocabularyMapDefinition.Scalar(
            identity,
            "option",
            "Option",
            "Owning style option identity.",
            ExplanationScalarKind.Text);
        VocabularyMapDefinition value = VocabularyMapDefinition.Scalar(
            identity,
            "value",
            "Value",
            "Selected value token on the owning option axis.",
            ExplanationScalarKind.Text);
        var tier = new VocabularyMapDefinition(
            new(identity, "tier"),
            "Tier",
            "Owning fidelity/presentation tier.",
            new VocabularyMapTarget.Terms(
                new VocabularyTermSetReference.Local(styleTiers)),
            VocabularyMapCardinality.ExactlyOne,
            VocabularyMapCoverage.Complete);
        VocabularyMapDefinition byteDivergent = VocabularyMapDefinition.Scalar(
            identity,
            "byte_divergent",
            "Byte Divergent",
            "Whether this choice may change emitted IL bytes.",
            ExplanationScalarKind.Boolean);
        VocabularyMapDefinition oracleEndorsed = VocabularyMapDefinition.Scalar(
            identity,
            "oracle_endorsed",
            "Oracle Endorsed",
            "Whether the declared runtime style oracle endorses this choice.",
            ExplanationScalarKind.Boolean);
        VocabularyMapDefinition corpusEndorsed = VocabularyMapDefinition.Scalar(
            identity,
            "corpus_endorsed",
            "Corpus Endorsed",
            "Whether the runtime source corpus endorses this choice.",
            ExplanationScalarKind.Boolean);
        VocabularyMapDefinition conflictGroup = VocabularyMapDefinition.Scalar(
            identity,
            "conflict_group",
            "Conflict Group",
            "Product-owned mutually-exclusive selection group.",
            ExplanationScalarKind.Text,
            VocabularyMapCardinality.OptionalOne);

        return new(
            identity,
            StyleChoicesLabel,
            "Selectable product-owned C# rendering choices.",
            [
                option,
                value,
                tier,
                byteDivergent,
                oracleEndorsed,
                corpusEndorsed,
                conflictGroup,
            ],
            StyleOptionCatalog.Choices.Select(choice => new VocabularyTerm(
                new(identity, choice.Id),
                choice.Title,
                choice.Summary,
                [
                    new(option, ExplanationValue.Text(choice.OptionId)),
                    new(value, ExplanationValue.Text(choice.ValueToken)),
                    new(
                        tier,
                        new ExplanationValue.VocabularyTerm(
                            new(styleTiers, choice.Tier.ToString()))),
                    new(byteDivergent, ExplanationValue.Boolean(choice.ByteDivergent)),
                    new(oracleEndorsed, ExplanationValue.Boolean(choice.OracleEndorsed)),
                    new(corpusEndorsed, ExplanationValue.Boolean(choice.CorpusEndorsed)),
                    choice.ConflictGroup is null
                        ? new(conflictGroup)
                        : new(conflictGroup, ExplanationValue.Text(choice.ConflictGroup)),
                ])));
    }
}
