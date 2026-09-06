using System.Runtime.ExceptionServices;
using DotnetInspector.Packages;
using BodyResult = InspectWeb.Engine.BrowserManagedOperationBodyResult<
    DotnetInspector.Packages.AcquiredPackageSourcePayload, string, string>;
using Producer = InspectWeb.Engine.BrowserManagedSharedProducer<
    DotnetInspector.Packages.AcquiredPackageSourcePayload, string, string, object>;

namespace InspectWeb.Engine;

internal sealed class BrowserSharedPackageAcquisition
{
    readonly Producer _producer;

    internal BrowserSharedPackageAcquisition(
        Func<Task<AcquiredPackageSourcePayload>> acquire,
        BrowserManagedEpochWorkSource? epochWork)
    {
        _producer = new Producer(
            async _ => new BodyResult.Succeeded(await acquire().ConfigureAwait(false)),
            epochWork: epochWork);
        Completion = ObserveAsync();
    }

    internal Task<AcquiredPackageSourcePayload> Completion { get; }
    internal bool IsCompleted => _producer.IsCompleted;

    internal async Task<AcquiredPackageSourcePayload> WaitAsync(CancellationToken cancellationToken)
    {
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

    async Task<AcquiredPackageSourcePayload> ObserveAsync() =>
        Value(await _producer.ObserveCompletionAsync().ConfigureAwait(false));

    static AcquiredPackageSourcePayload Value(BodyResult? result) =>
        result is BodyResult.Succeeded succeeded
            ? succeeded.Value
            : throw new InvalidOperationException("Package acquisition returned no payload.");
}
