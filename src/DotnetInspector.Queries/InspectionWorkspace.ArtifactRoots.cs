using System.Collections.Immutable;
using System.Runtime.CompilerServices;

using DotnetInspector.Artifacts;
using DotnetInspector.Packages;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

public sealed partial class InspectionWorkspace
{
    readonly SemaphoreSlim _rootCompositionGate = new(1, 1);
    readonly CancellationTokenSource _rootClose = new();
    readonly ConditionalWeakTable<ArtifactRootPreparationReceipt, object>
        _issuedRootPreparations = new();
    readonly Dictionary<ArtifactRootPreparationReceipt, RootPreparedBatch>
        _rootPreparations = [];
    readonly HashSet<RootLifetime> _rootLifetimes = [];
    readonly HashSet<Task> _rootConstructions = [];
    readonly List<Exception> _rootCleanupFailures = [];
    Dictionary<ArtifactRootCorrespondence, RootCurrent> _currentRoots = [];
    ArtifactRootCompositionGenerationIdentity _rootComposition = new();
    ArtifactRootAdmissionLimits _rootLimits = new();
    long _rootReservedBytes;
    int _rootReservedCount;
    TimeProvider _rootTime = TimeProvider.System;

    internal void ConfigureArtifactRootAdmission(
        ArtifactRootAdmissionLimits limits,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentOutOfRangeException.ThrowIfNegative(limits.MaxRoots);
        ArgumentOutOfRangeException.ThrowIfNegative(limits.MaxRetainedImageBytes);
        lock (_gate)
        {
            if (_state != InspectionWorkspaceState.Open
                || _rootReservedCount != 0 || _currentRoots.Count != 0)
                throw new InvalidOperationException(
                    "Root admission must be configured before preparation.");
            _rootLimits = limits;
            _rootTime = timeProvider ?? TimeProvider.System;
        }
    }

