using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspector.Sections;

namespace DotnetInspect.Web.Interop.Metadata;

public sealed record BrowserLibraryApiDiffRequest(
    int SchemaVersion,
    string PackageId,
    string CurrentVersion,
    string TargetVersion,
    string TargetFramework,
    string CompileAssetId);

public sealed record BrowserLibraryApiDiffResult(
    int SchemaVersion,
    BrowserLibraryApiDiffRequest? Request,
    BrowserLibraryApiDiffResultKind Kind,
    BrowserLibraryApiDiffSucceeded? Value,
    BrowserLibraryApiDiffUnavailable? Unavailable,
    BrowserLibraryApiDiffRejected? Rejected,
    BrowserLibraryApiDiffFailureKind? FailureKind,
    string? Error,
    string? Diagnostic,
    string? Reason)
{
    public InspectionEnvelope<JsonElement>? Inspection { get; init; }
}

public sealed record BrowserLibraryApiDiffSucceeded(
    string LibraryIdentifier,
    string LibraryDisplay,
    BrowserLibraryApiDiffEndpoint Target,
    BrowserLibraryApiDiffEndpoint Current,
    BrowserLibraryApiDiffAggregate Aggregate,
    BrowserLibraryApiDiffType[] Types);

public sealed record BrowserLibraryApiDiffUnavailable(
    BrowserLibraryApiDiffUnavailableKind Kind,
    BrowserLibraryApiDiffEndpoint Target,
    BrowserLibraryApiDiffEndpoint Current);

public sealed record BrowserLibraryApiDiffRejected(
    BrowserLibraryApiDiffRejectionKind Kind,
    BrowserLibraryApiDiffEndpoint? Target,
    BrowserLibraryApiDiffEndpoint? Current,
    long? Bound,
    long? Observed);

public sealed record BrowserLibraryApiDiffEndpoint(
    string PackageId,
    string Version,
    string Framework,
    BrowserLibraryApiDiffCompileAsset Asset,
    BrowserLibraryApiDiffAssemblyIdentity Assembly,
    BrowserLibraryApiDiffSurfaceScope Scope,
    bool IsComplete,
    BrowserLibraryApiDiffEndpointIssue[] Issues);

public sealed record BrowserLibraryApiDiffCompileAsset(
    string Id,
    string Path,
    string AssemblyName);

public sealed record BrowserLibraryApiDiffAssemblyIdentity(
    string Name,
    string? Version,
    string? Culture,
    string? PublicKeyToken);

public sealed record BrowserLibraryApiDiffEndpointIssue(
    BrowserLibraryApiDiffEndpointIssueKind Kind,
    BrowserLibraryApiDiffProjectionTruncation? Truncation = null,
    BrowserLibraryApiDiffOpenFailureKind? OpenFailureKind = null,
    string? Detail = null,
    BrowserLibraryApiDiffMetadataRootMalformedReason? MetadataRootReason = null,
    int? Count = null,
    BrowserLibraryApiDiffInspectionFailure[]? InspectionFailures = null);

public sealed record BrowserLibraryApiDiffInspectionFailure(
    string Operation,
    int SubjectToken,
    BrowserLibraryApiDiffInspectionFailureMechanism Mechanism,
    string Kind,
    string Detail,
    BrowserLibraryApiDiffAssemblyIdentity? SubjectAssembly,
    BrowserLibraryApiDiffAssemblyIdentity? DependencyAssembly);

public sealed record BrowserLibraryApiDiffProjectionTruncation(
    BrowserLibraryApiDiffProjectionLimit Limit,
    int Bound,
    int ProjectedParticipants,
    int OmittedParticipants,
    int ProjectedTypes,
    int ProjectedMembers,
    int ProjectedInspectionFailures,
    int ProjectedTypeForwarders,
    int InspectedMetadataRows,
    int ProjectedRetainedTextCharacters);

public sealed record BrowserLibraryApiDiffAggregate(
    int ChangedTypeCount,
    int AddedTypeCount,
    int RemovedTypeCount,
    int ChangedMemberCount,
    int BreakingCount,
    int AdditiveCount,
    int PotentiallyBreakingCount);

public sealed record BrowserLibraryApiDiffType(
    string DocumentIdentifier,
    string Display,
    BrowserLibraryApiDiffTypeState State,
    bool? TypeDefinitionChanged,
    int ChangedMemberCount,
    int BreakingCount,
    int AdditiveCount,
    int PotentiallyBreakingCount,
    BrowserLibraryApiDiffTypeIdentity? Before,
    BrowserLibraryApiDiffTypeIdentity? After,
    BrowserLibraryApiDiffMember[] Members,
    BrowserLibraryApiDiffChange[] Changes);

public sealed record BrowserLibraryApiDiffTypeIdentity(
    string Identifier,
    string Namespace,
    string[] Segments,
    string Display);

public sealed record BrowserLibraryApiDiffMember(
    string DocumentIdentifier,
    BrowserLibraryApiDiffMemberPairKind PairKind,
    BrowserLibraryApiDiffMemberRelationRole Role,
    BrowserLibraryApiDiffMemberIdentity? Before,
    BrowserLibraryApiDiffMemberIdentity? After,
    BrowserLibraryApiDiffChange[] Changes,
    BrowserLibraryApiDiffMatch? Match);

/// <summary>
/// The Findings-issued correspondence provenance behind a changed relation: the
/// match tier and its confidence. Absent when the relation was committed
/// exactly or has one side only.
/// </summary>
public sealed record BrowserLibraryApiDiffMatch(
    string Tier,
    int Confidence);

