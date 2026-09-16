using System.Runtime.ExceptionServices;
using DotnetInspector.Packages;
using BodyResult = DotnetInspect.Web.BrowserManagedOperationBodyResult<
    DotnetInspector.Packages.AcquiredPackageSourcePayload, string, string>;
using Producer = DotnetInspect.Web.BrowserManagedSharedProducer<
    DotnetInspector.Packages.AcquiredPackageSourcePayload, string, string, object>;

namespace DotnetInspect.Web;

internal sealed class BrowserSharedPackageAcquisition
{
    readonly object _sync = new();
    readonly Func<Task<AcquiredPackageSourcePayload>>? _acquire;
    readonly Producer? _producer;
    readonly Task<AcquiredPackageSourcePayload>? _registeredCompletion;
    Task<AcquiredPackageSourcePayload>? _unregisteredCompletion;

    internal BrowserSharedPackageAcquisition(
        Func<Task<AcquiredPackageSourcePayload>> acquire,
        BrowserManagedEpochWorkSource? epochWork)
    {
        ArgumentNullException.ThrowIfNull(acquire);
        if (epochWork is null)
            _acquire = acquire;
        else
        {
            _producer = new Producer(
                async _ => new BodyResult.Succeeded(await acquire().ConfigureAwait(false)),
                epochWork: epochWork);
            _registeredCompletion = ObserveAsync();
        }
    }

    internal Task<AcquiredPackageSourcePayload> Completion =>
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

    internal async Task<AcquiredPackageSourcePayload> WaitAsync(CancellationToken cancellationToken)
    {
        if (_producer is null)
            return await Completion.WaitAsync(cancellationToken).ConfigureAwait(false);

        Producer.Subscription? subscription = _producer.TryAttach();
        if (subscription is null)
        {
            // A sealed producer is already terminal or retained by its final
            // draining waiter/fault record. Reuse it instead of downloading twice.
            return await Completion.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        BodyResult? result = null;
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
                "Package acquisition wait and release both failed.",
                failure.SourceException,
                releaseFailure);
        }

        failure?.Throw();
        return Value(result);
    }

    Task<AcquiredPackageSourcePayload> StartUnregistered()
    {
        lock (_sync)
        {
            if (_unregisteredCompletion is not null)
                return _unregisteredCompletion;

            try
            {
                _unregisteredCompletion = _acquire!()
                    ?? Task.FromException<AcquiredPackageSourcePayload>(
                        new InvalidOperationException(
                            "Package acquisition returned no task."));
            }
            catch (Exception exception)
            {
                _unregisteredCompletion =
                    Task.FromException<AcquiredPackageSourcePayload>(exception);
            }
            return _unregisteredCompletion;
        }
    }

    async Task<AcquiredPackageSourcePayload> ObserveAsync() =>
        Value(await _producer!.ObserveCompletionAsync().ConfigureAwait(false));

    static AcquiredPackageSourcePayload Value(BodyResult? result) =>
        result is BodyResult.Succeeded succeeded
            ? succeeded.Value
            : throw new InvalidOperationException("Package acquisition returned no payload.");
}
