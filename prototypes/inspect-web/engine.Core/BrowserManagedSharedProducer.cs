using System.Runtime.ExceptionServices;

namespace InspectWeb.Engine;

internal enum BrowserManagedProducerDisposition
{
    AnotherWaiterRemains,
    ProducerTerminal,
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
/// for producer completion; it cannot transfer work to an epoch-work lease.
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
    Task? _producerTask;
    bool _started;
    bool _closed;

    internal BrowserManagedSharedProducer(
        Func<
            IBrowserManagedOperationEvents<TEvent>,
            Task<BrowserManagedOperationBodyResult<TValue, TError, TDiagnostic>>> start,
        Action? requestStopOnLastDetach = null,
        CancellationToken producerCancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(start);
        _start = start;
        _requestStopOnLastDetach = requestStopOnLastDetach;
        _producerCancellationToken = producerCancellationToken;
    }

    public bool IsClosed
    {
        get
        {
            lock (_sync)
                return _closed || _waiters.Count == 0 || _producerTask?.IsCompleted == true;
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

            // Keep the final subscription represented while draining, and
            // seal admission before invoking the feature's stop policy.
            _closed = true;
            // Observation can resume after the physical task has completed.
            requestStop = _producerTask?.IsCompleted != true;
        }

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

        Completion completion = await _completion.Task.ConfigureAwait(false);
        lock (_sync)
            _waiters.Remove(subscription);

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

    async Task ObserveAsync(
        Task<BrowserManagedOperationBodyResult<TValue, TError, TDiagnostic>> producer)
    {
        Completion completion;
        try
        {
            var result = await producer.ConfigureAwait(false);
            completion = new Completion(result, null);
        }
        catch (Exception exception)
        {
            // A canceled waiter may miss the producer's later failure. Keep
            // that observation for the final detach rather than discarding it.
            completion = new Completion(null, ExceptionDispatchInfo.Capture(exception));
        }

        _completion.SetResult(completion);
    }

    readonly record struct Completion(
        BrowserManagedOperationBodyResult<TValue, TError, TDiagnostic>? Result,
        ExceptionDispatchInfo? Failure);

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
            if (completion.Failure?.SourceException is OperationCanceledException cancellation)
                throw new BrowserManagedProducerCancellationException(cancellation);
            completion.Failure?.Throw();
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
