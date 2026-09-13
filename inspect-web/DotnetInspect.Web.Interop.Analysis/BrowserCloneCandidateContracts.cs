using System.Text.Json.Serialization;

namespace DotnetInspect.Web.Interop.Analysis;

public sealed record BrowserCloneCandidateRequest(
    int SchemaVersion,
    BrowserCloneCandidatePackage[] Packages,
    int SelectedPackageIndex,
    string Assembly,
    BrowserCloneCandidateSeedRequest Seed,
    BrowserCloneCandidateBreadth Breadth,
    BrowserCloneCandidateDiscovery Discovery);

public sealed record BrowserCloneCandidatePackage(
    string PackageId,
    string Version,
    string TargetFramework);

public sealed record BrowserCloneCandidateSeedRequest(
    BrowserCloneCandidateSeedKind Kind,
    string? TypeDefinitionId,
    BrowserCloneMemberAnchor? Member,
    BrowserCloneCandidateBodySelection? Body);

public sealed record BrowserCloneCandidateBodySelection(
    string MemberName,
    string SelectorKey,
    int MetadataToken);

public sealed record BrowserCloneCandidateResult(
    int SchemaVersion,
    BrowserCloneCandidateRequest Request,
    BrowserCloneCandidateResultKind Kind,
    BrowserCloneCandidateDocument? Document,
    BrowserCloneAssemblyIdentity? SeedLibrary,
    BrowserCloneCandidateOpenFailureKind? OpenFailureKind,
    BrowserCloneCandidateFailure? Failure,
    BrowserCloneCandidatePresentationRejectionKind?
        PresentationRejectionKind,
    BrowserCloneAssemblyIdentity? Subject,
    string? Detail,
    BrowserMetadataRootMalformedReason? MetadataRootReason);

public sealed record BrowserCloneCandidateDocument(
    int SchemaVersion,
    BrowserCloneCandidateSeed Seed,
    BrowserCloneCandidateBreadth Breadth,
    BrowserCloneCandidateDiscovery Discovery,
    double NameSimilarityThreshold,
    BrowserCloneCandidateLimits Limits,
    bool ScopeChangedDuringSearch,
    bool CoverageIsComplete,
    BrowserCloneCandidateRow[] Rows,
    BrowserCloneCandidateSeedCoverage[] Seeds,
    BrowserCloneCandidateLibraryCoverage[] Libraries,
    BrowserCloneCandidateReceipt Receipt,
    bool ResultLimitReached,
    int ResultLimitOmittedPairs);

public sealed record BrowserCloneCandidateSeed(
    BrowserCloneCandidateSeedKind Kind,
    BrowserMetadataTypeDefinitionName? Type,
    BrowserCloneMemberAnchor? Member);

public sealed record BrowserMetadataTypeDefinitionName(
    string Namespace,
    string[] Segments);

public sealed record BrowserCloneMemberAnchor(
    string StableSelector,
    string CanonicalSignature,
    string Fingerprint,
    string TypeFullName,
    string MemberName);

public sealed record BrowserCloneAssemblyIdentity(
    string Name,
    string? Version,
    string? Culture,
    string? PublicKeyToken);

public sealed record BrowserCloneCandidateProvenance(
    BrowserCloneCandidateProvenanceKind Kind,
    string? PackageId = null,
    string? PackageVersion = null,
    string? Tfm = null,
    string? Rid = null,
    string? Framework = null,
    string? FrameworkVersion = null,
    string? Project = null,
    string? ResolverSource = null,
    string? ContentRef = null,
    string? Digest = null,
    string? DeclaredName = null);

public sealed record BrowserCloneCandidateParticipant(
    int Ordinal,
    BrowserCloneAssemblyIdentity Assembly,
    BrowserCloneCandidateProvenance Provenance,
    string? ModuleVersionId);

public sealed record BrowserCloneCandidateMethod(
    BrowserCloneCandidateParticipant Participant,
    string ModuleVersionId,
    int MethodDefinitionToken,
    string AddressDisplay);

public sealed record BrowserCloneCandidateSimilarity(
    int Score,
    int OperationScore,
    int PositionScore,
    int BlockScore,
    int EdgeScore,
    int LocalScore,
    int SeedInstructions,
    int CandidateInstructions,
    int SeedBlocks,
    int CandidateBlocks,
    int SeedEdges,
    int CandidateEdges,
    int SeedLocals,
    int CandidateLocals);

public sealed record BrowserCloneCandidateNameQualification(
    double DeclaringTypeSimilarity,
    double MemberSimilarity);

public sealed record BrowserCloneCandidateRow(
    int Rank,
    BrowserCloneCandidateMethod Left,
    BrowserCloneCandidateMethod Right,
    BrowserCloneCandidateSimilarity Similarity,
    BrowserCloneCandidateNameQualification? NameQualification);

public sealed record BrowserCloneCandidateFailure(
    BrowserCloneCandidateFailureKind Kind,
    BrowserCloneAssemblyIdentity? Subject,
    string Detail);

public sealed record BrowserCloneCandidateAnalysisBlocker(
    BrowserCloneCandidateAnalysisBlockerKind Kind,
    string Detail);

