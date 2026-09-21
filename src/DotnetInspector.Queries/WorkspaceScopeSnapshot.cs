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
        RequestedTargetFramework = binding.CompileTargetFramework;
        SelectedTargetFramework = binding.Root.AssetSelection.TargetFramework;
        TargetFramework = SelectedTargetFramework ?? RequestedTargetFramework ?? Coordinate.Framework;
        RuntimeIdentifier = binding.Root.RequestedRuntimeIdentifier;
        SelectionStatus = binding.Root.AssetSelection.Status;
        ContentGeneration = binding.ContentGenerationIdentity;
        Selection = binding.SelectionIdentity;
    }

    public RealizedMemberCoordinate.Package Coordinate { get; }
    public string PackageId { get; }
    public string PackageVersion { get; }
    public string? TargetFramework { get; }
    public string? RequestedTargetFramework { get; }
    public string? SelectedTargetFramework { get; }
    public string? RuntimeIdentifier { get; }
    public PackageCompileAssetSelectionStatus SelectionStatus { get; }
    internal PackageContentGenerationIdentity ContentGeneration { get; }
    internal PackageRootSelectionIdentity Selection { get; }

    internal bool Matches(PackageRootBinding binding) =>
        ReferenceEquals(ContentGeneration, binding.ContentGenerationIdentity)
        && ReferenceEquals(Selection, binding.SelectionIdentity);
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

/// <summary>
/// Resource-free evidence identifying one Scope mutation before submission.
/// </summary>
public sealed class WorkspaceScopeOperationAssociation
{
    internal WorkspaceScopeOperationAssociation(
        InspectionWorkspaceIdentity workspace,
        WorkspaceScopePublicationOperationIdentity operation,
        WorkspaceScopeOperationKind kind,
        WorkspaceScopeRevision? expectedRevision,
        bool hasExplicitTarget)
    {
        Workspace = workspace;
        Operation = operation;
        Kind = kind;
        ExpectedRevisionWorkspace = expectedRevision?.Workspace;
        ExpectedRevision = expectedRevision?.Identity;
        HasExplicitTarget = hasExplicitTarget;
    }

    /// <summary>The Workspace that issued the request.</summary>
    public InspectionWorkspaceIdentity Workspace { get; }

    /// <summary>The exact identity shared by the request and every settlement arm.</summary>
    public WorkspaceScopePublicationOperationIdentity Operation { get; }

    /// <summary>The complete mutation kind frozen by the request.</summary>
    public WorkspaceScopeOperationKind Kind { get; }

    /// <summary>
    /// The Workspace carried by the submitted expected revision, or
    /// <see langword="null"/> when that evidence was missing.
    /// </summary>
    public InspectionWorkspaceIdentity? ExpectedRevisionWorkspace { get; }

    /// <summary>
    /// The submitted expected revision identity, or <see langword="null"/>
    /// when that evidence was missing.
    /// </summary>
    public WorkspaceScopeRevisionIdentity? ExpectedRevision { get; }

    /// <summary>Whether the request included explicit Package activation intent.</summary>
    public bool HasExplicitTarget { get; }
}

/// <summary>
/// Resource-free explicit activation intent for one exact Package
/// correspondence.
/// </summary>
public sealed class WorkspaceScopePackageTarget
{
    internal WorkspaceScopePackageTarget(
        PackageArtifactRootCorrespondence correspondence)
    {
        Correspondence = correspondence;
    }

    /// <summary>The exact Workspace-scoped Package correspondence.</summary>
    public PackageArtifactRootCorrespondence Correspondence { get; }
}

/// <summary>
/// One inert, single-submission Scope mutation request.
/// </summary>
/// <remarks>
/// Issuance performs no Scope read, preparation, deadline registration,
/// publication, supersession, or physical work. Package bindings remain
/// transient request inputs and are not retained by the association or result.
/// </remarks>
public sealed class WorkspaceScopeRequest
{
    int _submitted;

    internal WorkspaceScopeRequest(
        InspectionWorkspaceIdentity workspace,
        WorkspaceScopeRevision? expectedRevision,
        WorkspaceScopePublicationBaseIdentity? expectedPublicationBase,
        bool requirePublicationBase,
        ImmutableArray<PackageRootBinding> packages,
        DateTimeOffset deadline,
        WorkspaceScopeOperationKind kind,
        WorkspaceScopePackageTarget? target,
        WorkspacePackageOccurrenceIdentity? occurrence,
        PackageAssemblyContextRealizationOptions? realizationOptions)
    {
        Association = new(
            workspace,
            new(),
            kind,
            expectedRevision,
            target is not null);
        ExpectedRevision = expectedRevision;
        ExpectedPublicationBase = expectedPublicationBase;
        RequirePublicationBase = requirePublicationBase;
        Packages = packages;
        Deadline = deadline;
        Limits = WorkspaceScopeLimits.Closed;
        Target = target;
        Occurrence = occurrence;
        RealizationOptions = realizationOptions;
    }

