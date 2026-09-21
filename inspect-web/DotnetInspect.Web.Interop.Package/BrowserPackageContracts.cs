using System.Text.Json.Serialization;
using DotnetInspector.Queries;
using DotnetInspector.Sections;

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

public sealed record BrowserPackageLoadResult(
    BrowserPackageVersionSettlementInspection VersionSettlement,
    BrowserPackageInfoMeasurementInspection? PackageInfo,
    BrowserPackageSurface? Surface);

public sealed record BrowserPackageInfoMeasurementInspection(
    BrowserInspectionContentKind ContentKind,
    BrowserPackageInfoMeasurements Content,
    BrowserInspectionPortableProjection PortableProjection,
    BrowserInspectionDiagnostic[] Diagnostics);

public sealed record BrowserPackageInfoMeasurements(
    string Status,
    string PackageId,
    string PackageVersion,
    long? CompressedPackageBytes,
    string? SelectedTargetFramework,
    string[]? AvailableTargetFrameworks,
    string[]? SelectedTargetFrameworkFolders,
    long? SelectedLibraryPayloadBytes,
    int? SelectedLibraryCount,
    string? Detail,
    string? UnavailableReason,
    bool HasSelectedSlice);

public sealed record BrowserPackageVersionSettlementInspection(
    BrowserInspectionContentKind ContentKind,
    BrowserPackageVersionSettlementOutcome Content,
    BrowserInspectionPortableProjection PortableProjection,
    BrowserInspectionDiagnostic[] Diagnostics);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserPackageVersionSettlementOutcomeKind>))]
public enum BrowserPackageVersionSettlementOutcomeKind
{
    Settled,
    NotSettled,
}

public sealed record BrowserPackageVersionSettlementOutcome(
    BrowserPackageVersionSettlementOutcomeKind Kind,
    BrowserPackageVersionSettlementResult? Result,
    BrowserPackageVersionSettlementFailure? Failure);

public sealed record BrowserPackageVersionSettlementRequest(
    string PackageId,
    string? Version);

public sealed record BrowserPackageVersionSettlementCoordinate(
    string PackageId,
    string Version);

public sealed record BrowserPackageVersionSettlementResult(
    BrowserPackageVersionSettlementRequest Request,
    BrowserPackageVersionSettlementCoordinate Coordinate,
    bool IncludePrerelease,
    string? Freshness,
    BrowserPackageVersionSettlementListing[] Listings,
    BrowserPackageVersionSettlementSourceListing[] SourceListings);

public sealed record BrowserPackageVersionSettlementListing(
    string Version,
    bool Listed);

public sealed record BrowserPackageVersionSettlementSourceListing(
    string Version,
    string Feed,
    bool Listed);

public sealed record BrowserPackageVersionSettlementFailure(
    BrowserPackageVersionSettlementRequest Request,
    string Kind,
    string Reason,
    bool OperationTimedOut,
    BrowserPackageVersionSettlementAuthorityFailure[] AuthorityFailures);

public sealed record BrowserPackageVersionSettlementAuthorityFailure(
    string Authority,
    string Kind,
    string Message,
    string? TimeoutKind);

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
    int MaxPackageEntries,
    int Workspaces,
    int MaxWorkspaces,
    int MaxWorkspaceAssembliesPerRole,
    long ResidentBytes,
    long MaxResidentBytes,
    long MaxWorkspaceRetainedImageBytes);

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

[JsonConverter(typeof(JsonStringEnumConverter<BrowserPackageQueryAcquisitionTier>))]
public enum BrowserPackageQueryAcquisitionTier
{
    Nuspec,
    PackageContent,
    SearchMetadata,
    Assembly,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserPackageQueryExecutionClass>))]
public enum BrowserPackageQueryExecutionClass
{
    SearchMetadata,
    Nuspec,
    NuspecExpensive,
    PackageContent,
    Metadata,
    MetadataExpensive,
}

