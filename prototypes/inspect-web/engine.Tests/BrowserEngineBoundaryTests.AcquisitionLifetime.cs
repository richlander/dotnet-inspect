using System.Collections.Concurrent;
using DotnetInspector.Packages;
using NuGetFetch;

namespace InspectWeb.Engine.Tests;

public sealed partial class BrowserEngineBoundaryTests
{
    [Fact]
    public async Task SharedAcquisition_FinalCancellationTransfersOneLeaseAndLaterWaitersReuseIt()
    {
        string packageId = $"epoch.acquisition.{Guid.NewGuid():N}";
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new GalleryPackageHandler(
            packageId, "1.0.0", PackageDocuments(1), payloadRelease: release.Task);
        using IPackageSourceClient source = Gallery(handler);
        var registration = new BrowserManagedEpochWorkRegistration();
        var starts = new ConcurrentQueue<long>();
        var finishes = new ConcurrentQueue<long>();
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        registration.Register("opaque", (sequence, allowance) =>
        {
            Assert.Equal("opaque", allowance);
            starts.Enqueue(sequence);
        }, sequence =>
        {
            finishes.Enqueue(sequence);
            finished.TrySetResult();
        });
        using var firstCancellation = new CancellationTokenSource();
        using var secondCancellation = new CancellationTokenSource();
        using var laterCancellation = new CancellationTokenSource();
        Task<BrowserPackage> Acquire(CancellationToken token) =>
            BrowserPackageWorkspace.AcquireAsync(
                packageId, "1.0.0", source, PackageSourceIdentity.NuGetOrg,
                TimeSpan.FromSeconds(30), token, registration.Source);

        try
        {
            Task<BrowserPackage> first = Acquire(firstCancellation.Token);
            await AcquisitionWithin(handler.PayloadReadStarted.Task);
            Task<BrowserPackage> second = Acquire(secondCancellation.Token);
            firstCancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AcquisitionWithin(first));
            Assert.Empty(starts);
            Assert.False(second.IsCompleted);
            Console.WriteLine($"Non-final cancellation: epoch starts = {starts.Count}.");

            secondCancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AcquisitionWithin(second));
            Assert.Equal([1L], starts);
            Assert.Empty(finishes);
            Assert.Single(handler.Requested);
            Console.WriteLine($"Final cancellation: epoch starts = {starts.Count}, finishes = {finishes.Count}.");

