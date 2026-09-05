using System.Runtime.ExceptionServices;

namespace InspectWeb.Engine;

internal enum BrowserManagedProducerDisposition
{
    AnotherWaiterRemains,
    ProducerTerminal,
    EpochWorkLease,
}

internal interface IBrowserManagedSharedSubscription
{
    ValueTask<BrowserManagedProducerDisposition> DetachAsync();
}

internal sealed class BrowserManagedProducerCancellationException : Exception
{
    internal BrowserManagedProducerCancellationException(
        OperationCanceledException cancellation)
        : base("Shared producer cancellation is not waiter cancellation.", cancellation)
    {
        Cancellation = cancellation;
    }

    internal OperationCanceledException Cancellation { get; }
}

/// <summary>
/// Owns scoped waiters over a feature-owned producer. The final detach waits
/// for completion unless the feature supplies an epoch-work handoff.
/// </summary>
internal sealed class BrowserManagedSharedProducer<
    TValue,
    TError,
    TDiagnostic,
    TEvent> : IBrowserManagedOperationEvents<TEvent>
{
    readonly object _sync = new();
    readonly HashSet<Subscription> _waiters = [];
    readonly Func<
        IBrowserManagedOperationEvents<TEvent>,
        Task<BrowserManagedOperationBodyResult<TValue, TError, TDiagnostic>>> _start;
    readonly TaskCompletionSource<Completion> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly Action? _requestStopOnLastDetach;
    readonly CancellationToken _producerCancellationToken;
    readonly BrowserManagedEpochWorkSource? _epochWork;
    Task? _producerTask;
    BrowserManagedEpochWorkHandle? _workHandle;
    TaskCompletionSource? _handoff;
    bool _started;
    bool _closed;

    internal BrowserManagedSharedProducer(
        Func<
            IBrowserManagedOperationEvents<TEvent>,
            Task<BrowserManagedOperationBodyResult<TValue, TError, TDiagnostic>>> start,
        Action? requestStopOnLastDetach = null,
        CancellationToken producerCancellationToken = default,
        BrowserManagedEpochWorkSource? epochWork = null)
    {
        ArgumentNullException.ThrowIfNull(start);
        _start = start;
        _requestStopOnLastDetach = requestStopOnLastDetach;
        _producerCancellationToken = producerCancellationToken;
        _epochWork = epochWork;
    }

    public bool IsClosed
    {
        get
        {
            lock (_sync)
                return _closed
                    || (_epochWork is null && _waiters.Count == 0)
                    || _producerTask?.IsCompleted == true;
        }
    }

    internal int WaiterCount
    {
        get
        {
            lock (_sync)
                return _waiters.Count;
        }
    }

    internal async Task<BrowserManagedOperationBodyResult<TValue, TError, TDiagnostic>>
        ObserveCompletionAsync()
    {
        Completion completion = await _completion.Task.ConfigureAwait(false);
        completion.ThrowReleaseFailure(observedProducerFailure: false);
        completion.ThrowProducerFailure();
        return completion.Result
            ?? throw new InvalidOperationException("The shared producer returned no result.");
    }

    internal Subscription Attach(IBrowserManagedOperationEvents<TEvent> events)
    {
        Subscription subscription;
        bool start;
        lock (_sync)
        {
            if (_closed)
            {
                throw new InvalidOperationException(
                    "The shared producer no longer accepts waiters after its final detach begins.");
            }

            subscription = new Subscription(this, events);
            _waiters.Add(subscription);
            start = !_started;
            _started = true;
        }

        if (start)
            Start();
        return subscription;
    }

    void Start()
    {
        Task<BrowserManagedOperationBodyResult<TValue, TError, TDiagnostic>> producer;
        try
        {
            producer = _start(this)
                ?? throw new InvalidOperationException("The shared producer returned no task.");
        }
        catch (Exception exception)
        {
            producer = Task.FromException<
                BrowserManagedOperationBodyResult<TValue, TError, TDiagnostic>>(exception);
        }

        lock (_sync)
            _producerTask = producer;
        _ = ObserveAsync(producer);
    }

    public void Report(TEvent operationEvent)
    {
        Subscription[] waiters;
        lock (_sync)
        {
            if (_closed || _producerTask?.IsCompleted == true)
                return;
            waiters = [.. _waiters];
        }

        foreach (Subscription waiter in waiters)
            waiter.Report(operationEvent);
    }

    async ValueTask<BrowserManagedProducerDisposition> DetachAsync(
        Subscription subscription)
    {
        bool requestStop;
        TaskCompletionSource? handoff = null;
        BrowserManagedOperationBoundaryException? retainedStartFailure = null;
        lock (_sync)
        {
            if (!_waiters.Contains(subscription))
            {
                throw new InvalidOperationException(
                    "The shared producer no longer owns this subscription.");
            }

            subscription.Close();
            if (_waiters.Count > 1)
            {
                _waiters.Remove(subscription);
                return BrowserManagedProducerDisposition.AnotherWaiterRemains;
            }

            if (_epochWork is not null && _producerTask?.IsCompleted != true)
            {
                if (_workHandle is { } existing)
                {
                    _waiters.Remove(subscription);
                    if (existing.StartFailure is null)
                        return BrowserManagedProducerDisposition.EpochWorkLease;
                    retainedStartFailure = existing.StartFailure;
                }
                else
                    handoff = _handoff = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            // Terminal-bounded detachment seals admission and keeps its final
            // waiter represented while physical work drains.
            if (handoff is null)
                _closed = true;
            // Observation can resume after the physical task has completed.
            requestStop = _producerTask?.IsCompleted != true;
        }

        if (handoff is not null)
            return await TransferEpochWorkAsync(subscription, handoff).ConfigureAwait(false);

        Exception? stopFailure = null;
        if (requestStop && _requestStopOnLastDetach is not null)
        {
            try
            {
                _requestStopOnLastDetach();
            }
            catch (Exception exception)
            {
                stopFailure = exception;
            }
        }

        if (retainedStartFailure is not null)
        {
            throw new BrowserManagedOperationBoundaryException(
                "epoch-work-handoff",
                "The shared producer remains owned by an epoch-fault record.",
                retainedStartFailure,
                stopFailure is not null ? [stopFailure] : []);
        }

        Completion completion = await _completion.Task.ConfigureAwait(false);
        lock (_sync)
            _waiters.Remove(subscription);

        completion.ThrowReleaseFailure(subscription.ObservedCompletion);
        bool requestedProducerCancellation =
            requestStop
            && _requestStopOnLastDetach is not null
            && _producerCancellationToken.IsCancellationRequested
            && completion.Failure?.SourceException is OperationCanceledException canceled
            && canceled.CancellationToken == _producerCancellationToken;
        ExceptionDispatchInfo? unobservedFailure =
            subscription.ObservedCompletion || requestedProducerCancellation
                ? null
                : completion.Failure;
        if (stopFailure is not null && unobservedFailure is not null)
        {
            throw new AggregateException(
                "Shared producer stop and completion both failed during final detach.",
                stopFailure,
                unobservedFailure.SourceException);
        }
        if (stopFailure is not null)
            ExceptionDispatchInfo.Capture(stopFailure).Throw();
        unobservedFailure?.Throw();

        return BrowserManagedProducerDisposition.ProducerTerminal;
    }

    async ValueTask<BrowserManagedProducerDisposition> TransferEpochWorkAsync(
        Subscription subscription,
        TaskCompletionSource handoff)
    {
        BrowserManagedEpochWorkHandle? handle = null;
        Exception? failure = null;
        Exception? stopFailure = null;
        try
        {
            handle = _epochWork!.Acquire(_producerTask!);
            failure = handle.StartFailure;
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        bool requestStop;
        lock (_sync)
        {
            _workHandle = handle;
            if (failure is not null)
                _closed = true;
            requestStop = failure is not null && _waiters.Count == 1
                && _producerTask?.IsCompleted != true;
        }
        if (requestStop && _requestStopOnLastDetach is not null)
        {
            try
            {
                _requestStopOnLastDetach();
            }
            catch (Exception exception)
            {
                stopFailure = exception;
            }
        }

        bool terminal;
        bool anotherWaiter;
        lock (_sync)
        {
            terminal = _producerTask!.IsCompleted;
            // A closed registration cannot issue a fault record. Retain the
            // waiter and use the terminal-bounded path instead of orphaning work.
            if (handle is not null)
                _waiters.Remove(subscription);
            anotherWaiter = _waiters.Count > 0;
            _handoff = null;
        }
        handoff.SetResult();

        Completion? completion = null;
        if (terminal || handle is null)
        {
            completion = await _completion.Task.ConfigureAwait(false);
            if (handle is null)
            {
                lock (_sync)
                    _waiters.Remove(subscription);
            }
        }
        if (failure is not null)
        {
            var secondary = new List<Exception>();
            if (stopFailure is not null)
                secondary.Add(stopFailure);
            if (completion?.Failure is { } producerFailure)
                secondary.Add(producerFailure.SourceException);
            if (completion?.ReleaseFailure is { } releaseFailure)
                secondary.Add(releaseFailure);
            throw new BrowserManagedOperationBoundaryException(
                "epoch-work-handoff",
                "The shared producer could not acquire its epoch-work lease.",
                failure,
                secondary);
        }
        if (completion is { } completed)
        {
            completed.ThrowReleaseFailure(subscription.ObservedCompletion);
            if (!subscription.ObservedCompletion)
                completed.Failure?.Throw();
            return BrowserManagedProducerDisposition.ProducerTerminal;
        }
        return anotherWaiter
            ? BrowserManagedProducerDisposition.AnotherWaiterRemains
            : BrowserManagedProducerDisposition.EpochWorkLease;
    }

    async Task ObserveAsync(
        Task<BrowserManagedOperationBodyResult<TValue, TError, TDiagnostic>> producer)
    {
        Completion completion;
        try
        {
            var result = await producer.ConfigureAwait(false);
            completion = new Completion(result, null, null);
        }
        catch (Exception exception)
        {
            // A canceled waiter may miss the producer's later failure. Keep
            // that observation for the final detach rather than discarding it.
            completion = new Completion(null, ExceptionDispatchInfo.Capture(exception), null);
        }

        Task? handoff;
        lock (_sync)
        {
            if (_epochWork is not null)
                _closed = true;
            handoff = _handoff?.Task;
        }
        if (handoff is not null)
            await handoff.ConfigureAwait(false);
        try
        {
            _workHandle?.Dispose();
        }
        catch (Exception exception)
        {
            completion = completion with { ReleaseFailure = exception };
        }
        _completion.SetResult(completion);
    }

    readonly record struct Completion(
        BrowserManagedOperationBodyResult<TValue, TError, TDiagnostic>? Result,
        ExceptionDispatchInfo? Failure,
        Exception? ReleaseFailure)
    {
        internal void ThrowProducerFailure()
        {
            if (Failure?.SourceException is OperationCanceledException cancellation)
                throw new BrowserManagedProducerCancellationException(cancellation);
            Failure?.Throw();
        }

        internal void ThrowReleaseFailure(bool observedProducerFailure)
        {
            if (ReleaseFailure is not null)
            {
                throw new BrowserManagedOperationBoundaryException(
                    "epoch-work-completion",
                    "The shared producer failed to release its epoch-work ownership.",
                    ReleaseFailure,
                    !observedProducerFailure && Failure is { } failure
                        ? [failure.SourceException] : []);
            }
        }
    }

    internal sealed class Subscription : IBrowserManagedSharedSubscription
    {
        readonly BrowserManagedSharedProducer<TValue, TError, TDiagnostic, TEvent>
            _owner;
        IBrowserManagedOperationEvents<TEvent>? _events;

        internal Subscription(
            BrowserManagedSharedProducer<TValue, TError, TDiagnostic, TEvent> owner,
            IBrowserManagedOperationEvents<TEvent> events)
        {
            _owner = owner;
            _events = events;
        }

        internal bool ObservedCompletion { get; private set; }

        internal async Task<
            BrowserManagedOperationBodyResult<TValue, TError, TDiagnostic>>
            WaitAsync(CancellationToken operationToken)
        {
            Completion completion =
                await _owner._completion.Task.WaitAsync(operationToken).ConfigureAwait(false);
            ObservedCompletion = true;
            completion.ThrowProducerFailure();
            return completion.Result
                ?? throw new InvalidOperationException(
                    "The shared producer returned no result.");
        }

        internal void Report(TEvent operationEvent) =>
            Volatile.Read(ref _events)?.Report(operationEvent);

        internal void Close() => Interlocked.Exchange(ref _events, null);

        public ValueTask<BrowserManagedProducerDisposition> DetachAsync() =>
            _owner.DetachAsync(this);
    }
}