public sealed record BrowserPackageQueryPresetDescriptor(
    string Key,
    string Operator,
    string Value,
    string Label,
    string Summary,
    int Weight,
    BrowserPackageQueryAcquisitionTier Tier,
    BrowserPackageQueryExecutionClass ExecutionClass,
    string? SelectionGroupId,
    bool CombinesWithinSelectionGroup,
    string? ReplacementGroupId,
    string? DisplayGroupId,
    string? DisplayGroupLabel);

public sealed record BrowserPackageQueryCatalog(
    BrowserPackageQueryPresetDescriptor[] Presets,
    BrowserPackageQueryTermDescriptor[] Terms);

public sealed record BrowserPackageQueryTermDescriptor(
    string Key,
    string Label,
    string Summary,
    int Weight,
    BrowserPackageQueryAcquisitionTier Tier,
    BrowserPackageQueryExecutionClass ExecutionClass,
    string[] Operators,
    string ValueKind,
    string Example);

public sealed record BrowserPackageQueryTerm(
    string Key,
    string Operator,
    string Value);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserPackageQueryEvidenceScope>))]
public enum BrowserPackageQueryEvidenceScope
{
    Package,
    Query,
}

public sealed record BrowserPackageQueryEvidenceSummary(
    int Count,
    string[] Preview);

public sealed record BrowserPackageQueryAnswer(
    string Id,
    string Value,
    BrowserPackageQueryTerm? Term = null);

public sealed record BrowserPackageQueryEvidenceProperty(
    string Name,
    string Value);

public sealed record BrowserPackageQueryEvidence(
    string Id,
    BrowserPackageQueryEvidenceScope Scope,
    BrowserPackageQueryEvidenceSummary? Summary,
    BrowserPackageQueryEvidenceProperty[] Properties,
    long? Number,
    BrowserPackageQueryTerm? Term = null);

public sealed record BrowserPackageQueryDeclaredDependency(
    string Id,
    string VersionRange);

public sealed record BrowserPackageQueryDeclaredDependencyGroup(
    string TargetFramework,
    BrowserPackageQueryDeclaredDependency[] Dependencies,
    bool IsImplicitManifestGroup);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserPackageQueryManifestIdentityProvenance>))]
public enum BrowserPackageQueryManifestIdentityProvenance
{
    ExpectedCoordinate,
    SelfAttested,
}

public sealed record BrowserPackageQueryManifest(
    string PackageId,
    string Version,
    string ManifestVersion,
    string? Description,
    string? Authors,
    string? Repository,
    string? RepositoryType,
    string? RepositoryCommit,
    string? License,
    string? LicenseUrl,
    string[] PackageTypes,
    bool IsToolPackage,
    string? ReadmeFile,
    BrowserPackageQueryDeclaredDependencyGroup[] DependencyGroups,
    string? IconFile,
    string? IconUrl,
    BrowserPackageQueryManifestIdentityProvenance IdentityProvenance);

public sealed record BrowserPackageQueryRow(
    string PackageId,
    string Version,
    BrowserPackageQueryAcquisitionTier Tier,
    BrowserPackageQueryAnswer[] Answers,
    BrowserPackageQueryEvidence[] Evidence,
    long? TotalDownloads,
    bool? Verified,
    string Producer,
    string? Description = null,
    string? RootRequest = null)
{
    public string[] Owners { get; init; } = [];

    public BrowserPackageQueryManifest? Manifest { get; init; }
}

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
    DependencyTraversal,
    AssemblyAcquisition,
    AssemblyEvaluation,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserPackageQueryManifestFailureReason>))]
public enum BrowserPackageQueryManifestFailureReason
{
    MalformedXml,
    UnsupportedDocumentShape,
    IdentityMismatch,
    InvalidDependencyContract,
    ConfiguredLimitExceeded,
    InvalidIdentityContract,
}

