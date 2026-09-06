using System.Collections.Immutable;

namespace DotnetInspector.Queries;

/// <summary>Erasing freshness currency for one exact physical Root issuance.</summary>
public sealed class ArtifactRootGenerationReference
{
    internal ArtifactRootGenerationReference() { }
}

/// <summary>Erasing identity for one reserved or current composition epoch.</summary>
public sealed class ArtifactRootCompositionGenerationIdentity
{
    internal ArtifactRootCompositionGenerationIdentity() { }
}

public abstract record ArtifactRootRealizationStatus
{
    private protected ArtifactRootRealizationStatus() { }

    public sealed record Ready(ArtifactRootGenerationReference Generation)
        : ArtifactRootRealizationStatus;
    public sealed record Pending : ArtifactRootRealizationStatus;
    public sealed record Failed(ArtifactRootFailure Failure)
        : ArtifactRootRealizationStatus;
}

/// <summary>A resource-free point-in-time projection, never an access grant.</summary>
public sealed record ArtifactRootScopeProjection(
    ArtifactRootCorrespondence Correspondence,
    ArtifactRootRealizationStatus Status);

public enum ArtifactRootFailure
{
    Malformed,
    ForeignWorkspace,
    Absent,
    WorkspaceClosing,
    WorkspaceClosed,
    Cancelled,
    DeadlineExpired,
    CompositionMismatch,
    ArtifactGenerationMismatch,
    BindingPolicyMismatch,
    BudgetExceeded,
    PreparationFailed,
    PreparationAlreadyPublished,
    PreparationReleased,
    PreparationPublishing,
    ParticipantAlreadyConsumed,
    ParticipantRefused,
    ScopeBaseMismatch,
    Superseded,
}

public abstract record ArtifactRootResult<T>
{
    private protected ArtifactRootResult() { }

    public sealed record Available(T Value) : ArtifactRootResult<T>;
    public sealed record Rejected(ArtifactRootFailure Failure)
        : ArtifactRootResult<T>;
}

internal sealed class ArtifactRootPreparationIdentity;
internal sealed class ArtifactRootPreparationEntryIdentity;
internal sealed class ArtifactRootCandidateSetIdentity;
internal sealed class ArtifactRootCancellationIdentity;

internal sealed class ArtifactRootPreparationAuthority(
    InspectionWorkspaceIdentity workspace,
    WorkspaceScopePublicationCandidateIdentity candidateSet,
    DateTimeOffset deadline,
    CancellationToken cancellation)
{
    internal InspectionWorkspaceIdentity Workspace { get; } = workspace;
    internal WorkspaceScopePublicationCandidateIdentity CandidateSet { get; } = candidateSet;
    internal DateTimeOffset Deadline { get; } = deadline;
    internal CancellationToken Cancellation { get; } = cancellation;
    internal ArtifactRootCancellationIdentity CancellationIdentity { get; } = new();
}

internal enum ArtifactRootPreparationState
{
    Prepared,
    Publishing,
    Published,
    Released,
}

internal sealed record ArtifactRootPreparedEntry(
    ArtifactRootPreparationEntryIdentity Entry,
    ArtifactRootCorrespondence Correspondence);

// Physical ownership lives in the Workspace registry, not in historical receipts.
internal sealed class ArtifactRootPreparationReceipt(
    InspectionWorkspaceIdentity workspace,
    ArtifactRootCandidateSetIdentity candidateSet,
    DateTimeOffset deadline,
    ArtifactRootCancellationIdentity cancellation,
    ImmutableArray<ArtifactRootPreparedEntry> entries)
{
    internal InspectionWorkspaceIdentity Workspace { get; } = workspace;
    internal ArtifactRootPreparationIdentity Preparation { get; } = new();
    internal ArtifactRootCandidateSetIdentity CandidateSet { get; } = candidateSet;
    internal DateTimeOffset Deadline { get; } = deadline;
    internal ArtifactRootCancellationIdentity Cancellation { get; } = cancellation;
    internal ImmutableArray<ArtifactRootPreparedEntry> Entries { get; } = entries;
    internal ArtifactRootPreparationState State { get; set; }
    internal TaskCompletionSource<ImmutableArray<ArtifactRootFailure>> Settlement { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal abstract record ArtifactRootPublicationEntry
{
    private protected ArtifactRootPublicationEntry() { }

    internal sealed record Retain(
        ArtifactRootCorrespondence Correspondence,
        ArtifactRootGenerationReference Generation)
        : ArtifactRootPublicationEntry;
    internal sealed record Adopt(
        ArtifactRootPreparationIdentity Preparation,
        ArtifactRootPreparationEntryIdentity Entry)
        : ArtifactRootPublicationEntry;
}

internal sealed record ArtifactRootPublicationPlan(
    ArtifactRootPreparationAuthority Authority,
    ArtifactRootCompositionGenerationIdentity ExpectedComposition,
    ImmutableArray<ArtifactRootPublicationEntry> DesiredRoots,
    ImmutableArray<ArtifactRootPreparationReceipt> Preparations,
    ArtifactRootScopePublicationParticipant Participant)
{
    internal InspectionWorkspaceIdentity Workspace => Authority.Workspace;
    internal DateTimeOffset Deadline => Authority.Deadline;
    internal CancellationToken Cancellation => Authority.Cancellation;
}

internal sealed record ArtifactRootPublishedComposition(
    ArtifactRootCompositionGenerationIdentity Composition,
    ImmutableArray<ArtifactRootScopeProjection> Roots,
    WorkspaceScopePublicationResult ScopeResult);

internal sealed record ArtifactRootPublicationOutcome(
    ArtifactRootPublishedComposition? Published,
    ArtifactRootFailure? Failure,
    ImmutableArray<ArtifactRootFailure> CleanupFailures);

internal sealed record ArtifactRootReplacementSettlement(
    ArtifactRootCompositionGenerationIdentity Composition,
    ArtifactRootScopeProjection Root);

internal enum ArtifactRootReleaseOutcome
{
    Released,
    NoEffect,
    PreparationPublishing,
    PreparationAlreadyPublished,
    ForeignWorkspace,
    UnknownPreparation,
}

/// <summary>
/// Finite reservation ceilings for this package Root producer. Reservations
/// cover provisional, current, and draining Roots together.
/// </summary>
internal sealed record ArtifactRootAdmissionLimits
{
    internal int MaxRoots { get; init; } = 128;
    internal long MaxRetainedImageBytes { get; init; } =
        AssemblyContextGroupOptions.DefaultMaxRetainedImageBytes;
}
