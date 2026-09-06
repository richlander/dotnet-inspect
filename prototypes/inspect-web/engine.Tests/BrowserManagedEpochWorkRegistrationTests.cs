using BodyResult =
    InspectWeb.Engine.BrowserManagedOperationBodyResult<int, string, string>;
using Producer =
    InspectWeb.Engine.BrowserManagedSharedProducer<int, string, string, int>;

namespace InspectWeb.Engine.Tests;

public sealed class BrowserManagedEpochWorkRegistrationTests
{
    [Fact]
    public void MissingRegistration_FailsVisibly()
    {
        var registration = new BrowserManagedEpochWorkRegistration();

        Assert.Throws<InvalidOperationException>(() => registration.Source);
        Assert.Throws<InvalidOperationException>(() => { _ = registration.StopAndDrainAsync(); });
        Assert.Throws<InvalidOperationException>(registration.Unregister);
    }

    [Fact]
    public async Task Registration_IsSingleUse_AndCannotBeReplaced()
    {
        var registration = new BrowserManagedEpochWorkRegistration();
        registration.Register("first", (_, _) => { }, _ => { });
        BrowserManagedEpochWorkSource source = registration.Source;

        Assert.Throws<InvalidOperationException>(
            () => registration.Register("replacement", (_, _) => { }, _ => { }));
        Assert.Same(source, registration.Source);
        Assert.Throws<InvalidOperationException>(registration.Unregister);
        Assert.Same(source, registration.Source);

        await Within(registration.StopAndDrainAsync());
        Assert.Same(source, registration.Source);
        registration.Unregister();

        Assert.Throws<InvalidOperationException>(() => registration.Source);
        Assert.Throws<InvalidOperationException>(() => { _ = registration.StopAndDrainAsync(); });
        Assert.Throws<InvalidOperationException>(registration.Unregister);
        Assert.Throws<InvalidOperationException>(
            () => registration.Register("reuse", (_, _) => { }, _ => { }));
        Assert.Throws<InvalidOperationException>(() => source.Acquire(Task.CompletedTask));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-json:{opaque allowance}")]
    [InlineData("{\"kind\":\"unbounded\"}")]
    public async Task CachedSource_DeliversOpaqueAllowance_AndIncreasingSequences(string allowance)
    {
        var registration = new BrowserManagedEpochWorkRegistration();
        var starts = new List<(long Sequence, string Allowance)>();
        var finishes = new List<long>();
        registration.Register(
            allowance, (sequence, value) => starts.Add((sequence, value)), finishes.Add);
        BrowserManagedEpochWorkSource source = registration.Source;
        Assert.Same(source, registration.Source);

        for (int sequence = 1; sequence <= 2; sequence++)
        {
            var physical = Signal<BodyResult>();
            var producer = new Producer(_ => physical.Task, epochWork: registration.Source);
            Producer.Subscription waiter = producer.Attach(new Events());
            Assert.Equal(BrowserManagedProducerDisposition.EpochWorkLease,
                await Within(waiter.DetachAsync().AsTask()));
            Assert.Equal(((long)sequence, allowance), starts[^1]);
            Assert.Equal(sequence, starts.Count);
            Assert.Equal(sequence - 1, finishes.Count);
            Assert.False(physical.Task.IsCompleted);

            physical.SetResult(new BodyResult.Succeeded(sequence));
            Assert.Equal(sequence, Assert.IsType<BodyResult.Succeeded>(
                await Within(producer.ObserveCompletionAsync())).Value);
            Assert.Equal((long)sequence, finishes[^1]);
        }

        await Within(registration.StopAndDrainAsync());
        registration.Unregister();
    }

    [Fact]
    public async Task StopAndDrain_StopsAdmissionSynchronously_AndWaitsForPhysicalFinally()
    {
        var registration = new BrowserManagedEpochWorkRegistration();
        var release = Signal<bool>();
        var finalizing = Signal<bool>();
        var finish = Signal<bool>();
        var finishes = new List<long>();
        registration.Register("opaque", (_, _) => { }, finishes.Add);
        BrowserManagedEpochWorkSource source = registration.Source;
        async Task<BodyResult> Produce()
        {
            try
            {
                await release.Task;
                return new BodyResult.Succeeded(1);
            }
            finally
            {
                finalizing.SetResult(true);
                await finish.Task;
            }
        }
        var producer = new Producer(_ => Produce(), epochWork: source);
        Producer.Subscription waiter = producer.Attach(new Events());
        Assert.Equal(BrowserManagedProducerDisposition.EpochWorkLease,
            await Within(waiter.DetachAsync().AsTask()));

        Task drained = registration.StopAndDrainAsync();
        Assert.Throws<InvalidOperationException>(() => source.Acquire(Task.CompletedTask));
        Assert.False(drained.IsCompleted);
        Assert.Throws<InvalidOperationException>(registration.Unregister);
        Assert.Same(source, registration.Source);

        release.SetResult(true);
        await Within(finalizing.Task);
        Task<BodyResult> observation = producer.ObserveCompletionAsync();
        Assert.False(observation.IsCompleted);
        Assert.False(drained.IsCompleted);
        Assert.Empty(finishes);
        Assert.Throws<InvalidOperationException>(registration.Unregister);

        finish.SetResult(true);
        await Within(observation);
        await Within(drained);
        Assert.Equal([1L], finishes);
        Assert.Same(source, registration.Source);
        registration.Unregister();
    }

    [Fact]
    public async Task StartFailure_RetainsRegistrationUntilFaultedProducerFinishes()
    {
        var registration = new BrowserManagedEpochWorkRegistration();
        var physical = Signal<BodyResult>();
        var startFailure = new InvalidOperationException("start failed");
        var finishes = new List<long>();
        registration.Register("opaque", (_, _) => throw startFailure, finishes.Add);
        BrowserManagedEpochWorkSource source = registration.Source;
        var producer = new Producer(_ => physical.Task, epochWork: source);
        Producer.Subscription waiter = producer.Attach(new Events());

        var handoff = await Assert.ThrowsAsync<BrowserManagedOperationBoundaryException>(
            () => Within(waiter.DetachAsync().AsTask()));
        Assert.Equal("epoch-work-handoff", handoff.FailureKind);
        var start = Assert.IsType<BrowserManagedOperationBoundaryException>(handoff.InnerException);
        Assert.Same(startFailure, start.InnerException);

        Task drained = registration.StopAndDrainAsync();
        Assert.False(drained.IsCompleted);
        Assert.Throws<InvalidOperationException>(registration.Unregister);
        Assert.Same(source, registration.Source);
        Assert.False(physical.Task.IsCompleted);

        physical.SetResult(new BodyResult.Succeeded(1));
        await Within(producer.ObserveCompletionAsync());
        var drainFailure = await Assert.ThrowsAsync<BrowserManagedOperationBoundaryException>(
            () => Within(drained));
        Assert.Equal("epoch-work-drain", drainFailure.FailureKind);
        Assert.Same(start, drainFailure.InnerException);
        Assert.Empty(finishes);
        Assert.Same(source, registration.Source);
        registration.Unregister();
        Assert.Throws<InvalidOperationException>(() => registration.Source);
    }

    [Fact]
    public async Task FinishFailure_RemainsVisibleThroughDrain_AndExplicitUnregister()
    {
        var registration = new BrowserManagedEpochWorkRegistration();
        var physical = Signal<BodyResult>();
        var finishFailure = new InvalidOperationException("finish failed");
        registration.Register("opaque", (_, _) => { }, _ => throw finishFailure);
        BrowserManagedEpochWorkSource source = registration.Source;
        var producer = new Producer(_ => physical.Task, epochWork: source);
        Producer.Subscription waiter = producer.Attach(new Events());
        Assert.Equal(BrowserManagedProducerDisposition.EpochWorkLease,
            await Within(waiter.DetachAsync().AsTask()));

        Task drained = registration.StopAndDrainAsync();
        Assert.False(drained.IsCompleted);
        Assert.Throws<InvalidOperationException>(registration.Unregister);
        physical.SetResult(new BodyResult.Succeeded(1));

        var completion = await Assert.ThrowsAsync<BrowserManagedOperationBoundaryException>(
            () => Within(producer.ObserveCompletionAsync()));
        Assert.Equal("epoch-work-completion", completion.FailureKind);
        var finish = Assert.IsType<BrowserManagedOperationBoundaryException>(completion.InnerException);
        Assert.Same(finishFailure, finish.InnerException);
        var drainFailure = await Assert.ThrowsAsync<BrowserManagedOperationBoundaryException>(
            () => Within(drained));
        Assert.Equal("epoch-work-drain", drainFailure.FailureKind);
        Assert.Same(finish, drainFailure.InnerException);
        Assert.Same(source, registration.Source);
        registration.Unregister();
        Assert.Throws<InvalidOperationException>(() => registration.Source);
    }

    sealed class Events : IBrowserManagedOperationEvents<int>
    {
        public bool IsClosed => false;
        public void Report(int operationEvent) { }
    }

    static Task<T> Within<T>(Task<T> task) =>
        task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

    static Task Within(Task task) =>
        task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

    static TaskCompletionSource<T> Signal<T>() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
