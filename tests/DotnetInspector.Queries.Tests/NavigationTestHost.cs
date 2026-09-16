namespace DotnetInspector.Queries.Tests;

/// <summary>Host-owned scheduling around the product's explicit current-state slot.</summary>
internal sealed class NavigationTestHost
{
    readonly object _gate = new();
    readonly ViewFacetRegistry _registry;
    readonly Func<NavigationEvaluationRequest, CancellationToken, ValueTask<NavigationPreparation>> _prepare;
    readonly Dictionary<NavigationRequest, Pending> _pending = [];
    NavigationState _state;

    internal NavigationTestHost(
        InspectionWorkspaceIdentity workspace,
        NavigationEvaluationFacts facts,
        ViewFacetRegistry registry,
        Func<NavigationEvaluationRequest, CancellationToken, ValueTask<NavigationPreparation>> prepare)
    {
        _registry = registry;
        _prepare = prepare;
        NavigationOperationInitialization initialized = NavigationTransitions.Initialize(workspace, facts, registry);
        _state = initialized.State;
        Initialization = initialized.Result.Consumer;
    }

    internal NavigationConsumerResult Initialization { get; }
    internal NavigationState State
    {
        get
        {
            lock (_gate)
                return _state;
        }
    }
    internal NavigationConsumerSnapshot Snapshot => State.Snapshot;
    internal NavigationWorkspaceSnapshot InstalledSnapshot => State.InstalledSnapshot;

    internal async ValueTask<NavigationConsumerResult> ExecuteAsync(
        NavigationAction action, CancellationToken cancellationToken) =>
        (await ExecuteOperationAsync(action, cancellationToken)).Consumer;

    internal ValueTask<NavigationOperationResult> ExecuteOperationAsync(
        NavigationAction action, CancellationToken cancellationToken)
    {
        NavigationTransition transition;
        lock (_gate)
            transition = Commit(NavigationTransitions.Begin(_state, action));
        return FinishExplicitAsync(transition, cancellationToken);
    }

    internal NavigationActionPublicationResult
        PublishRetainedTypeAction(
            StructuralSubjectIdentity.TypeSubject subject,
            NavigationPublication? publication = null)
    {
        lock (_gate)
        {
            NavigationTransition transition = Commit(
                NavigationTransitions.PublishRetainedTypeAction(
                    _state,
                    publication ?? _state.Publication,
                    subject));
            return transition.ActionPublication!;
        }
    }

    internal async ValueTask<NavigationConsumerResult> ActivateLensAsync(
        NavigationLensIdentity lens, CancellationToken cancellationToken) =>
        (await ActivateLensOperationAsync(lens, cancellationToken)).Consumer;

    internal ValueTask<NavigationOperationResult> ActivateLensOperationAsync(
        NavigationLensIdentity lens, CancellationToken cancellationToken)
    {
        NavigationTransition transition;
        lock (_gate)
            transition = Commit(NavigationTransitions.BeginLens(_state, lens));
        return FinishExplicitAsync(transition, cancellationToken);
    }

