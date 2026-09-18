using System.Runtime.ExceptionServices;
using DotnetInspector.Packages;

namespace DotnetInspect.Web;

internal sealed class BrowserSharedPackageAcquisition
{
    readonly BrowserSharedOperation<AcquiredPackageSourcePayload> _operation;

    internal BrowserSharedPackageAcquisition(
        Func<Task<AcquiredPackageSourcePayload>> acquire,
        BrowserManagedEpochWorkSource? epochWork) =>
        _operation = new(
            acquire,
            epochWork,
            "Package acquisition");

    internal Task<AcquiredPackageSourcePayload> Completion =>
        _operation.Completion;

    internal bool IsCompleted => _operation.IsCompleted;

    internal Task<AcquiredPackageSourcePayload> WaitAsync(
        CancellationToken cancellationToken) =>
        _operation.WaitAsync(cancellationToken);
}

internal sealed class BrowserSharedOperation<T>
{
    readonly object _sync = new();
    readonly Func<Task<T>>? _operation;
    readonly BrowserManagedSharedProducer<T, string, string, object>? _producer;
    readonly Task<T>? _registeredCompletion;
    readonly string _name;
    Task<T>? _unregisteredCompletion;

    internal BrowserSharedOperation(
        Func<Task<T>> operation,
        BrowserManagedEpochWorkSource? epochWork,
        string name)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _name = name;
        if (epochWork is null)
            _operation = operation;
        else
        {
            _producer = new BrowserManagedSharedProducer<T, string, string, object>(
                async _ => new BrowserManagedOperationBodyResult<
                    T,
                    string,
                    string>.Succeeded(
                        await operation().ConfigureAwait(false)),
                epochWork: epochWork);
            _registeredCompletion = ObserveAsync();
        }
    }

    internal Task<T> Completion =>
        _producer is null ? StartUnregistered() : _registeredCompletion!;

    internal bool IsCompleted
    {
        get
        {
            if (_producer is not null)
                return _producer.IsCompleted;
            lock (_sync)
                return _unregisteredCompletion?.IsCompleted == true;
        }
    }

    internal async Task<T> WaitAsync(CancellationToken cancellationToken)
    {
        if (_producer is null)
            return await Completion.WaitAsync(cancellationToken).ConfigureAwait(false);

        BrowserManagedSharedProducer<T, string, string, object>.Subscription? subscription =
            _producer.TryAttach();
        if (subscription is null)
        {
            // A sealed producer is already terminal or retained by its final draining
            // waiter/fault record. Reuse it instead of starting duplicate work.
            return await Completion.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        BrowserManagedOperationBodyResult<T, string, string>? result = null;
        ExceptionDispatchInfo? failure = null;
        try
        {
            result = await subscription.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = ExceptionDispatchInfo.Capture(exception);
        }

        try
        {
            await subscription.DetachAsync().ConfigureAwait(false);
        }
        catch (Exception releaseFailure) when (failure is not null)
        {
            throw new AggregateException(
                $"{_name} wait and release both failed.",
                failure.SourceException,
                releaseFailure);
        }

        failure?.Throw();
        return Value(result);
    }

    Task<T> StartUnregistered()
    {
        lock (_sync)
        {
            if (_unregisteredCompletion is not null)
                return _unregisteredCompletion;

            try
            {
                _unregisteredCompletion = _operation!()
                    ?? Task.FromException<T>(
                        new InvalidOperationException(
                            $"{_name} returned no task."));
            }
            catch (Exception exception)
            {
                _unregisteredCompletion =
                    Task.FromException<T>(exception);
            }
            return _unregisteredCompletion;
        }
    }

    async Task<T> ObserveAsync() =>
        Value(await _producer!.ObserveCompletionAsync().ConfigureAwait(false));

    T Value(BrowserManagedOperationBodyResult<T, string, string>? result) =>
        result is BrowserManagedOperationBodyResult<T, string, string>.Succeeded succeeded
            ? succeeded.Value
            : throw new InvalidOperationException($"{_name} returned no result.");
}
