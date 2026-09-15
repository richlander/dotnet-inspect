using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace DotnetInspector.Queries;

public enum NavigationOperationKind
{
    Initialize,
    Subject,
    Package,
    Lens,
    DescendantLens,
    Maintenance,
    Synchronize,
}

public enum NavigationOutcomeKind
{
    Applied,
    Unavailable,
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
    NonDescendant,
    Registry,
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
    NavigationConsumerResolution? Resolution = null)
{
    public ImmutableArray<NavigationConsumerDiagnostic> Diagnostics { get; init; } = [];
}

/// <summary>
/// Every current result carries fresh authority and the complete installed projection.
/// Superseded completions have no authority and authorize no consumer effect.
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
