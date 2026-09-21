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
    /// Atomically replaces a closed Scope using already-acquired Packages.
    /// Requires a finite deadline. Bindings are
    /// transient operation inputs; snapshots retain only resource-free facts.
    /// </summary>
    public ValueTask<WorkspaceScopeOperationResult> ReplaceScopeAsync(
        WorkspaceScopeRevision expectedRevision,
        ImmutableArray<PackageRootBinding> packages,
        DateTimeOffset deadline,
        CancellationToken cancellationToken = default) =>
        SubmitScopeRequestAsync(
            IssueReplaceScopeRequest(expectedRevision, packages, deadline),
            cancellationToken);

    /// <summary>
    /// Issues an inert complete Scope replacement request. The returned
    /// association is available before explicit submission.
    /// </summary>
    public WorkspaceScopeRequest IssueReplaceScopeRequest(
        WorkspaceScopeRevision? expectedRevision,
        ImmutableArray<PackageRootBinding> packages,
        DateTimeOffset deadline,
        WorkspaceScopePackageTarget? target = null) =>
        IssueScopeRequest(
            expectedRevision,
            expectedPublicationBase: null,
            requirePublicationBase: false,
            packages,
            deadline,
            WorkspaceScopeOperationKind.Replace,
            target,
            occurrence: null,
            realizationOptions: null);

    /// <summary>
    /// Commits a fresh empty closed revision and supersedes current preparation
    /// without waiting for previously admitted queries to drain.
    /// </summary>
    public ValueTask<WorkspaceScopeOperationResult> ClearScopeAsync(
        WorkspaceScopeRevision expectedRevision,
        DateTimeOffset deadline,
        CancellationToken cancellationToken = default) =>
        SubmitScopeRequestAsync(
            IssueClearScopeRequest(expectedRevision, deadline),
            cancellationToken);

    /// <summary>
    /// Issues an inert complete Scope clear request. Clear has no activation
    /// target in the exact-Package profile.
    /// </summary>
    public WorkspaceScopeRequest IssueClearScopeRequest(
        WorkspaceScopeRevision? expectedRevision,
        DateTimeOffset deadline) =>
        IssueScopeRequest(
            expectedRevision,
            expectedPublicationBase: null,
            requirePublicationBase: false,
            [],
            deadline,
            WorkspaceScopeOperationKind.Clear,
            target: null,
            occurrence: null,
            realizationOptions: null);

    /// <summary>
    /// Appends one all-or-failure batch of distinct already-acquired Packages,
    /// preserving current order and exact occurrences. Surviving Packages
    /// must be Ready unless the batch has no effect.
    /// </summary>
    public ValueTask<WorkspaceScopeOperationResult> AddPackagesAsync(
        WorkspaceScopeRevision expectedRevision,
        ImmutableArray<PackageRootBinding> packages,
        DateTimeOffset deadline,
        CancellationToken cancellationToken = default) =>
        SubmitScopeRequestAsync(
            IssueAddPackagesRequest(expectedRevision, packages, deadline),
            cancellationToken);

    internal ValueTask<WorkspaceScopeOperationResult>
        AddPackagesWithRealizationOptionsAsync(
            WorkspaceScopeRevision expectedRevision,
            ImmutableArray<PackageRootBinding> packages,
            PackageAssemblyContextRealizationOptions realizationOptions,
            DateTimeOffset deadline,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(realizationOptions);
        realizationOptions.Validate();
        return SubmitScopeRequestAsync(IssueScopeRequest(
            expectedRevision,
            expectedPublicationBase: null,
            requirePublicationBase: false,
            packages,
            deadline,
            WorkspaceScopeOperationKind.Add,
            target: null,
            occurrence: null,
            realizationOptions),
            cancellationToken);
    }

    /// <summary>
    /// Issues an inert all-or-failure Package addition request, optionally
    /// naming one exact requested Package occurrence for activation.
    /// </summary>
    public WorkspaceScopeRequest IssueAddPackagesRequest(
        WorkspaceScopeRevision? expectedRevision,
        ImmutableArray<PackageRootBinding> packages,
        DateTimeOffset deadline,
        WorkspaceScopePackageTarget? target = null) =>
        IssueScopeRequest(
            expectedRevision,
            expectedPublicationBase: null,
            requirePublicationBase: false,
            packages,
            deadline,
            WorkspaceScopeOperationKind.Add,
            target,
            occurrence: null,
            realizationOptions: null);

    /// <summary>
    /// Appends one all-or-failure batch against an exact current Scope
    /// publication base.
    /// </summary>
    public ValueTask<WorkspaceScopeOperationResult> AddPackagesAsync(
        WorkspaceScopeRevision expectedRevision,
        WorkspaceScopePublicationBaseIdentity expectedPublicationBase,
        ImmutableArray<PackageRootBinding> packages,
        DateTimeOffset deadline,
        CancellationToken cancellationToken = default) =>
        SubmitScopeRequestAsync(
            IssueAddPackagesRequest(
                expectedRevision,
                expectedPublicationBase,
                packages,
                deadline),
            cancellationToken);

    /// <summary>
    /// Issues an inert all-or-failure Package addition request against one
    /// exact Scope publication base.
    /// </summary>
    public WorkspaceScopeRequest IssueAddPackagesRequest(
        WorkspaceScopeRevision? expectedRevision,
        WorkspaceScopePublicationBaseIdentity? expectedPublicationBase,
        ImmutableArray<PackageRootBinding> packages,
        DateTimeOffset deadline,
        WorkspaceScopePackageTarget? target = null) =>
        IssueScopeRequest(
            expectedRevision,
            expectedPublicationBase,
            requirePublicationBase: true,
            packages,
            deadline,
            WorkspaceScopeOperationKind.Add,
            target,
            occurrence: null,
            realizationOptions: null);

    /// <summary>
    /// Removes one exact current occurrence without choosing a successor or
    /// waiting for admitted queries to drain. Surviving Packages must be Ready.
    /// </summary>
    public ValueTask<WorkspaceScopeOperationResult> RemovePackageOccurrenceAsync(
        WorkspaceScopeRevision expectedRevision,
        WorkspacePackageOccurrenceIdentity occurrence,
        DateTimeOffset deadline,
        CancellationToken cancellationToken = default) =>
        SubmitScopeRequestAsync(
            IssueRemovePackageOccurrenceRequest(
                expectedRevision,
                occurrence,
                deadline),
            cancellationToken);

    /// <summary>
    /// Issues an inert request to remove one exact current occurrence. Remove
    /// has no activation target in the exact-Package profile.
    /// </summary>
    public WorkspaceScopeRequest IssueRemovePackageOccurrenceRequest(
        WorkspaceScopeRevision? expectedRevision,
        WorkspacePackageOccurrenceIdentity? occurrence,
        DateTimeOffset deadline) =>
        IssueScopeRequest(
            expectedRevision,
            expectedPublicationBase: null,
            requirePublicationBase: false,
            [],
            deadline,
            WorkspaceScopeOperationKind.Remove,
            target: null,
            occurrence,
            realizationOptions: null);

    /// <summary>
    /// Creates resource-free explicit activation intent for the exact Package
    /// request represented by an already-acquired binding.
    /// </summary>
    public WorkspaceScopePackageTarget CreateScopePackageTarget(
        PackageRootBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return new(new(_identity, PackageArtifactRootRequest.From(binding)));
    }

    /// <summary>
    /// Submits one issued request through this Workspace. A request identity
    /// can be submitted only once.
    /// </summary>
    public ValueTask<WorkspaceScopeOperationResult> SubmitScopeRequestAsync(
        WorkspaceScopeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.TryBeginSubmission())
        {
            throw new InvalidOperationException(
                "The Scope request has already been submitted.");
        }
        return MutateScopeAsync(request, cancellationToken);
    }

    WorkspaceScopeRequest IssueScopeRequest(
        WorkspaceScopeRevision? expectedRevision,
        WorkspaceScopePublicationBaseIdentity? expectedPublicationBase,
        bool requirePublicationBase,
        ImmutableArray<PackageRootBinding> packages,
        DateTimeOffset deadline,
        WorkspaceScopeOperationKind kind,
        WorkspaceScopePackageTarget? target,
        WorkspacePackageOccurrenceIdentity? occurrence,
        PackageAssemblyContextRealizationOptions? realizationOptions) =>
        new(
            _identity,
            expectedRevision,
            expectedPublicationBase,
            requirePublicationBase,
            packages,
            deadline,
            kind,
            target,
            occurrence,
            realizationOptions);

    /// <summary>
    /// Requests cancellation of the exact preparing operation and awaits its
    /// complete settlement. A no-longer-preparing action returns a typed
    /// control observation rather than a mutation result.
    /// </summary>
    public async ValueTask<WorkspaceScopeCancellationResult> CancelScopePreparationAsync(
        WorkspaceScopeCancellationAction action)
    {
        var read = await ReadArtifactRootCompositionAsync(_identity).ConfigureAwait(false);
        if (read is ArtifactRootResult<ArtifactRootCompositionReadLease>.Rejected rejected)
            return new WorkspaceScopeCancellationResult.Unavailable(_scopeSnapshot, rejected.Failure);
        ScopePreparation operation;
        using (var lease =
            ((ArtifactRootResult<ArtifactRootCompositionReadLease>.Available)read).Value)
        {
            lock (_gate)
            {
                if (RootWorkspaceFailure(_identity) is { } unavailable)
                    return new WorkspaceScopeCancellationResult.Unavailable(_scopeSnapshot, unavailable);
                WorkspaceScopeSnapshot current = ObserveScope(lease);
                if (action is null)
                    return new WorkspaceScopeCancellationResult.Rejected(
                        current, WorkspaceScopeRejection.Malformed);
                if (!ReferenceEquals(action.Workspace, _identity))
                    return new WorkspaceScopeCancellationResult.Rejected(
                        current, WorkspaceScopeRejection.ForeignWorkspace);
                if (_scopePreparation is not { } preparing
                    || !ReferenceEquals(preparing.Identity, action.Operation))
                    return new WorkspaceScopeCancellationResult.ObservedNoEffect(current);
                operation = preparing;
                if (!operation.Stop.IsCancellationRequested)
                {
                    var next = WithScopePreparation(current, current.Preparing);
                    operation.Cancel();
                    _scopeSnapshot = next;
                }
            }
        }
        return new WorkspaceScopeCancellationResult.Settled(
            await operation.Completion.ConfigureAwait(false));
    }

    async ValueTask<WorkspaceScopeOperationResult> MutateScopeAsync(
        WorkspaceScopeRequest request,
        CancellationToken cancellationToken)
    {
        WorkspaceScopeOperationAssociation association = request.Association;
        var read = await ReadArtifactRootCompositionAsync(_identity).ConfigureAwait(false);
        if (read is ArtifactRootResult<ArtifactRootCompositionReadLease>.Rejected rejected)
            return new WorkspaceScopeOperationResult.Unavailable(
                association, _scopeSnapshot, rejected.Failure);

        ScopePreparation operation;
        ImmutableArray<ScopeRequestedPackage> requested;
        using (var lease =
            ((ArtifactRootResult<ArtifactRootCompositionReadLease>.Available)read).Value)
        {
            lock (_gate)
            {
                if (RootWorkspaceFailure(_identity) is { } unavailable)
                    return new WorkspaceScopeOperationResult.Unavailable(
                        association, _scopeSnapshot, unavailable);
                WorkspaceScopeSnapshot current = ObserveScope(lease);
                WorkspaceScopeRejection? failure =
                    ValidateScopeSubmission(
                        request.ExpectedRevision,
                        request.ExpectedPublicationBase,
                        request.RequirePublicationBase,
                        request.Deadline,
                        request.Limits,
                        association,
                        current);
                if (failure is { } invalid)
                    return new WorkspaceScopeOperationResult.Rejected(
                        association, current, invalid);
                if (request.Packages.IsDefault
                    || request.Packages.Any(static package => package is null))
                    return new WorkspaceScopeOperationResult.Rejected(
                        association, current, WorkspaceScopeRejection.Malformed);

                if (association.Kind == WorkspaceScopeOperationKind.Remove)
                {
                    if (request.Occurrence is null)
                        return new WorkspaceScopeOperationResult.Rejected(
                            association, current, WorkspaceScopeRejection.Malformed);
                    if (!ReferenceEquals(
                        request.Occurrence.WorkspaceIdentity,
                        _identity))
                    {
                        return new WorkspaceScopeOperationResult.Rejected(
                            association, current, WorkspaceScopeRejection.ForeignWorkspace);
                    }
                    if (!current.Packages.Any(row => ReferenceEquals(
                        row.Occurrence.Identity,
                        request.Occurrence)))
                    {
                        return new WorkspaceScopeOperationResult.Rejected(
                            association, current, WorkspaceScopeRejection.OccurrenceNotCurrent);
                    }
                }

                if (request.Target is { } target
                    && !request.Packages.Any(binding =>
                        target.Correspondence.Equals(
                            new PackageArtifactRootCorrespondence(
                                _identity,
                                PackageArtifactRootRequest.From(binding)))))
                {
                    return new WorkspaceScopeOperationResult.Rejected(
                        association, current, WorkspaceScopeRejection.Malformed);
                }

                ImmutableArray<WorkspacePackageOccurrenceDescriptor> survivors =
                    association.Kind switch
                {
                    WorkspaceScopeOperationKind.Add => current.Packages,
                    WorkspaceScopeOperationKind.Remove =>
                        [.. current.Packages.Where(row => !ReferenceEquals(
                            row.Occurrence.Identity,
                            request.Occurrence))],
                    _ => [],
                };
                var unique = new HashSet<ArtifactRootCorrespondence>();
                foreach (WorkspacePackageOccurrenceDescriptor survivor in survivors)
                    unique.Add(survivor.Occurrence.Correspondence);
                var candidates = ImmutableArray.CreateBuilder<ScopeRequestedPackage>();
                foreach (PackageRootBinding binding in request.Packages)
                {
                    ArtifactRootCorrespondence correspondence =
                        CreatePackageArtifactRootCorrespondence(binding);
                    if (!unique.Add(correspondence))
                        continue;
                    WorkspacePackageOccurrenceDescriptor? retained = current.Packages.FirstOrDefault(
                        row => row.Occurrence.Correspondence.Equals(correspondence));
                    candidates.Add(retained?.Realization.Status is ArtifactRootRealizationStatus.Ready ready
                        ? new ScopeRequestedPackage.Retain(retained.Occurrence, ready.Generation)
                        : new ScopeRequestedPackage.Prepare(binding, correspondence, retained?.Occurrence));
                }
                if (candidates.Count > current.Revision.Limits.MaxPackages - survivors.Length)
                    return new WorkspaceScopeOperationResult.Rejected(
                        association, current, WorkspaceScopeRejection.PackageCapacityExceeded);
                if (association.Kind
                        is WorkspaceScopeOperationKind.Add
                        or WorkspaceScopeOperationKind.Remove
                    && _scopePreparation is not null)
                {
                    return new WorkspaceScopeOperationResult.Rejected(
                        association, current, WorkspaceScopeRejection.Busy);
                }

                operation = new(
                    _identity,
                    association,
                    request.Target?.Correspondence,
                    request.Deadline,
                    cancellationToken,
                    current);
                if (operation.Stop.IsCancellationRequested)
                {
                    operation.Dispose();
                    return new WorkspaceScopeOperationResult.Cancelled(
                        association, current);
                }
                if (association.Kind == WorkspaceScopeOperationKind.Add
                    && candidates.Count == 0)
                {
                    operation.Dispose();
                    return new WorkspaceScopeOperationResult.NoEffect(
                        association,
                        current,
                        FindRequestedOccurrence(
                            current,
                            request.Target?.Correspondence));
                }
                var complete = ImmutableArray.CreateBuilder<ScopeRequestedPackage>(survivors.Length + candidates.Count);
                foreach (WorkspacePackageOccurrenceDescriptor survivor in survivors)
                {
                    if (survivor.Realization.Status is not ArtifactRootRealizationStatus.Ready ready)
                    {
                        operation.Dispose();
                        return new WorkspaceScopeOperationResult.Failed(
                            association,
                            current,
                            ArtifactRootFailure.ArtifactGenerationMismatch);
                    }
                    complete.Add(new ScopeRequestedPackage.Retain(survivor.Occurrence, ready.Generation));
                }
                complete.AddRange(candidates);
                requested = complete.MoveToImmutable();
                var next = WithScopePreparation(current,
                    new(_identity, operation.Identity, association.Kind,
                        association.Kind == WorkspaceScopeOperationKind.Remove
                            ? 1
                            : candidates.Count,
                        request.Deadline));
                if (_scopePreparation is { } displaced)
                {
                    // Preserve a cancellation or deadline that won before displacement.
                    if (RootCancellationFailure(displaced.Authority) is null)
                        displaced.SupersededBy = operation.Identity;
                    // CancelAsync signals immediately without running source callbacks
                    // under the composition lease. Its completion is awaited outside.
                    displaced.Cancel();
                }
                _scopePreparation = operation;
                _scopeSnapshot = next;
            }
        }

        Task<WorkspaceScopeOperationResult> execution =
            ExecuteScopeMutationAsync(
                operation,
                requested,
                request.RealizationOptions);
        operation.Started.SetResult(execution);
        return await execution.ConfigureAwait(false);
    }

    async Task<WorkspaceScopeOperationResult> ExecuteScopeMutationAsync(
        ScopePreparation operation,
        ImmutableArray<ScopeRequestedPackage> requested,
        PackageAssemblyContextRealizationOptions? realizationOptions)
    {
        ArtifactRootPreparationReceipt? receipt = null;
        WorkspaceScopeOperationResult? result = null;
        ArtifactRootFailure physicalFailure = ArtifactRootFailure.PreparationFailed;
        try
        {
            ImmutableArray<PackageRootBinding> unmatched =
                [.. requested.OfType<ScopeRequestedPackage.Prepare>().Select(static package => package.Binding)];
            if (!unmatched.IsEmpty)
            {
                var prepared = await PreparePackageArtifactRootsAsync(
                    operation.Authority,
                    unmatched,
                    realizationOptions).ConfigureAwait(false);
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
        WorkspaceScopeRevision? expected,
        WorkspaceScopePublicationBaseIdentity? expectedPublicationBase,
        bool requirePublicationBase,
        DateTimeOffset deadline,
        WorkspaceScopeLimits limits,
        WorkspaceScopeOperationAssociation association,
        WorkspaceScopeSnapshot current)
    {
        if (expected is null
            || requirePublicationBase && expectedPublicationBase is null
            || !FiniteDeadline(deadline)
            || limits.MaxPackages <= 0
            || !Enum.IsDefined(association.Kind)
            || association.HasExplicitTarget
                && association.Kind
                    is not WorkspaceScopeOperationKind.Add
                    and not WorkspaceScopeOperationKind.Replace)
            return WorkspaceScopeRejection.Malformed;
        if (deadline <= _rootTime.GetUtcNow())
            return WorkspaceScopeRejection.DeadlineExpired;
        if (!ReferenceEquals(association.Workspace, _identity))
            return WorkspaceScopeRejection.ForeignWorkspace;
        if (!ReferenceEquals(expected.Workspace, _identity))
            return WorkspaceScopeRejection.ForeignWorkspace;
        if (!ReferenceEquals(expected.Identity, current.Revision.Identity))
            return WorkspaceScopeRejection.RevisionMismatch;
        if (requirePublicationBase
            && !ReferenceEquals(
                expectedPublicationBase,
                current.PublicationBase))
        {
            return WorkspaceScopeRejection.PublicationBaseMismatch;
        }
        return null;
    }

    ArtifactRootPublicationPlan CreateScopePlan(
        ScopePreparation operation,
        ImmutableArray<ScopeRequestedPackage> requested,
        ArtifactRootPreparationReceipt? receipt,
        WorkspaceScopeSnapshot current)
    {
        var entries = ImmutableArray.CreateBuilder<ArtifactRootPublicationEntry>(requested.Length);
        var occurrences = ImmutableArray.CreateBuilder<WorkspacePackageOccurrence>(requested.Length);
        int preparedIndex = 0;
        foreach (ScopeRequestedPackage package in requested)
        {
            switch (package)
            {
                case ScopeRequestedPackage.Retain retained:
                    entries.Add(new ArtifactRootPublicationEntry.Retain(
                        retained.Occurrence.Correspondence, retained.Generation));
                    occurrences.Add(retained.Occurrence);
                    break;
                case ScopeRequestedPackage.Prepare pending:
                    ArtifactRootPreparedEntry prepared = receipt!.Entries[preparedIndex++];
                    if (!prepared.Correspondence.Equals(pending.Correspondence))
                        throw new WorkspaceScopeInvariantException(current, ArtifactRootFailure.CompositionMismatch);
                    entries.Add(new ArtifactRootPublicationEntry.Adopt(receipt.Preparation, prepared.Entry));
                    occurrences.Add(pending.ExistingOccurrence
                        ?? new WorkspacePackageOccurrence(_identity,
                            new WorkspacePackageDescriptor(pending.Binding), pending.Correspondence));
                    break;
            }
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
                return new WorkspaceScopeOperationResult.Unavailable(
                    operation.Association,
                    _scopeSnapshot,
                    unavailable.Failure);
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
                return new WorkspaceScopeOperationResult.Unavailable(
                    operation.Association,
                    _scopeSnapshot,
                    closing);
            }
            WorkspaceScopeSnapshot current = ObserveScope(lease);
            if (ReferenceEquals(_scopePreparation, operation))
            {
                current = WithScopePreparation(current, null);
                _scopeSnapshot = current;
                _scopePreparation = null;
            }
            if (operation.SupersededBy is { } superseding)
            {
                return new WorkspaceScopeOperationResult.Superseded(
                    operation.Association,
                    current,
                    superseding);
            }
            if (RootCancellationFailure(operation.Authority) is not null
                || failure is ArtifactRootFailure.Cancelled or ArtifactRootFailure.DeadlineExpired)
            {
                return new WorkspaceScopeOperationResult.Cancelled(
                    operation.Association,
                    current);
            }
            return new WorkspaceScopeOperationResult.Failed(
                operation.Association,
                current,
                failure);
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
            var rows = ProjectScopePackages(current.Revision, lease.Roots, current);
            _scopeSnapshot = new(current.Revision, lease.Composition, rows,
                new(current.Revision.Identity), current.Preparing);
        }
        return _scopeSnapshot;
    }

    static ImmutableArray<WorkspacePackageOccurrenceDescriptor> ProjectScopePackages(
        WorkspaceScopeRevision revision,
        ImmutableArray<ArtifactRootScopeProjection> projections,
        WorkspaceScopeSnapshot? historical)
    {
        if (projections.Length != revision.Packages.Length)
            throw new WorkspaceScopeInvariantException(historical, ArtifactRootFailure.CompositionMismatch);
        var rows = ImmutableArray.CreateBuilder<WorkspacePackageOccurrenceDescriptor>(revision.Packages.Length);
        foreach (WorkspacePackageOccurrence occurrence in revision.Packages)
        {
            ArtifactRootScopeProjection? projection = projections.FirstOrDefault(
                candidate => candidate.Correspondence.Equals(occurrence.Correspondence));
            if (projection is null)
                throw new WorkspaceScopeInvariantException(historical, ArtifactRootFailure.Absent);
            rows.Add(new(occurrence, projection));
        }
        return rows.MoveToImmutable();
    }

    static WorkspaceScopeSnapshot WithScopePreparation(
        WorkspaceScopeSnapshot current,
        WorkspaceScopePreparationDescriptor? preparing) =>
        new(current.Revision, current.PhysicalComposition, current.Packages, current.Closure, preparing);

    static WorkspacePackageOccurrenceDescriptor? FindRequestedOccurrence(
        WorkspaceScopeSnapshot snapshot,
        ArtifactRootCorrespondence? target)
    {
        if (target is null)
            return null;

        WorkspacePackageOccurrenceDescriptor? match = null;
        foreach (WorkspacePackageOccurrenceDescriptor package in snapshot.Packages)
        {
            if (!package.Occurrence.Correspondence.Equals(target))
                continue;
            if (match is not null)
            {
                throw new WorkspaceScopeInvariantException(
                    snapshot,
                    ArtifactRootFailure.CompositionMismatch);
            }
            match = package;
        }
        return match
            ?? throw new WorkspaceScopeInvariantException(
                snapshot,
                ArtifactRootFailure.CompositionMismatch);
    }

    abstract record ScopeRequestedPackage
    {
        private protected ScopeRequestedPackage() { }

        internal sealed record Retain(
            WorkspacePackageOccurrence Occurrence,
            ArtifactRootGenerationReference Generation) : ScopeRequestedPackage;

        internal sealed record Prepare(
            PackageRootBinding Binding,
            ArtifactRootCorrespondence Correspondence,
            WorkspacePackageOccurrence? ExistingOccurrence) : ScopeRequestedPackage;
    }

    sealed class ScopePreparation : IDisposable
    {
        internal ScopePreparation(
            InspectionWorkspaceIdentity workspace,
            WorkspaceScopeOperationAssociation association,
            ArtifactRootCorrespondence? requestedTarget,
            DateTimeOffset deadline,
            CancellationToken cancellation,
            WorkspaceScopeSnapshot initial)
        {
            Association = association;
            RequestedTarget = requestedTarget;
            Initial = initial;
            Stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            Authority = new(workspace, new(), deadline, Stop.Token);
            Completion = Started.Task.Unwrap();
        }

        internal WorkspaceScopeOperationAssociation Association { get; }
        internal WorkspaceScopePublicationOperationIdentity Identity =>
            Association.Operation;
        internal ArtifactRootCorrespondence? RequestedTarget { get; }
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
            ImmutableArray<WorkspacePackageOccurrenceDescriptor> rows;
            try
            {
                rows = ProjectScopePackages(revision, roots, owner._scopeSnapshot);
            }
            catch (WorkspaceScopeInvariantException failure)
            {
                return new ArtifactRootResult<WorkspaceScopePreparedCommit>.Rejected(failure.Failure);
            }
            var snapshot = new WorkspaceScopeSnapshot(revision, candidateComposition,
                rows, new(revision.Identity), null);
            var result = new ScopeCommittedResult(
                new(
                    preparation.Association,
                    snapshot,
                    preparation.Association.Kind,
                    FindRequestedOccurrence(
                        snapshot,
                        preparation.RequestedTarget)));
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
