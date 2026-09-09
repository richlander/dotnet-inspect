using System.Text.Json.Serialization;

namespace InspectWeb.Engine.MetadataFacade;

/// <summary>
/// The Library API Diff browser wire contract (#6423). Every record here is declared and
/// source-generated inside <c>InspectWeb.Engine.MetadataExports</c>; a record structurally equal
/// to another facade's copy is still a separate module-local contract by design —
/// <c>ProductionFacadeWireContexts_AreAssemblyLocal</c> gates that ownership. The orientation is
/// stable: <c>Before</c> is the comparison target, <c>After</c> is the currently inspected
/// Library.
/// </summary>
public sealed record BrowserLibraryApiDiffRequest(
    string PackageId,
    string Version,
    string Framework,
    string ComparisonVersion,
    string Assembly);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserLibraryApiDiffResultKind>))]
public enum BrowserLibraryApiDiffResultKind
{
    Succeeded,
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

public sealed record BrowserLibraryApiDiffCancellation(
    BrowserLibraryApiDiffCancellationKind Kind,
    string? Reason)
{
    internal static BrowserLibraryApiDiffCancellation From(
        BrowserManagedCancellationRequestResult result) =>
        result switch
        {
            BrowserManagedCancellationRequestResult.Requested requested =>
                new(BrowserLibraryApiDiffCancellationKind.Requested, FormatReason(requested.Reason)),
            BrowserManagedCancellationRequestResult.AlreadyRequested requested =>
                new(
                    BrowserLibraryApiDiffCancellationKind.AlreadyRequested,
                    FormatReason(requested.Reason)),
            BrowserManagedCancellationRequestResult.NotActive =>
                new(BrowserLibraryApiDiffCancellationKind.NotActive, null),
            _ => throw new ArgumentOutOfRangeException(nameof(result)),
        };

    internal static string FormatReason(BrowserManagedOperationCancelReason reason) =>
        BrowserManagedOperationCancelReasons.Format(reason);

    internal static BrowserManagedOperationCancelReason ParseReason(string reason) =>
        BrowserManagedOperationCancelReasons.Parse(reason);
}

/// <summary>The closed managed-operation envelope. <c>Value</c> is set only when Kind is Succeeded.</summary>
public sealed record BrowserLibraryApiDiffResult(
    int Version,
    BrowserLibraryApiDiffResultKind Kind,
    BrowserLibraryApiDiff? Value,
    BrowserLibraryApiDiffFailureKind? FailureKind,
    string? Error,
    string? Diagnostic,
    string? Reason);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserLibraryApiDiffPresentationKind>))]
public enum BrowserLibraryApiDiffPresentationKind
{
    Available,
    Unavailable,
    Rejected,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserLibraryApiDiffUnavailableKind>))]
public enum BrowserLibraryApiDiffUnavailableKind
{
    BeforeIncomplete,
    AfterIncomplete,
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
}

/// <summary>
/// The closed presentation outcome: exactly one of <c>Summary</c>/<c>Document</c>,
/// <c>UnavailableKind</c>, or <c>RejectionKind</c> is populated, selected by <c>Kind</c>.
/// </summary>
public sealed record BrowserLibraryApiDiff(
    BrowserLibraryApiDiffRequest Request,
    BrowserLibraryApiDiffPresentationKind Kind,
    BrowserLibraryApiDiffEndpointSummary Before,
    BrowserLibraryApiDiffEndpointSummary After,
    BrowserLibraryApiDiffSummary? Summary,
    BrowserLibraryApiDiffDocument? Document,
    BrowserLibraryApiDiffUnavailableKind? UnavailableKind,
    BrowserLibraryApiDiffRejectionKind? RejectionKind);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserLibraryApiSurfaceScope>))]
public enum BrowserLibraryApiSurfaceScope
{
    Public,
    IncludeAll,
    PublicWithNonPublicTypes,
}

public sealed record BrowserLibraryApiDiffAssemblyIdentity(
    string Name,
    string? Version,
    string? Culture,
    string? PublicKeyToken);

public sealed record BrowserLibraryApiDiffEndpointSummary(
    BrowserLibraryApiDiffAssemblyIdentity Identity,
    BrowserLibraryApiSurfaceScope Scope,
    bool IsComplete,
    BrowserLibraryApiDiffEndpointIssue[] Issues);

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

[JsonConverter(typeof(JsonStringEnumConverter<BrowserLibraryApiDiffOpenFailureKind>))]
public enum BrowserLibraryApiDiffOpenFailureKind
{
    Unreadable,
    InvalidImage,
    ResourceBudget,
    UnsupportedMetadataFormat,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserLibraryApiDiffMetadataRootReason>))]
public enum BrowserLibraryApiDiffMetadataRootReason
{
    UnmappableMetadataDirectory,
    TruncatedFixedPrefix,
    InvalidSignature,
    InvalidVersionLength,
    TruncatedVersionField,
    MissingVersionTerminator,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserLibraryApiDiffTruncationLimit>))]
public enum BrowserLibraryApiDiffTruncationLimit
{
    Participants,
    Types,
    Members,
    InspectionFailures,
    TypeForwarders,
    MetadataRows,
    RetainedTextCharacters,
}

public sealed record BrowserLibraryApiDiffTruncation(
    BrowserLibraryApiDiffTruncationLimit Limit,
    int Bound,
    int ProjectedParticipants,
    int OmittedParticipants,
    int ProjectedTypes,
    int ProjectedMembers,
    int ProjectedInspectionFailures,
    int ProjectedTypeForwarders,
    int InspectedMetadataRows,
    int ProjectedRetainedTextCharacters);

