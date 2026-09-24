using System.Text.Json;

using DotnetInspector.Queries;
using DotnetInspector.Sections;

namespace DotnetInspect.Web.Interop.Analysis;

internal static class BrowserAnalysisInspectionProjection
{
    internal static BrowserAnalysisInspectionEnvelope Project(
        InspectionEnvelope<AssemblyIntegrationsEntry> inspection)
    {
        ArgumentNullException.ThrowIfNull(inspection);

        return new(
            JsonSerializer.SerializeToElement(
                inspection.Content,
                AssemblyIntegrationsInspectionJsonContext.Default
                    .AssemblyIntegrationsEntry),
            Project(inspection.Share),
            [.. inspection.Diagnostics.Select(Project)]);
    }

    internal static BrowserAnalysisInspectionEnvelope Project(
        InspectionEnvelope<
            AssemblyIntegrationOpportunitiesInspectionResult> inspection)
    {
        ArgumentNullException.ThrowIfNull(inspection);

        return new(
            JsonSerializer.SerializeToElement(
                inspection.Content,
                AssemblyIntegrationsInspectionJsonContext.Default
                    .AssemblyIntegrationOpportunitiesInspectionResult),
            Project(inspection.Share),
            [.. inspection.Diagnostics.Select(Project)]);
    }

    internal static BrowserAnalysisInspectionShare Project(
        InspectionShare share) =>
        share switch
        {
            InspectionShare.Available available =>
                new(
                    "available",
                    available.FullUrl,
                    available.Packet,
                    Path: null,
                    Reason: null),
            InspectionShare.NonProjectable nonProjectable =>
                new(
                    "nonProjectable",
                    FullUrl: null,
                    Packet: null,
                    nonProjectable.Path,
                    nonProjectable.Reason.ToString()),
            _ => throw new InvalidOperationException(
                "Unknown inspection Share outcome."),
        };

    internal static BrowserAnalysisInspectionDiagnostic Project(
        InspectionDiagnostic diagnostic) =>
        new(
            diagnostic.Code,
            diagnostic.Severity switch
            {
                InspectionDiagnosticSeverity.Information =>
                    BrowserAnalysisInspectionDiagnosticSeverity.Information,
                InspectionDiagnosticSeverity.Warning =>
                    BrowserAnalysisInspectionDiagnosticSeverity.Warning,
                InspectionDiagnosticSeverity.Error =>
                    BrowserAnalysisInspectionDiagnosticSeverity.Error,
                _ => throw new InvalidOperationException(
                    "Unknown inspection diagnostic severity."),
            },
            diagnostic.Summary.ToString(),
            diagnostic.Correspondence?.ToString());
}
