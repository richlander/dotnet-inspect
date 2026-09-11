using System.Collections.Immutable;

using DotnetInspector.Packages;

namespace DotnetInspector.Queries;

public sealed class WorkspaceScopeRevisionIdentity
{
    internal WorkspaceScopeRevisionIdentity() { }
}

public sealed class WorkspacePackageOccurrenceIdentity : InspectionWorkspaceOccurrenceIdentity
{
    internal WorkspacePackageOccurrenceIdentity(InspectionWorkspaceIdentity workspace)
        : base(workspace) { }
}

public sealed class WorkspaceClosureObservationIdentity
{
    internal WorkspaceClosureObservationIdentity() { }
}

/// <summary>
/// Resource-free Package facts retained by logical Workspace Scope.
/// </summary>
public sealed class WorkspacePackageDescriptor
{
    internal WorkspacePackageDescriptor(PackageRootBinding binding)
    {
        Coordinate = binding.Coordinate;
        PackageId = binding.Root.PackageId;
        PackageVersion = binding.Root.PackageVersion;
        RequestedTargetFramework = binding.Root.RequestedTargetFramework;
        SelectedTargetFramework = binding.Root.AssetSelection.TargetFramework;
        TargetFramework = SelectedTargetFramework ?? RequestedTargetFramework ?? Coordinate.Framework;
        RuntimeIdentifier = binding.Root.RequestedRuntimeIdentifier;
        SelectionStatus = binding.Root.AssetSelection.Status;
    }

    public RealizedMemberCoordinate.Package Coordinate { get; }
    public string PackageId { get; }
    public string PackageVersion { get; }
    public string? TargetFramework { get; }
    public string? RequestedTargetFramework { get; }
    public string? SelectedTargetFramework { get; }
    public string? RuntimeIdentifier { get; }
    public PackageCompileAssetSelectionStatus SelectionStatus { get; }
}

public sealed class WorkspacePackageOccurrence
{
    internal WorkspacePackageOccurrence(
        InspectionWorkspaceIdentity workspace,
        WorkspacePackageDescriptor package,
        ArtifactRootCorrespondence correspondence)
    {
        Identity = new(workspace);
        Package = package;
        Correspondence = correspondence;
    }

    public WorkspacePackageOccurrenceIdentity Identity { get; }
    public WorkspacePackageDescriptor Package { get; }
    public ArtifactRootCorrespondence Correspondence { get; }
}

public sealed class WorkspacePackageOccurrenceDescriptor
{
    internal WorkspacePackageOccurrenceDescriptor(
        WorkspacePackageOccurrence occurrence,
        ArtifactRootScopeProjection realization)
    {
        Occurrence = occurrence;
        Realization = realization;
    }

    public WorkspacePackageOccurrence Occurrence { get; }
    public ArtifactRootScopeProjection Realization { get; }
}

/// <summary>The fixed closed-Scope profile for exact package membership operations.</summary>
public sealed class WorkspaceScopeLimits
{
    public const int DefaultMaxPackages = 64;
    internal static WorkspaceScopeLimits Closed { get; } = new();
    private WorkspaceScopeLimits() { }
    public int MaxPackages => DefaultMaxPackages;
}

public sealed class WorkspaceScopeRevision
{
    internal WorkspaceScopeRevision(
        InspectionWorkspaceIdentity workspace,
        ImmutableArray<WorkspacePackageOccurrence> packages)
    {
        Workspace = workspace;
        Identity = new();
        Packages = packages;
    }

    public InspectionWorkspaceIdentity Workspace { get; }
    public WorkspaceScopeRevisionIdentity Identity { get; }
    public ImmutableArray<WorkspacePackageOccurrence> Packages { get; }
    public WorkspaceScopeLimits Limits => WorkspaceScopeLimits.Closed;
}

public enum WorkspaceClosureState
{
    ClosedBoundary,
}

/// <summary>Closed expansion eligibility, not a claim of no dependencies.</summary>
public sealed class WorkspaceClosureObservation
{
    internal WorkspaceClosureObservation(WorkspaceScopeRevisionIdentity revision)
    {
        Identity = new();
        Revision = revision;
    }

    public WorkspaceClosureObservationIdentity Identity { get; }
    public WorkspaceScopeRevisionIdentity Revision { get; }
    public WorkspaceClosureState State => WorkspaceClosureState.ClosedBoundary;
}

public enum WorkspaceScopeOperationKind
{
    Replace,
    Clear,
    Add,
    Remove,
}

/// <summary>An exact resource-free cancellation request, interpreted only by its Workspace.</summary>
public sealed class WorkspaceScopeCancellationAction
{
    internal WorkspaceScopeCancellationAction(
        InspectionWorkspaceIdentity workspace,
        WorkspaceScopePublicationOperationIdentity operation)
    {
        Workspace = workspace;
        Operation = operation;
    }

