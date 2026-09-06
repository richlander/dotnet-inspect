using System.Collections.Immutable;

namespace DotnetInspector.Queries;

public sealed partial class InspectionWorkspace
{
    WorkspaceScopeSnapshot? _scopeSnapshot;
    ScopePreparation? _scopePreparation;

    /// <summary>
    /// Reads complete current Scope state, refreshing physical observations
    /// without changing logical membership or the Artifact composition.
    /// </summary>
    public async ValueTask<WorkspaceScopeReadResult> GetScopeSnapshotAsync()
    {
        var read = await ReadArtifactRootCompositionAsync(_identity).ConfigureAwait(false);
        if (read is ArtifactRootResult<ArtifactRootCompositionReadLease>.Rejected rejected)
            return new WorkspaceScopeReadResult.Unavailable(_scopeSnapshot, rejected.Failure);
        using var lease =
            ((ArtifactRootResult<ArtifactRootCompositionReadLease>.Available)read).Value;
        lock (_gate)
        {
            if (RootWorkspaceFailure(_identity) is { } unavailable)
                return new WorkspaceScopeReadResult.Unavailable(_scopeSnapshot, unavailable);
            return new WorkspaceScopeReadResult.Available(ObserveScope(lease));
        }
    }

    /// <summary>
    /// Atomically replaces a closed Scope using already-acquired package Roots.
    /// Requires an asynchronous Workspace and a finite deadline. Bindings are
    /// transient operation inputs; snapshots retain only resource-free facts.
    /// </summary>
    public ValueTask<WorkspaceScopeOperationResult> ReplaceScopeAsync(
        WorkspaceScopeRevision expectedRevision,
        ImmutableArray<PackageRootBinding> roots,
        DateTimeOffset deadline,
        CancellationToken cancellationToken = default) =>
        MutateScopeAsync(expectedRevision, roots, deadline,
            WorkspaceScopeOperationKind.Replace, cancellationToken);

    /// <summary>
    /// Commits a fresh empty closed revision and supersedes current preparation
    /// without waiting for previously admitted queries to drain.
    /// </summary>
    public ValueTask<WorkspaceScopeOperationResult> ClearScopeAsync(
        WorkspaceScopeRevision expectedRevision,
        DateTimeOffset deadline,
        CancellationToken cancellationToken = default) =>
        MutateScopeAsync(expectedRevision, [], deadline,
            WorkspaceScopeOperationKind.Clear, cancellationToken);

    /// <summary>
    /// Requests cancellation of the exact preparing operation and awaits its
    /// complete settlement. An action for a no-longer-preparing operation has no effect.
    /// </summary>
    public async ValueTask<WorkspaceScopeOperationResult> CancelScopePreparationAsync(
        WorkspaceScopeCancellationAction action)
    {
        var read = await ReadArtifactRootCompositionAsync(_identity).ConfigureAwait(false);
        if (read is ArtifactRootResult<ArtifactRootCompositionReadLease>.Rejected rejected)
            return new WorkspaceScopeOperationResult.Unavailable(_scopeSnapshot, rejected.Failure);
        ScopePreparation operation;
        using (var lease =
            ((ArtifactRootResult<ArtifactRootCompositionReadLease>.Available)read).Value)
        {
            lock (_gate)
            {
                if (RootWorkspaceFailure(_identity) is { } unavailable)
                    return new WorkspaceScopeOperationResult.Unavailable(_scopeSnapshot, unavailable);
                WorkspaceScopeSnapshot current = ObserveScope(lease);
                if (action is null)
                    return new WorkspaceScopeOperationResult.Rejected(current, WorkspaceScopeRejection.Malformed);
                if (!ReferenceEquals(action.Workspace, _identity))
                    return new WorkspaceScopeOperationResult.Rejected(current, WorkspaceScopeRejection.ForeignWorkspace);
                if (_scopePreparation is not { } preparing
                    || !ReferenceEquals(preparing.Identity, action.Operation))
                    return new WorkspaceScopeOperationResult.NoEffect(current);
                operation = preparing;
                if (!operation.Stop.IsCancellationRequested)
                {
                    var next = WithScopePreparation(current, current.Preparing);
                    operation.Cancel();
                    _scopeSnapshot = next;
                }
            }
        }
        return await operation.Completion.ConfigureAwait(false);
    }