    async ValueTask<NavigationOperationResult> FinishExplicitAsync(
        NavigationTransition beginning, CancellationToken cancellationToken)
    {
        if (beginning.Result is { } immediate)
            return immediate;
        NavigationEvaluationRequest work = beginning.Work!;
        try
        {
            NavigationPreparation preparation;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                preparation = await _prepare(work, cancellationToken).AsTask().WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                preparation = new NavigationPreparation.Aborted("The host prerequisite was cancelled.");
            }
            NavigationEvaluationResult evaluation = NavigationTransitions.Evaluate(work, preparation, _registry);
            NavigationTransition completion;
            lock (_gate)
                completion = Commit(NavigationTransitions.Complete(_state, work, evaluation));
            Pump();
            return completion.Result
                ?? throw new InvalidOperationException($"Explicit completion rejected: {completion.Rejection}");
        }
        catch
        {
            lock (_gate)
                Commit(NavigationTransitions.Cancel(_state, work.Identity));
            Pump();
            throw;
        }
    }

    internal async ValueTask<NavigationConsumerResult> RefreshAsync(CancellationToken cancellationToken) =>
        (await QueueAsync(maintenance: true, cancellationToken)).Consumer;

    internal async ValueTask<NavigationConsumerResult> SynchronizeAsync(CancellationToken cancellationToken) =>
        (await QueueAsync(maintenance: false, cancellationToken)).Consumer;

    async ValueTask<NavigationOperationResult> QueueAsync(bool maintenance, CancellationToken cancellationToken)
    {
        NavigationRequest request;
        var pending = new Pending(cancellationToken);
        lock (_gate)
        {
            NavigationTransition queued = Commit(maintenance
                ? NavigationTransitions.QueueMaintenance(_state)
                : NavigationTransitions.QueueSynchronization(_state));
            request = queued.Request!;
            _pending.Add(request, pending);
        }
        using CancellationTokenRegistration registration = cancellationToken.Register(() => CancelPending(request));
        Pump();
        return await pending.Completion.Task;
    }

    void Pump()
    {
        NavigationEvaluationRequest? work;
        Pending? pending;
        lock (_gate)
        {
            NavigationTransition transition = Commit(NavigationTransitions.Advance(_state));
            if (transition.Result is { } result)
            {
                _pending.Remove(transition.Request!, out Pending? completed);
                completed!.Completion.SetResult(result);
            }
            work = transition.Work;
            pending = work is null ? null : _pending[work.Identity];
        }
        if (work is not null)
            _ = EvaluateQueuedAsync(work, pending!);
    }

    async Task EvaluateQueuedAsync(NavigationEvaluationRequest work, Pending pending)
    {
        try
        {
            pending.CancellationToken.ThrowIfCancellationRequested();
            NavigationPreparation preparation = await _prepare(work, pending.CancellationToken)
                .AsTask().WaitAsync(pending.CancellationToken);
            NavigationEvaluationResult evaluation = NavigationTransitions.Evaluate(work, preparation, _registry);
            lock (_gate)
            {
                if (!_pending.ContainsKey(work.Identity))
                    return;
                NavigationTransition completion = Commit(NavigationTransitions.Complete(_state, work, evaluation));
                if (completion.Rejection is { } rejection)
                    throw new InvalidOperationException($"Maintenance completion rejected: {rejection}");
                if (completion.Result is { } result)
                {
                    _pending.Remove(work.Identity);
                    pending.Completion.SetResult(result);
                }
            }
            Pump();
        }
        catch (OperationCanceledException) when (pending.CancellationToken.IsCancellationRequested)
        {
            CancelPending(work.Identity);
        }
        catch (Exception exception)
        {
            lock (_gate)
            {
                if (_pending.Remove(work.Identity))
                {
                    Commit(NavigationTransitions.Cancel(_state, work.Identity));
                    pending.Completion.SetException(exception);
                }
            }
            Pump();
        }
    }

    void CancelPending(NavigationRequest request)
    {
        lock (_gate)
        {
            if (_pending.Remove(request, out Pending? pending))
            {
                Commit(NavigationTransitions.Cancel(_state, request));
                pending.Completion.SetCanceled(pending.CancellationToken);
            }
        }
        Pump();
    }

    internal bool ValidateAuthority(NavigationEffectAuthority? authority)
    {
        lock (_gate)
            return NavigationTransitions.ValidateAuthority(_state, authority);
    }

    internal NavigationAuthorityResult RecordConsumerInstallation(NavigationEffectAuthority authority) =>
        ApplyAuthority(state => NavigationTransitions.RecordConsumerInstallation(state, authority));

    internal NavigationAuthorityResult Acknowledge(NavigationEffectAuthority authority) =>
        ApplyAuthority(state => NavigationTransitions.Acknowledge(state, authority));

    internal NavigationAuthorityResult Abandon(NavigationEffectAuthority authority) =>
        ApplyAuthority(state => NavigationTransitions.Abandon(state, authority));

    NavigationAuthorityResult ApplyAuthority(Func<NavigationState, NavigationTransition> transition)
    {
        NavigationAuthorityResult result;
        lock (_gate)
            result = Commit(transition(_state)).AuthorityResult!.Value;
        Pump();
        return result;
    }

    NavigationTransition Commit(NavigationTransition transition)
    {
        if (!NavigationTransitions.CanCommit(_state, transition))
            throw new InvalidOperationException("The host current-state slot changed before commit.");
        _state = transition.State;
        return transition;
    }

    sealed class Pending(CancellationToken cancellationToken)
    {
        internal CancellationToken CancellationToken { get; } = cancellationToken;
        internal TaskCompletionSource<NavigationOperationResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
