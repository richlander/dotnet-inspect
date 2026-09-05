using InspectWeb.Engine;
using BodyResult =
    InspectWeb.Engine.BrowserManagedOperationBodyResult<int, string, string>;
using Result =
    InspectWeb.Engine.BrowserManagedOperationResult<int, string, string>;
using Producer =
    InspectWeb.Engine.BrowserManagedSharedProducer<int, string, string, int>;

namespace InspectWeb.Engine.Tests;

public sealed class BrowserManagedEpochWorkTests(ITestOutputHelper output)
{
    [Fact]
    public async Task FinalWaiter_HandsOffBeforeSettlement_AndLaterWaiterReusesLease()
    {
        var physical = Signal<BodyResult>();
        var starts = new List<(long Sequence, string Allowance)>();
        var finishes = new List<long>();
        var bridge = new BrowserManagedOperationBridge();
        var reporter = new BrowserManagedEpochWorkReporter<string>(
            (sequence, allowance) =>
            {
                Assert.Equal(1, bridge.ActiveCount);
                starts.Add((sequence, allowance));
            },
            finishes.Add);
        var producer = new Producer(_ => physical.Task, epochWork: reporter.ForProducer("opaque"));
        var firstEvents = new List<int>();
        Task<Result> first = Run(bridge, "first", producer, firstEvents.Add);
        Task<Result> neighbor = Run(bridge, "neighbor", producer);
        producer.Report(1);
        Cancel(bridge, "first");
        Assert.IsType<Result.Canceled>(await Within(first));
        Assert.Empty(starts);
        Cancel(bridge, "neighbor");
        Assert.IsType<Result.Canceled>(await Within(neighbor));

        Assert.Equal([(1L, "opaque")], starts);
        Assert.Equal(0, bridge.ActiveCount);
        Assert.Equal(0, producer.WaiterCount);
        Assert.Equal(1, reporter.Snapshot().ActiveLeases);
        Assert.False(physical.Task.IsCompleted);
        Assert.False(producer.IsClosed);
        producer.Report(2);
        Assert.Equal([1], firstEvents);
        output.WriteLine("Final waiter settled: operations=0, waiters=0, leases=1, physicalCompleted=False");

        var laterEvents = new List<int>();
        Task<Result> later = Run(bridge, "later", producer, laterEvents.Add);
        producer.Report(3);
        Cancel(bridge, "later");
        Assert.IsType<Result.Canceled>(await Within(later));
        Assert.Equal([3], laterEvents);
        Assert.Single(starts);
        Assert.Empty(finishes);
        reporter.StopAdmission();
        Task drained = reporter.DrainAsync();
        Assert.False(drained.IsCompleted);
        Assert.Throws<InvalidOperationException>(reporter.Unregister);

        physical.SetResult(new BodyResult.Succeeded(7));
        Assert.Equal(7, Assert.IsType<BodyResult.Succeeded>(
            await Within(producer.ObserveCompletionAsync())).Value);
        await Within(drained);
        Assert.Equal([1L], finishes);
        Assert.True(producer.IsClosed);
        Assert.Equal(0, reporter.Snapshot().ActiveLeases);
        reporter.Unregister();
        Assert.False(reporter.Snapshot().Registered);
    }

    [Fact]
    public async Task LeaseRemainsThroughAsynchronousProducerFinally()
    {
        var release = Signal<bool>();
        var finalizing = Signal<bool>();
        var finish = Signal<bool>();
        int finished = 0;
        var reporter = new BrowserManagedEpochWorkReporter<string>(
            (_, _) => { }, _ => finished++);
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
        var producer = new Producer(_ => Produce(), epochWork: reporter.ForProducer("opaque"));
        var bridge = new BrowserManagedOperationBridge();
        Task<Result> operation = Run(bridge, "one", producer);
        Cancel(bridge, "one");
        Assert.IsType<Result.Canceled>(await Within(operation));
        release.SetResult(true);
        await Within(finalizing.Task);
        Assert.Equal(1, reporter.Snapshot().ActiveLeases);
        Assert.Equal(0, finished);
        Task<BodyResult> observation = producer.ObserveCompletionAsync();
        Assert.False(observation.IsCompleted);
        finish.SetResult(true);
        await Within(observation);
        Assert.Equal(1, finished);
    }

