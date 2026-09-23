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

        InspectionEnvelope<DocumentationQueryOutcome> inspection =
            await PackageDocumentationInspection.ExecuteAsync(
                    settlement,
                    handoff,
                    documentationId,
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
