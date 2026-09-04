namespace InspectWeb.Engine;

internal readonly record struct BrowserManagedOperationId
{
    BrowserManagedOperationId(string value)
    {
        Value = value;
    }

    internal string Value { get; }

    internal static BrowserManagedOperationId From(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        return new BrowserManagedOperationId(value);
    }

    public override string ToString() => Value ?? "";
}

internal enum BrowserManagedOperationCancelReason
{
    User,
    Superseded,
    Disposed,
    FeatureObserverFailed,
    Timeout,
    WorkerRestarted,
}

internal enum BrowserManagedOperationFailureKind
{
    Expected,
    Unexpected,
}

internal abstract record BrowserManagedOperationResult<
    TValue,
    TError,
    TDiagnostic>
{
    internal sealed record Succeeded(TValue Value)
        : BrowserManagedOperationResult<TValue, TError, TDiagnostic>;

    internal sealed record Failed(
        BrowserManagedOperationFailureKind FailureKind,
        TError Error,
        TDiagnostic Diagnostic)
        : BrowserManagedOperationResult<TValue, TError, TDiagnostic>;

    internal sealed record Canceled(BrowserManagedOperationCancelReason Reason)
        : BrowserManagedOperationResult<TValue, TError, TDiagnostic>;
}

internal abstract record BrowserManagedOperationBodyResult<
    TValue,
    TError,
    TDiagnostic>
{
    internal sealed record Succeeded(TValue Value)
        : BrowserManagedOperationBodyResult<TValue, TError, TDiagnostic>;

    internal sealed record Failed(TError Error, TDiagnostic Diagnostic)
        : BrowserManagedOperationBodyResult<TValue, TError, TDiagnostic>;
}

internal readonly record struct BrowserManagedOperationFailure<
    TError,
    TDiagnostic>(
    TError Error,
    TDiagnostic Diagnostic);

internal abstract record BrowserManagedCancellationRequestResult
{
    internal sealed record Requested(BrowserManagedOperationCancelReason Reason)
        : BrowserManagedCancellationRequestResult;

    internal sealed record AlreadyRequested(
        BrowserManagedOperationCancelReason Reason)
        : BrowserManagedCancellationRequestResult;

    internal sealed record NotActive : BrowserManagedCancellationRequestResult;
}

internal interface IBrowserManagedProgress<in TProgress>
{
    bool IsClosed { get; }

    void Report(TProgress progress);
}

internal enum BrowserManagedOperationCleanupStage
{
    ProgressCallback,
    SharedProducer,
    ActiveTable,
    CancellationSource,
}

internal sealed class BrowserManagedOperationBridgeTestHooks
{
    internal Action<BrowserManagedOperationCleanupStage>? CleanupCompleted
    {
        get;
        init;
    }

    internal Action? CalloutDrainSignaled { get; init; }

    internal Action? SettlementWaitingForCallouts { get; init; }
}

internal sealed class BrowserManagedOperationBoundaryException : Exception
{
    internal BrowserManagedOperationBoundaryException(
        string failureKind,
        string message,
        Exception? primaryFailure = null,
        IReadOnlyList<Exception>? secondaryFailures = null)
        : base(message, primaryFailure)
    {
        FailureKind = failureKind;
        SecondaryFailures = secondaryFailures ?? [];
    }

    internal string FailureKind { get; }

    internal IReadOnlyList<Exception> SecondaryFailures { get; }
}

/// <summary>
/// Owns dynamic managed-operation admission, callout lifetime, terminal
/// classification, and quiescent release for the inspect-web browser host.
/// </summary>
internal sealed class BrowserManagedOperationBridge
{
    readonly object _sync = new();
    readonly Dictionary<BrowserManagedOperationId, OperationEntry> _active = [];
    readonly BrowserManagedOperationBridgeTestHooks? _testHooks;

    internal BrowserManagedOperationBridge(
        BrowserManagedOperationBridgeTestHooks? testHooks = null)
    {
        _testHooks = testHooks;
    }

    internal int ActiveCount
    {
        get
        {
            lock (_sync)
                return _active.Count;
        }
    }