    async ValueTask<WorkspaceScopeOperationResult> MutateScopeAsync(
        WorkspaceScopeRevision expectedRevision,
        ImmutableArray<PackageRootBinding> roots,
        DateTimeOffset deadline,
        WorkspaceScopeOperationKind kind,
        CancellationToken cancellationToken)
    {
        var read = await ReadArtifactRootCompositionAsync(_identity).ConfigureAwait(false);
        if (read is ArtifactRootResult<ArtifactRootCompositionReadLease>.Rejected rejected)
            return new WorkspaceScopeOperationResult.Unavailable(_scopeSnapshot, rejected.Failure);

        ScopePreparation operation;
        ImmutableArray<ScopeRequestedRoot> requested;
        using (var lease =
            ((ArtifactRootResult<ArtifactRootCompositionReadLease>.Available)read).Value)
        {
            lock (_gate)
            {
                if (RootWorkspaceFailure(_identity) is { } unavailable)
                    return new WorkspaceScopeOperationResult.Unavailable(_scopeSnapshot, unavailable);
                WorkspaceScopeSnapshot current = ObserveScope(lease);
                WorkspaceScopeRejection? failure =
                    ValidateScopeSubmission(expectedRevision, deadline, current);
                if (failure is { } invalid)
                    return new WorkspaceScopeOperationResult.Rejected(current, invalid);
                if (roots.IsDefault || roots.Any(static root => root is null))
                    return new WorkspaceScopeOperationResult.Rejected(
                        current, WorkspaceScopeRejection.Malformed);

                var unique = new HashSet<ArtifactRootCorrespondence>();
                var candidates = ImmutableArray.CreateBuilder<ScopeRequestedRoot>();
                foreach (PackageRootBinding binding in roots)
                {
                    ArtifactRootCorrespondence correspondence =
                        CreatePackageArtifactRootCorrespondence(binding);
                    if (!unique.Add(correspondence))
                        continue;
                    WorkspaceRootOccurrenceDescriptor? retained = current.Roots.FirstOrDefault(
                        row => row.Occurrence.Correspondence.Equals(correspondence));
                    candidates.Add(new(binding, correspondence, retained));
                }
                if (candidates.Count > current.Revision.Limits.MaxRoots)
                    return new WorkspaceScopeOperationResult.Rejected(
                        current, WorkspaceScopeRejection.RootCapacityExceeded);

                operation = new(_identity, kind, deadline, cancellationToken, current);
                if (operation.Stop.IsCancellationRequested)
                {
                    operation.Dispose();
                    return new WorkspaceScopeOperationResult.Cancelled(current, operation.Identity);
                }
                requested = candidates.ToImmutable();
                var next = WithScopePreparation(current,
                    new(_identity, operation.Identity, kind, requested.Length, deadline));
                if (_scopePreparation is { } displaced)
                {
                    displaced.SupersededBy = operation.Identity;
                    // CancelAsync signals immediately without running source callbacks
                    // under the composition lease. Its completion is awaited outside.
                    displaced.Cancel();
                }
                _scopePreparation = operation;
                _scopeSnapshot = next;
            }
        }

        Task<WorkspaceScopeOperationResult> execution = ExecuteScopeMutationAsync(operation, requested);
        operation.Started.SetResult(execution);
        return await execution.ConfigureAwait(false);
    }

