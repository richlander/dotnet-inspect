using System.Collections.Concurrent;
using InspectWeb.Engine;
using BodyResult =
    InspectWeb.Engine.BrowserManagedOperationBodyResult<int, string, string>;
using Result =
    InspectWeb.Engine.BrowserManagedOperationResult<int, string, string>;

namespace InspectWeb.Engine.Tests;

public sealed class BrowserManagedOperationBridgeTests
{
    [ThreadStatic]
    static bool _insideReportCallout;

    [Fact]
    public async Task DistinctOperations_CancelOnlySelectedToken()
    {
        var bridge = new BrowserManagedOperationBridge();
        var firstStarted = NewSignal<CancellationToken>();
        var secondStarted = NewSignal<CancellationToken>();
        var release = NewSignal();

        Task<Result> first = Run(
            bridge,
            "first",
            async (token, _) =>
            {
                firstStarted.SetResult(token);
                await release.Task;
                return Success(1);
            });
        Task<Result> second = Run(
            bridge,
            "second",
            async (token, _) =>
            {
                secondStarted.SetResult(token);
                await release.Task;
                return Success(2);
            });

        CancellationToken firstToken = await firstStarted.Task;
        CancellationToken secondToken = await secondStarted.Task;
        Assert.IsType<BrowserManagedCancellationRequestResult.Requested>(
            bridge.RequestCancellation(
                Id("first"),
                BrowserManagedOperationCancelReason.User));
        Assert.True(firstToken.IsCancellationRequested);
        Assert.False(secondToken.IsCancellationRequested);

        release.SetResult();
        Assert.Equal(
            BrowserManagedOperationCancelReason.User,
            Assert.IsType<Result.Canceled>(await first).Reason);
        Assert.Equal(2, Assert.IsType<Result.Succeeded>(await second).Value);
        Assert.Equal(0, bridge.ActiveCount);
    }

    [Fact]
    public async Task DuplicateActiveId_RejectsWithoutStartingSecondBody()
    {
        var bridge = new BrowserManagedOperationBridge();
        var started = NewSignal();
        var release = NewSignal();
        int secondBodyCalls = 0;

        Task<Result> first = Run(
            bridge,
            "same",
            async (_, _) =>
            {
                started.SetResult();
                await release.Task;
                return Success(1);
            });
        await started.Task;

        BrowserManagedOperationBoundaryException failure =
            await Assert.ThrowsAsync<BrowserManagedOperationBoundaryException>(
                () => Run(
                    bridge,
                    "same",
                    (_, _) =>
                    {
                        secondBodyCalls++;
                        return Task.FromResult(Success(2));
                    }));

        Assert.Equal("duplicate-active-operation", failure.FailureKind);
        Assert.Equal(0, secondBodyCalls);
        Assert.Equal(1, bridge.ActiveCount);
        release.SetResult();
        Assert.Equal(1, Assert.IsType<Result.Succeeded>(await first).Value);
    }

    [Fact]
    public async Task Cancellation_RegistersBeforeBodyWaitAndFirstReasonWins()
    {
        var bridge = new BrowserManagedOperationBridge();
        var bodyStarted = NewSignal();
        var release = NewSignal();
        BrowserManagedCancellationRequestResult? reentrant = null;
        int tokenCallbacks = 0;

        Task<Result> operation = Run(
            bridge,
            "reentrant",
            async (token, _) =>
            {
                using CancellationTokenRegistration registration = token.Register(
                    () =>
                    {
                        tokenCallbacks++;
                        reentrant = bridge.RequestCancellation(
                            Id("reentrant"),
                            BrowserManagedOperationCancelReason.Superseded);
                    });
                bodyStarted.SetResult();
                await release.Task;
                return Success(1);
            });
        await bodyStarted.Task;

        BrowserManagedCancellationRequestResult requested =
            bridge.RequestCancellation(
                Id("reentrant"),
                BrowserManagedOperationCancelReason.User);

        Assert.Equal(
            BrowserManagedOperationCancelReason.User,
            Assert.IsType<
                BrowserManagedCancellationRequestResult.Requested>(requested).Reason);
        Assert.Equal(
            BrowserManagedOperationCancelReason.User,
            Assert.IsType<
                BrowserManagedCancellationRequestResult.AlreadyRequested>(
                    reentrant).Reason);
        Assert.Equal(1, tokenCallbacks);

        release.SetResult();
        Assert.Equal(
            BrowserManagedOperationCancelReason.User,
            Assert.IsType<Result.Canceled>(await operation).Reason);
    }

