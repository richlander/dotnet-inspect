using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using QuerySpace.Vocabulary;

namespace DotnetInspect.Web.Interop.Catalog;

/// <summary>
/// The product vocabularies Inspect Web ships, composed from their owners'
/// declarations. The CLI composes its own list; the two hosts' snapshot
/// identities are pinned to the same digest by their test suites.
/// </summary>
internal static class BrowserVocabularyComposition
{
    /// <summary>The contributions Inspect Web ships, in section order.</summary>
    internal static ProductVocabularyContribution[] CreateContributions()
    {
        VocabularyCatalogIdentity catalog = ProductVocabularyComposition.Catalog;
        return
        [
            new(
                ApiAccessibilityVocabulary.Declare(catalog),
                "api.type-inventory",
                "api.member-inventory"),
            new(
                StyleOptionVocabularies.DeclareStyleTiers(catalog),
                "decompiler.style-picker"),
            new(
                StyleOptionVocabularies.DeclareStyleChoices(catalog),
                "decompiler.style-picker",
                "decompiler.render"),
            new(
                BodyShapeVocabulary.Declare(catalog),
                "decompiler.body-kind"),
            new(
                PackageQueryDurableRowContract.DeclareVocabulary(catalog),
                PackageQueryDurableRowContract.ContractIdentity),
        ];
    }

    /// <summary>Inspect Web's exact product vocabulary snapshot.</summary>
    internal static VocabularySnapshot Snapshot { get; } =
        ProductVocabularyComposition.Compose(CreateContributions());
}