    internal Task<BrowserManagedOperationResult<TValue, TError, TDiagnostic>>
        RunAsync<TValue, TError, TDiagnostic, TProgress>(
            BrowserManagedOperationId operationId,
            Action<TProgress>? progressCallback,
            Func<
                CancellationToken,
                IBrowserManagedProgress<TProgress>,
                Task<
                    BrowserManagedOperationBodyResult<
                        TValue,
                        TError,
                        TDiagnostic>>> body,
            Func<
                Exception,
                BrowserManagedOperationFailure<TError, TDiagnostic>>
                unexpectedFailure)
    {
        Validate(operationId);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(unexpectedFailure);

        var entry = new OperationEntry(operationId, _testHooks);
        var progress = new ProgressGate<TProgress>(entry, progressCallback);
        entry.InstallProgressGate(progress);

        lock (_sync)
        {
            if (!_active.TryAdd(operationId, entry))
            {
                entry.DisposeRejected();
                return Task.FromException<
                    BrowserManagedOperationResult<TValue, TError, TDiagnostic>>(
                    new BrowserManagedOperationBoundaryException(
                        "duplicate-active-operation",
                        $"Managed operation '{operationId}' is already active."));
            }
        }

        Task<
            BrowserManagedOperationBodyResult<TValue, TError, TDiagnostic>>
            bodyTask;
        try
        {
            bodyTask = body(entry.Token, progress)
                ?? Task.FromException<
                    BrowserManagedOperationBodyResult<
                        TValue,
                        TError,
                        TDiagnostic>>(
                    new InvalidOperationException(
                        "The managed operation body returned no task."));
        }
        catch (Exception exception)
        {
            bodyTask = Task.FromException<
                BrowserManagedOperationBodyResult<
                    TValue,
                    TError,
                    TDiagnostic>>(exception);
        }

        return CompleteAsync(entry, bodyTask, unexpectedFailure);
    }

    internal BrowserManagedCancellationRequestResult RequestCancellation(
        BrowserManagedOperationId operationId,
        BrowserManagedOperationCancelReason reason)
    {
        Validate(operationId);

        OperationEntry? entry;
        lock (_sync)
            _active.TryGetValue(operationId, out entry);

        return entry is null
            ? new BrowserManagedCancellationRequestResult.NotActive()
            : entry.RequestCancellation(reason);
    }

    static void Validate(BrowserManagedOperationId operationId)
    {
        if (string.IsNullOrEmpty(operationId.Value))
        {
            throw new ArgumentException(
                "A managed operation ID is required.",
                nameof(operationId));
        }
    }

    async Task<BrowserManagedOperationResult<TValue, TError, TDiagnostic>>
        CompleteAsync<TValue, TError, TDiagnostic>(
            OperationEntry entry,
            Task<
                BrowserManagedOperationBodyResult<
                    TValue,
                    TError,
                    TDiagnostic>> bodyTask,
            Func<
                Exception,
                BrowserManagedOperationFailure<TError, TDiagnostic>>
                unexpectedFailure)
    {
        BrowserManagedOperationBodyResult<TValue, TError, TDiagnostic>?
            bodyResult = null;
        Exception? bodyFailure = null;
        try
        {
            bodyResult = await bodyTask.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            bodyFailure = exception;
        }

        await entry.BeginSettlement().ConfigureAwait(false);
        OperationSnapshot snapshot = entry.SnapshotForClassification();

        BrowserManagedOperationResult<TValue, TError, TDiagnostic>? result =
            null;
        Exception? classificationFailure = null;
        try
        {
            result = Classify(
                bodyResult,
                bodyFailure,
                snapshot,
                unexpectedFailure);
        }
        catch (Exception exception)
        {
            classificationFailure = exception;
        }

        List<Exception> cleanupFailures = Release(entry);
        Exception? boundaryFailure =
            snapshot.ProgressCallbackFailure ?? classificationFailure;
        if (boundaryFailure is not null)
        {
            var secondary = new List<Exception>();
            if (snapshot.ProgressCallbackFailure is not null
                && classificationFailure is not null)
            {
                secondary.Add(classificationFailure);
            }
            if (snapshot.TokenCallbackFailure is not null)
                secondary.Add(snapshot.TokenCallbackFailure);
            secondary.AddRange(cleanupFailures);
            throw new BrowserManagedOperationBoundaryException(
                snapshot.ProgressCallbackFailure is not null
                    ? "progress-callback"
                    : "terminal-classification",
                $"Managed operation '{entry.OperationId}' failed at its bridge boundary.",
                boundaryFailure,
                secondary);
        }

        if (cleanupFailures.Count > 0)
        {
            throw new BrowserManagedOperationBoundaryException(
                "cleanup",
                $"Managed operation '{entry.OperationId}' failed during release.",
                cleanupFailures[0],
                cleanupFailures.Skip(1).ToArray());
        }

        return result
            ?? throw new InvalidOperationException(
                "Managed operation classification produced no result.");
    }