    public async ValueTask<ArtifactRootResult<ArtifactRootCompositionGenerationIdentity>>
        GetCurrentArtifactRootCompositionGenerationAsync(
            InspectionWorkspaceIdentity workspace)
    {
        await _rootCompositionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                ArtifactRootFailure? failure = RootWorkspaceFailure(workspace);
                return failure is { } rejected
                    ? new ArtifactRootResult<ArtifactRootCompositionGenerationIdentity>.Rejected(rejected)
                    : new ArtifactRootResult<ArtifactRootCompositionGenerationIdentity>.Available(_rootComposition);
            }
        }
        finally { _rootCompositionGate.Release(); }
    }

    public async ValueTask<ArtifactRootResult<ArtifactRootScopeProjection>>
        GetCurrentRootScopeProjectionAsync(
            InspectionWorkspaceIdentity workspace,
            ArtifactRootCorrespondence correspondence)
    {
        ArgumentNullException.ThrowIfNull(correspondence);
        await _rootCompositionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                ArtifactRootFailure? failure = RootWorkspaceFailure(workspace);
                if (failure is null && !ReferenceEquals(correspondence.WorkspaceIdentity, _identity))
                    failure = ArtifactRootFailure.ForeignWorkspace;
                if (failure is { } rejected)
                    return new ArtifactRootResult<ArtifactRootScopeProjection>.Rejected(rejected);
                return _currentRoots.TryGetValue(correspondence, out RootCurrent? root)
                    ? new ArtifactRootResult<ArtifactRootScopeProjection>.Available(root.Projection)
                    : new ArtifactRootResult<ArtifactRootScopeProjection>.Rejected(ArtifactRootFailure.Absent);
            }
        }
        finally { _rootCompositionGate.Release(); }
    }

    internal async ValueTask<ArtifactRootResult<ArtifactRootPreparationReceipt>>
        PreparePackageArtifactRootsAsync(
            ArtifactRootPreparationAuthority authority,
            ImmutableArray<PackageRootBinding> packages,
            PackageAssemblyContextRealizationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(authority);
        options ??= new PackageAssemblyContextRealizationOptions();
        options.Validate();
        if (_lifetimeMode != InspectionWorkspaceLifetimeMode.Asynchronous)
            throw new InvalidOperationException("Root preparation requires an asynchronous Workspace.");
        if (authority.CandidateSet is null
            || packages.IsDefaultOrEmpty || packages.Any(static p => p is null)
            || !FiniteDeadline(authority.Deadline))
            return PreparationRejected(ArtifactRootFailure.Malformed);

        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        long bytes;
        lock (_gate)
        {
            ArtifactRootFailure? failure = RootWorkspaceFailure(authority.Workspace)
                ?? RootCancellationFailure(authority);
            if (failure is { } rejected)
                return PreparationRejected(rejected);
            if (packages.Length > _rootLimits.MaxRoots - _rootReservedCount)
                return PreparationRejected(ArtifactRootFailure.BudgetExceeded);
            long available = _rootLimits.MaxRetainedImageBytes - _rootReservedBytes;
            bytes = options.MaxAggregateRetainedImageBytes <= available / packages.Length
                ? packages.Length * options.MaxAggregateRetainedImageBytes
                : available;
            _rootReservedBytes += bytes;
            _rootReservedCount += packages.Length;
            _rootConstructions.Add(completion.Task);
        }

        var roots = new List<RootLifetime>(packages.Length);
        bool transferred = false;
        using var end = CancellationTokenSource.CreateLinkedTokenSource(
            authority.Cancellation, _rootClose.Token);
        using var deadlineEnd = new CancellationTokenSource();
        Task deadline = CancelAtRootDeadlineAsync(authority.Deadline, end, deadlineEnd.Token);
        try
        {
            long retainedBytes = 0;
            var entries = ImmutableArray.CreateBuilder<ArtifactRootPreparedEntry>(packages.Length);
            var correspondences = new HashSet<ArtifactRootCorrespondence>();
            foreach (PackageRootBinding package in packages)
            {
                end.Token.ThrowIfCancellationRequested();
                var correspondence = new PackageArtifactRootCorrespondence(
                    _identity, PackageArtifactRootRequest.From(package));
                if (!correspondences.Add(correspondence))
                    return PreparationRejected(ArtifactRootFailure.Malformed);
                if (package.Root.AssetSelection.Status is
                    PackageCompileAssetSelectionStatus.NoMatchingTargetFramework or
                    PackageCompileAssetSelectionStatus.InvalidImplementationAssets)
                    return PreparationRejected(ArtifactRootFailure.PreparationFailed);

                // Construction is sequential: earlier Roots spend actual bytes
                // from the batch envelope, never another whole per-Root ceiling.
                long rootBudget = Math.Min(
                    options.MaxAggregateRetainedImageBytes, bytes - retainedBytes);
                if (package.Root.AssetSelection.IsSelected && rootBudget < 2)
                    return PreparationRejected(ArtifactRootFailure.BudgetExceeded);
                PackageAssemblyContextRealizationOptions rootOptions = options with
                {
                    MaxAggregateRetainedImageBytes = rootBudget,
                };
                ArtifactPackageRootResources resources =
                    await ConstructPackageArtifactRootAsync(
                        package, rootOptions, provisional: true, end.Token)
                        .ConfigureAwait(false);
                var projection = new ArtifactRootScopeProjection(
                    correspondence,
                    new ArtifactRootRealizationStatus.Ready(new()));
                var root = new RootLifetime(resources, projection);
                roots.Add(root);
                foreach (AssemblyContextGroup group in DependentGroups(resources.Realization))
                {
                    foreach (AssemblyContextParticipant participant in group.Participants)
                    {
                        end.Token.ThrowIfCancellationRequested();
                        if (group.UseAssemblyImage(participant.Assembly, static _ => true)
                            is AssemblyImageAccessResult<bool>.Rejected)
                            return PreparationRejected(ArtifactRootFailure.PreparationFailed);
                    }
                }
                root.RetainedBytes = resources.CountRetainedImageBytes();
                if (root.RetainedBytes > rootBudget)
                    return PreparationRejected(ArtifactRootFailure.BudgetExceeded);
                retainedBytes += root.RetainedBytes;
                entries.Add(new(new(), correspondence));
            }

            var receipt = new ArtifactRootPreparationReceipt(
                _identity, new(), authority.Deadline,
                authority.CancellationIdentity, entries.MoveToImmutable());
            var batch = new RootPreparedBatch([.. roots]);
            lock (_gate)
            {
                ArtifactRootFailure? failure = RootWorkspaceFailure(authority.Workspace)
                    ?? RootCancellationFailure(authority);
                if (failure is { } rejected)
                    return PreparationRejected(rejected);
                _issuedRootPreparations.Add(receipt, new object());
                _rootPreparations.Add(receipt, batch);
                foreach (RootLifetime root in roots)
                    _rootLifetimes.Add(root);
                _rootReservedBytes -= bytes - retainedBytes;
                transferred = true;
            }
            _ = ObserveRootPreparationAsync(
                receipt, authority.Cancellation, batch.MonitorEnd.Task);
            return new ArtifactRootResult<ArtifactRootPreparationReceipt>.Available(receipt);
        }
        catch (OperationCanceledException) when (end.IsCancellationRequested)
        {
            lock (_gate)
                return PreparationRejected(RootWorkspaceFailure(authority.Workspace)
                    ?? RootCancellationFailure(authority) ?? ArtifactRootFailure.Cancelled);
        }
        catch (Exception failure) when (failure is IOException or InvalidOperationException
            or ArgumentException or BadImageFormatException)
        {
            if (failure.Data["DotnetInspector.Artifacts.Workspaces.CleanupFailures"]
                is IReadOnlyList<Exception> cleanup)
            {
                lock (_gate) _rootCleanupFailures.AddRange(cleanup);
            }
            return PreparationRejected(ArtifactRootFailure.PreparationFailed);
        }
        finally
        {
            await deadlineEnd.CancelAsync().ConfigureAwait(false);
            await deadline.ConfigureAwait(false);
            if (!transferred)
            {
                foreach (RootLifetime root in roots)
                {
                    ImmutableArray<Exception> failures =
                        await root.Resources.ReleaseAsync().ConfigureAwait(false);
                    lock (_gate) _rootCleanupFailures.AddRange(failures);
                }
                lock (_gate)
                {
                    _rootReservedBytes -= bytes;
                    _rootReservedCount -= packages.Length;
                }
            }
            lock (_gate) _rootConstructions.Remove(completion.Task);
            completion.SetResult();
        }
    }

    internal async ValueTask<ArtifactRootReleaseOutcome> ReleaseArtifactRootPreparationAsync(
        ArtifactRootPreparationReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        RootPreparedBatch? batch;
        lock (_gate)
        {
            if (!ReferenceEquals(receipt.Workspace, _identity))
                return ArtifactRootReleaseOutcome.ForeignWorkspace;
            if (!_issuedRootPreparations.TryGetValue(receipt, out _))
                return ArtifactRootReleaseOutcome.UnknownPreparation;
            if (receipt.State == ArtifactRootPreparationState.Publishing)
                return ArtifactRootReleaseOutcome.PreparationPublishing;
            if (receipt.State == ArtifactRootPreparationState.Published)
                return ArtifactRootReleaseOutcome.PreparationAlreadyPublished;
            if (receipt.State == ArtifactRootPreparationState.Released)
                batch = null;
            else
            {
                batch = _rootPreparations[receipt];
                _rootPreparations.Remove(receipt);
                receipt.State = ArtifactRootPreparationState.Released;
            }
        }
        if (batch is null)
        {
            await receipt.Settlement.Task.ConfigureAwait(false);
            return ArtifactRootReleaseOutcome.NoEffect;
        }
        await ReleaseRootBatchAsync(receipt, batch).ConfigureAwait(false);
        return ArtifactRootReleaseOutcome.Released;
    }

    async Task ObserveRootPreparationAsync(
        ArtifactRootPreparationReceipt receipt,
        CancellationToken cancellation,
        Task monitorEnd)
    {
        using var end = CancellationTokenSource.CreateLinkedTokenSource(
            cancellation, _rootClose.Token);
        Task deadline = WaitForRootDeadlineAsync(receipt.Deadline, end.Token);
        await Task.WhenAny(deadline, monitorEnd).ConfigureAwait(false);
        await end.CancelAsync().ConfigureAwait(false);
        await deadline.ConfigureAwait(false);
        if (!monitorEnd.IsCompleted)
            await ReleaseArtifactRootPreparationAsync(receipt).ConfigureAwait(false);
    }

    async Task CancelAtRootDeadlineAsync(
        DateTimeOffset deadline, CancellationTokenSource target, CancellationToken stop)
    {
        await WaitForRootDeadlineAsync(deadline, stop).ConfigureAwait(false);
        if (!stop.IsCancellationRequested)
            await target.CancelAsync().ConfigureAwait(false);
    }

    async Task WaitForRootDeadlineAsync(DateTimeOffset deadline, CancellationToken stop)
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                TimeSpan remaining = deadline - _rootTime.GetUtcNow();
                if (remaining <= TimeSpan.Zero)
                    return;
                await Task.Delay(
                    remaining > TimeSpan.FromDays(1) ? TimeSpan.FromDays(1) : remaining,
                    _rootTime, stop).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }

    async ValueTask<ImmutableArray<ArtifactRootFailure>> ReleaseRootBatchAsync(
        ArtifactRootPreparationReceipt receipt, RootPreparedBatch batch)
    {
        batch.MonitorEnd.TrySetResult();
        var failures = ImmutableArray.CreateBuilder<ArtifactRootFailure>();
        foreach (RootLifetime root in batch.Roots)
        {
            ImmutableArray<Exception> cleanup = await StartRootRetirement(root).ConfigureAwait(false);
            foreach (Exception _ in cleanup)
                failures.Add(ArtifactRootFailure.PreparationFailed);
        }
        ImmutableArray<ArtifactRootFailure> result = failures.ToImmutable();
        receipt.Settlement.TrySetResult(result);
        return result;
    }

    async Task<ImmutableArray<Exception>> ReleaseRootLifetimeAsync(RootLifetime root)
    {
        await root.Quiescence.Task.ConfigureAwait(false);
        ImmutableArray<Exception> failures =
            await root.Resources.ReleaseAsync().ConfigureAwait(false);
        lock (_gate)
        {
            if (_rootLifetimes.Remove(root))
            {
                _rootReservedBytes -= root.RetainedBytes;
                _rootReservedCount--;
                _rootCleanupFailures.AddRange(failures);
            }
        }
        root.Released.TrySetResult(failures);
        return failures;
    }

    Task<ImmutableArray<Exception>> StartRootRetirement(RootLifetime root)
    {
        root.Retire();
        lock (_gate)
        {
            if (!root.ReleaseStarted)
            {
                root.ReleaseStarted = true;
                _ = ReleaseRootLifetimeAsync(root);
            }
        }
        return root.Released.Task;
    }

    ArtifactRootFailure? RootWorkspaceFailure(InspectionWorkspaceIdentity workspace) =>
        !ReferenceEquals(workspace, _identity) ? ArtifactRootFailure.ForeignWorkspace
        : _state == InspectionWorkspaceState.Closing ? ArtifactRootFailure.WorkspaceClosing
        : _state == InspectionWorkspaceState.Closed ? ArtifactRootFailure.WorkspaceClosed
        : null;

    ArtifactRootFailure? RootCancellationFailure(ArtifactRootPreparationAuthority authority) =>
        authority.Cancellation.IsCancellationRequested ? ArtifactRootFailure.Cancelled
        : _rootTime.GetUtcNow() >= authority.Deadline ? ArtifactRootFailure.DeadlineExpired
        : null;

    static bool FiniteDeadline(DateTimeOffset deadline) =>
        deadline != DateTimeOffset.MinValue && deadline != DateTimeOffset.MaxValue;

    static ArtifactRootResult<ArtifactRootPreparationReceipt> PreparationRejected(ArtifactRootFailure failure) =>
        new ArtifactRootResult<ArtifactRootPreparationReceipt>.Rejected(failure);

    sealed record RootPreparedBatch(ImmutableArray<RootLifetime> Roots)
    {
        internal TaskCompletionSource MonitorEnd { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    sealed record RootCurrent(ArtifactRootScopeProjection Projection, RootLifetime? Lifetime);

    internal sealed class RootLifetime(
        ArtifactPackageRootResources resources,
        ArtifactRootScopeProjection projection)
    {
        readonly object _gate = new();
        int _queries;
        bool _retired;
        internal ArtifactPackageRootResources Resources { get; } = resources;
        internal ArtifactRootScopeProjection Projection { get; } = projection;
        internal long RetainedBytes { get; set; }
        internal bool ReleaseStarted { get; set; }
        internal TaskCompletionSource Quiescence { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<ImmutableArray<Exception>> Released { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal ArtifactRootQueryLease Enter()
        {
            lock (_gate)
            {
                if (_retired)
                    throw new InvalidOperationException("A retired Root cannot admit a query.");
                ArtifactQueryLease? lease = Resources.IssueQueryLease();
                _queries++;
                return new ArtifactRootQueryLease(this, lease);
            }
        }

        internal void Retire()
        {
            lock (_gate)
            {
                _retired = true;
                if (_queries == 0)
                    Quiescence.TrySetResult();
            }
        }

        internal void Exit()
        {
            lock (_gate)
            {
                _queries--;
                if (_retired && _queries == 0)
                    Quiescence.TrySetResult();
            }
        }
    }

    internal sealed class ArtifactRootQueryLease : IDisposable
    {
        RootLifetime? _root;
        ArtifactQueryLease? _lease;

        internal ArtifactRootQueryLease(RootLifetime root, ArtifactQueryLease? lease)
        {
            _root = root;
            _lease = lease;
        }

        internal PackageAssemblyContextRealization Realization =>
            (_root ?? throw new ObjectDisposedException(nameof(ArtifactRootQueryLease)))
                .Resources.Realization;

        public void Dispose()
        {
            RootLifetime? root = Interlocked.Exchange(ref _root, null);
            if (root is null) return;
            Interlocked.Exchange(ref _lease, null)?.Dispose();
            root.Exit();
        }
    }
}
