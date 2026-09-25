using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotnetInspect.Web.Interop.Catalog;

/// <summary>
/// The catalog facade's browser wire contract: product vocabulary, home demos, and workspace-share
/// transport.
/// </summary>
/// <remarks>
/// Every record here is declared and source-generated inside
/// <c>DotnetInspect.Web.Interop.Catalog</c>. A home-demo run returns package surfaces and a call
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
    int? DeclarationMetadataToken,
    string? ReturnType,
    BrowserParameterSurface[] Parameters,
    string? DocumentationId,
    string? Summary,
    string? Returns,
    BrowserExceptionSurface[] Exceptions,
    string StableSelector,
    string AnchorDigest,
    string CanonicalSignature,
    string AnchorTypeFullName,
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
    int UnavailableDependencyRoutes)
{
    public bool IsIncomplete =>
        IncompleteNodes > 0
        || IncompleteEdges > 0
        || BindingIdentityConflicts > 0
        || HasUnexploredTraversalBoundary
        || HasAnalysisFailureBoundary
        || UnavailableDependencyRoutes > 0;
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
/// One product home-demo catalog row from <c>EcosystemPackCatalog</c>.
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
/// <see cref="FocusKind"/> is <c>package</c> or <c>platform</c>;
/// <see cref="FocusId"/> is respectively the package id or Platform family.
/// For Platform results, <see cref="FocusAssembly"/> selects the returned surface
/// whose <see cref="BrowserPackageSurface.DefaultAssemblyId"/> has the same value.
/// <see cref="PlatformContextId"/> selects the Browser-retained demo scope for
/// subsequent graph requests; it is not a portable address or a resource lease.
/// The frontend applies this source-native identity to the surfaces returned
/// by the same operation; it does not parse product view or navigation definitions.
/// </summary>
public sealed record BrowserHomeDemoRunActivation(
    string FocusKind,
    string FocusId,
    string FocusVersion,
    string FocusFramework,
    string? FocusAssembly,
    string TypeId,
    string Section,
    string? MemberName,
    string? MemberKind,
    string? MemberAnchorDigest,
    string? MemberSection,
    string? PlatformContextId = null);

/// <summary>
/// Browser result of running one product home demo through the normal package
/// or Platform workspace and query path. Unknown ids return <see cref="Found"/> false;
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

public sealed record BrowserWorkspacePackageSourceRequirement(
    string Endpoint,
    BrowserWorkspacePackageSourceAuthentication Authentication);

[JsonConverter(
    typeof(JsonStringEnumConverter<BrowserWorkspacePackageSourceAuthentication>))]
public enum BrowserWorkspacePackageSourceAuthentication
{
    Anonymous,
    AuthenticationRequired,
}

public sealed record BrowserWorkspacePackageSourceRequirementsResult(
    bool Succeeded,
    BrowserWorkspacePackageSourceRequirement[] Sources,
    BrowserWorkspaceShareFailure? Failure);

public sealed record BrowserRetainedNavigationAction(
    string Session,
    string Generation,
    string Id,
    string Source,
    string Kind);

public sealed record BrowserRetainedNavigationAuthority(
    string Session,
    string Revision,
    string Intent,
    string Epoch);

public sealed record BrowserRetainedNavigationSubject(
    string Id,
    string Kind,
    string Label,
    string? Summary,
    string? Parent);

public sealed record BrowserRetainedNavigationSubjectDescriptor(
    string Kind,
    string Label,
    BrowserRetainedNavigationSubject? Subject,
    string State,
    bool IsActive,
    bool IsRetained,
    BrowserRetainedNavigationDiagnostic[] Evidence,
    BrowserRetainedNavigationAction? Action);

public sealed record BrowserRetainedNavigationPackageDescriptor(
    int Order,
    BrowserRetainedNavigationSubject Subject,
    string PackageId,
    string Version,
    string? Framework,
    string? RuntimeIdentifier,
    string Realization,
    string? RealizationFailure,
    string State,
    bool IsCurrent,
    BrowserRetainedNavigationAction? Action);

public sealed record BrowserRetainedNavigationLibraryDescriptor(
    BrowserRetainedNavigationSubjectDescriptor Navigation,
    string? AssetId,
    bool IsAggregate,
    bool IsPrimary);

public sealed record BrowserRetainedNavigationLensDescriptor(
    BrowserRetainedNavigationFacet Facet,
    string State,
    bool IsCurrent,
    BrowserRetainedNavigationLens? Target,
    string? Unavailability,
    string? Message,
    BrowserRetainedNavigationAction? Action);

public sealed record BrowserRetainedNavigationTypeDescriptor(
    BrowserRetainedNavigationSubjectDescriptor Navigation,
    string Library,
    string? Accessibility,
    string TypeKind,
    BrowserRetainedNavigationLensDescriptor[] DescendantLenses);

public sealed record BrowserRetainedNavigationMemberDescriptor(
    BrowserRetainedNavigationSubjectDescriptor Navigation,
    string Library,
    string ContainingType,
    string DeclaringType,
    string? Accessibility,
    string MemberKind,
    string? Signature,
    BrowserRetainedNavigationLensDescriptor[] DescendantLenses);

public sealed record BrowserRetainedNavigationFacet(
    string Id,
    string Kind,
    string Title,
    string Summary,
    int Order,
    string? Role);

public sealed record BrowserRetainedNavigationLens(
    string Id,
    BrowserRetainedNavigationSubject Subject,
    string Facet);

public sealed record BrowserRetainedNavigationResolution(
    string Kind,
    BrowserRetainedNavigationFacet? Descriptor,
    string? Unavailability,
    string? Message);

public sealed record BrowserRetainedNavigationRealization(
    string Kind,
    string? Failure);

public sealed record BrowserRetainedNavigationLensOutcome(
    string Kind,
    string Basis,
    BrowserRetainedNavigationSubject Subject,
    BrowserRetainedNavigationLens? EffectiveLens,
    BrowserRetainedNavigationLens? Request,
    string? PreferredRole,
    string? PolicyFailure,
    BrowserRetainedNavigationResolution? Resolution,
    BrowserRetainedNavigationRealization? Suspension);

public sealed record BrowserRetainedNavigationDiagnostic(
    string Kind,
    string Library,
    string Message);

public sealed record BrowserRetainedNavigationScopeStatus(
    string Kind,
    string? RuntimeFailure);

public sealed record BrowserRetainedNavigationScopeOutcome(
    string Kind,
    string Operation,
    string? Rejection,
    string? Failure);

public sealed record BrowserRetainedNavigationCoordinateOutcome(
    string Disposition,
    string Detail,
    string? LibraryPairing,
    string? TypeCorrespondence,
    string? MemberCorrespondence);

public sealed record BrowserRetainedNavigationRequest(
    BrowserRetainedNavigationSubject Source,
    BrowserRetainedNavigationSubject Destination,
    BrowserRetainedNavigationLens? Lens);

public sealed record BrowserRetainedNavigationOutcome(
    string Kind,
    string? Rejection,
    string? FailureSource,
    string? Message,
    BrowserRetainedNavigationRequest? Request,
    BrowserRetainedNavigationResolution? Resolution,
    BrowserRetainedNavigationScopeOutcome? Scope,
    BrowserRetainedNavigationDiagnostic[] Diagnostics,
    BrowserRetainedNavigationCoordinateOutcome? CoordinateRetention);

public sealed record BrowserRetainedNavigationSnapshot(
    string Generation,
    BrowserRetainedNavigationScopeStatus Scope,
    BrowserRetainedNavigationSubject Workspace,
    string? ActivePackage,
    BrowserRetainedNavigationSubject ActiveSubject,
    BrowserRetainedNavigationSubject? TypeInventoryLibraryContext,
    BrowserRetainedNavigationPackageDescriptor[] Packages,
    BrowserRetainedNavigationSubjectDescriptor[] Hierarchy,
    BrowserRetainedNavigationLibraryDescriptor[] Libraries,
    BrowserRetainedNavigationTypeDescriptor[] Types,
    BrowserRetainedNavigationMemberDescriptor[] Members,
    BrowserRetainedNavigationLensDescriptor[] Lenses,
    BrowserRetainedNavigationLensOutcome LensOutcome,
    BrowserRetainedNavigationDiagnostic[] Diagnostics);

public sealed record BrowserRetainedNavigationResult(
    string Operation,
    string Request,
    BrowserRetainedNavigationSnapshot Snapshot,
    BrowserRetainedNavigationOutcome Outcome,
    string Synchronization,
    BrowserRetainedNavigationAuthority? Authority);

public sealed record BrowserRetainedWorkspacePackage(
    string NavigationId,
    int ContextIndex,
    string ConsumerPackageSubjectId,
    BrowserPackageSurface Surface,
    BrowserRetainedWorkspaceTypePage TypePage);

public sealed record BrowserRetainedWorkspacePlatform(
    string NavigationId,
    int ContextIndex,
    string Family,
    string? RuntimeIdentifier,
    BrowserPackageSurface Surface,
    BrowserRetainedWorkspaceTypePage TypePage);

public sealed record BrowserRetainedWorkspaceTypePage(
    int Offset,
    int TotalTypes,
    int? NextOffset);

public sealed record BrowserRetainedWorkspaceSurfaceSummary(
    string? SelectedCompileFramework,
    int LibraryCount,
    int TypeCount,
    int MemberCount,
    int DocumentCount,
    bool HasInspectionNotices);

public sealed record BrowserRetainedWorkspacePackageInventory(
    string NavigationId,
    int ContextIndex,
    string ConsumerPackageSubjectId,
    BrowserRetainedWorkspaceSurfaceSummary Summary);

public sealed record BrowserRetainedWorkspacePlatformInventory(
    string NavigationId,
    int ContextIndex,
    string Family,
    string? RuntimeIdentifier,
    BrowserRetainedWorkspaceSurfaceSummary Summary);

public sealed record BrowserRetainedWorkspaceLibraryIdentity(
    string Name,
    string Version,
    string? Culture,
    string? PublicKeyToken);

public sealed record BrowserRetainedWorkspaceExactLibrary(
    string Kind,
    BrowserRetainedWorkspaceLibraryIdentity Library,
    string? PackageId,
    string? PackageVersion,
    string? PlatformFamily);

public sealed record BrowserRetainedWorkspaceEcosystemPopulation(
    string Kind,
    BrowserRetainedWorkspaceExactLibrary? ExactLibrary,
    string? PlatformFamily,
    string? PackagePrefix);

public sealed record BrowserRetainedWorkspaceEcosystem(
    string Id,
    string[] NamespaceRoots,
    string[] CorePackages,
    BrowserRetainedWorkspaceEcosystemPopulation[] Populations);

public sealed record BrowserRetainedWorkspaceRegistration(
    string Kind,
    BrowserRetainedWorkspaceExactLibrary? ExactLibrary,
    string? PackagePrefix,
    BrowserRetainedWorkspaceEcosystem? Ecosystem);

public sealed record BrowserRetainedWorkspaceDefinitionState(
    BrowserWorkspaceShareTab[] Tabs,
    BrowserWorkspaceShareContext[] Contexts,
    BrowserRetainedWorkspaceRegistration[] Registrations,
    string? ActiveTabId,
    string? SelectedContextId);

public sealed record BrowserRetainedWorkspacePackageAdmissionResult(
    string Status,
    BrowserRetainedWorkspacePackage? Package,
    string? Message);

public sealed record BrowserRetainedWorkspacePlatformAdmissionResult(
    string Status,
    BrowserRetainedWorkspacePlatform? Platform,
    string? Message);

public sealed record BrowserRetainedWorkspaceCleanup(string Message);

/// <summary>
/// Observable settlement for the realization replaced by a successful cutover.
/// </summary>
public sealed record BrowserRetainedWorkspacePredecessor(
    string SettlementId,
    string Reason);

/// <summary>
/// Detached managed evidence TypeScript posts after a successful cutover.
/// </summary>
public sealed record BrowserRetainedWorkspacePosting(
    string RetainedDefinitionId,
    string Label,
    string CanonicalLocation,
    string CanonicalPacket,
    string RealizationId,
    long PublicationOrdinal,
    BrowserRetainedWorkspaceDefinitionState Definition,
    BrowserRetainedNavigationResult Navigation,
    BrowserRetainedWorkspacePackageInventory[] Packages,
    BrowserRetainedWorkspacePlatformInventory[] Platforms,
    BrowserRetainedWorkspacePredecessor? Predecessor,
    BrowserRetainedWorkspaceCleanup? Cleanup);

/// <summary>
/// Detached candidate evidence offered to the Browser before managed cutover.
/// </summary>
public sealed record BrowserRetainedWorkspacePreparedPosting(
    string RetainedDefinitionId,
    string Label,
    string CanonicalLocation,
    string CanonicalPacket,
    BrowserRetainedWorkspaceDefinitionState Definition,
    BrowserRetainedNavigationResult Navigation,
    BrowserRetainedWorkspacePackageInventory[] Packages,
    BrowserRetainedWorkspacePlatformInventory[] Platforms);

/// <summary>Typed complete-restoration failure at the Browser boundary.</summary>
public sealed record BrowserRetainedWorkspaceActivationFailure(
    string Kind,
    string Message);

/// <summary>
/// Retained Workspace selection result. Status is <c>activated</c>,
/// <c>noEffect</c>, <c>superseded</c>, or <c>failed</c>.
/// </summary>
public sealed record BrowserRetainedWorkspaceActivationResult(
    string Status,
    BrowserRetainedWorkspacePosting? Posting,
    BrowserRetainedWorkspaceActivationFailure? Failure);

/// <summary>
/// Candidate preparation result. Status is <c>prepared</c>,
/// <c>noEffect</c>, <c>superseded</c>, or <c>failed</c>.
/// </summary>
public sealed record BrowserRetainedWorkspacePreparationResult(
    string Status,
    string? Receipt,
    BrowserRetainedWorkspacePreparedPosting? Preparation,
    BrowserRetainedWorkspacePosting? Posting,
    BrowserRetainedWorkspaceActivationFailure? Failure);

/// <summary>
/// Matching Browser completion for an irreversible activation or deactivation.
/// Status is <c>completed</c> or <c>unavailable</c>.
/// </summary>
public sealed record BrowserRetainedWorkspaceConsumerCompletionResult(
    string Status,
    bool? Succeeded,
    string? Failure,
    string? Message);

/// <summary>
/// Active-retained-definition deletion result. Status is <c>deactivated</c>,
/// <c>cleanupFailed</c>, <c>noEffect</c>, or <c>rejected</c>.
/// </summary>
public sealed record BrowserRetainedWorkspaceDeactivationResult(
    string Status,
    string? CompletionReceipt,
    BrowserRetainedWorkspaceSettlement? Settlement,
    string? Message);

/// <summary>Detached realization cleanup evidence.</summary>
public sealed record BrowserRetainedWorkspaceSettlement(
    bool Succeeded,
    string Reason,
    string? Failure);

/// <summary>
/// Predecessor observation result. Status is <c>settled</c> or <c>unknown</c>.
/// </summary>
public sealed record BrowserRetainedWorkspaceSettlementResult(
    string Status,
    BrowserRetainedWorkspaceSettlement? Settlement);

/// <summary>One page-session credential for an authenticated Workspace source.</summary>
public sealed record BrowserRetainedWorkspacePackageSourceCredential(
    string Username,
    string Pat);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BrowserVocabularyDocument))]
