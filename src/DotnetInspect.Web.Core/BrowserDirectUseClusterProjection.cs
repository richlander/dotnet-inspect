using System.Runtime.Versioning;

using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Analysis;
using ILInspector.Metadata;

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
        if (selectedCluster is < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(selectedCluster),
                "A selected Direct-Use Cluster ordinal must be positive.");
        }

        BrowserDirectUseDiagnosticInfo[] diagnostics =
        [
            .. inspection.Diagnostics.Select(Project),
        ];
        if (inspection.Content
            is AssemblyPairDirectUseClusterInspectionOutcome.Rejected rejected)
        {
            return new(
                "rejected",
                IsComplete: false,
                [],
                selectedCluster,
                [],
                diagnostics,
                rejected.Pair.Detail);
        }

        var available =
            (AssemblyPairDirectUseClusterInspectionOutcome.Available)
                inspection.Content;
        AssemblyPairDirectUseClusterProjection projection =
            available.Projection;
        AssemblyPairDirectUseClusterProjection? scoped =
            selectedCluster is int ordinal
                ? projection.ScopeToObservedCluster(ordinal)
                : null;
        if (selectedCluster is not null && scoped is null)
        {
            return new(
                "cluster-not-found",
                projection.IsComplete,
                [.. projection.Clusters.Select(Project)],
                selectedCluster,
                [],
                diagnostics,
                projection.Clusters.IsEmpty
                    ? "No Direct-Use Clusters were observed for this Library pair."
                    : "The selected Direct-Use Cluster ordinal was not observed.");
        }

        return new(
            "available",
            projection.IsComplete,
            [.. projection.Clusters.Select(Project)],
            selectedCluster,
            scoped is null
                ? []
                : [.. scoped.Pair.Occurrences.Select(Project)],
            diagnostics,
            Failure: null);
    }

    static BrowserDirectUseClusterInfo Project(
        AssemblyPairDirectUseCluster cluster) =>
        new(
            cluster.Ordinal,
            Project(
                cluster.Identity.Source,
                cluster.Identity.SourceModuleVersionId),
            Project(
                cluster.Identity.Target,
                cluster.Identity.TargetModuleVersionId),
            cluster.Identity.AnchorSourceMethodToken,
            cluster.Identity.AnchorTargetMethodToken,
            cluster.SourceMethods.Length,
            cluster.TargetTypes.Length,
            cluster.TargetMethods.Length,
            cluster.ExtensionMethodCount,
            cluster.CallSiteCount);

    static BrowserDirectUseCallSiteInfo Project(
        AssemblyPairCallUseOccurrence occurrence) =>
        new(
            Project(occurrence.Source, occurrence.SourceModuleVersionId),
            FormatMethod(occurrence.SourceMethod),
            occurrence.SourceMethod.MetadataToken,
            Project(occurrence.Target, occurrence.TargetModuleVersionId),
            FormatMethod(occurrence.TargetMethod),
            occurrence.TargetMethod.MetadataToken,
            occurrence.Call.Kind switch
            {
                CallKind.Call => "call",
                CallKind.CallVirtual => "callvirt",
                CallKind.NewObject => "newobj",
                _ => occurrence.Call.Kind.ToString(),
            },
            FormatMethod(occurrence.Call.EvidenceMethod),
            occurrence.Call.EvidenceMethod.ModuleVersionId.ToString("D"),
            occurrence.Call.EvidenceMethod.MetadataToken,
            occurrence.Call.ILOffset);

    static BrowserDirectUseLibraryInfo Project(
        AssemblyContextSubject subject,
        Guid moduleVersionId) =>
        new(
            subject.Identity.Name,
            subject.Identity.Version?.ToString(),
            subject.Identity.Culture,
            subject.Identity.PublicKeyToken,
            moduleVersionId.ToString("D"));

    static BrowserDirectUseDiagnosticInfo Project(
        InspectionDiagnostic diagnostic) =>
        new(
            diagnostic.Code,
            diagnostic.Severity.ToString().ToLowerInvariant(),
            diagnostic.Summary.ToString(),
            diagnostic.Correspondence?.ToString());

    static string FormatMethod(MethodIdentity method) =>
        $"{method.DeclaringType.ToQualifiedDisplayString()}.{method.Name}("
            + $"{string.Join(", ", method.ParameterTypes.Select(
                parameter => parameter.ToQualifiedDisplayString()))})";
}
