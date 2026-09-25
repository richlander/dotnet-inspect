using System.Text.Json.Serialization;

namespace DotnetInspect.Web.Interop.CallGraph;

/// <summary>
/// The call-graph facade's browser wire contract.
/// </summary>
/// <remarks>
/// Every record here is declared and source-generated inside
/// <c>DotnetInspect.Web.Interop.CallGraph</c>. Package and platform traversal return the same
/// contract; records structurally equal to another facade's remain separate module-local
/// declarations, and <c>ProductionFacadeWireContexts_AreAssemblyLocal</c> gates that ownership.
/// </remarks>
public sealed record BrowserCallGraph(
    string Mermaid,
    BrowserCallGraphNode Callers,
    BrowserCallGraphNode Callees,
    BrowserCallGraphScope Scope,
    BrowserCallGraphTarget[] Targets,
    BrowserCallGraphBoundary[] Boundaries,
    BrowserCallGraphDiagnostics Diagnostics,
    bool NoBody = false);

public sealed record BrowserCallGraphBoundary(
    string Id,
    string SourcePackageId,
    string SourcePackageVersion,
    string SourcePackageFramework,
    string SourceAssembly,
    string TargetPackageId,
    string TargetPackageVersion,
    string TargetPackageFramework,
    string TargetAssembly);

public sealed record BrowserCallGraphDiagnostics(
    int IncompleteNodes,
    int IncompleteEdges,
    int BindingIdentityConflicts,
    bool HasUnexploredTraversalBoundary,
    bool HasAnalysisFailureBoundary,
    int UnavailableDependencyRoutes,
    bool HasIncompleteCorrespondence,
    int UnclassifiedBoundaryEdges,
    int UnclassifiedBoundaryNamedEdges,
    string[] UnclassifiedBoundaryAssemblies,
    int PhysicalOccurrenceUnavailableEdges)
{
    public bool IsIncomplete =>
        IncompleteNodes > 0
        || IncompleteEdges > 0
        || BindingIdentityConflicts > 0
        || HasUnexploredTraversalBoundary
        || HasAnalysisFailureBoundary
        || UnavailableDependencyRoutes > 0
        || HasIncompleteCorrespondence
        || UnclassifiedBoundaryEdges > 0
        || PhysicalOccurrenceUnavailableEdges > 0;
}

public sealed record BrowserCallGraphTarget(
    string Id,
    string Assembly,
    string? AssemblyVersion,
    string? AssemblyCulture,
    string? AssemblyPublicKeyToken,
    string TypeFullName,
    string? TypeMetadataId,
    string? TypeDefinitionId,
    string MemberName,
    string[] ParameterTypes,
    string ReturnType,
    int GenericArity,
    int? MetadataToken,
    string SelectorKey,
    string Kind,
    string? PlatformPack,
    string? SurfaceAssemblyId,
    string? PackageId = null,
    string? PackageVersion = null,
    string? PackageFramework = null);

public sealed record BrowserCallGraphNode(
    string Label,
    string Status,
    bool InLoop,
    string? Source,
    BrowserCallGraphNode[] Children,
    string Assembly,
    string TypeFullName,
    string MemberName);

public sealed record BrowserCallGraphScope(
    int Packages,
    int Assemblies,
    int CallerAssemblies,
    string CalleeScope);

public sealed record BrowserDirectUseClusterInspection(
    string Outcome,
    bool IsComplete,
    BrowserDirectUseCluster[] Clusters,
    int? SelectedCluster,
    BrowserDirectUseCallSite[] CallSites,
    BrowserDirectUseDiagnostic[] Diagnostics,
    string? Failure);

public sealed record BrowserDirectUseCluster(
    int Ordinal,
    BrowserDirectUseLibrary Source,
    BrowserDirectUseLibrary Target,
    int AnchorSourceToken,
    int AnchorTargetToken,
    int SourceMembers,
    int ProviderTypes,
    int TargetMembers,
    int ExtensionMethods,
    int CallSites);

public sealed record BrowserDirectUseCallSite(
    BrowserDirectUseLibrary Source,
    string SourceMember,
    int SourceToken,
    BrowserDirectUseLibrary Target,
    string TargetMember,
    int TargetToken,
    string CallKind,
    string EvidenceMethod,
    string EvidenceModuleVersionId,
    int EvidenceToken,
    int IlOffset);

public sealed record BrowserDirectUseLibrary(
    string Name,
    string? Version,
    string? Culture,
    string? PublicKeyToken,
    string ModuleVersionId);

public sealed record BrowserDirectUseDiagnostic(
    string Code,
    string Severity,
    string Summary,
    string? Correspondence);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BrowserCallGraph))]
[JsonSerializable(typeof(BrowserDirectUseClusterInspection))]
internal sealed partial class BrowserCallGraphJsonContext : JsonSerializerContext;
