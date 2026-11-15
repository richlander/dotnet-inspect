using System.Runtime.Versioning;
using DotnetInspector.Sections;

namespace DotnetInspect.Web.Interop.CallGraph;

/// <summary>
/// Maps <c>DotnetInspect.Web.Core</c>'s DTO-neutral call-graph projection onto this facade's own
/// wire records.
/// </summary>
[SupportedOSPlatform("browser")]
internal static class BrowserCallGraphWireProjection
{
    internal static BrowserCallGraph Project(
        InspectionEnvelope<
            PackageDependencyMemberCallGraphInspectionOutcome> envelope,
        string packageId,
        string version,
        string targetFramework,
        string memberName)
    {
        if (envelope.Content
            is PackageDependencyMemberCallGraphInspectionOutcome
                .Unavailable unavailable)
        {
            if (unavailable.Reason
                != PackageDependencyMemberCallGraphInspectionUnavailableReason
                    .FocusMethodBodyUnavailable)
            {
                throw new InvalidOperationException(
                    $"The dependency-aware member call graph was unavailable ({unavailable.Reason}): {unavailable.Detail}");
            }

            var node = new BrowserCallGraphNode(
                memberName, "None", false, null, [], "", "", memberName);
            return new BrowserCallGraph(
                "graph LR\n",
                node,
                node,
                new BrowserCallGraphScope(1, 0, 0, ""),
                [],
                [],
                Project(
                    new BrowserCallGraphDiagnosticsInfo(
                        0, 0, 0, false, false, false, 0, 0, [], 0),
                    envelope.Diagnostics),
                NoBody: true);
        }

        var available =
            (PackageDependencyMemberCallGraphInspectionOutcome.Available)
                envelope.Content;
        return Project(
            BrowserCallGraphProjection.Project(
                available.Document,
                packageId,
                version,
                targetFramework),
            envelope.Diagnostics);
    }

    internal static BrowserCallGraph Project(
        BrowserCallGraphInfo graph,
        IEnumerable<InspectionDiagnostic>? inspectionDiagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return new(
            graph.Mermaid,
            Project(graph.Callers),
            Project(graph.Callees),
            Project(graph.Scope),
            [.. graph.Targets.Select(Project)],
            [.. graph.Boundaries.Select(Project)],
            Project(graph.Diagnostics, inspectionDiagnostics),
            graph.NoBody);
    }

    internal static BrowserCallGraphNode Project(BrowserCallGraphNodeInfo node) =>
        new(
            node.Label,
            node.Status,
            node.InLoop,
            node.Source,
            [.. node.Children.Select(Project)],
            node.Assembly,
            node.TypeFullName,
            node.MemberName);

    internal static BrowserCallGraphScope Project(BrowserCallGraphScopeInfo scope) =>
        new(
            scope.Packages,
            scope.Assemblies,
            scope.CallerAssemblies,
            scope.CalleeScope);

    internal static BrowserCallGraphDiagnostics Project(
        BrowserCallGraphDiagnosticsInfo diagnostics,
        IEnumerable<InspectionDiagnostic>? inspectionDiagnostics = null) =>
        new(
            diagnostics.IncompleteNodes,
            diagnostics.IncompleteEdges,
            diagnostics.BindingIdentityConflicts,
            diagnostics.HasUnexploredTraversalBoundary,
            diagnostics.HasAnalysisFailureBoundary,
            inspectionDiagnostics?.Count(static diagnostic =>
                diagnostic.Code
                    == "package-dependency-member-call-graph.route-unavailable")
                ?? 0,
            diagnostics.HasIncompleteCorrespondence,
            diagnostics.UnclassifiedBoundaryEdges,
            diagnostics.UnclassifiedBoundaryNamedEdges,
            diagnostics.UnclassifiedBoundaryAssemblies,
            diagnostics.PhysicalOccurrenceUnavailableEdges);

    internal static BrowserCallGraphTarget Project(BrowserCallGraphTargetInfo target) =>
        new(
            target.Id,
            target.Assembly,
            target.AssemblyVersion,
            target.AssemblyCulture,
            target.AssemblyPublicKeyToken,
            target.TypeFullName,
            target.TypeMetadataId,
            target.TypeDefinitionId,
            target.MemberName,
            target.ParameterTypes,
            target.ReturnType,
            target.GenericArity,
            target.MetadataToken,
            target.SelectorKey,
            target.Kind,
            target.PlatformPack,
            target.SurfaceAssemblyId,
            target.PackageId,
            target.PackageVersion,
            target.PackageFramework)
        {
            OwnerKind = target.OwnerKind,
            PlatformFamily = target.PlatformFamily,
            PlatformFramework = target.PlatformFramework,
            PlatformVersion = target.PlatformVersion,
        };

    static BrowserCallGraphBoundary Project(
        BrowserCallGraphBoundaryInfo boundary) =>
        new(
            boundary.Id,
            boundary.SourcePackageId,
            boundary.SourcePackageVersion,
            boundary.SourcePackageFramework,
            boundary.SourceAssembly,
            boundary.TargetPackageId,
            boundary.TargetPackageVersion,
            boundary.TargetPackageFramework,
            boundary.TargetAssembly);
}
