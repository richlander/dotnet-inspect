using System.Text.Json;
using System.Text.Json.Serialization;

namespace InspectWeb.Engine.CatalogFacade;

/// <summary>
/// The catalog facade's browser wire contract: product vocabulary, home demos, and workspace-share
/// transport.
/// </summary>
/// <remarks>
/// Every record here is declared and source-generated inside
/// <c>InspectWeb.Engine.CatalogExports</c>. A home-demo run returns package surfaces and a call
/// graph, and this facade declares its own transport for both rather than importing the package or
/// call-graph facade's; <c>ProductionFacadeWireContexts_AreAssemblyLocal</c> gates that ownership.
/// </remarks>
public sealed record BrowserCompileLibraryAvailability(
    BrowserCompileLibraryStatus Status,
    string? TargetFramework,
    string? Message);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserCompileLibraryStatus>))]
public enum BrowserCompileLibraryStatus
{
    Selected,
    NoCompileAssets,
    NoMatchingTargetFramework,
    EmptyCompileGroup,
    InvalidImplementationAssets,
}

public sealed record BrowserPackageSurface(
    string Package,
    string Version,
    string[] Frameworks,
    string ActiveFramework,
    BrowserPackageIcon? Icon,
    string? DefaultAssemblyId,
    BrowserCompileLibraryAvailability CompileLibrary,
    BrowserAssemblySurface[] Assemblies,
    BrowserTypeSurface[] Types,
    BrowserAccessibilityDescriptor[] Accessibility,
    int TotalMembers,
    BrowserPackageDocument[] Documents,
    string[] InspectionErrors,
    string? InspectionError);

public sealed record BrowserPackageIcon(
    string MediaType,
    string Base64);

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
    int PublicMembers,
    string? PlatformPack);

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
    BrowserMemberSurface[] Api,
    string? PlatformPack);

public sealed record BrowserMemberSurface(
    string Name,
    string Kind,
    string Signature,
    string Accessibility,
    bool IsStatic,
    bool IsUnsafe,
    bool IsVirtual,
    bool IsAbstract,
    bool IsOverride,
    bool IsExtension,
    bool IsObsolete,
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

public sealed record BrowserExceptionSurface(
    string Type,
    string Description);

public sealed record BrowserPackageDocument(
    string Kind,
    string Name,
    string Path,
    int Size);

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
    string Kind,
    string? PlatformPack,
    string? SurfaceAssemblyId);

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

/// <summary>
/// One vocabulary field's discoverable contract, mapped verbatim from
/// <c>DotnetInspector.Vocabulary.VocabularyWireField</c>. Kept as a browser-local record (rather
/// than reusing the product's wire type directly) so the TypeScript facade's JSON-wire-contract
/// discovery — which only walks types physically defined in this assembly — can generate a real
/// TypeScript interface for it instead of collapsing to <c>unknown</c>.
/// </summary>
public sealed record BrowserVocabularyField(
    string Id,
    string Label,
    string Summary,
    string Type,
    string[] Operators);

/// <summary>One vocabulary section, mapped verbatim from <c>DotnetInspector.Vocabulary.VocabularyWireSection</c>.</summary>
public sealed record BrowserVocabularySection(
    string Id,
    string Name,
    string Summary,
    string[] Categories,
    [property: JsonPropertyName("accepted_by")]
    string[] AcceptedBy,
    BrowserVocabularyField[] Fields,
    JsonElement[] Values);

/// <summary>
/// The product-owned query vocabulary document, mapped verbatim from
/// <c>DotnetInspector.Vocabulary.VocabularyWireDocument</c>. The browser receives the same
/// section/field/value document as the CLI and retains no separate labels, ordering, defaults, or
/// query semantics.
/// </summary>
public sealed record BrowserVocabularyDocument(
    [property: JsonPropertyName("schema_version")]
    int SchemaVersion,
    BrowserVocabularySection[] Sections);

/// <summary>
/// One product home-demo catalog row from <c>ProductInspectionDemos.Entries</c>.
/// Browser-local so <c>ts-jsexport</c> emits a real TypeScript interface.
/// </summary>
public sealed record BrowserHomeDemoCatalogEntry(
    string Id,
    string Title,
    string Summary);

/// <summary>Product home-demo catalog in display order.</summary>
public sealed record BrowserHomeDemoCatalog(
    BrowserHomeDemoCatalogEntry[] Demos);

/// <summary>
/// One workspace/navigation member coordinate projected for the browser.
/// <see cref="Kind"/> is <c>package</c> or <c>platform</c>.
/// </summary>
public sealed record BrowserHomeDemoMember(
    string Kind,
    string Id,
    string? Version,
    string? Framework,
    string? Assembly);