public sealed record BrowserPackageQueryFailure(
    string? PackageId,
    string? Version,
    string Producer,
    BrowserPackageQueryFailureKind Kind,
    string Message)
{
    public BrowserPackageQueryManifestFailureReason? ManifestFailureReason
    {
        get;
        init;
    }
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserPackageQueryProgressPhase>))]
public enum BrowserPackageQueryProgressPhase
{
    Search,
    Manifest,
    PackageContent,
    DependencyTraversal,
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
    int? SemanticMisses = null,
    int? NotApplicable = null,
    string? Scope = null)
{
    public int? Occurrences { get; init; }

    public int? NotEvaluated { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserPackageAssemblySemanticCandidateOutcomeKind>))]
public enum BrowserPackageAssemblySemanticCandidateOutcomeKind
{
    Matched,
    NoMatch,
    NotApplicable,
    Failure,
    NotEvaluated,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserPackageAssemblySemanticFailureKind>))]
public enum BrowserPackageAssemblySemanticFailureKind
{
    Acquisition,
    Evaluation,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserPackageAssemblySemanticNonEvaluationKind>))]
public enum BrowserPackageAssemblySemanticNonEvaluationKind
{
    OperationDeadline,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserPackageAssemblyNotApplicableReason>))]
public enum BrowserPackageAssemblyNotApplicableReason
{
    NoCompileAssets,
    NoMatchingTargetFramework,
    EmptyCompileGroup,
    NoImplementationCounterpart,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserPackageAssemblySemanticPopulationCompletionKind>))]
public enum BrowserPackageAssemblySemanticPopulationCompletionKind
{
    ExactPackageComplete,
    PrefixExhausted,
    CandidateLimitReached,
    SourcePageLimitReached,
    ClientPageLimitReached,
    SourceFailed,
}

public sealed record BrowserPackageAssemblySemanticOccurrence(
    string ModuleVersionId,
    int MethodDefinitionToken,
    int IlOffset,
    int UserStringToken,
    int LiteralCharacterCount,
    string LiteralText);

public sealed record BrowserPackageAssemblySemanticSelectedAsset(
    string Path,
    string AssemblyName,
    string TargetFramework,
    string Sequence,
    int Ordinal,
    int UnevaluatedSiblings,
    string RootRequest);

public sealed record BrowserPackageAssemblySemanticResult(
    int CandidateOrdinal,
    string PackageId,
    string Version,
    string Producer,
    BrowserPackageAssemblySemanticSelectedAsset SelectedAsset,
    BrowserPackageAssemblySemanticOccurrence[] Occurrences);

public sealed record BrowserPackageAssemblySemanticCandidateOutcome(
    BrowserPackageAssemblySemanticCandidateOutcomeKind Kind,
    int CandidateOrdinal,
    string PackageId,
    string Version,
    string Producer,
    BrowserPackageAssemblySemanticResult? Result,
    BrowserPackageAssemblySemanticSelectedAsset? SelectedAsset,
    string? RootRequest,
    BrowserPackageAssemblyNotApplicableReason? NotApplicableReason,
    BrowserPackageAssemblySemanticFailureKind? FailureKind,
    string? FailureStage,
    BrowserPackageAssemblySemanticNonEvaluationKind? NonEvaluationKind,
    string? TimeoutKind,
    double? TimeoutSeconds,
    string? Message);

public sealed record BrowserPackageAssemblySemanticPopulationFailure(
    int? CandidateOrdinal,
    string? PackageId,
    string? Version,
    string Authority,
    string Kind,
    string Message,
    string? TimeoutKind,
    double? TimeoutSeconds);

public sealed record BrowserPackageAssemblySemanticPopulation(
    int RequestedCandidates,
    int Candidates,
    BrowserPackageAssemblySemanticPopulationCompletionKind Completion,
    bool IsRequestedPopulationComplete,
    BrowserPackageAssemblySemanticPopulationFailure[] Failures);

public sealed record BrowserPackageAssemblySemanticCompletion(
    BrowserPackageAssemblySemanticPopulationCompletionKind Population,
    bool IsRequestedPopulationComplete,
    bool AllCandidatesHaveTerminalOutcomes,
    bool HasFailures,
    bool IsSemanticEvaluationComplete,
    bool IsOperationDeadlineExpired);

