using System.Text.Json.Serialization;

namespace InspectWeb.Engine.PackageFacade;

/// <summary>
/// The package facade's browser wire contract.
/// </summary>
/// <remarks>
/// <para>
/// Every record here is declared and source-generated inside
/// <c>InspectWeb.Engine.PackageExports</c>. A structurally equal declaration in another facade is
/// a separate module-local contract by design: each generated module keeps a self-contained
/// authenticated serializer vocabulary, and no facade serializes a type owned by
/// <c>InspectWeb.Engine.Core</c> or by a sibling export assembly.
/// </para>
/// <para>
/// <c>ProductionFacadeWireContexts_AreAssemblyLocal</c> gates that ownership.
/// </para>
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

/// <summary>
/// One package's browsable surface for one exact package/version/framework workspace, adapted
/// from <c>AssemblyContextApiSurfaceQuery</c>. Every classification, label, order, and default in
/// <see cref="Accessibility"/> comes from the product's own <c>ApiAccessibilityBucket</c> values;
/// the host restates none of them.
/// </summary>
/// <param name="InspectionErrors">
/// The whole entries rendered into <paramref name="InspectionError"/>. The browser uses these
/// product-owned boundaries when cumulative platform loads deduplicate notices.
/// </param>
/// <param name="InspectionError">
/// The participants the workspace could not project, if any. A partial surface says so rather
/// than reading as a complete one.
/// </param>
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

/// <summary>
/// One bounded embedded package icon. <see cref="Base64"/> contains only bytes admitted by
/// <c>PackageIconQuery</c>; the Browser host never transports the deprecated remote icon URL.
/// </summary>
public sealed record BrowserPackageIcon(
    string MediaType,
    string Base64);

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
    int PublicMembers,
    string? PlatformPack);

/// <summary>
/// One type row. <see cref="Id"/> is the browser key, <see cref="DefinitionId"/> is the escaped
/// structured metadata identity, <see cref="QueryId"/> is the identity a Research projection is
/// asked for, and <see cref="MetadataId"/> is the exact metadata lookup name (nested types
/// delimited by <c>+</c>). <see cref="Assembly"/> is the selected package asset used for engine
/// requests, <see cref="AssemblyId"/> joins that asset to its descriptor, and
/// <see cref="AssemblyName"/> is its metadata identity. They are separate because none is
/// interchangeable with another or with display text.
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
    BrowserMemberSurface[] Api,
    string? PlatformPack);

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

public sealed record BrowserWorkspacePackage(
    string Package,
    string Version,
    string Framework);

public sealed record BrowserWorkspacePackageOccurrence(
    string Action,
    string Package,
    string Version,
    string Framework);

public sealed record BrowserWorkspacePackageOccurrenceView(
    BrowserWorkspacePackageOccurrence[] Occurrences,
    bool Superseded);

public sealed record BrowserWorkspacePackageOccurrenceActivation(
    bool Activated,
    bool Superseded,
    BrowserPackageSurface? Package);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserPackageQueryFacetTier>))]
public enum BrowserPackageQueryFacetTier
{
    Nuspec,
    PackageContent,
    SearchMetadata,
}

public sealed record BrowserPackageQueryFacetDescriptor(
    string Id,
    string Label,
    string Summary,
    int Weight,
    BrowserPackageQueryFacetTier Tier,
    string? SelectionGroupId,
    bool CombinesWithinSelectionGroup,
    string? DisplayGroupId,
    string? DisplayGroupLabel);

public sealed record BrowserPackageQueryFacetCatalog(
    BrowserPackageQueryFacetDescriptor[] Facets);

public sealed record BrowserGalleryPackageTypeSuggestion(
    string Value,
    string Label);

public sealed record BrowserGalleryPackageTypeFacet(
    string Id,
    string Label,
    string Summary,
    BrowserGalleryPackageTypeSuggestion[] Suggestions);

public sealed record BrowserGalleryDiscoveryOrder(
    string Id,
    string Label,
    string Summary);

public sealed record BrowserGalleryDiscoveryCatalog(
    BrowserGalleryPackageTypeFacet PackageType,
    BrowserGalleryDiscoveryOrder[] Orders);

public sealed record BrowserPackageQueryEvidence(
    string Id,
    string Text);