    async Task<WorkspaceScopeOperationResult> ExecuteScopeMutationAsync(
        ScopePreparation operation,
        ImmutableArray<ScopeRequestedRoot> requested)
    {
        ArtifactRootPreparationReceipt? receipt = null;
        WorkspaceScopeOperationResult? result = null;
        ArtifactRootFailure physicalFailure = ArtifactRootFailure.PreparationFailed;
        try
        {
            ImmutableArray<PackageRootBinding> unmatched =
                [.. requested.Where(static root => !root.CanRetain).Select(static root => root.Binding)];
            if (!unmatched.IsEmpty)
            {
                var prepared = await PreparePackageArtifactRootsAsync(
                    operation.Authority, unmatched).ConfigureAwait(false);
                if (prepared is ArtifactRootResult<ArtifactRootPreparationReceipt>.Rejected failed)
                    physicalFailure = failed.Failure;
                else
                    receipt = ((ArtifactRootResult<ArtifactRootPreparationReceipt>.Available)prepared).Value;
            }

            if (unmatched.IsEmpty || receipt is not null)
            {
                ArtifactRootPublicationPlan? plan = null;
                var candidateRead =
                    await ReadArtifactRootCompositionAsync(_identity).ConfigureAwait(false);
                if (candidateRead is ArtifactRootResult<ArtifactRootCompositionReadLease>.Rejected failed)
                    physicalFailure = failed.Failure;
                else
                {
                    using var lease =
                        ((ArtifactRootResult<ArtifactRootCompositionReadLease>.Available)candidateRead).Value;
                    lock (_gate)
                    {
                        ArtifactRootFailure? failure = RootWorkspaceFailure(_identity)
                            ?? RootCancellationFailure(operation.Authority);
                        if (failure is null && !ReferenceEquals(_scopePreparation, operation))
                            failure = ArtifactRootFailure.Superseded;
                        if (failure is null
                            && !ReferenceEquals(lease.Composition, operation.Initial.PhysicalComposition))
                            failure = ArtifactRootFailure.CompositionMismatch;
                        if (failure is { } invalid)
                            physicalFailure = invalid;
                        else
                        {
                            WorkspaceScopeSnapshot current = ObserveScope(lease);
                            plan = CreateScopePlan(operation, requested, receipt, current);
                        }
                    }
                }
                if (plan is not null)
                {
                    ArtifactRootPublicationOutcome outcome =
                        await PublishArtifactRootCompositionAsync(plan).ConfigureAwait(false);
                    if (outcome.Published is { ScopeResult: ScopeCommittedResult committed })
                        result = committed.Value;
                    else
                        physicalFailure = outcome.Failure
                            ?? throw new InvalidOperationException("Scope publication returned no outcome.");
                }
            }
        }
        finally
        {
            if (receipt is not null && result is null)
                await ReleaseArtifactRootPreparationAsync(receipt).ConfigureAwait(false);
            if (result is null)
                result = await SettleScopeFailureAsync(operation, physicalFailure).ConfigureAwait(false);
            await operation.CancellationSettlement.ConfigureAwait(false);
            operation.Dispose();
        }
        return result;
    }

    WorkspaceScopeRejection? ValidateScopeSubmission(
        WorkspaceScopeRevision expected,
        DateTimeOffset deadline,
        WorkspaceScopeSnapshot current)
    {
        if (expected is null || !FiniteDeadline(deadline))
            return WorkspaceScopeRejection.Malformed;
        if (deadline <= _rootTime.GetUtcNow())
            return WorkspaceScopeRejection.DeadlineExpired;
        if (!ReferenceEquals(expected.Workspace, _identity))
            return WorkspaceScopeRejection.ForeignWorkspace;
        if (!ReferenceEquals(expected.Identity, current.Revision.Identity))
            return WorkspaceScopeRejection.RevisionMismatch;
        return _lifetimeMode == InspectionWorkspaceLifetimeMode.Asynchronous
            ? null : WorkspaceScopeRejection.AsynchronousWorkspaceRequired;
    }

    ArtifactRootPublicationPlan CreateScopePlan(
        ScopePreparation operation,
        ImmutableArray<ScopeRequestedRoot> requested,
        ArtifactRootPreparationReceipt? receipt,
        WorkspaceScopeSnapshot current)
    {
        var entries = ImmutableArray.CreateBuilder<ArtifactRootPublicationEntry>(requested.Length);
        var occurrences = ImmutableArray.CreateBuilder<WorkspaceRootOccurrence>(requested.Length);
        int preparedIndex = 0;
        foreach (ScopeRequestedRoot root in requested)
        {
            if (root.Retained?.Realization.Status is ArtifactRootRealizationStatus.Ready ready)
                entries.Add(new ArtifactRootPublicationEntry.Retain(root.Correspondence, ready.Generation));
            else
            {
                ArtifactRootPreparedEntry prepared = receipt!.Entries[preparedIndex++];
                if (!prepared.Correspondence.Equals(root.Correspondence))
                    throw new WorkspaceScopeInvariantException(current, ArtifactRootFailure.CompositionMismatch);
                entries.Add(new ArtifactRootPublicationEntry.Adopt(receipt.Preparation, prepared.Entry));
            }
            occurrences.Add(root.Retained?.Occurrence
                ?? new WorkspaceRootOccurrence(_identity,
                    new WorkspaceRootDescriptor.Package(root.Binding), root.Correspondence));
        }
        var revision = new WorkspaceScopeRevision(_identity, occurrences.MoveToImmutable());
        var candidate = new ScopePublicationCandidate(this, operation, current.PublicationBase, revision);
        return new(operation.Authority, current.PhysicalComposition,
            entries.MoveToImmutable(), receipt is null ? [] : [receipt], new(candidate));
    }