public sealed record BrowserPackageAssemblySemanticDocument(
    BrowserPackageAssemblySemanticPopulation Population,
    BrowserPackageAssemblySemanticResult[] Results,
    BrowserPackageAssemblySemanticCandidateOutcome[] CandidateOutcomes,
    int CandidateCount,
    int EvaluatedCandidateCount,
    int NotEvaluatedCount,
    int MatchedPackageCount,
    int OccurrenceCount,
    int SemanticMissCount,
    int NotApplicableCount,
    int FailureCount,
    BrowserPackageAssemblySemanticCompletion Completion);

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

[JsonConverter(typeof(JsonStringEnumConverter<BrowserInspectionContentKind>))]
public enum BrowserInspectionContentKind
{
    [JsonStringEnumMemberName("result")]
    Result,

    [JsonStringEnumMemberName("document")]
    Document,

    [JsonStringEnumMemberName("outcome")]
    Outcome,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserInspectionPortableProjectionKind>))]
public enum BrowserInspectionPortableProjectionKind
{
    Available,
    NonProjectable,
}

public sealed record BrowserInspectionPortableProjection(
    BrowserInspectionPortableProjectionKind Kind,
    string? FullUrl,
    string? Packet,
    string? Path,
    BrowserInspectionPortableProjectionFailureReason? Reason,
    string? Explanation);

[JsonConverter(
    typeof(JsonStringEnumConverter<
        BrowserInspectionPortableProjectionFailureReason>))]
public enum BrowserInspectionPortableProjectionFailureReason
{
    [JsonStringEnumMemberName("notSupported")]
    NotSupported,

    [JsonStringEnumMemberName("invalid")]
    Invalid,

    [JsonStringEnumMemberName("incomplete")]
    Incomplete,

    [JsonStringEnumMemberName("unavailable")]
    Unavailable,

    [JsonStringEnumMemberName("failed")]
    Failed,
}

internal static class BrowserInspectionWireProjection
{
    internal static BrowserInspectionContentKind Project(
        InspectionContentKind contentKind) =>
        contentKind switch
        {
            InspectionContentKind.Result =>
                BrowserInspectionContentKind.Result,
            InspectionContentKind.Document =>
                BrowserInspectionContentKind.Document,
            InspectionContentKind.Outcome =>
                BrowserInspectionContentKind.Outcome,
            _ => throw new InvalidOperationException(
                "Unknown inspection content kind."),
        };

    internal static BrowserInspectionPortableProjectionFailureReason Project(
        InspectionPortableProjectionFailureReason reason) =>
        reason switch
        {
            InspectionPortableProjectionFailureReason.NotSupported =>
                BrowserInspectionPortableProjectionFailureReason.NotSupported,
            InspectionPortableProjectionFailureReason.Invalid =>
                BrowserInspectionPortableProjectionFailureReason.Invalid,
            InspectionPortableProjectionFailureReason.Incomplete =>
                BrowserInspectionPortableProjectionFailureReason.Incomplete,
            InspectionPortableProjectionFailureReason.Unavailable =>
                BrowserInspectionPortableProjectionFailureReason.Unavailable,
            InspectionPortableProjectionFailureReason.Failed =>
                BrowserInspectionPortableProjectionFailureReason.Failed,
            _ => throw new InvalidOperationException(
                "Unknown portable projection failure reason."),
        };
}

public sealed record BrowserInspectionDiagnostic(
    string Code,
    string Severity,
    string Summary,
    string? Correspondence);

public sealed record BrowserExactLibraryApiInspection(
    BrowserInspectionContentKind ContentKind,
    BrowserExactLibraryApiInspectionResult Content,
    BrowserInspectionPortableProjection PortableProjection,
    BrowserInspectionDiagnostic[] Diagnostics);