    static BrowserManagedOperationResult<TValue, TError, TDiagnostic> Classify<
        TValue,
        TError,
        TDiagnostic>(
            BrowserManagedOperationBodyResult<TValue, TError, TDiagnostic>?
                bodyResult,
            Exception? bodyFailure,
            OperationSnapshot snapshot,
            Func<
                Exception,
                BrowserManagedOperationFailure<TError, TDiagnostic>>
                unexpectedFailure)
    {
        if (snapshot.TokenCallbackFailure is not null)
        {
            BrowserManagedOperationFailure<TError, TDiagnostic> failure =
                unexpectedFailure(snapshot.TokenCallbackFailure);
            return new BrowserManagedOperationResult<
                TValue,
                TError,
                TDiagnostic>.Failed(
                BrowserManagedOperationFailureKind.Unexpected,
                failure.Error,
                failure.Diagnostic);
        }

        if (bodyFailure is not null)
        {
            if (bodyFailure is OperationCanceledException
                && snapshot.CancellationReason is { } canceledReason
                && snapshot.OperationToken.IsCancellationRequested)
            {
                return new BrowserManagedOperationResult<
                    TValue,
                    TError,
                    TDiagnostic>.Canceled(canceledReason);
            }

            BrowserManagedOperationFailure<TError, TDiagnostic> failure =
                unexpectedFailure(bodyFailure);
            return new BrowserManagedOperationResult<
                TValue,
                TError,
                TDiagnostic>.Failed(
                BrowserManagedOperationFailureKind.Unexpected,
                failure.Error,
                failure.Diagnostic);
        }

        if (bodyResult is BrowserManagedOperationBodyResult<
                TValue,
                TError,
                TDiagnostic>.Failed expected)
        {
            return new BrowserManagedOperationResult<
                TValue,
                TError,
                TDiagnostic>.Failed(
                BrowserManagedOperationFailureKind.Expected,
                expected.Error,
                expected.Diagnostic);
        }

        if (bodyResult is not BrowserManagedOperationBodyResult<
                TValue,
                TError,
                TDiagnostic>.Succeeded succeeded)
        {
            throw new InvalidOperationException(
                "The managed operation body returned no supported result.");
        }

        return snapshot.CancellationReason is { } reason
            ? new BrowserManagedOperationResult<
                TValue,
                TError,
                TDiagnostic>.Canceled(reason)
            : new BrowserManagedOperationResult<
                TValue,
                TError,
                TDiagnostic>.Succeeded(succeeded.Value);
    }

    List<Exception> Release(OperationEntry entry)
    {
        var failures = new List<Exception>();
        AttemptCleanup(
            BrowserManagedOperationCleanupStage.ProgressCallback,
            entry.CloseProgress,
            failures);
        AttemptCleanup(
            BrowserManagedOperationCleanupStage.SharedProducer,
            static () => { },
            failures);
        AttemptCleanup(
            BrowserManagedOperationCleanupStage.ActiveTable,
            () => RemoveExact(entry),
            failures);
        AttemptCleanup(
            BrowserManagedOperationCleanupStage.CancellationSource,
            entry.DisposeCancellation,
            failures);
        entry.MarkReleased();
        return failures;
    }

