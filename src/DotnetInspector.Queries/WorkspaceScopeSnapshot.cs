using System.Collections.Immutable;

using DotnetInspector.Packages;

namespace DotnetInspector.Queries;

public sealed class WorkspaceScopeRevisionIdentity
{
    internal WorkspaceScopeRevisionIdentity() { }
}

public sealed class WorkspaceRootOccurrenceIdentity : InspectionWorkspaceOccurrenceIdentity
{
    internal WorkspaceRootOccurrenceIdentity(InspectionWorkspaceIdentity workspace)
        : base(workspace) { }
}

public sealed class WorkspaceClosureObservationIdentity
{
    internal WorkspaceClosureObservationIdentity() { }
}

public enum WorkspaceRootKind
{
    Package,
    NonPackage,
}

/// <summary>
/// Resource-free owner facts. Only the Package adapter issues descriptors in
/// this slice; non-package coordinates are not approximated as packages.
/// </summary>
public abstract class WorkspaceRootDescriptor
{
    private protected WorkspaceRootDescriptor(WorkspaceRootKind kind) => Kind = kind;

    public WorkspaceRootKind Kind { get; }

    public sealed class Package : WorkspaceRootDescriptor
    {
        internal Package(PackageRootBinding binding) : base(WorkspaceRootKind.Package)
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
}

public sealed class WorkspaceRootOccurrence
{
    internal WorkspaceRootOccurrence(
        InspectionWorkspaceIdentity workspace,
        WorkspaceRootDescriptor root,
        ArtifactRootCorrespondence correspondence)
    {
        Identity = new(workspace);
        Root = root;
        Correspondence = correspondence;
    }

    public WorkspaceRootOccurrenceIdentity Identity { get; }
    public WorkspaceRootDescriptor Root { get; }
    public ArtifactRootCorrespondence Correspondence { get; }
}

public sealed class WorkspaceRootOccurrenceDescriptor
{
    internal WorkspaceRootOccurrenceDescriptor(
        WorkspaceRootOccurrence occurrence,
        ArtifactRootScopeProjection realization)
    {
        Occurrence = occurrence;
        Realization = realization;
    }

    public WorkspaceRootOccurrence Occurrence { get; }
    public ArtifactRootScopeProjection Realization { get; }
}

/// <summary>The fixed closed-Scope profile for initial Replace/Clear adoption.</summary>
public sealed class WorkspaceScopeLimits
{
    public const int DefaultMaxRoots = 64;
    internal static WorkspaceScopeLimits Closed { get; } = new();
    private WorkspaceScopeLimits() { }
    public int MaxRoots => DefaultMaxRoots;
}

public sealed class WorkspaceScopeRevision
{
    internal WorkspaceScopeRevision(
        InspectionWorkspaceIdentity workspace,
        ImmutableArray<WorkspaceRootOccurrence> roots)
    {
        Workspace = workspace;
        Identity = new();
        Roots = roots;
    }

    public InspectionWorkspaceIdentity Workspace { get; }
    public WorkspaceScopeRevisionIdentity Identity { get; }
    public ImmutableArray<WorkspaceRootOccurrence> Roots { get; }
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
        int requestedRootCount,
        DateTimeOffset deadline)
    {
        Operation = operation;
        Kind = kind;
        RequestedRootCount = requestedRootCount;
        Deadline = deadline;
        Cancellation = new(workspace, operation);
    }

    public WorkspaceScopePublicationOperationIdentity Operation { get; }
    public WorkspaceScopeOperationKind Kind { get; }
    public int RequestedRootCount { get; }
    public DateTimeOffset Deadline { get; }
    public WorkspaceScopeCancellationAction Cancellation { get; }
}

/// <summary>Complete immutable logical state joined to one observed physical epoch.</summary>
public sealed class WorkspaceScopeSnapshot
{
    internal WorkspaceScopeSnapshot(
        WorkspaceScopeRevision revision,
        ArtifactRootCompositionGenerationIdentity physicalComposition,
        ImmutableArray<WorkspaceRootOccurrenceDescriptor> roots,
        WorkspaceClosureObservation closure,
        WorkspaceScopePreparationDescriptor? preparing)
    {
        Revision = revision;
        PublicationBase = new();
        PhysicalComposition = physicalComposition;
        Roots = roots;
        Closure = closure;
        Preparing = preparing;
    }

    public WorkspaceScopeRevision Revision { get; }
    public WorkspaceScopePublicationBaseIdentity PublicationBase { get; }
    public ArtifactRootCompositionGenerationIdentity PhysicalComposition { get; }
    public ImmutableArray<WorkspaceRootOccurrenceDescriptor> Roots { get; }
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
    RootCapacityExceeded,
    AsynchronousWorkspaceRequired,
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