    [Fact]
    public async Task CancellationReasons_RoundTripExactly()
    {
        foreach (BrowserManagedOperationCancelReason reason
            in Enum.GetValues<BrowserManagedOperationCancelReason>())
        {
            var bridge = new BrowserManagedOperationBridge();
            var started = NewSignal();
            var release = NewSignal();
            string operationId = $"reason-{reason}";
            Task<Result> operation = Run(
                bridge,
                operationId,
                async (_, _) =>
                {
                    started.SetResult();
                    await release.Task;
                    return Success(1);
                });
            await started.Task;

            Assert.Equal(
                reason,
                Assert.IsType<
                    BrowserManagedCancellationRequestResult.Requested>(
                        bridge.RequestCancellation(
                            Id(operationId),
                            reason)).Reason);
            release.SetResult();
            Assert.Equal(
                reason,
                Assert.IsType<Result.Canceled>(await operation).Reason);
        }
    }

    [Fact]
    public async Task ThrowingTokenCallback_ForcesUnexpectedFailure()
    {
        var bridge = new BrowserManagedOperationBridge();
        var started = NewSignal();
        var release = NewSignal();

        Task<Result> operation = Run(
            bridge,
            "token-failure",
            async (token, _) =>
            {
                using CancellationTokenRegistration registration = token.Register(
                    static () => throw new CallbackFailure("token"));
                started.SetResult();
                await release.Task;
                return ExpectedFailure("expected");
            });
        await started.Task;

        BrowserManagedCancellationRequestResult cancellation =
            bridge.RequestCancellation(
                Id("token-failure"),
                BrowserManagedOperationCancelReason.Timeout);
        Assert.IsType<BrowserManagedCancellationRequestResult.Requested>(
            cancellation);
        release.SetResult();

        Result.Failed failed = Assert.IsType<Result.Failed>(await operation);
        Assert.Equal(BrowserManagedOperationFailureKind.Unexpected, failed.FailureKind);
        Assert.Equal("AggregateException", failed.Error);
        Assert.Contains("token", failed.Diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThrowingProgressCallback_RejectsAfterRelease()
    {
        var bridge = new BrowserManagedOperationBridge();
        BrowserManagedCancellationRequestResult? internalCancellation = null;
        IBrowserManagedProgress<int>? retainedReporter = null;
        int callbackCalls = 0;

        Task<Result> operation = Run(
            bridge,
            "progress-failure",
            (token, progress) =>
            {
                retainedReporter = progress;
                progress.Report(1);
                internalCancellation = bridge.RequestCancellation(
                    Id("progress-failure"),
                    BrowserManagedOperationCancelReason.User);
                Assert.True(token.IsCancellationRequested);
                return Task.FromResult(Success(1));
            },
            _ =>
            {
                callbackCalls++;
                throw new CallbackFailure("progress");
            });

        BrowserManagedOperationBoundaryException failure =
            await Assert.ThrowsAsync<BrowserManagedOperationBoundaryException>(
                () => operation);

        Assert.Equal("progress-callback", failure.FailureKind);
        Assert.Equal("progress", Assert.IsType<CallbackFailure>(failure.InnerException).Message);
        Assert.Equal(
            BrowserManagedOperationCancelReason.FeatureObserverFailed,
            Assert.IsType<
                BrowserManagedCancellationRequestResult.AlreadyRequested>(
                    internalCancellation).Reason);
        Assert.NotNull(retainedReporter);
        Assert.True(retainedReporter.IsClosed);
        retainedReporter.Report(2);
        Assert.Equal(1, callbackCalls);
        Assert.Equal(0, bridge.ActiveCount);
        Assert.IsType<BrowserManagedCancellationRequestResult.NotActive>(
            bridge.RequestCancellation(
                Id("progress-failure"),
                BrowserManagedOperationCancelReason.User));
    }

    [Fact]
    public async Task Settlement_WaitsForInFlightProgressCallout()
    {
        var bridge = new BrowserManagedOperationBridge();
        var bodyRelease = NewSignal();
        var callbackEntered = new ManualResetEventSlim();
        var callbackRelease = new ManualResetEventSlim();
        CancellationToken testCancellation =
            TestContext.Current.CancellationToken;
        IBrowserManagedProgress<int>? reporter = null;

        Task<Result> operation = Run(
            bridge,
            "drain",
            async (_, progress) =>
            {
                reporter = progress;
                await bodyRelease.Task;
                return Success(1);
            },
            _ =>
            {
                callbackEntered.Set();
                callbackRelease.Wait(testCancellation);
            });
        Assert.NotNull(reporter);

        Task report = Task.Run(
            () => reporter.Report(1),
            testCancellation);
        Assert.True(
            callbackEntered.Wait(
                TimeSpan.FromSeconds(5),
                testCancellation));
        bodyRelease.SetResult();
        await Task.Delay(25, testCancellation);
        Assert.False(operation.IsCompleted);

        callbackRelease.Set();
        await report;
        Assert.Equal(1, Assert.IsType<Result.Succeeded>(await operation).Value);
    }

    [Fact]
    public void SettlementDrain_DoesNotContinueOnCalloutStack()
    {
        RunOnSingleThread(
            async () =>
            {
                Task<Result>? operation = null;
                var bodyResult = new TaskCompletionSource<BodyResult>();
                var settlementWaiting = new ManualResetEventSlim();
                CancellationToken testCancellation =
                    TestContext.Current.CancellationToken;
                IBrowserManagedProgress<int>? reporter = null;
                bool drainSignaled = false;
                bool cleanupRanOnCalloutStack = false;
                var bridge = new BrowserManagedOperationBridge(
                    new BrowserManagedOperationBridgeTestHooks
                    {
                        CalloutDrainSignaled = () => drainSignaled = true,
                        SettlementWaitingForCallouts =
                            settlementWaiting.Set,
                        CleanupCompleted = _ =>
                            cleanupRanOnCalloutStack |= _insideReportCallout,
                    });

                operation = Run(
                    bridge,
                    "single-thread-drain",
                    (_, progress) =>
                    {
                        reporter = progress;
                        return bodyResult.Task;
                    },
                    _ =>
                    {
                        bodyResult.SetResult(Success(1));
                        Assert.True(
                            settlementWaiting.Wait(
                                TimeSpan.FromSeconds(5),
                                testCancellation));
                    });
                Assert.NotNull(reporter);

                _insideReportCallout = true;
                try
                {
                    reporter.Report(1);
                }
                finally
                {
                    _insideReportCallout = false;
                }

                Assert.True(drainSignaled);
                Assert.Equal(
                    1,
                    Assert.IsType<Result.Succeeded>(await operation).Value);
                Assert.False(cleanupRanOnCalloutStack);
            });
    }

    [Fact]
    public async Task AcceptedCancellation_ClassifiesLateValueAndLinkedCancellation()
    {
        var bridge = new BrowserManagedOperationBridge();
        var firstStarted = NewSignal();
        var secondStarted = NewSignal();
        var release = NewSignal();
        using var foreignCancellation = new CancellationTokenSource();

        Task<Result> lateValue = Run(
            bridge,
            "late-value",
            async (_, _) =>
            {
                firstStarted.SetResult();
                await release.Task;
                return Success(1);
            });
        Task<Result> linkedCancellation = Run(
            bridge,
            "linked",
            async (_, _) =>
            {
                secondStarted.SetResult();
                await release.Task;
                throw new OperationCanceledException(foreignCancellation.Token);
            });
        await Task.WhenAll(firstStarted.Task, secondStarted.Task);

        _ = bridge.RequestCancellation(
            Id("late-value"),
            BrowserManagedOperationCancelReason.Superseded);
        _ = bridge.RequestCancellation(
            Id("linked"),
            BrowserManagedOperationCancelReason.User);
        release.SetResult();

        Assert.Equal(
            BrowserManagedOperationCancelReason.Superseded,
            Assert.IsType<Result.Canceled>(await lateValue).Reason);
        Assert.Equal(
            BrowserManagedOperationCancelReason.User,
            Assert.IsType<Result.Canceled>(await linkedCancellation).Reason);
    }

    [Fact]
    public async Task UnexpectedFailure_IsNotHiddenByCancellation()
    {
        var bridge = new BrowserManagedOperationBridge();
        var started = NewSignal();
        var release = NewSignal();

        Task<Result> operation = Run(
            bridge,
            "failure",
            async (_, _) =>
            {
                started.SetResult();
                await release.Task;
                throw new InvalidOperationException("broken");
            });
        await started.Task;
        _ = bridge.RequestCancellation(
            Id("failure"),
            BrowserManagedOperationCancelReason.User);
        release.SetResult();

        Result.Failed failed = Assert.IsType<Result.Failed>(await operation);
        Assert.Equal(BrowserManagedOperationFailureKind.Unexpected, failed.FailureKind);
        Assert.Equal("InvalidOperationException", failed.Error);
        Assert.Equal("broken", failed.Diagnostic);
    }

    [Fact]
    public async Task UnexplainedOperationCanceledException_IsUnexpectedFailure()
    {
        var bridge = new BrowserManagedOperationBridge();

        Result.Failed failed = Assert.IsType<Result.Failed>(
            await Run(
                bridge,
                "unexplained-cancellation",
                static (_, _) => Task.FromException<BodyResult>(
                    new OperationCanceledException("unexplained"))));

        Assert.Equal(BrowserManagedOperationFailureKind.Unexpected, failed.FailureKind);
        Assert.Equal("OperationCanceledException", failed.Error);
        Assert.Equal("unexplained", failed.Diagnostic);
    }

    [Fact]
    public async Task CancellationAfterSettlementStarts_IsNotActive()
    {
        var cleanupEntered = new ManualResetEventSlim();
        var cleanupRelease = new ManualResetEventSlim();
        CancellationToken testCancellation =
            TestContext.Current.CancellationToken;
        var bridge = new BrowserManagedOperationBridge(
            new BrowserManagedOperationBridgeTestHooks
            {
                CleanupCompleted = stage =>
                {
                    if (stage is not BrowserManagedOperationCleanupStage
                        .ProgressCallback)
                    {
                        return;
                    }

                    cleanupEntered.Set();
                    cleanupRelease.Wait(testCancellation);
                },
            });

        Task<Result> operation = Task.Run(
            () => Run(
                bridge,
                "settling",
                static (_, _) => Task.FromResult(Success(1))),
            testCancellation);
        Assert.True(
            cleanupEntered.Wait(
                TimeSpan.FromSeconds(5),
                testCancellation));

        Assert.IsType<BrowserManagedCancellationRequestResult.NotActive>(
            bridge.RequestCancellation(
                Id("settling"),
                BrowserManagedOperationCancelReason.User));

        cleanupRelease.Set();
        Assert.IsType<Result.Succeeded>(await operation);
    }

    [Fact]
    public async Task CleanupFailures_DoNotSkipLaterRelease()
    {
        foreach (BrowserManagedOperationCleanupStage failingStage
            in Enum.GetValues<BrowserManagedOperationCleanupStage>())
        {
            var visited = new List<BrowserManagedOperationCleanupStage>();
            bool injected = false;
            var bridge = new BrowserManagedOperationBridge(
                new BrowserManagedOperationBridgeTestHooks
                {
                    CleanupCompleted = stage =>
                    {
                        visited.Add(stage);
                        if (stage == failingStage && !injected)
                        {
                            injected = true;
                            throw new CleanupFailure(stage.ToString());
                        }
                    },
                });

            BrowserManagedOperationBoundaryException failure =
                await Assert.ThrowsAsync<BrowserManagedOperationBoundaryException>(
                    () => Run(
                        bridge,
                        $"cleanup-{failingStage}",
                        static (_, _) => Task.FromResult(Success(1))));

            Assert.Equal("cleanup", failure.FailureKind);
            Assert.IsType<CleanupFailure>(failure.InnerException);
            Assert.Equal(
                Enum.GetValues<BrowserManagedOperationCleanupStage>(),
                visited);
            Assert.Equal(0, bridge.ActiveCount);
            Assert.IsType<Result.Succeeded>(
                await Run(
                    bridge,
                    $"cleanup-{failingStage}",
                    static (_, _) => Task.FromResult(Success(2))));
        }
    }

    [Fact]
    public async Task EarlierBoundaryFailure_RemainsPrimaryAcrossCleanupFailures()
    {
        var bridge = new BrowserManagedOperationBridge(
            new BrowserManagedOperationBridgeTestHooks
            {
                CleanupCompleted = stage =>
                    throw new CleanupFailure(stage.ToString()),
            });

        BrowserManagedOperationBoundaryException failure =
            await Assert.ThrowsAsync<BrowserManagedOperationBoundaryException>(
                () => Run(
                    bridge,
                    "primary",
                    static (_, progress) =>
                    {
                        progress.Report(1);
                        return Task.FromResult(Success(1));
                    },
                    static _ => throw new CallbackFailure("primary")));

        Assert.Equal("progress-callback", failure.FailureKind);
        Assert.Equal("primary", Assert.IsType<CallbackFailure>(failure.InnerException).Message);
        Assert.Equal(4, failure.SecondaryFailures.Count);
        Assert.All(
            failure.SecondaryFailures,
            secondary => Assert.IsType<CleanupFailure>(secondary));
        Assert.Equal(0, bridge.ActiveCount);
    }

    [Fact]
    public async Task HealthyTerminalPaths_CloseRetainedProgressCallback()
    {
        foreach (string terminal in new[] { "succeeded", "failed", "canceled" })
        {
            var bridge = new BrowserManagedOperationBridge();
            var started = NewSignal();
            var release = NewSignal();
            IBrowserManagedProgress<int>? reporter = null;
            int callbackCalls = 0;
            string operationId = $"callback-{terminal}";
            Task<Result> operation = Run(
                bridge,
                operationId,
                async (_, progress) =>
                {
                    reporter = progress;
                    progress.Report(1);
                    started.SetResult();
                    await release.Task;
                    return terminal == "failed"
                        ? ExpectedFailure("expected")
                        : Success(1);
                },
                _ => callbackCalls++);
            await started.Task;
            if (terminal == "canceled")
            {
                _ = bridge.RequestCancellation(
                    Id(operationId),
                    BrowserManagedOperationCancelReason.User);
            }

            release.SetResult();
            _ = await operation;
            Assert.NotNull(reporter);
            Assert.True(reporter.IsClosed);
            reporter.Report(2);
            Assert.Equal(1, callbackCalls);
        }
    }

    [Fact]
    public async Task ProgressFailure_StillClassifiesBodyObservationOnce()
    {
        var bridge = new BrowserManagedOperationBridge();
        int classificationCalls = 0;

        Task<Result> operation =
            bridge.RunAsync<int, string, string, int>(
                Id("inert-classification"),
                static _ => throw new CallbackFailure("progress"),
                static (_, progress) =>
                {
                    progress.Report(1);
                    return Task.FromException<BodyResult>(
                        new InvalidOperationException("body"));
                },
                exception =>
                {
                    classificationCalls++;
                    return new BrowserManagedOperationFailure<string, string>(
                        exception.GetType().Name,
                        exception.Message);
                });

        BrowserManagedOperationBoundaryException failure =
            await Assert.ThrowsAsync<BrowserManagedOperationBoundaryException>(
                () => operation);

        Assert.Equal("progress-callback", failure.FailureKind);
        Assert.Equal("progress", Assert.IsType<CallbackFailure>(failure.InnerException).Message);
        Assert.Equal(1, classificationCalls);
        Assert.Equal(0, bridge.ActiveCount);
    }

    [Fact]
    public void ReentrantCancellation_DoesNotBlockSingleThreadedContext()
    {
        RunOnSingleThread(
            async () =>
            {
                var bridge = new BrowserManagedOperationBridge();
                var started = NewSignal();
                var release = NewSignal();
                int callbacks = 0;

                Task<Result> operation = Run(
                    bridge,
                    "single-thread",
                    async (token, progress) =>
                    {
                        using CancellationTokenRegistration registration =
                            token.Register(
                                () =>
                                {
                                    callbacks++;
                                    progress.Report(1);
                                    _ = bridge.RequestCancellation(
                                        Id("single-thread"),
                                        BrowserManagedOperationCancelReason.Timeout);
                                });
                        started.SetResult();
                        await release.Task;
                        return Success(1);
                    },
                    static _ => { });
                await started.Task;

                _ = bridge.RequestCancellation(
                    Id("single-thread"),
                    BrowserManagedOperationCancelReason.User);
                release.SetResult();

                Assert.IsType<Result.Canceled>(await operation);
                Assert.Equal(1, callbacks);
            });
    }

    [Fact]
    public async Task CompletedOperation_HasNoTombstoneOrOptionalProgressRequirement()
    {
        var bridge = new BrowserManagedOperationBridge();

        Assert.Equal(
            1,
            Assert.IsType<Result.Succeeded>(
                await Run(
                    bridge,
                    "neighbor",
                    static (_, progress) =>
                    {
                        Assert.True(progress.IsClosed);
                        progress.Report(1);
                        return Task.FromResult(Success(1));
                    },
                    progressCallback: null)).Value);
        Assert.IsType<BrowserManagedCancellationRequestResult.NotActive>(
            bridge.RequestCancellation(
                Id("neighbor"),
                BrowserManagedOperationCancelReason.User));
        Assert.Equal(
            2,
            Assert.IsType<Result.Succeeded>(
                await Run(
                    bridge,
                    "neighbor",
                    static (_, _) => Task.FromResult(Success(2)))).Value);
    }

    static Task<Result> Run(
        BrowserManagedOperationBridge bridge,
        string operationId,
        Func<
            CancellationToken,
            IBrowserManagedProgress<int>,
            Task<BodyResult>> body,
        Action<int>? progressCallback = null) =>
        bridge.RunAsync<int, string, string, int>(
            Id(operationId),
            progressCallback,
            body,
            static exception => new BrowserManagedOperationFailure<string, string>(
                exception.GetType().Name,
                exception.Message));

    static BrowserManagedOperationId Id(string value) =>
        BrowserManagedOperationId.From(value);

    static BodyResult Success(int value) =>
        new BodyResult.Succeeded(value);

    static BodyResult ExpectedFailure(string message) =>
        new BodyResult.Failed("expected", message);

    static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    static TaskCompletionSource<T> NewSignal<T>() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    static void RunOnSingleThread(Func<Task> action)
    {
        SynchronizationContext? previous = SynchronizationContext.Current;
        using var context = new PumpingSynchronizationContext(
            TestContext.Current.CancellationToken);
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            Task task = action();
            context.RunUntil(task);
            task.GetAwaiter().GetResult();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    sealed class PumpingSynchronizationContext : SynchronizationContext, IDisposable
    {
        readonly BlockingCollection<(SendOrPostCallback Callback, object? State)>
            _queue = [];
        readonly CancellationToken _cancellationToken;

        internal PumpingSynchronizationContext(
            CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
        }

        public override void Post(SendOrPostCallback d, object? state) =>
            _queue.Add((d, state), _cancellationToken);

        internal void RunUntil(Task task)
        {
            while (!task.IsCompleted)
            {
                if (!_queue.TryTake(
                        out var work,
                        millisecondsTimeout: 5_000,
                        _cancellationToken))
                {
                    throw new TimeoutException(
                        "The single-threaded operation did not make progress.");
                }

                work.Callback(work.State);
            }
        }

        public void Dispose() => _queue.Dispose();
    }

    sealed class CallbackFailure(string message) : Exception(message);

    sealed class CleanupFailure(string message) : Exception(message);
}
