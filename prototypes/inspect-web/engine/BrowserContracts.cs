using System.Text.Json;
using System.Text.Json.Serialization;

namespace InspectWeb.Engine;

/// <summary>
/// One package's browsable surface for one exact package/version/framework workspace, adapted
/// from <c>AssemblyContextApiSurfaceQuery</c>. Every classification, label, order, and default in
/// <see cref="Accessibility"/> comes from the product's own
/// <c>ApiAccessibilityBucket</c> values; the host restates none of them.
/// </summary>
/// <param name="InspectionError">
/// The participants the workspace could not project, if any. A partial surface says so rather
/// than reading as a complete one.
/// </param>
public sealed record BrowserPackageSurface(
    string Package,
    string Version,
    string[] Frameworks,
    string ActiveFramework,
    string DefaultAssemblyId,
    BrowserAssemblySurface[] Assemblies,
    BrowserTypeSurface[] Types,
    BrowserAccessibilityDescriptor[] Accessibility,
    int TotalMembers,
    BrowserPackageDocument[] Documents,
    string? InspectionError);

/// <summary>
/// One product-owned accessibility bucket, carried verbatim from
/// <c>DotnetInspector.Queries.ApiAccessibilityBucket</c>.
/// </summary>
public sealed record BrowserAccessibilityDescriptor(
    string Id,
    string Label,
    int Order,
    bool IsDefault,
    int Count);

public sealed record BrowserAssemblySurface(
    string Id,
    string Name,
    string Version,
    string? Culture,
    string? PublicKeyToken,
    string Asset,
    int PublicTypes,
    int PublicMembers);

/// <summary>
/// One type row. <see cref="Id"/> is the browser key, <see cref="DefinitionId"/> is the escaped
/// structured metadata identity, <see cref="QueryId"/> is the identity a Research projection is asked for, and
/// <see cref="MetadataId"/> is the exact metadata lookup name (nested types delimited by
/// <c>+</c>). <see cref="Assembly"/> is the selected package asset used for engine requests,
/// <see cref="AssemblyId"/> joins that asset to its descriptor, and <see cref="AssemblyName"/> is
/// its metadata identity. They are separate because none is interchangeable with another or with
/// display text.
/// </summary>
public sealed record BrowserTypeSurface(
    string Id,
    string DefinitionId,
    string QueryId,
    string MetadataId,
    string Name,
    string DisplayName,
    string Namespace,
    string Kind,
    string Accessibility,
    string AccessibilityId,
    string Assembly,
    string AssemblyId,
    string AssemblyName,
    int Members,
    string Signature,
    BrowserMemberSurface[] Api);

/// <summary>
/// One member overload. <see cref="StableSelector"/>, <see cref="AnchorDigest"/>, and
/// <see cref="CanonicalSignature"/> are the product's member anchor; <see cref="GraphSelectorKey"/>
/// and <see cref="BodySelectors"/> are the product's opaque call-graph correspondence. The host
/// transports them and never parses them.
/// </summary>
public sealed record BrowserMemberSurface(
    string Name,
    string Kind,
    string Signature,
    int GenericArity,
    int? MetadataToken,
    string? ReturnType,
    BrowserParameterSurface[] Parameters,
    string? DocumentationId,
    string? Summary,
    string? Returns,
    BrowserExceptionSurface[] Exceptions,
    string StableSelector,
    string AnchorDigest,
    string CanonicalSignature,
    string GraphSelectorKey,
    BrowserMemberBodySelector[] BodySelectors);

public sealed record BrowserMemberBodySelector(
    int Token,
    string MemberName,
    string SelectorKey);

public sealed record BrowserParameterSurface(
    string Name,
    string Type,
    string? Modifier,
    bool HasDefault,
    string? DefaultValue,
    string? Description);

/// <summary>
/// A browsable Markdown document shipped inside a package. The manifest carries presence and size
/// only; the body is fetched on demand so the surface payload stays small.
/// </summary>
public sealed record BrowserPackageDocument(
    string Kind,
    string Name,
    string Path,
    int Size);

public sealed record BrowserPackageDocumentContent(
    string Kind,
    string Name,
    string Path,
    string Text);

public sealed record BrowserExceptionSurface(
    string Type,
    string Description);

