using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace DotnetInspector.Queries;

public enum NavigationOperationKind
{
    Initialize,
    Scope,
    Subject,
    Package,
    RetainedType,
    Lens,
    DescendantLens,
    Maintenance,
    Synchronize,
}

public enum NavigationOutcomeKind
{
    Applied,
    Unavailable,
    Ambiguous,
    Rejected,
    Failed,
    Superseded,
    Aborted,
    Synchronized,
}

public enum NavigationSynchronizationDisposition
{
    Current,
    SynchronizationRequired,
}

public enum NavigationRejectionKind
{
    ForeignSession,
    StaleGeneration,
    UnknownAction,
    DuplicateAction,
    SourceMismatch,
    InvalidAction,
    ForeignWorkspace,
    ForeignOccurrence,
    ForeignLibrary,
    NonDescendant,
    Registry,
    ScopeOperation,
}

public enum NavigationFailureSource
{
    Registry,
    Policy,
    Preparation,
    Prerequisite,
}

public enum NavigationLensBasisKind
{
    Recommendation,
    ExactRequest,
}

public enum NavigationResolutionKind
{
    Available,
    Unavailable,
    Failed,
    Inapplicable,
    Unknown,
}

public enum NavigationDiagnosticKind
{
    ParticipantRejected,
    ParticipantFailed,
    InspectionFailed,
    TypeIdentityMissing,
    ProjectedMemberIdentityFailure,
    ProjectionOmitted,
}

public enum NavigationRealizationKind
{
    Ready,
    Pending,
    Failed,
}

public enum NavigationScopeSnapshotKind
{
    Current,
    Historical,
}

public enum NavigationScopeSettlementKind
{
    Committed,
    NoEffect,
    Rejected,
    Failed,
    Cancelled,
    Superseded,
    Unavailable,
}

/// <summary>
/// Whether the projected membership is current or retained only as historical
/// evidence after Scope runtime unavailability.
/// </summary>
public sealed record NavigationConsumerScopeStatus(
    NavigationScopeSnapshotKind Kind,
    ArtifactRootFailure? RuntimeFailure = null);

/// <summary>Typed Scope settlement details retained by a Navigation result.</summary>
public sealed record NavigationConsumerScopeOutcome(
    NavigationScopeSettlementKind Kind,
    WorkspaceScopeOperationKind Operation,
    WorkspaceScopeRejection? Rejection = null,
    ArtifactRootFailure? Failure = null)
{
    public static NavigationConsumerScopeOutcome FromSettlement(
        WorkspaceScopeOperationResult settlement) =>
        settlement switch
        {
            WorkspaceScopeOperationResult.Committed committed =>
                new(NavigationScopeSettlementKind.Committed, committed.Association.Kind),
            WorkspaceScopeOperationResult.NoEffect noEffect =>
                new(NavigationScopeSettlementKind.NoEffect, noEffect.Association.Kind),
            WorkspaceScopeOperationResult.Rejected rejected =>
                new(NavigationScopeSettlementKind.Rejected, rejected.Association.Kind,
                    Rejection: rejected.Reason),
            WorkspaceScopeOperationResult.Failed failed =>
                new(NavigationScopeSettlementKind.Failed, failed.Association.Kind,
                    Failure: failed.Failure),
            WorkspaceScopeOperationResult.Cancelled cancelled =>
                new(NavigationScopeSettlementKind.Cancelled, cancelled.Association.Kind),
            WorkspaceScopeOperationResult.Superseded superseded =>
                new(NavigationScopeSettlementKind.Superseded, superseded.Association.Kind),
            WorkspaceScopeOperationResult.Unavailable unavailable =>
                new(NavigationScopeSettlementKind.Unavailable, unavailable.Association.Kind,
                    Failure: unavailable.RuntimeFailure),
            _ => throw new InvalidOperationException("Unknown Scope settlement."),
        };
}

/// <summary>Navigation's coordinate decision; native evidence remains on the operation result.</summary>
public sealed record NavigationConsumerCoordinateOutcome(
    NavigationCoordinateRetentionDisposition Disposition,
    string Detail,
    CoordinateLibraryPairingStatus? LibraryPairing,
    ApiCoordinateCorrespondenceStatus? TypeCorrespondence,
    ApiCoordinateCorrespondenceStatus? MemberCorrespondence);

/// <summary>Transport currency only. None of these strings is a portable subject identity.</summary>
public sealed record NavigationAction(
    string Session,
    string Generation,
    string Id,
    string Source,
    NavigationOperationKind Kind);

/// <summary>Exact, single-consumption authority; consumers must validate it through its session.</summary>
public sealed record NavigationEffectAuthority(
    string Session,
    string Revision,
    string Intent,
    string Epoch);

public sealed record NavigationConsumerSubject(
    string Id,
    StructuralSubjectKind Kind,
    string Label,
    string? Summary,
    string? Parent);

public sealed record NavigationConsumerSubjectDescriptor(
    StructuralSubjectKind Kind,
    string Label,
    NavigationConsumerSubject? Subject,
    NavigationDescriptorState State,
    bool IsActive,
    bool IsRetained,
    ImmutableArray<NavigationConsumerDiagnostic> Evidence,
    NavigationAction? Action);