            Task<BrowserPackage> later = Acquire(laterCancellation.Token);
            laterCancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AcquisitionWithin(later));
            Assert.Equal([1L], starts);
            Assert.Single(handler.Requested);

            Task<BrowserPackage> healthy = Acquire(TestContext.Current.CancellationToken);
            Task drained = registration.StopAndDrainAsync();
            Assert.False(drained.IsCompleted);
            release.SetResult();
            BrowserPackage package = await healthy.WaitAsync(
                TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Equal(packageId, package.PackageId, ignoreCase: true);
            await AcquisitionWithin(finished.Task);
            await AcquisitionWithin(drained);
            Assert.Equal([1L], finishes);
            Console.WriteLine($"Later waiter succeeded: HTTP transfers = {handler.Requested.Count}, epoch finishes = {finishes.Count}.");
        }
        finally
        {
            release.TrySetResult();
            await AcquisitionWithin(registration.StopAndDrainAsync());
            registration.Unregister();
        }
    }

    [Fact]
    public async Task SharedAcquisition_HealthyWaiterReceivesPayloadWithoutEpochLease()
    {
        string packageId = $"epoch.neighbor.{Guid.NewGuid():N}";
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new GalleryPackageHandler(
            packageId, "1.0.0", PackageDocuments(1), payloadRelease: release.Task);
        using IPackageSourceClient source = Gallery(handler);
        var registration = new BrowserManagedEpochWorkRegistration();
        var starts = new ConcurrentQueue<long>();
        registration.Register("opaque", (sequence, _) => starts.Enqueue(sequence), _ => { });
        using var cancellation = new CancellationTokenSource();
        Task<BrowserPackage> Acquire(CancellationToken token) =>
            BrowserPackageWorkspace.AcquireAsync(
                packageId, "1.0.0", source, PackageSourceIdentity.NuGetOrg,
                TimeSpan.FromSeconds(30), token, registration.Source);

        try
        {
            Task<BrowserPackage> canceled = Acquire(cancellation.Token);
            await AcquisitionWithin(handler.PayloadReadStarted.Task);
            Task<BrowserPackage> healthy = Acquire(CancellationToken.None);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AcquisitionWithin(canceled));
            release.SetResult();
            BrowserPackage package = await healthy.WaitAsync(
                TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Equal(packageId, package.PackageId, ignoreCase: true);
            Assert.Single(handler.Requested);
            Assert.Empty(starts);
        }
        finally
        {
            release.TrySetResult();
            await AcquisitionWithin(registration.StopAndDrainAsync());
            registration.Unregister();
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SharedAcquisition_ReporterFailureRetainsPhysicalDrain(bool failStart)
    {
        string packageId = $"epoch.failure.{Guid.NewGuid():N}";
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new GalleryPackageHandler(
            packageId, "1.0.0", PackageDocuments(1), payloadRelease: release.Task);
        using IPackageSourceClient source = Gallery(handler);
        var registration = new BrowserManagedEpochWorkRegistration();
        var finishes = new ConcurrentQueue<long>();
        var callbackFailure = new InvalidOperationException("reporter failure");
        registration.Register("opaque", (_, _) =>
        {
            if (failStart) throw callbackFailure;
        }, sequence =>
        {
            finishes.Enqueue(sequence);
            throw callbackFailure;
        });
        using var cancellation = new CancellationTokenSource();
        try
        {
            Task<BrowserPackage> waiting = BrowserPackageWorkspace.AcquireAsync(
                packageId, "1.0.0", source, PackageSourceIdentity.NuGetOrg,
                TimeSpan.FromSeconds(30), cancellation.Token, registration.Source);
            await AcquisitionWithin(handler.PayloadReadStarted.Task);
            cancellation.Cancel();
            if (failStart)
            {
                var failure = await Assert.ThrowsAsync<AggregateException>(
                    () => AcquisitionWithin(waiting));
                Assert.IsType<BrowserManagedOperationBoundaryException>(failure.InnerExceptions[1]);
            }
            else
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AcquisitionWithin(waiting));

            Task drain = registration.StopAndDrainAsync();
            Assert.False(drain.IsCompleted);
            Assert.Throws<InvalidOperationException>(registration.Unregister);
            release.SetResult();
            var drainFailure = await Assert.ThrowsAsync<BrowserManagedOperationBoundaryException>(
                () => AcquisitionWithin(drain));
            Assert.Equal("epoch-work-drain", drainFailure.FailureKind);
            Assert.Equal(failStart ? 0 : 1, finishes.Count);
        }
        finally
        {
            release.TrySetResult();
            await Assert.ThrowsAsync<BrowserManagedOperationBoundaryException>(
                () => AcquisitionWithin(registration.StopAndDrainAsync()));
            registration.Unregister();
        }
    }

    [Fact]
    public async Task SharedAcquisition_SealedTerminalDrainReusesPhysicalProducer()
    {
        var physical = new TaskCompletionSource<AcquiredPackageSourcePayload>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int starts = 0;
        var acquisition = new BrowserSharedPackageAcquisition(() =>
        {
            starts++;
            return physical.Task;
        }, epochWork: null);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Task<AcquiredPackageSourcePayload> first = acquisition.WaitAsync(cancellation.Token);
        Task<AcquiredPackageSourcePayload> later = acquisition.WaitAsync(cancellation.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => later);
        Assert.False(first.IsCompleted);
        Assert.Equal(1, starts);
        var failure = new InvalidOperationException("physical failure");
        physical.SetException(failure);
        await Assert.ThrowsAsync<AggregateException>(() => first);
        Assert.Same(failure,
            await Assert.ThrowsAsync<InvalidOperationException>(() => acquisition.Completion));
    }

    static Task AcquisitionWithin(Task task) =>
        task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
}
