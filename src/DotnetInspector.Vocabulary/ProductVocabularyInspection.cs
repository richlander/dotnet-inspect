using DotnetInspector.Sections;
using QuerySpace.Vocabulary;

namespace DotnetInspector.Vocabulary;

/// <summary>Completes a host-composed Product Vocabulary as a host-neutral inspection.</summary>
public static class ProductVocabularyInspection
{
    public static InspectionEnvelope<VocabularySnapshot> Execute(
        VocabularySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new(
            snapshot,
            new InspectionShare.NonProjectable(
                "vocabulary/share",
                "The static Product Vocabulary catalog does not define a "
                + "Workspace Share projection."));
    }
}
