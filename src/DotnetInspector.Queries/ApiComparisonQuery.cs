using ILInspector.Findings;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>
/// Compares two already-resolved API surfaces and retains both their Finding correspondence and
/// Metadata-owned compatibility classification.
/// </summary>
public static class ApiComparisonQuery
{
    public static InspectionQuery<ApiFindingComparison> Definition { get; } =
        new("API comparison", InspectionCost.NetworkFree);

    public static ApiFindingComparison Execute(ApiSurface oldSurface, ApiSurface newSurface)
    {
        ArgumentNullException.ThrowIfNull(oldSurface);
        ArgumentNullException.ThrowIfNull(newSurface);

        return MetadataFindings.CompareApi(
            oldSurface,
            newSurface,
            new FindingSubject("api", "API surface"),
            new ApiDiffOptions(ApiDiffScope.Signature),
            memberAcceptanceThreshold: MetadataFindings.ExtensionInstanceMatchTier.Confidence);
    }
}