/// <summary>
/// One Metadata-issued compatibility change placed on the Type or Member it
/// describes. Text is the producer's inert message and values; the Browser
/// renders it and never re-derives classification from it.
/// </summary>
public sealed record BrowserLibraryApiDiffChange(
    BrowserLibraryApiDiffChangeKind Kind,
    BrowserLibraryApiDiffChangeClassification Classification,
    BrowserLibraryApiDiffChangeCategory Category,
    string Message,
    string? OldValue,
    string? NewValue);

public sealed record BrowserLibraryApiDiffMemberIdentity(
    string DeclaringTypeIdentifier,
    string StableSelector,
    string CanonicalSignature,
    string Fingerprint,
    string TypeFullName,
    string MemberName,
    string Display);

public sealed record BrowserLibraryApiDiffCancellation(
    BrowserLibraryApiDiffCancellationKind Kind,
    string? Reason);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserLibraryApiDiffResultKind>))]
public enum BrowserLibraryApiDiffResultKind
{
    Succeeded,
    Unavailable,
    Rejected,
    Failed,
    Canceled,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserLibraryApiDiffFailureKind>))]
public enum BrowserLibraryApiDiffFailureKind
{
    Expected,
    Unexpected,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserLibraryApiDiffCancellationKind>))]
public enum BrowserLibraryApiDiffCancellationKind
{
    Requested,
    AlreadyRequested,
    NotActive,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserLibraryApiDiffUnavailableKind>))]
public enum BrowserLibraryApiDiffUnavailableKind
{
    TargetIncomplete,
    CurrentIncomplete,
    BothIncomplete,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserLibraryApiDiffRejectionKind>))]
public enum BrowserLibraryApiDiffRejectionKind
{
    LogicalLibraryMismatch,
    FindingComparisonFailed,
    CompatibilityInspectionFailed,
    MissingExactTypeIdentity,
    MissingMemberAnchor,
    DuplicateExactTypeIdentity,
    UnassociatedStructuredSubject,
    ContradictoryOccupiedSideTopology,
    ChangedTypeCountLimitExceeded,
    TypeTextLimitExceeded,
    CollectionEntryLimitExceeded,
    SerializedResultLimitExceeded,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserLibraryApiDiffTypeState>))]
public enum BrowserLibraryApiDiffTypeState
{
    Diff,
    Addition,
    Deletion,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserLibraryApiDiffMemberPairKind>))]
public enum BrowserLibraryApiDiffMemberPairKind
{
    Changed,
    Added,
    Removed,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserLibraryApiDiffMemberRelationRole>))]
public enum BrowserLibraryApiDiffMemberRelationRole
{
    Before,
    After,
    Both,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserLibraryApiDiffSurfaceScope>))]
public enum BrowserLibraryApiDiffSurfaceScope
{
    Public,
    IncludeAll,
    PublicWithNonPublicTypes,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserLibraryApiDiffEndpointIssueKind>))]
public enum BrowserLibraryApiDiffEndpointIssueKind
{
    Truncated,
    Rejected,
    Failed,
    InspectionFailures,
    DegradedSignatures,
    UnexpectedAssemblyPopulation,
}

[JsonConverter(
    typeof(JsonStringEnumConverter<
        BrowserLibraryApiDiffInspectionFailureMechanism>))]
public enum BrowserLibraryApiDiffInspectionFailureMechanism
{
    Metadata,
    Relationship,
    Signature,
    TypeSpecification,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserLibraryApiDiffProjectionLimit>))]
public enum BrowserLibraryApiDiffProjectionLimit
{
    Participants,
    Types,
    Members,
    InspectionFailures,
    TypeForwarders,
    MetadataRows,
    RetainedTextCharacters,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserLibraryApiDiffOpenFailureKind>))]
public enum BrowserLibraryApiDiffOpenFailureKind
{
    Unreadable,
    InvalidImage,
    ResourceBudget,
    UnsupportedMetadataFormat,
}

[JsonConverter(
    typeof(JsonStringEnumConverter<BrowserLibraryApiDiffMetadataRootMalformedReason>))]
public enum BrowserLibraryApiDiffMetadataRootMalformedReason
{
    UnmappableMetadataDirectory,
    TruncatedFixedPrefix,
    InvalidSignature,
    InvalidVersionLength,
    TruncatedVersionField,
    MissingVersionTerminator,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserLibraryApiDiffChangeKind>))]
public enum BrowserLibraryApiDiffChangeKind
{
    TypeAdded,
    TypeRemoved,
    TypeKindChanged,
    SealedAdded,
    SealedRemoved,
    AbstractAdded,
    AbstractRemoved,
    BaseTypeChanged,
    InterfaceAdded,
    InterfaceRemoved,
    TypeParameterCountChanged,
    TypeParameterVarianceChanged,
    TypeParameterConstraintTightened,
    TypeParameterConstraintLoosened,
    MemberAdded,
    MemberRemoved,
    MemberSignatureChanged,
    VirtualRemoved,
    AbstractMemberAdded,
    EnumValueChanged,
    TypeAttributeAdded,
    TypeAttributeRemoved,
    MemberAttributeAdded,
    MemberAttributeRemoved,
}

[JsonConverter(
    typeof(JsonStringEnumConverter<BrowserLibraryApiDiffChangeClassification>))]
public enum BrowserLibraryApiDiffChangeClassification
{
    Additive,
    Breaking,
    PotentiallyBreaking,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserLibraryApiDiffChangeCategory>))]
public enum BrowserLibraryApiDiffChangeCategory
{
    Signature,
    Attribute,
}