public enum BrowserExactLibraryApiInspectionOutcome
{
    Available,
    NotFound,
    Ambiguous,
    Unavailable,
}

public enum BrowserExactLibraryApiInspectionFailureKind
{
    ContextLoad,
    PackageMismatch,
    CompileSelectionUnavailable,
    LibraryNotFound,
    LibraryAmbiguous,
    ParticipantUnavailable,
    InspectionIncomplete,
    ProjectionTruncated,
}

public enum BrowserExactLibraryApiAssetKind
{
    Reference,
    Library,
}

public enum BrowserExactLibraryApiProjectionLimit
{
    Participants,
    Types,
    Members,
    InspectionFailures,
    TypeForwarders,
    MetadataRows,
    RetainedTextCharacters,
}

public sealed record BrowserExactLibraryApiSourceCoordinate(
    string PackageId,
    string PackageVersion,
    string Producer,
    string? Framework);

public sealed record BrowserExactLibraryApiAsset(
    string Id,
    string Path,
    string AssemblyName,
    string TargetFramework,
    BrowserExactLibraryApiAssetKind Kind);

public sealed record BrowserExactLibraryApiAssemblyReferenceIdentity(
    string Name,
    string? Version,
    string? Culture,
    string? PublicKeyToken);

public sealed record BrowserExactLibraryApiAssemblyIdentity(
    BrowserExactLibraryApiAssemblyReferenceIdentity Identity,
    Guid ModuleVersionId);

public sealed record BrowserExactLibraryApiFacet(
    string Id,
    string SingularLabel,
    string PluralLabel,
    int Weight,
    int Count,
    bool IsDefault);

public sealed record BrowserExactLibraryApiNamespace(
    string Name,
    int Count);

public sealed record BrowserExactLibraryApiInventory(
    int PublicTypeCount,
    int PublicMemberCount,
    int PublicMethodCount,
    int PublicPropertyCount,
    BrowserExactLibraryApiFacet[] TypeKinds,
    BrowserExactLibraryApiNamespace[] Namespaces);

public sealed record BrowserExactLibraryApiProjectionTruncation(
    BrowserExactLibraryApiProjectionLimit Limit,
    int Bound,
    int ProjectedParticipants,
    int OmittedParticipants,
    int ProjectedTypes,
    int ProjectedMembers,
    int ProjectedInspectionFailures,
    int ProjectedTypeForwarders,
    int InspectedMetadataRows,
    int ProjectedRetainedTextCharacters);

public sealed record BrowserExactLibraryApiInspectionFailure(
    BrowserExactLibraryApiInspectionFailureKind Kind,
    string Detail,
    BrowserExactLibraryApiAssemblyReferenceIdentity? SubjectAssembly);

public sealed record BrowserExactLibraryApiInspectionResult(
    BrowserExactLibraryApiInspectionOutcome Outcome,
    string PackageId,
    string PackageVersion,
    string RequestedTargetFramework,
    string RequestedLibrary,
    BrowserExactLibraryApiSourceCoordinate? Source,
    BrowserExactLibraryApiAsset? Asset,
    BrowserExactLibraryApiAssemblyIdentity? Assembly,
    BrowserExactLibraryApiInventory? Inventory,
    BrowserExactLibraryApiProjectionTruncation? Truncation,
    BrowserExactLibraryApiInspectionFailure[] Failures,
    bool IsComplete,
    bool IsAvailable);

public sealed record BrowserPackageQueryDocument(
    BrowserPackageQueryRow[] Results,
    bool HasPackages,
    BrowserPackageQueryFailure[] Failures,
    BrowserPackageQueryCompletion Completion)
{
    public BrowserPackageAssemblySemanticDocument? AssemblySemantic { get; init; }
}

