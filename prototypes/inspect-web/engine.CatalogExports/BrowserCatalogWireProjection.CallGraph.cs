namespace InspectWeb.Engine.CatalogFacade;

internal static partial class BrowserCatalogWireProjection
{
    internal static BrowserCallGraph Project(BrowserCallGraphInfo graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return new(
            graph.Mermaid,
            Project(graph.Callers),
            Project(graph.Callees),
            Project(graph.Scope),
            [.. graph.Targets.Select(Project)],
            Project(graph.Diagnostics),
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
        BrowserCallGraphDiagnosticsInfo diagnostics) =>
        new(
            diagnostics.IncompleteNodes,
            diagnostics.IncompleteEdges,
            diagnostics.BindingIdentityConflicts,
            diagnostics.HasUnexploredTraversalBoundary,
            diagnostics.HasAnalysisFailureBoundary);

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
            target.SurfaceAssemblyId);
}
