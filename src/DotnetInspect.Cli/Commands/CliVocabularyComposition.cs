using DotnetInspector.InspectionContracts;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using QuerySpace.Vocabulary;

namespace DotnetInspect.Cli.Commands;

/// <summary>
/// The product vocabularies the CLI ships, composed from their owners'
/// declarations. Inspect Web composes its own list; the two hosts' snapshot
/// identities are pinned to the same digest by their test suites.
/// </summary>
internal static class CliVocabularyComposition
{
    /// <summary>The contributions the CLI ships, in section order.</summary>
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
                PackageQueryDurableRowVocabulary.Declare(catalog),
                PackageQueryDurableRowContract.ContractIdentity),
        ];
    }

    /// <summary>The CLI's exact product vocabulary snapshot.</summary>
    internal static VocabularySnapshot Snapshot { get; } =
        ProductVocabularyComposition.Compose(CreateContributions());
}
