using Xunit;

namespace NuGetFetch.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ThreadPoolDeadlineCollection
{
    public const string Name = "ThreadPoolDeadlineGuard";
}

[Collection(ThreadPoolDeadlineCollection.Name)]
public sealed class NuGetDeadlineRaceTests
{
    [Fact]
    public async Task RequestCompletion_UsesElapsedTimeWhenTimerCallbackIsDelayed()
    {
        await WithDelayedTimerCallbacksAsync(
            async () =>
            {
                using var operation = CreateOperation();

                NuGetRequestTimeoutException error =
                    await Assert.ThrowsAsync<NuGetRequestTimeoutException>(
                        () => operation.RunRequestAsync(
                            _ =>
                            {
                                Thread.Sleep(TimeSpan.FromMilliseconds(250));
                                return Task.FromResult(42);
                            }));

                Assert.Equal(TimeSpan.FromMilliseconds(40), error.Timeout);
            });
    }

    [Fact]
    public async Task StreamConsumption_UsesElapsedTimeWhenTimerCallbackIsDelayed()
    {
        await WithDelayedTimerCallbacksAsync(
            async () =>
            {
                using var operation = CreateOperation();
                using var owner = new MemoryStream();
                using Stream response =
                    await operation.RunStreamingRequestAsync(
                        _ => Task.FromResult<(Stream, IDisposable)>(
                            (new MemoryStream([42], writable: false), owner)));

                Thread.Sleep(TimeSpan.FromMilliseconds(250));

                NuGetRequestTimeoutException error =
                    Assert.Throws<NuGetRequestTimeoutException>(
                        () => response.ReadByte());
                Assert.Equal(TimeSpan.FromMilliseconds(40), error.Timeout);
            });
    }

    [Fact]
    public async Task MetadataBodyCompletion_UsesElapsedTimeWhenTimerCallbackIsDelayed()
    {
        await WithDelayedTimerCallbacksAsync(
            async () =>
            {
                await using var stream = new MemoryStream();

                NuGetMetadataBodyTimeoutException error =
                    await Assert.ThrowsAsync<NuGetMetadataBodyTimeoutException>(
                        () => NuGetMetadataReader.ReadStreamAsync(
                            stream,
                            static (_, _) =>
                            {
                                Thread.Sleep(TimeSpan.FromMilliseconds(250));
                                return ValueTask.FromResult(42);
                            },
                            CreateOptions(),
                            TestContext.Current.CancellationToken).AsTask());

                Assert.Equal(TimeSpan.FromMilliseconds(40), error.Timeout);
            });
    }

    [Fact]
    public async Task MetadataBodyAbort_UsesElapsedTimeWhenTimerCallbackIsDelayed()
    {
        await WithDelayedTimerCallbacksAsync(
            async () =>
            {
                await using var stream = new MemoryStream();

                NuGetMetadataBodyTimeoutException error =
                    await Assert.ThrowsAsync<NuGetMetadataBodyTimeoutException>(
                        () => NuGetMetadataReader.ReadStreamAsync<int>(
                            stream,
                            static (_, _) =>
                            {
                                Thread.Sleep(TimeSpan.FromMilliseconds(250));
                                return ValueTask.FromException<int>(
                                    new IOException(
                                        "Simulated post-deadline transport failure."));
                            },
                            CreateOptions(),
                            TestContext.Current.CancellationToken).AsTask());

                OperationCanceledException cancellation =
                    Assert.IsType<OperationCanceledException>(
                        error.InnerException);
                Assert.IsType<IOException>(
                    cancellation.InnerException);
                Assert.Equal(TimeSpan.FromMilliseconds(40), error.Timeout);
            });
    }

    [Fact]
    public async Task MetadataBodyCancellation_UsesElapsedTimeWhenTimerCallbackIsDelayed()
    {
        await WithDelayedTimerCallbacksAsync(
            async () =>
            {
                await using var stream = new MemoryStream();

                NuGetMetadataBodyTimeoutException error =
                    await Assert.ThrowsAsync<NuGetMetadataBodyTimeoutException>(
                        () => NuGetMetadataReader.ReadStreamAsync<int>(
                            stream,
                            static (_, _) =>
                            {
                                Thread.Sleep(TimeSpan.FromMilliseconds(250));
                                return ValueTask.FromException<int>(
                                    new OperationCanceledException(
                                        "Simulated unassociated cancellation."));
                            },
                            CreateOptions(),
                            TestContext.Current.CancellationToken).AsTask());

                Assert.IsType<OperationCanceledException>(
                    error.InnerException);
                Assert.Equal(TimeSpan.FromMilliseconds(40), error.Timeout);
            });
    }

    private static NuGetOperationDeadline CreateOperation() =>
        new(
            CreateOptions(),
            Timeout.InfiniteTimeSpan,
            TestContext.Current.CancellationToken);

    private static NuGetFetchOptions CreateOptions() =>
        new()
        {
            RequestTimeout = TimeSpan.FromMilliseconds(40),
            OperationTimeout = TimeSpan.FromSeconds(2),
            MetadataBodyTimeout = TimeSpan.FromMilliseconds(40),
        };

    private static async Task WithDelayedTimerCallbacksAsync(
        Func<Task> action)
    {
        ThreadPool.GetMinThreads(out int originalMinWorkers, out int minIo);
        ThreadPool.GetMaxThreads(out int originalMaxWorkers, out int maxIo);
        using var blockerStarted = new ManualResetEventSlim();
        using var releaseBlocker = new ManualResetEventSlim();
        bool currentThreadIsWorker = Thread.CurrentThread.IsThreadPoolThread;
        CancellationToken testCancellation =
            TestContext.Current.CancellationToken;

        try
        {
            Assert.True(ThreadPool.SetMinThreads(1, minIo));
            Assert.True(ThreadPool.SetMaxThreads(1, maxIo));
            if (!currentThreadIsWorker)
            {
                ThreadPool.QueueUserWorkItem(
                    _ =>
                    {
                        blockerStarted.Set();
                        releaseBlocker.Wait(testCancellation);
                    });
                blockerStarted.Wait(testCancellation);
            }

            await action();
        }
        finally
        {
            releaseBlocker.Set();
            Assert.True(ThreadPool.SetMaxThreads(originalMaxWorkers, maxIo));
            Assert.True(ThreadPool.SetMinThreads(originalMinWorkers, minIo));
        }
    }
}