    async ValueTask<WorkspaceScopeOperationResult> SettleScopeFailureAsync(
        ScopePreparation operation, ArtifactRootFailure failure)
    {
        var read = await ReadArtifactRootCompositionAsync(_identity).ConfigureAwait(false);
        if (read is ArtifactRootResult<ArtifactRootCompositionReadLease>.Rejected unavailable)
        {
            lock (_gate)
            {
                if (ReferenceEquals(_scopePreparation, operation))
                    _scopePreparation = null;
                return new WorkspaceScopeOperationResult.Unavailable(_scopeSnapshot, unavailable.Failure);
            }
        }
        using var lease =
            ((ArtifactRootResult<ArtifactRootCompositionReadLease>.Available)read).Value;
        lock (_gate)
        {
            if (RootWorkspaceFailure(_identity) is { } closing)
            {
                if (ReferenceEquals(_scopePreparation, operation))
                    _scopePreparation = null;
                return new WorkspaceScopeOperationResult.Unavailable(_scopeSnapshot, closing);
            }
            WorkspaceScopeSnapshot current = ObserveScope(lease);
            if (ReferenceEquals(_scopePreparation, operation))
            {
                current = WithScopePreparation(current, null);
                _scopeSnapshot = current;
                _scopePreparation = null;
            }
            if (operation.SupersededBy is { } superseding)
                return new WorkspaceScopeOperationResult.Superseded(current, superseding);
            if (RootCancellationFailure(operation.Authority) is not null
                || failure is ArtifactRootFailure.Cancelled or ArtifactRootFailure.DeadlineExpired)
                return new WorkspaceScopeOperationResult.Cancelled(current, operation.Identity);
            return new WorkspaceScopeOperationResult.Failed(current, failure);
        }
    }

    // Called only while holding Artifact's composition read lease and runtime gate.
    WorkspaceScopeSnapshot ObserveScope(ArtifactRootCompositionReadLease lease)
    {
        if (_scopeSnapshot is null)
        {
            if (!lease.Roots.IsEmpty)
                throw new WorkspaceScopeInvariantException(null, ArtifactRootFailure.CompositionMismatch);
            var revision = new WorkspaceScopeRevision(_identity, []);
            _scopeSnapshot = new(revision, lease.Composition, [], new(revision.Identity), null);
        }
        else if (!ReferenceEquals(_scopeSnapshot.PhysicalComposition, lease.Composition))
        {
            WorkspaceScopeSnapshot current = _scopeSnapshot;
            var rows = ProjectScopeRoots(current.Revision, lease.Roots, current);
            _scopeSnapshot = new(current.Revision, lease.Composition, rows,
                new(current.Revision.Identity), current.Preparing);
        }
        return _scopeSnapshot;
    }

    static ImmutableArray<WorkspaceRootOccurrenceDescriptor> ProjectScopeRoots(
        WorkspaceScopeRevision revision,
        ImmutableArray<ArtifactRootScopeProjection> projections,
        WorkspaceScopeSnapshot? historical)
    {
        if (projections.Length != revision.Roots.Length)
            throw new WorkspaceScopeInvariantException(historical, ArtifactRootFailure.CompositionMismatch);
        var rows = ImmutableArray.CreateBuilder<WorkspaceRootOccurrenceDescriptor>(revision.Roots.Length);
        foreach (WorkspaceRootOccurrence occurrence in revision.Roots)
        {
            ArtifactRootScopeProjection? projection = projections.FirstOrDefault(
                root => root.Correspondence.Equals(occurrence.Correspondence));
            if (projection is null)
                throw new WorkspaceScopeInvariantException(historical, ArtifactRootFailure.Absent);
            rows.Add(new(occurrence, projection));
        }
        return rows.MoveToImmutable();
    }

    static WorkspaceScopeSnapshot WithScopePreparation(
        WorkspaceScopeSnapshot current,
        WorkspaceScopePreparationDescriptor? preparing) =>
        new(current.Revision, current.PhysicalComposition, current.Roots, current.Closure, preparing);

    sealed record ScopeRequestedRoot(
        PackageRootBinding Binding,
        ArtifactRootCorrespondence Correspondence,
        WorkspaceRootOccurrenceDescriptor? Retained)
    {
        internal bool CanRetain => Retained?.Realization.Status is ArtifactRootRealizationStatus.Ready;
    }

