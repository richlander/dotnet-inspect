using System.Collections.Immutable;

using ILInspector.Metadata;

namespace DotnetInspector.Queries;

public sealed partial class InspectionWorkspace
{
    internal async ValueTask<ArtifactRootPublicationOutcome> PublishArtifactRootCompositionAsync(
        ArtifactRootPublicationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArtifactRootFailure? failure;
        lock (_gate)
            failure = ValidateRootPlanShape(plan);
        if (failure is { } malformed)
            return new(null, malformed, []);

        var batches = new List<(ArtifactRootPreparationReceipt Receipt, RootPreparedBatch Batch)>();
        var retired = new List<RootLifetime>();
        ArtifactRootPublishedComposition? published = null;
        var cleanup = ImmutableArray.CreateBuilder<ArtifactRootFailure>();
        await _rootCompositionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                failure = ValidateRootPlanApplicability(plan, checkReceipts: true);
                foreach (ArtifactRootPreparationReceipt receipt in plan.Preparations)
                {
                    if (receipt.State != ArtifactRootPreparationState.Prepared)
                        continue;
                    RootPreparedBatch batch = _rootPreparations[receipt];
                    batches.Add((receipt, batch));
                    receipt.State = failure is null
                        ? ArtifactRootPreparationState.Publishing
                        : ArtifactRootPreparationState.Released;
                    if (failure is not null)
                        _rootPreparations.Remove(receipt);
                }
            }

            if (failure is null)
            {
                var staged = new Dictionary<ArtifactRootCorrespondence, RootCurrent>();
                var projections = ImmutableArray.CreateBuilder<ArtifactRootScopeProjection>(
                    plan.DesiredRoots.Length);
                foreach (ArtifactRootPublicationEntry entry in plan.DesiredRoots)
                {
                    RootCurrent current;
                    if (entry is ArtifactRootPublicationEntry.Retain retain)
                        current = _currentRoots[retain.Correspondence];
                    else
                    {
                        var adopt = (ArtifactRootPublicationEntry.Adopt)entry;
                        var pair = batches.Single(p => ReferenceEquals(p.Receipt.Preparation, adopt.Preparation));
                        int index = FindPreparedEntry(pair.Receipt, adopt.Entry);
                        RootLifetime root = pair.Batch.Roots[index];
                        current = new(root.Projection, root);
                    }
                    staged.Add(current.Projection.Correspondence, current);
                    projections.Add(current.Projection);
                }
                ImmutableArray<ArtifactRootScopeProjection> roots = projections.MoveToImmutable();
                var candidateComposition = new ArtifactRootCompositionGenerationIdentity();
                ArtifactRootResult<WorkspaceScopePreparedCommit> preparation =
                    plan.Participant.PrepareCommit(_rootComposition, candidateComposition, roots);
                if (preparation is ArtifactRootResult<WorkspaceScopePreparedCommit>.Rejected rejected)
                    failure = rejected.Failure;
                else
                {
                    WorkspaceScopePreparedCommit token =
                        ((ArtifactRootResult<WorkspaceScopePreparedCommit>.Available)preparation).Value;
                    var result = new ArtifactRootPublishedComposition(
                        candidateComposition, roots, token.Result);
                    foreach (RootCurrent current in _currentRoots.Values)
                    {
                        if (current.Lifetime is { } lifetime
                            && (!staged.TryGetValue(current.Projection.Correspondence, out RootCurrent? next)
                                || !ReferenceEquals(next.Lifetime, lifetime)))
                            retired.Add(lifetime);
                    }
                    lock (_gate)
                    {
                        failure = ValidateRootPlanApplicability(plan, checkReceipts: false);
                        if (failure is null)
                        {
                            // All construction and validation precede this no-yield pair swap.
                            token.Commit();
                            _currentRoots = staged;
                            _rootComposition = candidateComposition;
                            foreach (var pair in batches)
                                pair.Receipt.State = ArtifactRootPreparationState.Published;
                            published = result;
                        }
                    }
                }
                lock (_gate)
                {
                    foreach (var pair in batches)
                    {
                        _rootPreparations.Remove(pair.Receipt);
                        if (published is null)
                            pair.Receipt.State = ArtifactRootPreparationState.Released;
                    }
                }
            }

