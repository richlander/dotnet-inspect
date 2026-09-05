using InspectWeb.Engine;
using BodyResult =
    InspectWeb.Engine.BrowserManagedOperationBodyResult<int, string, string>;
using Result =
    InspectWeb.Engine.BrowserManagedOperationResult<int, string, string>;
using Producer =
    InspectWeb.Engine.BrowserManagedSharedProducer<int, string, string, int>;

namespace InspectWeb.Engine.Tests;

public sealed class BrowserManagedSharedProducerTests(ITestOutputHelper output)
{
    [Fact]
    public async Task CancelingOneWaiter_PreservesProducerAndNeighborEvents()
    {
        var completion = Signal<BodyResult>();
        using var producerCancellation = new CancellationTokenSource();
        int producerStarts = 0;
        var producer = new Producer(
            _ =>
            {
                producerStarts++;
                return completion.Task;
            },
            producerCancellation.Cancel,
            producerCancellation.Token);
        var bridge = new BrowserManagedOperationBridge();
        var firstEvents = new List<int>();
        var secondEvents = new List<int>();

        Task<Result> first = Run(bridge, "first", producer, firstEvents.Add);
        Task<Result> second = Run(bridge, "second", producer, secondEvents.Add);
        Assert.Equal(2, producer.WaiterCount);
        Assert.Equal(1, producerStarts);
        producer.Report(1);

        Cancel(bridge, "first");
        var canceled = Assert.IsType<Result.Canceled>(await WithinTestAsync(first));
        Assert.Equal(1, producer.WaiterCount);
        Assert.Equal(1, bridge.ActiveCount);
        Assert.False(producerCancellation.IsCancellationRequested);
        Assert.False(second.IsCompleted);
        output.WriteLine(
            $"After A cancels: A=Canceled({canceled.Reason}), waiters={producer.WaiterCount}, producerCanceled={producerCancellation.IsCancellationRequested}, BCompleted={second.IsCompleted}");

        producer.Report(2);
        completion.SetResult(Success(7));
        var succeeded = Assert.IsType<Result.Succeeded>(await second);
        Assert.Equal(7, succeeded.Value);
        producer.Report(3);
        Assert.Equal([1], firstEvents);
        Assert.Equal([1, 2], secondEvents);
        Assert.Equal(0, producer.WaiterCount);
        Assert.Equal(0, bridge.ActiveCount);
        output.WriteLine(
            $"After producer completes: B=Succeeded({succeeded.Value}), AEvents=[{string.Join(",", firstEvents)}], BEvents=[{string.Join(",", secondEvents)}], active={bridge.ActiveCount}, waiters={producer.WaiterCount}");
    }

    [Fact]
    public async Task FinalWaiter_ClosesCallbackButWaitsForNaturalCompletion()
    {
        var completion = Signal<BodyResult>();
        var callbackClosed = Signal<bool>();
        var bridge = BridgeSignalingClosedCallback(callbackClosed);
        var producer = new Producer(_ => completion.Task);
        var events = new List<int>();
        Task<Result> operation = Run(bridge, "final", producer, events.Add);
        producer.Report(1);

        Cancel(bridge, "final");
        await WithinTestAsync(callbackClosed.Task);
        Assert.False(operation.IsCompleted);
        Assert.Equal(1, bridge.ActiveCount);
        Assert.Equal(1, producer.WaiterCount);
        producer.Report(2);
        Assert.Equal([1], events);
        output.WriteLine(
            $"Final cancellation pending: wrapperCompleted={operation.IsCompleted}, active={bridge.ActiveCount}, waiters={producer.WaiterCount}, events=[{string.Join(",", events)}]");

        Assert.IsType<BrowserManagedCancellationRequestResult.NotActive>(
            bridge.RequestCancellation(
                Id("final"), BrowserManagedOperationCancelReason.Timeout));

        completion.SetResult(Success(8));
        Assert.Equal(
            BrowserManagedOperationCancelReason.User,
            Assert.IsType<Result.Canceled>(await operation).Reason);
        Assert.Equal(0, bridge.ActiveCount);
        Assert.Equal(0, producer.WaiterCount);
        output.WriteLine(
            $"After physical completion: wrapperCompleted={operation.IsCompleted}, active={bridge.ActiveCount}, waiters={producer.WaiterCount}");
    }

