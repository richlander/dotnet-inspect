using DotnetInspector.Queries;
using DotnetInspector.Sections;

namespace DotnetInspector.Ecosystems;

/// <summary>
/// Evaluates admitted Ecosystem Find artifacts for one normalized Type
/// question and returns the completed host-neutral inspection result.
/// </summary>
public static class EcosystemFindIndexedTypeInspection
{
    private static readonly InspectionShare Share =
        new InspectionShare.NonProjectable(
            "ecosystem-find/indexed-type/share",
            "Indexed Ecosystem Type Find requests do not yet have a "
                + "canonical Workspace Share projection.");

    public static InspectionEnvelope<
        EcosystemFindIndexedIntegrationResult.Type> Execute(
        IReadOnlyList<EcosystemFindIndexArtifact> artifacts,
        IReadOnlyList<EcosystemFindIndexedIntegrationSourceIdentity>
            demandedSources,
        TypeFindQuestion question,
        int? maximumRows,
        string? typeFilter = null,
        CancellationToken cancellationToken = default)
    {
        var request = new EcosystemFindIndexedIntegrationRequest(
            artifacts,
            demandedSources,
            question,
            maximumRows,
            typeFilter);
        var content =
            (EcosystemFindIndexedIntegrationResult.Type)
                EcosystemFindIndexedIntegration.Evaluate(
                    request,
                    cancellationToken);
        return new(content, Share);
    }
}
