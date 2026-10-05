using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using QuerySpace.Vocabulary;

namespace TsJsExport;

internal static class TsJsExportVocabularyComposition
{
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

    internal static VocabularySnapshot Snapshot { get; } =
        ProductVocabularyComposition.Compose(CreateContributions());
}