    /// <summary>
    /// Resource-free evidence available before this request is submitted.
    /// </summary>
    public WorkspaceScopeOperationAssociation Association { get; }

    /// <summary>The optional exact current-snapshot publication guard.</summary>
    public WorkspaceScopePublicationBaseIdentity? ExpectedPublicationBase { get; }

    /// <summary>The finite preparation deadline supplied by the caller.</summary>
    public DateTimeOffset Deadline { get; }

    /// <summary>The Scope limits frozen for this request.</summary>
    public WorkspaceScopeLimits Limits { get; }

    /// <summary>The optional exact Package activation target.</summary>
    public WorkspaceScopePackageTarget? Target { get; }

    internal WorkspaceScopeRevision? ExpectedRevision { get; }
    internal bool RequirePublicationBase { get; }
    internal ImmutableArray<PackageRootBinding> Packages { get; }
    internal WorkspacePackageOccurrenceIdentity? Occurrence { get; }
    internal PackageAssemblyContextRealizationOptions? RealizationOptions { get; }

    internal bool TryBeginSubmission() =>
        Interlocked.CompareExchange(ref _submitted, 1, 0) == 0;
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

    /// <summary>
    /// Finds the exact Scope-issued occurrence corresponding to one acquired
    /// Package binding.
    /// </summary>
    public WorkspacePackageOccurrenceDescriptor? FindPackageOccurrence(
        PackageRootBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        PackageArtifactRootRequest request =
            PackageArtifactRootRequest.From(binding);
        WorkspacePackageOccurrenceDescriptor? match = null;
        foreach (WorkspacePackageOccurrenceDescriptor package in Packages)
        {
            if (package.Occurrence.Correspondence
                    is not PackageArtifactRootCorrespondence correspondence
                || !correspondence.Matches(request))
            {
                continue;
            }

            if (match is not null)
            {
                throw new WorkspaceScopeInvariantException(
                    this,
                    ArtifactRootFailure.CompositionMismatch);
            }

            match = package;
        }

        return match;
    }
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
    PublicationBaseMismatch,
    Busy = 6,
    OccurrenceNotCurrent,
}

/// <summary>
/// Typed cancellation-control response, distinct from mutation settlement.
/// </summary>
public abstract record WorkspaceScopeCancellationResult
{
    private protected WorkspaceScopeCancellationResult() { }

    /// <summary>The targeted mutation's original settlement.</summary>
    public sealed record Settled : WorkspaceScopeCancellationResult
    {
        internal Settled(WorkspaceScopeOperationResult settlement)
        {
            Settlement = settlement;
        }

        /// <summary>The original correlated mutation settlement.</summary>
        public WorkspaceScopeOperationResult Settlement { get; }
    }

    /// <summary>No matching preparation remained when the action was observed.</summary>
    public sealed record ObservedNoEffect : WorkspaceScopeCancellationResult
    {
        internal ObservedNoEffect(WorkspaceScopeSnapshot snapshot)
        {
            Snapshot = snapshot;
        }

        /// <summary>The complete current Scope snapshot.</summary>
        public WorkspaceScopeSnapshot Snapshot { get; }
    }

    /// <summary>The cancellation action was malformed or foreign.</summary>
    public sealed record Rejected : WorkspaceScopeCancellationResult
    {
        internal Rejected(
            WorkspaceScopeSnapshot snapshot,
            WorkspaceScopeRejection reason)
        {
            Snapshot = snapshot;
            Reason = reason;
        }

        /// <summary>The complete current Scope snapshot.</summary>
        public WorkspaceScopeSnapshot Snapshot { get; }

        /// <summary>The exact control rejection reason.</summary>
        public WorkspaceScopeRejection Reason { get; }
    }

    /// <summary>
    /// The Workspace runtime was unavailable; any retained snapshot is
    /// historical diagnostic evidence only.
    /// </summary>
    public sealed record Unavailable : WorkspaceScopeCancellationResult
    {
        internal Unavailable(
            WorkspaceScopeSnapshot? lastSnapshot,
            ArtifactRootFailure runtimeFailure)
        {
            LastSnapshot = lastSnapshot;
            RuntimeFailure = runtimeFailure;
        }

        /// <summary>Historical diagnostic evidence, never current authority.</summary>
        public WorkspaceScopeSnapshot? LastSnapshot { get; }

        /// <summary>The exact runtime unavailability outcome.</summary>
        public ArtifactRootFailure RuntimeFailure { get; }
    }
}

/// <summary>Closed union of terminal Scope mutation outcomes.</summary>
public abstract record WorkspaceScopeOperationResult
{
    private protected WorkspaceScopeOperationResult(
        WorkspaceScopeOperationAssociation association)
    {
        Association = association;
    }

    /// <summary>The original request association retained by every outcome.</summary>
    public WorkspaceScopeOperationAssociation Association { get; }

    /// <summary>The original operation identity retained for convenient access.</summary>
    public WorkspaceScopePublicationOperationIdentity Operation =>
        Association.Operation;

