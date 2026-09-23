using DotnetInspector.DocumentationHouse;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;

namespace DotnetInspector.Sections;

/// <summary>
/// Returns package documentation settlement through the shared host-neutral
/// inspection handoff.
/// </summary>
public static class PackageDocumentationInspection
{
    public static async ValueTask<
        InspectionEnvelope<DocumentationQueryOutcome>>
        ExecuteAsync(
            PackageHouseSettlement.Acquired settlement,
            PackageHouseLibraryHandoff.Compile handoff,
            string documentationId,
            DocumentationDemand demand,
            AssemblyContextSourceQueryContext? sourceContext = null,
            PackageDocumentationQueryLimits? limits = null,
            CancellationToken cancellationToken = default)
    {
        DocumentationQueryOutcome content =
            await PackageDocumentationQuery.ExecuteAsync(
                    settlement,
                    handoff,
                    documentationId,
                    demand,
                    sourceContext,
                    limits,
                    cancellationToken)
                .ConfigureAwait(false);
        return new(
            content,
            new InspectionShare.NonProjectable(
                "package/documentation",
                "Package documentation requests do not define a standalone share projection."));
    }

    public static async ValueTask<
        InspectionEnvelope<
            IReadOnlyDictionary<string, DocumentationQueryOutcome>>>
        ExecuteManyAsync(
            PackageHouseSettlement.Acquired settlement,
            PackageHouseLibraryHandoff.Compile handoff,
            IReadOnlyCollection<string> documentationIds,
            DocumentationDemand demand,
            AssemblyContextSourceQueryContext? sourceContext = null,
            PackageDocumentationQueryLimits? limits = null,
            CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<string, DocumentationQueryOutcome> content =
            await PackageDocumentationQuery.ExecuteManyAsync(
                    settlement,
                    handoff,
                    documentationIds,
                    demand,
                    sourceContext,
                    limits,
                    cancellationToken)
                .ConfigureAwait(false);
        return new(
            content,
            new InspectionShare.NonProjectable(
                "package/documentation",
                "Package documentation requests do not define a standalone share projection."));
    }
}
