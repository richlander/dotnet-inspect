using System.Runtime.ExceptionServices;
using System.Runtime.Versioning;
using DotnetInspector.Queries;

namespace DotnetInspect.Web;

/// <summary>
/// Browser-owned scheduling around one product-issued Navigation state slot.
/// The active Workspace owner supplies fresh preparation for each operation;
/// the slot retains no Workspace, Scope, lease, or evaluation facts.
/// </summary>
[SupportedOSPlatform("browser")]
internal sealed class BrowserNavigationStateSlot
{
    readonly object _gate = new();
    readonly ViewFacetRegistry _registry;
    readonly CancellationTokenSource _retirement = new();
    readonly Dictionary<NavigationRequest, Pending> _pending = [];
    NavigationState _state;
    NavigationEffectAuthority? _publishedAuthority;
    bool _retired;

    internal BrowserNavigationStateSlot(
        NavigationOperationInitialization initialization,
        ViewFacetRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(initialization);
        ArgumentNullException.ThrowIfNull(registry);
        _state = initialization.State;
        _registry = registry;
        Initialization = initialization.Result.Consumer;
        _publishedAuthority = Initialization.Authority;
    }

    internal NavigationConsumerResult Initialization { get; }
    internal string Id => _state.Id;
    internal InspectionWorkspaceIdentity Workspace => _state.Workspace;

    internal NavigationActionPublicationResult PublishRetainedTypeAction(
        StructuralSubjectIdentity.TypeSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        lock (_gate)
        {
            if (_retired)
            {
                return new(
                    NavigationActionPublicationKind.Stale,
                    Message: "The Browser Navigation state is retired.");
            }

            NavigationTransition transition = Commit(
                NavigationTransitions.PublishRetainedTypeAction(
                    _state,
                    _state.Publication,
                    subject));
            return transition.ActionPublication
                ?? throw new InvalidOperationException(
                    "Retained Type action publication returned no result.");
        }
    }

