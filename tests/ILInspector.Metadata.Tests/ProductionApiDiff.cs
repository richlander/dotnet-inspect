using Inspector.Findings;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// The production API comparer as its hosts call it: <see cref="MetadataFindings.CompareApi"/>
/// classifies through <see cref="ApiFindingClassifier"/>, with the extension/instance member
/// tier accepted, exactly as <c>ApiComparisonQuery</c> (<c>diff --library</c>, Inspect Web) and
/// <c>ResearchDiff</c> (<c>diff --analysis api</c>) do. Tests assert its <see cref="ApiDiff"/>
/// outcome.
/// </summary>
static class ProductionApiDiff
{
    static readonly FindingSubject Subject = new("api", "API surface");

    public static ApiDiff Compare(
        ApiSurface oldSurface,
        ApiSurface newSurface,
        ApiDiffOptions? options = null)
        => MetadataFindings.CompareApi(
            oldSurface,
            newSurface,
            Subject,
            options ?? ApiDiffOptions.Default,
            memberAcceptanceThreshold: MetadataFindings.ExtensionInstanceMatchTier.Confidence)
            .ApiDiff;
}