public sealed record BrowserMemberDocumentation(
    string? Summary,
    string? Returns,
    IReadOnlyDictionary<string, string> Parameters,
    BrowserExceptionSurface[] Exceptions);

public sealed record BrowserTypeCandidate(
    string Key,
    string Name,
    string Full);

public sealed record BrowserTypeSearchHit(
    string Key,
    string Kind);

public sealed record BrowserPackageCacheStats(
    int Packages,
    int Resident,
    int Workspaces,
    long ResidentBytes);

public sealed record BrowserBuildIdentity(
    string Version,
    string? Commit,
    string? BuiltAtUtc,
    string? CommitUrl);

/// <summary>
/// One type's metadata projection, adapted from <c>ResearchViews.TypeProjectionResult</c> — the
/// presentation-neutral seam the CLI consumes — so the browser never reimplements type-fact
/// composition.
/// </summary>
public sealed record BrowserTypeMetadata(
    string FullName,
    string? Namespace,
    string Name,
    string Kind,
    string[] Modifiers,
    string? Accessibility,
    string? Assembly,
    string? BaseType,
    string[] Interfaces,
    string[] DerivedTypes,
    BrowserTypeParameter[] TypeParameters,
    string[] Attributes,
    string? EnumUnderlyingType,
    BrowserTypeComposition? Composition,
    BrowserTypeGraphNode[] GraphNodes,
    BrowserTypeGraphEdge[] GraphEdges,
    string[] InspectionFailures);

public sealed record BrowserTypeParameter(string Name, string? Variance, string[] Constraints);

public sealed record BrowserTypeComposition(
    int Methods,
    int Properties,
    int Fields,
    int Events,
    int Constructors,
    int Operators,
    int ExplicitInterfaceImplementations,
    int ExtensionMethods,
    int Static,
    int Unsafe,
    int Async,
    int Virtual,
    int Abstract,
    int Override,
    int Extension,
    int Obsolete,
    int Total);

public sealed record BrowserTypeGraphNode(string Id, string DisplayName, string Role);

public sealed record BrowserTypeGraphEdge(string FromId, string ToId, string Kind);

/// <summary>
/// Declared package dependency groups and one selected assembly's direct references. Dependency
/// parsing and exact-framework selection belong to <c>PackageDependencyGroupsQuery</c>; direct
/// references belong to <c>AssemblyContextReferencesQuery</c>.
/// </summary>
public sealed record BrowserPackageDependencies(
    string Package,
    string Version,
    string ActiveFramework,
    string Assembly,
    BrowserPackageDependencyGroup[] DependencyGroups,
    BrowserAssemblyReference[] AssemblyReferences,
    string? DependencyGroupError,
    string? AssemblyReferenceError);

public sealed record BrowserPackageDependencyGroup(
    int Index,
    string Framework,
    bool IsActive,
    BrowserPackageDependency[] Dependencies);

public sealed record BrowserPackageDependency(
    string Id,
    string VersionRange);

public sealed record BrowserAssemblyReference(
    string Name,
    string Version,
    string? Culture,
    string? PublicKeyToken);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserDependencyCoordinateProvenance>))]
public enum BrowserDependencyCoordinateProvenance
{
    NuGetPackage,
    PlatformRuntime,
}

public sealed record BrowserDependencyCoordinateCandidate(
    string Key,
    BrowserDependencyCoordinateProvenance Provenance,
    string PackageId,
    string Version,
    string TargetFramework);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserDependencyCoordinateMatchOutcome>))]
public enum BrowserDependencyCoordinateMatchOutcome
{
    NoMatch,
    Unique,
    Ambiguous,
}

public sealed record BrowserDependencyCoordinateMatch(
    BrowserDependencyCoordinateMatchOutcome Outcome,
    string? CandidateKey);

/// <summary>
/// The annotated-source envelope: the product's portable <c>AnnotatedSourceDocument</c> serialized
/// by its owning <c>AnnotatedSourceDocumentJsonContext</c>, plus the provenance of the artifact it
/// was raised from. The document travels as a <see cref="JsonElement"/> so the wire shape stays
/// exactly the one the viewer's model validates — the host neither reshapes nor renames a field.
/// </summary>
/// <param name="ContextLimitation">
/// Set when the projection's whole-assembly fact context was narrower than a complete one, so a
/// short fact list is never mistaken for an honest absence of facts.
/// </param>
public sealed record BrowserAnnotatedSource(
    JsonElement Document,
    string Provenance,
    string? ContextLimitation);