    sealed class ScopePreparation : IDisposable
    {
        internal ScopePreparation(
            InspectionWorkspaceIdentity workspace,
            WorkspaceScopeOperationKind kind,
            DateTimeOffset deadline,
            CancellationToken cancellation,
            WorkspaceScopeSnapshot initial)
        {
            Kind = kind;
            Initial = initial;
            Stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            Authority = new(workspace, new(), deadline, Stop.Token);
            Completion = Started.Task.Unwrap();
        }

        internal WorkspaceScopePublicationOperationIdentity Identity { get; } = new();
        internal WorkspaceScopeOperationKind Kind { get; }
        internal WorkspaceScopeSnapshot Initial { get; }
        internal ArtifactRootPreparationAuthority Authority { get; }
        internal CancellationTokenSource Stop { get; }
        internal WorkspaceScopePublicationOperationIdentity? SupersededBy { get; set; }
        internal TaskCompletionSource<Task<WorkspaceScopeOperationResult>> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task<WorkspaceScopeOperationResult> Completion { get; }
        internal Task CancellationSettlement { get; private set; } = Task.CompletedTask;
        internal void Cancel()
        {
            if (!Stop.IsCancellationRequested)
                CancellationSettlement = Stop.CancelAsync();
        }
        public void Dispose() => Stop.Dispose();
    }

    sealed class ScopePublicationCandidate(
        InspectionWorkspace owner,
        ScopePreparation preparation,
        WorkspaceScopePublicationBaseIdentity expectedBase,
        WorkspaceScopeRevision revision) : IWorkspaceScopePublicationCandidate
    {
        public InspectionWorkspaceIdentity Workspace => owner.Identity;
        public WorkspaceScopePublicationBaseIdentity ExpectedBase => expectedBase;
        public WorkspaceScopePublicationOperationIdentity Operation => preparation.Identity;
        public WorkspaceScopePublicationCandidateIdentity CandidateSet => preparation.Authority.CandidateSet;

        public ArtifactRootResult<WorkspaceScopePreparedCommit> PrepareCommit(
            ArtifactRootCompositionGenerationIdentity currentComposition,
            ArtifactRootCompositionGenerationIdentity candidateComposition,
            ImmutableArray<ArtifactRootScopeProjection> roots)
        {
            if (!ReferenceEquals(owner._scopePreparation, preparation))
                return new ArtifactRootResult<WorkspaceScopePreparedCommit>.Rejected(ArtifactRootFailure.Superseded);
            if (!ReferenceEquals(owner._scopeSnapshot!.PublicationBase, expectedBase))
                return new ArtifactRootResult<WorkspaceScopePreparedCommit>.Rejected(ArtifactRootFailure.ScopeBaseMismatch);
            if (!ReferenceEquals(preparation.Initial.PhysicalComposition, currentComposition))
                return new ArtifactRootResult<WorkspaceScopePreparedCommit>.Rejected(ArtifactRootFailure.CompositionMismatch);
            ImmutableArray<WorkspaceRootOccurrenceDescriptor> rows;
            try
            {
                rows = ProjectScopeRoots(revision, roots, owner._scopeSnapshot);
            }
            catch (WorkspaceScopeInvariantException failure)
            {
                return new ArtifactRootResult<WorkspaceScopePreparedCommit>.Rejected(failure.Failure);
            }
            var snapshot = new WorkspaceScopeSnapshot(revision, candidateComposition,
                rows, new(revision.Identity), null);
            var result = new ScopeCommittedResult(
                new(snapshot, preparation.Kind, preparation.Identity));
            return new ArtifactRootResult<WorkspaceScopePreparedCommit>.Available(
                new ScopeCommit(owner, snapshot, result));
        }
    }

    sealed class ScopeCommittedResult(WorkspaceScopeOperationResult.Committed value)
        : WorkspaceScopePublicationResult
    {
        internal WorkspaceScopeOperationResult.Committed Value { get; } = value;
    }

    sealed class ScopeCommit(
        InspectionWorkspace owner,
        WorkspaceScopeSnapshot snapshot,
        ScopeCommittedResult result) : WorkspaceScopePreparedCommit(result)
    {
        internal override void Commit()
        {
            owner._scopeSnapshot = snapshot;
            owner._scopePreparation = null;
        }
    }
}