    internal async ValueTask<NavigationConsumerResult?> ExecuteAsync(
        NavigationAction action,
        Func<
            NavigationEvaluationRequest,
            CancellationToken,
            ValueTask<NavigationPreparation>> prepare,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(prepare);

        NavigationTransition beginning;
        lock (_gate)
        {
            if (_retired)
                return null;
            beginning = Commit(NavigationTransitions.Begin(_state, action));
            _publishedAuthority = null;
            TrackAuthority(beginning.Result);
        }
        if (beginning.Result is { } immediate)
            return Publish(immediate);

        NavigationEvaluationRequest work = beginning.Work!;
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _retirement.Token);
        try
        {
            NavigationPreparation preparation;
            try
            {
                operation.Token.ThrowIfCancellationRequested();
                preparation = await prepare(work, operation.Token)
                    .AsTask()
                    .WaitAsync(operation.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (_retirement.IsCancellationRequested)
            {
                lock (_gate)
                    Cancel(work.Identity);
                return null;
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                preparation = new NavigationPreparation.Aborted(
                    "The Browser navigation prerequisite was cancelled.");
            }

            NavigationEvaluationResult evaluation =
                NavigationTransitions.Evaluate(
                    work,
                    preparation,
                    _registry);
            NavigationTransition completion;
            lock (_gate)
            {
                if (_retired)
                {
                    Cancel(work.Identity);
                    return null;
                }
                completion = Commit(
                    NavigationTransitions.Complete(
                        _state,
                        work,
                        evaluation));
                TrackAuthority(completion.Result);
            }
            Pump();
            if (completion.Rejection is { } rejection)
            {
                throw new InvalidOperationException(
                    $"Browser Navigation completion was rejected: {rejection}.");
            }
            return completion.Result is { } result
                ? Publish(result)
                : null;
        }
        catch
        {
            lock (_gate)
                Cancel(work.Identity);
            Pump();
            throw;
        }
    }

    internal ValueTask<NavigationConsumerResult?> RefreshAsync(
        Func<
            NavigationEvaluationRequest,
            CancellationToken,
            ValueTask<NavigationPreparation>> prepare,
        CancellationToken cancellationToken = default) =>
        QueueAsync(
            maintenance: true,
            prepare,
            cancellationToken);

    internal ValueTask<NavigationConsumerResult?> SynchronizeAsync(
        Func<
            NavigationEvaluationRequest,
            CancellationToken,
            ValueTask<NavigationPreparation>> prepare,
        CancellationToken cancellationToken = default) =>
        QueueAsync(
            maintenance: false,
            prepare,
            cancellationToken);

    internal bool ValidateAuthority(
        NavigationEffectAuthority authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        lock (_gate)
        {
            return !_retired
                && NavigationTransitions.ValidateAuthority(
                    _state,
                    authority);
        }
    }

    internal NavigationAuthorityResult RecordConsumerPosting(
        NavigationEffectAuthority authority) =>
        ApplyAuthority(
            state => NavigationTransitions.RecordConsumerPosting(
                state,
                authority),
            settlesAuthority: false);

    internal NavigationAuthorityResult Acknowledge(
        NavigationEffectAuthority authority) =>
        ApplyAuthority(
            state => NavigationTransitions.Acknowledge(
                state,
                authority),
            settlesAuthority: true);

    internal NavigationAuthorityResult Abandon(
        NavigationEffectAuthority authority) =>
        ApplyAuthority(
            state => NavigationTransitions.Abandon(
                state,
                authority),
            settlesAuthority: true);

    internal void Retire()
    {
        List<Pending> pending;
        lock (_gate)
        {
            if (_retired)
                return;
            _retired = true;
            if (_publishedAuthority is { } authority
                && NavigationTransitions.ValidateAuthority(
                    _state,
                    authority))
            {
                Commit(NavigationTransitions.Abandon(
                    _state,
                    authority));
            }
            _publishedAuthority = null;
            foreach (NavigationRequest request in _pending.Keys)
                Cancel(request);
            pending = [.. _pending.Values];
            _pending.Clear();
        }

        Exception? cancellationFailure = null;
        try
        {
            _retirement.Cancel();
        }
        catch (Exception failure)
        {
            cancellationFailure = failure;
        }
        foreach (Pending request in pending)
            request.Completion.TrySetResult(null);
        if (cancellationFailure is not null)
            ExceptionDispatchInfo.Capture(cancellationFailure).Throw();
    }

    async ValueTask<NavigationConsumerResult?> QueueAsync(
        bool maintenance,
        Func<
            NavigationEvaluationRequest,
            CancellationToken,
            ValueTask<NavigationPreparation>> prepare,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prepare);
        cancellationToken.ThrowIfCancellationRequested();

        NavigationRequest request;
        var pending = new Pending(prepare, cancellationToken);
        lock (_gate)
        {
            if (_retired)
                return null;
            NavigationTransition queued = Commit(
                maintenance
                    ? NavigationTransitions.QueueMaintenance(_state)
                    : NavigationTransitions.QueueSynchronization(_state));
            request = queued.Request!;
            _pending.Add(request, pending);
        }

        using CancellationTokenRegistration registration =
            cancellationToken.Register(() => CancelPending(request));
        Pump();
        return await pending.Completion.Task.ConfigureAwait(false);
    }

    void Pump()
    {
        NavigationEvaluationRequest? work;
        Pending? pending;
        NavigationOperationResult? immediate;
        lock (_gate)
        {
            if (_retired)
                return;
            NavigationTransition transition =
                Commit(NavigationTransitions.Advance(_state));
            immediate = transition.Result;
            TrackAuthority(immediate);
            if (immediate is not null)
                _pending.Remove(transition.Request!, out pending);
            else
                pending = transition.Work is null
                    ? null
                    : _pending[transition.Work.Identity];
            work = transition.Work;
        }

        if (immediate is not null)
            pending!.Completion.TrySetResult(Publish(immediate));
        if (work is not null)
            _ = EvaluateQueuedAsync(work, pending!);
    }