public sealed record BrowserCloneCandidateSeedCoverage(
    BrowserCloneCandidateMethod Seed,
    BrowserCloneCandidateRetrievalDisposition Disposition,
    int RankedPairs,
    int SuppressedPairs,
    BrowserCloneCandidateAnalysisBlocker[] Blockers,
    BrowserCloneCandidateFailure[] Failures,
    bool IsComplete);

public sealed record BrowserCloneCandidateLibraryCoverage(
    BrowserCloneCandidateParticipant Participant,
    BrowserCloneCandidateParticipantMembership Membership,
    bool Admitted,
    int CandidateMethods,
    int DiscoveredMethods,
    long RetrievalPairs,
    long NameComparisonWork,
    BrowserCloneCandidateFailure[] Failures,
    BrowserCloneCandidateAnalysisBlocker[] AnalysisBlockers,
    bool IsComplete);

public sealed record BrowserCloneCandidateLimits(
    int MaximumResults,
    int MaximumSeedMethods,
    int MaximumCandidateMethods,
    int MaximumParticipants,
    long MaximumRetrievalPairs,
    int MaximumRetrievalChunkMethods,
    int MaximumNameCharacters,
    long MaximumNameComparisonWork,
    long MaximumNameCacheCells,
    BrowserCloneComparisonLimits? ComparisonLimits);

public sealed record BrowserCloneComparisonLimits(
    int MaximumInstructions,
    int MaximumBlocks,
    int MaximumEdges,
    int MaximumLocals,
    int MaximumVerificationSteps,
    int MaximumBodyBytes,
    int MaximumNearAlignmentIndexSteps,
    int MaximumNearAlignmentCandidates,
    int MaximumNearAlignmentVerificationSteps,
    int MaximumNearAlignmentAlternatives,
    int MaximumNearBlockElements);

public sealed record BrowserCloneCandidateReceipt(
    int SeedMethods,
    int CandidateMethods,
    int DiscoveredMethods,
    int AdmittedLibraries,
    int ExcludedLibraries,
    long NameComparisonWork,
    long RetrievalPairs,
    int RetrievalCalls,
    int RankedPairs,
    int SuppressedPairs,
    int ReturnedPairs,
    bool ResultLimitReached);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserCloneCandidateResultKind>))]
public enum BrowserCloneCandidateResultKind
{
    Available,
    Rejected,
    Failed,
    Unrepresentable,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserCloneCandidateSeedKind>))]
public enum BrowserCloneCandidateSeedKind
{
    Library,
    Type,
    Member,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserCloneCandidateBreadth>))]
public enum BrowserCloneCandidateBreadth
{
    // Missing JSON values use the product default.
    Everything,
    Self,
    SelfAndRegisteredEcosystems,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserCloneCandidateDiscovery>))]
public enum BrowserCloneCandidateDiscovery
{
    // Missing JSON values use the product default.
    SimilarNames,
    All,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserCloneCandidateProvenanceKind>))]
public enum BrowserCloneCandidateProvenanceKind
{
    Package,
    Platform,
    Project,
    Local,
    Designated,
    Embedded,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserCloneCandidateParticipantMembership>))]
public enum BrowserCloneCandidateParticipantMembership
{
    ContainingLibrary,
    RegisteredEcosystem,
    Available,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserCloneCandidateRetrievalDisposition>))]
public enum BrowserCloneCandidateRetrievalDisposition
{
    Completed,
    Unsupported,
    LimitReached,
    Failed,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserCloneCandidateAnalysisBlockerKind>))]
public enum BrowserCloneCandidateAnalysisBlockerKind
{
    MetadataReadFailure,
    MethodLimit,
    SeedUnsupported,
    SeedProductionLimit,
    SeedProductionFailure,
    CandidateProductionLimit,
    CandidateProductionFailure,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserCloneCandidateFailureKind>))]
public enum BrowserCloneCandidateFailureKind
{
    SeedTypeNotFound,
    SeedTypeAmbiguous,
    SeedMemberNotFound,
    SeedMemberAmbiguous,
    SeedMemberHasNoMethodBody,
    SeedPopulationLimitReached,
    CandidatePopulationLimitReached,
    ParticipantPopulationLimitReached,
    RetrievalWorkLimitReached,
    CandidateLibraryUnavailable,
    SeedLibraryReleased,
    CandidateLibraryReleased,
    MetadataInspectionFailed,
    NameDecodeFailed,
    NameWorkLimitReached,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserCloneCandidateOpenFailureKind>))]
public enum BrowserCloneCandidateOpenFailureKind
{
    Unreadable,
    InvalidImage,
    ResourceBudget,
    UnsupportedMetadataFormat,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserMetadataRootMalformedReason>))]
public enum BrowserMetadataRootMalformedReason
{
    UnmappableMetadataDirectory,
    TruncatedFixedPrefix,
    InvalidSignature,
    InvalidVersionLength,
    TruncatedVersionField,
    MissingVersionTerminator,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserCloneCandidatePresentationRejectionKind>))]
public enum BrowserCloneCandidatePresentationRejectionKind
{
    ParticipantCoverageMissing,
    ParticipantModuleInconsistent,
}
