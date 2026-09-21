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
            inspection.ResourcePath.Value,
            Project(inspection.ContentKind),
            JsonSerializer.SerializeToElement(
                inspection.Content,
                AssemblyIntegrationsInspectionJsonContext.Default
                    .AssemblyIntegrationsEntry),
            Project(inspection.PortableProjection),
            [.. inspection.Diagnostics.Select(Project)]);
    }

    internal static BrowserAnalysisInspectionEnvelope Project(
        InspectionEnvelope<
            AssemblyIntegrationOpportunitiesInspectionResult> inspection)
    {
        ArgumentNullException.ThrowIfNull(inspection);

        return new(
            inspection.ResourcePath.Value,
            Project(inspection.ContentKind),
            JsonSerializer.SerializeToElement(
                inspection.Content,
                AssemblyIntegrationsInspectionJsonContext.Default
                    .AssemblyIntegrationOpportunitiesInspectionResult),
            Project(inspection.PortableProjection),
            [.. inspection.Diagnostics.Select(Project)]);
    }

    static BrowserAnalysisInspectionPortableProjection Project(
        InspectionPortableProjection portableProjection) =>
        portableProjection switch
        {
            InspectionPortableProjection.Available available =>
                new(
                    "available",
                    available.FullUrl,
                    available.Packet,
                    Location: null,
                    Reason: null,
                    Explanation: null),
            InspectionPortableProjection.NonProjectable nonProjectable =>
                new(
                    "nonProjectable",
                    FullUrl: null,
                    Packet: null,
                    nonProjectable.Location,
                    Project(nonProjectable.Reason),
                    nonProjectable.Explanation),
            _ => throw new InvalidOperationException(
                "Unknown inspection portable projection."),
        };

    static BrowserAnalysisInspectionContentKind Project(
        InspectionContentKind contentKind) =>
        contentKind switch
        {
            InspectionContentKind.Result =>
                BrowserAnalysisInspectionContentKind.Result,
            InspectionContentKind.Document =>
                BrowserAnalysisInspectionContentKind.Document,
            InspectionContentKind.Outcome =>
                BrowserAnalysisInspectionContentKind.Outcome,
            _ => throw new InvalidOperationException(
                "Unknown inspection content kind."),
        };

    static BrowserAnalysisInspectionPortableProjectionFailureReason Project(
        InspectionPortableProjectionFailureReason reason) =>
        reason switch
        {
            InspectionPortableProjectionFailureReason.NotSupported =>
                BrowserAnalysisInspectionPortableProjectionFailureReason
                    .NotSupported,
            InspectionPortableProjectionFailureReason.Invalid =>
                BrowserAnalysisInspectionPortableProjectionFailureReason
                    .Invalid,
            InspectionPortableProjectionFailureReason.Incomplete =>
                BrowserAnalysisInspectionPortableProjectionFailureReason
                    .Incomplete,
            InspectionPortableProjectionFailureReason.Unavailable =>
                BrowserAnalysisInspectionPortableProjectionFailureReason
                    .Unavailable,
            InspectionPortableProjectionFailureReason.Failed =>
                BrowserAnalysisInspectionPortableProjectionFailureReason.Failed,
            _ => throw new InvalidOperationException(
                "Unknown portable projection failure reason."),
        };

    static BrowserAnalysisInspectionDiagnostic Project(
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
