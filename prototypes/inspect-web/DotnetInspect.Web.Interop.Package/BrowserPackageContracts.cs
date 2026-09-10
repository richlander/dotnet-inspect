using System.Text.Json.Serialization;

namespace DotnetInspect.Web.Interop.Package;

/// <summary>
/// The package facade's browser wire contract.
/// </summary>
/// <remarks>
/// <para>
/// Every record here is declared and source-generated inside
/// <c>DotnetInspect.Web.Interop.Package</c>. A structurally equal declaration in another facade is
/// a separate module-local contract by design: each generated module keeps a self-contained
/// authenticated serializer vocabulary, and no facade serializes a type owned by
/// <c>DotnetInspect.Web.Core</c> or by a sibling export assembly.
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
/// One member overload. <see cref="StableSelector"/>, <see cref="AnchorDigest"/>,
/// <see cref="CanonicalSignature"/>, and <see cref="AnchorTypeFullName"/> are the product's
/// member anchor; <see cref="GraphSelectorKey"/> and <see cref="BodySelectors"/> are the product's
/// opaque call-graph correspondence. The host transports them and never parses them.
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

public sealed record BrowserPlatformCatalog(
    string Tfm,
    string Version,
    BrowserPlatformLibrary[] Rows);

public sealed record BrowserPlatformLibrary(
    string Tfm,
    string Pack,
    string Assembly,
    string File,
    string Kind,
    string? ForwardsTo,
    string Version,
    int PublicTypes,
    bool InReferencePack,
    bool HasImplementation,
    string PackVersion);

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
    Assembly,
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

public sealed record BrowserPackageAssemblyQueryPattern(
    string Id,
    string Label,
    string Summary,
    int MaximumOperandLength,
    int MaximumPackages);

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

[JsonConverter(typeof(JsonStringEnumConverter<BrowserPackageQueryEvidenceScope>))]
public enum BrowserPackageQueryEvidenceScope
{
    Package,
    Query,
}

public sealed record BrowserPackageQueryEvidenceSummary(
    int Count,
    string[] Preview);

public sealed record BrowserPackageQueryEvidence(
    string Id,
    string Text,
    BrowserPackageQueryEvidenceScope Scope,
    BrowserPackageQueryEvidenceSummary? Summary);

public sealed record BrowserPackageQueryRow(
    string PackageId,
    string Version,
    BrowserPackageQueryFacetTier Tier,
    BrowserPackageQueryEvidence[] Evidence,
    long? TotalDownloads,
    bool? Verified,
    string Producer,
    string? Description = null,
    string? RootRequest = null);

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
    AssemblyAcquisition,
    AssemblyEvaluation,
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
    Assembly,
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
    ExactPackageComplete,
    ExplicitCandidatesComplete,
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
    long? EstimatedTotalHits = null,
    int? SemanticMisses = null,
    int? NotApplicable = null,
    string? Scope = null);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserPackageAssemblyAssessmentKind>))]
public enum BrowserPackageAssemblyAssessmentKind
{
    NoMatch,
    NotApplicable,
}

public sealed record BrowserPackageAssemblyAssessment(
    string PackageId,
    string Version,
    BrowserPackageAssemblyAssessmentKind Disposition,
    string Message,
    string? AssetPath,
    string RootRequest);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserPackageQueryEventKind>))]
public enum BrowserPackageQueryEventKind
{
    Progress,
    Match,
    Failure,
    Completed,
    Assessment,
}

public sealed record BrowserPackageQueryEvent(
    BrowserPackageQueryEventKind Kind,
    BrowserPackageQueryRow? Row,
    BrowserPackageQueryFailure? Failure,
    BrowserPackageQueryCompletion? Completion,
    BrowserPackageQueryProgress? Progress = null,
    BrowserPackageAssemblyAssessment? Assessment = null);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserPackageQueryResultKind>))]
public enum BrowserPackageQueryResultKind
{
    Succeeded,
    Failed,
    Canceled,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserPackageQueryOperationFailureKind>))]
public enum BrowserPackageQueryOperationFailureKind
{
    Expected,
    Unexpected,
}

