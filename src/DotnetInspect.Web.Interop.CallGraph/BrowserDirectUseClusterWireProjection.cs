using System.Runtime.Versioning;

using DotnetInspect.Web;

namespace DotnetInspect.Web.Interop.CallGraph;

[SupportedOSPlatform("browser")]
internal static class BrowserDirectUseClusterWireProjection
{
    internal static BrowserDirectUseClusterInspection Project(
        BrowserDirectUseClusterInspectionInfo inspection)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        return new(
            inspection.Outcome,
            inspection.IsComplete,
            [.. inspection.Clusters.Select(Project)],
            inspection.SelectedCluster,
            [.. inspection.CallSites.Select(Project)],
            [.. inspection.Diagnostics.Select(Project)],
            inspection.Failure);
    }

    static BrowserDirectUseCluster Project(
        BrowserDirectUseClusterInfo cluster) =>
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

    static BrowserDirectUseCallSite Project(
        BrowserDirectUseCallSiteInfo callSite) =>
        new(
            Project(callSite.Source),
            callSite.SourceMember,
            callSite.SourceToken,
            Project(callSite.Target),
            callSite.TargetMember,
            callSite.TargetToken,
            callSite.CallKind,
            callSite.EvidenceMethod,
            callSite.EvidenceModuleVersionId,
            callSite.EvidenceToken,
            callSite.IlOffset);

    static BrowserDirectUseLibrary Project(
        BrowserDirectUseLibraryInfo library) =>
        new(
            library.Name,
            library.Version,
            library.Culture,
            library.PublicKeyToken,
            library.ModuleVersionId);

    static BrowserDirectUseDiagnostic Project(
        BrowserDirectUseDiagnosticInfo diagnostic) =>
        new(
            diagnostic.Code,
            diagnostic.Severity,
            diagnostic.Summary,
            diagnostic.Correspondence);
}
