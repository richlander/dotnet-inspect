using System.Runtime.Versioning;

using DotnetInspector.Presentation;
using DotnetInspector.Sections;

namespace DotnetInspect.Web;

internal sealed record BrowserDirectUseClusterInfo(
    int Ordinal,
    BrowserDirectUseLibraryInfo Source,
    BrowserDirectUseLibraryInfo Target,
    int AnchorSourceToken,
    int AnchorTargetToken,
    int SourceMembers,
    int ProviderTypes,
    int TargetMembers,
    int ExtensionMethods,
    int CallSites);

internal sealed record BrowserDirectUseCallSiteInfo(
    BrowserDirectUseLibraryInfo Source,
    string SourceMember,
    int SourceToken,
    BrowserDirectUseLibraryInfo Target,
    string TargetMember,
    int TargetToken,
    string CallKind,
    string EvidenceMethod,
    string EvidenceModuleVersionId,
    int EvidenceToken,
    int IlOffset);

internal sealed record BrowserDirectUseLibraryInfo(
    string Name,
    string? Version,
    string? Culture,
    string? PublicKeyToken,
    string ModuleVersionId);

internal sealed record BrowserDirectUseDiagnosticInfo(
    string Code,
    string Severity,
    string Summary,
    string? Correspondence);

internal sealed record BrowserDirectUseClusterInspectionInfo(
    string Outcome,
    bool IsComplete,
    BrowserDirectUseClusterInfo[] Clusters,
    int? SelectedCluster,
    BrowserDirectUseCallSiteInfo[] CallSites,
    BrowserDirectUseDiagnosticInfo[] Diagnostics,
    string? Failure);

/// <summary>
/// DTO-neutral Browser projection over the host-neutral pair and Direct-Use
/// Cluster inspections.
/// </summary>
[SupportedOSPlatform("browser")]
internal static class BrowserDirectUseClusterProjection
{
    internal static BrowserDirectUseClusterInspectionInfo Project(
        InspectionEnvelope<AssemblyPairDirectUseClusterInspectionOutcome>
            inspection,
        int? selectedCluster)
    {
        ArgumentNullException.ThrowIfNull(inspection);

        BrowserDirectUseDiagnosticInfo[] diagnostics =
        [
            .. inspection.Diagnostics.Select(Project),
        ];
        DirectUseClusterPresentationResult presentation =
            AssemblyPairDirectUseClusterPresentation.Project(
                inspection,
                selectedCluster);

        return new(
            Outcome(presentation.Status),
            presentation.IsComplete,
            [.. presentation.Clusters.Select(Project)],
            presentation.SelectedCluster,
            [.. presentation.CallSites.Select(Project)],
            diagnostics,
            presentation.Failure);
    }

    static BrowserDirectUseClusterInfo Project(
        DirectUseClusterPresentation cluster) =>
        new(
            cluster.Ordinal,
            Project(cluster.Source),
            Project(cluster.Target),
            cluster.AnchorSourceToken,
            cluster.AnchorTargetToken,
            cluster.SourceMembers,
            cluster.ProviderTypes,
            cluster.TargetMembers,
            cluster.ExtensionMethods,
            cluster.CallSites);

    static BrowserDirectUseCallSiteInfo Project(
        DirectUseCallSitePresentation occurrence) =>
        new(
            Project(occurrence.Source),
            occurrence.SourceMember,
            occurrence.SourceToken,
            Project(occurrence.Target),
            occurrence.TargetMember,
            occurrence.TargetToken,
            occurrence.CallKind,
            occurrence.EvidenceMethod,
            occurrence.EvidenceModuleVersionId.ToString("D"),
            occurrence.EvidenceToken,
            occurrence.IlOffset);

    static BrowserDirectUseLibraryInfo Project(
        DirectUseLibraryPresentation library) =>
        new(
            library.Name,
            library.Version,
            library.Culture,
            library.PublicKeyToken,
            library.ModuleVersionId.ToString("D"));

    static BrowserDirectUseDiagnosticInfo Project(
        InspectionDiagnostic diagnostic) =>
        new(
            diagnostic.Code,
            diagnostic.Severity.ToString().ToLowerInvariant(),
            diagnostic.Summary.ToString(),
            diagnostic.Correspondence?.ToString());

    static string Outcome(DirectUseClusterPresentationStatus status) =>
        status switch
        {
            DirectUseClusterPresentationStatus.Available => "available",
            DirectUseClusterPresentationStatus.ClusterNotFound =>
                "cluster-not-found",
            DirectUseClusterPresentationStatus.Rejected => "rejected",
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };
}