public sealed record BrowserPackageQueryInspection(
    BrowserInspectionContentKind ContentKind,
    BrowserPackageQueryDocument Content,
    BrowserInspectionPortableProjection PortableProjection,
    BrowserInspectionDiagnostic[] Diagnostics);

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
    BrowserPackageQueryInspection? Inspection,
    BrowserPackageQueryOperationFailureKind? FailureKind,
    string? Error,
    string? Diagnostic,
    string? Reason)
{
    internal static BrowserPackageQueryResult ExpectedFailure(string error) =>
        new(
            3,
            BrowserPackageQueryResultKind.Failed,
            null,
            null,
            BrowserPackageQueryOperationFailureKind.Expected,
            error,
            error,
            null);

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
                new(3, BrowserPackageQueryResultKind.Succeeded,
                    succeeded.Value, null, null, null, null, null),
            BrowserManagedOperationResult<
                BrowserPackageQueryEvent,
                string,
                string>.Failed failed =>
                new(3, BrowserPackageQueryResultKind.Failed, null, null,
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
                new(3, BrowserPackageQueryResultKind.Canceled,
                    null, null, null, null, null,
                    BrowserManagedOperationCancelReasons.Format(canceled.Reason)),
            _ => throw new ArgumentOutOfRangeException(nameof(result)),
        };

    internal static BrowserPackageQueryResult From(
        BrowserManagedOperationResult<
            BrowserPackageQueryInspection,
            string,
            string> result) =>
        result switch
        {
            BrowserManagedOperationResult<
                BrowserPackageQueryInspection,
                string,
                string>.Succeeded succeeded =>
                new(3, BrowserPackageQueryResultKind.Succeeded,
                    null, succeeded.Value, null, null, null, null),
            BrowserManagedOperationResult<
                BrowserPackageQueryInspection,
                string,
                string>.Failed failed =>
                new(3, BrowserPackageQueryResultKind.Failed, null, null,
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
                BrowserPackageQueryInspection,
                string,
                string>.Canceled canceled =>
                new(3, BrowserPackageQueryResultKind.Canceled,
                    null, null, null, null, null,
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
    BrowserPackageDependencyDeclarationFailure[] DeclarationFailures,
    BrowserAssemblyReferenceResult AssemblyReferences,
    string? DependencyGroupError,
    BrowserCompileLibraryAvailability CompileLibrary);

public sealed record BrowserLibraryQueryInspection(
    BrowserInspectionContentKind ContentKind,
    BrowserLibraryQueryDocument Content,
    BrowserInspectionPortableProjection PortableProjection,
    BrowserInspectionDiagnostic[] Diagnostics);

public sealed record BrowserLibraryQueryDocument(
    BrowserLibraryQueryRow[] Results,
    BrowserLibraryQueryFailure[] Failures,
    BrowserLibraryQuerySummary Summary);

public sealed record BrowserLibraryQueryRow(
    string AssetId,
    string Library,
    string Path,
    string Source,
    string? Version,
    string SourceKind,
    string? TargetFramework,
    string[] MatchedReferences);

public sealed record BrowserLibraryQueryFailure(
    string? AssetId,
    string? Library,
    string? Path,
    string? Source,
    string Kind,
    string Message);

public sealed record BrowserLibraryQuerySummary(
    int PopulationCandidates,
    int CandidateLimit,
    int Candidates,
    int Matches,
    int Failures,
    string IncompleteReasons,
    bool IsComplete);

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

[JsonConverter(typeof(JsonStringEnumConverter<
    BrowserPackageDependencyDeclarationFailureKind>))]
public enum BrowserPackageDependencyDeclarationFailureKind
{
    ConflictingPackageDeclaration,
    InvalidPackageDeclaration,
    RestoredProject,
    AuthoredProject,
    AuthoredProjectUnresolvedSyntax,
}

public sealed record BrowserPackageDependencyDeclarationFailure(
    BrowserPackageDependencyDeclarationFailureKind Kind,
    string? Framework,
    string? Package,
    int? SourceOccurrenceCount);

public sealed record BrowserPackagePruningRequest(
    int SchemaVersion,
    string Family,
    string TargetFramework,
    string PlatformVersion,
    BrowserPackagePruningSupply[] Supplies);

public sealed record BrowserPackagePruningSupply(
    string Pack,
    string Family,
    string Package,
    string Version);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserPackagePruningCompletion>))]