            foreach (var pair in batches)
            {
                if (published is null)
                    cleanup.AddRange(await ReleaseRootBatchAsync(pair.Receipt, pair.Batch).ConfigureAwait(false));
                else
                {
                    pair.Batch.MonitorEnd.TrySetResult();
                    pair.Receipt.Settlement.TrySetResult([]);
                }
            }
        }
        finally { _rootCompositionGate.Release(); }

        if (published is not null)
        {
            foreach (RootLifetime root in retired)
                _ = StartRootRetirement(root);
        }
        return new(published, failure, cleanup.ToImmutable());
    }

    ArtifactRootFailure? ValidateRootPlanShape(ArtifactRootPublicationPlan plan)
    {
        if (plan.Authority is null || plan.ExpectedComposition is null
            || plan.Participant is null || plan.DesiredRoots.IsDefault
            || plan.Preparations.IsDefault || !FiniteDeadline(plan.Deadline))
            return ArtifactRootFailure.Malformed;
        if (plan.Participant.ExpectedBase is null || plan.Participant.Operation is null
            || plan.Participant.CandidateSet is null || plan.Authority.CandidateSet is null)
            return ArtifactRootFailure.Malformed;
        if (!ReferenceEquals(plan.Workspace, _identity)
            || !ReferenceEquals(plan.Participant.Workspace, _identity))
            return ArtifactRootFailure.ForeignWorkspace;
        if (!ReferenceEquals(plan.Authority.CandidateSet, plan.Participant.CandidateSet))
            return ArtifactRootFailure.Malformed;

        var receipts = new Dictionary<ArtifactRootPreparationIdentity, ArtifactRootPreparationReceipt>();
        int expectedEntries = 0;
        foreach (ArtifactRootPreparationReceipt receipt in plan.Preparations)
        {
            if (receipt is null || !ReferenceEquals(receipt.Workspace, _identity))
                return ArtifactRootFailure.ForeignWorkspace;
            if (!_issuedRootPreparations.TryGetValue(receipt, out _)
                || !receipts.TryAdd(receipt.Preparation, receipt)
                || receipt.Deadline != plan.Deadline
                || !ReferenceEquals(receipt.Cancellation, plan.Authority.CancellationIdentity))
                return ArtifactRootFailure.Malformed;
            expectedEntries += receipt.Entries.Length;
        }

        var adopted = new HashSet<ArtifactRootPreparationEntryIdentity>();
        var correspondences = new HashSet<ArtifactRootCorrespondence>();
        foreach (ArtifactRootPublicationEntry entry in plan.DesiredRoots)
        {
            ArtifactRootCorrespondence correspondence;
            if (entry is ArtifactRootPublicationEntry.Retain retain)
            {
                if (retain.Correspondence is null || retain.Generation is null)
                    return ArtifactRootFailure.Malformed;
                correspondence = retain.Correspondence;
            }
            else if (entry is ArtifactRootPublicationEntry.Adopt adopt
                && adopt.Preparation is not null && adopt.Entry is not null
                && receipts.TryGetValue(adopt.Preparation, out ArtifactRootPreparationReceipt? receipt))
            {
                int index = FindPreparedEntry(receipt, adopt.Entry);
                if (index < 0 || !adopted.Add(adopt.Entry))
                    return ArtifactRootFailure.Malformed;
                correspondence = receipt.Entries[index].Correspondence;
            }
            else
                return ArtifactRootFailure.Malformed;
            if (!ReferenceEquals(correspondence.WorkspaceIdentity, _identity))
                return ArtifactRootFailure.ForeignWorkspace;
            if (!correspondences.Add(correspondence))
                return ArtifactRootFailure.Malformed;
        }
        return adopted.Count == expectedEntries ? null : ArtifactRootFailure.Malformed;
    }

    ArtifactRootFailure? ValidateRootPlanApplicability(
        ArtifactRootPublicationPlan plan, bool checkReceipts)
    {
        if (checkReceipts)
        {
            foreach (ArtifactRootPreparationReceipt receipt in plan.Preparations)
            {
                ArtifactRootFailure? failure = RootReceiptFailure(receipt);
                if (failure is not null) return failure;
            }
        }
        ArtifactRootFailure? unavailable = RootWorkspaceFailure(plan.Workspace)
            ?? RootCancellationFailure(plan.Authority);
        if (unavailable is not null) return unavailable;
        if (!ReferenceEquals(plan.ExpectedComposition, _rootComposition))
            return ArtifactRootFailure.CompositionMismatch;
        long bytes = 0;
        foreach (ArtifactRootPublicationEntry entry in plan.DesiredRoots)
        {
            if (entry is ArtifactRootPublicationEntry.Retain retain)
            {
                if (!CurrentRootGeneration(retain.Correspondence, retain.Generation, out RootLifetime? root))
                    return ArtifactRootFailure.ArtifactGenerationMismatch;
                bytes += root!.RetainedBytes;
            }
            else
            {
                var adopt = (ArtifactRootPublicationEntry.Adopt)entry;
                ArtifactRootPreparationReceipt receipt = plan.Preparations.First(
                    r => ReferenceEquals(r.Preparation, adopt.Preparation));
                bytes += _rootPreparations[receipt].Roots[
                    FindPreparedEntry(receipt, adopt.Entry)].RetainedBytes;
            }
        }
        return plan.DesiredRoots.Length > _rootLimits.MaxRoots
            || bytes > _rootLimits.MaxRetainedImageBytes
                ? ArtifactRootFailure.BudgetExceeded : null;
    }

    static int FindPreparedEntry(
        ArtifactRootPreparationReceipt receipt, ArtifactRootPreparationEntryIdentity entry)
    {
        for (int index = 0; index < receipt.Entries.Length; index++)
            if (ReferenceEquals(receipt.Entries[index].Entry, entry))
                return index;
        return -1;
    }

    static ArtifactRootFailure? RootReceiptFailure(ArtifactRootPreparationReceipt receipt) =>
        receipt.State switch
        {
            ArtifactRootPreparationState.Published => ArtifactRootFailure.PreparationAlreadyPublished,
            ArtifactRootPreparationState.Released => ArtifactRootFailure.PreparationReleased,
            ArtifactRootPreparationState.Publishing => ArtifactRootFailure.PreparationPublishing,
            _ => null,
        };

    bool CurrentRootGeneration(
        ArtifactRootCorrespondence correspondence,
        ArtifactRootGenerationReference generation,
        out RootLifetime? lifetime)
    {
        lifetime = null;
        if (_currentRoots.TryGetValue(correspondence, out RootCurrent? root)
            && root.Projection.Status is ArtifactRootRealizationStatus.Ready ready
            && ReferenceEquals(ready.Generation, generation))
        {
            lifetime = root.Lifetime;
            return lifetime is not null;
        }
        return false;
    }

    internal async ValueTask<ArtifactRootResult<ArtifactRootQueryLease>> EnterArtifactRootQueryAsync(
        InspectionWorkspaceIdentity workspace,
        ArtifactRootCorrespondence correspondence,
        ArtifactRootGenerationReference generation,
        AssemblyBindingPolicyVersion? expectedPolicy = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(correspondence);
        ArgumentNullException.ThrowIfNull(generation);
        await _rootCompositionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ArtifactRootFailure? failure = RootWorkspaceFailure(workspace);
                if (failure is not null)
                    return new ArtifactRootResult<ArtifactRootQueryLease>.Rejected(failure.Value);
                if (!CurrentRootGeneration(correspondence, generation, out RootLifetime? root))
                    return new ArtifactRootResult<ArtifactRootQueryLease>.Rejected(
                        ArtifactRootFailure.ArtifactGenerationMismatch);
                if (expectedPolicy is not null && (!root!.Resources.Realization.HasAssemblyContexts
                    || !ReferenceEquals(expectedPolicy, root.Resources.Realization.SurfaceGroup.BindingPolicyVersion)))
                    return new ArtifactRootResult<ArtifactRootQueryLease>.Rejected(
                        ArtifactRootFailure.BindingPolicyMismatch);
                return new ArtifactRootResult<ArtifactRootQueryLease>.Available(root!.Enter());
            }
        }
        finally { _rootCompositionGate.Release(); }
    }

    internal async ValueTask<ArtifactRootResult<ArtifactRootCompositionGenerationIdentity>>
        RetireArtifactRootAsync(
            ArtifactRootCorrespondence correspondence,
            ArtifactRootGenerationReference generation)
    {
        RootLifetime? retired = null;
        await _rootCompositionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                ArtifactRootFailure? failure = RootWorkspaceFailure(correspondence.WorkspaceIdentity);
                if (failure is { } unavailable)
                    return new ArtifactRootResult<ArtifactRootCompositionGenerationIdentity>.Rejected(unavailable);
                if (!CurrentRootGeneration(correspondence, generation, out retired))
                    return new ArtifactRootResult<ArtifactRootCompositionGenerationIdentity>.Rejected(
                        ArtifactRootFailure.ArtifactGenerationMismatch);
                _currentRoots[correspondence] = new(
                    new(correspondence, new ArtifactRootRealizationStatus.Pending()), null);
                _rootComposition = new();
                return new ArtifactRootResult<ArtifactRootCompositionGenerationIdentity>.Available(_rootComposition);
            }
        }
        finally
        {
            _rootCompositionGate.Release();
            if (retired is not null)
                _ = StartRootRetirement(retired);
        }
    }

    internal async ValueTask<ArtifactRootResult<ArtifactRootCompositionGenerationIdentity>>
        FailArtifactRootReplacementAsync(
            ArtifactRootCorrespondence correspondence,
            ArtifactRootCompositionGenerationIdentity expectedComposition,
            ArtifactRootFailure failure)
    {
        await _rootCompositionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                ArtifactRootFailure? unavailable = RootWorkspaceFailure(correspondence.WorkspaceIdentity);
                if (unavailable is not null)
                    return new ArtifactRootResult<ArtifactRootCompositionGenerationIdentity>.Rejected(unavailable.Value);
                if (!ReferenceEquals(_rootComposition, expectedComposition))
                    return new ArtifactRootResult<ArtifactRootCompositionGenerationIdentity>.Rejected(
                        ArtifactRootFailure.CompositionMismatch);
                if (!_currentRoots.TryGetValue(correspondence, out RootCurrent? root)
                    || root.Projection.Status is not ArtifactRootRealizationStatus.Pending)
                    return new ArtifactRootResult<ArtifactRootCompositionGenerationIdentity>.Rejected(
                        ArtifactRootFailure.ArtifactGenerationMismatch);
                _currentRoots[correspondence] = new(
                    new(correspondence, new ArtifactRootRealizationStatus.Failed(failure)), null);
                _rootComposition = new();
                return new ArtifactRootResult<ArtifactRootCompositionGenerationIdentity>.Available(_rootComposition);
            }
        }
        finally { _rootCompositionGate.Release(); }
    }

    internal async ValueTask<ArtifactRootResult<ArtifactRootReplacementSettlement>>
        SettleArtifactRootReplacementAsync(
            ArtifactRootPreparationAuthority authority,
            ArtifactRootPreparationReceipt receipt,
            ArtifactRootCompositionGenerationIdentity expectedComposition)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(expectedComposition);
        lock (_gate)
        {
            if (!ReferenceEquals(authority.Workspace, _identity)
                || !ReferenceEquals(receipt.Workspace, _identity))
                return new ArtifactRootResult<ArtifactRootReplacementSettlement>.Rejected(
                    ArtifactRootFailure.ForeignWorkspace);
            if (!_issuedRootPreparations.TryGetValue(receipt, out _)
                || receipt.Entries.Length != 1 || !FiniteDeadline(authority.Deadline)
                || receipt.Deadline != authority.Deadline
                || !ReferenceEquals(receipt.Cancellation, authority.CancellationIdentity))
                return new ArtifactRootResult<ArtifactRootReplacementSettlement>.Rejected(
                    ArtifactRootFailure.Malformed);
        }
        await _rootCompositionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            ArtifactRootFailure? failure;
            RootPreparedBatch? batch = null;
            ArtifactRootReplacementSettlement? result = null;
            lock (_gate)
            {
                failure = RootReceiptFailure(receipt)
                    ?? RootWorkspaceFailure(authority.Workspace)
                    ?? RootCancellationFailure(authority);
                if (failure is null && !ReferenceEquals(_rootComposition, expectedComposition))
                    failure = ArtifactRootFailure.CompositionMismatch;
                ArtifactRootCorrespondence correspondence = receipt.Entries[0].Correspondence;
                if (failure is null && (!_currentRoots.TryGetValue(correspondence, out RootCurrent? current)
                    || current.Projection.Status is ArtifactRootRealizationStatus.Ready))
                    failure = ArtifactRootFailure.ArtifactGenerationMismatch;
                if (receipt.State == ArtifactRootPreparationState.Prepared)
                {
                    batch = _rootPreparations[receipt];
                    if (failure is null)
                    {
                        receipt.State = ArtifactRootPreparationState.Publishing;
                        RootLifetime root = batch.Roots[0];
                        var composition = new ArtifactRootCompositionGenerationIdentity();
                        result = new(composition, root.Projection);
                        var replacement = new RootCurrent(root.Projection, root);
                        failure = RootCancellationFailure(authority);
                        if (failure is null)
                        {
                            _currentRoots[correspondence] = replacement;
                            _rootComposition = composition;
                            receipt.State = ArtifactRootPreparationState.Published;
                        }
                    }
                    _rootPreparations.Remove(receipt);
                    if (failure is not null)
                        receipt.State = ArtifactRootPreparationState.Released;
                }
            }
            if (batch is not null)
            {
                if (failure is not null)
                    await ReleaseRootBatchAsync(receipt, batch).ConfigureAwait(false);
                else
                {
                    batch.MonitorEnd.TrySetResult();
                    receipt.Settlement.TrySetResult([]);
                }
            }
            return failure is { } rejected
                ? new ArtifactRootResult<ArtifactRootReplacementSettlement>.Rejected(rejected)
                : new ArtifactRootResult<ArtifactRootReplacementSettlement>.Available(result!);
        }
        finally { _rootCompositionGate.Release(); }
    }

    async Task<ImmutableArray<Exception>> CloseArtifactRootsAsync()
    {
        await _rootClose.CancelAsync().ConfigureAwait(false);
        Task[] constructions;
        lock (_gate) constructions = [.. _rootConstructions];
        await Task.WhenAll(constructions).ConfigureAwait(false);
        await _rootCompositionGate.WaitAsync().ConfigureAwait(false);
        RootLifetime[] roots;
        try
        {
            ArtifactRootPreparationReceipt[] receipts;
            lock (_gate) receipts = [.. _rootPreparations.Keys];
            foreach (ArtifactRootPreparationReceipt receipt in receipts)
                await ReleaseArtifactRootPreparationAsync(receipt).ConfigureAwait(false);
            lock (_gate)
            {
                _currentRoots = [];
                _rootComposition = new();
                roots = [.. _rootLifetimes];
            }
        }
        finally { _rootCompositionGate.Release(); }
        foreach (RootLifetime root in roots)
            _ = StartRootRetirement(root);
        foreach (RootLifetime root in roots)
            await root.Released.Task.ConfigureAwait(false);
        lock (_gate) return [.. _rootCleanupFailures];
    }

    /// <summary>
    /// Scope holds this same asynchronous exclusion lease while reading or
    /// preparing its own current pointer. It must release it before publishing.
    /// </summary>
    internal async ValueTask<ArtifactRootResult<ArtifactRootCompositionReadLease>>
        ReadArtifactRootCompositionAsync(InspectionWorkspaceIdentity workspace)
    {
        await _rootCompositionGate.WaitAsync().ConfigureAwait(false);
        bool transferred = false;
        try
        {
            lock (_gate)
            {
                ArtifactRootFailure? failure = RootWorkspaceFailure(workspace);
                if (failure is { } rejected)
                    return new ArtifactRootResult<ArtifactRootCompositionReadLease>.Rejected(rejected);
                var lease = new ArtifactRootCompositionReadLease(
                    _rootCompositionGate, _rootComposition,
                    [.. _currentRoots.Values.Select(root => root.Projection)]);
                transferred = true;
                return new ArtifactRootResult<ArtifactRootCompositionReadLease>.Available(lease);
            }
        }
        finally
        {
            if (!transferred)
                _rootCompositionGate.Release();
        }
    }

    internal sealed class ArtifactRootCompositionReadLease(
        SemaphoreSlim gate,
        ArtifactRootCompositionGenerationIdentity composition,
        ImmutableArray<ArtifactRootScopeProjection> roots) : IDisposable
    {
        SemaphoreSlim? _gate = gate;
        internal ArtifactRootCompositionGenerationIdentity Composition { get; } = composition;
        internal ImmutableArray<ArtifactRootScopeProjection> Roots { get; } = roots;
        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }
}