    public InspectionWorkspaceIdentity Workspace { get; }
    public WorkspaceScopePublicationOperationIdentity Operation { get; }
}

public sealed class WorkspaceScopePreparationDescriptor
{
    internal WorkspaceScopePreparationDescriptor(
        InspectionWorkspaceIdentity workspace,
        WorkspaceScopePublicationOperationIdentity operation,
        WorkspaceScopeOperationKind kind,
        int requestedPackageCount,
        DateTimeOffset deadline)
    {
        Operation = operation;
        Kind = kind;
        RequestedPackageCount = requestedPackageCount;
        Deadline = deadline;
        Cancellation = new(workspace, operation);
    }

    public WorkspaceScopePublicationOperationIdentity Operation { get; }
    public WorkspaceScopeOperationKind Kind { get; }
    public int RequestedPackageCount { get; }
    public DateTimeOffset Deadline { get; }
    public WorkspaceScopeCancellationAction Cancellation { get; }
}

/// <summary>Complete immutable logical state joined to one observed physical epoch.</summary>
public sealed class WorkspaceScopeSnapshot
{
    internal WorkspaceScopeSnapshot(
        WorkspaceScopeRevision revision,
        ArtifactRootCompositionGenerationIdentity physicalComposition,
        ImmutableArray<WorkspacePackageOccurrenceDescriptor> packages,
        WorkspaceClosureObservation closure,
        WorkspaceScopePreparationDescriptor? preparing)
    {
        Revision = revision;
        PublicationBase = new();
        PhysicalComposition = physicalComposition;
        Packages = packages;
        Closure = closure;
        Preparing = preparing;
    }

    public WorkspaceScopeRevision Revision { get; }
    public WorkspaceScopePublicationBaseIdentity PublicationBase { get; }
    public ArtifactRootCompositionGenerationIdentity PhysicalComposition { get; }
    public ImmutableArray<WorkspacePackageOccurrenceDescriptor> Packages { get; }
    public WorkspaceClosureObservation Closure { get; }
    public WorkspaceScopePreparationDescriptor? Preparing { get; }
}

public abstract record WorkspaceScopeReadResult
{
    private protected WorkspaceScopeReadResult() { }

    public sealed record Available(WorkspaceScopeSnapshot Snapshot) : WorkspaceScopeReadResult;
    public sealed record Unavailable(
        WorkspaceScopeSnapshot? LastSnapshot,
        ArtifactRootFailure RuntimeFailure) : WorkspaceScopeReadResult;
}

public enum WorkspaceScopeRejection
{
    Malformed,
    DeadlineExpired,
    ForeignWorkspace,
    RevisionMismatch,
    PackageCapacityExceeded,
    AsynchronousWorkspaceRequired,
    Busy,
    OccurrenceNotCurrent,
}

public abstract record WorkspaceScopeOperationResult
{
    private protected WorkspaceScopeOperationResult() { }

    public sealed record Committed(
        WorkspaceScopeSnapshot Snapshot,
        WorkspaceScopeOperationKind Effect,
        WorkspaceScopePublicationOperationIdentity Operation) : WorkspaceScopeOperationResult;
    public sealed record NoEffect(WorkspaceScopeSnapshot Snapshot) : WorkspaceScopeOperationResult;
    public sealed record Rejected(
        WorkspaceScopeSnapshot Snapshot,
        WorkspaceScopeRejection Reason) : WorkspaceScopeOperationResult;
    public sealed record Failed(
        WorkspaceScopeSnapshot Snapshot,
        ArtifactRootFailure Failure) : WorkspaceScopeOperationResult;
    public sealed record Cancelled(
        WorkspaceScopeSnapshot Snapshot,
        WorkspaceScopePublicationOperationIdentity Operation) : WorkspaceScopeOperationResult;
    public sealed record Superseded(
        WorkspaceScopeSnapshot Snapshot,
        WorkspaceScopePublicationOperationIdentity SupersedingOperation) : WorkspaceScopeOperationResult;
    public sealed record Unavailable(
        WorkspaceScopeSnapshot? LastSnapshot,
        ArtifactRootFailure RuntimeFailure) : WorkspaceScopeOperationResult;
}

/// <summary>
/// The physical owner no longer has an exact projection for committed Scope
/// membership. LastSnapshot is historical evidence, not current authority.
/// </summary>
public sealed class WorkspaceScopeInvariantException : InvalidOperationException
{
    internal WorkspaceScopeInvariantException(
        WorkspaceScopeSnapshot? lastSnapshot,
        ArtifactRootFailure failure)
        : base("The Artifact composition does not correspond to the committed Workspace Scope.")
    {
        LastSnapshot = lastSnapshot;
        Failure = failure;
    }

    public WorkspaceScopeSnapshot? LastSnapshot { get; }
    public ArtifactRootFailure Failure { get; }
}