public sealed record BrowserPackageQueryResult(
    int Version,
    BrowserPackageQueryResultKind Kind,
    BrowserPackageQueryEvent? Value,
    BrowserPackageQueryOperationFailureKind? FailureKind,
    string? Error,
    string? Diagnostic,
    string? Reason)
{
    internal static BrowserPackageQueryResult From(
        BrowserManagedOperationResult<
            BrowserPackageQueryEvent,
            string,
            string> result) =>
        result switch
        {
            BrowserManagedOperationResult<
                BrowserPackageQueryEvent,
                string,
                string>.Succeeded succeeded =>
                new(1, BrowserPackageQueryResultKind.Succeeded,
                    succeeded.Value, null, null, null, null),
            BrowserManagedOperationResult<
                BrowserPackageQueryEvent,
                string,
                string>.Failed failed =>
                new(1, BrowserPackageQueryResultKind.Failed, null,
                    failed.FailureKind switch
                    {
                        BrowserManagedOperationFailureKind.Expected =>
                            BrowserPackageQueryOperationFailureKind.Expected,
                        BrowserManagedOperationFailureKind.Unexpected =>
                            BrowserPackageQueryOperationFailureKind.Unexpected,
                        _ => throw new ArgumentOutOfRangeException(nameof(result)),
                    },
                    failed.Error, failed.Diagnostic, null),
            BrowserManagedOperationResult<
                BrowserPackageQueryEvent,
                string,
                string>.Canceled canceled =>
                new(1, BrowserPackageQueryResultKind.Canceled,
                    null, null, null, null,
                    BrowserManagedOperationCancelReasons.Format(canceled.Reason)),
            _ => throw new ArgumentOutOfRangeException(nameof(result)),
        };
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserPackageQueryCancellationKind>))]
public enum BrowserPackageQueryCancellationKind
{
    Requested,
    AlreadyRequested,
    NotActive,
}

public sealed record BrowserPackageQueryCancellation(
    BrowserPackageQueryCancellationKind Kind,
    string? Reason)
{
    internal static BrowserPackageQueryCancellation From(
        BrowserManagedCancellationRequestResult result) =>
        result switch
        {
            BrowserManagedCancellationRequestResult.Requested requested =>
                new(BrowserPackageQueryCancellationKind.Requested,
                    BrowserManagedOperationCancelReasons.Format(requested.Reason)),
            BrowserManagedCancellationRequestResult.AlreadyRequested requested =>
                new(BrowserPackageQueryCancellationKind.AlreadyRequested,
                    BrowserManagedOperationCancelReasons.Format(requested.Reason)),
            BrowserManagedCancellationRequestResult.NotActive =>
                new(BrowserPackageQueryCancellationKind.NotActive, null),
            _ => throw new ArgumentOutOfRangeException(nameof(result)),
        };
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserPackageQueryMatchCreditKind>))]
public enum BrowserPackageQueryMatchCreditKind
{
    Granted,
    NotActive,
}

public sealed record BrowserPackageQueryMatchCreditResponse(
    BrowserPackageQueryMatchCreditKind Kind,
    int? AdditionalMatchCredit)
{
    internal static BrowserPackageQueryMatchCreditResponse From(
        BrowserPackageQueryMatchCreditRequestResult result) =>
        result switch
        {
            BrowserPackageQueryMatchCreditRequestResult.Granted granted =>
                new(BrowserPackageQueryMatchCreditKind.Granted,
                    granted.AdditionalMatchCredit),
            BrowserPackageQueryMatchCreditRequestResult.NotActive =>
                new(BrowserPackageQueryMatchCreditKind.NotActive, null),
            _ => throw new ArgumentOutOfRangeException(nameof(result)),
        };
}

/// <summary>
/// Declared package dependency groups and one selected assembly's direct references. Dependency
/// parsing and compatible target-framework selection belong to
/// <c>PackageDependencyGroupsQuery</c>; direct references belong to
/// <c>AssemblyContextReferencesQuery</c>.
/// </summary>
public sealed record BrowserPackageDependencies(
    string Package,
    string Version,
    string ActiveFramework,
    string? Assembly,
    BrowserPackageDependencyGroup[] DependencyGroups,
    BrowserAssemblyReferenceResult AssemblyReferences,
    string? DependencyGroupError,
    BrowserCompileLibraryAvailability CompileLibrary);

public union BrowserAssemblyReferenceResult(BrowserAssemblyReferenceList, string);

public sealed record BrowserAssemblyReferenceList(
    BrowserAssemblyReference[] References);

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
    int CurrentVersionInsertionIndex,
    string? PreviousVersion,
    string? PreviousVersionUnavailableReason);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BrowserPackageVersions))]
[JsonSerializable(typeof(BrowserPackageSurface))]
[JsonSerializable(typeof(BrowserPackageDocumentContent))]
[JsonSerializable(typeof(BrowserMemberDocumentation))]
[JsonSerializable(typeof(BrowserPackageCacheStats))]
[JsonSerializable(typeof(BrowserPlatformCatalog))]
[JsonSerializable(typeof(BrowserPackageQueryFacetCatalog))]
[JsonSerializable(typeof(BrowserPackageAssemblyQueryPattern[]))]
[JsonSerializable(typeof(BrowserGalleryDiscoveryCatalog))]
[JsonSerializable(typeof(BrowserPackageQueryEvent))]
[JsonSerializable(typeof(BrowserPackageQueryResult))]
[JsonSerializable(typeof(BrowserPackageQueryCancellation))]
[JsonSerializable(typeof(BrowserPackageQueryMatchCreditResponse))]
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