public sealed record NavigationConsumerPackageDescriptor(
    int Order,
    NavigationConsumerSubject Subject,
    string PackageId,
    string Version,
    string? Framework,
    string? RuntimeIdentifier,
    NavigationRealizationKind Realization,
    ArtifactRootFailure? RealizationFailure,
    NavigationDescriptorState State,
    bool IsCurrent,
    NavigationAction? Action);

public sealed record NavigationConsumerLibraryDescriptor(
    NavigationConsumerSubjectDescriptor Navigation,
    string? AssetId,
    bool IsAggregate,
    bool IsPrimary);

public sealed record NavigationConsumerTypeDescriptor(
    NavigationConsumerSubjectDescriptor Navigation,
    string Library,
    string? Accessibility,
    string TypeKind,
    ImmutableArray<NavigationConsumerLensDescriptor> DescendantLenses);

public sealed record NavigationConsumerMemberDescriptor(
    NavigationConsumerSubjectDescriptor Navigation,
    string Library,
    string ContainingType,
    string DeclaringType,
    string? Accessibility,
    string MemberKind,
    string? Signature,
    ImmutableArray<NavigationConsumerLensDescriptor> DescendantLenses);

public sealed record NavigationConsumerFacet(
    string Id,
    StructuralSubjectKind Kind,
    string Title,
    string Summary,
    int Order,
    ViewFacetRole? Role);

public sealed record NavigationConsumerLens(
    string Id,
    NavigationConsumerSubject Subject,
    string Facet);

public sealed record NavigationConsumerResolution(
    NavigationResolutionKind Kind,
    NavigationConsumerFacet? Descriptor,
    ViewFacetUnavailabilityKind? Unavailability,
    string? Message);

public sealed record NavigationConsumerLensDescriptor(
    NavigationConsumerFacet Facet,
    NavigationDescriptorState State,
    bool IsCurrent,
    NavigationConsumerLens? Target,
    ViewFacetUnavailabilityKind? Unavailability,
    string? Message,
    NavigationAction? Action);

public sealed record NavigationConsumerLensOutcome(
    NavigationOutcomeKind Kind,
    NavigationLensBasisKind Basis,
    NavigationConsumerSubject Subject,
    NavigationConsumerLens? EffectiveLens,
    NavigationConsumerLens? Request,
    ViewFacetRole? PreferredRole,
    NavigationLensPolicyFailureKind? PolicyFailure,
    NavigationConsumerResolution? Resolution)
{
    public NavigationConsumerRealization? Suspension { get; init; }
}

public sealed record NavigationConsumerRealization(
    NavigationRealizationKind Kind,
    ArtifactRootFailure? Failure);

public sealed record NavigationConsumerDiagnostic(
    NavigationDiagnosticKind Kind,
    string Library,
    string Message);

/// <summary>
/// Complete Navigation-owned, resource-free projection. Collections retain producer order.
/// IDs are session-local lookup currencies, not canonical/restoration payloads.
/// </summary>
public sealed record NavigationConsumerSnapshot(
    string Generation,
    NavigationConsumerScopeStatus Scope,
    NavigationConsumerSubject Workspace,
    string? ActivePackage,
    NavigationConsumerSubject ActiveSubject,
    NavigationConsumerSubject? TypeInventoryLibraryContext,
    ImmutableArray<NavigationConsumerPackageDescriptor> Packages,
    ImmutableArray<NavigationConsumerSubjectDescriptor> Hierarchy,
    ImmutableArray<NavigationConsumerLibraryDescriptor> Libraries,
    ImmutableArray<NavigationConsumerTypeDescriptor> Types,
    ImmutableArray<NavigationConsumerMemberDescriptor> Members,
    ImmutableArray<NavigationConsumerLensDescriptor> Lenses,
    NavigationConsumerLensOutcome LensOutcome,
    ImmutableArray<NavigationConsumerDiagnostic> Diagnostics);

public sealed record NavigationConsumerRequest(
    NavigationConsumerSubject Source,
    NavigationConsumerSubject Destination,
    NavigationConsumerLens? Lens);

public sealed record NavigationConsumerOutcome(
    NavigationOutcomeKind Kind,
    NavigationRejectionKind? Rejection = null,
    NavigationFailureSource? FailureSource = null,
    string? Message = null,
    NavigationConsumerRequest? Request = null,
    NavigationConsumerResolution? Resolution = null,
    NavigationConsumerScopeOutcome? Scope = null)
{
    public ImmutableArray<NavigationConsumerDiagnostic> Diagnostics { get; init; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NavigationConsumerCoordinateOutcome? CoordinateRetention { get; init; }
}

/// <summary>
/// Every current result carries fresh authority and the complete installed projection.
/// Stale ordinary Navigation completions have no authority. A correlated Scope
/// Superseded settlement is current membership evidence and carries authority
/// for that complete projection.
/// </summary>
public sealed record NavigationConsumerResult(
    NavigationOperationKind Operation,
    string Request,
    NavigationConsumerSnapshot Snapshot,
    NavigationConsumerOutcome Outcome,
    NavigationSynchronizationDisposition Synchronization,
    NavigationEffectAuthority? Authority);

/// <summary>Source-generated transport for NativeAOT and Browser/Wasm consumers.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(NavigationConsumerResult))]
[JsonSerializable(typeof(NavigationConsumerSnapshot))]
[JsonSerializable(typeof(NavigationAction))]
[JsonSerializable(typeof(NavigationEffectAuthority))]
[JsonSerializable(typeof(NavigationAuthorityResult))]
public partial class NavigationConsumerJsonContext : JsonSerializerContext;