    /// <summary>A complete Scope transition committed.</summary>
    public sealed record Committed : WorkspaceScopeOperationResult
    {
        internal Committed(
            WorkspaceScopeOperationAssociation association,
            WorkspaceScopeSnapshot snapshot,
            WorkspaceScopeOperationKind effect,
            WorkspacePackageOccurrenceDescriptor? requestedOccurrence)
            : base(association)
        {
            Snapshot = snapshot;
            Effect = effect;
            RequestedOccurrence = requestedOccurrence;
        }

        /// <summary>The complete committed Scope snapshot.</summary>
        public WorkspaceScopeSnapshot Snapshot { get; }

        /// <summary>The committed mutation kind.</summary>
        public WorkspaceScopeOperationKind Effect { get; }

        /// <summary>
        /// The exact requested Package occurrence, present only for successful
        /// explicit activation intent.
        /// </summary>
        public WorkspacePackageOccurrenceDescriptor? RequestedOccurrence { get; }
    }

    /// <summary>The valid request changed no Scope state.</summary>
    public sealed record NoEffect : WorkspaceScopeOperationResult
    {
        internal NoEffect(
            WorkspaceScopeOperationAssociation association,
            WorkspaceScopeSnapshot snapshot,
            WorkspacePackageOccurrenceDescriptor? requestedOccurrence)
            : base(association)
        {
            Snapshot = snapshot;
            RequestedOccurrence = requestedOccurrence;
        }

        /// <summary>The unchanged complete current Scope snapshot.</summary>
        public WorkspaceScopeSnapshot Snapshot { get; }

        /// <summary>
        /// The exact requested Package occurrence, present only for successful
        /// explicit activation intent.
        /// </summary>
        public WorkspacePackageOccurrenceDescriptor? RequestedOccurrence { get; }
    }

    /// <summary>The request was rejected before mutation admission.</summary>
    public sealed record Rejected : WorkspaceScopeOperationResult
    {
        internal Rejected(
            WorkspaceScopeOperationAssociation association,
            WorkspaceScopeSnapshot snapshot,
            WorkspaceScopeRejection reason)
            : base(association)
        {
            Snapshot = snapshot;
            Reason = reason;
        }

        /// <summary>The complete current Scope snapshot.</summary>
        public WorkspaceScopeSnapshot Snapshot { get; }

        /// <summary>The exact rejection reason.</summary>
        public WorkspaceScopeRejection Reason { get; }
    }

    /// <summary>The request failed without changing Scope membership.</summary>
    public sealed record Failed : WorkspaceScopeOperationResult
    {
        internal Failed(
            WorkspaceScopeOperationAssociation association,
            WorkspaceScopeSnapshot snapshot,
            ArtifactRootFailure failure)
            : base(association)
        {
            Snapshot = snapshot;
            Failure = failure;
        }

        /// <summary>The complete current Scope snapshot.</summary>
        public WorkspaceScopeSnapshot Snapshot { get; }

        /// <summary>The exact Artifact failure.</summary>
        public ArtifactRootFailure Failure { get; }
    }

    /// <summary>The request observed cancellation or deadline expiry first.</summary>
    public sealed record Cancelled : WorkspaceScopeOperationResult
    {
        internal Cancelled(
            WorkspaceScopeOperationAssociation association,
            WorkspaceScopeSnapshot snapshot)
            : base(association)
        {
            Snapshot = snapshot;
        }

        /// <summary>The complete current Scope snapshot.</summary>
        public WorkspaceScopeSnapshot Snapshot { get; }
    }

    /// <summary>The admitted request was displaced by a valid newer operation.</summary>
    public sealed record Superseded : WorkspaceScopeOperationResult
    {
        internal Superseded(
            WorkspaceScopeOperationAssociation association,
            WorkspaceScopeSnapshot snapshot,
            WorkspaceScopePublicationOperationIdentity supersedingOperation)
            : base(association)
        {
            Snapshot = snapshot;
            SupersedingOperation = supersedingOperation;
        }

        /// <summary>The complete current Scope snapshot.</summary>
        public WorkspaceScopeSnapshot Snapshot { get; }

        /// <summary>The distinct operation that displaced the original request.</summary>
        public WorkspaceScopePublicationOperationIdentity SupersedingOperation { get; }
    }

    /// <summary>The Workspace runtime was unavailable.</summary>
    public sealed record Unavailable : WorkspaceScopeOperationResult
    {
        internal Unavailable(
            WorkspaceScopeOperationAssociation association,
            WorkspaceScopeSnapshot? lastSnapshot,
            ArtifactRootFailure runtimeFailure)
            : base(association)
        {
            LastSnapshot = lastSnapshot;
            RuntimeFailure = runtimeFailure;
        }

        /// <summary>Historical diagnostic evidence, never current authority.</summary>
        public WorkspaceScopeSnapshot? LastSnapshot { get; }

        /// <summary>The exact runtime unavailability outcome.</summary>
        public ArtifactRootFailure RuntimeFailure { get; }
    }
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