    void AttemptCleanup(
        BrowserManagedOperationCleanupStage stage,
        Action cleanup,
        List<Exception> failures)
    {
        try
        {
            cleanup();
            _testHooks?.CleanupCompleted?.Invoke(stage);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    void RemoveExact(OperationEntry entry)
    {
        lock (_sync)
        {
            if (!_active.TryGetValue(entry.OperationId, out OperationEntry? active)
                || !ReferenceEquals(active, entry)
                || !_active.Remove(entry.OperationId))
            {
                throw new InvalidOperationException(
                    $"Managed operation '{entry.OperationId}' no longer owns its active table entry.");
            }
        }
    }

    sealed class OperationEntry
    {
        readonly object _sync = new();
        readonly CancellationTokenSource _cancellation = new();
        readonly CancellationToken _token;
        readonly BrowserManagedOperationBridgeTestHooks? _testHooks;
        EntryState _state = EntryState.Active;
        IProgressGate? _progress;
        BrowserManagedOperationCancelReason? _cancellationReason;
        Exception? _tokenCallbackFailure;
        Exception? _progressCallbackFailure;
        int _calloutCount;
        TaskCompletionSource? _calloutsDrained;
        bool _classified;

        internal OperationEntry(
            BrowserManagedOperationId operationId,
            BrowserManagedOperationBridgeTestHooks? testHooks)
        {
            OperationId = operationId;
            _token = _cancellation.Token;
            _testHooks = testHooks;
        }

        internal BrowserManagedOperationId OperationId { get; }

        internal CancellationToken Token => _token;

        internal void InstallProgressGate(IProgressGate progress)
        {
            lock (_sync)
            {
                if (_progress is not null)
                {
                    throw new InvalidOperationException(
                        "The managed progress gate is already installed.");
                }

                _progress = progress;
            }
        }

        internal BrowserManagedCancellationRequestResult RequestCancellation(
            BrowserManagedOperationCancelReason reason)
        {
            lock (_sync)
            {
                if (_state is not EntryState.Active)
                    return new BrowserManagedCancellationRequestResult.NotActive();
                if (_cancellationReason is { } existing)
                {
                    return new BrowserManagedCancellationRequestResult
                        .AlreadyRequested(existing);
                }

                _cancellationReason = reason;
                _calloutCount++;
            }

            SignalCancellation();
            return new BrowserManagedCancellationRequestResult.Requested(reason);
        }

        internal bool TryAcquireProgressCallout()
        {
            lock (_sync)
            {
                if (_state is not EntryState.Active
                    || _progressCallbackFailure is not null)
                {
                    return false;
                }

                _calloutCount++;
                return true;
            }
        }

        internal void RecordProgressFailure(Exception exception)
        {
            bool signalCancellation = false;
            lock (_sync)
            {
                _progressCallbackFailure ??= exception;
                if (_cancellationReason is null
                    && _state is EntryState.Active or EntryState.Settling)
                {
                    _cancellationReason =
                        BrowserManagedOperationCancelReason.FeatureObserverFailed;
                    signalCancellation = true;
                }
            }

            if (signalCancellation)
                SignalCancellation(releaseCallout: false);
        }

        void SignalCancellation(bool releaseCallout = true)
        {
            try
            {
                _cancellation.Cancel(throwOnFirstException: false);
            }
            catch (Exception exception)
            {
                lock (_sync)
                    _tokenCallbackFailure ??= exception;
            }
            finally
            {
                if (releaseCallout)
                    ReleaseCallout();
            }
        }

        internal void ReleaseCallout()
        {
            TaskCompletionSource? drained = null;
            lock (_sync)
            {
                if (_calloutCount <= 0)
                {
                    throw new InvalidOperationException(
                        "The managed operation released a callout it did not own.");
                }

                _calloutCount--;
                if (_state is EntryState.Settling && _calloutCount == 0)
                    drained = _calloutsDrained;
            }

            if (drained is not null)
            {
                drained.TrySetResult();
                _testHooks?.CalloutDrainSignaled?.Invoke();
            }
        }

        internal Task BeginSettlement()
        {
            Task drain;
            lock (_sync)
            {
                if (_state is not EntryState.Active)
                {
                    throw new InvalidOperationException(
                        "The managed operation can settle only once.");
                }

                _state = EntryState.Settling;
                if (_calloutCount == 0)
                    return Task.CompletedTask;

                _calloutsDrained = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                drain = _calloutsDrained.Task;
            }

            _testHooks?.SettlementWaitingForCallouts?.Invoke();
            return drain;
        }

        internal OperationSnapshot SnapshotForClassification()
        {
            lock (_sync)
            {
                if (_state is not EntryState.Settling || _calloutCount != 0)
                {
                    throw new InvalidOperationException(
                        "Managed operation classification requires a drained settling entry.");
                }
                if (_classified)
                {
                    throw new InvalidOperationException(
                        "The managed operation was classified more than once.");
                }

                _classified = true;
                return new OperationSnapshot(
                    _token,
                    _cancellationReason,
                    _tokenCallbackFailure,
                    _progressCallbackFailure);
            }
        }

        internal void CloseProgress()
        {
            lock (_sync)
            {
                if (_state is not EntryState.Settling || _calloutCount != 0)
                {
                    throw new InvalidOperationException(
                        "Managed progress can close only after callout drain.");
                }

                _progress?.Close();
            }
        }

        internal void DisposeCancellation() => _cancellation.Dispose();

        internal void MarkReleased()
        {
            lock (_sync)
                _state = EntryState.Released;
        }

        internal void DisposeRejected()
        {
            lock (_sync)
            {
                _progress?.Close();
                _state = EntryState.Released;
            }

            _cancellation.Dispose();
        }

        enum EntryState
        {
            Active,
            Settling,
            Released,
        }
    }

    sealed class ProgressGate<TProgress> :
        IBrowserManagedProgress<TProgress>,
        IProgressGate
    {
        readonly OperationEntry _entry;
        Action<TProgress>? _callback;

        internal ProgressGate(
            OperationEntry entry,
            Action<TProgress>? callback)
        {
            _entry = entry;
            _callback = callback;
        }

        public bool IsClosed => Volatile.Read(ref _callback) is null;

        public void Report(TProgress progress)
        {
            Action<TProgress>? callback = Volatile.Read(ref _callback);
            if (callback is null || !_entry.TryAcquireProgressCallout())
                return;

            try
            {
                callback(progress);
            }
            catch (Exception exception)
            {
                Interlocked.Exchange(ref _callback, null);
                _entry.RecordProgressFailure(exception);
            }
            finally
            {
                _entry.ReleaseCallout();
            }
        }

        public void Close() => Interlocked.Exchange(ref _callback, null);
    }

    interface IProgressGate
    {
        void Close();
    }

    readonly record struct OperationSnapshot(
        CancellationToken OperationToken,
        BrowserManagedOperationCancelReason? CancellationReason,
        Exception? TokenCallbackFailure,
        Exception? ProgressCallbackFailure);
}