    [Fact]
    public async Task FinalWaiter_StopPolicyRunsOnceAndDrainsProducerFinally()
    {
        var release = Signal<bool>();
        var finallyEntered = Signal<bool>();
        var finishFinally = Signal<bool>();
        using var producerCancellation = new CancellationTokenSource();
        int stopCalls = 0;
        async Task<BodyResult> ProduceAsync()
        {
            try
            {
                await release.Task.WaitAsync(producerCancellation.Token);
                return Success(1);
            }
            finally
            {
                finallyEntered.SetResult(true);
                await finishFinally.Task;
            }
        }

        var producer = new Producer(
            _ => ProduceAsync(),
            () =>
            {
                stopCalls++;
                producerCancellation.Cancel();
            },
            producerCancellation.Token);
        var bridge = new BrowserManagedOperationBridge();
        Task<Result>[] operations =
        [
            Run(bridge, "one", producer),
            Run(bridge, "two", producer),
            Run(bridge, "three", producer),
        ];
        Cancel(bridge, "one");
        await WithinTestAsync(operations[0]);
        Assert.Equal(0, stopCalls);
        Assert.False(producerCancellation.IsCancellationRequested);

        Cancel(bridge, "two");
        Cancel(bridge, "three");
        await WithinTestAsync(finallyEntered.Task);
        Assert.Equal(1, stopCalls);
        Assert.Equal(1, producer.WaiterCount);
        Assert.Equal(1, bridge.ActiveCount);
        Assert.False(Task.WhenAll(operations).IsCompleted);

        Result.Failed lateAttachment =
            Assert.IsType<Result.Failed>(await Run(bridge, "late", producer));
        Assert.Equal(BrowserManagedOperationFailureKind.Unexpected, lateAttachment.FailureKind);
        Assert.Equal(nameof(InvalidOperationException), lateAttachment.Error);
        Assert.Equal(1, producer.WaiterCount);

        finishFinally.SetResult(true);
        Assert.All(await Task.WhenAll(operations), result => Assert.IsType<Result.Canceled>(result));
        Assert.Equal(0, producer.WaiterCount);
        Assert.Equal(0, bridge.ActiveCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProducerFailures_StayFeatureResultsForWaitingOperations(bool unexpected)
    {
        var completion = Signal<BodyResult>();
        var producer = new Producer(_ => completion.Task);
        var bridge = new BrowserManagedOperationBridge();
        Task<Result> first = Run(bridge, "first", producer);
        Task<Result> second = Run(bridge, "second", producer);

        if (unexpected)
            completion.SetException(new InvalidOperationException("producer failed"));
        else
            completion.SetResult(new BodyResult.Failed("expected", "producer failed"));

        Result.Failed firstFailure = Assert.IsType<Result.Failed>(await first);
        Assert.Equal(firstFailure, Assert.IsType<Result.Failed>(await second));
        Assert.Equal(
            unexpected
                ? BrowserManagedOperationFailureKind.Unexpected
                : BrowserManagedOperationFailureKind.Expected,
            firstFailure.FailureKind);
        Assert.Equal("producer failed", firstFailure.Diagnostic);
        Assert.Equal(0, producer.WaiterCount);
        Assert.Equal(0, bridge.ActiveCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CanceledFinalWaiter_DoesNotHideLaterProducerFailure(bool cancellation)
    {
        var completion = Signal<BodyResult>();
        var callbackClosed = Signal<bool>();
        var bridge = BridgeSignalingClosedCallback(callbackClosed);
        var producer = new Producer(_ => completion.Task);
        Task<Result> operation = Run(bridge, "final", producer);
        Cancel(bridge, "final");
        await WithinTestAsync(callbackClosed.Task);
        Exception producerFailure = cancellation
            ? new OperationCanceledException("unexplained producer cancellation")
            : new InvalidOperationException("late producer failure");
        completion.SetException(producerFailure);

        BrowserManagedOperationBoundaryException failure =
            await Assert.ThrowsAsync<BrowserManagedOperationBoundaryException>(() => operation);
        Assert.Equal("cleanup", failure.FailureKind);
        Assert.Same(producerFailure, failure.InnerException);
        Assert.Equal(0, producer.WaiterCount);
        Assert.Equal(0, bridge.ActiveCount);
    }

    [Fact]
    public async Task ThrowingStopPolicy_StillDrainsAndReleasesEveryStage()
    {
        var completion = Signal<BodyResult>();
        var stopEntered = Signal<bool>();
        var stopFailure = new InvalidOperationException("stop failed");
        var visited = new List<BrowserManagedOperationCleanupStage>();
        var bridge = new BrowserManagedOperationBridge(
            new BrowserManagedOperationBridgeTestHooks
            {
                CleanupCompleted = visited.Add,
            });
        var producer = new Producer(
            _ => completion.Task,
            () =>
            {
                stopEntered.SetResult(true);
                throw stopFailure;
            });
        Task<Result> operation = Run(bridge, "final", producer);
        Cancel(bridge, "final");
        await WithinTestAsync(stopEntered.Task);
        Assert.False(operation.IsCompleted);
        Assert.Equal(1, bridge.ActiveCount);
        Assert.Equal(1, producer.WaiterCount);
        completion.SetResult(Success(1));

        BrowserManagedOperationBoundaryException failure =
            await Assert.ThrowsAsync<BrowserManagedOperationBoundaryException>(() => operation);
        Assert.Equal("cleanup", failure.FailureKind);
        Assert.Same(stopFailure, failure.InnerException);
        Assert.Equal(
            [
                BrowserManagedOperationCleanupStage.EventCallback,
                BrowserManagedOperationCleanupStage.ActiveTable,
                BrowserManagedOperationCleanupStage.CancellationSource,
            ],
            visited);
        Assert.Equal(0, producer.WaiterCount);
        Assert.Equal(0, bridge.ActiveCount);
        Assert.IsType<Result.Succeeded>(
            await Run(bridge, "final", new Producer(_ => Task.FromResult(Success(2)))));
    }

    [Fact]
    public async Task EventFailure_RemainsPrimaryAcrossStopAndProducerFailures()
    {
        var completion = Signal<BodyResult>();
        var stopEntered = Signal<bool>();
        var observerFailure = new InvalidOperationException("observer failed");
        var stopFailure = new InvalidOperationException("stop failed");
        var producerFailure = new InvalidOperationException("producer failed");
        var bridge = new BrowserManagedOperationBridge();
        var producer = new Producer(
            _ => completion.Task,
            () =>
            {
                stopEntered.SetResult(true);
                throw stopFailure;
            });
        Task<Result> operation =
            Run(bridge, "final", producer, _ => throw observerFailure);
        producer.Report(1);
        await WithinTestAsync(stopEntered.Task);
        completion.SetException(producerFailure);

        BrowserManagedOperationBoundaryException failure =
            await Assert.ThrowsAsync<BrowserManagedOperationBoundaryException>(() => operation);
        Assert.Equal("event-callback", failure.FailureKind);
        Assert.Same(observerFailure, failure.InnerException);
        var cleanup = Assert.IsType<AggregateException>(
            Assert.Single(failure.SecondaryFailures));
        Assert.Equal([stopFailure, producerFailure], cleanup.InnerExceptions);
        Assert.Equal(0, producer.WaiterCount);
        Assert.Equal(0, bridge.ActiveCount);
    }

    [Fact]
    public async Task DuplicateId_DoesNotAttachAnotherWaiter()
    {
        var completion = Signal<BodyResult>();
        var producer = new Producer(_ => completion.Task);
        var bridge = new BrowserManagedOperationBridge();
        Task<Result> operation = Run(bridge, "same", producer);
        BrowserManagedOperationBoundaryException failure =
            await Assert.ThrowsAsync<BrowserManagedOperationBoundaryException>(
                () => Run(bridge, "same", producer));
        Assert.Equal("duplicate-active-operation", failure.FailureKind);
        Assert.Equal(1, producer.WaiterCount);

        completion.SetResult(Success(1));
        Assert.IsType<Result.Succeeded>(await operation);
        Assert.Equal(0, producer.WaiterCount);
    }

    [Fact]
    public async Task SynchronousProducerEvents_CanReenterAfterAdmissionWithoutRestartingProducer()
    {
        var completion = Signal<BodyResult>();
        var bridge = new BrowserManagedOperationBridge();
        int starts = 0;
        int callbackCalls = 0;
        var producer = new Producer(
            events =>
            {
                starts++;
                Assert.Equal(1, bridge.ActiveCount);
                events.Report(1);
                return completion.Task;
            });
        Task<Result>? neighbor = null;

        Task<Result> operation = Run(
            bridge,
            "first",
            producer,
            value =>
            {
                callbackCalls += value;
                neighbor = Run(bridge, "neighbor", producer);
                Cancel(bridge, "first");
            });

        Assert.Equal(1, callbackCalls);
        Assert.Equal(1, starts);
        Assert.NotNull(neighbor);
        Assert.IsType<Result.Canceled>(await WithinTestAsync(operation));
        Assert.Equal(1, producer.WaiterCount);
        completion.SetResult(Success(4));
        Assert.Equal(4, Assert.IsType<Result.Succeeded>(await neighbor).Value);
        Assert.Equal(0, bridge.ActiveCount);
        Assert.Equal(0, producer.WaiterCount);
    }

    [Fact]
    public async Task SynchronousFactoryFailure_ReleasesAdmittedOperation()
    {
        var bridge = new BrowserManagedOperationBridge();
        var producer = new Producer(
            _ =>
            {
                Assert.Equal(1, bridge.ActiveCount);
                throw new InvalidOperationException("synchronous producer failure");
            });

        Result.Failed failure = Assert.IsType<Result.Failed>(
            await Run(bridge, "failed", producer));
        Assert.Equal(BrowserManagedOperationFailureKind.Unexpected, failure.FailureKind);
        Assert.Equal("synchronous producer failure", failure.Diagnostic);
        Assert.Equal(0, bridge.ActiveCount);
        Assert.Equal(0, producer.WaiterCount);
    }

    [Fact]
    public async Task ThrowingWaiterEvent_DoesNotStopNeighborOrProducer()
    {
        var completion = Signal<BodyResult>();
        int stopCalls = 0;
        var producer = new Producer(_ => completion.Task, () => stopCalls++);
        var bridge = new BrowserManagedOperationBridge();
        var observerFailure = new InvalidOperationException("observer failed");
        var neighborEvents = new List<int>();
        Task<Result> first = Run(bridge, "first", producer, _ => throw observerFailure);
        Task<Result> neighbor = Run(bridge, "neighbor", producer, neighborEvents.Add);

        producer.Report(1);
        BrowserManagedOperationBoundaryException failure =
            await Assert.ThrowsAsync<BrowserManagedOperationBoundaryException>(() => first);
        Assert.Equal("event-callback", failure.FailureKind);
        Assert.Same(observerFailure, failure.InnerException);
        Assert.Equal(0, stopCalls);
        Assert.Equal(1, producer.WaiterCount);
        producer.Report(2);
        completion.SetResult(Success(5));
        Assert.Equal(5, Assert.IsType<Result.Succeeded>(await neighbor).Value);
        Assert.Equal([1, 2], neighborEvents);
        Assert.Equal(0, stopCalls);
        Assert.Equal(0, producer.WaiterCount);
        Assert.Equal(0, bridge.ActiveCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProducerCancellation_RemainsUnexpectedAfterReentrantWaiterCancellation(
        bool canceledTask)
    {
        var bridge = new BrowserManagedOperationBridge();
        using var producerCancellation = new CancellationTokenSource();
        producerCancellation.Cancel();
        var producer = new Producer(
            events =>
            {
                events.Report(1);
                if (canceledTask)
                    return Task.FromCanceled<BodyResult>(producerCancellation.Token);
                throw new OperationCanceledException("unexplained producer cancellation");
            });

        Result result = await Run(
            bridge, "first", producer, _ => Cancel(bridge, "first"));
        output.WriteLine($"Producer cancellation (canceledTask={canceledTask}): {result}");
        Result.Failed failure = Assert.IsType<Result.Failed>(result);

        Assert.Equal(BrowserManagedOperationFailureKind.Unexpected, failure.FailureKind);
        Assert.Equal(
            canceledTask ? nameof(TaskCanceledException) : nameof(OperationCanceledException),
            failure.Error);
        if (!canceledTask)
            Assert.Equal("unexplained producer cancellation", failure.Diagnostic);
        Assert.Equal(0, producer.WaiterCount);
        Assert.Equal(0, bridge.ActiveCount);
    }

    static BrowserManagedOperationBridge BridgeSignalingClosedCallback(
        TaskCompletionSource<bool> signal) =>
        new(
            new BrowserManagedOperationBridgeTestHooks
            {
                CleanupCompleted = stage =>
                {
                    if (stage == BrowserManagedOperationCleanupStage.EventCallback)
                        signal.TrySetResult(true);
                },
            });

    static Task<Result> Run(
        BrowserManagedOperationBridge bridge,
        string id,
        Producer producer,
        Action<int>? events = null) =>
        bridge.RunSharedAsync(
            Id(id),
            events,
            producer,
            static exception =>
                new BrowserManagedOperationFailure<string, string>(
                    exception.GetType().Name, exception.Message));

    static void Cancel(BrowserManagedOperationBridge bridge, string id) =>
        Assert.IsType<BrowserManagedCancellationRequestResult.Requested>(
            bridge.RequestCancellation(Id(id), BrowserManagedOperationCancelReason.User));

    static BrowserManagedOperationId Id(string value) =>
        BrowserManagedOperationId.From(value);

    static BodyResult Success(int value) => new BodyResult.Succeeded(value);

    static Task<T> WithinTestAsync<T>(Task<T> task) =>
        task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

    static TaskCompletionSource<T> Signal<T>() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