public enum BrowserPackagePruningCompletion
{
    Complete,
    Partial,
    Failed,
    NotApplicable,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserPackagePruningDisposition>))]
public enum BrowserPackagePruningDisposition
{
    PlatformDelegation,
    PackageRetained,
    CandidateUnavailable,
    NotEvaluated,
}

public sealed record BrowserPackagePruningResult(
    int SchemaVersion,
    string Package,
    string Version,
    string TargetFramework,
    string? SelectedFramework,
    string Family,
    string PlatformVersion,
    BrowserPackagePruningCompletion Completion,
    BrowserPackagePruningRow[] Rows,
    BrowserPackageDependencyDeclarationFailure[] DeclarationFailures,
    BrowserPackagePruningSummary Summary,
    string? Message);

public sealed record BrowserPackagePruningRow(
    string Package,
    string RequestedRange,
    string? CandidateVersion,
    string? PlatformSuppliedVersion,
    BrowserPackagePruningDisposition Disposition,
    string Reason);

public sealed record BrowserPackagePruningSummary(
    int Declarations,
    int Evaluated,
    int Delegated,
    int Retained,
    int NotEvaluated,
    int Failed,
    int DeclarationFailures);

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

[JsonConverter(typeof(JsonStringEnumConverter<BrowserPackageGraphIdentityRole>))]
public enum BrowserPackageGraphIdentityRole
{
    Inspected,
    SamePrefix,
    External,
}

public sealed record BrowserPackageVersions(
    string[] Versions,
    int CurrentVersionInsertionIndex,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? PreviousVersion,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? PreviousVersionUnavailableReason);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BrowserPackageVersions))]
[JsonSerializable(typeof(BrowserPackageLoadResult))]
[JsonSerializable(typeof(BrowserPackageSurface))]
[JsonSerializable(typeof(BrowserPackageDocumentContent))]
[JsonSerializable(typeof(BrowserPackageCacheStats))]
[JsonSerializable(typeof(BrowserPlatformCatalog))]
[JsonSerializable(typeof(BrowserPackageQueryCatalog))]
[JsonSerializable(typeof(BrowserPackageQueryTerm[]))]
[JsonSerializable(typeof(BrowserPackageQueryEvent))]
[JsonSerializable(typeof(BrowserPackageQueryDocument))]
[JsonSerializable(typeof(BrowserPackageQueryInspection))]
[JsonSerializable(typeof(BrowserExactLibraryApiInspection))]
[JsonSerializable(typeof(BrowserPackageQueryResult))]
[JsonSerializable(typeof(BrowserPackageQueryCancellation))]
[JsonSerializable(typeof(BrowserPackageQueryMatchCreditResponse))]
[JsonSerializable(typeof(BrowserPackageDependencies))]
[JsonSerializable(typeof(BrowserLibraryQueryInspection))]
[JsonSerializable(typeof(BrowserPackagePruningRequest))]
[JsonSerializable(typeof(BrowserPackagePruningResult))]
[JsonSerializable(typeof(BrowserWorkspacePackage[]))]
[JsonSerializable(typeof(BrowserWorkspacePackageOccurrenceView))]
[JsonSerializable(typeof(BrowserWorkspacePackageOccurrenceActivation))]
[JsonSerializable(typeof(BrowserDependencyCoordinateCandidate[]))]
[JsonSerializable(typeof(BrowserDependencyCoordinateMatch))]
[JsonSerializable(typeof(BrowserPackageGraphIdentityRole[]))]
[JsonSerializable(typeof(BrowserTypeCandidate[]))]
[JsonSerializable(typeof(BrowserTypeSearchHit[]))]
[JsonSerializable(typeof(string[]))]
internal sealed partial class BrowserPackageJsonContext : JsonSerializerContext;