/// <summary>One navigation tab from a resolved home demo.</summary>
public sealed record BrowserHomeDemoNavigationTab(
    string Id,
    BrowserHomeDemoMember Member);

/// <summary>View selectors from a resolved home demo.</summary>
public sealed record BrowserHomeDemoView(
    string? Library,
    string? Type,
    string? MemberAnchor,
    string? MemberKey,
    string? Section);

/// <summary>
/// Fully resolved product home demo: workspace members, navigation, and view.
/// Hosts own share encoding and any residual platform pack mapping.
/// </summary>
public sealed record BrowserHomeDemoResolved(
    string Id,
    string Title,
    string Summary,
    BrowserHomeDemoMember[] WorkspaceMembers,
    BrowserHomeDemoNavigationTab[] Tabs,
    int FocusTabIndex,
    BrowserHomeDemoView View);

/// <summary>
/// Result of resolving one home demo id. <see cref="Demo"/> is set only when
/// <see cref="Found"/> is true (avoids a bare JSON null on the JSExport surface).
/// </summary>
public sealed record BrowserHomeDemoResolveResult(
    bool Found,
    BrowserHomeDemoResolved? Demo);

/// <summary>
/// Exact browser selection produced while running one product home demo.
/// The frontend applies this identity to the package surfaces returned by the
/// same operation; it does not parse product view or navigation definitions.
/// </summary>
public sealed record BrowserHomeDemoRunActivation(
    string FocusPackage,
    string FocusVersion,
    string FocusFramework,
    string TypeId,
    string Section,
    string? MemberName,
    string? MemberKind,
    string? MemberAnchorDigest,
    string? MemberSection);

/// <summary>
/// Browser result of running one product home demo through the normal package
/// workspace and query path. Unknown ids return <see cref="Found"/> false;
/// known-demo failures remain visible exceptions.
/// </summary>
public sealed record BrowserHomeDemoRunResult(
    bool Found,
    BrowserPackageSurface[] Packages,
    BrowserHomeDemoRunActivation? Activation,
    BrowserCallGraph? CallGraph);

/// <summary>
/// One product-normalized source in a canonical workspace share packet.
/// <see cref="Kind"/> is <c>package</c> or <c>group</c>; <see cref="Source"/>
/// is the package id or leading-colon group expression.
/// </summary>
public sealed record BrowserWorkspaceShareTab(
    string Id,
    string Kind,
    string Source,
    string? Version,
    string? Framework,
    string? RuntimeIdentifier);

/// <summary>
/// One binding-consistent context expressed through stable packet-local tab ids.
/// </summary>
public sealed record BrowserWorkspaceShareContext(
    string Id,
    string[] TabIds);

/// <summary>Canonical product-owned view fields carried by share packet v1.</summary>
public sealed record BrowserWorkspaceShareView(
    string? Lens,
    string? Type,
    string? MemberAnchor,
    string? MemberSignature,
    string? Section,
    string[] Libraries);

/// <summary>
/// Long-form Browser transport for one canonical packet-local scenario.
/// TypeScript consumes these product-owned identities and never parses compact
/// packet fields or base64url.
/// </summary>
public sealed record BrowserWorkspaceShareState(
    BrowserWorkspaceShareTab[] Tabs,
    BrowserWorkspaceShareContext[] Contexts,
    string ActiveTabId,
    string SelectedContextId,
    BrowserWorkspaceShareView View);

/// <summary>Typed codec, transposition, or Browser-transport failure.</summary>
public sealed record BrowserWorkspaceShareFailure(
    string Kind,
    string Path,
    string Message);

public sealed record BrowserWorkspaceShareDecodeResult(
    bool Succeeded,
    BrowserWorkspaceShareState? State,
    BrowserWorkspaceShareFailure? Failure);

public sealed record BrowserWorkspaceShareEncodeResult(
    bool Succeeded,
    string? Packet,
    BrowserWorkspaceShareFailure? Failure);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BrowserVocabularyDocument))]
[JsonSerializable(typeof(BrowserHomeDemoCatalog))]
[JsonSerializable(typeof(BrowserHomeDemoResolveResult))]
[JsonSerializable(typeof(BrowserHomeDemoRunResult))]
[JsonSerializable(typeof(BrowserWorkspaceShareState))]
[JsonSerializable(typeof(BrowserWorkspaceShareDecodeResult))]
[JsonSerializable(typeof(BrowserWorkspaceShareEncodeResult))]
internal sealed partial class BrowserCatalogJsonContext : JsonSerializerContext;
