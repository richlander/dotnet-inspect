using System.Text.Json;

using DotnetInspector.Queries;
using DotnetInspector.Sections;

namespace DotnetInspect.Web.Interop.Analysis;

internal static class BrowserAnalysisInspectionProjection
{
    internal static InspectionEnvelope<JsonElement> Project(
        InspectionEnvelope<AssemblyIntegrationsEntry> inspection)
    {
        ArgumentNullException.ThrowIfNull(inspection);

        return new(
            JsonSerializer.SerializeToElement(
                inspection.Content,
                AssemblyIntegrationsInspectionJsonContext.Default
                    .AssemblyIntegrationsEntry),
            inspection.Share,
            inspection.Diagnostics);
    }

    internal static InspectionEnvelope<JsonElement> Project(
        InspectionEnvelope<
            AssemblyIntegrationOpportunitiesInspectionResult> inspection)
    {
        ArgumentNullException.ThrowIfNull(inspection);

        return new(
            JsonSerializer.SerializeToElement(
                inspection.Content,
                AssemblyIntegrationsInspectionJsonContext.Default
                    .AssemblyIntegrationOpportunitiesInspectionResult),
            inspection.Share,
            inspection.Diagnostics);
    }
}