/// <summary>
/// Ecosystem integration evidence for one workspace, carried exactly as
/// <c>AssemblyContextIntegrationsQuery</c> produced it: one group per package/version/framework,
/// one entry per participant, and each participant's own signals grouped by the integration name
/// the scanner assigned. Grouping is presentation; no signal, category, or count is composed here.
/// </summary>
public sealed record BrowserPackageIntegrations(
    string Package,
    string Version,
    string Framework,
    BrowserIntegrationCategory[] Categories,
    int TotalSignals,
    bool IsComplete,
    string? InspectionError);

public sealed record BrowserIntegrationCategory(
    string Integration,
    BrowserIntegrationSignal[] Signals);

public sealed record BrowserIntegrationSignal(string Kind, string Name, string Shape);

/// <summary>
/// Integration opportunities for one package workspace, composed by
/// <c>AssemblyContextIntegrationOpportunitiesQuery</c> from its typed Integrations prerequisite.
/// The host groups and deduplicates rows for display; it does not infer opportunity evidence.
/// </summary>
public sealed record BrowserPackageOpportunities(
    string Package,
    string Version,
    string ActiveFramework,
    BrowserOpportunityCategory[] Categories,
    int TotalOpportunities,
    bool IsComplete,
    string? InspectionError);

public sealed record BrowserOpportunityCategory(
    string Integration,
    BrowserOpportunityItem[] Items);

public sealed record BrowserOpportunityItem(
    string Api,
    string IntegrationType,
    string LookFor);

/// <summary>
/// One progressively acquired member call graph, projected through
/// <c>ILInspector.CallGraph.CallGraphProjection</c>. Graph identity, direction, cycles,
/// boundaries, and escaping belong to the projection; this record carries it across the Wasm
/// boundary and the browser owns rendering.
/// </summary>
public sealed record BrowserCallGraph(
    string Mermaid,
    BrowserCallGraphNode Callers,
    BrowserCallGraphNode Callees,
    BrowserCallGraphScope Scope,
    BrowserCallGraphTarget[] Targets,
    BrowserCallGraphDiagnostics Diagnostics,
    bool NoBody = false);

public sealed record BrowserCallGraphDiagnostics(
    int IncompleteNodes,
    int IncompleteEdges,
    int BindingIdentityConflicts,
    bool HasUnexploredTraversalBoundary,
    bool HasAnalysisFailureBoundary)
{
    public bool IsIncomplete =>
        IncompleteNodes > 0
        || IncompleteEdges > 0
        || BindingIdentityConflicts > 0
        || HasUnexploredTraversalBoundary
        || HasAnalysisFailureBoundary;
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
    string Kind);

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

public sealed record BrowserWorkspacePackage(
    string Package,
    string Version,
    string Framework);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BrowserPackageSurface))]
[JsonSerializable(typeof(BrowserPackageDocumentContent))]
[JsonSerializable(typeof(BrowserMemberDocumentation))]
[JsonSerializable(typeof(BrowserPackageCacheStats))]
[JsonSerializable(typeof(BrowserBuildIdentity))]
[JsonSerializable(typeof(BrowserPackageDependencies))]
[JsonSerializable(typeof(BrowserDependencyCoordinateCandidate[]))]
[JsonSerializable(typeof(BrowserDependencyCoordinateMatch))]
[JsonSerializable(typeof(BrowserPackageIntegrations))]
[JsonSerializable(typeof(BrowserPackageOpportunities))]
[JsonSerializable(typeof(BrowserTypeMetadata))]
[JsonSerializable(typeof(BrowserAnnotatedSource))]
[JsonSerializable(typeof(BrowserCallGraph))]
[JsonSerializable(typeof(BrowserWorkspacePackage[]))]
[JsonSerializable(typeof(BrowserTypeCandidate[]))]
[JsonSerializable(typeof(BrowserTypeSearchHit[]))]
[JsonSerializable(typeof(string[]))]
internal sealed partial class BrowserJsonContext : JsonSerializerContext;
