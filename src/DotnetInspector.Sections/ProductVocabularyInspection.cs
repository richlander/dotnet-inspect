using DotnetInspector.Vocabulary;

namespace DotnetInspector.Sections;

/// <summary>Completes the static Product Vocabulary as a host-neutral inspection.</summary>
public static class ProductVocabularyInspection
{
    public static InspectionEnvelope<VocabularySnapshot> Execute() =>
        new(
            VocabularyCatalog.Snapshot,
            new InspectionShare.NonProjectable(
                "vocabulary/share",
                "The static Product Vocabulary catalog does not define a "
                + "Workspace Share projection."));
}