    [Fact]
    public async Task CompletionDuringStart_WaitsForCommittedHandoff()
    {
        var physical = new TaskCompletionSource<BodyResult>();
        var trace = new List<string>();
        Producer? producer = null;
        var reporter = new BrowserManagedEpochWorkReporter<string>(
            (_, _) =>
            {
                trace.Add("start");
                physical.SetResult(new BodyResult.Succeeded(1));
                Assert.NotNull(producer);
                Assert.Equal(1, producer.WaiterCount);
                Assert.Equal(["start"], trace);
            },
            _ =>
            {
                Assert.NotNull(producer);
                Assert.Equal(0, producer.WaiterCount);
                trace.Add("finish");
            });
        producer = new Producer(_ => physical.Task, epochWork: reporter.ForProducer("opaque"));
        var bridge = new BrowserManagedOperationBridge();
        Task<Result> operation = Run(bridge, "one", producer);
        Cancel(bridge, "one");
        Assert.IsType<Result.Canceled>(await Within(operation));
        await Within(producer.ObserveCompletionAsync());
        Assert.Equal(["start", "finish"], trace);
        Assert.Equal(0, reporter.Snapshot().ActiveLeases);
        Assert.Equal(0, producer.WaiterCount);
    }

    [Fact]
    public async Task StartCanReenterNeighborAdmissionWithoutAnotherAllocation()
    {
        var physical = Signal<BodyResult>();
        var bridge = new BrowserManagedOperationBridge();
        Producer? producer = null;
        Task<Result>? neighbor = null;
        int starts = 0;
        var reporter = new BrowserManagedEpochWorkReporter<string>(
            (_, _) =>
            {
                starts++;
                Assert.NotNull(producer);
                neighbor = Run(bridge, "neighbor", producer);
            },
            _ => { });
        producer = new Producer(_ => physical.Task, epochWork: reporter.ForProducer("opaque"));
        Task<Result> first = Run(bridge, "first", producer);
        Cancel(bridge, "first");
        Assert.IsType<Result.Canceled>(await Within(first));
        Assert.NotNull(neighbor);
        Assert.Equal(1, producer.WaiterCount);
        Cancel(bridge, "neighbor");
        Assert.IsType<Result.Canceled>(await Within(neighbor));
        Assert.Equal(1, starts);
        physical.SetResult(new BodyResult.Succeeded(2));
        await Within(producer.ObserveCompletionAsync());
    }

    [Fact]
    public async Task StartFailure_TransfersFaultRecord_AndAttemptsPermittedStop()
    {
        var physical = Signal<BodyResult>();
        int stops = 0;
        int finishes = 0;
        var startFailure = new InvalidOperationException("start failed");
        var reporter = new BrowserManagedEpochWorkReporter<string>(
            (_, _) => throw startFailure, _ => finishes++);
        var producer = new Producer(
            _ => physical.Task, () => stops++, epochWork: reporter.ForProducer("opaque"));
        var bridge = new BrowserManagedOperationBridge();
        Task<Result> operation = Run(bridge, "one", producer);
        Cancel(bridge, "one");
        var failure = await Assert.ThrowsAsync<BrowserManagedOperationBoundaryException>(
            () => Within(operation));
        Assert.Equal("cleanup", failure.FailureKind);
        var handoff = Assert.IsType<BrowserManagedOperationBoundaryException>(failure.InnerException);
        var start = Assert.IsType<BrowserManagedOperationBoundaryException>(handoff.InnerException);
        Assert.Same(startFailure, start.InnerException);
        Assert.Equal(1, stops);
        Assert.Equal(1, reporter.Snapshot().FaultRecords);
        Assert.Equal(0, producer.WaiterCount);
        Assert.Equal(0, bridge.ActiveCount);
        Assert.False(physical.Task.IsCompleted);
        Task drained = reporter.DrainAsync();
        Assert.False(drained.IsCompleted);
        Assert.Throws<InvalidOperationException>(reporter.Unregister);

        physical.SetResult(new BodyResult.Succeeded(3));
        await Within(producer.ObserveCompletionAsync());
        await Assert.ThrowsAsync<BrowserManagedOperationBoundaryException>(() => Within(drained));
        Assert.Equal(0, reporter.Snapshot().FaultRecords);
        Assert.Equal(0, finishes);
        reporter.Unregister();
    }