/// <summary>
/// One closed endpoint issue. Only the field matching <c>Kind</c> is populated:
/// <c>Truncated</c>&#8594;<c>Truncation</c>; <c>Rejected</c>&#8594;<c>OpenFailureKind</c>,
/// <c>Detail</c>, <c>MetadataRootReason</c>; <c>Failed</c>&#8594;<c>Detail</c>;
/// <c>InspectionFailures</c>/<c>DegradedSignatures</c>/<c>UnexpectedAssemblyPopulation</c>&#8594;<c>Count</c>.
/// </summary>
public sealed record BrowserLibraryApiDiffEndpointIssue(
    BrowserLibraryApiDiffEndpointIssueKind Kind,
    BrowserLibraryApiDiffTruncation? Truncation,
    BrowserLibraryApiDiffOpenFailureKind? OpenFailureKind,
    string? Detail,
    BrowserLibraryApiDiffMetadataRootReason? MetadataRootReason,
    int? Count);

public sealed record BrowserLibraryApiDiffSummary(
    int ChangedTypeCount,
    int AddedTypeCount,
    int RemovedTypeCount,
    int ChangedMemberCount,
    int BreakingCount,
    int AdditiveCount,
    int PotentiallyBreakingCount);

public sealed record BrowserLibraryApiTypeIdentity(string Identifier, string Display);

public sealed record BrowserLibraryApiMemberAnchor(
    string StableSelector,
    string CanonicalSignature,
    string Fingerprint,
    string TypeFullName,
    string MemberName);

public sealed record BrowserLibraryApiMemberIdentity(
    BrowserLibraryApiTypeIdentity DeclaringType,
    BrowserLibraryApiMemberAnchor Anchor,
    string Display);

public sealed record BrowserLibraryApiMatchProvenance(
    string TierId,
    int TierConfidence,
    int Confidence);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserLibraryApiMemberPairKind>))]
public enum BrowserLibraryApiMemberPairKind
{
    Changed,
    Added,
    Removed,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserLibraryApiMemberRelationRole>))]
public enum BrowserLibraryApiMemberRelationRole
{
    Before,
    After,
    Both,
}

public sealed record BrowserLibraryApiMemberRelation(
    string Identifier,
    BrowserLibraryApiMemberPairKind PairKind,
    BrowserLibraryApiMemberIdentity? Before,
    BrowserLibraryApiMemberIdentity? After,
    BrowserLibraryApiMatchProvenance? Match);

public sealed record BrowserLibraryApiMemberDiff(
    BrowserLibraryApiMemberRelation Relation,
    BrowserLibraryApiMemberRelationRole Role);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserLibraryApiChangeSubjectKind>))]
public enum BrowserLibraryApiChangeSubjectKind
{
    Type,
    Member,
}

public sealed record BrowserLibraryApiChangeSubject(
    BrowserLibraryApiChangeSubjectKind Kind,
    BrowserLibraryApiTypeIdentity? BeforeType,
    BrowserLibraryApiTypeIdentity? AfterType,
    BrowserLibraryApiMemberIdentity? BeforeMember,
    BrowserLibraryApiMemberIdentity? AfterMember);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserLibraryApiChangeKind>))]
public enum BrowserLibraryApiChangeKind
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

[JsonConverter(typeof(JsonStringEnumConverter<BrowserLibraryApiChangeClassification>))]
public enum BrowserLibraryApiChangeClassification
{
    Additive,
    Breaking,
    PotentiallyBreaking,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserLibraryApiChangeCategory>))]
public enum BrowserLibraryApiChangeCategory
{
    Signature,
    Attribute,
}

public sealed record BrowserLibraryApiCompatibilityChange(
    BrowserLibraryApiChangeKind Kind,
    BrowserLibraryApiChangeClassification Classification,
    BrowserLibraryApiChangeCategory Category,
    string Message,
    string? OldValue,
    string? NewValue,
    BrowserLibraryApiChangeSubject Subject);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserLibraryApiTypePairKind>))]
public enum BrowserLibraryApiTypePairKind
{
    Present,
    Changed,
    Added,
    Removed,
}

public sealed record BrowserLibraryApiTypeDiff(
    BrowserLibraryApiTypeIdentity? Before,
    BrowserLibraryApiTypeIdentity? After,
    BrowserLibraryApiTypePairKind PairKind,
    bool? TypeDefinitionChanged,
    BrowserLibraryApiCompatibilityChange[] CompatibilityChanges,
    BrowserLibraryApiMemberDiff[] Members,
    int BreakingCount,
    int AdditiveCount,
    int PotentiallyBreakingCount,
    int ChangedMemberCount);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserLibraryApiSubjectChangeKind>))]
public enum BrowserLibraryApiSubjectChangeKind
{
    Diff,
    Addition,
    Deletion,
}

/// <summary>One ordered changed-Type row: the composition-level change plus its Type payload.</summary>
public sealed record BrowserLibraryApiTypeSubject(
    string Identifier,
    string Display,
    BrowserLibraryApiSubjectChangeKind Change,
    BrowserLibraryApiTypeDiff TypeDiff);

/// <summary>The Library-root comparison document: its identity plus every ordered changed-Type row.</summary>
public sealed record BrowserLibraryApiDiffDocument(
    string Identifier,
    string Display,
    BrowserLibraryApiTypeSubject[] Subjects);