    async Task EvaluateQueuedAsync(
        NavigationEvaluationRequest work,
        Pending pending)
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(
            pending.CancellationToken,
            _retirement.Token);
        try
        {
            NavigationPreparation preparation =
                await pending.Prepare(work, operation.Token)
                    .AsTask()
                    .WaitAsync(operation.Token)
                    .ConfigureAwait(false);
            NavigationEvaluationResult evaluation =
                NavigationTransitions.Evaluate(
                    work,
                    preparation,
                    _registry);
            NavigationOperationResult? result = null;
            lock (_gate)
            {
                if (_retired
                    || !_pending.ContainsKey(work.Identity))
                {
                    return;
                }

                NavigationTransition completion = Commit(
                    NavigationTransitions.Complete(
                        _state,
                        work,
                        evaluation));
                if (completion.Rejection is { } rejection)
                {
                    throw new InvalidOperationException(
                        $"Browser Navigation maintenance completion was rejected: {rejection}.");
                }
                if (completion.Result is not null)
                {
                    _pending.Remove(work.Identity);
                    result = completion.Result;
                    TrackAuthority(result);
                }
            }

            if (result is not null)
                pending.Completion.TrySetResult(Publish(result));
            Pump();
        }
        catch (OperationCanceledException)
            when (_retirement.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException)
            when (pending.CancellationToken.IsCancellationRequested)
        {
            CancelPending(work.Identity);
        }
        catch (Exception failure)
        {
            lock (_gate)
            {
                if (_pending.Remove(work.Identity))
                {
                    Cancel(work.Identity);
                    pending.Completion.TrySetException(failure);
                }
            }
            Pump();
        }
    }

    NavigationAuthorityResult ApplyAuthority(
        Func<NavigationState, NavigationTransition> transition,
        bool settlesAuthority)
    {
        NavigationAuthorityResult result;
        lock (_gate)
        {
            if (_retired)
                return NavigationAuthorityResult.InvalidAuthority;
            NavigationTransition applied = Commit(transition(_state));
            result = applied.AuthorityResult!.Value;
            if (settlesAuthority
                && result == NavigationAuthorityResult.Accepted)
            {
                _publishedAuthority = null;
            }
        }
        Pump();
        return result;
    }

    NavigationConsumerResult? Publish(
        NavigationOperationResult result)
    {
        NavigationConsumerResult consumer = result.Consumer;
        if (consumer.Outcome.Kind == NavigationOutcomeKind.Superseded)
            return null;
        lock (_gate)
        {
            if (_retired)
                return null;
        }
        return consumer;
    }

    void TrackAuthority(
        NavigationOperationResult? result)
    {
        if (result?.Consumer is not
            {
                Outcome.Kind: not NavigationOutcomeKind.Superseded,
                Authority: { } authority,
            })
        {
            return;
        }
        if (NavigationTransitions.ValidateAuthority(
                _state,
                authority))
        {
            _publishedAuthority = authority;
        }
    }

    void CancelPending(
        NavigationRequest request)
    {
        Pending? pending;
        lock (_gate)
        {
            if (!_pending.Remove(request, out pending))
                return;
            Cancel(request);
        }
        pending.Completion.TrySetCanceled(
            pending.CancellationToken);
        Pump();
    }

    void Cancel(
        NavigationRequest request)
    {
        NavigationTransition cancelled =
            NavigationTransitions.Cancel(_state, request);
        if (cancelled.Rejection is null)
            Commit(cancelled);
    }

    NavigationTransition Commit(
        NavigationTransition transition)
    {
        if (!NavigationTransitions.CanCommit(
                _state,
                transition))
        {
            throw new InvalidOperationException(
                "The Browser Navigation state changed before commit.");
        }
        _state = transition.State;
        return transition;
    }

    sealed class Pending(
        Func<
            NavigationEvaluationRequest,
            CancellationToken,
            ValueTask<NavigationPreparation>> prepare,
        CancellationToken cancellationToken)
    {
        internal Func<
            NavigationEvaluationRequest,
            CancellationToken,
            ValueTask<NavigationPreparation>> Prepare { get; } = prepare;
        internal CancellationToken CancellationToken { get; } =
            cancellationToken;
        internal TaskCompletionSource<NavigationConsumerResult?> Completion
        {
            get;
        } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