    [Fact]
    public async Task LateProducerFailure_RemainsObservableAfterLastWaiterSettles()
    {
        var physical = Signal<BodyResult>();
        var failure = new InvalidOperationException("late producer failure");
        int finishes = 0;
        var reporter = new BrowserManagedEpochWorkReporter<string>(
            (_, _) => { }, _ => finishes++);
        var producer = new Producer(_ => physical.Task, epochWork: reporter.ForProducer("opaque"));
        var bridge = new BrowserManagedOperationBridge();
        Task<Result> operation = Run(bridge, "one", producer);
        Cancel(bridge, "one");
        Assert.IsType<Result.Canceled>(await Within(operation));
        physical.SetException(failure);
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(
            () => Within(producer.ObserveCompletionAsync())));
        Assert.Equal(1, finishes);
        reporter.StopAdmission();
        await Within(reporter.DrainAsync());
        reporter.Unregister();
    }

    [Fact]
    public async Task StartFailure_WithReentrantNeighbor_PreservesTheOriginalFaultRecord()
    {
        var physical = Signal<BodyResult>();
        var bridge = new BrowserManagedOperationBridge();
        Producer? producer = null;
        Task<Result>? neighbor = null;
        int stops = 0;
        var reporter = new BrowserManagedEpochWorkReporter<string>(
            (_, _) =>
            {
                Assert.NotNull(producer);
                neighbor = Run(bridge, "neighbor", producer);
                throw new InvalidOperationException("start failed after neighbor admission");
            },
            _ => throw new InvalidOperationException("Fault records cannot report finish."));
        producer = new Producer(
            _ => physical.Task, () => stops++, epochWork: reporter.ForProducer("opaque"));
        Task<Result> first = Run(bridge, "first", producer);
        Cancel(bridge, "first");
        await Assert.ThrowsAsync<BrowserManagedOperationBoundaryException>(() => Within(first));
        Assert.NotNull(neighbor);
        Assert.Equal(1, producer.WaiterCount);
        Assert.Equal(0, stops);
        Cancel(bridge, "neighbor");
        await Assert.ThrowsAsync<BrowserManagedOperationBoundaryException>(() => Within(neighbor));
        Assert.Equal(1, stops);
        Assert.Equal(1, reporter.Snapshot().FaultRecords);
        physical.SetResult(new BodyResult.Succeeded(1));
        await Within(producer.ObserveCompletionAsync());
        await Assert.ThrowsAsync<BrowserManagedOperationBoundaryException>(
            () => Within(reporter.DrainAsync()));
        Assert.Equal(0, reporter.Snapshot().FaultRecords);
        reporter.Unregister();
    }

    [Fact]
    public async Task ProducerCancellation_IsAnObservableProducerFailure_NotObserverCancellation()
    {
        var physical = Signal<BodyResult>();
        using var cancellation = new CancellationTokenSource();
        var reporter = new BrowserManagedEpochWorkReporter<string>((_, _) => { }, _ => { });
        var producer = new Producer(_ => physical.Task, epochWork: reporter.ForProducer("opaque"));
        var bridge = new BrowserManagedOperationBridge();
        Task<Result> operation = Run(bridge, "one", producer);
        Cancel(bridge, "one");
        Assert.IsType<Result.Canceled>(await Within(operation));
        Task<BodyResult> observation = producer.ObserveCompletionAsync();
        cancellation.Cancel();
        physical.SetCanceled(cancellation.Token);
        var failure = await Assert.ThrowsAsync<BrowserManagedProducerCancellationException>(
            () => Within(observation));
        Assert.Equal(cancellation.Token, failure.Cancellation.CancellationToken);
        Assert.True(observation.IsFaulted);
        reporter.StopAdmission();
        await Within(reporter.DrainAsync());
        reporter.Unregister();
    }

    [Fact]
    public async Task FinishFailure_IsTerminalAndVisibleToProducerAndEpochDrain()
    {
        var physical = Signal<BodyResult>();
        var finishFailure = new InvalidOperationException("finish failed");
        int finishes = 0;
        var reporter = new BrowserManagedEpochWorkReporter<string>(
            (_, _) => { },
            _ =>
            {
                finishes++;
                throw finishFailure;
            });
        var producer = new Producer(_ => physical.Task, epochWork: reporter.ForProducer("opaque"));
        var bridge = new BrowserManagedOperationBridge();
        Task<Result> operation = Run(bridge, "one", producer);
        Cancel(bridge, "one");
        Assert.IsType<Result.Canceled>(await Within(operation));
        physical.SetResult(new BodyResult.Succeeded(4));
        var failure = await Assert.ThrowsAsync<BrowserManagedOperationBoundaryException>(
            () => Within(producer.ObserveCompletionAsync()));
        Assert.Equal("epoch-work-completion", failure.FailureKind);
        Assert.Same(finishFailure,
            Assert.IsType<BrowserManagedOperationBoundaryException>(failure.InnerException).InnerException);
        Assert.Equal(0, reporter.Snapshot().ActiveLeases);
        await Assert.ThrowsAsync<BrowserManagedOperationBoundaryException>(
            () => Within(reporter.DrainAsync()));
        reporter.Unregister();
        Assert.Equal(1, finishes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FinishFailure_RejectsAttachedWaiter_WithoutHidingProducerFailure(bool producerFails)
    {
        var physical = Signal<BodyResult>();
        var producerFailure = new InvalidOperationException("physical failure");
        var reporter = new BrowserManagedEpochWorkReporter<string>(
            (_, _) => { }, _ => throw new InvalidOperationException("finish failed"));
        var producer = new Producer(_ => physical.Task, epochWork: reporter.ForProducer("opaque"));
        var bridge = new BrowserManagedOperationBridge();
        Task<Result> first = Run(bridge, "first", producer);
        Cancel(bridge, "first");
        Assert.IsType<Result.Canceled>(await Within(first));
        Task<Result> later = Run(bridge, "later", producer);
        if (producerFails)
            physical.SetException(producerFailure);
        else
            physical.SetResult(new BodyResult.Succeeded(1));
        var boundary = await Assert.ThrowsAsync<BrowserManagedOperationBoundaryException>(
            () => Within(later));
        Assert.Equal("cleanup", boundary.FailureKind);
        var terminal = await Assert.ThrowsAsync<BrowserManagedOperationBoundaryException>(
            () => Within(producer.ObserveCompletionAsync()));
        Assert.Equal("epoch-work-completion", terminal.FailureKind);
        if (producerFails)
            Assert.Same(producerFailure, Assert.Single(terminal.SecondaryFailures));
        else
            Assert.Empty(terminal.SecondaryFailures);
        Assert.Equal(0, bridge.ActiveCount);
        Assert.Equal(0, producer.WaiterCount);
        await Assert.ThrowsAsync<BrowserManagedOperationBoundaryException>(
            () => Within(reporter.DrainAsync()));
        reporter.Unregister();
    }

    [Fact]
    public async Task SequenceExhaustion_IsVisibleAndRetainsPhysicalFaultOwnership()
    {
        var starts = new List<long>();
        var finishes = new List<long>();
        var reporter = new BrowserManagedEpochWorkReporter<string>(
            (sequence, _) => starts.Add(sequence), finishes.Add, maximumWorkSequence: 2);
        BrowserManagedEpochWorkSource source = reporter.ForProducer("opaque");
        BrowserManagedEpochWorkHandle first = source.Acquire(Task.CompletedTask);
        first.Dispose();
        first.Dispose();
        BrowserManagedEpochWorkHandle second = source.Acquire(Task.CompletedTask);
        second.Dispose();
        var physical = Signal<bool>();
        BrowserManagedEpochWorkHandle exhausted = source.Acquire(physical.Task);
        Assert.Null(exhausted.Sequence);
        Assert.Equal("epoch-work-exhausted", exhausted.StartFailure?.FailureKind);
        Assert.Equal([1L, 2L], starts);
        Assert.Equal([1L, 2L], finishes);
        Assert.Equal(2, reporter.Snapshot().LastSequence);
        Assert.Equal(1, reporter.Snapshot().FaultRecords);
        Task drain = reporter.DrainAsync();
        Assert.False(drain.IsCompleted);
        physical.SetResult(true);
        exhausted.Dispose();
        await Assert.ThrowsAsync<BrowserManagedOperationBoundaryException>(
            () => Within(drain));
        reporter.Unregister();
        Assert.Equal([1L, 2L], finishes);
    }

    [Fact]
    public void SequenceCeiling_CannotExceedJavaScriptSafeIntegerRange()
    {
        Assert.Equal(9_007_199_254_740_991L,
            BrowserManagedEpochWorkReporter<string>.MaximumWorkSequence);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BrowserManagedEpochWorkReporter<string>(
                (_, _) => { }, _ => { }, 9_007_199_254_740_992L));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReentrantStop_CannotUnregisterAnInFlightCallout(bool stopDuringFinish)
    {
        BrowserManagedEpochWorkReporter<string>? reporter = null;
        void StopAndObserveCallout()
        {
            Assert.NotNull(reporter);
            reporter.StopAdmission();
            Assert.Equal(1, reporter.Snapshot().PendingCallouts);
            Assert.False(reporter.DrainAsync().IsCompleted);
            Assert.Throws<InvalidOperationException>(reporter.Unregister);
        }
        reporter = new(
            (_, _) =>
            {
                if (!stopDuringFinish)
                    StopAndObserveCallout();
            },
            _ =>
            {
                if (stopDuringFinish)
                    StopAndObserveCallout();
            });
        BrowserManagedEpochWorkHandle handle = reporter.ForProducer("opaque").Acquire(Task.CompletedTask);
        Assert.Equal(1, reporter.Snapshot().ActiveLeases);
        handle.Dispose();
        await Within(reporter.DrainAsync());
        reporter.Unregister();
    }

    [Fact]
    public async Task StoppedRegistration_FallsBackToTerminalDrainInsteadOfOrphaningWork()
    {
        var physical = Signal<BodyResult>();
        var stopped = Signal<bool>();
        var reporter = new BrowserManagedEpochWorkReporter<string>((_, _) => { }, _ => { });
        BrowserManagedEpochWorkSource source = reporter.ForProducer("opaque");
        reporter.StopAdmission();
        await Within(reporter.DrainAsync());
        reporter.Unregister();
        var producer = new Producer(
            _ => physical.Task, () => stopped.SetResult(true), epochWork: source);
        var bridge = new BrowserManagedOperationBridge();
        Task<Result> operation = Run(bridge, "one", producer);
        Cancel(bridge, "one");
        await Within(stopped.Task);
        Assert.False(operation.IsCompleted);
        Assert.Equal(1, producer.WaiterCount);
        physical.SetResult(new BodyResult.Succeeded(1));
        await Assert.ThrowsAsync<BrowserManagedOperationBoundaryException>(() => Within(operation));
        Assert.Equal(0, bridge.ActiveCount);
        Assert.Equal(0, producer.WaiterCount);
    }

    static Task<Result> Run(
        BrowserManagedOperationBridge bridge, string id, Producer producer, Action<int>? events = null) =>
        bridge.RunSharedAsync(
            BrowserManagedOperationId.From(id), events, producer,
            static exception => new BrowserManagedOperationFailure<string, string>(
                exception.GetType().Name, exception.Message));

    static void Cancel(BrowserManagedOperationBridge bridge, string id) =>
        Assert.IsType<BrowserManagedCancellationRequestResult.Requested>(
            bridge.RequestCancellation(
                BrowserManagedOperationId.From(id), BrowserManagedOperationCancelReason.User));

    static Task<T> Within<T>(Task<T> task) =>
        task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

    static Task Within(Task task) =>
        task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

    static TaskCompletionSource<T> Signal<T>() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