public sealed record BrowserPackageQueryRow(
    string PackageId,
    string Version,
    BrowserPackageQueryFacetTier Tier,
    BrowserPackageQueryEvidence[] Evidence,
    long? TotalDownloads,
    bool? Verified,
    string Producer,
    string? Description = null);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserPackageQueryFailureKind>))]
public enum BrowserPackageQueryFailureKind
{
    Search,
    SearchContract,
    ManifestAcquisition,
    ManifestContract,
    InvalidManifest,
    PackageContentAcquisition,
    PackageContentEvaluation,
}

public sealed record BrowserPackageQueryFailure(
    string? PackageId,
    string? Version,
    string Producer,
    BrowserPackageQueryFailureKind Kind,
    string Message);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserPackageQueryProgressPhase>))]
public enum BrowserPackageQueryProgressPhase
{
    Search,
    Manifest,
    PackageContent,
}

public sealed record BrowserPackageQueryProgress(
    BrowserPackageQueryProgressPhase Phase,
    int Completed,
    int Limit);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserPackageQueryCompletionKind>))]
public enum BrowserPackageQueryCompletionKind
{
    Exhausted,
    MatchLimitReached,
    CandidateLimitReached,
    SourcePageLimitReached,
    ClientPageLimitReached,
    Failed,
    GalleryResponseComplete,
}

public sealed record BrowserPackageQueryCompletion(
    string Prefix,
    string Producer,
    int CandidateLimit,
    int MatchLimit,
    int Candidates,
    int Matches,
    int Failures,
    BrowserPackageQueryCompletionKind Kind,
    int? SourceCandidates = null,
    long? EstimatedTotalHits = null);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserPackageQueryEventKind>))]
public enum BrowserPackageQueryEventKind
{
    Progress,
    Match,
    Failure,
    Completed,
}

public sealed record BrowserPackageQueryEvent(
    BrowserPackageQueryEventKind Kind,
    BrowserPackageQueryRow? Row,
    BrowserPackageQueryFailure? Failure,
    BrowserPackageQueryCompletion? Completion,
    BrowserPackageQueryProgress? Progress = null);

/// <summary>
/// Declared package dependency groups and one selected assembly's direct references. Dependency
/// parsing and exact-framework selection belong to <c>PackageDependencyGroupsQuery</c>; direct
/// references belong to <c>AssemblyContextReferencesQuery</c>.
/// </summary>
public sealed record BrowserPackageDependencies(
    string Package,
    string Version,
    string ActiveFramework,
    string? Assembly,
    BrowserPackageDependencyGroup[] DependencyGroups,
    BrowserAssemblyReference[] AssemblyReferences,
    string? DependencyGroupError,
    string? AssemblyReferenceError,
    BrowserCompileLibraryAvailability CompileLibrary);

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

public sealed record BrowserPackageVersions(
    string[] Versions,
    string? PreviousVersion,
    string? PreviousVersionUnavailableReason);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BrowserPackageVersions))]
[JsonSerializable(typeof(BrowserPackageSurface))]
[JsonSerializable(typeof(BrowserPackageDocumentContent))]
[JsonSerializable(typeof(BrowserMemberDocumentation))]
[JsonSerializable(typeof(BrowserPackageCacheStats))]
[JsonSerializable(typeof(BrowserPackageQueryFacetCatalog))]
[JsonSerializable(typeof(BrowserGalleryDiscoveryCatalog))]
[JsonSerializable(typeof(BrowserPackageQueryEvent))]
[JsonSerializable(typeof(BrowserPackageDependencies))]
[JsonSerializable(typeof(BrowserWorkspacePackage[]))]
[JsonSerializable(typeof(BrowserWorkspacePackageOccurrenceView))]
[JsonSerializable(typeof(BrowserWorkspacePackageOccurrenceActivation))]
[JsonSerializable(typeof(BrowserDependencyCoordinateCandidate[]))]
[JsonSerializable(typeof(BrowserDependencyCoordinateMatch))]
[JsonSerializable(typeof(BrowserTypeCandidate[]))]
[JsonSerializable(typeof(BrowserTypeSearchHit[]))]
[JsonSerializable(typeof(string[]))]
internal sealed partial class BrowserPackageJsonContext : JsonSerializerContext;
