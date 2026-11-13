using System.Runtime.Versioning;

using DotnetInspector.DocumentationHouse;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;

namespace DotnetInspect.Web;

[SupportedOSPlatform("browser")]
internal static class BrowserPackageDocumentationQuery
{
    internal static async ValueTask<DocumentationQueryOutcome> ExecuteAsync(
        PackageHouseSettlement.Acquired settlement,
        PackageHouseLibraryHandoff.Compile handoff,
        string documentationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        ArgumentNullException.ThrowIfNull(handoff);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentationId);

        IReadOnlyDictionary<string, DocumentationQueryOutcome> outcomes =
            await ExecuteManyAsync(
                    settlement,
                    handoff,
                    [documentationId],
                    cancellationToken)
                .ConfigureAwait(false);
        return outcomes.TryGetValue(
            documentationId,
            out DocumentationQueryOutcome? outcome)
                ? outcome
                : throw new InvalidOperationException(
                    "The Package documentation query did not return the "
                        + "requested exact subject.");
    }

    internal static async ValueTask<
        IReadOnlyDictionary<string, DocumentationQueryOutcome>>
        ExecuteManyAsync(
            PackageHouseSettlement.Acquired settlement,
            PackageHouseLibraryHandoff.Compile handoff,
            IReadOnlyCollection<string> documentationIds,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        ArgumentNullException.ThrowIfNull(handoff);
        ArgumentNullException.ThrowIfNull(documentationIds);

        InspectionEnvelope<
            IReadOnlyDictionary<string, DocumentationQueryOutcome>>
            inspection =
                await PackageDocumentationInspection.ExecuteManyAsync(
                        settlement,
                        handoff,
                        documentationIds,
                        DocumentationDemand
                            .CompiledXmlAndAuthoredSourceDocumentation,
                        BrowserSourceQueryContext.Create(),
                        new PackageDocumentationQueryLimits
                        {
                            ApiSurface =
                                BrowserApiSurfacePolicy.ExtractionBounds,
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
        return inspection.Content;
    }
}