[JsonSerializable(typeof(BrowserHomeDemoCatalog))]
[JsonSerializable(typeof(BrowserHomeDemoResolveResult))]
[JsonSerializable(typeof(BrowserHomeDemoRunResult))]
[JsonSerializable(typeof(BrowserWorkspaceShareState))]
[JsonSerializable(typeof(BrowserWorkspaceShareDecodeResult))]
[JsonSerializable(typeof(BrowserWorkspaceShareEncodeResult))]
[JsonSerializable(typeof(BrowserWorkspacePackageSourceRequirementsResult))]
[JsonSerializable(typeof(BrowserRetainedWorkspacePreparationResult))]
[JsonSerializable(typeof(BrowserRetainedWorkspaceActivationResult))]
[JsonSerializable(typeof(BrowserRetainedWorkspaceConsumerCompletionResult))]
[JsonSerializable(typeof(BrowserRetainedWorkspacePackageAdmissionResult))]
[JsonSerializable(typeof(BrowserRetainedWorkspacePlatformAdmissionResult))]
[JsonSerializable(typeof(BrowserRetainedWorkspaceDeactivationResult))]
[JsonSerializable(typeof(BrowserRetainedWorkspaceSettlementResult))]
[JsonSerializable(typeof(Dictionary<
    string,
    BrowserRetainedWorkspacePackageSourceCredential>))]
internal sealed partial class BrowserCatalogJsonContext : JsonSerializerContext;
