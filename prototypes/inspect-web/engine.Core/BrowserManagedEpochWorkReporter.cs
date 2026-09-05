namespace InspectWeb.Engine;

internal abstract class BrowserManagedEpochWorkSource
{
    internal abstract BrowserManagedEpochWorkHandle Acquire(Task producer);
}

internal abstract class BrowserManagedEpochWorkHandle : IDisposable
{
    internal abstract long? Sequence { get; }
    internal abstract BrowserManagedOperationBoundaryException? StartFailure { get; }
    public abstract void Dispose();
}

internal readonly record struct BrowserManagedEpochWorkSnapshot(
    long LastSequence,
    int ActiveLeases,
    int FaultRecords,
    int PendingCallouts,
    bool AdmissionStopped,
    bool Registered);

/// <summary>
/// Owns one epoch's managed work identities and reporter lifetime. Allowances
/// are opaque here; the Worker owner supplies and validates their meaning.
/// </summary>
internal sealed class BrowserManagedEpochWorkReporter<TAllowance>
{
    internal const long MaximumWorkSequence = 9_007_199_254_740_991;

    readonly object _sync = new();
    readonly object _startOrder = new();
    readonly HashSet<Handle> _records = [];
    readonly List<Exception> _failures = [];
    readonly TaskCompletionSource _drained =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly long _maximumSequence;
    Action<long, TAllowance>? _started;
    Action<long>? _finished;
    long _lastSequence;
    bool _accepting = true;
    bool _registered = true;

    internal BrowserManagedEpochWorkReporter(
        Action<long, TAllowance> started,
        Action<long> finished,
        long maximumWorkSequence = MaximumWorkSequence)
    {
        ArgumentNullException.ThrowIfNull(started);
        ArgumentNullException.ThrowIfNull(finished);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumWorkSequence);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumWorkSequence, MaximumWorkSequence);
        _started = started;
        _finished = finished;
        _maximumSequence = maximumWorkSequence;
    }

    internal BrowserManagedEpochWorkSource ForProducer(TAllowance allowance) =>
        new Source(this, allowance);

    internal BrowserManagedEpochWorkSnapshot Snapshot()
    {
        lock (_sync)
        {
            return new(
                _lastSequence,
                _records.Count(record => record.State is WorkState.Active),
                _records.Count(record => record.State is WorkState.Faulted),
                _records.Count(record => record.State is WorkState.Starting or WorkState.Finishing),
                !_accepting,
                _registered);
        }
    }

    internal void StopAdmission()
    {
        lock (_sync)
        {
            _accepting = false;
            SignalDrain();
        }
    }

    internal async Task DrainAsync()
    {
        lock (_sync)
        {
            if (_accepting)
                throw new InvalidOperationException("Stop epoch-work admission before draining the reporter.");
        }
        await _drained.Task.ConfigureAwait(false);
        lock (_sync)
        {
            if (_failures.Count > 0)
            {
                throw new BrowserManagedOperationBoundaryException(
                    "epoch-work-drain",
                    "Epoch-work reporting failed before its records drained.",
                    _failures[0],
                    _failures.Skip(1).ToArray());
            }
        }
    }

    internal void Unregister()
    {
        lock (_sync)
        {
            if (_accepting || _records.Count != 0)
            {
                throw new InvalidOperationException(
                    "Epoch-work reporter unregister requires stopped admission and drained records.");
            }
            _registered = false;
            _started = null;
            _finished = null;
        }
    }

    BrowserManagedEpochWorkHandle Acquire(TAllowance allowance, Task producer)
    {
        // Allocation and start delivery share an order, without calling out
        // under the ledger or a producer's guard.
        lock (_startOrder)
        {
            Handle handle;
            Action<long, TAllowance> started;
            lock (_sync)
            {
                if (!_registered || !_accepting)
                    throw new InvalidOperationException("The epoch-work reporter no longer accepts work.");

                handle = new Handle(this, producer);
                _records.Add(handle);
                if (_lastSequence == _maximumSequence)
                {
                    handle.Failure = new BrowserManagedOperationBoundaryException(
                        "epoch-work-exhausted",
                        "The epoch-work sequence space is exhausted.");
                    handle.State = WorkState.Faulted;
                    RecordFailure(handle.Failure);
                    return handle;
                }
                handle.WorkSequence = ++_lastSequence;
                started = _started!;
            }

            BrowserManagedOperationBoundaryException? failure = null;
            try
            {
                started(handle.WorkSequence!.Value, allowance);
            }
            catch (Exception exception)
            {
                failure = new BrowserManagedOperationBoundaryException(
                    "epoch-work-start",
                    "The epoch-work start callback failed.",
                    exception);
            }

            lock (_sync)
            {
                handle.Failure = failure;
                handle.State = failure is null ? WorkState.Active : WorkState.Faulted;
                if (failure is not null)
                    RecordFailure(failure);
            }
            return handle;
        }
    }

    void Finish(Handle handle)
    {
        Action<long>? finished;
        lock (_sync)
        {
            if (handle.State is WorkState.Finishing or WorkState.Finished)
                return;
            finished = handle.State is WorkState.Active ? _finished : null;
            handle.State = WorkState.Finishing;
        }

        BrowserManagedOperationBoundaryException? failure = null;
        try
        {
            if (finished is not null)
                finished(handle.WorkSequence!.Value);
        }
        catch (Exception exception)
        {
            failure = new BrowserManagedOperationBoundaryException(
                "epoch-work-finish",
                "The epoch-work finish callback failed.",
                exception);
        }
        finally
        {
            lock (_sync)
            {
                if (failure is not null)
                    RecordFailure(failure);
                handle.State = WorkState.Finished;
                handle.Producer = null;
                _records.Remove(handle);
                SignalDrain();
            }
        }
        if (failure is not null)
            throw failure;
    }

    void RecordFailure(Exception failure)
    {
        _failures.Add(failure);
        _accepting = false;
    }

    void SignalDrain()
    {
        if (!_accepting && _records.Count == 0)
            _drained.TrySetResult();
    }

    enum WorkState
    {
        Starting,
        Active,
        Faulted,
        Finishing,
        Finished,
    }

    sealed class Source(
        BrowserManagedEpochWorkReporter<TAllowance> owner,
        TAllowance allowance) : BrowserManagedEpochWorkSource
    {
        internal override BrowserManagedEpochWorkHandle Acquire(Task producer) =>
            owner.Acquire(allowance, producer);
    }

    sealed class Handle(
        BrowserManagedEpochWorkReporter<TAllowance> owner,
        Task producer) : BrowserManagedEpochWorkHandle
    {
        internal Task? Producer = producer;
        internal long? WorkSequence;
        internal WorkState State;
        internal BrowserManagedOperationBoundaryException? Failure;

        internal override long? Sequence => WorkSequence;
        internal override BrowserManagedOperationBoundaryException? StartFailure => Failure;
        public override void Dispose() => owner.Finish(this);
    }
}
